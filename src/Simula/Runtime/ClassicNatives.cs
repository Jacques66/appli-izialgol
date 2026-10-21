using System.Globalization;
using System.Text;

namespace Studio.Simula.Runtime;

/// <summary>Primitives of the Algol 60 environmental block (Modified Report, appendix 2).</summary>
static class ClassicNatives
{
    static Value I(long v) => new() { I = v };

    public static void Register(Action<string, string, NativeFn> def)
    {
        // every channel is the console: input is read from it, output is written to it
        def("__a60_out", "IT>V", (vm, s, b) => { vm.WriteConsole(TextRef.Str((TextRef?)s[b + 1].O)); return default; });
        def("__a60_inchar", "I>I", (vm, s, b) => I(vm.ReadChar()));
        def("__a60_readint", "I>I", (vm, s, b) => I(ReadInteger(vm, "ininteger")));
        def("__a60_readreal", "I>R", (vm, s, b) => Value.Real(ReadReal(vm)));
        def("__a60_fault", "TR>V", (vm, s, b) =>
            throw new SimulaError("fault " + TextRef.Str((TextRef?)s[b].O) + " " + s[b + 1].R.ToString("G15", CultureInfo.InvariantCulture)));
        def("__a60_index", "TI>I", (vm, s, b) =>
        {
            // position (1-based) of the character with code c in the string, 0 if absent
            var t = TextRef.Str((TextRef?)s[b].O);
            return I(t.IndexOf((char)s[b + 1].I) + 1);
        });
        def("__a60_realtext", "R>T", (vm, s, b) => new Value { O = TextRef.FromString(RealText(s[b].R)) });
        def("__a60_lowercase", "T>T", (vm, s, b) => new Value { O = TextRef.FromString(TextRef.Str((TextRef?)s[b].O).ToLowerInvariant()) });
    }

    /// <summary>
    /// Layout of outreal in the Modified Report (1976) for a positive value: 16 significant
    /// digits (the number given by epsilon), the point after the first digits when the decimal
    /// exponent m satisfies -2 &lt;= m &lt; 15, otherwise one digit before the point and "e" m.
    /// The digits are correctly rounded (the specimen body of the report truncates them after
    /// repeated divisions by 10, which in binary arithmetic prints 12 as 11.99999999999999).
    /// </summary>
    static string RealText(double re)
    {
        const int n = 16;
        var e = re.ToString("E15", CultureInfo.InvariantCulture);    // d.ddddddddddddddde+xxx
        var digits = e.Substring(0, 1) + e.Substring(2, n - 1);
        int m = int.Parse(e.Substring(e.IndexOf('E') + 1), CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        if (m >= n || m < -2)
        {
            sb.Append(digits[0]).Append('.').Append(digits, 1, n - 1).Append('e').Append(m).Append(' ');
            return sb.ToString();
        }
        int p = m == n - 1 || m < 0 ? 0 : m + 1;
        if (m < 0) sb.Append(m == -2 ? "0.0" : "0.");
        for (int j = 1; j <= n; j++)
        {
            sb.Append(digits[j - 1]);
            if (j == p) sb.Append('.');
        }
        return sb.Append(' ').ToString();
    }

    static bool IsSpace(int c) => c == ' ' || c == '\n' || c == '\t' || c == '\r';

    static int SkipSpaces(Vm vm)
    {
        int c;
        do c = vm.ReadChar(); while (IsSpace(c));
        if (c < 0) throw new SimulaError("end of input reached while reading a number");
        return c;
    }

    static void CheckTerminator(Vm vm, int c, string who)
    {
        // the terminator of a number is a space, a newline or a semicolon (end of input is also accepted)
        if (c < 0 || IsSpace(c) || c == ';') return;
        throw new SimulaError($"invalid character '{(char)c}' in {who}");
    }

    static long ReadInteger(Vm vm, string who)
    {
        int c = SkipSpaces(vm);
        bool neg = false;
        if (c == '+' || c == '-') { neg = c == '-'; c = vm.ReadChar(); }
        if (c < '0' || c > '9') throw new SimulaError($"invalid character '{(c < 0 ? ' ' : (char)c)}' in {who}");
        long v = 0;
        while (c >= '0' && c <= '9')
        {
            v = v * 10 + (c - '0');
            if (v > 2147483648L) throw new SimulaError($"{who}: integer too large");
            c = vm.ReadChar();
        }
        CheckTerminator(vm, c, who);
        if (neg) v = -v;
        if (v > int.MaxValue) throw new SimulaError($"{who}: integer too large");
        return v;
    }

    static double ReadReal(Vm vm)
    {
        int c = SkipSpaces(vm);
        var sb = new StringBuilder();
        if (c == '+' || c == '-') { if (c == '-') sb.Append('-'); c = vm.ReadChar(); }
        bool digits = false;
        while (c >= '0' && c <= '9') { sb.Append((char)c); digits = true; c = vm.ReadChar(); }
        if (c == '.')
        {
            sb.Append('.');
            c = vm.ReadChar();
            while (c >= '0' && c <= '9') { sb.Append((char)c); digits = true; c = vm.ReadChar(); }
        }
        // exponent part: the ten symbol is written 10 (subscript), &, e or E
        if (c == '⏨' || c == '&' || c == 'e' || c == 'E')
        {
            if (!digits) sb.Append('1');
            digits = true;
            sb.Append('E');
            c = vm.ReadChar();
            while (c == ' ') c = vm.ReadChar();
            if (c == '+' || c == '-') { sb.Append((char)c); c = vm.ReadChar(); }
            bool ed = false;
            while (c >= '0' && c <= '9') { sb.Append((char)c); ed = true; c = vm.ReadChar(); }
            if (!ed) throw new SimulaError("invalid exponent in inreal");
        }
        if (!digits) throw new SimulaError($"invalid character '{(c < 0 ? ' ' : (char)c)}' in inreal");
        CheckTerminator(vm, c, "inreal");
        return double.Parse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

static class SimulaINatives
{
    /// <summary>Process designated by a Simula I element (attribute p__), or the object itself.</summary>
    static object? ProcessOf(object? o)
    {
        if (o is not SimObject so) return null;
        foreach (var a in so.Class.AllAttrs)
            if (a.Name == "p__") return so.S[a.Slot].O;
        return so;
    }

    public static void Register(Action<string, string, NativeFn> def)
    {
        def("__s1_same", "OO>B", (vm, s, b) => new Value { I = ReferenceEquals(ProcessOf(s[b].O), ProcessOf(s[b + 1].O)) ? 1 : 0 });
    }
}
