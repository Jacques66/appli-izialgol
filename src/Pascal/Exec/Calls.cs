using System.Runtime.CompilerServices;
using Studio.Pascal.Runtime;
using Studio.Pascal.Semantics;

namespace Studio.Pascal.Exec;

public enum ParamKind { Value, Var, Procedure, Function }

public sealed class ParamInfo
{
    public string Name = "";
    public ParamKind Kind;
    public PType Type = ErrorType.T;   // value/var parameters; result type of a function parameter
    public int Slot;
    public int Size = 1;
    /// <summary>Heading of a procedural parameter (null: 1974 form without a parameter list).</summary>
    public Signature? Sig;
}

/// <summary>Parameter list and result type of a routine or of a procedural parameter.</summary>
public sealed class Signature
{
    public List<ParamInfo> Params = new();
    public PType? Result;
    public bool IsFunction => Result != null;
    /// <summary>False for a 1974-style formal routine "procedure p" / "function f: real" whose parameters are not specified.</summary>
    public bool HasList = true;
}

public sealed class DebugVar
{
    public string Name = "";
    public int Slot;
    public PType Type = ErrorType.T;
    public bool IsRef;          // var parameter
    public bool IsResult;       // function result
    public bool IsRoutine;      // procedural parameter
}

/// <summary>Label (runtime identity).</summary>
public sealed class LabelInfo
{
    public readonly long Value;
    public LabelInfo(long v) { Value = v; }
    public override string ToString() => Value.ToString();
}

/// <summary>Compiled routine (or main program).</summary>
public sealed class RoutineInfo
{
    public string Name = "";
    public int Level;               // nesting level of the body: main program 1
    public bool IsMain;
    public Signature Sig = new();
    public bool IsFunction => Sig.IsFunction;
    public int ResultSlot = -1;
    public int FrameSize;
    public Cell[] Template = Array.Empty<Cell>();
    public readonly List<(int Offset, FileType Type, string Name, string? External)> Files = new();
    public Stmt Body = null!;
    public readonly List<DebugVar> Vars = new();
    public string File = "";
    public int Line, Col;
    /// <summary>Position of the final "end" of the routine body.</summary>
    public Node? EndPos;

    public Cell[] NewCells(ExecContext ctx)
    {
        var c = Template.Length == 0 ? Template : (Cell[])Template.Clone();
        foreach (var (off, t, name, ext) in Files) c[off] = Cell.Obj(ctx.NewFile(t, name, ext));
        return c;
    }

    /// <summary>Finalises the frame layout once all slots are allocated.</summary>
    public void Seal(int frameSize)
    {
        FrameSize = frameSize;
        Template = new Cell[frameSize];
        Array.Fill(Template, Cell.Undefined);
    }
}

public enum ArgKind { Value, Struct, Var }

public sealed class Arg
{
    public ArgKind Kind;
    public Expr E = null!;
    public int Size = 1;
}

public static class Calls
{
    const int MaxCallDepth = 200000;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cell Run(Frame nf, Node site)
    {
        var ctx = nf.Ctx;
        if (++ctx.CallDepth > MaxCallDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            ctx.CallDepth--;
            throw RT.Err("stack overflow (recursion too deep)", site);
        }
        nf.Caller = ctx.Top;
        ctx.Top = nf;
        var r = nf.R;
        nf.Line = r.Line; nf.Col = r.Col;
        try
        {
            int st = r.Body.Exec(nf);
            if (st != 0) throw RT.Err($"goto {nf.PendingGoto}: target statement not found", site);
            if (r.IsFunction)
            {
                var c = nf.C[r.ResultSlot];
                if (c.IsUndef) throw RT.Err($"function '{r.Name}' returned without assigning a result", r.EndPos ?? site);
                return c;
            }
            return default;
        }
        catch (GotoException g) when (ReferenceEquals(g.Target, nf))
        {
            throw RT.Err($"goto {g.Label}: the labelled statement is not active (a goto may not lead into a structured statement)", g.Site ?? site);
        }
        finally
        {
            ctx.Top = nf.Caller;
            ctx.CallDepth--;
        }
    }

