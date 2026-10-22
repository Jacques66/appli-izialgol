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
/// frame (supported by the desktop platforms; the browser cannot wait: it uses the asynchronous methods).
/// </summary>
public static class Modal
{
    /// <summary>Waits for a task started on the UI thread while the user interface keeps running
    /// (a completed task, such as a save without dialog, returns at once on every platform).</summary>
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
}

/// <summary>Message box (Avalonia has none): text, icon, OK / Yes-No / Yes-No-Cancel buttons.</summary>
public static class MessageBox
{
    /// <summary>Shows the message and waits for the answer. In the browser, which cannot wait, the message is shown
    /// and the method returns at once (OK, or No / Cancel for a question: questions use <see cref="ShowAsync"/>).</summary>
    public static MessageBoxResult Show(Visual? owner, string text, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var task = ShowAsync(owner, text, title, buttons, image);
        if (DialogWindow.UsesOverlays && !task.IsCompleted)
            return buttons == MessageBoxButton.OK ? MessageBoxResult.OK : buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
        return Modal.Wait(task);
    }

    public static async Task<MessageBoxResult> ShowAsync(Visual? owner, string text, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new DialogWindow
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
        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>Asks for a line of text (OK / Cancel); returns null when cancelled.</summary>
    public static async Task<string?> PromptAsync(Visual? owner, string text, string title, string value)
    {
        var dialog = new DialogWindow
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = owner == null,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            FontSize = 12.5,
        };
        dialog.Bind(TemplatedControl.BackgroundProperty, dialog.GetResourceObservable("Brush.PanelBackground"));
        dialog.Bind(TemplatedControl.ForegroundProperty, dialog.GetResourceObservable("Brush.Text"));
        string? result = null;
        var box = new TextBox { Text = value, Width = 460 };
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var (label, ok) in new[] { ("OK", true), ("Cancel", false) })
        {
            var b = new Button { Content = label, IsDefault = ok, IsCancel = !ok };
            if (Application.Current?.TryGetResource("DialogButton", null, out var theme) == true && theme is ControlTheme ct) b.Theme = ct;
            b.Click += (_, _) => { if (ok) result = box.Text; dialog.Close(); };
            buttonRow.Children.Add(b);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children = { new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 6) }, box, buttonRow },
        };
        dialog.Opened += (_, _) =>
        {
            box.Focus();
            // Select the file name without its extension
            var name = System.IO.Path.GetFileNameWithoutExtension(value);
            int at = value.LastIndexOf(name, StringComparison.Ordinal);
            if (name.Length > 0 && at >= 0) { box.SelectionStart = at; box.SelectionEnd = at + name.Length; }
            else box.SelectAll();
        };
        await dialog.ShowDialog(owner);
        return result;
    }
}
