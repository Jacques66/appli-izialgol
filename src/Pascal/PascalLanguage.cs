using Studio.Common;
using Studio.Pascal.Runtime;
using Studio.Pascal.Semantics;
using Studio.Pascal.Syntax;

namespace Studio.Pascal;

/// <summary>Pascal language service: the language of the Revised Report (Jensen and Wirth, 1974),
/// as implemented by Pascal 6000 on the CDC 6000 series (ETH Zürich).</summary>
public sealed class PascalLanguage : ILanguageService
{
    public string Id => "pascal";
    public string DisplayName => "Pascal";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".pas", ".p" };

    const int MaxDiagnostics = 200;

    public CompilationResult Compile(SourceFile main, CompileOptions options)
    {
        var diags = new List<Diagnostic>();
        try
        {
            var toks = new Lexer(main, diags).Run();
            var compiler = new Compiler(toks, diags, main.Path);
            var info = compiler.CompileProgram();
            if (info == null || diags.Any(d => d.Severity == Severity.Error)) return Done(diags, null);
            return Done(diags, new CompiledProgram(info, main));
        }
        catch (Exception e)
        {
            diags.Add(new Diagnostic(Severity.Error, main.Path, 0, 0, $"internal compiler error: {e.GetType().Name}: {e.Message}"));
            return Done(diags, null);
        }
    }

    static CompilationResult Done(List<Diagnostic> diags, ICompiledProgram? p)
    {
        // remove duplicates and limit the number of messages
        var seen = new HashSet<(string, int, int, string)>();
        var list = new List<Diagnostic>();
        foreach (var d in diags.OrderBy(d => d.File).ThenBy(d => d.Line).ThenBy(d => d.Column))
        {
            if (!seen.Add((d.File, d.Line, d.Column, d.Message))) continue;
            list.Add(d);
            if (list.Count >= MaxDiagnostics)
            {
                list.Add(new Diagnostic(Severity.Info, d.File, d.Line, d.Column, "too many errors: compilation stopped"));
                break;
            }
        }
        return new CompilationResult(list, p);
    }

    public ICompiledProgram Deserialize(byte[] payload)
    {
        var src = CompiledProgram.Parse(payload);
        var res = Compile(src, new CompileOptions());
        if (res.Program == null)
            throw new InvalidOperationException("invalid embedded Pascal program: " + string.Join("; ", res.Diagnostics.Select(d => d.ToString())));
        return res.Program;
    }
}
