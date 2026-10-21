using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Studio.Ide.Editor;

namespace Studio.Ide.Panels;

public enum ConsoleTextKind { Program, Input, Info, Error }

/// <summary>
/// Integrated console: continuous program output, line input, end of file.
/// Program output goes through a small ANSI/VT terminal emulator (<see cref="AnsiTerminal"/>):
/// cursor positioning, erase and colour sequences redraw the text in place.
/// </summary>
public partial class ConsolePanel : UserControl
{
    const int MaxChars = 4_000_000;

    sealed class Segment : TextSegment
    {
        public ConsoleTextKind Kind;
        /// <summary>ANSI colour (0..15) of a Program segment.</summary>
        public int Color = -1;
    }

    readonly TextSegmentCollection<Segment> _segments;
    readonly AnsiTerminal _terminal;
    readonly List<string> _history = new();
    int _historyIndex;
    bool _running;

    public ConsolePanel()
    {
        InitializeComponent();
        _segments = new TextSegmentCollection<Segment>(Output.Document);
        // The console is read-only: no undo history (in-place redraws would make it grow endlessly).
        Output.Document.UndoStack.SizeLimit = 0;
        _terminal = new AnsiTerminal(new DocumentBuffer(Output.Document, _segments));
        Output.TextArea.TextView.LineTransformers.Add(new Colorizer(this));
        Output.Options.EnableHyperlinks = false;
        Output.Options.EnableEmailHyperlinks = false;
        Output.TextArea.Caret.CaretBrush = Brushes.Transparent;
        // Typing in the output area sends the keystrokes to the input box
        Output.TextArea.PreviewTextInput += (_, e) =>
        {
            if (!Input.IsEnabled) return;
            Input.Focus();
            Input.AppendText(e.Text);
            Input.CaretIndex = Input.Text.Length;
            e.Handled = true;
        };
        ApplyTheme();
    }

    /// <summary>Line typed (null = end of file).</summary>
    public event Action<string?>? InputSubmitted;
    public event Action? StopRequested;
    public event Action<bool>? ClearOnRunChanged;

    public string Text => Output.Text;

    /// <summary>Terminal emulator of the program output (exposed for the self-test).</summary>
    public AnsiTerminal Terminal => _terminal;

    public bool ClearOnRun
    {
        get => ClearOnRunBox.IsChecked == true;
        set => ClearOnRunBox.IsChecked = value;
    }

    public void ApplyTheme()
    {
        var t = EditorTheme.Current;
        Output.Background = t.Background;
        Output.Foreground = t.Foreground;
        Output.TextArea.SelectionBrush = t.Selection;
        Output.TextArea.TextView.Redraw();
    }

    public void SetFont(FontFamily family, double size)
    {
        Output.FontFamily = family;
        Output.FontSize = Math.Max(9, size - 1);
        Input.FontFamily = family;
    }

    public void Clear()
    {
        Output.Document.Text = "";
        _segments.Clear();
        _terminal.ResetScreen();
    }

    /// <summary>
    /// Appends text. Program output is interpreted by the terminal emulator (escape sequences,
    /// '\r', overwrite after a cursor move); the other kinds are appended at the end as they are.
    /// </summary>
    public void Append(string text, ConsoleTextKind kind = ConsoleTextKind.Program)
    {
        if (string.IsNullOrEmpty(text)) return;
        var doc = Output.Document;
        if (doc.TextLength + text.Length > MaxChars) TrimStart(doc.TextLength + text.Length - MaxChars + MaxChars / 4);
        if (kind == ConsoleTextKind.Program)
        {
            doc.BeginUpdate();
            try { _terminal.Write(text); }
            finally { doc.EndUpdate(); }
            // a program drawing a screen: long lines must not wrap
            if (_terminal.ScreenMode && Output.WordWrap)
            {
                Output.WordWrap = false;
                Output.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            }
        }
        else AppendRaw(text, kind);
        Output.ScrollToEnd();
    }

    /// <summary>Appends at the end of the document, without interpretation.</summary>
    void AppendRaw(string text, ConsoleTextKind kind)
    {
        var doc = Output.Document;
        int start = doc.TextLength;
        doc.Insert(start, text);
        if (kind != ConsoleTextKind.Program)
            _segments.Add(new Segment { StartOffset = start, Length = text.Length, Kind = kind });
        _terminal.MoveToEnd();
    }

