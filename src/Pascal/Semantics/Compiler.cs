using Studio.Common;
using Studio.Pascal.Exec;
using Studio.Pascal.Runtime;
using Studio.Pascal.Syntax;

namespace Studio.Pascal.Semantics;

/// <summary>
/// One-pass compiler (recursive descent, as Wirth's own compilers): parses, checks the types and
/// builds the executable tree at the same time. Declarations precede their use (forward for routines,
/// pointer types resolved at the end of the type definition part).
/// </summary>
public sealed partial class Compiler
{
    readonly List<Token> toks;
    readonly List<Diagnostic> diags;
    readonly string file;
    int p;
    int lastErrorTok = -1;

    Scope scope;
    RoutineCtx rc = null!;
    Scope programScope = null!;
    VarSym inputSym = null!, outputSym = null!;
    readonly List<(string Name, Token At)> headingParams = new();
    SeqCtx? seq;
    readonly List<VarSym> activeForVars = new();
    bool inTypePart;
    readonly List<PointerType> pendingPointers = new();

    public static readonly ArrayType Alfa = new(new SubrangeType(IntegerType.T, 1, 10), CharType.T, true) { Name = "alfa" };

    public Compiler(List<Token> toks, List<Diagnostic> diags, string file)
    {
        this.toks = toks; this.diags = diags; this.file = file;
        scope = StandardScope();
    }

    // ------------------------------------------------------------------ tokens and diagnostics

    Token Cur => toks[p];
    Token Peek(int k = 1) => toks[Math.Min(p + k, toks.Count - 1)];
    bool Is(T k) => Cur.Kind == k;
    Token Advance() { var t = toks[p]; if (p < toks.Count - 1) p++; return t; }
    bool Accept(T k) { if (Cur.Kind == k) { Advance(); return true; } return false; }

    void Error(Token at, string msg)
    {
        diags.Add(new Diagnostic(Severity.Error, file, at.Line, at.Col, msg));
    }

    /// <summary>Syntax error at the current token (only one per token, to limit cascades).</summary>
    void SyntaxError(string msg)
    {
        if (lastErrorTok == p) return;
        lastErrorTok = p;
        Error(Cur, msg);
    }

    void Warning(Token at, string msg) => diags.Add(new Diagnostic(Severity.Warning, file, at.Line, at.Col, msg));

    bool Expect(T k)
    {
        if (Accept(k)) return true;
        SyntaxError($"'{Lexer.Spelling(k)}' expected but {Describe(Cur)} found");
        return false;
    }

    static string Describe(Token t) => t.Kind switch
    {
        T.Ident => $"identifier '{t.Raw}'",
        T.Int or T.Real => $"number {t.Raw}",
        T.Str => "string " + t.Raw,
        T.EOF => "end of file",
        _ => t.Kind >= T.And ? $"'{t.Raw.ToLowerInvariant()}'" : $"'{t.Raw}'",
    };

    string ExpectIdent()
    {
        if (Is(T.Ident)) return Advance().Text;
        SyntaxError($"identifier expected but {Describe(Cur)} found");
        return "";
    }

    /// <summary>Skips tokens until one of the given kinds (or a block-level keyword).</summary>
    void SkipTo(params T[] kinds)
    {
        while (!Is(T.EOF) && !kinds.Contains(Cur.Kind)) Advance();
    }

    TNode At<TNode>(TNode n, Token t) where TNode : Node
    {
        n.File = file; n.Line = t.Line; n.Col = t.Col;
        return n;
    }

    // ------------------------------------------------------------------ scopes

    Scope StandardScope()
    {
        var s = new Scope(null, 0);
        void Ty(string n, PType t) => s.Syms[n] = new TypeSym { Name = n, Type = t };
        Ty("integer", IntegerType.T); Ty("real", RealType.T); Ty("boolean", EnumType.Boolean); Ty("char", CharType.T);
        Ty("text", FileType.Text); Ty("alfa", Alfa);
        s.Syms["false"] = new ConstSym { Name = "false", Type = EnumType.Boolean, Ord = 0 };
        s.Syms["true"] = new ConstSym { Name = "true", Type = EnumType.Boolean, Ord = 1 };
        s.Syms["maxint"] = new ConstSym { Name = "maxint", Type = IntegerType.T, Ord = RT.MaxInt };
        foreach (var n in StdProcedures) s.Syms[n] = new StdSym { Name = n, Kind = StdKind.Procedure };
        foreach (var n in StdFunctions) s.Syms[n] = new StdSym { Name = n, Kind = StdKind.Function };
        return s;
    }

