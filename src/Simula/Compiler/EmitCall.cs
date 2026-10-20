using Studio.Simula.Runtime;

namespace Studio.Simula.Compiler;

public sealed partial class SimCompiler
{
    const int FlagWant = 1, FlagGeneric = 2;

    // ================================================================ identificateurs, appels, indices

    SType EmitCallLike(Expr e, bool wantResult, bool statement)
    {
        Expr fn; List<Expr> args;
        bool hasParens = false;
        switch (e)
        {
            case CallExpr ce: fn = ce.Fn; args = ce.Args; hasParens = true; break;
            default: fn = e; args = new List<Expr>(); break;
        }
        if (fn is DotExpr de) return EmitRemote(de, args, wantResult, statement, hasParens);
        if (fn is not Ident id) { Err(e.Pos, "appel invalide"); return SType.Err; }

        bool nativeCandidate = id.Name.StartsWith("__") && Natives.Exists(id.Name) && InPreludeCode();
        var found = Lookup(id.Name, g.Scope, id.Pos, quiet: nativeCandidate);
        if (found == null)
        {
            if (nativeCandidate) return EmitNative(id.Name, args, id.Pos, wantResult);
            foreach (var a in args) EmitExpr(a);
            return SType.Err;
        }
        var f = found.Value;
        switch (f.Sym)
        {
            case VarSym v:
                if (args.Count > 0 || hasParens) Err(id.Pos, $"'{id.Orig}' n'est ni un tableau ni une procedure");
                if (statement) Err(id.Pos, $"'{id.Orig}' : une variable n'est pas une instruction");
                if (f.Conn != null) { EmitObjectOf(f); E(Op.LdF, v.Slot); }
                else EmitLoadSlot(f.Frame, v.Slot);
                return TypeOf(v);
            case ParamSym ps:
                {
                    var t = ParamType(ps);
                    switch (ps.Kind)
                    {
                        case ParamKind.Simple:
                            if (args.Count > 0) Err(id.Pos, $"'{id.Orig}' n'est ni un tableau ni une procedure");
                            if (statement) Err(id.Pos, $"'{id.Orig}' : un parametre n'est pas une instruction");
                            LoadParamRaw(f, ps, t.Arith);
                            if (ps.IsName && t.B == BaseType.ShortInteger) E(Op.I2S);
                            return t;
                        case ParamKind.Array:
                            if (args.Count == 0) { Err(id.Pos, $"tableau '{id.Orig}' utilise sans indices"); return SType.Err; }
                            LoadParamRaw(f, ps, 0);
                            foreach (var a in args) ConvertToInt(EmitExpr(a), a.Pos);
                            E(Op.LdE, args.Count);
                            return t;
                        case ParamKind.Procedure:
                            {
                                LoadParamRaw(f, ps, 0);
                                foreach (var a in args) EmitGenericThunk(a);
                                if (wantResult && t == SType.Void) Err(id.Pos, $"la procedure '{id.Orig}' n'a pas de type et ne peut figurer dans une expression");
                                E(Op.CallP, args.Count, (wantResult ? FlagWant : 0) | FlagGeneric);
                                return t;
                            }
                        default:
                            Err(id.Pos, $"'{id.Orig}' ({ps.KindName}) ne peut figurer ici");
                            return SType.Err;
                    }
                }
            case ArraySym arr:
                {
                    if (args.Count == 0) { Err(id.Pos, $"tableau '{id.Orig}' utilise sans indices"); return SType.Err; }
                    if (args.Count != arr.Dims) Err(id.Pos, $"le tableau '{id.Orig}' a {arr.Dims} dimension(s), {args.Count} indice(s) donne(s)");
                    if (f.Conn != null) { EmitObjectOf(f); E(Op.LdF, arr.Slot); }
                    else EmitLoadSlot(f.Frame, arr.Slot);
                    foreach (var x in args) ConvertToInt(EmitExpr(x), x.Pos);
                    E(Op.LdE, args.Count);
                    return ElemOf(arr);
                }
            case ProcSym p:
                if (p.Intrinsic != null && f.Frame.Depth == 0) return EmitIntrinsic(p.Intrinsic, args, id.Pos, wantResult);
                return EmitProcCall(f, p, args, id.Pos, wantResult);
            case VirtualSym vs:
                if (vs.Kind != ParamKind.Procedure) { Err(id.Pos, $"'{id.Orig}' n'est pas une procedure"); return SType.Err; }
                EmitObjectOf(f);
                return EmitVirtualCall(vs, args, id.Pos, wantResult);
            case DetachSym:
                if (args.Count > 0) Err(id.Pos, "detach n'a pas de parametre");
                if (wantResult) Err(id.Pos, "detach n'a pas de valeur");
                EmitObjectOf(f);
                E(Op.Detach);
                return SType.Void;
            case ClassSym c:
                Err(id.Pos, $"'{id.Orig}' est une classe : utiliser 'new {id.Orig}'");
                return SType.Err;
            default:
                Err(id.Pos, $"'{id.Orig}' ({f.Sym.KindName}) ne peut figurer ici");
                return SType.Err;
        }
    }

