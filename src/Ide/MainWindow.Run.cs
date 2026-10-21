using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Studio.Common;
using Studio.Ide.Editor;
using Studio.Ide.Panels;
using Studio.Ide.Services;
using Studio.Tooling;

namespace Studio.Ide;

public enum StepKind { Into, Over, Out }

public partial class MainWindow
{
    RunSession? _session;
    Process? _externalProcess;
    int _selectedFrame;
    readonly HashSet<string> _expandedVariablePaths = new();
    IReadOnlyList<StackFrameInfo> _currentStack = Array.Empty<StackFrameInfo>();

    public RunSession? Session => _session;

    /// <summary>Runs (debug = false) or debugs (debug = true) the active document.</summary>
    public async Task RunAsync(bool debug, bool stopOnEntry = false)
    {
        if (ActiveDocument == null || _state != IdeState.Idle) return;
        if (!debug && _settings.ExternalConsole)
        {
            await RunExternalAsync();
            return;
        }
        var outcome = await BuildAsync(debug: true, showSuccess: false);
        if (outcome?.Result.Program == null) return;
        StartSession(outcome, debug, stopOnEntry);
    }

    void StartSession(BuildOutcome outcome, bool debug, bool stopOnEntry)
    {
        var doc = outcome.Document;
        if (ConsoleView.ClearOnRun) ConsoleView.Clear();
        var cts = new CancellationTokenSource();
        var workDir = doc.Directory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var io = new IdeProgramIO(Dispatcher, workDir, cts.Token,
            text => ConsoleView.Append(text),
            waiting =>
            {
                ConsoleView.SetWaiting(waiting);
                if (waiting && _state is IdeState.Running or IdeState.Debugging)
                {
                    SetStatusMessage("The program is waiting for input in the console.");
                    ShowBottomPanel("console");
                }
            });
        RunSession session;
        try
        {
            session = new RunSession(outcome.Result.Program!, io, cts, debug,
                new EditorRef(doc.DisplayName, doc.CompilePath, outcome.Service.DisplayName));
        }
        catch (Exception ex)
        {
            ShowError($"Cannot prepare the execution: {ex.Message}", "Run");
            return;
        }

        if (debug)
        {
            try
            {
                var dbg = session.Execution.Debugger;
                dbg.StopOnEntry = stopOnEntry || _settings.StopOnEntry;
                dbg.BreakOnError = _settings.BreakOnError;
                var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in _documents)
                {
                    dbg.SetBreakpoints(d.CompilePath, d.BreakpointLines);
                    sent.Add(d.CompilePath);
                }
                foreach (var (file, lines) in _settings.Breakpoints)
                    if (!sent.Contains(file)) dbg.SetBreakpoints(file, lines);
                // Paused is raised on the execution thread, which then blocks: marshal to the UI thread.
                dbg.Paused += info => Dispatcher.BeginInvoke(new Action(() => OnPaused(session, info)));
            }
            catch (Exception ex)
            {
                AppendOutput($"Debugger unavailable for {outcome.Service.DisplayName}: {ex.Message} — running without debugging.");
                session = new RunSession(outcome.Result.Program!, io, cts, false, session.Main);
                debug = false;
            }
        }

        _session = session;
        _allErrors.RemoveAll(e => e.IsRuntime);
        RefreshErrorList();
        ClearDebugPanels();
        ConsoleView.SetRunning(true);
        ConsoleView.AppendLine($"[{(debug ? "Debugging" : "Running")} {doc.DisplayName} — {DateTime.Now:HH:mm:ss}]", ConsoleTextKind.Info);
        ShowBottomPanel("console");
        SetState(debug ? IdeState.Debugging : IdeState.Running, debug ? "Debugging (F5 continue, Shift+F5 stop)" : "Running (Shift+F5 to stop)");
        AppendOutput($"{(debug ? "Debugging" : "Running")} {doc.DisplayName}…");

