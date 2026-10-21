using Studio.Common;
using Studio.Simula.Runtime;

namespace Studio.Simula.Compiler;

/// <summary>Phase B: code generation (with type checking).</summary>
public sealed partial class SimCompiler
{
    sealed class Gen
    {
        public CodeUnit Unit = null!;
        public List<int> Code = new();
        public FrameScope Frame = null!;
        public Scope Scope = null!;
        public List<(int At, int LabelId)> LabelFix = new();
    }

    Gen g = null!;
    readonly Queue<Action> pendingUnits = new();

    int Here => g.Code.Count;
    void E(Op op) => g.Code.Add((int)op);
    void E(Op op, int a) { g.Code.Add((int)op); g.Code.Add(a); }
    void E(Op op, int a, int b) { g.Code.Add((int)op); g.Code.Add(a); g.Code.Add(b); }
    void E(Op op, int a, int b, int c) { g.Code.Add((int)op); g.Code.Add(a); g.Code.Add(b); g.Code.Add(c); }
    void E(Op op, int a, int b, int c, int d) { g.Code.Add((int)op); g.Code.Add(a); g.Code.Add(b); g.Code.Add(c); g.Code.Add(d); }
    void E(Op op, params int[] a) { g.Code.Add((int)op); g.Code.AddRange(a); }
    int EJump(Op op) { E(op, -1); return Here - 1; }
    void Patch(int at) => g.Code[at] = Here;
    void PatchTo(int at, int target) => g.Code[at] = target;

    SrcPos curPos;

    void Line(SrcPos p)
    {
        if (p.Line == 0) return;
        curPos = p;
        var u = g.Unit;
        if (u.Lines.Count == 0 || u.Lines[^1].Line != p.Line || u.Lines[^1].File != p.File || u.Lines[^1].Pc != Here)
            u.Lines.Add(new LineEntry { Pc = Here, Line = p.Line, Col = p.Col, File = p.File });
        E(Op.Stmt, p.Line, p.Col);
    }

    void MarkPos(SrcPos p)
    {
        if (p.Line == 0) return;
        var u = g.Unit;
        u.Lines.Add(new LineEntry { Pc = Here, Line = p.Line, Col = p.Col, File = p.File });
    }

    CodeUnit NewUnit(string name, int file)
    {
        var u = new CodeUnit { Name = name, File = file };
        prog.Units.Add(u);
        return u;
    }

    void FinishUnit()
    {
        foreach (var (at, id) in g.LabelFix)
        {
            if (g.Unit.LabelPcs.TryGetValue(id, out var pc)) g.Code[at] = pc;
            else Err(SrcPos.None, "label not found in the unit (internal error)");
        }
        g.Unit.Code = g.Code.ToArray();
    }

    Gen BeginGen(CodeUnit u, FrameScope frame, Scope scope)
    {
        var old = g;
        g = new Gen { Unit = u, Frame = frame, Scope = scope };
        return old;
    }

    // ================================================================ entry point

    /// <summary>Lexer used for the user program and its included files (Simula by default).</summary>
    public Func<string, int, Action<SrcPos, string>, Func<string, List<Token>?>, List<Token>>? UserLexer { get; set; }

    List<Token> LexUser(string text, int fileIndex, Func<string, List<Token>?> includes)
    {
        if (UserLexer != null) return UserLexer(text, fileIndex, Err, includes);
        var lx = new Lexer(text, fileIndex, Err) { IncludeHandler = includes };
        return lx.Tokenize();
    }

