using Studio.Algol68.Exec;
using Studio.Algol68.Runtime;
using Studio.Algol68.Syntax;

namespace Studio.Algol68.Semantics;

public enum Strength { Soft, Weak, Meek, Firm, Strong }

public abstract class Sym
{
    public string Name = "";
    public Range Range = null!;
    public Ast? Pos;
}

public sealed class IdSym : Sym
{
    public Mode Mode = null!;
    public int Slot = -1;
    public bool Declared;
    public Value? Const;
    public Func<ExecContext, Value>? CtxValue;
    public BuiltinFn? Builtin;
    public bool IsLoopVar;
    public bool IsPrelude => Range.Routine == null;
}

public sealed class LabelSym : Sym { public JumpTarget Target = new(); }

public sealed class ModeSym : Sym
{
    public DeclarerAst? Decl;
    public Mode? Mode;
    public IndicantMode? Placeholder;
    public GenSpec? Gen;          // bornes effectives de la declaration (null : aucune)
    public bool HasActualBounds;
}

/// <summary>Definition d'operateur.</summary>
public sealed class OpSym : Sym
{
    public ProcMode Mode = null!;
    public Func<Node[], Node>? Factory;   // operateur standard : fabrique un noeud specialise
    public IdSym? Proc;                    // operateur declare : valeur procedure dans un emplacement
}

public sealed class Range
{
    public Range? Parent;
    public RoutineCtx? Routine;           // null : prelude
    public int Level;                     // niveau dans la routine (0 = portee principale de la routine)
    public int Depth;                     // profondeur statique absolue
    public readonly Dictionary<string, IdSym> Ids = new();
    public readonly Dictionary<string, LabelSym> Labels = new();
    public readonly Dictionary<string, ModeSym> Modes = new();
    public readonly Dictionary<string, List<OpSym>> Ops = new();
    public readonly Dictionary<string, int> Prios = new();
    public bool HasGenerators;            // contient des LOC : controle de portee du resultat
    public int FromLine, ToLine;
    public string File = "";
}

public sealed class RoutineCtx
{
    public RoutineCtx? Parent;
    public int Level;
    public RoutineInfo Info = null!;
    public Range? InnermostFree;
    public int NextSlot;
    public int Alloc() => NextSlot++;
}
