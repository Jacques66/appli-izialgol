using System.Diagnostics;
using System.Text;
using Studio.Common;
using Studio.Algol60;
using Studio.Simula;
using Studio.SimulaI;

// Test bench of the Simula engine languages (Simula 67 .sim, Algol 60 .a60/.alg, Simula I .s1): compiles and runs
// samples/simula, samples/algol60, samples/simula1 and tests/SimulaTests/cases,
// compares with <name>.out (optional input <name>.in). Usage: SimulaTests [file.sim | filter | --debugtest]
var root = FindRoot();
var simula = new SimulaLanguage();
var algol60 = new Algol60Language();
var simula1 = new SimulaILanguage();
EngineLanguage LangFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch { ".a60" or ".alg" => algol60, ".s1" => simula1, _ => simula };
static bool IsSource(string f) => Path.GetExtension(f).ToLowerInvariant() is ".sim" or ".a60" or ".alg" or ".s1";
if (args.Length == 1 && IsSource(args[0]) && File.Exists(args[0]))
{
    var r = RunFile(args[0], File.Exists(Path.ChangeExtension(args[0], ".in")) ? File.ReadAllText(Path.ChangeExtension(args[0], ".in")) : "", true);
    Console.Write(r.Output);
    if (r.Diag.Length > 0) Console.WriteLine(r.Diag);
    return r.Ok ? 0 : 1;
}
if (args.Length == 1 && args[0] == "--debugtest") return DebugTest();
var filter = args.Length > 0 ? args[0] : "";
var filesToTest = new List<string>();
foreach (var dir in new[] { Path.Combine(root, "samples", "simula"), Path.Combine(root, "samples", "algol60"), Path.Combine(root, "samples", "simula1"), Path.Combine(root, "tests", "SimulaTests", "cases") })
    if (Directory.Exists(dir)) filesToTest.AddRange(Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories).Where(IsSource));