    public static void Bind(Arg[] args, Frame f, Frame nf, List<ParamInfo> ps)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            int slot = ps[i].Slot;
            switch (a.Kind)
            {
                case ArgKind.Value: nf.C[slot] = a.E.Value(f); break;
                case ArgKind.Struct: RT.Copy(a.E.Addr(f), new Ref(nf.C, slot), a.Size); break;
                default:
                    var r = a.E.Addr(f);
                    nf.C[slot] = new Cell { O = r.S, N = r.I };
                    break;
            }
        }
    }

    /// <summary>Call of a 1974-style formal routine: parameters checked at run time (value parameters only).</summary>
    public static void BindUnchecked(Expr[] args, Frame f, Frame nf, Node site)
    {
        var r = nf.R;
        var ps = r.Sig.Params;
        string what = (r.IsFunction ? "function '" : "procedure '") + r.Name + "'";
        if (ps.Count != args.Length)
            throw RT.Err($"{what} (passed as a parameter) has {ps.Count} parameter(s), called with {args.Length}", site);
        for (int i = 0; i < args.Length; i++)
        {
            var p = ps[i]; var a = args[i];
            if (p.Kind != ParamKind.Value)
                throw RT.Err($"{what} passed as a parameter without a parameter list may only have value parameters ('{p.Name}' is not)", site);
            if (!TypeRules.AssignCompatible(p.Type, a.Type))
                throw RT.Err($"parameter {i + 1} of {what}: a value of type {a.Type} is not assignment compatible with '{p.Name}: {p.Type}'", site);
            if (p.Type.IsStructured) { RT.Copy(a.Addr(f), new Ref(nf.C, p.Slot), p.Size); continue; }
            if (p.Type.IsReal) { nf.C[p.Slot] = Cell.Real(a.Type.IsReal ? a.Real(f) : a.Ord(f)); continue; }
            var c = a.Value(f);
            if (p.Type.Kind == TK.Subrange && (c.N < p.Type.Lo || c.N > p.Type.Hi)) throw RT.RangeErr(c.N, p.Type, site);
            if (p.Type is SetType st && st.Base != null && c.O is PSet s)
            {
                var (mn, mx) = s.Bounds();
                if (mn >= 0 && (mn < st.Base.Lo || mx > st.Base.Hi)) throw RT.Err($"set element out of range for parameter '{p.Name}'", site);
            }
            nf.C[p.Slot] = c;
        }
    }
}

/// <summary>Call of a declared routine, or of a procedural parameter (Closure != null).</summary>
public sealed class CallNode : Expr
{
    readonly RoutineInfo? routine;
    readonly int hops;              // static link: frame hops above the caller
    readonly Expr? closure;         // procedural parameter
    readonly Arg[] args;
    readonly Expr[]? unchecked_;    // 1974-style formal routine without parameter list
    public string Name = "";

    public CallNode(RoutineInfo r, int hops, Arg[] args) { routine = r; this.hops = hops; this.args = args; Type = r.Sig.Result ?? ErrorType.T; }
    public CallNode(Expr closure, Signature sig, Arg[] args, Expr[]? uncheckedArgs)
    {
        this.closure = closure; this.args = args; unchecked_ = uncheckedArgs; Type = sig.Result ?? ErrorType.T;
    }

    public Cell Invoke(Frame f)
    {
        RoutineInfo r; Frame? st;
        if (closure != null)
        {
            var c = closure.Obj(f) as Closure ?? throw RT.Err($"call of an undefined procedural parameter '{Name}'", this);
            r = c.R; st = c.Static;
        }
        else { r = routine!; st = Up(f, hops); }
        var nf = new Frame(r, st, f.Ctx, f.Depth + 1);
        if (unchecked_ != null) Calls.BindUnchecked(unchecked_, f, nf, this);
        else Calls.Bind(args, f, nf, r.Sig.Params);
        f.Line = Line; f.Col = Col;
        return Calls.Run(nf, this);
    }

    public override long Ord(Frame f) => Invoke(f).N;
    public override double Real(Frame f) => Invoke(f).R;
    public override object? Obj(Frame f) => Invoke(f).O;
    public override Cell Value(Frame f) => Invoke(f);
}

/// <summary>Routine used as an actual procedural parameter: builds a closure.</summary>
public sealed class RoutineValue : Expr
{
    readonly RoutineInfo r; readonly int hops;
    public RoutineValue(RoutineInfo r, int hops) { this.r = r; this.hops = hops; Type = r.Sig.Result ?? ErrorType.T; }
    public override object? Obj(Frame f) => new Closure(r, Up(f, hops));
    public override Cell Value(Frame f) => Cell.Obj(Obj(f));
}

/// <summary>Formal procedural parameter passed on as an actual parameter.</summary>
public sealed class FormalRoutineValue : Expr
{
    readonly int hops, slot;
    public FormalRoutineValue(int hops, int slot) { this.hops = hops; this.slot = slot; }
    public override object? Obj(Frame f) => Up(f, hops).C[slot].O;
    public override Cell Value(Frame f) => Up(f, hops).C[slot];
}
