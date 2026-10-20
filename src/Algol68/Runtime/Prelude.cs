using System.Numerics;
using System.Text;
using Studio.Algol68.Exec;
using Studio.Algol68.Semantics;
using Range = Studio.Algol68.Semantics.Range;

namespace Studio.Algol68.Runtime;

/// <summary>Arithmetique reelle d'une longueur donnee (REAL : double ; LONG/LONG LONG REAL : MpReal).</summary>
public sealed class RA
{
    public readonly int Size; public readonly int Digits;
    public RA(int size, int digits) { Size = size; Digits = digits; }
    public bool IsDouble => Size == 0;
    public MpReal Mp(Value v) => ((MpReal)v.O!).WithDigits(Digits);
    public Value V(MpReal m) => Value.Obj(m.WithDigits(Digits));
    public Value FromDouble(double d) => IsDouble ? Value.R(d) : V(MpReal.FromDouble(d, Digits));
    public Value FromInt(Value i) => IsDouble ? Value.R(i.N) : V(MpReal.FromBig(Size == 0 ? i.N : (BigInteger)i.O!, Digits));
    public Value FromLong(long i) => IsDouble ? Value.R(i) : V(MpReal.FromInt(i, Digits));
    public Value Zero => IsDouble ? Value.R(0) : V(MpReal.Zero(Digits));
    public Value One => FromLong(1);
    public double D(Value v) => IsDouble ? v.Real : ((MpReal)v.O!).ToDouble();

    static Value Chk(double d, Node at) { if (double.IsNaN(d) || double.IsInfinity(d)) throw RT.Err("débordement réel (REAL)", at); return Value.R(d); }
    T Guard<T>(Func<T> f, Node at)
    {
        try { return f(); }
        catch (OverflowException) { throw RT.Err($"débordement réel ({(Size == 1 ? "LONG REAL" : "LONG LONG REAL")})", at); }
        catch (DivideByZeroException) { throw RT.Err("division par zéro", at); }
        catch (ArithmeticException e) { throw RT.Err(e.Message, at); }
    }

    public Value Add(Value a, Value b, Node at) => IsDouble ? Chk(a.Real + b.Real, at) : Guard(() => V(MpReal.Add(Mp(a), Mp(b))), at);
    public Value Sub(Value a, Value b, Node at) => IsDouble ? Chk(a.Real - b.Real, at) : Guard(() => V(MpReal.Sub(Mp(a), Mp(b))), at);
    public Value Mul(Value a, Value b, Node at) => IsDouble ? Chk(a.Real * b.Real, at) : Guard(() => V(MpReal.Mul(Mp(a), Mp(b))), at);
    public Value Div(Value a, Value b, Node at)
    {
        if (IsDouble) { if (b.Real == 0) throw RT.Err("division par zéro", at); return Chk(a.Real / b.Real, at); }
        return Guard(() => V(MpReal.Div(Mp(a), Mp(b))), at);
    }
    public Value Neg(Value a) => IsDouble ? Value.R(-a.Real) : V(-Mp(a));
    public Value Abs(Value a) => IsDouble ? Value.R(Math.Abs(a.Real)) : V(Mp(a).Abs());
    public int Compare(Value a, Value b) => IsDouble ? a.Real.CompareTo(b.Real) : Mp(a).CompareTo(Mp(b));
    public int Sign(Value a) => IsDouble ? Math.Sign(a.Real) : Mp(a).Sign;
    public bool IsZero(Value a) => IsDouble ? a.Real == 0 : Mp(a).IsZero;

    public Value PowInt(Value a, long n, Node at)
    {
        if (IsDouble)
        {
            double x = a.Real, r = 1; long k = Math.Abs(n);
            if (x == 0 && n < 0) throw RT.Err("division par zéro dans **", at);
            // RR : produit repete
            double b = x;
            while (k > 0) { if ((k & 1) != 0) r *= b; k >>= 1; if (k > 0) b *= b; }
            return Chk(n >= 0 ? r : 1 / r, at);
        }
        return Guard(() => V(MpReal.PowInt(Mp(a), n)), at);
    }
    public Value Pow(Value a, Value b, Node at)
    {
        if (IsDouble)
        {
            if (a.Real < 0) throw RT.Err("puissance réelle d'un nombre négatif", at);
            return Chk(Math.Pow(a.Real, b.Real), at);
        }
        return Guard(() => V(MpReal.Pow(Mp(a), Mp(b))), at);
    }

    public Value Fn(string name, Value a, Node at)
    {
        if (IsDouble)
        {
            double x = a.Real;
            double r = name switch
            {
                "sqrt" => x < 0 ? throw RT.Err("sqrt d'un nombre négatif", at) : Math.Sqrt(x),
                "exp" => Math.Exp(x),
                "ln" => x <= 0 ? throw RT.Err("ln d'un nombre non positif", at) : Math.Log(x),
                "log" => x <= 0 ? throw RT.Err("log d'un nombre non positif", at) : Math.Log10(x),
                "sin" => Math.Sin(x), "cos" => Math.Cos(x), "tan" => Math.Tan(x),
                "arcsin" => Math.Abs(x) > 1 ? throw RT.Err("arcsin hors du domaine [-1, 1]", at) : Math.Asin(x),
                "arccos" => Math.Abs(x) > 1 ? throw RT.Err("arccos hors du domaine [-1, 1]", at) : Math.Acos(x),
                "arctan" => Math.Atan(x),
                "sinh" => Math.Sinh(x), "cosh" => Math.Cosh(x), "tanh" => Math.Tanh(x),
                "arcsinh" => Math.Asinh(x), "arccosh" => Math.Acosh(x), "arctanh" => Math.Atanh(x),
                "cbrt" => Math.Cbrt(x),
                _ => throw new InvalidOperationException(name),
            };
            return Chk(r, at);
        }
        return Guard(() =>
        {
            var m = Mp(a);
            MpReal r = name switch
            {
                "sqrt" => MpReal.Sqrt(m), "exp" => MpReal.Exp(m), "ln" => MpReal.Ln(m),
                "log" => MpReal.Div(MpReal.Ln(m), MpReal.Ln(MpReal.FromInt(10, Digits))),
                "sin" => MpReal.Sin(m), "cos" => MpReal.Cos(m), "tan" => MpReal.Tan(m),
                "arcsin" => MpReal.Asin(m), "arccos" => MpReal.Acos(m), "arctan" => MpReal.Atan(m),
                "sinh" => Hyp(m, 0), "cosh" => Hyp(m, 1), "tanh" => Hyp(m, 2),
                "arcsinh" => MpReal.Ln(MpReal.Add(m, MpReal.Sqrt(MpReal.Add(MpReal.Mul(m, m), MpReal.One(Digits))))),
                "arccosh" => MpReal.Ln(MpReal.Add(m, MpReal.Sqrt(MpReal.Sub(MpReal.Mul(m, m), MpReal.One(Digits))))),
                "arctanh" => MpReal.Div(MpReal.Ln(MpReal.Div(MpReal.Add(MpReal.One(Digits), m), MpReal.Sub(MpReal.One(Digits), m))), MpReal.FromInt(2, Digits)),
                "cbrt" => m.IsZero ? m : (m.Sign > 0 ? MpReal.Exp(MpReal.Div(MpReal.Ln(m), MpReal.FromInt(3, Digits))) : -MpReal.Exp(MpReal.Div(MpReal.Ln(-m), MpReal.FromInt(3, Digits)))),
                _ => throw new InvalidOperationException(name),
            };
            return V(r);
        }, at);
    }

