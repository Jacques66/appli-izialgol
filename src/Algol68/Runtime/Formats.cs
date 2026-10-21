using System.Numerics;
using System.Text;
using Studio.Algol68.Exec;
using Studio.Algol68.Semantics;
using Studio.Algol68.Syntax;

namespace Studio.Algol68.Runtime;

// ---------------------------------------------------------------- format compile

public sealed class CRep
{
    public int Static = 1; public Node? Dyn;
    public int Eval(Frame env) => Dyn == null ? Static : (int)Math.Max(0, Dyn.EvalInt(env));
}
public sealed class CIns
{
    public static readonly CIns Empty = new() { Items = Array.Empty<(CRep, char, string)>() };
    public (CRep Rep, char Code, string Lit)[] Items = Array.Empty<(CRep, char, string)>();
    public bool IsEmpty => Items.Length == 0;
}
public sealed class CFrame { public CIns Ins = CIns.Empty; public CRep Rep = new(); public bool Supp; public char Marker; public int Radix; }
public sealed class CPattern
{
    public PatternKind Kind;
    public CFrame[] Frames = Array.Empty<CFrame>();
    public CIns Lead = CIns.Empty;
    public CIns[] Choices = Array.Empty<CIns>();
    public Node? FormatUnit;
    public Node[] GParams = Array.Empty<Node>();
}
public abstract class CPiece { }
public sealed class CPicture : CPiece { public CPattern? Pattern; public CIns Ins = CIns.Empty; }
public sealed class CCollection : CPiece { public CIns Before = CIns.Empty, After = CIns.Empty; public CRep Rep = new(); public CPiece[] Pieces = Array.Empty<CPiece>(); }
public sealed class CFormat { public CPiece[] Pieces = Array.Empty<CPiece>(); public bool HasPatterns; }

public sealed class FormatV
{
    public CFormat F = null!; public Frame? Env; public int Scope;
    public override string ToString() => "format";
}

public sealed class FormatTextNode : Node
{
    readonly CFormat fmt; readonly bool dynamic; readonly int level;
    public FormatTextNode(CFormat f, bool dynamic, int level) { fmt = f; this.dynamic = dynamic; this.level = level; Fresh = true; }
    public override Value Eval(Frame f) => Value.Obj(new FormatV { F = fmt, Env = f, Scope = dynamic ? f.Depth + level : 0 });
}

// ---------------------------------------------------------------- format state of a file

public sealed class FormatState
{
    public sealed class Level
    {
        public CPiece[] Pieces = Array.Empty<CPiece>();
        public int Idx;
        public int RepLeft;
        public CCollection? Coll;
        public FormatV FV = null!;
        public CPicture? NestedPic;
    }
    public readonly Stack<Level> L = new();
    public FormatV Root = null!;
    public bool Pending;    // insertions still to be performed
}

public sealed partial class Transput
{
    public void AssociateFormat(Frame fr, Value refFile, FileV f, FormatV fv, bool read, Node at)
    {
        if (f.Fmt != null) PurgeFormat(fr, refFile, f, read, at);
        var st = new FormatState { Root = fv };
        st.L.Push(new FormatState.Level { Pieces = fv.F.Pieces, FV = fv });
        f.Fmt = st;
    }

    // ------------------------------------------------------------------ insertions

    void DoInsertion(Frame fr, Value refFile, FileV f, CIns ins, Frame env, bool read, Node at, bool blankLiterals = false)
    {
        foreach (var (rep, code, lit) in ins.Items)
        {
            int n = rep.Eval(env);
            switch (code)
            {
                case '"':
                    for (int k = 0; k < n; k++)
                    {
                        if (read)
                        {
                            foreach (char c in lit)
                            {
                                int got = ReadChar(f);
                                if (got != c) CharError(fr, refFile, f, c, got, at);
                            }
                        }
                        else WriteString(f, blankLiterals ? new string(' ', lit.Length) : lit, at);
                    }
                    break;
                case 'x':
                    for (int k = 0; k < n; k++) { if (read) ReadChar(f); else WriteString(f, " ", at); }
                    break;
                case 'q':
                    for (int k = 0; k < n; k++)
                    {
                        if (read) { int got = ReadChar(f); if (got != ' ') CharError(fr, refFile, f, ' ', got, at); }
                        else WriteString(f, " ", at);
                    }
                    break;
                case 'y':
                    for (int k = 0; k < n; k++) { Monitor.Exit(f.Lock); try { Backspace(fr, refFile, at); } finally { Monitor.Enter(f.Lock); } }
                    break;
                case 'l':
                    for (int k = 0; k < n; k++) { Monitor.Exit(f.Lock); try { Newline(fr, refFile, at); } finally { Monitor.Enter(f.Lock); } }
                    break;
                case 'p':
                    for (int k = 0; k < n; k++) { Monitor.Exit(f.Lock); try { Newpage(fr, refFile, at); } finally { Monitor.Enter(f.Lock); } }
                    break;
                case 'k':
                    Monitor.Exit(f.Lock); try { SetCharNumber(fr, refFile, n, at); } finally { Monitor.Enter(f.Lock); }
                    break;
            }
        }
    }

