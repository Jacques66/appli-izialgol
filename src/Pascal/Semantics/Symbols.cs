using Studio.Pascal.Exec;
using Studio.Pascal.Syntax;

namespace Studio.Pascal.Semantics;

public abstract class Sym
{
    public string Name = "";
    public int Line, Col;
}

public sealed class ConstSym : Sym
{
    public PType Type = ErrorType.T;
    public long Ord;
    public double Real;
    public string? Str;
}

public sealed class TypeSym : Sym
{
    public PType Type = ErrorType.T;
}

public sealed class VarSym : Sym
{
    public PType Type = ErrorType.T;
    public int Level, Slot;
    public bool IsRef;           // var parameter
    public bool IsParam;
    /// <summary>input/output not listed in the program heading: any use is an error.</summary>
    public bool NotInHeading;
    public bool ReportedHeading;
}

/// <summary>Field visible inside a with statement.</summary>
public sealed class WithFieldSym : Sym
{
    public Field Field = null!;
    public RecordType Record = null!;
    public int Slot;
    public bool PackedRecord;
    public string RecordDesc = "";
}

public sealed class RoutineSym : Sym
{
    public RoutineInfo Info = null!;
    public Signature Sig => Info.Sig;
    public int Level;            // level of the body
    public bool Forward, Defined;
    /// <summary>An assignment to the function identifier occurs in its block (Revised Report 11).</summary>
    public bool ResultAssigned;
    public int ParamsEnd;        // first free slot after parameters and result
    public List<VarSym> ParamSyms = new();
    public List<FormalRoutineSym> FormalSyms = new();
    public bool IsFunction => Info.IsFunction;
}

/// <summary>Procedural parameter.</summary>
public sealed class FormalRoutineSym : Sym
{
    public Signature Sig = new();
    public int Level, Slot;
    public bool IsFunction => Sig.IsFunction;
}

public enum StdKind { Procedure, Function }

public sealed class StdSym : Sym
{
    public StdKind Kind;
}

public sealed class SeqCtx
{
    public SeqCtx? Parent;
    public readonly Dictionary<LabelInfo, int> Labels = new();
    public bool NonLocal;
    public bool Encloses(SeqCtx? inner)
    {
        for (var c = inner; c != null; c = c.Parent) if (ReferenceEquals(c, this)) return true;
        return false;
    }
}

public sealed class LabelSym : Sym
{
    public long Value;
    public LabelInfo Info = null!;
    public int Level;
    public bool Defined;
    public SeqCtx? Seq;
    public bool Used, UsedNonLocally;
}

public sealed class Scope
{
    public readonly Dictionary<string, Sym> Syms = new(StringComparer.OrdinalIgnoreCase);
    public readonly Scope? Parent;
    public readonly int Level;
    public readonly bool IsWith;
    public Scope(Scope? parent, int level, bool isWith = false) { Parent = parent; Level = level; IsWith = isWith; }
}

/// <summary>Routine being compiled.</summary>
public sealed class RoutineCtx
{
    public RoutineInfo Info = null!;
    public RoutineCtx? Parent;
    public int Level;
    public int NextSlot;
    public RoutineSym? Sym;
    public readonly Dictionary<long, LabelSym> Labels = new();
    public readonly List<(LabelSym L, SeqCtx? Ctx, Token At)> LocalGotos = new();
    public int Alloc(int n) { int s = NextSlot; NextSlot += Math.Max(1, n); return s; }
}
