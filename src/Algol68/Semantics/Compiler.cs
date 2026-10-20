using System.Numerics;
using Studio.Common;
using Studio.Algol68.Exec;
using Studio.Algol68.Runtime;
using Studio.Algol68.Syntax;

namespace Studio.Algol68.Semantics;

/// <summary>Analyse semantique (identification, modes, coercitions) et generation de l'arbre executable.</summary>
public sealed partial class Compiler
{
    public readonly ModeTable M = new();
    readonly List<Diagnostic> diags;
    public readonly Range PreludeRange;
    Range cur = null!;
    RoutineCtx routine = null!;
    int suppressDeclCheck;
    public int LongLongDigits = Prelude.DefaultLongLongDigits;

    public Compiler(List<Diagnostic> diags, int? precision)
    {
        this.diags = diags;
        if (precision != null) LongLongDigits = Math.Max(precision.Value, Prelude.LongDigits + 1);
        PreludeRange = new Range { Depth = 0 };
        Prelude.Build(this, PreludeRange);
    }

    public void Error(Ast? at, string msg)
    {
        if (at == null) diags.Add(new Diagnostic(Severity.Error, "", 0, 0, msg));
        else diags.Add(new Diagnostic(Severity.Error, at.File, at.Line, at.Col, msg));
    }
    public void Warning(Ast? at, string msg)
    {
        if (at == null) diags.Add(new Diagnostic(Severity.Warning, "", 0, 0, msg));
        else diags.Add(new Diagnostic(Severity.Warning, at.File, at.Line, at.Col, msg));
    }

    public RoutineInfo CompileProgram(SerialAst prog)
    {
        var info = new RoutineInfo { Name = "programme principal", Level = 0, File = prog.File, Line = prog.Line, Col = prog.Col };
        routine = new RoutineCtx { Level = 0, Info = info };
        cur = PreludeRange;
        var body = GenSerial(prog, M.Void, Strength.Strong, true);
        info.Body = body;
        info.FrameSize = routine.NextSlot;
        return info;
    }

    // ------------------------------------------------------------------ portees

    Range NewRange(Ast? at)
    {
        var r = new Range { Parent = cur, Routine = routine, Depth = cur.Depth + 1 };
        r.Level = cur.Routine == routine ? cur.Level + 1 : 0;
        if (at != null) { r.File = at.File; r.FromLine = at.Line; }
        return r;
    }

    /// <summary>Portee d'une clause suite (memoisee : l'analyse a priori et la generation partagent la meme).</summary>
    Range RangeOf(SerialAst s)
    {
        if (s.SemCache is Range r) return r;
        r = NewRange(s);
        s.SemCache = r;
        var save = cur;
        cur = r;
        try { DeclarePhrases(s.Phrases, r); }
        finally { cur = save; }
        r.ToLine = LastLine(s);
        return r;
    }

    static int LastLine(Ast a) => a switch
    {
        SerialAst s => s.Phrases.Count > 0 ? Math.Max(s.Line, LastLine(s.Phrases[^1])) : s.Line,
        LabeledUnit lu => Math.Max(lu.Line, LastLine(lu.Unit)),
        ClosedAst c => LastLine(c.Serial) + 1,
        CondAst c => Math.Max(c.Line, (c.Else != null ? LastLine(c.Else) : c.ElifCond != null ? LastLine(c.ElifCond) : LastLine(c.Then)) + 1),
        LoopAst l => LastLine(l.Body) + 1,
        _ => a.Line,
    };

    T WithRange<T>(Range r, Func<T> f)
    {
        var save = cur; cur = r;
        try { return f(); } finally { cur = save; }
    }

    // ------------------------------------------------------------------ recherche

    public IdSym? LookupId(string name)
    {
        for (var r = cur; r != null; r = r.Parent)
            if (r.Ids.TryGetValue(name, out var s)) return s;
        return null;
    }
    LabelSym? LookupLabel(string name)
    {
        for (var r = cur; r != null; r = r.Parent)
            if (r.Labels.TryGetValue(name, out var s)) return s;
        return null;
    }
    ModeSym? LookupMode(string name)
    {
        for (var r = cur; r != null; r = r.Parent)
            if (r.Modes.TryGetValue(name, out var s)) return s;
        return null;
    }

    /// <summary>Note l'usage d'un symbole non local par les routines en cours de compilation (portee des routines).</summary>
    void NoteFree(Range declRange)
    {
        if (declRange.Routine == null) return;
        for (var c = routine; c != null && c != declRange.Routine; c = c.Parent)
            if (c.InnermostFree == null || declRange.Depth > c.InnermostFree.Depth) c.InnermostFree = declRange;
    }

