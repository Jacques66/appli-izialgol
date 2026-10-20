namespace Studio.Algol68.Syntax;

// Arbre des textes de format (RR 10.3.4).

public sealed class ReplicatorAst : Ast
{
    public int Static = 1;
    public UnitAst? Dynamic;   // n(...)
    public bool Present;       // un replicateur a ete ecrit
}

/// <summary>Element d'insertion : alignement (k x y l p q) ou litteral replique.</summary>
public sealed class InsertElemAst : Ast
{
    public ReplicatorAst Rep = new();
    public char Code;          // 'k' 'x' 'y' 'l' 'p' 'q' ou '"' pour un litteral
    public string Literal = "";
}

public sealed class InsertionAst : Ast
{
    public List<InsertElemAst> Elems = new();
    public bool IsEmpty => Elems.Count == 0;
}

public sealed class FrameAst : Ast
{
    public InsertionAst Ins = new();
    public ReplicatorAst Rep = new();
    public bool Suppressed;
    public char Marker;        // 'd' 'z' 'a' '.' 'e' 'i' '+' '-' 'b' 'r'
    public int Radix;          // pour 'r'
}

public enum PatternKind { None, Integral, Real, Complex, Boolean, String, Bits, IntChoice, BoolChoice, Format, General, GeneralH }

public sealed class FormatPatternAst : Ast
{
    public PatternKind Kind;
    public List<FrameAst> Frames = new();
    public InsertionAst Lead = new();                // insertion des motifs c, b(..), f, g
    public List<InsertionAst> Choices = new();       // c(..) et b(..)
    public UnitAst? FormatUnit;                      // f(...)
    public List<UnitAst> GeneralParams = new();      // g(...)
}

public abstract class FormatPieceAst : Ast { }

public sealed class PictureAst : FormatPieceAst
{
    public FormatPatternAst? Pattern;
    public InsertionAst Insertion = new();           // insertion apres le motif
}

public sealed class CollectionAst : FormatPieceAst
{
    public InsertionAst Before = new();
    public ReplicatorAst Rep = new();
    public List<FormatPieceAst> Pieces = new();
    public InsertionAst After = new();
}

public sealed class FormatAst : Ast { public List<FormatPieceAst> Pieces = new(); }
