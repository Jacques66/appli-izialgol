using Studio.Common;
using Studio.Simula.Runtime;

namespace Studio.Simula.Compiler;

/// <summary>Phase A : construction des portees et des symboles, allocation des cases, disposition des classes.</summary>
public sealed partial class SimCompiler
{
    readonly List<Diagnostic> diags = new();
    readonly List<string> files = new();
    readonly SimProgram prog = new();
    readonly Dictionary<object, Scope> scopeOf = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<object, Scope> bodyScopeOf = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<object, int> tempOf = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<object, ClassSym> prefixedClassOf = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<string> includedModules = new(StringComparer.OrdinalIgnoreCase);
    FrameScope env = null!;
    ClassSym root = null!;
    int nextLabelId;
    int preludeFile = -1;
    Func<string, string, SourceFile?>? includeResolver;

    public IReadOnlyList<Diagnostic> Diagnostics => diags;

    void Err(SrcPos pos, string msg)
    {
        var f = pos.File >= 0 && pos.File < files.Count ? files[pos.File] : "";
        if (diags.Count > 400) return;
        if (diags.Any(d => d.Line == pos.Line && d.Column == pos.Col && d.Message == msg && d.File == f)) return;
        diags.Add(new Diagnostic(Severity.Error, f, pos.Line, pos.Col, msg));
    }

    void Warn(SrcPos pos, string msg)
    {
        var f = pos.File >= 0 && pos.File < files.Count ? files[pos.File] : "";
        diags.Add(new Diagnostic(Severity.Warning, f, pos.Line, pos.Col, msg));
    }

    bool HasErrors => diags.Any(d => d.Severity == Severity.Error);

    // ------------------------------------------------------------ declaration d'un nom

    void Define(Scope s, Sym sym)
    {
        sym.Scope = s;
        if (s.Syms.TryGetValue(sym.Name, out var old))
        {
            Err(sym.Pos, $"'{sym.Orig}' est deja declare dans ce bloc ({old.KindName})");
            return;
        }
        s.Syms[sym.Name] = sym;
    }

    /// <summary>Declare le contenu d'un bloc (liste de declarations + instructions) dans la portee s.</summary>
    void DeclareBlockContents(Scope s, List<Decl> decls, IEnumerable<Stmt> stmts, ClassSym? owner)
    {
        var procs = new List<ProcSym>();
        var classes = new List<ClassSym>();
        var switches = new List<SwitchSym>();
        var allDecls = new List<Decl>(decls);
        // modules externes : substitution textuelle des declarations
        for (int i = 0; i < allDecls.Count; i++)
        {
            if (allDecls[i] is ExternalDecl ed)
            {
                var mods = LoadExternal(ed);
                allDecls.InsertRange(i + 1, mods);
            }
        }
        foreach (var d in allDecls)
        {
            switch (d)
            {
                case VarDecl vd:
                    foreach (var it in vd.Items)
                    {
                        var v = new VarSym { Name = it.Name, Orig = it.Orig, Pos = it.Pos, Spec = vd.Type, OwnerClass = owner, IsConst = it.Const != null, ConstExpr = it.Const };
                        v.Slot = s.Frame.AllocSlot();
                        Define(s, v);
                    }
                    break;
                case ArrayDecl ad:
                    foreach (var seg in ad.Segments)
                        foreach (var n in seg.Names)
                        {
                            var a = new ArraySym { Name = n.Name, Orig = n.Orig, Pos = n.Pos, Spec = ad.Type, Dims = seg.Bounds.Count, Segment = seg, OwnerClass = owner };
                            a.Slot = s.Frame.AllocSlot();
                            Define(s, a);
                        }
                    break;
                case SwitchDecl sd:
                    {
                        var sw = new SwitchSym { Name = sd.Name, Orig = sd.Orig, Pos = sd.Pos, Decl = sd, Frame = s.Frame, OwnerClass = owner };
                        Define(s, sw);
                        switches.Add(sw);
                        break;
                    }
                case ProcDecl pd:
                    {
                        var p = new ProcSym { Name = pd.Name, Orig = pd.Orig, Pos = pd.Pos, Decl = pd, OwnerClass = owner };
                        p.Info = new ProcInfo { Id = prog.Procs.Count, Name = pd.Orig };
                        prog.Procs.Add(p.Info);
                        Define(s, p);
                        procs.Add(p);
                        break;
                    }
                case ClassDecl cd:
                    {
                        var c = new ClassSym { Name = cd.Name, Orig = cd.Orig, Pos = cd.Pos, Decl = cd, OwnerClass = owner, IsSystem = cd.FromPrelude };
                        Define(s, c);
                        classes.Add(c);
                        break;
                    }
                case ExternalDecl:
                    break;
            }
        }
        // etiquettes et temporaires des instructions
        DeclareStatements(s, stmts);
        // classes : disposition (prefixes d'abord)
        foreach (var c in classes) LayoutClass(c);
        // blocs prefixes de ce niveau (classes anonymes)
        // (crees par DeclareStatements)
        foreach (var p in procs) DeclareProcedure(p, s);
        foreach (var sw in switches) sw.UnitScope = null;
    }

