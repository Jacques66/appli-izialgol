using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Studio.Ide.Editor;

/// <summary>Highlighting from the tokens of the document CodeModel (analysis of the whole text:
/// multi-line comments are always correct).</summary>
public sealed class SyntaxColorizer : DocumentColorizingTransformer
{
    readonly EditorDocument _doc;

    public SyntaxColorizer(EditorDocument doc) { _doc = doc; }

    protected override void ColorizeLine(DocumentLine line)
    {
        var model = _doc.Model;
        var theme = EditorTheme.Current;
        int start = line.Offset, end = line.EndOffset;
        if (end <= start) return;
        var toks = model.Tokens;
        for (int i = model.FirstTokenEndingAfter(start); i < toks.Count && toks[i].Start < end; i++)
        {
            var t = toks[i];
            int s = Math.Max(t.Start, start), e = Math.Min(t.End, end);
            if (e <= s) continue;
            var style = theme.StyleFor(_doc.Language, t.Kind);
            if (style == null) continue;
            ChangeLinePart(s, e, el =>
            {
                el.TextRunProperties.SetForegroundBrush(style.Brush);
                if (style.Bold || style.Italic)
                {
                    var tf = el.TextRunProperties.Typeface;
                    el.TextRunProperties.SetTypeface(new Typeface(tf.FontFamily,
                        style.Italic ? FontStyle.Italic : tf.Style,
                        style.Bold ? FontWeight.Bold : tf.Weight,
                        tf.Stretch));
                }
            });
        }
    }
}
