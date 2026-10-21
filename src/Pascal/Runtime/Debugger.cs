using System.Text;
using Studio.Common;
using Studio.Pascal.Exec;
using Studio.Pascal.Semantics;

namespace Studio.Pascal.Runtime;

/// <summary>
/// Debugging: line breakpoints, pause, stepping, call stack, variables.
/// The execution thread raises Paused and then waits for Continue/Step*.
/// </summary>
public sealed class Debugger : IDebugger
{
    readonly ExecContext ctx;
    readonly object bpLock = new();
    Dictionary<string, HashSet<int>> breakpoints = new(StringComparer.OrdinalIgnoreCase);
    volatile bool hasBreakpoints;
    volatile bool pauseRequested;
    bool entryDone;
    bool stopOnEntry, breakOnError;

    enum StepMode { None, Into, Over, Out }
    volatile StepMode step = StepMode.None;
    int stepDepth; Frame? stepFrame; int stepLine; string stepFile = "";
    Frame? lastFrame; int lastLine; string lastFile = "";

    readonly object pauseLock = new();
    readonly ManualResetEventSlim resume = new(false);
    volatile bool paused;
    List<Frame> pausedFrames = new();
    int pausedLine, pausedCol; string pausedFile = "";
    int pausedDepth;

    public Debugger(ExecContext ctx) { this.ctx = ctx; }

    public event Action<PauseInfo>? Paused;

    public bool StopOnEntry { get => stopOnEntry; set { stopOnEntry = value; ctx.RefreshAttention(); } }
    public bool BreakOnError { get => breakOnError; set => breakOnError = value; }
    public bool IsPaused => paused;

    internal bool NeedsCheck => hasBreakpoints || pauseRequested || step != StepMode.None || (stopOnEntry && !entryDone);

