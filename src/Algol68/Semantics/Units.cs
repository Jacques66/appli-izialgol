using System.Globalization;
using System.Numerics;
using Studio.Algol68.Exec;
using Studio.Algol68.Runtime;
using Studio.Algol68.Syntax;

namespace Studio.Algol68.Semantics;

public sealed partial class Compiler
{
    readonly Dictionary<UnitAst, Mode?> apri = new(ReferenceEqualityComparer.Instance);
    string? pendingRoutineName;

    // ================================================================== modes a priori

    public Mode? Apriori(UnitAst u)
    {
        if (apri.TryGetValue(u, out var m)) return m;
        apri[u] = M.Any;
        m = ComputeApriori(u);
        apri[u] = m;
        return m;
    }

    Mode? SerialApriori(SerialAst s)
    {
        var r = RangeOf(s);
        return WithRange(r, () =>
        {
            var ys = new List<Mode?>();
            for (int i = 0; i < s.Phrases.Count; i++)
                if (s.Phrases[i] is LabeledUnit lu && (i == s.Phrases.Count - 1 || s.ExitAfter.Contains(i)))
                    ys.Add(Apriori(lu.Unit));
            if (ys.Count == 0) return M.Void;
            if (ys.Count == 1) return ys[0];
            return Balance(ys, s);
        });
    }

    Mode? ComputeApriori(UnitAst u)
    {
        switch (u)
        {
            case IntDenot d: return M.IntOf(Math.Max(0, d.Size));
            case RealDenot d: return M.RealOf(Math.Max(0, d.Size));
            case BitsDenot d: return M.BitsOf(Math.Max(0, d.Size));
            case StringDenot s: return s.Value.Length == 1 ? M.Char : M.RowChar;
            case BoolDenot: return M.Bool;
            case EmptyDenot: return M.Void;
            case NilAst: return M.Nil;
            case SkipAst: return M.Skip;
            case VacuumAst: return M.Vacuum;
            case JumpAst: return M.Jump;
            case IdentAst id:
                {
                    var s = LookupId(id.Name);
                    if (s != null) return s.Mode;
                    if (LookupLabel(id.Name) != null) return M.Jump;
                    return M.Any;
                }
            case ApplyAst a:
                {
                    var info = ClassifyApply(a, false);
                    return info.Result;
                }
            case SelectionAst sel:
                {
                    var info = ClassifySelection(sel, false);
                    return info.Result;
                }
            case GeneratorAst g: return M.Normalize(M.Ref(DeclMode(g.Declarer)));
            case CastAst c: return ValueMode(c.Declarer);
            case DyadicAst d:
                {
                    var op = IdentifyDyadic(d, false);
                    return op == null ? M.Any : op.Mode.Result;
                }
            case MonadicAst mo:
                {
                    var op = IdentifyMonadic(mo, false);
                    return op == null ? M.Any : op.Mode.Result;
                }
            case AssignAst asg:
                {
                    var a = Apriori(asg.Dest);
                    if (a == null) return M.Any;
                    var d = DeprocAll(a);
                    return d is RefMode ? d : M.Any;
                }
            case IdentityRelAst: return M.Bool;
            case RoutineTextAst rt: return RoutineHeaderMode(rt);
            case AndThenAst: return M.Bool;
            case AssertAst: return M.Void;
            case FormatTextAst: return M.Format;
            case LoopAst: return M.Void;
            case ParallelAst: return M.Void;
            case CollateralAst c:
                {
                    // un affichage n'a pas de mode a priori ; une collaterale de VOID est VOID
                    return null;
                }
            case ClosedAst c: return SerialApriori(c.Serial);
            case CondAst c:
                {
                    var r = RangeOf(c.If);
                    return WithRange(r, () =>
                    {
                        if (c.Brief && IsIntSelector(c.If)) return BalanceBranches(new List<Mode?> { SerialApriori(c.Then), ElseApriori(c.Else, c.ElifCond) }, c);
                        return BalanceBranches(new List<Mode?> { SerialApriori(c.Then), ElseApriori(c.Else, c.ElifCond) }, c);
                    });
                }
            case CaseAst c:
                {
                    var r = RangeOf(c.Selector);
                    return WithRange(r, () =>
                    {
                        var l = c.In.Select(Apriori).ToList();
                        l.Add(ElseApriori(c.Out, c.Ouse));
                        return BalanceBranches(l, c);
                    });
                }
            case ConformityAst c:
                {
                    var r = RangeOf(c.Selector);
                    return WithRange(r, () =>
                    {
                        var l = new List<Mode?>();
                        foreach (var cc in c.Cases) l.Add(WithRange(ConformityRange(cc), () => Apriori(cc.Unit)));
                        l.Add(ElseApriori(c.Out, c.Ouse));
                        return BalanceBranches(l, c);
                    });
                }
        }
        return M.Any;
    }

    Mode? ElseApriori(SerialAst? els, UnitAst? nested)
    {
        if (els != null) return SerialApriori(els);
        if (nested != null) return Apriori(nested);
        return M.Skip;
    }

    Mode? BalanceBranches(List<Mode?> l, Ast pos)
    {
        if (l.All(x => x == null || IsFlexible(x))) return l.FirstOrDefault(x => x is SpecialMode sm && sm.Kind == SpecialKind.Vacuum) ?? (l.Any(x => x == null) ? null : M.Skip);
        if (l.Any(x => x == null)) return null; // un affichage parmi les branches : contexte fort requis
        return Balance(l, pos);
    }

    bool IsIntSelector(SerialAst s)
    {
        var m = SerialApriori(s);
        return m != null && MeekValue(m) is PrimMode { Kind: Prim.Int, Size: 0 };
    }

    // ================================================================== generation