    public static readonly string[] StdProcedures =
        { "read", "readln", "write", "writeln", "get", "put", "reset", "rewrite", "page", "new", "dispose", "pack", "unpack" };
    public static readonly string[] StdFunctions =
        { "abs", "sqr", "sin", "cos", "exp", "ln", "sqrt", "arctan", "odd", "trunc", "round", "ord", "chr", "succ", "pred", "eof", "eoln" };

    Sym? Lookup(string name)
    {
        for (var s = scope; s != null; s = s.Parent)
            if (s.Syms.TryGetValue(name, out var sym)) return sym;
        return null;
    }

    void Declare(Sym s, Token at)
    {
        if (s.Name.Length == 0) return;
        if (scope.Syms.TryGetValue(s.Name, out var old))
        {
            Error(at, $"'{at.Raw}' is already declared in this block (line {old.Line})");
            return;
        }
        s.Line = at.Line; s.Col = at.Col;
        scope.Syms[s.Name] = s;
    }

    // ------------------------------------------------------------------ program

    public RoutineInfo? CompileProgram()
    {
        var main = new RoutineInfo { Name = "program", Level = 1, IsMain = true, File = file };
        var progTok = Cur;
        if (!Expect(T.Program)) { SkipTo(T.Ident, T.Semi); }
        var nameTok = Cur;
        string name = ExpectIdent();
        main.Name = nameTok.Kind == T.Ident ? nameTok.Raw : "program";
        main.Line = progTok.Line; main.Col = progTok.Col;
        if (Accept(T.LParen))
        {
            do
            {
                var t = Cur;
                string n = ExpectIdent();
                if (n.Length == 0) break;
                if (headingParams.Any(h => h.Name == n)) Error(t, $"program parameter '{t.Raw}' listed twice");
                else headingParams.Add((n, t));
            } while (Accept(T.Comma));
            Expect(T.RParen);
        }
        else SyntaxError("'(' expected: the program heading lists the program parameters, e.g. program " + (name.Length > 0 ? nameTok.Raw : "p") + "(input, output);");
        Expect(T.Semi);

        programScope = new Scope(scope, 1);
        scope = programScope;
        rc = new RoutineCtx { Info = main, Level = 1 };
        // input and output: standard text files of the program block
        inputSym = new VarSym { Name = "input", Type = FileType.Text, Level = 1, Slot = rc.Alloc(1), NotInHeading = !headingParams.Any(h => h.Name == "input") };
        outputSym = new VarSym { Name = "output", Type = FileType.Text, Level = 1, Slot = rc.Alloc(1), NotInHeading = !headingParams.Any(h => h.Name == "output") };
        programScope.Syms["input"] = inputSym;
        programScope.Syms["output"] = outputSym;
        main.Files.Add((inputSym.Slot, FileType.Text, "input", "input"));
        main.Files.Add((outputSym.Slot, FileType.Text, "output", "output"));
        if (!inputSym.NotInHeading) main.Vars.Add(new DebugVar { Name = "input", Slot = inputSym.Slot, Type = FileType.Text });
        if (!outputSym.NotInHeading) main.Vars.Add(new DebugVar { Name = "output", Slot = outputSym.Slot, Type = FileType.Text });

        var body = Block(main, isMain: true);
        main.Body = body;
        main.Seal(rc.NextSlot);
        if (!Accept(T.Dot))
        {
            if (Is(T.Semi) && Peek().Kind == T.EOF) SyntaxError("'.' expected at the end of the program (found ';')");
            else SyntaxError($"'.' expected at the end of the program but {Describe(Cur)} found");
        }
        else if (!Is(T.EOF)) Warning(Cur, "text after the final '.' is ignored");
        return main;
    }

    // ------------------------------------------------------------------ blocks