        var task = session.Start();
        task.ContinueWith(t => Dispatcher.BeginInvoke(new Action(() => OnFinished(session, t.Result))), TaskScheduler.Default);
    }

    void OnFinished(RunSession session, ExecutionResult result)
    {
        if (_session != session) return;
        session.IO.Flush();
        _session = null;
        ClearExecutionLine();
        ClearDebugPanels();
        ConsoleView.SetRunning(false);
        var elapsed = DateTime.Now - session.Started;
        string duration = elapsed.TotalSeconds < 1 ? $"{elapsed.TotalMilliseconds:F0} ms" : $"{elapsed.TotalSeconds:F1} s";
        string message;
        if (result.Error != null)
        {
            var err = result.Error;
            ConsoleView.AppendLine(ProgramHost.FormatError(err), ConsoleTextKind.Error);
            message = $"Runtime error: {err.Message}";
            AppendOutput(ProgramHost.FormatError(err));
            AddRuntimeError(err.Message, string.IsNullOrEmpty(err.File) ? session.Main.CompilePath : err.File, err.Line, err.Column);
            if (err.Line > 0 && !App.Options.Automated) NavigateTo(string.IsNullOrEmpty(err.File) ? session.Main.CompilePath : err.File, err.Line, err.Column);
        }
        else if (result.Cancelled || session.StopRequested)
        {
            ConsoleView.AppendLine($"[Program stopped — {duration}]", ConsoleTextKind.Info);
            message = "Program stopped.";
        }
        else
        {
            ConsoleView.AppendLine($"[Program finished with exit code {result.ExitCode} — {duration}]", ConsoleTextKind.Info);
            message = $"Program finished (exit code {result.ExitCode}).";
        }
        AppendOutput(message);
        LastResult = result;
        SetState(IdeState.Idle, message);
        RunFinished?.Invoke(result);
    }

    /// <summary>Last execution result (self-test).</summary>
    public ExecutionResult? LastResult { get; private set; }
    public event Action<ExecutionResult>? RunFinished;
    public event Action<PauseInfo>? DebuggerPaused;

    void OnPaused(RunSession session, PauseInfo info)
    {
        if (_session != session || session.StopRequested) return;
        session.IsPaused = true;
        session.LastPause = info;
        session.IO.Flush();
        _selectedFrame = 0;
        string reason = info.Reason switch
        {
            PauseReason.Breakpoint => "breakpoint",
            PauseReason.Step => "step",
            PauseReason.PauseRequest => "pause requested",
            PauseReason.Exception => "runtime error",
            PauseReason.Entry => "first statement",
            _ => "",
        };
        SetState(IdeState.Paused, $"Paused ({reason}) — {System.IO.Path.GetFileName(info.File)}, line {info.Line}");
        ShowExecutionPoint(info.File, info.Line, info.Column, top: true);
        LoadStack();
        LoadVariables(0);
        StackList.Opacity = VariablesTree.Opacity = 1;
        if (info.Reason == PauseReason.Exception)
        {
            var msg = info.Message ?? "runtime error";
            ConsoleView.AppendLine($"Runtime error: {msg}", ConsoleTextKind.Error);
            var doc = FindDocument(info.File);
            doc?.Markers.Add(info.Line, info.Column, Severity.Error, msg);
            ShowBottomPanel("variables");
            if (!App.Options.Automated)
                MessageBox.Show(this,
                    $"The program stopped on a runtime error:\n\n{msg}\n\n{System.IO.Path.GetFileName(info.File)}, line {info.Line}, column {info.Column}\n\n" +
                    "Inspect the call stack and the variables, then continue (F5) or stop (Shift+F5).",
                    "Runtime error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else if (!ConsoleView.IsWaiting)
            ShowBottomPanel("variables");
        if (!App.Options.Automated)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }
        DebuggerPaused?.Invoke(info);
    }

    async Task DebugOrContinueAsync()
    {
        if (_state == IdeState.Paused) Resume(d => d.Continue());
        else if (_state == IdeState.Idle) await RunAsync(true);
    }

    public async Task StepAsync(StepKind kind)
    {
        if (_state == IdeState.Paused)
        {
            Resume(kind switch
            {
                StepKind.Into => d => d.StepInto(),
                StepKind.Over => d => d.StepOver(),
                _ => d => d.StepOut(),
            });
        }
        else if (_state == IdeState.Idle && kind != StepKind.Out)
        {
            await RunAsync(true, stopOnEntry: true);
        }
    }

    public void Resume(Action<IDebugger> action)
    {
        var s = _session;
        if (s == null || !s.IsPaused || s.Debugger == null) return;
        s.IsPaused = false;
        ClearExecutionLine();
        StackList.Opacity = VariablesTree.Opacity = 0.45;
        SetState(IdeState.Debugging, "Debugging…");
        try { action(s.Debugger); }
        catch (Exception ex) { AppendOutput("Debugger: " + ex.Message); }
    }

    void PauseExecution()
    {
        var dbg = _session?.Debugger;
        if (dbg == null) return;
        try { dbg.RequestPause(); } catch (Exception ex) { AppendOutput("Debugger: " + ex.Message); }
        SetStatusMessage("Pause requested…");
    }

    public void StopExecution()
    {
        if (_externalProcess != null)
        {
            try { if (!_externalProcess.HasExited) _externalProcess.Kill(entireProcessTree: true); } catch { }
            return;
        }
        var s = _session;
        if (s == null) return;
        s.StopRequested = true;
        s.Cts.Cancel();
        if (s.IsPaused)
        {
            s.IsPaused = false;
            try { s.Debugger?.Continue(); } catch { }
        }
        SetStatusMessage("Stopping the program…");
        // A program that ignores cancellation cannot be killed (.NET thread): detach it.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_session == s)
            {
                AppendOutput("The program does not respond to the stop request; it is abandoned in the background.");
                OnFinished(s, new ExecutionResult(130, null, Cancelled: true));
            }
        };
        timer.Start();
    }

    async Task RestartAsync()
    {
        bool debug = _session?.IsDebugging ?? false;
        StopExecution();
        for (int i = 0; i < 40 && _state != IdeState.Idle; i++) await Task.Delay(100);
        if (_state == IdeState.Idle) await RunAsync(debug);
    }

    // ------------------------------------------------------------------ external console

    async Task RunExternalAsync()
    {
        var runner = RunnerLocator.Find();
        if (runner == null)
        {
            ShowError(RunnerLocator.NotFoundMessage, "Run");
            return;
        }
        var outcome = await BuildAsync(debug: true, showSuccess: false);
        if (outcome?.Result.Program == null) return;
        var doc = outcome.Document;
        try
        {
            var dir = System.IO.Path.Combine(PathLocator.TempDirectory, "run");
            System.IO.Directory.CreateDirectory(dir);
            var baseName = System.IO.Path.GetFileNameWithoutExtension(doc.DisplayName);
            var exe = System.IO.Path.Combine(dir, baseName + ".exe");
            try { if (System.IO.File.Exists(exe)) System.IO.File.Delete(exe); }
            catch { exe = System.IO.Path.Combine(dir, $"{baseName}-{Guid.NewGuid().ToString("N")[..6]}.exe"); }
            ExePackage.Write(runner, exe, outcome.Service.Id, outcome.Result.Program.Serialize());
            var workDir = doc.Directory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var psi = new ProcessStartInfo("cmd.exe",
                $"/v:on /c \"title {baseName} — AlgolStudio & \"{exe}\" & echo. & echo [Program finished with exit code !errorlevel!] & pause\"")
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = workDir,
            };
            var p = Process.Start(psi)!;
            p.EnableRaisingEvents = true;
            _externalProcess = p;
            p.Exited += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                _externalProcess = null;
                AppendOutput("External console closed.");
                SetState(IdeState.Idle, "External console closed.");
            }));
            AppendOutput($"Running in an external console: {exe}");
            SetState(IdeState.External, "The program is running in an external Windows console.");
        }
        catch (Exception ex)
        {
            ShowError($"Cannot start the external console: {ex.Message}", "Run");
        }
    }

    // ------------------------------------------------------------------ current line, call stack, variables

    void ShowExecutionPoint(string file, int line, int column, bool top)
    {
        ClearExecutionLine();
        var doc = FindDocument(file);
        if (doc == null && !string.IsNullOrEmpty(file) && System.IO.File.Exists(file)) doc = OpenFile(file);
        if (doc == null && string.IsNullOrEmpty(file) && _session != null) doc = FindDocument(_session.Main.CompilePath);
        if (doc == null || line <= 0)
        {
            // Frame without available source (standard library, prelude…)
            if (!string.IsNullOrEmpty(file)) SetStatusMessage($"Source not available: {file}, line {line}");
            return;
        }
        Activate(doc);
        doc.SetExecutionLine(line, top);
        doc.GoTo(line, Math.Max(1, column));
    }

    void ClearExecutionLine()
    {
        foreach (var d in _documents)
            if (d.ExecutionLine != null) d.SetExecutionLine(null);
    }

    void ClearCallerHighlight()
    {
        // Click in the editor after selecting a caller: go back to the current line.
        if (_session?.IsPaused == true && _selectedFrame != 0 && _session.LastPause is { } p)
        {
            _selectedFrame = 0;
            ClearExecutionLine();
            FindDocument(p.File)?.SetExecutionLine(p.Line, true);
            foreach (var it in (StackList.ItemsSource as IEnumerable<StackItem>) ?? Array.Empty<StackItem>()) it.IsSelectedFrame = it.Index == 0;
            LoadVariables(0);
        }
    }

    void ClearDebugPanels()
    {
        _currentStack = Array.Empty<StackFrameInfo>();
        StackList.ItemsSource = null;
        VariablesTree.ItemsSource = null;
        StackEmpty.Visibility = VariablesEmpty.Visibility = Visibility.Visible;
        VariablesHeader.Text = "Name = value   (type)";
        StackList.Opacity = VariablesTree.Opacity = 1;
    }

    void LoadStack()
    {
        var dbg = _session?.Debugger;
        if (dbg == null) return;
        try { _currentStack = dbg.GetStack(); }
        catch (Exception ex) { _currentStack = Array.Empty<StackFrameInfo>(); AppendOutput("Call stack unavailable: " + ex.Message); }
        var items = _currentStack.Select((f, i) => new StackItem(i, f)).ToList();
        if (items.Count > 0) items[Math.Min(_selectedFrame, items.Count - 1)].IsSelectedFrame = true;
        StackList.ItemsSource = items;
        StackEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public IReadOnlyList<StackItem> StackItems => (StackList.ItemsSource as IReadOnlyList<StackItem>) ?? Array.Empty<StackItem>();
    public IReadOnlyList<VariableNode> VariableNodes => (VariablesTree.ItemsSource as IReadOnlyList<VariableNode>) ?? Array.Empty<VariableNode>();

    void LoadVariables(int frame)
    {
        var dbg = _session?.Debugger;
        if (dbg == null) return;
        IReadOnlyList<VariableInfo> vars;
        try { vars = dbg.GetVariables(frame); }
        catch (Exception ex) { vars = Array.Empty<VariableInfo>(); AppendOutput("Variables unavailable: " + ex.Message); }
        string frameName = frame < _currentStack.Count ? _currentStack[frame].Name : "#" + frame;
        IReadOnlyList<VariableInfo> LoadChildren(VariableInfo v)
        {
            if (_session?.IsPaused != true || _session.Debugger != dbg)
                return new[] { new VariableInfo("(program is running)", "", "") };
            return dbg.GetChildren(v);
        }
        var nodes = new List<VariableNode>();
        for (int i = 0; i < vars.Count; i++)
        {
            var node = new VariableNode(vars[i], frameName + "/" + vars[i].Name, LoadChildren) { ExpandedPaths = _expandedVariablePaths };
            nodes.Add(node);
        }
        VariablesTree.ItemsSource = nodes;
        foreach (var n in nodes) if (_expandedVariablePaths.Contains(n.Path)) n.IsExpanded = true;
        VariablesEmpty.Visibility = nodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        VariablesEmpty.Text = nodes.Count == 0 && _session?.IsPaused == true
            ? "No variables in this block."
            : "Variables are shown when the program is paused in the debugger.";
        VariablesHeader.Text = $"Variables of '{frameName}'   —   name = value   (type)";
    }

    void OnStackDoubleClick(object sender, MouseButtonEventArgs e) => SelectFrameFromList();

    void OnStackKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { SelectFrameFromList(); e.Handled = true; }
    }

    void SelectFrameFromList()
    {
        if (StackList.SelectedItem is StackItem item) SelectFrame(item.Index);
    }

    public void SelectFrame(int index)
    {
        if (_session?.IsPaused != true) return;
        var items = StackItems;
        if (index < 0 || index >= items.Count) return;
        _selectedFrame = index;
        foreach (var it in items) it.IsSelectedFrame = it.Index == index;
        LoadVariables(index);
        var f = items[index].Frame;
        ShowExecutionPoint(f.File, f.Line, f.Column, top: index == 0);
    }
}
