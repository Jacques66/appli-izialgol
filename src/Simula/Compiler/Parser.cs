namespace Studio.Simula.Compiler;

public sealed class ParseError : Exception
{
    public ParseError(SrcPos pos, string msg) : base(msg) { Pos = pos; }
    public SrcPos Pos { get; }
}

public sealed class Parser
{
    readonly List<Token> toks;
    readonly Action<SrcPos, string> error;
    int p;
    public bool InPrelude;

    public Parser(List<Token> tokens, Action<SrcPos, string> error)
    {
        toks = tokens;
        this.error = error;
    }

    Token Cur => toks[p];
    T K => toks[p].Kind;
    T KAt(int n) => p + n < toks.Count ? toks[p + n].Kind : T.EOF;
    Token Next() { var t = toks[p]; if (p < toks.Count - 1) p++; return t; }
    bool Accept(T k) { if (K == k) { Next(); return true; } return false; }

    Token Expect(T k, string? what = null)
    {
        if (K != k) throw new ParseError(Cur.Pos, $"{what ?? "'" + Lexer.Describe(k) + "'"} attendu, trouve '{Cur}'");
        return Next();
    }

    (string Name, string Orig, SrcPos Pos) ExpectIdent(string what = "identificateur")
    {
        if (K != T.Ident) throw new ParseError(Cur.Pos, $"{what} attendu, trouve '{Cur}'");
        var t = Next();
        return (t.Text, t.Original, t.Pos);
    }

    // ================================================================ programme

    /// <summary>Programme = [tete externe] instruction. Rend aussi les declarations externes.</summary>
    public (Stmt? Program, List<Decl> Externals, List<Decl> Modules) ParseProgram()
    {
        var externals = new List<Decl>();
        var modules = new List<Decl>();
        Stmt? prog = null;
        try
        {
            while (K == T.KExternal)
            {
                externals.Add(ParseExternal());
                Expect(T.Semi);
            }
            // module compile separement : declaration de classe ou de procedure
            if (StartsDeclaration())
            {
                while (K != T.EOF)
                {
                    if (!StartsDeclaration()) throw new ParseError(Cur.Pos, "declaration attendue dans un module");
                    modules.Add(ParseDeclaration());
                    if (!Accept(T.Semi)) break;
                    while (K == T.KExternal) { externals.Add(ParseExternal()); Expect(T.Semi); }
                }
            }
            else
            {
                prog = ParseStatement();
                while (Accept(T.Semi)) { }
            }
            if (K != T.EOF) error(Cur.Pos, $"texte inattendu apres la fin du programme : '{Cur}'");
        }
        catch (ParseError e) { error(e.Pos, e.Message); }
        return (prog, externals, modules);
    }

    // ================================================================ declarations

    bool StartsDeclaration()
    {
        switch (K)
        {
            case T.KInteger: case T.KReal: case T.KBoolean: case T.KCharacter: case T.KText:
            case T.KRef: case T.KShort: case T.KLong: case T.KArray: case T.KProcedure:
            case T.KClass: case T.KSwitch: case T.KExternal:
                return true;
            case T.Ident:
                return KAt(1) == T.KClass;
            default:
                return false;
        }
    }

    TypeSpec? TryParseType()
    {
        var pos = Cur.Pos;
        switch (K)
        {
            case T.KInteger: Next(); return TypeSpec.Of(BaseType.Integer, pos);
            case T.KReal: Next(); return TypeSpec.Of(BaseType.Real, pos);
            case T.KBoolean: Next(); return TypeSpec.Of(BaseType.Boolean, pos);
            case T.KCharacter: Next(); return TypeSpec.Of(BaseType.Character, pos);
            case T.KText: Next(); return TypeSpec.Of(BaseType.Text, pos);
            case T.KShort: Next(); Expect(T.KInteger, "'integer' apres 'short'"); return TypeSpec.Of(BaseType.ShortInteger, pos);
            case T.KLong: Next(); Expect(T.KReal, "'real' apres 'long'"); return TypeSpec.Of(BaseType.LongReal, pos);
            case T.KRef:
                {
                    Next();
                    Expect(T.LPar);
                    var c = ExpectIdent("nom de classe");
                    Expect(T.RPar);
                    return new TypeSpec { Base = BaseType.Ref, RefClass = c.Name, Pos = pos };
                }
            default: return null;
        }
    }