    /// <summary>Block: declaration parts in the order of the Report, then the statement part.</summary>
    Sequence Block(RoutineInfo info, bool isMain)
    {
        bool seenConst = false, seenType = false, seenVar = false, seenRoutine = false, seenLabel = false;
        while (true)
        {
            var t = Cur;
            switch (t.Kind)
            {
                case T.Label:
                    if (seenLabel || seenConst || seenType || seenVar || seenRoutine) Error(t, "the label declaration part must come first and appear only once");
                    seenLabel = true;
                    LabelPart();
                    continue;
                case T.Const:
                    if (seenConst || seenType || seenVar || seenRoutine) Error(t, "the constant definition part must precede the type, variable and procedure declarations and appear only once");
                    seenConst = true;
                    ConstPart();
                    continue;
                case T.Type:
                    if (seenType || seenVar || seenRoutine) Error(t, "the type definition part must precede the variable and procedure declarations and appear only once");
                    seenType = true;
                    TypePart();
                    continue;
                case T.Var:
                    if (seenVar || seenRoutine) Error(t, "the variable declaration part must precede the procedure and function declarations and appear only once");
                    seenVar = true;
                    VarPart(isMain);
                    continue;
                case T.Procedure:
                case T.Function:
                    if (isMain && !seenRoutine) CheckHeadingFiles();
                    seenRoutine = true;
                    RoutineDeclaration();
                    continue;
            }
            break;
        }
        if (isMain && !seenRoutine) CheckHeadingFiles();
        // forward declarations without a body
        foreach (var s in scope.Syms.Values.OfType<RoutineSym>())
            if (s.Forward && !s.Defined)
                Error(new Token { Line = s.Line, Col = s.Col }, $"{(s.IsFunction ? "function" : "procedure")} '{s.Name}' is declared forward but its block is missing");

        var beginTok = Cur;
        if (!Is(T.Begin))
        {
            SyntaxError($"'begin' expected but {Describe(Cur)} found");
            SkipTo(T.Begin, T.Dot);
        }
        var body = CompoundStatement(topLevel: true);
        body.Stoppable = false;
        CheckLabels(beginTok);
        return body;
    }

    void CheckHeadingFiles()
    {
        foreach (var (n, at) in headingParams)
        {
            if (n is "input" or "output") continue;
            if (!programScope.Syms.TryGetValue(n, out var s) || s is not VarSym v)
                Error(at, $"program parameter '{at.Raw}' must be declared as a file variable of the program");
            else if (v.Type is not FileType) Error(at, $"program parameter '{at.Raw}' must be a file variable (implementation restriction: external files only)");
        }
        headingParams.RemoveAll(h => h.Name is not ("input" or "output"));
    }

    void CheckLabels(Token at)
    {
        foreach (var l in rc.Labels.Values)
        {
            if (!l.Defined)
            {
                if (l.Used) Error(new Token { Line = l.Line, Col = l.Col }, $"label {l.Value} is used in a goto statement but prefixes no statement");
                else Warning(new Token { Line = l.Line, Col = l.Col }, $"label {l.Value} is declared but not used");
            }
        }
        foreach (var (l, ctx, tok) in rc.LocalGotos)
        {
            if (!l.Defined || l.Seq == null) continue;
            if (!l.Seq.Encloses(ctx))
                Error(tok, $"goto {l.Value} leads into a structured statement (the labelled statement does not enclose the goto statement)");
        }
    }

    // ------------------------------------------------------------------ label, const, type and var parts

    void LabelPart()
    {
        Advance();
        do
        {
            var t = Cur;
            if (!Is(T.Int)) { SyntaxError($"label (unsigned integer) expected but {Describe(Cur)} found"); break; }
            Advance();
            if (t.IntVal > 9999) Error(t, $"label {t.Raw} too large (labels have at most 4 digits)");
            if (rc.Labels.ContainsKey(t.IntVal)) Error(t, $"label {t.IntVal} declared twice");
            else rc.Labels[t.IntVal] = new LabelSym { Name = t.Raw, Value = t.IntVal, Info = new LabelInfo(t.IntVal), Level = rc.Level, Line = t.Line, Col = t.Col };
        } while (Accept(T.Comma));
        Expect(T.Semi);
    }

    void ConstPart()
    {
        Advance();
        if (!Is(T.Ident)) SyntaxError($"constant definition expected but {Describe(Cur)} found");
        while (Is(T.Ident))
        {
            var t = Advance();
            Expect(T.Eq);
            var c = Constant();
            c.Name = t.Text;
            Declare(c, t);
            if (!Expect(T.Semi)) SkipTo(T.Semi, T.Type, T.Var, T.Procedure, T.Function, T.Begin);
            Accept(T.Semi);
        }
    }

    /// <summary>constant = [sign] (unsigned number | constant identifier) | string.</summary>
    ConstSym Constant()
    {
        var t = Cur;
        int sign = 0;
        if (Accept(T.Plus)) sign = 1;
        else if (Accept(T.Minus)) sign = -1;
        var c = new ConstSym { Type = ErrorType.T };
        switch (Cur.Kind)
        {
            case T.Int: c.Type = IntegerType.T; c.Ord = Advance().IntVal; break;
            case T.Real: c.Type = RealType.T; c.Real = Advance().RealVal; break;
            case T.Str:
                {
                    var s = Advance();
                    if (sign != 0) Error(t, "a sign cannot precede a string constant");
                    if (s.Text.Length == 1) { c.Type = CharType.T; c.Ord = s.Text[0]; }
                    else { c.Type = StringType(s.Text.Length); c.Str = s.Text; }
                    return c;
                }
            case T.Ident:
                {
                    var it = Advance();
                    var sym = Lookup(it.Text);
                    if (sym is ConstSym cs)
                    {
                        c.Type = cs.Type; c.Ord = cs.Ord; c.Real = cs.Real; c.Str = cs.Str;
                        if (sign != 0 && !cs.Type.IsNumeric && !cs.Type.IsError) { Error(t, $"a sign can only precede a numeric constant ('{it.Raw}' is of type {cs.Type})"); return c; }
                    }
                    else if (sym == null) Error(it, $"'{it.Raw}' is not declared");
                    else Error(it, $"'{it.Raw}' is not a constant");
                    break;
                }
            default:
                SyntaxError($"constant expected but {Describe(Cur)} found");
                return c;
        }
        if (sign == -1) { c.Ord = -c.Ord; c.Real = -c.Real; }
        return c;
    }

