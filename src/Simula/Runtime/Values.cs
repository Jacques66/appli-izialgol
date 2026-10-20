using System.Runtime.CompilerServices;
using System.Text;

namespace Studio.Simula.Runtime;

/// <summary>Machine value: integer/Boolean/character/real (bits) in I, references in O.</summary>
public struct Value
{
    public long I;
    public object? O;

    public double R
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] readonly get => BitConverter.Int64BitsToDouble(I);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] set => I = BitConverter.DoubleToInt64Bits(value);
    }

    public static Value Int(long v) => new() { I = v };
    public static Value Real(double v) => new() { I = BitConverter.DoubleToInt64Bits(v) };
    public static Value Bool(bool b) => new() { I = b ? 1 : 0 };
    public static Value Ref(object? o) => new() { O = o };
}

public sealed class SimulaError : Exception
{
    public SimulaError(string message) : base(message) { }
}

/// <summary>Text object: main frame of characters.</summary>
public sealed class TextObj
{
    public readonly char[] Chars;
    public readonly bool Const;
    public TextObj(char[] chars, bool isConst) { Chars = chars; Const = isConst; }
    public TextObj(string s, bool isConst) { Chars = s.ToCharArray(); Const = isConst; }
}

/// <summary>Text descriptor (text variable). null represents notext.</summary>
public sealed class TextRef
{
    public TextObj Obj;
    public int Start;   // 1..
    public int Length;  // >= 1
    public int Pos;     // 1..Length+1

    public TextRef(TextObj obj, int start, int length, int pos = 1)
    {
        Obj = obj; Start = start; Length = length; Pos = pos;
    }

    public TextRef Clone() => new(Obj, Start, Length, Pos);

    public static TextRef? FromString(string s, bool isConst = false) =>
        s.Length == 0 ? null : new TextRef(new TextObj(s, isConst), 1, s.Length);

    public static TextRef? Blanks(int n)
    {
        if (n < 0) throw new SimulaError("blanks: negative parameter");
        if (n == 0) return null;
        var a = new char[n];
        Array.Fill(a, ' ');
        return new TextRef(new TextObj(a, false), 1, n);
    }

    public char this[int i]   // 0-based within the frame
    {
        get => Obj.Chars[Start - 1 + i];
        set => Obj.Chars[Start - 1 + i] = value;
    }

    public override string ToString() => new(Obj.Chars, Start - 1, Length);

    public static string Str(TextRef? t) => t == null ? "" : t.ToString();
    public static int Len(TextRef? t) => t?.Length ?? 0;
}

/// <summary>Simula array with arbitrary bounds.</summary>
public sealed class SimArray
{
    public readonly int[] Lower, Upper, Stride;
    public readonly Value[] Data;
    public readonly byte ElemType;      // BaseType code of the element

    public SimArray(int[] lower, int[] upper, byte elemType)
    {
        int n = lower.Length;
        Lower = lower; Upper = upper; ElemType = elemType;
        Stride = new int[n];
        long size = 1;
        for (int d = n - 1; d >= 0; d--)
        {
            Stride[d] = (int)size;
            long ext = (long)upper[d] - lower[d] + 1;
            if (ext < 0) ext = 0;
            size *= ext;
            if (size > 200_000_000) throw new SimulaError("array too large");
        }
        Data = new Value[size];
    }

    SimArray(SimArray src)
    {
        Lower = (int[])src.Lower.Clone(); Upper = (int[])src.Upper.Clone(); Stride = (int[])src.Stride.Clone();
        ElemType = src.ElemType;
        Data = (Value[])src.Data.Clone();
    }

    public int Rank => Lower.Length;

    public SimArray Copy()
    {
        var c = new SimArray(this);
        // the texts of an array copied by value are copies of the values
        for (int i = 0; i < c.Data.Length; i++)
            if (c.Data[i].O is TextRef t) c.Data[i].O = Natives.CopyText(t);
        return c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Index1(long i)
    {
        if (i < Lower[0] || i > Upper[0]) throw IndexError(0, i);
        return (int)(i - Lower[0]);
    }

    public SimulaError IndexError(int dim, long i) =>
        new($"index {i} outside the bounds ({Lower[dim]}:{Upper[dim]})" + (Rank > 1 ? $" in dimension {dim + 1}" : ""));
}

public enum ObjState : byte { Attached, Detached, Resumed, Terminated }

public enum FrameKind : byte { Env, Block, Procedure, Object, PrefixedBlock, Thunk, Switch }

/// <summary>Block instance (block, procedure, object, thunk).</summary>
public class Frame
{
    public Value[] S;
    public int Sp;
    public Frame? Static;
    public Frame? Dyn;
    public CodeUnit Unit;
    public int Pc;
    public FrameKind Kind;
    public bool WantResult;
    public BlockInfo? Block;        // for inline blocks
    // quasi-parallel system head
    public SimObject? Operative;
    public Frame? MainReact;
    // thunk
    public byte ThunkConv;          // conversion to apply when a thunk returns
    public Thunk? ThunkRef;
    public int Base;                // start of the operand stack
    public Value Aux;               // kept value (assignment by name)

    public Frame(CodeUnit unit, int nslots, int maxStack, FrameKind kind)
    {
        Unit = unit;
        S = new Value[nslots + maxStack];
        Sp = nslots;
        Kind = kind;
    }
}

public sealed class SimObject : Frame
{
    public readonly ClassInfo Class;
    public ObjState State;
    public Frame? React;            // reactivation point (innermost frame)
    public int Serial;

    public SimObject(ClassInfo cls, bool prefixedBlock)
        : base(cls.Body, cls.NSlots, cls.Body.MaxStack, prefixedBlock ? FrameKind.PrefixedBlock : FrameKind.Object)
    {
        Class = cls;
    }
}

/// <summary>Parameter called by name (or parameter of a generic call).</summary>
public sealed class Thunk
{
    public CodeUnit Unit = null!;
    public Frame Env = null!;
    public int SetEntry = -1;       // -1: not assignable
    public int AltEntry = -1;       // "value" entry of a procedure actual parameter
    public byte ActualArith;        // 0 none, 1 integer, 2 real
    public byte ActualType;         // BaseType of the actual parameter
    public byte ActualKind;         // ParamKind of the actual parameter
    public int ActualDims = -1;
    public ClassInfo? ActualQual;
}

public sealed class ProcValue
{
    public ProcInfo Proc;
    public Frame Static;
    public ProcValue(ProcInfo p, Frame s) { Proc = p; Static = s; }
}

public sealed class LabelValue
{
    public Frame Frame;
    public int LabelId;
    public LabelValue(Frame f, int id) { Frame = f; LabelId = id; }
}

public sealed class SwitchValue
{
    public CodeUnit Unit;
    public Frame Env;
    public SwitchValue(CodeUnit u, Frame e) { Unit = u; Env = e; }
}
