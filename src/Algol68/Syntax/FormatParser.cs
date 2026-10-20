using System.Text;
using Studio.Common;

namespace Studio.Algol68.Syntax;

/// <summary>Analyse d'un texte de format $ ... $ (RR 10.3.4).</summary>
internal sealed class FormatParser
{
    readonly Parser parser;
    readonly string text;
    readonly string file;
    readonly List<Diagnostic> diags;
    readonly CompileOptions options;
    int p;
    int line, col;

    public FormatParser(Parser parser, Token t, List<Diagnostic> diags, CompileOptions options)
    {
        this.parser = parser; text = t.Text; file = t.File; this.diags = diags; this.options = options;
        line = t.FormatLine; col = t.FormatCol;
    }

    sealed class FmtError : Exception { public int L, C; public FmtError(string m, int l, int c) : base(m) { L = l; C = c; } }

    char Peek(int k = 0) => p + k < text.Length ? text[p + k] : '\0';
    char NextCh()
    {
        char c = text[p++];
        if (c == '\n') { line++; col = 1; } else col++;
        return c;
    }
    void SkipWs() { while (p < text.Length && char.IsWhiteSpace(text[p])) NextCh(); }
    FmtError Err(string m) => new(m, line, col);
    T Pos<T>(T a) where T : Ast { a.File = file; a.Line = line; a.Col = col; return a; }

    public FormatAst Parse()
    {
        var f = Pos(new FormatAst());
        try
        {
            f.Pieces = ParsePieces(false);
            SkipWs();
            if (p < text.Length) throw Err($"caractère inattendu « {Peek()} » dans le format");
        }
        catch (FmtError e)
        {
            diags.Add(new Diagnostic(Severity.Error, file, e.L, e.C, e.Message));
        }
        catch (ParseException e)
        {
            diags.Add(new Diagnostic(Severity.Error, e.Tok.File, e.Tok.Line, e.Tok.Col, e.Message));
        }
        return f;
    }

    List<FormatPieceAst> ParsePieces(bool inCollection)
    {
        var list = new List<FormatPieceAst>();
        while (true)
        {
            SkipWs();
            if (p >= text.Length || Peek() == ')')
            {
                if (list.Count == 0 && inCollection) throw Err("collection vide dans le format");
                if (list.Count == 0) list.Add(Pos(new PictureAst()));
                break;
            }
            list.Add(ParsePiece());
            SkipWs();
            if (Peek() == ',') { NextCh(); continue; }
            break;
        }
        return list;
    }

    // element brut
    sealed class Elem
    {
        public ReplicatorAst Rep = new();
        public char Kind;           // insertion : k x y l p q " ; cadre : d z a b . e i + - r ; motif special : c B f g h
        public string Literal = "";
        public bool Suppressed;
        public int Radix;
        public List<InsertionAst> Choices = new();
        public UnitAst? Unit;
        public List<UnitAst> Units = new();
        public int Line, Col;
        public bool IsInsertion => Kind is 'k' or 'x' or 'y' or 'l' or 'p' or 'q' or '"';
        public bool IsSpecial => Kind is 'c' or 'B' or 'f' or 'g' or 'h';
    }