    Decl ParseDeclaration()
    {
        var pos = Cur.Pos;
        if (K == T.KExternal) return ParseExternal();
        if (K == T.KClass) return ParseClass(null, pos, SrcPos.None);
        if (K == T.Ident && KAt(1) == T.KClass)
        {
            var pre = Next();
            return ParseClass(pre.Text, pos, pre.Pos);
        }
        if (K == T.KSwitch)
        {
            Next();
            var id = ExpectIdent();
            Expect(T.Assign);
            var sd = new SwitchDecl { Name = id.Name, Orig = id.Orig, Pos = pos };
            do sd.Items.Add(ParseExpr()); while (Accept(T.Comma));
            return sd;
        }
        if (K == T.KProcedure) return ParseProcedure(null, pos);
        if (K == T.KArray) return ParseArrayDecl(TypeSpec.Of(BaseType.Real, pos), pos);
        var type = TryParseType() ?? throw new ParseError(Cur.Pos, "declaration attendue");
        if (K == T.KProcedure) return ParseProcedure(type, pos);
        if (K == T.KArray) return ParseArrayDecl(type, pos);
        var vd = new VarDecl { Type = type, Pos = pos };
        do
        {
            var id = ExpectIdent();
            Expr? cst = null;
            if (Accept(T.Eq)) cst = ParseExpr();
            vd.Items.Add((id.Name, id.Orig, id.Pos, cst));
        } while (Accept(T.Comma));
        return vd;
    }

    ArrayDecl ParseArrayDecl(TypeSpec type, SrcPos pos)
    {
        Expect(T.KArray);
        var ad = new ArrayDecl { Type = type, Pos = pos };
        do
        {
            var seg = new ArraySegment();
            do seg.Names.Add(ExpectIdent("nom de tableau")); while (Accept(T.Comma));
            Expect(T.LPar, "'(' (bornes du tableau)");
            do
            {
                var bp = new BoundPair { Lower = ParseExpr() };
                Expect(T.Colon, "':' entre les bornes");
                bp.Upper = ParseExpr();
                seg.Bounds.Add(bp);
            } while (Accept(T.Comma));
            Expect(T.RPar);
            ad.Segments.Add(seg);
        } while (Accept(T.Comma));
        return ad;
    }

    ExternalDecl ParseExternal()
    {
        var pos = Expect(T.KExternal).Pos;
        var ed = new ExternalDecl { Pos = pos };
        if (Accept(T.KClass)) ed.IsClass = true;
        else
        {
            if (K == T.Ident) ed.Kind = Next().Text;
            ed.Type = TryParseType();
            Expect(T.KProcedure);
        }
        do
        {
            var id = ExpectIdent();
            string? ident = null;
            if (Accept(T.Eq)) ident = Expect(T.TextLit, "chaine d'identification externe").Text;
            ed.Items.Add((id.Name, ident, id.Pos));
        } while (Accept(T.Comma));
        if (!ed.IsClass && Accept(T.KIs))
        {
            var ppos = Cur.Pos;
            var type = TryParseType();
            ed.Spec = ParseProcedure(type, ppos, specOnly: true);
            ed.Spec.IsExternalSpec = true;
            ed.Spec.NativeKind = ed.Kind;
        }
        return ed;
    }

    static bool IsSpecifierStart(T k) => k is T.KInteger or T.KReal or T.KBoolean or T.KCharacter or T.KText
        or T.KRef or T.KShort or T.KLong or T.KArray or T.KProcedure or T.KLabel or T.KSwitch;

