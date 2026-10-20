using Studio.Algol68.Exec;

namespace Studio.Algol68.Runtime;

/// <summary>Clause parallele : chaque unite s'execute dans un vrai fil ; le parent attend la fin de toutes.</summary>
public sealed class ParNode : Node
{
    readonly Node[] units; readonly int level;
    public ParNode(Node[] units, int level) { this.units = units; this.level = level; }

    public override Value Eval(Frame f) { Exec(f); return default; }

    public override void Exec(Frame f)
    {
        var ctx = f.Ctx;
        if (units.Length == 0) return;
        if (units.Length == 1) { units[0].Exec(f); return; }
        var parent = ThreadCtx.Current;
        var group = new ParGroup { Parent = parent.Group };
        Exception? first = null;
        int remaining = units.Length;
        var threads = new Thread[units.Length];
        lock (ctx.SchedLock) ctx.RunnableThreads += units.Length;
        for (int i = 0; i < units.Length; i++)
        {
            var u = units[i];
            int id;
            lock (ctx.SchedLock) id = ++ctx.ThreadCounter;
            threads[i] = new Thread(() =>
            {
                var ts = new ThreadCtx { Group = group, Top = f, Name = $"PAR {id}", Id = id };
                ThreadCtx.Current = ts;
                try { u.Exec(f); }
                catch (ParAbortException) { }
                catch (Exception e) when (Capture(e, ctx))
                {
                    lock (ctx.SchedLock)
                    {
                        if (first == null || first is CancelledException && e is not CancelledException) first = e;
                    }
                    group.Aborted = true;
                    ctx.AbortedGroups++;
                    ctx.RefreshAttention();
                }
                finally
                {
                    lock (ctx.SchedLock)
                    {
                        remaining--;
                        ctx.RunnableThreads--;
                        Monitor.PulseAll(ctx.SchedLock);
                    }
                }
            }, 64 * 1024 * 1024) { IsBackground = true, Name = $"Algol68 PAR {id}" };
        }
        foreach (var t in threads) t.Start();
        lock (ctx.SchedLock)
        {
            ctx.RunnableThreads--;   // le parent attend
            try
            {
                while (remaining > 0)
                {
                    long epoch = ctx.SemaEpoch;
                    Monitor.Wait(ctx.SchedLock, 50);
                    if (first == null && Par.Persists(ctx, () => remaining > 0 && ctx.RunnableThreads <= 0 && ctx.BlockedOnSema > 0 && epoch == ctx.SemaEpoch))
                    {
                        // toutes les branches sont bloquees sur des semaphores
                        group.Aborted = true; ctx.AbortedGroups++; ctx.RefreshAttention();
                        first ??= RT.Err("interblocage : toutes les branches parallèles attendent un sémaphore (DOWN)", this);
                        Monitor.PulseAll(ctx.SchedLock);
                    }
                }
            }
            finally { ctx.RunnableThreads++; }
        }
        if (group.Aborted) { ctx.AbortedGroups--; ctx.RefreshAttention(); }
        if (first != null)
        {
            if (first is A68Error ae && ae.Stack == null) ae.Stack = Execution.CaptureStack(ae, ThreadCtx.Current);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
        }
    }

    static bool Capture(Exception e, ExecContext ctx)
    {
        if (e is A68Error ae && ae.Stack == null)
        {
            ae.Stack = Execution.CaptureStack(ae, ThreadCtx.Current);
            ctx.Debugger.OnError(ae);
        }
        return true;
    }
}

public static class Par
{
    /// <summary>Vrai si la condition (verrou tenu) reste vraie pendant 150 ms (filtre les reveils en cours).</summary>
    public static bool Persists(ExecContext ctx, Func<bool> cond)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 150)
        {
            if (!cond()) return false;
            Monitor.Wait(ctx.SchedLock, 25);
        }
        return cond();
    }

    public static void Down(Frame f, Value s, Node at)
    {
        var sema = s.O as SemaV ?? throw RT.Err("DOWN : sémaphore non initialisé", at);
        var ctx = f.Ctx;
        var g = ThreadCtx.Current.Group;
        lock (ctx.SchedLock)
        {
            while (sema.Level <= 0)
            {
                ctx.RunnableThreads--;
                ctx.BlockedOnSema++;
                try
                {
                    long epoch = ctx.SemaEpoch;
                    Monitor.Wait(ctx.SchedLock, 50);
                    // interblocage : aucun fil executable et aucun UP pendant l'attente
                    if (Par.Persists(ctx, () => ctx.RunnableThreads <= 0 && epoch == ctx.SemaEpoch && sema.Level <= 0))
                    {
                            throw RT.Err("interblocage : tous les processus attendent un sémaphore (DOWN)", at);
                    }
                }
                finally { ctx.RunnableThreads++; ctx.BlockedOnSema--; }
                if (ctx.CancelRequested) throw new CancelledException();
                if (g != null && g.IsAborted) throw new ParAbortException();
            }
            sema.Level--;
        }
    }

    public static void Up(Frame f, Value s, Node at)
    {
        var sema = s.O as SemaV ?? throw RT.Err("UP : sémaphore non initialisé", at);
        var ctx = f.Ctx;
        lock (ctx.SchedLock)
        {
            sema.Level++;
            ctx.SemaEpoch++;
            Monitor.PulseAll(ctx.SchedLock);
        }
    }
}
