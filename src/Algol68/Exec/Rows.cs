using System.Runtime.CompilerServices;
using Studio.Algol68.Runtime;
using Studio.Algol68.Semantics;

namespace Studio.Algol68.Exec;

/// <summary>Indice d'une tranche : indice simple ou trimmer.</summary>
public sealed class SliceIndexer
{
    public Node? Index;
    public bool Trimmer;
    public bool HasColon;
    public Node? Lwb, Upb, At;
}

public static class RowOps
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static A68Error OutOfBounds(long i, int lwb, int upb, int dim, Node at) =>
        RT.Err($"indice {i} hors des bornes [{lwb}:{upb}]" + (dim > 0 ? $" (dimension {dim + 1})" : ""), at);

    public static RowV GetRow(Value v, Node at)
    {
        if (v.O is RowV r) return r;
        if (ReferenceEquals(v.O, Value.Uninit) || v.O == null) throw RT.Err("rangee non initialisee", at);
        throw RT.Err("rangee attendue", at);
    }

    /// <summary>Applique indices et trimmers a un descripteur ; rend (rangee resultante, index d'element si tous indices).</summary>
    public static (RowV? row, int idx) Slice(RowV r, SliceIndexer[] ix, Frame f, Node at)
    {
        if (ix.Length != r.Dims) throw RT.Err($"nombre d'indices ({ix.Length}) different du nombre de dimensions ({r.Dims})", at);
        int offset = r.Offset;
        var lw = new List<int>(); var up = new List<int>(); var st = new List<int>();
        for (int d = 0; d < ix.Length; d++)
        {
            var x = ix[d];
            if (!x.Trimmer)
            {
                long i = x.Index!.EvalInt(f);
                if (i < r.Lwb[d] || i > r.Upb[d]) throw OutOfBounds(i, r.Lwb[d], r.Upb[d], ix.Length > 1 ? d : 0, at);
                offset += (int)(i - r.Lwb[d]) * r.Stride[d];
                continue;
            }
            long l = x.Lwb != null ? x.Lwb.EvalInt(f) : r.Lwb[d];
            long u = x.Upb != null ? x.Upb.EvalInt(f) : r.Upb[d];
            long newL;
            if (x.HasColon) newL = x.At != null ? x.At.EvalInt(f) : 1;
            else newL = x.At != null ? x.At.EvalInt(f) : l;
            if (u >= l && (l < r.Lwb[d] || u > r.Upb[d]))
                throw RT.Err($"trimmer [{l}:{u}] hors des bornes [{r.Lwb[d]}:{r.Upb[d]}]", at);
            if (u >= l) offset += (int)(l - r.Lwb[d]) * r.Stride[d];
            lw.Add((int)newL); up.Add((int)(newL + (u - l))); st.Add(r.Stride[d]);
        }
        if (lw.Count == 0) return (null, offset);
        return (new RowV(r.Data, offset, lw.ToArray(), up.ToArray(), st.ToArray(), r.Path), offset);
    }
}

/// <summary>a[i] sur un nom de rangee a une dimension : rend un nom d'element.</summary>
public sealed class Subscript1NameNode : Node
{
    readonly Node prim, index;
    public Subscript1NameNode(Node p, Node i) { prim = p; index = i; }
    public Node MakeDeref(Mode valMode) { var n = new Subscript1DerefNode(prim, index, valMode, false); n.SetPos(this); return n; }
    public override Value Eval(Frame f) => Value.Obj(Get(f));
    public override object? EvalObj(Frame f) => Get(f);
    RefV Get(Frame f)
    {
        var rr = Unsafe.As<RefV>(prim.EvalObj(f));
        if (rr == null || rr.A == null) throw RT.Err("indexation d'un nom NIL", this);
        var row = rr.A[rr.I].O as RowV ?? throw RT.Err("rangee non initialisee", this);
        long i = index.EvalInt(f);
        int idx = row.Index1(i);
        if (idx < 0) throw RowOps.OutOfBounds(i, row.Lwb[0], row.Upb[0], 0, this);
        if (row.Path == null) return new RefV(row.Data, idx, rr.Scope);
        var (a, j) = row.SlotAt(idx);
        return new RefV(a, j, rr.Scope);
    }
}

