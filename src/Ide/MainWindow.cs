using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Studio.Ide;

/// <summary>
/// Main window of the desktop: hosts the <see cref="MainView"/> (the whole IDE), restores and saves its
/// placement, asks before closing and reloads the files changed outside the editor when it is activated.
/// The browser shows the <see cref="MainView"/> directly.
/// </summary>
public sealed class MainWindow : Window
{
    bool _closingConfirmed;
    /// <summary>Position and size of the window in the normal state (saved when it is maximized).</summary>
    PixelPoint _normalPosition;
    Size _normalSize;

    public MainView View { get; }

    public MainWindow()
    {
        View = new MainView();
        Content = View;
        MinWidth = 640;
        MinHeight = 420;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://AlgolStudio.Ide/Assets/app.ico")));
        this.Bind(TitleProperty, View.GetObservable(MainView.TitleProperty));
        this.Bind(BackgroundProperty, this.GetResourceObservable("Brush.WindowBackground"));
        RestoreWindowPlacement();

        Closing += OnClosing;
        Activated += (_, _) => View.CheckExternalChanges();
        PositionChanged += (_, _) => { if (WindowState == WindowState.Normal) _normalPosition = Position; };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ClientSizeProperty && WindowState == WindowState.Normal) _normalSize = ClientSize;
        };
    }

    void RestoreWindowPlacement()
    {
        if (App.Options.Automated) { Width = 1280; Height = 800; WindowStartupLocation = WindowStartupLocation.CenterScreen; return; }
        var settings = View.Settings;
        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);
        _normalSize = new Size(Width, Height);
        if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
        {
            var pos = new PixelPoint((int)settings.WindowLeft, (int)settings.WindowTop);
            // Only on a connected screen (title bar visible)
            bool onScreen = Screens.All.Any(s => s.Bounds.Contains(new PixelPoint(pos.X + 50, pos.Y + 10)));
            if (onScreen)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = pos;
                _normalPosition = pos;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingConfirmed) return;
        if (!View.CanClose()) { e.Cancel = true; return; }
        SaveSession();
        _closingConfirmed = true;
        View.CloseDialogs();
    }

    /// <summary>Saves the window placement and the session of the view in the settings.</summary>
    public void SaveSession()
    {
        var settings = View.Settings;
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        var position = WindowState == WindowState.Normal ? Position : _normalPosition;
        var size = WindowState == WindowState.Normal ? ClientSize : _normalSize;
        if (size.Width > 0 && size.Height > 0)
        {
            settings.WindowLeft = position.X;
            settings.WindowTop = position.Y;
            settings.WindowWidth = size.Width;
            settings.WindowHeight = size.Height;
        }
        View.SaveSession();
    }
}