    public SimProgram? CompileProgram(SourceFile user, string preludeText, Func<string, string, SourceFile?>? resolver)
    {
        includeResolver = resolver;
        files.Add(user.Path);
        preludeFile = files.Count;
        files.Add("prelude.sim");
        prog.Files.AddRange(files);
        // (included or external files are appended after these)
        CreateRoot();

        // user program
        int includeNesting = 0;
        Func<string, List<Token>?>? handler = null;
        handler = name =>
        {
            var sf = includeResolver?.Invoke(name, user.Path);
            if (sf == null || ++includeNesting > 32) return null;
            int fi = files.Count;
            files.Add(sf.Path);
            prog.Files.Add(sf.Path);
            var r = LexUser(sf.Text, fi, handler!);
            includeNesting--;
            return r;
        };
        var utoks = LexUser(user.Text, 0, handler);
        var uparser = new Parser(utoks, Err) { Dialect = dialect };
        var (userStmt, userExternals, userModules) = uparser.ParseProgram();
        if (userStmt == null && userModules.Count > 0)
        {
            Err(new SrcPos(0, 1, 1), "this file is a module (class or procedure declaration), not a main program; it can be used through 'external class'/'external procedure'");
        }

        // prelude
        var plex = new Lexer(preludeText, preludeFile, (p, m) => Err(p, "prelude: " + m)) { AllowUnderscoreStart = true };
        var ptoks = plex.Tokenize();
        var pparser = new Parser(ptoks, (p, m) => Err(p, "prelude: " + m)) { InPrelude = true };
        var (envStmt, _, _) = pparser.ParseProgram();
        if (envStmt is not BlockStmt envBlock) { Err(SrcPos.None, "invalid prelude"); return null; }
        envBlock.IsProgramBlock = true;
        // insertion of the program
        bool inserted = false;
        Stmt programStmt = userStmt ?? new DummyStmt();
        if (userExternals.Count > 0)
        {
            // external head of the program: the declarations are placed in an enclosing block
            var wrap = new BlockStmt { Pos = programStmt.Pos, Decls = userExternals, Stmts = new List<Stmt> { programStmt } };
            programStmt = wrap;
        }
        ReplacePlaceholder(envBlock.Stmts, programStmt, ref inserted);
        if (!inserted) { Err(SrcPos.None, "prelude: program insertion point not found"); return null; }

        // phase A
        env = new FrameScope { Kind = FKind.Env, Depth = 0, IsPrelude = true, Label = "environment" };
        scopeOf[envBlock] = env;
        DeclareBlockContents(env, envBlock.Decls, envBlock.Stmts, null);
        MarkIntrinsics();
        if (diags.Count(d => d.Severity == Severity.Error) > 0 && diags.Any(d => d.File == "prelude.sim"))
            return null;

        // phase B
        var main = NewUnit("program", 0);
        prog.Main = main;
        BeginGen(main, env, env);
        EmitDeclInit(envBlock.Decls, env);
        foreach (var st in envBlock.Stmts) EmitStmt(st);
        E(Op.Halt);
        FinishUnit();
        main.NSlots = env.NSlots;
        prog.EnvSlots = env.NSlots;
        if (env.Syms.TryGetValue("stop_", out var stopSym) && stopSym is LabelSym ls) prog.StopLabel = ls.Id;
        if (env.Syms.TryGetValue("sysout_", out var so) && so is VarSym sov) prog.SysoutSlot = sov.Slot;
        main.Vars.AddRange(env.Syms.Values.Where(s => s.Pos.File != preludeFile || s.Name is "sysin_" or "sysout_").Select(DebugVar).Where(v => v != null).Select(v => v!));
        while (pendingUnits.Count > 0) pendingUnits.Dequeue()();
        foreach (var (cls, sw) in pendingSwitchVirtuals)
        {
            EnsureSwitchUnit(sw);
            // all classes inheriting this entry
        }
        // propagation of virtual switch units
        foreach (var c in prog.Classes)
            foreach (var ve in c.VTable)
                _ = ve;
        if (HasErrors) return null;
        return prog;
    }

    static void ReplacePlaceholder(List<Stmt> stmts, Stmt program, ref bool done)
    {
        for (int i = 0; i < stmts.Count; i++)
        {
            var s = stmts[i];
            if (s is ExprStmt es && es.E is Ident id && id.Name == "__program__")
            {
                program.Labels.InsertRange(0, s.Labels);
                stmts[i] = program;
                done = true;
                return;
            }
            switch (s)
            {
                case InspectStmt ins:
                    if (ins.DoBody is ExprStmt e2 && e2.E is Ident id2 && id2.Name == "__program__") { ins.DoBody = program; done = true; return; }
                    if (ins.DoBody != null) { var l = new List<Stmt> { ins.DoBody }; ReplacePlaceholder(l, program, ref done); ins.DoBody = l[0]; if (done) return; }
                    break;
                case BlockStmt b:
                    ReplacePlaceholder(b.Stmts, program, ref done);
                    if (done) return;
                    break;
            }
        }
    }

    // ================================================================ declarations: initialisation on block entry