    public Node Gen(UnitAst u, Mode target, Strength s)
    {
        Node n;
        switch (u)
        {
            case ClosedAst c: n = GenSerial(c.Serial, target, s, true); break;
            case CondAst c: n = GenCond(c, target); break;
            case CaseAst c: n = GenCase(c, target); break;
            case ConformityAst c: n = GenConformity(c, target); break;
            case CollateralAst c: n = GenCollateral(c, target); break;
            case SkipAst: n = GenSkip(target, u); break;
            case NilAst:
                if (target is not RefMode && target != M.Any) Error(u, $"NIL n'est pas un nom de mode {target}");
                n = new NilNode();
                break;
            case VacuumAst: n = GenVacuum(target, u); break;
            case JumpAst j: n = GenJump(j.Label, target, u); break;
            case IdentAst id when LookupId(id.Name) == null && LookupLabel(id.Name) != null: n = GenJump(id.Name, target, u); break;
            default:
                {
                    var (raw, a) = GenRaw(u);
                    if (a == null) { Error(u, "mode a priori indetermine"); a = M.Any; }
                    n = Coerce(raw, a, target, s, u);
                    break;
                }
        }
        if (n.Line == 0) n.SetPos(u);
        n.Mode = target;
        return n;
    }

    Node GenMeekInt(UnitAst u) => Gen(u, M.Int, Strength.Meek);

    Node GenSkip(Mode target, Ast at)
    {
        if (target.IsVoid) return new ConstNode(default).At<ConstNode>(at);
        var t = target;
        var mt = M;
        if (t is RefMode) return new NilNode().At<NilNode>(at);
        return new SkipNode(() => RT.SkipValue(t, mt)).At<SkipNode>(at);
    }

    Node GenVacuum(Mode target, Ast at)
    {
        if (target.IsVoid) return new ConstNode(default).At<ConstNode>(at);
        var r = target.AsRow;
        if (r == null) { if (target != M.Any) Error(at, $"() (rangee vide) en contexte de mode {target}"); return new ConstNode(default); }
        int dims = r.Dims;
        return new SkipNode(() =>
        {
            var l = new int[dims]; var u = new int[dims];
            for (int i = 0; i < dims; i++) { l[i] = 1; u[i] = 0; }
            return Value.Obj(RowV.Make(l, u, default));
        }).At<SkipNode>(at);
    }

    Node GenJump(string label, Mode target, Ast at)
    {
        var ls = LookupLabel(label);
        if (ls == null) { Error(at, $"etiquette « {label} » non declaree"); return new ConstNode(default); }
        NoteFree(ls.Range);
        int hops = HopsTo(ls.Range);
        if (target is ProcMode pm && pm.Params.Length == 0 && pm.Result.IsVoid)
            return new JumpProcNode(ls.Target, hops).At<JumpProcNode>(at);
        return new JumpNode(ls.Target, hops).At<JumpNode>(at);
    }

    (Node, Mode?) GenRaw(UnitAst u)
    {
        switch (u)
        {
            case IntDenot d: return GenIntDenot(d);
            case RealDenot d: return GenRealDenot(d);
            case BitsDenot d: return GenBitsDenot(d);
            case StringDenot s:
                if (s.Value.Length == 1) return (new ConstNode(Value.I(s.Value[0])).At<ConstNode>(u), M.Char);
                return (new ConstNode(RT.StrValue(s.Value)).At<ConstNode>(u), M.RowChar);
            case BoolDenot b: return (new ConstNode(Value.B(b.Value)).At<ConstNode>(u), M.Bool);
            case EmptyDenot: return (new ConstNode(default).At<ConstNode>(u), M.Void);
            case IdentAst id: return GenIdent(id);
            case ApplyAst a: return GenApply(a);
            case SelectionAst sel: return GenSelection(sel);
            case GeneratorAst g:
                {
                    var mode = M.Normalize(M.Ref(DeclMode(g.Declarer)));
                    var gen = MakeGen(g.Declarer, false);
                    if (!g.Heap) cur.HasGenerators = true;
                    return (new GeneratorNode(gen, g.Heap, cur.Level).At<GeneratorNode>(u), mode);
                }
            case CastAst c:
                {
                    var m = ValueMode(c.Declarer);
                    return (Gen(c.Enclosed, m, Strength.Strong), m);
                }
            case DyadicAst d: return GenDyadic(d);
            case MonadicAst mo: return GenMonadic(mo);
            case AssignAst asg: return GenAssign(asg);
            case IdentityRelAst ir: return GenIdentityRel(ir);
            case RoutineTextAst rt: return GenRoutineText(rt);
            case AndThenAst at:
                {
                    var l = Gen(at.Left, M.Bool, Strength.Meek); var r = Gen(at.Right, M.Bool, Strength.Meek);
                    return (new AndThenNode(l, r, at.IsOr).At<AndThenNode>(u), M.Bool);
                }
            case AssertAst asr: return (new AssertNode(Gen(asr.Cond, M.Bool, Strength.Meek)).At<AssertNode>(u), M.Void);
            case FormatTextAst ft: return (GenFormat(ft), M.Format);
            case LoopAst lp: return (GenLoop(lp), M.Void);
            case ParallelAst par: return (GenPar(par), M.Void);
        }
        Error(u, "construction non prise en charge ici");
        return (new ConstNode(default), M.Any);
    }

    // ------------------------------------------------------------------ denotations

