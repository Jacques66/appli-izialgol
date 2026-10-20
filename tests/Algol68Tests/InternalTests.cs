using System.Diagnostics;
using Studio.Common;
using Studio.Algol68;

namespace Algol68Tests;

/// <summary>Internal tests: inline programs, errors, debugging, serialisation, cancellation, performance.</summary>
static class InternalTests
{
    static readonly Algol68Language lang = new();

    static CompilationResult Compile(string src, string name = "internal.a68") =>
        lang.Compile(new SourceFile(name, src), new CompileOptions());

    static void Expect(string name, string src, string expected, string? input = null)
    {
        var cr = Compile(src, name + ".a68");
        if (!cr.Success) { Program.Fail("internal/" + name, string.Join("\n", cr.Diagnostics)); return; }
        var (o, res, _) = Program.Execute(cr.Program!, input);
        o = o.Replace("\r\n", "\n");
        if (res.Error != null) { Program.Fail("internal/" + name, res.Error.ToString()); return; }
        if (o != expected) { Program.Fail("internal/" + name, Program.Diff(expected, o)); return; }
        Program.Pass("internal/" + name);
    }

    static void ExpectCompileError(string name, string src, string fragment, int line = 0)
    {
        var cr = Compile(src, name + ".a68");
        if (cr.Success) { Program.Fail("internal/" + name, "compilation error expected"); return; }
        if (!cr.Diagnostics.Any(d => d.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase) && (line == 0 || d.Line == line)))
        { Program.Fail("internal/" + name, $"expected '{fragment}' (line {line}), got:\n" + string.Join("\n", cr.Diagnostics)); return; }
        Program.Pass("internal/" + name);
    }

    static void ExpectRuntimeError(string name, string src, string fragment, int line = 0)
    {
        var cr = Compile(src, name + ".a68");
        if (!cr.Success) { Program.Fail("internal/" + name, string.Join("\n", cr.Diagnostics)); return; }
        var (_, res, _) = Program.Execute(cr.Program!, null);
        if (res.Error == null) { Program.Fail("internal/" + name, "runtime error expected"); return; }
        if (!res.Error.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase) || (line != 0 && res.Error.Line != line))
        { Program.Fail("internal/" + name, $"expected '{fragment}' at line {line}, got {res.Error}"); return; }
        Program.Pass("internal/" + name);
    }

    /// <summary>Runs under the debugger; onPause chooses the action (0 continue, 1 into, 2 over, 3 out).</summary>
    static (List<(PauseReason reason, int line)> pauses, ExecutionResult res, string output) RunDebug(
        string src, Action<IDebugger> setup, Func<IDebugger, PauseInfo, int> onPause, string name = "dbg.a68")
    {
        var cr = Compile(src, name);
        if (!cr.Success) throw new InvalidOperationException(string.Join("\n", cr.Diagnostics));
        var io = new TestIO(null, Path.GetTempPath());
        var exec = cr.Program!.CreateExecution(io);
        var pauses = new List<(PauseReason, int)>();
        var dbg = exec.Debugger;
        dbg.Paused += pi =>
        {
            pauses.Add((pi.Reason, pi.Line));
            int act = pauses.Count > 200 ? 0 : onPause(dbg, pi);
            switch (act) { case 1: dbg.StepInto(); break; case 2: dbg.StepOver(); break; case 3: dbg.StepOut(); break; default: dbg.Continue(); break; }
        };
        setup(dbg);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var res = exec.Run(cts.Token);
        return (pauses, res, io.Output);
    }

    static void Check(string name, bool ok, string detail)
    {
        if (ok) Program.Pass("internal/" + name); else Program.Fail("internal/" + name, detail);
    }

    static string Lines(List<(PauseReason reason, int line)> p) => string.Join(",", p.Select(x => x.line));

    static void DebuggerTests()
    {
        const string prog = "INT s := 0;\nPROC p = (INT n) VOID:\n   s +:= n;\np(1); p(2);\nprint(s)\n";
        // breakpoint on the body of a PROC made of a single unit (on its own line)
        {
            string vars = "", stack = "";
            var (p, res, _) = RunDebug(prog, d => d.SetBreakpoints("dbg.a68", new[] { 3 }), (d, pi) =>
            {
                if (vars == "")
                {
                    vars = string.Join(";", d.GetVariables(0).Select(v => $"{v.Name}={v.Value}"));
                    vars += "|" + string.Join(";", d.GetVariables(1).Select(v => $"{v.Name}={v.Value}"));
                    stack = string.Join(";", d.GetStack().Select(s => $"{s.Name}@{s.Line}"));
                }
                return 0;
            });
            Check("debugger-breakpoint-unit-body", res.Error == null && Lines(p) == "3,3" && p.All(x => x.reason == PauseReason.Breakpoint),
                $"pauses: {Lines(p)} error: {res.Error}");
            Check("debugger-variables", vars == "n=1|s=0;p=routine p", "variables: " + vars);
            Check("debugger-stack", stack == "p@3;main program@4", "stack: " + stack);
        }
        // step into
        {
            var (p, res, _) = RunDebug(prog, d => d.StopOnEntry = true, (d, pi) => 1);
            Check("debugger-step-into", res.Error == null && Lines(p) == "1,2,4,3,4,3,5" && p[0].reason == PauseReason.Entry,
                $"pauses: {Lines(p)}");
        }
        // step over
        {
            var (p, res, _) = RunDebug(prog, d => d.StopOnEntry = true, (d, pi) => 2);
            Check("debugger-step-over", res.Error == null && Lines(p) == "1,2,4,5", $"pauses: {Lines(p)}");
        }
        // step out of a routine
        {
            var (p, res, _) = RunDebug(prog, d => d.SetBreakpoints("dbg.a68", new[] { 3 }), (d, pi) => pi.Line == 3 ? 3 : 0);
            Check("debugger-step-out", res.Error == null && Lines(p) == "3,4,3,5", $"pauses: {Lines(p)}");
        }
        // pause on error, with the stack intact
        {
            const string bad = "PROC f = (INT k) INT:\n   k % 0;\nprint(f(3))\n";
            string stack = "";
            var (p, res, _) = RunDebug(bad, d => d.BreakOnError = true, (d, pi) => { stack = string.Join(";", d.GetStack().Select(s => $"{s.Name}@{s.Line}")); return 0; });
            Check("debugger-pause-on-error", p.Count == 1 && p[0].reason == PauseReason.Exception && p[0].line == 2 && res.Error != null && stack == "f@2;main program@3",
                $"pauses: {Lines(p)} stack: {stack} error: {res.Error}");
        }
        // pause request during a long loop
        {
            var cr = Compile("INT n := 0;\nWHILE n < 100000000 DO\n   n +:= 1\nOD\n", "dbg.a68");
            var exec = cr.Program!.CreateExecution(new TestIO(null, Path.GetTempPath()));
            var dbg = exec.Debugger;
            PauseInfo? got = null; string nval = "";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            dbg.Paused += pi => { got = pi; nval = dbg.GetVariables(0).First(v => v.Name == "n").Value; cts.Cancel(); dbg.Continue(); };
            var timer = new Timer(_ => dbg.RequestPause(), null, 200, Timeout.Infinite);
            var res = exec.Run(cts.Token);
            timer.Dispose();
            Check("debugger-pause-request", res.Error == null && res.Cancelled && got != null && got.Reason == PauseReason.PauseRequest && got.Line is 2 or 3 && nval != "0",
                $"pause {got?.Reason} line {got?.Line} n={nval} {res.Error}");
        }
        // structures and rows can be expanded
        {
            const string src = "MODE P = STRUCT (INT x, [3] REAL v);\nP p;\nx OF p := 4; v OF p := (1, 2, 3);\nprint(x OF p)\n";
            string kids = "";
            var (p, res, _) = RunDebug(src, d => d.SetBreakpoints("dbg.a68", new[] { 4 }), (d, pi) =>
            {
                var pv = d.GetVariables(0).First(v => v.Name == "p");
                var ch = d.GetChildren(pv);
                kids = string.Join(";", ch.Select(c => $"{c.Name}={c.Value}"));
                var vv = ch.First(c => c.Name == "v");
                kids += "|" + string.Join(";", d.GetChildren(vv).Select(c => $"{c.Name}={c.Value}"));
                return 0;
            });
            Check("debugger-children", kids == "x=4;v=[1:3] (3 elements)|[1]=1;[2]=2;[3]=3", "children: " + kids);
        }
    }

    static void MiscTests()
    {
        // cancellation of an infinite loop
        {
            var cr = Compile("INT n := 0;\nDO n +:= 1 OD\n");
            var exec = cr.Program!.CreateExecution(new TestIO(null, Path.GetTempPath()));
            using var cts = new CancellationTokenSource(300);
            var sw = Stopwatch.StartNew();
            var res = exec.Run(cts.Token);
            Check("cancel-loop", res.Cancelled && sw.ElapsedMilliseconds < 5000, $"cancelled={res.Cancelled} in {sw.ElapsedMilliseconds} ms");
        }
        // cancellation inside PAR branches and while waiting on a semaphore
        {
            var cr = Compile("SEMA s = LEVEL 0;\nPAR (DO SKIP OD, (DOWN s; SKIP), DO SKIP OD)\n");
            var exec = cr.Program!.CreateExecution(new TestIO(null, Path.GetTempPath()));
            using var cts = new CancellationTokenSource(300);
            var res = exec.Run(cts.Token);
            Check("cancel-par", res.Cancelled, $"cancelled={res.Cancelled} error={res.Error}");
        }
        // cancellation while the debugger is paused
        {
            var cr = Compile("print(1);\nprint(2)\n");
            var exec = cr.Program!.CreateExecution(new TestIO(null, Path.GetTempPath()));
            var cts = new CancellationTokenSource();
            exec.Debugger.StopOnEntry = true;
            exec.Debugger.Paused += pi => cts.CancelAfter(100); // never resumes
            var res = exec.Run(cts.Token);
            Check("cancel-while-paused", res.Cancelled, $"cancelled={res.Cancelled}");
        }
        // serialisation: sources + includes, reloaded by Deserialize
        {
            var inc = new SourceFile("lib.a68", "PROC double = (INT x) INT: 2 * x;\n");
            var opts = new CompileOptions { IncludeResolver = (n, from) => n == "lib.a68" ? inc : null };
            var cr = lang.Compile(new SourceFile("main.a68", "PR include \"lib.a68\" PR\nprint((double(21), newline))\n"), opts);
            if (!cr.Success) { Program.Fail("internal/serialisation", string.Join("\n", cr.Diagnostics)); }
            else
            {
                var bytes = cr.Program!.Serialize();
                var p2 = lang.Deserialize(bytes);
                var (o, res, _) = Program.Execute(p2, null);
                Check("serialisation", p2.LanguageId == "algol68" && res.Error == null && o == "        +42\n", $"output [{o}] {res.Error}");
            }
        }
        // missing included file
        ExpectCompileError("missing-include", "PR include \"missing.a68\" PR\nSKIP\n", "not found", 1);
        // performance: 10 million iterations
        {
            var cr = Compile("INT s := 0;\nFOR i TO 10000000 DO s +:= i MOD 3 OD;\nprint(s)\n");
            var sw = Stopwatch.StartNew();
            var (o, res, _) = Program.Execute(cr.Program!, null);
            Check("performance-10M", res.Error == null && o.Trim() == "+10000000" && sw.Elapsed.TotalSeconds < 5,
                $"output {o} in {sw.Elapsed.TotalSeconds:F2} s");
        }
        // call stack in the runtime error
        {
            var cr = Compile("PROC g = (INT n) INT: 10 % n;\nPROC f = (INT n) INT: g(n - 1);\nprint(f(1))\n", "stack.a68");
            var (_, res, _) = Program.Execute(cr.Program!, null);
            var st = res.Error == null ? "" : string.Join(";", res.Error.Stack.Select(s => $"{s.Name}@{s.Line}"));
            Check("error-stack", res.Error != null && res.Error.Line == 1 && res.Error.File == "stack.a68" && st == "g@1;f@2;main program@3",
                $"{res.Error} stack={st}");
        }
        // exact position of diagnostics
        {
            var cr = Compile("BEGIN\n   INT x := 1;\n   x := y + 1\nEND\n");
            var d = cr.Diagnostics.FirstOrDefault();
            Check("diagnostic-position", d != null && d.Line == 3 && d.Column == 9 && d.Severity == Severity.Error, d?.ToString() ?? "none");
        }
        // error in a PAR branch: propagation and stop of the other branches
        ExpectRuntimeError("par-error", "[2] INT t;\nPAR (DO SKIP OD, t[3] := 1)\n", "out of bounds", 2);
        // breakpoint in a parallel branch
        {
            const string src = "INT a := 0, b := 0;\nPAR (a := 1,\n     b := 2);\nprint((a + b, newline))\n";
            var (p, res, o) = RunDebug(src, d => d.SetBreakpoints("dbg.a68", new[] { 3 }), (d, pi) => 0);
            Check("debugger-par", res.Error == null && Lines(p) == "3" && o == "         +3\n", $"pauses {Lines(p)} output {o} {res.Error}");
        }
        // printing a structure with an uninitialised field
        ExpectRuntimeError("uninitialised-field", "STRUCT (INT a, b) s;\na OF s := 1;\nprint(s)\n", "uninitialised", 3);
        // exact values of a few conversions (a68g algorithms)
        Expect("conversions", "print((whole(0, 0), whole(-5, 3), fixed(2.5, 0, 0), fixed(-0.25, 0, 2), float(0.0, 10, 3, 2), newline))",
            "0 -53-.25 +0.000e+0\n");
        Expect("stop", "print((\"a\", newline)); stop; print(\"b\")", "a\n");
    }

    /// <summary>Robustness: truncated or altered programs -> diagnostics, never an internal error.</summary>
    static void FuzzTests()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "algol68");
        if (!Directory.Exists(dir)) return;
        var rnd = new Random(1234);
        int cases = 0; var bad = new List<string>();
        foreach (var f in Directory.GetFiles(dir, "*.a68", SearchOption.AllDirectories).OrderBy(x => x))
        {
            var text = File.ReadAllText(f);
            for (int k = 0; k < 12; k++)
            {
                string t = text;
                int a = rnd.Next(t.Length), len = rnd.Next(1, 12);
                t = (k % 3) switch
                {
                    0 => t.Remove(a, Math.Min(len, t.Length - a)),
                    1 => t.Insert(a, new[] { "(", ")", ";", "IF", "OD", ",", ":=", "[", "$", "\"", "#", "'", "END" }[rnd.Next(13)]),
                    _ => t[..a],
                };
                cases++;
                try
                {
                    var cr = lang.Compile(new SourceFile(Path.GetFileName(f), t), new CompileOptions());
                    if (cr.Diagnostics.Any(d => d.Message.Contains("internal compiler error"))) bad.Add($"{Path.GetFileName(f)} #{k}: {cr.Diagnostics.First(d => d.Message.Contains("internal compiler error")).Message}");
                }
                catch (Exception e) { bad.Add($"{Path.GetFileName(f)} #{k}: exception {e.GetType().Name} {e.Message}"); }
            }
        }
        Check("compiler-robustness", bad.Count == 0, $"{bad.Count}/{cases} failing cases:\n" + string.Join("\n", bad.Take(15)));
    }

    public static void Run()
    {
        FuzzTests();
        Expect("hello", "print((\"Hello\", newline))", "Hello\n");
        DebuggerTests();
        MiscTests();
    }
}
