using System.Globalization;
using System.Numerics;
using System.Text;
using Studio.Algol68.Exec;
using Studio.Algol68.Semantics;

namespace Studio.Algol68.Runtime;

/// <summary>Formatless transput (RR 10.3.3) and value conversions.</summary>
public sealed partial class Transput
{
    // ------------------------------------------------------------------ values -> text (print)

    public static string StandardString(Mode m, Value v, Node at)
    {
        switch (m)
        {
            case PrimMode p:
                switch (p.Kind)
                {
                    case Prim.Int:
                        return p.Size == 0 ? Conv.WholeInt(v.N, Prelude.IntWidth + 1) : Conv.WholeInt((BigInteger)v.O!, Prelude.LongIntWidthOf(p.Size));
                    case Prim.Real:
                        if (p.Size == 0) return Conv.Float(v.Real, Prelude.RealWidth + Prelude.ExpWidth + 4, Prelude.RealWidth - 1, Prelude.ExpWidth + 1);
                        {
                            int w = Prelude.RealWidthOf(p.Size);
                            return Conv.LongReal((MpReal)v.O!, w + Prelude.ExpWidth + 4, w - 1, Prelude.ExpWidth + 1, 1);
                        }
                    case Prim.Bool: return v.Bool ? "T" : "F";
                    case Prim.Char: return ((char)v.N).ToString();
                    case Prim.Bits:
                        {
                            int w = Prelude.BitsWidthOf(p.Size);
                            BigInteger b = p.Size == 0 ? (BigInteger)(ulong)(uint)v.N : (BigInteger)v.O!;
                            var sb = new StringBuilder(w);
                            for (int i = w - 1; i >= 0; i--) sb.Append(((b >> i) & 1) == 1 ? 'T' : 'F');
                            return sb.ToString();
                        }
                    case Prim.Bytes: return ((string)v.O!).TrimEnd('\0');
                }
                break;
        }
        throw RT.Err($"value of mode {m} cannot be printed", at);
    }

    /// <summary>Writes an already "united" value (actual mode + value) without format.</summary>
    public void PutValue(Frame fr, Value refFile, FileV f, Mode m, Value v, Node at)
    {
        if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err($"output of an uninitialised value (mode {m})", at);
        switch (m)
        {
            case PrimMode p:
                if (p.Kind == Prim.Format) throw RT.Err("a FORMAT cannot be printed without a format (use printf/putf)", at);
                WriteString(f, StandardString(m, v, at), at);
                return;
            case UnionMode:
                if (v.O is UnionV u) { PutValue(fr, refFile, f, u.M, u.V, at); return; }
                throw RT.Err("uninitialised united value", at);
            case StructMode sm:
                {
                    var fields = (Value[])v.O!;
                    for (int i = 0; i < fields.Length; i++) PutValue(fr, refFile, f, sm.Fields[i].Mode, fields[i], at);
                    return;
                }
            case RowMode or FlexMode:
                {
                    var rm = m.AsRow!;
                    var row = RowOps.GetRow(v, at);
                    if (rm.Elem is PrimMode { Kind: Prim.Char }) { WriteString(f, row.AsString(), at); return; }
                    foreach (var e in row.Elements()) PutValue(fr, refFile, f, rm.Elem, e, at);
                    return;
                }
            case ProcMode:
                {
                    var proc = v.O as ProcV ?? throw RT.Err("uninitialised layout procedure", at);
                    Monitor.Exit(f.Lock);
                    try { Calls.Invoke(proc, new[] { refFile }, fr, at, fr.Depth + 1000); }
                    finally { Monitor.Enter(f.Lock); }
                    return;
                }
        }
        throw RT.Err($"value of mode {m} cannot be printed", at);
    }

