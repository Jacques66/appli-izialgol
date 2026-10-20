using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Studio.Simula.Runtime;

public delegate Value NativeFn(Vm vm, Value[] s, int b);

/// <summary>Primitives d'execution : textes, mathematiques, edition, fichiers.</summary>
public static class Natives
{
    sealed record Entry(string Name, string Sig, NativeFn Fn, bool Void);

    static readonly List<Entry> entries = new();
    static readonly Dictionary<string, int> byName = new();

    public static bool Exists(string name) => byName.ContainsKey(name);
    public static int Id(string name) => byName.TryGetValue(name, out var i) ? i : throw new InvalidOperationException("unknown primitive " + name);
    public static string Signature(string name) => entries[Id(name)].Sig;
    public static NativeFn Fn(int id) => entries[id].Fn;
    public static bool IsVoid(int id) => entries[id].Void;
    public static string NameOf(int id) => entries[id].Name;

    static void Def(string name, string sig, NativeFn fn)
    {
        byName[name] = entries.Count;
        entries.Add(new Entry(name, sig, fn, sig.EndsWith(">V")));
    }

    static readonly Value Nothing = default;
    static TextRef? T(Value v) => (TextRef?)v.O;
    static Value Tx(TextRef? t) => new() { O = t };
    static Value I(long v) => new() { I = v };
    static Value R(double v) => Value.Real(v);
    static Value B(bool v) => new() { I = v ? 1 : 0 };

    public static int ToInt(double r)
    {
        var x = Math.Floor(r + 0.5);
        if (double.IsNaN(x) || x > int.MaxValue || x < int.MinValue) throw new SimulaError("real -> integer conversion: value out of range");
        return (int)x;
    }

    public static TextRef? CopyText(TextRef? t)
    {
        if (t == null) return null;
        var a = new char[t.Length];
        Array.Copy(t.Obj.Chars, t.Start - 1, a, 0, t.Length);
        return new TextRef(new TextObj(a, false), 1, t.Length);
    }

    public static int CompareText(TextRef? a, TextRef? b)
    {
        int la = TextRef.Len(a), lb = TextRef.Len(b);
        int n = Math.Min(la, lb);
        for (int i = 0; i < n; i++)
        {
            int c = a![i].CompareTo(b![i]);
            if (c != 0) return c;
        }
        return la.CompareTo(lb);
    }

    public static void AssignText(TextRef? x, TextRef? y)
    {
        int ly = TextRef.Len(y);
        if (x == null)
        {
            if (ly != 0) throw new SimulaError("text assignment: the target is notext");
            return;
        }
        if (x.Obj.Const) throw new SimulaError("text assignment: the target is a constant");
        if (x.Length < ly) throw new SimulaError($"text assignment: target too short ({x.Length} < {ly})");
        if (ly > 0)
        {
            // safe copy even when the frames overlap
            var tmp = new char[ly];
            Array.Copy(y!.Obj.Chars, y.Start - 1, tmp, 0, ly);
            Array.Copy(tmp, 0, x.Obj.Chars, x.Start - 1, ly);
        }
        for (int i = ly; i < x.Length; i++) x[i] = ' ';
    }

    public static TextRef? Concat(Value[] s, int b, int n)
    {
        int total = 0;
        for (int i = 0; i < n; i++) total += TextRef.Len(T(s[b + i]));
        if (total == 0) return null;
        var a = new char[total];
        int p = 0;
        for (int i = 0; i < n; i++)
        {
            var t = T(s[b + i]);
            if (t == null) continue;
            Array.Copy(t.Obj.Chars, t.Start - 1, a, p, t.Length);
            p += t.Length;
        }
        return new TextRef(new TextObj(a, false), 1, total);
    }

    // ------------------------------------------------------------ edition (8.7)

    public static char LowTen = '&', DecimalMark = '.';

    static void PutItem(TextRef? t, string item)
    {
        if (t == null || t.Obj.Const) throw new SimulaError("editing into a constant text or notext");
        if (item.Length > t.Length)
        {
            for (int i = 0; i < t.Length; i++) t[i] = '*';
        }
        else
        {
            int pad = t.Length - item.Length;
            for (int i = 0; i < pad; i++) t[i] = ' ';
            for (int i = 0; i < item.Length; i++) t[pad + i] = item[i];
        }
        t.Pos = t.Length + 1;
    }

