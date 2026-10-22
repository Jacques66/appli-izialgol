using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using Studio.Common;
using Studio.Ide.Language;
using Studio.Ide.Services;
using Studio.Tooling;

namespace Studio.Ide.Editor;

/// <summary>A file open in a tab: AvalonEdit editor and all its services.</summary>
public sealed class EditorDocument : INotifyPropertyChanged
{
    static int _untitledCounter;

    readonly List<TextAnchor> _breakpoints = new();
    readonly DispatcherTimer _analysisTimer;
    readonly SyntaxColorizer _colorizer;
    CodeModel? _model;
    bool _isModified;
    string? _filePath;
    LanguageDefinition _language;
    CompletionWindow? _completion;

    public EditorDocument(LanguageDefinition language, string? filePath, string text, Encoding? encoding = null)
    {
        _language = language;
        _filePath = filePath;
        Encoding = encoding ?? new UTF8Encoding(false);
        if (filePath == null) UntitledName = $"Untitled{++_untitledCounter}{language.DefaultExtension}";
        else UntitledName = "";

        Editor = new TextEditor
        {
            ShowLineNumbers = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = CodeFont.Resolve(IdeSettings.DefaultFontFamily),
            Padding = new Thickness(2, 0, 0, 0),
        };
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.EnableRectangularSelection = true;
        Editor.Options.CutCopyWholeLine = true;
        Editor.Options.AllowToggleOverstrikeMode = true;
        Editor.Options.AllowScrollBelowDocument = false;
        Editor.Document.Text = text;
        Editor.Document.UndoStack.ClearAll();
        Editor.Document.FileName = filePath;

        _colorizer = new SyntaxColorizer(this);
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(new LineBackgroundRenderer(this));
        Brackets = new BracketHighlighter(this);
        Editor.TextArea.TextView.BackgroundRenderers.Add(Brackets);
        Markers = new TextMarkerService(Editor);
        BreakpointMarginControl = new BreakpointMargin(this);
        Editor.TextArea.LeftMargins.Insert(0, BreakpointMarginControl);
        Folding = FoldingManager.Install(Editor.TextArea);
        Editor.TextArea.IndentationStrategy = new SmartIndentation(this);

        Editor.Document.Changed += (_, _) =>
        {
            _model = null;
            IsModified = true;
            _analysisTimer!.Stop();
            _analysisTimer.Start();
            TextChangedSinceAnalysis?.Invoke(this);
        };
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            Brackets.Update(Editor.CaretOffset);
            CaretMoved?.Invoke(this);
        };
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.TextArea.TextEntering += OnTextEntering;

        // Ctrl+Space: completion (bound on the text area so it runs before any other handling;
        // Ctrl on every platform: Cmd+Space is the system search on macOS)
        Editor.TextArea.KeyBindings.Add(new KeyBinding { Command = new RelayCommand(_ => ShowCompletion()), Gesture = new KeyGesture(Key.Space, KeyModifiers.Control) });

        _analysisTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
        _analysisTimer.Tick += (_, _) => { _analysisTimer.Stop(); RefreshAnalysis(); };

