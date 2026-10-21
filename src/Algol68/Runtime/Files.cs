using System.Text;
using Studio.Algol68.Exec;

namespace Studio.Algol68.Runtime;

public sealed class ChannelV
{
    public string Name = "";
    public bool Reset, Set, Get, Put, Bin, Compress, Reidf, Draw;
    public int Kind;   // 0 stand in, 1 stand out, 2 stand back, 3 stand error, 4 disk
    public override string ToString() => $"canal {Name}";
}

public enum FileKind { StandIn, StandOut, StandError, Disk, Assoc }

/// <summary>File (RR 10.3.1.3): book, current position, event routines, format.</summary>
public sealed class FileV
{
    public ChannelV Channel = null!;
    public FileKind Kind;
    public string Idf = "";
    public string? FullPath;
    public bool Opened;
    public bool ReadMood, WriteMood, CharMood = true, BinMood;
    // in-memory book (disk, associated string)
    public StringBuilder Book = new();
    public int Pos;
    public bool Modified;
    public bool TruncateOnWrite;
    public RefV? AssocRef;
    public RefV? Assoc3;   // book associated with a REF [][][] CHAR (RR 10.3.1.4.e): pages, lines, characters
    // position (RR): page, line, character
    public int Page = 1, Line = 1, Char = 1;
    // standard input
    public string? InLine; public int InPos; public bool InEof;
    // events
    public ProcV? OnLogicalEnd, OnPhysicalEnd, OnLineEnd, OnPageEnd, OnFormatEnd, OnValueError, OnCharError, OnFormatError, OnOpenError, OnTransputError;
    public string Term = "";
    public FormatState? Fmt;
    // bounds of the book (RR 10.3.1.4.cc, establish with p, l, c): 0 = no bound
    public int MaxPage, MaxLine, MaxChar;
    // frame and file name of the transput call in progress (to call the event routines on output)
    public Frame? EvFrame; public Value EvRef;
    public readonly object Lock = new();
    public override string ToString() => Opened ? $"file '{Idf}'" : "closed file";
}

public sealed partial class Transput
{
    readonly ExecContext ctx;
    public readonly ChannelV StandInChannel = new() { Name = "stand in channel", Get = true, Kind = 0, Reidf = false };
    public readonly ChannelV StandOutChannel = new() { Name = "stand out channel", Put = true, Kind = 1 };
    public readonly ChannelV StandBackChannel = new() { Name = "stand back channel", Get = true, Put = true, Bin = true, Reset = true, Set = true, Kind = 2 };
    public readonly ChannelV StandErrorChannel = new() { Name = "stand error channel", Put = true, Kind = 3 };
    public readonly FileV StandIn, StandOut, StandError, StandBack;
    public readonly RefV StandInRef, StandOutRef, StandErrorRef, StandBackRef;
    public int Column;   // console column (for an eventual implicit newline)

    public Transput(ExecContext ctx)
    {
        this.ctx = ctx;
        StandIn = new FileV { Channel = StandInChannel, Kind = FileKind.StandIn, Idf = "stand in", Opened = true, ReadMood = true };
        StandOut = new FileV { Channel = StandOutChannel, Kind = FileKind.StandOut, Idf = "stand out", Opened = true, WriteMood = true };
        StandError = new FileV { Channel = StandErrorChannel, Kind = FileKind.StandError, Idf = "stand error", Opened = true, WriteMood = true };
        StandBack = new FileV { Channel = StandBackChannel, Kind = FileKind.Disk, Idf = "stand back", Opened = true };
        StandInRef = RefV.NewCell(Value.Obj(StandIn), 0);
        StandOutRef = RefV.NewCell(Value.Obj(StandOut), 0);
        StandErrorRef = RefV.NewCell(Value.Obj(StandError), 0);
        StandBackRef = RefV.NewCell(Value.Obj(StandBack), 0);
    }

    /// <summary>Flushes the disk files still open at the end of the execution.</summary>
    public void FlushAll()
    {
        lock (openFiles)
            foreach (var f in openFiles.ToArray())
                try { Flush(f); } catch { }
    }
    readonly List<FileV> openFiles = new();
    // books locked by lock (RR 10.3.1.4.o): they cannot be opened again
    readonly HashSet<string> lockedBooks = new(StringComparer.OrdinalIgnoreCase);
    bool IsLocked(FileV f) { lock (lockedBooks) return f.FullPath != null && lockedBooks.Contains(Path.GetFullPath(f.FullPath)); }

