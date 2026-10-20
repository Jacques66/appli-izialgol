using Studio.Common;

namespace Studio.Algol68;

public sealed class Algol68Language : ILanguageService
{
    public string Id => "algol68";
    public string DisplayName => "Algol 68";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".a68" };

    public CompilationResult Compile(SourceFile main, CompileOptions options) =>
        new(new[] { new Diagnostic(Severity.Error, main.Path, 1, 1, "compilateur Algol 68 en cours d'ecriture") }, null);

    public ICompiledProgram Deserialize(byte[] payload) => throw new NotImplementedException();
}