    void CharError(Frame fr, Value refFile, FileV f, char expected, int got, Node at) =>
        SuggestedChar(fr, refFile, f, expected, got, at, "formatted input");

    /// <summary>
    /// Calls the on char error routine with the suggestion (RR 10.3.1.3.cc, 10.3.4.1.1.ll) and yields the
    /// suggestion, as possibly replaced by the routine; raises a runtime error if the routine yields false.
    /// </summary>
    char SuggestedChar(Frame fr, Value refFile, FileV f, char suggestion, int got, Node at, string what = "input")
    {
        if (f.OnCharError != null)
        {
            var cell = RefV.NewCell(Value.I(suggestion), 0);
            Monitor.Exit(f.Lock);
            Value res;
            try { res = Calls.Invoke(f.OnCharError, new[] { refFile, Value.Obj(cell) }, fr, at, fr.Depth + 1000); }
            finally { Monitor.Enter(f.Lock); }
            if (res.N != 0) return (char)cell.A![0].N;
        }
        throw RT.Err($"unexpected character in {what}: '{(got < 0 ? "end of file" : ((char)got).ToString())}' instead of '{suggestion}'", at);
    }

    // ------------------------------------------------------------------ walking through the format

    (CPicture pic, FormatV fv) NextPicture(Frame fr, Value refFile, FileV f, bool read, Node at)
    {
        var st = f.Fmt ?? throw RT.Err("formatted transput without an associated format", at);
        int wraps = 0;
        while (true)
        {
            var top = st.L.Peek();
            var env = top.FV.Env!;
            if (top.Idx >= top.Pieces.Length)
            {
                if (top.Coll != null)
                {
                    top.RepLeft--;
                    if (top.RepLeft > 0) { top.Idx = 0; continue; }
                    st.L.Pop();
                    DoInsertion(fr, refFile, f, top.Coll.After, env, read, at);
                    st.L.Peek().Idx++;
                    continue;
                }
                if (top.NestedPic != null)
                {
                    st.L.Pop();
                    DoInsertion(fr, refFile, f, top.NestedPic.Ins, st.L.Peek().FV.Env!, read, at);
                    st.L.Peek().Idx++;
                    continue;
                }
                // end of the format
                if (++wraps > 2 || !st.Root.F.HasPatterns) throw RT.Err("format exhausted: no pattern available for the next value", at);
                bool mended = false;
                var old = f.Fmt;
                if (f.OnFormatEnd != null)
                {
                    Monitor.Exit(f.Lock);
                    try { mended = CallEvent(fr, f.OnFormatEnd, refFile, at); } finally { Monitor.Enter(f.Lock); }
                }
                if (mended)
                {
                    if (ReferenceEquals(f.Fmt, old)) throw RT.Err("format exhausted and not replaced by on format end", at);
                    st = f.Fmt!;
                    continue;
                }
                top.Idx = 0;
                continue;
            }
            var piece = top.Pieces[top.Idx];
            if (piece is CCollection c)
            {
                DoInsertion(fr, refFile, f, c.Before, env, read, at);
                int rep = c.Rep.Eval(env);
                if (rep <= 0) { DoInsertion(fr, refFile, f, c.After, env, read, at); top.Idx++; continue; }
                st.L.Push(new FormatState.Level { Pieces = c.Pieces, RepLeft = rep, Coll = c, FV = top.FV });
                continue;
            }
            var pic = (CPicture)piece;
            if (pic.Pattern == null)
            {
                DoInsertion(fr, refFile, f, pic.Ins, env, read, at);
                top.Idx++;
                continue;
            }
            if (pic.Pattern.Kind == PatternKind.Format)
            {
                DoInsertion(fr, refFile, f, pic.Pattern.Lead, env, read, at);
                var fv2 = pic.Pattern.FormatUnit!.Eval(env).O as FormatV ?? throw RT.Err("format pattern: uninitialised format", at);
                st.L.Push(new FormatState.Level { Pieces = fv2.F.Pieces, FV = fv2, NestedPic = pic });
                continue;
            }
            top.Idx++;
            return (pic, top.FV);
        }
    }

    /// <summary>Performs the remaining insertions up to the next pattern (end of putf/getf).</summary>
    public void PurgeFormat(Frame fr, Value refFile, FileV f, bool read, Node at)
    {
        var st = f.Fmt;
        if (st == null) return;
        for (int guard = 0; guard < 10000; guard++)
        {
            var top = st.L.Peek();
            var env = top.FV.Env!;
            if (top.Idx >= top.Pieces.Length)
            {
                if (top.Coll != null && top.RepLeft <= 1)
                {
                    st.L.Pop(); DoInsertion(fr, refFile, f, top.Coll.After, env, read, at); st.L.Peek().Idx++; continue;
                }
                if (top.NestedPic != null)
                {
                    st.L.Pop(); DoInsertion(fr, refFile, f, top.NestedPic.Ins, st.L.Peek().FV.Env!, read, at); st.L.Peek().Idx++; continue;
                }
                return;
            }
            if (top.Pieces[top.Idx] is CPicture p && p.Pattern == null)
            {
                DoInsertion(fr, refFile, f, p.Ins, env, read, at);
                top.Idx++;
                continue;
            }
            return;
        }
    }

