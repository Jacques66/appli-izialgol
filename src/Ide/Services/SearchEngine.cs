using System.Text.RegularExpressions;
using Studio.Ide.Editor;

namespace Studio.Ide.Services;

public sealed class SearchOptions
{
    public string Text { get; set; } = "";
    public string Replacement { get; set; } = "";
    public bool MatchCase { get; set; }
    public bool WholeWord { get; set; }
    public bool UseRegex { get; set; }
}

/// <summary>Find / replace in an editor (plain text or regular expression).</summary>
public static class SearchEngine
{
    public static Regex? Build(SearchOptions o, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(o.Text)) { error = "Nothing to search for."; return null; }
        var pattern = o.UseRegex ? o.Text : Regex.Escape(o.Text);
        if (o.WholeWord) pattern = @"(?<![\w])(?:" + pattern + @")(?![\w])";
        var opts = RegexOptions.Multiline | RegexOptions.CultureInvariant;
        if (!o.MatchCase) opts |= RegexOptions.IgnoreCase;
        try
        {
            return new Regex(pattern, opts, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            error = "Invalid regular expression: " + ex.Message;
            return null;
        }
    }

    public static bool FindNext(EditorDocument doc, Regex rx, bool backwards, out bool wrapped)
    {
        wrapped = false;
        var ed = doc.Editor;
        var text = ed.Document.Text;
        Match? found = null;
        try
        {
            if (!backwards)
            {
                int start = ed.SelectionStart + ed.SelectionLength;
                var m = rx.Match(text, Math.Min(start, text.Length));
                if (m.Success && m.Length == 0 && m.Index == start && ed.SelectionLength == 0) m = m.NextMatch();
                if (!m.Success) { m = rx.Match(text, 0); wrapped = true; }
                if (m.Success) found = m;
            }
            else
            {
                int limit = ed.SelectionStart;
                Match? last = null, lastAll = null;
                for (var m = rx.Match(text); m.Success; m = m.NextMatch())
                {
                    if (m.Index + m.Length <= limit && m.Index < limit) last = m;
                    lastAll = m;
                    if (m.Length == 0 && m.Index >= text.Length) break;
                }
                found = last;
                if (found == null) { found = lastAll; wrapped = true; }
            }
        }
        catch (RegexMatchTimeoutException) { return false; }
        if (found == null) return false;
        ed.Select(found.Index, found.Length);
        var loc = ed.Document.GetLocation(found.Index);
        foreach (var f in doc.Folding.GetFoldingsContaining(found.Index)) f.IsFolded = false;
        ed.ScrollTo(loc.Line, loc.Column);
        return true;
    }

    /// <summary>Replaces the selection if it matches the search.</summary>
    public static bool ReplaceCurrent(EditorDocument doc, Regex rx, SearchOptions o)
    {
        var ed = doc.Editor;
        if (ed.SelectionLength == 0) return false;
        var m = rx.Match(ed.Document.Text, ed.SelectionStart);
        if (!m.Success || m.Index != ed.SelectionStart || m.Length != ed.SelectionLength) return false;
        var replacement = o.UseRegex ? m.Result(o.Replacement) : o.Replacement;
        ed.Document.Replace(m.Index, m.Length, replacement);
        ed.Select(m.Index + replacement.Length, 0);
        return true;
    }

    /// <summary>Replaces all occurrences (a single undo step).</summary>
    public static int ReplaceAll(EditorDocument doc, Regex rx, SearchOptions o)
    {
        var ed = doc.Editor;
        var matches = rx.Matches(ed.Document.Text).Cast<Match>().Where(m => m.Length > 0).ToList();
        if (matches.Count == 0) return 0;
        using (ed.Document.RunUpdate())
        {
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var m = matches[i];
                ed.Document.Replace(m.Index, m.Length, o.UseRegex ? m.Result(o.Replacement) : o.Replacement);
            }
        }
        return matches.Count;
    }
}