    FormatPieceAst ParsePiece()
    {
        var elems = new List<Elem>();
        int startLine = line, startCol = col;
        while (true)
        {
            SkipWs();
            if (p >= text.Length || Peek() == ',' || Peek() == ')') break;
            int el = line, ec = col;
            var rep = ParseReplicator();
            SkipWs();
            char c = Peek();
            if (c == '(')
            {
                // collection
                foreach (var e in elems) if (!e.IsInsertion) throw Err("une collection ne peut suivre qu'une insertion");
                var coll = new CollectionAst { File = file, Line = el, Col = ec };
                coll.Before = ToInsertion(elems);
                coll.Rep = rep;
                NextCh();
                coll.Pieces = ParsePieces(true);
                SkipWs();
                if (Peek() != ')') throw Err("« ) » attendu dans le format");
                NextCh();
                var after = new List<Elem>();
                while (true)
                {
                    SkipWs();
                    if (p >= text.Length || Peek() == ',' || Peek() == ')') break;
                    int al = line, ac = col;
                    var r2 = ParseReplicator();
                    SkipWs();
                    var e2 = ParseInsertionElem(r2, al, ac) ?? throw Err($"insertion attendue après une collection, trouvé « {Peek()} »");
                    after.Add(e2);
                }
                coll.After = ToInsertion(after);
                return coll;
            }
            var ins = ParseInsertionElem(rep, el, ec);
            if (ins != null) { elems.Add(ins); continue; }
            var e3 = new Elem { Rep = rep, Line = el, Col = ec };
            if (c == 's')
            {
                NextCh(); SkipWs();
                e3.Suppressed = true;
                c = Peek();
                if (c == '"' )
                {
                    // litteral supprime : equivaut a rien en sortie (accepte)
                    var lit = ReadString();
                    elems.Add(new Elem { Rep = rep, Kind = '"', Literal = "", Line = el, Col = ec });
                    _ = lit;
                    continue;
                }
                if (!(c is 'd' or 'z' or 'a' or '.' or 'e' or 'i' or 'D' or 'Z' or 'A' or 'E' or 'I'))
                    throw Err($"cadre supprimable attendu après « s », trouvé « {c} »");
            }
            c = char.ToLowerInvariant(c);
            switch (c)
            {
                case 'd': case 'z': case 'a': case '.': case 'e': case 'i': case '+': case '-':
                    NextCh(); e3.Kind = c; break;
                case 'r':
                    NextCh();
                    if (rep.Dynamic != null || !rep.Present) throw Err("la base d'un motif de bits doit être un nombre (2r, 4r, 8r, 16r)");
                    e3.Kind = 'r'; e3.Radix = rep.Static;
                    if (e3.Radix is not (2 or 4 or 8 or 16)) throw Err($"base {e3.Radix} invalide pour un motif de bits");
                    e3.Rep = new ReplicatorAst();
                    break;
                case 'b':
                    {
                        NextCh();
                        int save = p, sl = line, sc = col;
                        SkipWs();
                        if (Peek() == '(')
                        {
                            NextCh();
                            e3.Kind = 'B';
                            e3.Choices.Add(ParseLiteralInsertion());
                            SkipWs();
                            if (Peek() != ',') throw Err("« , » attendu dans le motif de choix booleen");
                            NextCh();
                            e3.Choices.Add(ParseLiteralInsertion());
                            SkipWs();
                            if (Peek() != ')') throw Err("« ) » attendu dans le motif de choix booleen");
                            NextCh();
                        }
                        else { p = save; line = sl; col = sc; e3.Kind = 'b'; }
                        break;
                    }
                case 'c':
                    {
                        NextCh(); SkipWs();
                        if (Peek() != '(') throw Err("« ( » attendu après « c » (motif de choix)");
                        NextCh();
                        e3.Kind = 'c';
                        while (true)
                        {
                            e3.Choices.Add(ParseLiteralInsertion());
                            SkipWs();
                            if (Peek() == ',') { NextCh(); continue; }
                            break;
                        }
                        if (Peek() != ')') throw Err("« ) » attendu dans le motif de choix");
                        NextCh();
                        break;
                    }
                case 'f':
                    {
                        NextCh(); SkipWs();
                        if (Peek() != '(') throw Err("« ( » attendu après « f » (motif de format)");
                        e3.Kind = 'f';
                        e3.Unit = ParseEnclosedUnit();
                        break;
                    }
                case 'g':
                case 'h':
                    {
                        NextCh();
                        e3.Kind = c;
                        int save = p, sl = line, sc = col;
                        SkipWs();
                        if (Peek() == '(') e3.Units = ParseEnclosedUnits();
                        else { p = save; line = sl; col = sc; }
                        break;
                    }
                default:
                    throw Err($"caractère « {Peek()} » inattendu dans le format");
            }
            elems.Add(e3);
        }
        return BuildPicture(elems, startLine, startCol);
    }

