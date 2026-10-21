using System.Diagnostics;
using System.Text;
using Studio.Common;

namespace PascalTests;

/// <summary>Internal tests: debugger, serialisation, cancellation, interactive input, external files, performance.</summary>
static class InternalTests
{
    static CompilationResult Compile(string src, string name) =>
        Program.Lang.Compile(new SourceFile(name, src), new CompileOptions());

    static void Check(string name, bool ok, string detail)
    {
        if (ok) Program.Pass("internal/" + name); else Program.Fail("internal/" + name, detail);
    }

    /// <summary>Console that records the order of the writes and reads.</summary>
    sealed class LogIO : IProgramIO
    {
        readonly Queue<string> lines;
        public readonly StringBuilder Log = new();
        public readonly StringBuilder Out = new();
        public LogIO(string wd, params string[] input) { WorkingDirectory = wd; lines = new Queue<string>(input); }
        public void Write(string text) { lock (Log) { Log.Append(text); Out.Append(text); } }
        public string? ReadLine() { lock (Log) { var l = lines.Count > 0 ? lines.Dequeue() : null; Log.Append("<read " + (l ?? "EOF") + ">"); return l; } }
        public string WorkingDirectory { get; }
    }

    static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "pascaltests", "internal_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Runs under the debugger; onPause returns the action (0 continue, 1 into, 2 over, 3 out).</summary>
    static (List<(PauseReason reason, int line)> pauses, ExecutionResult res, string output) RunDebug(
        string src, Action<IDebugger> setup, Func<IDebugger, PauseInfo, int> onPause, string name = "dbg.pas")
    {
        var cr = Compile(src, name);
        if (!cr.Success) throw new InvalidOperationException(string.Join("\n", cr.Diagnostics));
        var io = new LogIO(Path.GetTempPath());
        var exec = cr.Program!.CreateExecution(io);
        var pauses = new List<(PauseReason, int)>();
        var dbg = exec.Debugger;
        dbg.Paused += pi =>
        {
            pauses.Add((pi.Reason, pi.Line));
            int act = pauses.Count > 300 ? 0 : onPause(dbg, pi);
            switch (act) { case 1: dbg.StepInto(); break; case 2: dbg.StepOver(); break; case 3: dbg.StepOut(); break; default: dbg.Continue(); break; }
        };
        setup(dbg);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var res = exec.Run(cts.Token);
        return (pauses, res, io.Out.ToString());
    }

    static string Lines(List<(PauseReason reason, int line)> p) => string.Join(",", p.Select(x => x.line));

    static string VarText(IReadOnlyList<VariableInfo> vs) => string.Join("; ", vs.Select(v => $"{v.Name}={v.Value}"));

    const string DebugProgram =
        "program dbg(output);\n" +                                      // 1
        "type rec = record a: integer; b: real end;\n" +                // 2
        "var s: integer; r: rec; arr: array[1..3] of char; p: ^rec;\n" + // 3
        "function sq(n: integer): integer;\n" +                         // 4
        "begin\n" +                                                     // 5
        "   sq := n * n\n" +                                            // 6
        "end;\n" +                                                      // 7
        "procedure add(var x: integer; n: integer);\n" +                // 8
        "begin\n" +                                                     // 9
        "   x := x + sq(n)\n" +                                         // 10
        "end;\n" +                                                      // 11
        "begin\n" +                                                     // 12
        "   s := 0; r.a := 7; r.b := 1.5;\n" +                          // 13
        "   arr[1] := 'x'; arr[2] := 'y'; arr[3] := 'z';\n" +           // 14
        "   new(p); p^ := r;\n" +                                       // 15
        "   add(s, 2);\n" +                                             // 16
        "   add(s, 3);\n" +                                             // 17
        "   writeln(s)\n" +                                             // 18
        "end.\n";                                                       // 19

