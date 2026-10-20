// Analyse lexicale legere pour l'editeur : coloration, repliage, appariement des blocs,
// structure du fichier (outline), identifiants pour l'auto-completion, indentation.
// Aucune dependance WPF (fichier lie dans tests/IdeTests).

namespace Studio.Ide.Language;

public enum TokenKind
{
    Keyword,      // mot reserve
    Type,         // mot de type (integer, INT, REF...)
    BoldWord,     // Algol 68 : mot gras non reserve (indicant de mode ou d'operateur)
    Builtin,      // procedure standard (OutText, print...)
    Identifier,
    Number,
    String,
    Char,
    Comment,
    Pragma,
    Operator,
    Punctuation,
}

/// <summary>Jeton. Norm = forme normalisee (minuscules pour Simula, majuscules sans apostrophes pour les mots gras Algol 68).</summary>
public readonly record struct Token(TokenKind Kind, int Start, int Length, string Norm)
{
    public int End => Start + Length;
}

public sealed record FoldRegion(int StartOffset, int EndOffset, string Title);

public enum OutlineKind { Procedure, Class, Operator, Mode, Label }

public sealed record OutlineItem(OutlineKind Kind, string Name, string Detail, int Offset, int Depth);

/// <summary>Paire ouvrant/fermant (indices de jetons) : begin/end, ( ), IF/FI...</summary>
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
    /// <summary>Pour chaque jeton apparie : indice du jeton correspondant.</summary>
    public Dictionary<int, int> PairOf { get; } = new();

    public string TokenText(Token t) => Text.Substring(t.Start, t.Length);

    /// <summary>Indice du premier jeton dont la fin est > offset (recherche dichotomique).</summary>
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

    /// <summary>Jeton contenant l'offset (ou qui se termine juste a l'offset), -1 sinon.</summary>
    public int TokenAt(int offset, bool allowEndTouch = true)
    {
        int i = FirstTokenEndingAfter(offset);
        if (i < Tokens.Count && Tokens[i].Start <= offset) return i;
        if (allowEndTouch && i > 0 && Tokens[i - 1].End == offset) return i - 1;
        return -1;
    }
}

/// <summary>Description d'un langage pour l'editeur.</summary>
public abstract class LanguageDefinition
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string DefaultExtension { get; }
    public abstract bool CaseSensitive { get; }
    /// <summary>Mots proposes par l'auto-completion (mots-cles, procedures standard).</summary>
    public abstract IReadOnlyList<string> CompletionWords { get; }
    public abstract string NewFileTemplate { get; }
    public abstract List<Token> Tokenize(string text);
    protected abstract void AnalyzeStructure(CodeModel model);

    public string IndentUnit { get; set; } = "   ";

    public CodeModel Analyze(string text)
    {
        var model = new CodeModel(text, Tokenize(text));
        try { AnalyzeStructure(model); }
        catch { /* l'analyse ne doit jamais faire tomber l'editeur */ }
        return model;
    }

    // ---- Indentation ----

    /// <summary>Mots ouvrants (+1) / fermants (-1) : solde d'une ligne.</summary>
    protected abstract int BlockDelta(Token t);
    /// <summary>Mots "intermediaires" (THEN, ELSE...) : alignes sur l'ouvrant, contenu indente.</summary>
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

    /// <summary>Indentation souhaitee pour une ligne, a partir de la ligne precedente non vide.</summary>
    public string ComputeIndent(string previousLine, string currentLine)
    {
        string prevIndent = previousLine[..(previousLine.Length - previousLine.TrimStart().Length)];
        int level = 0;
        var prevToks = Tokenize(previousLine);
        int delta = 0;
        // Les fermants en tete de la ligne precedente ont deja ete desindentes.
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

    // ---- Commentaires de ligne ----

    public abstract string CommentLine(string line);
    public abstract bool IsCommentedLine(string line);
    public abstract string UncommentLine(string line);

    // ---- Outils communs ----

    protected static void AddFold(CodeModel m, int openEnd, int closeEnd, string title)
    {
        // Le repliage commence a la fin de la ligne de l'ouvrant : l'en-tete reste visible.
        int lineEnd = m.Text.IndexOf('\n', openEnd);
        if (lineEnd < 0) return;
        if (lineEnd > 0 && m.Text[lineEnd - 1] == '\r') lineEnd--;
        if (closeEnd <= lineEnd + 1) return; // une seule ligne
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
    public static readonly EchoDefinition Echo = new();

    public static IReadOnlyList<LanguageDefinition> All { get; } = new LanguageDefinition[] { Simula, Algol68, Echo };

    public static LanguageDefinition? ById(string id) =>
        All.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));

    public static LanguageDefinition? ByExtension(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return All.FirstOrDefault(l => l.DefaultExtension == ext);
    }
}
