// Execution d'un programme compile : thread a grande pile, console Windows, format des erreurs.

using System.Text;
using Studio.Common;

namespace Studio.Tooling;

public static class ProgramHost
{
    /// <summary>Taille de pile du thread d'execution (programmes tres recursifs).</summary>
    public const int StackSize = 256 * 1024 * 1024;

    /// <summary>Execute sur un thread dedie a grande pile et attend la fin.</summary>
    public static ExecutionResult RunOnLargeStack(IProgramExecution execution, CancellationToken token)
    {
        ExecutionResult? result = null;
        var thread = new Thread(() => result = SafeRun(execution, token), StackSize)
        {
            Name = "Execution du programme",
            IsBackground = true,
        };
        thread.Start();
        thread.Join();
        return result ?? new ExecutionResult(1, InternalError("le programme s'est arrêté sans résultat"));
    }

    /// <summary>Appelle Run en interceptant toute exception (le contrat dit qu'il n'en leve pas, mais on se protege).</summary>
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
            return new ExecutionResult(1, InternalError(ex.GetType().Name + " : " + ex.Message));
        }
    }

    public static RuntimeErrorInfo InternalError(string message) =>
        new("erreur interne : " + message, "", 0, 0, Array.Empty<StackFrameInfo>());

    /// <summary>Message d'erreur d'execution + pile d'appels (une ligne par frame).</summary>
    public static string FormatError(RuntimeErrorInfo error)
    {
        var sb = new StringBuilder();
        sb.Append(error.ToString());
        foreach (var f in error.Stack)
        {
            sb.AppendLine();
            sb.Append("   à ").Append(string.IsNullOrEmpty(f.Name) ? "?" : f.Name);
            if (!string.IsNullOrEmpty(f.File) || f.Line > 0)
                sb.Append(" dans ").Append(f.File).Append('(').Append(f.Line).Append(',').Append(f.Column).Append(')');
        }
        return sb.ToString();
    }

    /// <summary>Code de sortie du processus pour un resultat d'execution.</summary>
    public static int ExitCodeOf(ExecutionResult result)
    {
        if (result.Cancelled) return result.ExitCode != 0 ? result.ExitCode : 130;
        if (result.Error != null) return result.ExitCode != 0 ? result.ExitCode : 1;
        return result.ExitCode;
    }

    /// <summary>
    /// Execute dans la console Windows (Runner, simc --run, a68c --run) : ConsoleProgramIO,
    /// Ctrl+C = arret, erreur d'execution sur stderr. Rend le code de sortie.
    /// </summary>
    public static int RunInConsole(ICompiledProgram program, string? workingDirectory = null)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { /* pas de console */ }
        // Entree redirigee (fichier, tube) : lue en UTF-8. Au clavier, on garde l'encodage de la console.
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
                result = new ExecutionResult(1, InternalError(ex.GetType().Name + " : " + ex.Message));
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
                Console.Error.WriteLine("exécution interrompue");
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