    static readonly Dictionary<int, ArrayType> stringTypes = new();
    static ArrayType StringType(int n)
    {
        lock (stringTypes)
        {
            if (!stringTypes.TryGetValue(n, out var t))
                stringTypes[n] = t = new ArrayType(new SubrangeType(IntegerType.T, 1, n), CharType.T, true);
            return t;
        }
    }

    void TypePart()
    {
        Advance();
        inTypePart = true;
        if (!Is(T.Ident)) SyntaxError($"type definition expected but {Describe(Cur)} found");
        while (Is(T.Ident))
        {
            var t = Advance();
            Expect(T.Eq);
            var ty = ParseType();
            ty.Name ??= t.Raw;
            Declare(new TypeSym { Name = t.Text, Type = ty }, t);
            if (!Expect(T.Semi)) SkipTo(T.Semi, T.Var, T.Procedure, T.Function, T.Begin);
            Accept(T.Semi);
        }
        inTypePart = false;
        foreach (var pt in pendingPointers) ResolvePointer(pt);
        pendingPointers.Clear();
    }

    void ResolvePointer(PointerType pt)
    {
        var name = pt.PendingName!;
        pt.PendingName = null;
        var sym = Lookup(name);
        if (sym is TypeSym ts) pt.Target = ts.Type;
        else
        {
            Error(new Token { Line = pt.Line, Col = pt.Col }, sym == null ? $"type '{name}' is not declared (pointer domain type)" : $"'{name}' is not a type");
            pt.Target = ErrorType.T;
        }
    }

    void VarPart(bool isMain)
    {
        Advance();
        if (!Is(T.Ident)) SyntaxError($"variable declaration expected but {Describe(Cur)} found");
        while (Is(T.Ident))
        {
            var names = new List<Token>();
            do
            {
                if (!Is(T.Ident)) { SyntaxError($"identifier expected but {Describe(Cur)} found"); break; }
                names.Add(Advance());
            } while (Accept(T.Comma));
            Expect(T.Colon);
            var ty = ParseType();
            if (ty.Size > 8_000_000) { Error(names[0], $"variable too large ({ty.Size} cells; the limit is 8000000)"); ty = ErrorType.T; }
            foreach (var n in names)
            {
                var v = new VarSym { Name = n.Text, Type = ty, Level = rc.Level, Slot = rc.Alloc(ty.Size) };
                Declare(v, n);
                AddVarFiles(v, n.Raw, isMain && headingParams.Any(h => h.Name == n.Text) ? n.Raw : null);
                rc.Info.Vars.Add(new DebugVar { Name = n.Raw, Slot = v.Slot, Type = ty });
            }
            if (!Expect(T.Semi)) SkipTo(T.Semi, T.Procedure, T.Function, T.Begin);
            Accept(T.Semi);
        }
    }

    void AddVarFiles(VarSym v, string name, string? external)
    {
        if (!v.Type.ContainsFile) return;
        var list = new List<(int, FileType)>();
        v.Type.CollectFiles(0, list);
        foreach (var (off, ft) in list)
            rc.Info.Files.Add((v.Slot + off, ft, name, v.Type is FileType ? external : null));
    }

    // ------------------------------------------------------------------ types

