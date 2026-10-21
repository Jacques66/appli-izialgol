using Studio.Simula.Runtime;

namespace Studio.Simula.Compiler;

public sealed partial class SimCompiler
{
    // ================================================================ assignment targets

    sealed class Target
    {
        public SType Type = SType.Err;
        public bool IsRef;
        public bool TextValue;          // := on a text
        public Action<bool> StoreFn = _ => { };
        public SType ResultType => Type;
        public void Store(SimCompiler c, bool keep) => StoreFn(keep);
    }

    void EmitLoadSlot(FrameScope frame, int slot)
    {
        int h = HopsTo(frame);
        if (h == -1) E(Op.LdG, slot);
        else if (h == 0) E(Op.Ld, slot);
        else E(Op.LdO, h, slot);
    }

    void EmitStoreSlot(FrameScope frame, int slot, bool text, bool keep)
    {
        if (keep) E(Op.Dup);
        int h = HopsTo(frame);
        if (h == -1) E(text ? Op.StTG : Op.StG, slot);
        else if (h == 0) E(text ? Op.StT : Op.St, slot);
        else E(text ? Op.StTO : Op.StO, h, slot);
    }

    /// <summary>Pushes the object holding the attribute found (connection or class frame).</summary>
    void EmitObjectOf(Found f)
    {
        if (f.Conn != null) EmitLoadSlot(f.Conn.Frame, f.Conn.TempSlot);
        else
        {
            int h = HopsTo(f.Frame);
            if (h == -1) E(Op.LdGThis);
            else E(Op.LdThis, h);
        }
    }

    Target TargetPrefix(Expr t, bool isRef)
    {
        var tg = new Target { IsRef = isRef };
        // text value assignment: the target may be any text expression
        if (!isRef)
        {
            var st = StaticTypeOf(t);
            if (st != null && st.B == BaseType.Text)
            {
                var tt = EmitExpr(t);
                tg.Type = SType.Text;
                tg.TextValue = true;
                tg.StoreFn = keep => E(keep ? Op.TAssignK : Op.TAssign);
                return tg;
            }
        }
        switch (t)
        {
            case Ident id:
                {
                    var f = Lookup(id.Name, g.Scope, id.Pos);
                    if (f == null) return tg;
                    var fv = f.Value;
                    switch (fv.Sym)
                    {
                        case VarSym v:
                            if (v.IsConst) Err(id.Pos, $"'{id.Orig}' is a constant");
                            tg.Type = TypeOf(v);
                            return SlotTarget(tg, fv, v.Slot);
                        case ParamSym ps when ps.Kind == ParamKind.Simple:
                            {
                                tg.Type = ParamType(ps);
                                if (ps.IsName)
                                {
                                    var arith = tg.Type.Arith;
                                    var frame = fv.Frame;
                                    if (fv.Conn != null) { Err(id.Pos, "name parameter not accessible through connection"); return tg; }
                                    tg.StoreFn = keep => E(keep ? Op.StNK : Op.StN, HopsTo(frame), ps.Slot, arith);
                                    return tg;
                                }
                                return SlotTarget(tg, fv, ps.Slot);
                            }
                        case ProcSym p:
                            {
                                // assignment to the result inside the procedure body
                                for (Scope? sc = g.Scope; sc != null; sc = sc.Parent)
                                {
                                    if (sc == p.Body)
                                    {
                                        tg.Type = ResultOf(p);
                                        if (tg.Type == SType.Void) { Err(id.Pos, $"'{id.Orig}' is a procedure without type"); return tg; }
                                        var frame = p.Body;
                                        var slot = p.Info.ResultSlot;
                                        bool text = tg.Type.B == BaseType.Text;
                                        tg.StoreFn = keep => EmitStoreSlot(frame, slot, text, keep);
                                        return tg;
                                    }
                                }
                                Err(id.Pos, $"assignment to procedure '{id.Orig}' outside its body");
                                return tg;
                            }
                        default:
                            Err(id.Pos, $"'{id.Orig}' ({fv.Sym.KindName}) cannot be assigned to");
                            return tg;
                    }
                }
            case CallExpr ce:
                {
                    // array element
                    var arr = EmitArrayRef(ce.Fn, out var elem, out var dims);
                    if (!arr) { Err(ce.Pos, "invalid target (array element expected)"); return tg; }
                    if (dims >= 0 && dims != ce.Args.Count) Err(ce.Pos, $"wrong number of subscripts ({ce.Args.Count} instead of {dims})");
                    foreach (var a in ce.Args) ConvertToInt(EmitExpr(a), a.Pos);
                    tg.Type = elem;
                    int n = ce.Args.Count;
                    bool text = elem.B == BaseType.Text;
                    tg.StoreFn = keep => E(text ? (keep ? Op.StTEK : Op.StTE) : (keep ? Op.StEK : Op.StE), n);
                    return tg;
                }
            case DotExpr de:
                {
                    var ot = EmitExpr(de.Obj);
                    if (!ot.IsRef || ot.Q == null) { if (!ot.IsErr) Err(de.Pos, "remote access on an expression that is not an object"); return tg; }
                    var a = FindAttr(ot.Q, de.Name, remote: true, from: g.Scope);
                    if (a == null) { Err(de.NamePos, $"'{de.Orig}' is not a visible attribute of {ot.Q.Orig}"); return tg; }
                    int slot;
                    switch (a)
                    {
                        case VarSym v:
                            if (v.IsConst) Err(de.NamePos, $"'{de.Orig}' is a constant");
                            tg.Type = TypeOf(v); slot = v.Slot; break;
                        case ParamSym ps when ps.Kind == ParamKind.Simple:
                            tg.Type = ParamType(ps); slot = ps.Slot; break;
                        default:
                            Err(de.NamePos, $"'{de.Orig}' cannot be assigned to");
                            return tg;
                    }
                    bool text = tg.Type.B == BaseType.Text;
                    tg.StoreFn = keep => E(text ? (keep ? Op.StTFK : Op.StTF) : (keep ? Op.StFK : Op.StF), slot);
                    return tg;
                }
            default:
                Err(t.Pos, "invalid assignment target");
                return tg;
        }
    }

