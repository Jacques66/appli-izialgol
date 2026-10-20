using System.Numerics;
using Studio.Common;

namespace Studio.Algol68.Syntax;

public sealed class ParseException : Exception
{
    public Token Tok;
    public ParseException(Token t, string msg) : base(msg) { Tok = t; }
}

/// <summary>Recursive-descent parser (with local backtracking) for Algol 68.</summary>
public sealed class Parser
{
    List<Token> toks;
    int pos;
    readonly List<Diagnostic> diags;
    readonly CompileOptions options;
    int speculative;
    public Stropping Stropping = Stropping.Upper;

    sealed class BoldScope
    {
        public readonly HashSet<string> Modes = new();
        public readonly Dictionary<string, int> Prios = new();
        public readonly HashSet<string> Ops = new();
    }
    List<BoldScope> scopes = new();

    public static readonly Dictionary<string, int> StandardPrios = new()
    {
        ["+:="] = 1, ["-:="] = 1, ["*:="] = 1, ["/:="] = 1, ["%:="] = 1, ["%*:="] = 1, ["+=:"] = 1,
        ["PLUSAB"] = 1, ["MINUSAB"] = 1, ["TIMESAB"] = 1, ["DIVAB"] = 1, ["OVERAB"] = 1, ["MODAB"] = 1, ["PLUSTO"] = 1,
        ["OR"] = 2, ["AND"] = 3, ["&"] = 3, ["XOR"] = 3,
        ["="] = 4, ["/="] = 4, ["~="] = 4, ["^="] = 4, ["EQ"] = 4, ["NE"] = 4,
        ["<"] = 5, ["<="] = 5, [">"] = 5, [">="] = 5, ["LT"] = 5, ["LE"] = 5, ["GT"] = 5, ["GE"] = 5,
        ["+"] = 6, ["-"] = 6,
        ["*"] = 7, ["/"] = 7, ["%"] = 7, ["OVER"] = 7, ["%*"] = 7, ["MOD"] = 7, ["ELEM"] = 7, ["SET"] = 7, ["CLEAR"] = 7,
        ["**"] = 8, ["^"] = 8, ["SHL"] = 8, ["SHR"] = 8, ["UP"] = 8, ["DOWN"] = 8, ["LWB"] = 8, ["UPB"] = 8, ["ELEMS"] = 8,
        ["I"] = 9, ["+*"] = 9,
    };

    static readonly HashSet<string> Keywords = new()
    {
        "BEGIN","END","IF","THEN","ELIF","ELSE","FI","CASE","IN","OUSE","OUT","ESAC","FOR","FROM","BY","TO","DOWNTO","WHILE","UNTIL","DO","OD",
        "GOTO","GO","SKIP","NIL","EMPTY","TRUE","FALSE","MODE","OP","PRIO","PROC","REF","LOC","HEAP","NEW","STRUCT","UNION","FLEX","LONG","SHORT",
        "INT","REAL","BOOL","CHAR","BITS","BYTES","STRING","COMPL","COMPLEX","VOID","FORMAT","FILE","CHANNEL","SEMA","PAR","OF","AT","IS","ISNT","EXIT",
        "ANDF","ORF","ANDTH","OREL","ASSERT","CO","COMMENT","PR","PRAGMAT",
    };
    static readonly HashSet<string> PrimDeclarers = new() { "INT", "REAL", "BOOL", "CHAR", "BITS", "BYTES", "STRING", "COMPL", "COMPLEX", "VOID", "FORMAT", "FILE", "CHANNEL", "SEMA" };

    public Parser(List<Token> toks, List<Diagnostic> diags, CompileOptions options)
    {
        this.toks = toks; this.diags = diags; this.options = options;
        var std = new BoldScope();
        foreach (var kv in StandardPrios) std.Prios[kv.Key] = kv.Value;
        scopes.Add(std);
    }

    Token Cur => toks[pos];
    Token Peek(int k = 1) => toks[Math.Min(pos + k, toks.Count - 1)];
    bool IsBold(string w) => Cur.Kind == TK.Bold && Cur.Text == w;
    bool IsBoldAt(int i, string w) { var t = toks[Math.Min(i, toks.Count - 1)]; return t.Kind == TK.Bold && t.Text == w; }
    Token Next() { var t = toks[pos]; if (pos < toks.Count - 1) pos++; return t; }
    ParseException Err(string msg) => new(Cur, msg);
    ParseException Err(Token t, string msg) => new(t, msg);

    void Expect(TK k, string what)
    {
        if (Cur.Kind != k) throw Err($"{what} expected, found {Cur}");
        Next();
    }
    void ExpectBold(string w)
    {
        if (!IsBold(w)) throw Err($"'{w}' expected, found {Cur}");
        Next();
    }
    Token? lastReported;
    void Report(Token t, string msg)
    {
        if (speculative > 0) return;
        // a single error per symbol: avoids cascades after an error
        if (lastReported != null && lastReported.Line == t.Line && lastReported.Col == t.Col && lastReported.File == t.File) return;
        lastReported = t;
        diags.Add(new Diagnostic(Severity.Error, t.File, t.Line, t.Col, msg));
    }

    // ------------------------------------------------------------ ranges of bold words

    bool IsModeIndicant(string w)
    {
        if (Keywords.Contains(w)) return false;
        for (int i = scopes.Count - 1; i >= 0; i--)
        {
            if (scopes[i].Modes.Contains(w)) return true;
            if (scopes[i].Ops.Contains(w) || scopes[i].Prios.ContainsKey(w)) return false;
        }
        return false;
    }
    int? PrioOf(string op)
    {
        for (int i = scopes.Count - 1; i >= 0; i--)
            if (scopes[i].Prios.TryGetValue(op, out int p)) return p;
        return null;
    }

    static bool IsOpener(Token t) => t.Kind is TK.LParen or TK.LBrack || (t.Kind == TK.Bold && t.Text is "BEGIN" or "IF" or "CASE" or "DO");
    static bool IsCloser(Token t) => t.Kind is TK.RParen or TK.RBrack || (t.Kind == TK.Bold && t.Text is "END" or "FI" or "ESAC" or "OD");
    static bool IsSerialTerminator(Token t) => t.Kind is TK.EOF or TK.RParen or TK.RBrack or TK.Bar or TK.BarColon ||
        (t.Kind == TK.Bold && t.Text is "END" or "FI" or "THEN" or "ELSE" or "ELIF" or "ESAC" or "IN" or "OUT" or "OUSE" or "OD" or "DO" or "UNTIL");
    bool doTerminates;
    /// <summary>Terminator of the current series (DO only terminates the WHILE part).</summary>
    bool IsTerm(Token t) => IsSerialTerminator(t) && (doTerminates || !(t.Kind == TK.Bold && t.Text == "DO"));

