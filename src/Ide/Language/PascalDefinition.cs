namespace Studio.Ide.Language;

/// <summary>Pascal (Revised Report, Pascal 6000): case-insensitive reserved words, comments { … } and (* … *)
/// (synonym brackets, several lines), strings '…' with doubled quotes, alternative symbols (. .) for [ ] and @ for ^.
/// Folding of begin/end, record/end, case/end (statement) and repeat/until; outline of the program,
/// its procedures, functions and type definitions.</summary>
public sealed class PascalDefinition : LanguageDefinition
{
    public override string Id => "pascal";
    public override string DisplayName => "Pascal";
    public override string DefaultExtension => ".pas";
    public override IReadOnlyList<string> Extensions { get; } = new[] { ".pas", ".p" };
    public override bool CaseSensitive => false;

    public static readonly string[] Keywords =
    {
        "and", "array", "begin", "case", "const", "div", "do", "downto", "else", "end", "file", "for",
        "function", "goto", "if", "in", "label", "mod", "nil", "not", "of", "or", "packed", "procedure",
        "program", "record", "repeat", "set", "then", "to", "type", "until", "var", "while", "with",
        "forward",
    };

    public static readonly string[] Types = { "integer", "real", "boolean", "char", "text", "alfa" };

    public static readonly string[] Builtins =
    {
        "abs", "sqr", "sin", "cos", "exp", "ln", "sqrt", "arctan", "odd", "trunc", "round", "ord", "chr",
        "succ", "pred", "eof", "eoln", "read", "readln", "write", "writeln", "get", "put", "reset", "rewrite",
        "page", "new", "dispose", "pack", "unpack", "maxint", "true", "false", "input", "output",
    };