    /// <summary>Removes at least <paramref name="chars"/> characters (whole lines) from the start (size limit).</summary>
    void TrimStart(int chars)
    {
        var doc = Output.Document;
        var line = chars < doc.TextLength ? doc.GetLineByOffset(chars) : null;
        int end = line == null ? doc.TextLength : chars > line.Offset ? line.EndOffset + line.DelimiterLength : line.Offset;
        if (end >= doc.TextLength)
        {
            doc.Text = "";
            _segments.Clear();
            _terminal.ResetScreen();
            return;
        }
        int removedLines = doc.GetLineByOffset(end).LineNumber - 1;
        doc.Remove(0, end);
        _terminal.LinesRemoved(removedLines);
    }

    /// <summary>Appends a message line (starting a new line if needed).</summary>
    public void AppendLine(string text, ConsoleTextKind kind)
    {
        var doc = Output.Document;
        if (doc.TextLength > 0 && doc.GetCharAt(doc.TextLength - 1) != '\n') AppendRaw("\n", ConsoleTextKind.Program);
        AppendRaw(text + "\n", kind);
        Output.ScrollToEnd();
    }

    public void SetRunning(bool running)
    {
        _running = running;
        if (running)
        {
            _terminal.Reset();
            Output.WordWrap = true;
            Output.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
        }
        Input.IsEnabled = running;
        SendButton.IsEnabled = running;
        EofButton.IsEnabled = running;
        StopButton.IsEnabled = running;
        if (!running) SetWaiting(false);
        StatusText.Text = running ? "Program running" : "";
    }

    public bool IsWaiting { get; private set; }

    public void SetWaiting(bool waiting)
    {
        if (!_running) waiting = false;
        IsWaiting = waiting;
        StatusText.Text = waiting ? "Waiting for input…" : _running ? "Program running" : "";
        Prompt.Foreground = waiting ? (Brush)FindResource("Brush.Accent") : (Brush)FindResource("Brush.TextDim");
        if (waiting && IsVisible) Input.Focus();
    }

    public void FocusInput()
    {
        if (Input.IsEnabled) Input.Focus();
    }

    /// <summary>Sends a line to the program (also used by the self-test).</summary>
    public void Submit(string? line)
    {
        if (!_running) return;
        if (line == null) AppendLine("^Z", ConsoleTextKind.Info);
        else
        {
            Append(line + "\n", ConsoleTextKind.Input);
            if (line.Length > 0 && (_history.Count == 0 || _history[^1] != line)) _history.Add(line);
            _historyIndex = _history.Count;
        }
        InputSubmitted?.Invoke(line);
    }

    void OnSend(object sender, RoutedEventArgs e)
    {
        var line = Input.Text;
        Input.Clear();
        Submit(line);
        Input.Focus();
    }

