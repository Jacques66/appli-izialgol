using Studio.Common;
using Studio.Algol68.Runtime;
using Studio.Algol68.Semantics;
using Studio.Algol68.Syntax;

namespace Studio.Algol68;

/// <summary>Service de langage Algol 68 (Revised Report, syntaxe compatible Algol 68 Genie).</summary>
public sealed class Algol68Language : ILanguageService
{
    public string Id => "algol68";
    public string DisplayName => "Algol 68";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".a68" };

    const int MaxDiagnostics = 200;
    static readonly object compileLock = new();

    public CompilationResult Compile(SourceFile main, CompileOptions options)
    {
        var diags = new List<Diagnostic>();
        try
        {
            lock (compileLock) // le prelude partage la precision LONG LONG
            {
                var lexer = new Lexer(diags, options);
                var toks = lexer.Run(main);
                var parser = new Parser(toks, diags, options) { Stropping = lexer.MainStropping };
                var ast = parser.ParseProgram();
                if (diags.Any(d => d.Severity == Severity.Error)) return Done(diags, null);
                var compiler = new Compiler(diags, lexer.PrecisionPragmat);
                var info = compiler.CompileProgram(ast);
                if (diags.Any(d => d.Severity == Severity.Error)) return Done(diags, null);
                return Done(diags, new CompiledProgram(info, main, lexer.Included.ToList()) { LongLongDigits = compiler.LongLongDigits });
            }
        }
        catch (Exception e)
        {
            diags.Add(new Diagnostic(Severity.Error, main.Path, 0, 0, $"erreur interne du compilateur : {e.GetType().Name} : {e.Message}"));
            return Done(diags, null);
        }
    }

    static CompilationResult Done(List<Diagnostic> diags, ICompiledProgram? p)
    {
        // dedoublonnage et limitation du nombre de messages
        var seen = new HashSet<(string, int, int, string)>();
        var list = new List<Diagnostic>();
        foreach (var d in diags.OrderBy(d => d.File).ThenBy(d => d.Line).ThenBy(d => d.Column))
        {
            if (!seen.Add((d.File, d.Line, d.Column, d.Message))) continue;
            list.Add(d);
            if (list.Count >= MaxDiagnostics)
            {
                list.Add(new Diagnostic(Severity.Info, d.File, d.Line, d.Column, "trop d'erreurs : analyse arretee"));
                break;
            }
        }
        return new CompilationResult(list, p);
    }

    public ICompiledProgram Deserialize(byte[] payload)
    {
        var (main, includes) = CompiledProgram.Parse(payload);
        var opts = new CompileOptions
        {
            IncludeResolver = (name, from) =>
            {
                if (includes.TryGetValue(name, out var s)) return s;
                foreach (var kv in includes)
                    if (string.Equals(Path.GetFileName(kv.Key), Path.GetFileName(name), StringComparison.OrdinalIgnoreCase)) return kv.Value;
                return null;
            },
        };
        var res = Compile(main, opts);
        if (res.Program == null)
            throw new InvalidOperationException("programme Algol 68 embarque invalide : " + string.Join("; ", res.Diagnostics.Select(d => d.ToString())));
        return res.Program;
    }
}
