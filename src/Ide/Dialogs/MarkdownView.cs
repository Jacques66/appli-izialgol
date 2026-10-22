using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Markdig.Extensions.Tables;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace Studio.Ide.Dialogs;

/// <summary>Colours of the help pages (light or dark theme).</summary>
public sealed record MarkdownColors(Color Background, Color Text, Color Dim, Color CodeBackground, Color Border, Color Link, Color Heading)
{
    public static MarkdownColors Light { get; } = new(Color.Parse("#FFFFFF"), Color.Parse("#1E1E1E"), Color.Parse("#6B7280"),
        Color.Parse("#F3F4F6"), Color.Parse("#D9DDE3"), Color.Parse("#0063B1"), Color.Parse("#0B4F8A"));
    public static MarkdownColors Dark { get; } = new(Color.Parse("#1E1F22"), Color.Parse("#D4D4D4"), Color.Parse("#8B919B"),
        Color.Parse("#2A2C30"), Color.Parse("#3C3F45"), Color.Parse("#5EA4EE"), Color.Parse("#9CDCFE"));
}

/// <summary>
/// Renders a Markdig document with Avalonia controls (replaces the WPF WebBrowser of the help window):
/// headings, paragraphs, emphasis, inline code, links, lists, fenced code, tables, quotes, rules and the
/// &lt;kbd&gt; HTML tag. Headings are anchors for <see cref="ScrollTo"/>; links are reported by <see cref="LinkClicked"/>.
/// </summary>
public sealed class MarkdownView : ScrollViewer
{
    const double BaseSize = 14;
    static FontFamily CodeFamily => Studio.Ide.Editor.CodeFont.Resolve(Services.IdeSettings.DefaultFontFamily);

