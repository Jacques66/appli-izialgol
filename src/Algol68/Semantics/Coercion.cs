using System.Numerics;
using Studio.Algol68.Exec;
using Studio.Algol68.Runtime;
using Studio.Algol68.Syntax;

namespace Studio.Algol68.Semantics;

public enum StepKind { Deproc, Deref, Unite, Reunite, Widen, RowValue, RowName, Void }

public readonly record struct Step(StepKind Kind, Mode From, Mode To, int Dims = 0, bool FromRow = false);

public sealed partial class Compiler
{
    static string StrengthName(Strength s) => s switch
    {
        Strength.Soft => "faible (soft)", Strength.Weak => "weak", Strength.Meek => "meek", Strength.Firm => "ferme (firm)", _ => "fort (strong)"
    };

    public static bool IsFlexible(Mode? m) => m is SpecialMode sm && sm.Kind is SpecialKind.Skip or SpecialKind.Nil or SpecialKind.Jump or SpecialKind.Vacuum;

    // ------------------------------------------------------------------ predicates

    public bool IsOutAcceptable(Mode m, HashSet<Mode>? seen = null)
    {
        switch (m)
        {
            case PrimMode p: return p.Kind is not (Prim.Void or Prim.File or Prim.Channel or Prim.Sema);
            case RowMode r: return IsOutAcceptable(r.Elem, seen);
            case FlexMode f: return IsOutAcceptable(f.Row.Elem, seen);
            case StructMode s:
                seen ??= new HashSet<Mode>();
                if (!seen.Add(s)) return false;
                return s.Fields.All(x => IsOutAcceptable(x.Mode, seen));
            case UnionMode u: return u.Members.All(x => IsOutAcceptable(x, seen) || x.IsVoid);
            case ProcMode pm: return ReferenceEquals(pm, M.ProcRefFileVoid);
        }
        return false;
    }

    public bool IsInAcceptable(Mode m)
    {
        if (ReferenceEquals(m, M.ProcRefFileVoid) || ReferenceEquals(m, M.Format)) return true;
        if (m is RefMode r) return IsReadable(r.Sub, new HashSet<Mode>());
        return false;
    }
    bool IsReadable(Mode m, HashSet<Mode> seen)
    {
        switch (m)
        {
            case PrimMode p: return p.Kind is not (Prim.Void or Prim.File or Prim.Channel or Prim.Sema or Prim.Format);
            case RowMode r: return IsReadable(r.Elem, seen);
            case FlexMode f: return IsReadable(f.Row.Elem, seen);
            case StructMode s: if (!seen.Add(s)) return false; return s.Fields.All(x => IsReadable(x.Mode, seen));
            case UnionMode u: return u.Members.All(x => IsReadable(x, seen));
        }
        return false;
    }

    Mode? Widen1(Mode m)
    {
        if (m is PrimMode p)
        {
            switch (p.Kind)
            {
                case Prim.Int: return M.RealOf(p.Size);
                case Prim.Real: return p.Size == 0 ? M.Compl : p.Size == 1 ? M.LongCompl : M.LongLongCompl;
                case Prim.Bits: return M.RowBool;
                case Prim.Bytes: return M.RowChar;
            }
        }
        return null;
    }

    public bool FirmlyRelated(Mode a, Mode b) =>
        FindCoercion(a, b, Strength.Firm) != null || FindCoercion(b, a, Strength.Firm) != null;

    // ------------------------------------------------------------------ recherche de coercitions