    int HopsTo(Range declRange) => routine.Level - declRange.Routine!.Level;

    // ------------------------------------------------------------------ declarants -> modes

    /// <summary>Mode (canonique, non normalise) d'un declarant.</summary>
    public Mode DeclMode(DeclarerAst d)
    {
        var raw = BuildRaw(d);
        if (raw is IndicantMode im && im.Target == null) return M.Any;
        Mode res;
        try { res = M.Intern(raw); }
        catch (InvalidOperationException) { Error(d, "mode mal forme"); return M.Any; }
        if (ContainsUnionDeclarer(d)) CheckUnions(res, d, new HashSet<Mode>(ReferenceEqualityComparer.Instance));
        return res;
    }

    static bool ContainsUnionDeclarer(DeclarerAst d) => d switch
    {
        UnionDeclarer => true,
        RefDeclarer r => ContainsUnionDeclarer(r.Sub),
        FlexDeclarer f => ContainsUnionDeclarer(f.Sub),
        RowDeclarer rd => ContainsUnionDeclarer(rd.Elem),
        ProcDeclarer p => p.Params.Any(ContainsUnionDeclarer) || ContainsUnionDeclarer(p.Result),
        StructDeclarer s => s.Fields.Any(f => ContainsUnionDeclarer(f.Mode)),
        _ => false,
    };

    /// <summary>RR 4.7.1 : aucune composante d'une union ne peut etre fermement liee a une autre.</summary>
    void CheckUnions(Mode m, Ast at, HashSet<Mode> seen)
    {
        if (!seen.Add(m)) return;
        switch (m)
        {
            case UnionMode u:
                for (int i = 0; i < u.Members.Length; i++)
                    for (int j = 0; j < u.Members.Length; j++)
                        if (i != j && FindCoercion(u.Members[i], u.Members[j], Strength.Firm) != null)
                        {
                            Error(at, $"union mal formee : les composantes {u.Members[i]} et {u.Members[j]} sont fermement liees");
                            return;
                        }
                foreach (var x in u.Members) CheckUnions(x, at, seen);
                break;
            case RefMode r: CheckUnions(r.Sub, at, seen); break;
            case FlexMode f: CheckUnions(f.Row, at, seen); break;
            case RowMode rw: CheckUnions(rw.Elem, at, seen); break;
            case ProcMode p: foreach (var x in p.Params) CheckUnions(x, at, seen); CheckUnions(p.Result, at, seen); break;
            case StructMode s: foreach (var f in s.Fields) CheckUnions(f.Mode, at, seen); break;
        }
    }

    Mode BuildRaw(DeclarerAst d)
    {
        switch (d)
        {
            case PrimDeclarer p: return PrimFromDeclarer(p);
            case IndicantDeclarer id:
                {
                    var ms = LookupMode(id.Name);
                    if (ms == null) { Error(d, $"mode « {id.Name} » non declare"); return M.Any; }
                    if (ms.Mode != null) return ms.Mode;
                    if (ms.Placeholder != null) return ms.Placeholder;
                    Error(d, $"mode « {id.Name} » utilise avant sa definition");
                    return M.Any;
                }
            case RefDeclarer r: return new RefMode(BuildRaw(r.Sub));
            case FlexDeclarer f:
                {
                    var sub = BuildRaw(f.Sub);
                    var s2 = ModeTable.Strip(sub);
                    if (s2 is RowMode rm) return new FlexMode(rm);
                    if (s2 is FlexMode) return s2;
                    if (s2 is IndicantMode im2 && im2.Target is RowMode rr) return new FlexMode(rr);
                    if (s2 == M.Any) return M.Any;
                    Error(d, "FLEX doit preceder un declarant de rangee");
                    return sub;
                }
            case ProcDeclarer pd: return new ProcMode(pd.Params.Select(BuildRaw).ToArray(), BuildRaw(pd.Result));
            case StructDeclarer sd:
                {
                    var names = new HashSet<string>();
                    foreach (var f in sd.Fields) if (!names.Add(f.Name)) Error(f.Pos, $"champ « {f.Name} » declare deux fois");
                    return new StructMode(sd.Fields.Select(f => (BuildRaw(f.Mode), f.Name)).ToArray());
                }
            case UnionDeclarer ud:
                {
                    var ms = ud.Members.Select(BuildRaw).ToArray();
                    if (ms.Length < 2) Error(d, "une union doit comporter au moins deux modes");
                    return new UnionMode(ms);
                }
            case RowDeclarer rd:
                {
                    Mode elem = BuildRaw(rd.Elem);
                    return new RowMode(rd.Bounds.Count, elem);
                }
        }
        return M.Any;
    }

