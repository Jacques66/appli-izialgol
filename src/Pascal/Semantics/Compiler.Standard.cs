using Studio.Pascal.Exec;
using Studio.Pascal.Runtime;
using Studio.Pascal.Syntax;

namespace Studio.Pascal.Semantics;

/// <summary>Standard procedures and functions (Revised Report, chapters 10 and 11).</summary>
public sealed partial class Compiler
{
    /// <summary>input or output used explicitly or by default: must be listed in the program heading.</summary>
    Designator StdFile(VarSym v, Token t)
    {
        if (v.NotInHeading && !v.ReportedHeading)
        {
            v.ReportedHeading = true;
            Error(t, $"'{v.Name}' must be listed in the program heading, e.g. program p(input, output); ('{t.Raw}' uses {v.Name})");
        }
        return At(new VarNode(rc.Level - 1, v.Slot) { Type = FileType.Text, Desc = v.Name, RootSymbol = v, IsEntire = true }, t);
    }

    bool CheckFileVar(Expr e, Token at, string proc)
    {
        if (e.Type.IsError) return false;
        if (e.Type is not FileType || e is not Designator) { Error(at, $"{proc}: a file variable is required (found {e.Type})"); return false; }
        return true;
    }

    Expr IntArg(string what, Token at)
    {
        var e = Expression();
        if (!e.Type.IsInteger && !e.Type.IsError) Error(at, $"{what} must be an integer (found {e.Type})");
        return e;
    }

    Stmt StdProcedure(string name, Token t)
    {
        switch (name)
        {
            case "write": case "writeln": return WriteProc(t, name == "writeln");
            case "read": case "readln": return ReadProc(t, name == "readln");
            case "get": case "put": case "reset": case "rewrite":
                {
                    if (!Expect(T.LParen)) return At(new EmptyStmt(), t);
                    var at = Cur;
                    var f = Expression();
                    Expect(T.RParen);
                    if (!CheckFileVar(f, at, name)) return At(new EmptyStmt(), t);
                    var op = name switch { "get" => FileOp.Get, "put" => FileOp.Put, "reset" => FileOp.Reset, _ => FileOp.Rewrite };
                    return At(new FileOpStmt(op, f), t);
                }
            case "page":
                {
                    Expr f;
                    if (Accept(T.LParen))
                    {
                        var at = Cur;
                        f = Expression();
                        Expect(T.RParen);
                        if (!CheckFileVar(f, at, "page")) return At(new EmptyStmt(), t);
                        if (f.Type is FileType { IsText: false }) { Error(at, "page applies to text files only"); return At(new EmptyStmt(), t); }
                    }
                    else f = StdFile(outputSym, t);
                    return At(new FileOpStmt(FileOp.Page, f), t);
                }
            case "new": case "dispose": return NewDispose(t, name == "new");
            case "pack": case "unpack": return PackUnpack(t, name == "unpack");
        }
        Error(t, $"unknown standard procedure '{name}'");
        return At(new EmptyStmt(), t);
    }

    // ------------------------------------------------------------------ write, writeln

