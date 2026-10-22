using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Studio.Common;
using Studio.Ide.Dialogs;
using Studio.Ide.Editor;
using Studio.Ide.Panels;
using Studio.Ide.Services;
using Studio.Tooling;

namespace Studio.Ide;

public partial class MainView
{
    static readonly SemaphoreSlim CompileLock = new(1, 1);
    readonly List<ErrorItem> _allErrors = new();
    readonly ObservableCollection<ErrorItem> _visibleErrors = new();
    readonly DispatcherTimer _backgroundCheckTimer;
    int _backgroundCheckVersion;

    /// <summary>Result of a compilation with its context.</summary>
    public sealed record BuildOutcome(EditorDocument Document, ILanguageService Service, CompilationResult Result, TimeSpan Duration);

    /// <summary>Compiles the active document. silent = background check (no output).</summary>
    async Task<BuildOutcome?> CompileAsync(EditorDocument doc, bool debug, bool silent)
    {
        var service = doc.Service;
        if (service == null)
        {
            if (!silent) AppendOutput($"No compiler for the {doc.Language.DisplayName} language.");
            return null;
        }
        var text = doc.Editor.Document.Text;
        var path = doc.CompilePath;
        // Snapshot of the open files (unsaved text) for includes
        var openTexts = _documents.ToDictionary(d => d.CompilePath, d => d.Editor.Document.Text, StringComparer.OrdinalIgnoreCase);
        var options = new CompileOptions
        {
            Debug = debug,
            IncludeResolver = (name, from) => ResolveInclude(name, from, openTexts),
        };

        var sw = Stopwatch.StartNew();
        CompilationResult result;
        await CompileLock.WaitAsync();
        try
        {
            result = await Task.Run(() =>
            {
                try
                {
                    return service.Compile(new SourceFile(path, text), options);
                }
                catch (Exception ex)
                {
                    return new CompilationResult(new[]
                    {
                        new Diagnostic(Severity.Error, path, 0, 0, $"internal compiler error: {ex.GetType().Name}: {ex.Message}"),
                    }, null);
                }
            });
        }
        finally
        {
            CompileLock.Release();
        }
        sw.Stop();
        return new BuildOutcome(doc, service, result, sw.Elapsed);
    }

    static SourceFile? ResolveInclude(string name, string from, Dictionary<string, string> openTexts)
    {
        var candidates = new List<string>();
        if (System.IO.Path.IsPathRooted(name)) candidates.Add(name);
        else
        {
            var dir = System.IO.Path.GetDirectoryName(from);
            if (!string.IsNullOrEmpty(dir)) candidates.Add(System.IO.Path.Combine(dir, name));
            candidates.Add(name);
        }
        foreach (var c in candidates)
        {
            string full;
            try { full = System.IO.Path.GetFullPath(c); } catch { continue; }
            if (openTexts.TryGetValue(full, out var t)) return new SourceFile(full, t);
            if (System.IO.File.Exists(full))
            {
                try { return new SourceFile(full, EditorDocument.ReadFile(full).Text); } catch { }
            }
        }
        // Unsaved open file, referred to by its name
        foreach (var (k, v) in openTexts)
            if (string.Equals(System.IO.Path.GetFileName(k), name, StringComparison.OrdinalIgnoreCase)) return new SourceFile(k, v);
        return null;
    }

    /// <summary>Build (F7): compiles and shows the diagnostics. Returns the result (null on failure).</summary>
    public async Task<BuildOutcome?> BuildAsync(bool debug = true, bool showSuccess = true)
    {
        var doc = ActiveDocument;
        if (doc == null || _state != IdeState.Idle) return null;
        SetState(IdeState.Building, "");
        _backgroundCheckTimer.Stop();
        _backgroundCheckVersion++;
        try
        {
            AppendOutput("");
            AppendOutput($"------ Build: {doc.DisplayName} ({doc.Language.DisplayName}) — {DateTime.Now:HH:mm:ss} ------");
            var outcome = await CompileAsync(doc, debug, silent: false);
            if (outcome == null) return null;
            var diags = outcome.Result.Diagnostics;
            foreach (var d in diags) AppendOutput(d.ToString());
            int errors = diags.Count(d => d.Severity == Severity.Error);
            int warnings = diags.Count(d => d.Severity == Severity.Warning);
            bool ok = outcome.Result.Success && outcome.Result.Program != null;
            string summary = ok
                ? $"Build succeeded — {errors} error(s), {warnings} warning(s) — {outcome.Duration.TotalMilliseconds:F0} ms"
                : $"Build failed — {Math.Max(errors, 1)} error(s), {warnings} warning(s)";
            AppendOutput("========== " + summary + " ==========");
            ShowDiagnostics(diags, outcome.Document);
            if (!ok)
            {
                if (errors == 0) AddRuntimeError("the compilation produced no program", doc.CompilePath, 0, 0, compileError: true);
                ShowBottomPanel("errors");
                SetStatusMessage(summary);
                if (_visibleErrors.Count > 0) ErrorList.SelectedIndex = 0;
                return null;
            }
            SetStatusMessage(summary);
            if (showSuccess && warnings > 0) ShowBottomPanel("errors");
            else if (showSuccess) ShowBottomPanel("output");
            return outcome;
        }
        finally
        {
            if (_state == IdeState.Building) SetState(IdeState.Idle);
        }
    }

    // ------------------------------------------------------------------ background checking

    void ScheduleBackgroundCheck()
    {
        if (!_settings.BackgroundCheck) return;
        _backgroundCheckTimer.Stop();
        _backgroundCheckTimer.Start();
    }