    void PushScope(int from)
    {
        var sc = new BoldScope();
        int depth = 0;
        int mode = 0; // 1 MODE, 2 PRIO, 3 OP
        bool pendingLoop = false;
        for (int i = from; i < toks.Count; i++)
        {
            var t = toks[i];
            if (depth == 0)
            {
                if (t.Kind == TK.Bold && t.Text is "FOR" or "FROM" or "BY" or "DOWNTO" or "WHILE" ||
                    (t.Kind == TK.Bold && t.Text == "TO" && !(i > 0 && toks[i - 1].Kind == TK.Bold && toks[i - 1].Text == "GO")))
                    pendingLoop = true;
                if (t.Kind == TK.Bold && t.Text == "DO")
                {
                    bool phraseStart = i == from || toks[i - 1].Kind is TK.Semi or TK.Colon || (toks[i - 1].Kind == TK.Bold && toks[i - 1].Text == "EXIT");
                    if (pendingLoop || phraseStart) { pendingLoop = false; depth++; continue; }
                }
                if (IsSerialTerminator(t)) break;
                if (t.Kind == TK.Semi || (t.Kind == TK.Bold && t.Text == "EXIT")) { mode = 0; continue; }
                if (t.Kind == TK.Bold && t.Text == "MODE") { mode = 1; AddModeAt(sc, i + 1); continue; }
                if (t.Kind == TK.Bold && t.Text == "PRIO") { mode = 2; AddPrioAt(sc, i + 1); continue; }
                if (t.Kind == TK.Bold && t.Text == "OP") { mode = 3; AddOpAt(sc, i + 1); continue; }
                if (t.Kind == TK.Bold && t.Text == "PROC") { mode = 0; continue; }
                if (t.Kind == TK.Comma)
                {
                    if (mode == 1) AddModeAt(sc, i + 1);
                    else if (mode == 2) AddPrioAt(sc, i + 1);
                    else if (mode == 3) AddOpAt(sc, i + 1);
                    continue;
                }
            }
            if (IsOpener(t)) depth++;
            else if (IsCloser(t)) { if (depth == 0) break; depth--; }
        }
        scopes.Add(sc);
    }
    void AddModeAt(BoldScope sc, int i)
    {
        if (i + 1 < toks.Count && toks[i].Kind == TK.Bold && !Keywords.Contains(toks[i].Text) && toks[i + 1].Kind == TK.Op && toks[i + 1].Text == "=")
            sc.Modes.Add(toks[i].Text);
    }
    void AddPrioAt(BoldScope sc, int i)
    {
        if (i + 2 >= toks.Count) return;
        var t = toks[i];
        if (t.Kind != TK.Bold && t.Kind != TK.Op) return;
        if (t.Kind == TK.Op && t.Text.Length > 1 && t.Text.EndsWith("=") && toks[i + 1].Kind == TK.IntDen)
        {
            sc.Prios[t.Text[..^1]] = (int)toks[i + 1].IntValue;
            return;
        }
        if (toks[i + 1].Kind == TK.Op && toks[i + 1].Text == "=" && toks[i + 2].Kind == TK.IntDen)
            sc.Prios[t.Text] = (int)toks[i + 2].IntValue;
    }
    void AddOpAt(BoldScope sc, int i)
    {
        // OP name = ...   or   OP (modes) mode name = ...
        int depth = 0;
        for (int j = i; j < toks.Count && j < i + 200; j++)
        {
            var t = toks[j];
            if (t.Kind == TK.LParen) depth++;
            else if (t.Kind == TK.RParen) depth--;
            else if (depth == 0 && t.Kind == TK.Bold && !Keywords.Contains(t.Text) && j + 1 < toks.Count && toks[j + 1].Kind == TK.Op && toks[j + 1].Text == "=")
            { sc.Ops.Add(t.Text); return; }
            else if (depth == 0 && (t.Kind == TK.Semi || IsSerialTerminator(t))) return;
            if (depth == 0 && t.Kind == TK.Op && j == i) return;
        }
    }
    void PopScope() => scopes.RemoveAt(scopes.Count - 1);

    // ------------------------------------------------------------ program

    public SerialAst ParseProgram()
    {
        var s = ParseSerialBody();
        if (Cur.Kind != TK.EOF)
            Report(Cur, $"unexpected symbol {Cur} after the end of the program (extra parenthesis or END?)");
        return s;
    }

    SerialAst ParseSerialBody()
    {
        PushScope(pos);
        try { return ParseSerialInner(); }
        finally { PopScope(); }
    }

    SerialAst ParseSerialInner(bool doTerm = false)
    {
        bool saveDo = doTerminates;
        doTerminates = doTerm;
        try { return ParseSerialInnerCore(); }
        finally { doTerminates = saveDo; }
    }

    SerialAst ParseSerialInnerCore()
    {
        var serial = new SerialAst().At<SerialAst>(Cur);
        while (true)
        {
            if (IsTerm(Cur) && serial.Phrases.Count > 0) break;
            if (IsTerm(Cur)) { Report(Cur, $"unit expected, found {Cur}"); break; }
            int start = pos;
            try
            {
                ParsePhraseInto(serial);
            }
            catch (ParseException e)
            {
                Report(e.Tok, e.Message);
                Recover(start);
            }
            if (Cur.Kind == TK.Semi) { Next(); if (IsTerm(Cur)) { Report(Cur, $"unit expected after ';', found {Cur}"); break; } continue; }
            if (IsBold("EXIT"))
            {
                serial.ExitAfter.Add(serial.Phrases.Count - 1);
                Next();
                continue;
            }
            if (IsTerm(Cur) || Cur.Kind == TK.Comma) break;
            Report(Cur, $"';' expected, found {Cur}");
            Recover(pos);
            if (Cur.Kind == TK.Semi) { Next(); continue; }
            break;
        }
        return serial;
    }

