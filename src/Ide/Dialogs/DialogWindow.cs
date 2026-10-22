using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using IconPath = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;

namespace Studio.Ide.Dialogs;

/// <summary>
/// Dialog written once for every platform: a window of its own on the desktop, a panel floating over the main
/// view in the browser (single-view lifetime, where there are no windows). Its members follow those of
/// <see cref="Window"/> (Title, SizeToContent, Show, ShowDialog, Close, Opened, Closed…), so that the XAML and the
/// code of the dialogs stay the same. Width, Height and their minimum and maximum are those of the window (panel).
/// </summary>
public class DialogWindow : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DialogWindow, string?>(nameof(Title));

    static int _zIndex;

    Window? _window;
    Panel? _overlay;
    OverlayLayer? _layer;
    object? _result;
    TaskCompletionSource<object?>? _closed;

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public SizeToContent SizeToContent { get; set; }
    public bool CanResize { get; set; } = true;
    public bool ShowInTaskbar { get; set; } = true;
    public WindowStartupLocation WindowStartupLocation { get; set; }
    public WindowIcon? Icon { get; set; }

    /// <summary>Raised when the dialog is shown.</summary>
    public event EventHandler? Opened;

    /// <summary>Raised when the dialog is closed.</summary>
    public event EventHandler? Closed;

    /// <summary>The application has no windows (browser): dialogs float over the main view.</summary>
    public static bool UsesOverlays => Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime;

    /// <summary>Shows the dialog without waiting (modeless).</summary>
    public void Show(Visual? owner = null)
    {
        if (UsesOverlays)
        {
            ShowOverlay(owner, modal: false);
            return;
        }
        var window = CreateWindow(owner);
        if (OwnerWindow(owner) is { } o) window.Show(o);
        else window.Show();
    }

    /// <summary>Shows the dialog (modal when there is an owner) and completes when it is closed.</summary>
    public Task ShowDialog(Visual? owner) => ShowDialog<object?>(owner);

    /// <summary>Shows the dialog (modal when there is an owner) and returns the result given to <see cref="Close(object?)"/>.</summary>
    public async Task<T> ShowDialog<T>(Visual? owner)
    {
        _closed = new TaskCompletionSource<object?>();
        if (UsesOverlays) ShowOverlay(owner, modal: true);
        else
        {
            var window = CreateWindow(owner);
            if (OwnerWindow(owner) is { IsVisible: true } o) return await window.ShowDialog<T>(o);
            window.Show();
        }
        return await _closed.Task is T t ? t : default!;
    }

    /// <summary>Closes the dialog.</summary>
    public void Close() => Close(null);

    /// <summary>Closes the dialog with a result (see <see cref="ShowDialog{T}"/>).</summary>
    public void Close(object? result)
    {
        _result = result;
        if (_window != null) _window.Close(result);
        else if (_overlay != null) CloseOverlay();
    }

    /// <summary>Brings the dialog to the front.</summary>
    public void Activate()
    {
        if (_window != null) _window.Activate();
        else if (_overlay != null) _overlay.ZIndex = ++_zIndex;
    }

    static Window? OwnerWindow(Visual? owner) => owner == null ? null : TopLevel.GetTopLevel(owner) as Window;

    void OnClosed()
    {
        Closed?.Invoke(this, EventArgs.Empty);
        _closed?.TrySetResult(_result);
    }

    // ------------------------------------------------------------------ desktop: a window

    Window CreateWindow(Visual? owner)
    {
        var window = new Window
        {
            SizeToContent = SizeToContent,
            CanResize = CanResize,
            ShowInTaskbar = ShowInTaskbar,
            WindowStartupLocation = WindowStartupLocation,
            Icon = Icon ?? OwnerWindow(owner)?.Icon,
        };
        window.Bind(Window.TitleProperty, this.GetObservable(TitleProperty));
        if (IsSet(BackgroundProperty)) window.Bind(BackgroundProperty, this.GetObservable(BackgroundProperty));
        MoveSizeTo(window);
        window.Content = this;
        window.Opened += (_, _) => Opened?.Invoke(this, EventArgs.Empty);
        window.Closed += (_, _) =>
        {
            window.Content = null;
            _window = null;
            OnClosed();
        };
        _window = window;
        return window;
    }

    /// <summary>The size given to the dialog is the size of its window (panel); the content fills it.</summary>
    void MoveSizeTo(Layoutable target)
    {
        if (!double.IsNaN(Width)) { target.Width = Width; Width = double.NaN; }
        if (!double.IsNaN(Height)) { target.Height = Height; Height = double.NaN; }
        if (MinWidth > 0) { target.MinWidth = MinWidth; MinWidth = 0; }
        if (MinHeight > 0) { target.MinHeight = MinHeight; MinHeight = 0; }
        if (!double.IsPositiveInfinity(MaxWidth)) { target.MaxWidth = MaxWidth; MaxWidth = double.PositiveInfinity; }
        if (!double.IsPositiveInfinity(MaxHeight)) { target.MaxHeight = MaxHeight; MaxHeight = double.PositiveInfinity; }
    }

    // ------------------------------------------------------------------ browser: a panel over the main view

    static TopLevel? MainTopLevel =>
        (Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView is { } view
            ? TopLevel.GetTopLevel(view) : null;

    void ShowOverlay(Visual? owner, bool modal)
    {
        var top = (owner != null ? TopLevel.GetTopLevel(owner) : null) ?? MainTopLevel;
        var layer = top != null ? OverlayLayer.GetOverlayLayer(top) : null;
        if (layer == null) throw new InvalidOperationException("The dialog cannot be shown: no overlay layer.");

        var title = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.Bind(TextBlock.TextProperty, this.GetObservable(TitleProperty));
        var closeIcon = new IconPath
        {
            Data = App.Resource<Geometry>("Icon.Close"),
            Width = 16, Height = 16,
            Stretch = Stretch.None,
        };
        closeIcon.Bind(Shape.FillProperty, closeIcon.GetResourceObservable("Brush.Text"));
        var close = new Button { Content = closeIcon, Padding = new Thickness(4), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current?.TryGetResource("ToolButton", null, out var theme) == true && theme is ControlTheme ct) close.Theme = ct;
        ToolTip.SetTip(close, "Close");
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        var titleBar = new DockPanel { Height = 30, Children = { close, title }, Cursor = new Cursor(StandardCursorType.SizeAll) };
        titleBar.Bind(Panel.BackgroundProperty, titleBar.GetResourceObservable("Brush.PanelHeader"));
        DockPanel.SetDock(titleBar, Dock.Top);

        var body = new DockPanel { Children = { titleBar } };
        var frame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            // A modal dialog is centred; a modeless one (find, help) stays at the top right, over the editor.
            HorizontalAlignment = modal ? HorizontalAlignment.Center : HorizontalAlignment.Right,
            VerticalAlignment = modal ? VerticalAlignment.Center : VerticalAlignment.Top,
            Margin = modal ? default : new Thickness(0, 64, 12, 0),
            BoxShadow = BoxShadows.Parse("0 6 24 0 #50000000"),
            Child = body,
            Focusable = true,
        };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Brush.Border"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Brush.PanelBackground"));
        MoveSizeTo(frame);
        body.Children.Add(this);

        // The title bar moves the panel.
        var offset = new TranslateTransform();
        frame.RenderTransform = offset;
        Point? dragStart = null;
        titleBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(layer);
            dragStart = new Point(p.X - offset.X, p.Y - offset.Y);
            e.Pointer.Capture(titleBar);
            Activate();
        };
        titleBar.PointerMoved += (_, e) =>
        {
            if (dragStart is not { } start) return;
            var p = e.GetPosition(layer);
            offset.X = p.X - start.X;
            offset.Y = p.Y - start.Y;
        };
        titleBar.PointerReleased += (_, e) => { dragStart = null; e.Pointer.Capture(null); };

        var host = new Panel { ZIndex = ++_zIndex };
        if (modal)
        {
            // Semi-transparent backdrop: the main view does not receive the clicks.
            host.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)) });
        }
        host.Children.Add(frame);
        host.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } });

        double maxWidth = frame.MaxWidth, maxHeight = frame.MaxHeight;
        void Fit()
        {
            var size = layer.Bounds.Size;
            host.Width = size.Width;
            host.Height = size.Height;
            frame.MaxWidth = Math.Min(maxWidth, Math.Max(200, size.Width - 24 - frame.Margin.Left - frame.Margin.Right));
            frame.MaxHeight = Math.Min(maxHeight, Math.Max(150, size.Height - 24 - frame.Margin.Top - frame.Margin.Bottom));
        }
        Fit();
        layer.PropertyChanged += OnLayerChanged;
        void OnLayerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == BoundsProperty) Fit();
        }
        _unsubscribeLayer = () => layer.PropertyChanged -= OnLayerChanged;

        layer.Children.Add(host);
        _layer = layer;
        _overlay = host;
        Dispatcher.UIThread.Post(() =>
        {
            Opened?.Invoke(this, EventArgs.Empty);
            // A modal dialog takes the keyboard (the editor behind must not receive the typed keys).
            if (modal && _overlay == host && !frame.IsKeyboardFocusWithin) frame.Focus();
        }, DispatcherPriority.Loaded);
    }

    Action? _unsubscribeLayer;

    void CloseOverlay()
    {
        var host = _overlay!;
        _overlay = null;
        _unsubscribeLayer?.Invoke();
        _unsubscribeLayer = null;
        _layer?.Children.Remove(host);
        _layer = null;
        OnClosed();
    }
}