    Target SlotTarget(Target tg, Found f, int slot)
    {
        bool text = tg.Type.B == BaseType.Text;
        if (f.Conn != null)
        {
            EmitLoadSlot(f.Conn.Frame, f.Conn.TempSlot);
            tg.StoreFn = keep => E(text ? (keep ? Op.StTFK : Op.StTF) : (keep ? Op.StFK : Op.StF), slot);
            return tg;
        }
        var frame = f.Frame;
        tg.StoreFn = keep => EmitStoreSlot(frame, slot, text, keep);
        return tg;
    }

    SType AssignConvert(SType from, Target t, SrcPos pos)
    {
        var to = t.Type;
        if (from.IsErr || to.IsErr) return to;
        if (t.IsRef)
        {
            if (to.B == BaseType.Text)
            {
                if (from.B != BaseType.Text) Err(pos, $"':-': text expected (found {from})");
                return to;
            }
            if (!to.IsRef) { Err(pos, $"':-' is reserved for references (target of type {to}: use ':=')"); return to; }
            Convert(from, to, pos);
            return to;
        }
        if (to.IsRef)
        {
            // Simula I has only one assignment operator for element pointers
            if (dialect == Dialect.SimulaI) { Convert(from, to, pos); return to; }
            Err(pos, "reference assignment: use ':-', not ':='");
            return to;
        }
        if (to.B == BaseType.Text)
        {
            if (from.B != BaseType.Text) Err(pos, $"text assignment: text expected (found {from})");
            return to;
        }
        Convert(from, to, pos);
        return to;
    }