    Mode PrimFromDeclarer(PrimDeclarer p)
    {
        int size = p.Size;
        if (size < 0) size = 0; // SHORT : equivalent a la longueur standard (int shorths = 1)
        bool sized = p.Name is "INT" or "REAL" or "BITS" or "BYTES" or "COMPL";
        if (!sized && p.Size != 0) Error(p, $"LONG/SHORT ne s'applique pas a {p.Name}");
        if (size > 2 || (p.Name == "BYTES" && size > 1))
        {
            Error(p, $"longueur non disponible pour {p.Name} (au plus {(p.Name == "BYTES" ? "LONG" : "LONG LONG")})");
            size = p.Name == "BYTES" ? 1 : 2;
        }
        return p.Name switch
        {
            "INT" => M.IntOf(size),
            "REAL" => M.RealOf(size),
            "BITS" => M.BitsOf(size),
            "BYTES" => size == 0 ? M.Bytes : M.LongBytes,
            "COMPL" => size == 0 ? M.Compl : size == 1 ? M.LongCompl : M.LongLongCompl,
            "BOOL" => M.Bool,
            "CHAR" => M.Char,
            "STRING" => M.String,
            "VOID" => M.Void,
            "FORMAT" => M.Format,
            "FILE" => M.File,
            "CHANNEL" => M.Channel,
            "SEMA" => M.Sema,
            _ => M.Any,
        };
    }

    /// <summary>Mode d'une valeur designee par un declarant (FLEX retire hors REF).</summary>
    public Mode ValueMode(DeclarerAst d) => M.Normalize(DeclMode(d), false);
    /// <summary>Mode du nom d'une variable declaree avec ce declarant.</summary>
    public Mode VarMode(DeclarerAst d) => M.Normalize(M.Ref(DeclMode(d)), false);

    // ------------------------------------------------------------------ generateurs

    GenSpec MakeGen(DeclarerAst d, bool flex)
    {
        switch (d)
        {
            case PrimDeclarer p:
                if (p.Name == "STRING") return new RowGen(new Node?[] { null }, new Node?[] { null }, UninitGen.Instance);
                if (p.Name == "COMPL") return new StructGen(new GenSpec[] { UninitGen.Instance, UninitGen.Instance });
                return UninitGen.Instance;
            case RefDeclarer: case ProcDeclarer: case UnionDeclarer: return UninitGen.Instance;
            case FlexDeclarer fd: return MakeGen(fd.Sub, true);
            case StructDeclarer sd: return new StructGen(sd.Fields.Select(f => MakeGen(f.Mode, false)).ToArray());
            case RowDeclarer rd:
                {
                    var lw = new Node?[rd.Bounds.Count]; var up = new Node?[rd.Bounds.Count];
                    for (int i = 0; i < rd.Bounds.Count; i++)
                    {
                        var b = rd.Bounds[i];
                        if (b.Formal || b.Upb == null)
                        {
                            if (!flex) Error(b, "bornes requises dans un declarant effectif (generateur ou declaration de variable)");
                            continue;
                        }
                        lw[i] = b.Lwb != null ? GenMeekInt(b.Lwb) : null;
                        up[i] = GenMeekInt(b.Upb);
                    }
                    return new RowGen(lw, up, MakeGen(rd.Elem, false));
                }
            case IndicantDeclarer id:
                {
                    var ms = LookupMode(id.Name);
                    if (ms == null) return UninitGen.Instance;
                    if (ms.Range.Routine == null)
                        return ms.Mode == M.String ? new RowGen(new Node?[] { null }, new Node?[] { null }, UninitGen.Instance) : GenFromMode(ms.Mode ?? M.Any, flex);
                    NoteFree(ms.Range);
                    if (ms.Gen == null) ms.Gen = CompileModeGen(ms);
                    return new IndicantGen { Inner = ms.Gen, Hops = HopsTo(ms.Range) };
                }
        }
        return UninitGen.Instance;
    }

