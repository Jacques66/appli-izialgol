using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace Studio.Ide.Editor;

/// <summary>Marge des points d'arret (clic = basculer) et de la fleche de ligne courante.</summary>
public sealed class BreakpointMargin : AbstractMargin
{
    readonly EditorDocument _doc;
    int _hoverLine = -1;

    public BreakpointMargin(EditorDocument doc)
    {
        _doc = doc;
        Cursor = Cursors.Hand;
        ToolTip = "Cliquer pour placer ou retirer un point d'arrêt (F9)";
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

    protected override void OnRender(DrawingContext dc)
    {
        var tv = TextView;
        var theme = EditorTheme.Current;
        dc.DrawRectangle(theme.Background, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));
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
            else if (line == _hoverLine)
            {
                var b = theme.BreakpointFill.Clone();
                b.Opacity = 0.3;
                dc.DrawEllipse(b, null, center, size / 2, size / 2);
            }
            if (_doc.ExecutionLine == line)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    double l = 2, r = 16, cy = center.Y, hh = size / 2;
                    ctx.BeginFigure(new Point(l, cy - hh / 2), true, true);
                    ctx.LineTo(new Point(l + 7, cy - hh / 2), true, false);
                    ctx.LineTo(new Point(l + 7, cy - hh), true, false);
                    ctx.LineTo(new Point(r, cy), true, false);
                    ctx.LineTo(new Point(l + 7, cy + hh), true, false);
                    ctx.LineTo(new Point(l + 7, cy + hh / 2), true, false);
                    ctx.LineTo(new Point(l, cy + hh / 2), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(_doc.ExecutionIsTopFrame ? theme.ArrowFill : theme.CallerLine, new Pen(Brushes.DimGray, 0.7), g);
            }
        }
    }

    int LineAt(MouseEventArgs e)
    {
        var tv = TextView;
        if (tv == null) return -1;
        var vl = tv.GetVisualLineFromVisualTop(e.GetPosition(tv).Y + tv.VerticalOffset);
        return vl?.FirstDocumentLine.LineNumber ?? -1;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        int line = LineAt(e);
        if (line > 0)
        {
            _doc.ToggleBreakpoint(line);
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int line = LineAt(e);
        if (line != _hoverLine) { _hoverLine = line; InvalidateVisual(); }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverLine = -1;
        InvalidateVisual();
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);
}

/// <summary>Fond des lignes : ligne d'execution courante, lignes avec point d'arret.</summary>
public sealed class LineBackgroundRenderer : IBackgroundRenderer
{
    readonly EditorDocument _doc;
    public LineBackgroundRenderer(EditorDocument doc) { _doc = doc; }
    // Calque « Selection » : au-dessus du surlignage de la ligne du curseur, sous le texte.
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (!textView.VisualLinesValid) return;
        var theme = EditorTheme.Current;
        var bps = _doc.BreakpointLineSet();
        foreach (var vl in textView.VisualLines)
        {
            int line = vl.FirstDocumentLine.LineNumber;
            Brush? brush = null;
            if (_doc.ExecutionLine == line) brush = _doc.ExecutionIsTopFrame ? theme.ExecutionLine : theme.CallerLine;
            else if (bps.Contains(line)) brush = theme.BreakpointLine;
            if (brush == null) continue;
            double top = vl.VisualTop - textView.VerticalOffset;
            dc.DrawRectangle(brush, null, new Rect(0, top, Math.Max(textView.ActualWidth, 1), vl.Height));
        }
    }
}

/// <summary>Appariement des parentheses et des blocs (begin/end, IF/FI…) autour du curseur.</summary>
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
        // Jeton qui touche le curseur (a droite d'abord, puis a gauche)
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
            builder.AddSegment(textView, new ICSharpCode.AvalonEdit.Document.TextSegment { StartOffset = s, Length = l });
        }
        var geo = builder.CreateGeometry();
        if (geo != null) dc.DrawGeometry(theme.BracketMatch, theme.BracketMatchBorder, geo);
    }
}
