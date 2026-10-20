// Running a compiled program: large-stack thread, Windows console, error formatting.

using System.Text;
using Studio.Common;

namespace Studio.Tooling;

public static class ProgramHost
{
    /// <summary>Stack size of the execution thread (deeply recursive programs).</summary>
    public const int StackSize = 256 * 1024 * 1024;

    /// <summary>Runs on a dedicated large-stack thread and waits for completion.</summary>
    public static ExecutionResult RunOnLargeStack(IProgramExecution execution, CancellationToken token)
    {
        ExecutionResult? result = null;
        var thread = new Thread(() => result = SafeRun(execution, token), StackSize)
        {
            Name = "Program execution",
            IsBackground = true,
        };
        thread.Start();
        thread.Join();
        return result ?? new ExecutionResult(1, InternalError("the program stopped without a result"));
    }

    /// <summary>Calls Run and catches any exception (the contract says it throws none, but we guard anyway).</summary>
    public static ExecutionResult SafeRun(IProgramExecution execution, CancellationToken token)
    {
        try
        {
            return execution.Run(token);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionResult(130, null, Cancelled: true);
        }
        catch (Exception ex)
        {
            return new ExecutionResult(1, InternalError(ex.GetType().Name + ": " + ex.Message));
        }
    }

    public static RuntimeErrorInfo InternalError(string message) =>
        new("internal error: " + message, "", 0, 0, Array.Empty<StackFrameInfo>());

    /// <summary>Runtime error message + call stack (one line per frame).</summary>
    public static string FormatError(RuntimeErrorInfo error)
    {
        var sb = new StringBuilder();
        sb.Append(error.ToString());
        foreach (var f in error.Stack)
        {
            sb.AppendLine();
            sb.Append("   at ").Append(string.IsNullOrEmpty(f.Name) ? "?" : f.Name);
            if (!string.IsNullOrEmpty(f.File) || f.Line > 0)
                sb.Append(" in ").Append(f.File).Append('(').Append(f.Line).Append(',').Append(f.Column).Append(')');
        }
        return sb.ToString();
    }

    /// <summary>Process exit code for an execution result.</summary>
    public static int ExitCodeOf(ExecutionResult result)
    {
        if (result.Cancelled) return result.ExitCode != 0 ? result.ExitCode : 130;
        if (result.Error != null) return result.ExitCode != 0 ? result.ExitCode : 1;
        return result.ExitCode;
    }

    /// <summary>
    /// Runs in the Windows console (Runner, simc --run, a68c --run): ConsoleProgramIO,
    /// Ctrl+C = stop, runtime error on stderr. Returns the exit code.
    /// </summary>
    public static int RunInConsole(ICompiledProgram program, string? workingDirectory = null)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { /* no console */ }
        // Redirected input (file, pipe): read as UTF-8. From the keyboard, keep the console encoding.
        try { if (Console.IsInputRedirected) Console.InputEncoding = new UTF8Encoding(false); } catch { }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            if (!cts.IsCancellationRequested) { e.Cancel = true; cts.Cancel(); }
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            var io = new ConsoleProgramIO(workingDirectory);
            ExecutionResult result;
            try
            {
                var execution = program.CreateExecution(io);
                result = RunOnLargeStack(execution, cts.Token);
            }
            catch (Exception ex)
            {
                result = new ExecutionResult(1, InternalError(ex.GetType().Name + ": " + ex.Message));
            }
            Console.Out.Flush();
            if (result.Error != null)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(FormatError(result.Error));
            }
            else if (result.Cancelled)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("execution interrupted");
            }
            Console.Error.Flush();
            return ExitCodeOf(result);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }
}