    GenSpec CompileModeGen(ModeSym ms)
    {
        // compile les bornes dans la portee de la declaration du mode
        var saveCur = cur; var saveRoutine = routine;
        cur = ms.Range; routine = ms.Range.Routine!;
        suppressDeclCheck++;
        var placeholder = new IndicantGen();
        ms.Gen = placeholder; // garde contre la recursion (via REF, qui ne genere rien)
        try { placeholder.Inner = MakeGen(ms.Decl!, false); placeholder.Hops = 0; return placeholder; }
        finally { suppressDeclCheck--; cur = saveCur; routine = saveRoutine; }
    }

    GenSpec GenFromMode(Mode m, bool flex)
    {
        switch (m)
        {
            case StructMode sm: return new StructGen(sm.Fields.Select(f => GenFromMode(f.Mode, false)).ToArray());
            case FlexMode fm: return new RowGen(new Node?[fm.Row.Dims], new Node?[fm.Row.Dims], GenFromMode(fm.Row.Elem, false));
            case RowMode rm:
                if (!flex) return new RowGen(new Node?[rm.Dims], new Node?[rm.Dims], GenFromMode(rm.Elem, false));
                return new RowGen(new Node?[rm.Dims], new Node?[rm.Dims], GenFromMode(rm.Elem, false));
        }
        return UninitGen.Instance;
    }

    // ------------------------------------------------------------------ pre-declaration d'une portee

    void DeclarePhrases(List<Ast> phrases, Range r)
    {
        // 1. modes
        var newModes = new List<ModeSym>();
        foreach (var ph in phrases)
            if (ph is ModeDeclAst md)
                foreach (var (name, decl, pos) in md.Items)
                {
                    if (r.Modes.ContainsKey(name)) { Error(pos, $"mode « {name} » declare deux fois dans la meme portee"); continue; }
                    var ms = new ModeSym { Name = name, Range = r, Decl = decl, Pos = pos, Placeholder = new IndicantMode(name) };
                    r.Modes[name] = ms; newModes.Add(ms);
                }
        if (newModes.Count > 0) ResolveModes(newModes);
        // 2. priorites
        foreach (var ph in phrases)
            if (ph is PrioDeclAst pd)
                foreach (var (name, p, pos) in pd.Items)
                {
                    if (r.Prios.ContainsKey(name)) Error(pos, $"priorite de « {name} » declaree deux fois");
                    r.Prios[name] = p;
                }
        // 3. etiquettes
        foreach (var ph in phrases)
            if (ph is LabeledUnit lu)
                foreach (var l in lu.Labels)
                {
                    if (r.Labels.ContainsKey(l) || r.Ids.ContainsKey(l)) { Error(lu, $"etiquette « {l} » declaree deux fois"); continue; }
                    r.Labels[l] = new LabelSym { Name = l, Range = r, Pos = lu, Target = new JumpTarget { Label = l, RoutineLevel = routine.Level } };
                }
        // 4. identificateurs et operateurs
        foreach (var ph in phrases)
        {
            switch (ph)
            {
                case IdentityDeclAst id:
                    {
                        var mode = ValueMode(id.Declarer);
                        foreach (var (name, _, pos) in id.Items) DeclareId(r, name, mode, pos);
                        break;
                    }
                case VarDeclAst vd:
                    {
                        var mode = VarMode(vd.Declarer);
                        foreach (var (name, _, pos) in vd.Items) DeclareId(r, name, mode, pos);
                        break;
                    }
                case ProcDeclAst pdl:
                    foreach (var (name, val, pos) in pdl.Items)
                    {
                        var pm = RoutineHeaderMode(val);
                        if (pm == null) { Error(pos, $"PROC {name} : un texte de routine est attendu"); pm = M.Any; }
                        DeclareId(r, name, pdl.Variable ? M.Normalize(M.Ref(pm)) : pm, pos);
                    }
                    break;
                case OpDeclAst od:
                    foreach (var (name, val, pos) in od.Items)
                    {
                        Mode? pm = od.Mode != null ? ValueMode(od.Mode) : RoutineHeaderMode(val);
                        if (pm is not ProcMode procMode) { Error(pos, $"OP {name} : un texte de routine est attendu"); continue; }
                        if (procMode.Params.Length is < 1 or > 2) { Error(pos, $"un operateur a un ou deux operandes (« {name} » en a {procMode.Params.Length})"); continue; }
                        if (procMode.Params.Length == 2 && LookupPrio(name) == null) Error(pos, $"operateur dyadique « {name} » sans declaration de priorite");
                        var slot = new IdSym { Name = $"OP {name}", Range = r, Mode = procMode, Slot = routine.Alloc(), Pos = pos };
                        var op = new OpSym { Name = name, Range = r, Mode = procMode, Proc = slot, Pos = pos };
                        if (!r.Ops.TryGetValue(name, out var list)) r.Ops[name] = list = new List<OpSym>();
                        foreach (var o in list)
                            if (o.Mode.Params.Length == procMode.Params.Length && o.Mode.Params.Zip(procMode.Params).All(p => FirmlyRelated(p.First, p.Second)))
                                Error(pos, $"operateur « {name} » deja declare pour des operandes de memes modes dans cette portee");
                        list.Add(op);
                        val.SemCache = op;
                    }
                    break;
            }
        }
    }

