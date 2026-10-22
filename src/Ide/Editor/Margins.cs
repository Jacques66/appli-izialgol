using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace Studio.Ide.Editor;

/// <summary>Breakpoint margin (click = toggle) and current-line arrow.</summary>
public sealed class BreakpointMargin : AbstractMargin, ICustomHitTest
{
    static readonly IPen ArrowPen = new ImmutablePen(Brushes.DimGray.ToImmutable(), 0.7);
    readonly EditorDocument _doc;
    int _hoverLine = -1;

    public BreakpointMargin(EditorDocument doc)
    {
        _doc = doc;
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, "Click to add or remove a breakpoint (F9)");
    }

    protected override Size MeasureOverride(Size availableSize) => new(18, 0);

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView != null) oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null) newTextView.VisualLinesChanged += OnVisualLinesChanged;
        InvalidateVisual();
    }

    void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext dc)
    {
        var tv = TextView;
        var theme = EditorTheme.Current;
        dc.DrawRectangle(theme.Background, null, new Rect(0, 0, Bounds.Width, Bounds.Height));
        if (tv == null || !tv.VisualLinesValid) return;
        var bps = _doc.BreakpointLineSet();
        foreach (var vl in tv.VisualLines)
        {
            int line = vl.FirstDocumentLine.LineNumber;
            double top = vl.VisualTop - tv.VerticalOffset;
            double h = vl.Height;
            double size = Math.Min(13, h - 2);
            var center = new Point(9, top + h / 2);
            if (bps.Contains(line))
                dc.DrawEllipse(theme.BreakpointFill, null, center, size / 2, size / 2);
            else if (line == _hoverLine && theme.BreakpointFill is ISolidColorBrush fill)
                dc.DrawEllipse(new ImmutableSolidColorBrush(fill.Color, 0.3), null, center, size / 2, size / 2);
            if (_doc.ExecutionLine == line)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    double l = 2, r = 16, cy = center.Y, hh = size / 2;
                    ctx.BeginFigure(new Point(l, cy - hh / 2), true);
                    ctx.LineTo(new Point(l + 7, cy - hh / 2));
                    ctx.LineTo(new Point(l + 7, cy - hh));
                    ctx.LineTo(new Point(r, cy));
                    ctx.LineTo(new Point(l + 7, cy + hh));
                    ctx.LineTo(new Point(l + 7, cy + hh / 2));
                    ctx.LineTo(new Point(l, cy + hh / 2));
                    ctx.EndFigure(true);
                }
                dc.DrawGeometry(_doc.ExecutionIsTopFrame ? theme.ArrowFill : theme.CallerLine, ArrowPen, g);
            }
        }
    }

    int LineAt(PointerEventArgs e)
    {
        var tv = TextView;
        if (tv == null) return -1;
        var vl = tv.GetVisualLineFromVisualTop(e.GetPosition(tv).Y + tv.VerticalOffset);
        return vl?.FirstDocumentLine.LineNumber ?? -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int line = LineAt(e);
        if (line > 0)
        {
            _doc.ToggleBreakpoint(line);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int line = LineAt(e);
        if (line != _hoverLine) { _hoverLine = line; InvalidateVisual(); }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverLine = -1;
        InvalidateVisual();
    }

    public bool HitTest(Point point) => true;
}

/// <summary>Line backgrounds: current execution line, lines with a breakpoint.</summary>
public sealed class LineBackgroundRenderer : IBackgroundRenderer
{
    readonly EditorDocument _doc;
    public LineBackgroundRenderer(EditorDocument doc) { _doc = doc; }
    // "Selection" layer: above the caret-line highlight, below the text.
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (!textView.VisualLinesValid) return;
        var theme = EditorTheme.Current;
        var bps = _doc.BreakpointLineSet();
        foreach (var vl in textView.VisualLines)
        {
            int line = vl.FirstDocumentLine.LineNumber;
            IBrush? brush = null;
            if (_doc.ExecutionLine == line) brush = _doc.ExecutionIsTopFrame ? theme.ExecutionLine : theme.CallerLine;
            else if (bps.Contains(line)) brush = theme.BreakpointLine;
            if (brush == null) continue;
            double top = vl.VisualTop - textView.VerticalOffset;
            dc.DrawRectangle(brush, null, new Rect(0, top, Math.Max(textView.Bounds.Width, 1), vl.Height));
        }
    }
}

/// <summary>Matching of brackets and blocks (begin/end, IF/FI…) around the caret.</summary>
public sealed class BracketHighlighter : IBackgroundRenderer
{
    readonly EditorDocument _doc;
    (int Start, int Length)? _a, _b;

    public BracketHighlighter(EditorDocument doc) { _doc = doc; }
    public KnownLayer Layer => KnownLayer.Selection;

    public void Update(int caret)
    {
        var old = (_a, _b);
        _a = _b = null;
        var m = _doc.Model;
        // Token touching the caret (right side first, then left side)
        int idx = m.TokenAt(caret, allowEndTouch: false);
        if (idx < 0 || !m.PairOf.ContainsKey(idx)) idx = m.TokenAt(caret, allowEndTouch: true);
        if (idx >= 0 && m.PairOf.TryGetValue(idx, out var other))
        {
            _a = (m.Tokens[idx].Start, m.Tokens[idx].Length);
            _b = (m.Tokens[other].Start, m.Tokens[other].Length);
        }
        if (old != (_a, _b)) _doc.Editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (_a == null || _b == null) return;
        var theme = EditorTheme.Current;
        var builder = new BackgroundGeometryBuilder { CornerRadius = 2, AlignToWholePixels = true };
        int len = _doc.Editor.Document.TextLength;
        foreach (var (s, l) in new[] { _a.Value, _b.Value })
        {
            if (s + l > len) return;
            builder.AddSegment(textView, new AvaloniaEdit.Document.TextSegment { StartOffset = s, Length = l });
        }
        var geo = builder.CreateGeometry();
        if (geo != null) dc.DrawGeometry(theme.BracketMatch, theme.BracketMatchBorder, geo);
    }
}