    void Recover(int start)
    {
        if (pos == start && Cur.Kind != TK.EOF && !IsTerm(Cur)) Next();
        int depth = 0;
        while (Cur.Kind != TK.EOF)
        {
            if (depth == 0 && (Cur.Kind == TK.Semi || IsTerm(Cur))) return;
            if (IsOpener(Cur)) depth++;
            else if (IsCloser(Cur)) { if (depth == 0) return; depth--; }
            Next();
        }
    }

    void ParsePhraseInto(SerialAst serial)
    {
        if (IsDeclarationStart())
        {
            ParseDeclarations(serial.Phrases);
            return;
        }
        var lu = new LabeledUnit().At<LabeledUnit>(Cur);
        while (Cur.Kind == TK.Ident && Peek().Kind == TK.Colon)
        {
            lu.Labels.Add(Cur.Text);
            Next(); Next();
        }
        if (lu.Labels.Count > 0 && IsTerm(Cur))
        {
            lu.Unit = new SkipAst().At<SkipAst>(Cur);
        }
        else lu.Unit = ParseUnit();
        serial.Phrases.Add(lu);
    }

    // ------------------------------------------------------------ declarations

    bool IsDeclarerStartAt(int i)
    {
        var t = toks[Math.Min(i, toks.Count - 1)];
        if (t.Kind == TK.LBrack) return true;
        if (t.Kind != TK.Bold) return false;
        if (PrimDeclarers.Contains(t.Text) || t.Text is "LONG" or "SHORT" or "REF" or "PROC" or "STRUCT" or "UNION" or "FLEX") return true;
        return IsModeIndicant(t.Text);
    }

    bool IsDeclarationStart()
    {
        var t = Cur;
        if (t.Kind == TK.Bold && t.Text is "MODE" or "OP" or "PRIO") return true;
        if (t.Kind == TK.Bold && t.Text is "LOC" or "HEAP" or "NEW")
        {
            // LOC INT x  (declaration) or  LOC INT := 3 (generator inside a unit)
            int save = pos;
            speculative++;
            try { Next(); ParseDeclarer(); return Cur.Kind == TK.Ident; }
            catch (ParseException) { return false; }
            finally { pos = save; speculative--; }
        }
        if (t.Kind == TK.Bold && t.Text == "PROC" && Peek().Kind == TK.Ident) return true;
        if (IsDeclarerStartAt(pos))
        {
            int save = pos;
            speculative++;
            try { ParseDeclarer(); return Cur.Kind == TK.Ident; }
            catch (ParseException) { return false; }
            finally { pos = save; speculative--; }
        }
        return false;
    }

    void ParseDeclarations(List<Ast> into)
    {
        while (true)
        {
            ParseOneDeclaration(into);
            if (Cur.Kind == TK.Comma && (IsDeclarationStart()))
            {
                // cannot happen: the comma is consumed by ParseOneDeclaration only when the declaration continues
            }
            if (Cur.Kind == TK.Comma)
            {
                int save = pos;
                Next();
                if (IsDeclarationStart()) continue;
                pos = save;
            }
            return;
        }
    }

    (string name, Token tok) ParseOpName()
    {
        var t = Cur;
        if (t.Kind == TK.Op) { Next(); return (t.Text, t); }
        if (t.Kind == TK.Bold && !Keywords.Contains(t.Text)) { Next(); return (t.Text, t); }
        if (t.Kind == TK.Bold && t.Text is "AND" or "OR") { Next(); return (t.Text, t); }
        throw Err($"operator symbol expected, found {t}");
    }

    /// <summary>Consumes '='; handles operators glued to '=' (OP +=, PRIO //=).</summary>
    void ExpectEqualsAfterOp(ref string name)
    {
        if (Cur.Kind == TK.Op && Cur.Text == "=") { Next(); return; }
        throw Err($"'=' expected, found {Cur}");
    }

