using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Shape = Avalonia.Controls.Shapes.Shape;
using IconPath = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Studio.Ide.Dialogs;

public enum MessageBoxButton { OK, YesNo, YesNoCancel }
public enum MessageBoxResult { None, OK, Yes, No, Cancel }
public enum MessageBoxImage { None, Error, Warning, Question, Information }

/// <summary>
/// Modal dialogs with a synchronous result, like the WPF ones: the caller waits in a nested dispatcher
/// frame (supported by the desktop platforms; a browser head needs asynchronous dialogs).
/// </summary>
public static class Modal
{
    /// <summary>Waits for a task started on the UI thread while the user interface keeps running.</summary>
    public static T Wait<T>(Task<T> task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }

    /// <summary>Shows a dialog (modal when there is an owner) and waits until it is closed.</summary>
    public static T? ShowDialog<T>(Window dialog, Window? owner)
    {
        if (owner != null && owner.IsVisible) return Wait(dialog.ShowDialog<T>(owner));
        var tcs = new TaskCompletionSource<T?>();
        T? result = default;
        dialog.Closed += (_, _) => tcs.TrySetResult(result);
        dialog.Show();
        return Wait(tcs.Task);
    }
}

/// <summary>Message box (Avalonia has none): text, icon, OK / Yes-No / Yes-No-Cancel buttons.</summary>
public static class MessageBox
{
    public static MessageBoxResult Show(Window? owner, string text, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = owner == null,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            MinWidth = 320,
            MaxWidth = 640,
            FontSize = 12.5,
        };
        dialog.Bind(TemplatedControl.BackgroundProperty, dialog.GetResourceObservable("Brush.PanelBackground"));
        dialog.Bind(TemplatedControl.ForegroundProperty, dialog.GetResourceObservable("Brush.Text"));
        if (owner?.Icon != null) dialog.Icon = owner.Icon;

        var result = buttons == MessageBoxButton.OK ? MessageBoxResult.OK : buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        Button? first = null;
        void AddButton(string label, MessageBoxResult value, bool isDefault = false, bool isCancel = false)
        {
            var b = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel };
            if (Application.Current?.TryGetResource("DialogButton", null, out var theme) == true && theme is ControlTheme ct) b.Theme = ct;
            b.Click += (_, _) => { result = value; dialog.Close(); };
            buttonRow.Children.Add(b);
            first ??= b;
        }
        switch (buttons)
        {
            case MessageBoxButton.OK:
                AddButton("OK", MessageBoxResult.OK, isDefault: true, isCancel: true);
                break;
            case MessageBoxButton.YesNo:
                AddButton("_Yes", MessageBoxResult.Yes, isDefault: true);
                AddButton("_No", MessageBoxResult.No, isCancel: true);
                break;
            default:
                AddButton("_Yes", MessageBoxResult.Yes, isDefault: true);
                AddButton("_No", MessageBoxResult.No);
                AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                break;
        }

        var body = new DockPanel { Margin = new Thickness(20, 18, 20, 16) };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        body.Children.Add(buttonRow);
        var (iconKey, brushKey) = image switch
        {
            MessageBoxImage.Error => ("Icon.ErrorMark", "Brush.Error"),
            MessageBoxImage.Warning => ("Icon.WarningMark", "Brush.Warning"),
            MessageBoxImage.Question => ("Icon.Help", "Brush.Info"),
            MessageBoxImage.Information => ("Icon.InfoMark", "Brush.Info"),
            _ => ("", ""),
        };
        if (iconKey.Length > 0)
        {
            var icon = new IconPath
            {
                Data = App.Resource<Geometry>(iconKey),
                Width = 16, Height = 16,
                Stretch = Stretch.None,
                RenderTransform = new ScaleTransform(2, 2),
                Margin = new Thickness(8, 8, 22, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            icon.Bind(Shape.FillProperty, icon.GetResourceObservable(brushKey));
            DockPanel.SetDock(icon, Dock.Left);
            body.Children.Add(icon);
        }
        body.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        dialog.Content = body;
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) dialog.Close();
        };
        dialog.Opened += (_, _) => first?.Focus();
        Modal.ShowDialog<object>(dialog, owner);
        return result;
    }
}