    /// <summary>Static type of a designator without generating code (null if unknown).</summary>
    SType? StaticTypeOf(Expr e)
    {
        switch (e)
        {
            case Ident id:
                {
                    var f = Lookup(id.Name, g.Scope, id.Pos, quiet: true);
                    if (f == null) return null;
                    return f.Value.Sym switch
                    {
                        VarSym v => TypeOf(v),
                        ParamSym ps when ps.Kind == ParamKind.Simple => ParamType(ps),
                        ProcSym p => ResultOf(p),
                        ParamSym ps2 when ps2.Kind == ParamKind.Procedure => ParamType(ps2),
                        _ => null
                    };
                }
            case CallExpr ce:
                {
                    if (ce.Fn is DotExpr d)
                    {
                        var ot = StaticTypeOf(d.Obj) ?? (d.Obj is TextLit or NotextLit ? SType.Text : null);
                        if (ot != null && ot.B == BaseType.Text) return TextMethodType(d.Name);
                        if (ot != null && ot.IsRef && ot.Q != null)
                        {
                            var a = FindAttr(ot.Q, d.Name, true, g.Scope);
                            return a switch { ArraySym arr => ElemOf(arr), ProcSym p => ResultOf(p), ParamSym ps => ParamType(ps), _ => null };
                        }
                        return null;
                    }
                    if (ce.Fn is Ident fid)
                    {
                        var f = Lookup(fid.Name, g.Scope, fid.Pos, quiet: true);
                        if (f == null) return null;
                        return f.Value.Sym switch
                        {
                            ArraySym a => ElemOf(a),
                            ParamSym ps when ps.Kind is ParamKind.Array or ParamKind.Procedure => ParamType(ps),
                            ProcSym p => p.Intrinsic is "copy" or "blanks" or "upcase" or "lowcase" or "datetime" ? SType.Text : ResultOf(p),
                            _ => null
                        };
                    }
                    return null;
                }
            case DotExpr de:
                {
                    var ot = StaticTypeOf(de.Obj);
                    if (ot != null && ot.B == BaseType.Text) return TextMethodType(de.Name);
                    if (ot != null && ot.IsRef && ot.Q != null)
                    {
                        var a = FindAttr(ot.Q, de.Name, true, g.Scope);
                        return a switch { VarSym v => TypeOf(v), ParamSym ps => ParamType(ps), ProcSym p => ResultOf(p), _ => null };
                    }
                    return null;
                }
            case QuaExpr q:
                {
                    var c = Lookup(q.Class, g.Scope, q.Pos, quiet: true);
                    return c?.Sym is ClassSym cs ? SType.Ref(cs) : null;
                }
            case ThisExpr th:
                {
                    var c = Lookup(th.Class, g.Scope, th.Pos, quiet: true);
                    return c?.Sym is ClassSym cs ? SType.Ref(cs) : null;
                }
            case NewExpr ne:
                {
                    var c = Lookup(ne.Class, g.Scope, ne.Pos, quiet: true);
                    return c?.Sym is ClassSym cs ? SType.Ref(cs) : null;
                }
            case TextLit or NotextLit: return SType.Text;
        }
        return null;
    }

    static SType? TextMethodType(string name) => name switch
    {
        "sub" or "strip" or "main" => SType.Text,
        "length" or "pos" or "start" or "getint" or "getfrac" => SType.Int,
        "getreal" => SType.LReal,
        "more" or "constant" => SType.Bool,
        "getchar" => SType.Char,
        "setpos" or "putchar" or "putint" or "putfix" or "putreal" or "putfrac" => SType.Void,
        _ => null
    };

    // ================================================================ expressions

    SType EmitExpr(Expr e)
    {
        switch (e)
        {
            case IntLit il:
                if (il.Value > int.MaxValue) Err(il.Pos, "integer constant too large");
                E(Op.PushI, (int)il.Value);
                return SType.Int;
            case RealLit rl:
                E(Op.PushR, prog.Reals.Count);
                prog.Reals.Add(rl.Value);
                return rl.IsLong ? SType.LReal : SType.Real;
            case BoolLit bl: E(Op.PushI, bl.Value ? 1 : 0); return SType.Bool;
            case CharLit cl: E(Op.PushI, cl.Value); return SType.Char;
            case TextLit tl:
                E(Op.PushText, prog.Texts.Count);
                prog.Texts.Add(new TextObj(tl.Value, true));
                return SType.Text;
            case NotextLit: E(Op.PushNone); return SType.Text;
            case NoneLit: E(Op.PushNone); return SType.NoneT;
            case Ident or CallExpr or DotExpr:
                return EmitCallLike(e, wantResult: true, statement: false);
            case UnExpr ue: return EmitUnary(ue);
            case BinExpr be: return EmitBinary(be);
            case IfExpr ie: return EmitIfExpr(ie);
            case NewExpr ne:
                if (dialect == Dialect.SimulaI && !ne.ElementWrapped && ne.Pos.File != preludeFile && IsActivityClass(ne.Class))
                {
                    // Simula I: a generator yields an element pointer to the new process
                    ne.ElementWrapped = true;
                    return EmitExpr(SimulaICall("selfelem__", ne));
                }
                return EmitNew(ne);
            case ThisExpr te: return EmitThis(te);
            case QuaExpr qe:
                {
                    var ot = EmitExpr(qe.Obj);
                    var c = LookupClass(qe.Class, g.Scope, qe.Pos);
                    if (c == null) return SType.Err;
                    if (!ot.IsRef) { if (!ot.IsErr) Err(qe.Pos, "qua: object expression expected"); return SType.Err; }
                    if (ot.Q != null && !c.IsSubclassOf(ot.Q) && !ot.Q.IsSubclassOf(c))
                        Err(qe.Pos, $"qua: '{c.Orig}' is unrelated to the qualification {ot}");
                    E(Op.Qua, c.Info.Id);
                    return SType.Ref(c);
                }
            case IsInExpr ie2:
                {
                    var ot = EmitExpr(ie2.Obj);
                    var c = LookupClass(ie2.Class, g.Scope, ie2.Pos);
                    if (!ot.IsRef && !ot.IsErr) Err(ie2.Pos, $"'{(ie2.IsIn ? "in" : "is")}': object expression expected");
                    E(ie2.IsIn ? Op.In : Op.Is, c?.Info.Id ?? 0);
                    return SType.Bool;
                }
        }
        Err(e.Pos, "unsupported expression");
        return SType.Err;
    }