/// <summary>a[i] (rangee a une dimension) dereference immediatement : lecture rapide.</summary>
public sealed class Subscript1DerefNode : Node
{
    readonly Node prim, index; readonly bool copy; readonly Mode elemMode;
    public Subscript1DerefNode(Node p, Node i, Mode elemMode, bool copy) { prim = p; index = i; this.elemMode = elemMode; this.copy = copy; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Value Get(Frame f)
    {
        var rr = Unsafe.As<RefV>(prim.EvalObj(f));
        if (rr == null || rr.A == null) throw RT.Err("indexation d'un nom NIL", this);
        var row = rr.A[rr.I].O as RowV ?? throw RT.Err("rangee non initialisee", this);
        long i = index.EvalInt(f);
        int idx = row.Index1(i);
        if (idx < 0) throw RowOps.OutOfBounds(i, row.Lwb[0], row.Upb[0], 0, this);
        var v = row.GetAt(idx);
        if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err($"element [{i}] non initialise", this);
        return v;
    }
    public override Value Eval(Frame f) { var v = Get(f); return copy ? RT.DeepCopy(v, elemMode) : v; }
    public override long EvalInt(Frame f) => Get(f).N;
    public override double EvalReal(Frame f) => Get(f).Real;
    public override bool EvalBool(Frame f) => Get(f).N != 0;
    public override object? EvalObj(Frame f) => Eval(f).O;
}

/// <summary>a[i] sur une valeur rangee a une dimension.</summary>
public sealed class Subscript1ValueNode : Node
{
    readonly Node prim, index;
    public Subscript1ValueNode(Node p, Node i) { prim = p; index = i; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Value Get(Frame f)
    {
        var row = RowOps.GetRow(prim.Eval(f), this);
        long i = index.EvalInt(f);
        int idx = row.Index1(i);
        if (idx < 0) throw RowOps.OutOfBounds(i, row.Lwb[0], row.Upb[0], 0, this);
        var v = row.GetAt(idx);
        if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err($"element [{i}] non initialise", this);
        return v;
    }
    public override Value Eval(Frame f) => Get(f);
    public override long EvalInt(Frame f) => Get(f).N;
    public override double EvalReal(Frame f) => Get(f).Real;
    public override bool EvalBool(Frame f) => Get(f).N != 0;
}

/// <summary>Tranche generale (plusieurs dimensions, trimmers), sur nom ou valeur.</summary>
public sealed class SliceNode : Node
{
    readonly Node prim; readonly SliceIndexer[] ix; readonly bool onName;
    public SliceNode(Node p, SliceIndexer[] ix, bool onName) { prim = p; this.ix = ix; this.onName = onName; }
    public override Value Eval(Frame f)
    {
        RowV row; int scope = 0;
        if (onName)
        {
            var rr = RT.CheckNil(prim.EvalObj(f) as RefV, this);
            row = rr.A![rr.I].O as RowV ?? throw RT.Err("rangee non initialisee", this);
            scope = rr.Scope;
        }
        else row = RowOps.GetRow(prim.Eval(f), this);
        var (res, idx) = RowOps.Slice(row, ix, f, this);
        if (res == null)
        {
            if (onName)
            {
                var (a, j) = row.SlotAt(idx);
                return Value.Obj(new RefV(a, j, scope));
            }
            var v = row.GetAt(idx);
            if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err("element non initialise", this);
            return v;
        }
        return onName ? Value.Obj(RefV.NewCell(Value.Obj(res), scope)) : Value.Obj(res);
    }
}

/// <summary>Selection d'un champ : sur un nom de structure (rend un nom) ou une valeur.</summary>
public sealed class SelectNameNode : Node
{
    readonly Node sec; readonly int field;
    public SelectNameNode(Node s, int field) { sec = s; this.field = field; }
    public Node MakeDeref(Mode valMode) { var n = new SelectDerefNode(sec, field, valMode, false); n.SetPos(this); return n; }
    public override Value Eval(Frame f) => Value.Obj(Get(f));
    public override object? EvalObj(Frame f) => Get(f);
    RefV Get(Frame f)
    {
        var r = Unsafe.As<RefV>(sec.EvalObj(f));
        if (r == null || r.A == null) throw RT.Err("selection dans un nom NIL", this);
        var s = r.A[r.I].O as Value[] ?? throw RT.Err("structure non initialisee", this);
        return new RefV(s, field, r.Scope);
    }
}

public sealed class SelectDerefNode : Node
{
    readonly Node sec; readonly int field; readonly bool copy; readonly Mode fmode;
    public SelectDerefNode(Node s, int field, Mode fmode, bool copy) { sec = s; this.field = field; this.fmode = fmode; this.copy = copy; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Value Get(Frame f)
    {
        var r = Unsafe.As<RefV>(sec.EvalObj(f));
        if (r == null || r.A == null) throw RT.Err("selection dans un nom NIL", this);
        var s = r.A[r.I].O as Value[] ?? throw RT.Err("structure non initialisee", this);
        var v = s[field];
        if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err("champ non initialise", this);
        return v;
    }
    public override Value Eval(Frame f) { var v = Get(f); return copy ? RT.DeepCopy(v, fmode) : v; }
    public override long EvalInt(Frame f) => Get(f).N;
    public override double EvalReal(Frame f) => Get(f).Real;
    public override bool EvalBool(Frame f) => Get(f).N != 0;
    public override object? EvalObj(Frame f) => Eval(f).O;
}

public sealed class SelectValueNode : Node
{
    readonly Node sec; readonly int field;
    public SelectValueNode(Node s, int field) { sec = s; this.field = field; }
    Value Get(Frame f)
    {
        var sv = sec.Eval(f);
        var s = sv.O as Value[] ?? throw RT.Err("structure non initialisee", this);
        var v = s[field];
        if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err("champ non initialise", this);
        return v;
    }
    public override Value Eval(Frame f) => Get(f);
    public override long EvalInt(Frame f) => Get(f).N;
    public override double EvalReal(Frame f) => Get(f).Real;
    public override bool EvalBool(Frame f) => Get(f).N != 0;
}

/// <summary>Selection multiple : champ d'une rangee de structures (nom ou valeur).</summary>
public sealed class SelectRowNode : Node
{
    readonly Node sec; readonly int field; readonly bool onName;
    public SelectRowNode(Node s, int field, bool onName) { sec = s; this.field = field; this.onName = onName; }
    public override Value Eval(Frame f)
    {
        RowV row; int scope = 0;
        if (onName)
        {
            var r = RT.CheckNil(sec.EvalObj(f) as RefV, this);
            row = r.A![r.I].O as RowV ?? throw RT.Err("rangee non initialisee", this);
            scope = r.Scope;
        }
        else row = RowOps.GetRow(sec.Eval(f), this);
        var path = row.Path == null ? new[] { field } : row.Path.Append(field).ToArray();
        var res = new RowV(row.Data, row.Offset, row.Lwb, row.Upb, row.Stride, path);
        return onName ? Value.Obj(RefV.NewCell(Value.Obj(res), scope)) : Value.Obj(res);
    }
}
