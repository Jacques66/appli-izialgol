using System.Windows;
using System.Windows.Input;
using Studio.Ide.Services;

namespace Studio.Ide.Dialogs;

/// <summary>Rechercher / remplacer (non modal) dans l'editeur actif.</summary>
public partial class FindReplaceWindow : Window
{
    readonly MainWindow _main;
    readonly SearchOptions _options;

    public FindReplaceWindow(MainWindow main, SearchOptions options)
    {
        InitializeComponent();
        _main = main;
        _options = options;
        FindBox.Text = options.Text;
        ReplaceBox.Text = options.Replacement;
        MatchCaseBox.IsChecked = options.MatchCase;
        WholeWordBox.IsChecked = options.WholeWord;
        RegexBox.IsChecked = options.UseRegex;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F3) { Find(Keyboard.Modifiers == ModifierKeys.Shift); e.Handled = true; }
        };
    }

    public void ShowMode(bool replace)
    {
        Title = replace ? "Remplacer" : "Rechercher";
        var v = replace ? Visibility.Visible : Visibility.Collapsed;
        ReplaceLabel.Visibility = ReplaceBox.Visibility = ReplaceButton.Visibility = ReplaceAllButton.Visibility = v;
        FindBox.Text = _options.Text;
        StatusText.Text = "";
        Dispatcher.BeginInvoke(new Action(() => { FindBox.Focus(); FindBox.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
    }

    void Sync()
    {
        _options.Text = FindBox.Text;
        _options.Replacement = ReplaceBox.Text;
        _options.MatchCase = MatchCaseBox.IsChecked == true;
        _options.WholeWord = WholeWordBox.IsChecked == true;
        _options.UseRegex = RegexBox.IsChecked == true;
    }

    void Find(bool backwards)
    {
        Sync();
        StatusText.Text = _main.FindNext(backwards);
    }

    void OnNext(object sender, RoutedEventArgs e) => Find(false);
    void OnPrevious(object sender, RoutedEventArgs e) => Find(true);

    void OnReplace(object sender, RoutedEventArgs e)
    {
        Sync();
        StatusText.Text = _main.ReplaceCurrent();
    }

    void OnReplaceAll(object sender, RoutedEventArgs e)
    {
        Sync();
        StatusText.Text = _main.ReplaceAll();
    }

    void OnClose(object sender, RoutedEventArgs e)
    {
        Sync();
        Close();
        _main.Activate();
        _main.ActiveDocument?.Editor.TextArea.Focus();
    }
}