    SType EmitUnary(UnExpr ue)
    {
        var t = EmitExpr(ue.E);
        switch (ue.Op)
        {
            case T.KNot:
                if (t.B != BaseType.Boolean && !t.IsErr) Err(ue.Pos, "'not': Boolean expected");
                E(Op.Not);
                return SType.Bool;
            case T.Minus:
                if (!t.IsArith) { if (!t.IsErr) Err(ue.Pos, "'-': arithmetic value expected"); return SType.Err; }
                E(t.IsReal ? Op.NegR : Op.NegI);
                return t.IsReal ? t : SType.Int;
            default:
                if (!t.IsArith && !t.IsErr) Err(ue.Pos, "'+': arithmetic value expected");
                return t.IsReal ? t : (t.IsErr ? t : SType.Int);
        }
    }

    SType EmitBinary(BinExpr be)
    {
        switch (be.Op)
        {
            case T.KAndThen:
            case T.KOrElse:
                {
                    var lt = EmitExpr(be.L);
                    if (lt.B != BaseType.Boolean && !lt.IsErr) Err(be.L.Pos, "Boolean expected");
                    E(Op.Dup);
                    var j = EJump(be.Op == T.KAndThen ? Op.Jf : Op.Jt);
                    E(Op.Pop);
                    var rt = EmitExpr(be.R);
                    if (rt.B != BaseType.Boolean && !rt.IsErr) Err(be.R.Pos, "Boolean expected");
                    Patch(j);
                    return SType.Bool;
                }
            case T.Amp:
                {
                    var parts = new List<Expr>();
                    void Flat(Expr x) { if (x is BinExpr b && b.Op == T.Amp) { Flat(b.L); Flat(b.R); } else parts.Add(x); }
                    Flat(be);
                    foreach (var p in parts)
                    {
                        var t = EmitExpr(p);
                        if (t.B != BaseType.Text && !t.IsErr) Err(p.Pos, "'&': text expected");
                    }
                    E(Op.Concat, parts.Count);
                    return SType.Text;
                }
        }
        var l = EmitExpr(be.L);
        var r = EmitExpr(be.R);
        if (l.IsErr || r.IsErr) return be.Op is T.Lt or T.Le or T.Eq or T.Ge or T.Gt or T.Ne or T.RefEq or T.RefNe or T.KAnd or T.KOr or T.KImp or T.KEqv ? SType.Bool : SType.Err;
        switch (be.Op)
        {
            case T.Plus: case T.Minus: case T.Star:
                {
                    if (!l.IsArith || !r.IsArith) { Err(be.Pos, $"arithmetic operation on {l} and {r}"); return SType.Err; }
                    var rt = ArithMax(l, r);
                    if (rt.IsReal)
                    {
                        if (!r.IsReal) E(Op.I2R);
                        if (!l.IsReal) E(Op.I2RU);
                        E(be.Op == T.Plus ? Op.AddR : be.Op == T.Minus ? Op.SubR : Op.MulR);
                    }
                    else E(be.Op == T.Plus ? Op.AddI : be.Op == T.Minus ? Op.SubI : Op.MulI);
                    return rt;
                }
            case T.Slash:
                {
                    if (!l.IsArith || !r.IsArith) { Err(be.Pos, $"'/' on {l} and {r}"); return SType.Err; }
                    if (!r.IsReal) E(Op.I2R);
                    if (!l.IsReal) E(Op.I2RU);
                    E(Op.DivR);
                    return l.B == BaseType.LongReal || r.B == BaseType.LongReal ? SType.LReal : SType.Real;
                }
            case T.IntDiv:
                if (!l.IsIntLike || !r.IsIntLike) { Err(be.Pos, dialect == Dialect.Simula67 ? "'//' only accepts integers" : "integer division (div) only accepts integers"); return SType.Err; }
                E(Op.DivI);
                return SType.Int;
            case T.Power:
                {
                    if (!l.IsArith || !r.IsArith) { Err(be.Pos, $"'**' on {l} and {r}"); return SType.Err; }
                    if (l.IsIntLike && r.IsIntLike) { E(Op.PowII); return SType.Int; }
                    if (l.IsReal && r.IsIntLike) { E(Op.PowRI); return l; }
                    if (!l.IsReal) E(Op.I2RU);
                    E(Op.PowRR);
                    return l.B == BaseType.LongReal || r.B == BaseType.LongReal ? SType.LReal : SType.Real;
                }
            case T.Lt: case T.Le: case T.Eq: case T.Ge: case T.Gt: case T.Ne:
                {
                    int rel = (int)be.Op;
                    if (l.IsArith && r.IsArith)
                    {
                        if (l.IsReal || r.IsReal)
                        {
                            if (!r.IsReal) E(Op.I2R);
                            if (!l.IsReal) E(Op.I2RU);
                            E(Op.CmpR, rel);
                        }
                        else E(Op.CmpI, rel);
                    }
                    else if (l.B == BaseType.Character && r.B == BaseType.Character) E(Op.CmpI, rel);
                    else if (l.B == BaseType.Text && r.B == BaseType.Text) E(Op.CmpT, rel);
                    else if (l.B == BaseType.Boolean && r.B == BaseType.Boolean) { Err(be.Pos, "Boolean values cannot be compared with a relational operator (use eqv)"); E(Op.CmpI, rel); }
                    else if (l.IsRef && r.IsRef && be.Op is T.Eq or T.Ne)
                    {
                        if (dialect == Dialect.SimulaI)
                        {
                            // Simula I: two elements are equal when they represent the same process
                            E(Op.Native, Natives.Id("__s1_same"), 2);
                            if (be.Op == T.Ne) E(Op.Not);
                        }
                        else Err(be.Pos, "object comparison: use '==' or '=/='");
                    }
                    else Err(be.Pos, $"cannot compare {l} and {r}");
                    return SType.Bool;
                }
            case T.RefEq: case T.RefNe:
                {
                    if (l.IsRef && r.IsRef)
                    {
                        if (l.Q != null && r.Q != null && !l.Q.IsSubclassOf(r.Q) && !r.Q.IsSubclassOf(l.Q))
                            Err(be.Pos, $"comparison of unrelated references: {l} and {r}");
                        E(be.Op == T.RefEq ? Op.RefEq : Op.RefNe);
                    }
                    else if (l.B == BaseType.Text && r.B == BaseType.Text) E(be.Op == T.RefEq ? Op.TRefEq : Op.TRefNe);
                    else Err(be.Pos, $"'{(be.Op == T.RefEq ? "==" : "=/=")}': references expected ({l}, {r})");
                    return SType.Bool;
                }
            case T.KAnd: case T.KOr: case T.KImp: case T.KEqv:
                if (l.B != BaseType.Boolean || r.B != BaseType.Boolean) Err(be.Pos, "logical operator: Booleans expected");
                E(be.Op switch { T.KAnd => Op.And, T.KOr => Op.Or, T.KImp => Op.Imp, _ => Op.Eqv });
                return SType.Bool;
        }
        Err(be.Pos, "unsupported operator");
        return SType.Err;
    }

