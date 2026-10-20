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

    public static void Run()
    {
        Expect("bonjour", "print((\"Bonjour\", newline))", "Bonjour\n");
    }
}
