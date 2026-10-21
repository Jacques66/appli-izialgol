using Studio.Pascal.Exec;
using Studio.Pascal.Runtime;
using Studio.Pascal.Syntax;

namespace Studio.Pascal.Semantics;

public sealed partial class Compiler
{
    static bool StartsStatement(T k) => k is T.Ident or T.Begin or T.If or T.While or T.Repeat or T.For or T.Case or T.With or T.Goto or T.Int;

    Sequence CompoundStatement(bool topLevel = false)
    {
        var beginTok = Cur;
        Expect(T.Begin);
        var ctx = new SeqCtx { Parent = seq };
        seq = ctx;
        var items = StatementSequence(T.End);
        if (!Accept(T.End))
        {
            SyntaxError($"'end' expected but {Describe(Cur)} found");
        }
        seq = ctx.Parent;
        return At(new Sequence(items.ToArray(), ctx.Labels, ctx.NonLocal), beginTok);
    }

    List<Stmt> StatementSequence(T closer)
    {
        var items = new List<Stmt>();
        while (true)
        {
            if (Is(closer) || Is(T.EOF)) break;
            items.Add(Statement(true, items.Count));
            if (Accept(T.Semi)) continue;
            if (Is(closer) || Is(T.EOF)) break;
            if (StartsStatement(Cur.Kind))
            {
                SyntaxError($"';' expected between statements but {Describe(Cur)} found");
                continue;
            }
            SyntaxError($"';' or '{Lexer.Spelling(closer)}' expected but {Describe(Cur)} found");
            SkipTo(T.Semi, T.End, T.Until, T.Begin, T.Procedure, T.Function, closer);
            if (Accept(T.Semi)) continue;
            if (Is(T.Begin)) continue;
            break;
        }
        return items;
    }

    Stmt Statement(bool inSequence, int index = 0)
    {
        LabelSym? lab = null;
        if (Is(T.Int) && Peek().Kind == T.Colon)
        {
            var lt = Advance(); Advance();
            if (!rc.Labels.TryGetValue(lt.IntVal, out lab))
                Error(lt, $"label {lt.IntVal} is not declared in the label declaration part of this block");
            else if (lab.Defined) { Error(lt, $"label {lt.IntVal} prefixes more than one statement"); lab = null; }
        }
        else if (Is(T.Int))
        {
            SyntaxError($"statement expected but {Describe(Cur)} found (a label must be followed by ':')");
            Advance();
        }
        if (lab != null && !inSequence)
        {
            var ctx = new SeqCtx { Parent = seq };
            seq = ctx;
            Define(lab, ctx, 0);
            var start = Cur;
            var s = Unlabelled();
            seq = ctx.Parent;
            return At(new Sequence(new[] { s }, ctx.Labels, ctx.NonLocal), start);
        }
        if (lab != null) Define(lab, seq!, index);
        return Unlabelled();
    }

    void Define(LabelSym lab, SeqCtx ctx, int index)
    {
        lab.Defined = true;
        lab.Seq = ctx;
        ctx.Labels[lab.Info] = index;
        if (lab.UsedNonLocally) ctx.NonLocal = true;
    }

    Stmt Unlabelled()
    {
        var t = Cur;
        switch (t.Kind)
        {
            case T.Begin: return CompoundStatement();
            case T.If: return IfStatement();
            case T.While: return WhileStatement();
            case T.Repeat: return RepeatStatement();
            case T.For: return ForStatement();
            case T.Case: return CaseStatement();
            case T.With: return WithStatement();
            case T.Goto: return GotoStatement();
            case T.Ident: return AssignmentOrCall();
            case T.Semi: case T.End: case T.Else: case T.Until:
                return At(new EmptyStmt(), t);
        }
        SyntaxError($"statement expected but {Describe(t)} found");
        SkipTo(T.Semi, T.End, T.Until, T.Else);
        return At(new EmptyStmt(), t);
    }

    // ------------------------------------------------------------------ assignment and procedure call

