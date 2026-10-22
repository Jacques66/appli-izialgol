using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Studio.Ide.Services;

namespace Studio.Ide.Dialogs;

/// <summary>Help: renders docs/simula.md, docs/algol68.md, docs/algol60.md, docs/simula1.md and docs/pascal.md
/// (Markdig → Avalonia controls, see <see cref="MarkdownView"/>).</summary>
public partial class HelpWindow : Window
{
    public sealed record TocEntry(string Title, string Id, int Level)
    {
        public Thickness Indent => new((Level - 1) * 12, 1, 0, 1);
    }

    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    string _currentTopic = "";

    public HelpWindow()
    {
        InitializeComponent();
        Viewer.LinkClicked += OnLinkClicked;
    }

    public void ShowTopic(string topic)
    {
        foreach (var item in TopicList.Items.OfType<ListBoxItem>())
            if ((string?)item.Tag == topic) { TopicList.SelectedItem = item; return; }
        TopicList.SelectedIndex = 0;
    }

    void OnTopicChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TopicList.SelectedItem is ListBoxItem { Tag: string topic }) Load(topic);
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
            case "algol60":
            case "simula1":
            case "pascal":
            {
                title = topic switch { "simula" => "Simula 67", "algol68" => "Algol 68", "algol60" => "Algol 60", "pascal" => "Pascal", _ => "Simula I" };
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
        Viewer.Render(doc, App.IsDarkTheme ? MarkdownColors.Dark : MarkdownColors.Light);
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

    void OnTocClick(object? sender, TappedEventArgs e) => GoToSelected();

    void OnTocKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) GoToSelected();
    }

    void GoToSelected()
    {
        if (TocList.SelectedItem is TocEntry entry) Viewer.ScrollTo(entry.Id);
    }

    void OnLinkClicked(string s)
    {
        // Internal anchors (#id) and links to the other help files
        if (s.StartsWith('#'))
        {
            Viewer.ScrollTo(s[1..]);
            return;
        }
        foreach (var topic in new[] { "simula1", "simula", "algol68", "algol60", "pascal" })
        {
            var file = topic + ".md";
            if (s.EndsWith(file, StringComparison.OrdinalIgnoreCase) || s.Contains(file + "#", StringComparison.OrdinalIgnoreCase))
            {
                // "simula.md" must not match "…simula1.md": check the character before the file name
                int at = s.LastIndexOf(file, StringComparison.OrdinalIgnoreCase);
                if (at > 0 && char.IsLetterOrDigit(s[at - 1])) continue;
                ShowTopic(topic);
                int hash = s.IndexOf('#');
                if (hash >= 0) Viewer.ScrollTo(s[(hash + 1)..]);
                return;
            }
        }
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
        {
            try { App.Platform.OpenUrl(uri.AbsoluteUri); } catch { }
        }
    }

    public const string ShortcutsMarkdown = @"# Keyboard shortcuts

On macOS, <kbd>Cmd</kbd> replaces <kbd>Ctrl</kbd> in these shortcuts, except <kbd>Ctrl+Space</kbd> (completion),
<kbd>Ctrl+Alt+Pause</kbd> and <kbd>Ctrl+Z</kbd> in the console input (end of file).

## Files

| Shortcut | Action |
|---|---|
| <kbd>Ctrl+N</kbd> | New Simula 67 program |
| <kbd>Ctrl+Shift+N</kbd> | New Algol 68 program |
| (menu only) | **File › New Algol 60 program** / **New Simula I program** / **New Pascal program** |
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
| <kbd>Ctrl+Shift+E</kbd> | Generate a standalone executable (.exe on Windows) |
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

* **File › New Simula 67 program** (<kbd>Ctrl+N</kbd>), **Algol 68** (<kbd>Ctrl+Shift+N</kbd>), **Algol 60**, **Simula I** or **Pascal** creates a tab with a template.
* The language is chosen from the extension (`.sim` Simula 67, `.a68` Algol 68, `.a60` or `.alg` Algol 60, `.s1` Simula I, `.pas` or `.p` Pascal); it can be changed in **Build › File language** or by clicking the language name in the status bar.
* The **Examples** menu opens the programs of the `samples` folder (one submenu per language).
* Files can be dragged and dropped into the window; a dropped folder becomes the root of the explorer.
* The session (open tabs, breakpoints, caret position, layout) is restored at start-up.

