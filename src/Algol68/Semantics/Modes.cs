using System.Text;

namespace Studio.Algol68.Semantics;

public enum Prim { Int, Real, Bool, Char, Bits, Bytes, Void, Format, File, Channel, Sema }

/// <summary>Mode Algol 68. Les modes sont internes (canoniques) : l'egalite de reference vaut equivalence.</summary>
public abstract class Mode
{
    public bool Canonical;
    public string? Name;            // indicant ayant servi a le declarer (pour l'affichage)
    internal int Id;
    public override string ToString() => ModeTable.Show(this);

    bool? hasNames, needsCopy;
    /// <summary>Le mode contient des noms ou des procedures (controle de portee necessaire).</summary>
    public bool HasNames => hasNames ??= ComputeHasNames(new HashSet<Mode>());
    /// <summary>Valeur composite (copie a faire lors d'une liaison).</summary>
    public bool NeedsCopy => needsCopy ??= ComputeNeedsCopy(new HashSet<Mode>());

    bool ComputeHasNames(HashSet<Mode> seen)
    {
        if (!seen.Add(this)) return false;
        return this switch
        {
            RefMode => true,
            ProcMode => true,
            PrimMode p => p.Kind is Prim.Format or Prim.File, // un format contient des procedures (replicateurs)
            StructMode s => s.Fields.Any(f => f.Mode.ComputeHasNames(seen)),
            UnionMode u => u.Members.Any(m => m.ComputeHasNames(seen)),
            RowMode r => r.Elem.ComputeHasNames(seen),
            FlexMode f => f.Row.ComputeHasNames(seen),
            SpecialMode => true,
            _ => false,
        };
    }
    bool ComputeNeedsCopy(HashSet<Mode> seen)
    {
        if (!seen.Add(this)) return false;
        return this switch
        {
            StructMode => true,
            RowMode => true,
            FlexMode => true,
            UnionMode u => u.Members.Any(m => m.ComputeNeedsCopy(seen)),
            SpecialMode => true,
            _ => false,
        };
    }

    public bool IsRow => this is RowMode or FlexMode;
    public RowMode? AsRow => this switch { RowMode r => r, FlexMode f => f.Row, _ => null };
    public bool IsVoid => this is PrimMode { Kind: Prim.Void };
}

public sealed class PrimMode : Mode
{
    public Prim Kind; public int Size;
    public PrimMode(Prim k, int size = 0) { Kind = k; Size = size; }
}
public sealed class RefMode : Mode { public Mode Sub; public RefMode(Mode s) { Sub = s; } }
public sealed class ProcMode : Mode
{
    public Mode[] Params; public Mode Result;
    public ProcMode(Mode[] p, Mode r) { Params = p; Result = r; }
}
public sealed class StructMode : Mode
{
    public (Mode Mode, string Name)[] Fields;
    public StructMode((Mode, string)[] f) { Fields = f; }
    public int IndexOf(string name) { for (int i = 0; i < Fields.Length; i++) if (Fields[i].Name == name) return i; return -1; }
}
public sealed class UnionMode : Mode
{
    public Mode[] Members;
    public UnionMode(Mode[] m) { Members = m; }
    public int IndexOf(Mode m) { for (int i = 0; i < Members.Length; i++) if (ReferenceEquals(Members[i], m)) return i; return -1; }
}
public sealed class RowMode : Mode
{
    public int Dims; public Mode Elem;
    public RowMode(int d, Mode e) { Dims = d; Elem = e; }
}
public sealed class FlexMode : Mode { public RowMode Row; public FlexMode(RowMode r) { Row = r; } }
/// <summary>Indicant en cours de resolution.</summary>
public sealed class IndicantMode : Mode { public Mode? Target; public string Ind; public IndicantMode(string n) { Ind = n; } }