    void ParseOneDeclaration(List<Ast> into)
    {
        var t = Cur;
        if (IsBold("MODE"))
        {
            Next();
            var d = new ModeDeclAst().At<ModeDeclAst>(t);
            while (true)
            {
                var nt = Cur;
                if (nt.Kind != TK.Bold || Keywords.Contains(nt.Text)) throw Err($"mode indicant expected, found {nt}");
                Next();
                if (!(Cur.Kind == TK.Op && Cur.Text == "=")) throw Err($"'=' expected after MODE {nt.Text}");
                Next();
                var decl = ParseDeclarer();
                d.Items.Add((nt.Text, decl, new SkipAst().At<SkipAst>(nt)));
                if (Cur.Kind == TK.Comma && Peek().Kind == TK.Bold && !Keywords.Contains(Peek().Text) && Peek(2).Kind == TK.Op && Peek(2).Text == "=")
                { Next(); continue; }
                break;
            }
            into.Add(d);
            return;
        }
        if (IsBold("PRIO"))
        {
            Next();
            var d = new PrioDeclAst().At<PrioDeclAst>(t);
            while (true)
            {
                var nt = Cur;
                string name;
                if (nt.Kind == TK.Op && nt.Text.Length > 1 && nt.Text.EndsWith("=") && Peek().Kind == TK.IntDen) { name = nt.Text[..^1]; Next(); }
                else
                {
                    (name, _) = ParseOpName();
                    if (!(Cur.Kind == TK.Op && Cur.Text == "=")) throw Err("'=' expected in the priority declaration");
                    Next();
                }
                if (Cur.Kind != TK.IntDen) throw Err("priority (digit 1 to 9) expected");
                int p = (int)Cur.IntValue;
                if (p < 1 || p > 9) Report(Cur, "the priority must be between 1 and 9");
                Next();
                d.Items.Add((name, p, new SkipAst().At<SkipAst>(nt)));
                if (Cur.Kind == TK.Comma && (Peek().Kind == TK.Op || (Peek().Kind == TK.Bold && !Keywords.Contains(Peek().Text))) && !IsDeclarerStartAt(pos + 1))
                { Next(); continue; }
                break;
            }
            into.Add(d);
            return;
        }
        if (IsBold("OP"))
        {
            Next();
            var d = new OpDeclAst().At<OpDeclAst>(t);
            if (Cur.Kind == TK.LParen)
            {
                // OP (INT, INT) INT name = unit
                var pd = new ProcDeclarer().At<ProcDeclarer>(Cur);
                Next();
                while (true)
                {
                    pd.Params.Add(ParseDeclarer());
                    if (Cur.Kind == TK.Comma) { Next(); continue; }
                    break;
                }
                Expect(TK.RParen, "')'");
                pd.Result = ParseDeclarer();
                d.Mode = pd;
            }
            while (true)
            {
                var nt = Cur;
                string name;
                if (nt.Kind == TK.Op && nt.Text.Length > 1 && nt.Text.EndsWith("=") && !(Peek().Kind == TK.Op && Peek().Text == "=") && nt.Text != "<=" && nt.Text != ">=" && nt.Text != "/=" && nt.Text != "~=" && nt.Text != "^=")
                { name = nt.Text[..^1]; Next(); }
                else
                {
                    (name, _) = ParseOpName();
                    if (!(Cur.Kind == TK.Op && Cur.Text == "=")) throw Err($"'=' expected after OP {name}");
                    Next();
                }
                var v = ParseUnit();
                d.Items.Add((name, v, new SkipAst().At<SkipAst>(nt)));
                if (Cur.Kind == TK.Comma && (Peek().Kind == TK.Op || (Peek().Kind == TK.Bold && !Keywords.Contains(Peek().Text) && !IsModeIndicant(Peek().Text))) )
                { Next(); continue; }
                break;
            }
            into.Add(d);
            return;
        }
        bool heap = false, hasLoc = false;
        if (Cur.Kind == TK.Bold && Cur.Text is "LOC" or "HEAP" or "NEW") { heap = Cur.Text != "LOC"; hasLoc = true; Next(); }
        if (IsBold("PROC") && Peek().Kind == TK.Ident)
        {
            Next();
            var d = new ProcDeclAst().At<ProcDeclAst>(t);
            d.Heap = heap;
            bool first = true;
            while (true)
            {
                var nt = Cur;
                if (nt.Kind != TK.Ident) throw Err($"identifier expected, found {nt}");
                Next();
                bool variable;
                if (Cur.Kind == TK.Op && Cur.Text == "=") variable = false;
                else if (Cur.Kind == TK.Becomes) variable = true;
                else throw Err($"'=' or ':=' expected after PROC {nt.Text}");
                if (!first && variable != d.Variable)
                {
                    into.Add(d);
                    d = new ProcDeclAst().At<ProcDeclAst>(nt); d.Heap = heap;
                }
                d.Variable = variable;
                first = false;
                Next();
                var v = ParseUnit();
                d.Items.Add((nt.Text, v, new SkipAst().At<SkipAst>(nt)));
                if (Cur.Kind == TK.Comma && Peek().Kind == TK.Ident && (Peek(2).Kind == TK.Becomes || (Peek(2).Kind == TK.Op && Peek(2).Text == "=")))
                { Next(); continue; }
                break;
            }
            into.Add(d);
            return;
        }
        // declarer followed by identifiers
        var declarer = ParseDeclarer();
        DeclarationAst? cur = null;
        while (true)
        {
            var nt = Cur;
            if (nt.Kind != TK.Ident) throw Err($"identifier expected, found {nt}");
            Next();
            if (Cur.Kind == TK.Op && Cur.Text == "=" && !hasLoc)
            {
                Next();
                var v = ParseUnit();
                if (cur is not IdentityDeclAst id) { if (cur != null) into.Add(cur); id = new IdentityDeclAst { Declarer = declarer }.At<IdentityDeclAst>(nt); cur = id; }
                id.Items.Add((nt.Text, v, new SkipAst().At<SkipAst>(nt)));
            }
            else
            {
                UnitAst? init = null;
                if (Cur.Kind == TK.Becomes) { Next(); init = ParseUnit(); }
                else if (Cur.Kind == TK.Op && Cur.Text == "=") throw Err("a LOC/HEAP declaration cannot be an identity declaration");
                if (cur is not VarDeclAst vd) { if (cur != null) into.Add(cur); vd = new VarDeclAst { Declarer = declarer, Heap = heap }.At<VarDeclAst>(nt); cur = vd; }
                vd.Items.Add((nt.Text, init, new SkipAst().At<SkipAst>(nt)));
            }
            if (Cur.Kind == TK.Comma && Peek().Kind == TK.Ident) { Next(); continue; }
            break;
        }
        if (cur != null) into.Add(cur);
    }

    // ------------------------------------------------------------ declarers