    public void SetBreakpoints(string file, IEnumerable<int> lines)
    {
        lock (bpLock)
        {
            var copy = new Dictionary<string, HashSet<int>>(breakpoints, StringComparer.OrdinalIgnoreCase);
            var set = new HashSet<int>(lines);
            if (set.Count == 0) copy.Remove(Norm(file)); else copy[Norm(file)] = set;
            breakpoints = copy;
            hasBreakpoints = copy.Count > 0;
        }
        ctx.RefreshAttention();
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> normCache = new();
    static string Norm(string f) => normCache.GetOrAdd(f, x => { try { return Path.GetFullPath(x); } catch { return x; } });

    public void RequestPause() { pauseRequested = true; ctx.RefreshAttention(); }

    public void Continue() { step = StepMode.None; Resume(); }
    public void StepInto() { SetupStep(StepMode.Into); Resume(); }
    public void StepOver() { SetupStep(StepMode.Over); Resume(); }
    public void StepOut() { SetupStep(StepMode.Out); Resume(); }

    void SetupStep(StepMode m)
    {
        step = m;
        stepDepth = pausedDepth;
        stepFrame = pausedFrames.Count > 0 ? pausedFrames[0] : null;
        stepLine = pausedLine; stepFile = pausedFile;
    }

    void Resume()
    {
        ctx.RefreshAttention();
        resume.Set();
    }

    /// <summary>Cancellation: releases a paused thread.</summary>
    internal void Release() { step = StepMode.None; resume.Set(); }

    internal void OnStatement(Frame f, Node at)
    {
        if (!NeedsCheck) return;
        int depth = ctx.CallDepth;
        if (stopOnEntry && !entryDone)
        {
            entryDone = true;
            Pause(PauseReason.Entry, f, at, null);
            return;
        }
        if (pauseRequested)
        {
            pauseRequested = false;
            Pause(PauseReason.PauseRequest, f, at, null);
            return;
        }
        switch (step)
        {
            case StepMode.Into:
                if (!ReferenceEquals(f, stepFrame) || at.Line != stepLine || at.File != stepFile) { Pause(PauseReason.Step, f, at, null); return; }
                break;
            case StepMode.Over:
                if (depth < stepDepth || (depth == stepDepth && (at.Line != stepLine || at.File != stepFile || !ReferenceEquals(f, stepFrame))))
                { Pause(PauseReason.Step, f, at, null); return; }
                break;
            case StepMode.Out:
                if (depth < stepDepth) { Pause(PauseReason.Step, f, at, null); return; }
                break;
        }
        if (hasBreakpoints)
        {
            var bps = breakpoints;
            if (bps.TryGetValue(Norm(at.File), out var lines) || bps.TryGetValue(at.File, out lines))
            {
                if (lines.Contains(at.Line))
                {
                    if (ReferenceEquals(f, lastFrame) && at.Line == lastLine && at.File == lastFile) return;
                    Pause(PauseReason.Breakpoint, f, at, null);
                    return;
                }
            }
        }
        if (!ReferenceEquals(f, lastFrame) || at.Line != lastLine) lastFrame = null;
    }

    internal void OnError(PasError e)
    {
        if (!breakOnError || Paused == null) return;
        if (ctx.Top == null) return;
        Pause(PauseReason.Exception, ctx.Top, null, e.Message, e.File, e.Line, e.Col);
    }

    void Pause(PauseReason reason, Frame f, Node? at, string? msg, string? file = null, int line = 0, int col = 0)
    {
        if (Paused == null) return;
        lock (pauseLock)
        {
            ctx.FlushOutput();
            var frames = new List<Frame>();
            for (var fr = ctx.Top; fr != null; fr = fr.Caller) frames.Add(fr);
            if (frames.Count == 0 || !ReferenceEquals(frames[0], f)) frames.Insert(0, f);
            pausedFrames = frames;
            pausedFile = file ?? at?.File ?? f.R.File; pausedLine = at?.Line ?? line; pausedCol = at?.Col ?? col;
            if (at != null) { f.Line = at.Line; f.Col = at.Col; }
            pausedDepth = ctx.CallDepth;
            step = StepMode.None;
            lastFrame = f; lastLine = pausedLine; lastFile = pausedFile;
            resume.Reset();
            paused = true;
            ctx.RefreshAttention();
            try
            {
                Paused?.Invoke(new PauseInfo(reason, pausedFile, pausedLine, pausedCol, msg));
                while (!resume.Wait(100))
                    if (ctx.CancelRequested) break;
            }
            finally { paused = false; }
            if (ctx.CancelRequested && reason != PauseReason.Exception) throw new CancelledException();
        }
    }

    // ------------------------------------------------------------------ inspection

    public IReadOnlyList<StackFrameInfo> GetStack()
    {
        var list = new List<StackFrameInfo>();
        for (int i = 0; i < pausedFrames.Count; i++)
        {
            var fr = pausedFrames[i];
            if (i == 0) list.Add(new StackFrameInfo(fr.R.Name, pausedFile, pausedLine, pausedCol));
            else list.Add(new StackFrameInfo(fr.R.Name, fr.R.File, fr.Line, fr.Col));
        }
        return list;
    }

    sealed record Handle(PType Type, Cell[] S, int I);

    public IReadOnlyList<VariableInfo> GetVariables(int frameIndex)
    {
        var res = new List<VariableInfo>();
        if (frameIndex < 0 || frameIndex >= pausedFrames.Count) return res;
        var fr = pausedFrames[frameIndex];
        foreach (var dv in fr.R.Vars)
        {
            if (dv.Slot >= fr.C.Length) continue;
            if (dv.IsRoutine)
            {
                var o = fr.C[dv.Slot].O;
                res.Add(new VariableInfo(dv.Name, dv.Type.ToString(), o is Closure c ? c.ToString() : "undefined"));
                continue;
            }
            if (dv.IsRef)
            {
                var c = fr.C[dv.Slot];
                if (c.O is Cell[] s) res.Add(Describe(dv.Name, dv.Type, s, (int)c.N));
                else res.Add(new VariableInfo(dv.Name, dv.Type.ToString(), "undefined"));
                continue;
            }
            res.Add(Describe(dv.IsResult ? dv.Name + " (result)" : dv.Name, dv.Type, fr.C, dv.Slot));
        }
        return res;
    }

    public IReadOnlyList<VariableInfo> GetChildren(VariableInfo variable)
    {
        var res = new List<VariableInfo>();
        if (variable.Handle is not Handle h) return res;
        switch (h.Type)
        {
            case ArrayType at:
                {
                    long n = at.Count;
                    for (long k = 0; k < n; k++)
                    {
                        if (k >= 1000) { res.Add(new VariableInfo("...", "", $"({n - 1000} more elements)")); break; }
                        res.Add(Describe("[" + Format.OrdText(at.Index, at.Index.Lo + k) + "]", at.Elem, h.S, h.I + (int)k * at.Elem.Size));
                    }
                    break;
                }
            case RecordType rt:
                foreach (var fl in rt.Fields)
                {
                    if (fl.Variant != null && !VariantActive(rt, fl, h)) continue;
                    res.Add(Describe(fl.Name, fl.Type, h.S, h.I + fl.Offset));
                }
                break;
            case PointerType pt:
                if (h.S[h.I].O is HeapObj ho && !ho.Disposed) res.Add(Describe("^", pt.Target, ho.C, 0));
                break;
        }
        return res;
    }

    /// <summary>A variant field is shown when its variant is selected by the tag (or when it has no tag field
    /// and one of its fields is defined).</summary>
    static bool VariantActive(RecordType rt, Field fl, Handle h)
    {
        for (var v = fl.Variant; v != null; v = v.Owner.Parent)
        {
            var part = v.Owner;
            if (part.Tag != null)
            {
                var c = h.S[h.I + part.Tag.Offset];
                if (c.IsUndef || !v.Labels.Contains(c.N)) return false;
            }
            else
            {
                bool any = false;
                for (int k = 0; k < v.Size && !any; k++) any = !h.S[h.I + v.Offset + k].IsUndef;
                if (!any) return false;
            }
        }
        return true;
    }

    VariableInfo Describe(string name, PType t, Cell[] s, int i)
    {
        string text = ValueText(t, s, i, 0);
        bool children = t switch
        {
            ArrayType at => !at.IsString && at.Count > 0,
            RecordType rt => rt.Fields.Count > 0,
            PointerType => s[i].O is HeapObj ho && !ho.Disposed,
            _ => false,
        };
        return new VariableInfo(name, t.ToString(), text, children, new Handle(t, s, i));
    }

    public static string ValueText(PType t, Cell[] s, int i, int depth)
    {
        try
        {
            var c = s[i];
            switch (t.Kind)
            {
                case TK.Array:
                    {
                        var at = (ArrayType)t;
                        if (at.IsString || at.Elem.Kind == TK.Char)
                        {
                            var sb = new StringBuilder("'");
                            for (int k = 0; k < at.Count; k++)
                            {
                                var e = s[i + k];
                                sb.Append(e.IsUndef ? '?' : (char)e.N == '\'' ? "''" : ((char)e.N).ToString());
                            }
                            return sb.Append('\'').ToString();
                        }
                        if (depth > 0) return "[...]";
                        return "array[" + Format.OrdText(at.Index, at.Index.Lo) + ".." + Format.OrdText(at.Index, at.Index.Hi) + $"] ({at.Count} elements)";
                    }
                case TK.Record:
                    {
                        var rt = (RecordType)t;
                        if (depth > 0) return "(...)";
                        var parts = new List<string>();
                        foreach (var fl in rt.Fields)
                        {
                            if (fl.Variant != null && !VariantActive(rt, fl, new Handle(t, s, i))) continue;
                            parts.Add(fl.Name + ": " + ValueText(fl.Type, s, i + fl.Offset, depth + 1));
                            if (parts.Count >= 6) { parts.Add("..."); break; }
                        }
                        return "(" + string.Join(", ", parts) + ")";
                    }
                case TK.File:
                    return c.O?.ToString() ?? "?";
            }
            if (c.IsUndef) return "undefined";
            switch (t.Kind)
            {
                case TK.Real: return Format.RealShort(c.R);
                case TK.Set: return c.O is PSet ps ? Format.SetText(t, ps) : "?";
                case TK.Pointer:
                    if (c.O == null) return "nil";
                    if (c.O is HeapObj h) return h.Disposed ? $"^#{h.Id} (disposed)" : $"^#{h.Id}";
                    return "?";
                default:
                    return t.IsOrdinal ? Format.OrdText(t, c.N) : "?";
            }
        }
        catch (Exception) { return "?"; }
    }
}
