using System.Globalization;
using System.Text;

namespace Studio.Simula.Compiler;

public enum T
{
    EOF, Ident, IntLit, RealLit, CharLit, TextLit,
    // symbols
    Plus, Minus, Star, Slash, IntDiv, Power, Amp, Assign, Denote,
    Lt, Le, Eq, Ge, Gt, Ne, RefEq, RefNe,
    Semi, Colon, LPar, RPar, Dot, Comma,
    // keywords
    KActivate, KAfter, KAnd, KArray, KAt, KBefore, KBegin, KBoolean, KCharacter, KClass,
    KDelay, KDo, KElse, KEnd, KEqv, KExternal, KFalse, KFor, KGo, KGoto, KHidden,
    KIf, KImp, KIn, KInner, KInspect, KInteger, KIs, KLabel, KLong, KName, KNew, KNone,
    KNot, KNotext, KOr, KOtherwise, KPrior, KProcedure, KProtected, KQua, KReactivate,
    KReal, KRef, KShort, KStep, KSwitch, KText, KThen, KThis, KTo, KTrue, KUntil,
    KValue, KVirtual, KWhen, KWhile,
    // compound forms recognised by the lexer
    KAndThen, KOrElse,
    // Algol 60 / Simula I only
    KOwn, KStringSpec,
}

public readonly record struct SrcPos(int File, int Line, int Col)
{
    public static readonly SrcPos None = new(0, 0, 0);
}

public sealed class Token
{
    public T Kind;
    public string Text = "";       // lower-case identifier / text / lexeme
    public string Original = "";   // source spelling for display
    public long IntValue;
    public double RealValue;
    public bool IsLongReal;
    public SrcPos Pos;
    public override string ToString() => Kind switch
    {
        T.Ident => Original,
        T.IntLit => IntValue.ToString(CultureInfo.InvariantCulture),
        T.RealLit => RealValue.ToString(CultureInfo.InvariantCulture),
        T.TextLit => "\"" + Text + "\"",
        T.CharLit => "'" + Text + "'",
        _ => Lexer.Describe(Kind)
    };
}

public sealed class Lexer
{
    static readonly Dictionary<string, T> Keywords = new()
    {
        ["activate"] = T.KActivate, ["after"] = T.KAfter, ["and"] = T.KAnd, ["array"] = T.KArray,
        ["at"] = T.KAt, ["before"] = T.KBefore, ["begin"] = T.KBegin, ["boolean"] = T.KBoolean,
        ["character"] = T.KCharacter, ["class"] = T.KClass, ["delay"] = T.KDelay, ["do"] = T.KDo,
        ["else"] = T.KElse, ["end"] = T.KEnd, ["eqv"] = T.KEqv, ["external"] = T.KExternal,
        ["false"] = T.KFalse, ["for"] = T.KFor, ["go"] = T.KGo, ["goto"] = T.KGoto,
        ["hidden"] = T.KHidden, ["if"] = T.KIf, ["imp"] = T.KImp, ["in"] = T.KIn,
        ["inner"] = T.KInner, ["inspect"] = T.KInspect, ["integer"] = T.KInteger, ["is"] = T.KIs,
        ["label"] = T.KLabel, ["long"] = T.KLong, ["name"] = T.KName, ["new"] = T.KNew,
        ["none"] = T.KNone, ["not"] = T.KNot, ["notext"] = T.KNotext, ["or"] = T.KOr,
        ["otherwise"] = T.KOtherwise, ["prior"] = T.KPrior, ["procedure"] = T.KProcedure,
        ["protected"] = T.KProtected, ["qua"] = T.KQua, ["reactivate"] = T.KReactivate,
        ["real"] = T.KReal, ["ref"] = T.KRef, ["short"] = T.KShort, ["step"] = T.KStep,
        ["switch"] = T.KSwitch, ["text"] = T.KText, ["then"] = T.KThen, ["this"] = T.KThis,
        ["to"] = T.KTo, ["true"] = T.KTrue, ["until"] = T.KUntil, ["value"] = T.KValue,
        ["virtual"] = T.KVirtual, ["when"] = T.KWhen, ["while"] = T.KWhile,
        // alternative representations
        ["lt"] = T.Lt, ["le"] = T.Le, ["eq"] = T.Eq, ["ge"] = T.Ge, ["gt"] = T.Gt, ["ne"] = T.Ne,
    };

