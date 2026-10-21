using Studio.Simula.Runtime;

namespace Studio.Simula.Compiler;

public sealed partial class SimCompiler
{
    const int FlagWant = 1, FlagGeneric = 2;

    // ================================================================ identifiers, calls, subscripts

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
        if (fn is not Ident id) { Err(e.Pos, "invalid call"); return SType.Err; }

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
                if (args.Count > 0 || hasParens) Err(id.Pos, $"'{id.Orig}' is neither an array nor a procedure");
                if (statement) Err(id.Pos, $"'{id.Orig}': a variable is not a statement");
                if (f.Conn != null) { EmitObjectOf(f); E(Op.LdF, v.Slot); }
                else EmitLoadSlot(f.Frame, v.Slot);
                return TypeOf(v);
            case ParamSym ps:
                {
                    var t = ParamType(ps);
                    switch (ps.Kind)
                    {
                        case ParamKind.Simple:
                            if (args.Count > 0) Err(id.Pos, $"'{id.Orig}' is neither an array nor a procedure");
                            if (statement) Err(id.Pos, $"'{id.Orig}': a parameter is not a statement");
                            LoadParamRaw(f, ps, t.Arith);
                            if (ps.IsName && t.B == BaseType.ShortInteger) E(Op.I2S);
                            return t;
                        case ParamKind.Array:
                            if (args.Count == 0) { Err(id.Pos, $"array '{id.Orig}' used without subscripts"); return SType.Err; }
                            LoadParamRaw(f, ps, 0);
                            foreach (var a in args) ConvertToInt(EmitExpr(a), a.Pos);
                            E(Op.LdE, args.Count);
                            return t;
                        case ParamKind.Procedure:
                            {
                                LoadParamRaw(f, ps, 0);
                                foreach (var a in args) EmitGenericThunk(a);
                                if (wantResult && t == SType.Void) Err(id.Pos, $"procedure '{id.Orig}' has no type and cannot be used in an expression");
                                E(Op.CallP, args.Count, (wantResult ? FlagWant : 0) | FlagGeneric);
                                return t;
                            }
                        default:
                            Err(id.Pos, $"'{id.Orig}' ({ps.KindName}) cannot be used here");
                            return SType.Err;
                    }
                }
            case ArraySym arr:
                {
                    if (args.Count == 0) { Err(id.Pos, $"array '{id.Orig}' used without subscripts"); return SType.Err; }
                    if (args.Count != arr.Dims) Err(id.Pos, $"array '{id.Orig}' has {arr.Dims} dimension(s), {args.Count} subscript(s) given");
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
                if (vs.Kind != ParamKind.Procedure) { Err(id.Pos, $"'{id.Orig}' is not a procedure"); return SType.Err; }
                EmitObjectOf(f);
                return EmitVirtualCall(vs, args, id.Pos, wantResult);
            case DetachSym:
                if (args.Count > 0) Err(id.Pos, "detach has no parameter");
                if (wantResult) Err(id.Pos, "detach has no value");
                EmitObjectOf(f);
                E(Op.Detach);
                return SType.Void;
            case ClassSym c:
                Err(id.Pos, $"'{id.Orig}' is a class: use 'new {id.Orig}'");
                return SType.Err;
            default:
                Err(id.Pos, $"'{id.Orig}' ({f.Sym.KindName}) cannot be used here");
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
            Err(de.Pos, ot.IsRef ? "remote access through 'none'" : $"remote access on an expression of type {ot}");
            return SType.Err;
        }
        var a = FindAttr(ot.Q, de.Name, remote: true, from: g.Scope);
        if (a == null) { Err(de.NamePos, $"'{de.Orig}' is not a visible attribute of class {ot.Q.Orig}"); return SType.Err; }
        switch (a)
        {
            case VarSym v:
                if (args.Count > 0 || hasParens) Err(de.NamePos, $"'{de.Orig}' is neither an array nor a procedure");
                if (statement) Err(de.NamePos, "a variable is not a statement");
                E(Op.LdF, v.Slot);
                return TypeOf(v);
            case ParamSym ps:
                {
                    var t = ParamType(ps);
                    E(Op.LdF, ps.Slot);
                    if (ps.Kind == ParamKind.Array)
                    {
                        if (args.Count == 0) { Err(de.NamePos, "array used without subscripts"); return SType.Err; }
                        foreach (var x in args) ConvertToInt(EmitExpr(x), x.Pos);
                        E(Op.LdE, args.Count);
                    }
                    else if (args.Count > 0) Err(de.NamePos, $"'{de.Orig}' is neither an array nor a procedure");
                    return t;
                }
            case ArraySym arr:
                if (args.Count == 0) { Err(de.NamePos, "array used without subscripts"); return SType.Err; }
                E(Op.LdF, arr.Slot);
                foreach (var x in args) ConvertToInt(EmitExpr(x), x.Pos);
                E(Op.LdE, args.Count);
                return ElemOf(arr);
            case ProcSym p:
                {
                    QueueProc(p);
                    if (p.Matches != null) return EmitVirtualCall(p.Matches, args, de.NamePos, wantResult);
                    var res = ResultOf(p);
                    if (wantResult && res == SType.Void) Err(de.NamePos, $"procedure '{de.Orig}' has no type");
                    EmitArgsFor(p, args, de.NamePos);
                    E(Op.CallR, p.Info.Id, args.Count, wantResult ? FlagWant : 0);
                    return res;
                }
            case VirtualSym vs:
                if (vs.Kind != ParamKind.Procedure) { Err(de.NamePos, $"'{de.Orig}' is not a procedure"); return SType.Err; }
                return EmitVirtualCall(vs, args, de.NamePos, wantResult);
            case DetachSym:
                if (wantResult) Err(de.NamePos, "detach has no value");
                E(Op.Detach);
                return SType.Void;
            default:
                Err(de.NamePos, $"'{de.Orig}' ({a.KindName}) cannot be accessed by dot notation");
                return SType.Err;
        }
    }

    void LoadParamRaw(Found f, ParamSym ps, int arith)
    {
        if (f.Conn != null) { EmitObjectOf(f); E(Op.LdF, ps.Slot); return; }
        if (ps.IsName) { E(Op.LdN, HopsTo(f.Frame), ps.Slot, arith | ((int)ps.Kind << 2)); return; }
        EmitLoadSlot(f.Frame, ps.Slot);
    }

    /// <summary>Pushes the array designated by e. False if e does not designate an array.</summary>
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
                    Err(id.Pos, $"'{id.Orig}' is not an array");
                    return false;
                }
            case DotExpr de:
                {
                    var ot = EmitExpr(de.Obj);
                    if (!ot.IsRef || ot.Q == null) { Err(de.Pos, "object expected"); return false; }
                    var a = FindAttr(ot.Q, de.Name, true, g.Scope);
                    if (a is ArraySym arr) { elem = ElemOf(arr); dims = arr.Dims; E(Op.LdF, arr.Slot); return true; }
                    if (a is ParamSym ps && ps.Kind == ParamKind.Array) { elem = ParamType(ps); E(Op.LdF, ps.Slot); return true; }
                    Err(de.NamePos, $"'{de.Orig}' is not an array");
                    return false;
                }
        }
        return false;
    }

    // ================================================================ procedure calls

    SType EmitProcCall(Found f, ProcSym p, List<Expr> args, SrcPos pos, bool wantResult)
    {
        QueueProc(p);
        if (p.Matches != null)
        {
            EmitObjectOf(f);
            return EmitVirtualCall(p.Matches, args, pos, wantResult);
        }
        var res = ResultOf(p);
        if (wantResult && res == SType.Void) Err(pos, $"procedure '{p.Orig}' has no type and cannot be used in an expression");
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
            Err(pos, $"'{p.Orig}' expects {p.Params.Count} parameter(s), {args.Count} given");
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
                Err(pos, $"'{v.Orig}' expects {sig.Params.Count} parameter(s), {args.Count} given");
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
        if (wantResult && res == SType.Void) Err(pos, $"virtual procedure '{v.Orig}' has no type");
        return res;
    }

    SType VirtualType(VirtualSym v)
    {
        if (v.Type != null) return v.Type;
        v.Type = v.Spec.Type == null ? SType.Void : ResolveSpec(v.Spec.Type, v.Scope.Parent ?? v.Scope);
        return v.Type;
    }

    /// <summary>Call protocol of a virtual: the common heading when known, otherwise null (generic call).</summary>
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
            // check the matches
            foreach (var m in AllMatches(v))
                if (!SameHeading(m, h)) Err(m.Pos, $"'{m.Orig}' does not conform to the specification of the virtual");
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

    // ================================================================ actual parameters

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
                        if (at.B != BaseType.Text) { Err(actual.Pos, $"parameter '{formal.Orig}': text expected (found {at})"); return; }
                        E(formal.Mode == ParamMode.Value ? Op.CopyT : Op.CloneT);
                        return;
                    }
                    if (t.IsArith)
                    {
                        if (!at.IsArith) { Err(actual.Pos, $"parameter '{formal.Orig}': arithmetic value expected (found {at})"); return; }
                        Convert(at, t, actual.Pos);
                        return;
                    }
                    if (t.IsRef)
                    {
                        if (!at.IsRef) { Err(actual.Pos, $"parameter '{formal.Orig}': reference expected (found {at})"); return; }
                        if (at.Q != null && t.Q != null && !at.Q.IsSubclassOf(t.Q))
                        {
                            if (t.Q.IsSubclassOf(at.Q)) E(Op.ChkQual, t.Q.Info.Id);
                            else Err(actual.Pos, $"parameter '{formal.Orig}': qualification {at} is incompatible with {t}");
                        }
                        return;
                    }
                    if (at.B != t.B) Err(actual.Pos, $"parameter '{formal.Orig}': {t} expected (found {at})");
                    return;
                }
            case ParamKind.Array:
                {
                    if (formal.Mode == ParamMode.Name) { EmitThunkArg(actual, formal); return; }
                    if (!EmitArrayRef(actual, out var elem, out var dims)) { Err(actual.Pos, $"parameter '{formal.Orig}': array expected"); return; }
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
        if (!ok) Err(pos, $"parameter '{formal.Orig}': {t} array expected (found {elem} array)");
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
                            Err(id.Pos, $"parameter '{formalName}': procedure expected");
                            E(Op.PushNone);
                            return;
                    }
                }
            case DotExpr de:
                {
                    var ot = EmitExpr(de.Obj);
                    if (!ot.IsRef || ot.Q == null) { Err(de.Pos, "object expected"); return; }
                    var a = FindAttr(ot.Q, de.Name, true, g.Scope);
                    if (a is ProcSym p)
                    {
                        QueueProc(p);
                        if (p.Matches != null) E(Op.MkProcV, p.Matches.Index);
                        else E(Op.MkProcR, p.Info.Id);
                        return;
                    }
                    if (a is VirtualSym vs && vs.Kind == ParamKind.Procedure) { E(Op.MkProcV, vs.Index); return; }
                    Err(de.NamePos, $"'{de.Orig}' is not a procedure");
                    return;
                }
        }
        Err(actual.Pos, $"parameter '{formalName}': procedure identifier expected");
        E(Op.PushNone);
    }

    void CheckProcResult(SType actual, SType formal, SrcPos pos, string formalName)
    {
        if (formal == SType.Void || formal.IsErr || actual.IsErr) return;
        if (actual == SType.Void) { Err(pos, $"parameter '{formalName}': procedure of type {formal} expected"); return; }
        if (formal.IsArith && actual.IsArith) return;
        if (formal.IsRef && actual.IsRef && (formal.Q == null || actual.Q == null || actual.Q.IsSubclassOf(formal.Q))) return;
        if (formal.B != actual.B) Err(pos, $"parameter '{formalName}': procedure of type {formal} expected (found {actual})");
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
        Err(actual.Pos, "switch identifier expected");
        E(Op.PushNone);
    }

    // ================================================================ thunks (call by name, generic call)

    void EmitThunkArg(Expr actual, ParamSym formal)
    {
        // passing a name parameter on to another name parameter: pass the same thunk
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
        // kind of the actual parameter, from its resolution
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
        var u = NewUnit("name parameter", actual.Pos.File);
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
                    if (!EmitArrayRef(actual, out var elem, out dims)) { Err(actual.Pos, "array expected"); E(Op.PushNone); }
                    else if (formalType != null && formalName != null)
                    {
                        // check the element type
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
                        // "value" entry: parameterless call (when the formal expects a value)
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
                        if (formalType.IsArith) { if (!t.IsArith) Err(actual.Pos, $"parameter '{formalName}': arithmetic value expected (found {t})"); }
                        else if (formalType.IsRef)
                        {
                            if (!t.IsRef) Err(actual.Pos, $"parameter '{formalName}': reference expected");
                            else if (t.Q != null && formalType.Q != null && !t.Q.IsSubclassOf(formalType.Q))
                            {
                                if (formalType.Q.IsSubclassOf(t.Q)) E(Op.ChkQual, formalType.Q.Info.Id);
                                else Err(actual.Pos, $"parameter '{formalName}': incompatible qualification");
                            }
                        }
                        else if (t.B != formalType.B) Err(actual.Pos, $"parameter '{formalName}': {formalType} expected (found {t})");
                    }
                    arith = t.Arith; atype = (byte)t.B; qual = t.Q?.Info.Id ?? -1;
                    E(Op.RetThunk);
                    if (formalType == null && IsVisibleIntegerLabel(actual))
                    {
                        // generic call (formal procedure) of Algol 60: an unsigned integer that names a
                        // visible label is also usable as a label if the formal turns out to be one
                        altEntry = Here;
                        EmitDesignational(actual);
                        E(Op.RetThunk);
                    }
                    // assignment entry
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
                        // errors here only mean "not assignable"
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

    // ================================================================ text attributes

    SType EmitTextMethod(DotExpr de, List<Expr> args, bool wantResult)
    {
        var name = de.Name;
        var rt = TextMethodType(name);
        if (rt == null) { Err(de.NamePos, $"'{de.Orig}' is not a text attribute"); foreach (var a in args) EmitExpr(a); return SType.Err; }
        string sig = name switch
        {
            "setpos" or "putint" => "I",
            "putchar" => "C",
            "sub" or "putfrac" => "II",
            "putfix" or "putreal" => "RI",
            _ => ""
        };
        if (args.Count != sig.Length) { Err(de.NamePos, $"'{de.Orig}' expects {sig.Length} parameter(s)"); foreach (var a in args) EmitExpr(a); return rt; }
        for (int i = 0; i < args.Count; i++) EmitArgAs(sig[i], args[i]);
        E(Op.Native, Natives.Id("t_" + name), args.Count + 1);
        if (!wantResult && rt != SType.Void) E(Op.Pop);
        if (wantResult && rt == SType.Void) Err(de.NamePos, $"'{de.Orig}' has no value");
        return rt;
    }

    void EmitArgAs(char k, Expr a)
    {
        var t = EmitExpr(a);
        if (t.IsErr) return;
        switch (k)
        {
            case 'I': ConvertToInt(t, a.Pos); break;
            case 'R': if (!t.IsArith) Err(a.Pos, "arithmetic value expected"); else if (!t.IsReal) E(Op.I2R); break;
            case 'C': if (t.B != BaseType.Character) Err(a.Pos, $"character expected (found {t})"); break;
            case 'B': if (t.B != BaseType.Boolean) Err(a.Pos, $"Boolean expected (found {t})"); break;
            case 'T': if (t.B != BaseType.Text) Err(a.Pos, $"text expected (found {t})"); break;
            case 'O': if (!t.IsRef) Err(a.Pos, "object expected"); break;
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
        if (args.Count != ps.Length) { Err(pos, $"{name}: {ps.Length} parameter(s) expected"); foreach (var a in args) EmitExpr(a); return SType.Err; }
        for (int i = 0; i < args.Count; i++)
        {
            if (ps[i] == 'A') { if (!EmitArrayRef(args[i], out _, out _)) Err(args[i].Pos, "array expected"); }
            else EmitArgAs(ps[i], args[i]);
        }
        E(Op.Native, Natives.Id(name), args.Count);
        var rt = SigType(parts[1][0]);
        if (!wantResult && rt != SType.Void) E(Op.Pop);
        return rt;
    }

    // ================================================================ standard procedures compiled inline

    SType EmitIntrinsic(string name, List<Expr> args, SrcPos pos, bool wantResult)
    {
        SType Ret(SType t)
        {
            if (!wantResult && t != SType.Void) E(Op.Pop);
            if (wantResult && t == SType.Void) Err(pos, $"'{name}' has no value");
            return t;
        }
        bool Count(int n)
        {
            if (args.Count == n) return true;
            Err(pos, $"'{name}' expects {n} parameter(s), {args.Count} given");
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
                    if (!t.IsArith) { if (!t.IsErr) Err(pos, "abs: arithmetic value expected"); return SType.Err; }
                    E(Op.Native, N(t.IsReal ? "absr" : "absi"), 1);
                    return Ret(t.IsReal ? t : SType.Int);
                }
            case "sign":
                {
                    if (!Count(1)) return SType.Err;
                    var t = EmitExpr(args[0]);
                    if (!t.IsArith) { if (!t.IsErr) Err(pos, "sign: arithmetic value expected"); return SType.Err; }
                    E(Op.Native, N(t.IsReal ? "signr" : "signi"), 1);
                    return Ret(SType.Int);
                }
            case "entier":
                {
                    if (!Count(1)) return SType.Err;
                    var t = EmitExpr(args[0]);
                    if (!t.IsArith) { if (!t.IsErr) Err(pos, "entier: arithmetic value expected"); return SType.Err; }
                    if (t.IsReal) E(Op.Native, N("entier"), 1);
                    return Ret(SType.Int);
                }
            case "mod":
            case "rem":
                {
                    if (!Count(2)) return SType.Err;
                    var a = EmitExpr(args[0]); var b = EmitExpr(args[1]);
                    if ((!a.IsIntLike && !a.IsErr) || (!b.IsIntLike && !b.IsErr)) Err(pos, $"{name}: integers expected");
                    E(Op.Native, N(name), 2);
                    return Ret(SType.Int);
                }
            case "max":
            case "min":
                {
                    if (!Count(2)) return SType.Err;
                    var a = EmitExpr(args[0]);
                    var b = EmitExpr(args[1]);
                    if (a.IsErr || b.IsErr) return SType.Err;
                    if (a.IsArith && b.IsArith)
                    {
                        var rt = ArithMax(a, b);
                        if (rt.IsReal)
                        {
                            if (!b.IsReal) E(Op.I2R);
                            if (!a.IsReal) E(Op.I2RU);
                            E(Op.Native, N(name + "r"), 2);
                        }
                        else E(Op.Native, N(name + "i"), 2);
                        return Ret(rt);
                    }
                    if (a.B == BaseType.Character && b.B == BaseType.Character) { E(Op.Native, N(name + "i"), 2); return Ret(SType.Char); }
                    if (a.B == BaseType.Text && b.B == BaseType.Text) { E(Op.Native, N(name + "t"), 2); return Ret(SType.Text); }
                    Err(pos, $"{name}: incompatible types {a} and {b}");
                    return SType.Err;
                }
            case "sqrt": case "sin": case "cos": case "tan": case "cotan": case "arcsin": case "arccos":
            case "arctan": case "sinh": case "cosh": case "tanh": case "ln": case "log10": case "exp":
            case "addepsilon": case "subepsilon":
                {
                    if (!Count(1)) return SType.Err;
                    var t = EmitExpr(args[0]);
                    if (!t.IsArith) { if (!t.IsErr) Err(pos, $"{name}: arithmetic value expected"); return SType.Err; }
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
                    if (!t.IsRef && !t.IsErr) Err(pos, $"{name}: object expected");
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
                    if (!EmitArrayRef(args[0], out _, out _)) Err(args[0].Pos, $"{name}: array expected");
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
        Err(pos, $"unsupported standard procedure '{name}'");
        return SType.Err;
    }
}
