using System.Runtime.CompilerServices;
using Studio.Pascal.Runtime;
using Studio.Pascal.Semantics;

namespace Studio.Pascal.Exec;

/// <summary>Executable node of the compiled tree.</summary>
public abstract class Node
{
    public string File = "";
    public int Line, Col;

    public T At<T>(Node n) where T : Node { File = n.File; Line = n.Line; Col = n.Col; return (T)this; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static Frame Up(Frame f, int hops)
    {
        while (hops-- > 0) f = f.Static!;
        return f;
    }

    protected InvalidOperationException Internal() => new($"internal error: {GetType().Name} cannot be evaluated this way");
}

/// <summary>Expression. Types are static: Ord for ordinal values, Real for reals, Obj for sets, pointers and
/// routines, Addr for structured values and variables.</summary>
public abstract class Expr : Node
{
    public PType Type = ErrorType.T;
    public virtual long Ord(Frame f) => throw Internal();
    public virtual double Real(Frame f) => throw Internal();
    public virtual object? Obj(Frame f) => throw Internal();
    public virtual Ref Addr(Frame f) => throw Internal();
    /// <summary>Scalar value as a cell.</summary>
    public virtual Cell Value(Frame f)
    {
        if (Type.IsReal) return Cell.Real(Real(f));
        if (Type.IsOrdinal) return Cell.Ord(Ord(f));
        return Cell.Obj(Obj(f));
    }
    public bool Bool(Frame f) => Ord(f) != 0;
}

// ---------------------------------------------------------------- constants

public sealed class ConstOrd : Expr
{
    public readonly long V;
    public ConstOrd(long v, PType t) { V = v; Type = t; }
    public override long Ord(Frame f) => V;
    public override Cell Value(Frame f) => Cell.Ord(V);
}

public sealed class ConstReal : Expr
{
    public readonly double V;
    public ConstReal(double v) { V = v; Type = RealType.T; }
    public override double Real(Frame f) => V;
    public override Cell Value(Frame f) => Cell.Real(V);
}

public sealed class ConstObj : Expr
{
    public readonly object? V;
    public ConstObj(object? v, PType t) { V = v; Type = t; }
    public override object? Obj(Frame f) => V;
    public override Cell Value(Frame f) => Cell.Obj(V);
}

/// <summary>String constant (packed array[1..n] of char); its cells are never modified.</summary>
public sealed class ConstStr : Expr
{
    public readonly string Text;
    readonly Cell[] cells;
    public ConstStr(string s, PType t)
    {
        Text = s; Type = t;
        cells = s.Select(c => Cell.Ord(c)).ToArray();
    }
    public override Ref Addr(Frame f) => new(cells, 0);
}

// ---------------------------------------------------------------- variables

/// <summary>Variable access (entire variable, component, referenced variable, buffer variable).</summary>
public abstract class Designator : Expr
{
    /// <summary>Source text (for messages).</summary>
    public string Desc = "";
    /// <summary>Component of a packed array or record (cannot be a var parameter).</summary>
    public bool PackedComponent;
    /// <summary>Tag field of a variant part (assignments clear the variants).</summary>
    public Field? TagField;
    /// <summary>Entire variable at the root of the access (for loop control checks).</summary>
    public object? RootSymbol;
    public bool IsEntire;

    [MethodImpl(MethodImplOptions.NoInlining)]
    protected PasError Undefined() => RT.Err($"undefined value: {Desc}", this);