    // ------------------------------------------------------------------ formatted output

    public void PutfList(Frame fr, Value refFile, Value row, Node at)
    {
        var f = GetFile(refFile, at);
        var items = RowOps.GetRow(row, at).Elements();
        lock (f.Lock)
        {
            var ev = EnterEvents(f, fr, refFile);
            try
            {
                SetWriteMood(f, at);
                foreach (var it in items)
                {
                    if (it.O is not UnionV u) throw RT.Err("uninitialised output item", at);
                    if (u.M == null || u.M is PrimMode { Kind: Prim.Format })
                    {
                        AssociateFormat(fr, refFile, f, (FormatV)u.V.O!, false, at);
                        continue;
                    }
                    PutFormatted(fr, refFile, f, u.M, u.V, at);
                }
                PurgeFormat(fr, refFile, f, false, at);
            }
            finally { LeaveEvents(f, ev); }
        }
    }

    void PutFormatted(Frame fr, Value refFile, FileV f, Mode m, Value v, Node at)
    {
        if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err($"output of an uninitialised value (mode {m})", at);
        switch (m)
        {
            case UnionMode:
                { var u = (UnionV)v.O!; PutFormatted(fr, refFile, f, u.M, u.V, at); return; }
            case StructMode sm when !IsCompl(sm):
                {
                    var fl = (Value[])v.O!;
                    for (int i = 0; i < fl.Length; i++) PutFormatted(fr, refFile, f, sm.Fields[i].Mode, fl[i], at);
                    return;
                }
            case RowMode or FlexMode:
                {
                    var rm = m.AsRow!;
                    var row = RowOps.GetRow(v, at);
                    if (rm.Elem is PrimMode { Kind: Prim.Char })
                    {
                        // a string is controlled by a single pattern
                        var (pic0, fv0) = NextPicture(fr, refFile, f, false, at);
                        PutPicture(fr, refFile, f, pic0, fv0, m, v, at);
                        return;
                    }
                    foreach (var e in row.Elements()) PutFormatted(fr, refFile, f, rm.Elem, e, at);
                    return;
                }
            case ProcMode:
                {
                    var proc = (ProcV)v.O!;
                    Monitor.Exit(f.Lock);
                    try { Calls.Invoke(proc, new[] { refFile }, fr, at, fr.Depth + 1000); } finally { Monitor.Enter(f.Lock); }
                    return;
                }
        }
        var (pic, fv) = NextPicture(fr, refFile, f, false, at);
        if (m is StructMode cs && IsCompl(cs) && pic.Pattern!.Kind is not PatternKind.Complex)
        {
            // a68g: a complex written with a real pattern uses two patterns
            var fl = (Value[])v.O!;
            PutPicture(fr, refFile, f, pic, fv, cs.Fields[0].Mode, fl[0], at);
            var (pic2, fv2) = NextPicture(fr, refFile, f, false, at);
            PutPicture(fr, refFile, f, pic2, fv2, cs.Fields[1].Mode, fl[1], at);
            return;
        }
        PutPicture(fr, refFile, f, pic, fv, m, v, at);
    }

    internal static bool IsCompl(StructMode sm) => sm.Fields.Length == 2 && sm.Fields[0].Name == "re" && sm.Fields[1].Name == "im" && sm.Fields[0].Mode is PrimMode { Kind: Prim.Real };

