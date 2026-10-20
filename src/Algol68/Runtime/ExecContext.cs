using System.Runtime.CompilerServices;
using Studio.Common;
using Studio.Algol68.Exec;

namespace Studio.Algol68.Runtime;

/// <summary>Etat propre a un fil d'execution (programme principal ou branche PAR).</summary>
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
    public ParGroup? Group;     // groupe PAR auquel appartient ce fil (null : fil principal)
    public string Name = "principal";
    public int Id;
}

/// <summary>Groupe de branches paralleles : arret collectif si une branche echoue.</summary>
public sealed class ParGroup
{
    public volatile bool Aborted;
    public ParGroup? Parent;
    public bool IsAborted { get { for (var g = this; g != null; g = g.Parent) if (g.Aborted) return true; return false; } }
}

/// <summary>Contexte d'une execution de programme.</summary>
public sealed class ExecContext
{
    public readonly IProgramIO IO;
    public readonly Debugger Debugger;
    public CancellationToken Token;
    /// <summary>Vrai si une verification est necessaire a chaque instruction (annulation, mise au point).</summary>
    public volatile bool Attention;
    public volatile bool CancelRequested;
    public readonly Transput Transput;
    public readonly CompiledProgram Program;
    public long RandomSeed = 1;
    public readonly object RandomLock = new();
    public readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    // fils et detection d'interblocage
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

    /// <summary>Appele a chaque instruction quand Attention est vrai.</summary>
    public void OnAttention(Frame f, Node at)
    {
        if (CancelRequested) throw new CancelledException();
        var g = ThreadCtx.Current.Group;
        if (g != null && g.IsAborted) throw new ParAbortException();
        Debugger.OnStatement(f, at);
    }
}

/// <summary>Arret d'une branche PAR suite a l'echec d'une autre branche.</summary>
public sealed class ParAbortException : Exception { }