    readonly StackPanel _panel = new() { Margin = new Thickness(28, 18, 28, 60) };
    readonly Dictionary<string, Control> _anchors = new(StringComparer.Ordinal);
    MarkdownColors _colors = MarkdownColors.Light;
    IBrush _text = Brushes.Black, _dim = Brushes.Gray, _codeBg = Brushes.LightGray, _border = Brushes.Gray, _link = Brushes.Blue, _heading = Brushes.Navy;
    string? _pendingAnchor;

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    public MarkdownView()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        Content = _panel;
    }

    /// <summary>A link was clicked (URL as written in the Markdown: "#anchor", "file.md", "https://…").</summary>
    public event Action<string>? LinkClicked;

    /// <summary>Number of rendered top-level blocks (0 = nothing shown).</summary>
    public int BlockCount => _panel.Children.Count;

    public void Render(MarkdownDocument document, MarkdownColors colors)
    {
        _colors = colors;
        _text = new ImmutableSolidColorBrush(colors.Text);
        _dim = new ImmutableSolidColorBrush(colors.Dim);
        _codeBg = new ImmutableSolidColorBrush(colors.CodeBackground);
        _border = new ImmutableSolidColorBrush(colors.Border);
        _link = new ImmutableSolidColorBrush(colors.Link);
        _heading = new ImmutableSolidColorBrush(colors.Heading);
        Background = new ImmutableSolidColorBrush(colors.Background);
        _anchors.Clear();
        _panel.Children.Clear();
        foreach (var block in document) AddBlock(_panel, block, _text);
        Offset = default;
    }

    /// <summary>Scrolls so that the heading with this id is at the top.</summary>
    public void ScrollTo(string id)
    {
        if (!_anchors.TryGetValue(id, out var target)) return;
        _pendingAnchor = id;
        Dispatcher.UIThread.Post(() =>
        {
            if (_pendingAnchor != id) return;
            _pendingAnchor = null;
            var p = target.TranslatePoint(new Point(0, 0), _panel);
            if (p != null) Offset = new Vector(0, Math.Max(0, p.Value.Y + _panel.Margin.Top - 8));
        }, DispatcherPriority.Background);
    }

    // ------------------------------------------------------------------ blocks

    void AddBlock(Panel parent, Block block, IBrush foreground)
    {
        switch (block)
        {
            case HeadingBlock h:
            {
                double size = h.Level switch { 1 => 26, 2 => 20, 3 => 16, _ => BaseSize };
                var tb = NewText(foreground: _heading, size: size);
                tb.FontWeight = FontWeight.SemiBold;
                AddInlines(tb.Inlines!, h.Inline, new Style(_heading));
                Control c = tb;
                if (h.Level <= 2)
                    c = new Border { Child = tb, BorderBrush = _border, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, h.Level == 1 ? 6 : 4) };
                c.Margin = new Thickness(0, parent.Children.Count == 0 && h.Level == 1 ? 6 : size * 1.2, 0, 8);
                var id = h.GetAttributes().Id;
                if (!string.IsNullOrEmpty(id)) _anchors[id] = c;
                parent.Children.Add(c);
                break;
            }
            case ParagraphBlock p:
            {
                var tb = NewText(foreground);
                AddInlines(tb.Inlines!, p.Inline, new Style(foreground));
                tb.Margin = new Thickness(0, 0, 0, 10);
                parent.Children.Add(tb);
                break;
            }
            case ListBlock list:
                parent.Children.Add(BuildList(list, foreground));
                break;
            case CodeBlock code:
                parent.Children.Add(BuildCode(code));
                break;
            case Table table:
                parent.Children.Add(BuildTable(table, foreground));
                break;
            case QuoteBlock quote:
            {
                var inner = new StackPanel();
                foreach (var b in quote) AddBlock(inner, b, _dim);
                parent.Children.Add(new Border
                {
                    Child = inner,
                    BorderBrush = _border,
                    BorderThickness = new Thickness(4, 0, 0, 0),
                    Padding = new Thickness(14, 2, 0, 0),
                    Margin = new Thickness(0, 0, 0, 10),
                });
                break;
            }
            case ThematicBreakBlock:
                parent.Children.Add(new Border { Height = 1, Background = _border, Margin = new Thickness(0, 8, 0, 16) });
                break;
            case HtmlBlock html:
            {
                var tb = NewText(foreground);
                tb.Text = string.Join("\n", html.Lines.Lines.Take(html.Lines.Count).Select(l => l.ToString()));
                tb.Margin = new Thickness(0, 0, 0, 10);
                parent.Children.Add(tb);
                break;
            }
            case ContainerBlock container:
                foreach (var b in container) AddBlock(parent, b, foreground);
                break;
        }
    }

    Control BuildList(ListBlock list, IBrush foreground)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        int number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*") };
            var marker = NewText(foreground);
            marker.Text = list.IsOrdered ? $"{number++}{list.OrderedDelimiter}" : "•";
            marker.TextAlignment = TextAlignment.Right;
            marker.Margin = new Thickness(0, 0, 8, 0);
            marker.TextWrapping = TextWrapping.NoWrap;
            var content = new StackPanel();
            foreach (var b in item) AddBlock(content, b, foreground);
            // Tight list: no space after the paragraphs of an item
            foreach (var c in content.Children.OfType<TextBlock>()) c.Margin = new Thickness(0, 0, 0, 3);
            Grid.SetColumn(content, 1);
            grid.Children.Add(marker);
            grid.Children.Add(content);
            panel.Children.Add(grid);
        }
        return panel;
    }

    Control BuildCode(CodeBlock code)
    {
        var lines = code.Lines.Lines.Take(code.Lines.Count).Select(l => l.ToString());
        var tb = new SelectableTextBlock
        {
            Text = string.Join("\n", lines).TrimEnd('\n', '\r'),
            FontFamily = CodeFamily,
            FontSize = 13,
            Foreground = _text,
            TextWrapping = TextWrapping.NoWrap,
        };
        return new Border
        {
            Background = _codeBg,
            BorderBrush = _border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new ScrollViewer
            {
                Content = tb,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            },
        };
    }

    Control BuildTable(Table table, IBrush foreground)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        int columns = table.OfType<TableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max();
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        int row = 0;
        foreach (var r in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            int col = 0;
            foreach (var cell in r.OfType<TableCell>())
            {
                var content = new StackPanel();
                foreach (var b in cell) AddBlock(content, b, foreground);
                foreach (var tb in content.Children.OfType<TextBlock>())
                {
                    tb.Margin = new Thickness(0);
                    tb.MaxWidth = 560;
                    if (r.IsHeader) tb.FontWeight = FontWeight.SemiBold;
                }
                var border = new Border
                {
                    Child = content,
                    BorderBrush = _border,
                    BorderThickness = new Thickness(col == 0 ? 1 : 0, row == 0 ? 1 : 0, 1, 1),
                    Padding = new Thickness(10, 5),
                    Background = r.IsHeader ? _codeBg : null,
                };
                Grid.SetRow(border, row);
                Grid.SetColumn(border, Math.Min(col, Math.Max(0, columns - 1)));
                grid.Children.Add(border);
                col++;
            }
            // Missing cells keep the grid lines
            for (; col < columns; col++)
            {
                var empty = new Border { BorderBrush = _border, BorderThickness = new Thickness(0, row == 0 ? 1 : 0, 1, 1) };
                Grid.SetRow(empty, row);
                Grid.SetColumn(empty, col);
                grid.Children.Add(empty);
            }
            row++;
        }
        return new ScrollViewer
        {
            Content = grid,
            Margin = new Thickness(0, 4, 0, 12),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
    }

    SelectableTextBlock NewText(IBrush foreground, double size = BaseSize) => new()
    {
        Foreground = foreground,
        FontSize = size,
        LineHeight = Math.Round(size * 1.5),
        TextWrapping = TextWrapping.Wrap,
    };

    // ------------------------------------------------------------------ inlines

    readonly record struct Style(IBrush Foreground, bool Bold = false, bool Italic = false, bool Strike = false, bool Code = false);

    void AddInlines(InlineCollection target, ContainerInline? container, Style style)
    {
        if (container == null) return;
        System.Text.StringBuilder? kbd = null;
        foreach (var inline in container)
            AddInline(target, inline, style, ref kbd);
    }

    void AddInline(InlineCollection target, MdInline inline, Style style, ref System.Text.StringBuilder? kbd)
    {
        switch (inline)
        {
            case HtmlInline html:
            {
                var tag = html.Tag.Trim().ToLowerInvariant();
                if (tag == "<kbd>") kbd = new System.Text.StringBuilder();
                else if (tag == "</kbd>" && kbd != null)
                {
                    target.Add(KeyCap(kbd.ToString()));
                    kbd = null;
                }
                else if (tag is "<br>" or "<br/>" or "<br />") target.Add(new LineBreak());
                return;
            }
            case LiteralInline lit:
                if (kbd != null) { kbd.Append(lit.Content.ToString()); return; }
                target.Add(MakeRun(lit.Content.ToString(), style));
                return;
            case HtmlEntityInline entity:
                if (kbd != null) { kbd.Append(entity.Transcoded.ToString()); return; }
                target.Add(MakeRun(entity.Transcoded.ToString(), style));
                return;
            case CodeInline code:
                target.Add(MakeRun(code.Content, style with { Code = true }));
                return;
            case LineBreakInline br:
                target.Add(br.IsHard ? new LineBreak() : MakeRun(" ", style));
                return;
            case EmphasisInline em:
            {
                var s = em.DelimiterChar == '~' ? style with { Strike = true }
                    : em.DelimiterCount >= 2 ? style with { Bold = true }
                    : style with { Italic = true };
                foreach (var child in em) AddInline(target, child, s, ref kbd);
                return;
            }
            case LinkInline link when !link.IsImage:
            {
                var url = link.Url ?? "";
                var text = new SelectableTextBlock { Foreground = _link, Cursor = new Cursor(StandardCursorType.Hand), FontSize = BaseSize };
                AddInlines(text.Inlines!, link, style with { Foreground = _link });
                text.TextDecorations = TextDecorations.Underline;
                ToolTip.SetTip(text, url);
                text.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) LinkClicked?.Invoke(url); };
                target.Add(new InlineUIContainer(text) { BaselineAlignment = BaselineAlignment.TextBottom });
                return;
            }
            case AutolinkInline auto:
            {
                var text = new TextBlock { Text = auto.Url, Foreground = _link, Cursor = new Cursor(StandardCursorType.Hand), TextDecorations = TextDecorations.Underline, FontSize = BaseSize };
                text.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) LinkClicked?.Invoke(auto.IsEmail ? "mailto:" + auto.Url : auto.Url); };
                target.Add(new InlineUIContainer(text) { BaselineAlignment = BaselineAlignment.TextBottom });
                return;
            }
            case ContainerInline c:
                foreach (var child in c) AddInline(target, child, style, ref kbd);
                return;
        }
    }

    Run MakeRun(string text, Style style)
    {
        var run = new Run(text) { Foreground = style.Foreground };
        if (style.Bold) run.FontWeight = FontWeight.Bold;
        if (style.Italic) run.FontStyle = FontStyle.Italic;
        if (style.Strike) run.TextDecorations = TextDecorations.Strikethrough;
        if (style.Code)
        {
            run.FontFamily = CodeFamily;
            run.FontSize = 13;
            run.Background = _codeBg;
        }
        return run;
    }

    InlineUIContainer KeyCap(string text) => new(new Border
    {
        BorderBrush = _border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3),
        Background = _codeBg,
        Padding = new Thickness(4, 0),
        Child = new TextBlock { Text = text, FontFamily = CodeFamily, FontSize = 12.5, Foreground = _text },
    }) { BaselineAlignment = BaselineAlignment.Center };
}