int pass = 0, fail = 0;
foreach (var f in filesToTest.OrderBy(x => x))
{
    if (filter.Length > 0 && !f.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
    var expPath = Path.ChangeExtension(f, ".out");
    if (!File.Exists(expPath)) continue;
    var input = File.Exists(Path.ChangeExtension(f, ".in")) ? File.ReadAllText(Path.ChangeExtension(f, ".in")) : "";
    var sw = Stopwatch.StartNew();
    var r = RunFile(f, input, false);
    var expected = File.ReadAllText(expPath).Replace("\r\n", "\n");
    var got = (r.Output + r.Diag).Replace("\r\n", "\n");
    if (got.TrimEnd() == expected.TrimEnd()) { pass++; Console.WriteLine($"OK    {Rel(f)} ({sw.ElapsedMilliseconds} ms)"); }
    else
    {
        fail++;
        Console.WriteLine($"FAIL  {Rel(f)}");
        var el = expected.TrimEnd().Split('\n'); var gl = got.TrimEnd().Split('\n');
        for (int i = 0; i < Math.Max(el.Length, gl.Length); i++)
        {
            var a = i < el.Length ? el[i] : "<absent>"; var b = i < gl.Length ? gl[i] : "<absent>";
            if (a != b) { Console.WriteLine($"   line {i + 1}:\n     expected: {a}\n     got     : {b}"); break; }
        }
    }
}
Console.WriteLine($"\n{pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;

string Rel(string p) => Path.GetRelativePath(root, p);

int DebugTest()
{
    var path = Path.Combine(root, "samples", "simula", "02_factorial.sim");
    var res = simula.Compile(new SourceFile(path, File.ReadAllText(path)), new CompileOptions());
    var io = new MemIO("", root);
    var exec = res.Program!.CreateExecution(io);
    var dbg = exec.Debugger;
    var pauses = new System.Collections.Concurrent.BlockingCollection<PauseInfo>();
    dbg.Paused += pi => pauses.Add(pi);
    dbg.SetBreakpoints(path, new[] { 8 });
    var t = new Thread(() => exec.Run(CancellationToken.None));
    t.Start();
    int errors = 0;
    void Check(bool cond, string what) { Console.WriteLine((cond ? "OK    " : "FAIL  ") + what); if (!cond) errors++; }
    var p1 = pauses.Take();
    Check(p1.Reason == PauseReason.Breakpoint && p1.Line == 8, $"stops at the breakpoint on line 8 (got {p1.Reason} line {p1.Line})");
    var st = dbg.GetStack();
    Check(st.Count >= 1 && st[0].Line == 8, "call stack: " + string.Join(" <- ", st.Select(f => f.Name + ":" + f.Line)));
    var vars = dbg.GetVariables(0);
    Check(vars.Any(v => v.Name == "i" && v.Value == "0"), "variables: " + string.Join(", ", vars.Select(v => v.Name + "=" + v.Value)));
    dbg.StepInto();
    var p2 = pauses.Take();
    Check(p2.Reason == PauseReason.Step, $"step into -> line {p2.Line}");
    var st2 = dbg.GetStack();
    Check(st2.Count >= 2 && st2[0].Name == "factorial", "entered factorial: " + string.Join(" <- ", st2.Select(f => f.Name + ":" + f.Line)));
    var v2 = dbg.GetVariables(0);
    Check(v2.Any(v => v.Name == "n"), "parameter n visible: " + string.Join(", ", v2.Select(v => v.Name + "=" + v.Value)));
    dbg.StepOut();
    var p3 = pauses.Take();
    Check(p3.Line == 8 || p3.Line == 6, $"step out -> line {p3.Line}");
    dbg.Continue();
    var p4 = pauses.Take();
    Check(p4.Reason == PauseReason.Breakpoint && p4.Line == 8, "next breakpoint (i = 1)");
    Check(dbg.GetVariables(0).Any(v => v.Name == "i" && v.Value == "1"), "i = 1");
    dbg.SetBreakpoints(path, Array.Empty<int>());
    dbg.Continue();
    t.Join();
    Check(io.Out.ToString().Contains("479001600"), "execution finished normally");
    Console.WriteLine(errors == 0 ? "debugger: all correct" : $"debugger: {errors} failure(s)");
    return errors == 0 ? 0 : 1;
}

(bool Ok, string Output, string Diag) RunFile(string path, string input, bool verbose)
{
    var src = new SourceFile(Path.GetFullPath(path), File.ReadAllText(path));
    var res = LangFor(path).Compile(src, new CompileOptions());
    var diag = new StringBuilder();
    foreach (var d in res.Diagnostics)
        diag.AppendLine($"{Path.GetFileName(d.File)}({d.Line},{d.Column}): {(d.Severity == Severity.Error ? "error" : "warning")}: {d.Message}");
    if (!res.Success) return (false, "", diag.ToString());
    var wd = Path.Combine(Path.GetTempPath(), "simulatests_" + Environment.ProcessId);
    Directory.CreateDirectory(wd);
    var io = new MemIO(input, wd);
    var exec = res.Program!.CreateExecution(io);
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var er = exec.Run(cts.Token);
    if (er.Error != null)
    {
        diag.AppendLine($"runtime error ({Path.GetFileName(er.Error.File)}:{er.Error.Line}): {er.Error.Message}");
        if (verbose) foreach (var s in er.Error.Stack) diag.AppendLine($"   at {s.Name} ({Path.GetFileName(s.File)}:{s.Line})");
    }
    if (er.Cancelled) diag.AppendLine("(timeout)");
    return (er.Error == null && !er.Cancelled, io.Out.ToString(), diag.ToString());
}

static string FindRoot()
{
    var d = AppContext.BaseDirectory;
    while (d != null && !File.Exists(Path.Combine(d, "Simula67Studio.sln"))) d = Path.GetDirectoryName(d);
    return d ?? Environment.CurrentDirectory;
}

sealed class MemIO : IProgramIO
{
    readonly StringReader input;
    public readonly StringBuilder Out = new();
    public MemIO(string inp, string wd) { input = new StringReader(inp); WorkingDirectory = wd; }
    public void Write(string text) => Out.Append(text);
    public string? ReadLine() => input.ReadLine();
    public string WorkingDirectory { get; }
}
