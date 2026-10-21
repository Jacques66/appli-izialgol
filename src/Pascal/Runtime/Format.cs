using System.Globalization;
using System.Text;
using Studio.Pascal.Semantics;

namespace Studio.Pascal.Runtime;

/// <summary>Conversion of values to text (write, debugger, messages).</summary>
public static class Format
{
    public const int DefaultIntWidth = 10, DefaultRealWidth = 22, DefaultBoolWidth = 10;
    public const int ExpDigits = 3;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Text of an ordinal value of type t (char as 'c', enumeration constant names).</summary>
    public static string OrdText(PType t, long v)
    {
        var h = t.Host;
        switch (h.Kind)
        {
            case TK.Char: return v >= 32 && v < 127 || v >= 160 && v <= 255 ? "'" + ((char)v == '\'' ? "''" : ((char)v).ToString()) + "'" : $"chr({v})";
            case TK.Boolean: return v == 0 ? "false" : v == 1 ? "true" : v.ToString(Inv);
            case TK.Enum:
                var e = (EnumType)h;
                return v >= 0 && v < e.Names.Count ? e.Names[(int)v] : v.ToString(Inv);
            default: return v.ToString(Inv);
        }
    }

    public static string Pad(string s, int w) => s.Length >= w ? s : new string(' ', w - s.Length) + s;

    public static string Int(long v, int w) => Pad(v.ToString(Inv), w);

    public static string Bool(bool b, int w) => Pad(b ? "TRUE" : "FALSE", w);

    /// <summary>Decimal digits of |x| (x != 0) from its shortest round-trip representation:
    /// |x| = 0.D * 10^P (D without leading zeros).</summary>
    static (string D, int P) Digits(double x)
    {
        string sh = Math.Abs(x).ToString("R", Inv);            // shortest round-trip form
        int e = sh.IndexOfAny(new[] { 'E', 'e' });
        string mant = e < 0 ? sh : sh[..e];
        int exp = e < 0 ? 0 : int.Parse(sh[(e + 1)..], Inv);
        int dot = mant.IndexOf('.');
        string digits = dot < 0 ? mant : mant.Remove(dot, 1);
        int intLen = dot < 0 ? mant.Length : dot;
        int lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0') lead++;
        digits = digits[lead..].TrimEnd('0');
        if (digits.Length == 0) return ("0", 1);
        return (digits, intLen - lead + exp);
    }

    /// <summary>Keeps the first n digits of D, rounding halves away from zero (the carry may add a digit).</summary>
    static (string D, int P) Round(string d, int p, int n)
    {
        if (n < 0) return ("", p);
        if (d.Length <= n) return (d.PadRight(n, '0'), p);
        var a = d[..n].ToCharArray();
        if (d[n] >= '5')
        {
            int k = n - 1;
            while (k >= 0)
            {
                if (a[k] == '9') { a[k] = '0'; k--; }
                else { a[k]++; break; }
            }
            if (k < 0) return ("1" + new string(a), p + 1);
        }
        return (new string(a), p);
    }

    /// <summary>Floating-point form (Revised Report 12.3, Pascal 6000): sign or blank, one digit, point,
    /// w - 8 fraction digits (at least 1), E, exponent sign, 3 exponent digits.</summary>
    public static string RealE(double x, int w)
    {
        int frac = Math.Max(w - ExpDigits - 5, 1);
        string mant; int exp;
        if (x == 0) { mant = "0." + new string('0', frac); exp = 0; }
        else
        {
            var (d, p) = Digits(x);
            var (rd, rp) = Round(d, p, frac + 1);
            if (rd.Length > frac + 1) rd = rd[..(frac + 1)];
            mant = rd[0] + "." + rd[1..];
            exp = rp - 1;
        }
        string es = Math.Abs(exp).ToString(Inv).PadLeft(ExpDigits, '0');
        return Pad((x < 0 ? "-" : " ") + mant + "E" + (exp < 0 ? "-" : "+") + es, w);
    }

    /// <summary>Fixed-point form x:w:d (rounded to d decimals, halves away from zero).</summary>
    public static string RealF(double x, int w, int d)
    {
        string ip, fp;
        bool nonzero = false;
        if (x == 0) { ip = "0"; fp = new string('0', d); }
        else
        {
            var (dg, p) = Digits(x);
            int keep = p + d;
            string rd; int rp;
            if (keep < 0) { rd = ""; rp = p; }
            else if (keep == 0) { (rd, rp) = dg[0] >= '5' ? ("1", p + 1) : ("", p); }
            else (rd, rp) = Round(dg, p, keep);
            // rd holds the digits from 10^(rp-1) down to 10^-d
            int total = rp + d;
            if (rd.Length < total) rd = rd.PadLeft(total, '0');
            if (total <= 0) { rd = ""; rp = -d; }
            ip = rp > 0 ? rd[..rp] : "0";
            fp = rp > 0 ? rd[rp..] : rd.PadLeft(d, '0');
            fp = fp.Length > d ? fp[..d] : fp.PadRight(d, '0');
            nonzero = (ip + fp).Any(c => c is >= '1' and <= '9');
        }
        return Pad((x < 0 && nonzero ? "-" : "") + ip + "." + fp, w);
    }

    /// <summary>Real value for the debugger and messages (shortest round-trip form).</summary>
    public static string RealShort(double x)
    {
        var s = x.ToString("R", Inv);
        if (!s.Contains('.') && !s.Contains('E') && !s.Contains("Infinity") && !s.Contains("NaN")) s += ".0";
        return s;
    }

    public static string SetText(PType t, PSet s)
    {
        var bt = (t as SetType)?.Base ?? IntegerType.T;
        var sb = new StringBuilder("[");
        bool first = true;
        long v = 0;
        while (v < 256)
        {
            if (!s.Contains(v)) { v++; continue; }
            long st = v;
            while (v + 1 < 256 && s.Contains(v + 1)) v++;
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(OrdText(bt, st));
            if (v > st) sb.Append(v == st + 1 ? ", " : "..").Append(OrdText(bt, v));
            v++;
        }
        return sb.Append(']').ToString();
    }
}
