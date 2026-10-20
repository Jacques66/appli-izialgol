using System.Numerics;
using System.Text;

namespace Studio.Algol68.Runtime;

/// <summary>
/// Conversions whole / fixed / float (RR 10.3.2.1); algorithms aligned with Algol 68 Genie
/// so that the output is identical character for character.
/// </summary>
public static class Conv
{
    public const char ErrorChar = '*';

    public static string ErrorChars(int n) => new string(ErrorChar, n == 0 ? 1 : Math.Abs(n));

    static readonly double[] Pow10Tab = { 10.0, 100.0, 1.0e4, 1.0e8, 1.0e16, 1.0e32, 1.0e64, 1.0e128, 1.0e256 };

    public static double TenUp(int expo)
    {
        double d = 1.0;
        bool neg = expo < 0;
        if (neg) expo = -expo;
        if (expo > 308) expo = 308;
        for (int i = 0; expo != 0; expo >>= 1, i++)
            if ((expo & 1) != 0) d *= Pow10Tab[i];
        return neg ? 1 / d : d;
    }

    // ------------------------------------------------------------------ integers

    /// <summary>Digits of n (>= 0); if more than 'width' digits: error characters.</summary>
    public static string SubWhole(BigInteger n, int width)
    {
        string s = BigInteger.Abs(n).ToString();
        if (s.Length > width) return ErrorChars(width);
        return s;
    }

    public static string WholeInt(BigInteger x, int width)
    {
        int length = Math.Abs(width) - (x.Sign < 0 || width > 0 ? 1 : 0);
        var n = BigInteger.Abs(x);
        if (width == 0) length = n.ToString().Length;
        if (length <= 0) return ErrorChars(width);
        string s = SubWhole(n, length);
        if (s.Contains(ErrorChar)) return ErrorChars(width);
        if (x.Sign < 0) s = "-" + s;
        else if (width > 0) s = "+" + s;
        if (width != 0) s = s.PadLeft(Math.Abs(width));
        return s;
    }

    // ------------------------------------------------------------------ REAL (double)

    static char ChooseDig(ref double y)
    {
        int c = (int)(y *= 10);
        if (c > 9) c = 9;
        y -= c;
        return (char)('0' + c);
    }

    public static string SubFixed(double x, int width, int after)
    {
        var sb = new StringBuilder();
        int before = 0;
        double y, z;
        z = y = x + 0.5 * TenUp(-after);
        int expo = 0;
        while (z >= 1) { expo++; z /= 10; }
        before += expo;
        if (expo > 30) { expo -= 30; y /= TenUp(30); }
        y /= TenUp(expo);
        int len = 0;
        bool overflow = false;
        for (int j = 0; j < before && !overflow; j++)
        {
            if (!(overflow = len >= width))
            {
                char ch = len < Prelude.RealWidth ? ChooseDig(ref y) : '0';
                sb.Append(ch); len++;
            }
        }
        if (after > 0 && !(overflow = len >= width)) sb.Append('.');
        for (int j = 0; j < after && !overflow; j++)
        {
            if (!(overflow = len >= width))
            {
                char ch = len < Prelude.RealWidth ? ChooseDig(ref y) : '0';
                sb.Append(ch); len++;
            }
        }
        if (overflow || sb.Length > width) return ErrorChars(width);
        return sb.ToString();
    }