    (Node, Mode?) GenIntDenot(IntDenot d)
    {
        int size = Math.Max(0, d.Size);
        if (size == 0)
        {
            if (d.Value > int.MaxValue) { Error(d, $"denotation entiere {d.Value} superieure a max int ({int.MaxValue})"); return (new ConstNode(Value.I(0)), M.Int); }
            return (new ConstNode(Value.I((long)d.Value)).At<ConstNode>(d), M.Int);
        }
        if (size > 2) size = 2;
        var max = Prelude.MaxLongInt(size, this);
        if (d.Value > max) Error(d, $"denotation trop grande pour {(size == 1 ? "LONG INT" : "LONG LONG INT")}");
        return (new ConstNode(Value.Obj(d.Value)).At<ConstNode>(d), M.IntOf(size));
    }

    (Node, Mode?) GenRealDenot(RealDenot d)
    {
        int size = Math.Max(0, Math.Min(2, d.Size));
        if (size == 0)
        {
            if (!double.TryParse(d.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || double.IsInfinity(v))
            { Error(d, $"denotation reelle {d.Text} hors limites"); v = 0; }
            return (new ConstNode(Value.R(v)).At<ConstNode>(d), M.Real);
        }
        var mp = MpReal.Parse(d.Text, Prelude.RealDigits(size, this));
        return (new ConstNode(Value.Obj(mp)).At<ConstNode>(d), M.RealOf(size));
    }

    (Node, Mode?) GenBitsDenot(BitsDenot d)
    {
        int size = Math.Max(0, Math.Min(2, d.Size));
        int width = Prelude.BitsWidthOf(size, this);
        if (d.Value.GetBitLength() > width) Error(d, $"denotation de bits {d.Text} trop large pour {width} bits");
        if (size == 0) return (new ConstNode(Value.I((long)(d.Value & uint.MaxValue))).At<ConstNode>(d), M.Bits);
        return (new ConstNode(Value.Obj(d.Value)).At<ConstNode>(d), M.BitsOf(size));
    }

    // ------------------------------------------------------------------ identificateurs

    (Node, Mode?) GenIdent(IdentAst id)
    {
        var s = LookupId(id.Name);
        if (s == null)
        {
            Error(id, $"identificateur « {id.Name} » non declare");
            return (new ConstNode(default).At<ConstNode>(id), M.Any);
        }
        return (SymNode(s, id), s.Mode);
    }

    Node SymNode(IdSym s, Ast at)
    {
        if (s.IsPrelude)
        {
            if (s.Const != null) return new ConstNode(s.Const.Value).At<ConstNode>(at);
            if (s.CtxValue != null) return new CtxValueNode(s.CtxValue).At<CtxValueNode>(at);
            if (s.Builtin != null) return new ConstNode(Value.Obj(new ProcV { Builtin = s.Builtin, Name = s.Name })).At<ConstNode>(at);
        }
        if (!s.Declared && s.Range.Routine == routine && suppressDeclCheck == 0 && !s.IsLoopVar)
            Error(at, $"« {s.Name} » utilise avant sa declaration");
        NoteFree(s.Range);
        var n = new SlotNode(HopsTo(s.Range), s.Slot) { Name = s.Name, NeedsObj = s.Mode is not PrimMode };
        n.SetPos(at);
        return n;
    }

    // ------------------------------------------------------------------ appels et tranches

    enum ApplyKind { Error, Call, SliceName, SliceValue }
    sealed record ApplyInfo(ApplyKind Kind, Mode? PrimTarget, Mode? Result, RowMode? Row = null, ProcMode? Proc = null, int Trims = 0);

    ApplyInfo ClassifyApply(ApplyAst a, bool report)
    {
        var A = Apriori(a.Primary);
        if (A == null) { if (report) Error(a.Primary, "le primaire d'un appel ou d'une tranche n'a pas de mode a priori"); return new ApplyInfo(ApplyKind.Error, null, M.Any); }
        if (A == M.Any) return new ApplyInfo(ApplyKind.Error, null, M.Any);
        int trims = a.Args.Count(x => x.IsTrimmer);
        var cur = A;
        for (int g = 0; g < 32; g++)
        {
            if (cur is ProcMode pm)
            {
                if (pm.Params.Length > 0)
                {
                    if (a.Brackets && report) Error(a, "appel de procedure avec des crochets [ ]");
                    Mode res = pm.Result;
                    if (trims > 0)
                    {
                        // parametrage partiel : PROC des parametres manquants
                        var rest = new List<Mode>();
                        for (int i = 0; i < pm.Params.Length && i < a.Args.Count; i++) if (a.Args[i].IsTrimmer) rest.Add(pm.Params[i]);
                        res = M.Intern(new ProcMode(rest.ToArray(), pm.Result));
                    }
                    return new ApplyInfo(ApplyKind.Call, cur, res, Proc: pm, Trims: trims);
                }
                cur = pm.Result; continue;
            }
            if (cur is RefMode rm)
            {
                var row = rm.Sub.AsRow;
                if (row != null)
                {
                    Mode res = trims == 0 ? M.Normalize(M.Ref(row.Elem)) : M.Normalize(M.Ref(M.Row(trims, row.Elem)));
                    return new ApplyInfo(ApplyKind.SliceName, cur, res, row, Trims: trims);
                }
                cur = M.Normalize(rm.Sub); continue;
            }
            if (cur is RowMode rv)
            {
                Mode res = trims == 0 ? rv.Elem : M.Row(trims, rv.Elem);
                return new ApplyInfo(ApplyKind.SliceValue, cur, res, rv, Trims: trims);
            }
            break;
        }
        if (report) Error(a, $"{A} n'est ni une procedure ni une rangee : appel ou tranche impossible");
        return new ApplyInfo(ApplyKind.Error, null, M.Any);
    }

    (Node, Mode?) GenApply(ApplyAst a)
    {
        var info = ClassifyApply(a, true);
        switch (info.Kind)
        {
            case ApplyKind.Call: return (GenCall(a, info), info.Result);
            case ApplyKind.SliceName:
            case ApplyKind.SliceValue:
                return (GenSlice(a, info), info.Result);
        }
        foreach (var x in a.Args) if (x.Unit != null) Gen(x.Unit, M.Any, Strength.Strong);
        return (new ConstNode(default).At<ConstNode>(a), M.Any);
    }

    Node GenCall(ApplyAst a, ApplyInfo info)
    {
        var pm = info.Proc!;
        if (a.Args.Count != pm.Params.Length)
        {
            Error(a, $"nombre d'arguments incorrect : {a.Args.Count} au lieu de {pm.Params.Length} pour {pm}");
        }
        var args = new List<Node?>();
        for (int i = 0; i < a.Args.Count; i++)
        {
            var x = a.Args[i];
            if (x.IsTrimmer)
            {
                if (x.HasColon || x.Lwb != null || x.Upb != null || x.AtLwb != null) Error(x, "trimmer dans une liste d'arguments");
                args.Add(null);
                continue;
            }
            var pmode = i < pm.Params.Length ? pm.Params[i] : M.Any;
            args.Add(Gen(x.Unit!, pmode, Strength.Strong));
        }
        // primaire : contexte meek vers le mode procedure
        Node proc;
        BuiltinFn? direct = null;
        if (a.Primary is IdentAst pid && LookupId(pid.Name) is IdSym ps && ps.IsPrelude && ps.Builtin != null && ReferenceEquals(ps.Mode, pm))
            direct = ps.Builtin;
        proc = direct == null ? Gen(a.Primary, pm, Strength.Meek) : null!;
        if (info.Trims > 0)
            return new PartialCallNode(proc ?? new ConstNode(Value.Obj(new ProcV { Builtin = direct, Name = ((IdentAst)a.Primary).Name })), args.ToArray(), pm).At<PartialCallNode>(a);
        var argNodes = args.Select(x => x!).ToArray();
        if (direct != null) return new BuiltinCallNode(direct, argNodes).At<BuiltinCallNode>(a);
        var modes = new Mode[argNodes.Length];
        for (int i = 0; i < modes.Length; i++)
        {
            var pmode = i < pm.Params.Length ? pm.Params[i] : M.Any;
            modes[i] = IsFresh(argNodes[i]) ? M.Int : pmode; // M.Int : pas de copie
        }
        return new CallNode(proc, argNodes, modes, cur.Level).At<CallNode>(a);
    }

    static bool IsFresh(Node n) => n is ConstNode or RowDisplayNode or StructDisplayNode or RowValueNode || n.Fresh;

    Node GenSlice(ApplyAst a, ApplyInfo info)
    {
        var row = info.Row!;
        if (a.Args.Count != row.Dims) Error(a, $"{a.Args.Count} indice(s) pour une rangee a {row.Dims} dimension(s)");
        var prim = Gen(a.Primary, info.PrimTarget!, Strength.Weak);
        bool onName = info.Kind == ApplyKind.SliceName;
        var ix = new SliceIndexer[a.Args.Count];
        for (int i = 0; i < ix.Length; i++)
        {
            var x = a.Args[i];
            if (!x.IsTrimmer) ix[i] = new SliceIndexer { Index = GenMeekInt(x.Unit!) };
            else ix[i] = new SliceIndexer
            {
                Trimmer = true, HasColon = x.HasColon,
                Lwb = x.Lwb != null ? GenMeekInt(x.Lwb) : null,
                Upb = x.Upb != null ? GenMeekInt(x.Upb) : null,
                At = x.AtLwb != null ? GenMeekInt(x.AtLwb) : null,
            };
        }
        bool transient = onName && (info.PrimTarget is RefMode { Sub: FlexMode } || IsTransient(prim));
        Node res;
        if (ix.Length == 1 && !ix[0].Trimmer && row.Dims == 1)
            res = onName ? new Subscript1NameNode(prim, ix[0].Index!).At<Subscript1NameNode>(a) : new Subscript1ValueNode(prim, ix[0].Index!).At<Subscript1ValueNode>(a);
        else res = new SliceNode(prim, ix, onName).At<SliceNode>(a);
        res.Transient = transient;
        return res;
    }

    // ------------------------------------------------------------------ selections

    enum SelKind { Error, Name, Value, RowName, RowValue }
    sealed record SelInfo(SelKind Kind, Mode? Target, Mode? Result, int Field);

    SelInfo ClassifySelection(SelectionAst sel, bool report)
    {
        var A = Apriori(sel.Secondary);
        if (A == null) { if (report) Error(sel, "selection dans une valeur sans mode a priori"); return new SelInfo(SelKind.Error, null, M.Any, -1); }
        if (A == M.Any) return new SelInfo(SelKind.Error, null, M.Any, -1);
        var cur = A;
        for (int g = 0; g < 32; g++)
        {
            if (cur is RefMode rm)
            {
                if (rm.Sub is StructMode sm)
                {
                    int f = sm.IndexOf(sel.Field);
                    if (f < 0) { if (report) Error(sel, $"pas de champ « {sel.Field} » dans {sm}"); return new SelInfo(SelKind.Error, null, M.Any, -1); }
                    return new SelInfo(SelKind.Name, cur, M.Normalize(M.Ref(sm.Fields[f].Mode)), f);
                }
                var row = rm.Sub.AsRow;
                if (row != null && row.Elem is StructMode sm2)
                {
                    int f = sm2.IndexOf(sel.Field);
                    if (f < 0) { if (report) Error(sel, $"pas de champ « {sel.Field} » dans {sm2}"); return new SelInfo(SelKind.Error, null, M.Any, -1); }
                    return new SelInfo(SelKind.RowName, cur, M.Normalize(M.Ref(M.Row(row.Dims, sm2.Fields[f].Mode))), f);
                }
                cur = M.Normalize(rm.Sub); continue;
            }
            if (cur is StructMode s)
            {
                int f = s.IndexOf(sel.Field);
                if (f < 0) { if (report) Error(sel, $"pas de champ « {sel.Field} » dans {s}"); return new SelInfo(SelKind.Error, null, M.Any, -1); }
                return new SelInfo(SelKind.Value, cur, s.Fields[f].Mode, f);
            }
            if (cur is RowMode r && r.Elem is StructMode s2)
            {
                int f = s2.IndexOf(sel.Field);
                if (f < 0) { if (report) Error(sel, $"pas de champ « {sel.Field} » dans {s2}"); return new SelInfo(SelKind.Error, null, M.Any, -1); }
                return new SelInfo(SelKind.RowValue, cur, M.Row(r.Dims, s2.Fields[f].Mode), f);
            }
            if (cur is ProcMode pm && pm.Params.Length == 0) { cur = pm.Result; continue; }
            break;
        }
        if (report) Error(sel, $"selection « {sel.Field} OF » dans une valeur de mode {A} qui n'est pas une structure");
        return new SelInfo(SelKind.Error, null, M.Any, -1);
    }

    (Node, Mode?) GenSelection(SelectionAst sel)
    {
        var info = ClassifySelection(sel, true);
        if (info.Kind == SelKind.Error) return (new ConstNode(default).At<ConstNode>(sel), M.Any);
        var sec = Gen(sel.Secondary, info.Target!, Strength.Weak);
        Node n = info.Kind switch
        {
            SelKind.Name => new SelectNameNode(sec, info.Field),
            SelKind.Value => new SelectValueNode(sec, info.Field),
            SelKind.RowName => new SelectRowNode(sec, info.Field, true),
            _ => new SelectRowNode(sec, info.Field, false),
        };
        if (info.Kind == SelKind.RowName && (info.Target is RefMode { Sub: FlexMode } || IsTransient(sec))) n.Transient = true;
        if (info.Kind == SelKind.Name && IsTransient(sec)) n.Transient = true;
        n.SetPos(sel);
        return (n, info.Result);
    }

    // ------------------------------------------------------------------ affectation, relation d'identite

    (Node, Mode?) GenAssign(AssignAst asg)
    {
        var a = Apriori(asg.Dest);
        if (a == null) { Error(asg.Dest, "destination d'affectation sans mode a priori"); a = M.Any; }
        var dt = DeprocAll(a);
        if (dt is not RefMode rm)
        {
            if (dt != M.Any) Error(asg.Dest, $"la destination d'une affectation doit etre un nom (mode REF ...), pas {a}");
            Gen(asg.Source, M.Any, Strength.Strong);
            return (new ConstNode(default).At<ConstNode>(asg), M.Any);
        }
        var dest = Gen(asg.Dest, dt, Strength.Soft);
        var sub = rm.Sub;
        var valMode = M.Normalize(sub);
        var src = Gen(asg.Source, valMode, Strength.Strong);
        CheckTransient(src, asg.Source, "affecte");
        bool scopeCheck = valMode.HasNames;
        Node n;
        if (dest is SlotNode sn && !sub.NeedsCopy && !scopeCheck) n = new AssignSlotNode(sn.Hops, sn.Slot, src);
        else n = new AssignNode(dest, src, sub, scopeCheck);
        n.SetPos(asg);
        return (n, dt);
    }

    (Node, Mode?) GenIdentityRel(IdentityRelAst ir)
    {
        var la = Apriori(ir.Left); var ra = Apriori(ir.Right);
        Mode? l = la == null ? null : DeprocAll(la);
        Mode? r = ra == null ? null : DeprocAll(ra);
        bool lf = l == null || IsFlexible(l), rf = r == null || IsFlexible(r);
        if (!lf && l is not RefMode && l != M.Any) Error(ir.Left, $"relation d'identite : {la} n'est pas un nom");
        if (!rf && r is not RefMode && r != M.Any) Error(ir.Right, $"relation d'identite : {ra} n'est pas un nom");
        Mode mode;
        if (lf && rf) { Error(ir, "relation d'identite sans mode determinable (NIL :=: NIL ?)"); mode = M.Any; }
        else if (lf) mode = r!;
        else if (rf) mode = l!;
        else if (FindCoercion(l!, r!, Strength.Strong) != null) mode = r!;
        else if (FindCoercion(r!, l!, Strength.Strong) != null) mode = l!;
        else { Error(ir, $"relation d'identite entre noms de modes incompatibles {la} et {ra}"); mode = M.Any; }
        var ln = Gen(ir.Left, mode, Strength.Strong);
        var rn = Gen(ir.Right, mode, Strength.Strong);
        return (new IdentityRelNode(ln, rn, ir.Not).At<IdentityRelNode>(ir), M.Bool);
    }

    // ------------------------------------------------------------------ textes de routine

    (Node, Mode?) GenRoutineText(RoutineTextAst rt)
    {
        var pm = (ProcMode)RoutineHeaderMode(rt)!;
        var info = new RoutineInfo
        {
            Name = pendingRoutineName ?? $"routine ({rt.Line}:{rt.Col})",
            ParamCount = rt.Params.Count, Mode = pm, Level = routine.Level + 1,
            File = rt.File, Line = rt.Line, Col = rt.Col,
        };
        pendingRoutineName = null;
        var ctx = new RoutineCtx { Parent = routine, Level = routine.Level + 1, Info = info };
        var saveR = routine; var saveC = cur;
        routine = ctx;
        try
        {
            var pr = NewRange(rt);
            pr.Level = 0;
            cur = pr;
            var names = new HashSet<string>();
            for (int i = 0; i < rt.Params.Count; i++)
            {
                var p = rt.Params[i];
                if (!names.Add(p.Name)) Error(p, $"parametre « {p.Name} » declare deux fois");
                var s = new IdSym { Name = p.Name, Range = pr, Mode = pm.Params[i], Slot = ctx.Alloc(), Declared = true, Pos = p };
                pr.Ids[p.Name] = s;
                info.Vars.Add(new DebugVar { Name = p.Name, Slot = s.Slot, Mode = s.Mode, FromLine = rt.Line, ToLine = int.MaxValue, File = rt.File });
            }
            var body = Gen(rt.Body, pm.Result, Strength.Strong);
            CheckTransient(body, rt.Body, "rendu par une routine");
            // le corps reduit a une unite est une instruction (points d'arret, pas a pas)
            info.Body = Stmt(body, rt.Body);
            info.FrameSize = ctx.NextSlot;
            info.CheckResultScope = pm.Result.HasNames;
            info.ResultMode = pm.Result;
        }
        finally { routine = saveR; cur = saveC; }
        int hops = -1, level = 0;
        if (ctx.InnermostFree != null && ctx.InnermostFree.Routine != null)
        {
            hops = routine.Level - ctx.InnermostFree.Routine.Level;
            level = ctx.InnermostFree.Level;
        }
        return (new RoutineTextNode(info, hops, level).At<RoutineTextNode>(rt), pm);
    }

    // ------------------------------------------------------------------ clauses suites

    Node Stmt(Node n) => n is StmtNode ? n : new StmtNode(n);
    Node Stmt(Node n, UnitAst u)
    {
        if (n is StmtNode) return n;
        var s = new StmtNode(n);
        var st = StartOf(u);
        s.File = st.File; s.Line = st.Line; s.Col = st.Col;
        return s;
    }

    /// <summary>Premier symbole d'une unite (position des instructions).</summary>
    static Ast StartOf(UnitAst u) => u switch
    {
        DyadicAst d => StartOf(d.Left),
        AssignAst a => StartOf(a.Dest),
        IdentityRelAst ir => StartOf(ir.Left),
        ApplyAst ap => StartOf(ap.Primary),
        AndThenAst at => StartOf(at.Left),
        _ => u,
    };

    public Node GenSerial(SerialAst s, Mode target, Strength st, bool newRange)
    {
        var r = RangeOf(s);
        var save = cur;
        cur = r;
        try
        {
            var serial = new SerialNode();
            serial.SetPos(s);
            var stmts = new List<Node>(); var yields = new List<bool>(); var completes = new List<bool>();
            bool hasLabels = false;
            bool seenLabel = false;
            int last = s.Phrases.Count - 1;
            for (int i = 0; i <= last; i++)
            {
                var ph = s.Phrases[i];
                if (ph is LabeledUnit lu)
                {
                    if (lu.Labels.Count > 0) seenLabel = true;
                    foreach (var l in lu.Labels)
                        if (r.Labels.TryGetValue(l, out var ls)) { ls.Target.Serial = serial; ls.Target.Index = stmts.Count; hasLabels = true; }
                    bool y = i == last || s.ExitAfter.Contains(i);
                    var node = y ? Gen(lu.Unit, target, st) : Gen(lu.Unit, M.Void, Strength.Strong);
                    stmts.Add(Stmt(node, lu.Unit)); yields.Add(y); completes.Add(y);
                }
                else
                {
                    if (i == last) Error(ph, "une clause doit se terminer par une unite, pas par une declaration");
                    if (seenLabel) Error(ph, "une declaration ne peut pas suivre une unite etiquetee dans la meme clause");
                    foreach (var n in GenDeclaration((DeclarationAst)ph))
                    {
                        stmts.Add(Stmt(n)); yields.Add(false); completes.Add(false);
                    }
                }
                if (i < last && s.ExitAfter.Contains(i) && ph is not LabeledUnit) Error(ph, "EXIT doit suivre une unite");
            }
            if (stmts.Count == 0) { stmts.Add(new ConstNode(default)); yields.Add(true); completes.Add(true); }
            // EXIT suivi d'une partie sans etiquette : inaccessible
            for (int i = 0; i < s.ExitAfter.Count; i++)
            {
                int k = s.ExitAfter[i] + 1;
                if (k <= last && s.Phrases[k] is LabeledUnit nl && nl.Labels.Count == 0) Error(nl, "unite inaccessible apres EXIT (une etiquette est requise)");
            }
            serial.Stmts = stmts.ToArray(); serial.Yields = yields.ToArray(); serial.Completes = completes.ToArray();
            serial.HasLabels = hasLabels;
            if (!target.IsVoid && target.HasNames && r.HasGenerators)
            {
                serial.ScopeLimitLevel = r.Level;
                serial.YieldMode = target;
            }
            serial.Mode = target;
            if (stmts.Count == 1 && !hasLabels && serial.ScopeLimitLevel < 0) return stmts[0];
            return serial;
        }
        finally { cur = save; }
    }

    IEnumerable<Node> GenDeclaration(DeclarationAst d)
    {
        switch (d)
        {
            case ModeDeclAst md:
                foreach (var (name, _, _) in md.Items)
                    if (cur.Modes.TryGetValue(name, out var ms) && ms.HasActualBounds && ms.Gen == null) ms.Gen = CompileModeGen(ms);
                yield break;
            case PrioDeclAst: yield break;
            case IdentityDeclAst id:
                foreach (var (name, val, pos) in id.Items)
                {
                    if (!cur.Ids.TryGetValue(name, out var s) || !ReferenceEquals(s.Pos, pos)) continue;
                    if (val is RoutineTextAst) pendingRoutineName = name;
                    var v = Gen(val, s.Mode, Strength.Strong);
                    CheckTransient(v, val, "lie a un identificateur");
                    pendingRoutineName = null;
                    s.Declared = true;
                    yield return new IdentityDeclNode(s.Slot, v, s.Mode, s.Mode.NeedsCopy && !IsFresh(v)).At<IdentityDeclNode>(pos);
                }
                yield break;
            case VarDeclAst vd:
                foreach (var (name, init, pos) in vd.Items)
                {
                    if (!cur.Ids.TryGetValue(name, out var s) || !ReferenceEquals(s.Pos, pos)) continue;
                    var sub = ((RefMode)s.Mode).Sub;
                    var gen = MakeGen(vd.Declarer, false);
                    Node? iv = null;
                    if (init != null)
                    {
                        if (init is RoutineTextAst) pendingRoutineName = name;
                        iv = Gen(init, M.Normalize(sub), Strength.Strong);
                        CheckTransient(iv, init, "affecte");
                        pendingRoutineName = null;
                    }
                    s.Declared = true;
                    if (!vd.Heap) cur.HasGenerators = true;
                    yield return new VarDeclNode(s.Slot, gen, vd.Heap, cur.Level, iv, sub, M.Normalize(sub).HasNames).At<VarDeclNode>(pos);
                }
                yield break;
            case ProcDeclAst pd:
                foreach (var (name, val, pos) in pd.Items)
                {
                    if (!cur.Ids.TryGetValue(name, out var s) || !ReferenceEquals(s.Pos, pos)) continue;
                    pendingRoutineName = name;
                    if (pd.Variable)
                    {
                        var sub = ((RefMode)s.Mode).Sub;
                        var iv = Gen(val, M.Normalize(sub), Strength.Strong);
                        pendingRoutineName = null;
                        s.Declared = true;
                        if (!pd.Heap) cur.HasGenerators = true;
                        yield return new VarDeclNode(s.Slot, UninitGen.Instance, pd.Heap, cur.Level, iv, sub, true).At<VarDeclNode>(pos);
                    }
                    else
                    {
                        var v = Gen(val, s.Mode, Strength.Strong);
                        pendingRoutineName = null;
                        s.Declared = true;
                        yield return new IdentityDeclNode(s.Slot, v, s.Mode, false).At<IdentityDeclNode>(pos);
                    }
                }
                yield break;
            case OpDeclAst od:
                foreach (var (name, val, pos) in od.Items)
                {
                    if (val.SemCache is not OpSym op) continue;
                    pendingRoutineName = "OP " + name;
                    var v = Gen(val, op.Mode, Strength.Strong);
                    pendingRoutineName = null;
                    op.Proc!.Declared = true;
                    yield return new IdentityDeclNode(op.Proc.Slot, v, op.Mode, false).At<IdentityDeclNode>(pos);
                }
                yield break;
        }
    }

    // ------------------------------------------------------------------ clauses de choix

    Node GenCond(CondAst c, Mode target)
    {
        var r = RangeOf(c.If);
        var save = cur; cur = r;
        try
        {
            if (c.Brief && IsIntSelector(c.If))
            {
                // ( n | a | b ) : clause de cas abregee a une seule unite
                var sel = GenSerial(c.If, M.Int, Strength.Meek, true);
                var one = GenSerial(c.Then, target, Strength.Strong, true);
                Node? outp = c.Else != null ? GenSerial(c.Else, target, Strength.Strong, true)
                    : c.ElifCond != null ? Gen(c.ElifCond, target, Strength.Strong) : target.IsVoid ? null : GenSkip(target, c);
                return new CaseNode(sel, new[] { one }, outp).At<CaseNode>(c);
            }
            var cond = GenSerial(c.If, M.Bool, Strength.Meek, true);
            var then = GenSerial(c.Then, target, Strength.Strong, true);
            Node? els = c.Else != null ? GenSerial(c.Else, target, Strength.Strong, true)
                : c.ElifCond != null ? Gen(c.ElifCond, target, Strength.Strong)
                : target.IsVoid ? null : GenSkip(target, c);
            return new CondNode(cond, then, els).At<CondNode>(c);
        }
        finally { cur = save; }
    }

    Node GenCase(CaseAst c, Mode target)
    {
        var r = RangeOf(c.Selector);
        var save = cur; cur = r;
        try
        {
            var sel = GenSerial(c.Selector, M.Int, Strength.Meek, true);
            var cases = c.In.Select(u => Stmt(Gen(u, target, Strength.Strong), u)).ToArray();
            Node? outp = c.Out != null ? GenSerial(c.Out, target, Strength.Strong, true)
                : c.Ouse != null ? Gen(c.Ouse, target, Strength.Strong)
                : target.IsVoid ? null : GenSkip(target, c);
            return new CaseNode(sel, cases, outp).At<CaseNode>(c);
        }
        finally { cur = save; }
    }

    Range ConformityRange(ConformityCase cc)
    {
        if (cc.SemCache is Range r) return r;
        r = NewRange(cc);
        cc.SemCache = r;
        if (cc.Name != null)
        {
            var mode = WithRange(r, () => ValueMode(cc.Declarer));
            var s = new IdSym { Name = cc.Name, Range = r, Mode = mode, Slot = routine.Alloc(), Declared = true, Pos = cc };
            r.Ids[cc.Name] = s;
            routine.Info.Vars.Add(new DebugVar { Name = cc.Name, Slot = s.Slot, Mode = mode, FromLine = cc.Line, ToLine = LastLine(cc.Unit), File = cc.File });
        }
        return r;
    }

    Node GenConformity(ConformityAst c, Mode target)
    {
        var r = RangeOf(c.Selector);
        var save = cur; cur = r;
        try
        {
            var sa = SerialApriori(c.Selector);
            var selMode = sa == null ? M.Any : MeekValue(sa);
            if (selMode is not UnionMode um)
            {
                if (selMode != M.Any) Error(c, $"la clause de conformite exige une valeur unie, pas {selMode}");
                um = null!;
            }
            var node = new ConformityNode();
            node.SetPos(c);
            node.Sel = GenSerial(c.Selector, selMode, Strength.Meek, true);
            var accept = new List<Mode[]>(); var rewrap = new List<bool>(); var slots = new List<int>(); var units = new List<Node>();
            foreach (var cc in c.Cases)
            {
                var cr = ConformityRange(cc);
                var cm = WithRange(cr, () => ValueMode(cc.Declarer));
                Mode[] acc;
                if (cm is UnionMode cu)
                {
                    acc = cu.Members;
                    if (um != null) foreach (var x in acc) if (um.IndexOf(x) < 0) Error(cc, $"le mode {x} n'est pas une composante de {um}");
                }
                else
                {
                    acc = new[] { cm };
                    if (um != null && um.IndexOf(cm) < 0 && cm != M.Any) Error(cc, $"le mode {cm} n'est pas une composante de {um}");
                }
                accept.Add(acc); rewrap.Add(cm is UnionMode);
                slots.Add(cc.Name != null ? cr.Ids[cc.Name].Slot : -1);
                units.Add(WithRange(cr, () => Stmt(Gen(cc.Unit, target, Strength.Strong), cc.Unit)));
            }
            node.Accept = accept.ToArray(); node.RewrapUnion = rewrap.ToArray(); node.Slots = slots.ToArray(); node.Cases = units.ToArray();
            node.Out = c.Out != null ? GenSerial(c.Out, target, Strength.Strong, true)
                : c.Ouse != null ? Gen(c.Ouse, target, Strength.Strong)
                : target.IsVoid ? null : GenSkip(target, c);
            return node;
        }
        finally { cur = save; }
    }

    Node GenCollateral(CollateralAst c, Mode target)
    {
        if (target.IsVoid || target == M.Any)
            return new SeqVoidNode(c.Units.Select(u => Stmt(Gen(u, target == M.Any ? M.Any : M.Void, Strength.Strong), u)).ToArray()).At<SeqVoidNode>(c);
        if (target is RowMode rm)
        {
            if (rm.Dims == 1)
            {
                var elems = c.Units.Select(u => Gen(u, rm.Elem, Strength.Strong)).ToArray();
                return new RowDisplayNode(elems, 1, rm.Elem).At<RowDisplayNode>(c);
            }
            var sub = M.Row(rm.Dims - 1, rm.Elem);
            var subs = c.Units.Select(u => Gen(u, sub, Strength.Strong)).ToArray();
            return new RowDisplayNode(subs, rm.Dims, rm.Elem).At<RowDisplayNode>(c);
        }
        if (target is StructMode sm)
        {
            if (c.Units.Count != sm.Fields.Length)
            {
                Error(c, $"affichage de structure : {c.Units.Count} valeurs pour {sm.Fields.Length} champs de {sm}");
            }
            var fields = new Node[sm.Fields.Length];
            for (int i = 0; i < fields.Length; i++)
                fields[i] = i < c.Units.Count ? Gen(c.Units[i], sm.Fields[i].Mode, Strength.Strong) : GenSkip(sm.Fields[i].Mode, c);
            return new StructDisplayNode(fields, sm.Fields.Select(f => f.Mode).ToArray()).At<StructDisplayNode>(c);
        }
        Error(c, $"un affichage (clause collaterale) ne peut pas produire une valeur de mode {target}");
        foreach (var u in c.Units) Gen(u, M.Any, Strength.Strong);
        return new ConstNode(default).At<ConstNode>(c);
    }

    // ------------------------------------------------------------------ boucles

    Node GenLoop(LoopAst lp)
    {
        var node = new LoopNode();
        node.SetPos(lp);
        if (lp.From != null) node.From = GenMeekInt(lp.From);
        if (lp.By != null) node.By = GenMeekInt(lp.By);
        if (lp.To != null) node.To = GenMeekInt(lp.To);
        node.DownTo = lp.DownTo;
        Range lr;
        if (lp.SemCache is Range cached) lr = cached;
        else
        {
            lr = NewRange(lp);
            lp.SemCache = lr;
            lr.ToLine = LastLine(lp);
        }
        var save = cur; cur = lr;
        try
        {
            if (lp.Var != null)
            {
                if (!lr.Ids.TryGetValue(lp.Var, out var vs))
                {
                    vs = new IdSym { Name = lp.Var, Range = lr, Mode = M.Int, Slot = routine.Alloc(), Declared = true, IsLoopVar = true, Pos = lp };
                    lr.Ids[lp.Var] = vs;
                    routine.Info.Vars.Add(new DebugVar { Name = lp.Var, Slot = vs.Slot, Mode = M.Int, FromLine = lp.Line, ToLine = LastLine(lp), File = lp.File });
                }
                node.Slot = vs.Slot;
            }
            if (lp.While != null)
            {
                var wr = RangeOf(lp.While);
                node.While = GenSerial(lp.While, M.Bool, Strength.Meek, true);
                cur = wr;
            }
            var br = RangeOf(lp.Body);
            node.Body = GenSerial(lp.Body, M.Void, Strength.Strong, true);
            if (lp.Until != null)
            {
                cur = br;
                node.Until = GenSerial(lp.Until, M.Bool, Strength.Meek, true);
            }
        }
        finally { cur = save; }
        return node;
    }

    Node GenPar(ParallelAst par)
    {
        var units = par.Units.Select(u => Stmt(Gen(u, M.Void, Strength.Strong), u)).ToArray();
        return new ParNode(units, cur.Level).At<ParNode>(par);
    }
}