    Stmt AssignmentOrCall()
    {
        var t = Cur;
        var sym = Lookup(t.Text);
        switch (sym)
        {
            case null:
                Error(t, $"'{t.Raw}' is not declared");
                Advance();
                SkipTo(T.Semi, T.End, T.Else, T.Until);
                return At(new EmptyStmt(), t);
            case RoutineSym rs when Peek().Kind == T.Assign:
                {
                    Advance();
                    var target = ResultTarget(rs, t);
                    return Assignment(target, t);
                }
            case RoutineSym rs:
                {
                    Advance();
                    if (rs.IsFunction) Error(t, $"function '{t.Raw}' cannot be called as a procedure (its result must be used)");
                    var call = RoutineCall(rs, t);
                    return call == null ? At(new EmptyStmt(), t) : At(new CallStmt(call), t);
                }
            case FormalRoutineSym fs:
                {
                    Advance();
                    if (Is(T.Assign)) { Error(t, $"cannot assign to the procedural parameter '{t.Raw}'"); SkipTo(T.Semi, T.End, T.Else, T.Until); return At(new EmptyStmt(), t); }
                    if (fs.IsFunction) Error(t, $"function '{t.Raw}' cannot be called as a procedure (its result must be used)");
                    var call = FormalCall(fs, t);
                    return call == null ? At(new EmptyStmt(), t) : At(new CallStmt(call), t);
                }
            case StdSym ss:
                {
                    Advance();
                    if (ss.Kind == StdKind.Function)
                    {
                        Error(t, $"standard function '{t.Raw}' cannot be used as a statement");
                        SkipTo(T.Semi, T.End, T.Else, T.Until);
                        return At(new EmptyStmt(), t);
                    }
                    return StdProcedure(ss.Name, t);
                }
            case VarSym or WithFieldSym:
                {
                    var d = Variable();
                    return Assignment(d, t);
                }
            case ConstSym:
                Error(t, $"'{t.Raw}' is a constant: it cannot be assigned");
                break;
            case TypeSym:
                Error(t, $"'{t.Raw}' is a type, not a variable or procedure");
                break;
        }
        Advance();
        SkipTo(T.Semi, T.End, T.Else, T.Until);
        return At(new EmptyStmt(), t);
    }

    Designator ResultTarget(RoutineSym rs, Token t)
    {
        for (var c = rc; c != null; c = c.Parent)
            if (ReferenceEquals(c.Sym, rs))
            {
                if (!rs.IsFunction) break;
                return At(new ResultNode(rc.Level - rs.Level, rs.Info.ResultSlot) { Type = rs.Info.Sig.Result!, Desc = "result of " + t.Raw }, t);
            }
        Error(t, rs.IsFunction ? $"the result of function '{t.Raw}' can only be assigned inside its own block" : $"'{t.Raw}' is a procedure: it cannot be assigned");
        return At(new ResultNode(0, 0) { Type = ErrorType.T }, t);
    }

    Stmt Assignment(Designator target, Token start)
    {
        var assignTok = Cur;
        if (!Expect(T.Assign))
        {
            SkipTo(T.Semi, T.End, T.Else, T.Until);
            return At(new EmptyStmt(), start);
        }
        var e = Expression();
        if (target.Type.ContainsFile && !target.Type.IsError)
        {
            Error(assignTok, "files (and structures containing files) cannot be assigned");
            return At(new EmptyStmt(), start);
        }
        CheckThreat(target, start, "assignment to");
        if (!TypeRules.AssignCompatible(target.Type, e.Type))
        {
            Error(assignTok, $"type mismatch: a value of type {e.Type} cannot be assigned to {target.Desc} of type {target.Type}" + StringHint(target.Type, e.Type));
            return At(new EmptyStmt(), start);
        }
        if (target.Type.IsError || e.Type.IsError) return At(new EmptyStmt(), start);
        var v = Coerce(e, target.Type, assignTok);
        if (target.Type.IsStructured) return At(new AssignStruct(target, v, target.Type.Size), start);
        if (target.TagField != null) return At(new AssignTag(target, v, target.TagField), start);
        return At(new AssignScalar(target, v), start);
    }

    static string StringHint(PType dst, PType src) =>
        dst.IsString && src.IsString ? $" (string lengths differ: {src.StringLength} and {dst.StringLength}; strings must have exactly the same length)" : "";