    void PutPicture(Frame fr, Value refFile, FileV f, CPicture pic, FormatV fv, Mode m, Value v, Node at)
    {
        var env = fv.Env!;
        var pat = pic.Pattern!;
        bool ok = true;
        switch (pat.Kind)
        {
            case PatternKind.General:
            case PatternKind.GeneralH:
                {
                    DoInsertion(fr, refFile, f, pat.Lead, env, false, at);
                    // RR 10.3.4.10.1.aa: a parametrized general pattern only accepts L INT and L REAL values
                    if (pat.GParams.Length > 0 && !IsNumber(m)) { ok = false; break; }
                    if (pat.GParams.Length == 0)
                    {
                        if (m is StructMode sm && IsCompl(sm))
                        {
                            var fl = (Value[])v.O!;
                            WriteString(f, StandardString(sm.Fields[0].Mode, fl[0], at) + StandardString(sm.Fields[1].Mode, fl[1], at), at);
                        }
                        else PutValue(fr, refFile, f, m, v, at);
                    }
                    else
                    {
                        var ps = pat.GParams.Select(p => (int)p.EvalInt(env)).ToArray();
                        string s = ps.Length switch
                        {
                            1 => WholeAny(m, v, ps[0]),
                            2 => FixedAny(m, v, ps[0], ps[1]),
                            _ => FloatAny(m, v, ps[0], ps[1], ps[2], pat.Kind == PatternKind.GeneralH ? (ps.Length > 3 ? ps[3] : 3) : 1),
                        };
                        WriteString(f, s, at);
                    }
                    break;
                }
            case PatternKind.IntChoice:
            case PatternKind.BoolChoice:
                {
                    DoInsertion(fr, refFile, f, pat.Lead, env, false, at);
                    int k;
                    if (pat.Kind == PatternKind.BoolChoice) { if (m is PrimMode { Kind: Prim.Bool }) k = v.Bool ? 1 : 2; else { ok = false; break; } }
                    else if (m is PrimMode { Kind: Prim.Int, Size: 0 }) k = (int)v.N;
                    else { ok = false; break; }
                    if (k < 1 || k > pat.Choices.Length) { ok = false; break; }
                    DoInsertion(fr, refFile, f, pat.Choices[k - 1], env, false, at);
                    break;
                }
            case PatternKind.Integral:
                {
                    if (m is not PrimMode { Kind: Prim.Int } ip) { ok = false; break; }
                    ok = EditIntegral(fr, refFile, f, pat.Frames, Prelude.Big(v, ip.Size), env, at);
                    break;
                }
            case PatternKind.Real:
                {
                    if (!IsRealOrInt(m)) { ok = false; break; }
                    ok = EditReal(fr, refFile, f, pat.Frames, m, v, env, at);
                    break;
                }
            case PatternKind.Complex:
                {
                    int iIdx = Array.FindIndex(pat.Frames, x => x.Marker == 'i');
                    var reF = pat.Frames[..iIdx]; var imF = pat.Frames[(iIdx + 1)..];
                    Mode rm; Value re, im;
                    if (m is StructMode sm2 && IsCompl(sm2)) { var fl = (Value[])v.O!; rm = sm2.Fields[0].Mode; re = fl[0]; im = fl[1]; }
                    else if (IsRealOrInt(m)) { rm = m; re = v; im = ZeroOf(m); }
                    else if (m is StructMode sm3 && sm3.Fields.Length == 2 && sm3.Fields[0].Name == "re") { var fl = (Value[])v.O!; rm = sm3.Fields[0].Mode; re = fl[0]; im = fl[1]; }
                    else { ok = false; break; }
                    ok = EditReal(fr, refFile, f, reF, rm, re, env, at);
                    if (!ok) break;
                    var iframe = pat.Frames[iIdx];
                    DoInsertion(fr, refFile, f, iframe.Ins, env, false, at);
                    if (!iframe.Supp) WriteString(f, "i", at);
                    ok = EditReal(fr, refFile, f, imF, rm, im, env, at);
                    break;
                }
            case PatternKind.Boolean:
                {
                    if (m is not PrimMode { Kind: Prim.Bool }) { ok = false; break; }
                    var fb = pat.Frames[0];
                    DoInsertion(fr, refFile, f, fb.Ins, env, false, at);
                    if (!fb.Supp) WriteString(f, v.Bool ? "T" : "F", at);
                    break;
                }
            case PatternKind.String:
                {
                    string s;
                    if (m is PrimMode { Kind: Prim.Char }) s = ((char)v.N).ToString();
                    else if (m.AsRow?.Elem is PrimMode { Kind: Prim.Char }) s = RowOps.GetRow(v, at).AsString();
                    else { ok = false; break; }
                    int pos = 0;
                    foreach (var fa in pat.Frames)
                    {
                        DoInsertion(fr, refFile, f, fa.Ins, env, false, at);
                        int n = fa.Rep.Eval(env);
                        for (int k = 0; k < n; k++)
                        {
                            if (pos >= s.Length) { ok = false; break; }
                            if (!fa.Supp) WriteString(f, s[pos].ToString(), at);
                            pos++;
                        }
                        if (!ok) break;
                    }
                    break;
                }
            case PatternKind.Bits:
                {
                    if (m is not PrimMode { Kind: Prim.Bits } bp) { ok = false; break; }
                    BigInteger b = bp.Size == 0 ? (BigInteger)(ulong)(uint)v.N : (BigInteger)v.O!;
                    ok = EditBits(fr, refFile, f, pat.Frames, b, env, at);
                    break;
                }
            default:
                ok = false; break;
        }
        if (!ok)
        {
            bool mended = false;
            if (f.OnValueError != null)
            {
                Monitor.Exit(f.Lock);
                try { mended = CallEvent(fr, f.OnValueError, refFile, at); } finally { Monitor.Enter(f.Lock); }
            }
            if (!mended) throw RT.Err($"value error: a value of mode {m} cannot be written with this format pattern", at);
        }
        DoInsertion(fr, refFile, f, pic.Ins, env, false, at);
    }

    static bool IsNumber(Mode m) => m is PrimMode { Kind: Prim.Int or Prim.Real };
    static bool IsRealOrInt(Mode m) => m is PrimMode { Kind: Prim.Int or Prim.Real };
    static Value ZeroOf(Mode m) => m is PrimMode { Kind: Prim.Real, Size: > 0 } p ? Value.Obj(MpReal.Zero(Prelude.RealDigits(p.Size))) : m is PrimMode { Kind: Prim.Int, Size: > 0 } ? Value.Obj(BigInteger.Zero) : default;

