namespace Studio.Ide.Language;

/// <summary>Langage de test « echo » (active par --test-language) : une instruction par ligne.</summary>
public sealed class EchoDefinition : LanguageDefinition
{
    public override string Id => "echo";
    public override string DisplayName => "Echo (langage de test)";
    public override string DefaultExtension => ".echo";
    public override bool CaseSensitive => false;

    static readonly string[] Words = { "print", "read", "set", "add", "list", "proc", "call", "loop", "end", "include", "sleep", "error", "exit" };
    static readonly HashSet<string> WordSet = new(Words, StringComparer.OrdinalIgnoreCase);
    public override IReadOnlyList<string> CompletionWords => Words;

    public override string NewFileTemplate =>
        "# Programme de test echo\r\nprint Bonjour !\r\nread nom\r\nprint Salut {nom}\r\n";

    public override List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        int i = 0, n = s.Length;
        bool lineStart = true;
        while (i < n)
        {
            char c = s[i];
            if (c == '\n') { lineStart = true; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#' && lineStart)
            {
                int st = i;
                while (i < n && s[i] != '\n') i++;
                tokens.Add(new Token(TokenKind.Comment, st, i - st, ""));
                continue;
            }
            if (IsIdentStart(c))
            {
                int st = i;
                while (i < n && IsIdentPart(s[i])) i++;
                var w = s.Substring(st, i - st).ToLowerInvariant();
                tokens.Add(new Token(lineStart && WordSet.Contains(w) ? TokenKind.Keyword : TokenKind.Identifier, st, i - st, w));
                lineStart = false;
                continue;
            }
            if (char.IsDigit(c))
            {
                int st = i;
                while (i < n && char.IsDigit(s[i])) i++;
                tokens.Add(new Token(TokenKind.Number, st, i - st, ""));
                lineStart = false;
                continue;
            }
            if (c == '{')
            {
                int st = i;
                int k = s.IndexOf('}', i);
                int nl = s.IndexOf('\n', i);
                i = k < 0 || (nl >= 0 && nl < k) ? i + 1 : k + 1;
                tokens.Add(new Token(TokenKind.String, st, i - st, ""));
                lineStart = false;
                continue;
            }
            tokens.Add(new Token(TokenKind.Operator, i, 1, c.ToString()));
            i++;
            lineStart = false;
        }
        return tokens;
    }

    protected override int BlockDelta(Token t) =>
        t.Kind == TokenKind.Keyword ? t.Norm switch { "proc" or "loop" => 1, "end" => -1, _ => 0 } : 0;

    protected override void AnalyzeStructure(CodeModel m)
    {
        var stack = new Stack<int>();
        for (int k = 0; k < m.Tokens.Count; k++)
        {
            var t = m.Tokens[k];
            if (t.Kind != TokenKind.Keyword) continue;
            if (t.Norm is "proc" or "loop")
            {
                stack.Push(k);
                if (t.Norm == "proc" && k + 1 < m.Tokens.Count)
                    m.Outline.Add(new OutlineItem(OutlineKind.Procedure, m.TokenText(m.Tokens[k + 1]), "proc", t.Start, stack.Count - 1));
            }
            else if (t.Norm == "end" && stack.Count > 0)
            {
                int o = stack.Pop();
                m.Pairs.Add(new TokenPair(o, k));
                m.PairOf[o] = k; m.PairOf[k] = o;
                AddFold(m, m.Tokens[o].Start, t.End, m.Tokens[o].Norm + " …");
            }
        }
        CollectIdentifiers(m, StringComparer.OrdinalIgnoreCase);
    }

    public override string CommentLine(string line)
    {
        int ind = line.Length - line.TrimStart().Length;
        return line.Trim().Length == 0 ? line : line[..ind] + "# " + line[ind..];
    }

    public override bool IsCommentedLine(string line) => line.TrimStart().StartsWith('#');

    public override string UncommentLine(string line)
    {
        int ind = line.Length - line.TrimStart().Length;
        var body = line[ind..];
        if (!body.StartsWith('#')) return line;
        body = body[1..];
        if (body.StartsWith(' ')) body = body[1..];
        return line[..ind] + body;
    }
}