public enum SpecialKind
{
    Skip, Nil, Jump, Vacuum,  // modes "flexibles" (sans mode a priori)
    Rows,                     // tout mode rangee (operandes de LWB, UPB...)
    OutType, InType,          // unions etendues de print/read
    Any,                      // erreur deja signalee
    Label,
}
public sealed class SpecialMode : Mode { public SpecialKind Kind; public SpecialMode(SpecialKind k) { Kind = k; } }

public sealed class ModeTable
{
    readonly Dictionary<int, List<Mode>> buckets = new();
    int nextId = 1;

    public readonly Mode Int, Real, Bool, Char, Bits, Bytes, Void, Format, File, Channel, Sema;
    public readonly Mode LongInt, LongLongInt, LongReal, LongLongReal, LongBits, LongLongBits, LongBytes;
    public readonly Mode Compl, LongCompl, LongLongCompl;
    public readonly Mode RowChar, String, RefString, RefInt, RefReal, RefBool, RefChar, RefFile;
    public readonly Mode RowBool, RowInt, RowReal, RowString;
    public readonly Mode ProcRefFileVoid, ProcRefFileBool, ProcVoid;
    public readonly SpecialMode Skip = new(SpecialKind.Skip), Nil = new(SpecialKind.Nil), Jump = new(SpecialKind.Jump),
        Vacuum = new(SpecialKind.Vacuum), Rows = new(SpecialKind.Rows), OutType = new(SpecialKind.OutType),
        InType = new(SpecialKind.InType), Any = new(SpecialKind.Any), LabelMode = new(SpecialKind.Label);
    public readonly Mode RowOutType, RowInType;

    public ModeTable()
    {
        foreach (var s in new[] { Skip, Nil, Jump, Vacuum, Rows, OutType, InType, Any, LabelMode }) { s.Canonical = true; s.Id = nextId++; }
        Int = P(Prim.Int); Real = P(Prim.Real); Bool = P(Prim.Bool); Char = P(Prim.Char);
        Bits = P(Prim.Bits); Bytes = P(Prim.Bytes); Void = P(Prim.Void); Format = P(Prim.Format);
        File = P(Prim.File); Channel = P(Prim.Channel); Sema = P(Prim.Sema);
        LongInt = P(Prim.Int, 1); LongLongInt = P(Prim.Int, 2);
        LongReal = P(Prim.Real, 1); LongLongReal = P(Prim.Real, 2);
        LongBits = P(Prim.Bits, 1); LongLongBits = P(Prim.Bits, 2);
        LongBytes = P(Prim.Bytes, 1);
        Compl = ComplOf(0); LongCompl = ComplOf(1); LongLongCompl = ComplOf(2);
        Compl.Name = "COMPL"; LongCompl.Name = "LONG COMPL"; LongLongCompl.Name = "LONG LONG COMPL";
        RowChar = Row(1, Char);
        String = Intern(new FlexMode(new RowMode(1, Char)));
        String.Name = "STRING";
        RefString = Ref(String);
        RefInt = Ref(Int); RefReal = Ref(Real); RefBool = Ref(Bool); RefChar = Ref(Char); RefFile = Ref(File);
        RowBool = Row(1, Bool); RowInt = Row(1, Int); RowReal = Row(1, Real); RowString = Row(1, RowChar);
        ProcRefFileVoid = Proc(Void, RefFile);
        ProcRefFileBool = Proc(Bool, RefFile);
        ProcVoid = Proc(Void);
        RowOutType = Intern(new RowMode(1, OutType));
        RowInType = Intern(new RowMode(1, InType));
    }

