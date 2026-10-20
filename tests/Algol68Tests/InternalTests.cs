using System.Diagnostics;
using Studio.Common;
using Studio.Algol68;

namespace Algol68Tests;

/// <summary>Tests internes : programmes en ligne, erreurs, mise au point, serialisation, annulation, performances.</summary>
static class InternalTests
{
    static readonly Algol68Language lang = new();

    static CompilationResult Compile(string src, string name = "interne.a68") =>
        lang.Compile(new SourceFile(name, src), new CompileOptions());

    static void Expect(string name, string src, string expected, string? input = null)
    {
        var cr = Compile(src, name + ".a68");
        if (!cr.Success) { Program.Fail("interne/" + name, string.Join("\n", cr.Diagnostics)); return; }
        var (o, res, _) = Program.Execute(cr.Program!, input);
        o = o.Replace("\r\n", "\n");
        if (res.Error != null) { Program.Fail("interne/" + name, res.Error.ToString()); return; }
        if (o != expected) { Program.Fail("interne/" + name, Program.Diff(expected, o)); return; }
        Program.Pass("interne/" + name);
    }

    static void ExpectCompileError(string name, string src, string fragment, int line = 0)
    {
        var cr = Compile(src, name + ".a68");
        if (cr.Success) { Program.Fail("interne/" + name, "erreur de compilation attendue"); return; }
        if (!cr.Diagnostics.Any(d => d.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase) && (line == 0 || d.Line == line)))
        { Program.Fail("interne/" + name, $"attendu « {fragment} » (ligne {line}), obtenu :\n" + string.Join("\n", cr.Diagnostics)); return; }
        Program.Pass("interne/" + name);
    }

    static void ExpectRuntimeError(string name, string src, string fragment, int line = 0)
    {
        var cr = Compile(src, name + ".a68");
        if (!cr.Success) { Program.Fail("interne/" + name, string.Join("\n", cr.Diagnostics)); return; }
        var (_, res, _) = Program.Execute(cr.Program!, null);
        if (res.Error == null) { Program.Fail("interne/" + name, "erreur d'execution attendue"); return; }
        if (!res.Error.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase) || (line != 0 && res.Error.Line != line))
        { Program.Fail("interne/" + name, $"attendu « {fragment} » ligne {line}, obtenu {res.Error}"); return; }
        Program.Pass("interne/" + name);
    }

    /// <summary>Execute sous le debogueur ; onPause choisit l'action (0 continue, 1 into, 2 over, 3 out).</summary>
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
        if (ok) Program.Pass("interne/" + name); else Program.Fail("interne/" + name, detail);
    }

    static string Lines(List<(PauseReason reason, int line)> p) => string.Join(",", p.Select(x => x.line));

    static void DebuggerTests()
    {
        const string prog = "INT s := 0;\nPROC p = (INT n) VOID:\n   s +:= n;\np(1); p(2);\nprint(s)\n";
        // point d'arret sur le corps d'une PROC reduit a une unite (sur sa propre ligne)
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
            Check("debogueur-point-arret-corps-unite", res.Error == null && Lines(p) == "3,3" && p.All(x => x.reason == PauseReason.Breakpoint),
                $"pauses : {Lines(p)} erreur : {res.Error}");
            Check("debogueur-variables", vars == "n=1|s=0;p=routine p", "variables : " + vars);
            Check("debogueur-pile", stack == "p@3;programme principal@4", "pile : " + stack);
        }
        // pas a pas detaille (into)
        {
            var (p, res, _) = RunDebug(prog, d => d.StopOnEntry = true, (d, pi) => 1);
            Check("debogueur-pas-a-pas-into", res.Error == null && Lines(p) == "1,2,4,3,4,3,5" && p[0].reason == PauseReason.Entry,
                $"pauses : {Lines(p)}");
        }
        // pas a pas principal (over)
        {
            var (p, res, _) = RunDebug(prog, d => d.StopOnEntry = true, (d, pi) => 2);
            Check("debogueur-pas-a-pas-over", res.Error == null && Lines(p) == "1,2,4,5", $"pauses : {Lines(p)}");
        }
        // sortie de routine (out)
        {
            var (p, res, _) = RunDebug(prog, d => d.SetBreakpoints("dbg.a68", new[] { 3 }), (d, pi) => pi.Line == 3 ? 3 : 0);
            Check("debogueur-step-out", res.Error == null && Lines(p) == "3,4,3,5", $"pauses : {Lines(p)}");
        }
        // pause sur erreur, avec pile intacte
        {
            const string bad = "PROC f = (INT k) INT:\n   k % 0;\nprint(f(3))\n";
            string stack = "";
            var (p, res, _) = RunDebug(bad, d => d.BreakOnError = true, (d, pi) => { stack = string.Join(";", d.GetStack().Select(s => $"{s.Name}@{s.Line}")); return 0; });
            Check("debogueur-pause-sur-erreur", p.Count == 1 && p[0].reason == PauseReason.Exception && p[0].line == 2 && res.Error != null && stack == "f@2;programme principal@3",
                $"pauses : {Lines(p)} pile : {stack} erreur : {res.Error}");
        }
        // structures et rangees explorables
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
            Check("debogueur-enfants", kids == "x=4;v=[1:3] (3 elements)|[1]=1;[2]=2;[3]=3", "enfants : " + kids);
        }
    }

    static void MiscTests()
    {
        // annulation d'une boucle infinie
        {
            var cr = Compile("INT n := 0;\nDO n +:= 1 OD\n");
            var exec = cr.Program!.CreateExecution(new TestIO(null, Path.GetTempPath()));
            using var cts = new CancellationTokenSource(300);
            var sw = Stopwatch.StartNew();
            var res = exec.Run(cts.Token);
            Check("annulation-boucle", res.Cancelled && sw.ElapsedMilliseconds < 5000, $"cancelled={res.Cancelled} en {sw.ElapsedMilliseconds} ms");
        }
        // annulation dans des branches PAR et pendant une attente de semaphore
        {
            var cr = Compile("SEMA s = LEVEL 0;\nPAR (DO SKIP OD, (DOWN s; SKIP), DO SKIP OD)\n");
            var exec = cr.Program!.CreateExecution(new TestIO(null, Path.GetTempPath()));
            using var cts = new CancellationTokenSource(300);
            var res = exec.Run(cts.Token);
            Check("annulation-par", res.Cancelled, $"cancelled={res.Cancelled} erreur={res.Error}");
        }
        // annulation pendant une pause du debogueur
        {
            var cr = Compile("print(1);\nprint(2)\n");
            var exec = cr.Program!.CreateExecution(new TestIO(null, Path.GetTempPath()));
            var cts = new CancellationTokenSource();
            exec.Debugger.StopOnEntry = true;
            exec.Debugger.Paused += pi => cts.CancelAfter(100); // ne reprend jamais
            var res = exec.Run(cts.Token);
            Check("annulation-en-pause", res.Cancelled, $"cancelled={res.Cancelled}");
        }
        // serialisation : sources + inclusions, rechargement par Deserialize
        {
            var inc = new SourceFile("lib.a68", "PROC double = (INT x) INT: 2 * x;\n");
            var opts = new CompileOptions { IncludeResolver = (n, from) => n == "lib.a68" ? inc : null };
            var cr = lang.Compile(new SourceFile("main.a68", "PR include \"lib.a68\" PR\nprint((double(21), newline))\n"), opts);
            if (!cr.Success) { Program.Fail("interne/serialisation", string.Join("\n", cr.Diagnostics)); }
            else
            {
                var bytes = cr.Program!.Serialize();
                var p2 = lang.Deserialize(bytes);
                var (o, res, _) = Program.Execute(p2, null);
                Check("serialisation", p2.LanguageId == "algol68" && res.Error == null && o == "        +42\n", $"sortie [{o}] {res.Error}");
            }
        }
        // inclusion introuvable
        ExpectCompileError("inclusion-absente", "PR include \"absent.a68\" PR\nSKIP\n", "introuvable", 1);
        // performances : 10 millions d'iterations
        {
            var cr = Compile("INT s := 0;\nFOR i TO 10000000 DO s +:= i MOD 3 OD;\nprint(s)\n");
            var sw = Stopwatch.StartNew();
            var (o, res, _) = Program.Execute(cr.Program!, null);
            Check("performances-10M", res.Error == null && o.Trim() == "+10000000" && sw.Elapsed.TotalSeconds < 5,
                $"sortie {o} en {sw.Elapsed.TotalSeconds:F2} s");
        }
        // pile d'appels dans l'erreur d'execution
        {
            var cr = Compile("PROC g = (INT n) INT: 10 % n;\nPROC f = (INT n) INT: g(n - 1);\nprint(f(1))\n", "pile.a68");
            var (_, res, _) = Program.Execute(cr.Program!, null);
            var st = res.Error == null ? "" : string.Join(";", res.Error.Stack.Select(s => $"{s.Name}@{s.Line}"));
            Check("pile-erreur", res.Error != null && res.Error.Line == 1 && res.Error.File == "pile.a68" && st == "g@1;f@2;programme principal@3",
                $"{res.Error} pile={st}");
        }
        // position exacte des diagnostics
        {
            var cr = Compile("BEGIN\n   INT x := 1;\n   x := y + 1\nEND\n");
            var d = cr.Diagnostics.FirstOrDefault();
            Check("position-diagnostic", d != null && d.Line == 3 && d.Column == 9 && d.Severity == Severity.Error, d?.ToString() ?? "aucun");
        }
        // erreur dans une branche PAR : propagation et arret des autres branches
        ExpectRuntimeError("erreur-par", "[2] INT t;\nPAR (DO SKIP OD, t[3] := 1)\n", "hors des bornes", 2);
        // point d'arret dans une branche parallele
        {
            const string src = "INT a := 0, b := 0;\nPAR (a := 1,\n     b := 2);\nprint((a + b, newline))\n";
            var (p, res, o) = RunDebug(src, d => d.SetBreakpoints("dbg.a68", new[] { 3 }), (d, pi) => 0);
            Check("debogueur-par", res.Error == null && Lines(p) == "3" && o == "         +3\n", $"pauses {Lines(p)} sortie {o} {res.Error}");
        }
        // impression de structure avec champ non initialise
        ExpectRuntimeError("champ-non-initialise", "STRUCT (INT a, b) s;\na OF s := 1;\nprint(s)\n", "non initialisee", 3);
        // valeurs exactes de quelques conversions (algorithmes a68g)
        Expect("conversions", "print((whole(0, 0), whole(-5, 3), fixed(2.5, 0, 0), fixed(-0.25, 0, 2), float(0.0, 10, 3, 2), newline))",
            "0 -53-.25 +0.000e+0\n");
        Expect("stop", "print((\"a\", newline)); stop; print(\"b\")", "a\n");
    }

    public static void Run()
    {
        Expect("bonjour", "print((\"Bonjour\", newline))", "Bonjour\n");
        DebuggerTests();
        MiscTests();
    }
}