    Elem? ParseInsertionElem(ReplicatorAst rep, int el, int ec)
    {
        char c = char.ToLowerInvariant(Peek());
        if (c == '"')
        {
            string s = ReadString();
            return new Elem { Rep = rep, Kind = '"', Literal = s, Line = el, Col = ec };
        }
        if (c is 'k' or 'x' or 'y' or 'l' or 'p' or 'q')
        {
            NextCh();
            return new Elem { Rep = rep, Kind = c, Line = el, Col = ec };
        }
        return null;
    }

    /// <summary>Litteral d'un motif de choix : [replicateur] "..." { [replicateur] "..." } avec alignements eventuels.</summary>
    InsertionAst ParseLiteralInsertion()
    {
        var elems = new List<Elem>();
        while (true)
        {
            SkipWs();
            if (p >= text.Length || Peek() == ',' || Peek() == ')') break;
            int el = line, ec = col;
            var rep = ParseReplicator();
            SkipWs();
            var e = ParseInsertionElem(rep, el, ec) ?? throw Err($"litteral attendu dans un motif de choix, trouvé « {Peek()} »");
            elems.Add(e);
        }
        return ToInsertion(elems);
    }

    string ReadString()
    {
        NextCh(); // "
        var sb = new StringBuilder();
        while (true)
        {
            if (p >= text.Length) throw Err("chaîne non terminee dans le format");
            char c = NextCh();
            if (c == '"')
            {
                if (Peek() == '"') { NextCh(); sb.Append('"'); continue; }
                break;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    ReplicatorAst ParseReplicator()
    {
        var r = Pos(new ReplicatorAst());
        if (char.IsDigit(Peek()))
        {
            int v = 0;
            while (char.IsDigit(Peek())) v = checked(v * 10 + (NextCh() - '0'));
            r.Static = v; r.Present = true;
            return r;
        }
        if ((Peek() == 'n' || Peek() == 'N'))
        {
            int save = p, sl = line, sc = col;
            NextCh(); SkipWs();
            if (Peek() == '(')
            {
                r.Dynamic = ParseEnclosedUnit();
                r.Present = true;
                return r;
            }
            p = save; line = sl; col = sc;
        }
        return r;
    }

    (string inner, int l, int c) ReadParenthesized()
    {
        if (Peek() != '(') throw Err("« ( » attendu");
        NextCh();
        int l = line, c = col;
        int depth = 1; bool inStr = false;
        var sb = new StringBuilder();
        while (true)
        {
            if (p >= text.Length) throw Err("« ) » manquante dans le format");
            char ch = NextCh();
            if (ch == '"') inStr = !inStr;
            if (!inStr)
            {
                if (ch == '(') depth++;
                else if (ch == ')') { depth--; if (depth == 0) break; }
            }
            sb.Append(ch);
        }
        return (sb.ToString(), l, c);
    }

    UnitAst ParseEnclosedUnit()
    {
        var (inner, l, c) = ReadParenthesized();
        var toks = Lexer.LexFragment(inner, file, l, c, parser.Stropping, diags, options);
        return parser.ParseFragmentUnit(toks);
    }

    List<UnitAst> ParseEnclosedUnits()
    {
        var (inner, l, c) = ReadParenthesized();
        var toks = Lexer.LexFragment(inner, file, l, c, parser.Stropping, diags, options);
        return parser.ParseFragmentUnits(toks);
    }

    InsertionAst ToInsertion(List<Elem> elems)
    {
        var ins = new InsertionAst { File = file, Line = elems.Count > 0 ? elems[0].Line : line, Col = elems.Count > 0 ? elems[0].Col : col };
        foreach (var e in elems)
            ins.Elems.Add(new InsertElemAst { Rep = e.Rep, Code = e.Kind, Literal = e.Literal, File = file, Line = e.Line, Col = e.Col });
        return ins;
    }

    PictureAst BuildPicture(List<Elem> elems, int l, int c)
    {
        var pic = new PictureAst { File = file, Line = l, Col = c };
        // derniere position d'un cadre ou motif
        int last = -1;
        for (int i = 0; i < elems.Count; i++) if (!elems[i].IsInsertion) last = i;
        pic.Insertion = ToInsertion(elems.Skip(last + 1).ToList());
        if (last < 0)
        {
            return pic;
        }
        var pat = new FormatPatternAst { File = file, Line = l, Col = c };
        var pending = new List<Elem>();
        var specials = new List<Elem>();
        for (int i = 0; i <= last; i++)
        {
            var e = elems[i];
            if (e.IsInsertion) { pending.Add(e); continue; }
            if (e.IsSpecial)
            {
                specials.Add(e);
                pat.Lead = ToInsertion(pending);
                pending.Clear();
                continue;
            }
            var fr = new FrameAst { Ins = ToInsertion(pending), Rep = e.Rep, Suppressed = e.Suppressed, Marker = e.Kind, Radix = e.Radix, File = file, Line = e.Line, Col = e.Col };
            pending.Clear();
            pat.Frames.Add(fr);
        }
        if (specials.Count > 0)
        {
            if (specials.Count > 1 || pat.Frames.Count > 0)
                throw new FmtError("un motif de choix, de format ou général ne peut pas être combine avec d'autres cadres", l, c);
            var s = specials[0];
            switch (s.Kind)
            {
                case 'c': pat.Kind = PatternKind.IntChoice; pat.Choices = s.Choices; break;
                case 'B': pat.Kind = PatternKind.BoolChoice; pat.Choices = s.Choices; break;
                case 'f': pat.Kind = PatternKind.Format; pat.FormatUnit = s.Unit; break;
                case 'g': pat.Kind = PatternKind.General; pat.GeneralParams = s.Units; break;
                case 'h': pat.Kind = PatternKind.GeneralH; pat.GeneralParams = s.Units; break;
            }
            pic.Pattern = pat;
            return pic;
        }
        pat.Kind = Classify(pat.Frames, l, c);
        pic.Pattern = pat;
        return pic;
    }

    PatternKind Classify(List<FrameAst> frames, int l, int c)
    {
        bool hasA = frames.Any(f => f.Marker == 'a');
        bool hasB = frames.Any(f => f.Marker == 'b');
        bool hasR = frames.Any(f => f.Marker == 'r');
        bool hasI = frames.Any(f => f.Marker == 'i');
        bool hasPE = frames.Any(f => f.Marker is '.' or 'e');
        if (hasA)
        {
            if (frames.Any(f => f.Marker != 'a')) throw new FmtError("un motif de chaîne ne contient que des cadres « a »", l, c);
            return PatternKind.String;
        }
        if (hasB)
        {
            if (frames.Count != 1) throw new FmtError("un motif booleen ne contient qu'un cadre « b »", l, c);
            return PatternKind.Boolean;
        }
        if (hasR)
        {
            if (frames[0].Marker != 'r' || frames.Skip(1).Any(f => f.Marker is not ('d' or 'z')))
                throw new FmtError("motif de bits mal formé (forme attendue : 2r8d)", l, c);
            return PatternKind.Bits;
        }
        if (hasI)
        {
            if (frames.Count(f => f.Marker == 'i') != 1) throw new FmtError("un motif complexe contient un seul cadre « i »", l, c);
            return PatternKind.Complex;
        }
        ValidateNumeric(frames, hasPE, l, c);
        return hasPE ? PatternKind.Real : PatternKind.Integral;
    }

    static void ValidateNumeric(List<FrameAst> frames, bool real, int l, int c)
    {
        int signs = frames.Count(f => f.Marker is '+' or '-');
        int points = frames.Count(f => f.Marker == '.');
        int exps = frames.Count(f => f.Marker == 'e');
        if (points > 1) throw new FmtError("plusieurs cadres « . » dans un motif", l, c);
        if (exps > 1) throw new FmtError("plusieurs cadres « e » dans un motif", l, c);
        if (signs > (exps > 0 ? 2 : 1)) throw new FmtError("trop de cadres de signe dans un motif", l, c);
        if (frames.All(f => f.Marker is '+' or '-')) throw new FmtError("motif numérique sans chiffre", l, c);
    }
}