    void EmitDeclInit(List<Decl> decls, Scope scope)
    {
        foreach (var d in decls)
        {
            switch (d)
            {
                case VarDecl vd:
                    foreach (var it in vd.Items)
                    {
                        if (dialect == Dialect.SimulaI && it.Const == null && vd.Type.Base == BaseType.Ref && vd.Type.RefClass == "set__"
                            && it.Pos.File != preludeFile && scope.Syms.TryGetValue(it.Name, out var setSym) && setSym is VarSym setVar)
                        {
                            // Simula I: a declared set exists from the start (empty)
                            var setPlan = TargetPrefix(new Ident { Name = it.Name, Orig = it.Orig, Pos = it.Pos }, true);
                            var st = EmitExpr(new NewExpr { Class = "set__", Orig = "set", Args = { new NoneLit { Pos = it.Pos } }, Pos = it.Pos });
                            AssignConvert(st, setPlan, it.Pos);
                            setPlan.Store(this, false);
                        }
                        if (it.Const == null) continue;
                        if (scope.Syms.TryGetValue(it.Name, out var s) && s is VarSym v)
                        {
                            var t = TypeOf(v);
                            var et = EmitExpr(it.Const);
                            if (t.B == BaseType.Text) { if (et.B != BaseType.Text) Err(it.Pos, "text constant expected"); E(Op.StT, v.Slot); }
                            else { Convert(et, t, it.Pos); E(Op.St, v.Slot); }
                        }
                    }
                    break;
                case ArrayDecl ad:
                    foreach (var seg in ad.Segments)
                    {
                        // own arrays are created once, at the first entry into their block
                        int ownSkip = -1;
                        if (ad.IsOwn && seg.Names.Count > 0 && scope.Syms.TryGetValue(seg.Names[0].Name, out var os0) && os0 is ArraySym oa0)
                        {
                            E(Op.LdG, oa0.Slot); E(Op.PushNone); E(Op.RefEq);
                            ownSkip = EJump(Op.Jf);
                        }
                        foreach (var bp in seg.Bounds)
                        {
                            CheckNoLocalRef(bp.Lower, scope);
                            CheckNoLocalRef(bp.Upper, scope);
                            ConvertToInt(EmitExpr(bp.Lower), bp.Lower.Pos);
                            ConvertToInt(EmitExpr(bp.Upper), bp.Upper.Pos);
                        }
                        ArraySym? first = null;
                        foreach (var n in seg.Names)
                        {
                            if (!scope.Syms.TryGetValue(n.Name, out var s) || s is not ArraySym a) continue;
                            var et = ElemOf(a);
                            if (first == null) E(Op.NewArr, (int)et.B, seg.Bounds.Count);
                            else E(Op.NewArrLike);
                            E(Op.Dup);
                            E(a.IsOwn ? Op.StG : Op.St, a.Slot);
                            first = a;
                        }
                        E(Op.Pop);
                        if (ownSkip >= 0) Patch(ownSkip);
                    }
                    break;
                case SwitchDecl sd:
                    if (scope.Syms.TryGetValue(sd.Name, out var ss) && ss is SwitchSym sw) EnsureSwitchUnit(sw);
                    break;
                case ProcDecl pd:
                    if (scope.Syms.TryGetValue(pd.Name, out var ps) && ps is ProcSym p) QueueProc(p);
                    break;
                case ClassDecl cd:
                    if (scope.Syms.TryGetValue(cd.Name, out var cs) && cs is ClassSym c) QueueClass(c);
                    break;
            }
        }
    }

    /// <summary>Array bounds and constants may not reference quantities of the same block.</summary>
    void CheckNoLocalRef(Expr e, Scope scope)
    {
        switch (e)
        {
            case Ident id:
                if (scope.Syms.TryGetValue(id.Name, out var s) && !(s is VarSym v && v.IsConst) && s is not ProcSym && s is not ParamSym)
                {
                    // in a class, formal parameters are allowed
                    Err(id.Pos, $"an array bound may not reference '{id.Orig}' declared in the same block");
                }
                break;
            case BinExpr b: CheckNoLocalRef(b.L, scope); CheckNoLocalRef(b.R, scope); break;
            case UnExpr u: CheckNoLocalRef(u.E, scope); break;
            case CallExpr c: foreach (var a in c.Args) CheckNoLocalRef(a, scope); break;
            case IfExpr ie: CheckNoLocalRef(ie.Cond, scope); CheckNoLocalRef(ie.Then, scope); CheckNoLocalRef(ie.Else, scope); break;
        }
    }

    void QueueProc(ProcSym p)
    {
        if (p.Emitted) return;
        p.Emitted = true;
        pendingUnits.Enqueue(() => EmitProcUnit(p));
    }

    readonly HashSet<ClassSym> queuedClasses = new();
    void QueueClass(ClassSym c)
    {
        if (!queuedClasses.Add(c)) return;
        pendingUnits.Enqueue(() => EmitClassUnit(c));
        // body declarations: local procedures and classes
    }

    // ================================================================ units

    void EmitProcUnit(ProcSym p)
    {
        var u = NewUnit(p.Orig, p.Pos.File);
        u.Proc = p.Info;
        p.Info.Unit = u;
        var res = ResultOf(p);
        p.Info.ResultType = (byte)res.B;
        p.Info.ResultQual = res.Q?.Info;
        var ms = scopeOf[p.Decl];
        var old = BeginGen(u, p.Body, ms);
        // direct entry
        foreach (var ps in p.Params) ResolveParam(ps);
        foreach (var ps in p.Params)
            if (ps.Kind == ParamKind.Array && ps.Mode == ParamMode.Value)
            {
                E(Op.Ld, ps.Slot); E(Op.CopyArr); E(Op.St, ps.Slot);
            }
        var body = p.Decl.Body!;
        if (body is BlockStmt b && scopeOf.TryGetValue(b, out var bs) && bs == ms)
        {
            foreach (var l in b.Labels) RegisterLabel(l.Name, ms);
            EmitDeclInit(b.Decls, ms);
            foreach (var st in b.Stmts) EmitStmt(st);
        }
        else EmitStmt(body);
        MarkPos(body is BlockStmt bb && bb.EndPos.Line > 0 ? bb.EndPos : p.Pos);
        E(Op.Ret);
        // generic entry (call through a procedure parameter or a virtual)
        u.GenericEntry = Here;
        foreach (var ps in p.Params) EmitGenericParam(ps);
        E(Op.Jmp, 0);
        FinishUnit();
        u.NSlots = p.Body.NSlots;
        u.Vars.AddRange(p.Body.Syms.Values.Concat(ms.Syms.Values).Select(DebugVar).Where(v => v != null).Select(v => v!));
        g = old;
    }

