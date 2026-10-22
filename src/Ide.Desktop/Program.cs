// Desktop head of AlgolStudio (Windows, macOS, Linux): command line, platform services, Avalonia start-up.

using Avalonia;

namespace Studio.Ide.Desktop;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        App.Options = AppOptions.Parse(args);
        App.Platform = new DesktopPlatform();
        if (App.Options.SmokeTest) App.AutomatedScenario = SelfTest.RunSmokeAsync;
        else if (App.Options.SelfTest) App.AutomatedScenario = SelfTest.RunAsync;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia configuration (also used by the XAML previewer).</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