    SType EmitRemote(DotExpr de, List<Expr> args, bool wantResult, bool statement, bool hasParens)
    {
        var ot = EmitExpr(de.Obj);
        if (ot.IsErr) { foreach (var x in args) EmitExpr(x); return SType.Err; }
        if (ot.B == BaseType.Text) return EmitTextMethod(de, args, wantResult);
        if (!ot.IsRef || ot.Q == null)
        {
            Err(de.Pos, ot.IsRef ? "acces distant sur 'none'" : $"acces distant sur une expression de type {ot}");
            return SType.Err;
        }
        var a = FindAttr(ot.Q, de.Name, remote: true, from: g.Scope);
        if (a == null) { Err(de.NamePos, $"'{de.Orig}' n'est pas un attribut visible de la classe {ot.Q.Orig}"); return SType.Err; }
        switch (a)
        {
            case VarSym v:
                if (args.Count > 0 || hasParens) Err(de.NamePos, $"'{de.Orig}' n'est ni un tableau ni une procedure");
                if (statement) Err(de.NamePos, "une variable n'est pas une instruction");
                E(Op.LdF, v.Slot);
                return TypeOf(v);
            case ParamSym ps:
                {
                    var t = ParamType(ps);
                    E(Op.LdF, ps.Slot);
                    if (ps.Kind == ParamKind.Array)
                    {
                        if (args.Count == 0) { Err(de.NamePos, "tableau utilise sans indices"); return SType.Err; }
                        foreach (var x in args) ConvertToInt(EmitExpr(x), x.Pos);
                        E(Op.LdE, args.Count);
                    }
                    else if (args.Count > 0) Err(de.NamePos, $"'{de.Orig}' n'est ni un tableau ni une procedure");
                    return t;
                }
            case ArraySym arr:
                if (args.Count == 0) { Err(de.NamePos, "tableau utilise sans indices"); return SType.Err; }
                E(Op.LdF, arr.Slot);
                foreach (var x in args) ConvertToInt(EmitExpr(x), x.Pos);
                E(Op.LdE, args.Count);
                return ElemOf(arr);
            case ProcSym p:
                {
                    QueueProc(p);
                    if (p.Matches != null) return EmitVirtualCall(p.Matches, args, de.NamePos, wantResult);
                    var res = ResultOf(p);
                    if (wantResult && res == SType.Void) Err(de.NamePos, $"la procedure '{de.Orig}' n'a pas de type");
                    EmitArgsFor(p, args, de.NamePos);
                    E(Op.CallR, p.Info.Id, args.Count, wantResult ? FlagWant : 0);
                    return res;
                }
            case VirtualSym vs:
                if (vs.Kind != ParamKind.Procedure) { Err(de.NamePos, $"'{de.Orig}' n'est pas une procedure"); return SType.Err; }
                return EmitVirtualCall(vs, args, de.NamePos, wantResult);
            case DetachSym:
                if (wantResult) Err(de.NamePos, "detach n'a pas de valeur");
                E(Op.Detach);
                return SType.Void;
            default:
                Err(de.NamePos, $"'{de.Orig}' ({a.KindName}) n'est pas accessible par la notation pointee");
                return SType.Err;
        }
    }

    void LoadParamRaw(Found f, ParamSym ps, int arith)
    {
        if (f.Conn != null) { EmitObjectOf(f); E(Op.LdF, ps.Slot); return; }
        if (ps.IsName) { E(Op.LdN, HopsTo(f.Frame), ps.Slot, arith | ((int)ps.Kind << 2)); return; }
        EmitLoadSlot(f.Frame, ps.Slot);
    }

    /// <summary>Empile un tableau designe par e. Faux si e ne designe pas un tableau.</summary>
    bool EmitArrayRef(Expr e, out SType elem, out int dims)
    {
        elem = SType.Err; dims = -1;
        switch (e)
        {
            case Ident id:
                {
                    var f = Lookup(id.Name, g.Scope, id.Pos);
                    if (f == null) return false;
                    var fv = f.Value;
                    if (fv.Sym is ArraySym a)
                    {
                        elem = ElemOf(a); dims = a.Dims;
                        if (fv.Conn != null) { EmitObjectOf(fv); E(Op.LdF, a.Slot); }
                        else EmitLoadSlot(fv.Frame, a.Slot);
                        return true;
                    }
                    if (fv.Sym is ParamSym ps && ps.Kind == ParamKind.Array)
                    {
                        elem = ParamType(ps);
                        LoadParamRaw(fv, ps, 0);
                        return true;
                    }
                    Err(id.Pos, $"'{id.Orig}' n'est pas un tableau");
                    return false;
                }
            case DotExpr de:
                {
                    var ot = EmitExpr(de.Obj);
                    if (!ot.IsRef || ot.Q == null) { Err(de.Pos, "objet attendu"); return false; }
                    var a = FindAttr(ot.Q, de.Name, true, g.Scope);
                    if (a is ArraySym arr) { elem = ElemOf(arr); dims = arr.Dims; E(Op.LdF, arr.Slot); return true; }
                    if (a is ParamSym ps && ps.Kind == ParamKind.Array) { elem = ParamType(ps); E(Op.LdF, ps.Slot); return true; }
                    Err(de.NamePos, $"'{de.Orig}' n'est pas un tableau");
                    return false;
                }
        }
        return false;
    }

