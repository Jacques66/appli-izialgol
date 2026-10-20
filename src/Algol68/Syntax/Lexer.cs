using System.Numerics;
using System.Text;
using Studio.Common;

namespace Studio.Algol68.Syntax;

public enum TK
{
    EOF, Ident, Bold, IntDen, RealDen, BitsDen, StringDen, FormatText, Op,
    LParen, RParen, LBrack, RBrack, Comma, Semi, Colon, Becomes, IdtyEq, IdtyNe, Bar, BarColon, At,
}

public sealed class Token
{
    public TK Kind;
    public string Text = "";          // identifier (without spaces), bold word (upper case), operator, string
    public string File = "";
    public int Line, Col;
    public BigInteger IntValue;       // IntDen / BitsDen
    public int Radix;                 // BitsDen
    public int FormatLine, FormatCol; // FormatText: position of the first character after '$'
    public override string ToString() => Kind switch
    {
        TK.EOF => "end of text",
        TK.Ident => $"'{Text}'",
        TK.Bold => $"'{Text}'",
        TK.StringDen => $"'\"{Text}\"'",
        TK.FormatText => "format text",
        _ => $"'{Text}'",
    };
}

public enum Stropping { Upper, Quote }

/// <summary>Lexical analysis: UPPER or QUOTE stropping, comments, pragmats, inclusions.</summary>
public sealed class Lexer
{
    readonly List<Diagnostic> diags;
    readonly CompileOptions options;
    readonly List<Token> tokens = new();
    readonly HashSet<string> includeStack = new(StringComparer.OrdinalIgnoreCase);
    public List<SourceFile> Included { get; } = new();
    public int? PrecisionPragmat { get; private set; }

    public Lexer(List<Diagnostic> diags, CompileOptions options) { this.diags = diags; this.options = options; }

    public Stropping MainStropping { get; private set; }

    /// <summary>Lexical analysis of a fragment (unit inside a format), with shifted positions.</summary>
    public static List<Token> LexFragment(string text, string file, int line, int col, Stropping strop, List<Diagnostic> diags, CompileOptions opt)
    {
        var lx = new Lexer(diags, opt);
        var s = new Src { Text = text, File = file, Line = line, Col = col };
        lx.LexSource(s, ref strop);
        lx.tokens.Add(new Token { Kind = TK.EOF, File = file, Line = s.Line, Col = s.Col });
        return lx.tokens;
    }

    public List<Token> Run(SourceFile file)
    {
        MainStropping = DetectStropping(file.Text);
        LexFile(file, MainStropping);
        tokens.Add(new Token { Kind = TK.EOF, File = file.Path, Line = lastLine, Col = lastCol });
        return tokens;
    }

    int lastLine = 1, lastCol = 1;