    static void DebuggerTests()
    {
        // breakpoint, call stack, variables with children
        {
            string stack = "", vars = "", children = "", inner = "";
            var (pauses, res, output) = RunDebug(DebugProgram, d => d.SetBreakpoints("dbg.pas", new[] { 10 }), (d, pi) =>
            {
                if (stack.Length == 0)
                {
                    stack = string.Join(" <- ", d.GetStack().Select(f => f.Name + ":" + f.Line));
                    inner = VarText(d.GetVariables(0));
                    var mainVars = d.GetVariables(1);
                    vars = VarText(mainVars);
                    var rv = mainVars.First(v => v.Name == "r");
                    var pv = mainVars.First(v => v.Name == "p");
                    var deref = d.GetChildren(pv);
                    children = VarText(d.GetChildren(rv)) + " | " + VarText(deref) + " | " + (deref.Count > 0 ? VarText(d.GetChildren(deref[0])) : "");
                }
                return 0;
            });
            Check("debugger: breakpoint hit twice", Lines(pauses) == "10,10" && pauses.All(p => p.reason == PauseReason.Breakpoint), Lines(pauses));
            Check("debugger: call stack", stack == "add:10 <- dbg:16", stack);
            Check("debugger: parameters (var parameter shown by its value)", inner == "x=0; n=2", inner);
            Check("debugger: global variables", vars.Contains("s=0") && vars.Contains("arr='xyz'") && vars.Contains("p=^#1") && vars.Contains("r=(a: 7, b: 1.5)"), vars);
            Check("debugger: record and pointer children", children == "a=7; b=1.5 | ^=(a: 7, b: 1.5) | a=7; b=1.5", children);
            Check("debugger: program completes", res.Error == null && output.Trim() == "13", output + res.Error);
        }
        // step into / over / out
        {
            var seq = new List<string>();
            int n = 0;
            var (pauses, res, _) = RunDebug(DebugProgram, d => d.SetBreakpoints("dbg.pas", new[] { 16 }), (d, pi) =>
            {
                seq.Add(pi.Reason + ":" + pi.Line + ":" + d.GetStack()[0].Name);
                n++;
                return n switch { 1 => 1, 2 => 1, 3 => 3, 4 => 2, _ => 0 };
            });
            // 16 (bp) -into-> 10 (add) -into-> 6 (sq) -out-> 17 (add has no further statement) -over-> 18
            string s = string.Join(" ", seq);
            Check("debugger: step into enters add then sq", seq.Count >= 3 && seq[1] == "Step:10:add" && seq[2] == "Step:6:sq", s);
            Check("debugger: step out returns to the caller", seq.Count >= 4 && seq[3] == "Step:17:dbg", s);
            Check("debugger: step over stays in main", seq.Count == 5 && seq[4] == "Step:18:dbg", s);
            Check("debugger: stepping run completes", res.Error == null, res.Error?.ToString() ?? "");
        }
        // stop on entry, then step over through the main program
        {
            var (pauses, res, _) = RunDebug(DebugProgram, d => d.StopOnEntry = true, (d, pi) => 2);
            Check("debugger: stop on entry + step over", pauses.Count > 0 && pauses[0].reason == PauseReason.Entry && pauses[0].line == 13
                && Lines(pauses) == "13,14,15,16,17,18", Lines(pauses));
        }
        // loops: the while header is a stopping point at each iteration
        {
            const string loop = "program loop(output);\nvar i: integer;\nbegin\n   i := 0;\n   while i < 3 do\n      i := i + 1;\n   repeat\n      i := i - 1\n   until i = 0\nend.\n";
            var (pauses, res, _) = RunDebug(loop, d => d.SetBreakpoints("dbg.pas", new[] { 5, 9 }), (d, pi) => 0);
            Check("debugger: breakpoints on while and until lines", Lines(pauses) == "5,5,5,5,9,9,9", Lines(pauses));
        }
        // break on error
        {
            const string bad = "program bad(output);\nvar a: array[1..2] of integer; i: integer;\nprocedure p(k: integer);\nbegin\n   a[k] := 1\nend;\nbegin\n   for i := 1 to 3 do p(i)\nend.\n";
            string where = "", vars = "";
            var (pauses, res, _) = RunDebug(bad, d => d.BreakOnError = true, (d, pi) =>
            {
                where = $"{pi.Reason}:{pi.Line}:{pi.Message}";
                vars = VarText(d.GetVariables(0)) + " / " + string.Join(" <- ", d.GetStack().Select(f => f.Name + ":" + f.Line));
                return 0;
            });
            Check("debugger: break on runtime error", where.StartsWith("Exception:5:index 3 out of bounds"), where);
            Check("debugger: variables at the error", vars == "k=3 / p:5 <- bad:8", vars);
            Check("debugger: error result after the pause", res.Error != null && res.Error.Line == 5, res.Error?.ToString() ?? "no error");
        }
        // pause request on an endless loop, then cancellation
        {
            const string endless = "program endless(output);\nvar i: integer;\nbegin\n   i := 0;\n   while true do\n      i := i + 1\nend.\n";
            var cr = Compile(endless, "endless.pas");
            var exec = cr.Program!.CreateExecution(new LogIO(Path.GetTempPath()));
            var dbg = exec.Debugger;
            var paused = new ManualResetEventSlim();
            string info = "";
            dbg.Paused += pi => { info = $"{pi.Reason}:{pi.Line}:" + VarText(dbg.GetVariables(0)); paused.Set(); };
            using var cts = new CancellationTokenSource();
            ExecutionResult? result = null;
            var t = new Thread(() => result = exec.Run(cts.Token));
            t.Start();
            Thread.Sleep(200);
            dbg.RequestPause();
            bool ok = paused.Wait(5000);
            cts.Cancel();
            t.Join(10000);
            Check("debugger: pause request", ok && info.StartsWith("PauseRequest:") && info.Contains("i="), info);
            Check("cancellation while paused", result != null && result.Cancelled, result?.ToString() ?? "still running");
        }
    }

