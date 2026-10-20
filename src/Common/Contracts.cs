// Contrat commun entre les compilateurs (Simula 67, Algol 68) et les outils (IDE, CLI, Runner).
// Toute implementation de langage expose un ILanguageService.

using System.Threading;

namespace Studio.Common;

public enum Severity { Error, Warning, Info }

/// <summary>Message du compilateur. Line et Column commencent a 1 (0 = inconnu).</summary>
public sealed record Diagnostic(Severity Severity, string File, int Line, int Column, string Message)
{
    public override string ToString() =>
        $"{File}({Line},{Column}): {(Severity == Severity.Error ? "erreur" : Severity == Severity.Warning ? "avertissement" : "info")}: {Message}";
}

public sealed record SourceFile(string Path, string Text);

public sealed class CompileOptions
{
    /// <summary>Resolution des fichiers inclus/externes. Recoit (nom demande, chemin du fichier qui inclut) et rend le texte ou null.</summary>
    public Func<string, string, SourceFile?>? IncludeResolver { get; set; }
    /// <summary>Genere les informations de mise au point (lignes, noms de variables).</summary>
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
    /// <summary>Null si la compilation a echoue.</summary>
    public ICompiledProgram? Program { get; }
    public bool Success => Program != null && !Diagnostics.Any(d => d.Severity == Severity.Error);
}

public interface ILanguageService
{
    /// <summary>Identifiant stable : "simula" ou "algol68".</summary>
    string Id { get; }
    string DisplayName { get; }
    /// <summary>Extensions en minuscules avec le point, ex. ".sim".</summary>
    IReadOnlyList<string> FileExtensions { get; }
    CompilationResult Compile(SourceFile main, CompileOptions options);
    /// <summary>Recharge un programme a partir de ICompiledProgram.Serialize() (utilise par les .exe generes).</summary>
    ICompiledProgram Deserialize(byte[] payload);
}

public interface ICompiledProgram
{
    string LanguageId { get; }
    /// <summary>Forme serialisee embarquee dans les .exe autonomes.</summary>
    byte[] Serialize();
    IProgramExecution CreateExecution(IProgramIO io);
}

/// <summary>Entrees/sorties de la console du programme (console Windows ou panneau de l'IDE).</summary>
public interface IProgramIO
{
    void Write(string text);
    /// <summary>Lit une ligne sans le terminateur ; bloque ; null en fin de fichier.</summary>
    string? ReadLine();
    /// <summary>Repertoire de base pour les fichiers ouverts par le programme.</summary>
    string WorkingDirectory { get; }
}

public sealed record StackFrameInfo(string Name, string File, int Line, int Column);

public sealed record RuntimeErrorInfo(string Message, string File, int Line, int Column, IReadOnlyList<StackFrameInfo> Stack)
{
    public override string ToString() => $"{File}({Line},{Column}): erreur d'execution: {Message}";
}

public sealed record ExecutionResult(int ExitCode, RuntimeErrorInfo? Error, bool Cancelled = false);

public interface IProgramExecution
{
    /// <summary>
    /// Execute le programme jusqu'au bout sur le thread appelant. Ne leve pas d'exception :
    /// les erreurs d'execution sont rendues dans ExecutionResult.Error.
    /// L'annulation (bouton Stop) rend Cancelled = true.
    /// </summary>
    ExecutionResult Run(CancellationToken cancellationToken);
    /// <summary>Mise au point. Il faut s'y abonner/configurer AVANT d'appeler Run.</summary>
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
    /// <summary>Opaque, reserve a l'implementation du langage (pour GetChildren).</summary>
    public object? Handle { get; }
}

/// <summary>
/// Modele : le thread d'execution (celui qui appelle Run) leve Paused puis se bloque jusqu'a
/// Continue/StepInto/StepOver/StepOut/Stop. Pendant la pause, GetStack/GetVariables/GetChildren
/// peuvent etre appeles depuis n'importe quel thread.
/// </summary>
public interface IDebugger
{
    /// <summary>Remplace les points d'arret d'un fichier. Appelable a tout moment (thread-safe).</summary>
    void SetBreakpoints(string file, IEnumerable<int> lines);
    /// <summary>Si vrai, pause sur la premiere instruction (PauseReason.Entry).</summary>
    bool StopOnEntry { get; set; }
    /// <summary>Si vrai, pause sur erreur d'execution (PauseReason.Exception) avant de terminer.</summary>
    bool BreakOnError { get; set; }
    void RequestPause();
    void Continue();
    void StepInto();
    void StepOver();
    void StepOut();
    /// <summary>Leve sur le thread d'execution.</summary>
    event Action<PauseInfo>? Paused;
    /// <summary>Frame 0 = la plus recente. Valide seulement en pause.</summary>
    IReadOnlyList<StackFrameInfo> GetStack();
    IReadOnlyList<VariableInfo> GetVariables(int frameIndex);
    IReadOnlyList<VariableInfo> GetChildren(VariableInfo variable);
}