    public static string IntItem(long i) => i.ToString(CultureInfo.InvariantCulture);

    public static string FixItem(double r, int n)
    {
        if (n < 0) throw new SimulaError("putfix: negative number of decimals");
        if (double.IsNaN(r) || double.IsInfinity(r)) return "*";
        var s = r.ToString("F" + n, CultureInfo.InvariantCulture);
        if (s.StartsWith("-") && s.Trim('-', '0', '.').Length == 0) s = s.Substring(1);
        return DecimalMark == '.' ? s : s.Replace('.', DecimalMark);
    }

    public static string RealItem(double r, int n)
    {
        if (n < 0) throw new SimulaError("putreal: negative number of digits");
        if (double.IsNaN(r) || double.IsInfinity(r)) return "*";
        if (n == 0)
        {
            int e0 = r == 0 ? 0 : (int)Math.Floor(Math.Log10(Math.Abs(r)) + 1e-12);
            // SIGN-PART EXPONENT form: implicit digit
            return (r < 0 ? "-" : "") + LowTen + Exp(e0 + (Math.Abs(r) / Math.Pow(10, e0) >= 5 ? 1 : 0));
        }
        if (r == 0)
        {
            var z = "0" + (n > 1 ? DecimalMark + new string('0', n - 1) : "");
            return z + LowTen + Exp(0);
        }
        var s = r.ToString("E" + (n - 1), CultureInfo.InvariantCulture); // d.dddE+xxx
        int ei = s.IndexOf('E');
        var mant = s.Substring(0, ei);
        int ex = int.Parse(s.Substring(ei + 1), CultureInfo.InvariantCulture);
        if (DecimalMark != '.') mant = mant.Replace('.', DecimalMark);
        return mant + LowTen + Exp(ex);
    }

    static string Exp(int e) => (e < 0 ? "-" : "+") + Math.Abs(e).ToString(Math.Abs(e) >= 100 ? "000" : "00", CultureInfo.InvariantCulture);

    public static string FracItem(long i, int n)
    {
        // groups of 3 digits, decimal mark followed by n digits
        bool neg = i < 0;
        var digits = Math.Abs(i).ToString(CultureInfo.InvariantCulture);
        if (n > 0 && digits.Length <= n) digits = new string('0', n - digits.Length + 1) + digits;
        string ip = n > 0 ? digits.Substring(0, digits.Length - n) : digits;
        string fp = n > 0 ? digits.Substring(digits.Length - n) : "";
        var sb = new StringBuilder();
        int first = ip.Length % 3;
        if (first == 0) first = 3;
        sb.Append(ip, 0, Math.Min(first, ip.Length));
        for (int k = first; k < ip.Length; k += 3) sb.Append(' ').Append(ip, k, 3);
        if (n > 0)
        {
            sb.Append(DecimalMark);
            for (int k = 0; k < fp.Length; k += 3)
            {
                if (k > 0) sb.Append(' ');
                sb.Append(fp, k, Math.Min(3, fp.Length - k));
            }
        }
        return (neg ? "-" : "") + sb;
    }

    // ------------------------------------------------------------ de-editing (8.6)

    static bool IsBlank(char c) => c == ' ' || c == '\t';

    public static long GetInt(TextRef? t)
    {
        int len = TextRef.Len(t), i = 0;
        while (i < len && IsBlank(t![i])) i++;
        bool neg = false;
        if (i < len && (t![i] == '+' || t[i] == '-')) { neg = t[i] == '-'; i++; while (i < len && IsBlank(t[i])) i++; }
        int start = i;
        long v = 0;
        while (i < len && char.IsAsciiDigit(t![i]))
        {
            v = v * 10 + (t[i] - '0');
            if (v > 2147483648L) throw new SimulaError("getint: integer out of range");
            i++;
        }
        if (i == start) throw new SimulaError("getint: no integer in the text");
        if (neg) v = -v;
        if (v > int.MaxValue) throw new SimulaError("getint: integer out of range");
        t!.Pos = i + 1;
        return v;
    }

