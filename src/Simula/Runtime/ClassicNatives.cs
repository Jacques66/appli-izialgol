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
        def("__a60_lowercase", "T>T", (vm, s, b) => new Value { O = TextRef.FromString(TextRef.Str((TextRef?)s[b].O).ToLowerInvariant()) });
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