    PType ParseType()
    {
        var t = Cur;
        if (Accept(T.Caret))
        {
            var it = Cur;
            string n = ExpectIdent();
            var pt = new PointerType { Line = it.Line, Col = it.Col };
            if (n.Length == 0) { pt.Target = ErrorType.T; return pt; }
            if (inTypePart) { pt.PendingName = n; pendingPointers.Add(pt); }
            else
            {
                pt.PendingName = n;
                ResolvePointer(pt);
            }
            return pt;
        }
        bool packed = Accept(T.Packed);
        switch (Cur.Kind)
        {
            case T.Array: return ArrayTypeDef(packed);
            case T.Record:
                {
                    Advance();
                    var rec = new RecordType(packed);
                    int off = 0;
                    FieldList(rec, null, ref off, T.End);
                    rec.SetSize(Math.Max(off, 1));
                    Expect(T.End);
                    return rec;
                }
            case T.Set:
                {
                    Advance();
                    Expect(T.Of);
                    var bt = SimpleType();
                    if (!bt.IsError && !bt.IsOrdinal) { Error(t, $"the base type of a set must be an ordinal type ({bt} is not)"); bt = ErrorType.T; }
                    if (!bt.IsError && !bt.IsChar && (bt.Lo < 0 || bt.Hi > TypeRules.MaxSetElement))
                    {
                        Error(t, $"set base type {bt} out of range: Pascal 6000 sets hold ordinal values 0..58 (only set of char and its subranges are allowed beyond)");
                        bt = ErrorType.T;
                    }
                    return new SetType(bt, packed);
                }
            case T.File:
                {
                    Advance();
                    Expect(T.Of);
                    var et = ParseType();
                    if (et.ContainsFile) Error(t, "the component type of a file may not be or contain a file type");
                    else if (et.ContainsPointer) Error(t, "implementation restriction: the component type of a file may not contain pointers");
                    return new FileType(et, false, packed);
                }
        }
        if (packed) SyntaxError($"'array', 'record', 'set' or 'file' expected after 'packed' but {Describe(Cur)} found");
        return SimpleType();
    }

    PType ArrayTypeDef(bool packed)
    {
        var t = Advance();
        Expect(T.LBrack);
        var indices = new List<PType>();
        do
        {
            var it = Cur;
            var ix = SimpleType();
            if (!ix.IsError && !ix.IsOrdinal) { Error(it, $"the index type of an array must be an ordinal type ({ix} is not)"); ix = ErrorType.T; }
            if (!ix.IsError && ix.Hi - ix.Lo + 1 > 8_000_000) { Error(it, $"index type {ix} too large"); ix = ErrorType.T; }
            indices.Add(ix);
        } while (Accept(T.Comma));
        Expect(T.RBrack);
        Expect(T.Of);
        var elem = ParseType();
        PType res = elem;
        for (int k = indices.Count - 1; k >= 0; k--)
        {
            if (indices[k].IsError) { res = ErrorType.T; continue; }
            if (res.IsError) continue;
            var at = new ArrayType(indices[k], res, packed);
            if ((long)at.Count * res.Size > 8_000_000) { Error(t, "array type too large (more than 8000000 cells)"); res = ErrorType.T; continue; }
            res = at;
        }
        return res;
    }

    /// <summary>Simple (ordinal) type: type identifier, enumeration or subrange; real is accepted where noted by the caller.</summary>
    PType SimpleType()
    {
        var t = Cur;
        if (Accept(T.LParen))
        {
            var names = new List<string>();
            var et = new EnumType(names);
            do
            {
                var it = Cur;
                string n = ExpectIdent();
                if (n.Length == 0) break;
                Declare(new ConstSym { Name = n, Type = et, Ord = names.Count }, it);
                names.Add(it.Raw);
            } while (Accept(T.Comma));
            Expect(T.RParen);
            return et;
        }
        if (Is(T.Ident) && Lookup(Cur.Text) is TypeSym ts && Peek().Kind != T.DotDot)
        {
            Advance();
            return ts.Type;
        }
        if (Is(T.Ident) && Lookup(Cur.Text) == null && Peek().Kind != T.DotDot)
        {
            Error(Cur, $"type '{Cur.Raw}' is not declared");
            Advance();
            return ErrorType.T;
        }
        if (Is(T.Ident) && Lookup(Cur.Text) is not (ConstSym or null) && Peek().Kind != T.DotDot)
        {
            Error(Cur, $"'{Cur.Raw}' is not a type");
            Advance();
            return ErrorType.T;
        }
        var lo = Constant();
        if (!Expect(T.DotDot)) return ErrorType.T;
        var hiTok = Cur;
        var hi = Constant();
        if (lo.Type.IsError || hi.Type.IsError) return ErrorType.T;
        if (!lo.Type.IsOrdinal || !hi.Type.IsOrdinal) { Error(t, "the bounds of a subrange must be ordinal constants (not real or string)"); return ErrorType.T; }
        if (!ReferenceEquals(lo.Type.Host, hi.Type.Host)) { Error(hiTok, $"subrange bounds of different types ({lo.Type} and {hi.Type})"); return ErrorType.T; }
        if (lo.Ord > hi.Ord) { Error(t, $"empty subrange: lower bound {Format.OrdText(lo.Type, lo.Ord)} is greater than upper bound {Format.OrdText(hi.Type, hi.Ord)}"); return ErrorType.T; }
        return new SubrangeType(lo.Type.Host, lo.Ord, hi.Ord);
    }

