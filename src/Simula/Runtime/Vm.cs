using System.Runtime.CompilerServices;
using System.Text;
using Studio.Common;

namespace Studio.Simula.Runtime;

sealed class HaltException : Exception { }
sealed class CancelException : Exception { }

/// <summary>Simula virtual machine: frames on the heap, coroutines (detach / resume / call).</summary>
public sealed partial class Vm
{
    readonly SimProgram prog;
    readonly IProgramIO io;
    public Frame Env = null!;
    readonly SimDebugger dbg;
    CancellationToken ct;
    int budget = 20000;
    static int objectCounter;

    // for the debugger and error reports
    internal Frame? CurFrame;
    internal int CurPc;

    public Vm(SimProgram program, IProgramIO io, SimDebugger debugger)
    {
        prog = program;
        this.io = io;
        dbg = debugger;
    }

    public ExecutionResult Run(CancellationToken token)
    {
        ct = token;
        Natives.LowTen = '&';
        Natives.DecimalMark = '.';
        Env = new Frame(prog.Main, prog.EnvSlots, 16, FrameKind.Env);
        Env.Base = prog.EnvSlots;
        dbgActive = dbg.Active;
        dbg.ActivityChanged = () => dbgActive = dbg.Active;
        try
        {
            Execute();
            CloseAll();
            return new ExecutionResult(0, null);
        }
        catch (CancelException)
        {
            CloseAll();
            return new ExecutionResult(-1, null, true);
        }
        catch (RuntimeFault rf)
        {
            FlushSysout();
            CloseAll();
            return new ExecutionResult(1, rf.Info);
        }
    }

    sealed class RuntimeFault : Exception
    {
        public RuntimeFault(RuntimeErrorInfo info) : base(info.Message) { Info = info; }
        public RuntimeErrorInfo Info { get; }
    }

    // ------------------------------------------------------------ position and call stack

    internal (string File, int Line, int Col) PosOf(Frame fr, int pc)
    {
        var le = fr.Unit.LineAt(Math.Max(0, pc - 1));
        if (le == null) return (prog.Files.Count > 0 ? prog.Files[0] : "", 0, 0);
        return (le.File >= 0 && le.File < prog.Files.Count ? prog.Files[le.File] : "", le.Line, le.Col);
    }

    internal static string FrameName(Frame fr) => fr.Kind switch
    {
        FrameKind.Block => fr.Block?.Name ?? "block",
        FrameKind.Object => fr.Unit.Name + (fr is SimObject so ? $" #{so.Serial}" : ""),
        FrameKind.Env => "program",
        FrameKind.Thunk => "name parameter",
        FrameKind.Switch => fr.Unit.Name,
        _ => fr.Unit.Name
    };

    internal List<(Frame Frame, int Pc)> Chain(Frame top, int topPc)
    {
        var l = new List<(Frame, int)>();
        Frame? fr = top;
        int pc = topPc;
        var seen = new HashSet<Frame>();
        while (fr != null && l.Count < 500 && seen.Add(fr))
        {
            l.Add((fr, pc));
            Frame? next = fr.Dyn;
            if (next == null && fr is SimObject so && so.State == ObjState.Resumed && so.Static != null)
                next = so.Static.MainReact;
            fr = next;
            pc = fr?.Pc ?? 0;
        }
        return l;
    }

    internal List<StackFrameInfo> StackInfo(Frame top, int topPc)
    {
        var res = new List<StackFrameInfo>();
        foreach (var (fr, pc) in Chain(top, topPc))
        {
            var (file, line, col) = PosOf(fr, pc);
            if (file == "prelude.sim") continue;
            res.Add(new StackFrameInfo(FrameName(fr), file, line, col));
        }
        return res;
    }

    RuntimeFault Fault(string msg, Frame f, int pc)
    {
        var (file, line, col) = PosOf(f, pc);
        // the position of an error inside the prelude is reported at the user's call site
        if (file == "prelude.sim")
        {
            foreach (var (fr, p) in Chain(f, pc))
            {
                var pp = PosOf(fr, p);
                if (pp.File != "prelude.sim" && pp.Line > 0) { (file, line, col) = pp; break; }
            }
        }
        return new RuntimeFault(new RuntimeErrorInfo(msg, file, line, col, StackInfo(f, pc)));
    }

    // ------------------------------------------------------------ execution loop

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Frame Up(Frame f, int hops)
    {
        while (hops-- > 0) f = f.Static!;
        return f;
    }