    public DeclarerAst ParseDeclarer()
    {
        var t = Cur;
        if (t.Kind == TK.LBrack)
        {
            Next();
            var rd = new RowDeclarer().At<RowDeclarer>(t);
            while (true)
            {
                var b = new BoundAst().At<BoundAst>(Cur);
                if (Cur.Kind == TK.Comma || Cur.Kind == TK.RBrack) b.Formal = true;
                else if (Cur.Kind == TK.Colon) { Next(); b.Formal = true; }
                else
                {
                    var u1 = ParseUnit();
                    if (Cur.Kind == TK.Colon)
                    {
                        Next();
                        b.Lwb = u1;
                        b.Upb = ParseUnit();
                    }
                    else { b.Upb = u1; }
                }
                rd.Bounds.Add(b);
                if (Cur.Kind == TK.Comma) { Next(); continue; }
                break;
            }
            Expect(TK.RBrack, "']'");
            rd.Elem = ParseDeclarer();
            return rd;
        }
        if (t.Kind != TK.Bold) throw Err($"declarer expected, found {t}");
        switch (t.Text)
        {
            case "LONG":
            case "SHORT":
                {
                    int size = 0;
                    while (IsBold("LONG") || IsBold("SHORT")) { size += Cur.Text == "LONG" ? 1 : -1; Next(); }
                    var bt = Cur;
                    if (bt.Kind != TK.Bold || !(bt.Text is "INT" or "REAL" or "BITS" or "BYTES" or "COMPL" or "COMPLEX"))
                        throw Err($"INT, REAL, BITS, BYTES or COMPL expected after LONG/SHORT, found {bt}");
                    Next();
                    return new PrimDeclarer { Name = bt.Text == "COMPLEX" ? "COMPL" : bt.Text, Size = size }.At<PrimDeclarer>(t);
                }
            case "REF": Next(); return new RefDeclarer { Sub = ParseDeclarer() }.At<RefDeclarer>(t);
            case "FLEX": Next(); return new FlexDeclarer { Sub = ParseDeclarer() }.At<FlexDeclarer>(t);
            case "PROC":
                {
                    Next();
                    var pd = new ProcDeclarer().At<ProcDeclarer>(t);
                    if (Cur.Kind == TK.LParen)
                    {
                        Next();
                        while (true)
                        {
                            pd.Params.Add(ParseDeclarer());
                            if (Cur.Kind == TK.Comma) { Next(); continue; }
                            break;
                        }
                        Expect(TK.RParen, "')'");
                    }
                    pd.Result = ParseDeclarer();
                    return pd;
                }
            case "STRUCT":
                {
                    Next();
                    var sd = new StructDeclarer().At<StructDeclarer>(t);
                    Expect(TK.LParen, "'(' after STRUCT");
                    DeclarerAst? fm = null;
                    while (true)
                    {
                        if (Cur.Kind != TK.Ident || fm == null) fm = ParseDeclarer();
                        if (Cur.Kind != TK.Ident) throw Err($"field name expected, found {Cur}");
                        var ft = Next();
                        sd.Fields.Add((fm, ft.Text, new SkipAst().At<SkipAst>(ft)));
                        if (Cur.Kind == TK.Comma) { Next(); continue; }
                        break;
                    }
                    Expect(TK.RParen, "')'");
                    return sd;
                }
            case "UNION":
                {
                    Next();
                    var ud = new UnionDeclarer().At<UnionDeclarer>(t);
                    Expect(TK.LParen, "'(' after UNION");
                    while (true)
                    {
                        ud.Members.Add(ParseDeclarer());
                        if (Cur.Kind == TK.Comma) { Next(); continue; }
                        break;
                    }
                    Expect(TK.RParen, "')'");
                    return ud;
                }
        }
        if (PrimDeclarers.Contains(t.Text))
        {
            Next();
            return new PrimDeclarer { Name = t.Text == "COMPLEX" ? "COMPL" : t.Text }.At<PrimDeclarer>(t);
        }
        if (IsModeIndicant(t.Text))
        {
            Next();
            return new IndicantDeclarer { Name = t.Text }.At<IndicantDeclarer>(t);
        }
        throw Err($"declarer expected, found {t}");
    }

    // ------------------------------------------------------------ units

    bool TrySpeculate<T>(Func<T> f, out T result)
    {
        int save = pos;
        speculative++;
        try { result = f(); return true; }
        catch (ParseException) { pos = save; result = default!; return false; }
        finally { speculative--; }
    }

    public UnitAst ParseUnit()
    {
        var t = Cur;
        if (IsBold("GOTO")) { Next(); return ParseJumpTarget(t); }
        if (IsBold("GO") && IsBoldAt(pos + 1, "TO")) { Next(); Next(); return ParseJumpTarget(t); }
        if (IsBold("ASSERT"))
        {
            Next();
            var c = ParseTertiary();
            return new AssertAst { Cond = c }.At<AssertAst>(t);
        }
        // routine text: (params) declarer : unit   |   declarer : unit
        if (t.Kind == TK.LParen && IsDeclarerStartAt(pos + 1))
        {
            if (TrySpeculate(ParseRoutineHeader, out var rt))
            {
                rt.Body = ParseUnit();
                return rt;
            }
        }
        else if (IsDeclarerStartAt(pos) && !(IsBold("PROC") && Peek().Kind == TK.Ident))
        {
            if (TrySpeculate(() => { var d = ParseDeclarer(); if (Cur.Kind != TK.Colon) throw Err("x"); Next(); return d; }, out var res))
            {
                var rt2 = new RoutineTextAst { Result = res }.At<RoutineTextAst>(t);
                rt2.Body = ParseUnit();
                return rt2;
            }
        }
        var left = ParseTertiary();
        if (Cur.Kind == TK.Becomes)
        {
            var bt = Next();
            var src = ParseUnit();
            return new AssignAst { Dest = left, Source = src }.At<AssignAst>(bt);
        }
        if (Cur.Kind == TK.IdtyEq || Cur.Kind == TK.IdtyNe || IsBold("IS") || IsBold("ISNT"))
        {
            var it = Next();
            bool not = it.Kind == TK.IdtyNe || it.Text == "ISNT";
            var right = ParseTertiary();
            return new IdentityRelAst { Left = left, Right = right, Not = not }.At<IdentityRelAst>(it);
        }
        return left;
    }

    UnitAst ParseJumpTarget(Token t)
    {
        if (Cur.Kind != TK.Ident) throw Err($"label expected after GOTO, found {Cur}");
        var l = Next();
        return new JumpAst { Label = l.Text }.At<JumpAst>(t);
    }

    RoutineTextAst ParseRoutineHeader()
    {
        var rt = new RoutineTextAst().At<RoutineTextAst>(Cur);
        Expect(TK.LParen, "'('");
        DeclarerAst? d = null;
        while (true)
        {
            if (Cur.Kind != TK.Ident || d == null) d = ParseDeclarer();
            if (Cur.Kind != TK.Ident) throw Err("parameter name expected");
            var nt = Next();
            rt.Params.Add(new ParamAst { Declarer = d, Name = nt.Text }.At<ParamAst>(nt));
            if (Cur.Kind == TK.Comma) { Next(); continue; }
            break;
        }
        Expect(TK.RParen, "')'");
        rt.Result = ParseDeclarer();
        if (Cur.Kind != TK.Colon) throw Err("':' expected");
        Next();
        return rt;
    }

    public UnitAst ParseTertiary() => ParseFormula(1);

    bool IsOperatorToken(Token t)
    {
        if (t.Kind == TK.Op) return true;
        if (t.Kind == TK.Bold)
        {
            if (t.Text is "AND" or "OR") return true;
            if (Keywords.Contains(t.Text)) return false;
            return !IsModeIndicant(t.Text);
        }
        return false;
    }