    static void SerialisationTests()
    {
        var src = File.ReadAllText(Path.Combine(Program.Root, "samples", "pascal", "04_eight_queens.pas"));
        var cr = Compile(src, "queens.pas");
        var payload = cr.Program!.Serialize();
        var again = Program.Lang.Deserialize(payload);
        var io1 = new LogIO(Path.GetTempPath()); cr.Program.CreateExecution(io1).Run(CancellationToken.None);
        var io2 = new LogIO(Path.GetTempPath()); again.CreateExecution(io2).Run(CancellationToken.None);
        Check("serialisation round trip", io1.Out.ToString() == io2.Out.ToString() && io2.Out.ToString().Contains("92 solutions") && again.LanguageId == "pascal", io2.Out.ToString());
        bool threw = false;
        try { Program.Lang.Deserialize(Encoding.UTF8.GetBytes("garbage")); } catch (Exception) { threw = true; }
        Check("invalid payload rejected", threw, "no exception");
    }

    static void CancellationTest()
    {
        var cr = Compile("program spin(output);\nbegin\n   while true do\nend.\n", "spin.pas");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var sw = Stopwatch.StartNew();
        var res = cr.Program!.CreateExecution(new LogIO(Path.GetTempPath())).Run(cts.Token);
        Check("cancellation of an endless loop", res.Cancelled && sw.ElapsedMilliseconds < 5000, res.ToString());
        var cr2 = Compile("program rec(output);\nprocedure p; begin p end;\nbegin p end.\n", "rec.pas");
        var res2 = cr2.Program!.CreateExecution(new LogIO(Path.GetTempPath())).Run(CancellationToken.None);
        Check("infinite recursion reported as stack overflow", res2.Error != null && res2.Error.Message.Contains("stack overflow"), res2.ToString());
    }

    static void InteractiveInputTest()
    {
        const string src = "program ask(input, output);\nvar n, m: integer;\nbegin\n   write('first? '); readln(n);\n   write('second? '); readln(m);\n   writeln('sum ', n + m:1)\nend.\n";
        var cr = Compile(src, "ask.pas");
        var io = new LogIO(Path.GetTempPath(), "3", "4");
        var res = cr.Program!.CreateExecution(io).Run(CancellationToken.None);
        var log = io.Log.ToString();
        Check("lazy input: prompts are written before each read", res.Error == null && log == "first? <read 3>second? <read 4>sum 7\n", log);
        const string src2 = "program noinput(input, output);\nbegin\n   writeln('no read at all')\nend.\n";
        var io2 = new LogIO(Path.GetTempPath(), "x");
        Compile(src2, "noinput.pas").Program!.CreateExecution(io2).Run(CancellationToken.None);
        Check("lazy input: the console is not read when the program does not read", !io2.Log.ToString().Contains("<read"), io2.Log.ToString());
    }

