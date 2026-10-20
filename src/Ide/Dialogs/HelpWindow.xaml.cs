using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Studio.Ide.Services;

namespace Studio.Ide.Dialogs;

/// <summary>Help: renders docs/simula.md and docs/algol68.md (Markdig → HTML).</summary>
public partial class HelpWindow : Window
{
    public sealed record TocEntry(string Title, string Id, int Level)
    {
        public Thickness Indent => new((Level - 1) * 12, 1, 0, 1);
    }

    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    string _currentTopic = "";
    string? _pendingAnchor;

    public HelpWindow()
    {
        InitializeComponent();
        Browser.Loaded += (_, _) => SuppressScriptErrors();
    }

    public void ShowTopic(string topic)
    {
        foreach (ListBoxItem item in TopicList.Items)
            if ((string)item.Tag == topic) { TopicList.SelectedItem = item; return; }
        TopicList.SelectedIndex = 0;
    }

    void OnTopicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TopicList.SelectedItem is ListBoxItem item) Load((string)item.Tag);
    }

    void Load(string topic)
    {
        _currentTopic = topic;
        string markdown;
        string title;
        switch (topic)
        {
            case "simula":
            case "algol68":
            {
                title = topic == "simula" ? "Simula 67" : "Algol 68";
                var docs = PathLocator.DocsDirectory;
                var file = docs == null ? null : System.IO.Path.Combine(docs, topic + ".md");
                if (file != null && System.IO.File.Exists(file))
                    markdown = System.IO.File.ReadAllText(file);
                else
                    markdown = $"# {title}\n\nThe documentation file `docs/{topic}.md` was not found.\n\n" +
                               "It is looked up in the `docs` folder next to the application, then in the parent folders.";
                break;
            }
            case "shortcuts":
                title = "Keyboard shortcuts";
                markdown = ShortcutsMarkdown;
                break;
            default:
                title = "Using the environment";
                markdown = IdeMarkdown;
                break;
        }
        Title = $"Help — {title}";
        var doc = Markdown.Parse(markdown, Pipeline);
        var toc = new List<TocEntry>();
        foreach (var h in doc.Descendants<HeadingBlock>())
        {
            if (h.Level > 3) continue;
            var id = h.GetAttributes().Id;
            var text = InlineText(h);
            if (!string.IsNullOrEmpty(id)) toc.Add(new TocEntry(text, id, h.Level));
        }
        TocList.ItemsSource = toc;
        var body = doc.ToHtml(Pipeline);
        Browser.NavigateToString(WrapHtml(body, title));
    }

    static string InlineText(HeadingBlock h)
    {
        var sb = new StringBuilder();
        if (h.Inline != null)
            foreach (var inline in h.Inline.Descendants())
                if (inline is Markdig.Syntax.Inlines.LiteralInline lit) sb.Append(lit.Content.ToString());
                else if (inline is Markdig.Syntax.Inlines.CodeInline code) sb.Append(code.Content);
        return sb.ToString();
    }

    static string WrapHtml(string body, string title)
    {
        bool dark = App.IsDarkTheme;
        string bg = dark ? "#1E1F22" : "#FFFFFF", fg = dark ? "#D4D4D4" : "#1E1E1E", dim = dark ? "#8B919B" : "#6B7280";
        string codeBg = dark ? "#2A2C30" : "#F3F4F6", border = dark ? "#3C3F45" : "#D9DDE3", link = dark ? "#5EA4EE" : "#0063B1";
        string h = dark ? "#9CDCFE" : "#0B4F8A";
        var html = $@"<!DOCTYPE html>
<html><head><meta http-equiv=""X-UA-Compatible"" content=""IE=edge""><meta charset=""utf-8"">
<title>{title}</title>
<style>
body {{ background:{bg}; color:{fg}; font-family:'Segoe UI',sans-serif; font-size:14px; line-height:1.55; margin:18px 28px 60px 28px; }}
h1,h2,h3,h4 {{ color:{h}; font-weight:600; margin-top:1.4em; }}
h1 {{ font-size:26px; border-bottom:1px solid {border}; padding-bottom:6px; margin-top:0.4em; }}
h2 {{ font-size:20px; border-bottom:1px solid {border}; padding-bottom:4px; }}
h3 {{ font-size:16px; }}
a {{ color:{link}; }}
code {{ font-family:'Cascadia Mono',Consolas,monospace; font-size:13px; background:{codeBg}; padding:1px 4px; border-radius:3px; }}
pre {{ background:{codeBg}; border:1px solid {border}; border-radius:4px; padding:10px 12px; overflow:auto; }}
pre code {{ background:transparent; padding:0; }}
table {{ border-collapse:collapse; margin:10px 0; }}
th, td {{ border:1px solid {border}; padding:5px 10px; text-align:left; vertical-align:top; }}
th {{ background:{codeBg}; }}
blockquote {{ border-left:4px solid {border}; margin:0; padding:2px 14px; color:{dim}; }}
kbd {{ border:1px solid {border}; border-radius:3px; padding:0 4px; background:{codeBg}; font-family:Consolas,monospace; }}
</style>
<script>function goTo(id) {{ var e = document.getElementById(id); if (e) {{ e.scrollIntoView(true); }} }}</script>
</head><body>{body}</body></html>";
        // WebBrowser.NavigateToString mishandles non-ASCII characters: use numeric entities.
        var sb = new StringBuilder(html.Length + 1024);
        foreach (var ch in html)
            if (ch < 128) sb.Append(ch);
            else sb.Append("&#").Append((int)ch).Append(';');
        return sb.ToString();
    }

    void OnTocClick(object sender, MouseButtonEventArgs e) => GoToSelected();

    void OnTocKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) GoToSelected();
    }

    void GoToSelected()
    {
        if (TocList.SelectedItem is TocEntry entry) ScrollTo(entry.Id);
    }

    void ScrollTo(string id)
    {
        try { Browser.InvokeScript("goTo", id); }
        catch { _pendingAnchor = id; }
    }

    void OnLoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        if (_pendingAnchor != null)
        {
            var a = _pendingAnchor;
            _pendingAnchor = null;
            ScrollTo(a);
        }
    }

    void OnNavigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        var uri = e.Uri;
        if (uri == null) return;
        // Internal anchors (#id) and links to the other help file
        var s = uri.OriginalString;
        if (s.StartsWith("about:", StringComparison.OrdinalIgnoreCase) && s.Contains('#'))
        {
            e.Cancel = true;
            ScrollTo(s[(s.IndexOf('#') + 1)..]);
            return;
        }
        if (s.EndsWith("simula.md", StringComparison.OrdinalIgnoreCase) || s.Contains("simula.md#")) { e.Cancel = true; ShowTopic("simula"); return; }
        if (s.EndsWith("algol68.md", StringComparison.OrdinalIgnoreCase) || s.Contains("algol68.md#")) { e.Cancel = true; ShowTopic("algol68"); return; }
        if (uri.Scheme is "http" or "https" or "mailto")
        {
            e.Cancel = true;
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        }
    }

    void SuppressScriptErrors()
    {
        try
        {
            var field = typeof(WebBrowser).GetField("_axIWebBrowser2", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var ax = field?.GetValue(Browser);
            ax?.GetType().InvokeMember("Silent", System.Reflection.BindingFlags.SetProperty, null, ax, new object[] { true });
        }
        catch { }
    }

    public const string ShortcutsMarkdown = @"# Keyboard shortcuts

## Files

| Shortcut | Action |
|---|---|
| <kbd>Ctrl+N</kbd> | New Simula 67 program |
| <kbd>Ctrl+Shift+N</kbd> | New Algol 68 program |
| <kbd>Ctrl+O</kbd> | Open |
| <kbd>Ctrl+S</kbd> | Save |
| <kbd>Ctrl+Shift+S</kbd> | Save all |
| <kbd>Ctrl+W</kbd>, <kbd>Ctrl+F4</kbd> | Close the tab (a middle click on the tab works too) |

## Editing

| Shortcut | Action |
|---|---|
| <kbd>Ctrl+Z</kbd> / <kbd>Ctrl+Y</kbd> | Undo / redo |
| <kbd>Ctrl+F</kbd> | Find (plain text or regular expression) |
| <kbd>Ctrl+H</kbd> | Replace |
| <kbd>F3</kbd> / <kbd>Shift+F3</kbd> | Find next / previous |
| <kbd>Ctrl+G</kbd> | Go to line |
| <kbd>Ctrl+Space</kbd> | Complete the word (keywords, standard procedures, identifiers of the file) |
| <kbd>Ctrl+K</kbd> / <kbd>Ctrl+Shift+K</kbd> | Comment / uncomment the selected lines |
| <kbd>Ctrl+I</kbd> | Re-indent the selection |
| <kbd>Ctrl+M</kbd>, <kbd>Ctrl+O</kbd> | Collapse all blocks |
| <kbd>Ctrl+M</kbd>, <kbd>Ctrl+L</kbd> | Expand all blocks |
| <kbd>Ctrl+M</kbd>, <kbd>Ctrl+M</kbd> | Collapse / expand the current block |
| <kbd>Ctrl++</kbd> / <kbd>Ctrl+-</kbd> / <kbd>Ctrl+0</kbd> | Zoom in / out / reset (or Ctrl+mouse wheel) |
| <kbd>Ins</kbd> | Insert / overwrite mode |

## Build, run, debug

| Shortcut | Action |
|---|---|
| <kbd>F7</kbd>, <kbd>Ctrl+Shift+B</kbd> | Build (compile the current file) |
| <kbd>Ctrl+Shift+E</kbd> | Generate a standalone .exe |
| <kbd>F5</kbd> | Start debugging / continue |
| <kbd>Ctrl+F5</kbd> | Run without debugging |
| <kbd>Shift+F5</kbd> | Stop |
| <kbd>Ctrl+Shift+F5</kbd> | Restart |
| <kbd>Ctrl+Alt+Pause</kbd> | Pause |
| <kbd>F9</kbd> | Toggle breakpoint (or click in the margin) |
| <kbd>Ctrl+Shift+F9</kbd> | Delete all breakpoints |
| <kbd>F11</kbd> | Step into |
| <kbd>F10</kbd> | Step over |
| <kbd>Shift+F11</kbd> | Step out |

## Integrated console

| Shortcut | Action |
|---|---|
| <kbd>Enter</kbd> | Send the line to the program |
| <kbd>Ctrl+Z</kbd> | End of file |
| <kbd>↑</kbd> / <kbd>↓</kbd> | History of the lines typed |

## Panels

| Shortcut | Action |
|---|---|
| <kbd>Ctrl+Alt+O</kbd> | Output |
| <kbd>Ctrl+Alt+E</kbd> | Error list |
| <kbd>Ctrl+Alt+C</kbd> | Console |
| <kbd>Ctrl+Alt+P</kbd> | Call stack |
| <kbd>Ctrl+Alt+V</kbd> | Variables |
| <kbd>F1</kbd> | Help on the language of the current file |
";

    public const string IdeMarkdown = @"# Using the environment

## Writing a program

* **File › New Simula 67 program** (<kbd>Ctrl+N</kbd>) or **Algol 68** (<kbd>Ctrl+Shift+N</kbd>) creates a tab with a template.
* The language is chosen from the extension (`.sim` or `.a68`); it can be changed in **Build › File language** or by clicking the language name in the status bar.
* The **Examples** menu opens the programs of the `samples` folder.
* Files can be dragged and dropped into the window; a dropped folder becomes the root of the explorer.
* The session (open tabs, breakpoints, caret position, layout) is restored at start-up.

## Simula 67

Keywords are case-insensitive. Comments: `comment … ;`, `! … ;` and the text that follows `end` up to the next `;`, `end`, `else`, `when` or `otherwise`.

## Algol 68

UPPER stropping: upper-case words (`BEGIN`, `INT`, `PROC`…) are bold words; QUOTE stropping (`'BEGIN'`) is recognised too.
Comments: `CO … CO`, `COMMENT … COMMENT`, `# … #`.

## Build and run

* **Build** (<kbd>F7</kbd>) compiles the current file: diagnostics appear in the **Output**, in the **error list** (double-click to go to the line) and are underlined in the editor. A background check underlines errors while you type.
* **Run** (<kbd>Ctrl+F5</kbd>) starts the program in the **integrated console**: output appears as it is produced, typed lines are sent to the program, <kbd>Ctrl+Z</kbd> sends end of file, **Stop** (<kbd>Shift+F5</kbd>) interrupts the run.
* The **Run in external Windows console** option (toolbar button) opens a real console window.
* **Generate a standalone .exe** produces a Windows executable that can be copied and run on its own (the .NET 8 runtime must be installed).

## Debugging

* Click in the left margin (or press <kbd>F9</kbd>) to set a breakpoint (red dot).
* <kbd>F5</kbd> starts debugging; when stopped, the current line is highlighted in yellow with an arrow.
* <kbd>F11</kbd> steps into procedures, <kbd>F10</kbd> steps over them, <kbd>Shift+F11</kbd> steps out of the current procedure.
* The **call stack** (double-click to show a caller) and the **variables** (expandable tree) are refreshed at each stop.
* A runtime error stops the program on the faulty line (option **Break on runtime error**).

## Command line

```
simc program.sim [-o program.exe] [--run] [--no-debug]
a68c program.a68 [-o program.exe] [--run] [--no-debug]
```
";
}
