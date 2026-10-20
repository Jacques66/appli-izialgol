using System.Numerics;
using System.Text;
using Studio.Common;
using Studio.Algol68.Exec;
using Studio.Algol68.Semantics;

namespace Studio.Algol68.Runtime;

/// <summary>
/// Mise au point : points d'arret par ligne, pause, pas a pas, pile, variables.
/// Le fil d'execution qui s'arrete leve Paused puis attend Continue/Step*.
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
    // dernier arret (evite de s'arreter plusieurs fois sur la meme ligne)
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

    /// <summary>Annulation : libere un fil en pause.</summary>
    internal void Release() { step = StepMode.None; resume.Set(); }

    internal void OnEnter(Frame f) { }
    internal void OnLeave(Frame f) { }

    internal void OnStatement(Frame f, Node at)
    {
        if (!NeedsCheck) return;
        var ts = ThreadCtx.Current;
        int depth = ts.CallDepth;
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
        if (!ReferenceEquals(f, lastFrame) || at.Line != lastLine) { lastFrame = null; }
    }

    internal void OnError(A68Error e)
    {
        if (!breakOnError || Paused == null) return;
        var ts = ThreadCtx.Current;
        if (ts.Top == null) return;
        Pause(PauseReason.Exception, ts.Top, null, e.Message, e.File, e.Line, e.Col);
    }

    void Pause(PauseReason reason, Frame f, Node? at, string? msg, string? file = null, int line = 0, int col = 0)
    {
        if (Paused == null) return;
        lock (pauseLock)
        {
            var ts = ThreadCtx.Current;
            var frames = new List<Frame>();
            for (var fr = ts.Top; fr != null; fr = fr.Caller) frames.Add(fr);
            if (frames.Count == 0 || !ReferenceEquals(frames[0], f)) frames.Insert(0, f);
            pausedFrames = frames;
            pausedFile = file ?? at?.File ?? f.File; pausedLine = at?.Line ?? line; pausedCol = at?.Col ?? col;
            if (at != null) { f.Line = at.Line; f.Col = at.Col; f.File = at.File; }
            pausedDepth = ts.CallDepth;
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
            if (i == 0) list.Add(new StackFrameInfo(fr.Info.Name, pausedFile, pausedLine, pausedCol));
            else list.Add(new StackFrameInfo(fr.Info.Name, fr.File, fr.Line, fr.Col));
        }
        return list;
    }

    sealed record Handle(Mode Mode, Value Value);

    public IReadOnlyList<VariableInfo> GetVariables(int frameIndex)
    {
        var res = new List<VariableInfo>();
        if (frameIndex < 0 || frameIndex >= pausedFrames.Count) return res;
        var fr = pausedFrames[frameIndex];
        int line = frameIndex == 0 ? pausedLine : fr.Line;
        var seen = new HashSet<string>();
        // les plus internes d'abord (declarees plus tard)
        foreach (var dv in fr.Info.Vars.OrderByDescending(v => v.FromLine))
        {
            if (dv.Slot >= fr.S.Length) continue;
            if (line != 0 && (line < dv.FromLine || line > dv.ToLine)) continue;
            var v = fr.S[dv.Slot];
            if (v.O == null && dv.Mode is not PrimMode) continue;   // pas encore elaboree
            if (!seen.Add(dv.Name)) continue;
            res.Add(Describe(dv.Name, dv.Mode, v));
        }
        res.Reverse();
        return res;
    }

    public IReadOnlyList<VariableInfo> GetChildren(VariableInfo variable)
    {
        var res = new List<VariableInfo>();
        if (variable.Handle is not Handle h) return res;
        var v = h.Value; var m = h.Mode;
        if (v.O == null && m is not PrimMode || ReferenceEquals(v.O, Value.Uninit)) return res;
        switch (m)
        {
            case RefMode rm:
                if (v.O is RefV r && r.A != null) res.Add(Describe("→", rm.Sub, r.A[r.I]));
                break;
            case StructMode sm:
                if (v.O is Value[] fl)
                    for (int i = 0; i < sm.Fields.Length && i < fl.Length; i++) res.Add(Describe(sm.Fields[i].Name, sm.Fields[i].Mode, fl[i]));
                break;
            case RowMode or FlexMode:
                if (v.O is RowV row)
                {
                    var elem = m.AsRow!.Elem;
                    int k = 0;
                    foreach (int idx in row.Indices())
                    {
                        if (k++ >= 1000) { res.Add(new VariableInfo("...", "", $"({row.Count - 1000} elements de plus)")); break; }
                        res.Add(Describe("[" + IndexText(row, idx) + "]", elem, row.GetAt(idx)));
                    }
                }
                break;
            case UnionMode:
                if (v.O is UnionV u) res.Add(Describe("(" + u.M + ")", u.M, u.V));
                break;
        }
        return res;
    }

    static string IndexText(RowV row, int dataIdx)
    {
        int rem = dataIdx - row.Offset;
        var parts = new int[row.Dims];
        // reconstruction des indices (strides positifs, decroissants)
        var order = Enumerable.Range(0, row.Dims).OrderByDescending(d => row.Stride[d]).ToArray();
        foreach (int d in order)
        {
            int s = row.Stride[d] == 0 ? 1 : row.Stride[d];
            parts[d] = row.Lwb[d] + rem / s;
            rem %= s;
        }
        return string.Join(", ", parts);
    }

    VariableInfo Describe(string name, Mode m, Value v)
    {
        string text = ValueText(m, v, 0);
        bool children = !ReferenceEquals(v.O, Value.Uninit) && m switch
        {
            RefMode => v.O is RefV r && r.A != null,
            StructMode => v.O is Value[],
            RowMode or FlexMode => v.O is RowV row && row.Count > 0 && !(m.AsRow!.Elem is PrimMode { Kind: Prim.Char }),
            UnionMode => v.O is UnionV,
            _ => false,
        };
        return new VariableInfo(name, m.ToString(), text, children, new Handle(m, v));
    }

    public static string ValueText(Mode m, Value v, int depth)
    {
        if (ReferenceEquals(v.O, Value.Uninit)) return "<non initialise>";
        try
        {
            switch (m)
            {
                case PrimMode p:
                    switch (p.Kind)
                    {
                        case Prim.Int: return p.Size == 0 ? v.N.ToString() : v.O?.ToString() ?? "?";
                        case Prim.Real: return p.Size == 0 ? v.Real.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) : v.O?.ToString() ?? "?";
                        case Prim.Bool: return v.Bool ? "TRUE" : "FALSE";
                        case Prim.Char: return "\"" + (char)v.N + "\"";
                        case Prim.Bits: return Transput.StandardString(m, v, null!);
                        case Prim.Bytes: return "\"" + ((string?)v.O ?? "").TrimEnd('\0') + "\"";
                        case Prim.Sema: return v.O is SemaV s ? $"LEVEL {s.Level}" : "?";
                        case Prim.File: return v.O?.ToString() ?? "?";
                        case Prim.Channel: return v.O?.ToString() ?? "?";
                        case Prim.Format: return "format";
                        case Prim.Void: return "EMPTY";
                    }
                    break;
                case RefMode:
                    if (v.O is RefV r) return r.A == null ? "NIL" : depth > 1 ? "nom" : "nom → " + ValueText(((RefMode)m).Sub, r.A[r.I], depth + 1);
                    break;
                case ProcMode: return v.O is ProcV pv ? pv.ToString() : "?";
                case StructMode sm:
                    {
                        if (v.O is not Value[] fl) return "?";
                        if (depth > 1) return "(...)";
                        var sb = new StringBuilder("(");
                        for (int i = 0; i < fl.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(ValueText(sm.Fields[i].Mode, fl[i], depth + 1)); }
                        return sb.Append(')').ToString();
                    }
                case RowMode or FlexMode:
                    {
                        if (v.O is not RowV row) return "?";
                        var elem = m.AsRow!.Elem;
                        if (elem is PrimMode { Kind: Prim.Char }) { try { return "\"" + row.AsString() + "\""; } catch { return "<chaine partielle>"; } }
                        return RT.BoundsText(row) + $" ({row.Count} elements)";
                    }
                case UnionMode:
                    return v.O is UnionV u ? $"({u.M}) " + ValueText(u.M, u.V, depth + 1) : "?";
            }
        }
        catch (Exception) { return "?"; }
        return v.ToString();
    }
}
