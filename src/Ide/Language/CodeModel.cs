// Lightweight lexical analysis for the editor: highlighting, folding, block matching,
// file structure (outline), identifiers for auto-completion, indentation.
// No WPF dependency (file linked into tests/IdeTests).

namespace Studio.Ide.Language;

public enum TokenKind
{
    Keyword,      // reserved word
    Type,         // type word (integer, INT, REF...)
    BoldWord,     // Algol 68: non-reserved bold word (mode or operator indicant)
    Builtin,      // standard procedure (OutText, print...)
    Identifier,
    Number,
    String,
    Char,
    Comment,
    Pragma,
    Operator,
    Punctuation,
}

/// <summary>Token. Norm = normalized form (lower case for Simula, upper case without quotes for Algol 68 bold words).</summary>
public readonly record struct Token(TokenKind Kind, int Start, int Length, string Norm)
{
    public int End => Start + Length;
}

public sealed record FoldRegion(int StartOffset, int EndOffset, string Title);

public enum OutlineKind { Procedure, Class, Operator, Mode, Label }

public sealed record OutlineItem(OutlineKind Kind, string Name, string Detail, int Offset, int Depth);

/// <summary>Opener/closer pair (token indices): begin/end, ( ), IF/FI...</summary>
public readonly record struct TokenPair(int Open, int Close);

public sealed class CodeModel
{
    public CodeModel(string text, IReadOnlyList<Token> tokens)
    {
        Text = text;
        Tokens = tokens;
    }

    public string Text { get; }
    public IReadOnlyList<Token> Tokens { get; }
    public List<FoldRegion> Folds { get; } = new();
    public List<TokenPair> Pairs { get; } = new();
    public List<OutlineItem> Outline { get; } = new();
    public List<string> Identifiers { get; } = new();
    /// <summary>For each matched token: index of the matching token.</summary>
    public Dictionary<int, int> PairOf { get; } = new();

    public string TokenText(Token t) => Text.Substring(t.Start, t.Length);

    /// <summary>Index of the first token whose end is > offset (binary search).</summary>
    public int FirstTokenEndingAfter(int offset)
    {
        int lo = 0, hi = Tokens.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (Tokens[mid].End <= offset) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>Token containing the offset (or ending exactly at the offset), -1 otherwise.</summary>
    public int TokenAt(int offset, bool allowEndTouch = true)
    {
        int i = FirstTokenEndingAfter(offset);
        if (i < Tokens.Count && Tokens[i].Start <= offset) return i;
        if (allowEndTouch && i > 0 && Tokens[i - 1].End == offset) return i - 1;
        return -1;
    }
}

/// <summary>Language description for the editor.</summary>
public abstract class LanguageDefinition
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string DefaultExtension { get; }
    /// <summary>All the file extensions of the language (lower case, with the dot).</summary>
    public virtual IReadOnlyList<string> Extensions => new[] { DefaultExtension };
    public abstract bool CaseSensitive { get; }
    /// <summary>Words offered by auto-completion (keywords, standard procedures).</summary>
    public abstract IReadOnlyList<string> CompletionWords { get; }
    public abstract string NewFileTemplate { get; }
    public abstract List<Token> Tokenize(string text);
    protected abstract void AnalyzeStructure(CodeModel model);

    public string IndentUnit { get; set; } = "   ";

    public CodeModel Analyze(string text)
    {
        var model = new CodeModel(text, Tokenize(text));
        try { AnalyzeStructure(model); }
        catch { /* analysis must never crash the editor */ }
        return model;
    }

    // ---- Indentation ----

    /// <summary>Opening (+1) / closing (-1) words: balance of a line.</summary>
    protected abstract int BlockDelta(Token t);
    /// <summary>"Middle" words (THEN, ELSE...): aligned with the opener, content indented.</summary>
    protected virtual bool IsMiddleWord(Token t) => false;