    Stmt WriteProc(Token t, bool ln)
    {
        string proc = ln ? "writeln" : "write";
        Expr? file = null;
        var items = new List<WriteItem>();
        bool any = false;
        if (Accept(T.LParen))
        {
            bool first = true;
            do
            {
                var at = Cur;
                var e = Expression();
                if (first && e.Type is FileType && e is Designator)
                {
                    file = e; first = false;
                    if (Is(T.Colon)) SyntaxError("a field width cannot be applied to a file");
                    continue;
                }
                first = false;
                any = true;
                file ??= StdFile(outputSym, t);
                var ft = file.Type as FileType;
                Expr? w = null, d = null;
                var wt = Cur;
                if (Accept(T.Colon)) w = IntArg("a field width", wt);
                var dt = Cur;
                if (Accept(T.Colon)) d = IntArg("the number of fraction digits", dt);
                if (e.Type.IsError || ft == null) continue;
                if (!ft.IsText)
                {
                    if (w != null) Error(wt, "field widths apply to text files only");
                    if (!TypeRules.AssignCompatible(ft.Elem, e.Type)) { Error(at, $"{proc}: a value of type {e.Type} cannot be written to a file of {ft.Elem}"); continue; }
                    items.Add(new WriteItem { Kind = WK.Elem, E = Coerce(e, ft.Elem, at), Size = ft.Elem.Size });
                    continue;
                }
                WK kind;
                if (e.Type.IsInteger) kind = WK.Int;
                else if (e.Type.IsReal) kind = WK.Real;
                else if (e.Type.IsBoolean) kind = WK.Bool;
                else if (e.Type.IsChar) kind = WK.Char;
                else if (e.Type.IsString) kind = WK.Str;
                else { Error(at, $"{proc}: values of type {e.Type} cannot be written to a text file (only integer, real, boolean, char and strings)"); continue; }
                if (d != null && kind != WK.Real) Error(dt, "the number of fraction digits (x:w:d) applies to real values only");
                items.Add(new WriteItem { Kind = kind, E = e, W = w, D = kind == WK.Real ? d : null, Size = e.Type.IsString ? e.Type.StringLength : 1 });
            } while (Accept(T.Comma));
            Expect(T.RParen);
        }
        file ??= StdFile(outputSym, t);
        if (file.Type is FileType f2)
        {
            if (ln && !f2.IsText) Error(t, "writeln applies to text files only");
            if (!ln && !any) Error(t, "write requires at least one value to write");
        }
        return At(new WriteStmt(file, items.ToArray(), ln), t);
    }

    // ------------------------------------------------------------------ read, readln

    Stmt ReadProc(Token t, bool ln)
    {
        string proc = ln ? "readln" : "read";
        Expr? file = null;
        var items = new List<ReadItem>();
        bool any = false;
        if (Accept(T.LParen))
        {
            bool first = true;
            do
            {
                var at = Cur;
                var d = VariableOrError();
                if (first && d.Type is FileType)
                {
                    file = d; first = false;
                    continue;
                }
                first = false;
                any = true;
                file ??= StdFile(inputSym, t);
                if (d.Type.IsError || file.Type is not FileType ft) continue;
                CheckThreat(d, at, "reading into");
                if (!ft.IsText)
                {
                    if (!TypeRules.AssignCompatible(d.Type, ft.Elem)) { Error(at, $"{proc}: an element of a file of {ft.Elem} cannot be read into {d.Desc} of type {d.Type}"); continue; }
                    items.Add(new ReadItem { Kind = RK.Elem, Target = d, Type = d.Type, Size = d.Type.Size, ToReal = d.Type.IsReal && ft.Elem.IsInteger });
                    continue;
                }
                RK kind;
                if (d.Type.IsInteger) kind = RK.Int;
                else if (d.Type.IsReal) kind = RK.Real;
                else if (d.Type.IsChar) kind = RK.Char;
                else { Error(at, $"{proc}: values of type {d.Type} cannot be read from a text file (only integer, real and char variables)"); continue; }
                items.Add(new ReadItem { Kind = kind, Target = d, Type = d.Type });
            } while (Accept(T.Comma));
            Expect(T.RParen);
        }
        file ??= StdFile(inputSym, t);
        if (file.Type is FileType f2)
        {
            if (ln && !f2.IsText) Error(t, "readln applies to text files only");
            if (!ln && !any) Error(t, "read requires at least one variable");
        }
        return At(new ReadStmt(file, items.ToArray(), ln), t);
    }

    // ------------------------------------------------------------------ new, dispose

