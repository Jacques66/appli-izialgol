using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Studio.Ide.Dialogs;

/// <summary>Go to line: ShowDialog&lt;bool&gt; returns true when a valid line was entered (<see cref="Line"/>).</summary>
public partial class GoToLineWindow : DialogWindow
{
    readonly int _max;

    // Constructor for the XAML previewer.
    public GoToLineWindow() : this(1, 1) { }

    public GoToLineWindow(int current, int max)
    {
        InitializeComponent();
        _max = max;
        Prompt.Text = $"Line number (1 – {max}):";
        LineBox.Text = current.ToString();
        Opened += (_, _) => { LineBox.Focus(); LineBox.SelectAll(); };
    }

    public int Line { get; private set; }

    void OnOk(object? sender, RoutedEventArgs e)
    {
        if (int.TryParse((LineBox.Text ?? "").Trim(), out var n) && n >= 1 && n <= _max)
        {
            Line = n;
            Close(true);
        }
        else
        {
            ErrorText.Text = $"Enter a number between 1 and {_max}.";
            ErrorText.IsVisible = true;
            LineBox.Focus();
            LineBox.SelectAll();
        }
    }

    void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