    static readonly HashSet<string> QuoteKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "BEGIN","END","PROC","INT","REAL","IF","FOR","MODE","PRINT","CO","COMMENT","PR","BOOL","CHAR","STRING","REF","STRUCT","WHILE","DO","OD","THEN","FI","ELSE","CASE","ESAC","OP","PRIO","LOC","HEAP","VOID","TRUE","FALSE","SKIP","NIL","UNION","FLEX","LONG","SHORT","GOTO","PAR","FORMAT","BITS","BYTES","COMPL","FILE","SEMA","OF","TO","FROM","BY","IN","OUT","IS","ISNT","EXIT","EMPTY","PRAGMAT","UPPER","QUOTE"
    };

    public static Stropping DetectStropping(string text)
    {
        // Ignores the contents of strings; looks for 'KEYWORD'.
        bool inStr = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') { inStr = !inStr; continue; }
            if (inStr) continue;
            if (c == '\'')
            {
                int j = i + 1;
                while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
                if (j < text.Length && text[j] == '\'' && j > i + 1)
                {
                    string w = text.Substring(i + 1, j - i - 1);
                    if (QuoteKeywords.Contains(w)) return Stropping.Quote;
                }
            }
        }
        return Stropping.Upper;
    }

    void Error(string file, int line, int col, string msg) => diags.Add(new Diagnostic(Severity.Error, file, line, col, msg));
    void Warning(string file, int line, int col, string msg) => diags.Add(new Diagnostic(Severity.Warning, file, line, col, msg));

    sealed class Src
    {
        public string Text = ""; public string File = ""; public int Pos; public int Line = 1, Col = 1;
        public char Peek(int k = 0) => Pos + k < Text.Length ? Text[Pos + k] : '\0';
        public bool AtEnd => Pos >= Text.Length;
        public char Next()
        {
            char c = Text[Pos++];
            if (c == '\n') { Line++; Col = 1; } else Col++;
            return c;
        }
    }

    static bool IsOpMonad(char c) => c is '+' or '-' or '!' or '?' or '%' or '^' or '&' or '~';
    static bool IsOpNomad(char c) => c is '<' or '>' or '/' or '=' or '*';

    void LexFile(SourceFile file, Stropping strop)
    {
        if (!includeStack.Add(file.Path))
        {
            Error(file.Path, 1, 1, $"recursive inclusion of file '{file.Path}'");
            return;
        }
        var s = new Src { Text = file.Text, File = file.Path };
        try { LexSource(s, ref strop); }
        finally { includeStack.Remove(file.Path); }
    }

    void Add(Token t) { tokens.Add(t); lastLine = t.Line; lastCol = t.Col; }

    void LexSource(Src s, ref Stropping strop)
    {
        while (true)
        {
            // spaces
            while (!s.AtEnd && char.IsWhiteSpace(s.Peek())) s.Next();
            if (s.AtEnd) return;
            int line = s.Line, col = s.Col;
            char c = s.Peek();
            Token Tok(TK k, string text) => new Token { Kind = k, Text = text, File = s.File, Line = line, Col = col };

            // comments #...# and ¢...¢
            if (c == '#' || c == '¢')
            {
                s.Next();
                while (!s.AtEnd && s.Peek() != c) s.Next();
                if (s.AtEnd) Error(s.File, line, col, "unterminated comment");
                else s.Next();
                continue;
            }
            // bold words
            string? bold = null;
            if (strop == Stropping.Upper && c >= 'A' && c <= 'Z')
            {
                var sb = new StringBuilder();
                while (!s.AtEnd && ((s.Peek() >= 'A' && s.Peek() <= 'Z') || char.IsDigit(s.Peek()) || s.Peek() == '_')) sb.Append(s.Next());
                bold = sb.ToString();
            }
            else if (strop == Stropping.Quote && c == '\'')
            {
                s.Next();
                var sb = new StringBuilder();
                while (!s.AtEnd && s.Peek() != '\'' && s.Peek() != '\n') sb.Append(s.Next());
                if (s.Peek() == '\'') s.Next(); else Error(s.File, line, col, "unterminated bold word (missing apostrophe)");
                bold = sb.ToString().Replace(" ", "").ToUpperInvariant();
            }
            if (bold != null)
            {
                if (bold == "CO" || bold == "COMMENT")
                {
                    if (!SkipUntilBold(s, bold, strop)) Error(s.File, line, col, $"unterminated {bold} comment");
                    continue;
                }
                if (bold == "PR" || bold == "PRAGMAT")
                {
                    string? body = ReadUntilBold(s, bold, strop);
                    if (body == null) { Error(s.File, line, col, $"unterminated {bold} pragmat"); continue; }
                    HandlePragmat(body, s, line, col, ref strop);
                    continue;
                }
                Add(Tok(TK.Bold, bold));
                continue;
            }
            // identifiers
            if (char.IsLetter(c) && (strop == Stropping.Quote || char.IsLower(c)))
            {
                var sb = new StringBuilder();
                while (true)
                {
                    while (!s.AtEnd && (char.IsLetterOrDigit(s.Peek()) || s.Peek() == '_') && (strop == Stropping.Quote || !(s.Peek() >= 'A' && s.Peek() <= 'Z')))
                        sb.Append(s.Next());
                    // inner spaces: continue if the next non-blank character is a lower-case letter/digit
                    int k = 0;
                    while (s.Peek(k) == ' ' || s.Peek(k) == '\t') k++;
                    if (k > 0)
                    {
                        char d = s.Peek(k);
                        bool cont = strop == Stropping.Quote ? (char.IsLetterOrDigit(d) || d == '_') : (char.IsLower(d) || char.IsDigit(d) || d == '_');
                        if (cont) { for (int i = 0; i < k; i++) s.Next(); continue; }
                    }
                    break;
                }
                string id = sb.ToString();
                if (strop == Stropping.Quote) id = id.ToLowerInvariant();
                Add(Tok(TK.Ident, id));
                continue;
            }
            // numeric denotations
            if (char.IsDigit(c) || (c == '.' && char.IsDigit(s.Peek(1))))
            {
                LexNumber(s, line, col);
                continue;
            }
            // strings
            if (c == '"')
            {
                s.Next();
                var sb = new StringBuilder();
                bool ok = false;
                while (!s.AtEnd)
                {
                    char d = s.Peek();
                    if (d == '\n') break;
                    s.Next();
                    if (d == '"')
                    {
                        if (s.Peek() == '"') { s.Next(); sb.Append('"'); continue; }
                        // concatenation of adjacent denotations: "abc" "def" (a68g extension)
                        ok = true; break;
                    }
                    sb.Append(d);
                }
                if (!ok) Error(s.File, line, col, "unterminated string");
                Add(Tok(TK.StringDen, sb.ToString()));
                continue;
            }
            // format text
            if (c == '$')
            {
                s.Next();
                int fl = s.Line, fc = s.Col;
                var sb = new StringBuilder();
                bool ok = false, inStr = false;
                while (!s.AtEnd)
                {
                    char d = s.Next();
                    if (d == '"') inStr = !inStr;
                    if (d == '$' && !inStr) { ok = true; break; }
                    sb.Append(d);
                }
                if (!ok) Error(s.File, line, col, "unterminated format text (missing '$')");
                var t = Tok(TK.FormatText, sb.ToString());
                t.FormatLine = fl; t.FormatCol = fc;
                Add(t);
                continue;
            }
            switch (c)
            {
                case '(': s.Next(); Add(Tok(TK.LParen, "(")); continue;
                case ')': s.Next(); Add(Tok(TK.RParen, ")")); continue;
                case '[': s.Next(); Add(Tok(TK.LBrack, "[")); continue;
                case ']': s.Next(); Add(Tok(TK.RBrack, "]")); continue;
                case '{': s.Next(); Add(Tok(TK.LParen, "(")); continue; // alternative representation
                case '}': s.Next(); Add(Tok(TK.RParen, ")")); continue;
                case ',': s.Next(); Add(Tok(TK.Comma, ",")); continue;
                case ';': s.Next(); Add(Tok(TK.Semi, ";")); continue;
                case '@': s.Next(); Add(Tok(TK.At, "@")); continue;
                case '|':
                    s.Next();
                    if (s.Peek() == ':') { s.Next(); Add(Tok(TK.BarColon, "|:")); }
                    else Add(Tok(TK.Bar, "|"));
                    continue;
                case ':':
                    s.Next();
                    if (s.Peek() == '=' && s.Peek(1) == ':') { s.Next(); s.Next(); Add(Tok(TK.IdtyEq, ":=:")); }
                    else if (s.Peek() == '/' && s.Peek(1) == '=' && s.Peek(2) == ':') { s.Next(); s.Next(); s.Next(); Add(Tok(TK.IdtyNe, ":/=:")); }
                    else if (s.Peek() == '~' && s.Peek(1) == '=' && s.Peek(2) == ':') { s.Next(); s.Next(); s.Next(); Add(Tok(TK.IdtyNe, ":/=:")); }
                    else if (s.Peek() == '=') { s.Next(); Add(Tok(TK.Becomes, ":=")); }
                    else Add(Tok(TK.Colon, ":"));
                    continue;
            }
            if (c == '!' && !IsOpNomad(s.Peek(1)))
            {
                // alternative representation of |
                s.Next();
                if (s.Peek() == ':') { s.Next(); Add(Tok(TK.BarColon, "|:")); } else Add(Tok(TK.Bar, "|"));
                continue;
            }
            if (IsOpMonad(c) || IsOpNomad(c))
            {
                var sb = new StringBuilder();
                sb.Append(s.Next());
                if (IsOpNomad(s.Peek()) && !(s.Peek() == '=' && s.Peek(1) == ':' ) ) sb.Append(s.Next());
                if (s.Peek() == ':' && s.Peek(1) == '=' && s.Peek(2) != ':') { s.Next(); s.Next(); sb.Append(":="); }
                else if (s.Peek() == '=' && s.Peek(1) == ':' && s.Peek(2) != '=') { s.Next(); s.Next(); sb.Append("=:"); }
                Add(Tok(TK.Op, sb.ToString()));
                continue;
            }
            // common Unicode characters
            string? uni = c switch { '×' => "*", '÷' => "%", '≤' => "<=", '≥' => ">=", '≠' => "/=", '¬' => "~", '∧' => "&", '∨' => "OR", '↑' => "**", _ => null };
            if (uni != null)
            {
                s.Next();
                Add(uni == "OR" ? Tok(TK.Bold, "OR") : Tok(TK.Op, uni));
                continue;
            }
            Error(s.File, line, col, $"unexpected character '{c}'");
            s.Next();
        }
    }

    bool SkipUntilBold(Src s, string word, Stropping strop) => ReadUntilBold(s, word, strop) != null;

    string? ReadUntilBold(Src s, string word, Stropping strop)
    {
        var sb = new StringBuilder();
        while (!s.AtEnd)
        {
            if (strop == Stropping.Upper)
            {
                char c = s.Peek();
                if (c >= 'A' && c <= 'Z' && (s.Pos == 0 || !IsBoldChar(s.Text[s.Pos - 1])))
                {
                    int j = s.Pos;
                    while (j < s.Text.Length && IsBoldChar(s.Text[j])) j++;
                    string w = s.Text.Substring(s.Pos, j - s.Pos);
                    if (w == word) { while (s.Pos < j) s.Next(); return sb.ToString(); }
                    while (s.Pos < j) sb.Append(s.Next());
                    continue;
                }
            }
            else if (s.Peek() == '\'')
            {
                int j = s.Pos + 1;
                while (j < s.Text.Length && s.Text[j] != '\'' && s.Text[j] != '\n') j++;
                if (j < s.Text.Length && s.Text[j] == '\'')
                {
                    string w = s.Text.Substring(s.Pos + 1, j - s.Pos - 1).Replace(" ", "").ToUpperInvariant();
                    if (w == word) { while (s.Pos <= j) s.Next(); return sb.ToString(); }
                }
            }
            sb.Append(s.Next());
        }
        return null;
    }

    static bool IsBoldChar(char c) => (c >= 'A' && c <= 'Z') || char.IsDigit(c) || c == '_';

    void HandlePragmat(string body, Src s, int line, int col, ref Stropping strop)
    {
        string b = body.Trim();
        string lower = b.ToLowerInvariant();
        if (lower == "quote" || lower == "quote stropping") { strop = Stropping.Quote; return; }
        if (lower == "upper" || lower == "upper stropping") { strop = Stropping.Upper; return; }
        if (lower.StartsWith("include") || lower.StartsWith("read"))
        {
            int q1 = b.IndexOf('"'), q2 = q1 >= 0 ? b.IndexOf('"', q1 + 1) : -1;
            if (q1 < 0 || q2 < 0) { Error(s.File, line, col, "include pragmat: quoted file name expected"); return; }
            string name = b.Substring(q1 + 1, q2 - q1 - 1);
            var resolved = options.IncludeResolver?.Invoke(name, s.File);
            if (resolved == null) { Error(s.File, line, col, $"included file '{name}' not found"); return; }
            Included.Add(resolved);
            LexFile(resolved, strop);
            return;
        }
        var m = System.Text.RegularExpressions.Regex.Match(lower, @"^precision\s*=?\s*(\d+)");
        if (m.Success)
        {
            int digits = int.Parse(m.Groups[1].Value);
            if (digits < 1 || digits > 10000) Error(s.File, line, col, "invalid precision (1..10000)");
            else PrecisionPragmat = digits;
            return;
        }
        // other pragmats: ignored
    }

    void LexNumber(Src s, int line, int col)
    {
        var digits = new StringBuilder();
        void ReadDigits(StringBuilder into)
        {
            while (true)
            {
                while (char.IsDigit(s.Peek())) into.Append(s.Next());
                // spaces between digits (RR): only if followed by a digit
                int k = 0;
                while (s.Peek(k) == ' ') k++;
                if (k > 0 && char.IsDigit(s.Peek(k)) && into.Length > 0) { for (int i = 0; i < k; i++) s.Next(); continue; }
                break;
            }
        }
        ReadDigits(digits);
        // bits denotation: NrDIGITS
        if (s.Peek() == 'r' && digits.Length > 0 && IsRadixDigit(s.Peek(1), int.Parse(digits.ToString())))
        {
            int radix = int.Parse(digits.ToString());
            s.Next();
            var sb = new StringBuilder();
            while (IsRadixDigit(s.Peek(), 16)) sb.Append(s.Next());
            if (radix != 2 && radix != 4 && radix != 8 && radix != 16)
                Error(s.File, line, col, $"invalid radix {radix} for a bits denotation (2, 4, 8 or 16)");
            BigInteger v = BigInteger.Zero;
            foreach (char ch in sb.ToString())
            {
                int d = Convert.ToInt32(ch.ToString(), 16);
                if (d >= radix) Error(s.File, line, col, $"invalid digit '{ch}' in radix {radix}");
                v = v * radix + d;
            }
            Add(new Token { Kind = TK.BitsDen, Text = digits + "r" + sb, IntValue = v, Radix = radix, File = s.File, Line = line, Col = col });
            return;
        }
        bool isReal = false;
        var text = new StringBuilder(digits.ToString());
        if (s.Peek() == '.' && char.IsDigit(s.Peek(1)))
        {
            isReal = true;
            text.Append(s.Next());
            var frac = new StringBuilder();
            ReadDigits(frac);
            text.Append(frac);
        }
        // exponent: e E \ \e ⏨
        int save = s.Pos;
        char p0 = s.Peek();
        int expLen = 0;
        if (p0 == 'e' || p0 == 'E' || p0 == '⏨') expLen = 1;
        else if (p0 == '\\') expLen = (s.Peek(1) == 'e' || s.Peek(1) == 'E') ? 2 : 1;
        if (expLen > 0)
        {
            int k = expLen;
            while (s.Peek(k) == ' ') k++;
            char sg = s.Peek(k);
            int k2 = k;
            if (sg == '+' || sg == '-') { k2++; while (s.Peek(k2) == ' ') k2++; }
            if (char.IsDigit(s.Peek(k2)))
            {
                isReal = true;
                for (int i = 0; i < k; i++) s.Next();
                text.Append('e');
                if (sg == '+' || sg == '-') { text.Append(s.Next()); while (s.Peek() == ' ') s.Next(); }
                var ex = new StringBuilder();
                ReadDigits(ex);
                text.Append(ex);
            }
        }
        _ = save;
        if (isReal)
        {
            string t = text.ToString();
            if (t.StartsWith(".")) t = "0" + t;
            Add(new Token { Kind = TK.RealDen, Text = t, File = s.File, Line = line, Col = col });
        }
        else
        {
            Add(new Token { Kind = TK.IntDen, Text = digits.ToString(), IntValue = BigInteger.Parse(digits.ToString()), File = s.File, Line = line, Col = col });
        }
    }

    static bool IsRadixDigit(char c, int radix)
    {
        int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
        return d >= 0 && d < Math.Max(radix, 16) && (radix <= 16);
    }
}
