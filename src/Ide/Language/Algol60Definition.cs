namespace Studio.Ide.Language;

/// <summary>Algol 60: reserved words (case-insensitive) or quote stropping ('BEGIN'), comments
/// "comment ... ;" and comments after "end" (up to ; end else), strings "..." with backslash
/// escapes and `...' / ‘...’ (nestable), publication symbols (× ÷ ↑ ≤ ≥ ≠ ¬ ∧ ∨ ⊃ ≡ ⏨).
/// Simula I derives from this definition.</summary>
public class Algol60Definition : LanguageDefinition
{
    public override string Id => "algol60";
    public override string DisplayName => "Algol 60";
    public override string DefaultExtension => ".a60";
    public override IReadOnlyList<string> Extensions { get; } = new[] { ".a60", ".alg" };
    public override bool CaseSensitive => false;

    public static readonly string[] Algol60Keywords =
    {
        "begin", "end", "if", "then", "else", "for", "do", "step", "until", "while", "goto", "go", "to",
        "switch", "procedure", "value", "label", "integer", "real", "Boolean", "array", "own", "string",
        "true", "false", "comment", "and", "or", "not", "impl", "equiv", "div", "power",
        "lt", "le", "eq", "ge", "gt", "ne", "less", "notgreater", "equal", "notless", "greater", "notequal",
    };

    public static readonly string[] Algol60Types = { "integer", "real", "Boolean", "string", "label" };

    public static readonly string[] Algol60Builtins =
    {
        "outstring", "outchar", "inchar", "outinteger", "ininteger", "outreal", "inreal", "outterminator",
        "stop", "fault", "abs", "iabs", "sign", "sqrt", "sin", "cos", "arctan", "ln", "exp", "entier",
        "length", "maxreal", "minreal", "maxint", "epsilon",
    };