    async Task BackgroundCheckAsync()
    {
        var doc = ActiveDocument;
        if (doc == null || !_settings.BackgroundCheck || _state != IdeState.Idle || doc.Service == null) return;
        int version = ++_backgroundCheckVersion;
        var outcome = await CompileAsync(doc, debug: true, silent: true);
        if (outcome == null || version != _backgroundCheckVersion || _state != IdeState.Idle) return;
        if (!_documents.Contains(doc)) return;
        ShowDiagnostics(outcome.Result.Diagnostics, doc);
    }

    // ------------------------------------------------------------------ error list and squiggles

    void ShowDiagnostics(IReadOnlyList<Diagnostic> diags, EditorDocument main)
    {
        _allErrors.Clear();
        foreach (var d in _documents) d.Markers.Clear();
        foreach (var d in diags)
        {
            var file = string.IsNullOrEmpty(d.File) ? main.CompilePath : d.File;
            _allErrors.Add(new ErrorItem(d.Severity, d.Message, file, d.Line, d.Column, false));
            var target = FindDocument(file) ?? (string.IsNullOrEmpty(d.File) ? main : null);
            if (target != null && d.Line > 0) target.Markers.Add(d.Line, d.Column, d.Severity, d.Message);
        }
        RefreshErrorList();
    }

    void AddRuntimeError(string message, string file, int line, int column, bool compileError = false)
    {
        _allErrors.Add(new ErrorItem(Severity.Error, message, file, line, column, !compileError));
        var doc = FindDocument(file);
        if (doc != null && line > 0) doc.Markers.Add(line, column, Severity.Error, message);
        RefreshErrorList();
    }

    void RemoveMarkersFor(EditorDocument doc) => doc.Markers.Clear();

    void RefreshErrorList()
    {
        _visibleErrors.Clear();
        foreach (var e in _allErrors)
        {
            bool show = e.Severity switch
            {
                Severity.Error => FilterErrors.IsChecked == true,
                Severity.Warning => FilterWarnings.IsChecked == true,
                _ => FilterInfos.IsChecked == true,
            };
            if (show) _visibleErrors.Add(e);
        }
        UpdateErrorCounts();
    }

    void UpdateErrorCounts()
    {
        int e = _allErrors.Count(x => x.Severity == Severity.Error);
        int w = _allErrors.Count(x => x.Severity == Severity.Warning);
        int i = _allErrors.Count(x => x.Severity == Severity.Info);
        FilterErrorsText.Text = e == 1 ? $"{e} error" : $"{e} errors";
        FilterWarningsText.Text = w == 1 ? $"{w} warning" : $"{w} warnings";
        FilterInfosText.Text = i == 1 ? $"{i} message" : $"{i} messages";
        ErrorsTabHeader.Text = e + w > 0 ? $"Errors ({e + w})" : "Errors";
    }

    public IReadOnlyList<ErrorItem> ErrorItems => _allErrors;

    void OnErrorFilterChanged(object? sender, RoutedEventArgs e) => RefreshErrorList();

    void OnErrorDoubleClick(object? sender, TappedEventArgs e) => NavigateToSelectedError();

    void OnErrorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { NavigateToSelectedError(); e.Handled = true; }
    }

    public void NavigateToSelectedError()
    {
        if (ErrorList.SelectedItem is ErrorItem item) NavigateTo(item.File, item.Line, item.Column);
    }

    // ------------------------------------------------------------------ exe generation

    /// <summary>Generates a standalone .exe. targetPath null = "Save as" dialog.</summary>
    public async Task<string?> GenerateExeAsync(string? targetPath)
    {
        var doc = ActiveDocument;
        if (doc == null) return null;
        var platform = App.Platform;
        var runner = platform.FindRunner();
        if (runner == null)
        {
            ShowError(RunnerLocator.NotFoundMessage, "Generate executable");
            return null;
        }
        if (targetPath == null)
        {
            var baseName = System.IO.Path.GetFileNameWithoutExtension(doc.DisplayName);
            var ext = platform.ExecutableExtension;
            var file = await SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Generate a standalone executable",
                FileTypeChoices = ext.Length > 0
                    ? new[] { new FilePickerFileType($"{platform.ExecutableDescription} (*{ext})") { Patterns = new[] { "*" + ext } } }
                    : null,
                SuggestedFileName = baseName + ext,
                DefaultExtension = ext.Length > 0 ? ext : null,
                ShowOverwritePrompt = true,
            }, doc.Directory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            targetPath = file?.TryGetLocalPath();
            if (targetPath == null) return null;
        }
        var outcome = await BuildAsync(debug: _settings.GenerateDebugInfo, showSuccess: false);
        if (outcome?.Result.Program == null) return null;
        try
        {
            var payload = outcome.Result.Program.Serialize();
            platform.WriteExecutable(runner, targetPath, outcome.Service.Id, payload);
            var size = new System.IO.FileInfo(targetPath).Length;
            AppendOutput($"Standalone executable generated: {targetPath} ({size / 1024.0:F0} KB, program {payload.Length} bytes)");
            AppendOutput("It needs the .NET 8 runtime (installed with this environment) and can be copied on its own.");
            SetStatusMessage($"Executable generated: {targetPath}");
            ShowBottomPanel("output");
            return targetPath;
        }
        catch (Exception ex)
        {
            ShowError($"Cannot generate {targetPath}:\n{ex.Message}", "Generate .exe");
            return null;
        }
    }

    public void ShowError(string message, string title)
    {
        AppendOutput("Error: " + message.Replace("\n", " "));
        if (!App.Options.Automated)
            MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
