using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Studio.Common;

namespace Studio.Ide.Editor;

public sealed class TextMarker : TextSegment
{
    public TextMarker(int start, int length, Severity severity, string message)
    {
        StartOffset = start; Length = length; Severity = severity; Message = message;
    }
    public Severity Severity { get; }
    public string Message { get; }
}

/// <summary>Wavy underline of diagnostics + tooltip on hover.</summary>
public sealed class TextMarkerService : IBackgroundRenderer
{
    readonly TextEditor _editor;
    readonly TextSegmentCollection<TextMarker> _markers;
    readonly ToolTip _toolTip = new();

    public TextMarkerService(TextEditor editor)
    {
        _editor = editor;
        _markers = new TextSegmentCollection<TextMarker>(editor.Document);
        editor.TextArea.TextView.BackgroundRenderers.Add(this);
        editor.TextArea.TextView.MouseHover += OnMouseHover;
        editor.TextArea.TextView.MouseHoverStopped += (_, _) => _toolTip.IsOpen = false;
        editor.TextArea.TextView.MouseLeave += (_, _) => _toolTip.IsOpen = false;
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public int Count => _markers.Count;
    public IEnumerable<TextMarker> Markers => _markers;

    public void Clear()
    {
        _markers.Clear();
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    /// <summary>Adds a marker at (line, column): the word under the column, or the whole line.</summary>
    public void Add(int line, int column, Severity severity, string message)
    {
        var doc = _editor.Document;
        if (line < 1) line = 1;
        if (line > doc.LineCount) line = doc.LineCount;
        var dl = doc.GetLineByNumber(line);
        var text = doc.GetText(dl);
        int start, end;
        if (column < 1)
        {
            int ind = text.Length - text.TrimStart().Length;
            start = dl.Offset + ind;
            end = dl.Offset + text.TrimEnd().Length;
        }
        else
        {
            int c = Math.Min(column - 1, text.Length);
            if (c == text.Length && c > 0) c--;
            start = dl.Offset + c;
            int e = c;
            while (e < text.Length && (char.IsLetterOrDigit(text[e]) || text[e] == '_')) e++;
            if (e == c) e = Math.Min(text.Length, c + 1);
            end = dl.Offset + e;
        }
        if (end <= start) end = Math.Min(doc.TextLength, start + 1);
        if (end <= start) { start = Math.Max(0, start - 1); end = start + (doc.TextLength > 0 ? 1 : 0); }
        _markers.Add(new TextMarker(start, Math.Max(0, end - start), severity, message));
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (_markers.Count == 0 || !textView.VisualLinesValid) return;
        var visual = textView.VisualLines;
        if (visual.Count == 0) return;
        int viewStart = visual[0].FirstDocumentLine.Offset;
        int viewEnd = visual[^1].LastDocumentLine.EndOffset;
        var theme = EditorTheme.Current;
        foreach (var m in _markers.FindOverlappingSegments(viewStart, viewEnd - viewStart))
        {
            var brush = m.Severity switch
            {
                Severity.Error => theme.ErrorSquiggle,
                Severity.Warning => theme.WarningSquiggle,
                _ => theme.InfoSquiggle,
            };
            var pen = new Pen(brush, 1);
            pen.Freeze();
            foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, m))
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    double y = r.Bottom - 1;
                    double x = r.Left;
                    double right = Math.Max(r.Right, r.Left + 6);
                    ctx.BeginFigure(new Point(x, y), false, false);
                    bool up = true;
                    while (x < right)
                    {
                        x += 2;
                        ctx.LineTo(new Point(Math.Min(x, right), up ? y - 2 : y), true, false);
                        up = !up;
                    }
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }
        }
    }

    void OnMouseHover(object sender, MouseEventArgs e)
    {
        var tv = _editor.TextArea.TextView;
        var pos = tv.GetPositionFloor(e.GetPosition(tv) + tv.ScrollOffset);
        if (pos == null) return;
        int offset = _editor.Document.GetOffset(pos.Value.Location);
        var hits = _markers.FindSegmentsContaining(offset).ToList();
        if (hits.Count == 0) return;
        _toolTip.PlacementTarget = tv;
        _toolTip.Content = string.Join(Environment.NewLine, hits.Select(h =>
            (h.Severity == Severity.Error ? "Error: " : h.Severity == Severity.Warning ? "Warning: " : "Info: ") + h.Message));
        _toolTip.IsOpen = true;
        e.Handled = true;
    }
}