    List<Decl> LoadExternal(ExternalDecl ed)
    {
        var result = new List<Decl>();
        foreach (var item in ed.Items)
        {
            if (ed.Spec != null && ed.Kind != null)
            {
                // procedure non Simula : non supportee (pas de code natif externe)
                Err(item.Pos, $"procedure externe de genre '{ed.Kind}' : seul le code Simula est accepte");
                continue;
            }
            var key = item.Ident ?? item.Name;
            if (!includedModules.Add(key)) continue;
            var fileName = item.Ident ?? item.Name + ".sim";
            var srcFile = includeResolver?.Invoke(fileName, files[item.Pos.File]);
            if (srcFile == null && item.Ident == null) srcFile = includeResolver?.Invoke(item.Name + ".SIM", files[item.Pos.File]);
            if (srcFile == null)
            {
                Err(item.Pos, $"module externe '{fileName}' introuvable");
                continue;
            }
            int fi = files.Count;
            files.Add(srcFile.Path);
            var lx = new Lexer(srcFile.Text, fi, Err);
            var toks = lx.Tokenize();
            var ps = new Parser(toks, Err);
            var (progStmt, exts, mods) = ps.ParseProgram();
            if (progStmt != null) { Err(item.Pos, $"le module '{fileName}' contient un programme et non une declaration"); continue; }
            result.AddRange(exts);
            result.AddRange(mods);
            bool found = mods.Any(m => (m is ClassDecl c && c.Name == item.Name) || (m is ProcDecl p && p.Name == item.Name));
            if (!found) Err(item.Pos, $"le module '{fileName}' ne declare pas '{item.Name}'");
            foreach (var m in mods)
                if (ed.IsClass != (m is ClassDecl) && (m is ClassDecl c2 ? c2.Name : (m as ProcDecl)?.Name) == item.Name)
                    Err(item.Pos, ed.IsClass ? $"'{item.Name}' n'est pas une classe" : $"'{item.Name}' n'est pas une procedure");
        }
        return result;
    }

    // ------------------------------------------------------------ instructions : etiquettes, temporaires, blocs

    Scope LabelOwner(Scope s) => s;

    void DeclareLabels(Scope s, Stmt st)
    {
        foreach (var l in st.Labels)
        {
            var ls = new LabelSym { Name = l.Name, Orig = l.Orig, Pos = l.Pos, Id = nextLabelId++, Frame = s.Frame };
            prog.LabelNames.Add(l.Orig);
            if (s.Frame.Kind == FKind.ClassBody && s is FrameScope) ls.OwnerClass = s.Frame.Class;
            Define(s, ls);
        }
    }

    void DeclareStatements(Scope s, IEnumerable<Stmt> stmts)
    {
        foreach (var st in stmts) DeclareStmt(s, st);
    }

