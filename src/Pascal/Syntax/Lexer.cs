using System.Globalization;
using System.Text;
using Studio.Common;

namespace Studio.Pascal.Syntax;

public enum T
{
    EOF, Ident, Int, Real, Str,
    Plus, Minus, Star, Slash, Assign, Dot, Comma, Semi, Colon, Eq, Ne, Lt, Le, Ge, Gt,
    LParen, RParen, LBrack, RBrack, Caret, DotDot,
    // reserved words
    And, Array, Begin, Case, Const, Div, Do, Downto, Else, End, File, For, Function, Goto, If, In,
    Label, Mod, Nil, Not, Of, Or, Packed, Procedure, Program, Record, Repeat, Set, Then, To, Type,
    Until, Var, While, With,
}

public sealed class Token
{
    public T Kind;
    public string Text = "";     // identifier (lower case), string value, or source text
    public string Raw = "";      // spelling in the source
    public long IntVal;
    public double RealVal;
    public int Line, Col;
    public override string ToString() => Kind switch
    {
        T.Ident => Raw,
        T.Int or T.Real => Raw,
        T.Str => "'" + Text.Replace("'", "''") + "'",
        T.EOF => "end of file",
        _ => Lexer.Spelling(Kind),
    };
}

/// <summary>Lexical analysis (Revised Report, chapter 3): case-insensitive words, comments { } and (* *),
/// alternative symbols (. .) for [ ], @ and ↑ for ^, strings with doubled quotes.</summary>
public sealed class Lexer
{
    static readonly Dictionary<string, T> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        ["and"] = T.And, ["array"] = T.Array, ["begin"] = T.Begin, ["case"] = T.Case, ["const"] = T.Const,
        ["div"] = T.Div, ["do"] = T.Do, ["downto"] = T.Downto, ["else"] = T.Else, ["end"] = T.End,
        ["file"] = T.File, ["for"] = T.For, ["function"] = T.Function, ["goto"] = T.Goto, ["if"] = T.If,
        ["in"] = T.In, ["label"] = T.Label, ["mod"] = T.Mod, ["nil"] = T.Nil, ["not"] = T.Not, ["of"] = T.Of,
        ["or"] = T.Or, ["packed"] = T.Packed, ["procedure"] = T.Procedure, ["program"] = T.Program,
        ["record"] = T.Record, ["repeat"] = T.Repeat, ["set"] = T.Set, ["then"] = T.Then, ["to"] = T.To,
        ["type"] = T.Type, ["until"] = T.Until, ["var"] = T.Var, ["while"] = T.While, ["with"] = T.With,
    };

    public static IReadOnlyCollection<string> ReservedWords => Reserved.Keys;

    public static string Spelling(T k) => k switch
    {
        T.Plus => "+", T.Minus => "-", T.Star => "*", T.Slash => "/", T.Assign => ":=", T.Dot => ".",
        T.Comma => ",", T.Semi => ";", T.Colon => ":", T.Eq => "=", T.Ne => "<>", T.Lt => "<", T.Le => "<=",
        T.Ge => ">=", T.Gt => ">", T.LParen => "(", T.RParen => ")", T.LBrack => "[", T.RBrack => "]",
        T.Caret => "^", T.DotDot => "..", T.Ident => "identifier", T.Int => "integer", T.Real => "real number",
        T.Str => "string", T.EOF => "end of file",
        _ => k.ToString().ToLowerInvariant(),
    };

    readonly List<Diagnostic> diags;
    readonly string file;
    readonly string s;
    int i, line = 1, lineStart;

    public Lexer(SourceFile src, List<Diagnostic> diags)
    {
        this.diags = diags; file = src.Path; s = src.Text;
    }

    void Error(int l, int c, string msg) => diags.Add(new Diagnostic(Severity.Error, file, l, c, msg));

    int Col => i - lineStart + 1;

    public List<Token> Run()
    {
        var list = new List<Token>();
        while (true)
        {
            var t = Next();
            list.Add(t);
            if (t.Kind == T.EOF) break;
        }
        return list;
    }

    char Peek(int k = 0) => i + k < s.Length ? s[i + k] : '\0';

    void NewLine() { line++; lineStart = i + 1; }

    void SkipBlanksAndComments()
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\n') { NewLine(); i++; continue; }
            if (char.IsWhiteSpace(c) || c == '﻿') { i++; continue; }
            if (c == '{' || (c == '(' && Peek(1) == '*'))
            {
                int l = line, col = Col;
                bool brace = c == '{';
                i += brace ? 1 : 2;
                bool closed = false;
                while (i < s.Length)
                {
                    if (s[i] == '\n') { NewLine(); i++; continue; }
                    if (brace && s[i] == '}') { i++; closed = true; break; }
                    if (!brace && s[i] == '*' && Peek(1) == ')') { i += 2; closed = true; break; }
                    i++;
                }
                if (!closed) Error(l, col, "unterminated comment");
                continue;
            }
            break;
        }
    }

    Token Make(T k, int l, int c, string raw) => new() { Kind = k, Line = l, Col = c, Raw = raw, Text = raw };

    Token Next()
    {
        SkipBlanksAndComments();
        int l = line, c = Col;
        if (i >= s.Length) return Make(T.EOF, l, c, "");
        char ch = s[i];
        if (char.IsLetter(ch) && ch < 128)
        {
            int st = i;
            while (i < s.Length && char.IsLetterOrDigit(s[i]) && s[i] < 128) i++;
            string w = s[st..i];
            if (i < s.Length && s[i] == '_')
            {
                Error(line, Col, "the character '_' is not allowed in identifiers (Revised Report: letters and digits only)");
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                w = s[st..i].Replace("_", "");
            }
            if (Reserved.TryGetValue(w, out var kw)) return Make(kw, l, c, w);
            var t = Make(T.Ident, l, c, w);
            t.Text = w.ToLowerInvariant();
            return t;
        }
        if (char.IsDigit(ch))
        {
            int st = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            bool isReal = false;
            if (Peek() == '.' && char.IsDigit(Peek(1)))
            {
                isReal = true; i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            if (Peek() is 'e' or 'E')
            {
                int save = i;
                i++;
                if (Peek() is '+' or '-') i++;
                if (char.IsDigit(Peek()))
                {
                    isReal = true;
                    while (i < s.Length && char.IsDigit(s[i])) i++;
                }
                else i = save;
            }
            string raw = s[st..i];
            if (i < s.Length && char.IsLetter(s[i]) && s[i] < 128)
                Error(line, Col, $"a number must be separated from the following word ('{raw}{s[i]}')");
            if (isReal)
            {
                var t = Make(T.Real, l, c, raw);
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out t.RealVal) || !double.IsFinite(t.RealVal))
                {
                    Error(l, c, $"real constant {raw} out of range");
                    t.RealVal = 0;
                }
                return t;
            }
            var it = Make(T.Int, l, c, raw);
            if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out it.IntVal) || it.IntVal > Runtime.RT.MaxInt)
            {
                Error(l, c, $"integer constant {raw} exceeds maxint (281474976710655)");
                it.IntVal = 0;
            }
            return it;
        }
        if (ch == '\'')
        {
            var sb = new StringBuilder();
            int st = i;
            i++;
            bool closed = false;
            while (i < s.Length)
            {
                if (s[i] == '\n' || s[i] == '\r') break;
                if (s[i] == '\'')
                {
                    if (Peek(1) == '\'') { sb.Append('\''); i += 2; continue; }
                    i++; closed = true; break;
                }
                char x = s[i];
                if (x > 255) { Error(line, Col, $"character '{x}' is outside the character set (ordinal values 0..255)"); x = '?'; }
                sb.Append(x);
                i++;
            }
            if (!closed) Error(l, c, "unterminated string (strings may not span lines)");
            else if (sb.Length == 0) Error(l, c, "empty string: a string must contain at least one character");
            var t = Make(T.Str, l, c, s[st..i]);
            t.Text = sb.Length == 0 ? " " : sb.ToString();
            return t;
        }
        i++;
        switch (ch)
        {
            case '+': return Make(T.Plus, l, c, "+");
            case '-': return Make(T.Minus, l, c, "-");
            case '*': return Make(T.Star, l, c, "*");
            case '/': return Make(T.Slash, l, c, "/");
            case ',': return Make(T.Comma, l, c, ",");
            case ';': return Make(T.Semi, l, c, ";");
            case '=': return Make(T.Eq, l, c, "=");
            case '[': return Make(T.LBrack, l, c, "[");
            case ']': return Make(T.RBrack, l, c, "]");
            case ')': return Make(T.RParen, l, c, ")");
            case '^': case '@': case '↑': return Make(T.Caret, l, c, ch.ToString());
            case ':':
                if (Peek() == '=') { i++; return Make(T.Assign, l, c, ":="); }
                return Make(T.Colon, l, c, ":");
            case '<':
                if (Peek() == '=') { i++; return Make(T.Le, l, c, "<="); }
                if (Peek() == '>') { i++; return Make(T.Ne, l, c, "<>"); }
                return Make(T.Lt, l, c, "<");
            case '>':
                if (Peek() == '=') { i++; return Make(T.Ge, l, c, ">="); }
                return Make(T.Gt, l, c, ">");
            case '.':
                if (Peek() == '.') { i++; return Make(T.DotDot, l, c, ".."); }
                if (Peek() == ')') { i++; return Make(T.RBrack, l, c, ".)"); }
                return Make(T.Dot, l, c, ".");
            case '(':
                if (Peek() == '.') { i++; return Make(T.LBrack, l, c, "(."); }
                return Make(T.LParen, l, c, "(");
            case '}':
                Error(l, c, "'}' without a matching '{'");
                return Next();
        }
        Error(l, c, $"illegal character '{ch}'");
        return Next();
    }
}
