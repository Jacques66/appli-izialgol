using System.Globalization;
using System.Text;
using Studio.Common;

namespace Studio.Simula.Runtime;

/// <summary>Mise au point : points d'arret, pas a pas, pile, variables.</summary>
public sealed class SimDebugger : IDebugger
{
    enum Mode { Run, Into, Over, Out }

    readonly object gate = new();
    readonly Dictionary<string, HashSet<int>> breakpoints = new(StringComparer.OrdinalIgnoreCase);
    volatile bool hasBreakpoints;
    volatile bool pauseRequested;
    Mode mode = Mode.Run;
    Frame? stepFrame;
    int stepLine;
    readonly SemaphoreSlim resume = new(0);
    volatile bool stopped;
    Vm? vm;
    Frame? pausedFrame;
    int pausedPc;
    bool entryDone;
    Frame? lastBpFrame;
    int lastBpLine = -1;
    SimProgram? program;

    internal void Attach(SimProgram p) => program = p;

    public bool StopOnEntry { get; set; }
    public bool BreakOnError { get; set; }
    public event Action<PauseInfo>? Paused;

    internal bool Active => hasBreakpoints || pauseRequested || mode != Mode.Run || (StopOnEntry && !entryDone);
    internal bool PauseRequested => pauseRequested;

    public void SetBreakpoints(string file, IEnumerable<int> lines)
    {
        lock (gate)
        {
            breakpoints[Norm(file)] = new HashSet<int>(lines);
            hasBreakpoints = breakpoints.Values.Any(h => h.Count > 0);
        }
    }

    static string Norm(string f)
    {
        try { return Path.GetFullPath(f); } catch { return f; }
    }

    public void RequestPause() => pauseRequested = true;
    public void Continue() => Release(Mode.Run);
    public void StepInto() => Release(Mode.Into);
    public void StepOver() => Release(Mode.Over);
    public void StepOut() => Release(Mode.Out);

    void Release(Mode m)
    {
        lock (gate)
        {
            mode = m;
            stepFrame = pausedFrame;
            stepLine = pausedFrame != null && vm != null ? vm.PosOf(pausedFrame, pausedPc).Line : -1;
        }
        resume.Release();
    }

    internal void Stop()
    {
        stopped = true;
        resume.Release();
    }

    bool IsPrelude(string file) => file == "prelude.sim";

    static bool OnChain(Frame cur, Frame target)
    {
        int n = 0;
        for (Frame? f = cur; f != null && n < 100000; f = f.Dyn, n++)
            if (f == target) return true;
        return false;
    }

    internal void OnStatement(Vm machine, Frame f, int pc, int line, int col)
    {
        vm = machine;
        var (file, _, _) = machine.PosOf(f, pc);
        if (IsPrelude(file)) return;
        PauseReason? reason = null;
        if (StopOnEntry && !entryDone) { entryDone = true; reason = PauseReason.Entry; }
        else if (pauseRequested) { pauseRequested = false; reason = PauseReason.PauseRequest; }
        else
        {
            switch (mode)
            {
                case Mode.Into:
                    if (f != stepFrame || line != stepLine) reason = PauseReason.Step;
                    break;
                case Mode.Over:
                    if (stepFrame == null || (f == stepFrame && line != stepLine) || (f != stepFrame && !OnChain(f, stepFrame))) reason = PauseReason.Step;
                    break;
                case Mode.Out:
                    if (stepFrame == null || (f != stepFrame && !OnChain(f, stepFrame))) reason = PauseReason.Step;
                    break;
            }
            if (reason == null && hasBreakpoints)
            {
                bool hit;
                lock (gate) hit = breakpoints.TryGetValue(Norm(file), out var set) && set.Contains(line);
                if (hit && !(f == lastBpFrame && line == lastBpLine)) reason = PauseReason.Breakpoint;
                if (!hit) { lastBpFrame = null; lastBpLine = -1; }
            }
        }
        if (reason == null) return;
        if (reason == PauseReason.Breakpoint) { lastBpFrame = f; lastBpLine = line; }
        else { lastBpFrame = f; lastBpLine = line; }
        DoPause(machine, f, pc, reason.Value, file, line, col, null);
    }

    internal void OnError(Vm machine, Frame f, int pc, string message)
    {
        vm = machine;
        var (file, line, col) = machine.PosOf(f, pc);
        DoPause(machine, f, pc, PauseReason.Exception, file, line, col, message);
    }

    void DoPause(Vm machine, Frame f, int pc, PauseReason reason, string file, int line, int col, string? msg)
    {
        if (stopped) return;
        pausedFrame = f;
        pausedPc = pc;
        mode = Mode.Run;
        try { Paused?.Invoke(new PauseInfo(reason, file, line, col, msg)); } catch { }
        resume.Wait();
        pausedFrame = null;
        if (stopped) throw new CancelException();
    }

    // ------------------------------------------------------------ inspection

    public IReadOnlyList<StackFrameInfo> GetStack()
    {
        if (vm == null || pausedFrame == null) return Array.Empty<StackFrameInfo>();
        return vm.StackInfo(pausedFrame, pausedPc);
    }

    List<(Frame Frame, int Pc)> VisibleChain()
    {
        if (vm == null || pausedFrame == null) return new();
        return vm.Chain(pausedFrame, pausedPc).Where(c => vm.PosOf(c.Frame, c.Pc).File != "prelude.sim").ToList();
    }

