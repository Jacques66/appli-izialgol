using System.Diagnostics;
using System.Text;
using Studio.Common;
using Studio.Simula;

// Banc de tests Simula : compile et execute samples/simula/**/*.sim et tests/SimulaTests/cases/**/*.sim,
// compare a <nom>.out (entree optionnelle <nom>.in). Usage : SimulaTests [fichier.sim | filtre]
var root = FindRoot();
var lang = new SimulaLanguage();
if (args.Length == 1 && args[0].EndsWith(".sim") && File.Exists(args[0]))
{
    var r = RunFile(args[0], File.Exists(Path.ChangeExtension(args[0], ".in")) ? File.ReadAllText(Path.ChangeExtension(args[0], ".in")) : "", true);
    Console.Write(r.Output);
    if (r.Diag.Length > 0) Console.WriteLine(r.Diag);
    return r.Ok ? 0 : 1;
}
var filter = args.Length > 0 ? args[0] : "";
var filesToTest = new List<string>();
foreach (var dir in new[] { Path.Combine(root, "samples", "simula"), Path.Combine(root, "tests", "SimulaTests", "cases") })
    if (Directory.Exists(dir)) filesToTest.AddRange(Directory.GetFiles(dir, "*.sim", SearchOption.AllDirectories));
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
        Console.WriteLine($"ECHEC {Rel(f)}");
        var el = expected.TrimEnd().Split('\n'); var gl = got.TrimEnd().Split('\n');
        for (int i = 0; i < Math.Max(el.Length, gl.Length); i++)
        {
            var a = i < el.Length ? el[i] : "<absent>"; var b = i < gl.Length ? gl[i] : "<absent>";
            if (a != b) { Console.WriteLine($"   ligne {i + 1}:\n     attendu : {a}\n     obtenu  : {b}"); break; }
        }
    }
}
Console.WriteLine($"\n{pass} reussi(s), {fail} echec(s)");
return fail == 0 ? 0 : 1;

string Rel(string p) => Path.GetRelativePath(root, p);

(bool Ok, string Output, string Diag) RunFile(string path, string input, bool verbose)
{
    var src = new SourceFile(Path.GetFullPath(path), File.ReadAllText(path));
    var res = lang.Compile(src, new CompileOptions());
    var diag = new StringBuilder();
    foreach (var d in res.Diagnostics)
        diag.AppendLine($"{Path.GetFileName(d.File)}({d.Line},{d.Column}): {(d.Severity == Severity.Error ? "erreur" : "avertissement")}: {d.Message}");
    if (!res.Success) return (false, "", diag.ToString());
    var io = new MemIO(input, Path.GetDirectoryName(Path.GetFullPath(path))!);
    var exec = res.Program!.CreateExecution(io);
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var er = exec.Run(cts.Token);
    if (er.Error != null)
    {
        diag.AppendLine($"erreur d'execution ({Path.GetFileName(er.Error.File)}:{er.Error.Line}): {er.Error.Message}");
        if (verbose) foreach (var s in er.Error.Stack) diag.AppendLine($"   a {s.Name} ({Path.GetFileName(s.File)}:{s.Line})");
    }
    if (er.Cancelled) diag.AppendLine("(delai depasse)");
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