    public Mode P(Prim k, int size = 0) => Intern(new PrimMode(k, size));
    public Mode Ref(Mode m) => Intern(new RefMode(m));
    public Mode Row(int dims, Mode e) => Intern(new RowMode(dims, e));
    public Mode Flex(Mode row) => Intern(new FlexMode((RowMode)row));
    public Mode Proc(Mode result, params Mode[] ps) => Intern(new ProcMode(ps, result));
    public Mode Struct(params (Mode, string)[] f) => Intern(new StructMode(f));
    public Mode ComplOf(int size) { var r = size == 0 ? Real : P(Prim.Real, size); return Struct((r, "re"), (r, "im")); }
    public Mode IntOf(int size) => size == 0 ? Int : P(Prim.Int, size);
    public Mode RealOf(int size) => size == 0 ? Real : P(Prim.Real, size);
    public Mode BitsOf(int size) => size == 0 ? Bits : P(Prim.Bits, size);

    /// <summary>Union aplatie et sans doublons ; une union d'un seul membre est ce membre.</summary>
    public Mode Union(IEnumerable<Mode> members)
    {
        var list = new List<Mode>();
        void Add(Mode m)
        {
            if (m is UnionMode u) { foreach (var x in u.Members) Add(x); return; }
            if (!list.Any(x => ReferenceEquals(x, m))) list.Add(m);
        }
        foreach (var m in members) Add(m);
        if (list.Count == 1) return list[0];
        return Intern(new UnionMode(list.ToArray()));
    }

    // ------------------------------------------------------------- normalisation (deflexage)

    readonly Dictionary<(Mode, bool), Mode> normCache = new();

    /// <summary>
    /// Mode d'une valeur : FLEX n'est conserve que sous REF (dans les champs/elements atteints depuis un nom).
    /// </summary>
    public Mode Normalize(Mode m, bool underRef = false)
    {
        if (normCache.TryGetValue((m, underRef), out var r)) return r;
        // les modes recursifs sont deja internes : on construit un graphe parallele
        var map = new Dictionary<(Mode, bool), Mode>();
        r = NormBuild(m, underRef, map);
        r = Intern(r);
        normCache[(m, underRef)] = r;
        return r;
    }

    Mode NormBuild(Mode m, bool underRef, Dictionary<(Mode, bool), Mode> map)
    {
        if (map.TryGetValue((m, underRef), out var done)) return done;
        if (NeedsNoNorm(m, underRef, new HashSet<(Mode, bool)>())) return m;
        switch (m)
        {
            case RefMode rm:
                {
                    var n = new RefMode(Void); map[(m, underRef)] = n;
                    n.Sub = NormBuild(rm.Sub, true, map);
                    return n;
                }
            case FlexMode fm:
                if (!underRef) return NormBuild(fm.Row, false, map);
                {
                    var n = new FlexMode(new RowMode(fm.Row.Dims, Void)); map[(m, underRef)] = n;
                    n.Row.Elem = NormBuild(fm.Row.Elem, true, map);
                    return n;
                }
            case RowMode row:
                {
                    var n = new RowMode(row.Dims, Void); map[(m, underRef)] = n;
                    n.Elem = NormBuild(row.Elem, underRef, map);
                    return n;
                }
            case StructMode sm:
                {
                    var n = new StructMode(new (Mode, string)[sm.Fields.Length]); map[(m, underRef)] = n;
                    for (int i = 0; i < sm.Fields.Length; i++) n.Fields[i] = (NormBuild(sm.Fields[i].Mode, underRef, map), sm.Fields[i].Name);
                    return n;
                }
            case UnionMode um:
                {
                    var n = new UnionMode(new Mode[um.Members.Length]); map[(m, underRef)] = n;
                    for (int i = 0; i < um.Members.Length; i++) n.Members[i] = NormBuild(um.Members[i], false, map);
                    return n;
                }
            case ProcMode pm:
                {
                    var n = new ProcMode(new Mode[pm.Params.Length], Void); map[(m, underRef)] = n;
                    for (int i = 0; i < pm.Params.Length; i++) n.Params[i] = NormBuild(pm.Params[i], false, map);
                    n.Result = NormBuild(pm.Result, false, map);
                    return n;
                }
        }
        return m;
    }

