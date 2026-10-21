using Studio.Simula.Runtime;

namespace Studio.Simula.Compiler;

public readonly record struct Found(Sym Sym, Scope Where, ConnScope? Conn, FrameScope Frame);

public sealed partial class SimCompiler
{
    static readonly HashSet<string> IntrinsicNames = new()
    {
        "abs", "sign", "entier", "mod", "rem", "max", "min", "sqrt", "sin", "cos", "tan", "cotan",
        "arcsin", "arccos", "arctan", "arctan2", "sinh", "cosh", "tanh", "ln", "log10", "exp",
        "char", "isochar", "rank", "isorank", "digit", "letter", "copy", "blanks", "upcase", "lowcase",
        "lowten", "decimalmark", "addepsilon", "subepsilon", "call", "resume", "sourceline", "error",
        "upperbound", "lowerbound", "datetime", "cputime", "clocktime",
    };

    void MarkIntrinsics()
    {
        foreach (var s in env.Syms.Values)
            if (s is ProcSym p && IntrinsicNames.Contains(p.Name)) p.Intrinsic = p.Name;
    }

    // ------------------------------------------------------------ protection context

    List<ClassSym> EnclosingClasses(Scope s)
    {
        var l = new List<ClassSym>();
        for (Scope? sc = s; sc != null; sc = sc.Parent)
            if (sc is FrameScope fs && fs.Class != null && fs.Kind is FKind.ClassBody or FKind.PrefixedBlock) l.Add(fs.Class);
        return l;
    }

    bool IsProtected(ClassSym k, Sym s, string name)
    {
        if (k.ProtectedHere.Contains(name)) return true;
        VirtualSym? v = s as VirtualSym ?? (s as ProcSym)?.Matches ?? (s as LabelSym)?.Matches ?? (s as SwitchSym)?.Matches;
        return v != null && v.OwnerClass != null && v.OwnerClass.ProtectedHere.Contains(name);
    }

    /// <summary>Looks up an attribute in class q and its prefixes (hidden / protected rules).</summary>
    Sym? FindAttr(ClassSym q, string name, bool remote, Scope from)
    {
        List<ClassSym>? ctx = null;
        for (var k = q; k != null; k = k.Prefix)
        {
            if (!k.Attrs.TryGetValue(name, out var s)) continue;
            if (remote && s is LabelSym or SwitchSym) continue;
            if (remote && s is VirtualSym vs && vs.Kind is ParamKind.Label or ParamKind.Switch) continue;
            // hidden: invisible from strictly inner subclasses
            bool hidden = false;
            for (var h = q; h != null; h = h.Prefix)
            {
                if (h != q && h.HiddenHere.Contains(name)) { hidden = true; break; }
                if (h == k) break;
            }
            if (hidden) continue;
            if (IsProtected(k, s, name))
            {
                ctx ??= EnclosingClasses(from);
                var defLevel = k;
                if (!ctx.Any(e => e.IsSubclassOf(defLevel))) continue;
            }
            return s;
        }
        return null;
    }

    /// <summary>Looks up an identifier from scope s.</summary>
    Found? Lookup(string name, Scope s, SrcPos pos, bool quiet = false)
    {
        for (Scope? sc = s; sc != null; sc = sc.Parent)
        {
            switch (sc)
            {
                case ConnScope cs:
                    if (cs.Qual != null)
                    {
                        var a = FindAttr(cs.Qual, name, remote: true, from: s);
                        if (a != null) return new Found(a, cs, cs, cs.Frame);
                    }
                    break;
                case FrameScope fs when fs.Kind is FKind.ClassBody or FKind.PrefixedBlock && fs.Class != null:
                    {
                        var a = FindAttr(fs.Class, name, remote: false, from: s);
                        if (a != null) return new Found(a, fs, null, fs);
                        break;
                    }
                default:
                    if (sc.Syms.TryGetValue(name, out var sym))
                        return new Found(sym, sc, null, sym is VarSym { IsOwn: true } or ArraySym { IsOwn: true } ? env : sc.Frame);
                    break;
            }
        }
        if (!quiet && !(Natives.Exists(name) && InPreludeCode())) Err(pos, $"undeclared identifier '{name}'");
        return null;
    }

    bool InPreludeCode()
    {
        for (Scope? sc = g.Scope; sc != null; sc = sc.Parent)
            if (sc is FrameScope fs) return fs.IsPrelude;
        return false;
    }

    ClassSym? LookupClass(string name, Scope s, SrcPos pos)
    {
        var f = Lookup(name, s, pos);
        if (f == null) return null;
        if (f.Value.Sym is ClassSym c) return c;
        Err(pos, $"'{name}' is not a class");
        return null;
    }

    int HopsTo(FrameScope f)
    {
        if (f.Depth == 0) return -1;
        int h = g.Frame.Depth - f.Depth;
        if (h < 0) { Err(curPos, $"access to a non-enclosing frame (internal error: {f.Label} from {g.Frame.Label})"); return 0; }
        return h;
    }

    // ------------------------------------------------------------ types