    public static string Describe(T k) => k switch
    {
        T.EOF => "end of file", T.Ident => "identifier", T.IntLit => "integer",
        T.RealLit => "real", T.CharLit => "character", T.TextLit => "string",
        T.Plus => "+", T.Minus => "-", T.Star => "*", T.Slash => "/", T.IntDiv => "//",
        T.Power => "**", T.Amp => "&", T.Assign => ":=", T.Denote => ":-", T.Lt => "<",
        T.Le => "<=", T.Eq => "=", T.Ge => ">=", T.Gt => ">", T.Ne => "<>", T.RefEq => "==",
        T.RefNe => "=/=", T.Semi => ";", T.Colon => ":", T.LPar => "(", T.RPar => ")",
        T.Dot => ".", T.Comma => ",", T.KAndThen => "and then", T.KOrElse => "or else",
        _ => k.ToString().Substring(1).ToLowerInvariant()
    };

    readonly string src;
    readonly int file;
    readonly Action<SrcPos, string> error;
    int i, line = 1, lineStart;
    public bool AllowUnderscoreStart;

    public Lexer(string source, int fileIndex, Action<SrcPos, string> error)
    {
        src = source;
        file = fileIndex;
        this.error = error;
    }

    SrcPos Here => new(file, line, i - lineStart + 1);
    char Cur => i < src.Length ? src[i] : '\0';
    char Peek(int n = 1) => i + n < src.Length ? src[i + n] : '\0';

    void NewLine() { line++; lineStart = i; }

