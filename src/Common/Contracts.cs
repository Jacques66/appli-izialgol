// Shared contract between the compilers (Simula 67, Algol 68) and the tools (IDE, CLI, Runner).
// Every language implementation exposes an ILanguageService.

using System.Threading;

namespace Studio.Common;

public enum Severity { Error, Warning, Info }

/// <summary>Compiler message. Line and Column are 1-based (0 = unknown).</summary>
public sealed record Diagnostic(Severity Severity, string File, int Line, int Column, string Message)
{
    public override string ToString() =>
        $"{File}({Line},{Column}): {(Severity == Severity.Error ? "error" : Severity == Severity.Warning ? "warning" : "info")}: {Message}";
}

public sealed record SourceFile(string Path, string Text);

public sealed class CompileOptions
{
    /// <summary>Resolves included/external files. Receives (requested name, path of the including file) and returns the text or null.</summary>
    public Func<string, string, SourceFile?>? IncludeResolver { get; set; }
    /// <summary>Generates debugging information (lines, variable names).</summary>
    public bool Debug { get; set; } = true;
}

public sealed class CompilationResult
{
    public CompilationResult(IReadOnlyList<Diagnostic> diagnostics, ICompiledProgram? program)
    {
        Diagnostics = diagnostics;
        Program = program;
    }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    /// <summary>Null when compilation failed.</summary>
    public ICompiledProgram? Program { get; }
    public bool Success => Program != null && !Diagnostics.Any(d => d.Severity == Severity.Error);
}

public interface ILanguageService
{
    /// <summary>Stable identifier: "simula", "algol68", "algol60" or "simula1".</summary>
    string Id { get; }
    string DisplayName { get; }
    /// <summary>Lower-case extensions including the dot, e.g. ".sim".</summary>
    IReadOnlyList<string> FileExtensions { get; }
    CompilationResult Compile(SourceFile main, CompileOptions options);
    /// <summary>Reloads a program from ICompiledProgram.Serialize() (used by generated .exe files).</summary>
    ICompiledProgram Deserialize(byte[] payload);
}

public interface ICompiledProgram
{
    string LanguageId { get; }
    /// <summary>Serialized form embedded in standalone .exe files.</summary>
    byte[] Serialize();
    IProgramExecution CreateExecution(IProgramIO io);
}

/// <summary>Console input/output of the program (Windows console or IDE panel).</summary>
public interface IProgramIO
{
    void Write(string text);
    /// <summary>Reads a line without its terminator; blocks; null at end of file.</summary>
    string? ReadLine();
    /// <summary>Base directory for the files opened by the program.</summary>
    string WorkingDirectory { get; }
}

public sealed record StackFrameInfo(string Name, string File, int Line, int Column);

public sealed record RuntimeErrorInfo(string Message, string File, int Line, int Column, IReadOnlyList<StackFrameInfo> Stack)
{
    public override string ToString() => $"{File}({Line},{Column}): runtime error: {Message}";
}

public sealed record ExecutionResult(int ExitCode, RuntimeErrorInfo? Error, bool Cancelled = false);

public interface IProgramExecution
{
    /// <summary>
    /// Runs the program to completion on the calling thread. Throws no exception:
    /// runtime errors are returned in ExecutionResult.Error.
    /// Cancellation (Stop button) returns Cancelled = true.
    /// </summary>
    ExecutionResult Run(CancellationToken cancellationToken);
    /// <summary>Debugging. Subscribe/configure it BEFORE calling Run.</summary>
    IDebugger Debugger { get; }
}

public enum PauseReason { Breakpoint, Step, PauseRequest, Exception, Entry }

public sealed record PauseInfo(PauseReason Reason, string File, int Line, int Column, string? Message);

public sealed class VariableInfo
{
    public VariableInfo(string name, string type, string value, bool hasChildren = false, object? handle = null)
    {
        Name = name; Type = type; Value = value; HasChildren = hasChildren; Handle = handle;
    }
    public string Name { get; }
    public string Type { get; }
    public string Value { get; }
    public bool HasChildren { get; }
    /// <summary>Opaque, reserved for the language implementation (for GetChildren).</summary>
    public object? Handle { get; }
}

/// <summary>
/// Model: the execution thread (the one running the program) raises Paused, then blocks until
/// Continue/StepInto/StepOver/StepOut or cancellation. While paused, GetStack/GetVariables/GetChildren
/// may be called from any thread.
/// </summary>
public interface IDebugger
{
    /// <summary>Replaces the breakpoints of a file. May be called at any time (thread-safe).</summary>
    void SetBreakpoints(string file, IEnumerable<int> lines);
    /// <summary>If true, pause on the first statement (PauseReason.Entry).</summary>
    bool StopOnEntry { get; set; }
    /// <summary>If true, pause on a runtime error (PauseReason.Exception) before terminating.</summary>
    bool BreakOnError { get; set; }
    void RequestPause();
    void Continue();
    void StepInto();
    void StepOver();
    void StepOut();
    /// <summary>Raised on the execution thread.</summary>
    event Action<PauseInfo>? Paused;
    /// <summary>Frame 0 = innermost. Only valid while paused.</summary>
    IReadOnlyList<StackFrameInfo> GetStack();
    IReadOnlyList<VariableInfo> GetVariables(int frameIndex);
    IReadOnlyList<VariableInfo> GetChildren(VariableInfo variable);
}
