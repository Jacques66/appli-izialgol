using Studio.Algol68.Exec;
using Studio.Algol68.Runtime;
using Studio.Algol68.Syntax;

namespace Studio.Algol68.Semantics;

public sealed partial class Compiler
{
    /// <summary>Operator identification (RR 7.2): innermost range where a definition fits (firm operands).</summary>
    OpSym? IdentifyOp(string name, Mode?[] operands, Ast pos, bool report)
    {
        bool exists = false;
        for (var r0 = cur; r0 != null && !exists; r0 = r0.Parent) exists = r0.Ops.ContainsKey(name);
        if (!exists)
        {
            if (report)
                Error(pos, char.IsLetter(name[0]) ? $"'{name}' not declared: neither a known mode nor a known operator" : $"operator '{name}' not declared");
            return null;
        }
        if (operands.Any(m => m == null))
        {
            if (report) Error(pos, $"operand of '{name}' has no a priori mode (a display must be given a mode by a cast)");
            return null;
        }
        if (operands.Any(m => m == M.Any)) return null;
        for (var r = cur; r != null; r = r.Parent)
        {
            if (!r.Ops.TryGetValue(name, out var list)) continue;
            OpSym? best = null; int bestCost = int.MaxValue; int count = 0;
            foreach (var op in list)
            {
                if (op.Mode.Params.Length != operands.Length) continue;
                int cost = 0; bool ok = true;
                for (int i = 0; i < operands.Length; i++)
                {
                    var steps = FindCoercion(operands[i]!, op.Mode.Params[i], Strength.Firm);
                    if (steps == null) { ok = false; break; }
                    cost += steps.Count;
                }
                if (!ok) continue;
                count++;
                if (cost < bestCost) { best = op; bestCost = cost; }
            }
            if (best != null) return best;
        }
        if (report)
        {
            string ms = string.Join(" and ", operands.Select(m => m!.ToString()));
            Error(pos, operands.Length == 2
                ? $"operator '{name}' not defined for operands of modes {ms}"
                : $"monadic operator '{name}' not defined for an operand of mode {ms}");
        }
        return null;
    }

    OpSym? IdentifyDyadic(DyadicAst d, bool report) =>
        IdentifyOp(d.Op, new[] { Apriori(d.Left), Apriori(d.Right) }, d, report);

    OpSym? IdentifyMonadic(MonadicAst m, bool report) =>
        IdentifyOp(m.Op, new[] { Apriori(m.Operand) }, m, report);

    (Node, Mode?) GenDyadic(DyadicAst d)
    {
        var op = IdentifyDyadic(d, true);
        if (op == null)
        {
            Gen(d.Left, M.Any, Strength.Strong); Gen(d.Right, M.Any, Strength.Strong);
            return (new ConstNode(default).At<ConstNode>(d), M.Any);
        }
        var l = Gen(d.Left, op.Mode.Params[0], Strength.Firm);
        var r = Gen(d.Right, op.Mode.Params[1], Strength.Firm);
        CheckTransient(l, d.Left, "an operand"); CheckTransient(r, d.Right, "an operand");
        return (ApplyOp(op, new[] { l, r }, d), op.Mode.Result);
    }

    (Node, Mode?) GenMonadic(MonadicAst m)
    {
        var op = IdentifyMonadic(m, true);
        if (op == null)
        {
            Gen(m.Operand, M.Any, Strength.Strong);
            return (new ConstNode(default).At<ConstNode>(m), M.Any);
        }
        var a = Gen(m.Operand, op.Mode.Params[0], Strength.Firm);
        CheckTransient(a, m.Operand, "an operand");
        return (ApplyOp(op, new[] { a }, m), op.Mode.Result);
    }

    Node ApplyOp(OpSym op, Node[] args, Ast pos)
    {
        Node n;
        if (op.Factory != null)
        {
            n = op.Factory(args);
        }
        else
        {
            NoteFree(op.Range);
            if (!op.Proc!.Declared && op.Range.Routine == routine && suppressDeclCheck == 0)
                Error(pos, $"operator '{op.Name}' used before its declaration");
            var slot = new SlotNode(HopsTo(op.Range), op.Proc.Slot) { Name = "OP " + op.Name, NeedsObj = true };
            slot.SetPos(pos);
            var modes = new Mode[args.Length];
            for (int i = 0; i < args.Length; i++) modes[i] = IsFresh(args[i]) ? M.Int : op.Mode.Params[i];
            n = new CallNode(slot, args, modes, cur.Level);
        }
        n.SetPos(pos);
        n.Mode = op.Mode.Result;
        return n;
    }
}
