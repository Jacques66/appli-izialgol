using System.Windows;

namespace Studio.Ide.Dialogs;

public partial class GoToLineWindow : Window
{
    readonly int _max;

    public GoToLineWindow(int current, int max)
    {
        InitializeComponent();
        _max = max;
        Prompt.Text = $"Line number (1 – {max}):";
        LineBox.Text = current.ToString();
        Loaded += (_, _) => { LineBox.Focus(); LineBox.SelectAll(); };
    }

    public int Line { get; private set; }

    void OnOk(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(LineBox.Text.Trim(), out var n) && n >= 1 && n <= _max)
        {
            Line = n;
            DialogResult = true;
        }
        else
        {
            ErrorText.Text = $"Enter a number between 1 and {_max}.";
            ErrorText.Visibility = Visibility.Visible;
            LineBox.Focus();
            LineBox.SelectAll();
        }
    }
}
