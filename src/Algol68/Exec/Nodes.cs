using System.Runtime.CompilerServices;
using Studio.Algol68.Runtime;
using Studio.Algol68.Semantics;

namespace Studio.Algol68.Exec;

/// <summary>Executable node (compiled tree). Modes are static: each node knows what it yields.</summary>
public abstract class Node
{
    public string File = "";
    public int Line, Col;
    public Mode? Mode;
    public bool Fresh;   // yields a freshly allocated value (no copy needed when bound)
    public bool Transient; // transient name (slice or selection of a FLEX name): may be neither stored nor bound
    public virtual Node? Wrapped => null;

    public abstract Value Eval(Frame f);
    public virtual long EvalInt(Frame f) => Eval(f).N;
    public virtual double EvalReal(Frame f) => Eval(f).Real;
    public virtual bool EvalBool(Frame f) => Eval(f).N != 0;
    public virtual void Exec(Frame f) => Eval(f);
    public virtual object? EvalObj(Frame f) => Eval(f).O;

    public T At<T>(Node n) where T : Node { File = n.File; Line = n.Line; Col = n.Col; return (T)this; }
    public T At<T>(Syntax.Ast a) where T : Node { File = a.File; Line = a.Line; Col = a.Col; return (T)this; }
    public void SetPos(Node n) { File = n.File; Line = n.Line; Col = n.Col; }
    public void SetPos(Syntax.Ast a) { File = a.File; Line = a.Line; Col = a.Col; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static Frame Up(Frame f, int hops)
    {
        while (hops-- > 0) f = f.Static!;
        return f;
    }
}

/// <summary>Information about a routine (or the main program).</summary>
public sealed class RoutineInfo
{
    public string Name = "";
    public int FrameSize;
    public int ParamCount;
    public Node Body = null!;
    public ProcMode? Mode;
    public int Level;
    public string File = ""; public int Line, Col;
    public List<DebugVar> Vars = new();
    public bool CheckResultScope;
    public Mode? ResultMode;
}

public sealed class DebugVar
{
    public string Name = ""; public int Slot; public Mode Mode = null!;
    public int FromLine, ToLine;   // textual extent
    public string File = "";
}

// ---------------------------------------------------------------- constants and identifiers

public sealed class ConstNode : Node
{
    readonly Value v;
    public ConstNode(Value v) { this.v = v; }
    public Value Value => v;
    public override Value Eval(Frame f) => v;
    public override long EvalInt(Frame f) => v.N;
    public override double EvalReal(Frame f) => v.Real;
    public override bool EvalBool(Frame f) => v.N != 0;
    public override object? EvalObj(Frame f) => v.O;
    public override void Exec(Frame f) { }
}

/// <summary>Value computed at run time (global prelude objects: stand out...).</summary>
public sealed class CtxValueNode : Node
{
    readonly Func<ExecContext, Value> get;
    public CtxValueNode(Func<ExecContext, Value> g) { get = g; }
    public override Value Eval(Frame f) => get(f.Ctx);
}

public sealed class SlotNode : Node
{
    public readonly int Slot, Hops;
    public string Name = "";
    public SlotNode(int hops, int slot) { Hops = hops; Slot = slot; }
    public override Value Eval(Frame f)
    {
        var v = Up(f, Hops).S[Slot];
        if (v.O == null && NeedsObj) throw RT.Err($"'{Name}' used before being defined", this);
        return v;
    }
    public bool NeedsObj;
    public override long EvalInt(Frame f) => Up(f, Hops).S[Slot].N;
    public override double EvalReal(Frame f) => Up(f, Hops).S[Slot].Real;
    public override bool EvalBool(Frame f) => Up(f, Hops).S[Slot].N != 0;
    public override object? EvalObj(Frame f) => Eval(f).O;
}

/// <summary>Dereferencing of a name.</summary>
public sealed class DerefNode : Node
{
    readonly Node r;
    readonly bool copy;
    readonly Mode valMode;
    public DerefNode(Node r, Mode valMode, bool copy) { this.r = r; this.valMode = valMode; this.copy = copy; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Value Get(Frame f)
    {
        var rf = Unsafe.As<RefV>(r.EvalObj(f));
        if (rf == null || rf.A == null) throw RT.Err("dereferencing NIL", this);
        var v = rf.A[rf.I];
        if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err("uninitialised value", this);
        return v;
    }
    public override Value Eval(Frame f) { var v = Get(f); return copy ? RT.DeepCopy(v, valMode) : v; }
    public override long EvalInt(Frame f) => Get(f).N;
    public override double EvalReal(Frame f) => Get(f).Real;
    public override bool EvalBool(Frame f) => Get(f).N != 0;
    public override object? EvalObj(Frame f) => Eval(f).O;
}

/// <summary>Direct dereferencing of a local variable (the most frequent case).</summary>
public sealed class DerefSlotNode : Node
{
    readonly int hops, slot;
    readonly bool copy;
    readonly Mode valMode;
    public string Name = "";
    public DerefSlotNode(int hops, int slot, Mode valMode, bool copy) { this.hops = hops; this.slot = slot; this.valMode = valMode; this.copy = copy; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Value Get(Frame f)
    {
        var o = Up(f, hops).S[slot].O;
        if (o == null) throw RT.Err($"'{Name}' used before its declaration", this);
        var rf = Unsafe.As<RefV>(o);
        if (rf.A == null) throw RT.Err("dereferencing NIL", this);
        var v = rf.A[rf.I];
        if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err($"uninitialised value ('{Name}')", this);
        return v;
    }
    public override Value Eval(Frame f) { var v = Get(f); return copy ? RT.DeepCopy(v, valMode) : v; }
    public override long EvalInt(Frame f) => Get(f).N;
    public override double EvalReal(Frame f) => Get(f).Real;
    public override bool EvalBool(Frame f) => Get(f).N != 0;
    public override object? EvalObj(Frame f) => Eval(f).O;
}

// ---------------------------------------------------------------- assignment

public sealed class AssignNode : Node
{
    readonly Node dest, src;
    readonly Mode valMode;
    readonly bool composite, scopeCheck;
    public AssignNode(Node dest, Node src, Mode valMode, bool scopeCheck)
    {
        this.dest = dest; this.src = src; this.valMode = valMode;
        composite = valMode.NeedsCopy; this.scopeCheck = scopeCheck;
    }
    public override Value Eval(Frame f) => Value.Obj(Do(f));
    public override object? EvalObj(Frame f) => Do(f);
    public override void Exec(Frame f) => Do(f);
    RefV Do(Frame f)
    {
        var r = Unsafe.As<RefV>(dest.EvalObj(f));
        if (r == null || r.A == null) throw RT.Err("assignment to NIL", this);
        var v = src.Eval(f);
        if (scopeCheck && RT.ScopeOf(v, valMode) > r.Scope) throw RT.Err("scope violation in an assignment", this);
        if (composite) RT.AssignInto(r.A, r.I, v, valMode, this);
        else
        {
            if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err("assignment of an uninitialised value", this);
            r.A[r.I] = v;
        }
        return r;
    }
}

/// <summary>Assignment of INT/REAL... to a local variable (fast path).</summary>
public sealed class AssignSlotNode : Node
{
    readonly int hops, slot;
    readonly Node src;
    public AssignSlotNode(int hops, int slot, Node src) { this.hops = hops; this.slot = slot; this.src = src; }
    public override Value Eval(Frame f) => Value.Obj(Do(f));
    public override object? EvalObj(Frame f) => Do(f);
    public override void Exec(Frame f) => Do(f);
    RefV Do(Frame f)
    {
        var r = Unsafe.As<RefV>(Up(f, hops).S[slot].O);
        if (r == null) throw RT.Err("variable used before its declaration", this);
        if (r.A == null) throw RT.Err("assignment to NIL", this);
        var v = src.Eval(f);
        if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err("assignment of an uninitialised value", this);
        r.A[r.I] = v;
        return r;
    }
}

// ---------------------------------------------------------------- generators

/// <summary>Describes how an initial value is created (actual bounds).</summary>
public abstract class GenSpec
{
    public abstract Value Create(Frame f, Node at);
}
public sealed class UninitGen : GenSpec
{
    public static readonly UninitGen Instance = new();
    public override Value Create(Frame f, Node at) => Value.UninitValue;
}
public sealed class ConstGen : GenSpec
{
    readonly Func<Value> make;
    public ConstGen(Func<Value> m) { make = m; }
    public override Value Create(Frame f, Node at) => make();
}
public sealed class StructGen : GenSpec
{
    readonly GenSpec[] fields;
    public StructGen(GenSpec[] f) { fields = f; }
    public override Value Create(Frame f, Node at)
    {
        var v = new Value[fields.Length];
        for (int i = 0; i < v.Length; i++) v[i] = fields[i].Create(f, at);
        return Value.Obj(v);
    }
}
public sealed class RowGen : GenSpec
{
    readonly Node?[] lwb, upb;
    readonly GenSpec elem;
    readonly bool elemUninit;
    public RowGen(Node?[] lwb, Node?[] upb, GenSpec elem) { this.lwb = lwb; this.upb = upb; this.elem = elem; elemUninit = elem is UninitGen; }
    public override Value Create(Frame f, Node at)
    {
        int d = lwb.Length;
        var l = new int[d]; var u = new int[d];
        for (int i = 0; i < d; i++)
        {
            l[i] = lwb[i] == null ? 1 : (int)lwb[i]!.EvalInt(f);
            u[i] = upb[i] == null ? 0 : (int)upb[i]!.EvalInt(f);
        }
        var row = RowV.Make(l, u, elemUninit ? Value.UninitValue : default);
        if (!elemUninit)
            for (int i = 0; i < row.Data.Length; i++) row.Data[i] = elem.Create(f, at);
        return Value.Obj(row);
    }
}
/// <summary>Mode declared with actual bounds: evaluated in the environ of the declaration.</summary>
public sealed class IndicantGen : GenSpec
{
    public GenSpec? Inner;
    public int Hops;
    public override Value Create(Frame f, Node at)
    {
        var e = f;
        for (int i = 0; i < Hops; i++) e = e.Static!;
        return Inner!.Create(e, at);
    }
}

public sealed class GeneratorNode : Node
{
    readonly GenSpec gen;
    readonly bool heap;
    readonly int rangeLevel;
    public GeneratorNode(GenSpec g, bool heap, int rangeLevel) { gen = g; this.heap = heap; this.rangeLevel = rangeLevel; }
    public override Value Eval(Frame f) => Value.Obj(Make(f));
    public override object? EvalObj(Frame f) => Make(f);
    RefV Make(Frame f) => RefV.NewCell(gen.Create(f, this), heap ? 0 : f.Depth + rangeLevel);
}

/// <summary>Variable declaration: slot := generated name, then optional initial assignment.</summary>
public sealed class VarDeclNode : Node
{
    readonly int slot;
    readonly GenSpec gen;
    readonly bool heap;
    readonly int rangeLevel;
    readonly Node? init;
    readonly Mode valMode;
    readonly bool composite, scopeCheck;
    public VarDeclNode(int slot, GenSpec gen, bool heap, int rangeLevel, Node? init, Mode valMode, bool scopeCheck)
    { this.slot = slot; this.gen = gen; this.heap = heap; this.rangeLevel = rangeLevel; this.init = init; this.valMode = valMode; composite = valMode.NeedsCopy; this.scopeCheck = scopeCheck; }
    public override Value Eval(Frame f) { Exec(f); return Value.Empty; }
    public override void Exec(Frame f)
    {
        var r = RefV.NewCell(gen.Create(f, this), heap ? 0 : f.Depth + rangeLevel);
        f.S[slot] = Value.Obj(r);
        if (init != null)
        {
            var v = init.Eval(f);
            if (scopeCheck && RT.ScopeOf(v, valMode) > r.Scope) throw RT.Err("scope violation in the initialisation", this);
            if (composite) RT.AssignInto(r.A!, 0, v, valMode, this);
            else
            {
                if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err("initialisation with an uninitialised value", this);
                r.A![0] = v;
            }
        }
    }
}

public sealed class IdentityDeclNode : Node
{
    readonly int slot;
    readonly Node value;
    readonly bool copy;
    readonly Mode mode;
    public IdentityDeclNode(int slot, Node value, Mode mode, bool copy) { this.slot = slot; this.value = value; this.mode = mode; this.copy = copy; }
    public override Value Eval(Frame f) { Exec(f); return Value.Empty; }
    public override void Exec(Frame f)
    {
        var v = value.Eval(f);
        if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err("identity declaration with an uninitialised value", this);
        f.S[slot] = copy ? RT.DeepCopy(v, mode) : v;
    }
}

// ---------------------------------------------------------------- clauses

/// <summary>Statement of a series: updates the current position and handles debugging.</summary>
public sealed class StmtNode : Node
{
    readonly Node inner;
    public override Node? Wrapped => inner;
    public StmtNode(Node inner) { this.inner = inner; SetPos(inner); Mode = inner.Mode; }
    public Node Inner => inner;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Mark(Frame f)
    {
        f.Line = Line; f.Col = Col; f.File = File;
        if (f.Ctx.Attention) f.Ctx.OnAttention(f, this);
    }
    public override Value Eval(Frame f) { Mark(f); return inner.Eval(f); }
    public override long EvalInt(Frame f) { Mark(f); return inner.EvalInt(f); }
    public override double EvalReal(Frame f) { Mark(f); return inner.EvalReal(f); }
    public override bool EvalBool(Frame f) { Mark(f); return inner.EvalBool(f); }
    public override object? EvalObj(Frame f) { Mark(f); return inner.EvalObj(f); }
    public override void Exec(Frame f) { Mark(f); inner.Exec(f); }
}

public sealed class JumpTarget
{
    public string Label = "";
    public SerialNode? Serial;
    public int Index;
    public int RoutineLevel;
}

public sealed class JumpException : Exception
{
    public readonly JumpTarget Target;
    public readonly Frame Frame;
    public JumpException(JumpTarget t, Frame f) { Target = t; Frame = f; }
}

public sealed class SerialNode : Node
{
    public Node[] Stmts = Array.Empty<Node>();
    public bool[] Completes = Array.Empty<bool>();   // the series completes after this statement (EXIT or last one)
    public bool[] Yields = Array.Empty<bool>();
    public bool HasLabels;
    public int ScopeLimitLevel = -1;   // >= 0: scope check of the yield (scope level of the clause)
    public Mode? YieldMode;