## Simula 67

Keywords are case-insensitive. Comments: `comment … ;`, `! … ;` and the text that follows `end` up to the next `;`, `end`, `else`, `when` or `otherwise`.

## Algol 68

UPPER stropping: upper-case words (`BEGIN`, `INT`, `PROC`…) are bold words; QUOTE stropping (`'BEGIN'`) is recognised too.
Comments: `CO … CO`, `COMMENT … COMMENT`, `# … #`.

## Algol 60

Two representations: reserved words (`begin`, `real`, `Boolean`…, case-insensitive) or quote stropping (`'BEGIN'`, `'REAL'`…), chosen automatically when the text contains `'BEGIN'` or `'COMMENT'`.
Comments: `comment … ;` and the text that follows `end` up to the next `;`, `end` or `else`.
Strings: `""text""` (escapes `\n`, `\t`, `\""`, `\\`), `` `text' `` and `‘text’`. The publication symbols `× ÷ ↑ ≤ ≥ ≠ ¬ ∧ ∨ ⊃ ≡ ⏨` are accepted as well as their ASCII forms.
Input and output go through `outstring`, `outinteger`, `outreal`, `ininteger`, `inreal`, `inchar`… (channel 1 is the console). See **Help › Algol 60 help**.

## Simula I

Algol 60 extended with the `SIMULA begin … end` block, `activity` declarations, `element` and `set` variables, `activate`, `hold`, `passivate`, `wait`, `inspect … when … otherwise`…
Comments after `end` also stop at `when` and `otherwise`. There is no `text` type: strings are written with `outstring`. See **Help › Simula I help**.

## Pascal

The Pascal of the Revised Report (Jensen and Wirth), as implemented by Pascal 6000 on the CDC 6600: reserved words are case-insensitive,
comments are written `{ … }` or `(* … *)`, strings `'text'` (a quote is doubled: `'don''t'`), `(.` `.)` may replace `[` `]` and `@` may replace `^`.
A program starts with its heading, e.g. `program hello(input, output);`, and ends with `end.`; files listed in the heading other than
`input` and `output` are files of the working directory. See **Help › Pascal help**.

## Build and run

* **Build** (<kbd>F7</kbd>) compiles the current file: diagnostics appear in the **Output**, in the **error list** (double-click to go to the line) and are underlined in the editor. A background check underlines errors while you type.
* **Run** (<kbd>Ctrl+F5</kbd>) starts the program in the **integrated console**: output appears as it is produced, typed lines are sent to the program, <kbd>Ctrl+Z</kbd> sends end of file, **Stop** (<kbd>Shift+F5</kbd>) interrupts the run.
* The console understands the usual **ANSI/VT escape sequences** (character 27, ESC), so a program can redraw a text screen in place (animations, games): `ESC[2J` clear screen (`ESC[J` to the end, `ESC[3J` ignored), `ESC[H` / `ESC[row;colH` (or `f`) cursor position (1-based, from the start of the program output), `ESC[A` `B` `C` `D` cursor moves, `ESC[K` clear to end of line (`ESC[1K`, `ESC[2K`), `ESC[s` / `ESC[u` save / restore the cursor, `ESC[?25l` / `ESC[?25h` (accepted), `ESC[…m` colours 30–37, 90–97, bold and reset 0 (other attributes are ignored). After a cursor move, text overwrites the characters in place; `\r` returns to column 1. In Simula 67: `outchar(char(27)); outtext(""[H"")`. The same sequences work in the external console and in the generated executables.
* The **Run in external console** option (toolbar button) opens a real console window: a Windows console, a Terminal window on macOS, a terminal emulator on Linux (x-terminal-emulator, gnome-terminal, konsole or xterm).
* **Generate a standalone executable** produces a program that can be copied and run on its own (the .NET 8 runtime must be installed): an `.exe` file on Windows, an executable file without extension on macOS and Linux.

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
a60c program.a60 [-o program.exe] [--run] [--no-debug]
s1c  program.s1  [-o program.exe] [--run] [--no-debug]
pasc program.pas [-o program.exe] [--run] [--no-debug]
```
";
}
