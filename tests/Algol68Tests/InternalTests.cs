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

    public static void Run()
    {
        Expect("bonjour", "print((\"Bonjour\", newline))", "Bonjour\n");
        DebuggerTests();
    }
}
