namespace Studio.Ide.Language;

/// <summary>Simula 67 : mots-cles insensibles a la casse, commentaires « comment ... ; », « ! ... ; »
/// et commentaires apres « end » (jusqu'a ; end else when otherwise).</summary>
public sealed class SimulaDefinition : LanguageDefinition
{
    public override string Id => "simula";
    public override string DisplayName => "Simula 67";
    public override string DefaultExtension => ".sim";
    public override bool CaseSensitive => false;

    public static readonly string[] Keywords =
    {
        "activate", "after", "and", "array", "at", "before", "begin", "boolean", "character", "class",
        "comment", "delay", "do", "else", "end", "eq", "eqv", "external", "false", "for", "ge", "go",
        "goto", "gt", "hidden", "if", "imp", "in", "inner", "inspect", "integer", "is", "label", "le",
        "long", "lt", "name", "ne", "new", "none", "not", "notext", "or", "otherwise", "prior",
        "procedure", "protected", "qua", "reactivate", "real", "ref", "short", "step", "switch", "text",
        "then", "this", "to", "true", "until", "value", "virtual", "when", "while",
    };

    static readonly HashSet<string> KeywordSet = new(Keywords, StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> TypeSet = new(new[]
        { "boolean", "character", "integer", "real", "text", "short", "long", "ref", "label" }, StringComparer.OrdinalIgnoreCase);

    public static readonly string[] Builtins =
    {
        "OutText", "OutImage", "OutInt", "OutReal", "OutFix", "OutChar", "OutFrac", "OutBool",
        "InInt", "InReal", "InText", "InImage", "InChar", "InFrac", "LastItem", "SysIn", "SysOut",
        "Simulation", "SimSet", "Head", "Link", "Linkage", "Process", "Hold", "Passivate", "Wait",
        "Cancel", "Time", "Current", "Main", "Idle", "Terminated", "EvTime", "NextEv",
        "Into", "Out", "Follow", "Precede", "First", "Last", "Suc", "Pred", "Empty", "Cardinal", "Clear",
        "Blanks", "Copy", "Length", "Sqrt", "Sin", "Cos", "Tan", "ArcTan", "ArcSin", "ArcCos", "Exp", "Ln", "Log10",
        "Abs", "Sign", "Entier", "Mod", "Rem", "Max", "Min", "Random", "Uniform", "Normal", "Draw",
        "RandInt", "NegExp", "Poisson", "Erlang", "Discrete", "Histd", "Linear", "Histo",
        "Terminate_Program", "Detach", "Resume", "Call", "UpCase", "LowCase", "GetChar", "PutChar",
        "GetInt", "PutInt", "GetReal", "PutReal", "PutFix", "GetFrac", "PutFrac", "Strip", "Sub", "Start", "Pos", "SetPos", "More",
        "Image", "EndFile", "Open", "Close", "InFile", "OutFile", "DirectFile", "PrintFile", "File",
        "BreakOutImage", "OutRecord", "Eject", "Line", "Char", "Rank", "IsLetter", "IsDigit",
        "Digit", "Letter", "MaxInt", "MinInt", "MaxReal", "MinReal", "ClockTime", "CpuTime", "DateTime",
        "Spacing", "Lowten", "DecimalMark", "UpperBound", "LowerBound", "Sourceline",
    };

    static readonly HashSet<string> BuiltinSet = new(Builtins, StringComparer.OrdinalIgnoreCase);

    static readonly string[] CompletionList = Keywords.Concat(Builtins).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    public override IReadOnlyList<string> CompletionWords => CompletionList;

    public override string NewFileTemplate =>
        "begin\r\n" +
        "   comment Nouveau programme Simula 67;\r\n" +
        "   OutText(\"Bonjour, monde !\");\r\n" +
        "   OutImage\r\n" +
        "end\r\n";

    public override List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        int n = s.Length, i = 0;
        while (i < n)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (IsIdentStart(c))
            {
                int st = i;
                while (i < n && IsIdentPart(s[i])) i++;
                string word = s.Substring(st, i - st);
                string lw = word.ToLowerInvariant();
                if (lw == "comment")
                {
                    i = SkipTo(s, i, ';');
                    tokens.Add(new Token(TokenKind.Comment, st, i - st, ""));
                    continue;
                }
                if (KeywordSet.Contains(lw))
                {
                    tokens.Add(new Token(TypeSet.Contains(lw) ? TokenKind.Type : TokenKind.Keyword, st, i - st, lw));
                    if (lw == "end") i = ScanEndComment(s, i, tokens);
                    continue;
                }
                tokens.Add(new Token(BuiltinSet.Contains(lw) ? TokenKind.Builtin : TokenKind.Identifier, st, i - st, lw));
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1])))
            {
                int st = i;
                while (i < n && char.IsDigit(s[i])) i++;
                if (i < n && s[i] == '.' && i + 1 < n && char.IsDigit(s[i + 1])) { i++; while (i < n && char.IsDigit(s[i])) i++; }
                if (i < n && s[i] == '&')
                {
                    int j = i + 1;
                    if (j < n && s[j] == '&') j++;
                    if (j < n && (s[j] == '+' || s[j] == '-')) j++;
                    if (j < n && char.IsDigit(s[j])) { i = j; while (i < n && char.IsDigit(s[i])) i++; }
                }
                tokens.Add(new Token(TokenKind.Number, st, i - st, ""));
                continue;
            }
            switch (c)
            {
                case '"':
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
                case '\'':
                {
                    int st = i;
                    if (i + 2 < n && s[i + 2] == '\'' && s[i + 1] != '\n') { i += 3; tokens.Add(new Token(TokenKind.Char, st, 3, "")); continue; }
                    if (i + 1 < n && s[i + 1] == '!')
                    {
                        int j = i + 2;
                        while (j < n && char.IsDigit(s[j])) j++;
                        if (j + 1 < n && s[j] == '!' && s[j + 1] == '\'') { i = j + 2; tokens.Add(new Token(TokenKind.Char, st, i - st, "")); continue; }
                    }
                    i++;
                    tokens.Add(new Token(TokenKind.Punctuation, st, 1, "'"));
                    continue;
                }
                case '!':
                {
                    int st = i;
                    i = SkipTo(s, i + 1, ';');
                    tokens.Add(new Token(TokenKind.Comment, st, i - st, ""));
                    continue;
                }
            }
            {
                int st = i;
                string op = MatchOperator(s, i);
                i += op.Length;
                var kind = op is "(" or ")" or "[" or "]" or "," or ";" or ":" or "." ? TokenKind.Punctuation : TokenKind.Operator;
                tokens.Add(new Token(kind, st, op.Length, op));
            }
        }
        return tokens;
    }

    static readonly string[] Ops = { "=/=", ":=", ":-", "**", "<=", ">=", "<>", "==", "//" };

    static string MatchOperator(string s, int i)
    {
        foreach (var op in Ops)
            if (string.CompareOrdinal(s, i, op, 0, op.Length) == 0) return op;
        return s[i].ToString();
    }

    /// <summary>Avance jusqu'apres le prochain caractere c (inclus) ou la fin du texte.</summary>
    static int SkipTo(string s, int i, char c)
    {
        int k = s.IndexOf(c, i);
        return k < 0 ? s.Length : k + 1;
    }

    static readonly string[] EndCommentStops = { "end", "else", "when", "otherwise" };

    /// <summary>Apres « end », tout ce qui precede ; end else when otherwise est un commentaire.</summary>
    static int ScanEndComment(string s, int i, List<Token> tokens)
    {
        int n = s.Length, j = i;
        int commentStart = -1, commentEnd = -1;
        while (j < n)
        {
            char c = s[j];
            if (c == ';') break;
            if (IsIdentStart(c))
            {
                int st = j;
                while (j < n && IsIdentPart(s[j])) j++;
                var w = s.Substring(st, j - st);
                if (EndCommentStops.Any(x => string.Equals(x, w, StringComparison.OrdinalIgnoreCase))) { j = st; break; }
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

    protected override void AnalyzeStructure(CodeModel m)
    {
        var toks = m.Tokens;
        var stack = new Stack<(int Index, int HeaderStart, string Title)>();
        var parenStack = new Stack<int>();
        int pendingHeader = -1;     // offset de la ligne d'en-tete procedure/class en attente de son begin
        string pendingTitle = "";
        bool inVirtual = false, skipNextDecl = false;
        HashSet<string>? pendingParams = null;
        int depth = 0;

        for (int k = 0; k < toks.Count; k++)
        {
            var t = toks[k];
            if (t.Kind == TokenKind.Punctuation)
            {
                if (t.Norm is "(" or "[") parenStack.Push(k);
                else if ((t.Norm is ")" or "]") && parenStack.Count > 0) AddPair(m, parenStack.Pop(), k);
                continue;
            }
            if (t.Kind != TokenKind.Keyword && t.Kind != TokenKind.Type) continue;
            switch (t.Norm)
            {
                case "virtual":
                    inVirtual = true;
                    break;
                case "external":
                    skipNextDecl = true;
                    break;
                case "begin":
                    stack.Push((k, pendingHeader >= 0 ? pendingHeader : t.Start, pendingHeader >= 0 ? pendingTitle : "begin … end"));
                    pendingHeader = -1; pendingParams = null; inVirtual = false;
                    depth++;
                    break;
                case "end":
                    if (stack.Count > 0)
                    {
                        var (open, header, title) = stack.Pop();
                        AddPair(m, open, k);
                        AddFold(m, header, t.End, title);
                        depth = Math.Max(0, depth - 1);
                    }
                    pendingHeader = -1;
                    break;
                case "procedure":
                case "class":
                {
                    bool isClass = t.Norm == "class";
                    int nameIdx = k + 1;
                    if (nameIdx >= toks.Count || toks[nameIdx].Kind is not (TokenKind.Identifier or TokenKind.Builtin)) break;
                    string name = m.TokenText(toks[nameIdx]);
                    if (inVirtual || skipNextDecl) { skipNextDecl = false; break; }
                    if (!isClass && pendingParams != null && pendingParams.Contains(name)) break; // specification de parametre
                    if (!isClass && k > 0 && toks[k - 1].Norm == "(") break;
                    string detail = "";
                    if (isClass && k > 0 && toks[k - 1].Kind is TokenKind.Identifier or TokenKind.Builtin)
                        detail = m.TokenText(toks[k - 1]) + " class";
                    else if (!isClass && k > 0 && toks[k - 1].Kind == TokenKind.Type)
                        detail = m.TokenText(toks[k - 1]) + " procedure";
                    // Parametres formels
                    var pars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (nameIdx + 1 < toks.Count && toks[nameIdx + 1].Norm == "(")
                        for (int j = nameIdx + 2; j < toks.Count && toks[j].Norm != ")"; j++)
                            if (toks[j].Kind is TokenKind.Identifier or TokenKind.Builtin) pars.Add(m.TokenText(toks[j]));
                    m.Outline.Add(new OutlineItem(isClass ? OutlineKind.Class : OutlineKind.Procedure, name,
                        detail.Length > 0 ? detail : isClass ? "class" : "procedure", toks[k].Start, depth));
                    if (pendingHeader < 0)
                    {
                        pendingHeader = LineStart(m.Text, (k > 0 && (toks[k - 1].Kind == TokenKind.Type || (isClass && toks[k - 1].Kind == TokenKind.Identifier))) ? toks[k - 1].Start : t.Start);
                        pendingTitle = (isClass ? "class " : "procedure ") + name + " …";
                        pendingParams = pars;
                    }
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

    // Commentaire de ligne : « ! texte » (un commentaire « ! » se termine au premier « ; »).
    // Chaque « ; » interieur est suivi de la marque « !~ » qui ouvre un nouveau commentaire, et
    // si la ligne ne se termine pas par « ; », on ajoute « !; » pour fermer le commentaire.
    // Les deux marques sont retirees au decommentaire.
    const string CloseMark = " !;";
    const string InnerMark = ";!~";

    public override string CommentLine(string line)
    {
        int ind = line.Length - line.TrimStart().Length;
        var body = line[ind..].TrimEnd();
        if (body.Length == 0) return line;
        bool endsWithSemicolon = body.EndsWith(';');
        var inner = endsWithSemicolon ? body[..^1] : body;
        inner = inner.Replace(";", InnerMark);
        return line[..ind] + "! " + inner + (endsWithSemicolon ? ";" : CloseMark);
    }

    public override bool IsCommentedLine(string line) => line.TrimStart().StartsWith('!');

    public override string UncommentLine(string line)
    {
        int ind = line.Length - line.TrimStart().Length;
        var body = line[ind..].TrimEnd();
        if (!body.StartsWith('!')) return line;
        body = body[1..];
        if (body.StartsWith(' ')) body = body[1..];
        if (body.EndsWith(CloseMark, StringComparison.Ordinal)) body = body[..^CloseMark.Length];
        else if (body.EndsWith("!;", StringComparison.Ordinal)) body = body[..^2];
        return line[..ind] + body.Replace(InnerMark, ";");
    }
}
