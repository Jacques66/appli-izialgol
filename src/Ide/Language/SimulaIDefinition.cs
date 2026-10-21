namespace Studio.Ide.Language;

/// <summary>Simula I: Algol 60 (both hardware representations) extended with the SIMULA block,
/// activity declarations, elements, sets and the sequencing statements. Comments after "end"
/// also stop at "when" and "otherwise".</summary>
public sealed class SimulaIDefinition : Algol60Definition
{
    public override string Id => "simula1";
    public override string DisplayName => "Simula I";
    public override string DefaultExtension => ".s1";
    public override IReadOnlyList<string> Extensions { get; } = new[] { ".s1" };

    public static readonly string[] SimulaIKeywords =
    {
        "SIMULA", "activity", "element", "set", "new", "none", "inspect", "when", "otherwise",
        "activate", "reactivate", "at", "delay", "before", "after", "prior", "this",
    };

    public static readonly string[] SimulaIBuiltins =
    {
        "hold", "passivate", "wait", "cancel", "current", "time", "idle", "terminated", "evtime", "nextev",
        "include", "remove", "head", "first", "last", "suc", "pred", "exist", "empty", "member", "cardinal",
        "clear", "draw", "randint", "uniform", "normal", "negexp", "poisson", "discrete", "histd",
    };

    static readonly HashSet<string> S1KeywordSet = new(Algol60Keywords.Concat(SimulaIKeywords), StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> S1TypeSet = new(Algol60Types.Concat(new[] { "element", "set" }), StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> S1BuiltinSet = new(Algol60Builtins.Concat(SimulaIBuiltins), StringComparer.OrdinalIgnoreCase);
    static readonly string[] S1EndCommentStops = { "end", "else", "when", "otherwise" };

    protected override HashSet<string> KeywordSet => S1KeywordSet;
    protected override HashSet<string> TypeSet => S1TypeSet;
    protected override HashSet<string> BuiltinSet => S1BuiltinSet;
    protected override string[] EndCommentStops => S1EndCommentStops;
    protected override string? ActivityWord => "activity";

    static readonly string[] S1CompletionList =
        Algol60Keywords.Concat(SimulaIKeywords).Concat(Algol60Builtins).Concat(SimulaIBuiltins)
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    public override IReadOnlyList<string> CompletionWords => S1CompletionList;

    public override string NewFileTemplate =>
        "comment New Simula I program;\r\n" +
        "SIMULA begin\r\n" +
        "   activity greeter(n); integer n;\r\n" +
        "   begin\r\n" +
        "      hold(n);\r\n" +
        "      outstring(1, \"hello at time \"); outreal(1, time); outstring(1, \"\\n\")\r\n" +
        "   end of greeter;\r\n" +
        "\r\n" +
        "   activate new greeter(5);\r\n" +
        "   hold(10)\r\n" +
        "end of simulation model\r\n";
}
