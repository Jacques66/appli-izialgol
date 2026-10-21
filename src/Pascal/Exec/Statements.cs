using System.Runtime.CompilerServices;
using Studio.Pascal.Runtime;
using Studio.Pascal.Semantics;

namespace Studio.Pascal.Exec;

/// <summary>Statement. Exec returns 0, or 1 when a local goto is pending (Frame.PendingGoto).</summary>
public abstract class Stmt : Node
{
    /// <summary>The debugger may stop on this statement (false for compound statements).</summary>
    public bool Stoppable = true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Run(Frame f)
    {
        if (f.Ctx.Attention && Stoppable) f.Ctx.OnAttention(f, this);
        return Exec(f);
    }

    public abstract int Exec(Frame f);
}

public sealed class EmptyStmt : Stmt
{
    public EmptyStmt() { Stoppable = false; }
    public override int Exec(Frame f) => 0;
}

/// <summary>Statement sequence (compound statement, repeat body, labelled statement) with its labels.</summary>
public sealed class Sequence : Stmt
{
    readonly Stmt[] items;
    readonly Dictionary<LabelInfo, int>? labels;
    readonly bool catchNonLocal;
    public Sequence(Stmt[] items, Dictionary<LabelInfo, int>? labels, bool catchNonLocal)
    {
        this.items = items; this.labels = labels is { Count: > 0 } ? labels : null; this.catchNonLocal = catchNonLocal && this.labels != null;
        Stoppable = false;
    }

    public override int Exec(Frame f)
    {
        int pc = 0;
        var its = items;
        while (pc < its.Length)
        {
            int st;
            if (catchNonLocal)
            {
                try { st = its[pc].Run(f); }
                catch (GotoException g) when (ReferenceEquals(g.Target, f) && labels!.ContainsKey(g.Label))
                {
                    pc = labels[g.Label];
                    continue;
                }
            }
            else st = its[pc].Run(f);
            if (st != 0)
            {
                if (labels != null && f.PendingGoto != null && labels.TryGetValue(f.PendingGoto, out int k))
                {
                    f.PendingGoto = null;
                    pc = k;
                    continue;
                }
                return st;
            }
            pc++;
        }
        return 0;
    }
}

public sealed class GotoStmt : Stmt
{
    readonly LabelInfo label; readonly int hops;
    public GotoStmt(LabelInfo l, int hops) { label = l; this.hops = hops; }
    public override int Exec(Frame f)
    {
        if (hops == 0) { f.PendingGoto = label; return 1; }
        throw new GotoException(label, Up(f, hops), this);
    }
}

public sealed class AssignScalar : Stmt
{
    readonly Expr target, value;
    public AssignScalar(Expr target, Expr value) { this.target = target; this.value = value; }
    public override int Exec(Frame f)
    {
        var v = value.Value(f);
        var r = target.Addr(f);
        r.S[r.I] = v;
        return 0;
    }
}

/// <summary>Assignment to a tag field: when the value changes, the fields of the variants become undefined.</summary>
public sealed class AssignTag : Stmt
{
    readonly Expr target, value; readonly int partOffsetFromTag, partSize;
    public AssignTag(Expr target, Expr value, Field tag)
    {
        this.target = target; this.value = value;
        var vp = tag.TagOf!;
        partOffsetFromTag = vp.Offset - tag.Offset; partSize = vp.Size;
    }
    public override int Exec(Frame f)
    {
        var v = value.Value(f);
        var r = target.Addr(f);
        var old = r.S[r.I];
        if (!old.IsUndef && old.N != v.N && f.Ctx.FixedTagStores.TryGetValue(r.S, out var h) && Array.IndexOf(h.FixedTagOffsets, r.I) >= 0)
            throw RT.Err("the tag field of a variable allocated by new with tag values cannot be changed (Revised Report 10.1.2)", this);
        r.S[r.I] = v;
        if (old.IsUndef || old.N != v.N)
            Array.Fill(r.S, Cell.Undefined, r.I + partOffsetFromTag, partSize);
        return 0;
    }
}

public sealed class AssignStruct : Stmt
{
    readonly Expr target, value; readonly int size;
    public AssignStruct(Expr target, Expr value, int size) { this.target = target; this.value = value; this.size = size; }
    public override int Exec(Frame f)
    {
        var src = value.Addr(f);
        var dst = target.Addr(f);
        RT.Copy(src, dst, size);
        return 0;
    }
}