    public List<Step>? FindCoercion(Mode from, Mode to, Strength s)
    {
        if (ReferenceEquals(from, to)) return new List<Step>();
        if (from == M.Any || to == M.Any) return new List<Step>();
        if (from is SpecialMode sp)
        {
            switch (sp.Kind)
            {
                case SpecialKind.Jump: return new List<Step>();
                case SpecialKind.Skip: return s == Strength.Strong ? new List<Step>() : null;
                case SpecialKind.Nil: return to is RefMode ? new List<Step>() : null;
                case SpecialKind.Vacuum: return to.IsRow || (to.IsVoid) ? new List<Step>() : null;
            }
        }
        if (to.IsVoid)
        {
            if (s != Strength.Strong) return null;
            var l = new List<Step>();
            if (from is ProcMode pm && pm.Params.Length == 0) l.Add(new Step(StepKind.Deproc, from, pm.Result));
            l.Add(new Step(StepKind.Void, from, to));
            return l;
        }
        var steps = new List<Step>();
        var cur = from;
        for (int guard = 0; guard < 64; guard++)
        {
            var fin = TryFinal(cur, to, s);
            if (fin != null) { steps.AddRange(fin); return steps; }
            if (cur is ProcMode pm && pm.Params.Length == 0) { steps.Add(new Step(StepKind.Deproc, cur, pm.Result)); cur = pm.Result; continue; }
            if (cur is RefMode rm && s != Strength.Soft)
            {
                var v = M.Normalize(rm.Sub);
                steps.Add(new Step(StepKind.Deref, cur, v));
                cur = v; continue;
            }
            return null;
        }
        return null;
    }

    List<Step>? TryFinal(Mode cur, Mode to, Strength s)
    {
        if (ReferenceEquals(cur, to)) return new List<Step>();
        if (to == M.Rows && cur.IsRow && s >= Strength.Meek) return new List<Step>();
        if (s >= Strength.Firm)
        {
            if (to is UnionMode tu)
            {
                if (cur is UnionMode cu)
                {
                    if (cu.Members.All(x => tu.IndexOf(x) >= 0)) return new List<Step> { new(StepKind.Reunite, cur, to) };
                }
                else if (tu.IndexOf(cur) >= 0) return new List<Step> { new(StepKind.Unite, cur, to) };
            }
            if (to == M.OutType && IsOutAcceptable(cur))
                return new List<Step> { new(cur is UnionMode ? StepKind.Reunite : StepKind.Unite, cur, to) };
            if (to == M.InType && IsInAcceptable(cur))
                return new List<Step> { new(StepKind.Unite, cur, to) };
        }
        if (s == Strength.Strong)
        {
            var direct = TryRow(cur, to);
            if (direct != null) return direct;
            var w = cur;
            var ws = new List<Step>();
            for (int g = 0; g < 4; g++)
            {
                var next = Widen1(w);
                if (next == null) break;
                ws.Add(new Step(StepKind.Widen, w, next));
                w = next;
                if (ReferenceEquals(w, to)) return ws;
                var r2 = TryRow(w, to);
                if (r2 != null) { ws.AddRange(r2); return ws; }
            }
            return TryRow(cur, to);
        }
        return null;
    }

    List<Step>? TryRow(Mode cur, Mode to)
    {
        if (to is RowMode rt)
        {
            var sub = TryFinalNoRow(cur, rt.Elem);
            if (sub != null) { sub.Add(new Step(StepKind.RowValue, cur, to, rt.Dims, false)); return sub; }
            if (cur is RowMode cr && cr.Dims < rt.Dims && ReferenceEquals(cr.Elem, rt.Elem))
                return new List<Step> { new(StepKind.RowValue, cur, to, rt.Dims - cr.Dims, true) };
            return null;
        }
        if (to is RefMode tr && tr.Sub is RowMode rt2 && cur is RefMode cref)
        {
            if (ReferenceEquals(cref.Sub, rt2.Elem)) return new List<Step> { new(StepKind.RowName, cur, to, rt2.Dims, false) };
            if (cref.Sub is RowMode cr2 && cr2.Dims < rt2.Dims && ReferenceEquals(cr2.Elem, rt2.Elem))
                return new List<Step> { new(StepKind.RowName, cur, to, rt2.Dims - cr2.Dims, true) };
        }
        return null;
    }