    public static double GetReal(TextRef? t)
    {
        int len = TextRef.Len(t), i = 0;
        while (i < len && IsBlank(t![i])) i++;
        var sb = new StringBuilder();
        if (i < len && (t![i] == '+' || t[i] == '-')) { if (t[i] == '-') sb.Append('-'); i++; while (i < len && IsBlank(t[i])) i++; }
        bool digits = false;
        while (i < len && char.IsAsciiDigit(t![i])) { sb.Append(t[i]); i++; digits = true; }
        if (i < len && (t![i] == DecimalMark || t[i] == '.') && i + 1 < len && char.IsAsciiDigit(t[i + 1]))
        {
            sb.Append('.'); i++;
            while (i < len && char.IsAsciiDigit(t[i])) { sb.Append(t[i]); i++; digits = true; }
        }
        if (i < len && (t![i] == LowTen || t[i] == '&' || t[i] == 'e' || t[i] == 'E'))
        {
            int save = i;
            int j = i + 1;
            if (j < len && t[j] == '&') j++;
            var es = new StringBuilder("E");
            while (j < len && IsBlank(t[j])) j++;
            if (j < len && (t[j] == '+' || t[j] == '-')) { es.Append(t[j]); j++; }
            while (j < len && IsBlank(t[j])) j++;
            int ds = j;
            while (j < len && char.IsAsciiDigit(t[j])) { es.Append(t[j]); j++; }
            if (j > ds)
            {
                if (!digits) sb.Append('1');
                digits = true;
                sb.Append(es);
                i = j;
            }
            else i = save;
        }
        if (!digits) throw new SimulaError("getreal: no real number in the text");
        t!.Pos = i + 1;
        return double.Parse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public static long GetFrac(TextRef? t)
    {
        int len = TextRef.Len(t), i = 0;
        while (i < len && IsBlank(t![i])) i++;
        bool neg = false;
        if (i < len && (t![i] == '+' || t[i] == '-')) { neg = t[i] == '-'; i++; while (i < len && IsBlank(t[i])) i++; }
        long v = 0;
        bool digits = false, mark = false;
        int last = i;
        while (i < len)
        {
            char c = t![i];
            if (char.IsAsciiDigit(c)) { v = v * 10 + (c - '0'); digits = true; i++; last = i; if (v > int.MaxValue) throw new SimulaError("getfrac: out of range"); continue; }
            if (c == ' ' && i + 1 < len && char.IsAsciiDigit(t[i + 1]) && digits) { i++; continue; }
            if ((c == DecimalMark || c == '.') && !mark && i + 1 < len && char.IsAsciiDigit(t[i + 1])) { mark = true; i++; continue; }
            break;
        }
        if (!digits) throw new SimulaError("getfrac: no number in the text");
        t!.Pos = last + 1;
        return neg ? -v : v;
    }

    // ------------------------------------------------------------ alea (9.9)

    public static long NextU(long u)
    {
        // generateur multiplicatif U(i+1) = U(i) * 5**13 mod 2**31 (impair)
        bool neg = u < 0;
        ulong x = (ulong)Math.Abs(u);
        x |= 1;
        x = (x * 1220703125UL) & 0x7FFFFFFFUL;
        if (x == 0) x = 1;
        return neg ? -(long)x : (long)x;
    }

    public static double Frac(long u)
    {
        double f = Math.Abs(u) / 2147483648.0;
        return u < 0 ? 1.0 - f : f;
    }

    /// <summary>Inverse of the normal cumulative distribution function (Acklam's algorithm).</summary>
    public static double InvNorm(double p)
    {
        if (p <= 0) p = 1e-300;
        if (p >= 1) p = 1 - 1e-16;
        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        double[] bb = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
        const double pl = 0.02425, ph = 1 - pl;
        double q, r;
        if (p < pl)
        {
            q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        if (p > ph)
        {
            q = Math.Sqrt(-2 * Math.Log(1 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        q = p - 0.5; r = q * q;
        return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q / (((((bb[0] * r + bb[1]) * r + bb[2]) * r + bb[3]) * r + bb[4]) * r + 1);
    }

    // ------------------------------------------------------------ table

    static Natives()
    {
        // ---- arithmetique
        Def("absi", "I>I", (vm, s, b) => I(Math.Abs(s[b].I)));
        Def("absr", "R>R", (vm, s, b) => R(Math.Abs(s[b].R)));
        Def("signi", "I>I", (vm, s, b) => I(Math.Sign(s[b].I)));
        Def("signr", "R>I", (vm, s, b) => I(Math.Sign(s[b].R)));
        Def("entier", "R>I", (vm, s, b) =>
        {
            var f = Math.Floor(s[b].R);
            if (double.IsNaN(f) || f > int.MaxValue || f < int.MinValue) throw new SimulaError("entier: value out of range");
            return I((long)f);
        });
        Def("mod", "II>I", (vm, s, b) =>
        {
            long i = s[b].I, j = s[b + 1].I;
            if (j == 0) throw new SimulaError("mod: division by zero");
            long res = i - (i / j) * j;
            return I(res == 0 ? 0 : Math.Sign(res) != Math.Sign(j) ? res + j : res);
        });
        Def("rem", "II>I", (vm, s, b) =>
        {
            long j = s[b + 1].I;
            if (j == 0) throw new SimulaError("rem: division by zero");
            return I(s[b].I - (s[b].I / j) * j);
        });
        Def("maxi", "II>I", (vm, s, b) => I(Math.Max(s[b].I, s[b + 1].I)));
        Def("mini", "II>I", (vm, s, b) => I(Math.Min(s[b].I, s[b + 1].I)));
        Def("maxr", "RR>R", (vm, s, b) => R(Math.Max(s[b].R, s[b + 1].R)));
        Def("minr", "RR>R", (vm, s, b) => R(Math.Min(s[b].R, s[b + 1].R)));
        Def("maxt", "TT>T", (vm, s, b) => CompareText(T(s[b]), T(s[b + 1])) >= 0 ? Tx(T(s[b])?.Clone()) : Tx(T(s[b + 1])?.Clone()));
        Def("mint", "TT>T", (vm, s, b) => CompareText(T(s[b]), T(s[b + 1])) <= 0 ? Tx(T(s[b])?.Clone()) : Tx(T(s[b + 1])?.Clone()));
        Def("sqrt", "R>R", (vm, s, b) => { var x = s[b].R; if (x < 0) throw new SimulaError("sqrt: negative argument"); return R(Math.Sqrt(x)); });
        Def("sin", "R>R", (vm, s, b) => R(Math.Sin(s[b].R)));
        Def("cos", "R>R", (vm, s, b) => R(Math.Cos(s[b].R)));
        Def("tan", "R>R", (vm, s, b) => R(Math.Tan(s[b].R)));
        Def("cotan", "R>R", (vm, s, b) => { var t = Math.Tan(s[b].R); if (t == 0) throw new SimulaError("cotan : argument invalide"); return R(1 / t); });
        Def("arcsin", "R>R", (vm, s, b) => { var x = s[b].R; if (x < -1 || x > 1) throw new SimulaError("arcsin: argument outside [-1,1]"); return R(Math.Asin(x)); });
        Def("arccos", "R>R", (vm, s, b) => { var x = s[b].R; if (x < -1 || x > 1) throw new SimulaError("arccos: argument outside [-1,1]"); return R(Math.Acos(x)); });
        Def("arctan", "R>R", (vm, s, b) => R(Math.Atan(s[b].R)));
        Def("arctan2", "RR>R", (vm, s, b) => { if (s[b].R == 0 && s[b + 1].R == 0) throw new SimulaError("arctan2 : arguments nuls"); return R(Math.Atan2(s[b].R, s[b + 1].R)); });
        Def("sinh", "R>R", (vm, s, b) => R(Math.Sinh(s[b].R)));
        Def("cosh", "R>R", (vm, s, b) => R(Math.Cosh(s[b].R)));
        Def("tanh", "R>R", (vm, s, b) => R(Math.Tanh(s[b].R)));
        Def("ln", "R>R", (vm, s, b) => { var x = s[b].R; if (x <= 0) throw new SimulaError("ln: argument not positive"); return R(Math.Log(x)); });
        Def("log10", "R>R", (vm, s, b) => { var x = s[b].R; if (x <= 0) throw new SimulaError("log10: argument not positive"); return R(Math.Log10(x)); });
        Def("exp", "R>R", (vm, s, b) => R(Math.Exp(s[b].R)));
        Def("addepsilon", "R>R", (vm, s, b) => R(Math.BitIncrement(s[b].R)));
        Def("subepsilon", "R>R", (vm, s, b) => R(Math.BitDecrement(s[b].R)));
        // ---- characters and texts
        Def("char", "I>C", (vm, s, b) => { var i = s[b].I; if (i < 0 || i > 255) throw new SimulaError($"char: {i} outside 0..255"); return I(i); });
        Def("isochar", "I>C", (vm, s, b) => { var i = s[b].I; if (i < 0 || i > 255) throw new SimulaError($"isochar: {i} outside 0..255"); return I(i); });
        Def("rank", "C>I", (vm, s, b) => I(s[b].I));
        Def("digit", "C>B", (vm, s, b) => B(s[b].I >= '0' && s[b].I <= '9'));
        Def("letter", "C>B", (vm, s, b) => { var c = s[b].I; return B((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')); });
        Def("lowten", "C>C", (vm, s, b) =>
        {
            char c = (char)s[b].I;
            if (char.IsAsciiDigit(c) || c is '+' or '-' or '.' or ',' || c < 32 || c >= 127) throw new SimulaError("lowten: illegal character");
            var old = LowTen; LowTen = c; return I(old);
        });
        Def("decimalmark", "C>C", (vm, s, b) =>
        {
            char c = (char)s[b].I;
            if (c != '.' && c != ',') throw new SimulaError("decimalmark: '.' or ',' expected");
            var old = DecimalMark; DecimalMark = c; return I(old);
        });
        Def("copy", "T>T", (vm, s, b) => Tx(CopyText(T(s[b]))));
        Def("blanks", "I>T", (vm, s, b) => Tx(TextRef.Blanks((int)s[b].I)));
        Def("upcase", "T>T", (vm, s, b) =>
        {
            var t = T(s[b]);
            if (t == null) return Tx(null);
            if (t.Obj.Const) throw new SimulaError("upcase: constant text");
            for (int i = 0; i < t.Length; i++) { var c = t[i]; if (c >= 'a' && c <= 'z') t[i] = (char)(c - 32); }
            t.Pos = 1;
            return Tx(t.Clone());
        });
        Def("lowcase", "T>T", (vm, s, b) =>
        {
            var t = T(s[b]);
            if (t == null) return Tx(null);
            if (t.Obj.Const) throw new SimulaError("lowcase: constant text");
            for (int i = 0; i < t.Length; i++) { var c = t[i]; if (c >= 'A' && c <= 'Z') t[i] = (char)(c + 32); }
            t.Pos = 1;
            return Tx(t.Clone());
        });
        Def("datetime", ">T", (vm, s, b) => Tx(TextRef.FromString(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))));
        Def("cputime", ">L", (vm, s, b) => R(Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds));
        Def("clocktime", ">L", (vm, s, b) => R(DateTime.Now.TimeOfDay.TotalSeconds));
        Def("error", "T>V", (vm, s, b) => throw new SimulaError(TextRef.Str(T(s[b]))));
        Def("upperbound", "AI>I", (vm, s, b) => { var a = (SimArray)s[b].O!; var i = s[b + 1].I; if (i < 1 || i > a.Rank) throw new SimulaError("upperbound : dimension invalide"); return I(a.Upper[i - 1]); });
        Def("lowerbound", "AI>I", (vm, s, b) => { var a = (SimArray)s[b].O!; var i = s[b + 1].I; if (i < 1 || i > a.Rank) throw new SimulaError("lowerbound : dimension invalide"); return I(a.Lower[i - 1]); });
        Def("__switcherror", "I>V", (vm, s, b) => throw new SimulaError($"switch index {s[b].I} out of range"));

        // ---- text attributes (text object first)
        Def("t_length", "T>I", (vm, s, b) => I(TextRef.Len(T(s[b]))));
        Def("t_pos", "T>I", (vm, s, b) => I(T(s[b])?.Pos ?? 1));
        Def("t_start", "T>I", (vm, s, b) => I(T(s[b])?.Start ?? 1));
        Def("t_more", "T>B", (vm, s, b) => { var t = T(s[b]); return B(t != null && t.Pos <= t.Length); });
        Def("t_constant", "T>B", (vm, s, b) => B(T(s[b]) is not { } t || t.Obj.Const));
        Def("t_main", "T>T", (vm, s, b) => { var t = T(s[b]); return Tx(t == null ? null : new TextRef(t.Obj, 1, t.Obj.Chars.Length)); });
        Def("t_setpos", "TI>V", (vm, s, b) =>
        {
            var t = T(s[b]); long i = s[b + 1].I;
            if (t == null) return Nothing;
            t.Pos = i < 1 || i > t.Length + 1 ? t.Length + 1 : (int)i;
            return Nothing;
        });
        Def("t_getchar", "T>C", (vm, s, b) =>
        {
            var t = T(s[b]);
            if (t == null || t.Pos > t.Length) throw new SimulaError("getchar: position outside the text");
            var c = t[t.Pos - 1]; t.Pos++; return I(c);
        });
        Def("t_putchar", "TC>V", (vm, s, b) =>
        {
            var t = T(s[b]);
            if (t == null || t.Obj.Const || t.Pos > t.Length) throw new SimulaError(t == null ? "putchar on notext" : t.Obj.Const ? "putchar on a constant text" : "putchar: position outside the text");
            t[t.Pos - 1] = (char)s[b + 1].I; t.Pos++; return Nothing;
        });
        Def("t_sub", "TII>T", (vm, s, b) =>
        {
            var t = T(s[b]); long i = s[b + 1].I, n = s[b + 2].I;
            int len = TextRef.Len(t);
            if (i < 0 || n < 0 || i + n > len + 1) throw new SimulaError($"sub({i},{n}) outside a text of length {len}");
            if (n == 0) return Tx(null);
            return Tx(new TextRef(t!.Obj, t.Start + (int)i - 1, (int)n));
        });
        Def("t_strip", "T>T", (vm, s, b) =>
        {
            var t = T(s[b]);
            if (t == null) return Tx(null);
            int n = t.Length;
            while (n > 0 && t[n - 1] == ' ') n--;
            return Tx(n == 0 ? null : new TextRef(t.Obj, t.Start, n));
        });
        Def("t_getint", "T>I", (vm, s, b) => I(GetInt(T(s[b]))));
        Def("t_getreal", "T>L", (vm, s, b) => R(GetReal(T(s[b]))));
        Def("t_getfrac", "T>I", (vm, s, b) => I(GetFrac(T(s[b]))));
        Def("t_putint", "TI>V", (vm, s, b) => { PutItem(T(s[b]), IntItem(s[b + 1].I)); return Nothing; });
        Def("t_putfix", "TRI>V", (vm, s, b) => { PutItem(T(s[b]), FixItem(s[b + 1].R, (int)s[b + 2].I)); return Nothing; });
        Def("t_putreal", "TRI>V", (vm, s, b) => { PutItem(T(s[b]), RealItem(s[b + 1].R, (int)s[b + 2].I)); return Nothing; });
        Def("t_putfrac", "TII>V", (vm, s, b) => { PutItem(T(s[b]), FracItem(s[b + 1].I, (int)s[b + 2].I)); return Nothing; });

        // ---- prelude primitives
        Def("__rnext", "I>I", (vm, s, b) => I(NextU(s[b].I)));
        Def("__rfrac", "I>L", (vm, s, b) => R(Frac(s[b].I)));
        Def("__lnr", "R>R", (vm, s, b) => R(Math.Log(s[b].R)));
        Def("__itemlen", "IRII>I", (vm, s, b) =>
        {
            // exact length of an edited item: kind, real, integer, n
            int kind = (int)s[b].I; double r = s[b + 1].R; long v = s[b + 2].I; int n = (int)s[b + 3].I;
            return I(kind switch
            {
                0 => IntItem(v).Length,
                1 => FixItem(r, n).Length,
                2 => RealItem(r, n).Length,
                _ => FracItem(v, n).Length
            });
        });
        Def("__invnorm", "R>R", (vm, s, b) => R(InvNorm(s[b].R)));
        Def("__realbits", "R>I", (vm, s, b) => I(s[b].I));
        // copies characters of t (from t.pos) into image (from image.pos), as many as fit
        Def("__copychars", "TT>V", (vm, s, b) =>
        {
            var img = T(s[b]); var t = T(s[b + 1]);
            if (t == null || img == null) return Nothing;
            if (img.Obj.Const) throw new SimulaError("image constante");
            int n = Math.Min(t.Length - t.Pos + 1, img.Length - img.Pos + 1);
            if (n <= 0) return Nothing;
            Array.Copy(t.Obj.Chars, t.Start - 1 + t.Pos - 1, img.Obj.Chars, img.Start - 1 + img.Pos - 1, n);
            t.Pos += n; img.Pos += n;
            return Nothing;
        });
        Def("__blankimage", "T>V", (vm, s, b) => { var t = T(s[b]); if (t != null) { for (int i = 0; i < t.Length; i++) t[i] = ' '; t.Pos = 1; } return Nothing; });
        FileNatives.Register(Def);
    }

    internal static Value MkText(string s) => Tx(TextRef.FromString(s));
}
