using System.Runtime.CompilerServices;
using Studio.Common;
using Studio.Algol68.Exec;

namespace Studio.Algol68.Runtime;

/// <summary>State specific to an execution thread (main program or PAR branch).</summary>
public sealed class ThreadCtx
{
    [ThreadStatic] static ThreadCtx? current;
    public static ThreadCtx Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => current ??= new ThreadCtx();
        set => current = value;
    }
    public Frame? Top;
    public int CallDepth;
    public ParGroup? Group;     // PAR group this thread belongs to (null: main thread)
    public string Name = "main";
    public int Id;
}

/// <summary>Group of parallel branches: all stop if one branch fails.</summary>
public sealed class ParGroup
{
    public volatile bool Aborted;
    public ParGroup? Parent;
    public bool IsAborted { get { for (var g = this; g != null; g = g.Parent) if (g.Aborted) return true; return false; } }
}

/// <summary>Context of one program execution.</summary>
public sealed class ExecContext
{
    public readonly IProgramIO IO;
    public readonly Debugger Debugger;
    public CancellationToken Token;
    /// <summary>True when a check is needed at each statement (cancellation, debugging).</summary>
    public volatile bool Attention;
    public volatile bool CancelRequested;
    public readonly Transput Transput;
    public readonly CompiledProgram Program;
    public long RandomSeed = 1;
    public readonly object RandomLock = new();
    public readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    // threads and deadlock detection
    public readonly object SchedLock = new();
    public int RunnableThreads = 1;
    public int BlockedOnSema;
    public long SemaEpoch;
    public int ThreadCounter;

    public ExecContext(CompiledProgram program, IProgramIO io)
    {
        Program = program; IO = io;
        Debugger = new Debugger(this);
        Transput = new Transput(this);
    }

    public volatile int AbortedGroups;

    public void RefreshAttention()
    {
        Attention = CancelRequested || Debugger.NeedsCheck || AbortedGroups > 0;
    }

    /// <summary>Called at each statement when Attention is true.</summary>
    public void OnAttention(Frame f, Node at)
    {
        if (CancelRequested) throw new CancelledException();
        var g = ThreadCtx.Current.Group;
        if (g != null && g.IsAborted) throw new ParAbortException();
        Debugger.OnStatement(f, at);
    }
}

/// <summary>Stop of a PAR branch after another branch failed.</summary>
public sealed class ParAbortException : Exception { }
