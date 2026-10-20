using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Studio.Common;
using Studio.Ide.Editor;
using Studio.Ide.Panels;
using Studio.Ide.Services;
using Studio.Tooling;

namespace Studio.Ide;

public partial class MainWindow
{
    static readonly SemaphoreSlim CompileLock = new(1, 1);
    readonly List<ErrorItem> _allErrors = new();
    readonly ObservableCollection<ErrorItem> _visibleErrors = new();
    readonly DispatcherTimer _backgroundCheckTimer;
    int _backgroundCheckVersion;

    /// <summary>Resultat d'une compilation avec son contexte.</summary>
    public sealed record BuildOutcome(EditorDocument Document, ILanguageService Service, CompilationResult Result, TimeSpan Duration);

    /// <summary>Compile le document actif. silent = verification en arriere-plan (pas de sortie).</summary>
    async Task<BuildOutcome?> CompileAsync(EditorDocument doc, bool debug, bool silent)
    {
        var service = doc.Service;
        if (service == null)
        {
            if (!silent) AppendOutput($"Aucun compilateur pour le langage {doc.Language.DisplayName}.");
            return null;
        }
        var text = doc.Editor.Document.Text;
        var path = doc.CompilePath;
        // Instantane des fichiers ouverts (texte non enregistre) pour les inclusions
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
                        new Diagnostic(Severity.Error, path, 0, 0, $"erreur interne du compilateur : {ex.GetType().Name} : {ex.Message}"),
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
        // Fichier ouvert non enregistre, designe par son nom
        foreach (var (k, v) in openTexts)
            if (string.Equals(System.IO.Path.GetFileName(k), name, StringComparison.OrdinalIgnoreCase)) return new SourceFile(k, v);
        return null;
    }

    /// <summary>Construire (F7) : compile, affiche les diagnostics. Rend le resultat (null si echec).</summary>
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
            AppendOutput($"------ Construction : {doc.DisplayName} ({doc.Language.DisplayName}) — {DateTime.Now:HH:mm:ss} ------");
            var outcome = await CompileAsync(doc, debug, silent: false);
            if (outcome == null) return null;
            var diags = outcome.Result.Diagnostics;
            foreach (var d in diags) AppendOutput(d.ToString());
            int errors = diags.Count(d => d.Severity == Severity.Error);
            int warnings = diags.Count(d => d.Severity == Severity.Warning);
            bool ok = outcome.Result.Success && outcome.Result.Program != null;
            string summary = ok
                ? $"Construction réussie — {errors} erreur(s), {warnings} avertissement(s) — {outcome.Duration.TotalMilliseconds:F0} ms"
                : $"Échec de la construction — {Math.Max(errors, 1)} erreur(s), {warnings} avertissement(s)";
            AppendOutput("========== " + summary + " ==========");
            ShowDiagnostics(diags, outcome.Document);
            if (!ok)
            {
                if (errors == 0) AddRuntimeError("la compilation n'a pas produit de programme", doc.CompilePath, 0, 0, compileError: true);
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

    // ------------------------------------------------------------------ verification en arriere-plan

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

    // ------------------------------------------------------------------ liste d'erreurs et soulignements

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
        FilterErrorsText.Text = e <= 1 ? $"{e} erreur" : $"{e} erreurs";
        FilterWarningsText.Text = w <= 1 ? $"{w} avertissement" : $"{w} avertissements";
        FilterInfosText.Text = i <= 1 ? $"{i} message" : $"{i} messages";
        ErrorsTabHeader.Text = e + w > 0 ? $"Erreurs ({e + w})" : "Erreurs";
    }

    public IReadOnlyList<ErrorItem> ErrorItems => _allErrors;

    void OnErrorFilterChanged(object sender, RoutedEventArgs e) => RefreshErrorList();

    void OnErrorDoubleClick(object sender, MouseButtonEventArgs e) => NavigateToSelectedError();

    void OnErrorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { NavigateToSelectedError(); e.Handled = true; }
    }

    public void NavigateToSelectedError()
    {
        if (ErrorList.SelectedItem is ErrorItem item) NavigateTo(item.File, item.Line, item.Column);
    }

    // ------------------------------------------------------------------ generation d'exe

    /// <summary>Genere un .exe autonome. targetPath null = dialogue « Enregistrer sous ».</summary>
    public async Task<string?> GenerateExeAsync(string? targetPath)
    {
        var doc = ActiveDocument;
        if (doc == null) return null;
        var runner = RunnerLocator.Find();
        if (runner == null)
        {
            ShowError(RunnerLocator.NotFoundMessage, "Générer un .exe");
            return null;
        }
        if (targetPath == null)
        {
            var baseName = System.IO.Path.GetFileNameWithoutExtension(doc.DisplayName);
            var dlg = new SaveFileDialog
            {
                Title = "Générer un exécutable autonome",
                Filter = "Exécutable Windows (*.exe)|*.exe",
                FileName = baseName + ".exe",
                DefaultExt = ".exe",
                InitialDirectory = doc.Directory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            if (dlg.ShowDialog(this) != true) return null;
            targetPath = dlg.FileName;
        }
        var outcome = await BuildAsync(debug: _settings.GenerateDebugInfo, showSuccess: false);
        if (outcome?.Result.Program == null) return null;
        try
        {
            var payload = outcome.Result.Program.Serialize();
            ExePackage.Write(runner, targetPath, outcome.Service.Id, payload);
            var size = new System.IO.FileInfo(targetPath).Length;
            AppendOutput($"Exécutable autonome généré : {targetPath} ({size / 1024.0:F0} Ko, programme {payload.Length} octets)");
            AppendOutput("Il nécessite le runtime .NET 8 (installé avec cet environnement) et peut être copié seul.");
            SetStatusMessage($"Exécutable généré : {targetPath}");
            ShowBottomPanel("output");
            return targetPath;
        }
        catch (Exception ex)
        {
            ShowError($"Impossible de générer {targetPath} :\n{ex.Message}", "Générer un .exe");
            return null;
        }
    }

    public void ShowError(string message, string title)
    {
        AppendOutput("Erreur : " + message.Replace("\n", " "));
        if (!App.Options.Automated)
            MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
