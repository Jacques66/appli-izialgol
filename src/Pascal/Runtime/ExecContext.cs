using Studio.Common;
using Studio.Pascal.Exec;
using Studio.Pascal.Semantics;

namespace Studio.Pascal.Runtime;

/// <summary>State of one program execution (single-threaded).</summary>
public sealed class ExecContext
{
    public readonly IProgramIO IO;
    public readonly Debugger Debugger;
    public readonly CompiledProgram Program;
    public CancellationToken Token;
    /// <summary>True when a check is needed at each statement (cancellation, debugging).</summary>
    public volatile bool Attention;
    public volatile bool CancelRequested;
    public Frame? Top;
    public int CallDepth;
    public int HeapCounter;
    /// <summary>Storage of the dynamic variables allocated with tag values (new(p, t1, ..., tn)).</summary>
    public readonly System.Runtime.CompilerServices.ConditionalWeakTable<Cell[], HeapObj> FixedTagStores = new();
    readonly List<PFile> files = new();
    readonly System.Text.StringBuilder pendingOut = new();

    public ExecContext(CompiledProgram program, IProgramIO io)
    {
        Program = program; IO = io;
        Debugger = new Debugger(this);
    }

    public void RefreshAttention() => Attention = CancelRequested || Debugger.NeedsCheck;

    /// <summary>Called at each statement when Attention is true.</summary>
    public void OnAttention(Frame f, Node at)
    {
        if (CancelRequested) throw new CancelledException();
        Debugger.OnStatement(f, at);
    }

    /// <summary>Creates a file variable. external: name of a program parameter (file of the working directory).</summary>
    public PFile NewFile(FileType t, string name, string? external)
    {
        PFile pf;
        if (external == "input") pf = new PFile(t, "input", this, consoleIn: true);
        else if (external == "output") pf = new PFile(t, "output", this, consoleOut: true);
        else
        {
            pf = new PFile(t, name, this);
            if (external != null)
            {
                pf.ExternalPath = Path.Combine(IO.WorkingDirectory, external);
                lock (files) files.Add(pf);
            }
        }
        return pf;
    }

    public void Out(string s)
    {
        // output is buffered in small chunks (flushed at each line end, before reading and at the end)
        pendingOut.Append(s);
        if (s.Contains('\n') || pendingOut.Length > 4096) FlushOutput();
    }

    public void FlushOutput()
    {
        if (pendingOut.Length == 0) return;
        var s = pendingOut.ToString();
        pendingOut.Clear();
        IO.Write(s);
    }

    /// <summary>End of the program: console output flushed, external files written.</summary>
    public void Finish()
    {
        FlushOutput();
        List<PFile> copy;
        lock (files) copy = files.ToList();
        foreach (var f in copy)
            try { f.Flush(); } catch (Exception) { }
    }
}
