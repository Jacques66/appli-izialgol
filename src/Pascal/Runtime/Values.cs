using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Studio.Pascal.Exec;
using Studio.Pascal.Semantics;

namespace Studio.Pascal.Runtime;

/// <summary>
/// One storage cell. Every scalar (integer, real, boolean, char, enumeration, subrange, pointer, set)
/// occupies one cell; arrays and records occupy consecutive cells.
/// Ordinal values are kept in N, reals in N as IEEE bits; O holds sets (PSet), pointers (HeapObj or
/// null for nil), files (PFile), routine values (Closure) and references of var parameters
/// (O = the Cell[] store, N = the index). O = Undef.V marks an undefined value.
/// </summary>
public struct Cell
{
    public long N;
    public object? O;

    public static readonly Cell Undefined = new() { O = Runtime.Undef.V };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cell Ord(long n) => new() { N = n };
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cell Real(double d) => new() { N = BitConverter.DoubleToInt64Bits(d) };
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cell Obj(object? o) => new() { O = o };

    public readonly double R => BitConverter.Int64BitsToDouble(N);
    public readonly bool IsUndef => ReferenceEquals(O, Runtime.Undef.V);
}

/// <summary>Marker of an undefined value.</summary>
public sealed class Undef
{
    public static readonly Undef V = new();
    Undef() { }
    public override string ToString() => "undefined";
}

/// <summary>Address of a variable: a store (frame, heap variable, file buffer, constant) and an index.</summary>
public readonly struct Ref
{
    public readonly Cell[] S;
    public readonly int I;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Ref(Cell[] s, int i) { S = s; I = i; }
}

/// <summary>Immutable set of ordinal values 0..255 (integer sets are limited to 0..58 by the compiler).</summary>
public sealed class PSet
{
    public readonly ulong W0, W1, W2, W3;
    public static readonly PSet Empty = new(0, 0, 0, 0);

    public PSet(ulong w0, ulong w1, ulong w2, ulong w3) { W0 = w0; W1 = w1; W2 = w2; W3 = w3; }

    public bool Contains(long v)
    {
        if (v < 0 || v > 255) return false;
        ulong w = (v >> 6) switch { 0 => W0, 1 => W1, 2 => W2, _ => W3 };
        return (w & (1UL << (int)(v & 63))) != 0;
    }

    public static PSet FromRange(long lo, long hi)
    {
        if (lo > hi) return Empty;
        ulong[] w = new ulong[4];
        for (long v = lo; v <= hi; v++) w[v >> 6] |= 1UL << (int)(v & 63);
        return new PSet(w[0], w[1], w[2], w[3]);
    }

    public PSet With(long lo, long hi)
    {
        if (lo > hi) return this;
        ulong[] w = { W0, W1, W2, W3 };
        for (long v = lo; v <= hi; v++) w[v >> 6] |= 1UL << (int)(v & 63);
        return new PSet(w[0], w[1], w[2], w[3]);
    }

    public PSet Union(PSet b) => new(W0 | b.W0, W1 | b.W1, W2 | b.W2, W3 | b.W3);
    public PSet Intersect(PSet b) => new(W0 & b.W0, W1 & b.W1, W2 & b.W2, W3 & b.W3);
    public PSet Minus(PSet b) => new(W0 & ~b.W0, W1 & ~b.W1, W2 & ~b.W2, W3 & ~b.W3);
    public bool SameAs(PSet b) => W0 == b.W0 && W1 == b.W1 && W2 == b.W2 && W3 == b.W3;
    public bool SubsetOf(PSet b) => (W0 & ~b.W0) == 0 && (W1 & ~b.W1) == 0 && (W2 & ~b.W2) == 0 && (W3 & ~b.W3) == 0;
    public int Count => BitOperations.PopCount(W0) + BitOperations.PopCount(W1) + BitOperations.PopCount(W2) + BitOperations.PopCount(W3);

    public IEnumerable<long> Elements()
    {
        for (long v = 0; v < 256; v++) if (Contains(v)) yield return v;
    }

    /// <summary>Smallest and largest element (-1 when empty).</summary>
    public (long min, long max) Bounds()
    {
        long min = -1, max = -1;
        for (long v = 0; v < 256; v++) if (Contains(v)) { if (min < 0) min = v; max = v; }
        return (min, max);
    }
}