    /// <summary>Field list of a record (or of a variant): fixed part then optional variant part.</summary>
    void FieldList(RecordType rec, Variant? owner, ref int off, T closer)
    {
        var into = owner?.Fields;
        while (Is(T.Ident))
        {
            var names = new List<Token>();
            do
            {
                if (!Is(T.Ident)) { SyntaxError($"field identifier expected but {Describe(Cur)} found"); break; }
                names.Add(Advance());
            } while (Accept(T.Comma));
            Expect(T.Colon);
            var ty = ParseType();
            foreach (var n in names) AddField(rec, owner, n, ty, ref off);
            if (!Accept(T.Semi)) break;
        }
        if (Is(T.Case))
        {
            var caseTok = Advance();
            var vp = new VariantPart { Parent = owner };
            Field? tag = null;
            PType tagType;
            if (Is(T.Ident) && Peek().Kind == T.Colon)
            {
                var tagTok = Advance();
                Advance();
                tagType = TypeIdent("tag field type");
                tag = AddField(rec, owner, tagTok, tagType, ref off);
                if (tag != null) tag.TagOf = vp;
            }
            else tagType = TypeIdent("tag type");
            if (!tagType.IsError && !tagType.IsOrdinal) { Error(caseTok, $"the tag type of a variant part must be ordinal ({tagType} is not)"); tagType = ErrorType.T; }
            vp.Tag = tag; vp.TagType = tagType;
            if (owner != null) owner.Nested = vp; else rec.VariantPart = vp;
            Expect(T.Of);
            vp.Offset = off;
            var allLabels = new HashSet<long>();
            while (!Is(T.EOF) && !Is(closer) && !Is(T.RParen))
            {
                if (Accept(T.Semi)) continue;
                var v = new Variant { Owner = vp, Offset = off };
                do
                {
                    var lt = Cur;
                    var c = Constant();
                    if (c.Type.IsError || tagType.IsError) continue;
                    if (!TypeRules.Compatible(c.Type, tagType) || !c.Type.IsOrdinal) Error(lt, $"variant label of type {c.Type} does not match the tag type {tagType}");
                    else if (c.Ord < tagType.Lo || c.Ord > tagType.Hi) Error(lt, $"variant label {Format.OrdText(tagType, c.Ord)} out of the range of the tag type {tagType}");
                    else if (!allLabels.Add(c.Ord)) Error(lt, $"variant label {Format.OrdText(tagType, c.Ord)} appears twice");
                    else v.Labels.Add(c.Ord);
                } while (Accept(T.Comma));
                Expect(T.Colon);
                Expect(T.LParen);
                FieldList(rec, v, ref off, T.RParen);
                Expect(T.RParen);
                v.Size = off - v.Offset;
                vp.Variants.Add(v);
                if (!Accept(T.Semi)) break;
            }
            vp.Size = off - vp.Offset;
        }
    }

    Field? AddField(RecordType rec, Variant? owner, Token n, PType ty, ref int off)
    {
        if (rec.Find(n.Text) != null) { Error(n, $"field '{n.Raw}' declared twice in the record"); return null; }
        var fl = new Field { Name = n.Raw, Type = ty, Offset = off, Variant = owner, Line = n.Line, Col = n.Col };
        off += Math.Max(1, ty.Size);
        rec.Fields.Add(fl);
        owner?.Fields.Add(fl);
        return fl;
    }

    PType TypeIdent(string what)
    {
        var t = Cur;
        string n = ExpectIdent();
        if (n.Length == 0) return ErrorType.T;
        var s = Lookup(n);
        if (s is TypeSym ts) return ts.Type;
        Error(t, s == null ? $"type '{t.Raw}' is not declared" : $"'{t.Raw}' is not a type identifier ({what} expected)");
        return ErrorType.T;
    }

    // ------------------------------------------------------------------ procedures and functions