    UnitAst ParseFormula(int minPrio)
    {
        var left = ParseOperand();
        while (true)
        {
            var t = Cur;
            if (t.Kind == TK.Bold && t.Text is "ANDF" or "ANDTH" or "ORF" or "OREL")
            {
                int p = t.Text is "ANDF" or "ANDTH" ? 3 : 2;
                if (p < minPrio) break;
                Next();
                var r = ParseFormula(p + 1);
                left = new AndThenAst { Left = left, Right = r, IsOr = p == 2 }.At<AndThenAst>(t);
                continue;
            }
            if (!IsOperatorToken(t)) break;
            int? prio = PrioOf(t.Text);
            if (prio == null)
            {
                Report(t, $"dyadic operator '{t.Text}' without priority declaration (PRIO)");
                prio = 9;
            }
            if (prio.Value < minPrio) break;
            Next();
            var right = ParseFormula(prio.Value + 1);
            left = new DyadicAst { Op = t.Text, Left = left, Right = right }.At<DyadicAst>(t);
        }
        return left;
    }

    UnitAst ParseOperand()
    {
        var t = Cur;
        if (IsOperatorToken(t))
        {
            Next();
            var operand = ParseOperand();
            return new MonadicAst { Op = t.Text, Operand = operand }.At<MonadicAst>(t);
        }
        return ParseSecondary();
    }

    UnitAst ParseSecondary()
    {
        var t = Cur;
        if (t.Kind == TK.Bold && t.Text is "LOC" or "HEAP" or "NEW")
        {
            Next();
            var d = ParseDeclarer();
            return new GeneratorAst { Heap = t.Text != "LOC", Declarer = d }.At<GeneratorAst>(t);
        }
        if (t.Kind == TK.Ident && IsBoldAt(pos + 1, "OF"))
        {
            Next(); Next();
            var sec = ParseSecondary();
            return new SelectionAst { Field = t.Text, Secondary = sec }.At<SelectionAst>(t);
        }
        return ParsePrimary();
    }

    UnitAst ParsePrimary()
    {
        var p = ParsePrimaryBase();
        while (true)
        {
            if (Cur.Kind == TK.LBrack || Cur.Kind == TK.LParen)
            {
                var ot = Cur;
                bool brack = ot.Kind == TK.LBrack;
                Next();
                var ap = new ApplyAst { Primary = p, Brackets = brack }.At<ApplyAst>(ot);
                var closer = brack ? TK.RBrack : TK.RParen;
                if (Cur.Kind == closer && !brack)
                {
                    throw Err("empty argument list");
                }
                while (true)
                {
                    ap.Args.Add(ParseIndexer(closer));
                    if (Cur.Kind == TK.Comma) { Next(); continue; }
                    break;
                }
                Expect(closer, brack ? "']'" : "')'");
                p = ap;
                continue;
            }
            break;
        }
        return p;
    }

    IndexerAst ParseIndexer(TK closer)
    {
        var ix = new IndexerAst().At<IndexerAst>(Cur);
        bool IsEndTok() => Cur.Kind == TK.Comma || Cur.Kind == closer;
        bool IsAt() => Cur.Kind == TK.At || IsBold("AT");
        if (IsEndTok()) { ix.IsTrimmer = true; return ix; }
        if (IsAt()) { Next(); ix.IsTrimmer = true; ix.AtLwb = ParseUnit(); return ix; }
        if (Cur.Kind == TK.Colon)
        {
            Next(); ix.IsTrimmer = true; ix.HasColon = true;
            if (!IsEndTok() && !IsAt()) ix.Upb = ParseUnit();
            if (IsAt()) { Next(); ix.AtLwb = ParseUnit(); }
            return ix;
        }
        var u = ParseUnit();
        if (Cur.Kind == TK.Colon)
        {
            Next(); ix.IsTrimmer = true; ix.HasColon = true; ix.Lwb = u;
            if (!IsEndTok() && !IsAt()) ix.Upb = ParseUnit();
            if (IsAt()) { Next(); ix.AtLwb = ParseUnit(); }
            return ix;
        }
        if (IsAt()) throw Err("':' expected before '@' in a trimmer");
        ix.Unit = u;
        return ix;
    }

    UnitAst ParsePrimaryBase()
    {
        var t = Cur;
        switch (t.Kind)
        {
            case TK.Ident: Next(); return new IdentAst { Name = t.Text }.At<IdentAst>(t);
            case TK.IntDen: Next(); return new IntDenot { Value = t.IntValue }.At<IntDenot>(t);
            case TK.RealDen: Next(); return new RealDenot { Text = t.Text }.At<RealDenot>(t);
            case TK.BitsDen: Next(); return new BitsDenot { Value = t.IntValue, Radix = t.Radix, Text = t.Text }.At<BitsDenot>(t);
            case TK.StringDen:
                {
                    Next();
                    string s = t.Text;
                    // adjacent denotations are concatenated (a68g extension)
                    while (Cur.Kind == TK.StringDen) { s += Cur.Text; Next(); }
                    return new StringDenot { Value = s }.At<StringDenot>(t);
                }
            case TK.FormatText:
                Next();
                return new FormatTextAst { Format = new FormatParser(this, t, diags, options).Parse() }.At<FormatTextAst>(t);
            case TK.LParen:
                return ParseParenClause();
        }
        if (t.Kind == TK.Bold)
        {
            switch (t.Text)
            {
                case "TRUE": Next(); return new BoolDenot { Value = true }.At<BoolDenot>(t);
                case "FALSE": Next(); return new BoolDenot { Value = false }.At<BoolDenot>(t);
                case "EMPTY": Next(); return new EmptyDenot().At<EmptyDenot>(t);
                case "NIL": Next(); return new NilAst().At<NilAst>(t);
                case "SKIP": Next(); return new SkipAst().At<SkipAst>(t);
                case "BEGIN": return ParseBeginClause();
                case "IF": return ParseIf();
                case "CASE": return ParseCase();
                case "FOR": case "FROM": case "BY": case "TO": case "DOWNTO": case "WHILE": case "DO": return ParseLoop();
                case "PAR": return ParsePar();
                case "LONG":
                case "SHORT":
                    {
                        // long denotation or cast
                        int k = pos, size = 0;
                        while (IsBoldAt(k, "LONG") || IsBoldAt(k, "SHORT")) { size += toks[k].Text == "LONG" ? 1 : -1; k++; }
                        var dt = toks[k];
                        if (dt.Kind is TK.IntDen or TK.RealDen or TK.BitsDen)
                        {
                            pos = k; Next();
                            return dt.Kind switch
                            {
                                TK.IntDen => new IntDenot { Value = dt.IntValue, Size = size }.At<IntDenot>(t),
                                TK.RealDen => new RealDenot { Text = dt.Text, Size = size }.At<RealDenot>(t),
                                _ => new BitsDenot { Value = dt.IntValue, Radix = dt.Radix, Size = size, Text = dt.Text }.At<BitsDenot>(t),
                            };
                        }
                        break;
                    }
            }
        }
        if (IsDeclarerStartAt(pos))
        {
            // cast: declarer enclosed-clause
            var d = ParseDeclarer();
            if (!(Cur.Kind == TK.LParen || IsBold("BEGIN")))
                throw Err($"enclosed clause expected after the declarer of a cast, found {Cur}");
            var e = ParsePrimaryBase();
            return new CastAst { Declarer = d, Enclosed = e }.At<CastAst>(t);
        }
        if (t.Kind == TK.Bold && !Keywords.Contains(t.Text))
            throw Err($"operand expected, found the operator {t}");
        throw Err($"unit expected, found {t}");
    }