        IsModified = false;
        RefreshAnalysis();
    }

    // ------------------------------------------------------------------ properties

    public TextEditor Editor { get; }
    public TextMarkerService Markers { get; }
    public FoldingManager Folding { get; }
    public BracketHighlighter Brackets { get; }
    public BreakpointMargin BreakpointMarginControl { get; }
    public TabItem? Tab { get; set; }
    public Encoding Encoding { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public string UntitledName { get; private set; }
    public string IndentUnit { get; set; } = "   ";

    public event Action<EditorDocument>? ModelUpdated;
    public event Action<EditorDocument>? CaretMoved;
    public event Action<EditorDocument>? BreakpointsChanged;
    public event Action<EditorDocument>? TextChangedSinceAnalysis;
    public event PropertyChangedEventHandler? PropertyChanged;

    void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string? FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            Editor.Document.FileName = value;
            Notify(); Notify(nameof(DisplayName)); Notify(nameof(Title)); Notify(nameof(CompilePath));
        }
    }

    public bool IsModified
    {
        get => _isModified;
        set { if (_isModified != value) { _isModified = value; Notify(); Notify(nameof(Title)); } }
    }

    public LanguageDefinition Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            _model = null;
            RefreshAnalysis();
            Editor.TextArea.TextView.Redraw();
            Notify();
        }
    }

    public ILanguageService? Service => LanguageRegistry.FindById(_language.Id);

    public string DisplayName => _filePath != null ? Path.GetFileName(_filePath) : UntitledName;
    public string Title => DisplayName + (IsModified ? " •" : "");
    /// <summary>Path passed to the compiler (full path, or temporary name for an unsaved file).</summary>
    public string CompilePath => _filePath ?? UntitledName;
    public string? Directory => _filePath != null ? Path.GetDirectoryName(_filePath) : null;

    /// <summary>Analysis of the current text (recomputed on demand after each change).</summary>
    public CodeModel Model => _model ??= _language.Analyze(Editor.Document.Text);

    // ------------------------------------------------------------------ analysis

    public void RefreshAnalysis()
    {
        var m = Model;
        try
        {
            var folds = m.Folds
                .Where(f => f.EndOffset <= Editor.Document.TextLength && f.StartOffset < f.EndOffset)
                .OrderBy(f => f.StartOffset)
                .Select(f => new NewFolding(f.StartOffset, f.EndOffset) { Name = " " + f.Title + " " })
                .ToList();
            Folding.UpdateFoldings(folds, -1);
        }
        catch (Exception ex) { Log.Write("folding: " + ex.Message); }
        Brackets.Update(Editor.CaretOffset);
        ModelUpdated?.Invoke(this);
        BreakpointsChanged?.Invoke(this);
    }

    // ------------------------------------------------------------------ breakpoints

    public HashSet<int> BreakpointLineSet()
    {
        var set = new HashSet<int>();
        foreach (var a in _breakpoints) if (!a.IsDeleted) set.Add(a.Line);
        return set;
    }

    public List<int> BreakpointLines => BreakpointLineSet().OrderBy(l => l).ToList();

    public void ToggleBreakpoint(int line)
    {
        if (line < 1 || line > Editor.Document.LineCount) return;
        var existing = _breakpoints.Where(a => !a.IsDeleted && a.Line == line).ToList();
        if (existing.Count > 0) _breakpoints.RemoveAll(existing.Contains);
        else AddBreakpointAnchor(line);
        RedrawMarkers();
        BreakpointsChanged?.Invoke(this);
    }

    public void SetBreakpoints(IEnumerable<int> lines)
    {
        _breakpoints.Clear();
        foreach (var l in lines.Distinct())
            if (l >= 1 && l <= Editor.Document.LineCount) AddBreakpointAnchor(l);
        RedrawMarkers();
        BreakpointsChanged?.Invoke(this);
    }

    void AddBreakpointAnchor(int line)
    {
        var anchor = Editor.Document.CreateAnchor(Editor.Document.GetLineByNumber(line).Offset);
        anchor.MovementType = AnchorMovementType.AfterInsertion;
        anchor.SurviveDeletion = true;
        _breakpoints.Add(anchor);
    }

    // ------------------------------------------------------------------ execution line

    public int? ExecutionLine { get; private set; }
    public bool ExecutionIsTopFrame { get; private set; } = true;

    public void SetExecutionLine(int? line, bool topFrame = true)
    {
        ExecutionLine = line;
        ExecutionIsTopFrame = topFrame;
        RedrawMarkers();
    }

    public void RedrawMarkers()
    {
        Editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
        BreakpointMarginControl.InvalidateVisual();
    }

    // ------------------------------------------------------------------ navigation

    public void GoTo(int line, int column = 1, bool select = false)
    {
        var doc = Editor.Document;
        if (doc.LineCount == 0) return;
        line = Math.Clamp(line, 1, doc.LineCount);
        var dl = doc.GetLineByNumber(line);
        column = Math.Clamp(column, 1, dl.Length + 1);
        Editor.TextArea.Caret.Location = new TextLocation(line, column);
        // Unfold the region containing the line
        foreach (var f in Folding.GetFoldingsContaining(dl.Offset)) f.IsFolded = false;
        if (select && dl.Length > 0)
        {
            var text = doc.GetText(dl);
            int ind = text.Length - text.TrimStart().Length;
            Editor.Select(dl.Offset + ind, text.Trim().Length);
        }
        Editor.ScrollTo(line, column);
        Editor.TextArea.Caret.BringCaretToView();
    }

    // ------------------------------------------------------------------ editing

    void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (_completion != null && !string.IsNullOrEmpty(e.Text) && !char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '_')
            _completion.CompletionList.RequestInsertion(e);
    }

    void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not { Length: 1 } || !char.IsLetter(e.Text[0])) return;
        var line = Editor.Document.GetLineByOffset(Editor.CaretOffset);
        var text = Editor.Document.GetText(line).Trim();
        if (text.Length == 0 || text.Length > 9 || text.Contains(' ')) return;
        if (IsReindentWord(text) || (text.Length > 1 && IsReindentWord(text[..^1])))
            Editor.TextArea.IndentationStrategy?.IndentLine(Editor.Document, line);
    }

    bool IsReindentWord(string w) => _language.Id switch
    {
        "simula" => string.Equals(w, "end", StringComparison.OrdinalIgnoreCase),
        "algol68" => w is "END" or "FI" or "OD" or "ESAC" or "ELSE" or "ELIF" or "THEN" or "OUT" or "OUSE",
        // Algol 60 / Simula I: also 'END (quote stropping)
        "algol60" or "simula1" => string.Equals(w.TrimStart('\''), "end", StringComparison.OrdinalIgnoreCase),
        "pascal" => string.Equals(w, "end", StringComparison.OrdinalIgnoreCase) || string.Equals(w, "until", StringComparison.OrdinalIgnoreCase),
        _ => string.Equals(w, "end", StringComparison.OrdinalIgnoreCase),
    };

    public void CloseCompletion() => _completion?.Close();

    public void ShowCompletion()
    {
        var doc = Editor.Document;
        int caret = Editor.CaretOffset;
        int start = caret;
        while (start > 0 && (char.IsLetterOrDigit(doc.GetCharAt(start - 1)) || doc.GetCharAt(start - 1) == '_')) start--;
        // Algol 68 QUOTE stropping: include the opening apostrophe
        string prefix = doc.GetText(start, caret - start);

        _completion = new CompletionWindow(Editor.TextArea)
        {
            StartOffset = start,
            EndOffset = caret,
            CloseWhenCaretAtBeginning = true,
        };
        var background = App.Resource<IBrush>("Brush.MenuBackground");
        var foreground = App.Resource<IBrush>("Brush.Text");
        _completion.CompletionList.Background = background;
        _completion.CompletionList.Foreground = foreground;
        _completion.CompletionList.BorderBrush = App.Resource<IBrush>("Brush.MenuBorder");
        _completion.CompletionList.ListBox.Background = background;
        _completion.CompletionList.ListBox.Foreground = foreground;
        var data = _completion.CompletionList.CompletionData;
        var seen = new HashSet<string>(_language.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        var currentWord = prefix;
        foreach (var id in Model.Identifiers.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            if (id != currentWord && seen.Add(id)) data.Add(new CompletionData(id, "identifier in this file", 1));
        foreach (var w in _language.CompletionWords)
            if (seen.Add(w)) data.Add(new CompletionData(w, _language.DisplayName + " keyword", 0));
        if (data.Count == 0) { _completion = null; return; }
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
        if (prefix.Length > 0) _completion.CompletionList.SelectItem(prefix);
    }

    /// <summary>Comments or uncomments the selected lines.</summary>
    public void ToggleComment(bool? comment = null)
    {
        var doc = Editor.Document;
        var sel = Editor.TextArea.Selection;
        int startLine, endLine;
        if (sel.IsEmpty) startLine = endLine = Editor.TextArea.Caret.Line;
        else
        {
            startLine = doc.GetLineByOffset(Editor.SelectionStart).LineNumber;
            var endOffset = Editor.SelectionStart + Editor.SelectionLength;
            var endDl = doc.GetLineByOffset(endOffset);
            endLine = endDl.LineNumber;
            if (endDl.Offset == endOffset && endLine > startLine) endLine--;
        }
        var lines = Enumerable.Range(startLine, endLine - startLine + 1).Select(doc.GetLineByNumber).ToList();
        var nonEmpty = lines.Where(l => doc.GetText(l).Trim().Length > 0).ToList();
        if (nonEmpty.Count == 0) return;
        bool doComment = comment ?? !nonEmpty.All(l => _language.IsCommentedLine(doc.GetText(l)));
        var documentText = doc.Text;
        using (doc.RunUpdate())
        {
            foreach (var l in nonEmpty)
            {
                var t = doc.GetText(l);
                var nt = doComment ? _language.CommentLine(t, documentText) : _language.UncommentLine(t);
                if (nt != t) doc.Replace(l.Offset, l.Length, nt);
            }
        }
        var first = doc.GetLineByNumber(startLine);
        var last = doc.GetLineByNumber(endLine);
        Editor.Select(first.Offset, last.EndOffset - first.Offset);
    }

    // ------------------------------------------------------------------ appearance

    public void ApplySettings(IdeSettings s)
    {
        try { Editor.FontFamily = CodeFont.Resolve(s.FontFamily); } catch { }
        Editor.FontSize = s.FontSize;
        Editor.WordWrap = s.WordWrap;
        Editor.ShowLineNumbers = s.ShowLineNumbers;
        Editor.Options.ShowSpaces = s.ShowWhitespace;
        Editor.Options.ShowTabs = s.ShowWhitespace;
        Editor.Options.IndentationSize = s.IndentSize;
        IndentUnit = new string(' ', s.IndentSize);
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        var t = EditorTheme.Current;
        Editor.Background = t.Background;
        Editor.Foreground = t.Foreground;
        Editor.LineNumbersForeground = t.LineNumbers;
        Editor.TextArea.SelectionBrush = t.Selection;
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.TextView.CurrentLineBackground = t.CurrentLine;
        Editor.TextArea.TextView.CurrentLineBorder = new ImmutablePen(t.CurrentLine.ToImmutable(), 1);
        Editor.TextArea.Caret.CaretBrush = t.Foreground;
        foreach (var margin in Editor.TextArea.LeftMargins.OfType<FoldingMargin>())
        {
            margin.FoldingMarkerBrush = t.FoldingMarker;
            margin.FoldingMarkerBackgroundBrush = t.FoldingBackground;
            margin.SelectedFoldingMarkerBrush = t.Foreground;
            margin.SelectedFoldingMarkerBackgroundBrush = t.FoldingBackground;
        }
        Editor.TextArea.TextView.Redraw();
        BreakpointMarginControl.InvalidateVisual();
    }

    // ------------------------------------------------------------------ files

    public static (string Text, Encoding Encoding) ReadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(true).GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true));
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
        try
        {
            return (new UTF8Encoding(false, true).GetString(bytes), new UTF8Encoding(false));
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.Latin1.GetString(bytes), Encoding.Latin1);
        }
    }

    public void Save(string path)
    {
        var text = Editor.Document.Text;
        var bytes = Encoding.GetBytes(text);
        var preamble = Encoding.GetPreamble();
        using (var fs = File.Create(path))
        {
            fs.Write(preamble);
            fs.Write(bytes);
        }
        FilePath = path;
        LastWriteUtc = File.GetLastWriteTimeUtc(path);
        IsModified = false;
        var lang = LanguageDefinitions.ByExtension(path);
        if (lang != null && lang != _language && (lang.Id != "echo" || LanguageRegistry.TestLanguageEnabled)) Language = lang;
    }

    public void Reload()
    {
        if (_filePath == null) return;
        var (text, enc) = ReadFile(_filePath);
        int caret = Editor.CaretOffset;
        Editor.Document.Text = text;
        Encoding = enc;
        Editor.CaretOffset = Math.Min(caret, text.Length);
        LastWriteUtc = File.GetLastWriteTimeUtc(_filePath);
        IsModified = false;
    }
}