    Stmt NewDispose(Token t, bool isNew)
    {
        string proc = isNew ? "new" : "dispose";
        if (!Expect(T.LParen)) return At(new EmptyStmt(), t);
        var at = Cur;
        var d = VariableOrError();
        var tags = new List<(int, long)>();
        var values = new List<long>();     // every tag value given (also those of variant parts without tag field)
        PointerType? pt = d.Type as PointerType;
        if (pt == null && !d.Type.IsError) Error(at, $"{proc}: a pointer variable is required (found {d.Type})");
        VariantPart? part = (pt?.Target as RecordType)?.VariantPart;
        while (Accept(T.Comma))
        {
            var ct = Cur;
            var c = Constant();
            if (pt == null || c.Type.IsError) continue;
            if (part == null) { Error(ct, $"{proc}: the record type has no (further) variant part for the tag value"); part = null; continue; }
            if (!c.Type.IsOrdinal || !TypeRules.Compatible(c.Type, part.TagType)) { Error(ct, $"{proc}: tag value of type {c.Type} does not match the tag type {part.TagType}"); continue; }
            var v = part.Find(c.Ord);
            if (v == null) { Error(ct, $"{proc}: no variant has the tag value {Format.OrdText(part.TagType, c.Ord)}"); continue; }
            if (part.Tag != null) tags.Add((part.Tag.Offset, c.Ord));
            values.Add(c.Ord);
            part = v.Nested;
        }
        Expect(T.RParen);
        if (pt == null) return At(new EmptyStmt(), t);
        CheckThreat(d, at, proc + " on");
        if (isNew) return At(new NewStmt(d, pt.Target, tags.ToArray(), values.ToArray()), t);
        return At(new DisposeStmt(d, tags.ToArray(), values.ToArray()), t);
    }

    // ------------------------------------------------------------------ pack, unpack

    Stmt PackUnpack(Token t, bool unpack)
    {
        string proc = unpack ? "unpack" : "pack";
        if (!Expect(T.LParen)) return At(new EmptyStmt(), t);
        Designator a, z; Expr i;
        Token at, zt, it;
        if (!unpack)
        {
            at = Cur; a = VariableOrError(); Expect(T.Comma);
            it = Cur; i = Expression(); Expect(T.Comma);
            zt = Cur; z = VariableOrError();
        }
        else
        {
            zt = Cur; z = VariableOrError(); Expect(T.Comma);
            at = Cur; a = VariableOrError(); Expect(T.Comma);
            it = Cur; i = Expression();
        }
        Expect(T.RParen);
        if (a.Type.IsError || z.Type.IsError || i.Type.IsError) return At(new EmptyStmt(), t);
        if (a.Type is not ArrayType aa || aa.Packed) { Error(at, $"{proc}: an unpacked array variable is required (found {a.Type})"); return At(new EmptyStmt(), t); }
        if (z.Type is not ArrayType zz || !zz.Packed) { Error(zt, $"{proc}: a packed array variable is required (found {z.Type})"); return At(new EmptyStmt(), t); }
        if (!ReferenceEquals(aa.Elem, zz.Elem)) { Error(zt, $"{proc}: the component types of the arrays differ ({aa.Elem} and {zz.Elem})"); return At(new EmptyStmt(), t); }
        if (!i.Type.IsOrdinal || !TypeRules.Compatible(i.Type, aa.Index)) { Error(it, $"{proc}: the index {i.Type} is not compatible with the index type {aa.Index} of the unpacked array"); return At(new EmptyStmt(), t); }
        if (zz.Count > aa.Count) { Error(zt, $"{proc}: the packed array has more components ({zz.Count}) than the unpacked array ({aa.Count})"); return At(new EmptyStmt(), t); }
        return At(new PackStmt(a, i, z, unpack), t);
    }

    // ------------------------------------------------------------------ standard functions