    bool NeedsNoNorm(Mode m, bool underRef, HashSet<(Mode, bool)> seen)
    {
        if (!seen.Add((m, underRef))) return true;
        return m switch
        {
            FlexMode f => underRef && NeedsNoNorm(f.Row.Elem, true, seen),
            RefMode r => NeedsNoNorm(r.Sub, true, seen),
            RowMode r => NeedsNoNorm(r.Elem, underRef, seen),
            StructMode s => s.Fields.All(f => NeedsNoNorm(f.Mode, underRef, seen)),
            UnionMode u => u.Members.All(x => NeedsNoNorm(x, false, seen)),
            ProcMode p => p.Params.All(x => NeedsNoNorm(x, false, seen)) && NeedsNoNorm(p.Result, false, seen),
            _ => true,
        };
    }

    /// <summary>Mode obtenu en dereferencant un nom de mode REF m.</summary>
    public Mode DerefValue(Mode refSub) => Normalize(refSub, false);

    // ------------------------------------------------------------- internement

    static int ShallowHash(Mode m) => m switch
    {
        PrimMode p => 1000 + (int)p.Kind * 10 + p.Size + 3,
        RefMode => 2,
        ProcMode p => 3 + p.Params.Length * 31,
        StructMode s => 4 + s.Fields.Length * 31 + s.Fields.Aggregate(0, (h, f) => h * 7 + f.Name.GetHashCode()),
        UnionMode u => 5 + u.Members.Length * 31,
        RowMode r => 6 + r.Dims * 31,
        FlexMode f => 7 + f.Row.Dims * 31,
        _ => 8,
    };

    public Mode Intern(Mode root)
    {
        if (root.Canonical) return root;
        if (root is IndicantMode im) return im.Target != null ? Intern(im.Target) : throw new InvalidOperationException("indicant non résolu");
        // noeuds non canoniques atteignables
        var nodes = new List<Mode>();
        var seen = new HashSet<Mode>(ReferenceEqualityComparer.Instance);
        void Walk(Mode m)
        {
            m = Strip(m);
            if (m.Canonical || !seen.Add(m)) return;
            nodes.Add(m);
            foreach (var c in Children(m)) Walk(c);
        }
        Walk(root);
        // remplacer les indicants dans les composantes
        foreach (var n in nodes) ReplaceChildren(n, Strip);
        var map = new Dictionary<Mode, Mode>(ReferenceEqualityComparer.Instance);
        // aplatir les unions (apres resolution des indicants)
        foreach (var n in nodes)
            if (n is UnionMode u) u.Members = FlattenUnionMembers(u);
        var fresh = new List<Mode>();
        foreach (var n in nodes)
        {
            int h = ShallowHash(n);
            Mode? found = null;
            if (buckets.TryGetValue(h, out var b))
                foreach (var c in b) if (Equivalent(c, n)) { found = c; break; }
            if (found == null)
                foreach (var c in fresh) if (ShallowHash(c) == h && Equivalent(c, n)) { found = c; break; }
            if (found != null) map[n] = found;
            else { fresh.Add(n); map[n] = n; }
        }
        foreach (var n in fresh)
        {
            ReplaceChildren(n, c => map.TryGetValue(c, out var r) ? r : c);
            n.Canonical = true;
            n.Id = nextId++;
            int h = ShallowHash(n);
            if (!buckets.TryGetValue(h, out var b)) buckets[h] = b = new List<Mode>();
            b.Add(n);
        }
        // apres remplacement, les unions peuvent contenir des doublons
        foreach (var n in fresh)
            if (n is UnionMode u)
            {
                var l = new List<Mode>();
                foreach (var x in u.Members) if (!l.Any(y => ReferenceEquals(y, x))) l.Add(x);
                u.Members = l.ToArray();
            }
        var res = map[Strip(root)];
        if (root.Name != null && res.Name == null) res.Name = root.Name;
        return res;
    }