    // ================================================================ appels de procedure

    SType EmitProcCall(Found f, ProcSym p, List<Expr> args, SrcPos pos, bool wantResult)
    {
        QueueProc(p);
        if (p.Matches != null)
        {
            EmitObjectOf(f);
            return EmitVirtualCall(p.Matches, args, pos, wantResult);
        }
        var res = ResultOf(p);
        if (wantResult && res == SType.Void) Err(pos, $"la procedure '{p.Orig}' n'a pas de type et ne peut figurer dans une expression");
        if (f.Conn != null)
        {
            EmitObjectOf(f);
            EmitArgsFor(p, args, pos);
            E(Op.CallR, p.Info.Id, args.Count, wantResult ? FlagWant : 0);
            return res;
        }
        EmitArgsFor(p, args, pos);
        E(Op.Call, p.Info.Id, HopsTo(f.Frame), args.Count, wantResult ? FlagWant : 0);
        return res;
    }

    void EmitArgsFor(ProcSym p, List<Expr> args, SrcPos pos)
    {
        if (args.Count != p.Params.Count)
        {
            Err(pos, $"'{p.Orig}' attend {p.Params.Count} parametre(s), {args.Count} fourni(s)");
            foreach (var a in args) EmitExpr(a);
            return;
        }
        for (int i = 0; i < args.Count; i++) EmitDirectArg(p.Params[i], args[i]);
    }

    SType EmitVirtualCall(VirtualSym v, List<Expr> args, SrcPos pos, bool wantResult)
    {
        var sig = VirtualProtocol(v);
        SType res;
        if (sig != null)
        {
            res = ResultOf(sig);
            if (args.Count != sig.Params.Count)
            {
                Err(pos, $"'{v.Orig}' attend {sig.Params.Count} parametre(s), {args.Count} fourni(s)");
                foreach (var a in args) EmitExpr(a);
            }
            else for (int i = 0; i < args.Count; i++) EmitDirectArg(sig.Params[i], args[i]);
            E(Op.CallV, v.Index, args.Count, wantResult ? FlagWant : 0);
        }
        else
        {
            res = VirtualType(v);
            foreach (var a in args) EmitGenericThunk(a);
            E(Op.CallV, v.Index, args.Count, (wantResult ? FlagWant : 0) | FlagGeneric);
        }
        if (wantResult && res == SType.Void) Err(pos, $"la procedure virtuelle '{v.Orig}' n'a pas de type");
        return res;
    }

    SType VirtualType(VirtualSym v)
    {
        if (v.Type != null) return v.Type;
        v.Type = v.Spec.Type == null ? SType.Void : ResolveSpec(v.Spec.Type, v.Scope.Parent ?? v.Scope);
        return v.Type;
    }

    /// <summary>Protocole d'appel d'un virtuel : entete commune si elle est connue, sinon null (appel generique).</summary>
    ProcSym? VirtualProtocol(VirtualSym v)
    {
        if (v.ProtocolDone) return v.DirectSig;
        v.ProtocolDone = true;
        if (v.Spec.Heading != null)
        {
            var h = new ProcSym { Name = v.Name, Orig = v.Orig, Pos = v.Pos, Decl = v.Spec.Heading, Info = new ProcInfo { Name = v.Orig } };
            var fs = new FrameScope { Parent = v.Scope, Kind = FKind.Procedure, Depth = v.Scope.Frame.Depth + 1, Proc = h };
            h.Scope = v.Scope;
            h.Body = fs;
            foreach (var spec in v.Spec.Heading.Params)
            {
                var ps = new ParamSym { Name = spec.Name, Orig = spec.Orig, Pos = spec.Pos, Spec = spec, Kind = spec.Kind, Scope = fs };
                h.Params.Add(ps);
            }
            v.DirectSig = h;
            // verification des correspondances
            foreach (var m in AllMatches(v))
                if (!SameHeading(m, h)) Err(m.Pos, $"'{m.Orig}' ne respecte pas la specification du virtuel");
            return h;
        }
        var matches = AllMatches(v).ToList();
        if (matches.Count == 0) return null;
        var first = matches[0];
        if (matches.All(m => SameHeading(m, first))) v.DirectSig = first;
        return v.DirectSig;
    }