    /// <summary>Conversion of a value to the type of a variable: integer to real, range checks.</summary>
    Expr Coerce(Expr e, PType dst, Token at)
    {
        if (dst.IsReal && e.Type.IsInteger)
            return e is ConstOrd c ? At(new ConstReal(c.V), at) : new IntToReal(e);
        if (dst.IsOrdinal && e.Type.IsOrdinal)
        {
            if (e is ConstOrd c)
            {
                if (c.V < dst.Lo || c.V > dst.Hi) Error(at, $"constant {Format.OrdText(dst, c.V)} out of range {Format.OrdText(dst, dst.Lo)}..{Format.OrdText(dst, dst.Hi)}");
                return e;
            }
            if (e.Type.Lo >= dst.Lo && e.Type.Hi <= dst.Hi) return e;
            return new RangeCheck(e, dst);
        }
        if (dst is SetType ds && ds.Base != null && e.Type is SetType es && es.Base != null)
        {
            if (es.Base.Lo >= ds.Base.Lo && es.Base.Hi <= ds.Base.Hi && !(e is SetCons)) return e;
            return new SetRangeCheck(e, ds);
        }
        return e;
    }

    /// <summary>Assignments, var parameters and reads may not alter the control variable of an active for statement.</summary>
    void CheckThreat(Expr target, Token at, string what)
    {
        if (target is Designator d && d.IsEntire && d.RootSymbol is VarSym v && activeForVars.Contains(v))
            Error(at, $"{what} '{v.Name}', the control variable of an enclosing for statement, is not allowed");
    }

    // ------------------------------------------------------------------ structured statements

    Expr BoolExpr(string what)
    {
        var t = Cur;
        var e = Expression();
        if (!e.Type.IsBoolean && !e.Type.IsError) Error(t, $"the condition of {what} must be of type boolean (found {e.Type})");
        return e;
    }

    Stmt IfStatement()
    {
        var t = Advance();
        var c = BoolExpr("an if statement");
        Expect(T.Then);
        var th = Statement(false);
        Stmt? el = null;
        if (Accept(T.Else)) el = Statement(false);
        return At(new IfStmt(c, th, el), t);
    }

    Stmt WhileStatement()
    {
        var t = Advance();
        var c = BoolExpr("a while statement");
        Expect(T.Do);
        var body = Statement(false);
        return At(new WhileStmt(c, body), t);
    }

    Stmt RepeatStatement()
    {
        var t = Advance();
        var ctx = new SeqCtx { Parent = seq };
        seq = ctx;
        var items = StatementSequence(T.Until);
        seq = ctx.Parent;
        var ut = Cur;
        Expect(T.Until);
        var c = BoolExpr("a repeat statement");
        var body = At(new Sequence(items.ToArray(), ctx.Labels, ctx.NonLocal), t);
        var pos = At(new EmptyStmt(), ut);
        return At(new RepeatStmt(body, c, pos), t);
    }

    Stmt ForStatement()
    {
        var t = Advance();
        var vt = Cur;
        var sym = Is(T.Ident) ? Lookup(Cur.Text) : null;
        string name;
        bool entire = true;
        if (sym is VarSym or WithFieldSym)
        {
            var d = Variable();
            name = vt.Text;
            entire = d is VarNode && sym is VarSym;
        }
        else name = ExpectIdent();
        VarSym? v = null;
        if (!entire)
            Error(vt, $"the control variable of a for statement must be an entire variable declared in this block ({TextOf(IndexOfTok(vt))} is not)");
        else if (sym is VarSym vs)
        {
            if (vs.Level != rc.Level || vs.IsParam)
                Error(vt, $"the control variable '{vt.Raw}' must be a variable declared in the variable declaration part of this block");
            else if (!vs.Type.IsOrdinal && !vs.Type.IsError)
                Error(vt, $"the control variable '{vt.Raw}' must be of an ordinal type (found {vs.Type})");
            else if (activeForVars.Contains(vs))
                Error(vt, $"'{vt.Raw}' is already the control variable of an enclosing for statement");
            else v = vs;
        }
        else if (name.Length > 0 && sym is not WithFieldSym)
            Error(vt, sym == null ? $"'{vt.Raw}' is not declared" : $"the control variable '{vt.Raw}' must be a simple variable");
        Expect(T.Assign);
        var ft = Cur;
        var from = Expression();
        bool down = false;
        if (Accept(T.Downto)) down = true;
        else Expect(T.To);
        var tt = Cur;
        var to = Expression();
        Expect(T.Do);
        if (v != null)
        {
            if (!TypeRules.Compatible(v.Type, from.Type) || !from.Type.IsOrdinal) { if (!from.Type.IsError) Error(ft, $"the initial value (type {from.Type}) is not compatible with the control variable (type {v.Type})"); v = null; }
            else if (!TypeRules.Compatible(v.Type, to.Type) || !to.Type.IsOrdinal) { if (!to.Type.IsError) Error(tt, $"the final value (type {to.Type}) is not compatible with the control variable (type {v.Type})"); v = null; }
        }
        if (v != null) activeForVars.Add(v);
        var body = Statement(false);
        if (v != null) activeForVars.Remove(v);
        if (v == null) return At(new EmptyStmt(), t);
        var ctl = At(new VarNode(0, v.Slot) { Type = v.Type, Desc = vt.Raw, RootSymbol = v }, vt);
        return At(new ForStmt(ctl, from, to, down, body), t);
    }

