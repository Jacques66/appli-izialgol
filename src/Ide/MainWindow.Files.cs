using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Studio.Ide.Editor;
using Studio.Ide.Language;
using Studio.Ide.Services;
using Studio.Tooling;

namespace Studio.Ide;

public partial class MainWindow
{
    const string FileFilter =
        "Programmes Simula 67 et Algol 68 (*.sim;*.a68)|*.sim;*.a68|Simula 67 (*.sim)|*.sim|Algol 68 (*.a68)|*.a68|Tous les fichiers (*.*)|*.*";

    string FileFilterWithEcho => LanguageRegistry.TestLanguageEnabled
        ? FileFilter + "|Echo, langage de test (*.echo)|*.echo"
        : FileFilter;

    public EditorDocument NewDocument(LanguageDefinition lang, string? text = null)
    {
        var doc = new EditorDocument(lang, null, text ?? lang.NewFileTemplate);
        AddDocument(doc);
        if (text == null)
        {
            // Curseur sur la ligne du message
            doc.GoTo(Math.Min(3, doc.Editor.Document.LineCount), 4);
        }
        return doc;
    }

    void OpenWithDialog()
    {
        var dlg = new OpenFileDialog
        {
            Filter = FileFilterWithEcho,
            Multiselect = true,
            Title = "Ouvrir un programme",
            InitialDirectory = ActiveDocument?.Directory ?? _settings.ExplorerRoot ?? "",
        };
        if (dlg.ShowDialog(this) == true)
            foreach (var f in dlg.FileNames) OpenFile(f);
    }

    void OpenFolderWithDialog()
    {
        var dlg = new OpenFolderDialog { Title = "Ouvrir un dossier dans l'explorateur", InitialDirectory = _settings.ExplorerRoot ?? "" };
        if (dlg.ShowDialog(this) == true)
        {
            SetExplorerRoot(dlg.FolderName);
            if (!_settings.ShowExplorer) { _settings.ShowExplorer = true; ApplyPanelLayout(); }
        }
    }

