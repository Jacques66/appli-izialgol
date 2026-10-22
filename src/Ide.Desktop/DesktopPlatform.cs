using System.Diagnostics;
using System.Text;
using Studio.Common;
using Studio.Ide.Services;
using Studio.Tooling;

namespace Studio.Ide.Desktop;

/// <summary>Platform services of the desktop head: Windows, macOS and Linux.</summary>
public sealed class DesktopPlatform : IPlatformServices
{
    static bool IsWindows => OperatingSystem.IsWindows();
    static bool IsMac => OperatingSystem.IsMacOS();

    public string ExternalConsoleName => IsWindows ? "Windows console" : IsMac ? "Terminal window" : "terminal window";
    public bool SupportsExternalConsole => true;
    public bool SupportsExecutables => true;
    public string ExecutableExtension => IsWindows ? ".exe" : "";
    public string ExecutableDescription => IsWindows ? "Windows executable" : IsMac ? "macOS executable" : "Linux executable";
    public string RevealInFileManagerText => IsWindows ? "Open in Windows Explorer" : IsMac ? "Reveal in Finder" : "Open the containing folder";

    public string? FindRunner() => RunnerLocator.Find();

    // ------------------------------------------------------------------ standalone executables

    public void WriteExecutable(string runnerPath, string targetPath, string languageId, byte[] payload)
    {
        ExePackage.Write(runnerPath, targetPath, languageId, payload);
        if (OperatingSystem.IsWindows()) return;
        // macOS / Linux: executable file (rwx for the owner, rx for the others)
        var mode = File.GetUnixFileMode(targetPath);
        File.SetUnixFileMode(targetPath, mode | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                         UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        if (IsMac) ReSign(targetPath);
    }

    /// <summary>
    /// macOS (best effort): the program appended to the launcher lies after its code signature, which stays
    /// valid for the signed pages; when the signature is reported invalid, try an ad-hoc signature.
    /// </summary>
    static void ReSign(string path)
    {
        try
        {
            if (Run("codesign", "--verify", path) == 0) return;
            int code = Run("codesign", "-s", "-", "--force", path);
            if (code != 0) Log.Write($"codesign -s - --force {path}: exit code {code}");
        }
        catch (Exception ex)
        {
            Log.Write("codesign unavailable: " + ex.Message);
        }
    }

    static int Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } return -1; }
        return p.ExitCode;
    }

    // ------------------------------------------------------------------ external console

    public ExternalRun StartInExternalConsole(string executablePath, string workingDirectory, string title)
    {
        if (OperatingSystem.IsWindows()) return StartWindowsConsole(executablePath, workingDirectory, title);
        return StartTerminal(executablePath, workingDirectory, title);
    }

    /// <summary>Windows: cmd.exe window that waits for a key at the end of the program.</summary>
    static ExternalRun StartWindowsConsole(string exe, string workDir, string title)
    {
        var psi = new ProcessStartInfo("cmd.exe",
            $"/v:on /c \"title {title} & \"{exe}\" & echo. & echo [Program finished with exit code !errorlevel!] & pause\"")
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = workDir,
        };
        var p = Process.Start(psi)!;
        p.EnableRaisingEvents = true;
        var exited = new TaskCompletionSource();
        p.Exited += (_, _) => exited.TrySetResult();
        if (p.HasExited) exited.TrySetResult();
        return new ExternalRun(exited.Task, () => { if (!p.HasExited) p.Kill(entireProcessTree: true); });
    }

    /// <summary>
    /// macOS / Linux: a shell script runs the program in a new terminal window, then waits for Enter.
    /// The script writes its process id (to stop it) and a marker file when it ends (terminals such as
    /// Terminal.app or gnome-terminal return at once, so the IDE cannot wait for the terminal process).
    /// </summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    static ExternalRun StartTerminal(string exe, string workDir, string title)
    {
        var dir = Path.Combine(PathLocator.TempDirectory, "run");
        Directory.CreateDirectory(dir);
        var id = Guid.NewGuid().ToString("N")[..8];
        var script = Path.Combine(dir, $"run-{id}.sh");
        var pidFile = Path.Combine(dir, $"run-{id}.pid");
        var doneFile = Path.Combine(dir, $"run-{id}.done");
        var sb = new StringBuilder();
        sb.Append("#!/bin/sh\n");
        sb.Append($"printf '\\033]0;%s\\007' {Quote(title)}\n");
        sb.Append($"echo $$ > {Quote(pidFile)}\n");
        sb.Append($"cd {Quote(workDir)} || exit 1\n");
        sb.Append($"{Quote(exe)}\n");
        sb.Append("code=$?\n");
        sb.Append("echo\n");
        sb.Append("echo \"[Program finished with exit code $code]\"\n");
        sb.Append("printf 'Press Enter to close this window...'\n");
        sb.Append("read dummy\n");
        sb.Append($"touch {Quote(doneFile)}\n");
        File.WriteAllText(script, sb.ToString(), new UTF8Encoding(false));
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        if (IsMac)
        {
            // Terminal.app: "do script" opens a new window running the command.
            var path = script.Replace("\\", "\\\\").Replace("\"", "\\\"");
            StartDetached("osascript", "-e", "tell application \"Terminal\"", "-e", "activate",
                "-e", $"do script \"exec /bin/sh \" & quoted form of \"{path}\"", "-e", "end tell");
        }
        else
        {
            var terminal = new[] { "x-terminal-emulator", "gnome-terminal", "konsole", "xterm" }.FirstOrDefault(IsInPath)
                ?? throw new InvalidOperationException("No terminal emulator found (x-terminal-emulator, gnome-terminal, konsole or xterm).");
            switch (terminal)
            {
                case "gnome-terminal": StartDetached(terminal, "--title", title, "--", "/bin/sh", script); break;
                case "xterm": StartDetached(terminal, "-T", title, "-e", "/bin/sh", script); break;
                default: StartDetached(terminal, "-e", "/bin/sh", script); break;
            }
        }

        var cts = new CancellationTokenSource();
        var completion = WaitForScriptAsync(pidFile, doneFile, cts.Token);
        return new ExternalRun(completion, () =>
        {
            cts.Cancel();
            if (ReadPid(pidFile) is int pid)
            {
                try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
            }
        });
    }

    /// <summary>Completes when the script has ended (marker file) or its shell is gone (window closed).</summary>
    static async Task WaitForScriptAsync(string pidFile, string doneFile, CancellationToken token)
    {
        var started = DateTime.UtcNow;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(400, token);
                if (File.Exists(doneFile)) break;
                if (ReadPid(pidFile) is int pid)
                {
                    if (!IsAlive(pid)) break;
                }
                else if (DateTime.UtcNow - started > TimeSpan.FromSeconds(60)) break; // the terminal never started the script
            }
        }
        catch (OperationCanceledException) { }
        // Stopped by the IDE: give the shell time to go away
        if (token.IsCancellationRequested) await Task.Delay(300);
        foreach (var f in new[] { pidFile, doneFile, Path.ChangeExtension(pidFile, ".sh") })
            try { File.Delete(f); } catch { }
    }

    static int? ReadPid(string pidFile)
    {
        try { return File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid) ? pid : null; }
        catch { return null; }
    }

    static bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch { return false; }
    }

    /// <summary>Single-quoted shell word.</summary>
    static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    static bool IsInPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(d => File.Exists(Path.Combine(d, program)));

    static void StartDetached(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
    }

    // ------------------------------------------------------------------ shell

    public void OpenUrl(string url)
    {
        if (IsWindows) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        else StartDetached(IsMac ? "open" : "xdg-open", url);
    }

    public void RevealInFileManager(string path)
    {
        if (IsWindows)
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();
            else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"")?.Dispose();
        }
        else if (IsMac)
        {
            if (File.Exists(path)) StartDetached("open", "-R", path);
            else if (Directory.Exists(path)) StartDetached("open", path);
        }
        else
        {
            var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            if (dir != null && Directory.Exists(dir)) StartDetached("xdg-open", dir);
        }
    }
}