    void DeclareStmt(Scope s, Stmt st)
    {
        DeclareLabels(s, st);
        switch (st)
        {
            case BlockStmt b:
                if (b.Prefix != null) DeclarePrefixedBlock(b, s);
                else if (b.Decls.Count > 0) DeclareSubBlock(b, s);
                else DeclareStatements(s, b.Stmts);
                break;
            case IfStmt i:
                DeclareStmt(s, i.Then);
                if (i.Else != null) DeclareStmt(s, i.Else);
                break;
            case WhileStmt w:
                DeclareStmt(s, w.Body);
                break;
            case ForStmt f:
                {
                    if (f.Elems.Count > 1) tempOf[f] = s.Frame.AllocSlot();
                    foreach (var e in f.Elems)
                        if (e.Kind == ForKind.StepUntil) tempOf[e] = s.Frame.AllocSlot();
                    var ls = new LabelScope { Parent = s, Frame = s.Frame, Label = "for" };
                    scopeOf[f] = ls;
                    DeclareStmt(ls, f.Body);
                    break;
                }
            case InspectStmt ins:
                {
                    tempOf[ins] = s.Frame.AllocSlot();
                    if (ins.DoBody != null)
                    {
                        var cs = new ConnScope { Parent = s, Frame = s.Frame, TempSlot = tempOf[ins], Label = "inspect" };
                        scopeOf[ins] = cs;
                        var ls = new LabelScope { Parent = cs, Frame = s.Frame };
                        bodyScopeOf[ins] = ls;
                        DeclareStmt(ls, ins.DoBody);
                    }
                    foreach (var w in ins.Whens)
                    {
                        var cs = new ConnScope { Parent = s, Frame = s.Frame, TempSlot = tempOf[ins], Label = "when" };
                        scopeOf[w] = cs;
                        var ls = new LabelScope { Parent = cs, Frame = s.Frame };
                        bodyScopeOf[w] = ls;
                        DeclareStmt(ls, w.Body);
                    }
                    if (ins.Otherwise != null) DeclareStmt(s, ins.Otherwise);
                    break;
                }
        }
    }

    void DeclareSubBlock(BlockStmt b, Scope parent)
    {
        var fs = new FrameScope { Parent = parent, Kind = FKind.Block, Depth = parent.Frame.Depth + 1, BlockNode = b, IsPrelude = parent.Frame.IsPrelude, Label = "bloc" };
        fs.Block = new BlockInfo { Name = $"bloc (ligne {b.Pos.Line})" };
        prog.Blocks.Add(fs.Block);
        scopeOf[b] = fs;
        DeclareBlockContents(fs, b.Decls, b.Stmts, null);
    }

    void DeclarePrefixedBlock(BlockStmt b, Scope parent)
    {
        var c = new ClassSym
        {
            Name = "§bloc" + b.Pos.Line + "_" + b.Pos.Col, Orig = $"bloc prefixe par {b.Prefix} (ligne {b.Pos.Line})",
            Pos = b.Pos, IsPrefixedBlock = true, PrefixedBlock = b, Scope = parent
        };
        prefixedClassOf[b] = c;
        LayoutClass(c);
        scopeOf[b] = c.Body;
    }

    // ------------------------------------------------------------ procedures

    void DeclareProcedure(ProcSym p, Scope s)
    {
        var pd = p.Decl;
        var fs = new FrameScope { Parent = s, Kind = FKind.Procedure, Depth = s.Frame.Depth + 1, Proc = p, IsPrelude = s.Frame.IsPrelude, Label = "procedure " + p.Orig };
        p.Body = fs;
        foreach (var spec in pd.Params)
        {
            var ps = new ParamSym { Name = spec.Name, Orig = spec.Orig, Pos = spec.Pos, Spec = spec, Kind = spec.Kind };
            ps.Slot = fs.AllocSlot();
            Define(fs, ps);
            p.Params.Add(ps);
        }
        p.Info.NParams = p.Params.Count;
        if (pd.Type != null)
        {
            p.Info.ResultSlot = fs.AllocSlot();
        }
        var ms = new MergedScope { Parent = fs, Frame = fs, Label = "corps" };
        scopeOf[pd] = ms;
        if (pd.Body is BlockStmt b && b.Prefix == null && b.Decls.Count > 0)
        {
            scopeOf[b] = ms;
            DeclareLabels(ms, b);
            DeclareBlockContents(ms, b.Decls, b.Stmts, null);
        }
        else if (pd.Body != null)
        {
            DeclareStmt(ms, pd.Body);
        }
    }

    // ------------------------------------------------------------ classes

