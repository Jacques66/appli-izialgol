using System.Text;
using Studio.Pascal.Exec;
using Studio.Pascal.Runtime;
using Studio.Pascal.Syntax;

namespace Studio.Pascal.Semantics;

public sealed partial class Compiler
{
    Expr ErrorExpr(Token t) => At(new ConstOrd(0, ErrorType.T), t);
    Designator ErrorDesignator(Token t) => At(new VarNode(0, 0) { Type = ErrorType.T, Desc = t.Raw }, t);

    /// <summary>Source text of the tokens [from, p) (for messages).</summary>
    string TextOf(int from)
    {
        var sb = new StringBuilder();
        for (int k = from; k < p; k++)
        {
            var t = toks[k];
            string s = t.Kind == T.Str ? t.Raw : t.Raw;
            if (sb.Length > 0 && (char.IsLetterOrDigit(sb[^1]) && s.Length > 0 && char.IsLetterOrDigit(s[0]))) sb.Append(' ');
            sb.Append(s);
            if (sb.Length > 60) { sb.Append("..."); break; }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ variables

    bool IsVariableStart() => Is(T.Ident) && Lookup(Cur.Text) is VarSym or WithFieldSym;

    /// <summary>Variable required (var parameter, read, new...): any expression is parsed, then checked.</summary>
    Designator VariableOrError(string? what = null)
    {
        var t = Cur;
        var e = Expression();
        if (e is Designator d && !e.Type.IsError) return d;
        if (!e.Type.IsError) Error(t, what ?? "a variable is required here");
        return ErrorDesignator(t);
    }

    Designator Variable()
    {
        int start = p;
        var t = Advance();
        var sym = Lookup(t.Text);
        Designator d;
        switch (sym)
        {
            case VarSym v:
                if (v.NotInHeading && !v.ReportedHeading)
                {
                    v.ReportedHeading = true;
                    Error(t, $"'{v.Name}' must be listed in the program heading, e.g. program p(input, output);");
                }
                collectVars?.Add(v);
                int hops = rc.Level - v.Level;
                d = v.IsRef ? new RefVarNode(hops, v.Slot, 0) : new VarNode(hops, v.Slot);
                d.Type = v.Type; d.Desc = t.Raw; d.RootSymbol = v; d.IsEntire = true;
                At(d, t);
                break;
            case WithFieldSym w:
                {
                    var rec = At(new RefVarNode(0, w.Slot, 0) { Type = w.Record, Desc = w.RecordDesc, PackedComponent = w.PackedRecord }, t);
                    d = FieldAccess(rec, w.Field, t, w.Field.Name);
                    break;
                }
            default:
                return ErrorDesignator(t);
        }
        return Selectors(d, start);
    }

    Designator FieldAccess(Expr rec, Field fl, Token t, string desc)
    {
        var checks = new List<VariantCheck>();
        for (var v = fl.Variant; v != null; v = v.Owner.Parent)
            if (v.Owner.Tag != null)
                checks.Add(new VariantCheck(v.Owner.Tag.Offset, v.Labels, v.Owner.Tag, ""));
        var rt = rec.Type as RecordType;
        var d = At(new FieldNode(rec, fl, checks.ToArray()), t);
        d.Desc = desc;
        d.PackedComponent = (rt?.Packed ?? false) || (rec is Designator rd && rd.PackedComponent);
        d.TagField = fl.TagOf != null ? fl : null;
        d.RootSymbol = (rec as Designator)?.RootSymbol;
        return d;
    }

    Designator Selectors(Designator d, int start)
    {
        while (true)
        {
            var t = Cur;
            if (Is(T.LBrack))
            {
                Advance();
                do
                {
                    var it = Cur;
                    var ix = Expression();
                    if (d.Type is not ArrayType at)
                    {
                        if (!d.Type.IsError) Error(t, $"{d.Desc} is not an array (type {d.Type}): it cannot be indexed");
                        d = ErrorDesignator(t);
                        continue;
                    }
                    if (!ix.Type.IsError && (!ix.Type.IsOrdinal || !TypeRules.Compatible(ix.Type, at.Index)))
                        Error(it, $"index of type {ix.Type} is not compatible with the index type {at.Index} of {d.Desc}");
                    else if (ix is ConstOrd c && (c.V < at.Index.Lo || c.V > at.Index.Hi))
                        Error(it, $"index {Format.OrdText(at.Index, c.V)} out of bounds {Format.OrdText(at.Index, at.Index.Lo)}..{Format.OrdText(at.Index, at.Index.Hi)}");
                    var nd = At(new IndexNode(d, ix, at), t);
                    nd.PackedComponent = at.Packed || d.PackedComponent;
                    nd.RootSymbol = d.RootSymbol;
                    d = nd;
                } while (Accept(T.Comma));
                Expect(T.RBrack);
                d.Desc = TextOf(start);
                continue;
            }
            if (Is(T.Dot) && Peek().Kind == T.Ident)
            {
                Advance();
                var ft = Advance();
                if (d.Type is not RecordType rt)
                {
                    if (!d.Type.IsError) Error(t, $"{d.Desc} is not a record (type {d.Type}): it has no field '{ft.Raw}'");
                    d = ErrorDesignator(ft);
                    continue;
                }
                var fl = rt.Find(ft.Text);
                if (fl == null) { Error(ft, $"the record type of {d.Desc} has no field '{ft.Raw}'"); d = ErrorDesignator(ft); continue; }
                d = FieldAccess(d, fl, ft, TextOf(start));
                continue;
            }
            if (Is(T.Caret))
            {
                Advance();
                if (d.Type is PointerType pt)
                {
                    var nd = At(new DerefNode(d, pt.Target), t);
                    nd.Desc = TextOf(start);
                    d = nd;
                }
                else if (d.Type is FileType ft)
                {
                    var nd = At(new BufferNode(d, ft.Elem), t);
                    nd.Desc = TextOf(start);
                    d = nd;
                }
                else
                {
                    if (!d.Type.IsError) Error(t, $"{d.Desc} is neither a pointer nor a file: '^' cannot be applied");
                    d = ErrorDesignator(t);
                }
                continue;
            }
            return d;
        }
    }

    // ------------------------------------------------------------------ expressions

    static bool IsRelOp(T k) => k is T.Eq or T.Ne or T.Lt or T.Le or T.Gt or T.Ge or T.In;

    Expr Expression()
    {
        var a = SimpleExpression();
        if (!IsRelOp(Cur.Kind)) return a;
        var opTok = Advance();
        var b = SimpleExpression();
        var r = Compare(opTok, a, b);
        if (IsRelOp(Cur.Kind)) SyntaxError("relational operators cannot be chained: use parentheses, e.g. (a < b) and (b < c)");
        return r;
    }

    Expr SimpleExpression()
    {
        var t = Cur;
        int sign = 0;
        if (Accept(T.Plus)) sign = 1;
        else if (Accept(T.Minus)) sign = -1;
        var a = Term();
        if (sign != 0)
        {
            if (!a.Type.IsNumeric && !a.Type.IsError) Error(t, $"sign applied to a non-numeric operand (type {a.Type})");
            else if (sign < 0)
            {
                if (a is ConstOrd c) a = At(new ConstOrd(-c.V, IntegerType.T), t);
                else if (a is ConstReal cr) a = At(new ConstReal(-cr.V), t);
                else if (a.Type.IsInteger) a = At(new IntNeg(a), t);
                else if (a.Type.IsReal) a = At(new RealNeg(a), t);
            }
        }
        while (Is(T.Plus) || Is(T.Minus) || Is(T.Or))
        {
            var opTok = Advance();
            if (Is(T.Plus) || Is(T.Minus)) SyntaxError("two consecutive operators: a sign may only start an expression (use parentheses)");
            var b = Term();
            a = Arith(opTok, opTok.Kind switch { T.Plus => Op.Add, T.Minus => Op.Sub, _ => Op.Or }, a, b);
        }
        return a;
    }

    Expr Term()
    {
        var a = Factor();
        while (Cur.Kind is T.Star or T.Slash or T.Div or T.Mod or T.And)
        {
            var opTok = Advance();
            if (Is(T.Plus) || Is(T.Minus)) SyntaxError("a sign may only start an expression: write the operand in parentheses, e.g. a * (-b)");
            var b = Factor();
            a = Arith(opTok, opTok.Kind switch { T.Star => Op.Mul, T.Slash => Op.RDiv, T.Div => Op.Div, T.Mod => Op.Mod, _ => Op.And }, a, b);
        }
        return a;
    }

    Expr ToReal(Expr e) => e is ConstOrd c ? At(new ConstReal(c.V), Tok(e)) : e.Type.IsReal ? e : new IntToReal(e);
    static Token Tok(Node n) => new() { Line = n.Line, Col = n.Col };

    Expr Arith(Token t, Op op, Expr a, Expr b)
    {
        var ta = a.Type; var tb = b.Type;
        if (ta.IsError || tb.IsError) return ErrorExpr(t);
        string opName = t.Raw.ToLowerInvariant();
        Expr Bad()
        {
            Error(t, $"operator '{opName}' cannot be applied to operands of types {ta} and {tb}");
            return ErrorExpr(t);
        }
        switch (op)
        {
            case Op.Add: case Op.Sub: case Op.Mul:
                if (ta.IsInteger && tb.IsInteger)
                {
                    if (a is ConstOrd ca && b is ConstOrd cb)
                    {
                        Int128 r = op == Op.Add ? (Int128)ca.V + cb.V : op == Op.Sub ? (Int128)ca.V - cb.V : (Int128)ca.V * cb.V;
                        if (r > RT.MaxInt || r < -RT.MaxInt) { Error(t, "integer overflow in a constant expression (the result exceeds maxint)"); return ErrorExpr(t); }
                        return At(new ConstOrd((long)r, IntegerType.T), t);
                    }
                    return At(new IntBin(op, a, b), t);
                }
                if (ta.IsNumeric && tb.IsNumeric) return At(new RealBin(op, ToReal(a), ToReal(b)), t);
                if (ta is SetType sa && tb is SetType sb)
                {
                    if (!TypeRules.Compatible(ta, tb)) return Bad();
                    return At(new SetBin(op, a, b, sa.Base != null ? ta : tb), t);
                }
                return Bad();
            case Op.RDiv:
                if (ta.IsNumeric && tb.IsNumeric) return At(new RealBin(Op.RDiv, ToReal(a), ToReal(b)), t);
                if (ta.IsInteger || tb.IsInteger) return Bad();
                return Bad();
            case Op.Div: case Op.Mod:
                if (ta.IsInteger && tb.IsInteger)
                {
                    if (b is ConstOrd cz && cz.V == 0) Error(t, $"division by zero ({opName} 0)");
                    return At(new IntBin(op, a, b), t);
                }
                if (ta.IsNumeric && tb.IsNumeric)
                {
                    Error(t, $"'{opName}' applies to integers only (use '/' for real division)");
                    return ErrorExpr(t);
                }
                return Bad();
            default:
                if (ta.IsBoolean && tb.IsBoolean) return At(new BoolBin(op == Op.And, a, b), t);
                return Bad();
        }
    }

    Expr Compare(Token t, Expr a, Expr b)
    {
        var ta = a.Type; var tb = b.Type;
        var op = t.Kind switch { T.Eq => Op.Eq, T.Ne => Op.Ne, T.Lt => Op.Lt, T.Le => Op.Le, T.Gt => Op.Gt, T.Ge => Op.Ge, _ => Op.In };
        if (ta.IsError || tb.IsError) return At(new ConstOrd(0, EnumType.Boolean), t);
        string opName = t.Raw.ToLowerInvariant();
        Expr Bad(string? why = null)
        {
            Error(t, why ?? $"operands of types {ta} and {tb} cannot be compared with '{opName}'");
            return At(new ConstOrd(0, EnumType.Boolean), t);
        }
        if (op == Op.In)
        {
            if (tb is not SetType sb) return Bad($"the right operand of 'in' must be a set (found {tb})");
            if (!ta.IsOrdinal) return Bad($"the left operand of 'in' must be of an ordinal type (found {ta})");
            if (sb.Base != null && !ReferenceEquals(ta.Host, sb.Base.Host)) return Bad($"the type {ta} of the left operand of 'in' does not match the base type {sb.Base} of the set");
            return At(new InNode(a, b), t);
        }
        if (ta.IsNumeric && tb.IsNumeric)
        {
            if (ta.IsReal || tb.IsReal) return At(new RealCmp(op, ToReal(a), ToReal(b)), t);
            return At(new OrdCmp(op, a, b), t);
        }
        if (ta.IsOrdinal && tb.IsOrdinal)
        {
            if (!ReferenceEquals(ta.Host, tb.Host)) return Bad();
            return At(new OrdCmp(op, a, b), t);
        }
        if (ta.IsString && tb.IsString)
        {
            if (ta.StringLength != tb.StringLength) return Bad($"strings of different lengths ({ta.StringLength} and {tb.StringLength}) cannot be compared");
            return At(new StrCmp(op, a, b, ta.StringLength), t);
        }
        if (ta is SetType && tb is SetType)
        {
            if (!TypeRules.Compatible(ta, tb)) return Bad();
            if (op is Op.Lt or Op.Gt) return Bad("'<' and '>' are not defined for sets (use '<=' and '>=' for inclusion)");
            return At(new SetCmp(op, a, b), t);
        }
        if ((ta.Kind is TK.Pointer or TK.Nil) && (tb.Kind is TK.Pointer or TK.Nil))
        {
            if (!TypeRules.Compatible(ta, tb)) return Bad();
            if (op is not (Op.Eq or Op.Ne)) return Bad("pointers can only be compared with '=' and '<>'");
            return At(new PtrCmp(op == Op.Eq, a, b), t);
        }
        return Bad();
    }

    Expr Factor()
    {
        var t = Cur;
        switch (t.Kind)
        {
            case T.Int: Advance(); return At(new ConstOrd(t.IntVal, IntegerType.T), t);
            case T.Real: Advance(); return At(new ConstReal(t.RealVal), t);
            case T.Str:
                Advance();
                if (t.Text.Length == 1) return At(new ConstOrd(t.Text[0], CharType.T), t);
                return At(new ConstStr(t.Text, StringType(t.Text.Length)), t);
            case T.Nil: Advance(); return At(new ConstObj(null, NilType.T), t);
            case T.LParen:
                {
                    Advance();
                    var e = Expression();
                    Expect(T.RParen);
                    if (e is Designator) e = new Paren(e);
                    return e;
                }
            case T.Not:
                {
                    Advance();
                    var e = Factor();
                    if (!e.Type.IsBoolean) { if (!e.Type.IsError) Error(t, $"'not' requires a boolean operand (found {e.Type})"); return ErrorExpr(t); }
                    if (e is ConstOrd c) return At(new ConstOrd(c.V ^ 1, EnumType.Boolean), t);
                    return At(new BoolNot(e), t);
                }
            case T.LBrack: return SetConstructor();
            case T.Ident: return IdentFactor();
        }
        SyntaxError($"expression expected but {Describe(t)} found");
        if (!Is(T.Semi) && !Is(T.End) && !Is(T.EOF) && !Is(T.Then) && !Is(T.Do) && !Is(T.Else) && !Is(T.RParen)) Advance();
        return ErrorExpr(t);
    }

    Expr SetConstructor()
    {
        var t = Advance();
        var items = new List<(Expr, Expr?)>();
        PType? elem = null;
        bool allConst = true;
        if (!Is(T.RBrack))
        {
            do
            {
                var it = Cur;
                var lo = Expression();
                Expr? hi = null;
                if (Accept(T.DotDot)) hi = Expression();
                foreach (var e in hi == null ? new[] { lo } : new[] { lo, hi })
                {
                    if (e.Type.IsError) { elem = ErrorType.T; continue; }
                    if (!e.Type.IsOrdinal) { Error(it, $"set elements must be of an ordinal type (found {e.Type})"); elem = ErrorType.T; continue; }
                    if (elem == null) elem = e.Type.Host;
                    else if (!elem.IsError && !ReferenceEquals(elem, e.Type.Host)) { Error(it, $"set elements of different types ({elem} and {e.Type})"); elem = ErrorType.T; }
                    if (e is ConstOrd c)
                    {
                        long max = e.Type.IsChar ? 255 : TypeRules.MaxSetElement;
                        if (c.V < 0 || c.V > max) Error(it, $"set element {Format.OrdText(e.Type, c.V)} out of range 0..{max} (Pascal 6000 sets hold ordinal values 0..58)");
                    }
                    else allConst = false;
                }
                items.Add((lo, hi));
            } while (Accept(T.Comma));
        }
        Expect(T.RBrack);
        if (elem == null) return At(new ConstObj(PSet.Empty, SetType.EmptySet), t);
        if (elem.IsError) return ErrorExpr(t);
        var st = new SetType(elem, false);
        if (allConst)
        {
            var s = PSet.Empty;
            foreach (var (lo, hi) in items)
            {
                long a = ((ConstOrd)lo).V, b = hi == null ? a : ((ConstOrd)hi).V;
                if (a <= b && a >= 0 && b <= 255) s = s.With(a, b);
            }
            return At(new ConstObj(s, st), t);
        }
        return At(new SetCons(items.ToArray(), st, elem), t);
    }

    Expr IdentFactor()
    {
        var t = Cur;
        var sym = Lookup(t.Text);
        switch (sym)
        {
            case null:
                Advance();
                Error(t, $"'{t.Raw}' is not declared");
                if (Is(T.LParen)) SkipParens();
                while (Is(T.LBrack) || Is(T.Dot) || Is(T.Caret)) { if (Is(T.LBrack)) SkipBrackets(); else { Advance(); if (Is(T.Ident)) Advance(); } }
                return ErrorExpr(t);
            case ConstSym c:
                Advance();
                if (c.Str != null) return At(new ConstStr(c.Str, c.Type), t);
                if (c.Type.IsReal) return At(new ConstReal(c.Real), t);
                return At(new ConstOrd(c.Ord, c.Type), t);
            case VarSym or WithFieldSym:
                return Variable();
            case RoutineSym rs:
                {
                    Advance();
                    if (!rs.IsFunction) { Error(t, $"procedure '{t.Raw}' cannot be used in an expression (it yields no value)"); if (Is(T.LParen)) SkipParens(); return ErrorExpr(t); }
                    var call = RoutineCall(rs, t);
                    if (Is(T.Caret) || Is(T.LBrack)) SyntaxError("selectors cannot be applied to a function result");
                    return call ?? ErrorExpr(t);
                }
            case FormalRoutineSym fs:
                {
                    Advance();
                    if (!fs.IsFunction) { Error(t, $"procedure '{t.Raw}' cannot be used in an expression (it yields no value)"); if (Is(T.LParen)) SkipParens(); return ErrorExpr(t); }
                    return (Expr?)FormalCall(fs, t) ?? ErrorExpr(t);
                }
            case StdSym ss:
                Advance();
                if (ss.Kind == StdKind.Procedure)
                {
                    Error(t, $"standard procedure '{t.Raw}' cannot be used in an expression");
                    if (Is(T.LParen)) SkipParens();
                    return ErrorExpr(t);
                }
                return StdFunction(ss.Name, t);
            case TypeSym:
                Advance();
                Error(t, $"'{t.Raw}' is a type, not a value");
                if (Is(T.LParen)) SkipParens();
                return ErrorExpr(t);
        }
        Advance();
        return ErrorExpr(t);
    }

    void SkipParens()
    {
        int depth = 0;
        do
        {
            if (Is(T.LParen)) depth++;
            else if (Is(T.RParen)) depth--;
            else if (Is(T.EOF) || Is(T.Semi) || Is(T.End)) return;
            Advance();
        } while (depth > 0);
    }

    void SkipBrackets()
    {
        int depth = 0;
        do
        {
            if (Is(T.LBrack)) depth++;
            else if (Is(T.RBrack)) depth--;
            else if (Is(T.EOF) || Is(T.Semi) || Is(T.End)) return;
            Advance();
        } while (depth > 0);
    }

    // ------------------------------------------------------------------ calls

    CallNode? RoutineCall(RoutineSym rs, Token t)
    {
        var args = ActualParams(rs.Sig, t);
        if (args == null) return null;
        var call = At(new CallNode(rs.Info, rc.Level - (rs.Level - 1), args) { Name = t.Raw }, t);
        return call;
    }

    CallNode? FormalCall(FormalRoutineSym fs, Token t)
    {
        var clo = At(new FormalRoutineValue(rc.Level - fs.Level, fs.Slot), t);
        if (fs.Sig.HasList)
        {
            var args = ActualParams(fs.Sig, t);
            if (args == null) return null;
            return At(new CallNode(clo, fs.Sig, args, null) { Name = t.Raw }, t);
        }
        // 1974 form: the parameters are checked when the call is executed
        var exprs = new List<Expr>();
        if (Accept(T.LParen))
        {
            do
            {
                var at = Cur;
                var e = Expression();
                if (e.Type.ContainsFile) Error(at, "a file cannot be passed to a procedural parameter without parameter list (value parameters only)");
                if (e.Type.Kind == TK.Nil) Error(at, "nil cannot be passed to a procedural parameter without parameter list");
                exprs.Add(e);
            } while (Accept(T.Comma));
            Expect(T.RParen);
        }
        return At(new CallNode(clo, fs.Sig, Array.Empty<Arg>(), exprs.ToArray()) { Name = t.Raw }, t);
    }

    Arg[]? ActualParams(Signature sig, Token t)
    {
        var ps = sig.Params;
        var args = new List<Arg>();
        string who = $"'{t.Raw}'";
        if (!Is(T.LParen))
        {
            if (ps.Count > 0) { Error(t, $"{who} expects {ps.Count} parameter(s)"); return null; }
            return Array.Empty<Arg>();
        }
        Advance();
        int i = 0;
        bool ok = true;
        do
        {
            var at = Cur;
            if (i >= ps.Count)
            {
                Expression();
                if (i == ps.Count) Error(at, $"too many parameters: {who} expects {ps.Count}");
                ok = false; i++;
                continue;
            }
            var pi = ps[i];
            string pn = $"parameter {i + 1} ('{pi.Name}') of {who}";
            switch (pi.Kind)
            {
                case ParamKind.Value:
                    {
                        var e = Expression();
                        if (!TypeRules.AssignCompatible(pi.Type, e.Type))
                        {
                            Error(at, $"{pn}: a value of type {e.Type} is not assignment compatible with {pi.Type}" + StringHint(pi.Type, e.Type));
                            ok = false;
                        }
                        else args.Add(new Arg { Kind = pi.Type.IsStructured ? ArgKind.Struct : ArgKind.Value, E = Coerce(e, pi.Type, at), Size = pi.Size });
                        break;
                    }
                case ParamKind.Var:
                    {
                        var d = VariableOrError($"{pn} is a var parameter: the actual parameter must be a variable");
                        if (d.Type.IsError) { ok = false; break; }
                        if (!TypeRules.Same(d.Type, pi.Type))
                        { Error(at, $"{pn} is a var parameter of type {pi.Type}: the variable {d.Desc} has type {d.Type} (the types must be identical)"); ok = false; break; }
                        if (d.PackedComponent) { Error(at, $"{pn}: a component of a packed structure cannot be passed as a var parameter"); ok = false; break; }
                        if (d.TagField != null) { Error(at, $"{pn}: a tag field cannot be passed as a var parameter"); ok = false; break; }
                        CheckThreat(d, at, "passing as a var parameter");
                        args.Add(new Arg { Kind = ArgKind.Var, E = d });
                        break;
                    }
                default:
                    {
                        bool wantFunc = pi.Kind == ParamKind.Function;
                        var e = RoutineArgument(pi, wantFunc, pn, at);
                        if (e == null) ok = false;
                        else args.Add(new Arg { Kind = ArgKind.Value, E = e });
                        break;
                    }
            }
            i++;
        } while (Accept(T.Comma));
        Expect(T.RParen);
        if (i < ps.Count) { Error(t, $"too few parameters: {who} expects {ps.Count}, {i} given"); ok = false; }
        return ok ? args.ToArray() : null;
    }

    Expr? RoutineArgument(ParamInfo pi, bool wantFunc, string pn, Token at)
    {
        string kind = wantFunc ? "function" : "procedure";
        if (!Is(T.Ident))
        {
            Expression();
            Error(at, $"{pn} is a {kind} parameter: the name of a {kind} is required");
            return null;
        }
        var nt = Advance();
        var sym = Lookup(nt.Text);
        Signature? actual = null;
        Expr? value = null;
        switch (sym)
        {
            case RoutineSym rs:
                actual = rs.Sig;
                value = At(new RoutineValue(rs.Info, rc.Level - (rs.Level - 1)), nt);
                break;
            case FormalRoutineSym fs:
                actual = fs.Sig;
                value = At(new FormalRoutineValue(rc.Level - fs.Level, fs.Slot), nt);
                break;
            case StdSym:
                Error(nt, $"{pn}: the standard {(((StdSym)sym).Kind == StdKind.Function ? "function" : "procedure")} '{nt.Raw}' cannot be passed as a parameter (declare a {kind} that calls it)");
                return null;
            default:
                Error(nt, $"{pn} is a {kind} parameter: '{nt.Raw}' is not a {kind}");
                if (Is(T.LParen)) SkipParens();
                return null;
        }
        if (Is(T.LParen) || Is(T.LBrack) || Is(T.Dot) || IsRelOp(Cur.Kind) || Cur.Kind is T.Plus or T.Minus or T.Star or T.Slash or T.Div or T.Mod or T.And or T.Or)
        {
            Error(Cur, $"{pn} is a {kind} parameter: only the name of the {kind} may be given");
            Expression();
            return null;
        }
        if (actual.IsFunction != wantFunc)
        {
            Error(nt, $"{pn}: '{nt.Raw}' is a {(actual.IsFunction ? "function" : "procedure")}, a {kind} is required");
            return null;
        }
        var why = Congruity(pi.Sig!, actual);
        if (why != null) { Error(nt, $"{pn}: '{nt.Raw}' does not match the parameter heading ({why})"); return null; }
        return value;
    }

    /// <summary>Congruity of the heading of a procedural parameter with an actual routine; null when congruent.</summary>
    static string? Congruity(Signature formal, Signature actual)
    {
        if (formal.IsFunction && actual.IsFunction && !TypeRules.Same(formal.Result!, actual.Result!))
            return $"result type {actual.Result} instead of {formal.Result}";
        if (!formal.HasList)
        {
            if (!actual.HasList) return null;
            var bad = actual.Params.FirstOrDefault(x => x.Kind != ParamKind.Value);
            return bad == null ? null : $"a routine passed to a parameter without parameter list may only have value parameters ('{bad.Name}' is not)";
        }
        if (!actual.HasList) return "its parameters are not specified";
        if (formal.Params.Count != actual.Params.Count) return $"{actual.Params.Count} parameter(s) instead of {formal.Params.Count}";
        for (int i = 0; i < formal.Params.Count; i++)
        {
            var a = formal.Params[i]; var b = actual.Params[i];
            if (a.Kind != b.Kind) return $"parameter {i + 1} is not of the same kind (value, var, procedure or function)";
            if (a.Kind is ParamKind.Value or ParamKind.Var)
            {
                if (!TypeRules.Same(a.Type, b.Type)) return $"parameter {i + 1} has type {b.Type} instead of {a.Type}";
            }
            else
            {
                var w = Congruity(a.Sig!, b.Sig!);
                if (w != null) return $"parameter {i + 1}: {w}";
            }
        }
        return null;
    }
}

/// <summary>Parenthesised variable: a value, no longer a variable (cannot be a var parameter or assigned).</summary>
public sealed class Paren : Expr
{
    readonly Expr e;
    public Paren(Expr e) { this.e = e; Type = e.Type; At<Paren>(e); }
    public override long Ord(Frame f) => e.Ord(f);
    public override double Real(Frame f) => e.Real(f);
    public override object? Obj(Frame f) => e.Obj(f);
    public override Ref Addr(Frame f) => e.Addr(f);
    public override Cell Value(Frame f) => e.Value(f);
}
