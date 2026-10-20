using Studio.Algol68.Exec;
using Studio.Algol68.Runtime;
using Studio.Algol68.Syntax;

namespace Studio.Algol68.Semantics;

public sealed partial class Compiler
{
    bool fmtDynamic;

    Node GenFormat(FormatTextAst ft)
    {
        fmtDynamic = false;
        var cf = new CFormat { Pieces = ft.Format.Pieces.Select(CompilePiece).ToArray() };
        cf.HasPatterns = HasPatterns(cf.Pieces);
        var n = new FormatTextNode(cf, fmtDynamic, cur.Level);
        n.SetPos(ft);
        return n;
    }

    static bool HasPatterns(CPiece[] ps) => ps.Any(p => p is CPicture { Pattern: not null } || (p is CCollection c && HasPatterns(c.Pieces)));

    CPiece CompilePiece(FormatPieceAst p)
    {
        switch (p)
        {
            case CollectionAst c:
                return new CCollection
                {
                    Before = CompileIns(c.Before), After = CompileIns(c.After), Rep = CompileRep(c.Rep),
                    Pieces = c.Pieces.Select(CompilePiece).ToArray(),
                };
            case PictureAst pic:
                return new CPicture { Pattern = pic.Pattern == null ? null : CompilePattern(pic.Pattern), Ins = CompileIns(pic.Insertion) };
        }
        throw new InvalidOperationException();
    }

    CRep CompileRep(ReplicatorAst r)
    {
        if (r.Dynamic != null)
        {
            fmtDynamic = true;
            return new CRep { Dyn = Gen(r.Dynamic, M.Int, Strength.Meek) };
        }
        return new CRep { Static = r.Present ? r.Static : 1 };
    }

    CIns CompileIns(InsertionAst ins)
    {
        if (ins.IsEmpty) return CIns.Empty;
        return new CIns { Items = ins.Elems.Select(e => (CompileRep(e.Rep), e.Code, e.Literal)).ToArray() };
    }

    CPattern CompilePattern(FormatPatternAst p)
    {
        var cp = new CPattern { Kind = p.Kind, Lead = CompileIns(p.Lead) };
        cp.Frames = p.Frames.Select(fr => new CFrame { Ins = CompileIns(fr.Ins), Rep = CompileRep(fr.Rep), Supp = fr.Suppressed, Marker = fr.Marker, Radix = fr.Radix }).ToArray();
        cp.Choices = p.Choices.Select(CompileIns).ToArray();
        if (p.FormatUnit != null) { fmtDynamic = true; cp.FormatUnit = Gen(p.FormatUnit, M.Format, Strength.Meek); }
        if (p.GeneralParams.Count > 0)
        {
            fmtDynamic = true;
            if (p.GeneralParams.Count > 4) Error(p, "the general pattern accepts at most 3 parameters (4 for h)");
            cp.GParams = p.GeneralParams.Select(u => Gen(u, M.Int, Strength.Meek)).ToArray();
        }
        if (p.Kind == PatternKind.Complex)
        {
            int i = cp.Frames.ToList().FindIndex(x => x.Marker == 'i');
            if (i <= 0 || i == cp.Frames.Length - 1) Error(p, "ill-formed complex pattern: a real pattern is required on each side of 'i'");
        }
        if (p.Kind == PatternKind.Real && !cp.Frames.Any(x => x.Marker is 'd' or 'z')) Error(p, "real pattern without digits");
        return cp;
    }
}