    void RoutineDeclaration()
    {
        bool isFunc = Advance().Kind == T.Function;
        var nameTok = Cur;
        string name = ExpectIdent();
        RoutineSym? sym = null;
        bool completingForward = false;
        if (name.Length > 0 && scope.Syms.TryGetValue(name, out var old) && old is RoutineSym rs && rs.Forward && !rs.Defined)
        {
            sym = rs; completingForward = true;
            if (rs.IsFunction != isFunc) Error(nameTok, $"'{nameTok.Raw}' was declared forward as a {(rs.IsFunction ? "function" : "procedure")}");
        }
        int level = rc.Level + 1;
        if (sym == null)
        {
            var info = new RoutineInfo { Name = nameTok.Kind == T.Ident ? nameTok.Raw : "?", Level = level, File = file, Line = nameTok.Line, Col = nameTok.Col };
            sym = new RoutineSym { Name = name, Info = info, Level = level };
            if (name.Length > 0) Declare(sym, nameTok);
            var ctxTmp = new RoutineCtx { Info = info, Level = level, Parent = rc };
            var outer = scope;
            scope = new Scope(outer, level);
            if (Is(T.LParen)) FormalParameters(info.Sig, ctxTmp, sym, declare: false);
            if (isFunc)
            {
                if (Accept(T.Colon))
                {
                    var rt = TypeIdent("result type");
                    if (!rt.IsError && !(rt.IsOrdinal || rt.IsReal || rt.Kind == TK.Pointer))
                    { Error(nameTok, $"the result type of a function must be a simple or pointer type ({rt} is not)"); rt = ErrorType.T; }
                    info.Sig.Result = rt;
                }
                else { SyntaxError($"':' and the result type expected after the function heading"); info.Sig.Result = ErrorType.T; }
                info.ResultSlot = ctxTmp.Alloc(1);
            }
            sym.ParamsEnd = ctxTmp.NextSlot;
            scope = outer;
        }
        else
        {
            sym.Info.Line = nameTok.Line; sym.Info.Col = nameTok.Col;
            if (Is(T.LParen))
            {
                Error(Cur, $"the parameter list of '{nameTok.Raw}' must not be repeated (it was declared forward)");
                var dummy = new Signature();
                var save = scope; scope = new Scope(save, level);
                FormalParameters(dummy, new RoutineCtx { Info = new RoutineInfo(), Level = level }, null, declare: false);
                scope = save;
            }
            if (Is(T.Colon))
            {
                Error(Cur, $"the result type of '{nameTok.Raw}' must not be repeated (it was declared forward)");
                Advance(); TypeIdent("result type");
            }
        }
        Expect(T.Semi);
        // directive forward
        if (Is(T.Ident) && Cur.Text == "forward")
        {
            Advance();
            if (completingForward) Error(nameTok, $"'{nameTok.Raw}' is already declared forward");
            sym.Forward = true;
            Expect(T.Semi);
            return;
        }
        if (Is(T.Ident) && Cur.Text is "extern" or "external" or "fortran")
        {
            Error(Cur, $"directive '{Cur.Raw}' is not supported (external routines are not available)");
            Advance(); Expect(T.Semi);
            sym.Defined = true;
            sym.Info.Body = new Sequence(Array.Empty<Stmt>(), null, false);
            return;
        }
        sym.Defined = true;
        RoutineBody(sym);
        if (!Accept(T.Semi))
        {
            SyntaxError($"';' expected after the declaration of '{nameTok.Raw}' but {Describe(Cur)} found");
            if (!Is(T.Begin) && !Is(T.Procedure) && !Is(T.Function)) SkipTo(T.Semi, T.Procedure, T.Function, T.Begin);
            Accept(T.Semi);
        }
    }

    void RoutineBody(RoutineSym sym)
    {
        var info = sym.Info;
        var outerScope = scope; var outerRc = rc; var outerSeq = seq;
        var outerFor = activeForVars.ToList();
        activeForVars.Clear();
        scope = new Scope(outerScope, sym.Level);
        rc = new RoutineCtx { Info = info, Level = sym.Level, Parent = outerRc, Sym = sym, NextSlot = sym.ParamsEnd };
        seq = null;
        info.Vars.Clear();
        foreach (var v in sym.ParamSyms)
        {
            scope.Syms[v.Name] = v;
            info.Vars.Add(new DebugVar { Name = v.Name, Slot = v.Slot, Type = v.Type, IsRef = v.IsRef });
        }
        foreach (var fsym in sym.FormalSyms)
        {
            scope.Syms[fsym.Name] = fsym;
            info.Vars.Add(new DebugVar { Name = fsym.Name, Slot = fsym.Slot, Type = fsym.Sig.Result ?? ErrorType.T, IsRoutine = true });
        }
        if (info.IsFunction) info.Vars.Add(new DebugVar { Name = info.Name, Slot = info.ResultSlot, Type = info.Sig.Result!, IsResult = true });
        // display order: parameters, locals, result
        info.Body = Block(info, isMain: false);
        info.EndPos = At(new EmptyStmt(), toks[Math.Max(0, p - 1)]);
        var res = info.Vars.FirstOrDefault(v => v.IsResult);
        if (res != null) { info.Vars.Remove(res); info.Vars.Add(res); }
        info.Seal(rc.NextSlot);
        scope = outerScope; rc = outerRc; seq = outerSeq;
        activeForVars.Clear(); activeForVars.AddRange(outerFor);
    }