    Expr StdFunction(string name, Token t)
    {
        if (name is "eof" or "eoln")
        {
            Expr f;
            if (Accept(T.LParen))
            {
                var at = Cur;
                f = Expression();
                Expect(T.RParen);
                if (!CheckFileVar(f, at, name)) return ErrorExpr(t);
                if (name == "eoln" && f.Type is FileType { IsText: false }) { Error(at, "eoln applies to text files only"); return ErrorExpr(t); }
            }
            else f = StdFile(inputSym, t);
            return At(new EofNode(f, name == "eoln"), t);
        }
        if (!Expect(T.LParen)) return ErrorExpr(t);
        var argTok = Cur;
        var a = Expression();
        if (Is(T.Comma)) { SyntaxError($"'{name}' takes exactly one parameter"); while (Accept(T.Comma)) Expression(); }
        Expect(T.RParen);
        if (a.Type.IsError) return ErrorExpr(t);
        Expr Bad(string want)
        {
            Error(argTok, $"the parameter of '{name}' must be {want} (found {a.Type})");
            return ErrorExpr(t);
        }
        switch (name)
        {
            case "abs":
            case "sqr":
                {
                    var fn = name == "abs" ? Fn.Abs : Fn.Sqr;
                    if (a.Type.IsInteger) return At(new IntFn(fn, a, IntegerType.T), t);
                    if (a.Type.IsReal) return At(new RealFn(fn, a), t);
                    return Bad("integer or real");
                }
            case "sin": case "cos": case "exp": case "ln": case "sqrt": case "arctan":
                {
                    if (!a.Type.IsNumeric) return Bad("integer or real");
                    var fn = name switch { "sin" => Fn.Sin, "cos" => Fn.Cos, "exp" => Fn.Exp, "ln" => Fn.Ln, "sqrt" => Fn.Sqrt, _ => Fn.Arctan };
                    return At(new RealFn(fn, ToReal(a)), t);
                }
            case "odd":
                if (!a.Type.IsInteger) return Bad("an integer");
                if (a is ConstOrd co) return At(new ConstOrd((co.V & 1) != 0 ? 1 : 0, EnumType.Boolean), t);
                return At(new IntFn(Fn.Odd, a, EnumType.Boolean), t);
            case "trunc": case "round":
                if (!a.Type.IsReal) return Bad("real");      // Revised Report 11.1.3: x is real (Pascal-P and Pascal 6000 reject integers)
                return At(new IntFn(name == "trunc" ? Fn.Trunc : Fn.Round, ToReal(a), IntegerType.T), t);
            case "ord":
                if (!a.Type.IsOrdinal) return Bad("of an ordinal type");
                if (a is ConstOrd c1) return At(new ConstOrd(c1.V, IntegerType.T), t);
                return At(new IntFn(Fn.Ord, a, IntegerType.T), t);
            case "chr":
                if (!a.Type.IsInteger) return Bad("an integer");
                if (a is ConstOrd c2)
                {
                    if (c2.V < 0 || c2.V > 255) { Error(argTok, $"chr({c2.V}): no character with this ordinal value (0..255)"); return ErrorExpr(t); }
                    return At(new ConstOrd(c2.V, CharType.T), t);
                }
                return At(new IntFn(Fn.Chr, a, CharType.T), t);
            case "succ": case "pred":
                {
                    if (!a.Type.IsOrdinal) return Bad("of an ordinal type");
                    var h = a.Type.Host;
                    if (a is ConstOrd c3)
                    {
                        long v = name == "succ" ? c3.V + 1 : c3.V - 1;
                        if (v < h.Lo || v > h.Hi) { Error(argTok, $"{name}({Format.OrdText(h, c3.V)}): the value has no {(name == "succ" ? "successor" : "predecessor")}"); return ErrorExpr(t); }
                        return At(new ConstOrd(v, h), t);
                    }
                    return At(new IntFn(name == "succ" ? Fn.Succ : Fn.Pred, a, h), t);
                }
        }
        Error(t, $"unknown standard function '{name}'");
        return ErrorExpr(t);
    }
}