    public static string WholeAny(Mode m, Value v, int w)
    {
        if (m is PrimMode { Kind: Prim.Int } p) return Conv.WholeInt(Prelude.Big(v, p.Size), w);
        return FixedAny(m, v, w, 0);
    }
    public static string FixedAny(Mode m, Value v, int w, int a)
    {
        var p = (PrimMode)m;
        if (p.Kind == Prim.Int)
        {
            if (p.Size == 0) return Conv.Fixed(v.N, w, a);
            return Conv.LongFixed(MpReal.FromBig((BigInteger)v.O!, Prelude.RealDigits(p.Size)), w, a);
        }
        if (p.Size == 0) return Conv.Fixed(v.Real, w, a);
        return Conv.LongFixed((MpReal)v.O!, w, a);
    }
    public static string FloatAny(Mode m, Value v, int w, int a, int e, int frmt = 1)
    {
        var p = (PrimMode)m;
        if (p.Kind == Prim.Int)
        {
            if (p.Size == 0) return Conv.Real(v.N, w, a, e, frmt);
            return Conv.LongReal(MpReal.FromBig((BigInteger)v.O!, Prelude.RealDigits(p.Size)), w, a, e, frmt);
        }
        if (p.Size == 0) return Conv.Real(v.Real, w, a, e, frmt);
        return Conv.LongReal((MpReal)v.O!, w, a, e, frmt);
    }

    // ---- editing (a68g: write_mould)

    struct Mood { public bool DigitBlank; public bool InsertionBlank; }

    sealed class XFrame { public CFrame F = null!; }

    /// <summary>Writes the frames of a mould with the string q (optional leading sign).</summary>
    void WriteMould(Frame fr, Value refFile, FileV f, List<CFrame> frames, StringBuilder q, ref int qi, ref Mood mood, Frame env, Node at)
    {
        foreach (var fm in frames)
        {
            int n = fm.Marker is 'z' or 'd' ? fm.Rep.Eval(env) : 1;
            for (int k = 0; k < n; k++)
            {
                if (k == 0) DoInsertion(fr, refFile, f, fm.Ins, env, false, at, mood.InsertionBlank);
                if (fm.Marker is '+' or '-') break;
                if (fm.Supp)
                {
                    if (qi < q.Length && q[qi] is '+' or '-' or ' ') qi++;
                    if (qi < q.Length) qi++;
                    continue;
                }
                // sign
                if (qi < q.Length && q[qi] is '+' or '-' or ' ' && fm.Marker is 'z' or 'd')
                {
                    WriteString(f, q[qi].ToString(), at); qi++;
                }
                if (qi >= q.Length) continue;
                char ch = q[qi];
                if (fm.Marker == 'z')
                {
                    if (ch == '0')
                    {
                        if (mood.DigitBlank) { WriteString(f, " ", at); mood.InsertionBlank = true; }
                        else { WriteString(f, "0", at); mood.InsertionBlank = false; }
                    }
                    else { WriteString(f, ch.ToString(), at); mood.DigitBlank = false; mood.InsertionBlank = false; }
                    qi++;
                }
                else if (fm.Marker == 'd')
                {
                    // RR 10.3.4.1.1.jj: zeros of z frames are also blanked between a d frame and the next
                    // nonzero digit (zd"-"zd"-19"2d edits 180168 as "18- 1-1968")
                    WriteString(f, ch.ToString(), at); qi++;
                    mood.DigitBlank = true; mood.InsertionBlank = false;
                }
            }
        }
    }

    static int CountZD(IEnumerable<CFrame> frames, Frame env) => frames.Where(x => x.Marker is 'z' or 'd').Sum(x => x.Rep.Eval(env));

    /// <summary>Moves the sign across the zeros of the leading z frames (a68g shift_sign).</summary>
    static void ShiftSign(List<CFrame> frames, StringBuilder q, Frame env)
    {
        int qi = 0;
        foreach (var fm in frames)
        {
            if (fm.Marker == 'd') return;
            if (fm.Marker != 'z') continue;
            int n = fm.Rep.Eval(env);
            for (int k = 0; k < n; k++)
            {
                if (qi + 1 < q.Length && q[qi] is '+' or '-' or ' ' && q[qi + 1] == '0')
                {
                    (q[qi], q[qi + 1]) = (q[qi + 1], q[qi]);
                    qi++;
                }
                else return;
            }
        }
    }

    static char? SignFrame(IEnumerable<CFrame> frames) => frames.FirstOrDefault(x => x.Marker is '+' or '-')?.Marker;

    bool EditIntegral(Frame fr, Value refFile, FileV f, CFrame[] frames, BigInteger v, Frame env, Node at)
    {
        var list = frames.ToList();
        int width = CountZD(list, env);
        var sign = SignFrame(list);
        string digits = Conv.SubWhole(BigInteger.Abs(v), width);
        if (digits.Contains(Conv.ErrorChar)) return false;
        if (sign == null && v.Sign < 0) return false;
        var q = new StringBuilder();
        if (sign != null) q.Append(v.Sign < 0 ? '-' : sign == '+' ? '+' : ' ');
        q.Append('0', width - digits.Length).Append(digits);
        if (sign != null) ShiftSign(list, q, env);
        var mood = new Mood { DigitBlank = true };
        int qi = 0;
        WriteMould(fr, refFile, f, list, q, ref qi, ref mood, env, at);
        // the sign moved across all the zeros ("+zz" with 0): it is written last, so that no position is lost
        if (sign != null && qi < q.Length && q[qi] is '+' or '-' or ' ') WriteString(f, q[qi].ToString(), at);
        return true;
    }

