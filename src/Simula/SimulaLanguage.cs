using Studio.Common;

namespace Studio.Simula;

public sealed class SimulaLanguage : ILanguageService
{
    public string Id => "simula";
    public string DisplayName => "Simula 67";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".sim" };

    public CompilationResult Compile(SourceFile main, CompileOptions options) =>
        new(new[] { new Diagnostic(Severity.Error, main.Path, 1, 1, "compilateur Simula en cours d'ecriture") }, null);

    public ICompiledProgram Deserialize(byte[] payload) => throw new NotImplementedException();
}
