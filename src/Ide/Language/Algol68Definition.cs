namespace Studio.Ide.Language;

/// <summary>Algol 68: UPPER stropping (upper-case words = bold words) and QUOTE stropping ('BEGIN'),
/// comments CO…CO, COMMENT…COMMENT, #…#, pragmats PR…PR.</summary>
public sealed class Algol68Definition : LanguageDefinition
{
    public override string Id => "algol68";
    public override string DisplayName => "Algol 68";
    public override string DefaultExtension => ".a68";
    public override bool CaseSensitive => true;

    public static readonly string[] Reserved =
    {
        "AT", "BEGIN", "BY", "CASE", "CO", "COMMENT", "DO", "ELIF", "ELSE", "EMPTY", "END", "ESAC", "EXIT",
        "FALSE", "FI", "FOR", "FROM", "GO", "GOTO", "HEAP", "IF", "IN", "IS", "ISNT", "LOC", "MODE", "NIL",
        "OD", "OF", "OP", "OUSE", "OUT", "PAR", "PR", "PRAGMAT", "PRIO", "PROC", "SKIP", "THEN", "TO",
        "TRUE", "WHILE", "UNTIL",
        // standard operators written in bold
        "ABS", "AND", "OR", "NOT", "MOD", "OVER", "ENTIER", "ROUND", "SIGN", "ODD", "UPB", "LWB", "ELEM",
        "SHL", "SHR", "UP", "DOWN", "LENG", "SHORTEN", "REPR", "BIN", "ARG", "RE", "IM", "CONJ",
        "LT", "LE", "GT", "GE", "EQ", "NE", "XOR", "PLUSAB", "MINUSAB", "TIMESAB", "DIVAB", "OVERAB", "MODAB",
    };

    public static readonly string[] Types =
    {
        "INT", "REAL", "BOOL", "CHAR", "STRING", "BITS", "BYTES", "COMPL", "VOID", "FILE", "CHANNEL",
        "SEMA", "FORMAT", "STRUCT", "UNION", "REF", "FLEX", "LONG", "SHORT",
    };

    public static readonly string[] Builtins =
    {
        "print", "printf", "read", "readf", "write", "writef", "newline", "newpage", "space", "backspace",
        "stand in", "stand out", "stand back", "put", "get", "putf", "getf", "whole", "fixed", "float",
        "sqrt", "sin", "cos", "tan", "arcsin", "arccos", "arctan", "exp", "ln", "log", "random", "pi",
        "max int", "max real", "small real", "int width", "real width", "open", "close", "establish",
        "create", "make term", "char in string", "string in string", "upb", "lwb", "abs", "entier", "round",
        "repr", "odd", "sign", "error char", "exp width", "stop",
    };

    static readonly HashSet<string> ReservedSet = new(Reserved, StringComparer.Ordinal);
    static readonly HashSet<string> TypeSet = new(Types, StringComparer.Ordinal);
    static readonly HashSet<string> BuiltinSet = new(Builtins.Where(b => !b.Contains(' ')), StringComparer.Ordinal);

    static readonly string[] CompletionList =
        Reserved.Concat(Types).Concat(Builtins).Distinct().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    public override IReadOnlyList<string> CompletionWords => CompletionList;

    public override string NewFileTemplate =>
        "BEGIN\r\n" +
        "   # New Algol 68 program #\r\n" +
        "   print((\"Hello, world!\", newline))\r\n" +
        "END\r\n";

