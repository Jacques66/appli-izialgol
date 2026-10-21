using System.Text;
using System.Text.Json;
using Studio.Common;
using Studio.Pascal.Exec;

namespace Studio.Pascal.Runtime;

/// <summary>Compiled program: executable tree + source (serialisation embeds the source).</summary>
public sealed class CompiledProgram : ICompiledProgram
{
    public string LanguageId => "pascal";
    public RoutineInfo Main { get; }
    public SourceFile Source { get; }
    public string MainFile => Source.Path;

    public CompiledProgram(RoutineInfo main, SourceFile src) { Main = main; Source = src; }

    sealed class Payload
    {
        public int Version { get; set; } = 1;
        public string Path { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public const string Magic = "PASSRC1\n";

    /// <summary>Serialised form: the source (the program is recompiled when loaded).</summary>
    public byte[] Serialize() =>
        Encoding.UTF8.GetBytes(Magic + JsonSerializer.Serialize(new Payload { Path = Source.Path, Text = Source.Text }));

    public static SourceFile Parse(byte[] payload)
    {
        var text = Encoding.UTF8.GetString(payload);
        if (!text.StartsWith(Magic)) throw new InvalidDataException("invalid Pascal payload");
        var p = JsonSerializer.Deserialize<Payload>(text[Magic.Length..]) ?? throw new InvalidDataException("empty Pascal payload");
        return new SourceFile(p.Path, p.Text);
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
        using var reg = cancellationToken.Register(() =>
        {
            ctx.CancelRequested = true;
            ctx.RefreshAttention();
            ctx.Debugger.Release();
        });
        ctx.RefreshAttention();
        // large stack: deep recursion
        var t = new Thread(() => result = Body(), 512 * 1024 * 1024) { Name = "Pascal main", IsBackground = true };
        t.Start();
        t.Join();
        return result ?? new ExecutionResult(1, new RuntimeErrorInfo("internal error: execution interrupted", program.MainFile, 0, 0, Array.Empty<StackFrameInfo>()));
    }

    ExecutionResult Body()
    {
        var main = program.Main;
        Frame? frame = null;
        try
        {
            frame = new Frame(main, null, ctx, 1);
            if (ctx.CancelRequested) return new ExecutionResult(130, null, true);
            Calls.Run(frame, main.Body);
            return new ExecutionResult(0, null);
        }
        catch (PasError e) when (Filter(e))
        {
            return new ExecutionResult(1, ToInfo(e));
        }
        catch (CancelledException) { return new ExecutionResult(130, null, true); }
        catch (GotoException g) { return new ExecutionResult(1, new RuntimeErrorInfo($"goto {g.Label}: target not found", program.MainFile, 0, 0, Array.Empty<StackFrameInfo>())); }
        catch (Exception e) when (e is not ThreadAbortException)
        {
            var err = new PasError(InternalMessage(e), null);
            var st = CaptureStack(err);
            return new ExecutionResult(1, new RuntimeErrorInfo(err.Message, st.Count > 0 ? st[0].File : program.MainFile, st.Count > 0 ? st[0].Line : 0, st.Count > 0 ? st[0].Column : 0, st));
        }
        finally
        {
            try { ctx.Finish(); } catch { }
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

    bool Filter(PasError e)
    {
        if (e.Stack == null)
        {
            e.Stack = CaptureStack(e);
            ctx.Debugger.OnError(e);
        }
        return true;
    }

    RuntimeErrorInfo ToInfo(PasError e)
    {
        var stack = e.Stack ?? Array.Empty<StackFrameInfo>();
        string file = e.Positioned ? e.File : stack.Count > 0 ? stack[0].File : program.MainFile;
        int line = e.Positioned ? e.Line : stack.Count > 0 ? stack[0].Line : 0;
        int col = e.Positioned ? e.Col : stack.Count > 0 ? stack[0].Column : 0;
        return new RuntimeErrorInfo(e.Message, file, line, col, stack);
    }

    /// <summary>Call stack at the time of the error (frame 0 = most recent).</summary>
    IReadOnlyList<StackFrameInfo> CaptureStack(PasError e)
    {
        var list = new List<StackFrameInfo>();
        bool first = true;
        for (var f = ctx.Top; f != null && list.Count < 200; f = f.Caller)
        {
            if (first && e.Positioned) list.Add(new StackFrameInfo(f.R.Name, e.File, e.Line, e.Col));
            else list.Add(new StackFrameInfo(f.R.Name, f.R.File, f.Line, f.Col));
            first = false;
        }
        return list;
    }
}