    UnitAst ParseBeginClause()
    {
        var t = Next();
        if (IsBold("END")) { Next(); return new VacuumAst().At<VacuumAst>(t); }
        var s = ParseSerialBody();
        if (Cur.Kind == TK.Comma && IsCollateralCandidate(s))
        {
            var col = new CollateralAst().At<CollateralAst>(t);
            col.Units.Add(((LabeledUnit)s.Phrases[0]).Unit);
            while (Cur.Kind == TK.Comma) { Next(); col.Units.Add(ParseUnit()); }
            ExpectBold("END");
            return col;
        }
        ExpectBold("END");
        return new ClosedAst { Serial = s }.At<ClosedAst>(t);
    }

    static bool IsCollateralCandidate(SerialAst s) =>
        s.Phrases.Count == 1 && s.Phrases[0] is LabeledUnit lu && lu.Labels.Count == 0 && s.ExitAfter.Count == 0;

    UnitAst ParseParenClause()
    {
        var t = Next();
        if (Cur.Kind == TK.RParen) { Next(); return new VacuumAst().At<VacuumAst>(t); }
        PushScope(pos);
        try
        {
            var s = ParseSerialInner();
            if (Cur.Kind == TK.Comma && IsCollateralCandidate(s))
            {
                var col = new CollateralAst().At<CollateralAst>(t);
                col.Units.Add(((LabeledUnit)s.Phrases[0]).Unit);
                while (Cur.Kind == TK.Comma) { Next(); col.Units.Add(ParseUnit()); }
                Expect(TK.RParen, "')'");
                return col;
            }
            if (Cur.Kind == TK.Bar)
            {
                Next();
                return ParseBriefChoice(t, s);
            }
            Expect(TK.RParen, "')'");
            return new ClosedAst { Serial = s }.At<ClosedAst>(t);
        }
        finally { PopScope(); }
    }

    /// <summary>( s | ... ): brief conditional, case or conformity clause; the range of s is already pushed.</summary>
    UnitAst ParseBriefChoice(Token t, SerialAst sel)
    {
        if (LooksLikeConformityCase())
        {
            var conf = new ConformityAst { Selector = sel }.At<ConformityAst>(t);
            ParseConformityCases(conf.Cases);
            if (Cur.Kind == TK.Bar) { Next(); conf.Out = ParseSerialBody(); }
            else if (Cur.Kind == TK.BarColon) { var ot = Next(); conf.Ouse = ParseBriefOuse(ot); return Close(conf); }
            return Close(conf);
        }
        var first = ParseSerialBody();
        if (Cur.Kind == TK.Comma)
        {
            // brief case clause
            var cs = new CaseAst { Selector = sel }.At<CaseAst>(t);
            if (!IsCollateralCandidate(first)) throw Err("single unit expected in the IN part of a case clause");
            cs.In.Add(((LabeledUnit)first.Phrases[0]).Unit);
            while (Cur.Kind == TK.Comma) { Next(); cs.In.Add(ParseUnit()); }
            if (Cur.Kind == TK.Bar) { Next(); cs.Out = ParseSerialBody(); }
            else if (Cur.Kind == TK.BarColon) { var ot = Next(); cs.Ouse = ParseBriefOuse(ot); return Close(cs); }
            return Close(cs);
        }
        var cond = new CondAst { If = sel, Then = first, Brief = true }.At<CondAst>(t);
        if (Cur.Kind == TK.Bar) { Next(); cond.Else = ParseSerialBody(); }
        else if (Cur.Kind == TK.BarColon)
        {
            var et = Next();
            cond.ElifCond = ParseBriefOuse(et);
            return Close(cond);
        }
        return Close(cond);
    }

    UnitAst Close(UnitAst u) { Expect(TK.RParen, "')'"); return u; }

    /// <summary>After |: : new brief choice without opening parenthesis; the closing parenthesis is shared.</summary>
    UnitAst ParseBriefOuse(Token t)
    {
        PushScope(pos);
        try
        {
            var s = ParseSerialInner();
            if (Cur.Kind != TK.Bar) throw Err("'|' expected after '|:'");
            Next();
            // same logic as ParseBriefChoice but without closing
            if (LooksLikeConformityCase())
            {
                var conf = new ConformityAst { Selector = s }.At<ConformityAst>(t);
                ParseConformityCases(conf.Cases);
                if (Cur.Kind == TK.Bar) { Next(); conf.Out = ParseSerialBody(); }
                else if (Cur.Kind == TK.BarColon) { var ot = Next(); conf.Ouse = ParseBriefOuse(ot); }
                return conf;
            }
            var first = ParseSerialBody();
            if (Cur.Kind == TK.Comma)
            {
                var cs = new CaseAst { Selector = s }.At<CaseAst>(t);
                cs.In.Add(((LabeledUnit)first.Phrases[0]).Unit);
                while (Cur.Kind == TK.Comma) { Next(); cs.In.Add(ParseUnit()); }
                if (Cur.Kind == TK.Bar) { Next(); cs.Out = ParseSerialBody(); }
                else if (Cur.Kind == TK.BarColon) { var ot = Next(); cs.Ouse = ParseBriefOuse(ot); }
                return cs;
            }
            var cond = new CondAst { If = s, Then = first, Brief = true }.At<CondAst>(t);
            if (Cur.Kind == TK.Bar) { Next(); cond.Else = ParseSerialBody(); }
            else if (Cur.Kind == TK.BarColon) { var et = Next(); cond.ElifCond = ParseBriefOuse(et); }
            return cond;
        }
        finally { PopScope(); }
    }