    bool EditBits(Frame fr, Value refFile, FileV f, CFrame[] frames, BigInteger b, Frame env, Node at)
    {
        int radix = frames[0].Radix;
        DoInsertion(fr, refFile, f, frames[0].Ins, env, false, at);
        var list = frames.Skip(1).ToList();
        int width = CountZD(list, env);
        var sb = new StringBuilder();
        if (b.IsZero) sb.Append('0');
        while (!b.IsZero) { sb.Insert(0, "0123456789abcdef"[(int)(b % radix)]); b /= radix; }
        if (sb.Length > width) return false;
        sb.Insert(0, new string('0', width - sb.Length));
        var mood = new Mood { DigitBlank = true };
        int qi = 0;
        WriteMould(fr, refFile, f, list, sb, ref qi, ref mood, env, at);
        return true;
    }

    bool EditReal(Frame fr, Value refFile, FileV f, CFrame[] frames, Mode m, Value v, Frame env, Node at)
    {
        // dissection: sign mould + integral part | point | fraction | e + exponent
        int ePos = Array.FindIndex(frames, x => x.Marker == 'e');
        var mant = ePos >= 0 ? frames[..ePos] : frames;
        var expo = ePos >= 0 ? frames[(ePos + 1)..] : Array.Empty<CFrame>();
        int pPos = Array.FindIndex(mant, x => x.Marker == '.');
        var stag = (pPos >= 0 ? mant[..pPos] : mant).ToList();
        var frac = (pPos >= 0 ? mant[(pPos + 1)..] : Array.Empty<CFrame>()).ToList();
        int stagDigits = CountZD(stag, env), fracDigits = CountZD(frac, env);
        int mantLength = pPos >= 0 ? 1 + stagDigits + fracDigits : stagDigits;
        var sign = SignFrame(stag);
        var p = (PrimMode)m;
        string str; int sgn; int expValue = 0;
        if (p.Size == 0)
        {
            double x = p.Kind == Prim.Int ? v.N : v.Real;
            if (double.IsNaN(x) || double.IsInfinity(x)) return false;
            sgn = Math.Sign(x);
            x = Math.Abs(x);
            if (ePos >= 0) Conv.Standardise(ref x, stagDigits, fracDigits, ref expValue);
            str = Conv.SubFixed(x, mantLength, fracDigits);
        }
        else
        {
            var x = p.Kind == Prim.Int ? MpReal.FromBig((BigInteger)v.O!, Prelude.RealDigits(p.Size)) : (MpReal)v.O!;
            sgn = x.Sign;
            x = x.Abs();
            if (ePos >= 0)
            {
                // exact standardisation
                var y = x; int q0 = 0;
                StandardiseMp(ref y, stagDigits, fracDigits, ref q0);
                x = y; expValue = q0;
            }
            str = Conv.LongSubFixed(x, mantLength, fracDigits);
        }
        if (str.Contains(Conv.ErrorChar)) return false;
        if (sign == null && sgn < 0) return false;
        int dot = str.IndexOf('.');
        string stagStr = dot >= 0 ? str[..dot] : str;
        string fracStr = dot >= 0 ? str[(dot + 1)..] : "";
        var q = new StringBuilder();
        if (sign != null) q.Append(sgn < 0 ? '-' : sign == '+' ? '+' : ' ');
        if (stagDigits - stagStr.Length > 0) q.Append('0', stagDigits - stagStr.Length);
        q.Append(stagStr);
        if (sign != null) ShiftSign(stag, q, env);
        var mood = new Mood { DigitBlank = true };
        int qi = 0;
        WriteMould(fr, refFile, f, stag, q, ref qi, ref mood, env, at);
        // a sign mould without digit frames before the point ("+.3d"): the sign is still written
        if (sign != null && qi < q.Length && q[qi] is '+' or '-' or ' ') { WriteString(f, q[qi].ToString(), at); qi++; }
        if (pPos >= 0)
        {
            var pf = mant[pPos];
            DoInsertion(fr, refFile, f, pf.Ins, env, false, at);
            if (!pf.Supp) WriteString(f, ".", at);
            var fq = new StringBuilder(fracStr);
            var fm = new Mood { DigitBlank = true }; // each integral mould blanks its leading zeros (RR 10.3.4.1.1.jj)
            int fi = 0;
            WriteMould(fr, refFile, f, frac, fq, ref fi, ref fm, env, at);
        }
        if (ePos >= 0)
        {
            var ef = frames[ePos];
            DoInsertion(fr, refFile, f, ef.Ins, env, false, at);
            if (!ef.Supp) WriteString(f, "e", at);
            if (!EditIntegral(fr, refFile, f, expo, expValue, env, at)) return false;
        }
        return true;
    }

