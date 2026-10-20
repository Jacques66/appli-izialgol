using System.Runtime.InteropServices;
using System.Windows;
using Studio.Ide.Services;
using Studio.Tooling;

namespace Studio.Ide.Dialogs;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var v = typeof(AboutWindow).Assembly.GetName().Version;
        VersionText.Text = $"Version {v?.ToString(3)}";
        RuntimeText.Text =
            $"{RuntimeInformation.FrameworkDescription} — {RuntimeInformation.OSDescription}\n" +
            $"Langages : {string.Join(", ", LanguageRegistry.Languages.Select(l => l.DisplayName))}\n" +
            $"Lanceur : {RunnerLocator.Find() ?? "introuvable"}\n" +
            $"Paramètres : {IdeSettings.Directory}";
    }
}
