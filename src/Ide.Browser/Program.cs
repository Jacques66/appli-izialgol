// Browser head of AlgolStudio (WebAssembly): virtual file system, platform services, fonts, Avalonia start-up.

using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Fonts.Inter;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Threading;
using Studio.Ide.Editor;
using Studio.Ide.Services;

[assembly: SupportedOSPlatform("browser")]

namespace Studio.Ide.Browser;

static class Program
{
    /// <summary>Monospace font embedded in this assembly (Assets/Fonts).</summary>
    const string CodeFontFamily = "fonts:DejaVu#DejaVu Sans Mono";

    static async Task Main(string[] args)
    {
        ThreadedRuntime.KeepAvaloniaSingleThreaded();
        VirtualFiles.Setup();
        PathLocator.BaseDirectory = VirtualFiles.Root;
        // Files to open can be given in the address: ?arg=/AlgolStudio/samples/simula67/01_hello.sim
        App.Options = AppOptions.Parse(args);
        App.Options.SettingsDir = VirtualFiles.SettingsDirectory;
        App.Platform = new BrowserPlatform();
        CodeFont.Fallback = new FontFamily(CodeFontFamily);

        try { await BuildAvaloniaApp().StartBrowserAppAsync("out"); }
        catch (Exception ex) { Console.Error.WriteLine("start-up failed: " + ex); throw; }

        // The technical log of the IDE is a file of the virtual file system: errors also go to the browser console.
        Dispatcher.UIThread.UnhandledException += (_, e) => Console.Error.WriteLine("AlgolStudio: " + e.Exception);
        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime { MainView: MainView view })
        {
            view.GetObservable(MainView.TitleProperty).Subscribe(new TitleObserver());
            VirtualFiles.OnStorageFull += () => view.ShowError(
                "The storage of the browser is full: the latest changes are not kept in this browser.\n" +
                "Download your programs (File › Download to the computer) and delete some files.", "Storage full");
            // The session (open files, settings) and the files of the user are kept in the browser storage.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                try { view.SaveSession(); } catch (Exception ex) { Log.Write("session: " + ex.Message); }
                VirtualFiles.Persist();
                Interop.SetUnsaved(view.Documents.Any(d => d.IsModified));
            };
            timer.Start();
        }
        Interop.HideSplash();
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .AfterPlatformServicesSetup(_ =>
            {
                ThreadedRuntime.InstallDispatcher();
                // Fonts registered before the view is created (it measures text at once): Inter for the user
                // interface, DejaVu Sans Mono for the code.
                FontManager.Current.AddFontCollection(new InterFontCollection());
                FontManager.Current.AddFontCollection(
                    new EmbeddedFontCollection(new Uri("fonts:DejaVu"), new Uri("avares://AlgolStudio.Browser/Assets/Fonts")));
            })
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "fonts:Inter#Inter",
                FontFallbacks = new[] { new FontFallback { FontFamily = new FontFamily(CodeFontFamily) } },
            });

    sealed class TitleObserver : IObserver<string>
    {
        public void OnNext(string value) => Interop.SetTitle(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
