namespace Studio.Ide.Services;

/// <summary>A program started in an external console window.</summary>
public sealed class ExternalRun
{
    readonly Action _kill;

    public ExternalRun(Task completion, Action kill)
    {
        Completion = completion;
        _kill = kill;
    }

    /// <summary>Completes when the console window (or the program running in it) is closed.</summary>
    public Task Completion { get; }

    public bool HasExited => Completion.IsCompleted;

    /// <summary>Stops the program and its console.</summary>
    public void Kill()
    {
        try { if (!HasExited) _kill(); } catch { }
    }
}

/// <summary>
/// Operations that depend on the platform the IDE runs on. The desktop head (src/Ide.Desktop) implements
/// them for Windows, macOS and Linux; the browser head (src/Ide.Browser) provides its own implementation.
/// File dialogs and the clipboard go through Avalonia (StorageProvider, Clipboard) and are not part of it.
/// </summary>
public interface IPlatformServices
{
    /// <summary>Name of the external console in the user interface ("Windows console", "Terminal window"…).</summary>
    string ExternalConsoleName { get; }

    /// <summary>A program can be run in an external console window.</summary>
    bool SupportsExternalConsole { get; }

    /// <summary>Standalone executables can be generated.</summary>
    bool SupportsExecutables { get; }

    /// <summary>Extension of the generated executables (".exe" on Windows, "" on macOS and Linux).</summary>
    string ExecutableExtension { get; }

    /// <summary>Description of the generated executables ("Windows executable"…).</summary>
    string ExecutableDescription { get; }

    /// <summary>Path of the launcher used as the base of the standalone executables (null if not found).</summary>
    string? FindRunner();

    /// <summary>Writes a standalone executable (launcher + compiled program) and makes it runnable.</summary>
    void WriteExecutable(string runnerPath, string targetPath, string languageId, byte[] payload);

    /// <summary>Runs an executable in a new console window; the window stays open at the end of the program.</summary>
    ExternalRun StartInExternalConsole(string executablePath, string workingDirectory, string title);

    /// <summary>Label of the command that shows a file in the file manager ("Open in Windows Explorer"…).</summary>
    string RevealInFileManagerText { get; }

    /// <summary>Opens a web address (or mailto:) in the default application.</summary>
    void OpenUrl(string url);

    /// <summary>Shows a file (selected) or a folder in the file manager.</summary>
    void RevealInFileManager(string path);

    /// <summary>The files live in a virtual file system kept by the application (browser): there is no folder
    /// dialog and no file manager; Open uploads files from the computer and "Download" saves a copy on it.</summary>
    bool IsVirtualFileSystem => false;

    /// <summary>Folder of the virtual file system where uploaded files are copied (null on the desktop).</summary>
    string? ImportDirectory => null;
}

/// <summary>Platform without external processes (default until a head sets <see cref="App.Platform"/>).</summary>
public sealed class NoPlatformServices : IPlatformServices
{
    public string ExternalConsoleName => "console";
    public bool SupportsExternalConsole => false;
    public bool SupportsExecutables => false;
    public string ExecutableExtension => "";
    public string ExecutableDescription => "Executable";
    public string? FindRunner() => null;
    public string RevealInFileManagerText => "Show in the file manager";

    public void WriteExecutable(string runnerPath, string targetPath, string languageId, byte[] payload) =>
        throw new PlatformNotSupportedException("Standalone executables cannot be generated on this platform.");

    public ExternalRun StartInExternalConsole(string executablePath, string workingDirectory, string title) =>
        throw new PlatformNotSupportedException("There is no external console on this platform.");

    public void OpenUrl(string url) { }
    public void RevealInFileManager(string path) { }
}