    public override long Ord(Frame f)
    {
        var r = Addr(f);
        ref var c = ref r.S[r.I];
        if (ReferenceEquals(c.O, Undef.V)) throw Undefined();
        return c.N;
    }
    public override double Real(Frame f)
    {
        var r = Addr(f);
        ref var c = ref r.S[r.I];
        if (ReferenceEquals(c.O, Undef.V)) throw Undefined();
        return BitConverter.Int64BitsToDouble(c.N);
    }
    public override object? Obj(Frame f)
    {
        var r = Addr(f);
        var o = r.S[r.I].O;
        if (ReferenceEquals(o, Undef.V)) throw Undefined();
        return o;
    }
    public override Cell Value(Frame f)
    {
        var r = Addr(f);
        var c = r.S[r.I];
        if (ReferenceEquals(c.O, Undef.V)) throw Undefined();
        return c;
    }
}

/// <summary>Variable of the current frame or of an enclosing frame (hops static links up).</summary>
public sealed class VarNode : Designator
{
    public readonly int Hops, Slot;
    public VarNode(int hops, int slot) { Hops = hops; Slot = slot; IsEntire = true; }
    public override Ref Addr(Frame f) => new(Up(f, Hops).C, Slot);
    public override long Ord(Frame f)
    {
        ref var c = ref Up(f, Hops).C[Slot];
        if (ReferenceEquals(c.O, Undef.V)) throw Undefined();
        return c.N;
    }
    public override double Real(Frame f)
    {
        ref var c = ref Up(f, Hops).C[Slot];
        if (ReferenceEquals(c.O, Undef.V)) throw Undefined();
        return BitConverter.Int64BitsToDouble(c.N);
    }
}

/// <summary>Variable parameter or with-record: the slot holds a reference (O = store, N = index) plus an offset.</summary>
public sealed class RefVarNode : Designator
{
    public readonly int Hops, Slot, Offset;
    public RefVarNode(int hops, int slot, int offset) { Hops = hops; Slot = slot; Offset = offset; }
    public override Ref Addr(Frame f)
    {
        var c = Up(f, Hops).C[Slot];
        return new(Unsafe.As<Cell[]>(c.O)!, (int)c.N + Offset);
    }
}

/// <summary>Result variable of a function (assignment to the function identifier).</summary>
public sealed class ResultNode : Designator
{
    public readonly int Hops, Slot;
    public ResultNode(int hops, int slot) { Hops = hops; Slot = slot; IsEntire = true; }
    public override Ref Addr(Frame f) => new(Up(f, Hops).C, Slot);
}

/// <summary>Indexed variable a[i].</summary>
public sealed class IndexNode : Designator
{
    readonly Expr arr, index;
    readonly long lo, hi;
    readonly int elemSize;
    readonly PType indexType;
    public IndexNode(Expr arr, Expr index, ArrayType at)
    {
        this.arr = arr; this.index = index;
        lo = at.Index.Lo; hi = at.Index.Hi; elemSize = at.Elem.Size; indexType = at.Index;
        Type = at.Elem;
    }
    public override Ref Addr(Frame f)
    {
        var a = arr.Addr(f);
        long i = index.Ord(f);
        if (i < lo || i > hi) throw RT.Err($"index {Format.OrdText(indexType, i)} out of bounds {Format.OrdText(indexType, lo)}..{Format.OrdText(indexType, hi)} in {Desc}", this);
        return new(a.S, a.I + (int)(i - lo) * elemSize);
    }
}

/// <summary>Check of a variant field: the tag at TagOffset (relative to the record) must select one of Labels.</summary>
public sealed record VariantCheck(int TagOffset, HashSet<long> Labels, Field Tag, string Variant);

/// <summary>Field designator r.f (also a field named inside a with statement).</summary>
public sealed class FieldNode : Designator
{
    readonly Expr rec;
    readonly int offset;
    readonly VariantCheck[]? checks;
    public FieldNode(Expr rec, Field fld, VariantCheck[]? checks)
    {
        this.rec = rec; offset = fld.Offset; this.checks = checks is { Length: > 0 } ? checks : null; Type = fld.Type;
    }
    public override Ref Addr(Frame f)
    {
        var r = rec.Addr(f);
        if (checks != null) Check(r);
        return new(r.S, r.I + offset);
    }
    void Check(Ref r)
    {
        foreach (var ch in checks!)
        {
            var c = r.S[r.I + ch.TagOffset];
            if (c.IsUndef) throw RT.Err($"access to the variant field {Desc} while the tag field '{ch.Tag.Name}' is undefined", this);
            if (!ch.Labels.Contains(c.N))
                throw RT.Err($"access to the variant field {Desc} of an inactive variant ({ch.Tag.Name} = {Format.OrdText(ch.Tag.Type, c.N)})", this);
        }
    }
}

/// <summary>Referenced variable p^.</summary>
public sealed class DerefNode : Designator
{
    readonly Expr ptr;
    public DerefNode(Expr ptr, PType target) { this.ptr = ptr; Type = target; }
    public override Ref Addr(Frame f)
    {
        var o = ptr.Obj(f);
        if (o is not HeapObj h) throw RT.Err($"nil pointer dereference: {Desc}", this);
        if (h.Disposed) throw RT.Err($"dangling pointer: {Desc} refers to a disposed variable", this);
        return new(h.C, 0);
    }
}

/// <summary>Buffer variable f^.</summary>
public sealed class BufferNode : Designator
{
    readonly Expr file;
    public BufferNode(Expr file, PType elem) { this.file = file; Type = elem; }
    public override Ref Addr(Frame f) => FileOf(file, f, this).BufferRef(this);
    public static PFile FileOf(Expr e, Frame f, Node at)
    {
        var r = e.Addr(f);
        return r.S[r.I].O as PFile ?? throw RT.Err("file variable not initialised", at);
    }
}

// ---------------------------------------------------------------- conversions and checks

public sealed class IntToReal : Expr
{
    readonly Expr e;
    public IntToReal(Expr e) { this.e = e; Type = RealType.T; At<IntToReal>(e); }
    public override double Real(Frame f) => e.Ord(f);
}

/// <summary>Range check of an ordinal value assigned to a subrange.</summary>
public sealed class RangeCheck : Expr
{
    readonly Expr e; readonly long lo, hi;
    public RangeCheck(Expr e, PType target) { this.e = e; lo = target.Lo; hi = target.Hi; Type = target; At<RangeCheck>(e); }
    public override long Ord(Frame f)
    {
        long v = e.Ord(f);
        if (v < lo || v > hi) throw RT.RangeErr(v, Type, this);
        return v;
    }
    public override Cell Value(Frame f) => Cell.Ord(Ord(f));
}

/// <summary>Check that the elements of a set value belong to the base type of the target.</summary>
public sealed class SetRangeCheck : Expr
{
    readonly Expr e; readonly long lo, hi; readonly PType bt;
    public SetRangeCheck(Expr e, SetType target) { this.e = e; bt = target.Base!; lo = bt.Lo; hi = bt.Hi; Type = target; At<SetRangeCheck>(e); }
    public override object? Obj(Frame f)
    {
        var s = (PSet)e.Obj(f)!;
        var (mn, mx) = s.Bounds();
        if (mn >= 0 && (mn < lo || mx > hi))
            throw RT.Err($"set element {Format.OrdText(bt, mn < lo ? mn : mx)} out of range {Format.OrdText(bt, lo)}..{Format.OrdText(bt, hi)}", this);
        return s;
    }
}

// ---------------------------------------------------------------- integer arithmetic

public enum Op { Add, Sub, Mul, RDiv, Div, Mod, And, Or, Eq, Ne, Lt, Le, Gt, Ge, In }

public sealed class IntBin : Expr
{
    readonly Expr a, b; readonly Op op;
    public IntBin(Op op, Expr a, Expr b) { this.op = op; this.a = a; this.b = b; Type = IntegerType.T; }
    public override long Ord(Frame f)
    {
        long x = a.Ord(f), y = b.Ord(f);
        switch (op)
        {
            case Op.Add: return RT.CheckInt(x + y, this);
            case Op.Sub: return RT.CheckInt(x - y, this);
            case Op.Mul: return RT.Mul(x, y, this);
            case Op.Div:
                if (y == 0) throw RT.Err("division by zero (div)", this);
                return x / y;
            case Op.Mod:
                if (y == 0) throw RT.Err("division by zero (mod)", this);
                return x % y;
        }
        throw Internal();
    }
}

public sealed class IntNeg : Expr
{
    readonly Expr a;
    public IntNeg(Expr a) { this.a = a; Type = IntegerType.T; }
    public override long Ord(Frame f) => -a.Ord(f);
}

public sealed class RealBin : Expr
{
    readonly Expr a, b; readonly Op op;
    public RealBin(Op op, Expr a, Expr b) { this.op = op; this.a = a; this.b = b; Type = RealType.T; }
    public override double Real(Frame f)
    {
        double x = a.Real(f), y = b.Real(f);
        switch (op)
        {
            case Op.Add: return RT.CheckReal(x + y, this);
            case Op.Sub: return RT.CheckReal(x - y, this);
            case Op.Mul: return RT.CheckReal(x * y, this);
            case Op.RDiv:
                if (y == 0) throw RT.Err("division by zero (/)", this);
                return RT.CheckReal(x / y, this);
        }
        throw Internal();
    }
}

public sealed class RealNeg : Expr
{
    readonly Expr a;
    public RealNeg(Expr a) { this.a = a; Type = RealType.T; }
    public override double Real(Frame f) => -a.Real(f);
}

// ---------------------------------------------------------------- boolean operators (both operands are evaluated)

public sealed class BoolBin : Expr
{
    readonly Expr a, b; readonly bool isAnd;
    public BoolBin(bool isAnd, Expr a, Expr b) { this.isAnd = isAnd; this.a = a; this.b = b; Type = EnumType.Boolean; }
    public override long Ord(Frame f)
    {
        long x = a.Ord(f), y = b.Ord(f);
        return isAnd ? x & y : x | y;
    }
}

public sealed class BoolNot : Expr
{
    readonly Expr a;
    public BoolNot(Expr a) { this.a = a; Type = EnumType.Boolean; }
    public override long Ord(Frame f) => a.Ord(f) ^ 1;
}

// ---------------------------------------------------------------- comparisons

public sealed class OrdCmp : Expr
{
    readonly Expr a, b; readonly Op op;
    public OrdCmp(Op op, Expr a, Expr b) { this.op = op; this.a = a; this.b = b; Type = EnumType.Boolean; }
    public override long Ord(Frame f)
    {
        long x = a.Ord(f), y = b.Ord(f);
        bool r = op switch { Op.Eq => x == y, Op.Ne => x != y, Op.Lt => x < y, Op.Le => x <= y, Op.Gt => x > y, _ => x >= y };
        return r ? 1 : 0;
    }
}

public sealed class RealCmp : Expr
{
    readonly Expr a, b; readonly Op op;
    public RealCmp(Op op, Expr a, Expr b) { this.op = op; this.a = a; this.b = b; Type = EnumType.Boolean; }
    public override long Ord(Frame f)
    {
        double x = a.Real(f), y = b.Real(f);
        bool r = op switch { Op.Eq => x == y, Op.Ne => x != y, Op.Lt => x < y, Op.Le => x <= y, Op.Gt => x > y, _ => x >= y };
        return r ? 1 : 0;
    }
}

/// <summary>Comparison of strings (packed arrays of char of the same length), lexicographic.</summary>
public sealed class StrCmp : Expr
{
    readonly Expr a, b; readonly Op op; readonly int len;
    public StrCmp(Op op, Expr a, Expr b, int len) { this.op = op; this.a = a; this.b = b; this.len = len; Type = EnumType.Boolean; }
    public override long Ord(Frame f)
    {
        var x = a.Addr(f); var y = b.Addr(f);
        int c = 0;
        for (int i = 0; i < len && c == 0; i++)
        {
            var cx = x.S[x.I + i]; var cy = y.S[y.I + i];
            if (cx.IsUndef || cy.IsUndef) throw RT.Err("comparison of a string with undefined characters", this);
            c = cx.N.CompareTo(cy.N);
        }
        bool r = op switch { Op.Eq => c == 0, Op.Ne => c != 0, Op.Lt => c < 0, Op.Le => c <= 0, Op.Gt => c > 0, _ => c >= 0 };
        return r ? 1 : 0;
    }
}

public sealed class SetCmp : Expr
{
    readonly Expr a, b; readonly Op op;
    public SetCmp(Op op, Expr a, Expr b) { this.op = op; this.a = a; this.b = b; Type = EnumType.Boolean; }
    public override long Ord(Frame f)
    {
        var x = (PSet)a.Obj(f)!; var y = (PSet)b.Obj(f)!;
        bool r = op switch { Op.Eq => x.SameAs(y), Op.Ne => !x.SameAs(y), Op.Le => x.SubsetOf(y), _ => y.SubsetOf(x) };
        return r ? 1 : 0;
    }
}

public sealed class PtrCmp : Expr
{
    readonly Expr a, b; readonly bool eq;
    public PtrCmp(bool eq, Expr a, Expr b) { this.eq = eq; this.a = a; this.b = b; Type = EnumType.Boolean; }
    public override long Ord(Frame f) => ReferenceEquals(a.Obj(f), b.Obj(f)) == eq ? 1 : 0;
}

public sealed class InNode : Expr
{
    readonly Expr e, s;
    public InNode(Expr e, Expr s) { this.e = e; this.s = s; Type = EnumType.Boolean; }
    public override long Ord(Frame f)
    {
        long v = e.Ord(f);
        return ((PSet)s.Obj(f)!).Contains(v) ? 1 : 0;
    }
}

// ---------------------------------------------------------------- sets

public sealed class SetBin : Expr
{
    readonly Expr a, b; readonly Op op;
    public SetBin(Op op, Expr a, Expr b, PType t) { this.op = op; this.a = a; this.b = b; Type = t; }
    public override object? Obj(Frame f)
    {
        var x = (PSet)a.Obj(f)!; var y = (PSet)b.Obj(f)!;
        return op switch { Op.Add => x.Union(y), Op.Sub => x.Minus(y), _ => x.Intersect(y) };
    }
}

/// <summary>Set constructor [a, b..c].</summary>
public sealed class SetCons : Expr
{
    readonly (Expr lo, Expr? hi)[] items;
    readonly long max;
    readonly PType elemType;
    public SetCons((Expr, Expr?)[] items, PType t, PType elemType)
    {
        this.items = items; Type = t; this.elemType = elemType;
        max = elemType.IsChar ? 255 : TypeRules.MaxSetElement;
    }
    public override object? Obj(Frame f)
    {
        var s = PSet.Empty;
        foreach (var (lo, hi) in items)
        {
            long a = lo.Ord(f);
            long b = hi == null ? a : hi.Ord(f);
            if (a > b) continue;
            if (a < 0 || b > max)
                throw RT.Err($"set element {Format.OrdText(elemType, a < 0 ? a : b)} out of range 0..{max} (Pascal 6000 sets hold ordinal values 0..58; set of char 0..255)", this);
            s = s.With(a, b);
        }
        return s;
    }
}
