using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Studio.Algol68.Semantics;

namespace Studio.Algol68.Runtime;

/// <summary>
/// Valeur Algol 68 : N porte INT, BOOL, CHAR, BITS et REAL (bits IEEE) ; O porte les autres valeurs
/// (RefV, RowV, Value[] pour les structures, UnionV, ProcV, BigInteger, MpReal, FileV...).
/// </summary>
public struct Value
{
    public long N;
    public object? O;

    public static readonly object Uninit = new UninitMarker();
    sealed class UninitMarker { public override string ToString() => "<non initialisé>"; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Value I(long n) => new() { N = n };
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Value R(double d) => new() { N = BitConverter.DoubleToInt64Bits(d) };
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Value B(bool b) => new() { N = b ? 1 : 0 };
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Value Obj(object? o) => new() { O = o };
    public static readonly Value Empty = default;
    public static Value UninitValue => new() { O = Uninit };

    public readonly double Real { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => BitConverter.Int64BitsToDouble(N); }
    public readonly bool Bool => N != 0;
    public readonly bool IsUninit => ReferenceEquals(O, Uninit);
    public override readonly string ToString() => O != null ? O.ToString() ?? "" : N.ToString();
}

/// <summary>Nom (reference) : designe la case A[I]. A == null : NIL.</summary>
public sealed class RefV
{
    public Value[]? A;
    public int I;
    public int Scope;
    public RefV(Value[]? a, int i, int scope) { A = a; I = i; Scope = scope; }
    public static readonly RefV Nil = new(null, 0, 0);
    public bool IsNil => A == null;
    public static RefV NewCell(Value init, int scope) => new(new[] { init }, 0, scope);
    public bool SameAs(RefV o) => ReferenceEquals(A, o.A) && I == o.I;
}

/// <summary>Valeur multiple (rangee) : descripteur sur un stockage partage.</summary>
public sealed class RowV
{
    public Value[] Data;
    public int Offset;
    public int[] Lwb, Upb, Stride;
    public int[]? Path;   // selection de champ appliquee a chaque element (selection multiple)
    public int Scope;

    public RowV(Value[] data, int offset, int[] lwb, int[] upb, int[] stride, int[]? path = null)
    { Data = data; Offset = offset; Lwb = lwb; Upb = upb; Stride = stride; Path = path; }

    public int Dims => Lwb.Length;

    public static RowV Make1(Value[] data, int lwb = 1) => new(data, 0, new[] { lwb }, new[] { lwb + data.Length - 1 }, new[] { 1 });

    public static RowV Make(int[] lwb, int[] upb, Value fill)
    {
        int n = Dims_Count(lwb, upb);
        var data = new Value[n];
        if (fill.O != null || fill.N != 0) Array.Fill(data, fill);
        var stride = new int[lwb.Length];
        int s = 1;
        for (int d = lwb.Length - 1; d >= 0; d--) { stride[d] = s; s *= Math.Max(0, upb[d] - lwb[d] + 1); }
        return new RowV(data, 0, (int[])lwb.Clone(), (int[])upb.Clone(), stride);
    }

    public static int Dims_Count(int[] lwb, int[] upb)
    {
        long n = 1;
        for (int d = 0; d < lwb.Length; d++) n *= Math.Max(0, (long)upb[d] - lwb[d] + 1);
        if (n > int.MaxValue / 2) throw new A68Error("rangée trop grande");
        return (int)n;
    }

    public int Count
    {
        get
        {
            long n = 1;
            for (int d = 0; d < Lwb.Length; d++) n *= Math.Max(0, (long)Upb[d] - Lwb[d] + 1);
            return (int)n;
        }
    }

    public int Length1 => Math.Max(0, Upb[0] - Lwb[0] + 1);

