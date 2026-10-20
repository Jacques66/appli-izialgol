using System.Collections.Concurrent;
using System.Text;
using System.Windows.Threading;
using Studio.Common;
using Studio.Tooling;

namespace Studio.Ide.Services;

/// <summary>Entrees/sorties du programme vers la console integree de l'IDE.
/// Write/ReadLine sont appeles sur le thread d'execution ; l'affichage est marshale vers le Dispatcher.</summary>
public sealed class IdeProgramIO : IProgramIO
{
    const int MaxPending = 512 * 1024;

    readonly Dispatcher _ui;
    readonly Action<string> _onOutput;
    readonly Action<bool> _onWaiting;
    readonly BlockingCollection<string?> _input = new();
    readonly CancellationToken _token;
    readonly StringBuilder _pending = new();
    readonly ManualResetEventSlim _drained = new(true);
    bool _flushScheduled;
    volatile bool _eof;

    public IdeProgramIO(Dispatcher ui, string workingDirectory, CancellationToken token, Action<string> onOutput, Action<bool> onWaiting)
    {
        _ui = ui;
        WorkingDirectory = workingDirectory;
        _token = token;
        _onOutput = onOutput;
        _onWaiting = onWaiting;
    }

    public string WorkingDirectory { get; }

    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        bool wait = false;
        lock (_pending)
        {
            _pending.Append(text);
            if (!_flushScheduled)
            {
                _flushScheduled = true;
                _ui.BeginInvoke(DispatcherPriority.Background, new Action(Flush));
            }
            if (_pending.Length > MaxPending) { _drained.Reset(); wait = true; }
        }
        // Contre-pression : un programme qui ecrit sans fin ne doit pas saturer la memoire.
        if (wait) WaitHandle.WaitAny(new[] { _drained.WaitHandle, _token.WaitHandle }, 2000);
    }

    /// <summary>Affiche la sortie en attente (thread de l'interface).</summary>
    public void Flush()
    {
        string s;
        lock (_pending)
        {
            s = _pending.ToString();
            _pending.Clear();
            _flushScheduled = false;
            _drained.Set();
        }
        if (s.Length > 0) _onOutput(s);
    }

    public string? ReadLine()
    {
        if (_eof || _token.IsCancellationRequested) return null;
        _ui.BeginInvoke(new Action(() => { Flush(); _onWaiting(true); }));
        try
        {
            var line = _input.Take(_token);
            if (line == null) _eof = true;
            return line;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _ui.BeginInvoke(new Action(() => _onWaiting(false)));
        }
    }

    /// <summary>Ligne saisie dans la console (null = fin de fichier).</summary>
    public void Submit(string? line) => _input.Add(line);
}

/// <summary>Une execution en cours (normale ou sous le debogueur).</summary>
public sealed class RunSession
{
    public RunSession(ICompiledProgram program, IdeProgramIO io, CancellationTokenSource cts, bool debug, EditorRef main)
    {
        Program = program;
        IO = io;
        Cts = cts;
        IsDebugging = debug;
        Main = main;
        Execution = program.CreateExecution(io);
    }

    public ICompiledProgram Program { get; }
    public IProgramExecution Execution { get; }
    public IdeProgramIO IO { get; }
    public CancellationTokenSource Cts { get; }
    public bool IsDebugging { get; }
    public EditorRef Main { get; }
    public IDebugger? Debugger => IsDebugging ? Execution.Debugger : null;
    public bool IsPaused { get; set; }
    public PauseInfo? LastPause { get; set; }
    public DateTime Started { get; } = DateTime.Now;
    public bool StopRequested { get; set; }

    public Task<ExecutionResult> Start()
    {
        var tcs = new TaskCompletionSource<ExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => tcs.SetResult(ProgramHost.SafeRun(Execution, Cts.Token)), ProgramHost.StackSize)
        {
            IsBackground = true,
            Name = "Programme",
        };
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>Fichier principal d'une execution (nom affiche, chemin de compilation).</summary>
public sealed record EditorRef(string DisplayName, string CompilePath, string LanguageName);
