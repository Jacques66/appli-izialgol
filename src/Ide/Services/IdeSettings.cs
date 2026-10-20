using System.Text.Json;
using System.Text.Json.Serialization;

namespace Studio.Ide.Services;

/// <summary>Persistent settings (%APPDATA%\Simula67Studio\settings.json).</summary>
public sealed class IdeSettings
{
    public List<string> RecentFiles { get; set; } = new();
    public List<string> RecentFolders { get; set; } = new();
    public List<string> OpenFiles { get; set; } = new();
    public string? ActiveFile { get; set; }
    public Dictionary<string, List<int>> Breakpoints { get; set; } = new();
    public Dictionary<string, int> CaretOffsets { get; set; } = new();
    public string? ExplorerRoot { get; set; }

    public double FontSize { get; set; } = 14;
    public string FontFamily { get; set; } = "Cascadia Mono, Consolas, Courier New";
    /// <summary>"light" or "dark".</summary>
    public string Theme { get; set; } = "light";
    public bool WordWrap { get; set; }
    public bool ShowWhitespace { get; set; }
    public bool ShowLineNumbers { get; set; } = true;
    public int IndentSize { get; set; } = 3;

    public bool ExternalConsole { get; set; }
    public bool ClearConsoleOnRun { get; set; } = true;
    public bool BackgroundCheck { get; set; } = true;
    public bool GenerateDebugInfo { get; set; } = true;
    public bool BreakOnError { get; set; } = true;
    public bool StopOnEntry { get; set; }

    public bool ShowExplorer { get; set; } = true;
    public bool ShowOutline { get; set; } = true;
    public bool ShowBottomPanel { get; set; } = true;
    public double LeftPanelWidth { get; set; } = 230;
    public double RightPanelWidth { get; set; } = 220;
    public double BottomPanelHeight { get; set; } = 220;

    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 820;
    public bool WindowMaximized { get; set; }

    // ------------------------------------------------------------------

    /// <summary>Settings directory (can be overridden with --settings-dir).</summary>
    public static string Directory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Simula67Studio");

    static string FilePath => Path.Combine(Directory, "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static IdeSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<IdeSettings>(File.ReadAllText(FilePath), Json);
                if (s != null)
                {
                    s.Breakpoints = new Dictionary<string, List<int>>(s.Breakpoints ?? new(), StringComparer.OrdinalIgnoreCase);
                    s.CaretOffsets = new Dictionary<string, int>(s.CaretOffsets ?? new(), StringComparer.OrdinalIgnoreCase);
                    s.RecentFiles ??= new(); s.OpenFiles ??= new(); s.RecentFolders ??= new();
                    if (s.FontSize < 6 || s.FontSize > 72) s.FontSize = 14;
                    if (s.IndentSize < 1 || s.IndentSize > 8) s.IndentSize = 3;
                    // Older settings files used French theme names ("sombre" = dark).
                    s.Theme = s.Theme is "dark" or "sombre" ? "dark" : "light";
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("cannot read settings: " + ex.Message);
        }
        return new IdeSettings
        {
            Breakpoints = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase),
            CaretOffsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        };
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex)
        {
            Log.Write("cannot save settings: " + ex.Message);
        }
    }

    public void AddRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 12) RecentFiles.RemoveRange(12, RecentFiles.Count - 12);
    }

    public void AddRecentFolder(string path)
    {
        RecentFolders.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, path);
        if (RecentFolders.Count > 8) RecentFolders.RemoveRange(8, RecentFolders.Count - 8);
    }
}

/// <summary>Technical log (%APPDATA%\Simula67Studio\ide.log).</summary>
public static class Log
{
    static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(IdeSettings.Directory);
                File.AppendAllText(Path.Combine(IdeSettings.Directory, "ide.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