    void OnEof(object sender, RoutedEventArgs e) => Submit(null);
    void OnStop(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
    void OnClear(object sender, RoutedEventArgs e) => Clear();
    void OnClearOnRunChanged(object sender, RoutedEventArgs e) => ClearOnRunChanged?.Invoke(ClearOnRun);

    void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSend(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Submit(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && _history.Count > 0)
        {
            _historyIndex = Math.Max(0, _historyIndex - 1);
            Input.Text = _history[_historyIndex];
            Input.CaretIndex = Input.Text.Length;
            e.Handled = true;
        }
        else if (e.Key == Key.Down && _history.Count > 0)
        {
            _historyIndex = Math.Min(_history.Count, _historyIndex + 1);
            Input.Text = _historyIndex < _history.Count ? _history[_historyIndex] : "";
            Input.CaretIndex = Input.Text.Length;
            e.Handled = true;
        }
    }

    sealed class Colorizer : DocumentColorizingTransformer
    {
        readonly ConsolePanel _owner;
        public Colorizer(ConsolePanel owner) { _owner = owner; }

        protected override void ColorizeLine(DocumentLine line)
        {
            var t = EditorTheme.Current;
            foreach (var seg in _owner._segments.FindOverlappingSegments(line.Offset, line.Length))
            {
                int s = Math.Max(seg.StartOffset, line.Offset), e = Math.Min(seg.EndOffset, line.EndOffset);
                if (e <= s) continue;
                var brush = seg.Kind switch
                {
                    ConsoleTextKind.Input => t.ConsoleInput,
                    ConsoleTextKind.Error => t.ConsoleError,
                    ConsoleTextKind.Program => AnsiBrush(seg.Color, t.IsDark),
                    _ => t.ConsoleInfo,
                };
                if (brush == null) continue;
                ChangeLinePart(s, e, el =>
                {
                    el.TextRunProperties.SetForegroundBrush(brush);
                    if (seg.Kind == ConsoleTextKind.Input)
                    {
                        var tf = el.TextRunProperties.Typeface;
                        el.TextRunProperties.SetTypeface(new Typeface(tf.FontFamily, tf.Style, FontWeights.SemiBold, tf.Stretch));
                    }
                    else if (seg.Kind == ConsoleTextKind.Info)
                    {
                        var tf = el.TextRunProperties.Typeface;
                        el.TextRunProperties.SetTypeface(new Typeface(tf.FontFamily, FontStyles.Italic, tf.Weight, tf.Stretch));
                    }
                });
            }
        }
    }

    // ------------------------------------------------------------------ ANSI colours

    static readonly Brush[] DarkPalette = Palette(
        "#000000", "#CD3131", "#0DBC79", "#E5E510", "#2472C8", "#BC3FBC", "#11A8CD", "#E5E5E5",
        "#666666", "#F14C4C", "#23D18B", "#F5F543", "#3B8EEA", "#D670D6", "#29B8DB", "#FFFFFF");

    static readonly Brush[] LightPalette = Palette(
        "#000000", "#CD3131", "#00BC00", "#949800", "#0451A5", "#BC05BC", "#0598BC", "#555555",
        "#666666", "#CD3131", "#14CE14", "#B5BA00", "#0451A5", "#BC05BC", "#0598BC", "#A5A5A5");

    static Brush[] Palette(params string[] colors) => colors.Select(c =>
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c));
        b.Freeze();
        return (Brush)b;
    }).ToArray();

    static Brush? AnsiBrush(int color, bool dark) =>
        color is >= 0 and < 16 ? (dark ? DarkPalette : LightPalette)[color] : null;

    /// <summary><see cref="ITerminalBuffer"/> over the AvalonEdit document; colours are Program segments.</summary>
    sealed class DocumentBuffer : ITerminalBuffer
    {
        readonly TextDocument _doc;
        readonly TextSegmentCollection<Segment> _segments;

        public DocumentBuffer(TextDocument doc, TextSegmentCollection<Segment> segments)
        {
            _doc = doc;
            _segments = segments;
        }

        int Offset(int line) => _doc.GetLineByNumber(line + 1).Offset;

        public int LineCount => _doc.LineCount;
        public int LineLength(int line) => _doc.GetLineByNumber(line + 1).Length;
        public string GetText(int line, int column, int length) => _doc.GetText(Offset(line) + column, length);
        public void Append(string text) => _doc.Insert(_doc.TextLength, text);

        public void Replace(int line, int column, int length, string text)
        {
            int offset = Offset(line) + column;
            if (text.Length == 0 && length > 0) Recolor(offset, length, -1);
            _doc.Replace(offset, length, text);
        }

        public void DeleteToEnd(int line, int column)
        {
            int offset = Offset(line) + column;
            if (offset >= _doc.TextLength) return;
            Recolor(offset, _doc.TextLength - offset, -1);
            _doc.Remove(offset, _doc.TextLength - offset);
        }

        public void SetForeground(int line, int column, int length, int color)
        {
            int offset = Offset(line) + column;
            length = Math.Min(length, _doc.TextLength - offset);
            if (length > 0) Recolor(offset, length, color);
        }

        /// <summary>Removes the styling of [offset, offset + length), then colours it (color >= 0).</summary>
        void Recolor(int offset, int length, int color)
        {
            int end = offset + length;
            List<Segment>? overlapping = null;
            foreach (var seg in _segments.FindOverlappingSegments(offset, length))
                if (seg.EndOffset > offset && seg.StartOffset < end) (overlapping ??= new()).Add(seg);
            if (overlapping != null)
            {
                foreach (var seg in overlapping)
                {
                    int s = seg.StartOffset, e = seg.EndOffset;
                    _segments.Remove(seg);
                    if (s < offset) _segments.Add(new Segment { StartOffset = s, Length = offset - s, Kind = seg.Kind, Color = seg.Color });
                    if (e > end) _segments.Add(new Segment { StartOffset = end, Length = e - end, Kind = seg.Kind, Color = seg.Color });
                }
            }
            if (color >= 0) _segments.Add(new Segment { StartOffset = offset, Length = length, Kind = ConsoleTextKind.Program, Color = color });
        }
    }
}
