using System.Text;

namespace Studio.Simula.Runtime;

/// <summary>Files opened by the program (BASICIO classes).</summary>
public sealed class SimFile
{
    public int Kind;            // 1 in, 2 out, 3 print, 4 direct, 5 inbyte, 6 outbyte, 7 directbyte ; 100 sysin, 101 sysout
    public string Path = "";
    public TextReader? Reader;
    public TextWriter? Writer;
    public Stream? Bytes;
    public List<string?>? Lines;     // direct file
    public bool Dirty;
    public bool Purge;
    public string? PendingLine;      // inrecord: rest of a line
    public bool Console;
}

static class FileNatives
{
    static TextRef? T(Value v) => (TextRef?)v.O;
    static Value I(long v) => new() { I = v };
    static Value B(bool v) => new() { I = v ? 1 : 0 };
    static readonly Value Nothing = default;

    public static void Register(Action<string, string, NativeFn> def)
    {
        // __fopen(kind, name, modes) -> handle (0 = failure)
        def("__fopen", "ITT>I", (vm, s, b) => I(vm.OpenFile((int)s[b].I, TextRef.Str(T(s[b + 1])), TextRef.Str(T(s[b + 2])))));
        def("__fclose", "I>B", (vm, s, b) => B(vm.CloseFile((int)s[b].I)));
        def("__fexists", "T>B", (vm, s, b) => B(File.Exists(vm.ResolvePath(TextRef.Str(T(s[b]))))));
        // reads one line into image: 0 read, 1 end of file
        def("__finimage", "IT>I", (vm, s, b) =>
        {
            var f = vm.GetFile((int)s[b].I);
            var img = T(s[b + 1]);
            string? line = f.PendingLine ?? vm.ReadLine(f);
            f.PendingLine = null;
            if (line == null) return I(1);
            int len = TextRef.Len(img);
            if (line.Length > len) throw new SimulaError($"inimage: line of {line.Length} characters is longer than the image ({len})");
            for (int i = 0; i < len; i++) img![i] = i < line.Length ? line[i] : ' ';
            img!.Pos = 1;
            return I(0);
        });
        // inrecord: -1 end of file; otherwise 2*count + (1 if the line is incomplete)
        def("__finrecord", "IT>I", (vm, s, b) =>
        {
            var f = vm.GetFile((int)s[b].I);
            var img = T(s[b + 1]);
            string? line = f.PendingLine ?? vm.ReadLine(f);
            f.PendingLine = null;
            if (line == null) return I(-1);
            int len = TextRef.Len(img);
            int n = Math.Min(len, line.Length);
            for (int i = 0; i < n; i++) img![i] = line[i];
            bool partial = line.Length > len;
            if (partial) f.PendingLine = line.Substring(len);
            return I(2L * n + (partial ? 1 : 0));
        });
        def("__foutimage", "IT>V", (vm, s, b) =>
        {
            var str = TextRef.Str(T(s[b + 1]));
            vm.WriteText(vm.GetFile((int)s[b].I), str.TrimEnd(' ') + "\n");
            return Nothing;
        });
        def("__foutrecord", "IT>V", (vm, s, b) => { vm.WriteText(vm.GetFile((int)s[b].I), TextRef.Str(T(s[b + 1])) + "\n"); return Nothing; });
        def("__fbreak", "IT>V", (vm, s, b) => { vm.WriteText(vm.GetFile((int)s[b].I), TextRef.Str(T(s[b + 1])), flush: true); return Nothing; });
        def("__fnewpage", "I>V", (vm, s, b) => { vm.WriteText(vm.GetFile((int)s[b].I), "\f"); return Nothing; });
        def("__fblank", "II>V", (vm, s, b) => { var n = (int)s[b + 1].I; if (n > 0) vm.WriteText(vm.GetFile((int)s[b].I), new string('\n', n)); return Nothing; });
        def("__fflush", "I>B", (vm, s, b) => { var f = vm.GetFile((int)s[b].I); f.Writer?.Flush(); f.Bytes?.Flush(); return B(true); });
        // fichiers directs (images)
        def("__dmaxloc", "I>I", (vm, s, b) => I(int.MaxValue - 1));
        def("__dlastloc", "I>I", (vm, s, b) =>
        {
            var f = vm.GetFile((int)s[b].I);
            var l = f.Lines!;
            int last = l.Count;
            while (last > 0 && l[last - 1] == null) last--;
            return I(last);
        });
        def("__dread", "IIT>I", (vm, s, b) =>
        {
            var f = vm.GetFile((int)s[b].I);
            int loc = (int)s[b + 1].I;
            var img = T(s[b + 2]);
            var l = f.Lines!;
            int len = TextRef.Len(img);
            string? line = loc - 1 < l.Count ? l[loc - 1] : null;
            if (line == null) return I(1);
            for (int i = 0; i < len; i++) img![i] = i < line.Length ? line[i] : ' ';
            img!.Pos = 1;
            return I(0);
        });
        def("__dwrite", "IIT>V", (vm, s, b) =>
        {
            var f = vm.GetFile((int)s[b].I);
            int loc = (int)s[b + 1].I;
            var l = f.Lines!;
            while (l.Count < loc) l.Add(null);
            l[loc - 1] = TextRef.Str(T(s[b + 2])).TrimEnd(' ');
            f.Dirty = true;
            return Nothing;
        });
        def("__ddelete", "II>B", (vm, s, b) =>
        {
            var f = vm.GetFile((int)s[b].I);
            int loc = (int)s[b + 1].I;
            var l = f.Lines!;
            if (loc - 1 >= l.Count || l[loc - 1] == null) return B(false);
            l[loc - 1] = null; f.Dirty = true;
            return B(true);
        });
        // bytes
        def("__binbyte", "I>I", (vm, s, b) => { var f = vm.GetFile((int)s[b].I); return I(f.Bytes!.ReadByte()); });
        def("__boutbyte", "II>V", (vm, s, b) =>
        {
            var f = vm.GetFile((int)s[b].I);
            long x = s[b + 1].I;
            if (x < 0 || x > 255) throw new SimulaError("outbyte: illegal byte value");
            f.Bytes!.WriteByte((byte)x);
            return Nothing;
        });
        def("__bseek", "II>V", (vm, s, b) => { var f = vm.GetFile((int)s[b].I); f.Bytes!.Position = s[b + 1].I - 1; return Nothing; });
        def("__blastloc", "I>I", (vm, s, b) => I(vm.GetFile((int)s[b].I).Bytes!.Length));
        def("__bdread", "II>I", (vm, s, b) =>
        {
            var f = vm.GetFile((int)s[b].I);
            long loc = s[b + 1].I;
            if (loc > f.Bytes!.Length) return I(0);
            f.Bytes.Position = loc - 1;
            int x = f.Bytes.ReadByte();
            return I(x < 0 ? 0 : x);
        });
        def("__bdwrite", "III>V", (vm, s, b) =>
        {
            var f = vm.GetFile((int)s[b].I);
            long loc = s[b + 1].I, x = s[b + 2].I;
            if (x < 0 || x > 255) throw new SimulaError("outbyte: illegal byte value");
            f.Bytes!.Position = loc - 1;
            f.Bytes.WriteByte((byte)x);
            return Nothing;
        });
    }
}
