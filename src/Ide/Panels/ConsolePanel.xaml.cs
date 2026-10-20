using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Studio.Ide.Editor;

namespace Studio.Ide.Panels;

public enum ConsoleTextKind { Program, Input, Info, Error }

/// <summary>Console integree : sortie du programme en continu, saisie de lignes, fin de fichier.</summary>
public partial class ConsolePanel : UserControl
{
    const int MaxChars = 4_000_000;

    sealed class Segment : TextSegment
    {
        public ConsoleTextKind Kind;
    }

    readonly TextSegmentCollection<Segment> _segments;
    readonly List<string> _history = new();
    int _historyIndex;
    bool _running;

    public ConsolePanel()
    {
        InitializeComponent();
        _segments = new TextSegmentCollection<Segment>(Output.Document);
        Output.TextArea.TextView.LineTransformers.Add(new Colorizer(this));
        Output.Options.EnableHyperlinks = false;
        Output.Options.EnableEmailHyperlinks = false;
        Output.TextArea.Caret.CaretBrush = Brushes.Transparent;
        // Taper dans la zone de sortie envoie la frappe vers la saisie
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

    /// <summary>Ligne saisie (null = fin de fichier).</summary>
    public event Action<string?>? InputSubmitted;
    public event Action? StopRequested;
    public event Action<bool>? ClearOnRunChanged;

    public string Text => Output.Text;

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
    }

    public void Append(string text, ConsoleTextKind kind = ConsoleTextKind.Program)
    {
        if (string.IsNullOrEmpty(text)) return;
        var doc = Output.Document;
        if (doc.TextLength + text.Length > MaxChars)
        {
            int remove = Math.Min(doc.TextLength, doc.TextLength + text.Length - MaxChars + MaxChars / 4);
            doc.Remove(0, remove);
        }
        int start = doc.TextLength;
        doc.Insert(start, text);
        if (kind != ConsoleTextKind.Program)
            _segments.Add(new Segment { StartOffset = start, Length = text.Length, Kind = kind });
        Output.ScrollToEnd();
    }

    /// <summary>Ajoute une ligne de message (en commencant une nouvelle ligne si besoin).</summary>
    public void AppendLine(string text, ConsoleTextKind kind)
    {
        var doc = Output.Document;
        if (doc.TextLength > 0 && doc.GetCharAt(doc.TextLength - 1) != '\n') Append("\n", ConsoleTextKind.Program);
        Append(text + "\n", kind);
    }

    public void SetRunning(bool running)
    {
        _running = running;
        Input.IsEnabled = running;
        SendButton.IsEnabled = running;
        EofButton.IsEnabled = running;
        StopButton.IsEnabled = running;
        if (!running) SetWaiting(false);
        StatusText.Text = running ? "Programme en cours d'exécution" : "";
    }

    public void SetWaiting(bool waiting)
    {
        if (!_running) waiting = false;
        StatusText.Text = waiting ? "En attente d'une saisie…" : _running ? "Programme en cours d'exécution" : "";
        Prompt.Foreground = waiting ? (Brush)FindResource("Brush.Accent") : (Brush)FindResource("Brush.TextDim");
        if (waiting && IsVisible) Input.Focus();
    }

    public void FocusInput()
    {
        if (Input.IsEnabled) Input.Focus();
    }

    /// <summary>Envoie une ligne au programme (utilise aussi par l'auto-test).</summary>
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
                    _ => t.ConsoleInfo,
                };
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
}