    static Mode[] FlattenUnionMembers(UnionMode u)
    {
        var list = new List<Mode>();
        var seen = new HashSet<Mode>(ReferenceEqualityComparer.Instance);
        void Add(Mode m)
        {
            m = Strip(m);
            if (m is UnionMode inner) { if (!seen.Add(inner)) return; foreach (var x in inner.Members) Add(x); return; }
            list.Add(m);
        }
        foreach (var m in u.Members) Add(m);
        return list.ToArray();
    }

    public static Mode Strip(Mode m)
    {
        int guard = 0;
        while (m is IndicantMode im && im.Target != null && guard++ < 1000) m = im.Target;
        return m;
    }

    static IEnumerable<Mode> Children(Mode m)
    {
        switch (m)
        {
            case RefMode r: yield return r.Sub; break;
            case ProcMode p: foreach (var x in p.Params) yield return x; yield return p.Result; break;
            case StructMode s: foreach (var f in s.Fields) yield return f.Mode; break;
            case UnionMode u: foreach (var x in u.Members) yield return x; break;
            case RowMode r: yield return r.Elem; break;
            case FlexMode f: yield return f.Row; break;
        }
    }

    static void ReplaceChildren(Mode m, Func<Mode, Mode> f)
    {
        switch (m)
        {
            case RefMode r: r.Sub = f(r.Sub); break;
            case ProcMode p: for (int i = 0; i < p.Params.Length; i++) p.Params[i] = f(p.Params[i]); p.Result = f(p.Result); break;
            case StructMode s: for (int i = 0; i < s.Fields.Length; i++) s.Fields[i] = (f(s.Fields[i].Mode), s.Fields[i].Name); break;
            case UnionMode u: for (int i = 0; i < u.Members.Length; i++) u.Members[i] = f(u.Members[i]); break;
            case RowMode r: r.Elem = f(r.Elem); break;
            case FlexMode fl: { var nr = f(fl.Row); if (nr is RowMode rr) fl.Row = rr; break; }
        }
    }

    /// <summary>Equivalence structurelle (RR 7.3.1), coinductive pour les modes recursifs.</summary>
    public static bool Equivalent(Mode a, Mode b) => Eq(a, b, new HashSet<(Mode, Mode)>());

    static bool Eq(Mode a, Mode b, HashSet<(Mode, Mode)> assumed)
    {
        a = Strip(a); b = Strip(b);
        if (ReferenceEquals(a, b)) return true;
        if (a.Canonical && b.Canonical) return false;
        if (!assumed.Add((a, b))) return true;
        switch (a)
        {
            case PrimMode pra: return b is PrimMode prb && pra.Kind == prb.Kind && pra.Size == prb.Size;
            case RefMode ra: return b is RefMode rb0 && Eq(ra.Sub, rb0.Sub, assumed);
            case FlexMode fa: return b is FlexMode fb0 && Eq(fa.Row, fb0.Row, assumed);
            case RowMode rwa: return b is RowMode rwb && rwa.Dims == rwb.Dims && Eq(rwa.Elem, rwb.Elem, assumed);
            case ProcMode pa:
                {
                    if (b is not ProcMode pb || pa.Params.Length != pb.Params.Length) return false;
                    for (int i = 0; i < pa.Params.Length; i++) if (!Eq(pa.Params[i], pb.Params[i], assumed)) return false;
                    return Eq(pa.Result, pb.Result, assumed);
                }
            case StructMode sa:
                {
                    if (b is not StructMode sb || sa.Fields.Length != sb.Fields.Length) return false;
                    for (int i = 0; i < sa.Fields.Length; i++)
                        if (sa.Fields[i].Name != sb.Fields[i].Name || !Eq(sa.Fields[i].Mode, sb.Fields[i].Mode, assumed)) return false;
                    return true;
                }
            case UnionMode ua:
                {
                    if (b is not UnionMode ub) return false;
                    var ma = FlattenUnionMembers(ua); var mb = FlattenUnionMembers(ub);
                    foreach (var x in ma) if (!mb.Any(y => Eq(x, y, new HashSet<(Mode, Mode)>(assumed)))) return false;
                    foreach (var y in mb) if (!ma.Any(x => Eq(x, y, new HashSet<(Mode, Mode)>(assumed)))) return false;
                    return true;
                }
            case SpecialMode: return false;
        }
        return false;
    }

