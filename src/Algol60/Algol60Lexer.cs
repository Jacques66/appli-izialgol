using System.Globalization;
using System.Text;
using Studio.Simula.Compiler;

namespace Studio.Algol60;

/// <summary>
/// Lexer for the hardware representations of Algol 60. It produces the tokens of the
/// shared (Simula) parser.
///  * Reserved-word representation (default): keywords are reserved, case-insensitive words
///    (begin, real, Boolean...), spaces separate tokens.
///  * Quote stropping (detected when the text contains 'BEGIN'): keywords are written between
///    apostrophes ('BEGIN', 'REAL'...), and spaces inside identifiers are ignored.
/// The publication symbols (× ÷ ↑ ≤ ≥ ≠ ¬ ∧ ∨ ⊃ ≡ ⏨ ‘ ’) and their usual ASCII equivalents are accepted.
/// </summary>
public sealed class Algol60Lexer
{
    /// <summary>Keywords of Algol 60, and of Simula I when <see cref="SimulaI"/> is set.</summary>
    static readonly Dictionary<string, T> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["begin"] = T.KBegin, ["end"] = T.KEnd, ["if"] = T.KIf, ["then"] = T.KThen, ["else"] = T.KElse,
        ["for"] = T.KFor, ["do"] = T.KDo, ["step"] = T.KStep, ["until"] = T.KUntil, ["while"] = T.KWhile,
        ["goto"] = T.KGoto, ["go"] = T.KGo, ["to"] = T.KTo, ["switch"] = T.KSwitch, ["procedure"] = T.KProcedure,
        ["value"] = T.KValue, ["label"] = T.KLabel, ["integer"] = T.KInteger, ["real"] = T.KReal,
        ["boolean"] = T.KBoolean, ["array"] = T.KArray, ["own"] = T.KOwn, ["string"] = T.KStringSpec,
        ["true"] = T.KTrue, ["false"] = T.KFalse,
        ["and"] = T.KAnd, ["or"] = T.KOr, ["not"] = T.KNot, ["impl"] = T.KImp, ["equiv"] = T.KEqv,
        ["div"] = T.IntDiv, ["power"] = T.Power,
        ["lt"] = T.Lt, ["le"] = T.Le, ["eq"] = T.Eq, ["ge"] = T.Ge, ["gt"] = T.Gt, ["ne"] = T.Ne,
        ["less"] = T.Lt, ["notgreater"] = T.Le, ["equal"] = T.Eq, ["notless"] = T.Ge, ["greater"] = T.Gt, ["notequal"] = T.Ne,
    };

    /// <summary>Additional keywords of Simula I (activity, element, set...).</summary>
    public static readonly Dictionary<string, T> SimulaIWords = new(StringComparer.OrdinalIgnoreCase);

    readonly string src;
    readonly int file;
    readonly Action<SrcPos, string> error;
    readonly bool quoteStropping;
    readonly Dictionary<string, T>? extra;
    int i, line = 1, lineStart;

    public Algol60Lexer(string source, int fileIndex, Action<SrcPos, string> error, Dictionary<string, T>? extraWords = null)
    {
        src = source;
        file = fileIndex;
        this.error = error;
        extra = extraWords;
        quoteStropping = DetectQuoteStropping(source);
    }

    public bool QuoteStropping => quoteStropping;

    public static bool DetectQuoteStropping(string s) =>
        s.Contains("'BEGIN'", StringComparison.OrdinalIgnoreCase) || s.Contains("'COMMENT'", StringComparison.OrdinalIgnoreCase);

    SrcPos Here => new(file, line, i - lineStart + 1);
    char Cur => i < src.Length ? src[i] : '\0';
    char Peek(int n = 1) => i + n < src.Length ? src[i + n] : '\0';

    void Advance()
    {
        if (src[i] == '\n') { i++; line++; lineStart = i; }
        else i++;
    }

    T? Keyword(string w)
    {
        if (extra != null && extra.TryGetValue(w, out var k2)) return k2;
        return Words.TryGetValue(w, out var k) ? k : null;
    }

    public List<Token> Tokenize()
    {
        var list = new List<Token>();
        while (true)
        {
            SkipBlanks();
            if (i >= src.Length) break;
            var pos = Here;
            var t = Next(pos);
            if (t == null) continue;
            if (t.Kind == T.Ident && t.Text == "comment" && !quoteStropping) { SkipComment(); continue; }
            if (t.Kind == T.KEnd) { list.Add(t); SkipEndComment(); continue; }
            list.Add(t);
        }
        list.Add(new Token { Kind = T.EOF, Pos = Here });
        return FatCommas(list);
    }

    void SkipBlanks()
    {
        while (i < src.Length && (char.IsWhiteSpace(src[i]) || src[i] == '﻿')) Advance();
    }

    /// <summary>"comment ... ;" (the semicolon belongs to the comment).</summary>
    void SkipComment()
    {
        var start = Here;
        while (i < src.Length && src[i] != ';') Advance();
        if (i >= src.Length) { error(start, "comment not terminated by ';'"); return; }
        i++;
    }

    /// <summary>After end: any text up to ';', end or else.</summary>
    void SkipEndComment()
    {
        while (i < src.Length)
        {
            char c = src[i];
            if (c == ';') return;
            if (quoteStropping && c == '\'')
            {
                int j = i + 1;
                while (j < src.Length && char.IsLetter(src[j])) j++;
                if (j < src.Length && src[j] == '\'')
                {
                    var w = src.Substring(i + 1, j - i - 1);
                    if (w.Equals("END", StringComparison.OrdinalIgnoreCase) || w.Equals("ELSE", StringComparison.OrdinalIgnoreCase)) return;
                }
            }
            if (!quoteStropping && char.IsLetter(c) && (i == 0 || !char.IsLetterOrDigit(src[i - 1])))
            {
                int s = i;
                while (i < src.Length && char.IsLetterOrDigit(src[i])) i++;
                var w = src.Substring(s, i - s);
                if (w.Equals("end", StringComparison.OrdinalIgnoreCase) || w.Equals("else", StringComparison.OrdinalIgnoreCase)) { i = s; return; }
                continue;
            }
            Advance();
        }
    }

    Token Tok(T k, SrcPos p, string text) => new() { Kind = k, Pos = p, Text = text, Original = text };

    Token? Next(SrcPos pos)
    {
        char c = Cur;
        // quote-stropped keyword or ten symbol
        if (quoteStropping && c == '\'')
        {
            int j = i + 1;
            var sb = new StringBuilder();
            while (j < src.Length && src[j] != '\'' && src[j] != '\n') { if (src[j] != ' ') sb.Append(src[j]); j++; }
            if (j >= src.Length || src[j] != '\'') { error(pos, "unterminated quoted word"); i = j; return null; }
            var w = sb.ToString();
            i = j + 1;
            if (w == "10") return Number(pos, startWithTen: true);
            if (w.Equals("COMMENT", StringComparison.OrdinalIgnoreCase)) { SkipComment(); return null; }
            if (w.Equals("GOTO", StringComparison.OrdinalIgnoreCase)) return Tok(T.KGoto, pos, "goto");
            var k = Keyword(w);
            if (k == null) { error(pos, $"unknown keyword '{w}'"); return null; }
            return Tok(k.Value, pos, w.ToLowerInvariant());
        }
        if (char.IsLetter(c) && c != '⏨') return Word(pos);
        if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek())) || c == '⏨') return Number(pos, false);
        if (c == '"' || c == '`' || c == '‘') return StringLit(pos);
        int len = 1;
        T kind;
        switch (c)
        {
            case '+': kind = T.Plus; break;
            case '-': if (Peek() == '>') { kind = T.KImp; len = 2; } else kind = T.Minus; break;
            case '*': if (Peek() == '*') { kind = T.Power; len = 2; } else kind = T.Star; break;
            case '×': kind = T.Star; break;
            case '/': if (Peek() == '=') { kind = T.Ne; len = 2; } else kind = T.Slash; break;
            case '÷': case '%': kind = T.IntDiv; break;
            case '↑': case '^': kind = T.Power; break;
            case '<':
                if (Peek() == '=') { kind = T.Le; len = 2; }
                else if (Peek() == '>') { kind = T.Ne; len = 2; }
                else kind = T.Lt;
                break;
            case '>': if (Peek() == '=') { kind = T.Ge; len = 2; } else kind = T.Gt; break;
            case '=':
                if (Peek() == '=') { kind = T.KEqv; len = 2; }
                else if (Peek() == '>') { kind = T.KImp; len = 2; }
                else kind = T.Eq;
                break;
            case '≤': kind = T.Le; break;
            case '≥': kind = T.Ge; break;
            case '≠': kind = T.Ne; break;
            case '!': if (Peek() == '=') { kind = T.Ne; len = 2; } else kind = T.KNot; break;
            case '~': if (Peek() == '=') { kind = T.Ne; len = 2; } else kind = T.KNot; break;
            case '¬': kind = T.KNot; break;
            case '&': case '∧': kind = T.KAnd; break;
            case '|': case '∨': kind = T.KOr; break;
            case '⊃': kind = T.KImp; break;
            case '≡': kind = T.KEqv; break;
            case ':': if (Peek() == '=') { kind = T.Assign; len = 2; } else kind = T.Colon; break;
            case ';': kind = T.Semi; break;
            case ',': kind = T.Comma; break;
            case '(': case '[': kind = T.LPar; break;
            case ')': case ']': kind = T.RPar; break;
            default:
                error(pos, $"unexpected character '{c}'");
                i++;
                return null;
        }
        var text = src.Substring(i, len);
        i += len;
        return Tok(kind, pos, text);
    }

    Token? Word(SrcPos pos)
    {
        int s = i;
        var sb = new StringBuilder();
        while (i < src.Length)
        {
            char c = src[i];
            if (char.IsLetterOrDigit(c) && c != '⏨') { sb.Append(c); i++; continue; }
            // quote stropping: spaces are not significant inside identifiers
            if (quoteStropping && c == ' ' && i + 1 < src.Length)
            {
                int j = i;
                while (j < src.Length && src[j] == ' ') j++;
                if (j < src.Length && char.IsLetterOrDigit(src[j]) && src[j] != '⏨') { i = j; continue; }
            }
            break;
        }
        var orig = sb.ToString();
        if (!quoteStropping)
        {
            if (orig.Equals("comment", StringComparison.OrdinalIgnoreCase)) return Tok(T.Ident, pos, "comment");
            var k = Keyword(orig);
            if (k != null) return Tok(k.Value, pos, orig.ToLowerInvariant());
        }
        return new Token { Kind = T.Ident, Pos = pos, Text = orig.ToLowerInvariant(), Original = orig };
    }

    Token Number(SrcPos pos, bool startWithTen)
    {
        var sb = new StringBuilder();
        bool isReal = false;
        if (!startWithTen)
        {
            while (char.IsDigit(Cur)) { sb.Append(Cur); i++; }
            if (Cur == '.' && char.IsDigit(Peek()))
            {
                isReal = true;
                sb.Append('.');
                i++;
                while (char.IsDigit(Cur)) { sb.Append(Cur); i++; }
            }
        }
        // exponent part: ⏨, '10', &, e or E followed by an optionally signed integer
        bool ten = startWithTen;
        if (!ten)
        {
            if (Cur == '⏨') { ten = true; i++; }
            else if (quoteStropping && Cur == '\'' && Peek() == '1' && Peek(2) == '0' && Peek(3) == '\'') { ten = true; i += 4; }
            else if ((Cur == '&' || Cur == 'e' || Cur == 'E') && (char.IsDigit(Peek()) || ((Peek() == '+' || Peek() == '-') && char.IsDigit(Peek(2))))) { ten = true; i++; }
        }
        if (ten)
        {
            isReal = true;
            if (sb.Length == 0) sb.Append('1');
            sb.Append('E');
            if (Cur == '+' || Cur == '-') { sb.Append(Cur); i++; }
            if (!char.IsDigit(Cur)) error(pos, "exponent expected after the ten symbol");
            while (char.IsDigit(Cur)) { sb.Append(Cur); i++; }
        }
        var t = new Token { Pos = pos, Original = sb.ToString(), Text = sb.ToString() };
        if (isReal)
        {
            t.Kind = T.RealLit;
            if (!double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out t.RealValue))
                error(pos, "invalid real number");
        }
        else
        {
            t.Kind = T.IntLit;
            if (!long.TryParse(sb.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) || v > int.MaxValue)
            {
                error(pos, "integer constant too large (maxint = 2147483647)");
                v = 0;
            }
            t.IntValue = v;
        }
        return t;
    }

    /// <summary>
    /// Strings: "..." (with \n \t \" \\ escapes), `...' or ‘...’ (which may be nested).
    /// The inner string `NL' (or ‘NL’) stands for a newline character.
    /// </summary>
    Token StringLit(SrcPos pos)
    {
        var sb = new StringBuilder();
        char open = Cur;
        i++;
        if (open == '"')
        {
            while (true)
            {
                if (i >= src.Length || src[i] == '\n') { error(pos, "unterminated string"); break; }
                char c = src[i];
                if (c == '"') { i++; break; }
                if (c == '\\' && i + 1 < src.Length)
                {
                    char e = src[i + 1];
                    sb.Append(e switch { 'n' => '\n', 't' => '\t', '"' => '"', '\\' => '\\', _ => e });
                    i += 2;
                    continue;
                }
                sb.Append(c);
                i++;
            }
        }
        else
        {
            char close = open == '`' ? '\'' : '’';
            int depth = 1;
            int innerStart = -1;
            while (true)
            {
                if (i >= src.Length) { error(pos, "unterminated string"); break; }
                char c = src[i];
                if (c == open)
                {
                    depth++;
                    if (depth == 2) innerStart = sb.Length;
                    sb.Append(c);
                    Advance();
                    continue;
                }
                if (c == close)
                {
                    depth--;
                    Advance();
                    if (depth == 0) break;
                    sb.Append(c);
                    if (depth == 1 && innerStart >= 0)
                    {
                        // inner string NL: a newline
                        var inner = sb.ToString(innerStart, sb.Length - innerStart);
                        if (inner.Length == 4 && inner.Substring(1, 2).Equals("NL", StringComparison.OrdinalIgnoreCase))
                        {
                            sb.Length = innerStart;
                            sb.Append('\n');
                        }
                        innerStart = -1;
                    }
                    continue;
                }
                sb.Append(c);
                Advance();
            }
        }
        var s = sb.ToString();
        return new Token { Kind = T.TextLit, Pos = pos, Text = s, Original = s };
    }

    /// <summary>The parameter delimiter ") letter string : (" is equivalent to a comma.</summary>
    static List<Token> FatCommas(List<Token> l)
    {
        var res = new List<Token>(l.Count);
        for (int k = 0; k < l.Count; k++)
        {
            if (l[k].Kind == T.RPar)
            {
                int j = k + 1;
                while (j < l.Count && l[j].Kind == T.Ident && l[j].Original.All(char.IsLetter)) j++;
                if (j > k + 1 && j + 1 < l.Count && l[j].Kind == T.Colon && l[j + 1].Kind == T.LPar)
                {
                    res.Add(new Token { Kind = T.Comma, Pos = l[k].Pos, Text = ",", Original = ",", });
                    k = j + 1;
                    continue;
                }
            }
            res.Add(l[k]);
        }
        return res;
    }
}