    public void PutList(Frame fr, Value refFile, Value row, Node at)
    {
        var f = GetFile(refFile, at);
        var items = RowOps.GetRow(row, at).Elements();
        lock (f.Lock)
        {
            SetWriteMood(f, at);
            foreach (var it in items)
            {
                if (it.O is not UnionV u) throw RT.Err("uninitialised output item", at);
                PutValue(fr, refFile, f, u.M, u.V, at);
            }
        }
    }

    // ------------------------------------------------------------------ formatless input

    bool IsNlFf(int c) => c == '\n' || c == '\f';

    /// <summary>Skips line and page ends (events on line end / on page end).</summary>
    int SkipNlFf(Frame fr, Value refFile, FileV f, int ch, Node at)
    {
        while (ch >= 0 && IsNlFf(ch))
        {
            // ch has already been consumed: put it back, then handle the event
            UnreadLast(f, ch);
            bool mended = false;
            var p = ch == '\n' ? f.OnLineEnd : f.OnPageEnd;
            if (p != null) { Monitor.Exit(f.Lock); try { mended = CallEvent(fr, p, refFile, at); } finally { Monitor.Enter(f.Lock); } }
            if (!mended)
            {
                // newline / newpage: consume up to the terminator
                int c;
                do { c = ReadChar(f); } while (c >= 0 && c != ch);
            }
            ch = ReadChar(f);
        }
        return ch;
    }

    void UnreadLast(FileV f, int ch)
    {
        if (ch < 0) return;
        if (f.Kind == FileKind.StandIn) f.InPos--;
        else f.Pos--;
        if (ch == '\n') { f.Line--; f.Char = 1; }
        else if (ch == '\f') { f.Page--; }
        else f.Char--;
    }

    string ScanWhile(FileV f, Func<char, bool> pred)
    {
        var sb = new StringBuilder();
        while (true)
        {
            int c = PeekChar(f);
            if (c < 0 || !pred((char)c)) break;
            sb.Append((char)ReadChar(f));
        }
        return sb.ToString();
    }

    int SkipSpacesAndLines(Frame fr, Value refFile, FileV f, Node at)
    {
        int ch = ReadChar(f);
        while (ch >= 0 && (ch == ' ' || ch == '\t' || IsNlFf(ch) || ch == '\r'))
        {
            if (IsNlFf(ch)) ch = SkipNlFf(fr, refFile, f, ch, at);
            else ch = ReadChar(f);
        }
        return ch;
    }

    string ScanInteger(Frame fr, Value refFile, FileV f, Node at)
    {
        int ch = SkipSpacesAndLines(fr, refFile, f, at);
        if (ch < 0) { EofDuringRead(fr, refFile, f, at); return ""; }
        var sb = new StringBuilder();
        if (ch == '+' || ch == '-') { sb.Append((char)ch); ch = ReadChar(f); while (ch == ' ') ch = ReadChar(f); }
        while (ch >= 0 && char.IsDigit((char)ch)) { sb.Append((char)ch); ch = ReadChar(f); }
        UnreadLast(f, ch);
        return sb.ToString();
    }

    string ScanReal(Frame fr, Value refFile, FileV f, Node at)
    {
        int ch = SkipSpacesAndLines(fr, refFile, f, at);
        if (ch < 0) { EofDuringRead(fr, refFile, f, at); return ""; }
        var sb = new StringBuilder();
        if (ch == '+' || ch == '-') { sb.Append((char)ch); ch = ReadChar(f); while (ch == ' ') ch = ReadChar(f); }
        while (ch >= 0 && char.IsDigit((char)ch)) { sb.Append((char)ch); ch = ReadChar(f); }
        if (ch == '.')
        {
            sb.Append('.'); ch = ReadChar(f);
            while (ch >= 0 && char.IsDigit((char)ch)) { sb.Append((char)ch); ch = ReadChar(f); }
        }
        if (ch == 'e' || ch == 'E' || ch == '\\')
        {
            sb.Append('e'); ch = ReadChar(f);
            while (ch == ' ') ch = ReadChar(f);
            if (ch == '+' || ch == '-') { sb.Append((char)ch); ch = ReadChar(f); }
            while (ch >= 0 && char.IsDigit((char)ch)) { sb.Append((char)ch); ch = ReadChar(f); }
        }
        UnreadLast(f, ch);
        return sb.ToString();
    }

