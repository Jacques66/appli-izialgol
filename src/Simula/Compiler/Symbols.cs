using Studio.Simula.Runtime;

namespace Studio.Simula.Compiler;

public sealed class SType
{
    public readonly BaseType B;
    public readonly ClassSym? Q;
    SType(BaseType b, ClassSym? q = null) { B = b; Q = q; }

    public static readonly SType Int = new(BaseType.Integer);
    public static readonly SType Short = new(BaseType.ShortInteger);
    public static readonly SType Real = new(BaseType.Real);
    public static readonly SType LReal = new(BaseType.LongReal);
    public static readonly SType Bool = new(BaseType.Boolean);
    public static readonly SType Char = new(BaseType.Character);
    public static readonly SType Text = new(BaseType.Text);
    public static readonly SType NoneT = new(BaseType.Ref);         // none : qualifie par une classe interieure a toutes
    public static readonly SType Void = new(BaseType.None);
    public static readonly SType Err = new(BaseType.Notype);
    public static readonly SType LabelT = new(BaseType.Label);
    public static readonly SType SwitchT = new(BaseType.Switch);

    static readonly Dictionary<ClassSym, SType> refs = new();
    public static SType Ref(ClassSym q)
    {
        lock (refs)
        {
            if (!refs.TryGetValue(q, out var t)) refs[q] = t = new SType(BaseType.Ref, q);
            return t;
        }
    }

    public bool IsArith => B is BaseType.Integer or BaseType.ShortInteger or BaseType.Real or BaseType.LongReal;
    public bool IsReal => B is BaseType.Real or BaseType.LongReal;
    public bool IsIntLike => B is BaseType.Integer or BaseType.ShortInteger;
    public bool IsRef => B == BaseType.Ref;
    public bool IsErr => B == BaseType.Notype;
    public byte Arith => IsIntLike ? (byte)1 : IsReal ? (byte)2 : (byte)0;

    public override string ToString() => B switch
    {
        BaseType.Ref => Q == null ? "none" : $"ref({Q.Orig})",
        BaseType.ShortInteger => "short integer",
        BaseType.LongReal => "long real",
        BaseType.None => "(sans type)",
        BaseType.Notype => "(erreur)",
        _ => B.ToString().ToLowerInvariant()
    };
}

public abstract class Sym
{
    public string Name = "", Orig = "";
    public SrcPos Pos;
    public Scope Scope = null!;            // portee de declaration
    public ClassSym? OwnerClass;           // attribut de cette classe
    public abstract string KindName { get; }
}

public sealed class VarSym : Sym
{
    public TypeSpec Spec = null!;
    public SType? Type;
    public int Slot;
    public bool IsConst;
    public Expr? ConstExpr;
    public Value ConstValue;
    public bool ConstDone, ConstBusy;
    public bool IsHiddenTemp;
    public override string KindName => IsConst ? "constante" : "variable";
}

public sealed class ArraySym : Sym
{
    public TypeSpec Spec = null!;
    public SType? Elem;
    public int Dims;
    public int Slot;
    public ArraySegment Segment = null!;
    public override string KindName => "tableau";
}

public sealed class ParamSym : Sym
{
    public ParamSpec Spec = null!;
    public SType? Type;            // type (element pour tableau, resultat pour procedure)
    public ParamKind Kind;
    public ParamMode Mode;         // Value, Default(=reference), Name (resolu)
    public int Slot;
    public bool IsClassParam;
    public override string KindName => Kind switch
    {
        ParamKind.Array => "parametre tableau",
        ParamKind.Procedure => "parametre procedure",
        ParamKind.Label => "parametre etiquette",
        ParamKind.Switch => "parametre aiguillage",
        _ => "parametre"
    };
    public bool IsName => Mode == ParamMode.Name;
}

public sealed class ProcSym : Sym
{
    public ProcDecl Decl = null!;
    public ProcInfo Info = null!;
    public SType? Result;          // Void pour procedure propre
    public List<ParamSym> Params = new();
    public FrameScope Body = null!;
    public VirtualSym? Matches;    // correspondance d'un virtuel
    public string? Intrinsic;      // appel remplace par une operation interne
    public bool Emitted;
    public override string KindName => "procedure";
}

public sealed class ClassSym : Sym
{
    public ClassDecl? Decl;
    public ClassSym? Prefix;
    public ClassInfo Info = null!;
    public FrameScope Body = null!;
    public int Level;
    public bool IsRoot, IsSystem, IsPrefixedBlock;
    public BlockStmt? PrefixedBlock;
    public Dictionary<string, Sym> Attrs = new();
    public List<ParamSym> Params = new();          // propres a ce niveau
    public List<ParamSym> AllParams = new();       // concatenes
    public List<VirtualSym> AllVirtuals = new();   // index = numero de virtuel
    public HashSet<string> ProtectedHere = new(), HiddenHere = new();
    public int SlotStart, SlotEnd;
    public bool LaidOut, Laying;
    public bool UsesThis;   // "this C" dans le corps (interdit comme prefixe de bloc)
    public List<ClassSym> Subclasses = new();
    public override string KindName => "classe";

    public bool IsSubclassOf(ClassSym c)
    {
        for (var k = this; k != null; k = k.Prefix) if (k == c) return true;
        return false;
    }

    public IEnumerable<ClassSym> ChainInnerToOuter()
    {
        for (var k = this; k != null; k = k.Prefix) yield return k;
    }
}

public sealed class LabelSym : Sym
{
    public int Id;
    public FrameScope Frame = null!;
    public VirtualSym? Matches;
    public override string KindName => "etiquette";
}

public sealed class SwitchSym : Sym
{
    public SwitchDecl Decl = null!;
    public CodeUnit? Unit;
    public FrameScope Frame = null!;
    public FrameScope? UnitScope;
    public VirtualSym? Matches;
    public override string KindName => "aiguillage";
}

public sealed class VirtualSym : Sym
{
    public VirtualSpec Spec = null!;
    public int Index;
    public ParamKind Kind;         // Procedure, Label, Switch
    public SType? Type;
    public ProcSym? Heading;       // specification de procedure (is ...)
    public bool ProtocolDone;
    public ProcSym? DirectSig;     // si toutes les correspondances ont la meme entete
    public override string KindName => "quantite virtuelle";
}

public sealed class DetachSym : Sym { public override string KindName => "procedure detach"; }

// ------------------------------------------------------------------ portees

public abstract class Scope
{
    public Scope? Parent;
    public FrameScope Frame = null!;
    public Dictionary<string, Sym> Syms = new();
    public string Label = "";
}

public enum FKind { Env, Block, Procedure, ClassBody, PrefixedBlock, Thunk, Switch }

public sealed class FrameScope : Scope
{
    public FKind Kind;
    public int Depth;
    public int NSlots;
    public ClassSym? Class;        // ClassBody et PrefixedBlock
    public ProcSym? Proc;
    public BlockInfo? Block;       // bloc en ligne
    public BlockStmt? BlockNode;
    public bool IsPrelude;
    public FrameScope() { Frame = this; }

    public int AllocSlot() => NSlots++;
}

/// <summary>Bloc de connexion (inspect) : rend visibles les attributs de l'objet connecte.</summary>
public sealed class ConnScope : Scope
{
    public ClassSym? Qual;
    public int TempSlot;
}

/// <summary>Bloc fictif (corps de for, corps de procedure sans declarations...) pour les etiquettes.</summary>
public sealed class LabelScope : Scope { }

/// <summary>Bloc sans cadre propre fusionne dans le cadre englobant (corps de procedure).</summary>
public sealed class MergedScope : Scope { }
