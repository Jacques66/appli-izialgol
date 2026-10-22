using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Studio.Ide.Services;

namespace Studio.Ide.Dialogs;

/// <summary>Find / replace (modeless) in the active editor.</summary>
public partial class FindReplaceWindow : DialogWindow
{
    readonly MainView _main;
    readonly SearchOptions _options;

    // Constructor for the XAML previewer.
    public FindReplaceWindow() : this(null!, new SearchOptions()) { }

    public FindReplaceWindow(MainView main, SearchOptions options)
    {
        InitializeComponent();
        _main = main;
        _options = options;
        FindBox.Text = options.Text;
        ReplaceBox.Text = options.Replacement;
        MatchCaseBox.IsChecked = options.MatchCase;
        WholeWordBox.IsChecked = options.WholeWord;
        RegexBox.IsChecked = options.UseRegex;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.F3) { Find(e.KeyModifiers == KeyModifiers.Shift); e.Handled = true; }
            else if (e.Key == Key.Escape) { OnClose(null, e); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }

    public void ShowMode(bool replace)
    {
        Title = replace ? "Replace" : "Find";
        ReplaceLabel.IsVisible = ReplaceBox.IsVisible = ReplaceButton.IsVisible = ReplaceAllButton.IsVisible = replace;
        FindBox.Text = _options.Text;
        StatusText.Text = "";
        Dispatcher.UIThread.Post(() => { FindBox.Focus(); FindBox.SelectAll(); }, DispatcherPriority.Input);
    }

    void Sync()
    {
        _options.Text = FindBox.Text ?? "";
        _options.Replacement = ReplaceBox.Text ?? "";
        _options.MatchCase = MatchCaseBox.IsChecked == true;
        _options.WholeWord = WholeWordBox.IsChecked == true;
        _options.UseRegex = RegexBox.IsChecked == true;
    }

    void Find(bool backwards)
    {
        Sync();
        StatusText.Text = _main.FindNext(backwards);
    }

    void OnNext(object? sender, RoutedEventArgs e) => Find(false);
    void OnPrevious(object? sender, RoutedEventArgs e) => Find(true);

    void OnReplace(object? sender, RoutedEventArgs e)
    {
        Sync();
        StatusText.Text = _main.ReplaceCurrent();
    }

    void OnReplaceAll(object? sender, RoutedEventArgs e)
    {
        Sync();
        StatusText.Text = _main.ReplaceAll();
    }

    void OnClose(object? sender, RoutedEventArgs e)
    {
        Sync();
        Close();
        _main.Activate();
        _main.ActiveDocument?.Editor.TextArea.Focus();
    }
}
