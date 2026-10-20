// Test harness for the Algol 68 compiler.
// Usage: dotnet run --project tests/Algol68Tests [-- --show file.a68] [-- --filter text] [-- -v]
using System.Diagnostics;
using System.Text;
using Studio.Common;
using Studio.Algol68;

namespace Algol68Tests;

sealed class TestIO : IProgramIO
{
    readonly StringBuilder output = new();
    readonly Queue<string> input;
    public TestIO(string? inputText, string workDir)
    {
        WorkingDirectory = workDir;
        input = new Queue<string>();
        if (inputText != null)
        {
            var t = inputText.Replace("\r\n", "\n");
            if (t.EndsWith("\n")) t = t[..^1];
            if (t.Length > 0 || inputText.Length > 0) foreach (var l in t.Split('\n')) input.Enqueue(l);
        }
    }
    public void Write(string text) { lock (output) output.Append(text); }
    public string? ReadLine() { lock (input) return input.Count > 0 ? input.Dequeue() : null; }
    public string WorkingDirectory { get; }
    public string Output { get { lock (output) return output.ToString(); } }
}

static class Program
{
    static int passed, failed;
    static readonly List<string> failures = new();
    static bool verbose;
    static readonly Algol68Language lang = new();

    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Simula67Studio.sln"))) d = d.Parent;
        return d?.FullName ?? Directory.GetCurrentDirectory();
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string? show = null, filter = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--show" && i + 1 < args.Length) show = args[++i];
            else if (args[i] == "--filter" && i + 1 < args.Length) filter = args[++i];
            else if (args[i] is "-v" or "--verbose") verbose = true;
        }
        if (show != null) return Show(show);
        var root = RepoRoot();
        var samples = Path.Combine(root, "samples", "algol68");
        var sw = Stopwatch.StartNew();
        foreach (var file in Directory.GetFiles(samples, "*.a68", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (filter != null && !file.Replace('\\', '/').Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            if (Path.GetFileName(Path.GetDirectoryName(file)!) == "include") continue;
            RunSample(file, samples);
        }
        if (filter == null || "internal".Contains(filter)) InternalTests.Run();
        Console.WriteLine();
        Console.WriteLine($"Result: {passed} passed, {failed} failed in {sw.Elapsed.TotalSeconds:F1} s");
        foreach (var f in failures) Console.WriteLine("  FAILED " + f);
        return failed == 0 ? 0 : 1;
    }

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

    static string Norm(string s) => s.Replace("\r\n", "\n");

    internal static CompileOptions OptionsFor(string file) => new()
    {
        IncludeResolver = (name, from) =>
        {
            var dir = Path.GetDirectoryName(from) ?? "";
            foreach (var cand in new[] { Path.Combine(dir, name), Path.Combine(dir, "include", name) })
                if (File.Exists(cand)) return new SourceFile(cand, File.ReadAllText(cand));
            return null;
        },
    };

    internal static (string output, ExecutionResult result, string workDir) Execute(ICompiledProgram prog, string? input, int timeoutSec = 60)
    {
        var work = Path.Combine(Path.GetTempPath(), "a68tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        var io = new TestIO(input, work);
        var exec = prog.CreateExecution(io);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        var res = exec.Run(cts.Token);
        return (io.Output, res, work);
    }

    static void RunSample(string file, string samplesRoot)
    {
        string name = Path.GetRelativePath(samplesRoot, file).Replace('\\', '/');
        string bas = Path.ChangeExtension(file, null);
        string? expOut = File.Exists(bas + ".out") ? Norm(File.ReadAllText(bas + ".out")) : null;
        string? input = File.Exists(bas + ".in") ? File.ReadAllText(bas + ".in") : null;
        string? expErr = File.Exists(bas + ".err") ? Norm(File.ReadAllText(bas + ".err")) : null;
        string? expRte = File.Exists(bas + ".rte") ? Norm(File.ReadAllText(bas + ".rte")) : null;
        var src = new SourceFile(file, File.ReadAllText(file));
        CompilationResult cr;
        try { cr = lang.Compile(src, OptionsFor(file)); }
        catch (Exception e) { Fail(name, "compiler exception: " + e); return; }
        if (expErr != null)
        {
            if (cr.Success) { Fail(name, "compilation error expected, but compilation succeeded"); return; }
            foreach (var line in expErr.Split('\n').Where(l => l.Trim().Length > 0))
            {
                var (lno, text) = ParseExpect(line);
                if (!cr.Diagnostics.Any(d => d.Severity == Severity.Error && d.Message.Contains(text, StringComparison.OrdinalIgnoreCase) && (lno == 0 || d.Line == lno)))
                {
                    Fail(name, $"expected diagnostic not found: {line}\ngot:\n" + string.Join("\n", cr.Diagnostics.Select(d => "  " + d)));
                    return;
                }
            }
            Pass(name);
            return;
        }
        if (!cr.Success)
        {
            Fail(name, "compilation errors:\n" + string.Join("\n", cr.Diagnostics.Select(d => "  " + d)));
            return;
        }
        var (output, res, work) = Execute(cr.Program!, input);
        try { Directory.Delete(work, true); } catch { }
        output = Norm(output);
        if (expRte != null)
        {
            if (res.Error == null) { Fail(name, "runtime error expected, none raised\noutput:\n" + output); return; }
            var lines = expRte.Split('\n').Where(l => l.Trim().Length > 0).ToList();
            var (lno, text) = ParseExpect(lines[0]);
            if (!res.Error.Message.Contains(text, StringComparison.OrdinalIgnoreCase) || (lno != 0 && res.Error.Line != lno))
            { Fail(name, $"expected runtime error: {lines[0]}\ngot: {res.Error}"); return; }
            if (expOut != null && output != expOut) { Fail(name, Diff(expOut, output)); return; }
            Pass(name);
            return;
        }
        if (res.Error != null || res.Cancelled)
        {
            Fail(name, $"runtime error: {res.Error?.ToString() ?? "cancelled (timeout?)"}\n" +
                (res.Error != null ? string.Join("\n", res.Error.Stack.Select(s => $"    at {s.Name} {s.File}({s.Line},{s.Column})")) : "") + "\noutput:\n" + output);
            return;
        }
        if (expOut == null) { Fail(name, "missing .out file; actual output:\n" + output); return; }
        if (output != expOut) { Fail(name, Diff(expOut, output)); return; }
        Pass(name);
    }

    static (int line, string text) ParseExpect(string l)
    {
        // form "12: message" or "message"
        int c = l.IndexOf(':');
        if (c > 0 && int.TryParse(l[..c].Trim(), out int n)) return (n, l[(c + 1)..].Trim());
        return (0, l.Trim());
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
        foreach (var l in b.Take(60)) sb.AppendLine("    |" + l);
        return sb.ToString();
    }

    static int Show(string file)
    {
        var src = new SourceFile(Path.GetFullPath(file), File.ReadAllText(file));
        var cr = lang.Compile(src, OptionsFor(Path.GetFullPath(file)));
        foreach (var d in cr.Diagnostics) Console.WriteLine(d);
        if (!cr.Success) return 1;
        var bas = Path.ChangeExtension(file, null);
        string? input = File.Exists(bas + ".in") ? File.ReadAllText(bas + ".in") : null;
        var sw = Stopwatch.StartNew();
        var (output, res, work) = Execute(cr.Program!, input);
        Console.Write(output);
        Console.WriteLine($"\n--- exit code {res.ExitCode}{(res.Cancelled ? " (cancelled)" : "")} in {sw.ElapsedMilliseconds} ms; directory {work}");
        if (res.Error != null)
        {
            Console.WriteLine(res.Error);
            foreach (var s in res.Error.Stack) Console.WriteLine($"    at {s.Name} {s.File}({s.Line},{s.Column})");
        }
        return res.ExitCode;
    }
}
