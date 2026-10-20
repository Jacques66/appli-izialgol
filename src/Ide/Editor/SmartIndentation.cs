using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;

namespace Studio.Ide.Editor;

/// <summary>Indentation automatique : +1 niveau apres un ouvrant (begin, BEGIN, IF, DO…),
/// -1 pour une ligne qui commence par un fermant (end, FI, OD…) ou un mot intermediaire (THEN, ELSE…).</summary>
public sealed class SmartIndentation : IIndentationStrategy
{
    readonly EditorDocument _doc;
    public SmartIndentation(EditorDocument doc) { _doc = doc; }

    public void IndentLine(TextDocument document, DocumentLine line)
    {
        var prev = line.PreviousLine;
        while (prev != null && document.GetText(prev).Trim().Length == 0) prev = prev.PreviousLine;
        if (prev == null) return;
        var prevText = document.GetText(prev);
        var curText = document.GetText(line);
        var lang = _doc.Language;
        lang.IndentUnit = _doc.IndentUnit;
        string indent = lang.ComputeIndent(prevText, curText);
        int oldIndentLen = curText.Length - curText.TrimStart(' ', '\t').Length;
        if (curText[..oldIndentLen] == indent) return;
        document.Replace(line.Offset, oldIndentLen, indent, OffsetChangeMappingType.RemoveAndInsert);
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine)
    {
        using (document.RunUpdate())
            for (int i = beginLine; i <= endLine; i++)
                IndentLine(document, document.GetLineByNumber(i));
    }
}
