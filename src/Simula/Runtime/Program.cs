namespace Studio.Simula.Runtime;

public enum Op
{
    Nop,
    // constantes et pile
    PushI,          // v
    PushR,          // idx (constante reelle)
    PushNone,
    PushText,       // idx (constante texte)
    Pop,
    Dup,
    // variables
    Ld, St,                 // slot
    StT,                    // slot (stocke une copie du descripteur texte)
    LdO, StO, StTO,         // hops slot
    LdG, StG, StTG,         // slot (bloc de l'environnement)
    LdF, StF, StFK, StTF, StTFK,  // slot ; objet sous la valeur
    LdE, StE, StEK, StTE, StTEK,  // n : nombre d'indices
    LdThis,                 // hops : cadre englobant
    LdGThis,                // cadre de l'environnement
    // parametres par nom / thunks
    LdN,                    // hops slot formalArith : evalue le thunk
    StN, StNK,              // hops slot formalArith
    EvalThunk,              // formalArith : thunk sur la pile
    MkThunk,                // unit setEntry actualArith actualType actualKind actualDims qualId
    RetThunk,               // fin d'un thunk (valeur sur la pile)
    RetThunkSet,            // fin d'un thunk en affectation
    // conversions
    I2R, I2RU, R2I, I2S, R2S, CloneT, CopyT, CopyArr,
    ChkQual,                // classId : verifie l'appartenance (none permis)
    ChkArr,                 // elemType dims
    // arithmetique
    AddI, SubI, MulI, DivI, NegI, PowII,
    AddR, SubR, MulR, DivR, NegR, PowRR, PowRI,
    CmpI,                   // rel
    CmpR,                   // rel
    CmpT,                   // rel
    RefEq, RefNe, TRefEq, TRefNe,
    Not, And, Or, Imp, Eqv,
    Is, In, Qua,            // classId
    Concat,                 // n
    TAssign, TAssignK,      // affectation de valeur texte
    StepTest,               // isReal : [delta c limit] -> bool
    // controle
    Jmp, Jf, Jt,            // pc
    JmpSlot,                // slot : saut a l'adresse contenue dans la variable
    Stmt,                   // line col
    EnterBlk,               // blockId
    ExitBlk,
    Goto,                   // labelId hops  (saut non local statique)
    GotoV,                  // LabelValue sur la pile
    MkLabel,                // labelId hops
    MkLabelVirt,            // vidx : objet sur la pile
    MkSwitch,               // unit hops
    MkSwitchVirt,           // vidx : objet sur la pile
    NewArr,                 // elemType dims : bornes sur la pile
    NewArrLike,             // nouveau tableau aux memes bornes
    SwitchSel,              // indice sur la pile, SwitchValue dessous -> LabelValue
    SwitchSelL,             // unit hops ; indice sur la pile
    RetSwitch,
    // appels
    Call,                   // proc hops argc flags  (hops -1 : environnement)
    CallR,                  // proc argc flags (objet sous les arguments)
    CallV,                  // vidx argc flags generic (objet sous les arguments)
    CallP,                  // argc flags generic (ProcValue sous les arguments)
    MkProc,                 // proc hops
    MkProcR,                // proc  (objet sur la pile)
    MkProcV,                // vidx  (objet sur la pile)
    Ret,
    Native,                 // id argc
    // objets
    New,                    // class hops argc
    NewS,                   // class argc (cadre statique sous les arguments)
    NewBlk,                 // class hops argc
    EndObj,
    Detach,
    Resume,
    CallObj,
    Halt,
}

public sealed class BlockInfo
{
    public string Name = "";
    public int NSlots, MaxStack;
    public List<VarDebug> Vars = new();
}

public sealed record VarDebug(string Name, int Slot, string Type, byte Kind);

public sealed class LineEntry
{
    public int Pc, Line, Col, File;
}

public sealed class CodeUnit
{
    public string Name = "";
    public int[] Code = Array.Empty<int>();
    public int NSlots;
    public int MaxStack;
    public int GenericEntry = -1;
    public List<LineEntry> Lines = new();
    public Dictionary<int, int> LabelPcs = new();
    public List<VarDebug> Vars = new();
    public ProcInfo? Proc;
    public ClassInfo? Class;
    public bool IsThunk, IsSwitch;
    public int File;

    public LineEntry? LineAt(int pc)
    {
        LineEntry? best = null;
        int lo = 0, hi = Lines.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Lines[mid].Pc <= pc) { best = Lines[mid]; lo = mid + 1; }
            else hi = mid - 1;
        }
        return best;
    }
}

public sealed class ProcInfo
{
    public int Id;
    public string Name = "";
    public CodeUnit Unit = null!;
    public int NParams;
    public byte ResultType;      // BaseType ; 0 = procedure propre
    public int ResultSlot = -1;
    public ClassInfo? ResultQual;
}

public sealed class VirtualEntry
{
    public ProcInfo? Proc;
    public int LabelId = -1;
    public CodeUnit? SwitchUnit;
}

public sealed class ClassInfo
{
    public int Id;
    public string Name = "";
    public ClassInfo? Prefix;
    public int Level;               // 0 pour la classe racine
    public int NSlots;
    public CodeUnit Body = null!;
    public int[] ParamSlots = Array.Empty<int>();
    public VirtualEntry[] VTable = Array.Empty<VirtualEntry>();
    public bool IsPrefixedBlock;
    public ClassInfo[] Chain = Array.Empty<ClassInfo>();   // Chain[k] = prefixe de niveau k
    public List<VarDebug> Attrs = new();

    public bool IsSubclassOf(ClassInfo c) => c.Level <= Level && Chain[c.Level] == c;
}

public sealed class SimProgram
{
    public List<CodeUnit> Units = new();
    public List<ProcInfo> Procs = new();
    public List<ClassInfo> Classes = new();
    public List<BlockInfo> Blocks = new();
    public List<double> Reals = new();
    public List<TextObj> Texts = new();
    public List<string> Files = new();
    public CodeUnit Main = null!;
    public int EnvSlots;
    public int StopLabel;
    public int SysoutSlot = -1;
    public List<string> LabelNames = new();
}