    /// <summary>Ouvre un fichier (ou active son onglet s'il est deja ouvert).</summary>
    public EditorDocument? OpenFile(string path, bool activate = true)
    {
        try
        {
            path = System.IO.Path.GetFullPath(path);
            var existing = _documents.FirstOrDefault(d => PathLocator.SamePath(d.FilePath, path));
            if (existing != null)
            {
                if (activate) Activate(existing);
                return existing;
            }
            var (text, enc) = EditorDocument.ReadFile(path);
            var lang = LanguageDefinitions.ByExtension(path);
            if (lang == null || (lang.Id == "echo" && !LanguageRegistry.TestLanguageEnabled))
                lang = GuessLanguage(text);
            var doc = new EditorDocument(lang, path, text, enc) { LastWriteUtc = System.IO.File.GetLastWriteTimeUtc(path) };
            AddDocument(doc, activate);
            if (_settings.Breakpoints.TryGetValue(path, out var bps)) doc.SetBreakpoints(bps);
            if (_settings.CaretOffsets.TryGetValue(path, out var caret) && caret <= doc.Editor.Document.TextLength)
            {
                doc.Editor.CaretOffset = caret;
                Dispatcher.BeginInvoke(new Action(() => doc.Editor.TextArea.Caret.BringCaretToView()), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            _settings.AddRecentFile(path);
            ScheduleBackgroundCheck();
            return doc;
        }
        catch (Exception ex)
        {
            if (!App.Options.Automated)
                MessageBox.Show(this, $"Impossible d'ouvrir le fichier\n{path}\n\n{ex.Message}", "Ouvrir", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendOutput($"Impossible d'ouvrir {path} : {ex.Message}");
            return null;
        }
    }

    /// <summary>Extension inconnue : Algol 68 si on voit des mots gras en majuscules, sinon Simula.</summary>
    static LanguageDefinition GuessLanguage(string text)
    {
        var t = text.Length > 4000 ? text[..4000] : text;
        int algol = System.Text.RegularExpressions.Regex.Matches(t, @"\b(BEGIN|END|PROC|INT|REAL|MODE|FI|OD|ESAC)\b").Count;
        int simula = System.Text.RegularExpressions.Regex.Matches(t, @"\b(begin|end|procedure|integer|class|OutText|OutImage)\b", System.Text.RegularExpressions.RegexOptions.None).Count;
        return algol > simula ? LanguageDefinitions.Algol68 : LanguageDefinitions.Simula;
    }

    public bool SaveDocument(EditorDocument doc)
    {
        if (doc.FilePath == null) return SaveDocumentAs(doc);
        try
        {
            doc.Save(doc.FilePath);
            SetStatusMessage($"Enregistré : {doc.FilePath}");
            UpdateStatusBar();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible d'enregistrer\n{doc.FilePath}\n\n{ex.Message}", "Enregistrer", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    public bool SaveDocumentAs(EditorDocument doc, string? path = null)
    {
        if (path == null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = FileFilterWithEcho,
                FilterIndex = doc.Language.Id switch { "simula" => 2, "algol68" => 3, "echo" => 5, _ => 1 },
                FileName = doc.FilePath != null ? System.IO.Path.GetFileName(doc.FilePath) : doc.UntitledName,
                DefaultExt = doc.Language.DefaultExtension,
                AddExtension = true,
                InitialDirectory = doc.Directory ?? _settings.ExplorerRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Title = "Enregistrer sous",
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        var oldPath = doc.FilePath;
        try
        {
            doc.Save(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible d'enregistrer\n{path}\n\n{ex.Message}", "Enregistrer sous", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        _settings.AddRecentFile(path);
        if (oldPath == null || !PathLocator.SamePath(oldPath, path)) OnDocumentBreakpointsChanged(doc);
        SetStatusMessage($"Enregistré : {path}");
        UpdateStatusBar();
        UpdateTitle();
        UpdateOutline();
        return true;
    }

    void SaveAll()
    {
        foreach (var d in _documents.Where(d => d.IsModified).ToList())
            if (!SaveDocument(d)) break;
    }

    /// <summary>Demande d'enregistrer les fichiers modifies. Rend false si l'utilisateur annule.</summary>
    bool ConfirmSaveModified(IList<EditorDocument> docs)
    {
        var modified = docs.Where(d => d.IsModified).ToList();
        if (modified.Count == 0) return true;
        var names = string.Join("\n", modified.Select(d => "  • " + d.DisplayName));
        var r = MessageBox.Show(this, $"Enregistrer les modifications des fichiers suivants ?\n\n{names}",
            "Simula 67 & Algol 68 Studio", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes)
            foreach (var d in modified)
            {
                Activate(d);
                if (!SaveDocument(d)) return false;
            }
        return true;
    }

    public bool CloseDocument(EditorDocument doc, bool force = false)
    {
        if (!force && !App.Options.Automated && !ConfirmSaveModified(new[] { doc })) return false;
        if (doc.FilePath != null) _settings.CaretOffsets[doc.FilePath] = doc.Editor.CaretOffset;
        int idx = DocTabs.Items.IndexOf(doc.Tab);
        DocTabs.Items.Remove(doc.Tab);
        _documents.Remove(doc);
        RemoveMarkersFor(doc);
        if (DocTabs.Items.Count > 0) DocTabs.SelectedIndex = Math.Min(idx, DocTabs.Items.Count - 1);
        UpdateWelcome();
        UpdateStatusBar();
        UpdateOutline();
        return true;
    }

    void CloseAll()
    {
        if (!App.Options.Automated && !ConfirmSaveModified(_documents.ToList())) return;
        foreach (var d in _documents.ToList()) CloseDocument(d, force: true);
    }

    // ------------------------------------------------------------------ menus dynamiques

    void OnRecentMenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != RecentFilesMenu) return;
        RecentFilesMenu.Items.Clear();
        var files = _settings.RecentFiles.Where(System.IO.File.Exists).ToList();
        int i = 1;
        foreach (var f in files)
        {
            var mi = new MenuItem { Header = $"_{i++} {System.IO.Path.GetFileName(f)}", ToolTip = f, InputGestureText = ShortDir(f) };
            mi.Click += (_, _) => OpenFile(f);
            RecentFilesMenu.Items.Add(mi);
        }
        if (files.Count == 0) RecentFilesMenu.Items.Add(new MenuItem { Header = "(aucun)", IsEnabled = false });
        else
        {
            RecentFilesMenu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "Effacer la liste" };
            clear.Click += (_, _) => { _settings.RecentFiles.Clear(); UpdateWelcome(); };
            RecentFilesMenu.Items.Add(clear);
        }
    }

    void OnRecentFoldersMenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != RecentFoldersMenu) return;
        RecentFoldersMenu.Items.Clear();
        var dirs = _settings.RecentFolders.Where(System.IO.Directory.Exists).ToList();
        foreach (var d in dirs)
        {
            var mi = new MenuItem { Header = d.Replace("_", "__") };
            mi.Click += (_, _) => SetExplorerRoot(d);
            RecentFoldersMenu.Items.Add(mi);
        }
        if (dirs.Count == 0) RecentFoldersMenu.Items.Add(new MenuItem { Header = "(aucun)", IsEnabled = false });
    }

    static string ShortDir(string f)
    {
        var d = System.IO.Path.GetDirectoryName(f) ?? "";
        return d.Length > 50 ? "…" + d[^48..] : d;
    }

    public static List<string> ListSamples()
    {
        var root = PathLocator.SamplesDirectory;
        var list = new List<string>();
        if (root == null) return list;
        foreach (var (sub, ext) in new[] { ("simula", "*.sim"), ("algol68", "*.a68") })
        {
            var dir = System.IO.Path.Combine(root, sub);
            if (System.IO.Directory.Exists(dir))
                list.AddRange(System.IO.Directory.GetFiles(dir, ext, System.IO.SearchOption.AllDirectories)
                    .Where(f => !IsNonSampleDir(System.IO.Path.GetRelativePath(dir, f)))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }
        return list;
    }

    /// <summary>Sous-dossiers qui ne contiennent pas d'exemples compilables (fichiers inclus, erreurs volontaires).</summary>
    static bool IsNonSampleDir(string relPath)
    {
        var parts = relPath.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return parts.Take(parts.Length - 1).Any(p => p.Equals("include", StringComparison.OrdinalIgnoreCase) || p.Equals("erreurs", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Ajoute au menu les exemples d'un dossier, avec un sous-menu par sous-dossier.</summary>
    int FillSamplesMenu(MenuItem group, string dir, string ext)
    {
        int count = 0;
        foreach (var sub in System.IO.Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            if (System.IO.Path.GetFileName(sub).Equals("include", StringComparison.OrdinalIgnoreCase)) continue;
            var subMenu = new MenuItem { Header = System.IO.Path.GetFileName(sub).Replace("_", "__") };
            int n = FillSamplesMenu(subMenu, sub, ext);
            if (n > 0) { group.Items.Add(subMenu); count += n; }
        }
        foreach (var f in System.IO.Directory.GetFiles(dir, ext).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var mi = new MenuItem { Header = System.IO.Path.GetFileName(f).Replace("_", "__"), ToolTip = FirstCommentLine(f) ?? f };
            mi.Click += (_, _) => OpenFile(f);
            group.Items.Add(mi);
            count++;
        }
        return count;
    }

    void OnExamplesMenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != ExamplesMenu) return;
        ExamplesMenu.Items.Clear();
        var root = PathLocator.SamplesDirectory;
        if (root == null)
        {
            ExamplesMenu.Items.Add(new MenuItem { Header = "(dossier samples introuvable)", IsEnabled = false });
            return;
        }
        foreach (var (sub, ext, title) in new[] { ("simula", "*.sim", "Simula 67"), ("algol68", "*.a68", "Algol 68") })
        {
            var group = new MenuItem { Header = title };
            var dir = System.IO.Path.Combine(root, sub);
            int count = System.IO.Directory.Exists(dir) ? FillSamplesMenu(group, dir, ext) : 0;
            if (count == 0) group.Items.Add(new MenuItem { Header = "(aucun exemple)", IsEnabled = false });
            ExamplesMenu.Items.Add(group);
        }
        ExamplesMenu.Items.Add(new Separator());
        var browse = new MenuItem { Header = "Afficher le dossier des exemples dans l'explorateur" };
        browse.Click += (_, _) => { SetExplorerRoot(root); if (!_settings.ShowExplorer) { _settings.ShowExplorer = true; ApplyPanelLayout(); } };
        ExamplesMenu.Items.Add(browse);
    }

    /// <summary>Premiere ligne de commentaire d'un exemple (infobulle du menu).</summary>
    static string? FirstCommentLine(string path)
    {
        try
        {
            foreach (var line in System.IO.File.ReadLines(path).Take(6))
            {
                var t = line.Trim();
                if (t.StartsWith("comment", StringComparison.OrdinalIgnoreCase) || t.StartsWith('!') || t.StartsWith('#') || t.StartsWith("CO"))
                    return t.Length > 120 ? t[..120] + "…" : t;
            }
        }
        catch { }
        return null;
    }

    // ------------------------------------------------------------------ navigation

    /// <summary>Va a (fichier, ligne, colonne) : onglet ouvert correspondant, sinon ouverture du fichier.</summary>
    public EditorDocument? NavigateTo(string file, int line, int column)
    {
        var doc = FindDocument(file);
        if (doc == null && !string.IsNullOrEmpty(file) && System.IO.File.Exists(file)) doc = OpenFile(file);
        doc ??= ActiveDocument;
        if (doc == null) return null;
        Activate(doc);
        if (line > 0) doc.GoTo(line, Math.Max(1, column));
        doc.Editor.TextArea.Focus();
        return doc;
    }

    /// <summary>Document correspondant a un chemin du compilateur (chemin complet, nom provisoire ou nom de fichier).</summary>
    public EditorDocument? FindDocument(string? file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        return _documents.FirstOrDefault(d => PathLocator.SamePath(d.CompilePath, file))
               ?? _documents.FirstOrDefault(d => string.Equals(d.DisplayName, System.IO.Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ points d'arret

    void OnDocumentBreakpointsChanged(EditorDocument doc)
    {
        var lines = doc.BreakpointLines;
        if (doc.FilePath != null)
        {
            if (lines.Count == 0) _settings.Breakpoints.Remove(doc.FilePath);
            else _settings.Breakpoints[doc.FilePath] = lines;
        }
        _session?.Debugger?.SetBreakpoints(doc.CompilePath, lines);
    }

    void ClearAllBreakpoints()
    {
        foreach (var d in _documents) d.SetBreakpoints(Array.Empty<int>());
        foreach (var key in _settings.Breakpoints.Keys.ToList())
        {
            _session?.Debugger?.SetBreakpoints(key, Array.Empty<int>());
            _settings.Breakpoints.Remove(key);
        }
        SetStatusMessage("Tous les points d'arrêt ont été supprimés.");
    }
}
