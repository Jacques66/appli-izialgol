using System.Runtime.CompilerServices;
using Studio.Algol68.Runtime;

namespace Studio.Algol68.Exec;

// Noeuds specialises des operateurs standard les plus frequents (INT, REAL, BOOL).

public abstract class Bin : Node
{
    protected readonly Node A, B;
    protected Bin(Node a, Node b) { A = a; B = b; }
}

public abstract class IntBin : Bin
{
    protected IntBin(Node a, Node b) : base(a, b) { }
    public override Value Eval(Frame f) => Value.I(EvalInt(f));
    public override double EvalReal(Frame f) => EvalInt(f);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected long Chk(long r)
    {
        if (r > int.MaxValue || r < int.MinValue) throw RT.Err("débordement d'entier (INT)", this);
        return r;
    }
}
public sealed class IntAdd : IntBin { public IntAdd(Node a, Node b) : base(a, b) { } public override long EvalInt(Frame f) => Chk(A.EvalInt(f) + B.EvalInt(f)); }
public sealed class IntSub : IntBin { public IntSub(Node a, Node b) : base(a, b) { } public override long EvalInt(Frame f) => Chk(A.EvalInt(f) - B.EvalInt(f)); }
public sealed class IntMul : IntBin { public IntMul(Node a, Node b) : base(a, b) { } public override long EvalInt(Frame f) => Chk(A.EvalInt(f) * B.EvalInt(f)); }
public sealed class IntOver : IntBin
{
    public IntOver(Node a, Node b) : base(a, b) { }
    public override long EvalInt(Frame f)
    {
        long x = A.EvalInt(f), y = B.EvalInt(f);
        if (y == 0) throw RT.Err("division entière par zéro", this);
        return Chk(x / y);
    }
}
public sealed class IntMod : IntBin
{
    public IntMod(Node a, Node b) : base(a, b) { }
    public override long EvalInt(Frame f)
    {
        long x = A.EvalInt(f), y = B.EvalInt(f);
        if (y == 0) throw RT.Err("MOD par zéro", this);
        long r = x % y;
        if (r < 0) r += Math.Abs(y);
        return r;
    }
}
public sealed class IntPow : IntBin
{
    public IntPow(Node a, Node b) : base(a, b) { }
    public override long EvalInt(Frame f)
    {
        long x = A.EvalInt(f), n = B.EvalInt(f);
        if (n < 0) throw RT.Err("exposant négatif dans INT ** INT", this);
        long r = 1;
        while (n > 0)
        {
            if ((n & 1) != 0) r = Chk(r * x);
            n >>= 1;
            if (n > 0) x = Chk(x * x);
        }
        return r;
    }
}

public enum Cmp { Lt, Le, Eq, Ne, Ge, Gt }

public sealed class IntCmp : Bin
{
    readonly Cmp op;
    public IntCmp(Node a, Node b, Cmp op) : base(a, b) { this.op = op; }
    public override Value Eval(Frame f) => Value.B(EvalBool(f));
    public override bool EvalBool(Frame f)
    {
        long x = A.EvalInt(f), y = B.EvalInt(f);
        return op switch { Cmp.Lt => x < y, Cmp.Le => x <= y, Cmp.Eq => x == y, Cmp.Ne => x != y, Cmp.Ge => x >= y, _ => x > y };
    }
}

public sealed class RealCmp : Bin
{
    readonly Cmp op;
    public RealCmp(Node a, Node b, Cmp op) : base(a, b) { this.op = op; }
    public override Value Eval(Frame f) => Value.B(EvalBool(f));
    public override bool EvalBool(Frame f)
    {
        double x = A.EvalReal(f), y = B.EvalReal(f);
        return op switch { Cmp.Lt => x < y, Cmp.Le => x <= y, Cmp.Eq => x == y, Cmp.Ne => x != y, Cmp.Ge => x >= y, _ => x > y };
    }
}