    public IReadOnlyList<VariableInfo> GetVariables(int frameIndex)
    {
        var chain = VisibleChain();
        if (frameIndex < 0 || frameIndex >= chain.Count) return Array.Empty<VariableInfo>();
        var fr = chain[frameIndex].Frame;
        var res = new List<VariableInfo>();
        IEnumerable<VarDebug> vars = fr switch
        {
            SimObject so => so.Class.Attrs,
            _ when fr.Kind == FrameKind.Block && fr.Block != null => fr.Block.Vars,
            _ => fr.Unit.Vars
        };
        foreach (var v in vars.OrderBy(v => v.Slot))
            if (v.Slot < fr.S.Length) res.Add(Describe(v.Name, v.Type, v.Kind, fr.S[v.Slot]));
        // variables des blocs englobants (portee statique)
        if (fr.Kind == FrameKind.Block && fr.Static != null)
        {
            var st = fr.Static;
            res.Add(new VariableInfo("(englobant)", "", Vm.FrameName(st), true, st));
        }
        if (fr.Kind == FrameKind.Procedure && fr.Static is SimObject sobj)
            res.Add(new VariableInfo("(objet)", sobj.Class.Name, Vm.FrameName(sobj), true, sobj));
        return res;
    }

    public IReadOnlyList<VariableInfo> GetChildren(VariableInfo variable)
    {
        var res = new List<VariableInfo>();
        switch (variable.Handle)
        {
            case SimObject so:
                foreach (var v in so.Class.Attrs.OrderBy(v => v.Slot))
                    if (v.Slot < so.S.Length) res.Add(Describe(v.Name, v.Type, v.Kind, so.S[v.Slot]));
                break;
            case Frame fr:
                {
                    IEnumerable<VarDebug> vars = fr.Kind == FrameKind.Block && fr.Block != null ? fr.Block.Vars : fr.Unit.Vars;
                    foreach (var v in vars.OrderBy(v => v.Slot))
                        if (v.Slot < fr.S.Length) res.Add(Describe(v.Name, v.Type, v.Kind, fr.S[v.Slot]));
                    break;
                }
            case (SimArray a, string elemType):
                {
                    int n = Math.Min(a.Data.Length, 2000);
                    for (int i = 0; i < n; i++)
                    {
                        var idx = new int[a.Rank];
                        int rest = i;
                        for (int d = 0; d < a.Rank; d++) { idx[d] = a.Lower[d] + rest / a.Stride[d]; rest %= a.Stride[d]; }
                        res.Add(Describe("(" + string.Join(",", idx) + ")", elemType, 0, a.Data[i]));
                    }
                    if (a.Data.Length > n) res.Add(new VariableInfo("...", "", $"{a.Data.Length - n} elements de plus"));
                    break;
                }
            case TextRef t:
                res.Add(new VariableInfo("length", "integer", t.Length.ToString(CultureInfo.InvariantCulture)));
                res.Add(new VariableInfo("pos", "integer", t.Pos.ToString(CultureInfo.InvariantCulture)));
                res.Add(new VariableInfo("start", "integer", t.Start.ToString(CultureInfo.InvariantCulture)));
                res.Add(new VariableInfo("constant", "boolean", t.Obj.Const ? "true" : "false"));
                break;
        }
        return res;
    }

    static VariableInfo Describe(string name, string type, byte kind, Value v)
    {
        if (kind == 2) return new VariableInfo(name, type + " (par nom)", "<parametre par nom>");
        if (kind == 1 || type.EndsWith("array"))
        {
            if (v.O is SimArray a)
            {
                var bounds = string.Join(",", Enumerable.Range(0, a.Rank).Select(d => $"{a.Lower[d]}:{a.Upper[d]}"));
                var et = type.EndsWith(" array") ? type[..^6] : "real";
                return new VariableInfo(name, type, $"array({bounds})", a.Data.Length > 0, (a, et));
            }
            return new VariableInfo(name, type, "(non alloue)");
        }
        var t = type.Trim();
        if (t.StartsWith("ref(") || t == "none")
        {
            if (v.O is SimObject so) return new VariableInfo(name, type, $"{so.Class.Name} #{so.Serial} ({StateName(so.State)})", true, so);
            if (v.O is Frame) return new VariableInfo(name, type, "bloc", false);
            return new VariableInfo(name, type, "none");
        }
        switch (t)
        {
            case "integer": case "short integer": return new VariableInfo(name, type, v.I.ToString(CultureInfo.InvariantCulture));
            case "real": case "long real": return new VariableInfo(name, type, v.R.ToString("G15", CultureInfo.InvariantCulture));
            case "boolean": return new VariableInfo(name, type, v.I != 0 ? "true" : "false");
            case "character":
                {
                    char c = (char)v.I;
                    return new VariableInfo(name, type, c < 32 || c == 127 ? $"'!{(int)c}!'" : $"'{c}'");
                }
            case "text":
                {
                    if (v.O is TextRef tr)
                    {
                        var str = tr.ToString();
                        if (str.Length > 200) str = str[..200] + "...";
                        return new VariableInfo(name, type, "\"" + str + "\"", true, tr);
                    }
                    return new VariableInfo(name, type, "notext");
                }
        }
        // parametre procedure, etiquette...
        return v.O switch
        {
            ProcValue pv => new VariableInfo(name, type, "procedure " + pv.Proc.Name),
            LabelValue => new VariableInfo(name, type, "etiquette"),
            SwitchValue => new VariableInfo(name, type, "aiguillage"),
            Thunk => new VariableInfo(name, type, "<parametre>"),
            _ => new VariableInfo(name, type, v.O?.ToString() ?? v.I.ToString(CultureInfo.InvariantCulture))
        };
    }

    static string StateName(ObjState s) => s switch
    {
        ObjState.Attached => "attache", ObjState.Detached => "detache", ObjState.Resumed => "actif", _ => "termine"
    };
}