    List<Step>? TryFinalNoRow(Mode cur, Mode to)
    {
        if (ReferenceEquals(cur, to)) return new List<Step>();
        if (to is UnionMode tu)
        {
            if (cur is UnionMode cu) { if (cu.Members.All(x => tu.IndexOf(x) >= 0)) return new List<Step> { new(StepKind.Reunite, cur, to) }; }
            else if (tu.IndexOf(cur) >= 0) return new List<Step> { new(StepKind.Unite, cur, to) };
        }
        if (to == M.OutType && IsOutAcceptable(cur)) return new List<Step> { new(cur is UnionMode ? StepKind.Reunite : StepKind.Unite, cur, to) };
        if (to == M.InType && IsInAcceptable(cur)) return new List<Step> { new(StepKind.Unite, cur, to) };
        var w = cur; var ws = new List<Step>();
        for (int g = 0; g < 4; g++)
        {
            var next = Widen1(w);
            if (next == null) break;
            ws.Add(new Step(StepKind.Widen, w, next));
            w = next;
            if (ReferenceEquals(w, to)) return ws;
        }
        return null;
    }

    // ------------------------------------------------------------------ application

    public Node Coerce(Node n, Mode from, Mode to, Strength s, Ast pos)
    {
        if (ReferenceEquals(from, to)) { n.Mode ??= to; return n; }
        var steps = FindCoercion(from, to, s);
        if (steps == null)
        {
            Error(pos, $"mode {from} ne peut pas être converti en {to} dans un contexte {StrengthName(s)}");
            n.Mode = to;
            return n;
        }
        foreach (var st in steps) n = ApplyStep(n, st, pos);
        n.Mode = to;
        return n;
    }

    Node ApplyStep(Node n, Step st, Ast pos)
    {
        Node r;
        switch (st.Kind)
        {
            case StepKind.Deproc:
                r = new DeprocNode(n, cur.Level);
                break;
            case StepKind.Deref:
                r = n switch
                {
                    SlotNode sn when !sn.NeedsObj || true => new DerefSlotNode(sn.Hops, sn.Slot, st.To, false) { Name = sn.Name },
                    Subscript1NameNode => FuseSubscript(n, st.To),
                    SelectNameNode => FuseSelect(n, st.To),
                    _ => new DerefNode(n, st.To, false),
                };
                break;
            case StepKind.Unite:
                r = st.From is UnionMode ? new ReuniteNode(n) : new UniteNode(n, st.From);
                break;
            case StepKind.Reunite:
                r = new ReuniteNode(n);
                break;
            case StepKind.Widen:
                r = MakeWiden(n, st.From, st.To);
                break;
            case StepKind.RowValue:
                {
                    var elem = st.To.AsRow!.Elem;
                    r = new RowValueNode(n, st.Dims, st.FromRow, elem);
                    break;
                }
            case StepKind.RowName:
                r = new RowNameNode(n, st.Dims, st.FromRow);
                r.Transient = st.From is RefMode { Sub: FlexMode } || IsTransient(n);
                break;
            case StepKind.Void:
                r = new VoidNode(n);
                break;
            default: r = n; break;
        }
        r.SetPos(n.Line != 0 ? n : NodeAt(pos));
        r.Mode = st.To;
        return r;
    }

    static Node NodeAt(Ast a) { var c = new ConstNode(default); c.SetPos(a); return c; }

    Node FuseSubscript(Node n, Mode valMode) => n is Subscript1NameNode s ? s.MakeDeref(valMode) : new DerefNode(n, valMode, false);
    Node FuseSelect(Node n, Mode valMode) => n is SelectNameNode s ? s.MakeDeref(valMode) : new DerefNode(n, valMode, false);

    Node MakeWiden(Node n, Mode from, Mode to)
    {
        var p = (PrimMode)from;
        switch (p.Kind)
        {
            case Prim.Int:
                if (p.Size == 0) return new WidenIntRealNode(n);
                {
                    int digits = Prelude.RealDigits(p.Size);
                    return new FuncNode(n, (v, at) => Value.Obj(MpReal.FromBig((BigInteger)v.O!, digits)));
                }
            case Prim.Real:
                if (p.Size == 0) return new FuncNode(n, (v, at) => Value.Obj(new[] { v, Value.R(0) }));
                {
                    int digits = Prelude.RealDigits(p.Size);
                    return new FuncNode(n, (v, at) => Value.Obj(new[] { v, Value.Obj(MpReal.Zero(digits)) }));
                }
            case Prim.Bits:
                {
                    int w = Prelude.BitsWidthOf(p.Size);
                    return new FuncNode(n, (v, at) => Value.Obj(Prelude.BitsToRowBool(v, p.Size)));
                }
            case Prim.Bytes:
                return new FuncNode(n, (v, at) => RT.StrValue((string)v.O!));
        }
        return n;
    }