    MpReal Hyp(MpReal m, int kind)
    {
        var e = MpReal.Exp(m); var ei = MpReal.Div(MpReal.One(Digits), e);
        var two = MpReal.FromInt(2, Digits);
        return kind switch
        {
            0 => MpReal.Div(MpReal.Sub(e, ei), two),
            1 => MpReal.Div(MpReal.Add(e, ei), two),
            _ => MpReal.Div(MpReal.Sub(e, ei), MpReal.Add(e, ei)),
        };
    }

    public Value Atan2(Value y, Value x, Node at)
    {
        if (IsDouble)
        {
            if (x.Real == 0 && y.Real == 0) throw RT.Err("arctan2(0, 0) indéfini", at);
            return Value.R(Math.Atan2(y.Real, x.Real));
        }
        return Guard(() => V(MpReal.Atan2(Mp(y), Mp(x))), at);
    }

    public Value Pi => IsDouble ? Value.R(Math.PI) : V(MpReal.Pi(Digits));

    public Value Entier(Value a, Node at, int intSize, Compiler? c)
    {
        if (IsDouble)
        {
            double f = Math.Floor(a.Real);
            if (f > int.MaxValue || f < int.MinValue) throw RT.Err("ENTIER : résultat hors de l'intervalle des INT", at);
            return Value.I((long)f);
        }
        return Prelude.BigV(Mp(a).Floor(), intSize, at);
    }
    public Value Round(Value a, Node at, int intSize)
    {
        if (IsDouble)
        {
            double x = a.Real;
            double r = x >= 0 ? Math.Floor(x + 0.5) : -Math.Floor(-x + 0.5);
            if (r > int.MaxValue || r < int.MinValue) throw RT.Err("ROUND : résultat hors de l'intervalle des INT", at);
            return Value.I((long)r);
        }
        return Prelude.BigV(Mp(a).Round(), intSize, at);
    }
}

/// <summary>Prelude standard (RR 10.2) : operateurs, constantes, fonctions.</summary>
public static partial class Prelude
{
    public const int LongDigits = 35;
    public const int DefaultLongLongDigits = 70;
    public const int BytesWidth = 32;
    public const int LongBytesWidth = 256;
    public const int IntWidth = 10;
    public const int RealWidth = 15;
    public const int ExpWidth = 3;
    public const int BitsWidth = 32;
    public const int LongBitsWidth = 116;

    static int llDigits = DefaultLongLongDigits;
    /// <summary>Precision courante de LONG LONG (fixee a la compilation par PR precision PR).</summary>
    public static int LLDigits => llDigits;
    public static void SetLongLong(int digits) { llDigits = digits; }

    public static int RealDigits(int size, Compiler? c = null) => size == 0 ? 17 : size == 1 ? LongDigits : (c?.LongLongDigits ?? llDigits);
    public static int RealWidthOf(int size) => size == 0 ? RealWidth : size == 1 ? (LongDigits - 7) : (llDigits - 7);
    public static int LongIntWidthOf(int size) => size == 0 ? IntWidth : size == 1 ? LongDigits + 1 : llDigits + 1;
    public static int BitsWidthOf(int size, Compiler? c = null) => size == 0 ? BitsWidth : size == 1 ? LongBitsWidth : (int)Math.Ceiling((c?.LongLongDigits ?? llDigits) * Math.Log2(10)) - 1;
    public static BigInteger MaxLongInt(int size, Compiler? c = null) => MpReal.Pow10(size == 1 ? LongDigits : (c?.LongLongDigits ?? llDigits)) - 1;

    public static Value BigV(BigInteger b, int size, Node at)
    {
        if (size == 0)
        {
            if (b > int.MaxValue || b < int.MinValue) throw RT.Err("débordement d'entier (INT)", at);
            return Value.I((long)b);
        }
        if (BigInteger.Abs(b) > MaxLongInt(size)) throw RT.Err($"débordement d'entier ({(size == 1 ? "LONG INT" : "LONG LONG INT")})", at);
        return Value.Obj(b);
    }
    public static BigInteger Big(Value v, int size) => size == 0 ? v.N : (BigInteger)v.O!;

    public static RowV BitsToRowBool(Value v, int size)
    {
        int w = BitsWidthOf(size);
        var data = new Value[w];
        BigInteger b = size == 0 ? (BigInteger)(ulong)(uint)v.N : (BigInteger)v.O!;
        for (int i = 0; i < w; i++) data[i].N = ((b >> (w - 1 - i)) & 1) == 1 ? 1 : 0;
        return RowV.Make1(data);
    }

    // ------------------------------------------------------------------ enregistrement

    sealed class Ctx
    {
        public Compiler C = null!; public Range R = null!; public ModeTable M = null!;
    }
    static Ctx X = null!;