    /// <summary>Partie parametres formels + modes + specifications. Rend la liste.</summary>
    List<ParamSpec> ParseFormalPart(bool forClass)
    {
        var ps = new List<ParamSpec>();
        if (!Accept(T.LPar)) return ps;
        do
        {
            var id = ExpectIdent("parametre formel");
            if (ps.Any(x => x.Name == id.Name)) error(id.Pos, $"parametre '{id.Orig}' repete");
            ps.Add(new ParamSpec { Name = id.Name, Orig = id.Orig, Pos = id.Pos });
        } while (Accept(T.Comma));
        Expect(T.RPar);
        Expect(T.Semi);
        // modes
        while (K == T.KValue || K == T.KName)
        {
            var isName = Next().Kind == T.KName;
            if (isName && forClass) error(Cur.Pos, "le mode 'name' n'existe pas pour les parametres de classe");
            do
            {
                var id = ExpectIdent();
                var spec = ps.FirstOrDefault(x => x.Name == id.Name);
                if (spec == null) error(id.Pos, $"'{id.Orig}' n'est pas un parametre formel");
                else if (spec.Mode != ParamMode.Default) error(id.Pos, $"mode de transmission de '{id.Orig}' deja donne");
                else spec.Mode = isName ? ParamMode.Name : ParamMode.Value;
            } while (Accept(T.Comma));
            Expect(T.Semi);
        }
        // specifications
        while (IsSpecifierStart(K))
        {
            // attention : "procedure" peut aussi etre un specificateur
            var spos = Cur.Pos;
            ParamKind kind = ParamKind.Simple;
            TypeSpec? type = null;
            if (Accept(T.KLabel)) kind = ParamKind.Label;
            else if (Accept(T.KSwitch)) kind = ParamKind.Switch;
            else if (Accept(T.KArray)) { kind = ParamKind.Array; type = TypeSpec.Of(BaseType.Real, spos); }
            else if (Accept(T.KProcedure)) kind = ParamKind.Procedure;
            else
            {
                type = TryParseType();
                if (Accept(T.KArray)) kind = ParamKind.Array;
                else if (Accept(T.KProcedure)) kind = ParamKind.Procedure;
            }
            do
            {
                var id = ExpectIdent("parametre formel");
                var spec = ps.FirstOrDefault(x => x.Name == id.Name);
                if (spec == null) error(id.Pos, $"'{id.Orig}' n'est pas un parametre formel");
                else if (spec.Specified) error(id.Pos, $"'{id.Orig}' specifie deux fois");
                else { spec.Specified = true; spec.Kind = kind; spec.Type = type; }
            } while (Accept(T.Comma));
            Expect(T.Semi);
        }
        foreach (var s in ps)
            if (!s.Specified) error(s.Pos, $"le parametre '{s.Orig}' n'est pas specifie");
        return ps;
    }

    ProcDecl ParseProcedure(TypeSpec? type, SrcPos pos, bool specOnly = false)
    {
        Expect(T.KProcedure);
        var id = ExpectIdent("nom de procedure");
        var pd = new ProcDecl { Name = id.Name, Orig = id.Orig, Type = type, Pos = pos };
        if (K == T.LPar) pd.Params = ParseFormalPart(false);
        else Expect(T.Semi);
        if (specOnly)
        {
            // corps vide : instruction vide
            pd.Body = new DummyStmt { Pos = Cur.Pos };
            return pd;
        }
        pd.Body = ParseStatement();
        return pd;
    }

