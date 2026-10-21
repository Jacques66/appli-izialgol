using Studio.Pascal.Runtime;
using Studio.Pascal.Semantics;

namespace Studio.Pascal.Exec;

// ---------------------------------------------------------------- input and output

public enum WK { Int, Real, Bool, Char, Str, Elem }

public sealed class WriteItem
{
    public WK Kind;
    public Expr E = null!;
    public Expr? W, D;
    public int Size = 1;
}

public sealed class WriteStmt : Stmt
{
    readonly Expr file; readonly WriteItem[] items; readonly bool ln;
    public WriteStmt(Expr file, WriteItem[] items, bool ln) { this.file = file; this.items = items; this.ln = ln; }

    int Width(Expr? w, int def, Frame f)
    {
        if (w == null) return def;
        long v = w.Ord(f);
        if (v < 0) throw RT.Err($"field width {v} must not be negative", w);
        return (int)Math.Min(v, 100000);
    }

    public override int Exec(Frame f)
    {
        var pf = BufferNode.FileOf(file, f, this);
        foreach (var it in items)
        {
            if (it.Kind == WK.Elem)
            {
                if (it.E.Type.IsStructured) pf.WriteElem(it.E.Addr(f), this);
                else pf.WriteElem(new Ref(new[] { it.E.Value(f) }, 0), this);
                continue;
            }
            string s;
            switch (it.Kind)
            {
                case WK.Int: { long v = it.E.Ord(f); s = Format.Int(v, Width(it.W, Format.DefaultIntWidth, f)); break; }
                case WK.Real:
                    {
                        double v = it.E.Real(f);
                        int w = Width(it.W, Format.DefaultRealWidth, f);
                        if (it.D != null)
                        {
                            long d = it.D.Ord(f);
                            if (d < 1) throw RT.Err($"number of fraction digits {d} must be at least 1", it.D);
                            s = Format.RealF(v, w, (int)Math.Min(d, 300));
                        }
                        else s = Format.RealE(v, w);
                        break;
                    }
                case WK.Bool:
                    {
                        bool b = it.E.Ord(f) != 0;
                        int w = Width(it.W, Format.DefaultBoolWidth, f);
                        string t = b ? "TRUE" : "FALSE";
                        s = w < t.Length ? t[..w] : Format.Pad(t, w);
                        break;
                    }
                case WK.Char:
                    {
                        char c = (char)it.E.Ord(f);
                        int w = Width(it.W, 1, f);
                        s = w == 0 ? "" : new string(' ', w - 1) + c;
                        break;
                    }
                default:
                    {
                        var r = it.E.Addr(f);
                        string t = Chars(r, it.Size);
                        int w = Width(it.W, it.Size, f);
                        s = w < t.Length ? t[..w] : Format.Pad(t, w);
                        break;
                    }
            }
            pf.WriteText(s, this);
        }
        if (ln) pf.WriteLn(this);
        return 0;
    }

    string Chars(Ref r, int n)
    {
        var a = new char[n];
        for (int i = 0; i < n; i++)
        {
            var c = r.S[r.I + i];
            if (c.IsUndef) throw RT.Err("write of a string with undefined characters", this);
            a[i] = (char)c.N;
        }
        return new string(a);
    }
}

public enum RK { Int, Real, Char, Elem }

public sealed class ReadItem
{
    public RK Kind;
    public Expr Target = null!;
    public PType Type = ErrorType.T;
    public int Size = 1;
    public bool ToReal;     // file of integer read into a real variable
}

public sealed class ReadStmt : Stmt
{
    readonly Expr file; readonly ReadItem[] items; readonly bool ln;
    public ReadStmt(Expr file, ReadItem[] items, bool ln) { this.file = file; this.items = items; this.ln = ln; }
    public override int Exec(Frame f)
    {
        var pf = BufferNode.FileOf(file, f, this);
        foreach (var it in items)
        {
            switch (it.Kind)
            {
                case RK.Int:
                    {
                        long v = pf.ReadInt(this);
                        if (it.Type.Kind == TK.Subrange && (v < it.Type.Lo || v > it.Type.Hi)) throw RT.RangeErr(v, it.Type, this);
                        var r = it.Target.Addr(f); r.S[r.I] = Cell.Ord(v);
                        break;
                    }
                case RK.Real:
                    {
                        double v = pf.ReadReal(this);
                        var r = it.Target.Addr(f); r.S[r.I] = Cell.Real(v);
                        break;
                    }
                case RK.Char:
                    {
                        long v = pf.ReadChar(this);
                        if (it.Type.Kind == TK.Subrange && (v < it.Type.Lo || v > it.Type.Hi)) throw RT.RangeErr(v, it.Type, this);
                        var r = it.Target.Addr(f); r.S[r.I] = Cell.Ord(v);
                        break;
                    }
                default:
                    {
                        if (it.Type.IsStructured) { pf.ReadElem(it.Target.Addr(f), this); break; }
                        var tmp = new Cell[1];
                        pf.ReadElem(new Ref(tmp, 0), this);
                        var c = tmp[0];
                        if (it.ToReal && !c.IsUndef) c = Cell.Real(c.N);
                        if (it.Type.Kind == TK.Subrange && !c.IsUndef && (c.N < it.Type.Lo || c.N > it.Type.Hi)) throw RT.RangeErr(c.N, it.Type, this);
                        var r = it.Target.Addr(f); r.S[r.I] = c;
                        break;
                    }
            }
        }
        if (ln) pf.ReadLn(this);
        return 0;
    }
}

