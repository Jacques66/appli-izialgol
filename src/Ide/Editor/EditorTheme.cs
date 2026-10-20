using System.Windows;
using System.Windows.Media;
using Studio.Ide.Language;

namespace Studio.Ide.Editor;

public sealed record TokenStyle(Brush Brush, bool Bold = false, bool Italic = false);

/// <summary>Editor colors (syntax highlighting, margins, highlights) for one theme.</summary>
public sealed class EditorTheme
{
    public required bool IsDark { get; init; }
    public required Brush Background { get; init; }
    public required Brush Foreground { get; init; }
    public required Brush LineNumbers { get; init; }
    public required Brush CurrentLine { get; init; }
    public required Brush Selection { get; init; }
    public required Brush SelectionText { get; init; }
    public required Brush BracketMatch { get; init; }
    public required Pen BracketMatchBorder { get; init; }
    public required Brush ExecutionLine { get; init; }
    public required Brush CallerLine { get; init; }
    public required Brush BreakpointFill { get; init; }
    public required Brush BreakpointLine { get; init; }
    public required Brush ArrowFill { get; init; }
    public required Brush ErrorSquiggle { get; init; }
    public required Brush WarningSquiggle { get; init; }
    public required Brush InfoSquiggle { get; init; }
    public required Brush SearchHighlight { get; init; }
    public required Brush ConsoleInput { get; init; }
    public required Brush ConsoleInfo { get; init; }
    public required Brush ConsoleError { get; init; }
    public required Brush FoldingMarker { get; init; }
    public required Brush FoldingBackground { get; init; }
    public required Dictionary<TokenKind, TokenStyle> Simula { get; init; }
    public required Dictionary<TokenKind, TokenStyle> Algol { get; init; }

    public TokenStyle? StyleFor(LanguageDefinition lang, TokenKind kind)
    {
        var map = lang is Algol68Definition ? Algol : Simula;
        return map.TryGetValue(kind, out var s) ? s : null;
    }

    static SolidColorBrush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    static Pen P(string hex, double w)
    {
        var p = new Pen(B(hex), w);
        p.Freeze();
        return p;
    }

    public static EditorTheme Light { get; } = new()
    {
        IsDark = false,
        Background = B("#FFFFFF"),
        Foreground = B("#1E1E1E"),
        LineNumbers = B("#2B91AF"),
        CurrentLine = B("#F2F6FC"),
        Selection = B("#ADD6FF"),
        SelectionText = B("#000000"),
        BracketMatch = B("#3300A0E0"),
        BracketMatchBorder = P("#7F7F9FBF", 1),
        ExecutionLine = B("#FFF3B0"),
        CallerLine = B("#D8F0D8"),
        BreakpointFill = B("#D32F2F"),
        BreakpointLine = B("#FBE3E3"),
        ArrowFill = B("#F5C518"),
        ErrorSquiggle = B("#E51400"),
        WarningSquiggle = B("#C89A00"),
        InfoSquiggle = B("#1E88E5"),
        SearchHighlight = B("#66F6D860"),
        ConsoleInput = B("#0B6E2A"),
        ConsoleInfo = B("#6B7280"),
        ConsoleError = B("#C62828"),
        FoldingMarker = B("#8A8F98"),
        FoldingBackground = B("#FFFFFF"),
        Simula = new()
        {
            [TokenKind.Keyword] = new(B("#0000C8")),
            [TokenKind.Type] = new(B("#2B6CB0")),
            [TokenKind.Builtin] = new(B("#795E26")),
            [TokenKind.Number] = new(B("#098658")),
            [TokenKind.String] = new(B("#A31515")),
            [TokenKind.Char] = new(B("#A31515")),
            [TokenKind.Comment] = new(B("#2E8B3A"), Italic: true),
            [TokenKind.Pragma] = new(B("#7F7F7F")),
            [TokenKind.Operator] = new(B("#444444")),
        },
        Algol = new()
        {
            [TokenKind.Keyword] = new(B("#0000C8"), Bold: true),
            [TokenKind.Type] = new(B("#2B6CB0"), Bold: true),
            [TokenKind.BoldWord] = new(B("#7B1FA2"), Bold: true),
            [TokenKind.Builtin] = new(B("#795E26")),
            [TokenKind.Number] = new(B("#098658")),
            [TokenKind.String] = new(B("#A31515")),
            [TokenKind.Char] = new(B("#A31515")),
            [TokenKind.Comment] = new(B("#2E8B3A"), Italic: true),
            [TokenKind.Pragma] = new(B("#7F7F7F"), Italic: true),
            [TokenKind.Operator] = new(B("#444444")),
        },
    };

    public static EditorTheme Dark { get; } = new()
    {
        IsDark = true,
        Background = B("#1E1F22"),
        Foreground = B("#D4D4D4"),
        LineNumbers = B("#6E7681"),
        CurrentLine = B("#26282C"),
        Selection = B("#264F78"),
        SelectionText = B("#FFFFFF"),
        BracketMatch = B("#40569CD6"),
        BracketMatchBorder = P("#80569CD6", 1),
        ExecutionLine = B("#4D4A2A"),
        CallerLine = B("#2A4532"),
        BreakpointFill = B("#E05252"),
        BreakpointLine = B("#4A2A2A"),
        ArrowFill = B("#F5C518"),
        ErrorSquiggle = B("#F14C4C"),
        WarningSquiggle = B("#CCA700"),
        InfoSquiggle = B("#3794FF"),
        SearchHighlight = B("#66806020"),
        ConsoleInput = B("#7FD18B"),
        ConsoleInfo = B("#8B919B"),
        ConsoleError = B("#F07070"),
        FoldingMarker = B("#8B919B"),
        FoldingBackground = B("#1E1F22"),
        Simula = new()
        {
            [TokenKind.Keyword] = new(B("#569CD6")),
            [TokenKind.Type] = new(B("#4EC9B0")),
            [TokenKind.Builtin] = new(B("#DCDCAA")),
            [TokenKind.Number] = new(B("#B5CEA8")),
            [TokenKind.String] = new(B("#CE9178")),
            [TokenKind.Char] = new(B("#CE9178")),
            [TokenKind.Comment] = new(B("#6A9955"), Italic: true),
            [TokenKind.Pragma] = new(B("#808080")),
            [TokenKind.Operator] = new(B("#C8C8C8")),
        },
        Algol = new()
        {
            [TokenKind.Keyword] = new(B("#569CD6"), Bold: true),
            [TokenKind.Type] = new(B("#4EC9B0"), Bold: true),
            [TokenKind.BoldWord] = new(B("#C586C0"), Bold: true),
            [TokenKind.Builtin] = new(B("#DCDCAA")),
            [TokenKind.Number] = new(B("#B5CEA8")),
            [TokenKind.String] = new(B("#CE9178")),
            [TokenKind.Char] = new(B("#CE9178")),
            [TokenKind.Comment] = new(B("#6A9955"), Italic: true),
            [TokenKind.Pragma] = new(B("#808080"), Italic: true),
            [TokenKind.Operator] = new(B("#C8C8C8")),
        },
    };

    public static EditorTheme Current { get; set; } = Light;
}