    static void StandardiseMp(ref MpReal y, int before, int after, ref int p)
    {
        // same principle as Conv.Standardise, in exact decimal
        int dg = y.Digits + after + 30;
        y = y.WithDigits(dg);
        var g = MpReal.Make(1, before, dg); var h = MpReal.Make(1, before - 1, dg);
        if (!y.IsZero)
        {
            var (_, e10) = y.DecimalDigits();
            int shift = e10 - (before - 1);
            if (shift != 0) { y = MpReal.Make(y.M, y.E - shift, dg); p += shift; }
            while (y.CompareTo(g) >= 0) { y = MpReal.Make(y.M, y.E - 1, dg); p++; }
            while (y.CompareTo(h) < 0) { y = MpReal.Make(y.M, y.E + 1, dg); p--; }
        }
        var t = MpReal.Add(y, MpReal.Make(5, -after - 1, dg));
        if (t.CompareTo(g) >= 0) { y = h; p++; }
    }

    // ------------------------------------------------------------------ formatted input

    public void GetfList(Frame fr, Value refFile, Value row, Node at)
    {
        var f = GetFile(refFile, at);
        var items = RowOps.GetRow(row, at).Elements();
        lock (f.Lock)
        {
            SetReadMood(f, at);
            foreach (var it in items)
            {
                if (it.O is not UnionV u) throw RT.Err("uninitialised input item", at);
                if (u.M is PrimMode { Kind: Prim.Format })
                {
                    AssociateFormat(fr, refFile, f, (FormatV)u.V.O!, true, at);
                    continue;
                }
                GetFormattedItem(fr, refFile, f, u.M, u.V, at);
            }
            PurgeFormat(fr, refFile, f, true, at);
        }
    }

    void GetFormattedItem(Frame fr, Value refFile, FileV f, Mode m, Value v, Node at)
    {
        if (m is ProcMode) { GetValue(fr, refFile, f, m, v, at); return; }
        var r = v.O as RefV ?? throw RT.Err("name expected for input", at);
        if (r.A == null) throw RT.Err("input into NIL", at);
        GetFormattedInto(fr, refFile, f, ((RefMode)m).Sub, r.A, r.I, at);
    }

    void GetFormattedInto(Frame fr, Value refFile, FileV f, Mode m, Value[] arr, int idx, Node at)
    {
        switch (m)
        {
            case StructMode sm when !IsCompl(sm):
                {
                    var fl = arr[idx].O as Value[] ?? throw RT.Err("uninitialised structure", at);
                    for (int i = 0; i < sm.Fields.Length; i++) GetFormattedInto(fr, refFile, f, sm.Fields[i].Mode, fl, i, at);
                    return;
                }
            case RowMode rm when rm.Elem is not PrimMode { Kind: Prim.Char }:
                {
                    var row = arr[idx].O as RowV ?? throw RT.Err("uninitialised row", at);
                    foreach (int k in row.Indices()) { var (a2, i2) = row.SlotAt(k); GetFormattedInto(fr, refFile, f, rm.Elem, a2, i2, at); }
                    return;
                }
        }
        var (pic, fv) = NextPicture(fr, refFile, f, true, at);
        var env = fv.Env!;
        var pat = pic.Pattern!;
        bool ok = true;
        switch (pat.Kind)
        {
            case PatternKind.General:
            case PatternKind.GeneralH:
                DoInsertion(fr, refFile, f, pat.Lead, env, true, at);
                ReadInto(fr, refFile, f, m, arr, idx, at);
                break;
            case PatternKind.IntChoice:
            case PatternKind.BoolChoice:
                {
                    DoInsertion(fr, refFile, f, pat.Lead, env, true, at);
                    int found = -1;
                    for (int k = 0; k < pat.Choices.Length && found < 0; k++)
                    {
                        string lit = string.Concat(pat.Choices[k].Items.Where(x => x.Code == '"').Select(x => string.Concat(Enumerable.Repeat(x.Lit, x.Rep.Eval(env)))));
                        if (TryMatch(f, lit)) found = k + 1;
                    }
                    if (found < 0) { ok = false; break; }
                    if (pat.Kind == PatternKind.BoolChoice && m is PrimMode { Kind: Prim.Bool }) arr[idx] = Value.B(found == 1);
                    else if (pat.Kind == PatternKind.IntChoice && m is PrimMode { Kind: Prim.Int, Size: 0 }) arr[idx] = Value.I(found);
                    else ok = false;
                    break;
                }
            default:
                {
                    string s = InditString(fr, refFile, f, pat, env, at);
                    ok = ConvertIndited(pat, m, s, arr, idx, at);
                    break;
                }
        }
        if (!ok)
        {
            bool mended = false;
            if (f.OnValueError != null) { Monitor.Exit(f.Lock); try { mended = CallEvent(fr, f.OnValueError, refFile, at); } finally { Monitor.Enter(f.Lock); } }
            if (!mended) throw RT.Err($"value error in formatted input (mode {m})", at);
        }
        DoInsertion(fr, refFile, f, pic.Ins, env, true, at);
    }

