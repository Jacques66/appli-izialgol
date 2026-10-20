// Tests de bout en bout de l'outillage (sans les vrais compilateurs) :
// langage de test « echo », debogueur, exe autonome via Runner, CLI, analyse de l'editeur.
// Usage : dotnet run --project tests/IdeTests [-- --samples]
//   --samples : compile aussi samples/simula/*.sim et samples/algol68/*.a68 avec les vrais compilateurs.

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

        Test("echo : compilation correcte", EchoCompileOk);
        Test("echo : diagnostics (ligne/colonne, avertissement, bloc non fermé)", EchoDiagnostics);
        Test("echo : exécution avec entrées/sorties et fin de fichier", EchoRunIo);
        Test("echo : erreur d'exécution avec pile d'appels", EchoRuntimeError);
        Test("echo : fichiers inclus (IncludeResolver)", EchoInclude);
        Test("echo : sérialisation / désérialisation", EchoSerialize);
        Test("débogueur : point d'arrêt, pile, variables, enfants", DebugBreakpoint);
        Test("débogueur : pas à pas détaillé / principal / sortant", DebugStepping);
        Test("débogueur : arrêt à l'entrée et demande de pause", DebugEntryAndPause);
        Test("débogueur : arrêt (annulation) pendant une pause", DebugCancelWhilePaused);
        Test("débogueur : arrêt sur erreur d'exécution", DebugBreakOnError);
        Test("débogueur : points d'arrêt modifiés pendant l'exécution", DebugBreakpointsAtRuntime);
        Test("exécution : annulation d'un programme bloqué en lecture", CancelDuringRead);
        Test("Runner : sans programme → message d'usage", RunnerUsage);
        Test("exe autonome : génération + exécution + entrée standard", ExeEndToEnd);
        Test("exe autonome : erreur d'exécution sur stderr, code de sortie", ExeRuntimeError);
        Test("exe autonome : code de sortie du programme", ExeExitCode);
        Test("simc / a68c : usage, fichier introuvable, diagnostics", CliBasics);
        Test("éditeur Simula : jetons et commentaires", SimulaLexer);
        Test("éditeur Simula : repliage, appariement, structure", SimulaStructure);
        Test("éditeur Algol 68 : stropping UPPER/QUOTE, commentaires", AlgolLexer);
        Test("éditeur Algol 68 : repliage, appariement, structure", AlgolStructure);
        Test("éditeur : indentation automatique", Indentation);
        Test("éditeur : commenter / décommenter", CommentToggle);

        if (args.Contains("--samples")) Samples();

        Console.WriteLine();
        Console.WriteLine($"{_passed} réussi(s), {_failed} échoué(s) en {sw.Elapsed.TotalSeconds:F1} s");
        foreach (var f in Failures) Console.WriteLine("  ÉCHEC : " + f);
        return _failed == 0 ? 0 : 1;
    }

    static void Test(string name, Action body)
    {
        try
        {
            body();
            _passed++;
            Console.WriteLine("  ok    " + name);
        }
        catch (Exception ex)
        {
            _failed++;
            Failures.Add(name + " : " + ex.Message);
            Console.WriteLine("  ÉCHEC " + name);
            Console.WriteLine("        " + ex.Message.Replace("\n", "\n        "));
        }
    }

    static void Check(bool cond, string message)
    {
        if (!cond) throw new Exception(message);
    }

    static void Eq<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what} : attendu « {expected} », obtenu « {actual} »");
    }

    // ------------------------------------------------------------------ outils

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
        Check(r.Success, "compilation échouée : " + string.Join("; ", r.Diagnostics));
        return r.Program!;
    }

    const string DebugProgram =
        "# programme de mise au point\n" +   // 1
        "set x = 1\n" +                       // 2
        "list l = a, b, c\n" +                // 3
        "call p\n" +                          // 4
        "print fin {x}\n" +                   // 5
        "proc p\n" +                          // 6
        "  set local = 42\n" +                // 7
        "  add x 10\n" +                      // 8
        "  print dans p\n" +                  // 9
        "end\n";                              // 10

    /// <summary>Lance l'execution sur un thread ; les pauses arrivent dans la file.</summary>
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
            if (!Pauses.TryTake(out var p, TimeSpan.FromSeconds(10))) throw new Exception("pause attendue non reçue");
            return p;
        }

        public ExecutionResult Wait()
        {
            if (!_thread!.Join(TimeSpan.FromSeconds(10))) throw new Exception("le programme ne s'est pas terminé");
            return Result!;
        }
    }

    // ------------------------------------------------------------------ echo

    static void EchoCompileOk()
    {
        var r = Echo.Compile(new SourceFile("a.echo", "print bonjour\nset x = 3\n"), new CompileOptions());
        Check(r.Success, "succès attendu");
        Eq(0, r.Diagnostics.Count, "nombre de diagnostics");
        Eq("echo", r.Program!.LanguageId, "LanguageId");
    }

    static void EchoDiagnostics()
    {
        var r = Echo.Compile(new SourceFile("b.echo", "print ok\n   bidule 3\nprint\nloop 2\n  print x\n"), new CompileOptions());
        Check(!r.Success, "échec attendu");
        Check(r.Program == null, "pas de programme attendu");
        var err = r.Diagnostics.First(d => d.Message.Contains("bidule"));
        Eq(2, err.Line, "ligne"); Eq(4, err.Column, "colonne"); Eq(Severity.Error, err.Severity, "gravité");
        Eq("b.echo(2,4): erreur: instruction inconnue « bidule »", err.ToString(), "format");
        Check(r.Diagnostics.Any(d => d.Severity == Severity.Warning && d.Line == 3), "avertissement ligne 3");
        Check(r.Diagnostics.Any(d => d.Line == 4 && d.Message.Contains("end")), "bloc non terminé ligne 4");
    }

    static void EchoRunIo()
    {
        var p = CompileEcho("print Quel est ton nom ?\nread nom\nprint Bonjour {nom} !\nread autre\nprint eof={eof}\n");
        var io = new FakeIO("Ada", null);
        var res = p.CreateExecution(io).Run(CancellationToken.None);
        Eq(0, res.ExitCode, "code");
        Check(res.Error == null, "pas d'erreur");
        Eq("Quel est ton nom ?\nBonjour Ada !\neof=1\n", io.Output, "sortie");
    }

    static void EchoRuntimeError()
    {
        var p = CompileEcho("print avant\ncall p\nproc p\n  error boum {x}\nend\n");
        var res = p.CreateExecution(new FakeIO()).Run(CancellationToken.None);
        Check(res.Error != null, "erreur attendue");
        Eq(4, res.Error!.Line, "ligne de l'erreur");
        Eq("boum ", res.Error.Message, "message");
        Eq(2, res.Error.Stack.Count, "profondeur de pile");
        Eq("p", res.Error.Stack[0].Name, "frame 0");
        Eq(2, res.Error.Stack[1].Line, "ligne de l'appel");
        Check(ProgramHost.FormatError(res.Error).Contains("à p dans"), "format de la pile");
    }

    static void EchoInclude()
    {
        var resolver = new Func<string, string, SourceFile?>((name, from) =>
            name == "lib.echo" ? new SourceFile("C:\\test\\lib.echo", "proc salut\n  print salut\nend\n") : null);
        var p = CompileEcho("include lib.echo\ncall salut\n", resolver: resolver);
        var io = new FakeIO();
        p.CreateExecution(io).Run(CancellationToken.None);
        Eq("salut\n", io.Output, "sortie");
        var bad = Echo.Compile(new SourceFile("x.echo", "include absent.echo\n"), new CompileOptions { IncludeResolver = resolver });
        Check(!bad.Success && bad.Diagnostics[0].Message.Contains("introuvable"), "inclusion introuvable");
    }

    static void EchoSerialize()
    {
        var resolver = new Func<string, string, SourceFile?>((name, from) =>
            new SourceFile("C:\\test\\lib.echo", "proc salut\n  print salut {x}\nend\n"));
        var p = CompileEcho("set x = 7\ninclude lib.echo\ncall salut\n", resolver: resolver);
        var p2 = Echo.Deserialize(p.Serialize());
        var io = new FakeIO();
        p2.CreateExecution(io).Run(CancellationToken.None);
        Eq("salut 7\n", io.Output, "sortie après désérialisation");
    }

    // ------------------------------------------------------------------ debogueur

    static void DebugBreakpoint()
    {
        var s = new DebugSession(CompileEcho(DebugProgram));
        s.Dbg.SetBreakpoints("C:\\test\\prog.echo", new[] { 8 });
        s.Start();
        var p = s.Next();
        Eq(PauseReason.Breakpoint, p.Reason, "raison");
        Eq(8, p.Line, "ligne");
        var stack = s.Dbg.GetStack();
        Eq(2, stack.Count, "profondeur");
        Eq("p", stack[0].Name, "frame 0");
        Eq(8, stack[0].Line, "ligne frame 0");
        Eq(4, stack[1].Line, "ligne frame 1");
        var locals = s.Dbg.GetVariables(0);
        Check(locals.Any(v => v.Name == "local" && v.Value == "42"), "variable locale");
        var globals = s.Dbg.GetVariables(1);
        var l = globals.First(v => v.Name == "l");
        Check(l.HasChildren, "liste avec enfants");
        var kids = s.Dbg.GetChildren(l);
        Eq(3, kids.Count, "enfants"); Eq("\"b\"", kids[1].Value, "enfant 1");
        s.Dbg.Continue();
        var res = s.Wait();
        Eq(0, res.ExitCode, "code");
        Eq("dans p\nfin 11\n", s.IO.Output, "sortie");
    }

    static void DebugStepping()
    {
        var s = new DebugSession(CompileEcho(DebugProgram));
        s.Dbg.SetBreakpoints("C:\\test\\prog.echo", new[] { 4 });
        s.Start();
        Eq(4, s.Next().Line, "arrêt sur call");
        s.Dbg.StepInto();
        var p = s.Next();
        Eq(PauseReason.Step, p.Reason, "raison step"); Eq(7, p.Line, "pas détaillé → ligne 7");
        s.Dbg.StepOver();
        Eq(8, s.Next().Line, "pas principal → ligne 8");
        s.Dbg.StepOut();
        Eq(5, s.Next().Line, "pas sortant → ligne 5");
        s.Dbg.StepOver();
        s.Wait();
        // pas principal au-dessus d'un appel
        var s2 = new DebugSession(CompileEcho(DebugProgram));
        s2.Dbg.SetBreakpoints("C:\\test\\prog.echo", new[] { 4 });
        s2.Start();
        Eq(4, s2.Next().Line, "arrêt sur call (2)");
        s2.Dbg.StepOver();
        Eq(5, s2.Next().Line, "pas principal au-dessus de l'appel");
        s2.Dbg.Continue();
        s2.Wait();
    }

    static void DebugEntryAndPause()
    {
        var s = new DebugSession(CompileEcho("set n = 0\nloop 100000\n  add n 1\n  sleep 1\nend\n"));
        s.Dbg.StopOnEntry = true;
        s.Start();
        var p = s.Next();
        Eq(PauseReason.Entry, p.Reason, "raison entrée"); Eq(1, p.Line, "ligne entrée");
        s.Dbg.Continue();
        Thread.Sleep(100);
        s.Dbg.RequestPause();
        p = s.Next();
        Eq(PauseReason.PauseRequest, p.Reason, "raison pause");
        Check(p.Line is 3 or 4, "ligne dans la boucle");
        s.Cts.Cancel();
        var r = s.Wait();
        Check(r.Cancelled, "annulé");
    }

    static void DebugCancelWhilePaused()
    {
        var s = new DebugSession(CompileEcho(DebugProgram));
        s.Dbg.SetBreakpoints("C:\\test\\prog.echo", new[] { 2 });
        s.Start();
        s.Next();
        s.Cts.Cancel();
        var r = s.Wait();
        Check(r.Cancelled, "Cancelled attendu");
    }

    static void DebugBreakOnError()
    {
        var s = new DebugSession(CompileEcho("set v = 5\ncall p\nproc p\n  set w = 1\n  error mauvais {v}\nend\n"));
        s.Dbg.BreakOnError = true;
        s.Start();
        var p = s.Next();
        Eq(PauseReason.Exception, p.Reason, "raison");
        Eq(5, p.Line, "ligne");
        Eq("mauvais 5", p.Message, "message");
        Eq(2, s.Dbg.GetStack().Count, "pile conservée");
        Check(s.Dbg.GetVariables(0).Any(v => v.Name == "w"), "variables locales visibles");
        s.Dbg.Continue();
        var r = s.Wait();
        Check(r.Error != null && r.Error.Line == 5, "erreur rendue après la pause");
    }

    static void DebugBreakpointsAtRuntime()
    {
        var s = new DebugSession(CompileEcho("print a\nread x\nprint b\nprint c\n"));
        s.Dbg.SetBreakpoints("C:\\test\\prog.echo", new[] { 1 });
        s.Start();
        Eq(1, s.Next().Line, "premier arrêt");
        s.Dbg.SetBreakpoints("C:\\test\\prog.echo", new[] { 4 });
        s.Dbg.Continue();
        s.IO.Add("valeur");
        Eq(4, s.Next().Line, "nouvel arrêt");
        s.Dbg.Continue();
        s.Wait();
        Eq("a\nb\nc\n", s.IO.Output, "sortie");
    }

    static void CancelDuringRead()
    {
        // Le programme attend une entree : l'IDE annule et l'IO rend null (fin de fichier).
        var io = new CancellableIO();
        var exec = CompileEcho("read x\nread y\nprint fini\n").CreateExecution(io);
        var cts = new CancellationTokenSource();
        io.Token = cts.Token;
        ExecutionResult? res = null;
        var t = new Thread(() => res = exec.Run(cts.Token));
        t.Start();
        Thread.Sleep(100);
        cts.Cancel();
        Check(t.Join(5000), "terminé après annulation");
        Check(res!.Cancelled, "Cancelled");
    }

    sealed class CancellableIO : IProgramIO
    {
        public CancellationToken Token;
        public void Write(string text) { }
        public string? ReadLine() { Token.WaitHandle.WaitOne(); return null; }
        public string WorkingDirectory => ".";
    }

    // ------------------------------------------------------------------ exe autonome

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
        if (!proc.WaitForExit(timeoutMs)) { proc.Kill(true); throw new Exception($"{exe} ne s'est pas terminé"); }
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
        Eq(2, code, "code de sortie");
        Check(output.Contains("ne contient aucun programme"), "message d'usage : " + output);
    }

    static void ExeEndToEnd()
    {
        var exe = MakeExe("print Nom ?\nread nom\nprint Bonjour {nom} — été\nread n2\nprint eof={eof}\n", "bonjour");
        var pkg = ExePackage.Read(exe);
        Check(pkg != null && pkg.Value.LanguageId == "echo", "payload lisible");
        var (code, output, err) = RunProcess(exe, stdin: "Grâce\n");
        Eq(0, code, "code de sortie (stderr : " + err + ")");
        Eq("Nom ?\nBonjour Grâce — été\neof=1\n", output, "sortie");
        // L'exe est autonome : copie dans un autre dossier, sans Runner.exe a cote
        var other = Path.Combine(TempDir, "ailleurs");
        Directory.CreateDirectory(other);
        var copy = Path.Combine(other, "copie.exe");
        File.Copy(exe, copy, true);
        var (code2, out2, _) = RunProcess(copy, stdin: "Ada\n");
        Eq(0, code2, "code copie");
        Check(out2.Contains("Bonjour Ada"), "sortie copie");
    }

    static void ExeRuntimeError()
    {
        var exe = MakeExe("print début\ncall p\nproc p\n  error division par zéro\nend\n", "erreur");
        var (code, output, err) = RunProcess(exe);
        Eq(1, code, "code de sortie");
        Eq("début\n", output, "stdout");
        Check(err.Contains("erreur d'execution: division par zéro"), "stderr : " + err);
        Check(err.Contains("(4,3)"), "position : " + err);
        Check(err.Contains("à p dans"), "pile : " + err);
    }

    static void ExeExitCode()
    {
        var exe = MakeExe("print x\nexit 7\n", "sortie7");
        Eq(7, RunProcess(exe).Code, "code de sortie");
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
            Check(tool != null, exe + " introuvable");
            var (c1, o1, _) = RunProcess(tool!, "--help");
            Eq(0, c1, exe + " --help");
            Check(o1.Contains("Usage"), "texte d'usage");
            var (c2, _, e2) = RunProcess(tool!, "absent" + ext);
            Eq(2, c2, exe + " fichier introuvable");
            Check(e2.Contains("introuvable"), "message introuvable");
            var (c3, _, _) = RunProcess(tool!, "--bidon");
            Eq(2, c3, exe + " option inconnue");
            // Programme volontairement faux : le compilateur doit rendre au moins une erreur au format Diagnostic
            Directory.CreateDirectory(TempDir);
            var bad = Path.Combine(TempDir, "faux" + ext);
            File.WriteAllText(bad, "§§§ ceci n'est pas un programme ;;; ((( \n");
            var (c4, o4, _) = RunProcess(tool!, $"\"{bad}\"");
            Eq(1, c4, exe + " programme faux");
            Check(o4.Contains("): erreur: "), "diagnostic au format attendu : " + o4);
        }
    }

    // ------------------------------------------------------------------ analyse de l'editeur

    static List<(TokenKind Kind, string Text)> Toks(LanguageDefinition def, string src) =>
        def.Tokenize(src).Select(t => (t.Kind, src.Substring(t.Start, t.Length))).ToList();

    static void SimulaLexer()
    {
        var d = LanguageDefinitions.Simula;
        var t = Toks(d, "Begin Integer i; comment ceci est ignoré; i := 3; ! autre commentaire; OutText(\"a\"\"b\"); c := 'x'; end bloc principal");
        Check(t.Contains((TokenKind.Keyword, "Begin")), "Begin mot-clé");
        Check(t.Contains((TokenKind.Type, "Integer")), "Integer type");
        Check(t.Contains((TokenKind.Comment, "comment ceci est ignoré;")), "comment … ;");
        Check(t.Contains((TokenKind.Comment, "! autre commentaire;")), "! … ;");
        Check(t.Contains((TokenKind.String, "\"a\"\"b\"")), "chaîne avec guillemet doublé");
        Check(t.Contains((TokenKind.Char, "'x'")), "caractère");
        Check(t.Contains((TokenKind.Builtin, "OutText")), "OutText standard");
        Check(t.Contains((TokenKind.Comment, "bloc principal")), "commentaire après end");
        var t2 = Toks(d, "if a then begin x := 1 end commentaire else y := 2.5&-3;");
        Check(t2.Contains((TokenKind.Comment, "commentaire")), "commentaire après end jusqu'à else");
        Check(t2.Contains((TokenKind.Keyword, "else")), "else reste un mot-clé");
        Check(t2.Contains((TokenKind.Number, "2.5&-3")), "réel avec exposant &");
        Check(t2.Contains((TokenKind.Operator, ":=")), "affectation");
    }

    static void SimulaStructure()
    {
        var d = LanguageDefinitions.Simula;
        var src = string.Join("\n",
            "begin",
            "   integer procedure carre(n); integer n;",
            "   begin",
            "      carre := n * n",
            "   end carre;",
            "   Process class Voiture(v); real v;",
            "   virtual: procedure rouler;",
            "   begin",
            "      procedure arreter;",
            "      begin",
            "         OutText(\"stop\")",
            "      end;",
            "      inner",
            "   end;",
            "   OutInt(carre(3), 4)",
            "end");
        var m = d.Analyze(src);
        var names = m.Outline.Select(o => o.Name).ToList();
        Eq("carre,Voiture,arreter", string.Join(",", names), "outline");
        Eq(OutlineKind.Class, m.Outline[1].Kind, "classe");
        Eq("Process class", m.Outline[1].Detail, "préfixe de classe");
        Eq(1, m.Outline[0].Depth, "profondeur carre");
        Eq(2, m.Outline[2].Depth, "profondeur arreter");
        Eq(4, m.Folds.Count, "nombre de replis");
        // begin (ligne 1) apparie avec le dernier end
        int beginIdx = 0;
        Check(m.PairOf.TryGetValue(beginIdx, out var endIdx) && src.Substring(m.Tokens[endIdx].Start, 3) == "end" && m.Tokens[endIdx].Start == src.LastIndexOf("end"), "appariement begin/end");
        Check(m.Identifiers.Contains("carre") && m.Identifiers.Contains("Voiture"), "identifiants");
    }

    static void AlgolLexer()
    {
        var d = LanguageDefinitions.Algol68;
        var t = Toks(d, "BEGIN INT n := 3; CO un commentaire CO REAL x = 1.5e3; # autre # 'IF' n > 2 'THEN' print((\"oui\", newline)) 'FI'; COMMENT long COMMENT MODE NODE = STRUCT(INT v) END");
        Check(t.Contains((TokenKind.Keyword, "BEGIN")), "BEGIN");
        Check(t.Contains((TokenKind.Type, "INT")), "INT type");
        Check(t.Contains((TokenKind.Comment, "CO un commentaire CO")), "CO … CO");
        Check(t.Contains((TokenKind.Comment, "# autre #")), "# … #");
        Check(t.Contains((TokenKind.Comment, "COMMENT long COMMENT")), "COMMENT … COMMENT");
        Check(t.Contains((TokenKind.Keyword, "'IF'")), "QUOTE stropping 'IF'");
        Check(t.Contains((TokenKind.Number, "1.5e3")), "réel");
        Check(t.Contains((TokenKind.String, "\"oui\"")), "chaîne");
        Check(t.Contains((TokenKind.Builtin, "print")), "print standard");
        Check(t.Contains((TokenKind.BoldWord, "NODE")), "indicant de mode");
        var q = Toks(d, "'begin' 'co' ignoré 'co' 'end'");
        Check(q.Contains((TokenKind.Comment, "'co' ignoré 'co'")), "'co' … 'co'");
        Check(d.Tokenize("'begin'")[0].Norm == "BEGIN", "normalisation QUOTE");
    }

    static void AlgolStructure()
    {
        var d = LanguageDefinitions.Algol68;
        var src = string.Join("\n",
            "BEGIN",
            "   MODE POINT = STRUCT(REAL x, y);",
            "   PROC carre = (INT n) INT:",
            "   BEGIN",
            "      n * n",
            "   END;",
            "   OP NORME = (POINT p) REAL: sqrt(x OF p ** 2 + y OF p ** 2);",
            "   PROC print line = VOID: print((\"--\", newline)),",
            "        other = VOID: SKIP;",
            "   FOR i TO 3 DO",
            "      IF i > 1",
            "      THEN print(i)",
            "      FI",
            "   OD",
            "END");
        var m = d.Analyze(src);
        Eq("POINT,carre,NORME,print line,other", string.Join(",", m.Outline.Select(o => o.Name)), "outline");
        Eq(OutlineKind.Mode, m.Outline[0].Kind, "MODE");
        Eq(OutlineKind.Operator, m.Outline[2].Kind, "OP");
        Check(m.Folds.Count >= 4, "replis BEGIN/END, DO/OD, IF/FI : " + m.Folds.Count);
        Check(m.Folds.Any(f => f.Title == "IF … FI"), "repli IF/FI");
        Check(m.Folds.Any(f => f.Title == "DO … OD"), "repli DO/OD");
        int ifIdx = m.Tokens.ToList().FindIndex(t => t.Norm == "IF");
        Check(m.PairOf.TryGetValue(ifIdx, out var fi) && m.Tokens[fi].Norm == "FI", "appariement IF/FI");
    }

    static void Indentation()
    {
        var s = LanguageDefinitions.Simula;
        Eq("      ", s.ComputeIndent("   begin", ""), "après begin");
        Eq("   ", s.ComputeIndent("   x := 1;", ""), "ligne ordinaire");
        Eq("   ", s.ComputeIndent("      x := 1", "end"), "end désindenté");
        Eq("   ", s.ComputeIndent("   end;", ""), "après end");
        var a = LanguageDefinitions.Algol68;
        Eq("   ", a.ComputeIndent("IF x > 0", ""), "après IF");
        Eq("", a.ComputeIndent("IF x > 0", "THEN"), "THEN aligné sur IF");
        Eq("   ", a.ComputeIndent("THEN print(x)", ""), "après THEN");
        Eq("", a.ComputeIndent("THEN print(x)", "ELSE"), "ELSE aligné");
        Eq("   ", a.ComputeIndent("FOR i TO 10 DO", ""), "après DO");
        Eq("", a.ComputeIndent("   x +:= 1", "OD"), "OD désindenté");
    }

    static void CommentToggle()
    {
        var s = LanguageDefinitions.Simula;
        foreach (var line in new[] { "   x := 1;", "   if a then", "OutImage", "   class Pile(n); integer n;", "a := 1; OutText(\"x;y\"); b := 2" })
        {
            var c = s.CommentLine(line);
            Check(s.IsCommentedLine(c), "commenté : " + c);
            Eq(line, s.UncommentLine(c), "aller-retour Simula");
            // Le commentaire ne doit pas deborder sur la ligne suivante
            var toks = s.Tokenize(c + "\nx := 2;");
            Check(toks.Any(t => t.Kind == TokenKind.Identifier && t.Start > c.Length), "ligne suivante non commentée : " + c);
            Check(toks.Where(t => t.Start < c.Length).All(t => t.Kind == TokenKind.Comment), "ligne entièrement commentée : " + c);
        }
        var a = LanguageDefinitions.Algol68;
        var l2 = "   print(x) # déjà #";
        var c2 = a.CommentLine(l2);
        Check(a.IsCommentedLine(c2), "commenté Algol");
        Eq(1, a.Tokenize(c2).Count, "un seul commentaire");
        Eq(l2, a.UncommentLine(c2), "aller-retour Algol");
    }

    // ------------------------------------------------------------------ vrais compilateurs

    static void Samples()
    {
        Console.WriteLine();
        Console.WriteLine("Exemples avec les vrais compilateurs :");
        var root = FindUp("samples");
        if (root == null) { Console.WriteLine("  (dossier samples introuvable)"); return; }
        foreach (var (sub, ext) in new[] { ("simula", ".sim"), ("algol68", ".a68") })
        {
            var dir = Path.Combine(root, sub);
            if (!Directory.Exists(dir)) continue;
            var lang = LanguageRegistry.FindByExtension(ext)!;
            foreach (var f in Directory.GetFiles(dir, "*" + ext).OrderBy(x => x))
            {
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
                    if (!r.Success) status = "erreurs : " + string.Join(" | ", r.Diagnostics.Where(d => d.Severity == Severity.Error).Take(2));
                    else
                    {
                        var p2 = lang.Deserialize(r.Program!.Serialize());
                        var input = Path.ChangeExtension(f, ".in");
                        var io = new FakeIO(File.Exists(input) ? File.ReadAllLines(input).Cast<string?>().Append(null).ToArray() : new string?[] { null });
                        var res = ProgramHost.RunOnLargeStack(p2.CreateExecution(io), new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token);
                        var expected = Path.ChangeExtension(f, ".out");
                        string cmp = File.Exists(expected)
                            ? (File.ReadAllText(expected).Replace("\r\n", "\n").TrimEnd() == io.Output.Replace("\r\n", "\n").TrimEnd() ? ", sortie conforme" : ", SORTIE DIFFÉRENTE")
                            : "";
                        status = res.Error != null ? "erreur d'exécution : " + res.Error : $"ok (code {res.ExitCode}, {io.Output.Length} car.{cmp})";
                    }
                }
                catch (Exception ex) { status = "exception : " + ex.GetType().Name + " " + ex.Message; }
                Console.WriteLine($"  {sub}/{Path.GetFileName(f)} : {status}");
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