    SType EmitIfExpr(IfExpr ie)
    {
        EmitCond(ie.Cond);
        var j = EJump(Op.Jf);
        var t1 = EmitExpr(ie.Then);
        int nopAt = Here;
        E(Op.Nop);
        var j2 = EJump(Op.Jmp);
        Patch(j);
        var t2 = EmitExpr(ie.Else);
        SType rt;
        if (t1.IsErr || t2.IsErr) rt = SType.Err;
        else if (t1.IsArith && t2.IsArith)
        {
            rt = ArithMax(t1, t2);
            if (rt.IsReal)
            {
                if (!t1.IsReal) g.Code[nopAt] = (int)Op.I2R;
                if (!t2.IsReal) E(Op.I2R);
            }
        }
        else if (t1.IsRef && t2.IsRef)
        {
            if (t1.Q == null) rt = t2;
            else if (t2.Q == null) rt = t1;
            else
            {
                var c = CommonClass(t1.Q, t2.Q);
                if (c == null) { Err(ie.Pos, "conditional expression: qualifications without a common class"); rt = SType.Err; }
                else rt = SType.Ref(c);
            }
        }
        else if (t1.B == t2.B) rt = t1;
        else { Err(ie.Pos, $"conditional expression: incompatible types {t1} and {t2}"); rt = SType.Err; }
        if (rt.B == BaseType.Text) { g.Code[nopAt] = (int)Op.CloneT; E(Op.CloneT); }
        Patch(j2);
        return rt;
    }