public abstract class RealBin : Bin
{
    protected RealBin(Node a, Node b) : base(a, b) { }
    public override Value Eval(Frame f) => Value.R(EvalReal(f));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected double Chk(double r)
    {
        if (double.IsInfinity(r) || double.IsNaN(r)) throw RT.Err("débordement réel (REAL)", this);
        return r;
    }
}
public sealed class RealAdd : RealBin { public RealAdd(Node a, Node b) : base(a, b) { } public override double EvalReal(Frame f) => Chk(A.EvalReal(f) + B.EvalReal(f)); }
public sealed class RealSub : RealBin { public RealSub(Node a, Node b) : base(a, b) { } public override double EvalReal(Frame f) => Chk(A.EvalReal(f) - B.EvalReal(f)); }
public sealed class RealMul : RealBin { public RealMul(Node a, Node b) : base(a, b) { } public override double EvalReal(Frame f) => Chk(A.EvalReal(f) * B.EvalReal(f)); }
public sealed class RealDiv : RealBin
{
    public RealDiv(Node a, Node b) : base(a, b) { }
    public override double EvalReal(Frame f)
    {
        double x = A.EvalReal(f), y = B.EvalReal(f);
        if (y == 0) throw RT.Err("division par zéro", this);
        return Chk(x / y);
    }
}

public sealed class IntNeg : Node
{
    readonly Node a;
    public IntNeg(Node a) { this.a = a; }
    public override Value Eval(Frame f) => Value.I(EvalInt(f));
    public override long EvalInt(Frame f) { long v = -a.EvalInt(f); if (v > int.MaxValue) throw RT.Err("débordement d'entier (INT)", this); return v; }
}
public sealed class RealNeg : Node
{
    readonly Node a;
    public RealNeg(Node a) { this.a = a; }
    public override Value Eval(Frame f) => Value.R(-a.EvalReal(f));
    public override double EvalReal(Frame f) => -a.EvalReal(f);
}

public sealed class BoolAnd : Bin
{
    public BoolAnd(Node a, Node b) : base(a, b) { }
    public override Value Eval(Frame f) => Value.B(EvalBool(f));
    public override bool EvalBool(Frame f) { bool x = A.EvalBool(f); bool y = B.EvalBool(f); return x & y; }
}
public sealed class BoolOr : Bin
{
    public BoolOr(Node a, Node b) : base(a, b) { }
    public override Value Eval(Frame f) => Value.B(EvalBool(f));
    public override bool EvalBool(Frame f) { bool x = A.EvalBool(f); bool y = B.EvalBool(f); return x | y; }
}
public sealed class BoolNot : Node
{
    readonly Node a;
    public BoolNot(Node a) { this.a = a; }
    public override Value Eval(Frame f) => Value.B(!a.EvalBool(f));
    public override bool EvalBool(Frame f) => !a.EvalBool(f);
}

/// <summary>Operateur dyadique generique.</summary>
public sealed class Op2 : Bin
{
    readonly Func<Value, Value, Node, Value> fn;
    public Op2(Node a, Node b, Func<Value, Value, Node, Value> fn) : base(a, b) { this.fn = fn; Fresh = true; }
    public override Value Eval(Frame f) => fn(A.Eval(f), B.Eval(f), this);
}
/// <summary>Operateur monadique generique.</summary>
public sealed class Op1 : Node
{
    readonly Node a; readonly Func<Value, Node, Value> fn;
    public Op1(Node a, Func<Value, Node, Value> fn) { this.a = a; this.fn = fn; Fresh = true; }
    public override Value Eval(Frame f) => fn(a.Eval(f), this);
}
/// <summary>Operateur generique a contexte (acces au cadre : SEMA, transput...).</summary>
public sealed class OpF : Node
{
    readonly Node[] args; readonly Func<Frame, Value[], Node, Value> fn;
    public OpF(Node[] args, Func<Frame, Value[], Node, Value> fn) { this.args = args; this.fn = fn; Fresh = true; }
    public override Value Eval(Frame f)
    {
        var v = new Value[args.Length];
        for (int i = 0; i < v.Length; i++) v[i] = args[i].Eval(f);
        return fn(f, v, this);
    }
}