public enum FileOp { Get, Put, Reset, Rewrite, Page }

public sealed class FileOpStmt : Stmt
{
    readonly FileOp op; readonly Expr file;
    public FileOpStmt(FileOp op, Expr file) { this.op = op; this.file = file; }
    public override int Exec(Frame f)
    {
        var pf = BufferNode.FileOf(file, f, this);
        switch (op)
        {
            case FileOp.Get: pf.Get(this); break;
            case FileOp.Put: pf.Put(this); break;
            case FileOp.Reset: pf.Reset(this); break;
            case FileOp.Rewrite: pf.Rewrite(this); break;
            default: pf.Page(this); break;
        }
        return 0;
    }
}

public sealed class EofNode : Expr
{
    readonly Expr file; readonly bool eoln;
    public EofNode(Expr file, bool eoln) { this.file = file; this.eoln = eoln; Type = EnumType.Boolean; }
    public override long Ord(Frame f)
    {
        var pf = BufferNode.FileOf(file, f, this);
        return (eoln ? pf.Eoln(this) : pf.Eof(this)) ? 1 : 0;
    }
}

// ---------------------------------------------------------------- dynamic allocation

public sealed class NewStmt : Stmt
{
    readonly Expr ptr; readonly PType target; readonly (int Offset, long Value)[] tags; readonly long[] values;
    readonly List<(int, FileType)> files = new();
    public NewStmt(Expr ptr, PType target, (int, long)[] tags, long[] values)
    {
        this.ptr = ptr; this.target = target; this.tags = tags; this.values = values;
        target.CollectFiles(0, files);
    }
    public override int Exec(Frame f)
    {
        var ctx = f.Ctx;
        var cells = new Cell[Math.Max(1, target.Size)];
        Array.Fill(cells, Cell.Undefined);
        foreach (var (off, ft) in files) cells[off] = Cell.Obj(ctx.NewFile(ft, "(dynamic)", null));
        foreach (var (off, v) in tags) cells[off] = Cell.Ord(v);
        var h = new HeapObj(target, cells, ++ctx.HeapCounter) { FixedVariant = values.Length > 0 };
        if (values.Length > 0)
        {
            // new(p, t1, ..., tn): the tag fields must keep these values (Revised Report 10.1.2)
            h.TagValues = values;
            h.FixedTagOffsets = tags.Select(t => t.Offset).ToArray();
            if (tags.Length > 0) ctx.FixedTagStores.AddOrUpdate(cells, h);
        }
        var r = ptr.Addr(f);
        r.S[r.I] = Cell.Obj(h);
        return 0;
    }
}

public sealed class DisposeStmt : Stmt
{
    readonly Designator ptr; readonly (int Offset, long Value)[] tags; readonly long[] values;
    public DisposeStmt(Designator ptr, (int, long)[] tags, long[] values) { this.ptr = ptr; this.tags = tags; this.values = values; }
    public override int Exec(Frame f)
    {
        var o = ptr.Obj(f);
        if (o is not HeapObj h) throw RT.Err("dispose(nil): the pointer does not reference a variable", this);
        if (h.Disposed) throw RT.Err($"dispose of a variable that was already disposed ({ptr.Desc})", this);
        if (h.TagValues != null)
        {
            // a variable allocated by new(p, t1, ..., tn) is disposed by dispose(p, t1, ..., tn) with identical tag values
            if (!h.TagValues.AsSpan().SequenceEqual(values))
                throw RT.Err($"dispose: {ptr.Desc} was allocated by new with {h.TagValues.Length} tag value(s); dispose must name the same tag values", this);
        }
        else
        {
            foreach (var (off, v) in tags)
            {
                var c = h.C[off];
                if (c.IsUndef || c.N != v)
                    throw RT.Err($"dispose: the tag values given do not match the variant of {ptr.Desc}", this);
            }
        }
        h.Disposed = true;
        var r = ptr.Addr(f);
        r.S[r.I] = Cell.Undefined;
        return 0;
    }
}