    ClassDecl ParseClass(string? prefix, SrcPos pos, SrcPos prefixPos)
    {
        Expect(T.KClass);
        var id = ExpectIdent("nom de classe");
        var cd = new ClassDecl { Name = id.Name, Orig = id.Orig, Prefix = prefix, Pos = pos, PrefixPos = prefixPos, FromPrelude = InPrelude };
        if (K == T.LPar) cd.Params = ParseFormalPart(true);
        else Expect(T.Semi);
        // protection
        while (K == T.KHidden || K == T.KProtected)
        {
            bool hidden = false, prot = false;
            while (K == T.KHidden || K == T.KProtected)
            {
                if (Next().Kind == T.KHidden) hidden = true; else prot = true;
            }
            do
            {
                var n = ExpectIdent();
                if (hidden) cd.Hidden.Add((n.Name, n.Pos));
                if (prot) cd.Protected.Add((n.Name, n.Pos));
            } while (Accept(T.Comma));
            Expect(T.Semi);
        }
        // partie virtuelle
        if (K == T.KVirtual)
        {
            Next();
            Expect(T.Colon, "':' apres 'virtual'");
            while (K is T.KProcedure or T.KLabel or T.KSwitch or T.KInteger or T.KReal or T.KBoolean or T.KCharacter or T.KText or T.KRef or T.KShort or T.KLong)
            {
                var vpos = Cur.Pos;
                if (Accept(T.KLabel) || (K == T.KSwitch && Next() != null))
                {
                    var isLabel = toks[p - 1].Kind == T.KLabel;
                    do
                    {
                        var n = ExpectIdent();
                        cd.Virtuals.Add(new VirtualSpec { Name = n.Name, Orig = n.Orig, Pos = n.Pos, Kind = isLabel ? ParamKind.Label : ParamKind.Switch });
                    } while (Accept(T.Comma));
                    Expect(T.Semi);
                    continue;
                }
                var type = TryParseType();
                Expect(T.KProcedure, "'procedure' dans la partie virtuelle");
                var first = ExpectIdent();
                if (Accept(T.KIs))
                {
                    var hpos = Cur.Pos;
                    var htype = TryParseType();
                    var head = ParseProcedure(htype, hpos, specOnly: true);
                    if (head.Name != first.Name) error(head.Pos, $"la specification doit porter sur '{first.Orig}'");
                    if ((htype?.ToString() ?? "") != (type?.ToString() ?? "")) error(head.Pos, "type de la specification differente de celui du virtuel");
                    cd.Virtuals.Add(new VirtualSpec { Name = first.Name, Orig = first.Orig, Pos = first.Pos, IsProcedure = true, Type = type, Heading = head, Kind = ParamKind.Procedure });
                    Expect(T.Semi);
                    continue;
                }
                cd.Virtuals.Add(new VirtualSpec { Name = first.Name, Orig = first.Orig, Pos = first.Pos, IsProcedure = true, Type = type, Kind = ParamKind.Procedure });
                while (Accept(T.Comma))
                {
                    var n = ExpectIdent();
                    cd.Virtuals.Add(new VirtualSpec { Name = n.Name, Orig = n.Orig, Pos = n.Pos, IsProcedure = true, Type = type, Kind = ParamKind.Procedure });
                }
                Expect(T.Semi);
            }
        }
        // corps
        Stmt body;
        if (K == T.KBegin) body = ParseBlock(null, new List<Expr>(), Cur.Pos, SrcPos.None, allowInner: true);
        else body = ParseStatement(allowInner: true);
        if (body is BlockStmt b && b.Prefix == null)
        {
            cd.Decls = b.Decls;
            bool seenInner = false;
            foreach (var s in b.Stmts)
            {
                if (s is InnerStmt)
                {
                    if (seenInner) error(s.Pos, "un seul 'inner' par corps de classe");
                    seenInner = true;
                    cd.HasInner = true;
                    cd.InnerPos = s.Pos;
                    foreach (var l in s.Labels) cd.InnerLabels.Add(l.Name);
                    continue;
                }
                (seenInner ? cd.After : cd.Before).Add(s);
            }
            // les etiquettes de l'instruction inner sont rattachees a l'instruction suivante
            if (cd.HasInner && cd.InnerLabels.Count > 0)
            {
                var lbls = b.Stmts.First(s => s is InnerStmt).Labels;
                var holder = new DummyStmt { Pos = cd.InnerPos };
                holder.Labels.AddRange(lbls);
                cd.After.Insert(0, holder);
            }
            cd.EndPos = b.EndPos;
        }
        else
        {
            if (body is InnerStmt) { cd.HasInner = true; cd.InnerPos = body.Pos; }
            else if (body is not DummyStmt || body.Labels.Count > 0) cd.Before.Add(body);
            cd.EndPos = body.Pos;
        }
        return cd;
    }