    // ------------------------------------------------------------- affichage

    public static string Show(Mode m) { var sb = new StringBuilder(); ShowInto(sb, m, 0, new HashSet<Mode>(ReferenceEqualityComparer.Instance)); return sb.ToString(); }

    static void ShowInto(StringBuilder sb, Mode m, int depth, HashSet<Mode> stack)
    {
        m = Strip(m);
        if (m.Name != null && depth > 0) { sb.Append(m.Name); return; }
        if (m.Name != null && (m.Name == "STRING" || m.Name.Contains("COMPL"))) { sb.Append(m.Name); return; }
        if (stack.Contains(m)) { sb.Append(m.Name ?? "..."); return; }
        stack.Add(m);
        try
        {
            switch (m)
            {
                case PrimMode p:
                    for (int i = 0; i < p.Size; i++) sb.Append("LONG ");
                    for (int i = 0; i > p.Size; i--) sb.Append("SHORT ");
                    sb.Append(p.Kind switch
                    {
                        Prim.Int => "INT", Prim.Real => "REAL", Prim.Bool => "BOOL", Prim.Char => "CHAR", Prim.Bits => "BITS", Prim.Bytes => "BYTES",
                        Prim.Void => "VOID", Prim.Format => "FORMAT", Prim.File => "FILE", Prim.Channel => "CHANNEL", Prim.Sema => "SEMA", _ => "?"
                    });
                    break;
                case RefMode r: sb.Append("REF "); ShowInto(sb, r.Sub, depth + 1, stack); break;
                case FlexMode f: sb.Append("FLEX "); ShowInto(sb, f.Row, depth + 1, stack); break;
                case RowMode r:
                    sb.Append('[').Append(new string(',', r.Dims - 1)).Append("] ");
                    ShowInto(sb, r.Elem, depth + 1, stack);
                    break;
                case ProcMode p:
                    sb.Append("PROC ");
                    if (p.Params.Length > 0)
                    {
                        sb.Append('(');
                        for (int i = 0; i < p.Params.Length; i++) { if (i > 0) sb.Append(", "); ShowInto(sb, p.Params[i], depth + 1, stack); }
                        sb.Append(") ");
                    }
                    ShowInto(sb, p.Result, depth + 1, stack);
                    break;
                case StructMode s:
                    sb.Append("STRUCT (");
                    for (int i = 0; i < s.Fields.Length; i++) { if (i > 0) sb.Append(", "); ShowInto(sb, s.Fields[i].Mode, depth + 1, stack); sb.Append(' ').Append(s.Fields[i].Name); }
                    sb.Append(')');
                    break;
                case UnionMode u:
                    sb.Append("UNION (");
                    for (int i = 0; i < u.Members.Length; i++) { if (i > 0) sb.Append(", "); ShowInto(sb, u.Members[i], depth + 1, stack); }
                    sb.Append(')');
                    break;
                case SpecialMode s:
                    sb.Append(s.Kind switch
                    {
                        SpecialKind.Skip => "SKIP", SpecialKind.Nil => "NIL", SpecialKind.Jump => "saut", SpecialKind.Vacuum => "()",
                        SpecialKind.Rows => "ROWS", SpecialKind.OutType => "SIMPLOUT", SpecialKind.InType => "SIMPLIN", SpecialKind.Label => "étiquette", _ => "?"
                    });
                    break;
                case IndicantMode im: sb.Append(im.Ind); break;
            }
        }
        finally { stack.Remove(m); }
    }
}
