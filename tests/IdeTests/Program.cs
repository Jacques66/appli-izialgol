// End-to-end tests of the tooling (without the real compilers):
// "echo" test language, debugger, standalone exe through Runner, CLI, editor analysis.
// Usage: dotnet run --project tests/IdeTests [-- --samples]
//   --samples: also compiles and runs samples/simula and samples/algol68 with the real compilers.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Studio.Common;
using Studio.Ide.Language;
using Studio.Tooling;
using Studio.Tooling.Testing;

namespace Studio.IdeTests;

static class Program
{
    static int _passed, _failed;
    static readonly List<string> Failures = new();

    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var sw = Stopwatch.StartNew();

        Test("echo: successful compilation", EchoCompileOk);
        Test("echo: diagnostics (line/column, warning, unterminated block)", EchoDiagnostics);
        Test("echo: execution with input/output and end of file", EchoRunIo);
        Test("echo: runtime error with call stack", EchoRuntimeError);
        Test("echo: included files (IncludeResolver)", EchoInclude);
        Test("echo: serialization / deserialization", EchoSerialize);
        Test("debugger: breakpoint, stack, variables, children", DebugBreakpoint);
        Test("debugger: step into / step over / step out", DebugStepping);
        Test("debugger: stop on entry and pause request", DebugEntryAndPause);
        Test("debugger: stop (cancellation) while paused", DebugCancelWhilePaused);
        Test("debugger: break on runtime error", DebugBreakOnError);
        Test("debugger: breakpoints changed while running", DebugBreakpointsAtRuntime);
        Test("execution: cancelling a program blocked on input", CancelDuringRead);
        Test("Runner: without a program -> usage message", RunnerUsage);
        Test("standalone exe: generation + execution + standard input", ExeEndToEnd);
        Test("standalone exe: runtime error on stderr, exit code", ExeRuntimeError);
        Test("standalone exe: program exit code", ExeExitCode);
        Test("simc / a68c: usage, file not found, diagnostics", CliBasics);
        Test("Simula editor: tokens and comments", SimulaLexer);
        Test("Simula editor: folding, matching, outline", SimulaStructure);
        Test("Algol 68 editor: UPPER/QUOTE stropping, comments", AlgolLexer);
        Test("Algol 68 editor: folding, matching, outline", AlgolStructure);
        Test("editor: automatic indentation", Indentation);
        Test("editor: comment / uncomment", CommentToggle);