    static void Op(string names, Mode result, Mode[] ps, Func<Node[], Node> factory)
    {
        var pm = (ProcMode)X.M.Intern(new ProcMode(ps, result));
        foreach (var name in names.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!X.R.Ops.TryGetValue(name, out var list)) X.R.Ops[name] = list = new List<OpSym>();
            list.Add(new OpSym { Name = name, Range = X.R, Mode = pm, Factory = factory });
        }
    }
    static void Op2(string names, Mode result, Mode a, Mode b, Func<Value, Value, Node, Value> fn) =>
        Op(names, result, new[] { a, b }, n => new Exec.Op2(n[0], n[1], fn));
    static void Op1(string names, Mode result, Mode a, Func<Value, Node, Value> fn) =>
        Op(names, result, new[] { a }, n => new Exec.Op1(n[0], fn));

    public static void Const(string name, Mode m, Value v) =>
        X.R.Ids[name] = new IdSym { Name = name, Range = X.R, Mode = m, Const = v, Declared = true };
    public static void CtxConst(string name, Mode m, Func<ExecContext, Value> get) =>
        X.R.Ids[name] = new IdSym { Name = name, Range = X.R, Mode = m, CtxValue = get, Declared = true };
    public static void Proc(string names, Mode result, Mode[] ps, BuiltinFn fn)
    {
        var pm = X.M.Intern(new ProcMode(ps, result));
        foreach (var name in names.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            X.R.Ids[name] = new IdSym { Name = name, Range = X.R, Mode = pm, Builtin = fn, Declared = true };
    }

    static string LongPrefix(int s) => s == 0 ? "" : s == 1 ? "long" : "longlong";

    public static void Build(Compiler c, Range r)
    {
        X = new Ctx { C = c, R = r, M = c.M };
        llDigits = c.LongLongDigits;
        var M = c.M;
        BuildIntOps(M);
        BuildRealOps(M);
        BuildComplOps(M);
        BuildBoolCharStringOps(M);
        BuildBitsBytesOps(M);
        BuildRowsSemaOps(M);
        BuildConstants(M);
        BuildMath(M);
        BuildTransput(c, M);
    }

    // ------------------------------------------------------------------ entiers

    static void BuildIntOps(ModeTable M)
    {
        var I = M.Int; var B = M.Bool; var R = M.Real;
        Op("+", I, new[] { I, I }, a => new IntAdd(a[0], a[1]));
        Op("-", I, new[] { I, I }, a => new IntSub(a[0], a[1]));
        Op("*", I, new[] { I, I }, a => new IntMul(a[0], a[1]));
        Op("% OVER", I, new[] { I, I }, a => new IntOver(a[0], a[1]));
        Op("%* MOD", I, new[] { I, I }, a => new IntMod(a[0], a[1]));
        Op("/", R, new[] { I, I }, a => new RealDiv(new WidenIntRealNode(a[0]), new WidenIntRealNode(a[1])));
        Op("** ^ UP", I, new[] { I, I }, a => new IntPow(a[0], a[1]));
        foreach (var (n, cmp) in CmpNames()) Op(n, B, new[] { I, I }, a => new IntCmp(a[0], a[1], cmp));
        Op("-", I, new[] { I }, a => new IntNeg(a[0]));
        Op("+", I, new[] { I }, a => a[0]);
        Op1("ABS", I, I, (v, at) => { long x = Math.Abs(v.N); if (x > int.MaxValue) throw RT.Err("débordement d'entier (INT)", at); return Value.I(x); });
        Op1("SIGN", I, I, (v, at) => Value.I(Math.Sign(v.N)));
        Op1("ODD", B, I, (v, at) => Value.B((v.N & 1) != 0));
        Op1("REPR", M.Char, I, (v, at) =>
        {
            if (v.N < 0 || v.N > char.MaxValue) throw RT.Err($"REPR {v.N} : pas de caractère correspondant", at);
            return Value.I(v.N);
        });
        Op("+* I", M.Compl, new[] { I, I }, a => new Exec.Op2(a[0], a[1], (x, y, at) => Value.Obj(new[] { Value.R(x.N), Value.R(y.N) })));
        Op1("LENG", M.LongInt, I, (v, at) => Value.Obj((BigInteger)v.N));
        Op1("SHORTEN", I, M.LongInt, (v, at) => BigV((BigInteger)v.O!, 0, at));
        // LONG INT, LONG LONG INT
        for (int s0 = 1; s0 <= 2; s0++)
        {
            int s = s0;
            int size = s;
            var L = M.IntOf(s); var LR = M.RealOf(s);
            BigInteger G(Value v) => (BigInteger)v.O!;
            Op2("+", L, L, L, (x, y, at) => BigV(G(x) + G(y), size, at));
            Op2("-", L, L, L, (x, y, at) => BigV(G(x) - G(y), size, at));
            Op2("*", L, L, L, (x, y, at) => BigV(G(x) * G(y), size, at));
            Op2("% OVER", L, L, L, (x, y, at) => { if (G(y).IsZero) throw RT.Err("division entière par zéro", at); return BigV(BigInteger.Divide(G(x), G(y)), size, at); });
            Op2("%* MOD", L, L, L, (x, y, at) =>
            {
                if (G(y).IsZero) throw RT.Err("MOD par zéro", at);
                var rr = BigInteger.Remainder(G(x), G(y)); if (rr.Sign < 0) rr += BigInteger.Abs(G(y));
                return Value.Obj(rr);
            });
            var ra = new RA(s, RealDigits(s));
            Op2("/", LR, L, L, (x, y, at) => ra.Div(ra.FromInt(x), ra.FromInt(y), at));
            Op2("** ^ UP", L, L, I, (x, y, at) =>
            {
                if (y.N < 0) throw RT.Err("exposant négatif dans LONG INT ** INT", at);
                var bx = G(x); var lim = MaxLongInt(size);
                BigInteger res = 1;
                for (long k = 0; k < y.N; k++) { res *= bx; if (BigInteger.Abs(res) > lim) throw RT.Err("débordement d'entier (LONG INT)", at); }
                return Value.Obj(res);
            });
            foreach (var (n, cmp) in CmpNames()) { var cc = cmp; Op2(n, B, L, L, (x, y, at) => Value.B(CmpRes(G(x).CompareTo(G(y)), cc))); }
            Op1("-", L, L, (v, at) => Value.Obj(-G(v)));
            Op1("+", L, L, (v, at) => v);
            Op1("ABS", L, L, (v, at) => Value.Obj(BigInteger.Abs(G(v))));
            Op1("SIGN", I, L, (v, at) => Value.I(G(v).Sign));
            Op1("ODD", B, L, (v, at) => Value.B(!G(v).IsEven));
            var LC = s == 1 ? M.LongCompl : M.LongLongCompl;
            Op2("+* I", LC, L, L, (x, y, at) => Value.Obj(new[] { ra.FromInt(x), ra.FromInt(y) }));
            if (s == 1)
            {
                Op1("LENG", M.LongLongInt, L, (v, at) => v);
                Op1("SHORTEN", L, M.LongLongInt, (v, at) => BigV(G(v), 1, at));
            }
        }
    }

    static IEnumerable<(string, Cmp)> CmpNames()
    {
        yield return ("< LT", Cmp.Lt); yield return ("<= LE", Cmp.Le); yield return ("= EQ", Cmp.Eq);
        yield return ("/= ~= ^= NE", Cmp.Ne); yield return (">= GE", Cmp.Ge); yield return ("> GT", Cmp.Gt);
    }
    public static bool CmpRes(int c, Cmp op) => op switch
    {
        Cmp.Lt => c < 0, Cmp.Le => c <= 0, Cmp.Eq => c == 0, Cmp.Ne => c != 0, Cmp.Ge => c >= 0, _ => c > 0
    };

    // ------------------------------------------------------------------ reels

    static void BuildRealOps(ModeTable M)
    {
        var I = M.Int; var B = M.Bool; var R = M.Real;
        Node W(Node n) => new WidenIntRealNode(n);
        // REAL
        Op("+", R, new[] { R, R }, a => new RealAdd(a[0], a[1]));
        Op("-", R, new[] { R, R }, a => new RealSub(a[0], a[1]));
        Op("*", R, new[] { R, R }, a => new RealMul(a[0], a[1]));
        Op("/", R, new[] { R, R }, a => new RealDiv(a[0], a[1]));
        Op("+", R, new[] { I, R }, a => new RealAdd(W(a[0]), a[1]));
        Op("-", R, new[] { I, R }, a => new RealSub(W(a[0]), a[1]));
        Op("*", R, new[] { I, R }, a => new RealMul(W(a[0]), a[1]));
        Op("/", R, new[] { I, R }, a => new RealDiv(W(a[0]), a[1]));
        Op("+", R, new[] { R, I }, a => new RealAdd(a[0], W(a[1])));
        Op("-", R, new[] { R, I }, a => new RealSub(a[0], W(a[1])));
        Op("*", R, new[] { R, I }, a => new RealMul(a[0], W(a[1])));
        Op("/", R, new[] { R, I }, a => new RealDiv(a[0], W(a[1])));
        foreach (var (n, cmp) in CmpNames())
        {
            var cc = cmp;
            Op(n, B, new[] { R, R }, a => new RealCmp(a[0], a[1], cc));
            Op(n, B, new[] { I, R }, a => new RealCmp(W(a[0]), a[1], cc));
            Op(n, B, new[] { R, I }, a => new RealCmp(a[0], W(a[1]), cc));
        }
        Op("-", R, new[] { R }, a => new RealNeg(a[0]));
        Op("+", R, new[] { R }, a => a[0]);
        var r0 = new RA(0, 17);
        Op1("ABS", R, R, (v, at) => Value.R(Math.Abs(v.Real)));
        Op1("SIGN", I, R, (v, at) => Value.I(Math.Sign(v.Real)));
        Op1("ROUND", I, R, (v, at) => r0.Round(v, at, 0));
        Op1("ENTIER", I, R, (v, at) => r0.Entier(v, at, 0, null));
        Op2("** ^ UP", R, R, I, (x, y, at) => r0.PowInt(x, y.N, at));
        Op2("** ^ UP", R, R, R, (x, y, at) => r0.Pow(x, y, at));
        Op2("** ^ UP", R, I, R, (x, y, at) => r0.Pow(Value.R(x.N), y, at));
        Op2("+* I", M.Compl, R, R, (x, y, at) => Value.Obj(new[] { x, y }));
        Op2("+* I", M.Compl, I, R, (x, y, at) => Value.Obj(new[] { Value.R(x.N), y }));
        Op2("+* I", M.Compl, R, I, (x, y, at) => Value.Obj(new[] { x, Value.R(y.N) }));
        var ra1 = new RA(1, LongDigits);
        Op1("LENG", M.LongReal, R, (v, at) => ra1.FromDouble(v.Real));
        Op1("SHORTEN", R, M.LongReal, (v, at) =>
        {
            double d = ((MpReal)v.O!).ToDouble();
            if (double.IsInfinity(d)) throw RT.Err("SHORTEN : valeur hors de l'intervalle des REAL", at);
            return Value.R(d);
        });
        // LONG REAL, LONG LONG REAL
        for (int s0 = 1; s0 <= 2; s0++)
        {
            int s = s0;
            int size = s;
            var L = M.RealOf(s); var LI = M.IntOf(s);
            var ra = new RA(s, RealDigits(s));
            var LC = s == 1 ? M.LongCompl : M.LongLongCompl;
            Op2("+", L, L, L, ra.Add); Op2("-", L, L, L, ra.Sub); Op2("*", L, L, L, ra.Mul); Op2("/", L, L, L, ra.Div);
            Op2("+", L, LI, L, (x, y, at) => ra.Add(ra.FromInt(x), y, at));
            Op2("-", L, LI, L, (x, y, at) => ra.Sub(ra.FromInt(x), y, at));
            Op2("*", L, LI, L, (x, y, at) => ra.Mul(ra.FromInt(x), y, at));
            Op2("/", L, LI, L, (x, y, at) => ra.Div(ra.FromInt(x), y, at));
            Op2("+", L, L, LI, (x, y, at) => ra.Add(x, ra.FromInt(y), at));
            Op2("-", L, L, LI, (x, y, at) => ra.Sub(x, ra.FromInt(y), at));
            Op2("*", L, L, LI, (x, y, at) => ra.Mul(x, ra.FromInt(y), at));
            Op2("/", L, L, LI, (x, y, at) => ra.Div(x, ra.FromInt(y), at));
            foreach (var (n, cmp) in CmpNames())
            {
                var cc = cmp;
                Op2(n, B, L, L, (x, y, at) => Value.B(CmpRes(ra.Compare(x, y), cc)));
                Op2(n, B, LI, L, (x, y, at) => Value.B(CmpRes(ra.Compare(ra.FromInt(x), y), cc)));
                Op2(n, B, L, LI, (x, y, at) => Value.B(CmpRes(ra.Compare(x, ra.FromInt(y)), cc)));
            }
            Op1("-", L, L, (v, at) => ra.Neg(v));
            Op1("+", L, L, (v, at) => v);
            Op1("ABS", L, L, (v, at) => ra.Abs(v));
            Op1("SIGN", I, L, (v, at) => Value.I(ra.Sign(v)));
            Op1("ROUND", LI, L, (v, at) => ra.Round(v, at, size));
            Op1("ENTIER", LI, L, (v, at) => ra.Entier(v, at, size, null));
            Op2("** ^ UP", L, L, I, (x, y, at) => ra.PowInt(x, y.N, at));
            Op2("** ^ UP", L, L, L, (x, y, at) => ra.Pow(x, y, at));
            Op2("+* I", LC, L, L, (x, y, at) => Value.Obj(new[] { x, y }));
            Op2("+* I", LC, LI, L, (x, y, at) => Value.Obj(new[] { ra.FromInt(x), y }));
            Op2("+* I", LC, L, LI, (x, y, at) => Value.Obj(new[] { x, ra.FromInt(y) }));
            if (s == 1)
            {
                var ra2 = new RA(2, RealDigits(2));
                Op1("LENG", M.LongLongReal, L, (v, at) => ra2.V((MpReal)v.O!));
                Op1("SHORTEN", L, M.LongLongReal, (v, at) => ra.V((MpReal)v.O!));
            }
        }
    }

    // ------------------------------------------------------------------ complexes

    static Value[] Cx(Value v, Node at) => v.O as Value[] ?? throw RT.Err("complexe non initialisé", at);

    static void BuildComplOps(ModeTable M)
    {
        var I = M.Int; var B = M.Bool;
        for (int s0 = 0; s0 <= 2; s0++)
        {
            int s = s0;
            int size = s;
            var C = s == 0 ? M.Compl : s == 1 ? M.LongCompl : M.LongLongCompl;
            var R = M.RealOf(s); var LI = M.IntOf(s);
            var ra = new RA(s, RealDigits(s));
            Value Mk(Value re, Value im) => Value.Obj(new[] { re, im });
            Value FromR(Value r) => Mk(r, ra.Zero);
            Value FromI(Value i) => Mk(ra.FromInt(i), ra.Zero);
            Value Add(Value a, Value b, Node at) { var x = Cx(a, at); var y = Cx(b, at); return Mk(ra.Add(x[0], y[0], at), ra.Add(x[1], y[1], at)); }
            Value Sub(Value a, Value b, Node at) { var x = Cx(a, at); var y = Cx(b, at); return Mk(ra.Sub(x[0], y[0], at), ra.Sub(x[1], y[1], at)); }
            Value Mul(Value a, Value b, Node at)
            {
                var x = Cx(a, at); var y = Cx(b, at);
                return Mk(ra.Sub(ra.Mul(x[0], y[0], at), ra.Mul(x[1], y[1], at), at), ra.Add(ra.Mul(x[0], y[1], at), ra.Mul(x[1], y[0], at), at));
            }
            Value Div(Value a, Value b, Node at)
            {
                var x = Cx(a, at); var y = Cx(b, at);
                var d = ra.Add(ra.Mul(y[0], y[0], at), ra.Mul(y[1], y[1], at), at);
                if (ra.IsZero(d)) throw RT.Err("division complexe par zéro", at);
                var nre = ra.Add(ra.Mul(x[0], y[0], at), ra.Mul(x[1], y[1], at), at);
                var nim = ra.Sub(ra.Mul(x[1], y[0], at), ra.Mul(x[0], y[1], at), at);
                return Mk(ra.Div(nre, d, at), ra.Div(nim, d, at));
            }
            bool Eq(Value a, Value b, Node at) { var x = Cx(a, at); var y = Cx(b, at); return ra.Compare(x[0], y[0]) == 0 && ra.Compare(x[1], y[1]) == 0; }
            void Arith(string n, Func<Value, Value, Node, Value> fn)
            {
                Op2(n, C, C, C, fn);
                Op2(n, C, C, R, (a, b, at) => fn(a, FromR(b), at));
                Op2(n, C, R, C, (a, b, at) => fn(FromR(a), b, at));
                Op2(n, C, C, LI, (a, b, at) => fn(a, FromI(b), at));
                Op2(n, C, LI, C, (a, b, at) => fn(FromI(a), b, at));
            }
            Arith("+", Add); Arith("-", Sub); Arith("*", Mul); Arith("/", Div);
            foreach (var (n, neg) in new[] { ("= EQ", false), ("/= ~= ^= NE", true) })
            {
                bool ng = neg;
                Op2(n, B, C, C, (a, b, at) => Value.B(Eq(a, b, at) != ng));
                Op2(n, B, C, R, (a, b, at) => Value.B(Eq(a, FromR(b), at) != ng));
                Op2(n, B, R, C, (a, b, at) => Value.B(Eq(FromR(a), b, at) != ng));
                Op2(n, B, C, LI, (a, b, at) => Value.B(Eq(a, FromI(b), at) != ng));
                Op2(n, B, LI, C, (a, b, at) => Value.B(Eq(FromI(a), b, at) != ng));
            }
            Op1("-", C, C, (v, at) => { var x = Cx(v, at); return Mk(ra.Neg(x[0]), ra.Neg(x[1])); });
            Op1("+", C, C, (v, at) => v);
            Op1("RE", R, C, (v, at) => Cx(v, at)[0]);
            Op1("IM", R, C, (v, at) => Cx(v, at)[1]);
            Op1("CONJ", C, C, (v, at) => { var x = Cx(v, at); return Mk(x[0], ra.Neg(x[1])); });
            Op1("ABS", R, C, (v, at) =>
            {
                var x = Cx(v, at);
                if (ra.IsDouble) return Value.R(Math.Sqrt(x[0].Real * x[0].Real + x[1].Real * x[1].Real));
                return ra.Fn("sqrt", ra.Add(ra.Mul(x[0], x[0], at), ra.Mul(x[1], x[1], at), at), at);
            });
            Op1("ARG", R, C, (v, at) => { var x = Cx(v, at); return ra.Atan2(x[1], x[0], at); });
            Op2("** ^ UP", C, C, I, (a, b, at) =>
            {
                Value p = Mk(ra.One, ra.Zero);
                long n = Math.Abs(b.N);
                for (long k = 0; k < n; k++) p = Mul(p, a, at);
                return b.N >= 0 ? p : Div(Mk(ra.One, ra.Zero), p, at);
            });
            if (s < 2)
            {
                var C2 = s == 0 ? M.LongCompl : M.LongLongCompl;
                var ra2 = new RA(s + 1, RealDigits(s + 1));
                Op1("LENG", C2, C, (v, at) =>
                {
                    var x = Cx(v, at);
                    return s == 0 ? Mk(ra2.FromDouble(x[0].Real), ra2.FromDouble(x[1].Real)) : Mk(ra2.V((MpReal)x[0].O!), ra2.V((MpReal)x[1].O!));
                });
                Op1("SHORTEN", C, C2, (v, at) =>
                {
                    var x = Cx(v, at);
                    return s == 0 ? Mk(Value.R(((MpReal)x[0].O!).ToDouble()), Value.R(((MpReal)x[1].O!).ToDouble())) : Mk(ra.V((MpReal)x[0].O!), ra.V((MpReal)x[1].O!));
                });
            }
            // affectations combinees
            var RC = M.Ref(C);
            foreach (var (n, fn) in new (string, Func<Value, Value, Node, Value>)[] { ("+:= PLUSAB", Add), ("-:= MINUSAB", Sub), ("*:= TIMESAB", Mul), ("/:= DIVAB", Div) })
            {
                var f2 = fn;
                Op(n, RC, new[] { RC, C }, a => new OpAb(a[0], a[1], f2));
                Op(n, RC, new[] { RC, R }, a => new OpAb(a[0], a[1], (x, y, at) => f2(x, FromR(y), at)));
                Op(n, RC, new[] { RC, LI }, a => new OpAb(a[0], a[1], (x, y, at) => f2(x, FromI(y), at)));
            }
        }
        // affectations combinees INT et REAL
        var RI = M.RefInt; var RR = M.RefReal; var Ii = M.Int; var Rr = M.Real;
        Op("+:= PLUSAB", RI, new[] { RI, Ii }, a => new IntPlusAb(a[0], a[1], 0));
        Op("-:= MINUSAB", RI, new[] { RI, Ii }, a => new IntPlusAb(a[0], a[1], 1));
        Op("*:= TIMESAB", RI, new[] { RI, Ii }, a => new IntPlusAb(a[0], a[1], 2));
        Op("%:= OVERAB", RI, new[] { RI, Ii }, a => new IntPlusAb(a[0], a[1], 3));
        Op("%*:= MODAB", RI, new[] { RI, Ii }, a => new IntPlusAb(a[0], a[1], 4));
        Op("+:= PLUSAB", RR, new[] { RR, Rr }, a => new RealPlusAb(a[0], a[1], 0));
        Op("-:= MINUSAB", RR, new[] { RR, Rr }, a => new RealPlusAb(a[0], a[1], 1));
        Op("*:= TIMESAB", RR, new[] { RR, Rr }, a => new RealPlusAb(a[0], a[1], 2));
        Op("/:= DIVAB", RR, new[] { RR, Rr }, a => new RealPlusAb(a[0], a[1], 3));
        Op("+:= PLUSAB", RR, new[] { RR, Ii }, a => new RealPlusAb(a[0], new WidenIntRealNode(a[1]), 0));
        Op("-:= MINUSAB", RR, new[] { RR, Ii }, a => new RealPlusAb(a[0], new WidenIntRealNode(a[1]), 1));
        Op("*:= TIMESAB", RR, new[] { RR, Ii }, a => new RealPlusAb(a[0], new WidenIntRealNode(a[1]), 2));
        Op("/:= DIVAB", RR, new[] { RR, Ii }, a => new RealPlusAb(a[0], new WidenIntRealNode(a[1]), 3));
        for (int s0 = 1; s0 <= 2; s0++)
        {
            int s = s0;
            int size = s;
            var L = M.IntOf(s); var RL = M.Ref(L);
            BigInteger G(Value v) => (BigInteger)v.O!;
            Op("+:= PLUSAB", RL, new[] { RL, L }, a => new OpAb(a[0], a[1], (x, y, at) => BigV(G(x) + G(y), size, at)));
            Op("-:= MINUSAB", RL, new[] { RL, L }, a => new OpAb(a[0], a[1], (x, y, at) => BigV(G(x) - G(y), size, at)));
            Op("*:= TIMESAB", RL, new[] { RL, L }, a => new OpAb(a[0], a[1], (x, y, at) => BigV(G(x) * G(y), size, at)));
            Op("%:= OVERAB", RL, new[] { RL, L }, a => new OpAb(a[0], a[1], (x, y, at) => { if (G(y).IsZero) throw RT.Err("division entière par zéro", at); return BigV(G(x) / G(y), size, at); }));
            Op("%*:= MODAB", RL, new[] { RL, L }, a => new OpAb(a[0], a[1], (x, y, at) =>
            {
                if (G(y).IsZero) throw RT.Err("MOD par zéro", at);
                var rr = BigInteger.Remainder(G(x), G(y)); if (rr.Sign < 0) rr += BigInteger.Abs(G(y)); return Value.Obj(rr);
            }));
            var LRm = M.RealOf(s); var RLR = M.Ref(LRm);
            var ra = new RA(s, RealDigits(s));
            foreach (var (n, fn) in new (string, Func<Value, Value, Node, Value>)[] { ("+:= PLUSAB", ra.Add), ("-:= MINUSAB", ra.Sub), ("*:= TIMESAB", ra.Mul), ("/:= DIVAB", ra.Div) })
            {
                var f2 = fn;
                Op(n, RLR, new[] { RLR, LRm }, a => new OpAb(a[0], a[1], f2));
                Op(n, RLR, new[] { RLR, L }, a => new OpAb(a[0], a[1], (x, y, at) => f2(x, ra.FromInt(y), at)));
            }
        }
    }

    // ------------------------------------------------------------------ booleens, caracteres, chaines

    static string S(Value v, Node at) => v.O is RowV r ? r.AsString() : RT.Str(v, at);
    static string C(Value v) => ((char)v.N).ToString();

    public static int StringCompare(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) { int c = a[i] - b[i]; if (c != 0) return c; }
        return a.Length.CompareTo(b.Length);
    }

    static void BuildBoolCharStringOps(ModeTable M)
    {
        var B = M.Bool; var I = M.Int; var Ch = M.Char; var St = M.RowChar;
        Op("AND &", B, new[] { B, B }, a => new BoolAnd(a[0], a[1]));
        Op("OR", B, new[] { B, B }, a => new BoolOr(a[0], a[1]));
        Op2("XOR", B, B, B, (x, y, at) => Value.B(x.Bool != y.Bool));
        Op("NOT ~", B, new[] { B }, a => new BoolNot(a[0]));
        Op2("= EQ", B, B, B, (x, y, at) => Value.B(x.Bool == y.Bool));
        Op2("/= ~= ^= NE", B, B, B, (x, y, at) => Value.B(x.Bool != y.Bool));
        Op1("ABS", I, B, (v, at) => Value.I(v.Bool ? 1 : 0));
        // caracteres
        foreach (var (n, cmp) in CmpNames()) { var cc = cmp; Op(n, B, new[] { Ch, Ch }, a => new IntCmp(a[0], a[1], cc)); }
        Op1("ABS", I, Ch, (v, at) => Value.I(v.N));
        Op2("+", St, Ch, Ch, (x, y, at) => RT.StrValue(C(x) + C(y)));
        Op2("*", St, Ch, I, (x, y, at) => RT.StrValue(Repeat(C(x), y.N, at)));
        Op2("*", St, I, Ch, (x, y, at) => RT.StrValue(Repeat(C(y), x.N, at)));
        // chaines
        foreach (var (n, cmp) in CmpNames())
        {
            var cc = cmp;
            Op2(n, B, St, St, (x, y, at) => Value.B(CmpRes(StringCompare(S(x, at), S(y, at)), cc)));
            Op2(n, B, St, Ch, (x, y, at) => Value.B(CmpRes(StringCompare(S(x, at), C(y)), cc)));
            Op2(n, B, Ch, St, (x, y, at) => Value.B(CmpRes(StringCompare(C(x), S(y, at)), cc)));
        }
        Op2("+", St, St, St, (x, y, at) => Concat(x, y, at));
        Op2("+", St, St, Ch, (x, y, at) => RT.StrValue(S(x, at) + C(y)));
        Op2("+", St, Ch, St, (x, y, at) => RT.StrValue(C(x) + S(y, at)));
        Op2("*", St, St, I, (x, y, at) => RT.StrValue(Repeat(S(x, at), y.N, at)));
        Op2("*", St, I, St, (x, y, at) => RT.StrValue(Repeat(S(y, at), x.N, at)));
        var RS = M.RefString;
        var StrMode = M.String;
        Op("+:= PLUSAB", RS, new[] { RS, St }, a => new OpAb(a[0], a[1], (x, y, at) => Concat(x, y, at), StrMode));
        Op("+:= PLUSAB", RS, new[] { RS, Ch }, a => new OpAb(a[0], a[1], (x, y, at) => RT.StrValue(S(x, at) + C(y)), StrMode));
        Op("*:= TIMESAB", RS, new[] { RS, I }, a => new OpAb(a[0], a[1], (x, y, at) => RT.StrValue(Repeat(S(x, at), y.N, at)), StrMode));
        Op("+=: PLUSTO", RS, new[] { St, RS }, a => new OpTo(a[0], a[1], (x, y, at) => Concat(x, y, at), StrMode));
        Op("+=: PLUSTO", RS, new[] { Ch, RS }, a => new OpTo(a[0], a[1], (x, y, at) => RT.StrValue(C(x) + S(y, at)), StrMode));
    }

    static Value Concat(Value x, Value y, Node at)
    {
        var a = x.O as RowV ?? throw RT.Err("chaîne non initialisée", at);
        var b = y.O as RowV ?? throw RT.Err("chaîne non initialisée", at);
        var ea = a.Elements(); var eb = b.Elements();
        var data = new Value[ea.Length + eb.Length];
        Array.Copy(ea, data, ea.Length); Array.Copy(eb, 0, data, ea.Length, eb.Length);
        foreach (var v in data) if (ReferenceEquals(v.O, Value.Uninit)) throw RT.Err("caractère non initialisé dans une chaîne", at);
        return Value.Obj(RowV.Make1(data));
    }

    static string Repeat(string s, long n, Node at)
    {
        if (n <= 0) return "";
        if (s.Length * n > 100_000_000) throw RT.Err("chaîne trop longue", at);
        var sb = new StringBuilder((int)(s.Length * n));
        for (long i = 0; i < n; i++) sb.Append(s);
        return sb.ToString();
    }

    // ------------------------------------------------------------------ bits et octets

    public static BigInteger BitsMask(int size) => (BigInteger.One << BitsWidthOf(size)) - 1;

    static void BuildBitsBytesOps(ModeTable M)
    {
        var I = M.Int; var B = M.Bool;
        for (int s0 = 0; s0 <= 2; s0++)
        {
            int s = s0;
            int size = s;
            var Bt = M.BitsOf(s);
            var LI = M.IntOf(s);
            int w = BitsWidthOf(s);
            var mask = BitsMask(s);
            BigInteger G(Value v) => size == 0 ? (BigInteger)(ulong)(uint)v.N : (BigInteger)v.O!;
            Value Mk(BigInteger b) => size == 0 ? Value.I((long)(uint)(b & mask)) : Value.Obj(b & mask);
            Op2("= EQ", B, Bt, Bt, (x, y, at) => Value.B(G(x) == G(y)));
            Op2("/= ~= ^= NE", B, Bt, Bt, (x, y, at) => Value.B(G(x) != G(y)));
            Op2("AND &", Bt, Bt, Bt, (x, y, at) => Mk(G(x) & G(y)));
            Op2("OR", Bt, Bt, Bt, (x, y, at) => Mk(G(x) | G(y)));
            Op2("XOR", Bt, Bt, Bt, (x, y, at) => Mk(G(x) ^ G(y)));
            Op1("NOT ~", Bt, Bt, (v, at) => Mk(~G(v) & mask));
            Op2("<= LE", B, Bt, Bt, (x, y, at) => Value.B((G(x) | G(y)) == G(y)));
            Op2(">= GE", B, Bt, Bt, (x, y, at) => Value.B((G(x) | G(y)) == G(x)));
            Value Shift(Value x, long n, Node at)
            {
                if (Math.Abs(n) > w) throw RT.Err($"décalage de {n} positions supérieur à la largeur des bits ({w})", at);
                return n >= 0 ? Mk(G(x) << (int)n) : Mk(G(x) >> (int)-n);
            }
            Op2("SHL UP ^ **", Bt, Bt, I, (x, y, at) => Shift(x, y.N, at));
            Op2("SHR DOWN", Bt, Bt, I, (x, y, at) => Shift(x, -y.N, at));
            Op1("ABS", LI, Bt, (v, at) => size == 0 ? Value.I((long)(int)(uint)v.N) : Value.Obj(G(v))); // a68g : interpretation signee sur 32 bits
            Op1("BIN", Bt, LI, (v, at) =>
            {
                var b = Big(v, size);
                if (b.Sign < 0) throw RT.Err("BIN d'un entier négatif", at);
                return Mk(b);
            });
            Op2("ELEM", B, I, Bt, (x, y, at) =>
            {
                if (x.N < 1 || x.N > w) throw RT.Err($"ELEM : indice {x.N} hors de [1:{w}]", at);
                return Value.B(((G(y) >> (int)(w - x.N)) & 1) == 1);
            });
            Op2("SET", Bt, I, Bt, (x, y, at) =>
            {
                if (x.N < 1 || x.N > w) throw RT.Err($"SET : indice {x.N} hors de [1:{w}]", at);
                return Mk(G(y) | (BigInteger.One << (int)(w - x.N)));
            });
            Op2("CLEAR", Bt, I, Bt, (x, y, at) =>
            {
                if (x.N < 1 || x.N > w) throw RT.Err($"CLEAR : indice {x.N} hors de [1:{w}]", at);
                return Mk(G(y) & ~(BigInteger.One << (int)(w - x.N)));
            });
            if (s < 2)
            {
                var Bt2 = M.BitsOf(s + 1);
                int s2 = s + 1;
                Op1("LENG", Bt2, Bt, (v, at) => Value.Obj(G(v)));
                Op1("SHORTEN", Bt, Bt2, (v, at) =>
                {
                    var b = (BigInteger)v.O!;
                    if (b > mask) throw RT.Err("SHORTEN : bits non représentables dans la longueur inférieure", at);
                    return Mk(b);
                });
            }
        }
        // ABS (BITS) INT : interpretation non signee limitee a INT (a68g : valeur sur 32 bits)
        // BYTES
        for (int s0 = 0; s0 <= 1; s0++)
        {
            int s = s0;
            var By = s == 0 ? M.Bytes : M.LongBytes;
            int w = s == 0 ? BytesWidth : LongBytesWidth;
            string G(Value v, Node at) => v.O as string ?? throw RT.Err("octets non initialisés", at);
            foreach (var (n, cmp) in CmpNames()) { var cc = cmp; Op2(n, B, By, By, (x, y, at) => Value.B(CmpRes(StringCompare(G(x, at), G(y, at)), cc))); }
            Op2("ELEM", M.Char, I, By, (x, y, at) =>
            {
                if (x.N < 1 || x.N > w) throw RT.Err($"ELEM : indice {x.N} hors de [1:{w}]", at);
                return Value.I(G(y, at)[(int)x.N - 1]);
            });
            if (s == 0)
            {
                Op1("LENG", M.LongBytes, By, (v, at) => Value.Obj(G(v, at).PadRight(LongBytesWidth, '\0')));
                Op1("SHORTEN", By, M.LongBytes, (v, at) => Value.Obj(G(v, at)[..BytesWidth]));
            }
        }
    }

    // ------------------------------------------------------------------ rangees, semaphores

    static void BuildRowsSemaOps(ModeTable M)
    {
        var I = M.Int;
        Op("LWB", I, new[] { M.Rows }, a => new BoundNode(null, a[0], false));
        Op("UPB", I, new[] { M.Rows }, a => new BoundNode(null, a[0], true));
        Op("LWB", I, new[] { I, M.Rows }, a => new BoundNode(a[0], a[1], false));
        Op("UPB", I, new[] { I, M.Rows }, a => new BoundNode(a[0], a[1], true));
        Op1("ELEMS", I, M.Rows, (v, at) => Value.I(RowOps.GetRow(v, at).Count));
        Op2("ELEMS", I, I, M.Rows, (x, y, at) =>
        {
            var r = RowOps.GetRow(y, at);
            if (x.N < 1 || x.N > r.Dims) throw RT.Err($"dimension {x.N} inexistante", at);
            return Value.I(Math.Max(0, r.Upb[x.N - 1] - r.Lwb[x.N - 1] + 1));
        });
        var Se = M.Sema;
        Op1("LEVEL", Se, I, (v, at) => Value.Obj(new SemaV((int)v.N)));
        Op1("LEVEL", I, Se, (v, at) => { var s = v.O as SemaV ?? throw RT.Err("sémaphore non initialisé", at); lock (s.Lock) return Value.I(s.Level); });
        Op("UP", M.Void, new[] { Se }, a => new OpF(a, (f, v, at) => { Par.Up(f, v[0], at); return default; }));
        Op("DOWN", M.Void, new[] { Se }, a => new OpF(a, (f, v, at) => { Par.Down(f, v[0], at); return default; }));
    }

    // ------------------------------------------------------------------ constantes

    static void BuildConstants(ModeTable M)
    {
        var I = M.Int;
        Const("maxint", I, Value.I(int.MaxValue));
        Const("minint", I, Value.I(int.MinValue));
        Const("maxreal", M.Real, Value.R(double.MaxValue));
        Const("minreal", M.Real, Value.R(double.Epsilon));
        Const("smallreal", M.Real, Value.R(double.Epsilon * 0 + 2.220446049250313e-16));
        Const("intwidth", I, Value.I(IntWidth));
        Const("realwidth", I, Value.I(RealWidth));
        Const("expwidth", I, Value.I(ExpWidth));
        Const("bitswidth", I, Value.I(BitsWidth));
        Const("byteswidth", I, Value.I(BytesWidth));
        Const("longbyteswidth", I, Value.I(LongBytesWidth));
        Const("intlengths", I, Value.I(3)); Const("intshorths", I, Value.I(1));
        Const("reallengths", I, Value.I(3)); Const("realshorths", I, Value.I(1));
        Const("compllengths", I, Value.I(3)); Const("complshorths", I, Value.I(1));
        Const("bitslengths", I, Value.I(3)); Const("bitsshorths", I, Value.I(1));
        Const("byteslengths", I, Value.I(2)); Const("bytesshorths", I, Value.I(1));
        Const("maxabschar", I, Value.I(char.MaxValue));
        Const("nullcharacter", M.Char, Value.I(0)); Const("nullchar", M.Char, Value.I(0));
        Const("flip", M.Char, Value.I('T')); Const("flop", M.Char, Value.I('F'));
        Const("errorchar", M.Char, Value.I('*'));
        Const("expchar", M.Char, Value.I('e'));
        Const("blank", M.Char, Value.I(' ')); Const("blankchar", M.Char, Value.I(' ')); Const("blankcharacter", M.Char, Value.I(' '));
        Const("newlinechar", M.Char, Value.I('\n')); Const("newlinecharacter", M.Char, Value.I('\n'));
        Const("formfeedchar", M.Char, Value.I('\f')); Const("formfeedcharacter", M.Char, Value.I('\f'));
        Const("tabchar", M.Char, Value.I('\t')); Const("tabcharacter", M.Char, Value.I('\t'));
        Const("pi", M.Real, Value.R(Math.PI));
        Const("maxbits", M.Bits, Value.I(uint.MaxValue));
        for (int s0 = 1; s0 <= 2; s0++)
        {
            int s = s0;
            string p = LongPrefix(s);
            var ra = new RA(s, RealDigits(s));
            Const(p + "maxint", M.IntOf(s), Value.Obj(MaxLongInt(s)));
            Const(p + "intwidth", I, Value.I(LongIntWidthOf(s)));
            Const(p + "realwidth", I, Value.I(RealWidthOf(s)));
            Const(p + "expwidth", I, Value.I(ExpWidth));
            Const(p + "bitswidth", I, Value.I(BitsWidthOf(s)));
            Const(p + "pi", M.RealOf(s), ra.Pi);
            Const(p + "smallreal", M.RealOf(s), ra.V(MpReal.Make(1, -(RealWidthOf(s)), RealDigits(s))));
            Const(p + "maxreal", M.RealOf(s), ra.V(MpReal.Make(MpReal.Pow10(RealDigits(s)) - 1, MpReal.MaxExponent - RealDigits(s), RealDigits(s))));
            Const(p + "minreal", M.RealOf(s), ra.V(MpReal.Make(1, -MpReal.MaxExponent + 1, RealDigits(s))));
            Const(p + "maxbits", M.BitsOf(s), Value.Obj(BitsMask(s)));
        }
    }

    // ------------------------------------------------------------------ fonctions mathematiques

    static readonly string[] MathFns = { "sqrt", "exp", "ln", "log", "cos", "sin", "tan", "arccos", "arcsin", "arctan", "sinh", "cosh", "tanh", "arcsinh", "arccosh", "arctanh", "cbrt" };
    static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["arccos"] = new[] { "acos" }, ["arcsin"] = new[] { "asin" }, ["arctan"] = new[] { "atan" },
        ["arcsinh"] = new[] { "asinh" }, ["arccosh"] = new[] { "acosh" }, ["arctanh"] = new[] { "atanh" }, ["cbrt"] = new[] { "curt" },
    };

    static void BuildMath(ModeTable M)
    {
        for (int s0 = 0; s0 <= 2; s0++)
        {
            int s = s0;
            var R = M.RealOf(s);
            var ra = new RA(s, RealDigits(s));
            string p = LongPrefix(s);
            foreach (var fn in MathFns)
            {
                var name = fn;
                var names = p + name;
                if (Aliases.TryGetValue(name, out var al)) foreach (var a in al) names += " " + p + a;
                if (s == 1) names += " d" + name; // a68g : dsqrt...
                if (s == 2) names += " q" + name;
                Proc(names, R, new[] { R }, (f, a, at) => ra.Fn(name, a[0], at));
            }
            Proc(p + "arctan2 " + (s == 0 ? "atan2" : p + "atan2"), R, new[] { R, R }, (f, a, at) => ra.Atan2(a[1], a[0], at));
            // complexes
            var C = s == 0 ? M.Compl : s == 1 ? M.LongCompl : M.LongLongCompl;
            foreach (var fn in new[] { "sqrt", "exp", "ln", "cos", "sin", "tan", "arccos", "arcsin", "arctan" })
            {
                var name = fn;
                string names = p + "complex" + name;
                if (s == 0) names += " c" + name;
                Proc(names, C, new[] { C }, (f, a, at) => ComplexFn(ra, name, a[0], at));
            }
            // aleatoire
            var LI = M.IntOf(s);
            Proc(p + "random", R, Array.Empty<Mode>(), (f, a, at) => ra.FromDouble(NextRandom(f.Ctx)));
            Proc(p + "nextrandom", R, new[] { M.Ref(LI) }, (f, a, at) =>
            {
                var r = RT.CheckNil(a[0].O as RefV, at);
                var cur = RT.CheckInit(r.A![r.I], at);
                long seed = s == 0 ? cur.N : (long)((BigInteger)cur.O! % int.MaxValue);
                seed = (long)((seed * 1103515245L + 12345L) & 0x7FFFFFFF);
                r.A[r.I] = s == 0 ? Value.I(seed) : Value.Obj((BigInteger)seed);
                return ra.FromDouble(seed / 2147483648.0);
            });
        }
        Proc("firstrandom", M.Void, new[] { M.Int }, (f, a, at) => { lock (f.Ctx.RandomLock) f.Ctx.RandomSeed = a[0].N * 2654435761L + 1; return default; });
        Proc("rnd", M.Real, Array.Empty<Mode>(), (f, a, at) => Value.R(NextRandom(f.Ctx)));
        // divers a68g
        Proc("seconds cputime clock", M.Real, Array.Empty<Mode>(), (f, a, at) => Value.R(f.Ctx.Clock.Elapsed.TotalSeconds));
        Proc("sleep", M.Int, new[] { M.Int }, (f, a, at) => { Thread.Sleep((int)Math.Max(0, a[0].N) * 1000); return Value.I(0); });
        Proc("stop", M.Void, Array.Empty<Mode>(), (f, a, at) => throw new StopException(0));
        foreach (var (name, pred) in new (string, Func<char, bool>)[]
        {
            ("isalnum", char.IsLetterOrDigit), ("isalpha", char.IsLetter), ("isdigit", char.IsDigit), ("islower", char.IsLower),
            ("isupper", char.IsUpper), ("isspace", char.IsWhiteSpace), ("ispunct", char.IsPunctuation), ("iscntrl", char.IsControl),
            ("isxdigit", c => Uri.IsHexDigit(c)), ("isprint", c => !char.IsControl(c)), ("isgraph", c => !char.IsControl(c) && c != ' '),
        })
        {
            var pr = pred;
            Proc(name, M.Bool, new[] { M.Char }, (f, a, at) => Value.B(pr((char)a[0].N)));
        }
        Proc("toupper", M.Char, new[] { M.Char }, (f, a, at) => Value.I(char.ToUpperInvariant((char)a[0].N)));
        Proc("tolower", M.Char, new[] { M.Char }, (f, a, at) => Value.I(char.ToLowerInvariant((char)a[0].N)));
    }

    static double NextRandom(ExecContext ctx)
    {
        lock (ctx.RandomLock)
        {
            // xorshift64*
            ulong x = (ulong)ctx.RandomSeed;
            if (x == 0) x = 88172645463325252UL;
            x ^= x >> 12; x ^= x << 25; x ^= x >> 27;
            ctx.RandomSeed = (long)x;
            ulong r = x * 2685821657736338717UL;
            return (r >> 11) * (1.0 / (1UL << 53));
        }
    }

    static Value ComplexFn(RA ra, string name, Value v, Node at)
    {
        var x = Cx(v, at);
        if (ra.IsDouble)
        {
            var z = new Complex(x[0].Real, x[1].Real);
            Complex r = name switch
            {
                "sqrt" => Complex.Sqrt(z), "exp" => Complex.Exp(z), "ln" => z == Complex.Zero ? throw RT.Err("ln de zéro", at) : Complex.Log(z),
                "cos" => Complex.Cos(z), "sin" => Complex.Sin(z), "tan" => Complex.Tan(z),
                "arccos" => Complex.Acos(z), "arcsin" => Complex.Asin(z), "arctan" => Complex.Atan(z),
                _ => z,
            };
            return Value.Obj(new[] { Value.R(r.Real), Value.R(r.Imaginary) });
        }
        // multiprecision : formules explicites
        Value re = x[0], im = x[1];
        Value Mk(Value a, Value b) => Value.Obj(new[] { a, b });
        switch (name)
        {
            case "exp":
                {
                    var e = ra.Fn("exp", re, at);
                    return Mk(ra.Mul(e, ra.Fn("cos", im, at), at), ra.Mul(e, ra.Fn("sin", im, at), at));
                }
            case "ln":
                {
                    var mod = ra.Fn("sqrt", ra.Add(ra.Mul(re, re, at), ra.Mul(im, im, at), at), at);
                    return Mk(ra.Fn("ln", mod, at), ra.Atan2(im, re, at));
                }
            case "sqrt":
                {
                    var mod = ra.Fn("sqrt", ra.Add(ra.Mul(re, re, at), ra.Mul(im, im, at), at), at);
                    var two = ra.FromLong(2);
                    var a = ra.Fn("sqrt", ra.Div(ra.Add(mod, re, at), two, at), at);
                    var b = ra.Fn("sqrt", ra.Div(ra.Sub(mod, re, at), two, at), at);
                    if (ra.Sign(im) < 0) b = ra.Neg(b);
                    return Mk(a, b);
                }
            case "sin":
                return Mk(ra.Mul(ra.Fn("sin", re, at), ra.Fn("cosh", im, at), at), ra.Mul(ra.Fn("cos", re, at), ra.Fn("sinh", im, at), at));
            case "cos":
                return Mk(ra.Mul(ra.Fn("cos", re, at), ra.Fn("cosh", im, at), at), ra.Neg(ra.Mul(ra.Fn("sin", re, at), ra.Fn("sinh", im, at), at)));
            default:
                {
                    // repli : calcul en double puis elargissement
                    var z = new Complex(ra.D(re), ra.D(im));
                    Complex r = name switch { "tan" => Complex.Tan(z), "arccos" => Complex.Acos(z), "arcsin" => Complex.Asin(z), "arctan" => Complex.Atan(z), _ => z };
                    return Mk(ra.FromDouble(r.Real), ra.FromDouble(r.Imaginary));
                }
        }
    }
}
