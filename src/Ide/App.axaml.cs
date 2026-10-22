using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Studio.Ide.Dialogs;
using Studio.Ide.Editor;
using Studio.Ide.Services;
using Studio.Tooling;

namespace Studio.Ide;

/// <summary>Command-line options.</summary>
public sealed class AppOptions
{
    public List<string> Files { get; } = new();
    /// <summary>Starts, shows the window, optionally takes a screenshot, then exits (exit code 0 when no exception occurred).</summary>
    public bool SmokeTest { get; set; }
    public string? Screenshot { get; set; }
    /// <summary>Automated end-to-end scenario with the "echo" test language.</summary>
    public bool SelfTest { get; set; }
    public string? SelfTestDir { get; set; }
    public bool TestLanguage { get; set; }
    public string? SettingsDir { get; set; }
    public string? Theme { get; set; }

    public bool Automated => SmokeTest || SelfTest;

    public static AppOptions Parse(string[] args)
    {
        var o = new AppOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (a.ToLowerInvariant())
            {
                case "--smoke-test": o.SmokeTest = true; break;
                case "--screenshot": o.Screenshot = Next(); break;
                case "--self-test": o.SelfTest = true; o.TestLanguage = true; break;
                case "--self-test-dir": o.SelfTestDir = Next(); break;
                case "--test-language": o.TestLanguage = true; break;
                case "--settings-dir": o.SettingsDir = Next(); break;
                case "--theme": o.Theme = Next(); break;
                default:
                    if (!a.StartsWith("--")) o.Files.Add(a);
                    break;
            }
        }
        return o;
    }
}

public partial class App : Application
{
    /// <summary>Command-line options (set by the platform head before the application starts).</summary>
    public static AppOptions Options { get; set; } = new();
    /// <summary>Platform operations (set by the platform head before the application starts).</summary>
    public static IPlatformServices Platform { get; set; } = new NoPlatformServices();
    /// <summary>Automated scenario (smoke test, self-test) started once the main view is shown; set by the head.</summary>
    public static Func<MainView, Task>? AutomatedScenario { get; set; }

    public static int UnhandledErrors { get; private set; }
    public static readonly List<string> ErrorMessages = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (Options.SettingsDir != null) IdeSettings.Directory = Path.GetFullPath(Options.SettingsDir);
        else if (Options.Automated)
            IdeSettings.Directory = Path.Combine(Path.GetTempPath(), "AlgolStudio-test-" + Environment.ProcessId);
        LanguageRegistry.TestLanguageEnabled = Options.TestLanguage ||
            Environment.GetEnvironmentVariable("ALGOLSTUDIO_TEST_LANGUAGE") == "1";

        Dispatcher.UIThread.UnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            Log.Write("unhandled exception: " + ev.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            Log.Write("task: " + ev.Exception);
            ev.SetObserved();
        };

        // Monospace font of the panels (output, console, find): same choice as the editor (see CodeFont).
        Resources["CodeFont"] = CodeFont.Resolve(IdeSettings.DefaultFontFamily);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var window = new MainWindow();
            desktop.MainWindow = window;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Browser: the main view is the whole page; dialogs float over it (see DialogWindow).
            singleView.MainView = new MainView();
        }
        base.OnFrameworkInitializationCompleted();
    }

    void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        UnhandledErrors++;
        ErrorMessages.Add(e.Exception.ToString());
        Log.Write("exception in the user interface: " + e.Exception);
        e.Handled = true;
        if (!Options.Automated)
        {
            var message = "An unexpected error occurred:\n\n" + e.Exception.Message +
                          "\n\nDetails were written to " + Path.Combine(IdeSettings.Directory, "ide.log");
            Dispatcher.UIThread.Post(() => MessageBox.Show(MainViewOrNull, message, "AlgolStudio",
                MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    void OnNativeAbout(object? sender, EventArgs e)
    {
        if (MainViewOrNull is { } owner) _ = new AboutWindow().ShowDialog(owner);
    }

    /// <summary>Main window (desktop) or main view (browser).</summary>
    static Visual? MainViewOrNull => Current?.ApplicationLifetime switch
    {
        IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
        ISingleViewApplicationLifetime singleView => singleView.MainView,
        _ => null,
    };

    /// <summary>Open windows of the application.</summary>
    public static IReadOnlyList<Window> Windows =>
        (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? Array.Empty<Window>();

    /// <summary>Ends the application with an exit code.</summary>
    public static void Shutdown(int exitCode)
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown(exitCode);
        else if (Current?.ApplicationLifetime is IControlledApplicationLifetime controlled) controlled.Shutdown(exitCode);
    }

    public static bool IsDarkTheme { get; private set; }

    public static void ApplyTheme(bool dark)
    {
        IsDarkTheme = dark;
        if (Current != null) Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        EditorTheme.Current = dark ? EditorTheme.Dark : EditorTheme.Light;
    }

    /// <summary>Application resource for the current theme (brushes, icon geometries…).</summary>
    public static T Resource<T>(string key) where T : class
    {
        if (Current != null && Current.TryGetResource(key, Current.ActualThemeVariant, out var value) && value is T t) return t;
        throw new KeyNotFoundException("resource not found: " + key);
    }
}