    IEnumerable<ProcSym> AllMatches(VirtualSym v)
    {
        var seen = new HashSet<ProcSym>();
        var stack = new Stack<ClassSym>();
        if (v.OwnerClass != null) stack.Push(v.OwnerClass);
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            if (c.Attrs.TryGetValue(v.Name, out var s) && s is ProcSym p && p.Matches == v && seen.Add(p)) yield return p;
            foreach (var sub in c.Subclasses) stack.Push(sub);
        }
    }

    bool SameHeading(ProcSym a, ProcSym b)
    {
        if (a.Params.Count != b.Params.Count) return false;
        if (ResultOf(a).B != ResultOf(b).B) return false;
        for (int i = 0; i < a.Params.Count; i++)
        {
            var x = a.Params[i]; var y = b.Params[i];
            ResolveParam(x); ResolveParam(y);
            if (x.Kind != y.Kind || x.Mode != y.Mode || x.Type!.B != y.Type!.B || x.Type.Q != y.Type.Q) return false;
        }
        return true;
    }

    // ================================================================ parametres effectifs

    void EmitDirectArg(ParamSym formal, Expr actual)
    {
        ResolveParam(formal);
        var t = formal.Type!;
        switch (formal.Kind)
        {
            case ParamKind.Simple:
                if (formal.Mode == ParamMode.Name) { EmitThunkArg(actual, formal); return; }
                {
                    var at = EmitExpr(actual);
                    if (at.IsErr) return;
                    if (t.B == BaseType.Text)
                    {
                        if (at.B != BaseType.Text) { Err(actual.Pos, $"parametre '{formal.Orig}' : texte attendu (trouve {at})"); return; }
                        E(formal.Mode == ParamMode.Value ? Op.CopyT : Op.CloneT);
                        return;
                    }
                    if (t.IsArith)
                    {
                        if (!at.IsArith) { Err(actual.Pos, $"parametre '{formal.Orig}' : arithmetique attendu (trouve {at})"); return; }
                        Convert(at, t, actual.Pos);
                        return;
                    }
                    if (t.IsRef)
                    {
                        if (!at.IsRef) { Err(actual.Pos, $"parametre '{formal.Orig}' : reference attendue (trouve {at})"); return; }
                        if (at.Q != null && t.Q != null && !at.Q.IsSubclassOf(t.Q))
                        {
                            if (t.Q.IsSubclassOf(at.Q)) E(Op.ChkQual, t.Q.Info.Id);
                            else Err(actual.Pos, $"parametre '{formal.Orig}' : qualification {at} incompatible avec {t}");
                        }
                        return;
                    }
                    if (at.B != t.B) Err(actual.Pos, $"parametre '{formal.Orig}' : {t} attendu (trouve {at})");
                    return;
                }
            case ParamKind.Array:
                {
                    if (formal.Mode == ParamMode.Name) { EmitThunkArg(actual, formal); return; }
                    if (!EmitArrayRef(actual, out var elem, out var dims)) { Err(actual.Pos, $"parametre '{formal.Orig}' : tableau attendu"); return; }
                    CheckArrayElem(formal, t, elem, actual.Pos);
                    return;
                }
            case ParamKind.Procedure:
                if (formal.Mode == ParamMode.Name) { EmitThunkArg(actual, formal); return; }
                EmitProcValue(actual, t, formal.Orig);
                return;
            case ParamKind.Label:
                if (formal.Mode == ParamMode.Name) { EmitThunkArg(actual, formal); return; }
                EmitDesignational(actual);
                return;
            case ParamKind.Switch:
                if (formal.Mode == ParamMode.Name) { EmitThunkArg(actual, formal); return; }
                EmitSwitchValue(actual);
                return;
        }
    }

    void CheckArrayElem(ParamSym formal, SType t, SType elem, SrcPos pos)
    {
        if (elem.IsErr || t.IsErr) return;
        bool ok = t.B == elem.B || (t.IsRef && elem.IsRef) || (t.IsReal && elem.IsReal);
        if (t.IsRef && elem.IsRef && t.Q != null && elem.Q != null && !elem.Q.IsSubclassOf(t.Q)) ok = false;
        if (!ok) Err(pos, $"parametre '{formal.Orig}' : tableau de {t} attendu (trouve tableau de {elem})");
    }

    void EmitProcValue(Expr actual, SType formalType, string formalName)
    {
        switch (actual)
        {
            case Ident id:
                {
                    var f = Lookup(id.Name, g.Scope, id.Pos);
                    if (f == null) { E(Op.PushNone); return; }
                    var fv = f.Value;
                    switch (fv.Sym)
                    {
                        case ProcSym p:
                            QueueProc(p);
                            CheckProcResult(ResultOf(p), formalType, id.Pos, formalName);
                            if (p.Matches != null) { EmitObjectOf(fv); E(Op.MkProcV, p.Matches.Index); return; }
                            if (fv.Conn != null) { EmitObjectOf(fv); E(Op.MkProcR, p.Info.Id); return; }
                            E(Op.MkProc, p.Info.Id, HopsTo(fv.Frame));
                            return;
                        case VirtualSym vs when vs.Kind == ParamKind.Procedure:
                            EmitObjectOf(fv); E(Op.MkProcV, vs.Index); return;
                        case ParamSym ps when ps.Kind == ParamKind.Procedure:
                            CheckProcResult(ParamType(ps), formalType, id.Pos, formalName);
                            LoadParamRaw(fv, ps, 0);
                            return;
                        default:
                            Err(id.Pos, $"parametre '{formalName}' : procedure attendue");
                            E(Op.PushNone);
                            return;
                    }
                }
            case DotExpr de:
                {
                    var ot = EmitExpr(de.Obj);
                    if (!ot.IsRef || ot.Q == null) { Err(de.Pos, "objet attendu"); return; }
                    var a = FindAttr(ot.Q, de.Name, true, g.Scope);
                    if (a is ProcSym p)
                    {
                        QueueProc(p);
                        if (p.Matches != null) E(Op.MkProcV, p.Matches.Index);
                        else E(Op.MkProcR, p.Info.Id);
                        return;
                    }
                    if (a is VirtualSym vs && vs.Kind == ParamKind.Procedure) { E(Op.MkProcV, vs.Index); return; }
                    Err(de.NamePos, $"'{de.Orig}' n'est pas une procedure");
                    return;
                }
        }
        Err(actual.Pos, $"parametre '{formalName}' : identificateur de procedure attendu");
        E(Op.PushNone);
    }

    void CheckProcResult(SType actual, SType formal, SrcPos pos, string formalName)
    {
        if (formal == SType.Void || formal.IsErr || actual.IsErr) return;
        if (actual == SType.Void) { Err(pos, $"parametre '{formalName}' : procedure de type {formal} attendue"); return; }
        if (formal.IsArith && actual.IsArith) return;
        if (formal.IsRef && actual.IsRef && (formal.Q == null || actual.Q == null || actual.Q.IsSubclassOf(formal.Q))) return;
        if (formal.B != actual.B) Err(pos, $"parametre '{formalName}' : procedure de type {formal} attendue (trouve {actual})");
    }

    void EmitSwitchValue(Expr actual)
    {
        if (actual is Ident id)
        {
            var f = Lookup(id.Name, g.Scope, id.Pos);
            if (f == null) { E(Op.PushNone); return; }
            switch (f.Value.Sym)
            {
                case SwitchSym sw when sw.Matches == null:
                    EnsureSwitchUnit(sw);
                    E(Op.MkSwitch, prog.Units.IndexOf(sw.Unit!), HopsTo(sw.Frame));
                    return;
                case SwitchSym sw2:
                    EmitObjectOf(f.Value); E(Op.MkSwitchVirt, sw2.Matches!.Index); return;
                case VirtualSym vs when vs.Kind == ParamKind.Switch:
                    EmitObjectOf(f.Value); E(Op.MkSwitchVirt, vs.Index); return;
                case ParamSym ps when ps.Kind == ParamKind.Switch:
                    ResolveParam(ps);
                    LoadParamRaw(f.Value, ps, 0);
                    return;
            }
        }
        Err(actual.Pos, "identificateur d'aiguillage (switch) attendu");
        E(Op.PushNone);
    }

    // ================================================================ thunks (appel par nom, appel generique)

    void EmitThunkArg(Expr actual, ParamSym formal)
    {
        // transmission d'un parametre par nom a un autre parametre par nom : on passe le meme thunk
        if (actual is Ident pid)
        {
            var f = Lookup(pid.Name, g.Scope, pid.Pos, quiet: true);
            if (f is { Sym: ParamSym ps } && ps.IsName && f.Value.Conn == null && ps.Kind == formal.Kind)
            {
                ResolveParam(ps);
                if (formal.Kind != ParamKind.Simple || (ps.Type!.B == formal.Type!.B && ps.Type.Q == formal.Type.Q))
                {
                    EmitLoadSlot(f.Value.Frame, ps.Slot);
                    return;
                }
            }
        }
        BuildThunk(actual, formal.Kind, formal.Type!, formal.Orig);
    }

    void EmitGenericThunk(Expr actual)
    {
        // genre de l'effectif d'apres sa resolution
        ParamKind kind = ParamKind.Simple;
        if (actual is Ident id)
        {
            var f = Lookup(id.Name, g.Scope, id.Pos, quiet: true);
            if (f != null)
            {
                switch (f.Value.Sym)
                {
                    case ArraySym: kind = ParamKind.Array; break;
                    case ProcSym p: kind = ParamKind.Procedure; break;
                    case VirtualSym vs: kind = vs.Kind; break;
                    case LabelSym: kind = ParamKind.Label; break;
                    case SwitchSym: kind = ParamKind.Switch; break;
                    case ParamSym ps:
                        kind = ps.Kind;
                        if (ps.IsName && f.Value.Conn == null) { EmitLoadSlot(f.Value.Frame, ps.Slot); return; }
                        break;
                }
            }
        }
        else if (actual is DotExpr de)
        {
            var st = StaticTypeOf(de.Obj);
            if (st != null && st.IsRef && st.Q != null)
            {
                var a = FindAttr(st.Q, de.Name, true, g.Scope);
                if (a is ArraySym || (a is ParamSym aps && aps.Kind == ParamKind.Array)) kind = ParamKind.Array;
                else if (a is ProcSym || (a is VirtualSym avs && avs.Kind == ParamKind.Procedure)) kind = ParamKind.Procedure;
            }
        }
        BuildThunk(actual, kind, null, null);
    }

    static bool IsDesignator(Expr e) => e is Ident or DotExpr || (e is CallExpr);

    void BuildThunk(Expr actual, ParamKind kind, SType? formalType, string? formalName)
    {
        var u = NewUnit("parametre par nom", actual.Pos.File);
        u.IsThunk = true;
        var ts = new FrameScope { Parent = g.Scope, Kind = FKind.Thunk, Depth = g.Frame.Depth + 1, NSlots = 1, IsPrelude = g.Frame.IsPrelude, Label = "thunk" };
        var old = BeginGen(u, ts, ts);
        int setEntry = -1, altEntry = -1;
        byte arith = 0, atype = 0;
        int dims = -1, qual = -1;
        MarkPos(actual.Pos);
        switch (kind)
        {
            case ParamKind.Array:
                {
                    if (!EmitArrayRef(actual, out var elem, out dims)) { Err(actual.Pos, "tableau attendu"); E(Op.PushNone); }
                    else if (formalType != null && formalName != null)
                    {
                        // controle du type d'element
                        var fake = new ParamSym { Orig = formalName };
                        CheckArrayElem(fake, formalType, elem, actual.Pos);
                    }
                    atype = (byte)elem.B;
                    E(Op.RetThunk);
                    break;
                }
            case ParamKind.Procedure:
                {
                    EmitProcValue(actual, formalType ?? SType.Void, formalName ?? "?");
                    E(Op.RetThunk);
                    if (formalType == null)
                    {
                        // entree « valeur » : appel sans parametre (si le formel attend une valeur)
                        altEntry = Here;
                        var t = EmitCallLike(actual, wantResult: true, statement: false);
                        arith = t.Arith; atype = (byte)t.B; qual = t.Q?.Info.Id ?? -1;
                        E(Op.RetThunk);
                    }
                    break;
                }
            case ParamKind.Label:
                EmitDesignational(actual);
                E(Op.RetThunk);
                break;
            case ParamKind.Switch:
                EmitSwitchValue(actual);
                E(Op.RetThunk);
                break;
            default:
                {
                    var t = EmitExpr(actual);
                    if (formalType != null && !t.IsErr && !formalType.IsErr)
                    {
                        if (formalType.IsArith) { if (!t.IsArith) Err(actual.Pos, $"parametre '{formalName}' : arithmetique attendu (trouve {t})"); }
                        else if (formalType.IsRef)
                        {
                            if (!t.IsRef) Err(actual.Pos, $"parametre '{formalName}' : reference attendue");
                            else if (t.Q != null && formalType.Q != null && !t.Q.IsSubclassOf(formalType.Q))
                            {
                                if (formalType.Q.IsSubclassOf(t.Q)) E(Op.ChkQual, formalType.Q.Info.Id);
                                else Err(actual.Pos, $"parametre '{formalName}' : qualification incompatible");
                            }
                        }
                        else if (t.B != formalType.B) Err(actual.Pos, $"parametre '{formalName}' : {formalType} attendu (trouve {t})");
                    }
                    arith = t.Arith; atype = (byte)t.B; qual = t.Q?.Info.Id ?? -1;
                    E(Op.RetThunk);
                    // entree d'affectation
                    if (IsAssignable(actual))
                    {
                        int save = diags.Count;
                        setEntry = Here;
                        bool isRef = t.IsRef || t.B == BaseType.Text;
                        var plan = TargetPrefix(actual, isRef);
                        E(Op.Ld, 0);
                        if (plan.Type.B == BaseType.ShortInteger) E(Op.I2S);
                        if (plan.Type.IsRef && plan.Type.Q != null) E(Op.ChkQual, plan.Type.Q.Info.Id);
                        plan.Store(this, false);
                        E(Op.RetThunkSet);
                        // des erreurs ici signifient seulement « non affectable »
                        if (diags.Count > save) { diags.RemoveRange(save, diags.Count - save); setEntry = -1; }
                    }
                    break;
                }
        }
        FinishUnit();
        u.NSlots = 1;
        g = old;
        E(Op.MkThunk, prog.Units.IndexOf(u), setEntry, altEntry, arith, atype, (int)kind, qual);
    }

    bool IsAssignable(Expr e)
    {
        switch (e)
        {
            case Ident id:
                {
                    var f = Lookup(id.Name, g.Scope, id.Pos, quiet: true);
                    return f?.Sym switch
                    {
                        VarSym v => !v.IsConst,
                        ParamSym ps => ps.Kind == ParamKind.Simple,
                        _ => false
                    };
                }
            case CallExpr ce:
                {
                    if (ce.Fn is Ident fid)
                    {
                        var f = Lookup(fid.Name, g.Scope, fid.Pos, quiet: true);
                        return f?.Sym is ArraySym || (f?.Sym is ParamSym ps && ps.Kind == ParamKind.Array);
                    }
                    if (ce.Fn is DotExpr d)
                    {
                        var st = StaticTypeOf(d.Obj);
                        if (st != null && st.IsRef && st.Q != null)
                        {
                            var a = FindAttr(st.Q, d.Name, true, g.Scope);
                            return a is ArraySym || (a is ParamSym ps && ps.Kind == ParamKind.Array);
                        }
                    }
                    return false;
                }
            case DotExpr de:
                {
                    var st = StaticTypeOf(de.Obj);
                    if (st != null && st.IsRef && st.Q != null)
                    {
                        var a = FindAttr(st.Q, de.Name, true, g.Scope);
                        return a is VarSym v && !v.IsConst || (a is ParamSym ps && ps.Kind == ParamKind.Simple);
                    }
                    return false;
                }
        }
        return false;
    }

    // ================================================================ attributs des textes

    SType EmitTextMethod(DotExpr de, List<Expr> args, bool wantResult)
    {
        var name = de.Name;
        var rt = TextMethodType(name);
        if (rt == null) { Err(de.NamePos, $"'{de.Orig}' n'est pas un attribut de texte"); foreach (var a in args) EmitExpr(a); return SType.Err; }
        string sig = name switch
        {
            "setpos" or "putint" => "I",
            "putchar" => "C",
            "sub" or "putfrac" => "II",
            "putfix" or "putreal" => "RI",
            _ => ""
        };
        if (args.Count != sig.Length) { Err(de.NamePos, $"'{de.Orig}' attend {sig.Length} parametre(s)"); foreach (var a in args) EmitExpr(a); return rt; }
        for (int i = 0; i < args.Count; i++) EmitArgAs(sig[i], args[i]);
        E(Op.Native, Natives.Id("t_" + name), args.Count + 1);
        if (!wantResult && rt != SType.Void) E(Op.Pop);
        if (wantResult && rt == SType.Void) Err(de.NamePos, $"'{de.Orig}' n'a pas de valeur");
        return rt;
    }

    void EmitArgAs(char k, Expr a)
    {
        var t = EmitExpr(a);
        if (t.IsErr) return;
        switch (k)
        {
            case 'I': ConvertToInt(t, a.Pos); break;
            case 'R': if (!t.IsArith) Err(a.Pos, "arithmetique attendu"); else if (!t.IsReal) E(Op.I2R); break;
            case 'C': if (t.B != BaseType.Character) Err(a.Pos, $"caractere attendu (trouve {t})"); break;
            case 'B': if (t.B != BaseType.Boolean) Err(a.Pos, $"booleen attendu (trouve {t})"); break;
            case 'T': if (t.B != BaseType.Text) Err(a.Pos, $"texte attendu (trouve {t})"); break;
            case 'O': if (!t.IsRef) Err(a.Pos, "objet attendu"); break;
            case 'X': break;
        }
    }

    static SType SigType(char k) => k switch
    {
        'I' => SType.Int, 'R' => SType.Real, 'L' => SType.LReal, 'B' => SType.Bool, 'C' => SType.Char,
        'T' => SType.Text, 'O' => SType.NoneT, _ => SType.Void
    };

    SType EmitNative(string name, List<Expr> args, SrcPos pos, bool wantResult)
    {
        var sig = Natives.Signature(name);
        var parts = sig.Split('>');
        var ps = parts[0];
        if (args.Count != ps.Length) { Err(pos, $"{name} : {ps.Length} parametre(s) attendu(s)"); foreach (var a in args) EmitExpr(a); return SType.Err; }
        for (int i = 0; i < args.Count; i++)
        {
            if (ps[i] == 'A') { if (!EmitArrayRef(args[i], out _, out _)) Err(args[i].Pos, "tableau attendu"); }
            else EmitArgAs(ps[i], args[i]);
        }
        E(Op.Native, Natives.Id(name), args.Count);
        var rt = SigType(parts[1][0]);
        if (!wantResult && rt != SType.Void) E(Op.Pop);
        return rt;
    }

    // ================================================================ procedures standard traitees en ligne

    SType EmitIntrinsic(string name, List<Expr> args, SrcPos pos, bool wantResult)
    {
        SType Ret(SType t)
        {
            if (!wantResult && t != SType.Void) E(Op.Pop);
            if (wantResult && t == SType.Void) Err(pos, $"'{name}' n'a pas de valeur");
            return t;
        }
        bool Count(int n)
        {
            if (args.Count == n) return true;
            Err(pos, $"'{name}' attend {n} parametre(s), {args.Count} fourni(s)");
            foreach (var a in args) EmitExpr(a);
            return false;
        }
        int N(string n) => Natives.Id(n);
        switch (name)
        {
            case "abs":
                {
                    if (!Count(1)) return SType.Err;
                    var t = EmitExpr(args[0]);
                    if (!t.IsArith) { if (!t.IsErr) Err(pos, "abs : arithmetique attendu"); return SType.Err; }
                    E(Op.Native, N(t.IsReal ? "absr" : "absi"), 1);
                    return Ret(t.IsReal ? t : SType.Int);
                }
            case "sign":
                {
                    if (!Count(1)) return SType.Err;
                    var t = EmitExpr(args[0]);
                    if (!t.IsArith) { if (!t.IsErr) Err(pos, "sign : arithmetique attendu"); return SType.Err; }
                    E(Op.Native, N(t.IsReal ? "signr" : "signi"), 1);
                    return Ret(SType.Int);
                }
            case "entier":
                {
                    if (!Count(1)) return SType.Err;
                    var t = EmitExpr(args[0]);
                    if (!t.IsArith) { if (!t.IsErr) Err(pos, "entier : arithmetique attendu"); return SType.Err; }
                    if (t.IsReal) E(Op.Native, N("entier"), 1);
                    return Ret(SType.Int);
                }
            case "mod":
            case "rem":
                {
                    if (!Count(2)) return SType.Err;
                    var a = EmitExpr(args[0]); var b = EmitExpr(args[1]);
                    if ((!a.IsIntLike && !a.IsErr) || (!b.IsIntLike && !b.IsErr)) Err(pos, $"{name} : entiers attendus");
                    E(Op.Native, N(name), 2);
                    return Ret(SType.Int);
                }
            case "max":
            case "min":
                {
                    if (!Count(2)) return SType.Err;
                    var a = EmitExpr(args[0]);
                    int nopAt = Here; E(Op.Nop);
                    var b = EmitExpr(args[1]);
                    if (a.IsErr || b.IsErr) return SType.Err;
                    if (a.IsArith && b.IsArith)
                    {
                        var rt = ArithMax(a, b);
                        if (rt.IsReal)
                        {
                            if (!a.IsReal) g.Code[nopAt] = (int)Op.I2R;
                            if (!b.IsReal) E(Op.I2R);
                            E(Op.Native, N(name + "r"), 2);
                        }
                        else E(Op.Native, N(name + "i"), 2);
                        return Ret(rt);
                    }
                    if (a.B == BaseType.Character && b.B == BaseType.Character) { E(Op.Native, N(name + "i"), 2); return Ret(SType.Char); }
                    if (a.B == BaseType.Text && b.B == BaseType.Text) { E(Op.Native, N(name + "t"), 2); return Ret(SType.Text); }
                    Err(pos, $"{name} : types incompatibles {a} et {b}");
                    return SType.Err;
                }
            case "sqrt": case "sin": case "cos": case "tan": case "cotan": case "arcsin": case "arccos":
            case "arctan": case "sinh": case "cosh": case "tanh": case "ln": case "log10": case "exp":
            case "addepsilon": case "subepsilon":
                {
                    if (!Count(1)) return SType.Err;
                    var t = EmitExpr(args[0]);
                    if (!t.IsArith) { if (!t.IsErr) Err(pos, $"{name} : arithmetique attendu"); return SType.Err; }
                    if (!t.IsReal) E(Op.I2R);
                    E(Op.Native, N(name), 1);
                    return Ret(t.B == BaseType.LongReal ? SType.LReal : SType.Real);
                }
            case "arctan2":
                {
                    if (!Count(2)) return SType.Err;
                    EmitArgAs('R', args[0]); EmitArgAs('R', args[1]);
                    E(Op.Native, N("arctan2"), 2);
                    return Ret(SType.Real);
                }
            case "char": case "isochar":
                if (!Count(1)) return SType.Err;
                EmitArgAs('I', args[0]); E(Op.Native, N(name), 1); return Ret(SType.Char);
            case "rank": case "isorank":
                if (!Count(1)) return SType.Err;
                EmitArgAs('C', args[0]); E(Op.Native, N("rank"), 1); return Ret(SType.Int);
            case "digit": case "letter":
                if (!Count(1)) return SType.Err;
                EmitArgAs('C', args[0]); E(Op.Native, N(name), 1); return Ret(SType.Bool);
            case "lowten": case "decimalmark":
                if (!Count(1)) return SType.Err;
                EmitArgAs('C', args[0]); E(Op.Native, N(name), 1); return Ret(SType.Char);
            case "copy": case "upcase": case "lowcase":
                if (!Count(1)) return SType.Err;
                EmitArgAs('T', args[0]); E(Op.Native, N(name), 1); return Ret(SType.Text);
            case "blanks":
                if (!Count(1)) return SType.Err;
                EmitArgAs('I', args[0]); E(Op.Native, N("blanks"), 1); return Ret(SType.Text);
            case "call":
            case "resume":
                {
                    if (!Count(1)) return SType.Err;
                    var t = EmitExpr(args[0]);
                    if (!t.IsRef && !t.IsErr) Err(pos, $"{name} : objet attendu");
                    E(name == "call" ? Op.CallObj : Op.Resume);
                    return Ret(SType.Void);
                }
            case "sourceline":
                if (!Count(0)) return SType.Err;
                E(Op.PushI, pos.Line);
                return Ret(SType.Int);
            case "error":
                if (!Count(1)) return SType.Err;
                EmitArgAs('T', args[0]); E(Op.Native, N("error"), 1); return Ret(SType.Void);
            case "upperbound": case "lowerbound":
                {
                    if (!Count(2)) return SType.Err;
                    if (!EmitArrayRef(args[0], out _, out _)) Err(args[0].Pos, $"{name} : tableau attendu");
                    EmitArgAs('I', args[1]);
                    E(Op.Native, N(name), 2);
                    return Ret(SType.Int);
                }
            case "datetime":
                if (!Count(0)) return SType.Err;
                E(Op.Native, N("datetime"), 0); return Ret(SType.Text);
            case "cputime": case "clocktime":
                if (!Count(0)) return SType.Err;
                E(Op.Native, N(name), 0); return Ret(SType.LReal);
        }
        Err(pos, $"procedure standard '{name}' non geree");
        return SType.Err;
    }
}
