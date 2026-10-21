// Test bench of the Pascal compiler: compiles and runs every program of samples/pascal and
// tests/PascalTests/cases, compares the output (program output, then compiler diagnostics and the runtime
// error, if any) with <name>.out; <name>.in is the console input. Then runs the internal tests
// (debugger, serialisation, files, cancellation).
// Usage: PascalTests [filter] | --show file.pas | -v
using System.Diagnostics;
using System.Text;
using Studio.Common;
using Studio.Pascal;

namespace PascalTests;

sealed class MemIO : IProgramIO
{
    readonly StringReader input;
    readonly StringBuilder output = new();
    public MemIO(string inp, string wd) { input = new StringReader(inp); WorkingDirectory = wd; }
    public void Write(string text) { lock (output) output.Append(text); }
    public string? ReadLine() { lock (input) return input.ReadLine(); }
    public string WorkingDirectory { get; }
    public string Output { get { lock (output) return output.ToString(); } }
}

static class Program
{
    internal static readonly PascalLanguage Lang = new();
    static int passed, failed;
    static readonly List<string> failures = new();
    static bool verbose;
    internal static string Root = "";

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Root = FindRoot();
        string? filter = null, show = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--show" && i + 1 < args.Length) show = args[++i];
            else if (args[i] is "-v" or "--verbose") verbose = true;
            else filter = args[i];
        }
        if (show != null)
        {
            var path = Path.GetFullPath(show);
            var inPath = Path.ChangeExtension(path, ".in");
            var r = RunFile(path, File.Exists(inPath) ? File.ReadAllText(inPath) : "");
            Console.Write(r);
            return 0;
        }
        var sw = Stopwatch.StartNew();
        var files = new List<string>();
        foreach (var dir in new[] { Path.Combine(Root, "samples", "pascal"), Path.Combine(Root, "tests", "PascalTests", "cases") })
            if (Directory.Exists(dir)) files.AddRange(Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories).Where(f => Path.GetExtension(f).ToLowerInvariant() is ".pas" or ".p"));
        foreach (var f in files.OrderBy(x => x, StringComparer.Ordinal))
        {
            string name = Path.GetRelativePath(Root, f).Replace('\\', '/');
            if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var expPath = Path.ChangeExtension(f, ".out");
            if (!File.Exists(expPath)) { Fail(name, "missing .out file; actual output:\n" + RunFile(f, ReadIn(f))); continue; }
            var expected = Norm(File.ReadAllText(expPath)).TrimEnd();
            string got;
            try { got = Norm(RunFile(f, ReadIn(f))).TrimEnd(); }
            catch (Exception e) { Fail(name, "exception: " + e); continue; }
            if (got == expected) Pass(name);
            else Fail(name, Diff(expected, got));
        }
        if (filter == null || "internal".Contains(filter, StringComparison.OrdinalIgnoreCase)) InternalTests.Run();
        Console.WriteLine();
        Console.WriteLine($"Result: {passed} passed, {failed} failed in {sw.Elapsed.TotalSeconds:F1} s");
        foreach (var f in failures) Console.WriteLine("  FAILED " + f);
        return failed == 0 ? 0 : 1;
    }

    static string ReadIn(string f)
    {
        var p = Path.ChangeExtension(f, ".in");
        return File.Exists(p) ? File.ReadAllText(p) : "";
    }

    internal static string Norm(string s) => s.Replace("\r\n", "\n");

    internal static void Pass(string name)
    {
        passed++;
        if (verbose) Console.WriteLine("ok     " + name);
        else Console.Write('.');
    }

    internal static void Fail(string name, string why)
    {
        failed++;
        failures.Add(name);
        Console.WriteLine();
        Console.WriteLine($"FAILED {name}\n{why}");
    }

    /// <summary>Program output, then the diagnostics, then the runtime error and its call stack.</summary>
    internal static string RunFile(string path, string input)
    {
        var src = new SourceFile(Path.GetFullPath(path), File.ReadAllText(path));
        var res = Lang.Compile(src, new CompileOptions());
        var diag = new StringBuilder();
        foreach (var d in res.Diagnostics)
            diag.Append('\n').Append($"{Path.GetFileName(d.File)}({d.Line},{d.Column}): {(d.Severity == Severity.Error ? "error" : d.Severity == Severity.Warning ? "warning" : "info")}: {d.Message}");
        if (!res.Success) return diag.ToString().TrimStart('\n') + "\n";
        var wd = Path.Combine(Path.GetTempPath(), "pascaltests", Path.GetFileNameWithoutExtension(path) + "_" + Environment.ProcessId);
        if (Directory.Exists(wd)) Directory.Delete(wd, true);
        Directory.CreateDirectory(wd);
        // data files used by a test: <name>.<file> next to the source are copied as <file>
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string stem = Path.GetFileNameWithoutExtension(path);
        foreach (var data in Directory.GetFiles(dir, stem + ".*"))
        {
            var ext = Path.GetExtension(data).TrimStart('.');
            if (ext is "pas" or "p" or "in" or "out") continue;
            File.Copy(data, Path.Combine(wd, ext), true);
        }
        var io = new MemIO(input, wd);
        var exec = res.Program!.CreateExecution(io);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var er = exec.Run(cts.Token);
        var sb = new StringBuilder(io.Output);
        sb.Append(diag);
        if (er.Error != null)
        {
            if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
            sb.Append($"runtime error ({Path.GetFileName(er.Error.File)}:{er.Error.Line}:{er.Error.Column}): {er.Error.Message}\n");
            foreach (var s in er.Error.Stack.Take(8)) sb.Append($"   at {s.Name} ({Path.GetFileName(s.File)}:{s.Line})\n");
            if (er.Error.Stack.Count > 8) sb.Append($"   ... ({er.Error.Stack.Count - 8} more frames)\n");
        }
        if (er.Cancelled) sb.Append("\n(timeout)\n");
        try { Directory.Delete(wd, true); } catch { }
        return sb.ToString();
    }

    internal static string Diff(string exp, string got)
    {
        var a = exp.Split('\n'); var b = got.Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            string x = i < a.Length ? a[i] : "<missing>", y = i < b.Length ? b[i] : "<missing>";
            if (x != y)
            {
                sb.AppendLine($"  first difference at line {i + 1}:");
                sb.AppendLine($"    expected: [{x}]");
                sb.AppendLine($"    actual:   [{y}]");
                break;
            }
        }
        sb.AppendLine("  full actual output:");
        foreach (var l in b.Take(80)) sb.AppendLine("    |" + l);
        return sb.ToString();
    }

    static string FindRoot()
    {
        var d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "Simula67Studio.sln"))) d = Path.GetDirectoryName(d);
        return d ?? Environment.CurrentDirectory;
    }
}