    void Execute()
    {
        Frame f = Env;
        int[] code = f.Unit.Code;
        int pc = 0;
        Value[] s = f.S;
        int sp = f.Sp;
        var classes = prog.Classes;
        var procs = prog.Procs;
        var units = prog.Units;

        while (true)
        {
            try
            {
                while (true)
                {
                    if (sp + 8 > s.Length)
                    {
                        Array.Resize(ref f.S, s.Length * 2 + 8);
                        s = f.S;
                    }
                    switch ((Op)code[pc++])
                    {
                        case Op.Nop: break;
                        case Op.PushI: s[sp++] = new Value { I = code[pc++] }; break;
                        case Op.PushR: s[sp++] = Value.Real(prog.Reals[code[pc++]]); break;
                        case Op.PushNone: s[sp++] = default; break;
                        case Op.PushText:
                            {
                                var to = prog.Texts[code[pc++]];
                                s[sp++] = new Value { O = new TextRef(to, 1, to.Chars.Length) };
                                break;
                            }
                        case Op.Pop: sp--; break;
                        case Op.Dup: s[sp] = s[sp - 1]; sp++; break;

                        case Op.Ld: s[sp++] = s[code[pc++]]; break;
                        case Op.St: s[code[pc++]] = s[--sp]; break;
                        case Op.StT: { var v = s[--sp]; s[code[pc++]] = new Value { O = ((TextRef?)v.O)?.Clone() }; break; }
                        case Op.LdO: { var fr = Up(f, code[pc++]); s[sp++] = fr.S[code[pc++]]; break; }
                        case Op.StO: { var fr = Up(f, code[pc++]); fr.S[code[pc++]] = s[--sp]; break; }
                        case Op.StTO: { var fr = Up(f, code[pc++]); var v = s[--sp]; fr.S[code[pc++]] = new Value { O = ((TextRef?)v.O)?.Clone() }; break; }
                        case Op.LdG: s[sp++] = Env.S[code[pc++]]; break;
                        case Op.StG: Env.S[code[pc++]] = s[--sp]; break;
                        case Op.StTG: { var v = s[--sp]; Env.S[code[pc++]] = new Value { O = ((TextRef?)v.O)?.Clone() }; break; }

                        case Op.LdF:
                            {
                                var o = s[sp - 1].O as Frame ?? throw new SimulaError("attribute access through a none reference");
                                s[sp - 1] = o.S[code[pc++]];
                                break;
                            }
                        case Op.StF:
                        case Op.StFK:
                        case Op.StTF:
                        case Op.StTFK:
                            {
                                var op = (Op)code[pc - 1];
                                var v = s[--sp];
                                var o = s[--sp].O as Frame ?? throw new SimulaError("attribute assignment through a none reference");
                                if (op is Op.StTF or Op.StTFK) v = new Value { O = ((TextRef?)v.O)?.Clone() };
                                o.S[code[pc++]] = v;
                                if (op is Op.StFK or Op.StTFK) s[sp++] = v;
                                break;
                            }
                        case Op.LdE:
                            {
                                int n = code[pc++];
                                var a = s[sp - n - 1].O as SimArray ?? throw new SimulaError("array not initialised");
                                int idx = Index(a, s, sp - n, n);
                                sp -= n;
                                s[sp - 1] = a.Data[idx];
                                break;
                            }
                        case Op.StE:
                        case Op.StEK:
                        case Op.StTE:
                        case Op.StTEK:
                            {
                                var op = (Op)code[pc - 1];
                                int n = code[pc++];
                                var v = s[--sp];
                                var a = s[sp - n - 1].O as SimArray ?? throw new SimulaError("array not initialised");
                                int idx = Index(a, s, sp - n, n);
                                sp -= n + 1;
                                if (op is Op.StTE or Op.StTEK) v = new Value { O = ((TextRef?)v.O)?.Clone() };
                                a.Data[idx] = v;
                                if (op is Op.StEK or Op.StTEK) s[sp++] = v;
                                break;
                            }
                        case Op.LdThis: s[sp++] = new Value { O = Up(f, code[pc++]) }; break;
                        case Op.LdGThis: s[sp++] = new Value { O = Env }; break;

                        // ---------------------------------------- thunks
                        case Op.LdN:
                            {
                                var fr = Up(f, code[pc++]);
                                var th = (Thunk)fr.S[code[pc++]].O!;
                                int ak = code[pc++];
                                f.Pc = pc; f.Sp = sp;
                                f = EnterThunk(th, ak, f);
                                code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.EvalThunk:
                            {
                                var th = (Thunk)s[--sp].O!;
                                int ak = code[pc++];
                                f.Pc = pc; f.Sp = sp;
                                f = EnterThunk(th, ak, f);
                                code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.StN:
                        case Op.StNK:
                            {
                                bool keep = (Op)code[pc - 1] == Op.StNK;
                                var fr = Up(f, code[pc++]);
                                var th = (Thunk)fr.S[code[pc++]].O!;
                                int arith = code[pc++] & 3;
                                var v = s[--sp];
                                if (th.SetEntry < 0) throw new SimulaError("assignment to a name parameter whose actual parameter is not a variable");
                                var cv = v;
                                if (arith != 0 && th.ActualArith != 0 && arith != th.ActualArith)
                                    cv = arith == 1 ? Value.Real(v.I) : new Value { I = Natives.ToInt(v.R) };
                                f.Pc = pc; f.Sp = sp;
                                var tf = new Frame(th.Unit, 1, 8, FrameKind.Thunk) { Static = th.Env, Dyn = f, ThunkRef = th, WantResult = keep, Aux = v };
                                tf.Base = 1;
                                tf.S[0] = cv;
                                tf.Pc = th.SetEntry;
                                f = tf;
                                code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.MkThunk:
                            {
                                var th = new Thunk
                                {
                                    Unit = units[code[pc]], SetEntry = code[pc + 1], AltEntry = code[pc + 2],
                                    ActualArith = (byte)code[pc + 3], ActualType = (byte)code[pc + 4], ActualKind = (byte)code[pc + 5],
                                    ActualQual = code[pc + 6] >= 0 ? classes[code[pc + 6]] : null, Env = f
                                };
                                pc += 7;
                                s[sp++] = new Value { O = th };
                                break;
                            }
                        case Op.RetThunk:
                            {
                                var v = s[--sp];
                                var th = f.ThunkRef!;
                                int conv = f.ThunkConv;
                                if (conv != 0 && th.ActualArith != 0 && conv != th.ActualArith)
                                    v = conv == 2 ? Value.Real(v.I) : new Value { I = Natives.ToInt(v.R) };
                                var nf = f.Dyn!;
                                f = nf; code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                s[sp++] = v;
                                break;
                            }
                        case Op.RetThunkSet:
                            {
                                bool keep = f.WantResult;
                                var v = f.Aux;
                                var nf = f.Dyn!;
                                f = nf; code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                if (keep) s[sp++] = v;
                                break;
                            }

                        // ---------------------------------------- conversions
                        case Op.I2R: s[sp - 1] = Value.Real(s[sp - 1].I); break;
                        case Op.I2RU: s[sp - 2] = Value.Real(s[sp - 2].I); break;
                        case Op.R2I: s[sp - 1] = new Value { I = Natives.ToInt(s[sp - 1].R) }; break;
                        case Op.I2S:
                            {
                                long v = s[sp - 1].I;
                                if (v < short.MinValue || v > short.MaxValue) throw new SimulaError($"value {v} outside the range of short integer");
                                break;
                            }
                        case Op.R2S:
                            {
                                long v = Natives.ToInt(s[sp - 1].R);
                                if (v < short.MinValue || v > short.MaxValue) throw new SimulaError($"value {v} outside the range of short integer");
                                s[sp - 1] = new Value { I = v };
                                break;
                            }
                        case Op.CloneT: s[sp - 1] = new Value { O = ((TextRef?)s[sp - 1].O)?.Clone() }; break;
                        case Op.CopyT: s[sp - 1] = new Value { O = Natives.CopyText((TextRef?)s[sp - 1].O) }; break;
                        case Op.CopyArr: s[sp - 1] = new Value { O = (s[sp - 1].O as SimArray)?.Copy() }; break;
                        case Op.ChkQual:
                            {
                                var ci = classes[code[pc++]];
                                if (s[sp - 1].O is SimObject so && !so.Class.IsSubclassOf(ci))
                                    throw new SimulaError($"reference to an object of class {so.Class.Name} is not compatible with ref({ci.Name})");
                                break;
                            }
                        case Op.ChkArr: pc += 2; break;

                        // ---------------------------------------- integer arithmetic (32 bits, overflow is a runtime error)
                        case Op.AddI: sp--; s[sp - 1] = new Value { I = CheckInt(s[sp - 1].I + s[sp].I) }; break;
                        case Op.SubI: sp--; s[sp - 1] = new Value { I = CheckInt(s[sp - 1].I - s[sp].I) }; break;
                        case Op.MulI: sp--; s[sp - 1] = new Value { I = CheckInt(s[sp - 1].I * s[sp].I) }; break;
                        case Op.DivI:
                            {
                                sp--;
                                long d = s[sp].I;
                                if (d == 0) throw new SimulaError("integer division by zero");
                                s[sp - 1] = new Value { I = CheckInt(s[sp - 1].I / d) };
                                break;
                            }
                        case Op.NegI: s[sp - 1] = new Value { I = CheckInt(-s[sp - 1].I) }; break;
                        case Op.PowII:
                            {
                                sp--;
                                long i = s[sp - 1].I, j = s[sp].I;
                                if (j < 0 || (i == 0 && j == 0)) throw new SimulaError($"undefined integer exponentiation ({i}**{j})");
                                s[sp - 1] = new Value { I = PowInt(i, j) };
                                break;
                            }
                        // ---------------------------------------- reals
                        case Op.AddR: sp--; s[sp - 1] = Value.Real(CheckReal(s[sp - 1].R + s[sp].R)); break;
                        case Op.SubR: sp--; s[sp - 1] = Value.Real(CheckReal(s[sp - 1].R - s[sp].R)); break;
                        case Op.MulR: sp--; s[sp - 1] = Value.Real(CheckReal(s[sp - 1].R * s[sp].R)); break;
                        case Op.DivR:
                            {
                                sp--;
                                double d = s[sp].R;
                                if (d == 0) throw new SimulaError("division by zero");
                                s[sp - 1] = Value.Real(CheckReal(s[sp - 1].R / d));
                                break;
                            }
                        case Op.NegR: s[sp - 1] = Value.Real(-s[sp - 1].R); break;
                        case Op.PowRR:
                            {
                                sp--;
                                double x = s[sp - 1].R, r = s[sp].R;
                                if (x < 0 || (x == 0 && r <= 0)) throw new SimulaError(FormattableString.Invariant($"undefined exponentiation ({x}**{r})"));
                                s[sp - 1] = Value.Real(CheckReal(x > 0 ? Math.Pow(x, r) : 0.0));
                                break;
                            }
                        case Op.PowRI:
                            {
                                sp--;
                                double x = s[sp - 1].R; long i = s[sp].I;
                                if (i == 0 && x == 0) throw new SimulaError("undefined exponentiation (0.0**0)");
                                if (x == 0 && i < 0) throw new SimulaError("division by zero (0.0 ** negative exponent)");
                                s[sp - 1] = Value.Real(CheckReal(Math.Pow(x, i)));
                                break;
                            }
                        case Op.CmpI:
                            {
                                sp--;
                                long a = s[sp - 1].I, b = s[sp].I;
                                s[sp - 1] = new Value { I = Rel(code[pc++], a.CompareTo(b)) ? 1 : 0 };
                                break;
                            }
                        case Op.CmpR:
                            {
                                sp--;
                                double a = s[sp - 1].R, b = s[sp].R;
                                int c = a < b ? -1 : a > b ? 1 : 0;
                                s[sp - 1] = new Value { I = Rel(code[pc++], c) ? 1 : 0 };
                                break;
                            }
                        case Op.CmpT:
                            {
                                sp--;
                                int c = Natives.CompareText((TextRef?)s[sp - 1].O, (TextRef?)s[sp].O);
                                s[sp - 1] = new Value { I = Rel(code[pc++], c) ? 1 : 0 };
                                break;
                            }
                        case Op.RefEq: sp--; s[sp - 1] = new Value { I = ReferenceEquals(s[sp - 1].O, s[sp].O) ? 1 : 0 }; break;
                        case Op.RefNe: sp--; s[sp - 1] = new Value { I = ReferenceEquals(s[sp - 1].O, s[sp].O) ? 0 : 1 }; break;
                        case Op.TRefEq: sp--; s[sp - 1] = new Value { I = TextIdentical((TextRef?)s[sp - 1].O, (TextRef?)s[sp].O) ? 1 : 0 }; break;
                        case Op.TRefNe: sp--; s[sp - 1] = new Value { I = TextIdentical((TextRef?)s[sp - 1].O, (TextRef?)s[sp].O) ? 0 : 1 }; break;
                        case Op.Not: s[sp - 1] = new Value { I = s[sp - 1].I == 0 ? 1 : 0 }; break;
                        case Op.And: sp--; s[sp - 1] = new Value { I = s[sp - 1].I & s[sp].I }; break;
                        case Op.Or: sp--; s[sp - 1] = new Value { I = s[sp - 1].I | s[sp].I }; break;
                        case Op.Imp: sp--; s[sp - 1] = new Value { I = (s[sp - 1].I == 0 || s[sp].I != 0) ? 1 : 0 }; break;
                        case Op.Eqv: sp--; s[sp - 1] = new Value { I = s[sp - 1].I == s[sp].I ? 1 : 0 }; break;
                        case Op.Is:
                            {
                                var ci = classes[code[pc++]];
                                s[sp - 1] = new Value { I = s[sp - 1].O is SimObject so && so.Class == ci ? 1 : 0 };
                                break;
                            }
                        case Op.In:
                            {
                                var ci = classes[code[pc++]];
                                s[sp - 1] = new Value { I = s[sp - 1].O is SimObject so && so.Class.IsSubclassOf(ci) ? 1 : 0 };
                                break;
                            }
                        case Op.Qua:
                            {
                                var ci = classes[code[pc++]];
                                var o = s[sp - 1].O as SimObject ?? throw new SimulaError($"qua {ci.Name} applied to none");
                                if (!o.Class.IsSubclassOf(ci)) throw new SimulaError($"qua: the object of class {o.Class.Name} does not belong to class {ci.Name}");
                                break;
                            }
                        case Op.Concat:
                            {
                                int n = code[pc++];
                                var r = Natives.Concat(s, sp - n, n);
                                sp -= n;
                                s[sp++] = new Value { O = r };
                                break;
                            }
                        case Op.TAssign:
                        case Op.TAssignK:
                            {
                                var y = (TextRef?)s[--sp].O;
                                var x = (TextRef?)s[--sp].O;
                                Natives.AssignText(x, y);
                                if ((Op)code[pc - 1] == Op.TAssignK) s[sp++] = new Value { O = x };
                                break;
                            }
                        case Op.StepTest:
                            {
                                int mask = code[pc++];
                                var l = s[--sp]; var c = s[--sp]; var d = s[--sp];
                                bool ok;
                                if (mask == 0)
                                    ok = d.I > 0 ? c.I <= l.I : d.I < 0 ? c.I >= l.I : true;
                                else
                                {
                                    double dd = (mask & 1) != 0 ? d.R : d.I, cc = (mask & 2) != 0 ? c.R : c.I, ll = (mask & 4) != 0 ? l.R : l.I;
                                    ok = dd > 0 ? cc <= ll : dd < 0 ? cc >= ll : true;
                                }
                                s[sp++] = new Value { I = ok ? 1 : 0 };
                                break;
                            }

                        // ---------------------------------------- control
                        case Op.Jmp:
                            {
                                int t = code[pc];
                                if (t < pc && --budget <= 0) Tick();
                                pc = t;
                                break;
                            }
                        case Op.Jf: if (s[--sp].I == 0) pc = code[pc]; else pc++; break;
                        case Op.Jt: if (s[--sp].I != 0) pc = code[pc]; else pc++; break;
                        case Op.JmpSlot: pc = (int)s[code[pc]].I; break;
                        case Op.Stmt:
                            pc += 2;
                            if (--budget <= 0) Tick();
                            if (dbgActive)
                            {
                                f.Pc = pc; f.Sp = sp;
                                dbg.OnStatement(this, f, pc, code[pc - 2], code[pc - 1]);
                            }
                            break;
                        case Op.EnterBlk:
                            {
                                var bi = prog.Blocks[code[pc++]];
                                var nf = new Frame(f.Unit, bi.NSlots, 8, FrameKind.Block) { Static = f, Dyn = f, Block = bi, Base = bi.NSlots };
                                f.Pc = pc; f.Sp = sp;
                                f = nf; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.ExitBlk:
                            {
                                var nf = f.Dyn!;
                                f = nf; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.Goto:
                            {
                                int id = code[pc++]; int hops = code[pc++];
                                var target = hops < 0 ? Env : Up(f, hops);
                                f.Pc = pc; f.Sp = sp;
                                f = DoGoto(f, target, id, out pc);
                                code = f.Unit.Code; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.GotoV:
                            {
                                var lv = s[--sp].O as LabelValue ?? throw new SimulaError("goto: invalid designational expression");
                                f.Pc = pc; f.Sp = sp;
                                f = DoGoto(f, lv.Frame, lv.LabelId, out pc);
                                code = f.Unit.Code; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.MkLabel:
                            {
                                int id = code[pc++]; int hops = code[pc++];
                                s[sp++] = new Value { O = new LabelValue(hops < 0 ? Env : Up(f, hops), id) };
                                break;
                            }
                        case Op.MkLabelVirt:
                            {
                                int vi = code[pc++];
                                var o = s[sp - 1].O as SimObject ?? throw new SimulaError("virtual label of a none object");
                                var e = o.Class.VTable[vi];
                                if (e.LabelId < 0) throw new SimulaError("unmatched virtual label");
                                s[sp - 1] = new Value { O = new LabelValue(o, e.LabelId) };
                                break;
                            }
                        case Op.MkSwitch:
                            {
                                var u = units[code[pc++]]; int hops = code[pc++];
                                s[sp++] = new Value { O = new SwitchValue(u, hops < 0 ? Env : Up(f, hops)) };
                                break;
                            }
                        case Op.MkSwitchVirt:
                            {
                                int vi = code[pc++];
                                var o = s[sp - 1].O as SimObject ?? throw new SimulaError("virtual switch of a none object");
                                var e = o.Class.VTable[vi];
                                if (e.SwitchUnit == null) throw new SimulaError("unmatched virtual switch");
                                s[sp - 1] = new Value { O = new SwitchValue(e.SwitchUnit, o) };
                                break;
                            }
                        case Op.SwitchSel:
                        case Op.SwitchSelL:
                            {
                                SwitchValue sv;
                                long idx;
                                if ((Op)code[pc - 1] == Op.SwitchSel)
                                {
                                    idx = s[--sp].I;
                                    sv = s[--sp].O as SwitchValue ?? throw new SimulaError("invalid switch");
                                }
                                else
                                {
                                    var u = units[code[pc++]]; int hops = code[pc++];
                                    idx = s[--sp].I;
                                    sv = new SwitchValue(u, hops < 0 ? Env : Up(f, hops));
                                }
                                f.Pc = pc; f.Sp = sp;
                                var nf = new Frame(sv.Unit, 1, 8, FrameKind.Switch) { Static = sv.Env, Dyn = f, Base = 1 };
                                nf.S[0] = new Value { I = idx };
                                f = nf; code = f.Unit.Code; pc = 0; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.RetSwitch:
                            {
                                var v = s[--sp];
                                var nf = f.Dyn!;
                                f = nf; code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                s[sp++] = v;
                                break;
                            }

                        // ---------------------------------------- calls
                        case Op.Call:
                            {
                                var pi = procs[code[pc]]; int hops = code[pc + 1]; int argc = code[pc + 2]; int flags = code[pc + 3];
                                pc += 4;
                                var st = hops < 0 ? Env : Up(f, hops);
                                f.Pc = pc; sp -= argc; f.Sp = sp;
                                f = EnterProc(pi, st, s, sp, argc, flags, f);
                                code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.CallR:
                            {
                                var pi = procs[code[pc]]; int argc = code[pc + 1]; int flags = code[pc + 2];
                                pc += 3;
                                var st = s[sp - argc - 1].O as Frame ?? throw new SimulaError($"call of {pi.Name} through a none reference");
                                f.Pc = pc; sp -= argc; f.Sp = sp - 1;
                                var nf = EnterProc(pi, st, s, sp, argc, flags, f);
                                sp--;
                                f = nf; code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.CallV:
                            {
                                int vi = code[pc]; int argc = code[pc + 1]; int flags = code[pc + 2];
                                pc += 3;
                                var o = s[sp - argc - 1].O as SimObject ?? throw new SimulaError("call of a virtual procedure through a none reference");
                                var pi = o.Class.VTable[vi].Proc ?? throw new SimulaError($"unmatched virtual procedure in class {o.Class.Name}");
                                f.Pc = pc; sp -= argc; f.Sp = sp - 1;
                                var nf = EnterProc(pi, o, s, sp, argc, flags, f);
                                f = nf; code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.CallP:
                            {
                                int argc = code[pc]; int flags = code[pc + 1];
                                pc += 2;
                                var pv = s[sp - argc - 1].O as ProcValue ?? throw new SimulaError("call of an undefined procedure parameter");
                                f.Pc = pc; sp -= argc; f.Sp = sp - 1;
                                var nf = EnterProc(pv.Proc, pv.Static, s, sp, argc, flags, f);
                                f = nf; code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.MkProc:
                            {
                                var pi = procs[code[pc++]]; int hops = code[pc++];
                                s[sp++] = new Value { O = new ProcValue(pi, hops < 0 ? Env : Up(f, hops)) };
                                break;
                            }
                        case Op.MkProcR:
                            {
                                var pi = procs[code[pc++]];
                                var o = s[sp - 1].O as Frame ?? throw new SimulaError("procedure of a none object");
                                s[sp - 1] = new Value { O = new ProcValue(pi, o) };
                                break;
                            }
                        case Op.MkProcV:
                            {
                                int vi = code[pc++];
                                var o = s[sp - 1].O as SimObject ?? throw new SimulaError("virtual procedure of a none object");
                                var pi = o.Class.VTable[vi].Proc ?? throw new SimulaError("unmatched virtual procedure");
                                s[sp - 1] = new Value { O = new ProcValue(pi, o) };
                                break;
                            }
                        case Op.Ret:
                            {
                                var pi = f.Unit.Proc!;
                                bool want = f.WantResult && pi.ResultType != 0;
                                Value v = default;
                                if (want)
                                {
                                    v = f.S[pi.ResultSlot];
                                    if (v.O is TextRef tr) v.O = tr.Clone();
                                }
                                var nf = f.Dyn!;
                                f = nf; code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                if (want) s[sp++] = v;
                                break;
                            }
                        case Op.Native:
                            {
                                int id = code[pc++]; int argc = code[pc++];
                                CurFrame = f; CurPc = pc;
                                f.Pc = pc; f.Sp = sp;
                                var r = Natives.Fn(id)(this, s, sp - argc);
                                sp -= argc;
                                if (!Natives.IsVoid(id)) s[sp++] = r;
                                break;
                            }
                        case Op.NewArr:
                            {
                                int et = code[pc++]; int n = code[pc++];
                                var lo = new int[n]; var hi = new int[n];
                                for (int d = 0; d < n; d++) { lo[d] = (int)s[sp - 2 * n + 2 * d].I; hi[d] = (int)s[sp - 2 * n + 2 * d + 1].I; }
                                sp -= 2 * n;
                                s[sp++] = new Value { O = new SimArray(lo, hi, (byte)et) };
                                break;
                            }
                        case Op.NewArrLike:
                            {
                                var a = (SimArray)s[sp - 1].O!;
                                s[sp - 1] = new Value { O = new SimArray((int[])a.Lower.Clone(), (int[])a.Upper.Clone(), a.ElemType) };
                                break;
                            }

                        // ---------------------------------------- objects and sequencing
                        case Op.New:
                        case Op.NewBlk:
                            {
                                bool blk = (Op)code[pc - 1] == Op.NewBlk;
                                var ci = classes[code[pc]]; int hops = code[pc + 1]; int argc = code[pc + 2];
                                pc += 3;
                                var st = hops < 0 ? Env : Up(f, hops);
                                var o = MakeObject(ci, st, blk, s, sp - argc, argc);
                                sp -= argc;
                                if (!blk) s[sp++] = new Value { O = o };
                                f.Pc = pc; f.Sp = sp;
                                o.Dyn = f;
                                f = o; code = f.Unit.Code; pc = 0; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.NewS:
                            {
                                var ci = classes[code[pc]]; int argc = code[pc + 1];
                                pc += 2;
                                var st = s[sp - argc - 1].O as Frame ?? throw new SimulaError("new: enclosing object is none");
                                var o = MakeObject(ci, st, false, s, sp - argc, argc);
                                sp -= argc + 1;
                                s[sp++] = new Value { O = o };
                                f.Pc = pc; f.Sp = sp;
                                o.Dyn = f;
                                f = o; code = f.Unit.Code; pc = 0; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.EndObj:
                            {
                                var o = (SimObject)f;
                                Frame nf;
                                if (o.Kind == FrameKind.PrefixedBlock)
                                {
                                    o.State = ObjState.Terminated;
                                    nf = o.Dyn!;
                                }
                                else if (o.State == ObjState.Attached)
                                {
                                    o.State = ObjState.Terminated;
                                    nf = o.Dyn!;
                                    o.Dyn = null;
                                }
                                else if (o.State == ObjState.Resumed)
                                {
                                    o.State = ObjState.Terminated;
                                    var head = o.Static!;
                                    head.Operative = null;
                                    nf = head.MainReact ?? throw new SimulaError("object end: main component not found");
                                    head.MainReact = null;
                                }
                                else throw new SimulaError("end of an object in an inconsistent state");
                                o.Sp = o.Base;
                                if (o.S.Length > o.Base + 4) Array.Resize(ref o.S, o.Base);
                                f = nf; code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.Detach:
                            {
                                var x = s[--sp].O;
                                if (x is not SimObject o) throw new SimulaError("detach: none object");
                                if (o.Kind == FrameKind.PrefixedBlock) break;
                                Frame nf;
                                switch (o.State)
                                {
                                    case ObjState.Attached:
                                        if (!Operating(f, o)) throw new SimulaError($"detach of an attached object that is not operating ({o.Class.Name})");
                                        f.Pc = pc; f.Sp = sp;
                                        o.React = f;
                                        o.State = ObjState.Detached;
                                        nf = o.Dyn ?? throw new SimulaError("detach: object not attached to any block");
                                        o.Dyn = null;
                                        break;
                                    case ObjState.Resumed:
                                        {
                                            f.Pc = pc; f.Sp = sp;
                                            o.React = f;
                                            o.State = ObjState.Detached;
                                            var head = o.Static!;
                                            head.Operative = null;
                                            nf = head.MainReact ?? throw new SimulaError("detach: main component not found");
                                            head.MainReact = null;
                                            break;
                                        }
                                    case ObjState.Detached: throw new SimulaError($"detach of an object that is already detached ({o.Class.Name})");
                                    default: throw new SimulaError($"detach of a terminated object ({o.Class.Name})");
                                }
                                f = nf; code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.Resume:
                            {
                                var y = s[--sp].O as SimObject ?? throw new SimulaError("resume(none)");
                                if (y.State == ObjState.Resumed) break;
                                if (y.State == ObjState.Attached) throw new SimulaError($"resume of an attached object ({y.Class.Name})");
                                if (y.State == ObjState.Terminated) throw new SimulaError($"resume of a terminated object ({y.Class.Name})");
                                var head = y.Static!;
                                if (head.Kind is not (FrameKind.Block or FrameKind.PrefixedBlock or FrameKind.Env))
                                    throw new SimulaError($"resume: object {y.Class.Name} is not local to a system head (block)");
                                f.Pc = pc; f.Sp = sp;
                                var x = head.Operative;
                                if (x == null) head.MainReact = f;
                                else { x.React = f; x.State = ObjState.Detached; }
                                y.State = ObjState.Resumed;
                                head.Operative = y;
                                var nf = y.React!;
                                y.React = null;
                                f = nf; code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.CallObj:
                            {
                                var y = s[--sp].O as SimObject ?? throw new SimulaError("call(none)");
                                if (y.State != ObjState.Detached) throw new SimulaError($"call of a {(y.State == ObjState.Terminated ? "terminated" : y.State == ObjState.Attached ? "attached" : "resumed")} object ({y.Class.Name})");
                                f.Pc = pc; f.Sp = sp;
                                y.State = ObjState.Attached;
                                y.Dyn = f;
                                var nf = y.React!;
                                y.React = null;
                                f = nf; code = f.Unit.Code; pc = f.Pc; s = f.S; sp = f.Sp;
                                break;
                            }
                        case Op.Halt:
                            CurFrame = f; CurPc = pc;
                            return;
                        default:
                            throw new SimulaError($"unknown instruction {(Op)code[pc - 1]} (internal error)");
                    }
                }
            }
            catch (SimulaError e)
            {
                CurFrame = f; CurPc = pc;
                if (dbg.BreakOnError) dbg.OnError(this, f, pc, e.Message);
                throw Fault(e.Message, f, pc);
            }
            catch (HaltException)
            {
                return;
            }
            catch (CancelException) { throw; }
            catch (IndexOutOfRangeException e) when (e.TargetSite?.DeclaringType == typeof(Vm))
            {
                CurFrame = f; CurPc = pc;
                throw Fault("internal machine error (" + e.Message + ")", f, pc);
            }
            catch (InvalidCastException e)
            {
                CurFrame = f; CurPc = pc;
                throw Fault("internal machine error (" + e.Message + ")", f, pc);
            }
            catch (IOException e)
            {
                CurFrame = f; CurPc = pc;
                throw Fault("input/output error: " + e.Message, f, pc);
            }
            catch (UnauthorizedAccessException e)
            {
                CurFrame = f; CurPc = pc;
                throw Fault("file access denied: " + e.Message, f, pc);
            }
            catch (StackOverflowException)
            {
                throw;
            }
            catch (OutOfMemoryException)
            {
                CurFrame = f; CurPc = pc;
                throw Fault("out of memory (recursion or allocation too large)", f, pc);
            }
        }
    }

    bool dbgActive;

    void Tick()
    {
        budget = 2000;
        dbgActive = dbg.Active;
        if (ct.IsCancellationRequested) throw new CancelException();
        if (dbg.PauseRequested) { }
    }

    /// <summary>Checks that the result of an integer operation is representable (32 bits).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long CheckInt(long r)
    {
        if (r < int.MinValue || r > int.MaxValue) throw new SimulaError("integer overflow");
        return r;
    }

    /// <summary>Checks that the result of a real operation is finite (real overflow is a runtime error).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double CheckReal(double r)
    {
        if (!double.IsFinite(r)) throw new SimulaError("real overflow");
        return r;
    }

    /// <summary>i ** j for j &gt;= 0 by repeated multiplication (1 for j = 0), with overflow detection.</summary>
    internal static long PowInt(long i, long j)
    {
        if (j == 0) return 1;
        if (i == 0 || i == 1) return i;
        if (i == -1) return (j & 1) == 0 ? 1 : -1;
        long r = 1;
        for (long k = 0; k < j; k++)
        {
            r *= i;
            if (r < int.MinValue || r > int.MaxValue) throw new SimulaError("integer overflow");
        }
        return r;
    }

    static bool Rel(int rel, int c) => (Compiler.T)rel switch
    {
        Compiler.T.Lt => c < 0, Compiler.T.Le => c <= 0, Compiler.T.Eq => c == 0,
        Compiler.T.Ge => c >= 0, Compiler.T.Gt => c > 0, _ => c != 0
    };

    static bool Operating(Frame f, SimObject o)
    {
        // dynamic enclosure (7.3.4): a resumed object leads to its system head
        int n = 0;
        for (Frame? x = f; x != null && n < 1_000_000; n++)
        {
            if (x == o) return true;
            x = x is SimObject so && so.State == ObjState.Resumed ? so.Static : x.Dyn;
        }
        return false;
    }

    static bool TextIdentical(TextRef? a, TextRef? b)
    {
        if (a == null || b == null) return a == null && b == null;
        return a.Obj == b.Obj && a.Start == b.Start && a.Length == b.Length;
    }

    static int Index(SimArray a, Value[] s, int b, int n)
    {
        if (n != a.Rank) throw new SimulaError($"number of subscripts ({n}) differs from the array dimension ({a.Rank})");
        if (n == 1) return a.Index1(s[b].I);
        int idx = 0;
        for (int d = 0; d < n; d++)
        {
            long i = s[b + d].I;
            if (i < a.Lower[d] || i > a.Upper[d]) throw a.IndexError(d, i);
            idx += (int)(i - a.Lower[d]) * a.Stride[d];
        }
        return idx;
    }

    Frame EnterProc(ProcInfo pi, Frame st, Value[] s, int argBase, int argc, int flags, Frame caller)
    {
        bool generic = (flags & 2) != 0;
        if (argc != pi.NParams) throw new SimulaError($"{pi.Name}: {pi.NParams} parameter(s) expected, {argc} given");
        var u = pi.Unit;
        var nf = new Frame(u, u.NSlots, 8, FrameKind.Procedure) { Static = st, Dyn = caller, WantResult = (flags & 1) != 0, Base = u.NSlots };
        Array.Copy(s, argBase, nf.S, 0, argc);
        nf.Pc = generic ? u.GenericEntry : 0;
        return nf;
    }

    Frame EnterThunk(Thunk th, int ak, Frame caller)
    {
        int arith = ak & 3, kind = ak >> 2;
        int entry = 0;
        if (kind == 0 && th.ActualKind == 2)
        {
            // procedure actual, simple formal: call the procedure
            if (th.AltEntry < 0) throw new SimulaError("parameter: a value is expected but the actual parameter is a procedure");
            entry = th.AltEntry;
        }
        else if (kind == (int)Compiler.ParamKind.Label && th.ActualKind == 0 && th.AltEntry >= 0)
        {
            // Algol 60: unsigned integer actual parameter used as a label (generic call)
            entry = th.AltEntry;
        }
        else if (kind != 0 && th.ActualKind != kind && !(th.ActualKind == 0 && kind == 2))
            throw new SimulaError("parameter: the kind of the actual parameter (array, procedure, label...) does not match the formal");
        var tf = new Frame(th.Unit, 1, 8, FrameKind.Thunk) { Static = th.Env, Dyn = caller, ThunkRef = th, ThunkConv = (byte)arith, Base = 1 };
        tf.Pc = entry;
        return tf;
    }

    SimObject MakeObject(ClassInfo ci, Frame st, bool blk, Value[] s, int argBase, int argc)
    {
        if (ci.Body == null) throw new SimulaError($"class {ci.Name} has no body (internal error)");
        var o = new SimObject(ci, blk) { Static = st, State = ObjState.Attached, Base = ci.NSlots, Serial = Interlocked.Increment(ref objectCounter) };
        var ps = ci.ParamSlots;
        if (argc != ps.Length) throw new SimulaError($"{ci.Name}: {ps.Length} parameter(s) expected");
        for (int i = 0; i < argc; i++) o.S[ps[i]] = s[argBase + i];
        return o;
    }

    /// <summary>goto to the point (target, label): passes through the end of the intermediate instances (7.3.5).</summary>
    Frame DoGoto(Frame from, Frame target, int labelId, out int pc)
    {
        var y = from;
        int guard = 0;
        while (y != target)
        {
            if (y == Env || ++guard > 1_000_000) throw new SimulaError($"goto: label '{LabelName(labelId)}' is not accessible");
            Frame? next;
            if (y is SimObject o && o.Kind == FrameKind.Object)
            {
                if (o.State == ObjState.Resumed)
                {
                    o.State = ObjState.Terminated;
                    var head = o.Static!;
                    head.Operative = null;
                    next = head.MainReact;
                    head.MainReact = null;
                }
                else
                {
                    o.State = ObjState.Terminated;
                    next = o.Dyn;
                    o.Dyn = null;
                }
            }
            else
            {
                if (y is SimObject pb) pb.State = ObjState.Terminated;
                next = y.Dyn;
            }
            y = next ?? throw new SimulaError($"goto: label '{LabelName(labelId)}' is not accessible from this point");
        }
        if (!target.Unit.LabelPcs.TryGetValue(labelId, out pc))
            throw new SimulaError($"goto: label '{LabelName(labelId)}' not found");
        target.Sp = target.Base;
        return target;
    }

    string LabelName(int id) => id >= 0 && id < prog.LabelNames.Count ? prog.LabelNames[id] : "?";
}