    static void ExternalFileTest()
    {
        var wd = TempDir();
        File.WriteAllText(Path.Combine(wd, "numbers"), "5 6\n7\n");
        const string src = "program files(output, numbers, total, data);\nvar numbers, total: text; data: file of integer; n, s: integer;\n" +
                           "begin\n   reset(numbers); s := 0;\n   while not eof(numbers) do begin read(numbers, n); s := s + n; if eoln(numbers) then readln(numbers) end;\n" +
                           "   rewrite(total); writeln(total, 'total ', s:1); write(total, 'last line');\n" +
                           "   rewrite(data); write(data, s, 2 * s)\nend.\n";
        var res = Compile(src, "files.pas").Program!.CreateExecution(new LogIO(wd)).Run(CancellationToken.None);
        var total = File.Exists(Path.Combine(wd, "total")) ? File.ReadAllText(Path.Combine(wd, "total")) : "<missing>";
        Check("external text file read from and written to the working directory", res.Error == null && total.Replace("\r\n", "\n") == "total 18\nlast line\n", total + res.Error);
        // the data file written by the first program is read back by a second one
        const string src2 = "program back(output, data);\nvar data: file of integer; n: integer;\nbegin\n   reset(data);\n   while not eof(data) do begin read(data, n); write(n:4) end;\n   writeln\nend.\n";
        var io = new LogIO(wd);
        var res2 = Compile(src2, "back.pas").Program!.CreateExecution(io).Run(CancellationToken.None);
        Check("external file of integer persists between programs", res2.Error == null && io.Out.ToString() == "  18  36\n", io.Out + res2.Error?.ToString());
        // a missing external file reads as an empty file
        var io3 = new LogIO(wd);
        var res3 = Compile("program empty(output, nothing);\nvar nothing: text;\nbegin reset(nothing); writeln(eof(nothing)) end.\n", "empty.pas").Program!.CreateExecution(io3).Run(CancellationToken.None);
        Check("missing external file is empty", res3.Error == null && io3.Out.ToString().Trim() == "TRUE", io3.Out + res3.Error?.ToString());
        try { Directory.Delete(wd, true); } catch { }
    }

    static void DiagnosticsTest()
    {
        var cr = Compile("program d(output);\nvar i: integer;\nbegin\n   i := 'x';\n   j := 2\nend.\n", "diag.pas");
        var errs = cr.Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Check("several errors with file, line and column", !cr.Success && errs.Count == 2 && errs[0].Line == 4 && errs[0].Column == 6 && errs[1].Line == 5 && errs[1].Column == 4 && errs[0].File == "diag.pas",
            string.Join("\n", cr.Diagnostics));
    }

    static void PerformanceTest()
    {
        const string src = "program perf(output);\nvar i, j, s: integer; a: array[0..999] of integer;\nbegin\n   s := 0;\n" +
                           "   for i := 0 to 999 do a[i] := i;\n   for j := 1 to 3000 do for i := 0 to 999 do s := s + a[i] mod 7;\n   writeln(s)\nend.\n";
        var cr = Compile(src, "perf.pas");
        var io = new LogIO(Path.GetTempPath());
        var sw = Stopwatch.StartNew();
        var res = cr.Program!.CreateExecution(io).Run(CancellationToken.None);
        sw.Stop();
        Check($"performance: 3 million loop iterations in {sw.ElapsedMilliseconds} ms", res.Error == null && io.Out.ToString().Trim() == "8991000" && sw.ElapsedMilliseconds < 20000, io.Out + res.Error?.ToString());
    }

    public static void Run()
    {
        DebuggerTests();
        SerialisationTests();
        CancellationTest();
        InteractiveInputTest();
        ExternalFileTest();
        DiagnosticsTest();
        PerformanceTest();
    }
}
