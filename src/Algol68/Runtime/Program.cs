using System.Text;
using System.Text.Json;
using Studio.Common;
using Studio.Algol68.Exec;

namespace Studio.Algol68.Runtime;

/// <summary>Compiled program: executable tree + sources (serialisation embeds the sources).</summary>
public sealed class CompiledProgram : ICompiledProgram
{
    public string LanguageId => "algol68";
    public RoutineInfo Main { get; }
    public SourceFile MainSource { get; }
    public IReadOnlyList<SourceFile> Includes { get; }
    public string MainFile => MainSource.Path;
    /// <summary>LONG LONG precision (PR precision PR) to restore before execution.</summary>
    public int LongLongDigits { get; init; } = Prelude.DefaultLongLongDigits;

    public CompiledProgram(RoutineInfo main, SourceFile src, IReadOnlyList<SourceFile> includes)
    {
        Main = main; MainSource = src; Includes = includes;
    }

    sealed class Payload
    {
        public int Version { get; set; } = 1;
        public string MainPath { get; set; } = "";
        public string MainText { get; set; } = "";
        public List<string[]> Includes { get; set; } = new();
    }

    public const string Magic = "A68SRC1\n";

    /// <summary>Serialised form: the sources (the program is recompiled when loaded).</summary>
    public byte[] Serialize()
    {
        var p = new Payload { MainPath = MainSource.Path, MainText = MainSource.Text };
        foreach (var inc in Includes) p.Includes.Add(new[] { inc.Path, inc.Text });
        return Encoding.UTF8.GetBytes(Magic + JsonSerializer.Serialize(p));
    }

    public static (SourceFile main, Dictionary<string, SourceFile> includes) Parse(byte[] payload)
    {
        var text = Encoding.UTF8.GetString(payload);
        if (!text.StartsWith(Magic)) throw new InvalidDataException("invalid Algol 68 payload");
        var p = JsonSerializer.Deserialize<Payload>(text[Magic.Length..]) ?? throw new InvalidDataException("empty Algol 68 payload");
        var incs = new Dictionary<string, SourceFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in p.Includes) if (i.Length == 2) incs[i[0]] = new SourceFile(i[0], i[1]);
        return (new SourceFile(p.MainPath, p.MainText), incs);
    }

    public IProgramExecution CreateExecution(IProgramIO io) => new Execution(this, io);
}

public sealed class Execution : IProgramExecution
{
    readonly CompiledProgram program;
    readonly ExecContext ctx;

    public Execution(CompiledProgram p, IProgramIO io)
    {
        program = p;
        ctx = new ExecContext(p, io);
    }

    public IDebugger Debugger => ctx.Debugger;
    public ExecContext Context => ctx;

    public ExecutionResult Run(CancellationToken cancellationToken)
    {
        ExecutionResult? result = null;
        ctx.Token = cancellationToken;
        Prelude.SetLongLong(program.LongLongDigits);
        using var reg = cancellationToken.Register(() =>
        {
            ctx.CancelRequested = true;
            ctx.RefreshAttention();
            ctx.Debugger.Release();
            lock (ctx.SchedLock) Monitor.PulseAll(ctx.SchedLock);
        });
        ctx.RefreshAttention();
        // large stack: deep recursion (man or boy, Ackermann...)
        var t = new Thread(() => result = Body(), 512 * 1024 * 1024) { Name = "Algol68 main", IsBackground = true };
        t.Start();
        t.Join();
        return result ?? new ExecutionResult(1, new RuntimeErrorInfo("internal error: execution interrupted", program.MainFile, 0, 0, Array.Empty<StackFrameInfo>()));
    }

    ExecutionResult Body()
    {
        var ts = new ThreadCtx();
        ThreadCtx.Current = ts;
        var main = program.Main;
        var frame = new Frame(main, null, 1, ctx);
        ts.Top = frame;
        frame.File = main.File; frame.Line = main.Line; frame.Col = main.Col;
        try
        {
            if (ctx.CancelRequested) return new ExecutionResult(130, null, true);
            main.Body.Exec(frame);
            return new ExecutionResult(0, null);
        }
        catch (A68Error e) when (Filter(e, ts))
        {
            return new ExecutionResult(1, ToInfo(e));
        }
        catch (CancelledException) { return new ExecutionResult(130, null, true); }
        catch (StopException s) { return new ExecutionResult(s.Code, null); }
        catch (ParAbortException) { return new ExecutionResult(1, new RuntimeErrorInfo("parallel execution interrupted", program.MainFile, 0, 0, Array.Empty<StackFrameInfo>())); }
        catch (JumpException j) { return new ExecutionResult(1, new RuntimeErrorInfo($"jump to label '{j.Target.Label}' which is no longer accessible", program.MainFile, 0, 0, Array.Empty<StackFrameInfo>())); }
        catch (Exception e) when (e is not ThreadAbortException)
        {
            var err = new A68Error(InternalMessage(e));
            return new ExecutionResult(1, new RuntimeErrorInfo(err.Message, frame.File, frame.Line, frame.Col, CaptureStack(err, ts)));
        }
        finally
        {
            try { ctx.Transput.FlushAll(); } catch { }
        }
    }

    static string InternalMessage(Exception e) => e switch
    {
        StackOverflowException or InsufficientExecutionStackException => "stack overflow (recursion too deep)",
        OutOfMemoryException => "out of memory",
        IOException io => "input/output error: " + io.Message,
        UnauthorizedAccessException ua => "access denied: " + ua.Message,
        _ => $"internal runtime error ({e.GetType().Name}: {e.Message})",
    };

    bool Filter(A68Error e, ThreadCtx ts)
    {
        if (e.Stack == null)
        {
            e.Stack = CaptureStack(e, ts);
            ctx.Debugger.OnError(e);
        }
        return true;
    }

    RuntimeErrorInfo ToInfo(A68Error e)
    {
        var stack = e.Stack ?? Array.Empty<StackFrameInfo>();
        string file = e.Positioned ? e.File : stack.Count > 0 ? stack[0].File : program.MainFile;
        int line = e.Positioned ? e.Line : stack.Count > 0 ? stack[0].Line : 0;
        int col = e.Positioned ? e.Col : stack.Count > 0 ? stack[0].Column : 0;
        return new RuntimeErrorInfo(e.Message, file, line, col, stack);
    }

    /// <summary>Call stack at the time of the error (frame 0 = most recent).</summary>
    public static IReadOnlyList<StackFrameInfo> CaptureStack(A68Error e, ThreadCtx ts)
    {
        var list = new List<StackFrameInfo>();
        bool first = true;
        for (var f = ts.Top; f != null && list.Count < 200; f = f.Caller)
        {
            if (first && e.Positioned) list.Add(new StackFrameInfo(f.Info.Name, e.File, e.Line, e.Col));
            else list.Add(new StackFrameInfo(f.Info.Name, f.File, f.Line, f.Col));
            first = false;
        }
        return list;
    }
}
