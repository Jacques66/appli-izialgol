using System.Globalization;
using System.Numerics;
using System.Text;

namespace Studio.Algol68.Runtime;

/// <summary>
/// Multiprecision decimal real (LONG REAL, LONG LONG REAL): value = M x 10^E, |M| < 10^Digits.
/// Transcendental functions are computed in fixed point with guard digits.
/// </summary>
public sealed class MpReal : IComparable<MpReal>
{
    public readonly BigInteger M;
    public readonly int E;
    public readonly int Digits;
    public const int MaxExponent = 999999;

    MpReal(BigInteger m, int e, int digits) { M = m; E = e; Digits = digits; }

    public static MpReal Zero(int digits) => new(BigInteger.Zero, 0, digits);
    public static MpReal One(int digits) => FromInt(1, digits);
    public bool IsZero => M.IsZero;
    public int Sign => M.Sign;

    static readonly Dictionary<int, BigInteger> pow10 = new();
    public static BigInteger Pow10(int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (n < 64)
        {
            lock (pow10)
            {
                if (pow10.TryGetValue(n, out var p)) return p;
                p = BigInteger.Pow(10, n); pow10[n] = p; return p;
            }
        }
        return BigInteger.Pow(10, n);
    }

    public static int DigitCount(BigInteger m)
    {
        if (m.IsZero) return 1;
        m = BigInteger.Abs(m);
        int d = (int)Math.Floor(BigInteger.Log10(m)) + 1;
        if (d < 1) d = 1;
        // correct the rounding errors of Log10
        while (d > 1 && m < Pow10(d - 1)) d--;
        while (m >= Pow10(d)) d++;
        return d;
    }

    static BigInteger DivRound(BigInteger a, BigInteger b)
    {
        var q = BigInteger.DivRem(a, b, out var r);
        if (BigInteger.Abs(r) * 2 >= BigInteger.Abs(b)) q += (a.Sign * b.Sign >= 0) ? 1 : -1;
        return q;
    }

    public static MpReal Make(BigInteger m, int e, int digits)
    {
        if (m.IsZero) return new MpReal(BigInteger.Zero, 0, digits);
        int dc = DigitCount(m);
        if (dc > digits)
        {
            int k = dc - digits;
            m = DivRound(m, Pow10(k));
            e += k;
            if (DigitCount(m) > digits) { m = DivRound(m, 10); e++; }
        }
        // remove trailing zeros (canonical form)
        while (!m.IsZero && m % 10 == 0) { m /= 10; e++; }
        if (e + DigitCount(m) > MaxExponent) throw new OverflowException("overflow (LONG REAL)");
        if (e + DigitCount(m) < -MaxExponent) return new MpReal(BigInteger.Zero, 0, digits);
        return new MpReal(m, e, digits);
    }

    public MpReal WithDigits(int digits) => digits == Digits ? this : Make(M, E, digits);

    public static MpReal FromInt(BigInteger i, int digits) => Make(i, 0, digits);
    public static MpReal FromBig(BigInteger i, int digits) => Make(i, 0, digits);

    public static MpReal FromDouble(double d, int digits)
    {
        if (d == 0) return Zero(digits);
        // exact conversion of the binary value
        long bits = BitConverter.DoubleToInt64Bits(d);
        bool neg = bits < 0;
        int exp = (int)((bits >> 52) & 0x7FF);
        long mant = bits & 0xFFFFFFFFFFFFFL;
        if (exp == 0) exp++; else mant |= 1L << 52;
        exp -= 1075;
        BigInteger m = mant;
        if (neg) m = -m;
        if (exp >= 0) return Make(m * BigInteger.Pow(2, exp), 0, digits);
        // m / 2^-exp = m * 5^-exp / 10^-exp
        return Make(m * BigInteger.Pow(5, -exp), exp, Math.Max(digits, 20)).WithDigits(digits);
    }

