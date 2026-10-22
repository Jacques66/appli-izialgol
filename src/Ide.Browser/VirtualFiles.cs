using System.Text.Json;

namespace Studio.Ide.Browser;

/// <summary>
/// Files of the browser version. The .NET runtime of the browser has an in-memory file system, so the IDE, the
/// compilers and the programs use System.IO as on the desktop. At start-up it receives:
/// <list type="bullet">
/// <item>/AlgolStudio/samples and /AlgolStudio/docs: the examples and the help (resources of this assembly);</item>
/// <item>/AlgolStudio/My programs: the files of the user (uploaded with Open, or saved with Save as);</item>
/// <item>/AlgolStudio/.settings: the settings of the IDE;</item>
/// </list>
/// then the files saved in the browser storage (localStorage) by a previous visit. Every file written by the user
/// (a new file, or a modified example) is saved again in the browser storage by <see cref="Persist"/>.
/// </summary>
static class VirtualFiles
{
    public const string Root = "/AlgolStudio";
    public static readonly string Programs = Root + "/My programs";
    public static readonly string SettingsDirectory = Root + "/.settings";
    const string StorageKey = "AlgolStudio.files";

    /// <summary>Examples and help files with their write time once extracted (a later write time = modified).</summary>
    static readonly Dictionary<string, DateTime> Bundled = new(StringComparer.Ordinal);
    static string? _lastSignature;
    static bool _storageFullReported;

    public static void Setup()
    {
        var assembly = typeof(VirtualFiles).Assembly;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var relative = name.Replace('\\', '/');
            if (!relative.StartsWith("samples/") && !relative.StartsWith("docs/")) continue;
            var path = Root + "/" + relative;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var input = assembly.GetManifestResourceStream(name)!)
            using (var output = File.Create(path))
                input.CopyTo(output);
            Bundled[path] = File.GetLastWriteTimeUtc(path);
        }
        Directory.CreateDirectory(Programs);
        Directory.CreateDirectory(SettingsDirectory);
        Restore();
        _lastSignature = Signature(UserFiles());

        // First visit: the explorer shows the whole virtual file system, the theme follows the browser.
        var settings = Path.Combine(SettingsDirectory, "settings.json");
        if (!File.Exists(settings))
        {
            bool dark = false;
            try { dark = Interop.PrefersDark(); } catch { }
            File.WriteAllText(settings, JsonSerializer.Serialize(new { ExplorerRoot = Root, Theme = dark ? "dark" : "light" }));
        }
    }

    /// <summary>Writes the files saved by a previous visit.</summary>
    static void Restore()
    {
        string? json;
        try { json = Interop.GetItem(StorageKey); }
        catch (Exception ex) { Console.WriteLine("browser storage unavailable: " + ex.Message); return; }
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var files = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            foreach (var (path, content) in files)
            {
                if (!path.StartsWith(Root + "/", StringComparison.Ordinal)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, Convert.FromBase64String(content));
                Bundled.Remove(path); // a modified example stays a file of the user
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("cannot restore the saved files: " + ex.Message);
        }
    }

    /// <summary>Files written by the user: all the files except the unmodified examples and help files and the logs.</summary>
    static List<string> UserFiles()
    {
        var list = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            var p = path.Replace('\\', '/');
            if (p.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            if (Bundled.TryGetValue(p, out var written) && File.GetLastWriteTimeUtc(p) == written) continue;
            list.Add(p);
        }
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    static string Signature(List<string> files) =>
        string.Join("\n", files.Select(f => $"{f}|{File.GetLastWriteTimeUtc(f).Ticks}|{new FileInfo(f).Length}"));

    /// <summary>Saves the files of the user in the browser storage when they changed (called periodically).</summary>
    public static void Persist()
    {
        try
        {
            var files = UserFiles();
            var signature = Signature(files);
            if (signature == _lastSignature) return;
            var map = files.ToDictionary(f => f, f => Convert.ToBase64String(File.ReadAllBytes(f)));
            if (Interop.SetItem(StorageKey, JsonSerializer.Serialize(map)))
            {
                _lastSignature = signature;
                _storageFullReported = false;
            }
            else if (!_storageFullReported)
            {
                _storageFullReported = true;
                Console.WriteLine("The browser storage is full: the latest changes are not kept.");
                OnStorageFull?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("cannot save the files in the browser storage: " + ex.Message);
        }
    }

    /// <summary>Raised once when the browser storage refuses the files.</summary>
    public static event Action? OnStorageFull;
}