    SType EmitThis(ThisExpr te)
    {
        var c = LookupClass(te.Class, g.Scope, te.Pos);
        if (c == null) return SType.Err;
        for (Scope? sc = g.Scope; sc != null; sc = sc.Parent)
        {
            if (sc is FrameScope fs && fs.Kind is FKind.ClassBody or FKind.PrefixedBlock && fs.Class != null && fs.Class.IsSubclassOf(c))
            {
                if (fs.Kind == FKind.ClassBody) MarkUsesThis(fs.Class, c);
                int h = HopsTo(fs);
                if (h == -1) E(Op.LdGThis); else E(Op.LdThis, h);
                return SType.Ref(c);
            }
            if (sc is ConnScope cs && cs.Qual != null && cs.Qual.IsSubclassOf(c))
            {
                EmitLoadSlot(cs.Frame, cs.TempSlot);
                return SType.Ref(c);
            }
        }
        Err(te.Pos, $"'this {te.Orig}' outside the class body or a connection block");
        return SType.Err;
    }

    static void MarkUsesThis(ClassSym body, ClassSym c)
    {
        for (var k = body; k != null; k = k.Prefix) { if (k == c) { k.UsesThis = true; break; } }
    }

    // ================================================================ object generators

    SType EmitNew(NewExpr ne)
    {
        var f = Lookup(ne.Class, g.Scope, ne.Pos);
        if (f == null) { foreach (var a in ne.Args) EmitExpr(a); return SType.Err; }
        if (f.Value.Sym is not ClassSym c) { Err(ne.Pos, $"'{ne.Orig}' is not a class"); return SType.Err; }
        if (c.IsRoot || c.IsPrefixedBlock) { Err(ne.Pos, "invalid class"); return SType.Err; }
        QueueClass(c);
        if (f.Value.Conn != null)
        {
            EmitLoadSlot(f.Value.Conn.Frame, f.Value.Conn.TempSlot);
            EmitClassArgs(c, ne.Args, ne.Pos);
            E(Op.NewS, c.Info.Id, ne.Args.Count);
        }
        else
        {
            // static frame: the block or object in which the class is declared
            var declFrame = f.Value.Frame;
            EmitClassArgs(c, ne.Args, ne.Pos);
            E(Op.New, c.Info.Id, HopsTo(declFrame), ne.Args.Count);
        }
        return SType.Ref(c);
    }

    void EmitClassArgs(ClassSym c, List<Expr> args, SrcPos pos)
    {
        var ps = c.AllParams;
        if (args.Count != ps.Count)
        {
            Err(pos, $"class '{c.Orig}' expects {ps.Count} parameter(s), {args.Count} given");
            foreach (var a in args) EmitExpr(a);
            return;
        }
        for (int i = 0; i < args.Count; i++) EmitDirectArg(ps[i], args[i]);
    }

    // ================================================================ Simula I

    bool IsActivityClass(string name)
    {
        if (!env.Syms.TryGetValue("activity__", out var a) || a is not ClassSym act) return false;
        var f = Lookup(name, g.Scope, SrcPos.None, quiet: true);
        return f?.Sym is ClassSym c && c.IsSubclassOf(act);
    }

    static CallExpr SimulaICall(string proc, Expr arg) =>
        new() { Fn = new Ident { Name = proc, Orig = proc, Pos = arg.Pos }, Args = { arg }, Pos = arg.Pos };

    /// <summary>Simula I: the process designated by an element pointer (none for none).</summary>
    Expr SimulaIProcess(Expr element) =>
        element is CallExpr { Fn: Ident { Name: "proc__" } } ? element : SimulaICall("proc__", element);
}