    public List<Token> Tokenize()
    {
        var list = new List<Token>();
        bool atLineStart = true;
        while (true)
        {
            // directive lines: '%' in the first column
            if (atLineStart && Cur == '%')
            {
                int s = i;
                while (i < src.Length && src[i] != '\n') i++;
                Directive(src.Substring(s, i - s), list);
                continue;
            }
            atLineStart = false;
            if (i >= src.Length) break;
            char c = Cur;
            if (c == '\n') { i++; NewLine(); atLineStart = true; continue; }
            if (c == ' ' || c == '\t' || c == '\r' || c == '\f' || c == '\v' || c == '\b' || c == '\0' || c == '\u007f' || c == '﻿') { i++; continue; }
            if (c == '!') { SkipDirectComment(); continue; }
            var pos = Here;
            Token? t = null;
            if (char.IsLetter(c) || (c == '_' && AllowUnderscoreStart)) t = Word(pos);
            else if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek())) || (c == '&' && (char.IsDigit(Peek()) || ((Peek() == '+' || Peek() == '-') && char.IsDigit(Peek(2))) || (Peek() == '&' && (char.IsDigit(Peek(2)) || Peek(2) == '+' || Peek(2) == '-')))))
                t = Number(pos);
            else if (c == '"') t = StringLit(pos);
            else if (c == '\'') t = CharConst(pos);
            else t = Symbol(pos);
            if (t == null) continue;
            if (t.Kind == T.KEnd) { list.Add(t); SkipEndComment(); continue; }
            // "comment" is equivalent to "!"
            if (t.Kind == T.Ident && t.Text == "comment") { SkipDirectComment(alreadyConsumed: true); continue; }
            // and then / or else
            if (t.Kind == T.KThen && list.Count > 0 && list[^1].Kind == T.KAnd) { list[^1].Kind = T.KAndThen; continue; }
            if (t.Kind == T.KElse && list.Count > 0 && list[^1].Kind == T.KOr) { list[^1].Kind = T.KOrElse; continue; }
            list.Add(t);
        }
        list.Add(new Token { Kind = T.EOF, Pos = Here });
        return list;
    }

    public List<(string Name, SrcPos Pos)> Includes { get; } = new();

    /// <summary>Textual inclusion (%include): returns the tokens of the included file, or null if it cannot be found.</summary>
    public Func<string, List<Token>?>? IncludeHandler;

    void Directive(string text, List<Token> list)
    {
        // %include "file"  or  %include file
        var d = text.Length > 1 ? text.Substring(1).Trim() : "";
        if (d.StartsWith("include", StringComparison.OrdinalIgnoreCase) || d.StartsWith("insert", StringComparison.OrdinalIgnoreCase))
        {
            var pos = Here;
            var arg = d.Substring(d.IndexOf(' ') < 0 ? d.Length : d.IndexOf(' ')).Trim().Trim('"', '\'');
            if (arg.Length == 0) { error(pos, "%include: file name expected"); return; }
            Includes.Add((arg, pos));
            if (IncludeHandler == null) return;
            var toks = IncludeHandler(arg);
            if (toks == null) { error(pos, $"%include: file '{arg}' not found"); return; }
            list.AddRange(toks.Where(t => t.Kind != T.EOF));
        }
    }

    void SkipDirectComment(bool alreadyConsumed = false)
    {
        // "!" ... ";"  (the ";" belongs to the comment)
        if (!alreadyConsumed) i++;
        var start = Here;
        while (i < src.Length && src[i] != ';')
        {
            if (src[i] == '\n') { i++; NewLine(); continue; }
            i++;
        }
        if (i >= src.Length) { error(start, "comment not terminated by ';'"); return; }
        i++;
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    void SkipEndComment()
    {
        // after end: everything up to ; end else when otherwise (whole words)
        while (i < src.Length)
        {
            char c = src[i];
            if (c == ';') return;
            if (c == '\n') { i++; NewLine(); continue; }
            if (char.IsLetter(c) && (i == 0 || !IsWordChar(src[i - 1])))
            {
                int s = i;
                while (i < src.Length && IsWordChar(src[i])) i++;
                var w = src.Substring(s, i - s).ToLowerInvariant();
                if (w is "end" or "else" or "when" or "otherwise") { i = s; return; }
                continue;
            }
            i++;
        }
    }

    Token Word(SrcPos pos)
    {
        int s = i;
        while (i < src.Length && IsWordChar(src[i])) i++;
        var orig = src.Substring(s, i - s);
        var low = orig.ToLowerInvariant();
        var t = new Token { Pos = pos, Original = orig, Text = low };
        t.Kind = Keywords.TryGetValue(low, out var k) ? k : T.Ident;
        return t;
    }

    Token Number(SrcPos pos)
    {
        var t = new Token { Pos = pos };
        int s = i;
        // radix : 2R..., 8R..., 16R...
        if (char.IsDigit(Cur))
        {
            int j = i;
            while (j < src.Length && char.IsDigit(src[j])) j++;
            if (j < src.Length && (src[j] == 'R' || src[j] == 'r') && j + 1 < src.Length && IsRadixDigit(src[j + 1]))
            {
                var rs = src.Substring(i, j - i);
                int radix = int.Parse(rs, CultureInfo.InvariantCulture);
                if (radix != 2 && radix != 4 && radix != 8 && radix != 16) error(pos, $"radix {radix} not allowed (2, 4, 8 or 16)");
                i = j + 1;
                ulong v = 0;
                while (i < src.Length && (IsRadixDigit(src[i]) || src[i] == '_'))
                {
                    if (src[i] == '_') { i++; continue; }
                    int d = Convert.ToInt32(src[i].ToString(), 16);
                    if (d >= radix) error(pos, $"digit '{src[i]}' invalid in radix {radix}");
                    v = v * (ulong)radix + (ulong)d;
                    if (v > uint.MaxValue) { error(pos, "integer constant too large"); v = 0; }
                    i++;
                }
                t.Kind = T.IntLit;
                t.IntValue = unchecked((int)(uint)v);
                t.Original = src.Substring(s, i - s);
                return t;
            }
        }
        var sb = new StringBuilder();
        bool isReal = false;
        while (i < src.Length && (char.IsDigit(src[i]) || src[i] == '_')) { if (src[i] != '_') sb.Append(src[i]); i++; }
        if (Cur == '.' && char.IsDigit(Peek()))
        {
            isReal = true;
            sb.Append('.');
            i++;
            while (i < src.Length && (char.IsDigit(src[i]) || src[i] == '_')) { if (src[i] != '_') sb.Append(src[i]); i++; }
        }
        if (Cur == '&')
        {
            isReal = true;
            i++;
            if (Cur == '&') { t.IsLongReal = true; i++; }
            if (sb.Length == 0) sb.Append('1');
            sb.Append('E');
            if (Cur == '+' || Cur == '-') { sb.Append(Cur); i++; }
            if (!char.IsDigit(Cur)) error(pos, "exponent expected after '&'");
            while (i < src.Length && (char.IsDigit(src[i]) || src[i] == '_')) { if (src[i] != '_') sb.Append(src[i]); i++; }
        }
        t.Original = src.Substring(s, i - s);
        if (isReal)
        {
            t.Kind = T.RealLit;
            if (!double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out t.RealValue))
                error(pos, "invalid real number");
        }
        else
        {
            t.Kind = T.IntLit;
            if (!long.TryParse(sb.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) || v > int.MaxValue + 1L)
            {
                error(pos, "integer constant too large (maxint = 2147483647)");
                v = 0;
            }
            t.IntValue = v;
        }
        return t;
    }

    static bool IsRadixDigit(char c) => char.IsDigit(c) || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

    // !ddd!: ISO code in strings and characters
    bool TryIsoCode(out char ch)
    {
        ch = '\0';
        if (Cur != '!') return false;
        int j = i + 1, n = 0, v = 0;
        while (j < src.Length && char.IsDigit(src[j]) && n < 3) { v = v * 10 + (src[j] - '0'); j++; n++; }
        if (n == 0 || j >= src.Length || src[j] != '!' || v > 255) return false;
        ch = (char)v;
        i = j + 1;
        return true;
    }

    Token StringLit(SrcPos pos)
    {
        var sb = new StringBuilder();
        while (true)
        {
            // one simple string
            i++; // "
            while (true)
            {
                if (i >= src.Length || src[i] == '\n') { error(pos, "unterminated string (a simple string must fit on one line)"); goto done; }
                if (src[i] == '"')
                {
                    if (Peek() == '"') { sb.Append('"'); i += 2; continue; }
                    i++;
                    break;
                }
                if (src[i] == '!' && TryIsoCode(out var iso)) { sb.Append(iso); continue; }
                if (src[i] == '\r') { i++; continue; }
                sb.Append(src[i]);
                i++;
            }
            // concatenation of simple strings separated by token separators
            int save = i, saveLine = line, saveLs = lineStart;
            while (true)
            {
                if (i < src.Length && (src[i] == ' ' || src[i] == '\t' || src[i] == '\r')) { i++; continue; }
                if (i < src.Length && src[i] == '\n') { i++; NewLine(); continue; }
                if (i < src.Length && src[i] == '!' && !(i + 1 < src.Length && char.IsDigit(src[i + 1])))
                {
                    // direct comment between two strings
                    int j = i + 1;
                    while (j < src.Length && src[j] != ';') j++;
                    if (j < src.Length)
                    {
                        for (int k = i; k < j; k++) if (src[k] == '\n') { line++; lineStart = k + 1; }
                        i = j + 1;
                        continue;
                    }
                }
                break;
            }
            if (Cur == '"') continue;
            i = save; line = saveLine; lineStart = saveLs;
            break;
        }
    done:
        return new Token { Kind = T.TextLit, Text = sb.ToString(), Original = sb.ToString(), Pos = pos };
    }

    Token CharConst(SrcPos pos)
    {
        i++; // '
        char ch;
        if (Cur == '!' && TryIsoCode(out var iso)) ch = iso;
        else if (i < src.Length && src[i] != '\n') { ch = src[i]; i++; }
        else { error(pos, "invalid character constant"); return new Token { Kind = T.CharLit, Text = " ", Pos = pos }; }
        if (Cur != '\'') error(pos, "character constant not closed by '");
        else i++;
        return new Token { Kind = T.CharLit, Text = ch.ToString(), IntValue = ch, Original = ch.ToString(), Pos = pos };
    }

    Token? Symbol(SrcPos pos)
    {
        char c = Cur;
        T k;
        int len = 1;
        switch (c)
        {
            case '+': k = T.Plus; break;
            case '-': k = T.Minus; break;
            case '*': if (Peek() == '*') { k = T.Power; len = 2; } else k = T.Star; break;
            case '/': if (Peek() == '/') { k = T.IntDiv; len = 2; } else k = T.Slash; break;
            case '&': k = T.Amp; break;
            case ':':
                if (Peek() == '=') { k = T.Assign; len = 2; }
                else if (Peek() == '-') { k = T.Denote; len = 2; }
                else k = T.Colon;
                break;
            case '<':
                if (Peek() == '=') { k = T.Le; len = 2; }
                else if (Peek() == '>') { k = T.Ne; len = 2; }
                else k = T.Lt;
                break;
            case '>': if (Peek() == '=') { k = T.Ge; len = 2; } else k = T.Gt; break;
            case '=':
                if (Peek() == '=') { k = T.RefEq; len = 2; }
                else if (Peek() == '/' && Peek(2) == '=') { k = T.RefNe; len = 3; }
                else k = T.Eq;
                break;
            case '\\': if (Peek() == '=') { k = T.Ne; len = 2; } else goto default; break;
            case ';': k = T.Semi; break;
            case '(': k = T.LPar; break;
            case ')': k = T.RPar; break;
            case '[': k = T.LPar; break;
            case ']': k = T.RPar; break;
            case '.': k = T.Dot; break;
            case ',': k = T.Comma; break;
            default:
                error(pos, $"unexpected character '{c}'");
                i++;
                return null;
        }
        var t = new Token { Kind = k, Pos = pos, Original = src.Substring(i, len), Text = src.Substring(i, len) };
        i += len;
        return t;
    }
}