    // ------------------------------------------------------------------ equilibrage

    /// <summary>Choisit un mode vers lequel tous les modes donnes peuvent etre fortement convertis (RR 3.2.1 : balancing).</summary>
    public Mode? Balance(List<Mode?> modes, Ast pos)
    {
        var real = modes.Where(m => m != null && !IsFlexible(m)).Cast<Mode>().ToList();
        if (real.Count == 0)
        {
            if (modes.Any(m => m is SpecialMode { Kind: SpecialKind.Nil })) return null;
            return null;
        }
        if (real.Any(m => m == M.Any)) return M.Any;
        // candidats : chaque mode et ses formes deproceduree/dereferencees, par profondeur croissante
        var chains = real.Select(Chain).ToList();
        int maxDepth = chains.Max(c => c.Count);
        for (int d = 0; d < maxDepth; d++)
            foreach (var c in chains)
            {
                if (d >= c.Count) continue;
                var cand = c[d];
                bool ok = true;
                foreach (var m in modes)
                {
                    if (m == null) continue;
                    if (FindCoercion(m, cand, Strength.Strong) == null) { ok = false; break; }
                }
                if (ok) return cand;
            }
        Error(pos, "les branches de la clause n'ont pas de mode commun : " + string.Join(", ", real.Distinct().Select(m => m.ToString())));
        return M.Any;
    }

    List<Mode> Chain(Mode m)
    {
        var l = new List<Mode> { m };
        var cur = m;
        for (int g = 0; g < 32; g++)
        {
            if (cur is ProcMode pm && pm.Params.Length == 0) cur = pm.Result;
            else if (cur is RefMode rm) cur = M.Normalize(rm.Sub);
            else break;
            l.Add(cur);
        }
        return l;
    }

    /// <summary>Mode obtenu par deproceduring complet (contexte soft).</summary>
    Mode DeprocAll(Mode m)
    {
        for (int g = 0; g < 32 && m is ProcMode pm && pm.Params.Length == 0; g++) m = pm.Result;
        return m;
    }

    /// <summary>Contexte weak : deproc/deref jusqu'a une rangee/structure, en gardant un REF si possible.</summary>
    Mode? WeakTarget(Mode m, Func<Mode, bool> accept)
    {
        var cur = m;
        for (int g = 0; g < 32; g++)
        {
            if (cur is RefMode rm && accept(rm.Sub)) return cur;
            if (accept(cur)) return cur;
            if (cur is ProcMode pm && pm.Params.Length == 0) { cur = pm.Result; continue; }
            if (cur is RefMode r2) { cur = M.Normalize(r2.Sub); continue; }
            return null;
        }
        return null;
    }

    /// <summary>Contexte meek : deproc/deref complet.</summary>
    Mode MeekValue(Mode m)
    {
        var cur = m;
        for (int g = 0; g < 32; g++)
        {
            if (cur is ProcMode pm && pm.Params.Length == 0) { cur = pm.Result; continue; }
            if (cur is RefMode r2) { cur = M.Normalize(r2.Sub); continue; }
            return cur;
        }
        return cur;
    }
}

public sealed partial class Compiler
{
    static bool IsTransient(Node? n)
    {
        for (int g = 0; n != null && g < 16; g++, n = n.Wrapped) if (n.Transient) return true;
        return false;
    }

    /// <summary>RR 2.1.3.6.c : un nom transitoire ne peut etre ni affecte, ni lie, ni rendu, ni operande.</summary>
    void CheckTransient(Node n, Ast pos, string usage)
    {
        if (IsTransient(n))
            Error(pos, $"nom transitoire (tranche ou sélection d'un nom FLEX) : il ne peut pas être {usage}");
    }
}
