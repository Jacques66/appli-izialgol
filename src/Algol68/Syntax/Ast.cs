using System.Numerics;

namespace Studio.Algol68.Syntax;

/// <summary>Node of the syntax tree.</summary>
public abstract class Ast
{
    public string File = "";
    public int Line, Col;
    public T At<T>(Token t) where T : Ast { File = t.File; Line = t.Line; Col = t.Col; return (T)this; }
    public T At<T>(Ast a) where T : Ast { File = a.File; Line = a.Line; Col = a.Col; return (T)this; }
    // semantic cache
    internal object? SemCache;
}

// ---------------------------------------------------------------- declarers

public abstract class DeclarerAst : Ast { }

/// <summary>INT, REAL, BITS, BYTES, COMPL with a length; BOOL, CHAR, STRING, VOID, FORMAT, FILE, CHANNEL, SEMA.</summary>
public sealed class PrimDeclarer : DeclarerAst { public string Name = ""; public int Size; }
public sealed class IndicantDeclarer : DeclarerAst { public string Name = ""; }
public sealed class RefDeclarer : DeclarerAst { public DeclarerAst Sub = null!; }
public sealed class FlexDeclarer : DeclarerAst { public DeclarerAst Sub = null!; }
public sealed class ProcDeclarer : DeclarerAst { public List<DeclarerAst> Params = new(); public DeclarerAst Result = null!; }
public sealed class StructDeclarer : DeclarerAst { public List<(DeclarerAst Mode, string Name, Ast Pos)> Fields = new(); }
public sealed class UnionDeclarer : DeclarerAst { public List<DeclarerAst> Members = new(); }
public sealed class BoundAst : Ast { public UnitAst? Lwb, Upb; public bool Formal; }
public sealed class RowDeclarer : DeclarerAst { public List<BoundAst> Bounds = new(); public DeclarerAst Elem = null!; }

// ---------------------------------------------------------------- units

public abstract class UnitAst : Ast { }

public sealed class IntDenot : UnitAst { public BigInteger Value; public int Size; }
public sealed class RealDenot : UnitAst { public string Text = ""; public int Size; }
public sealed class BitsDenot : UnitAst { public BigInteger Value; public int Radix; public int Size; public string Text = ""; }
public sealed class StringDenot : UnitAst { public string Value = ""; }
public sealed class BoolDenot : UnitAst { public bool Value; }
public sealed class EmptyDenot : UnitAst { }
public sealed class NilAst : UnitAst { }
public sealed class SkipAst : UnitAst { }
public sealed class VacuumAst : UnitAst { }
public sealed class IdentAst : UnitAst { public string Name = ""; }

public sealed class DyadicAst : UnitAst { public string Op = ""; public UnitAst Left = null!, Right = null!; }
public sealed class MonadicAst : UnitAst { public string Op = ""; public UnitAst Operand = null!; }

/// <summary>Element of a call or slice: unit, or trimmer [lwb]:[upb][@ at], or empty.</summary>
public sealed class IndexerAst : Ast
{
    public UnitAst? Unit;            // subscript or argument
    public bool IsTrimmer;
    public UnitAst? Lwb, Upb, AtLwb; // trimmer
    public bool HasColon;
}
public sealed class ApplyAst : UnitAst { public UnitAst Primary = null!; public List<IndexerAst> Args = new(); public bool Brackets; }
public sealed class SelectionAst : UnitAst { public string Field = ""; public UnitAst Secondary = null!; }
public sealed class GeneratorAst : UnitAst { public bool Heap; public DeclarerAst Declarer = null!; }
public sealed class CastAst : UnitAst { public DeclarerAst Declarer = null!; public UnitAst Enclosed = null!; }
public sealed class AssignAst : UnitAst { public UnitAst Dest = null!, Source = null!; }
public sealed class IdentityRelAst : UnitAst { public UnitAst Left = null!, Right = null!; public bool Not; }
public sealed class ParamAst : Ast { public DeclarerAst Declarer = null!; public string Name = ""; }
public sealed class RoutineTextAst : UnitAst { public List<ParamAst> Params = new(); public DeclarerAst Result = null!; public UnitAst Body = null!; }
public sealed class JumpAst : UnitAst { public string Label = ""; }
public sealed class AndThenAst : UnitAst { public UnitAst Left = null!, Right = null!; public bool IsOr; }
public sealed class AssertAst : UnitAst { public UnitAst Cond = null!; }
public sealed class FormatTextAst : UnitAst { public FormatAst Format = null!; }

// clauses
public sealed class SerialAst : Ast
{
    public List<Ast> Phrases = new();      // DeclarationAst or LabeledUnit
    // each EXIT completes a "series": index of the phrase after which EXIT appears
    public List<int> ExitAfter = new();
}
public sealed class LabeledUnit : Ast { public List<string> Labels = new(); public UnitAst Unit = null!; }

public sealed class ClosedAst : UnitAst { public SerialAst Serial = null!; }
public sealed class CollateralAst : UnitAst { public List<UnitAst> Units = new(); }
public sealed class ParallelAst : UnitAst { public List<UnitAst> Units = new(); }
public sealed class CondAst : UnitAst
{
    public SerialAst If = null!; public SerialAst Then = null!;
    public SerialAst? Else;          // ELSE ... or ELIF (as a nested CondAst)
    public UnitAst? ElifCond;        // ELIF: nested conditional clause
    public bool Brief;               // form ( | | ): conditional or case depending on the mode
}
public sealed class CaseAst : UnitAst
{
    public SerialAst Selector = null!;
    public List<UnitAst> In = new();
    public SerialAst? Out;
    public UnitAst? Ouse;           // OUSE: nested CaseAst or ConformityAst
}
public sealed class ConformityCase : Ast { public DeclarerAst Declarer = null!; public string? Name; public UnitAst Unit = null!; public List<DeclarerAst>? Alternatives; }
public sealed class ConformityAst : UnitAst
{
    public SerialAst Selector = null!;
    public List<ConformityCase> Cases = new();
    public SerialAst? Out;
    public UnitAst? Ouse;
}
public sealed class LoopAst : UnitAst
{
    public string? Var;
    public UnitAst? From, By, To;
    public bool DownTo;
    public SerialAst? While;
    public SerialAst Body = null!;
    public SerialAst? Until;
}

// ---------------------------------------------------------------- declarations

public abstract class DeclarationAst : Ast { }
public sealed class ModeDeclAst : DeclarationAst { public List<(string Name, DeclarerAst Declarer, Ast Pos)> Items = new(); }
public sealed class PrioDeclAst : DeclarationAst { public List<(string Name, int Prio, Ast Pos)> Items = new(); }
public sealed class OpDeclAst : DeclarationAst
{
    public DeclarerAst? Mode;     // form OP (INT,INT)INT + = ...
    public List<(string Name, UnitAst Value, Ast Pos)> Items = new();
}
public sealed class IdentityDeclAst : DeclarationAst { public DeclarerAst Declarer = null!; public List<(string Name, UnitAst Value, Ast Pos)> Items = new(); }
public sealed class VarDeclAst : DeclarationAst { public bool Heap; public DeclarerAst Declarer = null!; public List<(string Name, UnitAst? Init, Ast Pos)> Items = new(); }
/// <summary>PROC p = routine text (or PROC p := routine text: variable).</summary>
public sealed class ProcDeclAst : DeclarationAst { public bool Variable; public bool Heap; public List<(string Name, UnitAst Value, Ast Pos)> Items = new(); }