    // ================================================================ instructions

    static bool EndsStatement(T k) => k is T.Semi or T.KEnd or T.KElse or T.KOtherwise or T.KWhen or T.EOF;

    Stmt ParseStatement(bool allowInner = false)
    {
        var labels = new List<(string, string, SrcPos)>();
        while (K == T.Ident && KAt(1) == T.Colon)
        {
            var t = Next();
            Next();
            labels.Add((t.Text, t.Original, t.Pos));
        }
        var st = ParseUnlabelled(allowInner);
        st.Labels.InsertRange(0, labels);
        return st;
    }

    Stmt ParseUnlabelled(bool allowInner)
    {
        var pos = Cur.Pos;
        switch (K)
        {
            case T.Semi: case T.KEnd: case T.KElse: case T.KOtherwise: case T.KWhen: case T.EOF:
                return new DummyStmt { Pos = pos };
            case T.KBegin:
                return ParseBlock(null, new List<Expr>(), pos, SrcPos.None, false);
            case T.KIf:
                {
                    Next();
                    var c = ParseExpr();
                    Expect(T.KThen);
                    var th = ParseStatement();
                    Stmt? el = null;
                    if (Accept(T.KElse)) el = ParseStatement();
                    return new IfStmt { Cond = c, Then = th, Else = el, Pos = pos };
                }
            case T.KWhile:
                {
                    Next();
                    var c = ParseExpr();
                    Expect(T.KDo);
                    return new WhileStmt { Cond = c, Body = ParseStatement(), Pos = pos };
                }
            case T.KFor: return ParseFor();
            case T.KGoto:
                Next();
                return new GotoStmt { Target = ParseExpr(), Pos = pos };
            case T.KGo:
                Next();
                Expect(T.KTo, "'to' apres 'go'");
                return new GotoStmt { Target = ParseExpr(), Pos = pos };
            case T.KInspect: return ParseInspect();
            case T.KInner:
                Next();
                if (!allowInner) error(pos, "'inner' n'est permis qu'au niveau du corps d'une classe");
                return new InnerStmt { Pos = pos };
            case T.KActivate: case T.KReactivate: return ParseActivate();
        }
        if (StartsDeclaration())
            throw new ParseError(pos, "declaration interdite ici (les declarations doivent preceder les instructions du bloc)");
        // expression : affectation, appel, generateur, bloc prefixe
        var e = ParseExpr();
        if (K == T.KBegin)
        {
            // bloc prefixe
            if (e is Ident id) return ParseBlock(id.Name, new List<Expr>(), pos, id.Pos, false);
            if (e is CallExpr ce && ce.Fn is Ident cid) return ParseBlock(cid.Name, ce.Args, pos, cid.Pos, false);
            throw new ParseError(Cur.Pos, "prefixe de bloc invalide");
        }
        if (K == T.Assign || K == T.Denote)
        {
            var asg = new AssignStmt { Pos = pos };
            var target = e;
            while (K == T.Assign || K == T.Denote)
            {
                var isRef = Next().Kind == T.Denote;
                asg.Targets.Add((target, isRef));
                target = ParseExpr();
            }
            asg.Value = target;
            return asg;
        }
        if (!EndsStatement(K)) throw new ParseError(Cur.Pos, $"';' attendu, trouve '{Cur}'");
        return new ExprStmt { E = e, Pos = pos };
    }