    void EofDuringRead(Frame fr, Value refFile, FileV f, Node at)
    {
        Monitor.Exit(f.Lock);
        try { LogicalFileEnd(fr, refFile, f, at); } finally { Monitor.Enter(f.Lock); }
        throw new EofMended();
    }
    sealed class EofMended : Exception { }

    void ValueError(Frame fr, Value refFile, FileV f, Mode m, string text, Node at)
    {
        if (f.OnValueError != null)
        {
            bool ok;
            Monitor.Exit(f.Lock);
            try { ok = CallEvent(fr, f.OnValueError, refFile, at); } finally { Monitor.Enter(f.Lock); }
            if (ok) return;
        }
        throw RT.Err($"value error in transput: '{text}' is not suitable for mode {m}", at);
    }

    void CheckEof(Frame fr, Value refFile, FileV f, Node at)
    {
        if (AtEof(f))
        {
            Monitor.Exit(f.Lock);
            try { LogicalFileEnd(fr, refFile, f, at); } finally { Monitor.Enter(f.Lock); }
        }
    }

    public static bool ParseIntText(string s, int size, out Value v)
    {
        v = default;
        string t = s.Replace(" ", "");
        if (t.Length == 0 || t == "+" || t == "-") return false;
        if (!BigInteger.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var b)) return false;
        if (size == 0) { if (b > int.MaxValue || b < int.MinValue) return false; v = Value.I((long)b); return true; }
        if (BigInteger.Abs(b) > Prelude.MaxLongInt(size)) return false;
        v = Value.Obj(b); return true;
    }

    public static bool ParseRealText(string s, int size, out Value v)
    {
        v = default;
        string t = s.Replace(" ", "");
        if (t.Length == 0 || !t.Any(char.IsDigit)) return false;
        if (t.EndsWith("e") || t.EndsWith("e+") || t.EndsWith("e-")) return false;
        if (size == 0)
        {
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || double.IsInfinity(d)) return false;
            v = Value.R(d); return true;
        }
        try { v = Value.Obj(MpReal.Parse(t, Prelude.RealDigits(size))); return true; }
        catch { return false; }
    }

    /// <summary>Reads a value into the name r (mode REF X).</summary>
    public void GetValue(Frame fr, Value refFile, FileV f, Mode refMode, Value target, Node at)
    {
        if (refMode is ProcMode)
        {
            var proc = target.O as ProcV ?? throw RT.Err("uninitialised layout procedure", at);
            Monitor.Exit(f.Lock);
            try { Calls.Invoke(proc, new[] { refFile }, fr, at, fr.Depth + 1000); }
            finally { Monitor.Enter(f.Lock); }
            return;
        }
        if (refMode is not RefMode rm) throw RT.Err($"input into a value of mode {refMode} (a name is expected)", at);
        var r = target.O as RefV ?? throw RT.Err("name expected for input", at);
        if (r.A == null) throw RT.Err("input into NIL", at);
        try { ReadInto(fr, refFile, f, rm.Sub, r.A, r.I, at); }
        catch (EofMended) { }
    }

    void ReadInto(Frame fr, Value refFile, FileV f, Mode m, Value[] arr, int idx, Node at)
    {
        switch (m)
        {
            case PrimMode p:
                CheckEof(fr, refFile, f, at);
                switch (p.Kind)
                {
                    case Prim.Int:
                        {
                            var s = ScanInteger(fr, refFile, f, at);
                            if (ParseIntText(s, p.Size, out var v)) arr[idx] = v; else ValueError(fr, refFile, f, m, s, at);
                            return;
                        }
                    case Prim.Real:
                        {
                            var s = ScanReal(fr, refFile, f, at);
                            if (ParseRealText(s, p.Size, out var v)) arr[idx] = v; else ValueError(fr, refFile, f, m, s, at);
                            return;
                        }
                    case Prim.Bool:
                        {
                            int ch = SkipSpacesAndLines(fr, refFile, f, at);
                            if (ch < 0) { EofDuringRead(fr, refFile, f, at); return; }
                            if (ch == 'T') arr[idx] = Value.B(true);
                            else if (ch == 'F') arr[idx] = Value.B(false);
                            else ValueError(fr, refFile, f, m, ch < 0 ? "" : ((char)ch).ToString(), at);
                            return;
                        }
                    case Prim.Char:
                        {
                            int ch = ReadChar(f);
                            ch = SkipNlFf(fr, refFile, f, ch, at);
                            if (ch < 0) { Monitor.Exit(f.Lock); try { LogicalFileEnd(fr, refFile, f, at); } finally { Monitor.Enter(f.Lock); } return; }
                            arr[idx] = Value.I(ch);
                            return;
                        }
                    case Prim.Bits:
                        {
                            int ch = SkipSpacesAndLines(fr, refFile, f, at);
                            UnreadLast(f, ch);
                            var s = ScanWhile(f, c => c == 'T' || c == 'F');
                            int w = Prelude.BitsWidthOf(p.Size);
                            if (s.Length == 0 || s.Length > w) { ValueError(fr, refFile, f, m, s, at); return; }
                            BigInteger b = 0;
                            foreach (char c in s) b = (b << 1) | (c == 'T' ? 1 : 0);
                            arr[idx] = p.Size == 0 ? Value.I((long)b) : Value.Obj(b);
                            return;
                        }
                    case Prim.Bytes:
                        {
                            var s = ScanString(fr, refFile, f, at);
                            int w = p.Size == 0 ? Prelude.BytesWidth : Prelude.LongBytesWidth;
                            arr[idx] = Value.Obj(s.Length >= w ? s[..w] : s.PadRight(w, '\0'));
                            return;
                        }
                }
                break;
            case FlexMode fm when fm.Row.Dims == 1 && fm.Row.Elem is PrimMode { Kind: Prim.Char }:
                {
                    CheckEof(fr, refFile, f, at);
                    var s = ScanString(fr, refFile, f, at);
                    arr[idx] = RT.StrValue(s);
                    return;
                }
            case StructMode sm:
                {
                    var fields = arr[idx].O as Value[] ?? throw RT.Err("uninitialised structure in input", at);
                    for (int i = 0; i < sm.Fields.Length; i++) ReadInto(fr, refFile, f, sm.Fields[i].Mode, fields, i, at);
                    return;
                }
            case RowMode or FlexMode:
                {
                    var rmode = m.AsRow!;
                    var row = arr[idx].O as RowV ?? throw RT.Err("uninitialised row in input", at);
                    foreach (int k in row.Indices())
                    {
                        var (a2, i2) = row.SlotAt(k);
                        ReadInto(fr, refFile, f, rmode.Elem, a2, i2, at);
                    }
                    return;
                }
            case UnionMode:
                {
                    if (arr[idx].O is UnionV u)
                    {
                        var tmp = new[] { u.V };
                        ReadInto(fr, refFile, f, u.M, tmp, 0, at);
                        arr[idx] = Value.Obj(new UnionV(u.M, tmp[0]));
                        return;
                    }
                    throw RT.Err("input into an uninitialised union", at);
                }
        }
        throw RT.Err($"input is not possible for mode {m}", at);
    }

    string ScanString(Frame fr, Value refFile, FileV f, Node at)
    {
        var sb = new StringBuilder();
        while (true)
        {
            int c = PeekChar(f);
            if (c < 0) { if (sb.Length == 0) { Monitor.Exit(f.Lock); try { LogicalFileEnd(fr, refFile, f, at); } finally { Monitor.Enter(f.Lock); } } break; }
            if (IsNlFf(c))
            {
                var p = c == '\n' ? f.OnLineEnd : f.OnPageEnd;
                if (p != null) { Monitor.Exit(f.Lock); try { CallEvent(fr, p, refFile, at); } finally { Monitor.Enter(f.Lock); } }
                break;
            }
            if (f.Term.Length > 0 && f.Term.IndexOf((char)c) >= 0) break;
            sb.Append((char)ReadChar(f));
        }
        return sb.ToString();
    }

    public void GetList(Frame fr, Value refFile, Value row, Node at)
    {
        var f = GetFile(refFile, at);
        var items = RowOps.GetRow(row, at).Elements();
        lock (f.Lock)
        {
            SetReadMood(f, at);
            foreach (var it in items)
            {
                if (it.O is not UnionV u) throw RT.Err("uninitialised input item", at);
                GetValue(fr, refFile, f, u.M, u.V, at);
            }
        }
    }

    // ------------------------------------------------------------------ binary transput

    public void PutBin(Frame fr, Value refFile, Value row, Node at)
    {
        var f = GetFile(refFile, at);
        lock (f.Lock)
        {
            CheckOpen(f, at);
            if (!f.Channel.Bin && f.Kind != FileKind.Disk) throw RT.Err($"the channel of '{f.Idf}' does not allow binary transput", at);
            SetWriteMood(f, at);
            foreach (var it in RowOps.GetRow(row, at).Elements())
            {
                if (it.O is not UnionV u) throw RT.Err("uninitialised element", at);
                var sb = new StringBuilder();
                ToBin(u.M, u.V, sb, at);
                WriteString(f, sb.ToString(), at);
            }
        }
    }

    // binary representation: compact delimited text (one "record" per simple value)
    static void ToBin(Mode m, Value v, StringBuilder sb, Node at)
    {
        if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err("binary transput of an uninitialised value", at);
        switch (m)
        {
            case PrimMode p:
                switch (p.Kind)
                {
                    case Prim.Int: sb.Append(p.Size == 0 ? v.N.ToString(CultureInfo.InvariantCulture) : ((BigInteger)v.O!).ToString(CultureInfo.InvariantCulture)); break;
                    case Prim.Real: sb.Append(p.Size == 0 ? v.Real.ToString("R", CultureInfo.InvariantCulture) : ((MpReal)v.O!).ToString()); break;
                    case Prim.Bool: sb.Append(v.Bool ? 'T' : 'F'); break;
                    case Prim.Char: sb.Append(((int)v.N).ToString(CultureInfo.InvariantCulture)); break;
                    case Prim.Bits: sb.Append(p.Size == 0 ? ((uint)v.N).ToString(CultureInfo.InvariantCulture) : ((BigInteger)v.O!).ToString(CultureInfo.InvariantCulture)); break;
                    case Prim.Bytes: sb.Append(Convert.ToBase64String(Encoding.Unicode.GetBytes((string)v.O!))); break;
                    default: throw RT.Err($"binary transput is not possible for {m}", at);
                }
                sb.Append('\u001e');
                return;
            case UnionMode: { var u = (UnionV)v.O!; ToBin(u.M, u.V, sb, at); return; }
            case StructMode sm: { var fl = (Value[])v.O!; for (int i = 0; i < fl.Length; i++) ToBin(sm.Fields[i].Mode, fl[i], sb, at); return; }
            case RowMode or FlexMode:
                {
                    var rm = m.AsRow!; var row = (RowV)v.O!;
                    sb.Append(row.Count.ToString(CultureInfo.InvariantCulture)).Append('\u001e');
                    foreach (var e in row.Elements()) ToBin(rm.Elem, e, sb, at);
                    return;
                }
        }
        throw RT.Err($"binary transput is not possible for {m}", at);
    }

    public void GetBin(Frame fr, Value refFile, Value row, Node at)
    {
        var f = GetFile(refFile, at);
        lock (f.Lock)
        {
            CheckOpen(f, at);
            SetReadMood(f, at);
            foreach (var it in RowOps.GetRow(row, at).Elements())
            {
                if (it.O is not UnionV u) throw RT.Err("uninitialised element", at);
                if (u.M is ProcMode) { GetValue(fr, refFile, f, u.M, u.V, at); continue; }
                var r = u.V.O as RefV ?? throw RT.Err("name expected", at);
                FromBin(fr, refFile, f, ((RefMode)u.M).Sub, r.A!, r.I, at);
            }
        }
    }

    string NextRecord(Frame fr, Value refFile, FileV f, Node at)
    {
        var sb = new StringBuilder();
        while (true)
        {
            int c = ReadChar(f);
            if (c < 0)
            {
                if (sb.Length == 0) { Monitor.Exit(f.Lock); try { LogicalFileEnd(fr, refFile, f, at); } finally { Monitor.Enter(f.Lock); } }
                break;
            }
            if (c == '\u001e') break;
            sb.Append((char)c);
        }
        return sb.ToString();
    }

    void FromBin(Frame fr, Value refFile, FileV f, Mode m, Value[] arr, int idx, Node at)
    {
        switch (m)
        {
            case PrimMode p:
                {
                    var s = NextRecord(fr, refFile, f, at);
                    try
                    {
                        arr[idx] = p.Kind switch
                        {
                            Prim.Int => p.Size == 0 ? Value.I(long.Parse(s, CultureInfo.InvariantCulture)) : Value.Obj(BigInteger.Parse(s, CultureInfo.InvariantCulture)),
                            Prim.Real => p.Size == 0 ? Value.R(double.Parse(s, CultureInfo.InvariantCulture)) : Value.Obj(MpReal.Parse(s, Prelude.RealDigits(p.Size))),
                            Prim.Bool => Value.B(s == "T"),
                            Prim.Char => Value.I(int.Parse(s, CultureInfo.InvariantCulture)),
                            Prim.Bits => p.Size == 0 ? Value.I((long)uint.Parse(s, CultureInfo.InvariantCulture)) : Value.Obj(BigInteger.Parse(s, CultureInfo.InvariantCulture)),
                            Prim.Bytes => Value.Obj(Encoding.Unicode.GetString(Convert.FromBase64String(s))),
                            _ => throw new FormatException(),
                        };
                    }
                    catch (FormatException) { throw RT.Err($"invalid binary data for mode {m}", at); }
                    return;
                }
            case StructMode sm:
                {
                    var fl = arr[idx].O as Value[] ?? throw RT.Err("uninitialised structure", at);
                    for (int i = 0; i < sm.Fields.Length; i++) FromBin(fr, refFile, f, sm.Fields[i].Mode, fl, i, at);
                    return;
                }
            case FlexMode fm:
                {
                    var n = int.Parse(NextRecord(fr, refFile, f, at), CultureInfo.InvariantCulture);
                    var data = new Value[n];
                    for (int i = 0; i < n; i++) FromBin(fr, refFile, f, fm.Row.Elem, data, i, at);
                    arr[idx] = Value.Obj(RowV.Make1(data));
                    return;
                }
            case RowMode rm:
                {
                    var row = arr[idx].O as RowV ?? throw RT.Err("uninitialised row", at);
                    var cnt = int.Parse(NextRecord(fr, refFile, f, at), CultureInfo.InvariantCulture);
                    if (cnt != row.Count) throw RT.Err($"binary input: {cnt} elements recorded for a row of {row.Count}", at);
                    foreach (int k in row.Indices()) { var (a2, i2) = row.SlotAt(k); FromBin(fr, refFile, f, rm.Elem, a2, i2, at); }
                    return;
                }
        }
        throw RT.Err($"binary transput is not possible for {m}", at);
    }
}