/// <summary>Operateurs d'affectation combinee (PLUSAB...) : a := a OP b, rend a.</summary>
public sealed class OpAb : Node
{
    readonly Node r, v; readonly Func<Value, Value, Node, Value> fn; readonly Semantics.Mode? assignMode;
    public OpAb(Node r, Node v, Func<Value, Value, Node, Value> fn, Semantics.Mode? assignMode = null) { this.r = r; this.v = v; this.fn = fn; this.assignMode = assignMode; }
    public override Value Eval(Frame f) => Value.Obj(Do(f));
    public override object? EvalObj(Frame f) => Do(f);
    public override void Exec(Frame f) => Do(f);
    RefV Do(Frame f)
    {
        var rf = RT.CheckNil(r.EvalObj(f) as RefV, this);
        var b = v.Eval(f);
        var old = RT.CheckInit(rf.A![rf.I], this);
        var nv = fn(old, b, this);
        if (assignMode != null) RT.AssignInto(rf.A!, rf.I, nv, assignMode, this);
        else rf.A![rf.I] = nv;
        return rf;
    }
}
/// <summary>PLUSTO : a +=: b  (b := a + b), rend b.</summary>
public sealed class OpTo : Node
{
    readonly Node a, r; readonly Func<Value, Value, Node, Value> fn; readonly Semantics.Mode assignMode;
    public OpTo(Node a, Node r, Func<Value, Value, Node, Value> fn, Semantics.Mode assignMode) { this.a = a; this.r = r; this.fn = fn; this.assignMode = assignMode; }
    public override Value Eval(Frame f) => Value.Obj(Do(f));
    public override object? EvalObj(Frame f) => Do(f);
    RefV Do(Frame f)
    {
        var x = a.Eval(f);
        var rf = RT.CheckNil(r.EvalObj(f) as RefV, this);
        var old = RT.CheckInit(rf.A![rf.I], this);
        RT.AssignInto(rf.A!, rf.I, fn(x, old, this), assignMode, this);
        return rf;
    }
}

public sealed class IntPlusAb : Node
{
    readonly Node r, v; readonly int kind; // 0 +, 1 -, 2 *, 3 %, 4 MOD
    public IntPlusAb(Node r, Node v, int kind) { this.r = r; this.v = v; this.kind = kind; }
    public override Value Eval(Frame f) => Value.Obj(Do(f));
    public override object? EvalObj(Frame f) => Do(f);
    public override void Exec(Frame f) => Do(f);
    RefV Do(Frame f)
    {
        var rf = Unsafe.As<RefV>(r.EvalObj(f));
        if (rf == null || rf.A == null) throw RT.Err("affectation à NIL", this);
        long b = v.EvalInt(f);
        ref Value cell = ref rf.A[rf.I];
        if (ReferenceEquals(cell.O, Value.Uninit)) throw RT.Err("valeur non initialisée", this);
        long a = cell.N, res;
        switch (kind)
        {
            case 0: res = a + b; break;
            case 1: res = a - b; break;
            case 2: res = a * b; break;
            case 3: if (b == 0) throw RT.Err("division entière par zéro", this); res = a / b; break;
            default:
                if (b == 0) throw RT.Err("MOD par zéro", this);
                res = a % b; if (res < 0) res += Math.Abs(b); break;
        }
        if (res > int.MaxValue || res < int.MinValue) throw RT.Err("débordement d'entier (INT)", this);
        cell.N = res;
        return rf;
    }
}

public sealed class RealPlusAb : Node
{
    readonly Node r, v; readonly int kind; // 0 + 1 - 2 * 3 /
    public RealPlusAb(Node r, Node v, int kind) { this.r = r; this.v = v; this.kind = kind; }
    public override Value Eval(Frame f) => Value.Obj(Do(f));
    public override object? EvalObj(Frame f) => Do(f);
    public override void Exec(Frame f) => Do(f);
    RefV Do(Frame f)
    {
        var rf = Unsafe.As<RefV>(r.EvalObj(f));
        if (rf == null || rf.A == null) throw RT.Err("affectation à NIL", this);
        double b = v.EvalReal(f);
        ref Value cell = ref rf.A[rf.I];
        if (ReferenceEquals(cell.O, Value.Uninit)) throw RT.Err("valeur non initialisée", this);
        double a = cell.Real, res;
        switch (kind)
        {
            case 0: res = a + b; break;
            case 1: res = a - b; break;
            case 2: res = a * b; break;
            default: if (b == 0) throw RT.Err("division par zéro", this); res = a / b; break;
        }
        if (double.IsInfinity(res) || double.IsNaN(res)) throw RT.Err("débordement réel (REAL)", this);
        cell.N = BitConverter.DoubleToInt64Bits(res);
        return rf;
    }
}

/// <summary>Bornes d'une rangee (LWB, UPB).</summary>
public sealed class BoundNode : Node
{
    readonly Node? dim; readonly Node row; readonly bool upper;
    public BoundNode(Node? dim, Node row, bool upper) { this.dim = dim; this.row = row; this.upper = upper; }
    public override Value Eval(Frame f) => Value.I(EvalInt(f));
    public override long EvalInt(Frame f)
    {
        long d = dim?.EvalInt(f) ?? 1;
        var r = RowOps.GetRow(row.Eval(f), this);
        if (d < 1 || d > r.Dims) throw RT.Err($"dimension {d} inexistante (la rangée a {r.Dims} dimension(s))", this);
        return upper ? r.Upb[d - 1] : r.Lwb[d - 1];
    }
}