    BlockStmt ParseBlock(string? prefix, List<Expr> prefixArgs, SrcPos pos, SrcPos prefixPos, bool allowInner)
    {
        Expect(T.KBegin);
        var b = new BlockStmt { Pos = pos, Prefix = prefix, PrefixArgs = prefixArgs, PrefixPos = prefixPos };
        // declarations
        while (StartsDeclaration())
        {
            try
            {
                b.Decls.Add(ParseDeclaration());
                if (K != T.KEnd) Expect(T.Semi, "';' apres la declaration");
            }
            catch (ParseError e) { error(e.Pos, e.Message); Sync(); }
        }
        // instructions
        while (true)
        {
            try
            {
                if (K == T.KEnd) break;
                if (K == T.EOF) { error(Cur.Pos, "'end' attendu avant la fin du fichier"); return b; }
                var st = ParseStatement(allowInner);
                b.Stmts.Add(st);
                if (K == T.KEnd) break;
                if (K == T.EOF) { error(Cur.Pos, "'end' attendu avant la fin du fichier"); return b; }
                if (StartsDeclaration() && K != T.KExternal)
                    throw new ParseError(Cur.Pos, "';' attendu");
                Expect(T.Semi, "';' ou 'end'");
                if (StartsDeclaration())
                {
                    error(Cur.Pos, "declaration apres une instruction : en Simula les declarations doivent preceder les instructions du bloc");
                    b.Decls.Add(ParseDeclaration());
                    if (K != T.KEnd) Expect(T.Semi);
                }
            }
            catch (ParseError e) { error(e.Pos, e.Message); if (!Sync()) return b; }
        }
        b.EndPos = Cur.Pos;
        Expect(T.KEnd);
        return b;
    }

    /// <summary>Saute jusqu'a ';' (consomme) ou 'end' (non consomme). Faux en fin de fichier.</summary>
    bool Sync()
    {
        int depth = 0;
        while (K != T.EOF)
        {
            if (K == T.KBegin) depth++;
            if (K == T.KEnd) { if (depth == 0) return true; depth--; }
            if (K == T.Semi && depth == 0) { Next(); return true; }
            Next();
        }
        return false;
    }

    Stmt ParseFor()
    {
        var pos = Expect(T.KFor).Pos;
        var fs = new ForStmt { Pos = pos };
        var id = ExpectIdent("variable controlee");
        fs.Var = new Ident { Name = id.Name, Orig = id.Orig, Pos = id.Pos };
        if (Accept(T.Denote)) fs.IsRef = true;
        else Expect(T.Assign, "':=' ou ':-'");
        do
        {
            var epos = Cur.Pos;
            var a = ParseExpr();
            if (Accept(T.KStep))
            {
                var b = ParseExpr();
                Expect(T.KUntil, "'until'");
                var c = ParseExpr();
                fs.Elems.Add(new ForElem { Kind = ForKind.StepUntil, A = a, B = b, C = c, Pos = epos });
            }
            else if (Accept(T.KWhile))
                fs.Elems.Add(new ForElem { Kind = ForKind.While, A = a, B = ParseExpr(), Pos = epos });
            else
                fs.Elems.Add(new ForElem { Kind = ForKind.Value, A = a, Pos = epos });
        } while (Accept(T.Comma));
        Expect(T.KDo);
        fs.Body = ParseStatement();
        return fs;
    }

    Stmt ParseInspect()
    {
        var pos = Expect(T.KInspect).Pos;
        var ins = new InspectStmt { Pos = pos, Obj = ParseExpr() };
        if (Accept(T.KDo)) ins.DoBody = ParseStatement();
        else
        {
            if (K != T.KWhen) throw new ParseError(Cur.Pos, "'do' ou 'when' attendu apres inspect");
            while (K == T.KWhen)
            {
                var wpos = Next().Pos;
                var c = ExpectIdent("nom de classe");
                Expect(T.KDo);
                ins.Whens.Add(new WhenClause { Class = c.Name, Pos = c.Pos, Body = ParseStatement() });
            }
        }
        if (Accept(T.KOtherwise)) ins.Otherwise = ParseStatement();
        return ins;
    }

