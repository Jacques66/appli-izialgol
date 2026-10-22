using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Studio.Ide.Services;

namespace Studio.Ide.Dialogs;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var v = typeof(AboutWindow).Assembly.GetName().Version;
        VersionText.Text = $"Version {v?.ToString(3)}";
        RuntimeText.Text =
            $"{RuntimeInformation.FrameworkDescription} — {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\n" +
            $"Languages: {string.Join(", ", Studio.Tooling.LanguageRegistry.Languages.Select(l => l.DisplayName))}\n" +
            $"Launcher: {App.Platform.FindRunner() ?? "not found"}\n" +
            $"Settings: {IdeSettings.Directory}";
    }

    void OnClose(object? sender, RoutedEventArgs e) => Close();
}