    public double ToDouble()
    {
        if (M.IsZero) return 0;
        return double.Parse(M.ToString(CultureInfo.InvariantCulture) + "E" + E.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    public static MpReal Parse(string text, int digits)
    {
        text = text.Trim();
        int ePos = text.IndexOfAny(new[] { 'e', 'E' });
        string mant = ePos >= 0 ? text[..ePos] : text;
        int exp = ePos >= 0 ? int.Parse(text[(ePos + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0;
        bool neg = mant.StartsWith('-');
        mant = mant.TrimStart('+', '-');
        int dot = mant.IndexOf('.');
        if (dot >= 0) { exp -= mant.Length - dot - 1; mant = mant.Remove(dot, 1); }
        if (mant.Length == 0) mant = "0";
        var m = BigInteger.Parse(mant, CultureInfo.InvariantCulture);
        return Make(neg ? -m : m, exp, digits);
    }

    public static MpReal operator -(MpReal a) => new(-a.M, a.E, a.Digits);
    public MpReal Abs() => M.Sign < 0 ? -this : this;

    public static MpReal Add(MpReal a, MpReal b)
    {
        int d = Math.Max(a.Digits, b.Digits);
        if (a.IsZero) return b.WithDigits(d);
        if (b.IsZero) return a.WithDigits(d);
        // avoid huge alignments when the magnitudes are very far apart
        int ma = a.E + DigitCount(a.M), mb = b.E + DigitCount(b.M);
        if (ma - mb > d + 2) return a.WithDigits(d);
        if (mb - ma > d + 2) return b.WithDigits(d);
        int e = Math.Min(a.E, b.E);
        var m = a.M * Pow10(a.E - e) + b.M * Pow10(b.E - e);
        return Make(m, e, d);
    }
    public static MpReal Sub(MpReal a, MpReal b) => Add(a, -b);
    public static MpReal Mul(MpReal a, MpReal b) => Make(a.M * b.M, a.E + b.E, Math.Max(a.Digits, b.Digits));
    public static MpReal Div(MpReal a, MpReal b)
    {
        if (b.IsZero) throw new DivideByZeroException();
        int d = Math.Max(a.Digits, b.Digits);
        int shift = d + 2 + DigitCount(b.M) - DigitCount(a.M);
        if (shift < 0) shift = 0;
        var q = DivRound(a.M * Pow10(shift), b.M);
        return Make(q, a.E - b.E - shift, d);
    }

    public int CompareTo(MpReal? o)
    {
        if (o is null) return 1;
        if (M.Sign != o.M.Sign) return M.Sign.CompareTo(o.M.Sign);
        if (M.IsZero) return 0;
        int e = Math.Min(E, o.E);
        int ma = E + DigitCount(M), mb = o.E + DigitCount(o.M);
        if (ma != mb) return M.Sign > 0 ? ma.CompareTo(mb) : mb.CompareTo(ma);
        return (M * Pow10(E - e)).CompareTo(o.M * Pow10(o.E - e));
    }

    /// <summary>Floor (ENTIER).</summary>
    public BigInteger Floor()
    {
        if (E >= 0) return M * Pow10(E);
        var q = BigInteger.DivRem(M, Pow10(-E), out var r);
        if (r.Sign < 0) q -= 1;
        return q;
    }
    /// <summary>Round to nearest (ROUND).</summary>
    public BigInteger Round()
    {
        if (E >= 0) return M * Pow10(E);
        var p = Pow10(-E);
        var q = BigInteger.DivRem(BigInteger.Abs(M), p, out var r);
        if (r * 2 >= p) q += 1;
        return M.Sign < 0 ? -q : q;
    }

    // ------------------------------------------------------------- fixed point

    /// <summary>Value * 10^p, rounded to an integer.</summary>
    BigInteger ToFixed(int p)
    {
        int s = E + p;
        if (s >= 0) return M * Pow10(s);
        return DivRound(M, Pow10(-s));
    }
    static MpReal FromFixed(BigInteger f, int p, int digits) => Make(f, -p, digits);

    static BigInteger ISqrt(BigInteger n)
    {
        if (n.Sign <= 0) return BigInteger.Zero;
        var x = (BigInteger)Math.Sqrt((double)n);
        if (x.IsZero) x = 1;
        // Newton
        while (true)
        {
            var y = (x + n / x) >> 1;
            if (BigInteger.Abs(y - x) <= 1)
            {
                while (y * y > n) y--;
                while ((y + 1) * (y + 1) <= n) y++;
                return y;
            }
            x = y;
        }
    }

    public static MpReal Sqrt(MpReal a)
    {
        if (a.M.Sign < 0) throw new ArithmeticException("square root of a negative number");
        if (a.IsZero) return a;
        int p = a.Digits + 6;
        // n = M * 10^t with (E - t) even and at least 2p digits
        int t = 2 * p - DigitCount(a.M);
        if (t < 0) t = 0;
        if (((a.E - t) & 1) != 0) t++;
        var r = ISqrt(a.M * Pow10(t));
        return Make(r, (a.E - t) / 2, a.Digits);
    }

    static readonly Dictionary<int, BigInteger> piCache = new();
    /// <summary>pi * 10^p (Machin).</summary>
    static BigInteger PiFixed(int p)
    {
        lock (piCache)
        {
            if (piCache.TryGetValue(p, out var v)) return v;
            int g = p + 10;
            var one = Pow10(g);
            BigInteger AtanInv(int x)
            {
                BigInteger sum = 0, term = one / x, x2 = (BigInteger)x * x;
                int n = 1; int sign = 1;
                while (!term.IsZero)
                {
                    sum += sign * (term / n);
                    term /= x2; n += 2; sign = -sign;
                }
                return sum;
            }
            var pi = 16 * AtanInv(5) - 4 * AtanInv(239);
            v = pi / Pow10(10);
            piCache[p] = v;
            return v;
        }
    }
    public static MpReal Pi(int digits) => FromFixed(PiFixed(digits + 4), digits + 4, digits);

    static readonly Dictionary<int, BigInteger> ln10Cache = new();

    /// <summary>exp in fixed point: x*10^p -> exp(x)*10^p, for reasonable |x|.</summary>
    static BigInteger ExpFixed(BigInteger x, int p)
    {
        // reduction: x / 2^k
        var one = Pow10(p);
        int k = 0;
        var xr = x;
        var lim = one / 1000;
        while (BigInteger.Abs(xr) > lim) { xr /= 2; k++; }
        BigInteger sum = one, term = one;
        for (int n = 1; n < 10000; n++)
        {
            term = term * xr / one / n;
            if (term.IsZero) break;
            sum += term;
        }
        for (int i = 0; i < k; i++) sum = sum * sum / one;
        return sum;
    }

    public static MpReal Exp(MpReal a)
    {
        int d = a.Digits;
        double approx = a.ToDouble();
        if (approx > 2302585.0) throw new OverflowException("overflow in exp");
        if (approx < -2302585.0) return Zero(d);
        // exp(x) = exp(r) * 10^q  with x = q ln10 + r
        int g = 12;
        int p = d + g + Math.Max(0, (int)Math.Abs(approx / 2.302585) .ToString().Length);
        var ln10 = Ln10Fixed(p);
        var xf = a.ToFixed(p);
        var q = BigInteger.Divide(xf, ln10);
        if (xf.Sign < 0 && q * ln10 != xf) q -= 1;
        var r = xf - q * ln10;
        var er = ExpFixed(r, p);
        return Make(er, (int)q - p, d);
    }

    static BigInteger Ln10Fixed(int p)
    {
        lock (ln10Cache)
        {
            if (ln10Cache.TryGetValue(p, out var v)) return v;
            // ln 10 = 3 ln 2 + ln(1.25) ; ln(x) = 2 atanh((x-1)/(x+1))
            int g = p + 10;
            var one = Pow10(g);
            BigInteger Atanh(BigInteger num, BigInteger den) // atanh(num/den)
            {
                BigInteger sum = 0, term = one * num / den, d2 = den * den, n2 = num * num;
                int n = 1;
                while (!term.IsZero)
                {
                    sum += term / n;
                    term = term * n2 / d2; n += 2;
                }
                return sum;
            }
            var ln2 = 2 * Atanh(1, 3);
            var ln125 = 2 * Atanh(1, 9);
            v = (3 * ln2 + ln125) / Pow10(10);
            ln10Cache[p] = v;
            return v;
        }
    }

    public static MpReal Ln(MpReal a)
    {
        if (a.M.Sign <= 0) throw new ArithmeticException("logarithm of a non-positive number");
        int d = a.Digits;
        int p = d + 12;
        // a = m * 10^k with m in [1,10)
        int k = a.E + DigitCount(a.M) - 1;
        var m = Make(a.M, a.E - k, d + 12);
        double y0 = Math.Log(m.ToDouble());
        // Newton: y = y + 2 (m - e^y)/(m + e^y)
        var mf = m.ToFixed(p);
        BigInteger y = new BigInteger(y0 * 1e15) * Pow10(p) / Pow10(15);
        var one = Pow10(p);
        for (int it = 0; it < 100; it++)
        {
            var ey = ExpFixed(y, p);
            var delta = 2 * (mf - ey) * one / (mf + ey);
            y += delta;
            if (BigInteger.Abs(delta) <= 2) break;
        }
        var res = y + k * Ln10Fixed(p);
        return Make(res, -p, d);
    }

    /// <summary>sin and cos in fixed point for |x| <= pi/4.</summary>
    static (BigInteger sin, BigInteger cos) SinCosFixed(BigInteger x, int p)
    {
        var one = Pow10(p);
        var x2 = x * x / one;
        BigInteger s = x, c = one, ts = x, tc = one;
        for (int n = 1; n < 10000; n++)
        {
            ts = -ts * x2 / one / ((2 * n) * (2 * n + 1));
            tc = -tc * x2 / one / ((2 * n - 1) * (2 * n));
            if (ts.IsZero && tc.IsZero) break;
            s += ts; c += tc;
        }
        return (s, c);
    }

    static (BigInteger sin, BigInteger cos) SinCosReduced(MpReal a, int p)
    {
        int extra = Math.Max(0, a.E + DigitCount(a.M));
        int pp = p + extra + 4;
        var pi = PiFixed(pp);
        var halfPi = pi / 2;
        var x = a.ToFixed(pp);
        // x = q * pi/2 + r, |r| <= pi/4
        var q = BigInteger.Divide(x + halfPi / 2 * x.Sign, halfPi);
        var r = x - q * halfPi;
        var (s, c) = SinCosFixed(r / Pow10(pp - p), p);
        int quad = (int)(((q % 4) + 4) % 4);
        return quad switch
        {
            0 => (s, c),
            1 => (c, -s),
            2 => (-s, -c),
            _ => (-c, s),
        };
    }

    public static MpReal Sin(MpReal a) { int p = a.Digits + 10; var (s, _) = SinCosReduced(a, p); return Make(s, -p, a.Digits); }
    public static MpReal Cos(MpReal a) { int p = a.Digits + 10; var (_, c) = SinCosReduced(a, p); return Make(c, -p, a.Digits); }
    public static MpReal Tan(MpReal a)
    {
        int p = a.Digits + 10; var (s, c) = SinCosReduced(a, p);
        if (c.IsZero) throw new ArithmeticException("infinite tangent");
        return Make(s * Pow10(p) / c, -p, a.Digits);
    }

    static BigInteger AtanFixed(BigInteger x, int p)
    {
        var one = Pow10(p);
        bool inv = false; int sign = x.Sign;
        x = BigInteger.Abs(x);
        if (x > one) { x = one * one / x; inv = true; }
        // half angles: atan(x) = 2 atan(x / (1 + sqrt(1 + x^2)))
        int halvings = 0;
        var lim = one / 10;
        while (x > lim)
        {
            var sq = ISqrt(one * one + x * x);
            x = x * one / (one + sq);
            halvings++;
        }
        var x2 = x * x / one;
        BigInteger sum = 0, term = x;
        for (int n = 1; !term.IsZero; n += 2)
        {
            sum += term / n * ((n / 2) % 2 == 0 ? 1 : -1);
            term = term * x2 / one;
        }
        sum <<= halvings;
        if (inv) sum = PiFixed(p) / 2 - sum;
        return sign < 0 ? -sum : sum;
    }

    public static MpReal Atan(MpReal a) { int p = a.Digits + 10; return Make(AtanFixed(a.ToFixed(p), p), -p, a.Digits); }

    public static MpReal Asin(MpReal a)
    {
        int p = a.Digits + 10;
        var one = Pow10(p);
        var x = a.ToFixed(p);
        if (BigInteger.Abs(x) > one) throw new ArithmeticException("arcsin outside the domain [-1, 1]");
        if (BigInteger.Abs(x) == one) return Make(x.Sign * PiFixed(p) / 2, -p, a.Digits);
        var c = ISqrt(one * one - x * x);
        return Make(AtanFixed(x * one / c, p), -p, a.Digits);
    }

    public static MpReal Acos(MpReal a)
    {
        int p = a.Digits + 10;
        var asin = Asin(a.WithDigits(a.Digits + 10));
        var halfPi = FromFixed(PiFixed(p) / 2, p, a.Digits + 10);
        return Sub(halfPi, asin).WithDigits(a.Digits);
    }

    public static MpReal Atan2(MpReal y, MpReal x)
    {
        int d = Math.Max(x.Digits, y.Digits);
        if (x.IsZero)
        {
            if (y.IsZero) throw new ArithmeticException("arctan2(0, 0) is undefined");
            var hp = Div(Pi(d), FromInt(2, d));
            return y.Sign > 0 ? hp : -hp;
        }
        var at = Atan(Div(y, x));
        if (x.Sign > 0) return at;
        return y.Sign >= 0 ? Add(at, Pi(d)) : Sub(at, Pi(d));
    }

    public static MpReal PowInt(MpReal a, long n)
    {
        int d = a.Digits;
        var res = One(d + 5);
        var b = a.WithDigits(d + 5);
        long k = Math.Abs(n);
        while (k > 0)
        {
            if ((k & 1) != 0) res = Mul(res, b);
            k >>= 1;
            if (k > 0) b = Mul(b, b);
        }
        if (n < 0) res = Div(One(d + 5), res);
        return res.WithDigits(d);
    }

    public static MpReal Pow(MpReal a, MpReal b)
    {
        if (a.IsZero) return b.Sign > 0 ? a : throw new ArithmeticException("0 ** x with x <= 0");
        if (a.Sign < 0) throw new ArithmeticException("real power of a negative number");
        return Exp(Mul(b.WithDigits(b.Digits + 5), Ln(a.WithDigits(a.Digits + 5)))).WithDigits(Math.Max(a.Digits, b.Digits));
    }

    // ------------------------------------------------------------- conversion to digits

    /// <summary>Exact decimal digits: (digit string, decimal exponent of the first digit).</summary>
    public (string digits, int exp10) DecimalDigits()
    {
        if (M.IsZero) return ("0", 0);
        string s = BigInteger.Abs(M).ToString(CultureInfo.InvariantCulture);
        return (s, E + s.Length - 1);
    }

    public override string ToString()
    {
        var (ds, e) = DecimalDigits();
        var sb = new StringBuilder();
        if (M.Sign < 0) sb.Append('-');
        sb.Append(ds[0]);
        if (ds.Length > 1) sb.Append('.').Append(ds, 1, ds.Length - 1);
        sb.Append('e').Append(e);
        return sb.ToString();
    }
}