    /// <summary>Formal parameter list: value, var, procedure and function parameters.</summary>
    void FormalParameters(Signature sig, RoutineCtx ctx, RoutineSym? owner, bool declare)
    {
        Expect(T.LParen);
        while (true)
        {
            var t = Cur;
            if (Is(T.Procedure) || Is(T.Function))
            {
                bool isFunc = Advance().Kind == T.Function;
                var names = new List<Token>();
                do
                {
                    if (!Is(T.Ident)) { SyntaxError($"identifier expected but {Describe(Cur)} found"); break; }
                    names.Add(Advance());
                } while (Accept(T.Comma));
                var psig = new Signature();
                if (Is(T.LParen))
                {
                    if (names.Count > 1) Error(t, "a parameter list may follow only a single procedural parameter name");
                    var save = scope; scope = new Scope(save, ctx.Level + 1);
                    FormalParameters(psig, new RoutineCtx { Info = new RoutineInfo(), Level = ctx.Level + 1 }, null, declare: false);
                    scope = save;
                }
                else psig.HasList = false;
                if (isFunc)
                {
                    if (Expect(T.Colon))
                    {
                        var rt = TypeIdent("result type");
                        if (!rt.IsError && !(rt.IsOrdinal || rt.IsReal || rt.Kind == TK.Pointer))
                        { Error(t, $"the result type of a function must be a simple or pointer type ({rt} is not)"); rt = ErrorType.T; }
                        psig.Result = rt;
                    }
                    else psig.Result = ErrorType.T;
                }
                foreach (var n in names)
                {
                    var pi = new ParamInfo { Name = n.Raw, Kind = isFunc ? ParamKind.Function : ParamKind.Procedure, Type = psig.Result ?? ErrorType.T, Slot = ctx.Alloc(1), Sig = psig };
                    sig.Params.Add(pi);
                    var fs = new FormalRoutineSym { Name = n.Text, Sig = psig, Level = ctx.Level, Slot = pi.Slot, Line = n.Line, Col = n.Col };
                    if (owner != null)
                    {
                        if (owner.ParamSyms.Any(v => v.Name == fs.Name) || owner.FormalSyms.Any(v => v.Name == fs.Name)) Error(n, $"parameter '{n.Raw}' declared twice");
                        owner.FormalSyms.Add(fs);
                    }
                    else if (scope.Syms.ContainsKey(fs.Name)) Error(n, $"parameter '{n.Raw}' declared twice");
                    scope.Syms[fs.Name] = fs;
                }
            }
            else
            {
                bool isVar = Accept(T.Var);
                var names = new List<Token>();
                do
                {
                    if (!Is(T.Ident)) { SyntaxError($"parameter name expected but {Describe(Cur)} found"); break; }
                    names.Add(Advance());
                } while (Accept(T.Comma));
                Expect(T.Colon);
                var tt = Cur;
                PType ty;
                if (Is(T.Ident)) ty = TypeIdent("parameter type");
                else
                {
                    SyntaxError("the type of a parameter must be a type identifier (declare the type in a type definition)");
                    ty = ParseType();
                }
                if (!isVar && ty.ContainsFile) Error(tt, "a file (or a structure containing a file) can only be passed as a var parameter");
                foreach (var n in names)
                {
                    int size = isVar ? 1 : Math.Max(1, ty.Size);
                    var pi = new ParamInfo { Name = n.Raw, Kind = isVar ? ParamKind.Var : ParamKind.Value, Type = ty, Slot = ctx.Alloc(size), Size = size };
                    sig.Params.Add(pi);
                    var v = new VarSym { Name = n.Text, Type = ty, Level = ctx.Level, Slot = pi.Slot, IsRef = isVar, IsParam = true, Line = n.Line, Col = n.Col };
                    if (owner != null)
                    {
                        if (owner.ParamSyms.Any(x => x.Name == v.Name) || owner.FormalSyms.Any(x => x.Name == v.Name)) Error(n, $"parameter '{n.Raw}' declared twice");
                        owner.ParamSyms.Add(v);
                    }
                    else if (scope.Syms.ContainsKey(v.Name)) Error(n, $"parameter '{n.Raw}' declared twice");
                    scope.Syms[v.Name] = v;
                }
            }
            if (Accept(T.Semi)) continue;
            if (Is(T.RParen)) break;
            SyntaxError($"';' or ')' expected in the parameter list but {Describe(Cur)} found");
            SkipTo(T.RParen, T.Semi, T.Begin);
            if (Accept(T.Semi)) continue;
            break;
        }
        Expect(T.RParen);
    }
}