    public int NetOpeners(string line)
    {
        int n = 0;
        foreach (var t in Tokenize(line)) n += BlockDelta(t);
        return n;
    }

    public bool StartsWithCloserOrMiddle(string line)
    {
        var toks = Tokenize(line);
        if (toks.Count == 0) return false;
        return BlockDelta(toks[0]) < 0 || IsMiddleWord(toks[0]);
    }

    public bool StartsWithMiddle(string line)
    {
        var toks = Tokenize(line);
        return toks.Count > 0 && IsMiddleWord(toks[0]);
    }

    /// <summary>Desired indentation for a line, based on the previous non-empty line.</summary>
    public string ComputeIndent(string previousLine, string currentLine)
    {
        string prevIndent = previousLine[..(previousLine.Length - previousLine.TrimStart().Length)];
        int level = 0;
        var prevToks = Tokenize(previousLine);
        int delta = 0;
        // Closers at the start of the previous line have already been outdented.
        for (int i = 0; i < prevToks.Count; i++)
        {
            int d = BlockDelta(prevToks[i]);
            if (i == 0 && d < 0) continue;
            delta += d;
        }
        if (delta > 0) level++;
        else if (delta < 0) level--;
        if (prevToks.Count > 0 && IsMiddleWord(prevToks[0])) level++;
        if (StartsWithCloserOrMiddle(currentLine)) level--;
        var indent = prevIndent;
        if (level > 0) indent += IndentUnit;
        else if (level < 0) indent = RemoveIndentUnit(indent);
        return indent;
    }

    string RemoveIndentUnit(string indent)
    {
        if (indent.EndsWith(IndentUnit, StringComparison.Ordinal)) return indent[..^IndentUnit.Length];
        if (indent.EndsWith('\t')) return indent[..^1];
        int n = Math.Min(IndentUnit.Length, indent.Length);
        return indent[..^n];
    }

    // ---- Line comments ----

    public abstract string CommentLine(string line);
    /// <summary>Comments a line of the given document (for languages whose comment syntax depends on the whole text).</summary>
    public virtual string CommentLine(string line, string documentText) => CommentLine(line);
    public abstract bool IsCommentedLine(string line);
    public abstract string UncommentLine(string line);

    // ---- Shared helpers ----

    protected static void AddFold(CodeModel m, int openEnd, int closeEnd, string title)
    {
        // Folding starts at the end of the opener's line: the header stays visible.
        int lineEnd = m.Text.IndexOf('\n', openEnd);
        if (lineEnd < 0) return;
        if (lineEnd > 0 && m.Text[lineEnd - 1] == '\r') lineEnd--;
        if (closeEnd <= lineEnd + 1) return; // single line
        if (m.Text.IndexOf('\n', lineEnd + 1) < 0) return;
        m.Folds.Add(new FoldRegion(lineEnd, closeEnd, title));
    }

    protected static void CollectIdentifiers(CodeModel m, StringComparer comparer)
    {
        var seen = new HashSet<string>(comparer);
        foreach (var t in m.Tokens)
            if (t.Kind == TokenKind.Identifier && t.Length > 1)
            {
                var s = m.TokenText(t);
                if (seen.Add(s)) m.Identifiers.Add(s);
            }
    }

    protected static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
    protected static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';
}

public static class LanguageDefinitions
{
    public static readonly SimulaDefinition Simula = new();
    public static readonly Algol68Definition Algol68 = new();
    public static readonly Algol60Definition Algol60 = new();
    public static readonly SimulaIDefinition SimulaI = new();
    public static readonly EchoDefinition Echo = new();

    public static IReadOnlyList<LanguageDefinition> All { get; } = new LanguageDefinition[] { Simula, Algol68, Algol60, SimulaI, Echo };

    public static LanguageDefinition? ById(string id) =>
        All.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));

    public static LanguageDefinition? ByExtension(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return All.FirstOrDefault(l => l.Extensions.Contains(ext));
    }
}
