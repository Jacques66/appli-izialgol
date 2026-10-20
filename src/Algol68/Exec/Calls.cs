using System.Runtime.CompilerServices;
using Studio.Algol68.Runtime;
using Studio.Algol68.Semantics;

namespace Studio.Algol68.Exec;

public static class Calls
{
    const int MaxCallDepth = 100000;

    /// <summary>Appel generique d'une valeur procedure.</summary>
    public static Value Invoke(ProcV p, Value[] args, Frame caller, Node site, int callerDepth)
    {
        if (p.Jump != null) throw new JumpException(p.Jump, p.Env!);
        if (p.Bound != null) args = MergeBound(p, args);
        if (p.Builtin != null) return p.Builtin(caller, args, site);
        var r = p.Routine!;
        var nf = new Frame(r, p.Env, callerDepth + 1, caller.Ctx);
        for (int i = 0; i < args.Length; i++) nf.S[i] = args[i];
        return Run(nf, site);
    }

    static Value[] MergeBound(ProcV p, Value[] args)
    {
        var all = new Value[p.Bound!.Length];
        int k = 0;
        for (int i = 0; i < all.Length; i++) all[i] = p.BoundMask![i] ? p.Bound[i] : args[k++];
        return all;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Value Run(Frame nf, Node site)
    {
        var ts = ThreadCtx.Current;
        if (++ts.CallDepth > MaxCallDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            ts.CallDepth--;
            throw RT.Err("debordement de pile (recursion trop profonde)", site);
        }
        nf.Caller = ts.Top;
        ts.Top = nf;
        nf.Line = site.Line; nf.Col = site.Col; nf.File = site.File;
        try
        {
            var v = nf.Info.Body.Eval(nf);
            if (nf.Info.CheckResultScope && RT.ScopeOf(v, nf.Info.ResultMode!) >= nf.Depth)
                throw RT.Err($"violation de portee : la routine « {nf.Info.Name} » rend une valeur locale", site);
            return v;
        }
        finally
        {
            ts.Top = nf.Caller;
            ts.CallDepth--;
        }
    }
}

/// <summary>Appel d'une procedure quelconque (valeur calculee).</summary>
public sealed class CallNode : Node
{
    readonly Node proc; readonly Node[] args; readonly int siteLevel; readonly bool[] copyArg; readonly Mode[] argModes;
    public CallNode(Node proc, Node[] args, Mode[] argModes, int siteLevel)
    {
        this.proc = proc; this.args = args; this.siteLevel = siteLevel; this.argModes = argModes;
        copyArg = argModes.Select(m => m.NeedsCopy).ToArray();
    }
    public override Value Eval(Frame f)
    {
        var p = proc.EvalObj(f) as ProcV ?? throw RT.Err("appel d'une procedure non initialisee", this);
        if (p.Routine != null && p.Bound == null && p.Jump == null)
        {
            var r = p.Routine;
            var nf = new Frame(r, p.Env, f.Depth + siteLevel + 1, f.Ctx);
            for (int i = 0; i < args.Length; i++)
            {
                var v = args[i].Eval(f);
                if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err($"argument {i + 1} non initialise", this);
                nf.S[i] = copyArg[i] ? RT.DeepCopy(v, argModes[i]) : v;
            }
            return Calls.Run(nf, this);
        }
        var av = new Value[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            var v = args[i].Eval(f);
            av[i] = copyArg[i] ? RT.DeepCopy(v, argModes[i]) : v;
        }
        return Calls.Invoke(p, av, f, this, f.Depth + siteLevel);
    }
}

/// <summary>Appel direct d'une primitive du prelude.</summary>
public sealed class BuiltinCallNode : Node
{
    readonly BuiltinFn fn; readonly Node[] args;
    public BuiltinCallNode(BuiltinFn fn, Node[] args) { this.fn = fn; this.args = args; }
    public override Value Eval(Frame f)
    {
        var av = new Value[args.Length];
        for (int i = 0; i < args.Length; i++) av[i] = args[i].Eval(f);
        return fn(f, av, this);
    }
}

/// <summary>Deprocedurage : appel sans parametre.</summary>
public sealed class DeprocNode : Node
{
    readonly Node proc; readonly int siteLevel;
    public DeprocNode(Node proc, int siteLevel) { this.proc = proc; this.siteLevel = siteLevel; SetPos(proc); }
    public override Value Eval(Frame f)
    {
        var p = proc.EvalObj(f) as ProcV ?? throw RT.Err("appel d'une procedure non initialisee", this);
        if (p.Routine != null && p.Bound == null)
        {
            var nf = new Frame(p.Routine, p.Env, f.Depth + siteLevel + 1, f.Ctx);
            return Calls.Run(nf, this);
        }
        return Calls.Invoke(p, Array.Empty<Value>(), f, this, f.Depth + siteLevel);
    }
}

/// <summary>Texte de routine : fabrique une fermeture.</summary>
public sealed class RoutineTextNode : Node
{
    readonly RoutineInfo info;
    readonly int scopeHops;      // -1 : portee globale
    readonly int scopeLevel;
    public RoutineTextNode(RoutineInfo info, int scopeHops, int scopeLevel) { this.info = info; this.scopeHops = scopeHops; this.scopeLevel = scopeLevel; }
    public override Value Eval(Frame f) => Value.Obj(Make(f));
    public override object? EvalObj(Frame f) => Make(f);
    ProcV Make(Frame f)
    {
        int scope = 0;
        if (scopeHops >= 0) scope = Up(f, scopeHops).Depth + scopeLevel;
        return new ProcV { Routine = info, Env = f, Scope = scope, Name = info.Name };
    }
}

/// <summary>Parametrage partiel (extension a68g) : p(a, , c).</summary>
public sealed class PartialCallNode : Node
{
    readonly Node proc; readonly Node?[] args; readonly ProcMode full;
    public PartialCallNode(Node proc, Node?[] args, ProcMode full) { this.proc = proc; this.args = args; this.full = full; }
    public override Value Eval(Frame f)
    {
        var p = proc.EvalObj(f) as ProcV ?? throw RT.Err("procedure non initialisee", this);
        int n = full.Params.Length;
        var bound = new Value[n]; var mask = new bool[n];
        if (p.Bound != null)
        {
            // composer avec un parametrage existant
            int k = 0;
            for (int i = 0; i < p.Bound.Length; i++)
            {
                if (p.BoundMask![i]) { bound[i] = p.Bound[i]; mask[i] = true; }
            }
            _ = k;
        }
        int a = 0;
        for (int i = 0; i < n; i++)
        {
            if (p.BoundMask != null && p.BoundMask[i]) continue;
            var an = args[a++];
            if (an != null) { bound[i] = an.Eval(f); mask[i] = true; }
        }
        return Value.Obj(new ProcV { Routine = p.Routine, Builtin = p.Builtin, Env = p.Env, Scope = p.Scope, Name = p.Name, Bound = bound, BoundMask = mask, Jump = p.Jump });
    }
}
