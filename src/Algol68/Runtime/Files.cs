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
    // position (RR): page, line, character
    public int Page = 1, Line = 1, Char = 1;
    // standard input
    public string? InLine; public int InPos; public bool InEof;
    // events
    public ProcV? OnLogicalEnd, OnPhysicalEnd, OnLineEnd, OnPageEnd, OnFormatEnd, OnValueError, OnCharError, OnFormatError, OnOpenError, OnTransputError;
    public string Term = "";
    public FormatState? Fmt;
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

    /// <summary>establish: new (empty) book.</summary>
    public int Establish(Frame fr, Value refFile, string idf, Value channel, Node at)
    {
        var ch = GetChannel(channel, at);
        var f = NewDiskFile(idf, ch);
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

    public void Newline(Frame fr, Value refFile, Node at)
    {
        var f = GetFile(refFile, at);
        lock (f.Lock)
        {
            CheckOpen(f, at);
            if (f.ReadMood || (!f.WriteMood && f.Channel.Get && !f.Channel.Put))
            {
                SetReadMood(f, at);
                while (true)
                {
                    int c = ReadChar(f);
                    if (c < 0) { LogicalFileEnd(fr, refFile, f, at); return; }
                    if (c == '\n') return;
                }
            }
            SetWriteMood(f, at);
            WriteString(f, "\n", at);
        }
    }

    public void Newpage(Frame fr, Value refFile, Node at)
    {
        var f = GetFile(refFile, at);
        lock (f.Lock)
        {
            CheckOpen(f, at);
            if (f.ReadMood || (!f.WriteMood && f.Channel.Get && !f.Channel.Put))
            {
                SetReadMood(f, at);
                while (true)
                {
                    int c = ReadChar(f);
                    if (c < 0) { LogicalFileEnd(fr, refFile, f, at); return; }
                    if (c == '\f') return;
                }
            }
            SetWriteMood(f, at);
            WriteString(f, "\f", at);
        }
    }

    public void Space(Frame fr, Value refFile, Node at)
    {
        var f = GetFile(refFile, at);
        lock (f.Lock)
        {
            CheckOpen(f, at);
            if (f.ReadMood || (!f.WriteMood && f.Channel.Get && !f.Channel.Put))
            {
                SetReadMood(f, at);
                if (ReadChar(f) < 0) LogicalFileEnd(fr, refFile, f, at);
                return;
            }
            SetWriteMood(f, at);
            WriteString(f, " ", at);
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
            if (f.Kind == FileKind.Assoc && f.AssocRef!.A![f.AssocRef.I].O is RowV row)
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