    public override Value Eval(Frame f)
    {
        var st = Stmts;
        int i = 0;
        Value v = default;
        while (true)
        {
            try
            {
                for (; i < st.Length; i++)
                {
                    if (Yields[i])
                    {
                        v = st[i].Eval(f);
                        if (Completes[i]) goto done;
                    }
                    else st[i].Exec(f);
                }
                goto done;
            }
            catch (JumpException j) when (HasLabels && ReferenceEquals(j.Target.Serial, this) && ReferenceEquals(j.Frame, f))
            {
                i = j.Target.Index;
            }
        }
    done:
        if (ScopeLimitLevel >= 0 && YieldMode != null)
        {
            int lim = f.Depth + ScopeLimitLevel;
            if (RT.ScopeOf(v, YieldMode) >= lim) throw RT.Err("scope violation: the yield of the clause refers to a local value", this);
        }
        return v;
    }
    public override void Exec(Frame f) => Eval(f);
    public override long EvalInt(Frame f) => Eval(f).N;
}

public sealed class CondNode : Node
{
    readonly Node cond, then; readonly Node? els;
    public CondNode(Node c, Node t, Node? e) { cond = c; then = t; els = e; }
    public override Value Eval(Frame f) => cond.EvalBool(f) ? then.Eval(f) : els != null ? els.Eval(f) : default;
    public override long EvalInt(Frame f) => cond.EvalBool(f) ? then.EvalInt(f) : els!.EvalInt(f);
    public override double EvalReal(Frame f) => cond.EvalBool(f) ? then.EvalReal(f) : els!.EvalReal(f);
    public override bool EvalBool(Frame f) => cond.EvalBool(f) ? then.EvalBool(f) : els!.EvalBool(f);
    public override void Exec(Frame f) { if (cond.EvalBool(f)) then.Exec(f); else els?.Exec(f); }
}

public sealed class CaseNode : Node
{
    readonly Node sel; readonly Node[] cases; readonly Node? outPart;
    public CaseNode(Node s, Node[] c, Node? o) { sel = s; cases = c; outPart = o; }
    public override Value Eval(Frame f)
    {
        long k = sel.EvalInt(f);
        if (k >= 1 && k <= cases.Length) return cases[k - 1].Eval(f);
        return outPart != null ? outPart.Eval(f) : default;
    }
    public override void Exec(Frame f)
    {
        long k = sel.EvalInt(f);
        if (k >= 1 && k <= cases.Length) cases[k - 1].Exec(f);
        else outPart?.Exec(f);
    }
}

public sealed class ConformityNode : Node
{
    public Node Sel = null!;
    public Mode[][] Accept = Array.Empty<Mode[]>();   // concrete modes accepted by each case
    public bool[] RewrapUnion = Array.Empty<bool>();   // the case declares a union: the value stays united
    public int[] Slots = Array.Empty<int>();           // -1: no identifier
    public Node[] Cases = Array.Empty<Node>();
    public Node? Out;
    public override Value Eval(Frame f) => Run(f, true);
    public override void Exec(Frame f) => Run(f, false);
    Value Run(Frame f, bool want)
    {
        var sv = Sel.Eval(f);
        if (sv.O is not UnionV u) throw RT.Err("conformity clause on an uninitialised united value", this);
        for (int c = 0; c < Cases.Length; c++)
        {
            foreach (var m in Accept[c])
                if (ReferenceEquals(m, u.M))
                {
                    if (Slots[c] >= 0) f.S[Slots[c]] = RewrapUnion[c] ? sv : u.V;
                    if (want) return Cases[c].Eval(f);
                    Cases[c].Exec(f); return default;
                }
        }
        if (Out != null) { if (want) return Out.Eval(f); Out.Exec(f); }
        return default;
    }
}

public sealed class LoopNode : Node
{
    public int Slot = -1;
    public Node? From, By, To;
    public bool DownTo;
    public Node? While, Until;
    public Node Body = null!;
    public override Value Eval(Frame f) { Exec(f); return default; }
    public override void Exec(Frame f)
    {
        long from = From?.EvalInt(f) ?? 1;
        long by = By?.EvalInt(f) ?? 1;
        if (DownTo) by = -by;
        bool hasTo = To != null;
        long to = hasTo ? To!.EvalInt(f) : 0;
        var ctx = f.Ctx;
        var s = f.S;
        int slot = Slot;
        var w = While; var u = Until; var body = Body;
        for (long i = from; ; i += by)
        {
            if (hasTo && (by >= 0 ? i > to : i < to)) break;
            if (i > int.MaxValue || i < int.MinValue) throw RT.Err("loop counter overflow", this);
            if (ctx.Attention) ctx.OnAttention(f, this);
            if (slot >= 0) s[slot].N = i;
            if (w != null && !w.EvalBool(f)) break;
            body.Exec(f);
            if (u != null && u.EvalBool(f)) break;
            if (!hasTo && by == 0 && slot < 0 && w == null && u == null) continue;
        }
    }
}

public sealed class SeqVoidNode : Node
{
    readonly Node[] items;
    public SeqVoidNode(Node[] items) { this.items = items; }
    public override Value Eval(Frame f) { foreach (var n in items) n.Exec(f); return default; }
}

public sealed class VoidNode : Node
{
    readonly Node inner;
    public VoidNode(Node i) { inner = i; SetPos(i); }
    public override Value Eval(Frame f) { inner.Exec(f); return default; }
    public override void Exec(Frame f) => inner.Exec(f);
}

public sealed class RowDisplayNode : Node
{
    readonly Node[] elems;
    readonly int dims;          // dimensions of the result
    readonly Mode elemMode;     // mode of the elements (dims == 1) or of the sub-rows
    public RowDisplayNode(Node[] e, int dims, Mode elemMode) { elems = e; this.dims = dims; this.elemMode = elemMode; }
    public override Value Eval(Frame f)
    {
        if (dims == 1)
        {
            var data = new Value[elems.Length];
            bool copy = elemMode.NeedsCopy;
            for (int i = 0; i < elems.Length; i++)
            {
                var v = elems[i].Eval(f);
                data[i] = copy ? RT.DeepCopy(v, elemMode) : v;
            }
            return Value.Obj(RowV.Make1(data));
        }
        // row of rows -> one more dimension; all sub-rows must have the same bounds
        var subs = new RowV[elems.Length];
        for (int i = 0; i < elems.Length; i++) subs[i] = (RowV)RT.CheckInit(elems[i].Eval(f), this).O!;
        int sd = dims - 1;
        var lwb = new int[dims]; var upb = new int[dims];
        lwb[0] = 1; upb[0] = elems.Length;
        if (subs.Length > 0)
        {
            for (int d = 0; d < sd; d++) { lwb[d + 1] = subs[0].Lwb[d]; upb[d + 1] = subs[0].Upb[d]; }
            foreach (var s in subs)
                if (!s.SameBounds(subs[0])) throw RT.Err("row display: the sub-rows do not have the same bounds", this);
        }
        else for (int d = 0; d < sd; d++) { lwb[d + 1] = 1; upb[d + 1] = 0; }
        var row = RowV.Make(lwb, upb, default);
        int k = 0;
        bool cp = elemMode.NeedsCopy;
        foreach (var s in subs)
            foreach (var e in s.Elements()) row.Data[k++] = cp ? RT.DeepCopy(e, elemMode) : e;
        return Value.Obj(row);
    }
}

public sealed class StructDisplayNode : Node
{
    readonly Node[] fields;
    readonly Mode[] modes;
    public StructDisplayNode(Node[] f, Mode[] m) { fields = f; modes = m; }
    public override Value Eval(Frame f)
    {
        var v = new Value[fields.Length];
        for (int i = 0; i < v.Length; i++)
        {
            var x = fields[i].Eval(f);
            v[i] = modes[i].NeedsCopy ? RT.DeepCopy(x, modes[i]) : x;
        }
        return Value.Obj(v);
    }
}

public sealed class IdentityRelNode : Node
{
    readonly Node l, r; readonly bool not;
    public IdentityRelNode(Node l, Node r, bool not) { this.l = l; this.r = r; this.not = not; }
    public override Value Eval(Frame f) => Value.B(EvalBool(f));
    public override bool EvalBool(Frame f)
    {
        var a = l.EvalObj(f) as RefV; var b = r.EvalObj(f) as RefV;
        bool same = a == null || b == null ? a == b : (a.IsNil && b.IsNil) || a.SameAs(b);
        return same != not;
    }
}

public sealed class JumpNode : Node
{
    readonly JumpTarget target; readonly int hops;
    public JumpNode(JumpTarget t, int hops) { target = t; this.hops = hops; }
    public override Value Eval(Frame f) => throw new JumpException(target, Up(f, hops));
}

/// <summary>Label used as a PROC VOID (RR: jump in a PROC VOID context).</summary>
public sealed class JumpProcNode : Node
{
    readonly JumpTarget target; readonly int hops;
    public JumpProcNode(JumpTarget t, int hops) { target = t; this.hops = hops; }
    public override Value Eval(Frame f)
    {
        var tf = Up(f, hops);
        return Value.Obj(new ProcV { Jump = target, Env = tf, Scope = tf.Depth, Name = target.Label });
    }
}

public sealed class AndThenNode : Node
{
    readonly Node l, r; readonly bool isOr;
    public AndThenNode(Node l, Node r, bool isOr) { this.l = l; this.r = r; this.isOr = isOr; }
    public override Value Eval(Frame f) => Value.B(EvalBool(f));
    public override bool EvalBool(Frame f) => isOr ? (l.EvalBool(f) || r.EvalBool(f)) : (l.EvalBool(f) && r.EvalBool(f));
}

public sealed class AssertNode : Node
{
    readonly Node c;
    public AssertNode(Node c) { this.c = c; }
    public override Value Eval(Frame f)
    {
        if (!c.EvalBool(f)) throw RT.Err("assertion failed (ASSERT)", this);
        return default;
    }
}

public sealed class NilNode : Node
{
    public override Value Eval(Frame f) => Value.Obj(RefV.Nil);
    public override object? EvalObj(Frame f) => RefV.Nil;
}

/// <summary>Undefined value (SKIP) of the required mode.</summary>
public sealed class SkipNode : Node
{
    readonly Func<Value> make;
    public SkipNode(Func<Value> make) { this.make = make; }
    public override Value Eval(Frame f) => make();
    public override void Exec(Frame f) { }
}

// ---------------------------------------------------------------- coercions

public sealed class WidenIntRealNode : Node
{
    readonly Node a;
    public WidenIntRealNode(Node a) { this.a = a; SetPos(a); }
    public override Value Eval(Frame f) => Value.R(a.EvalInt(f));
    public override double EvalReal(Frame f) => a.EvalInt(f);
}

public sealed class FuncNode : Node
{
    readonly Node a; readonly Func<Value, Node, Value> fn;
    public FuncNode(Node a, Func<Value, Node, Value> fn) { this.a = a; this.fn = fn; SetPos(a); }
    public override Value Eval(Frame f) => fn(a.Eval(f), this);
}

public sealed class UniteNode : Node
{
    readonly Node a; readonly Mode member;
    public override Node? Wrapped => a;
    public UniteNode(Node a, Mode member) { this.a = a; this.member = member; SetPos(a); }
    public override Value Eval(Frame f)
    {
        var v = a.Eval(f);
        if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err("uniting an uninitialised value", this);
        return Value.Obj(new UnionV(member, v));
    }
}

/// <summary>Already united value placed in a wider union: nothing to do.</summary>
public sealed class ReuniteNode : Node
{
    readonly Node a;
    public override Node? Wrapped => a;
    public ReuniteNode(Node a) { this.a = a; SetPos(a); }
    public override Value Eval(Frame f) => a.Eval(f);
}

/// <summary>Rowing of a value: X -> [1:1] X (or adding dimensions).</summary>
public sealed class RowValueNode : Node
{
    readonly Node a; readonly int addDims; readonly bool fromRow; readonly Mode elemMode;
    public RowValueNode(Node a, int addDims, bool fromRow, Mode elemMode) { this.a = a; this.addDims = addDims; this.fromRow = fromRow; this.elemMode = elemMode; SetPos(a); }
    public override Value Eval(Frame f)
    {
        var v = a.Eval(f);
        if (!fromRow)
        {
            var data = new[] { elemMode.NeedsCopy ? RT.DeepCopy(v, elemMode) : v };
            int d = addDims;
            var l = Enumerable.Repeat(1, d).ToArray();
            var s = Enumerable.Repeat(1, d).ToArray();
            return Value.Obj(new RowV(data, 0, l, (int[])l.Clone(), s));
        }
        var r = (RowV)RT.CheckInit(v, this).O!;
        var lw = new int[r.Dims + addDims]; var up = new int[r.Dims + addDims]; var st = new int[r.Dims + addDims];
        for (int i = 0; i < addDims; i++) { lw[i] = 1; up[i] = 1; st[i] = 0; }
        for (int i = 0; i < r.Dims; i++) { lw[addDims + i] = r.Lwb[i]; up[addDims + i] = r.Upb[i]; st[addDims + i] = r.Stride[i]; }
        return Value.Obj(new RowV(r.Data, r.Offset, lw, up, st, r.Path));
    }
}

/// <summary>Rowing of a name: REF X -> REF [1:1] X (the cell is shared).</summary>
public sealed class RowNameNode : Node
{
    readonly Node a; readonly int addDims; readonly bool fromRow;
    public override Node? Wrapped => a;
    public RowNameNode(Node a, int addDims, bool fromRow) { this.a = a; this.addDims = addDims; this.fromRow = fromRow; SetPos(a); }
    public override Value Eval(Frame f)
    {
        var r = RT.CheckNil(a.EvalObj(f) as RefV, this);
        if (!fromRow)
        {
            var l = Enumerable.Repeat(1, addDims).ToArray();
            var row = new RowV(r.A!, r.I, l, (int[])l.Clone(), Enumerable.Repeat(1, addDims).ToArray());
            return Value.Obj(RefV.NewCell(Value.Obj(row), r.Scope));
        }
        var src = (RowV)RT.CheckInit(r.A![r.I], this).O!;
        var lw = new int[src.Dims + addDims]; var up = new int[src.Dims + addDims]; var st = new int[src.Dims + addDims];
        for (int i = 0; i < addDims; i++) { lw[i] = 1; up[i] = 1; st[i] = 0; }
        for (int i = 0; i < src.Dims; i++) { lw[addDims + i] = src.Lwb[i]; up[addDims + i] = src.Upb[i]; st[addDims + i] = src.Stride[i]; }
        return Value.Obj(RefV.NewCell(Value.Obj(new RowV(src.Data, src.Offset, lw, up, st, src.Path)), r.Scope));
    }
}

public sealed class CopyNode : Node
{
    readonly Node a; readonly Mode m;
    public CopyNode(Node a, Mode m) { this.a = a; this.m = m; SetPos(a); }
    public override Value Eval(Frame f) => RT.DeepCopy(a.Eval(f), m);
}