    /// <summary>Records the transput call in progress on f (event routines on output); returns the previous one.</summary>
    internal static (Frame?, Value) EnterEvents(FileV f, Frame fr, Value refFile)
    {
        var old = (f.EvFrame, f.EvRef);
        f.EvFrame = fr; f.EvRef = refFile;
        return old;
    }
    internal static void LeaveEvents(FileV f, (Frame?, Value) old) => (f.EvFrame, f.EvRef) = old;

    public static FileV GetFile(Value refFile, Node at)
    {
        var r = refFile.O as RefV ?? throw RT.Err("REF FILE expected", at);
        if (r.A == null) throw RT.Err("NIL file", at);
        var v = r.A[r.I];
        if (v.O is FileV f) return f;
        throw RT.Err("uninitialised file (use open, establish, create or associate)", at);
    }

    // ------------------------------------------------------------------ opening / closing

    string Resolve(string idf) => Path.IsPathRooted(idf) ? idf : Path.Combine(ctx.IO.WorkingDirectory, idf);

    FileV NewDiskFile(string idf, ChannelV ch)
    {
        var f = new FileV { Channel = ch, Kind = FileKind.Disk, Idf = idf, FullPath = Resolve(idf), Opened = true };
        return f;
    }

    static ChannelV GetChannel(Value v, Node at) => v.O as ChannelV ?? throw RT.Err("uninitialised channel", at);

    void StoreFile(Value refFile, FileV f, Node at)
    {
        var r = refFile.O as RefV ?? throw RT.Err("REF FILE expected", at);
        if (r.A == null) throw RT.Err("NIL file", at);
        r.A[r.I] = Value.Obj(f);
    }

    /// <summary>open: existing book (0 on success, as in a68g).</summary>
    public int Open(Frame fr, Value refFile, string idf, Value channel, Node at)
    {
        var ch = GetChannel(channel, at);
        if (ch.Kind is 0 or 1 or 3)
        {
            var std = ch.Kind == 0 ? StandIn : ch.Kind == 1 ? StandOut : StandError;
            StoreFile(refFile, std, at);
            return 0;
        }
        var f = NewDiskFile(idf, ch);
        if (IsLocked(f)) return Unavailable(fr, refFile, f, at);
        try
        {
            if (File.Exists(f.FullPath)) f.Book.Append(File.ReadAllText(f.FullPath!));
            f.TruncateOnWrite = true;
        }
        catch (Exception e)
        {
            StoreFile(refFile, f, at);
            f.Opened = false;
            if (!OpenError(fr, refFile, f, at)) throw RT.Err($"cannot open file '{idf}': {e.Message}", at);
            return 1;
        }
        StoreFile(refFile, f, at);
        lock (openFiles) openFiles.Add(f);
        return 0;
    }