    UnitAst ParseIf()
    {
        var t = Next(); // IF or ELIF
        PushScope(pos);
        try
        {
            var c = ParseSerialInner();
            ExpectBold("THEN");
            var then = ParseSerialBody();
            var cond = new CondAst { If = c, Then = then }.At<CondAst>(t);
            if (IsBold("ELSE")) { Next(); cond.Else = ParseSerialBody(); ExpectBold("FI"); }
            else if (IsBold("ELIF")) { cond.ElifCond = ParseIf(); /* consomme FI */ }
            else ExpectBold("FI");
            return cond;
        }
        finally { PopScope(); }
    }

    bool LooksLikeConformityCase()
    {
        if (Cur.Kind != TK.LParen || !IsDeclarerStartAt(pos + 1)) return false;
        int save = pos;
        speculative++;
        try
        {
            Next();
            ParseDeclarer();
            while (Cur.Kind == TK.Comma) { Next(); ParseDeclarer(); }
            if (Cur.Kind == TK.Ident) Next();
            if (Cur.Kind != TK.RParen) return false;
            Next();
            return Cur.Kind == TK.Colon;
        }
        catch (ParseException) { return false; }
        finally { pos = save; speculative--; }
    }

    void ParseConformityCases(List<ConformityCase> cases)
    {
        while (true)
        {
            var t = Cur;
            Expect(TK.LParen, "'('");
            var cc = new ConformityCase().At<ConformityCase>(t);
            cc.Declarer = ParseDeclarer();
            if (Cur.Kind == TK.Comma)
            {
                // extension: several modes (INT, REAL x):  -> union
                var ud = new UnionDeclarer().At<UnionDeclarer>(t);
                ud.Members.Add(cc.Declarer);
                while (Cur.Kind == TK.Comma) { Next(); ud.Members.Add(ParseDeclarer()); }
                cc.Declarer = ud;
            }
            if (Cur.Kind == TK.Ident) cc.Name = Next().Text;
            Expect(TK.RParen, "')'");
            Expect(TK.Colon, "':'");
            cc.Unit = ParseUnit();
            cases.Add(cc);
            if (Cur.Kind == TK.Comma) { Next(); continue; }
            break;
        }
    }

    UnitAst ParseCase()
    {
        var t = Next(); // CASE or OUSE
        PushScope(pos);
        try
        {
            var sel = ParseSerialInner();
            ExpectBold("IN");
            if (LooksLikeConformityCase())
            {
                var conf = new ConformityAst { Selector = sel }.At<ConformityAst>(t);
                ParseConformityCases(conf.Cases);
                if (IsBold("OUT")) { Next(); conf.Out = ParseSerialBody(); ExpectBold("ESAC"); }
                else if (IsBold("OUSE")) conf.Ouse = ParseCase();
                else ExpectBold("ESAC");
                return conf;
            }
            var cs = new CaseAst { Selector = sel }.At<CaseAst>(t);
            while (true)
            {
                cs.In.Add(ParseUnit());
                if (Cur.Kind == TK.Comma) { Next(); continue; }
                break;
            }
            if (IsBold("OUT")) { Next(); cs.Out = ParseSerialBody(); ExpectBold("ESAC"); }
            else if (IsBold("OUSE")) cs.Ouse = ParseCase();
            else ExpectBold("ESAC");
            return cs;
        }
        finally { PopScope(); }
    }

    UnitAst ParseLoop()
    {
        var t = Cur;
        var lp = new LoopAst().At<LoopAst>(t);
        if (IsBold("FOR"))
        {
            Next();
            if (Cur.Kind != TK.Ident) throw Err("identifier expected after FOR");
            lp.Var = Next().Text;
        }
        if (IsBold("FROM")) { Next(); lp.From = ParseUnit(); }
        if (IsBold("BY")) { Next(); lp.By = ParseUnit(); }
        if (IsBold("TO")) { Next(); lp.To = ParseUnit(); }
        else if (IsBold("DOWNTO")) { Next(); lp.To = ParseUnit(); lp.DownTo = true; }
        if (lp.By == null && IsBold("BY")) { Next(); lp.By = ParseUnit(); }
        bool pushed = false;
        if (IsBold("WHILE"))
        {
            Next();
            PushScope(pos); pushed = true;
            lp.While = ParseSerialInner(true);
        }
        try
        {
            ExpectBold("DO");
            PushScope(pos);
            try
            {
                lp.Body = ParseSerialInner();
                if (IsBold("UNTIL"))
                {
                    Next();
                    lp.Until = ParseSerialBody();
                }
            }
            finally { PopScope(); }
            ExpectBold("OD");
        }
        finally { if (pushed) PopScope(); }
        return lp;
    }

    UnitAst ParsePar()
    {
        var t = Next();
        var par = new ParallelAst().At<ParallelAst>(t);
        var e = ParsePrimaryBase();
        switch (e)
        {
            case CollateralAst c: par.Units.AddRange(c.Units); break;
            case ClosedAst cl: par.Units.Add(cl); break;
            case VacuumAst: break;
            default: throw Err(t, "collateral clause expected after PAR");
        }
        return par;
    }

    // ------------------------------------------------------------ fragments (formats)

    /// <summary>Parses a unit contained in a text fragment (dynamic format replicator...).</summary>
    internal UnitAst ParseFragmentUnit(List<Token> fragment)
    {
        var saveToks = toks; int savePos = pos;
        toks = fragment; pos = 0;
        try
        {
            var u = ParseUnit();
            if (Cur.Kind != TK.EOF) throw Err($"unexpected symbol {Cur} in the format");
            return u;
        }
        finally { toks = saveToks; pos = savePos; }
    }

    internal List<UnitAst> ParseFragmentUnits(List<Token> fragment)
    {
        var saveToks = toks; int savePos = pos;
        toks = fragment; pos = 0;
        try
        {
            var list = new List<UnitAst>();
            while (true)
            {
                list.Add(ParseUnit());
                if (Cur.Kind == TK.Comma) { Next(); continue; }
                break;
            }
            if (Cur.Kind != TK.EOF) throw Err($"unexpected symbol {Cur} in the format");
            return list;
        }
        finally { toks = saveToks; pos = savePos; }
    }
}