    void EmitGenericParam(ParamSym ps)
    {
        if (ps.Mode == ParamMode.Name) return;
        var t = ParamType(ps);
        E(Op.Ld, ps.Slot);
        switch (ps.Kind)
        {
            case ParamKind.Simple:
                if (t.B == BaseType.Text)
                {
                    E(Op.EvalThunk, 0);
                    E(ps.Mode == ParamMode.Value ? Op.CopyT : Op.CloneT);
                }
                else if (t.IsArith)
                {
                    E(Op.EvalThunk, t.Arith);
                    if (t.B == BaseType.ShortInteger) E(Op.I2S);
                }
                else
                {
                    E(Op.EvalThunk, 0);
                    if (t.IsRef && t.Q != null) E(Op.ChkQual, t.Q.Info.Id);
                }
                break;
            default:
                E(Op.EvalThunk, (int)ps.Kind << 2);
                break;
        }
        E(Op.St, ps.Slot);
    }

    void EmitClassUnit(ClassSym c)
    {
        var u = NewUnit(c.IsPrefixedBlock ? c.Orig : "class " + c.Orig, c.Pos.File);
        u.Class = c.Info;
        c.Info.Body = u;
        var chain = c.ChainInnerToOuter().Where(k => !k.IsRoot).Reverse().ToList();
        var old = BeginGen(u, chain.Count > 0 ? chain[0].Body : c.Body, chain.Count > 0 ? chain[0].Body : c.Body);
        // initialisation: block heads of every level (outermost first)
        foreach (var k in chain)
        {
            g.Frame = k.Body; g.Scope = k.Body;
            foreach (var ps in k.Params) ResolveParam(ps);
            foreach (var ps in k.Params)
                if (ps.Kind == ParamKind.Array && ps.Spec.Mode == ParamMode.Value) { E(Op.Ld, ps.Slot); E(Op.CopyArr); E(Op.St, ps.Slot); }
            var decls = k.Decl?.Decls ?? k.PrefixedBlock!.Decls;
            EmitDeclInit(decls, k.Body);
        }
        if (chain.Count > 0) EmitClassLevel(chain, 0);
        MarkPos(c.Decl?.EndPos ?? c.PrefixedBlock?.EndPos ?? c.Pos);
        E(Op.EndObj);
        FinishUnit();
        u.NSlots = c.SlotEnd;
        u.Vars.AddRange(c.Info.Attrs);
        g = old;
    }

    void EmitClassLevel(List<ClassSym> chain, int i)
    {
        var k = chain[i];
        g.Frame = k.Body; g.Scope = k.Body;
        if (k.IsPrefixedBlock)
        {
            foreach (var st in k.PrefixedBlock!.Stmts) EmitStmt(st);
            return;
        }
        var cd = k.Decl!;
        foreach (var st in cd.Before) EmitStmt(st);
        if (i + 1 < chain.Count) EmitClassLevel(chain, i + 1);
        g.Frame = k.Body; g.Scope = k.Body;
        foreach (var st in cd.After) EmitStmt(st);
    }

    void EnsureSwitchUnit(SwitchSym sw)
    {
        if (sw.Unit != null) return;
        var u = NewUnit("switch " + sw.Orig, sw.Pos.File);
        u.IsSwitch = true;
        sw.Unit = u;
        var fs = new FrameScope { Parent = sw.Scope, Kind = FKind.Switch, Depth = sw.Frame.Depth + 1, NSlots = 1, Label = "switch " + sw.Orig };
        sw.UnitScope = fs;
        var old = BeginGen(u, fs, fs);
        int i = 1;
        foreach (var item in sw.Decl.Items)
        {
            E(Op.Ld, 0); E(Op.PushI, i); E(Op.CmpI, (int)T.Eq);
            var j = EJump(Op.Jf);
            EmitDesignational(item);
            E(Op.RetSwitch);
            Patch(j);
            i++;
        }
        MarkPos(sw.Pos);
        E(Op.Ld, 0);
        E(Op.Native, Natives.Id("__switcherror"), 1);
        E(Op.RetSwitch);
        FinishUnit();
        u.NSlots = 1;
        g = old;
        // virtual entries
        foreach (var c in prog.Classes) { }
        if (sw.Matches != null)
            PatchSwitchVirtual(sw);
    }