    static readonly HashSet<string> KeywordSet = new(Keywords, StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> TypeSet = new(Types, StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> BuiltinSet = new(Builtins, StringComparer.OrdinalIgnoreCase);

    static readonly string[] CompletionList =
        Keywords.Concat(Types).Concat(Builtins).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    public override IReadOnlyList<string> CompletionWords => CompletionList;

    public override string NewFileTemplate =>
        "program hello(input, output);\r\n" +
        "{ New Pascal program }\r\n" +
        "begin\r\n" +
        "   writeln('Hello, world!')\r\n" +
        "end.\r\n";

    public override List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        int n = s.Length, i = 0;
        while (i < n)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c) || c == '﻿') { i++; continue; }
            if (c == '{' || (c == '(' && i + 1 < n && s[i + 1] == '*'))
            {
                // (* and *) are synonyms for { and } (as in the compiler): the first } or *) ends the comment
                int st = i;
                int kb = s.IndexOf('}', i + 1);
                int ks = s.IndexOf("*)", i + (c == '{' ? 1 : 2), StringComparison.Ordinal);
                if (kb < 0 && ks < 0) i = n;
                else if (ks < 0 || (kb >= 0 && kb < ks)) i = kb + 1;
                else i = ks + 2;
                tokens.Add(new Token(TokenKind.Comment, st, i - st, ""));
                continue;
            }
            if (IsIdentStart(c))
            {
                int st = i;
                while (i < n && IsIdentPart(s[i])) i++;
                string lw = s.Substring(st, i - st).ToLowerInvariant();
                var kind = KeywordSet.Contains(lw) ? TokenKind.Keyword
                         : TypeSet.Contains(lw) ? TokenKind.Type
                         : BuiltinSet.Contains(lw) ? TokenKind.Builtin
                         : TokenKind.Identifier;
                tokens.Add(new Token(kind, st, i - st, lw));
                continue;
            }
            if (char.IsDigit(c))
            {
                int st = i;
                while (i < n && char.IsDigit(s[i])) i++;
                // "1..10": the dots belong to the subrange symbol
                if (i + 1 < n && s[i] == '.' && char.IsDigit(s[i + 1]))
                {
                    i++;
                    while (i < n && char.IsDigit(s[i])) i++;
                }
                if (i < n && (s[i] == 'e' || s[i] == 'E'))
                {
                    int j = i + 1;
                    if (j < n && (s[j] == '+' || s[j] == '-')) j++;
                    if (j < n && char.IsDigit(s[j])) { i = j; while (i < n && char.IsDigit(s[i])) i++; }
                }
                tokens.Add(new Token(TokenKind.Number, st, i - st, ""));
                continue;
            }
            if (c == '\'')
            {
                int st = i++;
                bool single = true;
                int chars = 0;
                while (i < n)
                {
                    if (s[i] == '\'')
                    {
                        if (i + 1 < n && s[i + 1] == '\'') { i += 2; chars++; continue; }
                        i++;
                        break;
                    }
                    if (s[i] == '\n' || s[i] == '\r') break;
                    i++; chars++;
                }
                single = chars == 1;
                tokens.Add(new Token(single ? TokenKind.Char : TokenKind.String, st, i - st, ""));
                continue;
            }
            {
                int st = i;
                string op = MatchOperator(s, i, out string norm);
                i += op.Length;
                var kind = norm is "(" or ")" or "[" or "]" or "," or ";" or ":" or "." ? TokenKind.Punctuation : TokenKind.Operator;
                tokens.Add(new Token(kind, st, op.Length, norm));
            }
        }
        return tokens;
    }

    static readonly string[] Ops = { ":=", "<=", ">=", "<>", "..", "(.", ".)" };

    static string MatchOperator(string s, int i, out string norm)
    {
        foreach (var op in Ops)
            if (string.CompareOrdinal(s, i, op, 0, op.Length) == 0)
            {
                norm = op switch { "(." => "[", ".)" => "]", _ => op };
                return op;
            }
        norm = s[i] switch { '@' or '↑' => "^", _ => s[i].ToString() };
        return s[i].ToString();
    }

    protected override int BlockDelta(Token t) =>
        t.Kind == TokenKind.Keyword ? t.Norm switch { "begin" or "record" or "repeat" or "case" => 1, "end" or "until" => -1, _ => 0 } : 0;

    sealed class Open
    {
        public int Index;
        public string Word = "";
        public int Header;
        public string Title = "";
        public bool IsBody;
    }

    sealed class Pending
    {
        public int Header;
        public string Title = "";
        public int StackCount;
    }

    protected override void AnalyzeStructure(CodeModel m)
    {
        var toks = m.Tokens;
        var stack = new List<Open>();
        var parens = new Stack<int>();
        var pending = new List<Pending>();
        int bodies = 0;          // open routine bodies (program included)
        bool inTypePart = false;
        int typePartStack = -1;

        int Depth() => bodies + pending.Count - 1;

        for (int k = 0; k < toks.Count; k++)
        {
            var t = toks[k];
            if (t.Kind == TokenKind.Comment) continue;
            if (t.Kind == TokenKind.Punctuation)
            {
                if (t.Norm is "(" or "[") parens.Push(k);
                else if (t.Norm is ")" or "]") { if (parens.Count > 0) AddPair(m, parens.Pop(), k); }
                continue;
            }
            // type definitions: "name =" at the level of the type part
            if (inTypePart && t.Kind == TokenKind.Identifier && parens.Count == 0 && stack.Count == typePartStack
                && k + 1 < toks.Count && toks[k + 1].Norm == "=" && (k == 0 || toks[k - 1].Norm is ";" or "type"))
            {
                m.Outline.Add(new OutlineItem(OutlineKind.Mode, m.TokenText(t), "type", t.Start, Math.Max(0, Depth() + 1)));
                continue;
            }
            if (t.Kind != TokenKind.Keyword) continue;
            switch (t.Norm)
            {
                case "type":
                    if (parens.Count == 0) { inTypePart = true; typePartStack = stack.Count; }
                    break;
                case "var": case "const": case "label":
                    if (parens.Count == 0) inTypePart = false;
                    break;
                case "program":
                case "procedure":
                case "function":
                    {
                        if (parens.Count > 0) break;   // procedural parameter
                        inTypePart = false;
                        if (k + 1 >= toks.Count || toks[k + 1].Kind is not (TokenKind.Identifier or TokenKind.Builtin)) break;
                        string name = m.TokenText(toks[k + 1]);
                        m.Outline.Add(new OutlineItem(t.Norm == "program" ? OutlineKind.Class : OutlineKind.Procedure, name, t.Norm, t.Start, Math.Max(0, Depth() + 1)));
                        pending.Add(new Pending { Header = LineStart(m.Text, t.Start), Title = t.Norm + " " + name + " …", StackCount = stack.Count });
                        break;
                    }
                case "forward":
                    if (pending.Count > 0 && pending[^1].StackCount == stack.Count) pending.RemoveAt(pending.Count - 1);
                    break;
                case "begin":
                    {
                        inTypePart = false;
                        var o = new Open { Index = k, Word = "begin", Header = t.Start, Title = "begin … end" };
                        if (pending.Count > 0 && pending[^1].StackCount == stack.Count)
                        {
                            var pd = pending[^1];
                            pending.RemoveAt(pending.Count - 1);
                            o.Header = pd.Header; o.Title = pd.Title; o.IsBody = true;
                            bodies++;
                        }
                        stack.Add(o);
                        break;
                    }
                case "record":
                    stack.Add(new Open { Index = k, Word = "record", Header = t.Start, Title = "record … end" });
                    break;
                case "case":
                    // the variant part of a record has no "end" of its own
                    if (stack.Count > 0 && stack[^1].Word == "record") break;
                    stack.Add(new Open { Index = k, Word = "case", Header = t.Start, Title = "case … end" });
                    break;
                case "repeat":
                    stack.Add(new Open { Index = k, Word = "repeat", Header = t.Start, Title = "repeat … until" });
                    break;
                case "end":
                    {
                        int j = stack.FindLastIndex(o => o.Word != "repeat");
                        if (j < 0) break;
                        var o = stack[j];
                        stack.RemoveRange(j, stack.Count - j);
                        AddPair(m, o.Index, k);
                        AddFold(m, o.Header, t.End, o.Title);
                        if (o.IsBody) bodies = Math.Max(0, bodies - 1);
                        break;
                    }
                case "until":
                    {
                        int j = stack.FindLastIndex(o => o.Word == "repeat");
                        if (j < 0) break;
                        var o = stack[j];
                        stack.RemoveRange(j, stack.Count - j);
                        AddPair(m, o.Index, k);
                        AddFold(m, o.Header, t.End, o.Title);
                        break;
                    }
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

    // Line comments: { text }. The two kinds of comment brackets are synonyms, so a closing } or *) of the
    // line is altered (to ")" and "* )") to keep the whole line inside the comment.

    public override string CommentLine(string line)
    {
        int ind = line.Length - line.TrimStart().Length;
        var body = line[ind..].TrimEnd();
        if (body.Length == 0) return line;
        return line[..ind] + "{ " + body.Replace("*)", "* )").Replace("}", ")") + " }";
    }

    public override bool IsCommentedLine(string line)
    {
        var t = line.Trim();
        return (t.StartsWith('{') && t.EndsWith('}')) || (t.StartsWith("(*") && t.EndsWith("*)") && t.Length >= 4);
    }

    public override string UncommentLine(string line)
    {
        int ind = line.Length - line.TrimStart().Length;
        var body = line[ind..].TrimEnd();
        if (body.StartsWith('{') && body.EndsWith('}')) body = body[1..^1];
        else if (body.StartsWith("(*") && body.EndsWith("*)") && body.Length >= 4) body = body[2..^2];
        else return line;
        if (body.StartsWith(' ')) body = body[1..];
        if (body.EndsWith(' ')) body = body[..^1];
        return line[..ind] + body;
    }
}