    ClassSym? FindPrefixClass(string name, Scope declScope, SrcPos pos)
    {
        // le prefixe doit etre une classe du meme bloc ; classes systeme : environnement
        // (dans un corps de classe ou un bloc prefixe, le bloc comprend les niveaux de prefixe)
        if (declScope is FrameScope cfs && cfs.Class != null && cfs.Kind is FKind.ClassBody or FKind.PrefixedBlock)
        {
            for (var k = cfs.Class.Prefix; k != null; k = k.Prefix)
                if (k.Attrs.TryGetValue(name, out var inh) && inh is ClassSym ic) { if (!declScope.Syms.ContainsKey(name)) return ic; }
        }
        if (declScope.Syms.TryGetValue(name, out var s))
        {
            if (s is ClassSym c) return c;
            Err(pos, $"'{name}' n'est pas une classe");
            return null;
        }
        // portees de connexion / fictives : remonter jusqu'au cadre
        for (var sc = declScope.Parent; sc != null; sc = sc.Parent)
        {
            if (sc.Syms.TryGetValue(name, out s))
            {
                if (s is ClassSym c)
                {
                    if (c.IsSystem || sc == env || sc.Frame == declScope.Frame) return c;
                    Err(pos, $"la classe prefixe '{c.Orig}' doit etre declaree dans le meme bloc");
                    return c;
                }
                Err(pos, $"'{name}' n'est pas une classe");
                return null;
            }
        }
        Err(pos, $"classe '{name}' inconnue");
        return null;
    }

    void LayoutClass(ClassSym c)
    {
        if (c.LaidOut) return;
        if (c.Laying) { Err(c.Pos, $"la classe '{c.Orig}' figure dans sa propre suite de prefixes"); return; }
        c.Laying = true;
        var declScope = c.Scope;
        string? prefixName = c.Decl?.Prefix ?? c.PrefixedBlock?.Prefix;
        SrcPos prefixPos = c.Decl?.PrefixPos ?? c.PrefixedBlock!.PrefixPos;
        ClassSym pre = root;
        if (prefixName != null)
        {
            var pc = FindPrefixClass(prefixName, declScope, prefixPos);
            if (pc != null)
            {
                LayoutClass(pc);
                if (!pc.LaidOut) pc = null;
            }
            if (pc != null) pre = pc;
        }
        c.Prefix = pre;
        pre.Subclasses.Add(c);
        c.Level = pre.Level + 1;
        c.SlotStart = pre.SlotEnd;
        var fs = new FrameScope
        {
            Parent = declScope, Kind = c.IsPrefixedBlock ? FKind.PrefixedBlock : FKind.ClassBody,
            Depth = declScope.Frame.Depth + 1, Class = c, NSlots = c.SlotStart, IsPrelude = c.IsSystem || declScope.Frame.IsPrelude,
            Label = c.IsPrefixedBlock ? c.Orig : "classe " + c.Orig
        };
        c.Body = fs;
        c.Info = new ClassInfo { Id = prog.Classes.Count, Name = c.Orig, IsPrefixedBlock = c.IsPrefixedBlock, Level = c.Level };
        prog.Classes.Add(c.Info);
        c.Info.Prefix = pre.Info;
        c.AllParams.AddRange(pre.AllParams);
        c.AllVirtuals.AddRange(pre.AllVirtuals);
        var cd = c.Decl;
        if (cd != null)
        {
            foreach (var spec in cd.Params)
            {
                var ps = new ParamSym { Name = spec.Name, Orig = spec.Orig, Pos = spec.Pos, Spec = spec, Kind = spec.Kind, IsClassParam = true, OwnerClass = c };
                if (spec.Kind is ParamKind.Procedure or ParamKind.Label or ParamKind.Switch)
                    Err(spec.Pos, "les parametres procedure, etiquette et aiguillage sont interdits pour les classes");
                ps.Slot = fs.AllocSlot();
                Define(fs, ps);
                c.Params.Add(ps);
                c.AllParams.Add(ps);
            }
            foreach (var h in cd.Hidden) c.HiddenHere.Add(h.Name);
            foreach (var pr in cd.Protected) c.ProtectedHere.Add(pr.Name);
            foreach (var vs in cd.Virtuals)
            {
                var inherited = c.AllVirtuals.FirstOrDefault(v => v.Name == vs.Name);
                if (inherited != null) { Err(vs.Pos, $"'{vs.Orig}' est deja virtuel dans un prefixe"); continue; }
                var v = new VirtualSym { Name = vs.Name, Orig = vs.Orig, Pos = vs.Pos, Spec = vs, Kind = vs.Kind, OwnerClass = c, Index = c.AllVirtuals.Count, Scope = fs };
                c.AllVirtuals.Add(v);
            }
        }
        // corps : declarations
        List<Decl> decls = cd?.Decls ?? c.PrefixedBlock!.Decls;
        var stmts = cd != null ? cd.Before.Concat(cd.After) : c.PrefixedBlock!.Stmts;
        DeclareBlockContents(fs, decls, stmts, c);
        // virtuels non apparies : visibles comme attributs
        foreach (var v in c.AllVirtuals.Where(v => v.OwnerClass == c))
            if (!fs.Syms.ContainsKey(v.Name)) fs.Syms[v.Name] = v;
            else
            {
                // declaration correspondante au meme niveau
                var m = fs.Syms[v.Name];
                SetMatch(m, v);
            }
        // correspondances des virtuels herites
        foreach (var kv in fs.Syms.ToList())
        {
            var inh = pre.AllVirtuals.FirstOrDefault(v => v.Name == kv.Key);
            if (inh != null && kv.Value is not VirtualSym) SetMatch(kv.Value, inh);
        }
        c.Attrs = fs.Syms;
        c.SlotEnd = fs.NSlots;
        // informations d'execution
        c.Info.NSlots = c.SlotEnd;
        var chain = new ClassInfo[c.Level + 1];
        for (var k = c; k != null; k = k.Prefix) chain[k.Level] = k.Info;
        c.Info.Chain = chain;
        c.Info.ParamSlots = c.AllParams.Select(p => p.Slot).ToArray();
        var vt = new VirtualEntry[c.AllVirtuals.Count];
        for (int i = 0; i < vt.Length; i++)
            vt[i] = i < pre.Info.VTable.Length ? pre.Info.VTable[i] : new VirtualEntry();
        foreach (var kv in fs.Syms)
        {
            switch (kv.Value)
            {
                case ProcSym p when p.Matches != null: vt[p.Matches.Index] = new VirtualEntry { Proc = p.Info }; break;
                case LabelSym l when l.Matches != null: vt[l.Matches.Index] = new VirtualEntry { LabelId = l.Id }; break;
                case SwitchSym sw when sw.Matches != null: vt[sw.Matches.Index] = new VirtualEntry(); pendingSwitchVirtuals.Add((c, sw)); break;
            }
        }
        c.Info.VTable = vt;
        c.Info.Attrs = pre.Info.Attrs.Concat(fs.Syms.Values.Select(DebugVar).Where(v => v != null).Select(v => v!))
            .Where(v => !v.Name.EndsWith("__")).ToList();
        c.Laying = false;
        c.LaidOut = true;
    }