    int IndexOfTok(Token t) => toks.IndexOf(t);

    Stmt CaseStatement()
    {
        var t = Advance();
        var st = Cur;
        var sel = Expression();
        if (!sel.Type.IsOrdinal && !sel.Type.IsError) Error(st, $"the case selector must be of an ordinal type (found {sel.Type})");
        Expect(T.Of);
        var map = new Dictionary<long, int>();
        var branches = new List<Stmt>();
        while (!Is(T.End) && !Is(T.EOF))
        {
            if (Accept(T.Semi)) continue;
            if (Is(T.Else) || (Is(T.Ident) && Cur.Text == "otherwise"))
            {
                SyntaxError($"'{Cur.Raw}' is not allowed in a case statement: Pascal has no default case (every selector value must appear as a case label)");
                Advance();
                Accept(T.Colon);
                StatementSequence(T.End);
                break;
            }
            int k = branches.Count;
            do
            {
                var lt = Cur;
                var c = Constant();
                if (c.Type.IsError || sel.Type.IsError) continue;
                if (!c.Type.IsOrdinal || !TypeRules.Compatible(c.Type, sel.Type)) Error(lt, $"case label of type {c.Type} is not compatible with the selector (type {sel.Type})");
                else if (!map.TryAdd(c.Ord, k)) Error(lt, $"case label {Format.OrdText(sel.Type, c.Ord)} appears more than once");
            } while (Accept(T.Comma));
            Expect(T.Colon);
            branches.Add(Statement(false));
            if (!Accept(T.Semi))
            {
                if (!Is(T.End)) SyntaxError($"';' or 'end' expected in the case statement but {Describe(Cur)} found");
                break;
            }
        }
        Expect(T.End);
        return At(new CaseStmt(sel, map, branches.ToArray()), t);
    }

    Stmt WithStatement()
    {
        var t = Advance();
        var recs = new List<(Designator d, int slot)>();
        int scopes = 0;
        do
        {
            var rt = Cur;
            if (!Is(T.Ident)) { SyntaxError($"record variable expected but {Describe(Cur)} found"); break; }
            var d = VariableOrError();
            if (d.Type is RecordType rec)
            {
                int slot = rc.Alloc(1);
                recs.Add((d, slot));
                var ws = new Scope(scope, rc.Level, isWith: true);
                foreach (var fl in rec.Fields)
                    ws.Syms[fl.Name] = new WithFieldSym { Name = fl.Name, Field = fl, Record = rec, Slot = slot, PackedRecord = rec.Packed || d.PackedComponent, RecordDesc = d.Desc };
                scope = ws;
                scopes++;
            }
            else if (!d.Type.IsError) Error(rt, $"{d.Desc} is not a record variable (type {d.Type})");
        } while (Accept(T.Comma));
        Expect(T.Do);
        var body = Statement(false);
        for (int k = 0; k < scopes; k++) scope = scope.Parent!;
        for (int k = recs.Count - 1; k >= 0; k--)
            body = At(new WithStmt(recs[k].d, recs[k].slot, body), t);
        return body;
    }

    Stmt GotoStatement()
    {
        var t = Advance();
        var lt = Cur;
        if (!Is(T.Int)) { SyntaxError($"label expected after goto but {Describe(Cur)} found"); return At(new EmptyStmt(), t); }
        Advance();
        LabelSym? l = null;
        for (var c = rc; c != null && l == null; c = c.Parent) c.Labels.TryGetValue(lt.IntVal, out l);
        if (l == null) { Error(lt, $"label {lt.IntVal} is not declared"); return At(new EmptyStmt(), t); }
        l.Used = true;
        if (l.Level == rc.Level)
        {
            rc.LocalGotos.Add((l, seq, lt));
            return At(new GotoStmt(l.Info, 0), t);
        }
        l.UsedNonLocally = true;
        return At(new GotoStmt(l.Info, rc.Level - l.Level), t);
    }
}