    int? LookupPrio(string name)
    {
        for (var r = cur; r != null; r = r.Parent) if (r.Prios.TryGetValue(name, out int p)) return p;
        return Parser.StandardPrios.TryGetValue(name, out int sp) ? sp : null;
    }

    void DeclareId(Range r, string name, Mode mode, Ast pos)
    {
        if (r.Ids.ContainsKey(name) || r.Labels.ContainsKey(name)) { Error(pos, $"« {name} » declare deux fois dans la meme portee"); return; }
        var s = new IdSym { Name = name, Range = r, Mode = mode, Slot = routine.Alloc(), Pos = pos };
        r.Ids[name] = s;
        routine.Info.Vars.Add(new DebugVar { Name = name, Slot = s.Slot, Mode = mode, FromLine = pos.Line, ToLine = int.MaxValue, File = pos.File });
    }

    Mode? RoutineHeaderMode(UnitAst u)
    {
        if (u is RoutineTextAst rt)
        {
            var ps = rt.Params.Select(p => ValueMode(p.Declarer)).ToArray();
            return M.Intern(new ProcMode(ps, ValueMode(rt.Result)));
        }
        return null;
    }

    void ResolveModes(List<ModeSym> mods)
    {
        foreach (var ms in mods) ms.Placeholder!.Target = BuildRaw(ms.Decl!);
        foreach (var ms in mods)
        {
            if (!WellFormed(ms.Placeholder!)) { Error(ms.Pos, $"mode « {ms.Name} » mal forme (recursion non protegee par REF/PROC et STRUCT/PROC)"); ms.Placeholder!.Target = M.Any; }
        }
        foreach (var ms in mods)
        {
            ms.HasActualBounds = HasActualBounds(ms.Decl!);
            try
            {
                var m = M.Intern(ms.Placeholder!);
                if (m.Name == null) m.Name = ms.Name;
                ms.Mode = m;
            }
            catch (InvalidOperationException) { Error(ms.Pos, $"mode « {ms.Name} » mal forme"); ms.Mode = M.Any; }
        }
        foreach (var ms in mods) ms.Placeholder = null;
    }

    static bool HasActualBounds(DeclarerAst d) => d switch
    {
        RowDeclarer rd => rd.Bounds.Any(b => !b.Formal) || HasActualBounds(rd.Elem),
        FlexDeclarer f => HasActualBounds(f.Sub),
        StructDeclarer s => s.Fields.Any(f => HasActualBounds(f.Mode)),
        _ => false,
    };

    /// <summary>RR 7.4 : chaque cycle doit passer par un "yin" (REF, PROC) et un "yang" (STRUCT, PROC).</summary>
    static bool WellFormed(IndicantMode p)
    {
        var visited = new HashSet<(Mode, bool, bool)>();
        bool ok = true;
        void Walk(Mode m, bool yin, bool yang)
        {
            if (!ok) return;
            if (m.Canonical) return;
            if (ReferenceEquals(m, p))
            {
                if (!(yin && yang)) ok = false;
                return;
            }
            if (!visited.Add((m, yin, yang))) return;
            switch (m)
            {
                case IndicantMode im: if (im.Target != null) Walk(im.Target, yin, yang); break;
                case RefMode r: Walk(r.Sub, true, yang); break;
                case ProcMode pm: foreach (var x in pm.Params) Walk(x, true, true); Walk(pm.Result, true, true); break;
                case StructMode s: foreach (var f in s.Fields) Walk(f.Mode, yin, true); break;
                case UnionMode u: foreach (var x in u.Members) Walk(x, yin, yang); break;
                case RowMode rm: Walk(rm.Elem, yin, yang); break;
                case FlexMode fm: Walk(fm.Row, yin, yang); break;
            }
        }
        if (p.Target != null) Walk(p.Target, false, false);
        return ok;
    }
}