    /// <summary>Index dans Data (avant application du chemin de champs) ; -1 si hors bornes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Index1(long i)
    {
        if (i < Lwb[0] || i > Upb[0]) return -1;
        return Offset + (int)(i - Lwb[0]) * Stride[0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Value GetAt(int idx)
    {
        var v = Data[idx];
        if (Path != null) v = Walk(v);
        return v;
    }

    Value Walk(Value v)
    {
        foreach (int f in Path!)
        {
            if (v.O is not Value[] s) return Value.UninitValue;
            v = s[f];
        }
        return v;
    }

    /// <summary>Case de stockage designee par un element (pour un nom d'element).</summary>
    public (Value[] arr, int idx) SlotAt(int idx)
    {
        if (Path == null) return (Data, idx);
        Value[] arr = Data; int i = idx;
        foreach (int f in Path)
        {
            if (arr[i].O is not Value[] s) throw new A68Error("sélection dans une structure non initialisée");
            arr = s; i = f;
        }
        return (arr, i);
    }

    public void SetAt(int idx, Value v)
    {
        var (a, i) = SlotAt(idx);
        a[i] = v;
    }

    /// <summary>Enumere les index Data des elements en ordre lexicographique.</summary>
    public IEnumerable<int> Indices()
    {
        int dims = Dims;
        for (int d = 0; d < dims; d++) if (Upb[d] < Lwb[d]) yield break;
        var cur = (int[])Lwb.Clone();
        while (true)
        {
            int idx = Offset;
            for (int d = 0; d < dims; d++) idx += (cur[d] - Lwb[d]) * Stride[d];
            yield return idx;
            int k = dims - 1;
            while (k >= 0)
            {
                if (cur[k] < Upb[k]) { cur[k]++; break; }
                cur[k] = Lwb[k]; k--;
            }
            if (k < 0) yield break;
        }
    }

    public Value[] Elements()
    {
        var res = new Value[Count];
        int k = 0;
        if (Dims == 1 && Path == null)
        {
            int n = Length1, s = Stride[0], o = Offset;
            if (s == 1) { Array.Copy(Data, o, res, 0, n); return res; }
            for (int i = 0; i < n; i++) res[i] = Data[o + i * s];
            return res;
        }
        foreach (int idx in Indices()) res[k++] = GetAt(idx);
        return res;
    }

    public bool SameBounds(RowV o)
    {
        if (o.Dims != Dims) return false;
        for (int d = 0; d < Dims; d++)
        {
            int n1 = Math.Max(0, Upb[d] - Lwb[d] + 1), n2 = Math.Max(0, o.Upb[d] - o.Lwb[d] + 1);
            if (n1 != n2) return false;
            if (n1 > 0 && Lwb[d] != o.Lwb[d]) return false;
        }
        return true;
    }

    // ---- chaines

    public static RowV FromString(string s)
    {
        var data = new Value[s.Length];
        for (int i = 0; i < s.Length; i++) data[i].N = s[i];
        return Make1(data);
    }

    public string AsString()
    {
        int n = Count;
        if (n == 0) return "";
        var sb = new StringBuilder(n);
        if (Dims == 1 && Path == null)
        {
            int s = Stride[0], o = Offset;
            for (int i = 0; i < n; i++)
            {
                var v = Data[o + i * s];
                if (v.IsUninit) throw new A68Error("caractère non initialisé dans une chaîne");
                sb.Append((char)v.N);
            }
            return sb.ToString();
        }
        foreach (var v in Elements())
        {
            if (v.IsUninit) throw new A68Error("caractère non initialisé dans une chaîne");
            sb.Append((char)v.N);
        }
        return sb.ToString();
    }

    public static readonly RowV EmptyString = Make1(Array.Empty<Value>());
}

/// <summary>Valeur d'un mode union : mode effectif (non union) et valeur.</summary>
public sealed class UnionV
{
    public readonly Mode M;
    public readonly Value V;
    public UnionV(Mode m, Value v) { M = m; V = v; }
}

public delegate Value BuiltinFn(Frame caller, Value[] args, Exec.Node site);

/// <summary>Valeur procedure : routine + environnement, ou primitive.</summary>
public sealed class ProcV
{
    public Exec.RoutineInfo? Routine;
    public Frame? Env;
    public int Scope;
    public BuiltinFn? Builtin;
    public string Name = "";
    // parametrage partiel (extension a68g)
    public Value[]? Bound;
    public bool[]? BoundMask;
    public ProcMode? FullMode;
    // saut (etiquette utilisee comme PROC VOID)
    public Exec.JumpTarget? Jump;
    public override string ToString() => Routine != null ? $"routine {Routine.Name}" : Builtin != null ? $"primitive {Name}" : "procédure";
}

/// <summary>Cadre d'activation (une routine ou le programme principal).</summary>
public sealed class Frame
{
    public readonly Value[] S;
    public readonly Frame? Static;
    public readonly int Depth;           // profondeur dynamique de la portee principale de la routine
    public readonly Exec.RoutineInfo Info;
    public readonly ExecContext Ctx;
    public int Line, Col;
    public string File = "";
    public Frame? Caller;

    public Frame(Exec.RoutineInfo info, Frame? stat, int depth, ExecContext ctx)
    {
        Info = info; Static = stat; Depth = depth; Ctx = ctx;
        S = info.FrameSize == 0 ? Array.Empty<Value>() : new Value[info.FrameSize];
    }
}

/// <summary>Semaphore (SEMA).</summary>
public sealed class SemaV
{
    public int Level;
    public readonly object Lock = new();
    public SemaV(int l) { Level = l; }
}

/// <summary>Erreur d'execution Algol 68.</summary>
public sealed class A68Error : Exception
{
    public string File = ""; public int Line, Col;
    public bool Positioned;
    public IReadOnlyList<Studio.Common.StackFrameInfo>? Stack;
    public A68Error(string msg) : base(msg) { }
    public A68Error(string msg, Exec.Node? at) : base(msg)
    {
        if (at != null) { File = at.File; Line = at.Line; Col = at.Col; Positioned = true; }
    }
}

/// <summary>Arret demande (annulation).</summary>
public sealed class CancelledException : Exception { }

/// <summary>Arret normal par stop (exit) d'un programme.</summary>
public sealed class StopException : Exception { public int Code; public StopException(int c) { Code = c; } }
