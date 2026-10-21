using Studio.Simula;
using Studio.Simula.Compiler;

namespace Studio.Algol60;

/// <summary>
/// Algol 60 (Revised Report 1963, with the environmental block and the rules of the
/// Modified Report 1976), compiled by the Simula engine.
/// </summary>
public sealed class Algol60Language : EngineLanguage
{
    public override string Id => "algol60";
    public override string DisplayName => "Algol 60";
    public override IReadOnlyList<string> FileExtensions { get; } = new[] { ".a60", ".alg" };
    protected override Dialect Dialect => Dialect.Algol60;

    static string? prelude;
    /// <summary>The Algol 60 environmental block (also the base of the Simula I environment).</summary>
    public static string EnvironmentSource => prelude ??= ReadResource(typeof(Algol60Language), "Studio.Algol60.Prelude60.sim");
    protected override string PreludeText => EnvironmentSource;

    protected override Func<string, int, Action<SrcPos, string>, Func<string, List<Token>?>, List<Token>>? UserLexer =>
        (text, file, err, includes) => new Algol60Lexer(text, file, err).Tokenize();
}