/// <summary>Dynamic variable created by new.</summary>
public sealed class HeapObj
{
    public readonly Cell[] C;
    public readonly PType Type;
    public bool Disposed;
    public readonly int Id;
    /// <summary>Allocated with explicit variant tag values (new(p, t1, ...)).</summary>
    public bool FixedVariant;
    /// <summary>Tag values of new(p, t1, ..., tn), null for new(p).</summary>
    public long[]? TagValues;
    /// <summary>Offsets of the tag fields set by new(p, t1, ..., tn): their values must remain constant.</summary>
    public int[] FixedTagOffsets = Array.Empty<int>();
    public HeapObj(PType type, Cell[] cells, int id) { Type = type; C = cells; Id = id; }
}

/// <summary>Value of a procedural parameter: a routine and its static environment.</summary>
public sealed class Closure
{
    public readonly RoutineInfo R;
    public readonly Frame? Static;
    public Closure(RoutineInfo r, Frame? s) { R = r; Static = s; }
    public override string ToString() => (R.IsFunction ? "function " : "procedure ") + R.Name;
}

/// <summary>Activation record of a routine (or of the main program).</summary>
public sealed class Frame
{
    public readonly Cell[] C;
    public readonly Frame? Static;
    public Frame? Caller;
    public readonly RoutineInfo R;
    public readonly ExecContext Ctx;
    public int Line, Col;
    public readonly int Depth;
    /// <summary>Pending local goto (label being searched by the enclosing statement sequences).</summary>
    public LabelInfo? PendingGoto;

    public Frame(RoutineInfo r, Frame? stat, ExecContext ctx, int depth)
    {
        R = r; Static = stat; Ctx = ctx; Depth = depth;
        C = r.NewCells(ctx);
    }
}

/// <summary>Runtime error with source position.</summary>
public sealed class PasError : Exception
{
    public PasError(string message, Node? at) : base(message)
    {
        if (at != null) { File = at.File; Line = at.Line; Col = at.Col; }
    }
    public string File = "";
    public int Line, Col;
    public bool Positioned => Line > 0;
    public IReadOnlyList<Studio.Common.StackFrameInfo>? Stack;
}

/// <summary>Non-local goto (leaves one or more routine activations).</summary>
public sealed class GotoException : Exception
{
    public readonly LabelInfo Label;
    public readonly Frame Target;
    public readonly Node? Site;
    public GotoException(LabelInfo l, Frame target, Node? site) : base("goto " + l.Value) { Label = l; Target = target; Site = site; }
}

public sealed class CancelledException : Exception { }

public static class RT
{
    public const long MaxInt = 281474976710655L;   // 2^48 - 1 (Pascal 6000)

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static PasError Err(string msg, Node? at) => new(msg, at);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long CheckInt(long v, Node at)
    {
        if (v > MaxInt || v < -MaxInt) throw Overflow(at);
        return v;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static PasError Overflow(Node at) => Err("integer overflow: the result exceeds maxint (281474976710655)", at);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Mul(long a, long b, Node at)
    {
        if ((ulong)(a + 0x7FFFFFFF) <= 0xFFFFFFFEUL && (ulong)(b + 0x7FFFFFFF) <= 0xFFFFFFFEUL) return CheckInt(a * b, at);
        Int128 p = (Int128)a * b;
        if (p > MaxInt || p < -MaxInt) throw Overflow(at);
        return (long)p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CheckReal(double d, Node at)
    {
        if (!double.IsFinite(d)) throw RealError(d, at);
        return d;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static PasError RealError(double d, Node at) =>
        Err(double.IsNaN(d) ? "real arithmetic: undefined result" : "real overflow", at);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static PasError UndefErr(Node at, string? what = null) =>
        Err(what == null ? "undefined value used" : $"undefined value used: {what}", at);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static PasError RangeErr(long v, PType t, Node at) =>
        Err($"value {Format.OrdText(t, v)} out of range {Format.OrdText(t, t.Lo)}..{Format.OrdText(t, t.Hi)}", at);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long CheckRange(long v, long lo, long hi, PType t, Node at)
    {
        if (v < lo || v > hi) throw RangeErr(v, t, at);
        return v;
    }

    /// <summary>Copies a structured value (n cells).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Copy(Ref src, Ref dst, int n)
    {
        if (n == 1) dst.S[dst.I] = src.S[src.I];
        else Array.Copy(src.S, src.I, dst.S, dst.I, n);
    }

    public static string Chars(Ref r, int n)
    {
        var sb = new StringBuilder(n);
        for (int i = 0; i < n; i++)
        {
            var c = r.S[r.I + i];
            sb.Append(c.IsUndef ? ' ' : (char)c.N);
        }
        return sb.ToString();
    }
}
