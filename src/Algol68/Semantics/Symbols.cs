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
    /// <summary>Standard procedure with another number of parameters (establish of the RR and of a68g).</summary>
    public IdSym? Alt;
    public bool IsPrelude => Range.Routine == null;
}

public sealed class LabelSym : Sym { public JumpTarget Target = new(); }

public sealed class ModeSym : Sym
{
    public DeclarerAst? Decl;
    public Mode? Mode;
    public IndicantMode? Placeholder;
    public GenSpec? Gen;          // actual bounds of the declaration (null: none)
    public bool HasActualBounds;
}

/// <summary>Operator definition.</summary>
public sealed class OpSym : Sym
{
    public ProcMode Mode = null!;
    public Func<Node[], Node>? Factory;   // standard operator: builds a specialised node
    public IdSym? Proc;                    // declared operator: procedure value in a slot
}

public sealed class Range
{
    public Range? Parent;
    public RoutineCtx? Routine;           // null: prelude
    public int Level;                     // level in the routine (0 = main range of the routine)
    public int Depth;                     // absolute static depth
    public readonly Dictionary<string, IdSym> Ids = new();
    public readonly Dictionary<string, LabelSym> Labels = new();
    public readonly Dictionary<string, ModeSym> Modes = new();
    public readonly Dictionary<string, List<OpSym>> Ops = new();
    public readonly Dictionary<string, int> Prios = new();
    public bool HasGenerators;            // contains LOC generators: scope check of the yield
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