    void PatchSwitchVirtual(SwitchSym sw)
    {
        var owner = sw.OwnerClass;
        if (owner == null) return;
        void Walk(ClassSym c)
        {
            var e = c.Info.VTable[sw.Matches!.Index];
            // a subclass that redefines it keeps its own entry
            if (c == owner || (e.SwitchUnit == null && e.Proc == null && e.LabelId < 0))
                c.Info.VTable[sw.Matches.Index] = new VirtualEntry { SwitchUnit = sw.Unit };
            foreach (var s in c.Subclasses)
                if (!(s.Attrs.TryGetValue(sw.Name, out var o) && o is SwitchSym && o != sw)) Walk(s);
        }
        Walk(owner);
    }

    // ================================================================ statements

    void RegisterLabel(string name, Scope s)
    {
        if (s.Syms.TryGetValue(name, out var sym) && sym is LabelSym l)
            g.Unit.LabelPcs[l.Id] = Here;
    }

    void EmitLabels(Stmt st)
    {
        foreach (var l in st.Labels)
        {
            for (Scope? s = g.Scope; s != null; s = s.Parent)
            {
                if (s.Syms.TryGetValue(l.Name, out var sym) && sym is LabelSym ls && ls.Pos.Equals(l.Pos))
                {
                    g.Unit.LabelPcs[ls.Id] = Here;
                    break;
                }
                if (s is FrameScope) break;
            }
        }
    }

    void EmitStmt(Stmt st)
    {
        EmitLabels(st);
        switch (st)
        {
            case DummyStmt: break;
            case BlockStmt b: EmitBlock(b); break;
            case AssignStmt a: Line(a.Pos); EmitAssign(a); break;
            case ExprStmt es: Line(es.Pos); EmitExprStmt(es); break;
            case IfStmt i:
                {
                    Line(i.Pos);
                    EmitCond(i.Cond);
                    var j = EJump(Op.Jf);
                    EmitStmt(i.Then);
                    if (i.Else != null)
                    {
                        var j2 = EJump(Op.Jmp);
                        Patch(j);
                        EmitStmt(i.Else);
                        Patch(j2);
                    }
                    else Patch(j);
                    break;
                }
            case WhileStmt w:
                {
                    int top = Here;
                    Line(w.Pos);
                    EmitCond(w.Cond);
                    var j = EJump(Op.Jf);
                    EmitStmt(w.Body);
                    E(Op.Jmp, top);
                    Patch(j);
                    break;
                }
            case ForStmt f: Line(f.Pos); EmitFor(f); break;
            case GotoStmt gt: Line(gt.Pos); EmitGoto(gt.Target); break;
            case InspectStmt ins: Line(ins.Pos); EmitInspect(ins); break;
            case ActivateStmt act: Line(act.Pos); EmitActivate(act); break;
            case InnerStmt inn: Err(inn.Pos, "misplaced 'inner'"); break;
            default: Err(st.Pos, "unsupported statement"); break;
        }
    }

    void EmitCond(Expr e)
    {
        var t = EmitExpr(e);
        if (t.B != BaseType.Boolean && !t.IsErr) Err(e.Pos, $"Boolean expression expected (found {t})");
    }

    void EmitBlock(BlockStmt b)
    {
        if (b.Prefix != null)
        {
            var c = prefixedClassOf[b];
            Line(b.Pos);
            EmitPrefixedBlock(b, c);
            return;
        }
        if (!scopeOf.TryGetValue(b, out var sc))
        {
            foreach (var st in b.Stmts) EmitStmt(st);
            return;
        }
        if (sc is MergedScope || sc == g.Scope)
        {
            foreach (var st in b.Stmts) EmitStmt(st);
            return;
        }
        var fs = (FrameScope)sc;
        int blockId = prog.Blocks.IndexOf(fs.Block!);
        E(Op.EnterBlk, blockId);
        var oldFrame = g.Frame; var oldScope = g.Scope;
        g.Frame = fs; g.Scope = fs;
        EmitDeclInit(b.Decls, fs);
        foreach (var st in b.Stmts) EmitStmt(st);
        g.Frame = oldFrame; g.Scope = oldScope;
        fs.Block!.NSlots = fs.NSlots;
        fs.Block.Vars.Clear();
        fs.Block.Vars.AddRange(fs.Syms.Values.Select(DebugVar).Where(v => v != null).Select(v => v!));
        MarkPos(b.EndPos);
        E(Op.ExitBlk);
    }

    void EmitPrefixedBlock(BlockStmt b, ClassSym c)
    {
        var pre = c.Prefix!;
        if (pre.UsesThis) Err(b.PrefixPos, $"class '{pre.Orig}' uses 'this' and cannot be used as a block prefix");
        QueueClass(c);
        // prefix parameters
        EmitClassArgs(c, b.PrefixArgs, b.PrefixPos);
        var hops = HopsTo(c.Scope.Frame);
        E(Op.NewBlk, c.Info.Id, hops, b.PrefixArgs.Count);
    }

    // ---------------------------------------------------------------- assignment

    void EmitAssign(AssignStmt a)
    {
        var plans = new List<Target>();
        foreach (var (t, isRef) in a.Targets) plans.Add(TargetPrefix(t, isRef));
        var vt = EmitExpr(a.Value);
        for (int i = plans.Count - 1; i >= 0; i--)
        {
            var p = plans[i];
            vt = AssignConvert(vt, p, a.Value.Pos);
            p.Store(this, keep: i > 0);
            vt = p.ResultType;
        }
    }