    readonly List<(ClassSym Cls, SwitchSym Sw)> pendingSwitchVirtuals = new();

    void SetMatch(Sym m, VirtualSym v)
    {
        switch (m)
        {
            case ProcSym p when v.Kind == ParamKind.Procedure: p.Matches = v; break;
            case LabelSym l when v.Kind == ParamKind.Label: l.Matches = v; break;
            case SwitchSym s when v.Kind == ParamKind.Switch: s.Matches = v; break;
            default: Err(m.Pos, $"'{m.Orig}' ne correspond pas au genre de la quantite virtuelle ({KindOfVirtual(v)})"); break;
        }
    }

    static string KindOfVirtual(VirtualSym v) => v.Kind switch
    {
        ParamKind.Label => "etiquette", ParamKind.Switch => "aiguillage", _ => "procedure"
    };

    VarDebug? DebugVar(Sym s) => s switch
    {
        VarSym v when !v.IsHiddenTemp => new VarDebug(v.Orig, v.Slot, v.Spec.ToString(), 0),
        ArraySym a => new VarDebug(a.Orig, a.Slot, a.Spec + " array", 1),
        ParamSym p => new VarDebug(p.Orig, p.Slot, (p.Spec.Type?.ToString() ?? "") + (p.Kind == ParamKind.Array ? " array" : p.Kind == ParamKind.Procedure ? " procedure" : p.Kind == ParamKind.Label ? "label" : p.Kind == ParamKind.Switch ? "switch" : ""), p.IsName ? (byte)2 : (byte)0),
        _ => null
    };

    // ------------------------------------------------------------ racine

    void CreateRoot()
    {
        root = new ClassSym { Name = "§racine", Orig = "(classe racine)", IsRoot = true, LaidOut = true, Level = 0, IsSystem = true };
        root.Info = new ClassInfo { Id = prog.Classes.Count, Name = "(racine)", Level = 0 };
        root.Info.Chain = new[] { root.Info };
        prog.Classes.Add(root.Info);
        root.Body = new FrameScope { Kind = FKind.ClassBody, Depth = 1, Class = root };
        var det = new DetachSym { Name = "detach", Orig = "detach", OwnerClass = root, Scope = root.Body };
        root.Body.Syms["detach"] = det;
        root.Attrs = root.Body.Syms;
    }
}