    public override List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        int n = s.Length, i = 0;
        while (i < n)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            // Bold word in UPPER stropping
            if (c is >= 'A' and <= 'Z')
            {
                int st = i;
                while (i < n && (s[i] is >= 'A' and <= 'Z' || char.IsDigit(s[i]) || s[i] == '_')) i++;
                var word = s.Substring(st, i - st);
                if (TryComment(s, ref i, st, word, quoted: false, tokens)) continue;
                tokens.Add(new Token(Classify(word), st, i - st, word));
                continue;
            }
            // Bold word in QUOTE stropping: 'begin' or 'BEGIN'
            if (c == '\'' && i + 1 < n && char.IsLetter(s[i + 1]))
            {
                int j = i + 1;
                while (j < n && char.IsLetterOrDigit(s[j])) j++;
                if (j < n && s[j] == '\'')
                {
                    int st = i;
                    var word = s.Substring(i + 1, j - i - 1).ToUpperInvariant();
                    i = j + 1;
                    if (TryComment(s, ref i, st, word, quoted: true, tokens)) continue;
                    tokens.Add(new Token(Classify(word), st, i - st, word));
                    continue;
                }
            }
            if (IsIdentStart(c))
            {
                int st = i;
                while (i < n && (char.IsLower(s[i]) || char.IsDigit(s[i]) || s[i] == '_' || (char.IsLetter(s[i]) && !(s[i] is >= 'A' and <= 'Z')))) i++;
                if (i == st) i++;
                var w = s.Substring(st, i - st);
                tokens.Add(new Token(BuiltinSet.Contains(w) ? TokenKind.Builtin : TokenKind.Identifier, st, i - st, w));
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1])))
            {
                int st = i;
                while (i < n && char.IsDigit(s[i])) i++;
                if (i < n && s[i] == 'r' && i + 1 < n && char.IsLetterOrDigit(s[i + 1]))
                {
                    i++;
                    while (i < n && (char.IsDigit(s[i]) || s[i] is >= 'a' and <= 'f')) i++;
                }
                else
                {
                    if (i < n && s[i] == '.' && i + 1 < n && char.IsDigit(s[i + 1])) { i++; while (i < n && char.IsDigit(s[i])) i++; }
                    if (i < n && (s[i] == 'e' || s[i] == '\\' || s[i] == '⏨'))
                    {
                        int j = i + 1;
                        if (j < n && (s[j] == '+' || s[j] == '-')) j++;
                        if (j < n && char.IsDigit(s[j])) { i = j; while (i < n && char.IsDigit(s[i])) i++; }
                    }
                }
                tokens.Add(new Token(TokenKind.Number, st, i - st, ""));
                continue;
            }
            if (c == '#')
            {
                int st = i;
                int k = s.IndexOf('#', i + 1);
                i = k < 0 ? n : k + 1;
                tokens.Add(new Token(TokenKind.Comment, st, i - st, ""));
                continue;
            }
            if (c == '"')
            {
                int st = i++;
                while (i < n)
                {
                    if (s[i] == '"') { if (i + 1 < n && s[i + 1] == '"') { i += 2; continue; } i++; break; }
                    if (s[i] == '\n') break;
                    i++;
                }
                tokens.Add(new Token(TokenKind.String, st, i - st, ""));
                continue;
            }
            {
                int st = i;
                string op = MatchOperator(s, i);
                i += op.Length;
                var kind = op is "(" or ")" or "[" or "]" or "," or ";" or ":" or "|" or "|:" ? TokenKind.Punctuation : TokenKind.Operator;
                tokens.Add(new Token(kind, st, op.Length, op));
            }
        }
        return tokens;
    }

    static TokenKind Classify(string word) =>
        TypeSet.Contains(word) ? TokenKind.Type : ReservedSet.Contains(word) ? TokenKind.Keyword : TokenKind.BoldWord;

    /// <summary>CO / COMMENT / PR / PRAGMAT: comment (or pragmat) up to the same word.</summary>
    static bool TryComment(string s, ref int i, int st, string word, bool quoted, List<Token> tokens)
    {
        if (word is not ("CO" or "COMMENT" or "PR" or "PRAGMAT")) return false;
        int end = FindClosingBold(s, i, word, quoted);
        i = end;
        tokens.Add(new Token(word.StartsWith("PR") ? TokenKind.Pragma : TokenKind.Comment, st, i - st, ""));
        return true;
    }

    static int FindClosingBold(string s, int from, string word, bool quoted)
    {
        int n = s.Length;
        if (quoted)
        {
            int k = from;
            while (true)
            {
                k = s.IndexOf('\'', k);
                if (k < 0) return n;
                int j = k + 1;
                while (j < n && char.IsLetterOrDigit(s[j])) j++;
                if (j < n && s[j] == '\'' && string.Equals(s.Substring(k + 1, j - k - 1), word, StringComparison.OrdinalIgnoreCase))
                    return j + 1;
                k = j < n && s[j] == '\'' ? j : k + 1;
            }
        }
        int p = from;
        while (true)
        {
            p = s.IndexOf(word, p, StringComparison.Ordinal);
            if (p < 0) return n;
            bool startOk = p == 0 || !(s[p - 1] is >= 'A' and <= 'Z' || char.IsDigit(s[p - 1]));
            int e = p + word.Length;
            bool endOk = e >= n || !(s[e] is >= 'A' and <= 'Z' || char.IsDigit(s[e]));
            if (startOk && endOk) return e;
            p = e;
        }
    }

    static readonly string[] Ops =
    {
        ":/=:", ":=:", "+:=", "-:=", "*:=", "/:=", "%:=", "%*:=", "+=:", "**", ":=", "=:", "<=", ">=", "/=",
        "..", "|:", "&", "%*",
    };

    static string MatchOperator(string s, int i)
    {
        foreach (var op in Ops)
            if (string.CompareOrdinal(s, i, op, 0, op.Length) == 0) return op;
        return s[i].ToString();
    }

    static readonly Dictionary<string, string> Openers = new()
    {
        ["BEGIN"] = "END", ["IF"] = "FI", ["CASE"] = "ESAC", ["DO"] = "OD", ["("] = ")", ["["] = "]",
    };
    static readonly HashSet<string> Closers = new() { "END", "FI", "ESAC", "OD", ")", "]" };
    static readonly HashSet<string> Middles = new() { "THEN", "ELSE", "ELIF", "IN", "OUT", "OUSE" };

    static bool IsStructural(Token t) =>
        t.Kind is TokenKind.Keyword or TokenKind.Punctuation;

    protected override int BlockDelta(Token t)
    {
        if (!IsStructural(t)) return 0;
        if (t.Norm is "BEGIN" or "IF" or "CASE" or "DO" or "(") return 1;
        if (t.Norm is "END" or "FI" or "ESAC" or "OD" or ")") return -1;
        return 0;
    }

    protected override bool IsMiddleWord(Token t) => t.Kind == TokenKind.Keyword && Middles.Contains(t.Norm);

    protected override void AnalyzeStructure(CodeModel m)
    {
        var toks = m.Tokens;
        var stack = new Stack<int>();
        int depth = 0;
        for (int k = 0; k < toks.Count; k++)
        {
            var t = toks[k];
            if (!IsStructural(t) && t.Kind != TokenKind.Type) continue;
            if (Openers.ContainsKey(t.Norm) && IsStructural(t))
            {
                stack.Push(k);
                if (t.Norm is "BEGIN" or "(") depth++;
            }
            else if (Closers.Contains(t.Norm) && IsStructural(t))
            {
                // Error tolerance: pop up to the matching opener
                if (stack.Any(o => Openers[toks[o].Norm] == t.Norm))
                {
                    while (stack.Count > 0)
                    {
                        int o = stack.Pop();
                        var ot = toks[o];
                        if (ot.Norm is "BEGIN" or "(") depth = Math.Max(0, depth - 1);
                        if (Openers[ot.Norm] == t.Norm)
                        {
                            m.Pairs.Add(new TokenPair(o, k));
                            m.PairOf[o] = k; m.PairOf[k] = o;
                            if (ot.Norm != "[")
                                AddFold(m, ot.Start, t.End, ot.Norm == "(" ? "( … )" : $"{ot.Norm} … {t.Norm}");
                            break;
                        }
                    }
                }
            }
            else if (t.Norm == "PROC" && t.Kind == TokenKind.Keyword)
                AddOutline(m, k, OutlineKind.Procedure, "PROC", depth);
            else if (t.Norm == "OP" && t.Kind == TokenKind.Keyword)
                AddOutline(m, k, OutlineKind.Operator, "OP", depth);
            else if (t.Norm == "MODE" && t.Kind == TokenKind.Keyword)
                AddOutline(m, k, OutlineKind.Mode, "MODE", depth);
        }
        CollectIdentifiers(m, StringComparer.Ordinal);
    }

    /// <summary>PROC name = …, OP name = …, MODE NAME = … (several declarations separated by commas).</summary>
    static void AddOutline(CodeModel m, int k, OutlineKind kind, string word, int depth)
    {
        var toks = m.Tokens;
        int j = k + 1;
        while (j < toks.Count)
        {
            // Name: consecutive identifiers (spaces are allowed in Algol 68 names), bold word or symbol
            int nameStart = j;
            var parts = new List<string>();
            if (kind == OutlineKind.Procedure)
                while (j < toks.Count && toks[j].Kind is TokenKind.Identifier or TokenKind.Builtin) parts.Add(m.TokenText(toks[j++]));
            else if (j < toks.Count && (toks[j].Kind is TokenKind.BoldWord or TokenKind.Keyword or TokenKind.Operator or TokenKind.Type))
                parts.Add(toks[j++].Norm is { Length: > 0 } nm ? nm : m.TokenText(toks[j - 1]));
            if (parts.Count == 0 || j >= toks.Count) return;
            var next = toks[j];
            if (next.Norm is not ("=" or ":=")) return;
            m.Outline.Add(new OutlineItem(kind, string.Join(" ", parts), word, toks[nameStart].Start, depth));
            // Look for a comma at the same bracket level before the next ";" (multiple declarations)
            int level = 0; j++;
            bool more = false;
            for (; j < toks.Count; j++)
            {
                var t = toks[j];
                if (t.Norm is "(" or "[" or "BEGIN" or "IF" or "CASE" or "DO") level++;
                else if (t.Norm is ")" or "]" or "END" or "FI" or "ESAC" or "OD") { if (--level < 0) return; }
                else if (level == 0 && t.Norm == ";") return;
                else if (level == 0 && t.Norm == ",")
                {
                    // ", name =": new declaration of the same kind
                    int a = j + 1;
                    while (a < toks.Count && toks[a].Norm != "=" && toks[a].Kind is TokenKind.Identifier or TokenKind.Builtin or TokenKind.BoldWord or TokenKind.Operator) a++;
                    if (a < toks.Count && a > j + 1 && toks[a].Norm == "=") { j++; more = true; break; }
                }
            }
            if (!more) return;
        }
    }

    public override string CommentLine(string line)
    {
        int ind = line.Length - line.TrimStart().Length;
        var body = line[ind..].TrimEnd();
        if (body.Length == 0) return line;
        return line[..ind] + "# " + body.Replace("#", "№") + " #";
    }

    public override bool IsCommentedLine(string line)
    {
        var t = line.Trim();
        return t.StartsWith('#') && t.EndsWith('#') && t.Length >= 2;
    }

    public override string UncommentLine(string line)
    {
        int ind = line.Length - line.TrimStart().Length;
        var body = line[ind..].TrimEnd();
        if (!(body.StartsWith('#') && body.EndsWith('#') && body.Length >= 2)) return line;
        body = body[1..^1];
        if (body.StartsWith(' ')) body = body[1..];
        if (body.EndsWith(' ')) body = body[..^1];
        return line[..ind] + body.Replace("№", "#");
    }
}