    // ---------------------------------------------------------------- expression statement

    void EmitExprStmt(ExprStmt es)
    {
        var e = es.E;
        switch (e)
        {
            case Ident or CallExpr or DotExpr:
                {
                    var t = EmitCallLike(e, wantResult: false, statement: true);
                    break;
                }
            case NewExpr ne:
                EmitNew(ne);
                E(Op.Pop);
                break;
            default:
                Err(e.Pos, "statement expected (procedure call, assignment or object generator)");
                break;
        }
    }

    // ---------------------------------------------------------------- for

    void EmitFor(ForStmt f)
    {
        var body = f.Body;
        var lscope = scopeOf[f];
        Expr cv = f.Var;
        if (cv is Ident cvi)
        {
            var found = Lookup(cvi.Name, g.Scope, cvi.Pos);
            if (found != null)
            {
                if (dialect == Dialect.Simula67 && found.Value.Sym is ParamSym pp && pp.IsName) Err(cv.Pos, "the controlled variable cannot be a parameter called by name");
                if (found.Value.Sym is ProcSym) Err(cv.Pos, "the controlled variable cannot be a procedure identifier");
                if (found.Value.Sym is VarSym vs && vs.IsConst) Err(cv.Pos, "the controlled variable cannot be a constant");
            }
        }
        else if (dialect == Dialect.Simula67 || cv is not CallExpr) { Err(f.Pos, "invalid controlled variable"); return; }
        bool multi = f.Elems.Count > 1;
        int kSlot = multi ? tempOf[f] : -1;
        var retFix = new List<int>();
        var bodyCalls = new List<int>();
        int bodyStart = -1;

        void EmitBodyInline()
        {
            var os = g.Scope;
            g.Scope = lscope;
            EmitStmt(body);
            g.Scope = os;
        }

        void CallBody()
        {
            if (!multi) { EmitBodyInline(); return; }
            E(Op.PushI, -1);
            retFix.Add(Here - 1);
            E(Op.St, kSlot);
            bodyCalls.Add(EJump(Op.Jmp));
            // return address
            g.Code[retFix[^1]] = Here;
        }

        foreach (var el in f.Elems)
        {
            switch (el.Kind)
            {
                case ForKind.Value:
                    {
                        AssignControlled(f, el.A);
                        CallBody();
                        break;
                    }
                case ForKind.While:
                    {
                        int top = Here;
                        AssignControlled(f, el.A);
                        EmitCond(el.B);
                        var j = EJump(Op.Jf);
                        CallBody();
                        E(Op.Jmp, top);
                        Patch(j);
                        break;
                    }
                case ForKind.StepUntil:
                    {
                        if (f.IsRef) { Err(el.Pos, "step ... until not allowed with ':-'"); break; }
                        int dSlot = tempOf[el];
                        long? stepConst = el.B is IntLit sil ? sil.Value
                            : el.B is UnExpr { Op: T.Minus, E: IntLit nil } ? -nil.Value : null;
                        if (stepConst is long stp && stp != 0)
                        {
                            // constant step: DELTA*(C-A3) <= 0 reduces to a comparison
                            AssignControlled(f, el.A);
                            int testc = Here;
                            var cvc = EmitExpr(cv);
                            var ltc = EmitExpr(el.C);
                            if (!ltc.IsArith && !ltc.IsErr) Err(el.C.Pos, "arithmetic limit expected");
                            int relc = (int)(stp > 0 ? T.Le : T.Ge);
                            if (cvc.IsReal || ltc.IsReal)
                            {
                                if (!ltc.IsReal) E(Op.I2R);
                                if (!cvc.IsReal) E(Op.I2RU);
                                E(Op.CmpR, relc);
                            }
                            else E(Op.CmpI, relc);
                            var jc = EJump(Op.Jf);
                            CallBody();
                            var planc = TargetPrefix(cv, false);
                            var cc = EmitExpr(cv);
                            E(Op.PushI, (int)stp);
                            SType sumc;
                            if (cc.IsReal) { E(Op.I2R); E(Op.AddR); sumc = SType.Real; }
                            else { E(Op.AddI); sumc = SType.Int; }
                            AssignConvert(sumc, planc, el.Pos);
                            planc.Store(this, false);
                            E(Op.Jmp, testc);
                            Patch(jc);
                            break;
                        }
                        // C := A
                        var ct = AssignControlled(f, el.A);
                        // DELTA := B
                        var bt = EmitExpr(el.B);
                        if (!bt.IsArith && !bt.IsErr) Err(el.B.Pos, "arithmetic step expected");
                        E(Op.St, dSlot);
                        int test = Here;
                        // DELTA*(C-A3) <= 0
                        E(Op.Ld, dSlot);
                        var cvt = EmitExpr(cv);
                        var lt = EmitExpr(el.C);
                        if (!lt.IsArith && !lt.IsErr) Err(el.C.Pos, "arithmetic limit expected");
                        int mask = (bt.IsReal ? 1 : 0) | (cvt.IsReal ? 2 : 0) | (lt.IsReal ? 4 : 0);
                        E(Op.StepTest, mask);
                        var j = EJump(Op.Jf);
                        CallBody();
                        // DELTA := B ; C := C + DELTA
                        EmitExpr(el.B);
                        E(Op.St, dSlot);
                        var plan = TargetPrefix(cv, false);
                        var c2 = EmitExpr(cv);
                        E(Op.Ld, dSlot);
                        SType sum;
                        if (c2.IsReal || bt.IsReal)
                        {
                            if (!bt.IsReal) E(Op.I2R);
                            if (!c2.IsReal) E(Op.I2RU);
                            E(Op.AddR);
                            sum = SType.Real;
                        }
                        else { E(Op.AddI); sum = SType.Int; }
                        AssignConvert(sum, plan, el.Pos);
                        plan.Store(this, false);
                        E(Op.Jmp, test);
                        Patch(j);
                        break;
                    }
            }
        }
        if (multi)
        {
            var jend = EJump(Op.Jmp);
            bodyStart = Here;
            EmitBodyInline();
            E(Op.JmpSlot, kSlot);
            Patch(jend);
            foreach (var bc in bodyCalls) PatchTo(bc, bodyStart);
        }
    }

