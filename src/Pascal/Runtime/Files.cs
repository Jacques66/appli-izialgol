using System.Globalization;
using System.Text;
using Studio.Pascal.Exec;
using Studio.Pascal.Semantics;

namespace Studio.Pascal.Runtime;

public enum FileMode { Undefined, Inspection, Generation }

/// <summary>
/// File variable (Revised Report 6.2.4, 12). The contents are kept in memory; files listed in the program
/// heading (other than input and output) are external files of the working directory: they are loaded at
/// the first reset and written back after rewrite (at the next reset and at the end of the program).
/// input and output are the console. Other files are internal (temporary) files.
/// </summary>
public sealed class PFile
{
    public readonly FileType Type;
    public readonly string Name;
    public readonly ExecContext Ctx;
    public string? ExternalPath;
    public readonly bool ConsoleIn, ConsoleOut;
    public FileMode Mode;
    public readonly Cell[] Buffer;
    bool dirty, loaded;

    // text files
    readonly List<string> lines = new();
    readonly StringBuilder outLine = new();
    int lineIdx;
    string? curLine;
    int col;
    bool needFetch;
    int consoleCol;       // characters written on the current console line

    // other files
    readonly List<Cell[]> elems = new();
    int pos;

    public PFile(FileType type, string name, ExecContext ctx, bool consoleIn = false, bool consoleOut = false)
    {
        Type = type; Name = name; Ctx = ctx; ConsoleIn = consoleIn; ConsoleOut = consoleOut;
        Buffer = new Cell[type.IsText ? 1 : Math.Max(1, type.Elem.Size)];
        Array.Fill(Buffer, Cell.Undefined);
        if (consoleIn) { Mode = FileMode.Inspection; needFetch = true; }
        if (consoleOut) Mode = FileMode.Generation;
    }

    public bool IsText => Type.IsText;

    PasError Err(string msg, Node at) => RT.Err($"file {Name}: {msg}", at);

    public override string ToString()
    {
        string kind = IsText ? "text" : "file";
        string m = Mode switch { FileMode.Inspection => "reading", FileMode.Generation => "writing", _ => "not open" };
        string where = ConsoleIn || ConsoleOut ? "console" : ExternalPath != null ? Path.GetFileName(ExternalPath) : "internal";
        string extra = "";
        if (Mode == FileMode.Inspection && IsText && !ConsoleIn) extra = $", line {lineIdx + (needFetch ? 1 : 0)}";
        if (Mode == FileMode.Inspection && !IsText) extra = $", position {pos}";
        if (Mode == FileMode.Generation && !IsText && !ConsoleOut) extra = $", {elems.Count} elements";
        if (Mode == FileMode.Generation && IsText && !ConsoleOut) extra = $", {lines.Count} lines";
        return $"{kind} ({where}, {m}{extra})";
    }

    // ------------------------------------------------------------------ opening

    public void Reset(Node at)
    {
        if (ConsoleOut) throw Err("reset of the output file is not possible", at);
        if (ConsoleIn) { Mode = FileMode.Inspection; return; }   // the console cannot be rewound
        if (Mode == FileMode.Generation && IsText && outLine.Length > 0) { lines.Add(outLine.ToString()); outLine.Clear(); }
        if (ExternalPath != null)
        {
            if (dirty) Flush();
            else if (!loaded) Load(at);
        }
        Mode = FileMode.Inspection;
        if (IsText)
        {
            lineIdx = -1; needFetch = true; curLine = null; col = 0;
        }
        else
        {
            pos = 0;
            LoadBuffer();
        }
    }

    public void Rewrite(Node at)
    {
        if (ConsoleIn) throw Err("rewrite of the input file is not possible", at);
        if (ConsoleOut) { Mode = FileMode.Generation; return; }
        Mode = FileMode.Generation;
        lines.Clear(); outLine.Clear(); elems.Clear();
        Array.Fill(Buffer, Cell.Undefined);
        loaded = true;
        if (ExternalPath != null) dirty = true;
    }