    Stmt ParseActivate()
    {
        var pos = Cur.Pos;
        var a = new ActivateStmt { Pos = pos, Reactivate = Next().Kind == T.KReactivate };
        a.Obj = ParseExpr();
        switch (K)
        {
            case T.KAt: case T.KDelay:
                a.Code = Next().Kind == T.KAt ? "at" : "delay";
                a.Time = ParseExpr();
                if (Accept(T.KPrior)) a.Prior = true;
                break;
            case T.KBefore: case T.KAfter:
                a.Code = Next().Kind == T.KBefore ? "before" : "after";
                a.Other = ParseExpr();
                break;
        }
        return a;
    }

    // ================================================================ expressions

    public Expr ParseExpr()
    {
        if (K == T.KIf)
        {
            var pos = Next().Pos;
            var c = ParseExpr();
            Expect(T.KThen);
            var th = ParseSimple();
            Expect(T.KElse, "'else' (expression conditionnelle)");
            var el = ParseExpr();
            return new IfExpr { Cond = c, Then = th, Else = el, Pos = pos };
        }
        return ParseSimple();
    }

    Expr ParseSimple() => ParseOrElse();

    Expr ParseOrElse()
    {
        var l = ParseAndThen();
        while (K == T.KOrElse) { var pos = Next().Pos; l = new BinExpr { Op = T.KOrElse, L = l, R = ParseAndThen(), Pos = pos }; }
        return l;
    }

    Expr ParseAndThen()
    {
        var l = ParseEqv();
        while (K == T.KAndThen) { var pos = Next().Pos; l = new BinExpr { Op = T.KAndThen, L = l, R = ParseEqv(), Pos = pos }; }
        return l;
    }

    Expr ParseEqv()
    {
        var l = ParseImp();
        while (K == T.KEqv) { var pos = Next().Pos; l = new BinExpr { Op = T.KEqv, L = l, R = ParseImp(), Pos = pos }; }
        return l;
    }

    Expr ParseImp()
    {
        var l = ParseOr();
        while (K == T.KImp) { var pos = Next().Pos; l = new BinExpr { Op = T.KImp, L = l, R = ParseOr(), Pos = pos }; }
        return l;
    }

    Expr ParseOr()
    {
        var l = ParseAnd();
        while (K == T.KOr) { var pos = Next().Pos; l = new BinExpr { Op = T.KOr, L = l, R = ParseAnd(), Pos = pos }; }
        return l;
    }

    Expr ParseAnd()
    {
        var l = ParseNot();
        while (K == T.KAnd) { var pos = Next().Pos; l = new BinExpr { Op = T.KAnd, L = l, R = ParseNot(), Pos = pos }; }
        return l;
    }

    Expr ParseNot()
    {
        if (K == T.KNot) { var pos = Next().Pos; return new UnExpr { Op = T.KNot, E = ParseNot(), Pos = pos }; }
        return ParseRelation();
    }

    Expr ParseRelation()
    {
        var l = ParseConcat();
        switch (K)
        {
            case T.Lt: case T.Le: case T.Eq: case T.Ge: case T.Gt: case T.Ne: case T.RefEq: case T.RefNe:
                {
                    var t = Next();
                    return new BinExpr { Op = t.Kind, L = l, R = ParseConcat(), Pos = t.Pos };
                }
            case T.KIs: case T.KIn:
                {
                    var t = Next();
                    var c = ExpectIdent("nom de classe");
                    return new IsInExpr { Obj = l, Class = c.Name, Orig = c.Orig, IsIn = t.Kind == T.KIn, Pos = t.Pos };
                }
        }
        return l;
    }

    Expr ParseConcat()
    {
        var l = ParseAdditive();
        while (K == T.Amp) { var pos = Next().Pos; l = new BinExpr { Op = T.Amp, L = l, R = ParseAdditive(), Pos = pos }; }
        return l;
    }