    SType ResolveSpec(TypeSpec? spec, Scope scope)
    {
        if (spec == null) return SType.Void;
        switch (spec.Base)
        {
            case BaseType.Integer: return SType.Int;
            case BaseType.ShortInteger: return SType.Short;
            case BaseType.Real: return SType.Real;
            case BaseType.LongReal: return SType.LReal;
            case BaseType.Boolean: return SType.Bool;
            case BaseType.Character: return SType.Char;
            case BaseType.Text: return SType.Text;
            case BaseType.Ref:
                {
                    var save = g;
                    // the lookup is done from the declaring scope
                    var gtmp = g ?? new Gen { Scope = scope, Frame = scope.Frame };
                    if (g == null) g = gtmp;
                    var c = LookupClass(spec.RefClass!, scope, spec.Pos);
                    g = save;
                    return c == null ? SType.Err : SType.Ref(c);
                }
        }
        return SType.Err;
    }

    SType TypeOf(VarSym v) => v.Type ??= ResolveSpec(v.Spec, v.Scope);
    SType ElemOf(ArraySym a) => a.Elem ??= ResolveSpec(a.Spec, a.Scope);
    SType ResultOf(ProcSym p) => p.Result ??= p.Decl.Type == null ? SType.Void : ResolveSpec(p.Decl.Type, p.Scope);

    void ResolveParam(ParamSym ps)
    {
        if (ps.Type != null) return;
        var spec = ps.Spec;
        SType t;
        switch (ps.Kind)
        {
            case ParamKind.Label: t = SType.LabelT; break;
            case ParamKind.Switch: t = SType.SwitchT; break;
            default: t = !spec.Specified ? SType.Err : spec.Type == null ? SType.Void : ResolveSpec(spec.Type, ps.Scope.Parent ?? ps.Scope); break;
        }
        ps.Type = t;
        bool valueType = t.IsArith || t.B is BaseType.Boolean or BaseType.Character;
        switch (spec.Mode)
        {
            case ParamMode.Name:
                ps.Mode = ParamMode.Name;
                break;
            case ParamMode.Value:
                if (ps.Kind == ParamKind.Simple && (valueType || t.B == BaseType.Text)) ps.Mode = ParamMode.Value;
                else if (ps.Kind == ParamKind.Array && valueType) ps.Mode = ParamMode.Value;
                else { Err(spec.Pos, $"call by value not allowed for '{spec.Orig}'"); ps.Mode = ParamMode.Default; }
                break;
            default:
                if (dialect != Dialect.Simula67)
                {
                    // Algol 60: parameters not in the value part are called by name;
                    // for arrays and procedures, name and reference are equivalent
                    ps.Mode = ps.Kind is ParamKind.Array or ParamKind.Procedure ? ParamMode.Default : ParamMode.Name;
                }
                else
                    ps.Mode = ps.Kind == ParamKind.Simple && valueType ? ParamMode.Value : ParamMode.Default;
                break;
        }
    }

    SType ParamType(ParamSym ps) { ResolveParam(ps); return ps.Type!; }

    static string TypeName(SType t) => t.ToString();

    // ------------------------------------------------------------ compatibility

    /// <summary>Innermost class including both qualifications (for if-expressions).</summary>
    static ClassSym? CommonClass(ClassSym? a, ClassSym? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        for (var k = a; k != null; k = k.Prefix)
            if (b.IsSubclassOf(k)) return k;
        return null;
    }

    static SType ArithMax(SType a, SType b)
    {
        if (a.B == BaseType.LongReal || b.B == BaseType.LongReal) return SType.LReal;
        if (a.IsReal || b.IsReal) return SType.Real;
        return SType.Int;
    }

    void Convert(SType from, SType to, SrcPos pos)
    {
        if (from.IsErr || to.IsErr) return;
        if (to.IsArith)
        {
            if (!from.IsArith) { Err(pos, $"type {from} is incompatible with {to}"); return; }
            if (to.IsReal) { if (from.IsIntLike) E(Op.I2R); return; }
            if (to.B == BaseType.ShortInteger) { E(from.IsReal ? Op.R2S : Op.I2S); return; }
            if (from.IsReal) E(Op.R2I);
            return;
        }
        if (to.B == BaseType.Text) { if (from.B != BaseType.Text) Err(pos, $"type {from} is incompatible with text"); return; }
        if (to.IsRef)
        {
            if (!from.IsRef) { Err(pos, $"type {from} is incompatible with {to}"); return; }
            if (from.Q == null || to.Q == null) return;
            if (from.Q.IsSubclassOf(to.Q)) return;
            if (to.Q.IsSubclassOf(from.Q)) { E(Op.ChkQual, to.Q.Info.Id); return; }
            Err(pos, $"unrelated qualifications: {from} and {to}");
            return;
        }
        if (from.B != to.B) Err(pos, $"type {from} is incompatible with {to}");
    }

    void ConvertToInt(SType t, SrcPos pos)
    {
        if (t.IsErr) return;
        if (!t.IsArith) { Err(pos, $"arithmetic expression expected (found {t})"); return; }
        if (t.IsReal) E(Op.R2I);
    }
}