        if (args.Contains("--samples")) Samples();

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed in {sw.Elapsed.TotalSeconds:F1} s");
        foreach (var f in Failures) Console.WriteLine("  FAILED: " + f);
        return _failed == 0 ? 0 : 1;
    }

    static void Test(string name, Action body)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            body();
            _passed++;
            Console.WriteLine($"  ok      {name} ({sw.ElapsedMilliseconds} ms)");
        }
        catch (Exception ex)
        {
            _failed++;
            Failures.Add(name + ": " + ex.Message);
            Console.WriteLine("  FAILED  " + name);
            Console.WriteLine("          " + ex.Message.Replace("\n", "\n          "));
        }
    }

    static void Check(bool cond, string message)
    {
        if (!cond) throw new Exception(message);
    }

    static void Eq<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what}: expected \"{expected}\", got \"{actual}\"");
    }

    // ------------------------------------------------------------------ helpers

    sealed class FakeIO : IProgramIO
    {
        readonly BlockingCollection<string?> _input = new();
        readonly StringBuilder _out = new();
        public FakeIO(params string?[] lines) { foreach (var l in lines) _input.Add(l); }
        public void Add(string? line) => _input.Add(line);
        public void Write(string text) { lock (_out) _out.Append(text); }
        public string? ReadLine() => _input.TryTake(out var l, TimeSpan.FromSeconds(10)) ? l : null;
        public string WorkingDirectory => Path.GetTempPath();
        public string Output { get { lock (_out) return _out.ToString(); } }
    }

    static readonly EchoLanguage Echo = new();
    static string TempDir = Path.Combine(Path.GetTempPath(), "Simula67StudioTests", Guid.NewGuid().ToString("N")[..8]);

    static ICompiledProgram CompileEcho(string text, string path = "C:\\test\\prog.echo", Func<string, string, SourceFile?>? resolver = null)
    {
        var r = Echo.Compile(new SourceFile(path, text), new CompileOptions { IncludeResolver = resolver });
        Check(r.Success, "compilation failed: " + string.Join("; ", r.Diagnostics));
        return r.Program!;
    }

    const string DebugProgram =
        "# debugging program\n" +            // 1
        "set x = 1\n" +                       // 2
        "list l = a, b, c\n" +                // 3
        "call p\n" +                          // 4
        "print end {x}\n" +                   // 5
        "proc p\n" +                          // 6
        "  set local = 42\n" +                // 7
        "  add x 10\n" +                      // 8
        "  print in p\n" +                    // 9
        "end\n";                              // 10

    /// <summary>Runs the program on a thread; pauses arrive in the queue.</summary>
    sealed class DebugSession
    {
        public readonly BlockingCollection<PauseInfo> Pauses = new();
        public readonly IProgramExecution Exec;
        public readonly IDebugger Dbg;
        public readonly CancellationTokenSource Cts = new();
        public readonly FakeIO IO;
        Thread? _thread;
        public ExecutionResult? Result;

        public DebugSession(ICompiledProgram p, FakeIO? io = null)
        {
            IO = io ?? new FakeIO();
            Exec = p.CreateExecution(IO);
            Dbg = Exec.Debugger;
            Dbg.Paused += info => Pauses.Add(info);
        }

        public void Start()
        {
            _thread = new Thread(() => Result = Exec.Run(Cts.Token)) { IsBackground = true };
            _thread.Start();
        }

        public PauseInfo Next()
        {
            if (!Pauses.TryTake(out var p, TimeSpan.FromSeconds(10))) throw new Exception("expected pause not received");
            return p;
        }

        public ExecutionResult Wait()
        {
            if (!_thread!.Join(TimeSpan.FromSeconds(10))) throw new Exception("the program did not finish");
            return Result!;
        }
    }

    // ------------------------------------------------------------------ echo

    static void EchoCompileOk()
    {
        var r = Echo.Compile(new SourceFile("a.echo", "print hello\nset x = 3\n"), new CompileOptions());
        Check(r.Success, "success expected");
        Eq(0, r.Diagnostics.Count, "number of diagnostics");
        Eq("echo", r.Program!.LanguageId, "LanguageId");
    }

    static void EchoDiagnostics()
    {
        var r = Echo.Compile(new SourceFile("b.echo", "print ok\n   bidule 3\nprint\nloop 2\n  print x\n"), new CompileOptions());
        Check(!r.Success, "failure expected");
        Check(r.Program == null, "no program expected");
        var err = r.Diagnostics.First(d => d.Message.Contains("bidule"));
        Eq(2, err.Line, "line"); Eq(4, err.Column, "column"); Eq(Severity.Error, err.Severity, "severity");
        Eq("b.echo(2,4): error: unknown statement 'bidule'", err.ToString(), "format");
        Check(r.Diagnostics.Any(d => d.Severity == Severity.Warning && d.Line == 3), "warning on line 3");
        Check(r.Diagnostics.Any(d => d.Line == 4 && d.Message.Contains("end")), "unterminated block on line 4");
    }

    static void EchoRunIo()
    {
        var p = CompileEcho("print What is your name?\nread name\nprint Hello {name}!\nread other\nprint eof={eof}\n");
        var io = new FakeIO("Ada", null);
        var res = p.CreateExecution(io).Run(CancellationToken.None);
        Eq(0, res.ExitCode, "code");
        Check(res.Error == null, "no error");
        Eq("What is your name?\nHello Ada!\neof=1\n", io.Output, "output");
    }

    static void EchoRuntimeError()
    {
        var p = CompileEcho("print before\ncall p\nproc p\n  error boom {x}\nend\n");
        var res = p.CreateExecution(new FakeIO()).Run(CancellationToken.None);
        Check(res.Error != null, "error expected");
        Eq(4, res.Error!.Line, "error line");
        Eq("boom ", res.Error.Message, "message");
        Eq(2, res.Error.Stack.Count, "stack depth");
        Eq("p", res.Error.Stack[0].Name, "frame 0");
        Eq(2, res.Error.Stack[1].Line, "call line");
        Check(ProgramHost.FormatError(res.Error).Contains("at p in"), "stack format");
    }

    static void EchoInclude()
    {
        var resolver = new Func<string, string, SourceFile?>((name, from) =>
            name == "lib.echo" ? new SourceFile("C:\\test\\lib.echo", "proc greet\n  print hi\nend\n") : null);
        var p = CompileEcho("include lib.echo\ncall greet\n", resolver: resolver);
        var io = new FakeIO();
        p.CreateExecution(io).Run(CancellationToken.None);
        Eq("hi\n", io.Output, "output");
        var bad = Echo.Compile(new SourceFile("x.echo", "include missing.echo\n"), new CompileOptions { IncludeResolver = resolver });
        Check(!bad.Success && bad.Diagnostics[0].Message.Contains("not found"), "missing include");
    }

    static void EchoSerialize()
    {
        var resolver = new Func<string, string, SourceFile?>((name, from) =>
            new SourceFile("C:\\test\\lib.echo", "proc greet\n  print hi {x}\nend\n"));
        var p = CompileEcho("set x = 7\ninclude lib.echo\ncall greet\n", resolver: resolver);
        var p2 = Echo.Deserialize(p.Serialize());
        var io = new FakeIO();
        p2.CreateExecution(io).Run(CancellationToken.None);
        Eq("hi 7\n", io.Output, "output after deserialization");
    }

    // ------------------------------------------------------------------ debugger

    static void DebugBreakpoint()
    {
        var s = new DebugSession(CompileEcho(DebugProgram));
        s.Dbg.SetBreakpoints("C:\\test\\prog.echo", new[] { 8 });
        s.Start();
        var p = s.Next();
        Eq(PauseReason.Breakpoint, p.Reason, "reason");
        Eq(8, p.Line, "line");
        var stack = s.Dbg.GetStack();
        Eq(2, stack.Count, "depth");
        Eq("p", stack[0].Name, "frame 0");
        Eq(8, stack[0].Line, "frame 0 line");
        Eq(4, stack[1].Line, "frame 1 line");
        var locals = s.Dbg.GetVariables(0);
        Check(locals.Any(v => v.Name == "local" && v.Value == "42"), "local variable");
        var globals = s.Dbg.GetVariables(1);
        var l = globals.First(v => v.Name == "l");
        Check(l.HasChildren, "list with children");
        var kids = s.Dbg.GetChildren(l);
        Eq(3, kids.Count, "children"); Eq("\"b\"", kids[1].Value, "child 1");
        s.Dbg.Continue();
        var res = s.Wait();
        Eq(0, res.ExitCode, "code");
        Eq("in p\nend 11\n", s.IO.Output, "output");
    }

    static void DebugStepping()
    {
        var s = new DebugSession(CompileEcho(DebugProgram));
        s.Dbg.SetBreakpoints("C:\\test\\prog.echo", new[] { 4 });
        s.Start();
        Eq(4, s.Next().Line, "stop on call");
        s.Dbg.StepInto();
        var p = s.Next();
        Eq(PauseReason.Step, p.Reason, "step reason"); Eq(7, p.Line, "step into -> line 7");
        s.Dbg.StepOver();
        Eq(8, s.Next().Line, "step over -> line 8");
        s.Dbg.StepOut();
        Eq(5, s.Next().Line, "step out -> line 5");
        s.Dbg.StepOver();
        s.Wait();
        // step over a call
        var s2 = new DebugSession(CompileEcho(DebugProgram));
        s2.Dbg.SetBreakpoints("C:\\test\\prog.echo", new[] { 4 });
        s2.Start();
        Eq(4, s2.Next().Line, "stop on call (2)");
        s2.Dbg.StepOver();
        Eq(5, s2.Next().Line, "step over the call");
        s2.Dbg.Continue();
        s2.Wait();
    }

    static void DebugEntryAndPause()
    {
        var s = new DebugSession(CompileEcho("set n = 0\nloop 100000\n  add n 1\n  sleep 1\nend\n"));
        s.Dbg.StopOnEntry = true;
        s.Start();
        var p = s.Next();
        Eq(PauseReason.Entry, p.Reason, "entry reason"); Eq(1, p.Line, "entry line");
        s.Dbg.Continue();
        Thread.Sleep(100);
        s.Dbg.RequestPause();
        p = s.Next();
        Eq(PauseReason.PauseRequest, p.Reason, "pause reason");
        Check(p.Line is 3 or 4, "line inside the loop");
        s.Cts.Cancel();
        var r = s.Wait();
        Check(r.Cancelled, "cancelled");
    }

    static void DebugCancelWhilePaused()
    {
        var s = new DebugSession(CompileEcho(DebugProgram));
        s.Dbg.SetBreakpoints("C:\\test\\prog.echo", new[] { 2 });
        s.Start();
        s.Next();
        s.Cts.Cancel();
        var r = s.Wait();
        Check(r.Cancelled, "Cancelled expected");
    }

    static void DebugBreakOnError()
    {
        var s = new DebugSession(CompileEcho("set v = 5\ncall p\nproc p\n  set w = 1\n  error bad {v}\nend\n"));
        s.Dbg.BreakOnError = true;
        s.Start();
        var p = s.Next();
        Eq(PauseReason.Exception, p.Reason, "reason");
        Eq(5, p.Line, "line");
        Eq("bad 5", p.Message, "message");
        Eq(2, s.Dbg.GetStack().Count, "stack preserved");
        Check(s.Dbg.GetVariables(0).Any(v => v.Name == "w"), "local variables visible");
        s.Dbg.Continue();
        var r = s.Wait();
        Check(r.Error != null && r.Error.Line == 5, "error returned after the pause");
    }

    static void DebugBreakpointsAtRuntime()
    {
        var s = new DebugSession(CompileEcho("print a\nread x\nprint b\nprint c\n"));
        s.Dbg.SetBreakpoints("C:\\test\\prog.echo", new[] { 1 });
        s.Start();
        Eq(1, s.Next().Line, "first stop");
        s.Dbg.SetBreakpoints("C:\\test\\prog.echo", new[] { 4 });
        s.Dbg.Continue();
        s.IO.Add("value");
        Eq(4, s.Next().Line, "new stop");
        s.Dbg.Continue();
        s.Wait();
        Eq("a\nb\nc\n", s.IO.Output, "output");
    }

    static void CancelDuringRead()
    {
        // The program waits for input: the IDE cancels and the IO returns null (end of file).
        var io = new CancellableIO();
        var exec = CompileEcho("read x\nread y\nprint done\n").CreateExecution(io);
        var cts = new CancellationTokenSource();
        io.Token = cts.Token;
        ExecutionResult? res = null;
        var t = new Thread(() => res = exec.Run(cts.Token));
        t.Start();
        Thread.Sleep(100);
        cts.Cancel();
        Check(t.Join(5000), "finished after cancellation");
        Check(res!.Cancelled, "Cancelled");
    }

    sealed class CancellableIO : IProgramIO
    {
        public CancellationToken Token;
        public void Write(string text) { }
        public string? ReadLine() { Token.WaitHandle.WaitOne(); return null; }
        public string WorkingDirectory => ".";
    }

    // ------------------------------------------------------------------ standalone exe

    static string RunnerPath()
    {
        var r = RunnerLocator.Find();
        Check(r != null, RunnerLocator.NotFoundMessage);
        return r!;
    }

    static (int Code, string Out, string Err) RunProcess(string exe, string args = "", string? stdin = null, int timeoutMs = 20000)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        using var proc = Process.Start(psi)!;
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        if (stdin != null) proc.StandardInput.Write(stdin);
        proc.StandardInput.Close();
        if (!proc.WaitForExit(timeoutMs)) { proc.Kill(true); throw new Exception($"{exe} did not finish"); }
        return (proc.ExitCode, outTask.Result.Replace("\r\n", "\n"), errTask.Result.Replace("\r\n", "\n"));
    }

    static string MakeExe(string source, string name)
    {
        Directory.CreateDirectory(TempDir);
        var p = CompileEcho(source, Path.Combine(TempDir, name + ".echo"));
        var exe = Path.Combine(TempDir, name + ".exe");
        ExePackage.Write(RunnerPath(), exe, p.LanguageId, p.Serialize());
        return exe;
    }

    static void RunnerUsage()
    {
        var (code, output, _) = RunProcess(RunnerPath());
        Eq(2, code, "exit code");
        Check(output.Contains("contains no program"), "usage message: " + output);
    }

    static void ExeEndToEnd()
    {
        // A non-ASCII name checks that UTF-8 standard input and output survive the round trip.
        var exe = MakeExe("print Name?\nread name\nprint Hello {name} — naïve café\nread n2\nprint eof={eof}\n", "hello");
        var pkg = ExePackage.Read(exe);
        Check(pkg != null && pkg.Value.LanguageId == "echo", "payload readable");
        var (code, output, err) = RunProcess(exe, stdin: "Zoë\n");
        Eq(0, code, "exit code (stderr: " + err + ")");
        Eq("Name?\nHello Zoë — naïve café\neof=1\n", output, "output");
        // The exe is standalone: copy it to another folder, without Runner.exe next to it
        var other = Path.Combine(TempDir, "elsewhere");
        Directory.CreateDirectory(other);
        var copy = Path.Combine(other, "copy.exe");
        File.Copy(exe, copy, true);
        var (code2, out2, _) = RunProcess(copy, stdin: "Ada\n");
        Eq(0, code2, "copy exit code");
        Check(out2.Contains("Hello Ada"), "copy output");
    }

    static void ExeRuntimeError()
    {
        var exe = MakeExe("print start\ncall p\nproc p\n  error division by zero\nend\n", "error");
        var (code, output, err) = RunProcess(exe);
        Eq(1, code, "exit code");
        Eq("start\n", output, "stdout");
        Check(err.Contains("runtime error: division by zero"), "stderr: " + err);
        Check(err.Contains("(4,3)"), "position: " + err);
        Check(err.Contains("at p in"), "stack: " + err);
    }

    static void ExeExitCode()
    {
        var exe = MakeExe("print x\nexit 7\n", "exit7");
        Eq(7, RunProcess(exe).Code, "exit code");
    }

    // ------------------------------------------------------------------ CLI

    static string? FindTool(string project, string exe)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            foreach (var cfg in new[] { "Debug", "Release" })
            {
                var p = Path.Combine(dir.FullName, "src", project, "bin", cfg, "net8.0", exe);
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    static void CliBasics()
    {
        foreach (var (proj, exe, ext) in new[] { ("SimC", "simc.exe", ".sim"), ("A68C", "a68c.exe", ".a68") })
        {
            var tool = FindTool(proj, exe);
            Check(tool != null, exe + " not found");
            var (c1, o1, _) = RunProcess(tool!, "--help");
            Eq(0, c1, exe + " --help");
            Check(o1.Contains("Usage"), "usage text");
            var (c2, _, e2) = RunProcess(tool!, "missing" + ext);
            Eq(2, c2, exe + " file not found");
            Check(e2.Contains("not found"), "'not found' message");
            var (c3, _, _) = RunProcess(tool!, "--bogus");
            Eq(2, c3, exe + " unknown option");
            // Deliberately wrong program: the compiler must report at least one error in Diagnostic format
            Directory.CreateDirectory(TempDir);
            var bad = Path.Combine(TempDir, "wrong" + ext);
            File.WriteAllText(bad, "§§§ this is not a program ;;; ((( \n");
            var (c4, o4, _) = RunProcess(tool!, $"\"{bad}\"");
            Eq(1, c4, exe + " wrong program");
            Check(o4.Contains("): error: "), "diagnostic in the expected format: " + o4);
        }
    }

    // ------------------------------------------------------------------ editor analysis

    static List<(TokenKind Kind, string Text)> Toks(LanguageDefinition def, string src) =>
        def.Tokenize(src).Select(t => (t.Kind, src.Substring(t.Start, t.Length))).ToList();

    static void SimulaLexer()
    {
        var d = LanguageDefinitions.Simula;
        var t = Toks(d, "Begin Integer i; comment this is ignored; i := 3; ! another comment; OutText(\"a\"\"b\"); c := 'x'; end main block");
        Check(t.Contains((TokenKind.Keyword, "Begin")), "Begin keyword");
        Check(t.Contains((TokenKind.Type, "Integer")), "Integer type");
        Check(t.Contains((TokenKind.Comment, "comment this is ignored;")), "comment ... ;");
        Check(t.Contains((TokenKind.Comment, "! another comment;")), "! ... ;");
        Check(t.Contains((TokenKind.String, "\"a\"\"b\"")), "string with doubled quote");
        Check(t.Contains((TokenKind.Char, "'x'")), "character");
        Check(t.Contains((TokenKind.Builtin, "OutText")), "standard OutText");
        Check(t.Contains((TokenKind.Comment, "main block")), "comment after end");
        var t2 = Toks(d, "if a then begin x := 1 end remark else y := 2.5&-3;");
        Check(t2.Contains((TokenKind.Comment, "remark")), "comment after end up to else");
        Check(t2.Contains((TokenKind.Keyword, "else")), "else stays a keyword");
        Check(t2.Contains((TokenKind.Number, "2.5&-3")), "real with & exponent");
        Check(t2.Contains((TokenKind.Operator, ":=")), "assignment");
        var t3 = Toks(d, "%include \"lib.sim\"\nx := 16RFF + 1_000;");
        Check(t3[0] == (TokenKind.Pragma, "%include \"lib.sim\""), "%include directive");
        Check(t3.Contains((TokenKind.Number, "16RFF")) && t3.Contains((TokenKind.Number, "1_000")), "integers with radix and _");
    }

    static void SimulaStructure()
    {
        var d = LanguageDefinitions.Simula;
        var src = string.Join("\n",
            "begin",
            "   integer procedure square(n); integer n;",
            "   begin",
            "      square := n * n",
            "   end square;",
            "   Process class Car(v); real v;",
            "   virtual: procedure drive;",
            "   begin",
            "      procedure halt;",
            "      begin",
            "         OutText(\"stop\")",
            "      end;",
            "      inner",
            "   end;",
            "   OutInt(square(3), 4)",
            "end");
        var m = d.Analyze(src);
        var names = m.Outline.Select(o => o.Name).ToList();
        Eq("square,Car,halt", string.Join(",", names), "outline");
        Eq(OutlineKind.Class, m.Outline[1].Kind, "class");
        Eq("Process class", m.Outline[1].Detail, "class prefix");
        Eq(1, m.Outline[0].Depth, "square depth");
        Eq(2, m.Outline[2].Depth, "halt depth");
        Eq(4, m.Folds.Count, "number of folds");
        // begin (line 1) matches the last end
        int beginIdx = 0;
        Check(m.PairOf.TryGetValue(beginIdx, out var endIdx) && src.Substring(m.Tokens[endIdx].Start, 3) == "end" && m.Tokens[endIdx].Start == src.LastIndexOf("end"), "begin/end matching");
        Check(m.Identifiers.Contains("square") && m.Identifiers.Contains("Car"), "identifiers");
    }

    static void AlgolLexer()
    {
        var d = LanguageDefinitions.Algol68;
        var t = Toks(d, "BEGIN INT n := 3; CO a comment CO REAL x = 1.5e3; # another # 'IF' n > 2 'THEN' print((\"yes\", newline)) 'FI'; COMMENT long COMMENT MODE NODE = STRUCT(INT v) END");
        Check(t.Contains((TokenKind.Keyword, "BEGIN")), "BEGIN");
        Check(t.Contains((TokenKind.Type, "INT")), "INT type");
        Check(t.Contains((TokenKind.Comment, "CO a comment CO")), "CO ... CO");
        Check(t.Contains((TokenKind.Comment, "# another #")), "# ... #");
        Check(t.Contains((TokenKind.Comment, "COMMENT long COMMENT")), "COMMENT ... COMMENT");
        Check(t.Contains((TokenKind.Keyword, "'IF'")), "QUOTE stropping 'IF'");
        Check(t.Contains((TokenKind.Number, "1.5e3")), "real");
        Check(t.Contains((TokenKind.String, "\"yes\"")), "string");
        Check(t.Contains((TokenKind.Builtin, "print")), "standard print");
        Check(t.Contains((TokenKind.BoldWord, "NODE")), "mode indicant");
        var q = Toks(d, "'begin' 'co' ignored 'co' 'end'");
        Check(q.Contains((TokenKind.Comment, "'co' ignored 'co'")), "'co' ... 'co'");
        Check(d.Tokenize("'begin'")[0].Norm == "BEGIN", "QUOTE normalization");
    }

    static void AlgolStructure()
    {
        var d = LanguageDefinitions.Algol68;
        var src = string.Join("\n",
            "BEGIN",
            "   MODE POINT = STRUCT(REAL x, y);",
            "   PROC square = (INT n) INT:",
            "   BEGIN",
            "      n * n",
            "   END;",
            "   OP NORM = (POINT p) REAL: sqrt(x OF p ** 2 + y OF p ** 2);",
            "   PROC print line = VOID: print((\"--\", newline)),",
            "        other = VOID: SKIP;",
            "   FOR i TO 3 DO",
            "      IF i > 1",
            "      THEN print(i)",
            "      FI",
            "   OD",
            "END");
        var m = d.Analyze(src);
        Eq("POINT,square,NORM,print line,other", string.Join(",", m.Outline.Select(o => o.Name)), "outline");
        Eq(OutlineKind.Mode, m.Outline[0].Kind, "MODE");
        Eq(OutlineKind.Operator, m.Outline[2].Kind, "OP");
        Check(m.Folds.Count >= 4, "BEGIN/END, DO/OD, IF/FI folds: " + m.Folds.Count);
        Check(m.Folds.Any(f => f.Title == "IF … FI"), "IF/FI fold");
        Check(m.Folds.Any(f => f.Title == "DO … OD"), "DO/OD fold");
        int ifIdx = m.Tokens.ToList().FindIndex(t => t.Norm == "IF");
        Check(m.PairOf.TryGetValue(ifIdx, out var fi) && m.Tokens[fi].Norm == "FI", "IF/FI matching");
    }

    static void Indentation()
    {
        var s = LanguageDefinitions.Simula;
        Eq("      ", s.ComputeIndent("   begin", ""), "after begin");
        Eq("   ", s.ComputeIndent("   x := 1;", ""), "ordinary line");
        Eq("   ", s.ComputeIndent("      x := 1", "end"), "end unindented");
        Eq("   ", s.ComputeIndent("   end;", ""), "after end");
        var a = LanguageDefinitions.Algol68;
        Eq("   ", a.ComputeIndent("IF x > 0", ""), "after IF");
        Eq("", a.ComputeIndent("IF x > 0", "THEN"), "THEN aligned with IF");
        Eq("   ", a.ComputeIndent("THEN print(x)", ""), "after THEN");
        Eq("", a.ComputeIndent("THEN print(x)", "ELSE"), "ELSE aligned");
        Eq("   ", a.ComputeIndent("FOR i TO 10 DO", ""), "after DO");
        Eq("", a.ComputeIndent("   x +:= 1", "OD"), "OD unindented");
    }

    static void CommentToggle()
    {
        var s = LanguageDefinitions.Simula;
        foreach (var line in new[] { "   x := 1;", "   if a then", "OutImage", "   class Stack(n); integer n;", "a := 1; OutText(\"x;y\"); b := 2" })
        {
            var c = s.CommentLine(line);
            Check(s.IsCommentedLine(c), "commented: " + c);
            Eq(line, s.UncommentLine(c), "Simula round trip");
            // The comment must not spill over onto the next line
            var toks = s.Tokenize(c + "\nx := 2;");
            Check(toks.Any(t => t.Kind == TokenKind.Identifier && t.Start > c.Length), "next line not commented: " + c);
            Check(toks.Where(t => t.Start < c.Length).All(t => t.Kind == TokenKind.Comment), "line fully commented: " + c);
        }
        var a = LanguageDefinitions.Algol68;
        var l2 = "   print(x) # already #";
        var c2 = a.CommentLine(l2);
        Check(a.IsCommentedLine(c2), "Algol commented");
        Eq(1, a.Tokenize(c2).Count, "a single comment");
        Eq(l2, a.UncommentLine(c2), "Algol round trip");
    }

    // ------------------------------------------------------------------ real compilers

    /// <summary>Folders whose files are not standalone samples (included files, deliberate compile errors).</summary>
    static bool IsSkippedSample(string relPath)
    {
        var parts = relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Take(parts.Length - 1).Any(p =>
            p.Equals("include", StringComparison.OrdinalIgnoreCase) || p.Equals("errors", StringComparison.OrdinalIgnoreCase));
    }

    static bool IsRuntimeErrorSample(string relPath) =>
        relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
               .Any(p => p.Equals("runtime_errors", StringComparison.OrdinalIgnoreCase));

    static void Samples()
    {
        Console.WriteLine();
        Console.WriteLine("Samples with the real compilers:");
        var root = FindUp("samples");
        if (root == null) { Console.WriteLine("  (samples folder not found)"); return; }
        foreach (var (sub, ext) in new[] { ("simula", ".sim"), ("algol68", ".a68") })
        {
            var dir = Path.Combine(root, sub);
            if (!Directory.Exists(dir)) continue;
            var lang = LanguageRegistry.FindByExtension(ext)!;
            var files = Directory.GetFiles(dir, "*" + ext, SearchOption.AllDirectories)
                .Where(f => !IsSkippedSample(Path.GetRelativePath(dir, f)))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                var rel = Path.GetRelativePath(dir, f).Replace('\\', '/');
                bool expectRuntimeError = IsRuntimeErrorSample(rel);
                var src = new SourceFile(f, File.ReadAllText(f));
                string status;
                try
                {
                    var r = lang.Compile(src, new CompileOptions
                    {
                        IncludeResolver = (n, from) =>
                        {
                            var p = Path.Combine(Path.GetDirectoryName(from) ?? "", n);
                            return File.Exists(p) ? new SourceFile(p, File.ReadAllText(p)) : null;
                        },
                    });
                    if (!r.Success) status = "errors: " + string.Join(" | ", r.Diagnostics.Where(d => d.Severity == Severity.Error).Take(2));
                    else
                    {
                        var p2 = lang.Deserialize(r.Program!.Serialize());
                        var input = Path.ChangeExtension(f, ".in");
                        var io = new FakeIO(File.Exists(input) ? File.ReadAllLines(input).Cast<string?>().Append(null).ToArray() : new string?[] { null });
                        var res = ProgramHost.RunOnLargeStack(p2.CreateExecution(io), new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token);
                        var expected = Path.ChangeExtension(f, ".out");
                        string cmp = File.Exists(expected)
                            ? (File.ReadAllText(expected).Replace("\r\n", "\n").TrimEnd() == io.Output.Replace("\r\n", "\n").TrimEnd() ? ", output matches" : ", OUTPUT DIFFERS")
                            : "";
                        if (expectRuntimeError)
                            status = res.Error != null ? "expected runtime error: " + res.Error : $"no runtime error (code {res.ExitCode}){cmp}";
                        else
                            status = res.Error != null ? "runtime error: " + res.Error : $"ok (code {res.ExitCode}, {io.Output.Length} chars{cmp})";
                    }
                }
                catch (Exception ex) { status = "exception: " + ex.GetType().Name + " " + ex.Message; }
                Console.WriteLine($"  {sub}/{rel}: {status}");
            }
        }
    }

    static string? FindUp(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, name);
            if (Directory.Exists(p)) return p;
        }
        return null;
    }
}