    Expr ParseAdditive()
    {
        Expr l;
        if (K == T.Plus || K == T.Minus)
        {
            var t = Next();
            l = new UnExpr { Op = t.Kind, E = ParseTerm(), Pos = t.Pos };
        }
        else l = ParseTerm();
        while (K == T.Plus || K == T.Minus)
        {
            var t = Next();
            l = new BinExpr { Op = t.Kind, L = l, R = ParseTerm(), Pos = t.Pos };
        }
        return l;
    }

    Expr ParseTerm()
    {
        var l = ParseFactor();
        while (K == T.Star || K == T.Slash || K == T.IntDiv)
        {
            var t = Next();
            l = new BinExpr { Op = t.Kind, L = l, R = ParseFactor(), Pos = t.Pos };
        }
        return l;
    }

    Expr ParseFactor()
    {
        var l = ParsePostfix();
        while (K == T.Power)
        {
            var t = Next();
            l = new BinExpr { Op = T.Power, L = l, R = ParsePostfix(), Pos = t.Pos };
        }
        return l;
    }

    Expr ParsePostfix()
    {
        var e = ParsePrimary();
        while (true)
        {
            if (K == T.Dot)
            {
                Next();
                var id = ExpectIdent("nom d'attribut");
                e = new DotExpr { Obj = e, Name = id.Name, Orig = id.Orig, NamePos = id.Pos, Pos = id.Pos };
            }
            else if (K == T.LPar && (e is Ident || e is DotExpr || e is CallExpr))
            {
                var pos = Next().Pos;
                var ce = new CallExpr { Fn = e, Pos = e.Pos };
                if (K != T.RPar)
                {
                    do ce.Args.Add(ParseExpr()); while (Accept(T.Comma));
                }
                Expect(T.RPar);
                e = ce;
            }
            else if (K == T.KQua)
            {
                var pos = Next().Pos;
                var c = ExpectIdent("nom de classe");
                e = new QuaExpr { Obj = e, Class = c.Name, Orig = c.Orig, Pos = pos };
            }
            else return e;
        }
    }

    Expr ParsePrimary()
    {
        var t = Cur;
        switch (t.Kind)
        {
            case T.IntLit: Next(); return new IntLit { Value = t.IntValue, Pos = t.Pos };
            case T.RealLit: Next(); return new RealLit { Value = t.RealValue, IsLong = t.IsLongReal, Pos = t.Pos };
            case T.CharLit: Next(); return new CharLit { Value = (char)t.IntValue, Pos = t.Pos };
            case T.TextLit: Next(); return t.Text.Length == 0 ? new NotextLit { Pos = t.Pos } : new TextLit { Value = t.Text, Pos = t.Pos };
            case T.KTrue: Next(); return new BoolLit { Value = true, Pos = t.Pos };
            case T.KFalse: Next(); return new BoolLit { Value = false, Pos = t.Pos };
            case T.KNone: Next(); return new NoneLit { Pos = t.Pos };
            case T.KNotext: Next(); return new NotextLit { Pos = t.Pos };
            case T.Ident: Next(); return new Ident { Name = t.Text, Orig = t.Original, Pos = t.Pos };
            case T.LPar:
                {
                    Next();
                    var e = ParseExpr();
                    Expect(T.RPar);
                    return e;
                }
            case T.KNew:
                {
                    Next();
                    var c = ExpectIdent("nom de classe");
                    var ne = new NewExpr { Class = c.Name, Orig = c.Orig, Pos = t.Pos };
                    if (Accept(T.LPar))
                    {
                        if (K != T.RPar) { do ne.Args.Add(ParseExpr()); while (Accept(T.Comma)); }
                        Expect(T.RPar);
                    }
                    return ne;
                }
            case T.KThis:
                {
                    Next();
                    var c = ExpectIdent("nom de classe");
                    return new ThisExpr { Class = c.Name, Orig = c.Orig, Pos = t.Pos };
                }
            case T.KIf:
                return ParseExpr();
        }
        throw new ParseError(t.Pos, $"expression attendue, trouve '{t}'");
    }
}
