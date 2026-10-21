namespace Studio.Simula.Runtime;

public enum Op
{
    Nop,
    // constants and stack
    PushI,          // v
    PushR,          // idx (real constant)
    PushNone,
    PushText,       // idx (text constant)
    Pop,
    Dup,
    // variables
    Ld, St,                 // slot
    StT,                    // slot (stores a copy of the text descriptor)
    LdO, StO, StTO,         // hops slot
    LdG, StG, StTG,         // slot (environment block)
    LdF, StF, StFK, StTF, StTFK,  // slot; object below the value
    LdE, StE, StEK, StTE, StTEK,  // n: number of subscripts
    LdThis,                 // hops: enclosing frame
    LdGThis,                // environment frame
    // name parameters / thunks
    LdN,                    // hops slot formalArith: evaluates the thunk
    StN, StNK,              // hops slot formalArith
    EvalThunk,              // formalArith: thunk on the stack
    MkThunk,                // unit setEntry actualArith actualType actualKind actualDims qualId
    RetThunk,               // end of a thunk (value on the stack)
    RetThunkSet,            // end of a thunk in assignment mode
    // conversions
    I2R, I2RU, R2I, I2S, R2S, CloneT, CopyT, CopyArr,
    ChkQual,                // classId: checks the membership (none allowed)
    ChkArr,                 // elemType dims
    // arithmetic
    AddI, SubI, MulI, DivI, NegI, PowII,
    AddR, SubR, MulR, DivR, NegR, PowRR, PowRI,
    CmpI,                   // rel
    CmpR,                   // rel
    CmpT,                   // rel
    RefEq, RefNe, TRefEq, TRefNe,
    Not, And, Or, Imp, Eqv,
    Is, In, Qua,            // classId
    Concat,                 // n
    TAssign, TAssignK,      // text value assignment
    StepTest,               // isReal : [delta c limit] -> bool
    // control
    Jmp, Jf, Jt,            // pc
    JmpSlot,                // slot: jump to the address held in the variable
    Stmt,                   // line col
    EnterBlk,               // blockId
    ExitBlk,
    Goto,                   // labelId hops  (static non-local jump)
    GotoV,                  // LabelValue on the stack
    MkLabel,                // labelId hops
    MkLabelVirt,            // vidx: object on the stack
    MkSwitch,               // unit hops
    MkSwitchVirt,           // vidx: object on the stack
    NewArr,                 // elemType dims: bounds on the stack
    NewArrLike,             // new array with the same bounds
    SwitchSel,              // index on the stack, SwitchValue below -> LabelValue
    SwitchSelL,             // unit hops; index on the stack
    RetSwitch,
    // calls
    Call,                   // proc hops argc flags  (hops -1 : environnement)
    CallR,                  // proc argc flags (object below the arguments)
    CallV,                  // vidx argc flags generic (object below the arguments)
    CallP,                  // argc flags generic (ProcValue below the arguments)
    MkProc,                 // proc hops
    MkProcR,                // proc  (object on the stack)
    MkProcV,                // vidx  (object on the stack)
    Ret,
    Native,                 // id argc
    // objects
    New,                    // class hops argc
    NewS,                   // class argc (static frame below the arguments)
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
    public byte ResultType;      // BaseType; 0 = proper procedure
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
    public int Level;               // 0 for the root class
    public int NSlots;
    public CodeUnit Body = null!;
    public int[] ParamSlots = Array.Empty<int>();
    public VirtualEntry[] VTable = Array.Empty<VirtualEntry>();
    public bool IsPrefixedBlock;
    public ClassInfo[] Chain = Array.Empty<ClassInfo>();   // Chain[k] = prefix of level k
    public List<VarDebug> Attrs = new();
    public List<VarDebug> AllAttrs = new();   // including internal attributes

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