    public static string Fixed(double x, int width, int after)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return ErrorChars(width);
        int length = Math.Abs(width) - (x < 0 || width > 0 ? 1 : 0);
        if (after >= 0 && (length > after || width == 0))
        {
            double y = Math.Abs(x);
            if (width == 0)
            {
                length = after == 0 ? 1 : 0;
                double z0 = TenUp(-after), z1 = TenUp(length);
                while (y + 0.5 * z0 > z1) { length++; z1 *= 10.0; }
                length += after == 0 ? 0 : after + 1;
            }
            string s = SubFixed(y, length, after);
            if (!s.Contains(ErrorChar))
            {
                if (length > s.Length && (s.Length > 0 ? s[0] == '.' : true) && y < 1.0) s = "0" + s;
                if (x < 0) s = "-" + s;
                else if (width > 0) s = "+" + s;
                if (width != 0) s = s.PadLeft(Math.Abs(width));
                return s;
            }
            if (after > 0) return Fixed(x, width, after - 1);
            return ErrorChars(width);
        }
        return ErrorChars(width);
    }

    public static void Standardise(ref double y, int before, int after, ref int p)
    {
        double g = 1.0;
        for (int j = 0; j < before; j++) g *= 10.0;
        double h = g / 10.0;
        while (y >= g) { y *= 0.1; p++; }
        if (y != 0.0) while (y < h) { y *= 10.0; p--; }
        double f = 1.0;
        for (int j = 0; j < after; j++) f *= 0.1;
        if (y + 0.5 * f >= g) { y = h; p++; }
    }

    public static string Real(double x, int width, int after, int expo, int frmt)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return ErrorChars(width);
        int before = Math.Abs(width) - Math.Abs(expo) - (after != 0 ? after + 1 : 0) - 2;
        if (Math.Sign(before) + Math.Sign(after) > 0)
        {
            double y = Math.Abs(x);
            int q = 0;
            Standardise(ref y, before, after, ref q);
            if (frmt > 0)
            {
                while (q % frmt != 0) { y *= 10; q--; if (after > 0) after--; }
            }
            else
            {
                double upb = TenUp(-frmt), lwb = TenUp(-frmt - 1);
                while (y < lwb) { y *= 10; q--; if (after > 0) after--; }
                while (y > upb) { y /= 10; q++; if (after > 0) after++; }
            }
            string t1 = Fixed(Math.Sign(x) * y, Math.Sign(width) * (Math.Abs(width) - Math.Abs(expo) - 1), after);
            string t2 = WholeInt(q, expo);
            string s = t1 + "e" + t2;
            if (expo == 0 || s.Contains(ErrorChar))
                return Real(x, width, after != 0 ? after - 1 : 0, expo > 0 ? expo + 1 : expo - 1, frmt);
            return s;
        }
        return ErrorChars(width);
    }

    public static string Float(double x, int width, int after, int expo) => Real(x, width, after, expo, 1);

    // ------------------------------------------------------------------ LONG REAL (MpReal, exact)

    static MpReal Half10(int after, int digits) => MpReal.Make(5, -after - 1, digits + after + 20);

    public static string LongSubFixed(MpReal x, int width, int after)
    {
        int dg = x.Digits + after + 30;
        var y = MpReal.Add(x.WithDigits(dg), Half10(after, dg));
        // integral part and digits
        var (ds, e10) = y.DecimalDigits();
        if (y.IsZero) { ds = "0"; e10 = 0; }
        // exact decimal representation: digits ds, the first digit has weight 10^e10
        var sb = new StringBuilder();
        int before = e10 >= 0 ? e10 + 1 : 0;
        if (y.IsZero) before = 0;
        int len = 0; bool overflow = false;
        char DigitAt(int pow) // digit of weight 10^pow
        {
            int idx = e10 - pow;
            if (idx < 0 || idx >= ds.Length) return '0';
            return ds[idx];
        }
        for (int j = 0; j < before && !overflow; j++)
            if (!(overflow = len >= width)) { sb.Append(DigitAt(before - 1 - j)); len++; }
        if (after > 0 && !(overflow = len >= width)) sb.Append('.');
        for (int j = 0; j < after && !overflow; j++)
            if (!(overflow = len >= width)) { sb.Append(DigitAt(-1 - j)); len++; }
        if (overflow || sb.Length > width) return ErrorChars(width);
        return sb.ToString();
    }

    public static string LongFixed(MpReal x, int width, int after)
    {
        bool neg = x.Sign < 0;
        var ax = x.Abs();
        int length = Math.Abs(width) - (neg || width > 0 ? 1 : 0);
        if (after >= 0 && (length > after || width == 0))
        {
            if (width == 0)
            {
                length = after == 0 ? 1 : 0;
                int dg = x.Digits + after + 30;
                var z0half = Half10(after, dg);
                var t = MpReal.Add(ax.WithDigits(dg), z0half);
                var z1 = MpReal.Make(1, length, dg);
                while (t.CompareTo(z1) > 0) { length++; z1 = MpReal.Make(1, length, dg); }
                length += after == 0 ? 0 : after + 1;
            }
            string s = LongSubFixed(ax, length, after);
            if (!s.Contains(ErrorChar))
            {
                bool lt1 = ax.CompareTo(MpReal.One(ax.Digits)) < 0;
                if (length > s.Length && (s.Length > 0 ? s[0] == '.' : true) && lt1) s = "0" + s;
                if (neg) s = "-" + s;
                else if (width > 0) s = "+" + s;
                if (width != 0) s = s.PadLeft(Math.Abs(width));
                return s;
            }
            if (after > 0) return LongFixed(x, width, after - 1);
            return ErrorChars(width);
        }
        return ErrorChars(width);
    }

    static void LongStandardise(ref MpReal y, int before, int after, ref int p)
    {
        int dg = y.Digits + after + 30;
        y = y.WithDigits(dg);
        var g = MpReal.Make(1, before, dg);
        var h = MpReal.Make(1, before - 1, dg);
        if (!y.IsZero)
        {
            // direct adjustment of the exponent
            var (ds, e10) = y.DecimalDigits();
            int shift = e10 - (before - 1);
            if (shift != 0) { y = MpReal.Make(y.M, y.E - shift, dg); p += shift; }
            while (y.CompareTo(g) >= 0) { y = MpReal.Make(y.M, y.E - 1, dg); p++; }
            while (y.CompareTo(h) < 0) { y = MpReal.Make(y.M, y.E + 1, dg); p--; }
        }
        var t = MpReal.Add(y, Half10(after, dg));
        if (t.CompareTo(g) >= 0) { y = h; p++; }
    }

    public static string LongReal(MpReal x, int width, int after, int expo, int frmt)
    {
        int before = Math.Abs(width) - Math.Abs(expo) - (after != 0 ? after + 1 : 0) - 2;
        if (Math.Sign(before) + Math.Sign(after) > 0)
        {
            var y = x.Abs();
            int q = 0;
            LongStandardise(ref y, before, after, ref q);
            int dg = y.Digits;
            if (frmt > 0)
            {
                while (q % frmt != 0) { y = MpReal.Make(y.M, y.E + 1, dg); q--; if (after > 0) after--; }
            }
            else
            {
                var upb = MpReal.Make(1, -frmt, dg); var lwb = MpReal.Make(1, -frmt - 1, dg);
                while (y.CompareTo(lwb) < 0 && !y.IsZero) { y = MpReal.Make(y.M, y.E + 1, dg); q--; if (after > 0) after--; }
                while (y.CompareTo(upb) > 0) { y = MpReal.Make(y.M, y.E - 1, dg); q++; if (after > 0) after++; }
            }
            string t1 = LongFixed(x.Sign < 0 ? -y : y, Math.Sign(width) * (Math.Abs(width) - Math.Abs(expo) - 1), after);
            string t2 = WholeInt(q, expo);
            string s = t1 + "e" + t2;
            if (expo == 0 || s.Contains(ErrorChar))
                return LongReal(x, width, after != 0 ? after - 1 : 0, expo > 0 ? expo + 1 : expo - 1, frmt);
            return s;
        }
        return ErrorChars(width);
    }
}