    bool TryMatch(FileV f, string lit)
    {
        PeekChar(f); // load the current line of standard input before remembering the position
        int savePos = f.Kind == FileKind.StandIn ? f.InPos : f.Pos;
        int sl = f.Line, sc = f.Char, sp = f.Page;
        foreach (char c in lit)
        {
            int got = PeekChar(f);
            if (got != c || got == '\n')
            {
                if (f.Kind == FileKind.StandIn) f.InPos = savePos; else f.Pos = savePos;
                f.Line = sl; f.Char = sc; f.Page = sp;
                return false;
            }
            ReadChar(f);
        }
        return true;
    }

    string InditString(Frame fr, Value refFile, FileV f, CPattern pat, Frame env, Node at)
    {
        var sb = new StringBuilder();
        foreach (var fm in pat.Frames)
        {
            DoInsertion(fr, refFile, f, fm.Ins, env, true, at);
            int n = fm.Marker is 'd' or 'z' or 'a' ? fm.Rep.Eval(env) : 1;
            if (fm.Marker == 'r') continue;
            for (int k = 0; k < n; k++)
            {
                if (fm.Supp)
                {
                    sb.Append(fm.Marker switch { 'd' or 'z' => '0', '.' => '.', 'e' => 'e', 'i' => 'i', _ => ' ' });
                    continue;
                }
                int c = ReadChar(f);
                if (c < 0) { Monitor.Exit(f.Lock); try { LogicalFileEnd(fr, refFile, f, at); } finally { Monitor.Enter(f.Lock); } break; }
                sb.Append((char)c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Text of a number read under the control of a sign mould and an integral mould (RR 10.3.4.1.1.kk):
    /// spaces before the sign or the first digit are skipped (a space may stand for the sign "+"), the other
    /// spaces, read by z frames, stand for zeros ("18 168" read with zdzd2d is 180168).
    /// </summary>
    static string InditedNumber(string s)
    {
        var t = s.TrimStart(' ');
        if (t.Length == 0) return "0";
        if (t is "+" or "-") return t + "0";
        return t.Replace(' ', '0');
    }

    static string InditedReal(string s)
    {
        s = s.Replace('E', 'e').Replace('\\', 'e');
        int e = s.IndexOf('e');
        if (e < 0) return InditedNumber(s);
        return InditedNumber(s[..e]) + "e" + InditedNumber(s[(e + 1)..]);
    }

    bool ConvertIndited(CPattern pat, Mode m, string s, Value[] arr, int idx, Node at)
    {
        switch (pat.Kind)
        {
            case PatternKind.Integral:
                if (m is not PrimMode { Kind: Prim.Int } ip) return false;
                if (!ParseIntText(InditedNumber(s), ip.Size, out var iv)) return false;
                arr[idx] = iv; return true;
            case PatternKind.Real:
                if (m is not PrimMode { Kind: Prim.Real } rp) return false;
                if (!ParseRealText(InditedReal(s), rp.Size, out var rv)) return false;
                arr[idx] = rv; return true;
            case PatternKind.Complex:
                {
                    if (m is not StructMode sm) return false;
                    int i = s.IndexOfAny(new[] { 'i', 'I' });
                    if (i < 0) return false;
                    var rmode = (PrimMode)sm.Fields[0].Mode;
                    if (!ParseRealText(InditedReal(s[..i]), rmode.Size, out var re) || !ParseRealText(InditedReal(s[(i + 1)..]), rmode.Size, out var im)) return false;
                    arr[idx] = Value.Obj(new[] { re, im });
                    return true;
                }
            case PatternKind.Boolean:
                if (m is not PrimMode { Kind: Prim.Bool }) return false;
                if (s == "T") arr[idx] = Value.B(true); else if (s == "F") arr[idx] = Value.B(false); else return false;
                return true;
            case PatternKind.String:
                if (m is PrimMode { Kind: Prim.Char }) { if (s.Length != 1) return false; arr[idx] = Value.I(s[0]); return true; }
                if (m is FlexMode) { arr[idx] = RT.StrValue(s); return true; }
                if (m is RowMode) { var row = arr[idx].O as RowV; if (row == null || row.Count != s.Length) return false; int k = 0; foreach (int ix in row.Indices()) { row.SetAt(ix, Value.I(s[k++])); } return true; }
                return false;
            case PatternKind.Bits:
                {
                    if (m is not PrimMode { Kind: Prim.Bits } bp) return false;
                    int radix = pat.Frames[0].Radix;
                    BigInteger b = 0;
                    foreach (char c in s.Replace(" ", "0"))
                    {
                        int d = Convert.ToInt32(c.ToString(), 16);
                        if (d >= radix) return false;
                        b = b * radix + d;
                    }
                    if (b.GetBitLength() > Prelude.BitsWidthOf(bp.Size)) return false;
                    arr[idx] = bp.Size == 0 ? Value.I((long)b) : Value.Obj(b);
                    return true;
                }
        }
        return false;
    }
}
