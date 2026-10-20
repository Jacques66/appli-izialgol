using System.Numerics;
using System.Runtime.CompilerServices;
using Studio.Algol68.Exec;
using Studio.Algol68.Semantics;

namespace Studio.Algol68.Runtime;

/// <summary>Common runtime services (copies, assignments, scopes, checks).</summary>
public static class RT
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static A68Error Err(string msg, Node? at) => new(msg, at);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Value CheckInit(Value v, Node at)
    {
        if (ReferenceEquals(v.O, Value.Uninit)) throw Err("uninitialised value", at);
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RefV CheckNil(RefV? r, Node at)
    {
        if (r == null || r.A == null) throw Err("dereferencing NIL", at);
        return r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long CheckInt(long v, Node at)
    {
        if (v > int.MaxValue || v < int.MinValue) throw Err("integer overflow (INT)", at);
        return v;
    }

    public static double CheckReal(double d, Node at)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) throw Err("overflow or undefined result (REAL)", at);
        return d;
    }

    /// <summary>Deep copy of a composite value (rows, structures, unions).</summary>
    public static Value DeepCopy(Value v, Mode m)
    {
        if (v.O == null || ReferenceEquals(v.O, Value.Uninit)) return v;
        switch (m)
        {
            case StructMode sm:
                {
                    var src = (Value[])v.O;
                    var dst = new Value[src.Length];
                    for (int i = 0; i < src.Length; i++) dst[i] = sm.Fields[i].Mode.NeedsCopy ? DeepCopy(src[i], sm.Fields[i].Mode) : src[i];
                    return Value.Obj(dst);
                }
            case RowMode or FlexMode:
                {
                    var rm = m.AsRow!;
                    var row = (RowV)v.O;
                    return Value.Obj(CopyRow(row, rm.Elem));
                }
            case UnionMode:
                {
                    var u = (UnionV)v.O;
                    if (!u.M.NeedsCopy) return v;
                    return Value.Obj(new UnionV(u.M, DeepCopy(u.V, u.M)));
                }
            case SpecialMode:
                {
                    if (v.O is UnionV u2 && u2.M.NeedsCopy) return Value.Obj(new UnionV(u2.M, DeepCopy(u2.V, u2.M)));
                    return v;
                }
        }
        return v;
    }

    public static RowV CopyRow(RowV row, Mode elem)
    {
        var elems = row.Elements();
        if (elem.NeedsCopy) for (int i = 0; i < elems.Length; i++) elems[i] = DeepCopy(elems[i], elem);
        int dims = row.Dims;
        var lwb = (int[])row.Lwb.Clone();
        var upb = (int[])row.Upb.Clone();
        var stride = new int[dims];
        int s = 1;
        for (int d = dims - 1; d >= 0; d--) { stride[d] = s; s *= Math.Max(0, upb[d] - lwb[d] + 1); }
        return new RowV(elems, 0, lwb, upb, stride);
    }

    /// <summary>Assignment into a cell: in place for structures and non-flexible rows.</summary>
    public static void AssignInto(Value[] a, int i, Value v, Mode m, Node at)
    {
        switch (m)
        {
            case StructMode sm:
                {
                    var cur = a[i].O as Value[];
                    var src = (Value[])CheckInit(v, at).O!;
                    if (cur == null) { a[i] = DeepCopy(v, m); return; }
                    for (int k = 0; k < src.Length; k++)
                    {
                        var fm = sm.Fields[k].Mode;
                        if (fm.NeedsCopy) AssignInto(cur, k, src[k], fm, at);
                        else cur[k] = src[k];
                    }
                    return;
                }
            case FlexMode fm2:
                a[i] = Value.Obj(CopyRow((RowV)CheckInit(v, at).O!, fm2.Row.Elem));
                return;
            case RowMode rm:
                {
                    var src = (RowV)CheckInit(v, at).O!;
                    if (a[i].O is not RowV cur) { a[i] = Value.Obj(CopyRow(src, rm.Elem)); return; }
                    if (!cur.SameBounds(src))
                        throw Err($"assignment of rows with different bounds ({BoundsText(src)} into {BoundsText(cur)})", at);
                    AssignRowElements(cur, src, rm.Elem, at);
                    return;
                }
            case UnionMode:
            case SpecialMode:
                a[i] = DeepCopy(v, m);
                return;
        }
        a[i] = v;
    }

    public static void AssignRowElements(RowV dst, RowV src, Mode elem, Node at)
    {
        var vals = src.Elements(); // copy first: handles overlap (a[2:5] := a[1:4])
        int k = 0;
        bool composite = elem.NeedsCopy;
        foreach (int idx in dst.Indices())
        {
            var (arr, j) = dst.SlotAt(idx);
            if (composite) AssignInto(arr, j, vals[k], elem, at);
            else arr[j] = vals[k];
            k++;
        }
    }

    public static string BoundsText(RowV r)
    {
        var parts = new List<string>();
        for (int d = 0; d < r.Dims; d++) parts.Add($"{r.Lwb[d]}:{r.Upb[d]}");
        return "[" + string.Join(", ", parts) + "]";
    }

    /// <summary>Scope (dynamic depth, larger = younger) of a value.</summary>
    public static int ScopeOf(Value v, Mode m)
    {
        if (v.O == null || ReferenceEquals(v.O, Value.Uninit)) return 0;
        switch (v.O)
        {
            case RefV r: return r.Scope;
            case ProcV p: return p.Scope;
            case FormatV fv: return fv.Scope;
            case UnionV u: return u.M.HasNames ? ScopeOf(u.V, u.M) : 0;
        }
        switch (m)
        {
            case StructMode sm:
                {
                    var f = (Value[])v.O;
                    int s = 0;
                    for (int i = 0; i < f.Length; i++) if (sm.Fields[i].Mode.HasNames) s = Math.Max(s, ScopeOf(f[i], sm.Fields[i].Mode));
                    return s;
                }
            case RowMode or FlexMode:
                {
                    var rm = m.AsRow!;
                    if (!rm.Elem.HasNames) return 0;
                    var row = (RowV)v.O;
                    int s = 0;
                    foreach (var e in row.Elements()) s = Math.Max(s, ScopeOf(e, rm.Elem));
                    return s;
                }
        }
        return 0;
    }

    public static void CheckScope(Value v, Mode m, int limit, Node at)
    {
        int s = ScopeOf(v, m);
        if (s > limit) throw Err("scope violation: the value does not live long enough for this assignment or yield", at);
    }

    /// <summary>Value yielded by SKIP for a mode.</summary>
    public static Value SkipValue(Mode m, ModeTable mt)
    {
        switch (m)
        {
            case PrimMode p:
                switch (p.Kind)
                {
                    case Prim.Int: return p.Size == 0 ? Value.I(0) : Value.Obj(BigInteger.Zero);
                    case Prim.Real: return p.Size == 0 ? Value.R(0) : Value.Obj(MpReal.Zero(Prelude.RealDigits(p.Size)));
                    case Prim.Bits: return p.Size == 0 ? Value.I(0) : Value.Obj(BigInteger.Zero);
                    case Prim.Bytes: return Value.Obj(new string('\0', p.Size == 0 ? Prelude.BytesWidth : Prelude.LongBytesWidth));
                    case Prim.Bool: return Value.B(false);
                    case Prim.Char: return Value.I(' ');
                    case Prim.Void: return Value.Empty;
                    case Prim.Sema: return Value.Obj(new SemaV(0));
                    default: return Value.UninitValue;
                }
            case RefMode: return Value.Obj(RefV.Nil);
            case StructMode sm:
                {
                    var f = new Value[sm.Fields.Length];
                    for (int i = 0; i < f.Length; i++) f[i] = SkipValue(sm.Fields[i].Mode, mt);
                    return Value.Obj(f);
                }
            case RowMode or FlexMode:
                {
                    var rm = m.AsRow!;
                    var lwb = new int[rm.Dims]; var upb = new int[rm.Dims];
                    for (int d = 0; d < rm.Dims; d++) { lwb[d] = 1; upb[d] = 0; }
                    return Value.Obj(RowV.Make(lwb, upb, default));
                }
        }
        return Value.UninitValue;
    }

    // ------------------------------------------------------------- strings

    public static string Str(Value v, Node? at = null)
    {
        if (v.O is RowV r) return r.AsString();
        if (v.O == null) return ((char)v.N).ToString();
        if (ReferenceEquals(v.O, Value.Uninit)) throw Err("uninitialised string", at);
        return v.O.ToString() ?? "";
    }

    public static Value StrValue(string s) => Value.Obj(RowV.FromString(s));
}