    /// <summary>establish: new (empty) book; p, l, c (RR form) bound the number of pages, of lines per page and of characters per line.</summary>
    public int Establish(Frame fr, Value refFile, string idf, Value channel, Node at, long p = 0, long l = 0, long c = 0)
    {
        var ch = GetChannel(channel, at);
        if (p < 0 || l < 0 || c < 0 || p > int.MaxValue || l > int.MaxValue || c > int.MaxValue)
            throw RT.Err($"establish: invalid bounds ({p}, {l}, {c})", at);
        var f = NewDiskFile(idf, ch);
        f.MaxPage = (int)p; f.MaxLine = (int)l; f.MaxChar = (int)c;
        if (IsLocked(f)) return Unavailable(fr, refFile, f, at);
        try
        {
            var dir = Path.GetDirectoryName(f.FullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(f.FullPath!, "");
        }
        catch (Exception e)
        {
            StoreFile(refFile, f, at);
            f.Opened = false;
            if (!OpenError(fr, refFile, f, at)) throw RT.Err($"cannot create file '{idf}': {e.Message}", at);
            return 1;
        }
        f.Modified = true;
        StoreFile(refFile, f, at);
        lock (openFiles) openFiles.Add(f);
        return 0;
    }

    public int Create(Frame fr, Value refFile, Value channel, Node at)
    {
        string idf = "a68_" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        return Establish(fr, refFile, idf, channel, at);
    }

    /// <summary>reidf (RR 10.3.1.3.s): new identification of the book of a disk file.</summary>
    public void Reidf(FileV f, string idf, Node at)
    {
        lock (f.Lock)
        {
            if (f.Kind != FileKind.Disk || f.FullPath == null) { f.Idf = idf; return; }
            var to = Resolve(idf);
            try
            {
                Flush(f);
                if (File.Exists(f.FullPath)) File.Move(f.FullPath, to, true);
            }
            catch (Exception e) { throw RT.Err($"reidf: cannot rename '{f.Idf}' to '{idf}': {e.Message}", at); }
            f.FullPath = to; f.Idf = idf;
        }
    }

    /// <summary>lock (RR 10.3.1.4.o): closes the file; its book cannot be opened again.</summary>
    public void Lock(Value refFile, Node at)
    {
        var f = GetFile(refFile, at);
        Close(refFile, at);
        if (f.FullPath != null) lock (lockedBooks) lockedBooks.Add(Path.GetFullPath(f.FullPath));
    }

    /// <summary>associate (RR 10.3.1.4.e): the book is the row of pages of lines of characters referred to by r.</summary>
    public void Associate3(Value refFile, RefV r, Node at)
    {
        if (r.A == null) throw RT.Err("associate: NIL row", at);
        var f = new FileV
        {
            Channel = new ChannelV { Name = "associated book", Get = true, Put = true, Reset = true, Set = true, Kind = 4 },
            Kind = FileKind.Assoc, Idf = "", Opened = true, Assoc3 = r,
        };
        LoadAssoc3(f, at);
        StoreFile(refFile, f, at);
    }

    static RowV Row1(Value v, Node at) => v.O as RowV ?? throw RT.Err("associate: uninitialised row", at);
    static Value Elem1(RowV r, int k) => r.GetAt(r.Index1(r.Lwb[0] + k - 1));

    /// <summary>Text image of the associated row: each line ends with a new line, each page with a new page.</summary>
    static void LoadAssoc3(FileV f, Node at)
    {
        var b = f.Book; b.Clear();
        var pages = Row1(f.Assoc3!.A![f.Assoc3.I], at);
        for (int p = 1; p <= pages.Length1; p++)
        {
            var lines = Row1(Elem1(pages, p), at);
            for (int l = 1; l <= lines.Length1; l++)
            {
                var chars = Row1(Elem1(lines, l), at);
                for (int c = 1; c <= chars.Length1; c++)
                {
                    var v = Elem1(chars, c);
                    b.Append(ReferenceEquals(v.O, Value.Uninit) ? ' ' : (char)v.N);
                }
                b.Append('\n');
            }
            b.Append('\f');
        }
    }

    /// <summary>Writes one character into an associated row (the position is good: see LineFull).</summary>
    void WriteAssoc3(FileV f, char c, Node at)
    {
        var b = f.Book;
        switch (c)
        {
            case '\n':
                if (f.Pos < b.Length && b[f.Pos] != '\f')
                {
                    while (f.Pos < b.Length && b[f.Pos] != '\n') f.Pos++;
                    if (f.Pos < b.Length) f.Pos++;
                }
                f.Line++; f.Char = 1;
                return;
            case '\f':
                while (f.Pos < b.Length && b[f.Pos] != '\f') f.Pos++;
                if (f.Pos < b.Length) f.Pos++;
                f.Page++; f.Line = 1; f.Char = 1;
                return;
            case '\b':
                if (f.Char > 1) { f.Pos--; f.Char--; }
                return;
        }
        if (LineFull(f)) throw RT.Err("the line of the associated book is full", at);
        b[f.Pos] = c;
        var chars = Row1(Elem1(Row1(Elem1(Row1(f.Assoc3!.A![f.Assoc3.I], at), f.Page), at), f.Line), at);
        chars.SetAt(chars.Index1(chars.Lwb[0] + f.Char - 1), Value.I(c));
        f.Pos++; f.Char++;
    }

    // RR 10.3.1.5: line ended, page ended, physical file ended (books with bounds)
    static bool LineFull(FileV f) => f.Assoc3 != null ? f.Pos >= f.Book.Length || f.Book[f.Pos] is '\n' or '\f' : f.MaxChar > 0 && f.Char > f.MaxChar;
    static bool PageFull(FileV f) => f.Assoc3 != null ? f.Pos >= f.Book.Length || f.Book[f.Pos] == '\f' : f.MaxLine > 0 && f.Line > f.MaxLine;
    static bool FileFull(FileV f) => f.Assoc3 != null ? f.Pos >= f.Book.Length : f.MaxPage > 0 && f.Page > f.MaxPage;
    internal static bool Bounded(FileV f) => f.Assoc3 != null || (f.MaxChar | f.MaxLine | f.MaxPage) != 0;

    /// <summary>Characters still available on the current line, and length of the line.</summary>
    static (int room, int length) LineRoom(FileV f)
    {
        if (f.Assoc3 == null) return (f.MaxChar - f.Char + 1, f.MaxChar);
        int i = f.Pos;
        while (i < f.Book.Length && f.Book[i] is not ('\n' or '\f')) i++;
        return (i - f.Pos, f.Char - 1 + i - f.Pos);
    }

    public void Associate(Value refFile, Value refString, Node at)
    {
        var rs = refString.O as RefV ?? throw RT.Err("REF STRING expected", at);
        if (rs.A == null) throw RT.Err("associate: NIL string", at);
        var f = new FileV
        {
            Channel = new ChannelV { Name = "associated string", Get = true, Put = true, Reset = true, Set = true, Kind = 4 },
            Kind = FileKind.Assoc, Idf = "", Opened = true, AssocRef = rs,
        };
        var cur = rs.A[rs.I];
        if (cur.O is RowV row) f.Book.Append(row.AsString());
        StoreFile(refFile, f, at);
    }

    /// <summary>The book is not available (locked): the file stays closed and the status is 1 (RR 10.3.1.4).</summary>
    int Unavailable(Frame fr, Value refFile, FileV f, Node at)
    {
        StoreFile(refFile, f, at);
        f.Opened = false;
        OpenError(fr, refFile, f, at);
        return 1;
    }

    bool OpenError(Frame fr, Value refFile, FileV f, Node at)
    {
        if (f.OnOpenError == null) return false;
        return CallEvent(fr, f.OnOpenError, refFile, at);
    }

    public void Flush(FileV f)
    {
        if (f.Kind == FileKind.Disk && f.Modified && f.FullPath != null)
        {
            File.WriteAllText(f.FullPath, f.Book.ToString());
            f.Modified = false;
        }
    }

    public void Close(Value refFile, Node at, bool scratch = false)
    {
        var f = GetFile(refFile, at);
        if (f.Kind is FileKind.StandIn or FileKind.StandOut or FileKind.StandError) return;
        if (!f.Opened) throw RT.Err("close: file not open", at);
        try
        {
            if (scratch) { if (f.FullPath != null && File.Exists(f.FullPath)) File.Delete(f.FullPath); }
            else Flush(f);
        }
        catch (Exception e) { throw RT.Err($"error while closing '{f.Idf}': {e.Message}", at); }
        f.Opened = false;
        lock (openFiles) openFiles.Remove(f);
    }

    // ------------------------------------------------------------------ events

    /// <summary>Calls an event routine PROC (REF FILE) BOOL.</summary>
    public bool CallEvent(Frame fr, ProcV p, Value refFile, Node at)
    {
        var v = Calls.Invoke(p, new[] { refFile }, fr, at, fr.Depth + 1000);
        return v.N != 0;
    }

    /// <summary>Logical end of file while reading: event or error.</summary>
    public void LogicalFileEnd(Frame fr, Value refFile, FileV f, Node at)
    {
        var p = f.OnLogicalEnd ?? f.OnPhysicalEnd;
        if (p != null && CallEvent(fr, p, refFile, at)) return;
        throw RT.Err($"end of file reached while reading ({f.Idf})", at);
    }

    void CheckOpen(FileV f, Node at)
    {
        if (!f.Opened) throw RT.Err($"file '{f.Idf}' is not open", at);
    }

    void SetWriteMood(FileV f, Node at)
    {
        CheckOpen(f, at);
        if (!f.Channel.Put) throw RT.Err($"the channel of '{f.Idf}' does not allow writing", at);
        if (f.ReadMood && f.Kind == FileKind.Disk && !f.WriteMood)
        {
            // switch to writing
        }
        if (!f.WriteMood)
        {
            f.WriteMood = true; f.ReadMood = false;
            if (f.Kind == FileKind.Disk && f.TruncateOnWrite) { f.Book.Clear(); f.Pos = 0; f.TruncateOnWrite = false; f.Modified = true; f.Line = f.Char = f.Page = 1; }
        }
    }

    void SetReadMood(FileV f, Node at)
    {
        CheckOpen(f, at);
        if (!f.Channel.Get) throw RT.Err($"the channel of '{f.Idf}' does not allow reading", at);
        if (!f.ReadMood)
        {
            if (f.WriteMood && f.Kind == FileKind.Disk) Flush(f);
            f.ReadMood = true; f.WriteMood = false;
        }
    }

    // ------------------------------------------------------------------ writing

    public void WriteString(FileV f, string s, Node at)
    {
        if (s.Length == 0) return;
        if (Bounded(f))
        {
            // bounded book: each character is written at a good position (RR 10.3.1.6.e-g)
            foreach (char c in s)
            {
                if (c == '\n') GetGoodPage(f, at);
                else if (c == '\f') GetGoodFile(f, at);
                else if (c != '\b') GetGoodLine(f, at);
                RawWrite(f, c.ToString(), at);
            }
            return;
        }
        RawWrite(f, s, at);
    }

    /// <summary>Calls an event routine during output, the lock of the file being released.</summary>
    bool OutputEvent(FileV f, ProcV? p, Node at)
    {
        if (p == null || f.EvFrame == null) return false;
        bool held = Monitor.IsEntered(f.Lock);
        if (held) Monitor.Exit(f.Lock);
        try { return CallEvent(f.EvFrame, p, f.EvRef, at); }
        finally { if (held) Monitor.Enter(f.Lock); }
    }

    // RR 10.3.1.6.e: get good line (on line end, then newline)
    void GetGoodLine(FileV f, Node at)
    {
        for (int guard = 0; ; guard++)
        {
            GetGoodPage(f, at);
            if (!LineFull(f)) return;
            int line = f.Line, page = f.Page;
            bool mended = OutputEvent(f, f.OnLineEnd, at);
            // default action (or the routine left the position unchanged): newline
            if (!mended || (LineFull(f) && f.Line == line && f.Page == page)) { GetGoodPage(f, at); RawWrite(f, "\n", at); }
            if (guard > 1000) throw RT.Err("on line end: the line remains full", at);
        }
    }

    // RR 10.3.1.6.f: get good page (on page end, then newpage)
    void GetGoodPage(FileV f, Node at)
    {
        for (int guard = 0; ; guard++)
        {
            GetGoodFile(f, at);
            if (!PageFull(f)) return;
            int page = f.Page;
            bool mended = OutputEvent(f, f.OnPageEnd, at);
            if (!mended || (PageFull(f) && f.Page == page)) { GetGoodFile(f, at); RawWrite(f, "\f", at); }
            if (guard > 1000) throw RT.Err("on page end: the page remains full", at);
        }
    }

    // RR 10.3.1.6.g: get good file (on physical file end)
    void GetGoodFile(FileV f, Node at)
    {
        if (!FileFull(f)) return;
        if (!OutputEvent(f, f.OnPhysicalEnd, at) || FileFull(f))
            throw RT.Err(f.Assoc3 != null ? "physical end of the associated book reached"
                : $"physical end of file '{f.Idf}' reached ({f.MaxPage} pages of {f.MaxLine} lines of {f.MaxChar} characters)", at);
    }

    /// <summary>Formatless output of a number (RR 10.3.3.1.a): the whole number goes on one line.</summary>
    internal void FitOnLine(FileV f, int n, Node at)
    {
        if (!Bounded(f)) return;
        for (int guard = 0; ; guard++)
        {
            GetGoodLine(f, at);
            var (room, length) = LineRoom(f);
            if (n > length) throw RT.Err($"a number of {n} characters does not fit on a line of {length} characters", at);
            if (n <= room) return;
            int line = f.Line, page = f.Page;
            bool mended = OutputEvent(f, f.OnLineEnd, at);
            if (!mended || (f.Line == line && f.Page == page)) { GetGoodPage(f, at); RawWrite(f, "\n", at); }
            if (guard > 1000) throw RT.Err("on line end: the line remains full", at);
        }
    }

    void RawWrite(FileV f, string s, Node at)
    {
        if (f.Assoc3 != null) { foreach (char c in s) WriteAssoc3(f, c, at); return; }
        switch (f.Kind)
        {
            case FileKind.StandOut:
            case FileKind.StandError:
                ctx.IO.Write(s);
                break;
            case FileKind.Disk:
                if (f.Pos >= f.Book.Length) f.Book.Append(s);
                else
                {
                    int over = Math.Min(s.Length, f.Book.Length - f.Pos);
                    f.Book.Remove(f.Pos, over);
                    f.Book.Insert(f.Pos, s);
                }
                f.Pos += s.Length;
                f.Modified = true;
                break;
            case FileKind.Assoc:
                if (f.Pos >= f.Book.Length) f.Book.Append(s);
                else { int over = Math.Min(s.Length, f.Book.Length - f.Pos); f.Book.Remove(f.Pos, over); f.Book.Insert(f.Pos, s); }
                f.Pos += s.Length;
                f.AssocRef!.A![f.AssocRef.I] = RT.StrValue(f.Book.ToString());
                break;
            default:
                throw RT.Err($"cannot write to '{f.Idf}'", at);
        }
        foreach (char c in s)
        {
            if (c == '\n') { f.Line++; f.Char = 1; }
            else if (c == '\f') { f.Page++; f.Line = 1; f.Char = 1; }
            else if (c == '\b') { if (f.Char > 1) f.Char--; }
            else f.Char++;
        }
    }

    public void Put(Frame fr, Value refFile, FileV f, string s, Node at)
    {
        lock (f.Lock)
        {
            SetWriteMood(f, at);
            WriteString(f, s, at);
        }
    }

    // ------------------------------------------------------------------ reading character by character

    /// <summary>Next character (-1 at end of file) without consuming it.</summary>
    public int PeekChar(FileV f)
    {
        switch (f.Kind)
        {
            case FileKind.StandIn:
                if (f.InEof) return -1;
                if (f.InLine == null || f.InPos > f.InLine.Length)
                {
                    var l = ctx.IO.ReadLine();
                    if (l == null) { f.InEof = true; return -1; }
                    f.InLine = l; f.InPos = 0;
                }
                return f.InPos < f.InLine.Length ? f.InLine[f.InPos] : '\n';
            case FileKind.Disk:
            case FileKind.Assoc:
                return f.Pos < f.Book.Length ? f.Book[f.Pos] : -1;
        }
        return -1;
    }

    public int ReadChar(FileV f)
    {
        int c = PeekChar(f);
        if (c < 0) return -1;
        if (f.Kind == FileKind.StandIn) f.InPos++;
        else f.Pos++;
        if (c == '\n') { f.Line++; f.Char = 1; }
        else if (c == '\f') { f.Page++; f.Line = 1; f.Char = 1; }
        else f.Char++;
        return c;
    }

    public bool AtEof(FileV f) => PeekChar(f) < 0;

    // ------------------------------------------------------------------ layout

    public void Newline(Frame fr, Value refFile, Node at) => Layout(fr, refFile, '\n', at);
    public void Newpage(Frame fr, Value refFile, Node at) => Layout(fr, refFile, '\f', at);
    public void Space(Frame fr, Value refFile, Node at) => Layout(fr, refFile, ' ', at);

    /// <summary>newline, newpage and space (RR 10.3.1.6.a, c, d).</summary>
    void Layout(Frame fr, Value refFile, char what, Node at)
    {
        var f = GetFile(refFile, at);
        lock (f.Lock)
        {
            CheckOpen(f, at);
            if (f.ReadMood || (!f.WriteMood && f.Channel.Get && !f.Channel.Put))
            {
                SetReadMood(f, at);
                if (what == ' ') { if (ReadChar(f) < 0) LogicalFileEnd(fr, refFile, f, at); return; }
                while (true)
                {
                    int c = ReadChar(f);
                    if (c < 0) { LogicalFileEnd(fr, refFile, f, at); return; }
                    if (c == what) return;
                }
            }
            var ev = EnterEvents(f, fr, refFile);
            try
            {
                SetWriteMood(f, at);
                WriteString(f, what.ToString(), at);
            }
            finally { LeaveEvents(f, ev); }
        }
    }

    public void Backspace(Frame fr, Value refFile, Node at)
    {
        var f = GetFile(refFile, at);
        lock (f.Lock)
        {
            CheckOpen(f, at);
            if (f.Char <= 1) return;
            if (f.ReadMood)
            {
                if (f.Kind == FileKind.StandIn) { if (f.InPos > 0) { f.InPos--; f.Char--; } }
                else if (f.Pos > 0) { f.Pos--; f.Char--; }
                return;
            }
            if (f.Kind is FileKind.Disk or FileKind.Assoc) { f.Pos--; f.Char--; }
            else WriteString(f, "\b", at);
        }
    }

    /// <summary>set char number (RR 10.3.1.6).</summary>
    public void SetCharNumber(Frame fr, Value refFile, long n, Node at)
    {
        var f = GetFile(refFile, at);
        lock (f.Lock)
        {
            CheckOpen(f, at);
            if (n < 1) throw RT.Err($"set char number: invalid position {n}", at);
            if (f.ReadMood)
            {
                while (f.Char < n)
                {
                    int c = PeekChar(f);
                    if (c < 0 || c == '\n') throw RT.Err("set char number: end of line reached", at);
                    ReadChar(f);
                }
                while (f.Char > n) Backspace(fr, refFile, at);
                return;
            }
            SetWriteMood(f, at);
            if (f.Char < n) WriteString(f, new string(' ', (int)(n - f.Char)), at);
            else if (f.Char > n)
            {
                if (f.Kind is FileKind.Disk or FileKind.Assoc) { int back = (int)(f.Char - n); f.Pos -= back; f.Char -= back; }
                else WriteString(f, new string('\b', (int)(f.Char - n)), at);
            }
        }
    }

    /// <summary>reset: back to the start of the book.</summary>
    public void Reset(Value refFile, Node at)
    {
        var f = GetFile(refFile, at);
        lock (f.Lock)
        {
            if (f.Kind is FileKind.StandIn or FileKind.StandOut or FileKind.StandError)
            {
                if (!f.Channel.Reset) throw RT.Err($"reset is not possible on '{f.Idf}'", at);
                return;
            }
            if (f.WriteMood) Flush(f);
            f.Pos = 0; f.Line = f.Char = f.Page = 1;
            f.ReadMood = f.WriteMood = false;
            f.TruncateOnWrite = false;
            if (f.Assoc3 != null) LoadAssoc3(f, at);
            else if (f.Kind == FileKind.Assoc && f.AssocRef!.A![f.AssocRef.I].O is RowV row)
            {
                f.Book.Clear(); f.Book.Append(row.AsString());
            }
        }
    }

    /// <summary>set (RR): moves to page p, line l, character c.</summary>
    public void SetPos(Frame fr, Value refFile, long p, long l, long c, Node at)
    {
        var f = GetFile(refFile, at);
        lock (f.Lock)
        {
            if (f.Kind is not (FileKind.Disk or FileKind.Assoc) || !f.Channel.Set) throw RT.Err($"set is not possible on '{f.Idf}'", at);
            bool write = f.WriteMood;
            int pos = 0, page = 1, line = 1, ch = 1;
            var b = f.Book;
            while (pos < b.Length && (page < p || (page == p && line < l)))
            {
                char x = b[pos++];
                if (x == '\n') { line++; }
                else if (x == '\f') { page++; line = 1; }
            }
            if (page != p || line != l) throw RT.Err($"set: position ({p}, {l}, {c}) is outside the book", at);
            while (ch < c && pos < b.Length && b[pos] != '\n' && b[pos] != '\f') { pos++; ch++; }
            if (ch < c) throw RT.Err($"set: position ({p}, {l}, {c}) is outside the book", at);
            f.Pos = pos; f.Page = page; f.Line = line; f.Char = ch;
            _ = write;
        }
    }

    /// <summary>seek (a68g): moves to character n (from the start), returns the old position.</summary>
    public int Seek(Value refFile, long n, Node at)
    {
        var f = GetFile(refFile, at);
        lock (f.Lock)
        {
            if (f.Kind is not (FileKind.Disk or FileKind.Assoc)) throw RT.Err($"seek is not possible on '{f.Idf}'", at);
            int old = f.Pos;
            f.Pos = (int)Math.Clamp(n, 0, f.Book.Length);
            // recompute the RR position
            f.Page = 1; f.Line = 1; f.Char = 1;
            for (int i = 0; i < f.Pos; i++)
            {
                char x = f.Book[i];
                if (x == '\n') { f.Line++; f.Char = 1; } else if (x == '\f') { f.Page++; f.Line = 1; f.Char = 1; } else f.Char++;
            }
            return old;
        }
    }
}