    SType AssignControlled(ForStmt f, Expr value)
    {
        var plan = TargetPrefix(f.Var, f.IsRef);
        var vt = EmitExpr(value);
        AssignConvert(vt, plan, value.Pos);
        plan.Store(this, false);
        return plan.ResultType;
    }

    // ---------------------------------------------------------------- goto

    void EmitGoto(Expr target)
    {
        if (target is Ident id)
        {
            var f = Lookup(id.Name, g.Scope, id.Pos);
            if (f is { Sym: LabelSym ls } && ls.Matches == null)
            {
                var hops = HopsTo(ls.Frame);
                if (hops == 0 && f.Value.Conn == null && (UnitHasLabelScope(ls)))
                {
                    E(Op.Jmp, -1);
                    g.LabelFix.Add((Here - 1, ls.Id));
                    return;
                }
                if (f.Value.Conn == null)
                {
                    E(Op.Goto, ls.Id, hops);
                    return;
                }
            }
        }
        EmitDesignational(target);
        E(Op.GotoV);
    }

    /// <summary>Is the label emitted in the current unit (same runtime frame)?</summary>
    bool UnitHasLabelScope(LabelSym ls)
    {
        // same static frame: either the current frame or a level of the same concatenated class
        if (ls.Frame == g.Frame) return true;
        if (g.Unit.Class != null && ls.Frame.Kind is FKind.ClassBody or FKind.PrefixedBlock && ls.Frame.Class != null)
        {
            var cls = g.Unit.Class;
            return cls.IsSubclassOf(ls.Frame.Class.Info) && g.Frame.Kind is FKind.ClassBody or FKind.PrefixedBlock;
        }
        return false;
    }

    void EmitDesignational(Expr e)
    {
        switch (e)
        {
            case Ident id:
                {
                    var f = Lookup(id.Name, g.Scope, id.Pos);
                    if (f == null) { E(Op.PushNone); return; }
                    switch (f.Value.Sym)
                    {
                        case LabelSym ls:
                            if (ls.Matches != null) { EmitObjectOf(f.Value); E(Op.MkLabelVirt, ls.Matches.Index); return; }
                            if (f.Value.Conn != null) { Err(id.Pos, "a label cannot be accessed through connection"); E(Op.PushNone); return; }
                            E(Op.MkLabel, ls.Id, HopsTo(ls.Frame));
                            return;
                        case VirtualSym vs when vs.Kind == ParamKind.Label:
                            EmitObjectOf(f.Value); E(Op.MkLabelVirt, vs.Index); return;
                        case ParamSym ps when ps.Kind == ParamKind.Label:
                            ResolveParam(ps);
                            LoadParamRaw(f.Value, ps, 0);
                            return;
                        default:
                            Err(id.Pos, $"'{id.Orig}' is not a label");
                            E(Op.PushNone);
                            return;
                    }
                }
            case CallExpr ce when ce.Fn is Ident sid && ce.Args.Count == 1:
                {
                    var f = Lookup(sid.Name, g.Scope, sid.Pos);
                    if (f == null) { E(Op.PushNone); return; }
                    switch (f.Value.Sym)
                    {
                        case SwitchSym sw when sw.Matches == null && f.Value.Conn == null:
                            EnsureSwitchUnit(sw);
                            ConvertToInt(EmitExpr(ce.Args[0]), ce.Args[0].Pos);
                            E(Op.SwitchSelL, prog.Units.IndexOf(sw.Unit!), HopsTo(sw.Frame));
                            return;
                        case SwitchSym sw2 when sw2.Matches != null:
                            EmitObjectOf(f.Value); E(Op.MkSwitchVirt, sw2.Matches.Index);
                            ConvertToInt(EmitExpr(ce.Args[0]), ce.Args[0].Pos);
                            E(Op.SwitchSel);
                            return;
                        case VirtualSym vs when vs.Kind == ParamKind.Switch:
                            EmitObjectOf(f.Value); E(Op.MkSwitchVirt, vs.Index);
                            ConvertToInt(EmitExpr(ce.Args[0]), ce.Args[0].Pos);
                            E(Op.SwitchSel);
                            return;
                        case ParamSym ps when ps.Kind == ParamKind.Switch:
                            ResolveParam(ps);
                            LoadParamRaw(f.Value, ps, 0);
                            ConvertToInt(EmitExpr(ce.Args[0]), ce.Args[0].Pos);
                            E(Op.SwitchSel);
                            return;
                        default:
                            Err(sid.Pos, $"'{sid.Orig}' is not a switch");
                            E(Op.PushNone);
                            return;
                    }
                }
            case IfExpr ie:
                {
                    EmitCond(ie.Cond);
                    var j = EJump(Op.Jf);
                    EmitDesignational(ie.Then);
                    var j2 = EJump(Op.Jmp);
                    Patch(j);
                    EmitDesignational(ie.Else);
                    Patch(j2);
                    return;
                }
            default:
                Err(e.Pos, "designational expression (label or switch) expected");
                E(Op.PushNone);
                return;
        }
    }

