using Studio.Algol60;
using Studio.Simula;
using Studio.Simula.Compiler;

namespace Studio.SimulaI;

/// <summary>
/// Simula I (Dahl and Nygaard, 1965): Algol 60 extended with a SIMULA block, activity
/// declarations, element pointers, sets and the sequencing statements, compiled by the
/// Simula engine.
/// </summary>
public sealed class SimulaILanguage : EngineLanguage
{
    public override string Id => "simula1";
    public override string DisplayName => "Simula I";
    public override IReadOnlyList<string> FileExtensions { get; } = new[] { ".s1" };
    protected override Dialect Dialect => Dialect.SimulaI;

    static string? prelude;
    protected override string PreludeText => prelude ??= BuildPrelude();

    /// <summary>Algol 60 environmental block + Simula I runtime declarations.</summary>
    static string BuildPrelude()
    {
        var algol = Algol60Language.EnvironmentSource;
        var runtime = ReadResource(typeof(SimulaILanguage), "Studio.SimulaI.RuntimeI.sim");
        const string marker = "   minint__ := -2147483647 - 1;";
        int at = algol.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) throw new InvalidOperationException("Algol 60 environment: insertion point not found");
        return algol.Substring(0, at) + runtime + "\n" + algol.Substring(at);
    }

    protected override string Vocabulary(string message) => message
        .Replace("ref(elem__)", "element").Replace("ref(set__)", "set").Replace("ref(activity__)", "activity")
        .Replace("activity__ class ", "activity ");

    protected override Func<string, int, Action<SrcPos, string>, Func<string, List<Token>?>, List<Token>>? UserLexer =>
        (text, file, err, includes) => Transform(new Algol60Lexer(text, file, err) { EndCommentStops = new[] { "when", "otherwise" } }.Tokenize());

    /// <summary>Simula I vocabulary, mapped onto the tokens of the shared parser.</summary>
    static readonly Dictionary<string, T> Keywords = new()
    {
        ["new"] = T.KNew, ["none"] = T.KNone, ["inspect"] = T.KInspect, ["when"] = T.KWhen,
        ["otherwise"] = T.KOtherwise, ["activate"] = T.KActivate, ["reactivate"] = T.KReactivate,
        ["at"] = T.KAt, ["delay"] = T.KDelay, ["before"] = T.KBefore, ["after"] = T.KAfter, ["prior"] = T.KPrior,
    };

    static Token Synth(Token at, T kind, string text) => new() { Kind = kind, Pos = at.Pos, Text = text, Original = text };

    static List<Token> Transform(List<Token> toks)
    {
        var res = new List<Token>(toks.Count + 16);
        for (int k = 0; k < toks.Count; k++)
        {
            var t = toks[k];
            if (t.Kind != T.Ident) { res.Add(t); continue; }
            if (Keywords.TryGetValue(t.Text, out var kw)) { res.Add(Synth(t, kw, t.Text)); continue; }
            switch (t.Text)
            {
                case "activity":
                    // activity A ...  =>  activity__ class A ...
                    res.Add(Synth(t, T.Ident, "activity__"));
                    res.Add(Synth(t, T.KClass, "class"));
                    continue;
                case "element":
                case "set":
                    {
                        bool isSet = t.Text == "set";
                        // only in a declaration: followed by an identifier or by 'array'
                        var next = k + 1 < toks.Count ? toks[k + 1] : null;
                        bool decl = next != null && (next.Kind == T.Ident || next.Kind == T.KArray || next.Kind == T.KProcedure);
                        if (!decl) break;
                        res.Add(Synth(t, T.KRef, "ref"));
                        res.Add(Synth(t, T.LPar, "("));
                        res.Add(Synth(t, T.Ident, isSet ? "set__" : "elem__"));
                        res.Add(Synth(t, T.RPar, ")"));
                        if (isSet && next!.Kind == T.Ident)
                        {
                            // set S1, S2 (passenger): the optional activity qualification is ignored
                            int j = k + 1;
                            while (j < toks.Count && toks[j].Kind != T.Semi)
                            {
                                if (toks[j].Kind == T.LPar && j + 2 < toks.Count && toks[j + 1].Kind == T.Ident && toks[j + 2].Kind == T.RPar)
                                {
                                    toks.RemoveRange(j, 3);
                                    continue;
                                }
                                j++;
                            }
                        }
                        continue;
                    }
            }
            res.Add(t);
        }
        return res;
    }
}