    void Load(Node at)
    {
        loaded = true;
        lines.Clear(); elems.Clear();
        if (ExternalPath == null || !File.Exists(ExternalPath)) return;   // a missing external file is empty
        try
        {
            if (IsText)
            {
                var text = File.ReadAllText(ExternalPath, Encoding.UTF8).Replace("\r\n", "\n").Replace('\r', '\n');
                if (text.EndsWith('\n')) text = text[..^1];
                if (text.Length == 0 && new FileInfo(ExternalPath).Length == 0) return;
                foreach (var l in text.Split('\n'))
                    lines.Add(new string(l.Select(c => c > 255 ? '?' : c).ToArray()));
            }
            else
            {
                using var br = new BinaryReader(File.OpenRead(ExternalPath));
                if (br.BaseStream.Length == 0) return;
                var magic = br.ReadBytes(4);
                if (Encoding.ASCII.GetString(magic) != "PASF") throw Err($"'{Path.GetFileName(ExternalPath)}' is not a Pascal data file", at);
                int size = br.ReadInt32();
                if (size != Buffer.Length) throw Err($"'{Path.GetFileName(ExternalPath)}' holds elements of another type ({size} cells instead of {Buffer.Length})", at);
                while (br.BaseStream.Position < br.BaseStream.Length)
                {
                    var e = new Cell[size];
                    for (int i = 0; i < size; i++)
                    {
                        byte tag = br.ReadByte();
                        e[i] = tag switch
                        {
                            1 => Cell.Ord(br.ReadInt64()),
                            2 => Cell.Obj(new PSet(br.ReadUInt64(), br.ReadUInt64(), br.ReadUInt64(), br.ReadUInt64())),
                            _ => Cell.Undefined,
                        };
                    }
                    elems.Add(e);
                }
            }
        }
        catch (EndOfStreamException) { throw Err("truncated data file", at); }
        catch (IOException e) { throw Err("input/output error: " + e.Message, at); }
    }

    /// <summary>Writes an external file generated by the program to the disk.</summary>
    public void Flush()
    {
        if (ExternalPath == null || !dirty) return;
        dirty = false;
        if (IsText)
        {
            var sb = new StringBuilder();
            foreach (var l in lines) sb.Append(l).Append('\n');
            if (outLine.Length > 0) sb.Append(outLine).Append('\n');   // the last line is closed implicitly
            File.WriteAllText(ExternalPath, sb.ToString(), new UTF8Encoding(false));
        }
        else
        {
            using var bw = new BinaryWriter(File.Create(ExternalPath));
            bw.Write(Encoding.ASCII.GetBytes("PASF"));
            bw.Write(Buffer.Length);
            foreach (var e in elems)
                foreach (var c in e)
                {
                    if (c.IsUndef) bw.Write((byte)0);
                    else if (c.O is PSet s) { bw.Write((byte)2); bw.Write(s.W0); bw.Write(s.W1); bw.Write(s.W2); bw.Write(s.W3); }
                    else { bw.Write((byte)1); bw.Write(c.N); }
                }
        }
    }

    // ------------------------------------------------------------------ inspection (reading)

    void CheckRead(Node at)
    {
        if (Mode != FileMode.Inspection)
            throw Err(Mode == FileMode.Generation ? "reading a file that is being written (call reset first)" : "file not opened for reading (call reset first)", at);
    }

    void Fetch()
    {
        if (!needFetch) return;
        needFetch = false;
        col = 0;
        if (ConsoleIn)
        {
            Ctx.FlushOutput();
            curLine = Ctx.IO.ReadLine();
            if (curLine != null) curLine = new string(curLine.Select(c => c > 255 ? '?' : c).ToArray());
            lineIdx++;
            return;
        }
        lineIdx++;
        curLine = lineIdx < lines.Count ? lines[lineIdx] : null;
    }

    void LoadBuffer()
    {
        if (pos < elems.Count) Array.Copy(elems[pos], Buffer, Buffer.Length);
        else Array.Fill(Buffer, Cell.Undefined);
    }

    public bool Eof(Node at)
    {
        if (Mode == FileMode.Generation) return true;
        if (Mode == FileMode.Undefined) throw Err("eof of a file that is not open (call reset or rewrite first)", at);
        if (IsText) { Fetch(); return curLine == null; }
        return pos >= elems.Count;
    }

    public bool Eoln(Node at)
    {
        if (!IsText) throw Err("eoln applies to text files only", at);
        CheckRead(at);
        Fetch();
        if (curLine == null) throw Err("eoln at end of file", at);
        return col >= curLine.Length;
    }

    /// <summary>Current character (blank at the end of a line).</summary>
    public char Peek(Node at)
    {
        CheckRead(at);
        Fetch();
        if (curLine == null) throw Err("attempt to read past the end of the file", at);
        return col < curLine.Length ? curLine[col] : ' ';
    }

    bool AtEoln => curLine != null && col >= curLine.Length;

    public void Get(Node at)
    {
        CheckRead(at);
        if (IsText)
        {
            Fetch();
            if (curLine == null) throw Err("get past the end of the file", at);
            if (col >= curLine.Length) needFetch = true;
            else col++;
            return;
        }
        if (pos >= elems.Count) throw Err("get past the end of the file", at);
        pos++;
        LoadBuffer();
    }

    /// <summary>Buffer variable f^.</summary>
    public Ref BufferRef(Node at)
    {
        if (IsText && Mode == FileMode.Inspection)
        {
            Fetch();
            Buffer[0] = curLine == null ? Cell.Undefined : Cell.Ord(col < curLine.Length ? curLine[col] : ' ');
        }
        return new Ref(Buffer, 0);
    }

    public void ReadLn(Node at)
    {
        if (!IsText) throw Err("readln applies to text files only", at);
        CheckRead(at);
        Fetch();
        if (curLine == null) throw Err("readln past the end of the file", at);
        needFetch = true;
    }