/// <summary>pack(a, i, z) and unpack(z, a, i).</summary>
public sealed class PackStmt : Stmt
{
    readonly Expr a, i, z; readonly bool unpack;
    readonly long aLo, aHi, zCount; readonly int esize; readonly PType aIndex;
    public PackStmt(Expr a, Expr i, Expr z, bool unpack)
    {
        this.a = a; this.i = i; this.z = z; this.unpack = unpack;
        var at = (ArrayType)a.Type; var zt = (ArrayType)z.Type;
        aLo = at.Index.Lo; aHi = at.Index.Hi; zCount = zt.Count; esize = at.Elem.Size; aIndex = at.Index;
    }
    public override int Exec(Frame f)
    {
        long start = i.Ord(f);
        if (start < aLo || start + zCount - 1 > aHi)
            throw RT.Err($"{(unpack ? "unpack" : "pack")}: index {Format.OrdText(aIndex, start)} leaves too few elements of the unpacked array ({zCount} needed)", this);
        var ra = a.Addr(f); var rz = z.Addr(f);
        var ua = new Ref(ra.S, ra.I + (int)(start - aLo) * esize);
        if (unpack) RT.Copy(rz, ua, (int)zCount * esize);
        else RT.Copy(ua, rz, (int)zCount * esize);
        return 0;
    }
}

// ---------------------------------------------------------------- standard functions

public enum Fn { Abs, Sqr, Sin, Cos, Exp, Ln, Sqrt, Arctan, Odd, Trunc, Round, Ord, Chr, Succ, Pred }

public sealed class IntFn : Expr
{
    readonly Fn fn; readonly Expr a;
    public IntFn(Fn fn, Expr a, PType t) { this.fn = fn; this.a = a; Type = t; }
    public override long Ord(Frame f)
    {
        switch (fn)
        {
            case Fn.Abs: return Math.Abs(a.Ord(f));
            case Fn.Sqr: { long v = a.Ord(f); return RT.Mul(v, v, this); }
            case Fn.Odd: return (a.Ord(f) & 1) != 0 ? 1 : 0;
            case Fn.Ord: return a.Ord(f);
            case Fn.Chr:
                {
                    long v = a.Ord(f);
                    if (v < 0 || v > 255) throw RT.Err($"chr({v}): no character with this ordinal value (0..255)", this);
                    return v;
                }
            case Fn.Succ:
                {
                    long v = a.Ord(f);
                    if (v >= a.Type.Host.Hi) throw RT.Err($"succ({Format.OrdText(a.Type, v)}): no successor", this);
                    return v + 1;
                }
            case Fn.Pred:
                {
                    long v = a.Ord(f);
                    if (v <= a.Type.Host.Lo) throw RT.Err($"pred({Format.OrdText(a.Type, v)}): no predecessor", this);
                    return v - 1;
                }
            case Fn.Trunc:
            case Fn.Round:
                {
                    double x = a.Real(f);
                    double t = fn == Fn.Trunc ? Math.Truncate(x) : (x >= 0 ? Math.Floor(x + 0.5) : -Math.Floor(-x + 0.5));
                    if (!(Math.Abs(t) <= RT.MaxInt)) throw RT.Err($"{(fn == Fn.Trunc ? "trunc" : "round")}({Format.RealShort(x)}): result exceeds maxint", this);
                    return (long)t;
                }
        }
        throw Internal();
    }
}

public sealed class RealFn : Expr
{
    readonly Fn fn; readonly Expr a;
    public RealFn(Fn fn, Expr a) { this.fn = fn; this.a = a; Type = RealType.T; }
    public override double Real(Frame f)
    {
        double x = a.Real(f);
        switch (fn)
        {
            case Fn.Abs: return Math.Abs(x);
            case Fn.Sqr: return RT.CheckReal(x * x, this);
            case Fn.Sin: return Math.Sin(x);
            case Fn.Cos: return Math.Cos(x);
            case Fn.Arctan: return Math.Atan(x);
            case Fn.Exp: return RT.CheckReal(Math.Exp(x), this);
            case Fn.Ln:
                if (x <= 0) throw RT.Err($"ln({Format.RealShort(x)}): argument must be positive", this);
                return Math.Log(x);
            case Fn.Sqrt:
                if (x < 0) throw RT.Err($"sqrt({Format.RealShort(x)}): argument must not be negative", this);
                return Math.Sqrt(x);
        }
        throw Internal();
    }
}
