using System.Windows;
using System.Windows.Threading;
using Studio.Ide.Editor;
using Studio.Ide.Services;
using Studio.Tooling;

namespace Studio.Ide;

/// <summary>Options de la ligne de commande.</summary>
public sealed class AppOptions
{
    public List<string> Files { get; } = new();
    /// <summary>Demarre, affiche la fenetre, capture eventuellement un ecran, puis se ferme (code 0 si aucune exception).</summary>
    public bool SmokeTest { get; set; }
    public string? Screenshot { get; set; }
    /// <summary>Scenario automatique de bout en bout avec le langage de test « echo ».</summary>
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
    public static AppOptions Options { get; private set; } = new();
    public static int UnhandledErrors { get; private set; }
    public static readonly List<string> ErrorMessages = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Options = AppOptions.Parse(e.Args);
        if (Options.SettingsDir != null) IdeSettings.Directory = Path.GetFullPath(Options.SettingsDir);
        else if (Options.Automated)
            IdeSettings.Directory = Path.Combine(Path.GetTempPath(), "Simula67Studio-test-" + Environment.ProcessId);
        LanguageRegistry.TestLanguageEnabled = Options.TestLanguage ||
            Environment.GetEnvironmentVariable("SIMULA67STUDIO_TEST_LANGUAGE") == "1";

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            Log.Write("exception non gérée : " + ev.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            Log.Write("tâche : " + ev.Exception);
            ev.SetObserved();
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        UnhandledErrors++;
        ErrorMessages.Add(e.Exception.ToString());
        Log.Write("exception dans l'interface : " + e.Exception);
        e.Handled = true;
        if (!Options.Automated)
            MessageBox.Show(MainWindow!, "Une erreur inattendue s'est produite :\n\n" + e.Exception.Message +
                "\n\nLe détail a été écrit dans " + Path.Combine(IdeSettings.Directory, "ide.log"),
                "Simula 67 & Algol 68 Studio", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static bool IsDarkTheme { get; private set; }

    public static void ApplyTheme(bool dark)
    {
        IsDarkTheme = dark;
        var dicts = Current.Resources.MergedDictionaries;
        dicts[0] = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        EditorTheme.Current = dark ? EditorTheme.Dark : EditorTheme.Light;
    }
}