public sealed class CallStmt : Stmt
{
    readonly CallNode call;
    public CallStmt(CallNode c) { call = c; }
    public override int Exec(Frame f) { call.Invoke(f); return 0; }
}

public sealed class IfStmt : Stmt
{
    readonly Expr cond; readonly Stmt then; readonly Stmt? els;
    public IfStmt(Expr c, Stmt t, Stmt? e) { cond = c; then = t; els = e; }
    public override int Exec(Frame f)
    {
        if (cond.Ord(f) != 0) return then.Run(f);
        return els != null ? els.Run(f) : 0;
    }
}

public sealed class WhileStmt : Stmt
{
    readonly Expr cond; readonly Stmt body;
    public WhileStmt(Expr c, Stmt b) { cond = c; body = b; }
    public override int Exec(Frame f)
    {
        var ctx = f.Ctx;
        bool first = true;
        while (true)
        {
            if (!first && ctx.Attention) ctx.OnAttention(f, this);
            first = false;
            if (cond.Ord(f) == 0) return 0;
            int st = body.Run(f);
            if (st != 0) return st;
        }
    }
}

public sealed class RepeatStmt : Stmt
{
    readonly Stmt body; readonly Expr cond; readonly Node untilPos;
    public RepeatStmt(Stmt b, Expr c, Node untilPos) { body = b; cond = c; this.untilPos = untilPos; Stoppable = false; }
    public override int Exec(Frame f)
    {
        var ctx = f.Ctx;
        while (true)
        {
            int st = body.Exec(f);
            if (st != 0) return st;
            if (ctx.Attention) ctx.OnAttention(f, untilPos);
            if (cond.Ord(f) != 0) return 0;
        }
    }
}

public sealed class ForStmt : Stmt
{
    readonly Designator control; readonly Expr from, to; readonly bool down; readonly Stmt body; readonly PType ctype;
    public ForStmt(Designator control, Expr from, Expr to, bool down, Stmt body)
    {
        this.control = control; this.from = from; this.to = to; this.down = down; this.body = body; ctype = control.Type;
    }
    public override int Exec(Frame f)
    {
        long a = from.Ord(f), b = to.Ord(f);
        var r = control.Addr(f);
        if (down ? a < b : a > b)
        {
            r.S[r.I] = Cell.Undefined;     // Revised Report 9.2.3.3: the final value is left undefined
            return 0;
        }
        long lo = ctype.Lo, hi = ctype.Hi;
        if (a < lo || a > hi) throw RT.RangeErr(a, ctype, from);
        if (b < lo || b > hi) throw RT.RangeErr(b, ctype, to);
        var ctx = f.Ctx;
        long v = a;
        bool first = true;
        while (true)
        {
            if (!first && ctx.Attention) ctx.OnAttention(f, this);
            first = false;
            r.S[r.I] = Cell.Ord(v);
            int st = body.Run(f);
            if (st != 0) return st;    // goto out of the loop: the control variable keeps its value
            if (v == b) break;
            v = down ? v - 1 : v + 1;
        }
        r.S[r.I] = Cell.Undefined;     // Revised Report 9.2.3.3: undefined after the loop
        return 0;
    }
}

public sealed class CaseStmt : Stmt
{
    readonly Expr sel; readonly Dictionary<long, int> map; readonly Stmt[] branches;
    public CaseStmt(Expr sel, Dictionary<long, int> map, Stmt[] branches) { this.sel = sel; this.map = map; this.branches = branches; }
    public override int Exec(Frame f)
    {
        long v = sel.Ord(f);
        if (!map.TryGetValue(v, out int k))
            throw RT.Err($"case selector value {Format.OrdText(sel.Type, v)} matches no case label", this);
        return branches[k].Run(f);
    }
}

/// <summary>with r do s: the address of r is computed once and kept in a frame slot.</summary>
public sealed class WithStmt : Stmt
{
    readonly Expr rec; readonly int slot; readonly Stmt body;
    public WithStmt(Expr rec, int slot, Stmt body) { this.rec = rec; this.slot = slot; this.body = body; }
    public override int Exec(Frame f)
    {
        var r = rec.Addr(f);
        f.C[slot] = new Cell { O = r.S, N = r.I };
        return body.Run(f);
    }
}
