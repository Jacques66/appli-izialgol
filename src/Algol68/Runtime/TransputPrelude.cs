using System.Numerics;
using Studio.Algol68.Exec;
using Studio.Algol68.Semantics;

namespace Studio.Algol68.Runtime;

public static partial class Prelude
{
    static Transput T(Frame f) => f.Ctx.Transput;
    static string Str(Value v, Node at) => RT.Str(v, at);

    static void BuildTransput(Compiler c, ModeTable M)
    {
        var RF = M.RefFile; var V = M.Void; var I = M.Int; var B = M.Bool; var S = M.RowChar;
        var ROut = M.RowOutType; var RIn = M.RowInType;
        var PRF = M.ProcRefFileVoid; var PRFB = M.ProcRefFileBool;
        var Ch = M.Channel;

        CtxConst("standin", RF, x => Value.Obj(x.Transput.StandInRef));
        CtxConst("standout", RF, x => Value.Obj(x.Transput.StandOutRef));
        CtxConst("standerror", RF, x => Value.Obj(x.Transput.StandErrorRef));
        CtxConst("standback", RF, x => Value.Obj(x.Transput.StandBackRef));
        CtxConst("standinchannel", Ch, x => Value.Obj(x.Transput.StandInChannel));
        CtxConst("standoutchannel", Ch, x => Value.Obj(x.Transput.StandOutChannel));
        CtxConst("standerrorchannel", Ch, x => Value.Obj(x.Transput.StandErrorChannel));
        CtxConst("standbackchannel", Ch, x => Value.Obj(x.Transput.StandBackChannel));

        // transput sans format
        Proc("print write", V, new[] { ROut }, (f, a, at) => { var t = T(f); t.PutList(f, Value.Obj(t.StandOutRef), a[0], at); return default; });
        Proc("put", V, new[] { RF, ROut }, (f, a, at) => { T(f).PutList(f, a[0], a[1], at); return default; });
        Proc("read", V, new[] { RIn }, (f, a, at) => { var t = T(f); t.GetList(f, Value.Obj(t.StandInRef), a[0], at); return default; });
        Proc("get", V, new[] { RF, RIn }, (f, a, at) => { T(f).GetList(f, a[0], a[1], at); return default; });
        // formate
        Proc("printf writef", V, new[] { ROut }, (f, a, at) => { var t = T(f); t.PutfList(f, Value.Obj(t.StandOutRef), a[0], at); return default; });
        Proc("putf", V, new[] { RF, ROut }, (f, a, at) => { T(f).PutfList(f, a[0], a[1], at); return default; });
        Proc("readf", V, new[] { RIn }, (f, a, at) => { var t = T(f); t.GetfList(f, Value.Obj(t.StandInRef), a[0], at); return default; });
        Proc("getf", V, new[] { RF, RIn }, (f, a, at) => { T(f).GetfList(f, a[0], a[1], at); return default; });
        // binaire
        Proc("printbin writebin", V, new[] { ROut }, (f, a, at) => { var t = T(f); t.PutBin(f, Value.Obj(t.StandBackRef), a[0], at); return default; });
        Proc("putbin", V, new[] { RF, ROut }, (f, a, at) => { T(f).PutBin(f, a[0], a[1], at); return default; });
        Proc("readbin", V, new[] { RIn }, (f, a, at) => { var t = T(f); t.GetBin(f, Value.Obj(t.StandBackRef), a[0], at); return default; });
        Proc("getbin", V, new[] { RF, RIn }, (f, a, at) => { T(f).GetBin(f, a[0], a[1], at); return default; });

        // fichiers
        Proc("open", I, new[] { RF, S, Ch }, (f, a, at) => Value.I(T(f).Open(f, a[0], Str(a[1], at), a[2], at)));
        Proc("establish", I, new[] { RF, S, Ch }, (f, a, at) => Value.I(T(f).Establish(f, a[0], Str(a[1], at), a[2], at)));
        Proc("create", I, new[] { RF, Ch }, (f, a, at) => Value.I(T(f).Create(f, a[0], a[1], at)));
        Proc("associate", V, new[] { RF, M.RefString }, (f, a, at) => { T(f).Associate(a[0], a[1], at); return default; });
        Proc("close", PRF.ResultVoidParams(), new[] { RF }, (f, a, at) => { T(f).Close(a[0], at); return default; });
        Proc("lock", V, new[] { RF }, (f, a, at) => { T(f).Close(a[0], at); return default; });
        Proc("scratch erase", V, new[] { RF }, (f, a, at) => { T(f).Close(a[0], at, true); return default; });
        Proc("newline", V, new[] { RF }, (f, a, at) => { T(f).Newline(f, a[0], at); return default; });
        Proc("newpage", V, new[] { RF }, (f, a, at) => { T(f).Newpage(f, a[0], at); return default; });
        Proc("space", V, new[] { RF }, (f, a, at) => { T(f).Space(f, a[0], at); return default; });
        Proc("backspace", V, new[] { RF }, (f, a, at) => { T(f).Backspace(f, a[0], at); return default; });
        Proc("reset rewind", V, new[] { RF }, (f, a, at) => { T(f).Reset(a[0], at); return default; });
        Proc("set", V, new[] { RF, I, I, I }, (f, a, at) => { T(f).SetPos(f, a[0], a[1].N, a[2].N, a[3].N, at); return default; });
        Proc("seek", I, new[] { RF, I }, (f, a, at) => Value.I(T(f).Seek(a[0], a[1].N, at)));
        Proc("setcharnumber", V, new[] { RF, I }, (f, a, at) => { T(f).SetCharNumber(f, a[0], a[1].N, at); return default; });
        Proc("charnumber", I, new[] { RF }, (f, a, at) => Value.I(Transput.GetFile(a[0], at).Char));
        Proc("linenumber", I, new[] { RF }, (f, a, at) => Value.I(Transput.GetFile(a[0], at).Line));
        Proc("pagenumber", I, new[] { RF }, (f, a, at) => Value.I(Transput.GetFile(a[0], at).Page));
        Proc("maketerm", V, new[] { RF, S }, (f, a, at) => { Transput.GetFile(a[0], at).Term = Str(a[1], at); return default; });
        Proc("idf", S, new[] { RF }, (f, a, at) => RT.StrValue(Transput.GetFile(a[0], at).Idf));
        Proc("term", S, new[] { RF }, (f, a, at) => RT.StrValue(Transput.GetFile(a[0], at).Term));
        Proc("programidf", S, Array.Empty<Mode>(), (f, a, at) => RT.StrValue(f.Ctx.Program.MainFile));

        // evenements
        foreach (var (name, setter) in new (string, Action<FileV, ProcV?>)[]
        {
            ("onlogicalfileend onfileend", (fl, p) => fl.OnLogicalEnd = p),
            ("onphysicalfileend", (fl, p) => fl.OnPhysicalEnd = p),
            ("onlineend", (fl, p) => fl.OnLineEnd = p),
            ("onpageend", (fl, p) => fl.OnPageEnd = p),
            ("onformatend", (fl, p) => fl.OnFormatEnd = p),
            ("onvalueerror", (fl, p) => fl.OnValueError = p),
            ("onformaterror", (fl, p) => fl.OnFormatError = p),
            ("onopenerror", (fl, p) => fl.OnOpenError = p),
            ("ontransputerror", (fl, p) => fl.OnTransputError = p),
        })
        {
            var st = setter;
            Proc(name, V, new[] { RF, PRFB }, (f, a, at) => { st(Transput.GetFile(a[0], at), a[1].O as ProcV); return default; });
        }
        var charErr = M.Proc(B, RF, M.RefChar);
        Proc("oncharerror", V, new[] { RF, charErr }, (f, a, at) => { Transput.GetFile(a[0], at).OnCharError = a[1].O as ProcV; return default; });

        // enquetes
        foreach (var (name, pred) in new (string, Func<FileV, bool>)[]
        {
            ("putpossible", fl => fl.Opened && fl.Channel.Put),
            ("getpossible", fl => fl.Opened && fl.Channel.Get),
            ("binpossible", fl => fl.Opened && (fl.Channel.Bin || fl.Kind == FileKind.Disk)),
            ("setpossible", fl => fl.Opened && (fl.Channel.Set || fl.Kind is FileKind.Disk or FileKind.Assoc)),
            ("resetpossible rewindpossible", fl => fl.Opened && (fl.Channel.Reset || fl.Kind is FileKind.Disk or FileKind.Assoc)),
            ("reidfpossible", fl => fl.Opened && fl.Channel.Reidf),
            ("compressible", fl => false),
            ("drawpossible", fl => false),
        })
        {
            var pr = pred;
            Proc(name, B, new[] { RF }, (f, a, at) => Value.B(pr(Transput.GetFile(a[0], at))));
        }
        Proc("eof endoffile", B, new[] { RF }, (f, a, at) => { var fl = Transput.GetFile(a[0], at); lock (fl.Lock) return Value.B(T(f).AtEof(fl)); });
        Proc("eoln endofline", B, new[] { RF }, (f, a, at) => { var fl = Transput.GetFile(a[0], at); lock (fl.Lock) { int ch = T(f).PeekChar(fl); return Value.B(ch < 0 || ch == '\n'); } });

        // conversions
        var NUMBER = M.Union(new[] { M.Int, M.LongInt, M.LongLongInt, M.Real, M.LongReal, M.LongLongReal });
        Proc("whole", S, new[] { NUMBER, I }, (f, a, at) => { var u = (UnionV)a[0].O!; return RT.StrValue(Transput.WholeAny(u.M, u.V, (int)a[1].N)); });
        Proc("fixed", S, new[] { NUMBER, I, I }, (f, a, at) => { var u = (UnionV)a[0].O!; return RT.StrValue(Transput.FixedAny(u.M, u.V, (int)a[1].N, (int)a[2].N)); });
        Proc("float", S, new[] { NUMBER, I, I, I }, (f, a, at) => { var u = (UnionV)a[0].O!; return RT.StrValue(Transput.FloatAny(u.M, u.V, (int)a[1].N, (int)a[2].N, (int)a[3].N)); });
        Proc("real", S, new[] { NUMBER, I, I, I, I }, (f, a, at) => { var u = (UnionV)a[0].O!; return RT.StrValue(Transput.FloatAny(u.M, u.V, (int)a[1].N, (int)a[2].N, (int)a[3].N, (int)a[4].N)); });

        // chaines
        Proc("charinstring", B, new[] { M.Char, M.RefInt, S }, (f, a, at) =>
        {
            var s = Str(a[2], at); int i = s.IndexOf((char)a[0].N);
            if (i >= 0 && a[1].O is RefV r && r.A != null) r.A[r.I] = Value.I(i + LwbOf(a[2]));
            return Value.B(i >= 0);
        });
        Proc("lastcharinstring", B, new[] { M.Char, M.RefInt, S }, (f, a, at) =>
        {
            var s = Str(a[2], at); int i = s.LastIndexOf((char)a[0].N);
            if (i >= 0 && a[1].O is RefV r && r.A != null) r.A[r.I] = Value.I(i + LwbOf(a[2]));
            return Value.B(i >= 0);
        });
        Proc("stringinstring", B, new[] { S, M.RefInt, S }, (f, a, at) =>
        {
            var s = Str(a[2], at); int i = s.IndexOf(Str(a[0], at), StringComparison.Ordinal);
            if (i >= 0 && a[1].O is RefV r && r.A != null) r.A[r.I] = Value.I(i + LwbOf(a[2]));
            return Value.B(i >= 0);
        });

        // bits pack / bytes pack
        for (int s = 0; s <= 2; s++)
        {
            int size = s;
            int w = BitsWidthOf(s);
            Proc(LongPrefix(s) + "bitspack", M.BitsOf(s), new[] { M.RowBool }, (f, a, at) =>
            {
                var els = RowOps.GetRow(a[0], at).Elements();
                if (els.Length > w) throw RT.Err($"bits pack : {els.Length} booleens pour {w} bits", at);
                BigInteger b = 0;
                foreach (var e in els) b = (b << 1) | (RT.CheckInit(e, at).N != 0 ? 1 : 0);
                return size == 0 ? Value.I((long)b) : Value.Obj(b);
            });
        }
        Proc("bytespack", M.Bytes, new[] { S }, (f, a, at) =>
        {
            var s = Str(a[0], at);
            if (s.Length > BytesWidth) throw RT.Err($"bytes pack : chaine de {s.Length} caracteres pour {BytesWidth} octets", at);
            return Value.Obj(s.PadRight(BytesWidth, '\0'));
        });
        Proc("longbytespack", M.LongBytes, new[] { S }, (f, a, at) =>
        {
            var s = Str(a[0], at);
            if (s.Length > LongBytesWidth) throw RT.Err($"long bytes pack : chaine de {s.Length} caracteres pour {LongBytesWidth} octets", at);
            return Value.Obj(s.PadRight(LongBytesWidth, '\0'));
        });

        // lectures et ecritures simplifiees (Algol68C, a68g)
        foreach (var (suffix, mode) in new (string, Mode)[] { ("int", M.Int), ("real", M.Real), ("bool", M.Bool), ("char", M.Char), ("string", M.String), ("longint", M.LongInt), ("longreal", M.LongReal), ("bits", M.Bits), ("compl", M.Compl) })
        {
            var md = mode;
            var refMode = M.Ref(md);
            var valMode = M.Normalize(md);
            Proc("read" + suffix, valMode, Array.Empty<Mode>(), (f, a, at) =>
            {
                var t = T(f);
                var cell = RefV.NewCell(md is FlexMode ? RT.StrValue("") : md is StructMode sm ? Value.Obj(new Value[sm.Fields.Length]) : Value.UninitValue, 0);
                var item = Value.Obj(new UnionV(refMode, Value.Obj(cell)));
                t.GetList(f, Value.Obj(t.StandInRef), Value.Obj(RowV.Make1(new[] { item })), at);
                return cell.A![0];
            });
            Proc("get" + suffix, valMode, new[] { RF }, (f, a, at) =>
            {
                var cell = RefV.NewCell(md is FlexMode ? RT.StrValue("") : md is StructMode sm ? Value.Obj(new Value[sm.Fields.Length]) : Value.UninitValue, 0);
                var item = Value.Obj(new UnionV(refMode, Value.Obj(cell)));
                T(f).GetList(f, a[0], Value.Obj(RowV.Make1(new[] { item })), at);
                return cell.A![0];
            });
            Proc("print" + suffix, V, new[] { valMode }, (f, a, at) =>
            {
                var t = T(f);
                t.PutList(f, Value.Obj(t.StandOutRef), Value.Obj(RowV.Make1(new[] { Value.Obj(new UnionV(valMode, a[0])) })), at);
                return default;
            });
            Proc("put" + suffix, V, new[] { RF, valMode }, (f, a, at) =>
            {
                T(f).PutList(f, a[0], Value.Obj(RowV.Make1(new[] { Value.Obj(new UnionV(valMode, a[1])) })), at);
                return default;
            });
        }
        Proc("readline", M.RowChar, Array.Empty<Mode>(), (f, a, at) =>
        {
            var t = T(f); var fl = t.StandIn;
            lock (fl.Lock)
            {
                if (t.AtEof(fl)) t.LogicalFileEnd(f, Value.Obj(t.StandInRef), fl, at);
                var sb = new System.Text.StringBuilder();
                while (true) { int ch = t.ReadChar(fl); if (ch < 0 || ch == '\n') break; sb.Append((char)ch); }
                return RT.StrValue(sb.ToString());
            }
        });
    }

    static int LwbOf(Value v) => v.O is RowV r ? r.Lwb[0] : 1;
}

static class ModeExt
{
    public static Mode ResultVoidParams(this Mode m) => ((ProcMode)m).Result;
}
