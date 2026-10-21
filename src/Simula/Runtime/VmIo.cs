using System.Text;

namespace Studio.Simula.Runtime;

public sealed partial class Vm
{
    readonly Dictionary<int, SimFile> files = new();
    int nextHandle = 1;

    internal string ResolvePath(string name)
    {
        if (Path.IsPathRooted(name)) return name;
        return Path.Combine(io.WorkingDirectory, name);
    }

    internal SimFile GetFile(int h) =>
        files.TryGetValue(h, out var f) ? f : throw new SimulaError("file not open");

    static readonly Encoding Latin = Encoding.UTF8;

    /// <summary>Opens a file; returns 0 on failure (open then returns false).</summary>
    internal int OpenFile(int kind, string name, string modes)
    {
        var f = new SimFile { Kind = kind, Path = name };
        var ms = modes.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim().ToLowerInvariant()).ToList();
        string Last(params string[] opts) => ms.LastOrDefault(m => opts.Contains(m)) ?? "";
        f.Purge = Last("purge", "nopurge") == "purge";
        try
        {
            switch (kind)
            {
                case 100: f.Console = true; break;
                case 101: f.Console = true; break;
                case 1:
                case 5:
                    {
                        var p = ResolvePath(name);
                        if (!File.Exists(p)) return 0;
                        if (kind == 1) f.Reader = new StreamReader(p, Latin, true);
                        else f.Bytes = File.OpenRead(p);
                        break;
                    }
                case 2:
                case 3:
                case 6:
                    {
                        var p = ResolvePath(name);
                        var create = Last("create", "nocreate", "anycreate");
                        bool exists = File.Exists(p);
                        if (create == "create" && exists) return 0;
                        if (create == "nocreate" && !exists) return 0;
                        bool append = Last("append", "noappend") == "append";
                        if (kind == 6) f.Bytes = new FileStream(p, append ? FileMode.Append : FileMode.Create, FileAccess.Write);
                        else f.Writer = new StreamWriter(p, append, new UTF8Encoding(false));
                        break;
                    }
                case 4:
                case 7:
                    {
                        var p = ResolvePath(name);
                        var create = Last("create", "nocreate", "anycreate");
                        bool exists = File.Exists(p);
                        if (create == "create" && exists) return 0;
                        if ((create == "nocreate" || create == "") && !exists) return 0;
                        if (kind == 4)
                        {
                            f.Lines = exists ? File.ReadAllLines(p, Latin).Select(l => (string?)l).ToList() : new List<string?>();
                        }
                        else
                        {
                            bool ro = Last("readonly", "writeonly", "readwrite") == "readonly";
                            f.Bytes = new FileStream(p, FileMode.OpenOrCreate, ro ? FileAccess.Read : FileAccess.ReadWrite);
                        }
                        break;
                    }
                default:
                    return 0;
            }
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
        int h = nextHandle++;
        files[h] = f;
        return h;
    }

    internal bool CloseFile(int h)
    {
        if (!files.TryGetValue(h, out var f)) return false;
        files.Remove(h);
        try
        {
            f.Reader?.Dispose();
            f.Writer?.Flush();
            f.Writer?.Dispose();
            f.Bytes?.Dispose();
            if (f.Lines != null && f.Dirty)
            {
                var p = ResolvePath(f.Path);
                int last = f.Lines.Count;
                while (last > 0 && f.Lines[last - 1] == null) last--;
                File.WriteAllLines(p, f.Lines.Take(last).Select(l => l ?? ""), new UTF8Encoding(false));
            }
            if (f.Purge && !f.Console) File.Delete(ResolvePath(f.Path));
        }
        catch (IOException) { return false; }
        return true;
    }

    internal string? ReadLine(SimFile f)
    {
        if (f.Console) return io.ReadLine();
        return f.Reader?.ReadLine();
    }

    internal void WriteText(SimFile f, string text, bool flush = false)
    {
        if (f.Console) { io.Write(text); return; }
        if (f.Writer == null) throw new SimulaError("file not open for output");
        f.Writer.Write(text);
        if (flush) f.Writer.Flush();
    }

    /// <summary>On error, the current output line is written out.</summary>
    void FlushSysout()
    {
        try
        {
            if (prog.SysoutSlot < 0 || Env == null) return;
            if (Env.S[prog.SysoutSlot].O is not SimObject so) return;
            var img = so.Class.Attrs.FirstOrDefault(a => a.Name.Equals("image", StringComparison.OrdinalIgnoreCase));
            if (img == null) return;
            if (so.S[img.Slot].O is TextRef t && t.Pos > 1)
            {
                io.Write(new string(t.Obj.Chars, t.Start - 1, t.Pos - 1).TrimEnd() + "\n");
                t.Pos = 1;
                for (int i = 0; i < t.Length; i++) t[i] = ' ';
            }
        }
        catch { }
    }

    void CloseAll()
    {
        foreach (var h in files.Keys.ToList())
        {
            try { CloseFile(h); } catch { }
        }
    }
}

public sealed partial class Vm
{
    // character stream used by the Algol 60 / Simula I input procedures
    string? charLine;
    int charPos;
    bool charEof;

    /// <summary>Next character of the console input (newline included), or -1 at end of file.</summary>
    internal int ReadChar()
    {
        if (charEof) return -1;
        if (charLine == null)
        {
            charLine = io.ReadLine();
            charPos = 0;
            if (charLine == null) { charEof = true; return -1; }
        }
        if (charPos < charLine.Length) return charLine[charPos++];
        charLine = null;
        return '\n';
    }

    /// <summary>Puts back the last character read (one character of look-back).</summary>
    internal void UnreadChar(int c)
    {
        if (c < 0) return;
        if (c == '\n') { charLine = ""; charPos = 0; return; }
        if (charLine != null && charPos > 0) charPos--;
    }

    internal void WriteConsole(string text) => io.Write(text);
}