    public char ReadChar(Node at)
    {
        char c = Peek(at);
        Get(at);
        return c;
    }

    void SkipBlanks(Node at, string what)
    {
        while (true)
        {
            CheckRead(at);
            Fetch();
            if (curLine == null) throw Err($"end of file reached while reading {what}", at);
            if (AtEoln) { needFetch = true; continue; }
            char c = curLine[col];
            if (c == ' ' || c == '\t') { col++; continue; }
            return;
        }
    }

    string Describe(char c) => c == ' ' ? (AtEoln ? "end of line" : "' '") : $"'{c}'";

    public long ReadInt(Node at)
    {
        SkipBlanks(at, "an integer");
        bool neg = false;
        char c = curLine![col];
        if (c is '+' or '-') { neg = c == '-'; col++; }
        if (AtEoln || !char.IsDigit(curLine[col]))
            throw Err($"digit expected while reading an integer, found {Describe(AtEoln ? ' ' : curLine[col])}", at);
        Int128 v = 0;
        while (!AtEoln && char.IsDigit(curLine[col]))
        {
            v = v * 10 + (curLine[col] - '0');
            if (v > RT.MaxInt) throw Err("integer read exceeds maxint", at);
            col++;
        }
        return neg ? -(long)v : (long)v;
    }

    public double ReadReal(Node at)
    {
        SkipBlanks(at, "a real number");
        var sb = new StringBuilder();
        char c = curLine![col];
        if (c is '+' or '-') { sb.Append(c); col++; }
        if (AtEoln || !char.IsDigit(curLine[col]))
            throw Err($"digit expected while reading a real number, found {Describe(AtEoln ? ' ' : curLine[col])}", at);
        while (!AtEoln && char.IsDigit(curLine[col])) sb.Append(curLine[col++]);
        if (!AtEoln && curLine[col] == '.' && col + 1 < curLine.Length && char.IsDigit(curLine[col + 1]))
        {
            sb.Append(curLine[col++]);
            while (!AtEoln && char.IsDigit(curLine[col])) sb.Append(curLine[col++]);
        }
        if (!AtEoln && curLine[col] is 'e' or 'E')
        {
            int save = col;
            var e = new StringBuilder("E");
            col++;
            if (!AtEoln && curLine[col] is '+' or '-') e.Append(curLine[col++]);
            if (!AtEoln && char.IsDigit(curLine[col]))
            {
                while (!AtEoln && char.IsDigit(curLine[col])) e.Append(curLine[col++]);
                sb.Append(e);
            }
            else col = save;
        }
        if (!double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d))
            throw Err($"real number {sb} out of range", at);
        return d;
    }

    /// <summary>read(f, v) for a non-text file: v := f^; get(f).</summary>
    public void ReadElem(Ref dst, Node at)
    {
        CheckRead(at);
        if (pos >= elems.Count) throw Err("attempt to read past the end of the file", at);
        Array.Copy(Buffer, 0, dst.S, dst.I, Buffer.Length);
        Get(at);
    }

    // ------------------------------------------------------------------ generation (writing)

    void CheckWrite(Node at)
    {
        if (Mode != FileMode.Generation)
            throw Err(Mode == FileMode.Inspection ? "writing to a file that is being read (call rewrite first)" : "file not opened for writing (call rewrite first)", at);
    }

    public void WriteText(string s, Node at)
    {
        CheckWrite(at);
        if (ConsoleOut) { Ctx.Out(s); consoleCol += s.Length; return; }
        outLine.Append(s);
    }

    public void WriteLn(Node at)
    {
        if (!IsText) throw Err("writeln applies to text files only", at);
        CheckWrite(at);
        if (ConsoleOut) { Ctx.Out("\n"); consoleCol = 0; return; }
        lines.Add(outLine.ToString());
        outLine.Clear();
    }

    public void Page(Node at)
    {
        if (!IsText) throw Err("page applies to text files only", at);
        CheckWrite(at);
        bool lineOpen = ConsoleOut ? consoleCol > 0 : outLine.Length > 0;
        if (lineOpen) WriteLn(at);
        WriteText("\f", at);
    }

    public void Put(Node at)
    {
        CheckWrite(at);
        if (IsText)
        {
            if (Buffer[0].IsUndef) throw Err("put with an undefined buffer variable", at);
            WriteText(((char)Buffer[0].N).ToString(), at);
            Buffer[0] = Cell.Undefined;
            return;
        }
        elems.Add((Cell[])Buffer.Clone());
        Array.Fill(Buffer, Cell.Undefined);
    }

    /// <summary>write(f, x) for a non-text file: f^ := x; put(f).</summary>
    public void WriteElem(Ref src, Node at)
    {
        CheckWrite(at);
        elems.Add(src.S.AsSpan(src.I, Buffer.Length).ToArray());
    }
}