    static readonly HashSet<string> A60KeywordSet = new(Algol60Keywords, StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> A60TypeSet = new(Algol60Types, StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> A60BuiltinSet = new(Algol60Builtins, StringComparer.OrdinalIgnoreCase);
    static readonly string[] A60EndCommentStops = { "end", "else" };

    // Vocabulary, overridden by Simula I.
    protected virtual HashSet<string> KeywordSet => A60KeywordSet;
    protected virtual HashSet<string> TypeSet => A60TypeSet;
    protected virtual HashSet<string> BuiltinSet => A60BuiltinSet;
    /// <summary>Words that terminate the comment following "end".</summary>
    protected virtual string[] EndCommentStops => A60EndCommentStops;
    /// <summary>Declaration word of a process class (Simula I "activity"), null if none.</summary>
    protected virtual string? ActivityWord => null;

    static readonly string[] A60CompletionList =
        Algol60Keywords.Concat(Algol60Builtins).Distinct(StringComparer.OrdinalIgnoreCase)
                       .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    public override IReadOnlyList<string> CompletionWords => A60CompletionList;

    public override string NewFileTemplate =>
        "comment New Algol 60 program;\r\n" +
        "begin\r\n" +
        "   outstring(1, \"Hello, world!\\n\")\r\n" +
        "end\r\n";

    /// <summary>Quote stropping is chosen, as by the compiler, when the text contains 'BEGIN' or 'COMMENT'.
    /// Bare words are then identifiers; quoted words are always recognised as keywords.</summary>
    public static bool IsQuoteStropped(string text) =>
        text.Contains("'BEGIN'", StringComparison.OrdinalIgnoreCase) || text.Contains("'COMMENT'", StringComparison.OrdinalIgnoreCase);

    static bool IsLetter(char c) => char.IsLetter(c) && c != '⏨';
    static bool IsLetterOrDigit(char c) => char.IsLetterOrDigit(c) && c != '⏨';

    /// <summary>Quoted word 'WORD' (letters, spaces allowed inside) starting at i: index of the closing
    /// apostrophe, or -1. The word is returned in lower case without spaces.</summary>
    static int MatchQuotedWord(string s, int i, out string word)
    {
        word = "";
        int n = s.Length, j = i + 1;
        bool digits = false;
        var sb = new System.Text.StringBuilder();
        while (j < n && s[j] != '\'' && s[j] != '\n' && s[j] != '\r')
        {
            char c = s[j];
            if (char.IsDigit(c)) digits = true;
            else if (!IsLetter(c) && c != ' ') return -1;
            if (c != ' ') sb.Append(c);
            j++;
        }
        if (j >= n || s[j] != '\'' || sb.Length == 0) return -1;
        word = sb.ToString().ToLowerInvariant();
        if (digits && word != "10") return -1;   // apart from the ten symbol '10', quoted words hold letters only
        return j;
    }

    public override List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        bool quote = IsQuoteStropped(s);
        int n = s.Length, i = 0;
        while (i < n)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c) || c == '﻿') { i++; continue; }
            // Quoted keyword 'BEGIN' (or the ten symbol '10')
            if (c == '\'')
            {
                int close = MatchQuotedWord(s, i, out var qw);
                if (close > 0)
                {
                    int st = i;
                    if (qw == "10")
                    {
                        i = ScanExponent(s, close + 1);
                        tokens.Add(new Token(TokenKind.Number, st, i - st, ""));
                        continue;
                    }
                    i = close + 1;
                    if (qw == "comment")
                    {
                        i = SkipTo(s, i, ';');
                        tokens.Add(new Token(TokenKind.Comment, st, i - st, ""));
                        continue;
                    }
                    tokens.Add(new Token(TypeSet.Contains(qw) ? TokenKind.Type : TokenKind.Keyword, st, i - st, qw));
                    if (qw == "end") i = ScanEndComment(s, i, quote, tokens);
                    continue;
                }
            }
            if (IsLetter(c))
            {
                int st = i;
                while (i < n && IsLetterOrDigit(s[i])) i++;
                string lw = s.Substring(st, i - st).ToLowerInvariant();
                if (!quote)
                {
                    if (lw == "comment")
                    {
                        i = SkipTo(s, i, ';');
                        tokens.Add(new Token(TokenKind.Comment, st, i - st, ""));
                        continue;
                    }
                    if (KeywordSet.Contains(lw))
                    {
                        tokens.Add(new Token(TypeSet.Contains(lw) ? TokenKind.Type : TokenKind.Keyword, st, i - st, lw));
                        if (lw == "end") i = ScanEndComment(s, i, quote, tokens);
                        continue;
                    }
                }
                tokens.Add(new Token(BuiltinSet.Contains(lw) ? TokenKind.Builtin : TokenKind.Identifier, st, i - st, lw));
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1])) || c == '⏨')
            {
                int st = i;
                while (i < n && char.IsDigit(s[i])) i++;
                if (i < n && s[i] == '.' && i + 1 < n && char.IsDigit(s[i + 1])) { i++; while (i < n && char.IsDigit(s[i])) i++; }
                if (i < n)
                {
                    if (s[i] == '⏨') i = ScanExponent(s, i + 1);
                    else if (s[i] == '\'' && MatchQuotedWord(s, i, out var ten) is > 0 and var tenEnd && ten == "10") i = ScanExponent(s, tenEnd + 1);
                    else if (s[i] is '&' or 'e' or 'E')
                    {
                        int j = i + 1;
                        if (j < n && (s[j] == '+' || s[j] == '-')) j++;
                        if (j < n && char.IsDigit(s[j])) i = ScanExponent(s, i + 1);
                    }
                }
                tokens.Add(new Token(TokenKind.Number, st, i - st, ""));
                continue;
            }
            if (c == '"')
            {
                int st = i++;
                while (i < n)
                {
                    if (s[i] == '\\' && i + 1 < n && s[i + 1] != '\n') { i += 2; continue; }
                    if (s[i] == '"') { i++; break; }
                    if (s[i] == '\n') break;
                    i++;
                }
                tokens.Add(new Token(TokenKind.String, st, i - st, ""));
                continue;
            }
            if (c is '`' or '‘')
            {
                // `text' and ‘text’: may be nested and may span several lines
                char open = c, close = c == '`' ? '\'' : '’';
                int st = i++, depth = 1;
                while (i < n && depth > 0)
                {
                    if (s[i] == open) depth++;
                    else if (s[i] == close) depth--;
                    i++;
                }
                tokens.Add(new Token(TokenKind.String, st, i - st, ""));
                continue;
            }
            {
                int st = i;
                string op = MatchOperator(s, i);
                i += op.Length;
                var kind = op is "(" or ")" or "[" or "]" or "," or ";" or ":" ? TokenKind.Punctuation : TokenKind.Operator;
                tokens.Add(new Token(kind, st, op.Length, op));
            }
        }
        return tokens;
    }

    /// <summary>Optional sign and digits of an exponent, starting at i.</summary>
    static int ScanExponent(string s, int i)
    {
        int n = s.Length;
        if (i < n && (s[i] == '+' || s[i] == '-')) i++;
        while (i < n && char.IsDigit(s[i])) i++;
        return i;
    }

    static readonly string[] Ops = { ":=", "**", "<=", ">=", "<>", "/=", "!=", "~=", "==", "=>", "->" };

    static string MatchOperator(string s, int i)
    {
        foreach (var op in Ops)
            if (string.CompareOrdinal(s, i, op, 0, op.Length) == 0) return op;
        return s[i].ToString();
    }

    /// <summary>Advances past the next character c (inclusive) or to the end of the text.</summary>
    static int SkipTo(string s, int i, char c)
    {
        int k = s.IndexOf(c, i);
        return k < 0 ? s.Length : k + 1;
    }

    /// <summary>After "end", everything before ";" or one of the stop words is a comment.
    /// With quote stropping only quoted stop words ('END', 'ELSE'...) end it.</summary>
    int ScanEndComment(string s, int i, bool quote, List<Token> tokens)
    {
        int n = s.Length, j = i;
        int commentStart = -1, commentEnd = -1;
        var stops = EndCommentStops;
        while (j < n)
        {
            char c = s[j];
            if (c == ';') break;
            if (c == '\'')
            {
                int close = MatchQuotedWord(s, j, out var qw);
                if (close > 0 && stops.Contains(qw)) break;
            }
            if (IsLetter(c))
            {
                int st = j;
                while (j < n && IsLetterOrDigit(s[j])) j++;
                if (!quote && stops.Any(x => string.Equals(x, s.Substring(st, j - st), StringComparison.OrdinalIgnoreCase))) { j = st; break; }
                if (commentStart < 0) commentStart = st;
                commentEnd = j;
                continue;
            }
            if (!char.IsWhiteSpace(c))
            {
                if (commentStart < 0) commentStart = j;
                commentEnd = j + 1;
            }
            j++;
        }
        if (commentStart >= 0)
        {
            tokens.Add(new Token(TokenKind.Comment, commentStart, commentEnd - commentStart, ""));
            return commentEnd;
        }
        return i;
    }

    protected override int BlockDelta(Token t) =>
        t.Kind == TokenKind.Keyword ? t.Norm switch { "begin" => 1, "end" => -1, _ => 0 } : 0;

    /// <summary>Words that may start the specification part of a procedure (or activity) heading.</summary>
    static bool IsSpecificationWord(Token t) =>
        t.Kind == TokenKind.Type || (t.Kind == TokenKind.Keyword && t.Norm is "value" or "array" or "switch" or "own" or "procedure");

    protected override void AnalyzeStructure(CodeModel m)
    {
        var toks = m.Tokens;
        var stack = new Stack<(int Index, int HeaderStart, string Title)>();
        var parenStack = new Stack<int>();
        int pendingHeader = -1;     // offset of the procedure/activity heading line waiting for its begin
        string pendingTitle = "";
        HashSet<string>? pendingParams = null;
        bool expectBody = false;    // after a ";" of a heading: specification or body expected
        int depth = 0;
        string? activity = ActivityWord;

        for (int k = 0; k < toks.Count; k++)
        {
            var t = toks[k];
            if (t.Kind == TokenKind.Comment) continue;
            if (expectBody && pendingHeader >= 0)
            {
                expectBody = false;
                // The body is a simple statement (no begin): the heading does not open a fold.
                if (!(t.Kind == TokenKind.Keyword && t.Norm == "begin") && !IsSpecificationWord(t))
                {
                    pendingHeader = -1;
                    pendingParams = null;
                }
            }
            if (t.Kind == TokenKind.Punctuation)
            {
                if (t.Norm is "(" or "[") parenStack.Push(k);
                else if ((t.Norm is ")" or "]") && parenStack.Count > 0) AddPair(m, parenStack.Pop(), k);
                else if (t.Norm == ";" && pendingHeader >= 0) expectBody = true;
                continue;
            }
            if (t.Kind != TokenKind.Keyword && t.Kind != TokenKind.Type) continue;
            if (t.Norm == "begin")
            {
                // Simula I: "SIMULA begin" opens the simulation block
                bool simulaBlock = k > 0 && toks[k - 1].Kind == TokenKind.Keyword && toks[k - 1].Norm == "simula";
                string title = pendingHeader >= 0 ? pendingTitle : simulaBlock ? "SIMULA begin … end" : "begin … end";
                int header = pendingHeader >= 0 ? pendingHeader : simulaBlock ? toks[k - 1].Start : t.Start;
                stack.Push((k, header, title));
                pendingHeader = -1; pendingParams = null;
                depth++;
            }
            else if (t.Norm == "end")
            {
                if (stack.Count > 0)
                {
                    var (open, header, title) = stack.Pop();
                    AddPair(m, open, k);
                    AddFold(m, header, t.End, title);
                    depth = Math.Max(0, depth - 1);
                }
                pendingHeader = -1; pendingParams = null;
            }
            else if (t.Norm == "procedure" || (activity != null && t.Norm == activity))
            {
                bool isActivity = t.Norm != "procedure";
                int nameIdx = k + 1;
                if (nameIdx >= toks.Count || toks[nameIdx].Kind is not (TokenKind.Identifier or TokenKind.Builtin)) continue;
                string name = m.TokenText(toks[nameIdx]);
                if (!isActivity && pendingParams != null && pendingParams.Contains(name)) continue; // parameter specification
                string detail = !isActivity && k > 0 && toks[k - 1].Kind == TokenKind.Type
                    ? m.TokenText(toks[k - 1]) + " procedure"
                    : isActivity ? "activity" : "procedure";
                var pars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (nameIdx + 1 < toks.Count && toks[nameIdx + 1].Norm == "(")
                    for (int j = nameIdx + 2; j < toks.Count && toks[j].Norm != ")"; j++)
                        if (toks[j].Kind is TokenKind.Identifier or TokenKind.Builtin) pars.Add(m.TokenText(toks[j]));
                m.Outline.Add(new OutlineItem(isActivity ? OutlineKind.Class : OutlineKind.Procedure, name, detail, t.Start, depth));
                pendingHeader = LineStart(m.Text, !isActivity && k > 0 && toks[k - 1].Kind == TokenKind.Type ? toks[k - 1].Start : t.Start);
                pendingTitle = (isActivity ? "activity " : "procedure ") + name + " …";
                pendingParams = pars;
                expectBody = false;
            }
        }
        CollectIdentifiers(m, StringComparer.OrdinalIgnoreCase);
    }

    static int LineStart(string text, int offset)
    {
        int k = offset > 0 ? text.LastIndexOf('\n', offset - 1) : -1;
        return k + 1;
    }

    static void AddPair(CodeModel m, int open, int close)
    {
        m.Pairs.Add(new TokenPair(open, close));
        m.PairOf[open] = close;
        m.PairOf[close] = open;
    }

    // Line comment: "comment text ;" ('COMMENT' with quote stropping). The comment ends at the
    // first ";": each inner ";" is followed by the mark "comment~" which opens a new comment, and
    // if the line does not end with ";", " ~;" is appended to close the comment.
    // Both marks are removed when uncommenting.
    const string CloseMark = " ~;";

    public override string CommentLine(string line) => CommentLine(line, quote: false);

    public override string CommentLine(string line, string documentText) => CommentLine(line, IsQuoteStropped(documentText));

    static string CommentLine(string line, bool quote)
    {
        int ind = line.Length - line.TrimStart().Length;
        var body = line[ind..].TrimEnd();
        if (body.Length == 0) return line;
        string word = quote ? "'COMMENT'" : "comment";
        bool endsWithSemicolon = body.EndsWith(';');
        var inner = endsWithSemicolon ? body[..^1] : body;
        inner = inner.Replace(";", ";" + word + "~");
        return line[..ind] + word + " " + inner + (endsWithSemicolon ? ";" : CloseMark);
    }

    static string? CommentWord(string trimmed)
    {
        foreach (var w in new[] { "'COMMENT'", "comment" })
            if (trimmed.StartsWith(w, StringComparison.OrdinalIgnoreCase)
                && (trimmed.Length == w.Length || !IsLetterOrDigit(trimmed[w.Length])))
                return trimmed[..w.Length];
        return null;
    }

    public override bool IsCommentedLine(string line) => CommentWord(line.TrimStart()) != null;

    public override string UncommentLine(string line)
    {
        int ind = line.Length - line.TrimStart().Length;
        var body = line[ind..].TrimEnd();
        var word = CommentWord(body);
        if (word == null) return line;
        body = body[word.Length..];
        if (body.StartsWith(' ')) body = body[1..];
        if (body.EndsWith(CloseMark, StringComparison.Ordinal)) body = body[..^CloseMark.Length];
        else if (body.EndsWith("~;", StringComparison.Ordinal)) body = body[..^2];
        return line[..ind] + body.Replace(";" + word + "~", ";");
    }
}