    // ---------------------------------------------------------------- inspect

    void EmitInspect(InspectStmt ins)
    {
        if (dialect == Dialect.SimulaI && ins.Pos.File != preludeFile) ins.Obj = SimulaIProcess(ins.Obj);
        int temp = tempOf[ins];
        var ot = EmitExpr(ins.Obj);
        if (!ot.IsRef && !ot.IsErr) Err(ins.Obj.Pos, "inspect: object expression expected");
        StoreFrameSlot(temp, ins);
        var ends = new List<int>();
        if (ins.DoBody != null)
        {
            var cs = (ConnScope)scopeOf[ins];
            cs.Qual = ot.Q;
            if (ot.Q == null && ot.IsRef) Err(ins.Obj.Pos, "inspect none: unknown qualification");
            LoadFrameSlot(temp, ins);
            E(Op.PushNone);
            E(Op.RefEq);
            var jo = EJump(Op.Jt);
            var os = g.Scope;
            g.Scope = bodyScopeOf[ins];
            EmitStmt(ins.DoBody);
            g.Scope = os;
            ends.Add(EJump(Op.Jmp));
            Patch(jo);
        }
        else
        {
            foreach (var w in ins.Whens)
            {
                var cls = LookupClass(w.Class, g.Scope, w.Pos);
                var cs = (ConnScope)scopeOf[w];
                cs.Qual = cls;
                if (cls != null && ot.Q != null && !cls.IsSubclassOf(ot.Q) && !ot.Q.IsSubclassOf(cls))
                    Err(w.Pos, $"class '{cls.Orig}' is unrelated to the qualification {ot}");
                LoadFrameSlot(temp, ins);
                E(Op.In, cls?.Info.Id ?? 0);
                var jn = EJump(Op.Jf);
                var os = g.Scope;
                g.Scope = bodyScopeOf[w];
                EmitStmt(w.Body);
                g.Scope = os;
                ends.Add(EJump(Op.Jmp));
                Patch(jn);
            }
        }
        if (ins.Otherwise != null) EmitStmt(ins.Otherwise);
        foreach (var j in ends) Patch(j);
    }

    // temporaries are allocated in the frame containing the statement
    void StoreFrameSlot(int slot, object node) => E(Op.St, slot);
    void LoadFrameSlot(int slot, object node) => E(Op.Ld, slot);

    // ---------------------------------------------------------------- activate

    void EmitActivate(ActivateStmt a)
    {
        var f = Lookup("activat__", g.Scope, a.Pos, quiet: true);
        if (f == null || f.Value.Sym is not ProcSym p)
        {
            Err(a.Pos, "activation statement outside a block or class prefixed by 'simulation'");
            return;
        }
        var args = new List<Expr>
        {
            new BoolLit { Value = a.Reactivate, Pos = a.Pos },
            dialect == Dialect.SimulaI && a.Pos.File != preludeFile ? SimulaIProcess(a.Obj) : a.Obj,
            new TextLit { Value = a.Code, Pos = a.Pos },
            a.Time ?? new RealLit { Value = 0, Pos = a.Pos },
            a.Other == null ? new NoneLit { Pos = a.Pos } : dialect == Dialect.SimulaI && a.Pos.File != preludeFile ? SimulaIProcess(a.Other) : a.Other,
            new BoolLit { Value = a.Prior, Pos = a.Pos },
        };
        EmitProcCall(f.Value, p, args, a.Pos, wantResult: false);
    }
}
