using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Studio.Ide.Dialogs;
using Studio.Ide.Editor;
using Studio.Ide.Language;
using Studio.Ide.Services;
using Studio.Tooling;

namespace Studio.Ide;

public partial class MainView
{
    const string FileFilter =
        "All programs (*.sim;*.a68;*.a60;*.alg;*.s1;*.pas;*.p)|*.sim;*.a68;*.a60;*.alg;*.s1;*.pas;*.p|Simula 67 (*.sim)|*.sim|Algol 68 (*.a68)|*.a68|" +
        "Algol 60 (*.a60;*.alg)|*.a60;*.alg|Simula I (*.s1)|*.s1|Pascal (*.pas;*.p)|*.pas;*.p|All files (*.*)|*.*";

    string FileFilterWithEcho => LanguageRegistry.TestLanguageEnabled
        ? FileFilter + "|Echo, test language (*.echo)|*.echo"
        : FileFilter;

    public EditorDocument NewDocument(LanguageDefinition lang, string? text = null)
    {
        var doc = new EditorDocument(lang, null, text ?? lang.NewFileTemplate);
        AddDocument(doc);
        if (text == null)
        {
            // Caret on the message line
            doc.GoTo(Math.Min(3, doc.Editor.Document.LineCount), 4);
        }
        return doc;
    }

    /// <summary>File types of the file dialogs, from a "description|patterns|…" filter string.</summary>
    static List<FilePickerFileType> FileTypes(string filter)
    {
        var parts = filter.Split('|');
        var list = new List<FilePickerFileType>();
        for (int i = 0; i + 1 < parts.Length; i += 2)
            list.Add(new FilePickerFileType(parts[i]) { Patterns = parts[i + 1].Split(';') });
        return list;
    }

    /// <summary>File dialogs of the window (desktop) or of the page (browser).</summary>
    IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)!.StorageProvider;

    async Task<IStorageFolder?> FolderAsync(string? path) =>
        string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path) || App.Platform.IsVirtualFileSystem
            ? null : await StorageProvider.TryGetFolderFromPathAsync(path);

    async Task OpenWithDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            FileTypeFilter = FileTypes(FileFilterWithEcho),
            AllowMultiple = true,
            Title = "Open a program",
            SuggestedStartLocation = await FolderAsync(ActiveDocument?.Directory ?? _settings.ExplorerRoot),
        });
        foreach (var f in files)
            if ((f.TryGetLocalPath() ?? await ImportAsync(f)) is { } path) OpenFile(path);
    }

    /// <summary>Browser: copies a file of the computer (upload) into the virtual file system and returns its path
    /// (null on the desktop, where the files have a local path).</summary>
    async Task<string?> ImportAsync(IStorageItem item)
    {
        var dir = App.Platform.ImportDirectory;
        if (dir == null || item is not IStorageFile file) return null;
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            var name = System.IO.Path.GetFileName(file.Name);
            var path = System.IO.Path.Combine(dir, name);
            for (int i = 2; System.IO.File.Exists(path); i++)
                path = System.IO.Path.Combine(dir, $"{System.IO.Path.GetFileNameWithoutExtension(name)} ({i}){System.IO.Path.GetExtension(name)}");
            await using (var input = await file.OpenReadAsync())
            await using (var output = System.IO.File.Create(path))
                await input.CopyToAsync(output);
            AppendOutput($"Imported from the computer: {path}");
            OnExplorerRefresh(null, new RoutedEventArgs());
            return path;
        }
        catch (Exception ex)
        {
            ShowError($"Cannot import {file.Name}:\n{ex.Message}", "Open");
            return null;
        }
    }

    /// <summary>Browser: saves a copy of the document on the computer (download).</summary>
    async Task DownloadAsync(EditorDocument doc)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Download to the computer",
                SuggestedFileName = doc.DisplayName,
                DefaultExtension = doc.Language.DefaultExtension,
                ShowOverwritePrompt = true,
            });
            if (file == null) return;
            var bytes = doc.Encoding.GetPreamble().Concat(doc.Encoding.GetBytes(doc.Editor.Document.Text)).ToArray();
            await using (var stream = await file.OpenWriteAsync())
                await stream.WriteAsync(bytes);
            SetStatusMessage($"Downloaded: {file.Name}");
        }
        catch (Exception ex)
        {
            ShowError($"Cannot download {doc.DisplayName}:\n{ex.Message}", "Download");
        }
    }

    async Task OpenFolderWithDialogAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open a folder in the explorer",
            SuggestedStartLocation = await FolderAsync(_settings.ExplorerRoot),
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            SetExplorerRoot(path);
            if (!_settings.ShowExplorer) { _settings.ShowExplorer = true; ApplyPanelLayout(); }
        }
    }

    /// <summary>Opens a file (or activates its tab when it is already open).</summary>
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
                Dispatcher.UIThread.Post(() => doc.Editor.TextArea.Caret.BringCaretToView(), DispatcherPriority.Loaded);
            }
            _settings.AddRecentFile(path);
            ScheduleBackgroundCheck();
            return doc;
        }
        catch (Exception ex)
        {
            if (!App.Options.Automated)
                MessageBox.Show(this, $"Cannot open the file\n{path}\n\n{ex.Message}", "Open", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendOutput($"Cannot open {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Unknown extension: Pascal for a program heading "program name(...);", Simula I for a "SIMULA begin"
    /// block, Algol 60 when its standard procedures or quoted keywords are found, Algol 68 when upper-case bold
    /// words are found, otherwise Simula 67.</summary>
    static LanguageDefinition GuessLanguage(string text)
    {
        var t = text.Length > 4000 ? text[..4000] : text;
        const System.Text.RegularExpressions.RegexOptions ic = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^\s*(\{[^}]*\}\s*|\(\*.*?\*\)\s*)*program\s+[a-z][a-z0-9]*\s*\(", ic | System.Text.RegularExpressions.RegexOptions.Singleline)) return LanguageDefinitions.Pascal;
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"\bSIMULA\s*(\bbegin\b|'BEGIN')", ic)) return LanguageDefinitions.SimulaI;
        int algol60 = System.Text.RegularExpressions.Regex.Matches(t,
            @"\b(outstring|outinteger|outreal|ininteger|inreal|outchar|inchar|outterminator)\b|'(PROCEDURE|INTEGER|BOOLEAN|ARRAY|STEP|UNTIL|SWITCH)'", ic).Count;
        int simula67 = System.Text.RegularExpressions.Regex.Matches(t, @"\b(OutText|OutImage|OutInt|OutReal|InInt|InImage|class|ref)\b", ic).Count;
        if (algol60 > 0 && algol60 >= simula67) return LanguageDefinitions.Algol60;
        int algol = System.Text.RegularExpressions.Regex.Matches(t, @"\b(BEGIN|END|PROC|INT|REAL|MODE|FI|OD|ESAC)\b").Count;
        int simula = System.Text.RegularExpressions.Regex.Matches(t, @"\b(begin|end|procedure|integer|class|OutText|OutImage)\b", System.Text.RegularExpressions.RegexOptions.None).Count;
        return algol > simula ? LanguageDefinitions.Algol68 : LanguageDefinitions.Simula;
    }

    public bool SaveDocument(EditorDocument doc) => Modal.Wait(SaveDocumentAsync(doc));

    public async Task<bool> SaveDocumentAsync(EditorDocument doc)
    {
        if (doc.FilePath == null) return await SaveDocumentAsAsync(doc);
        try
        {
            doc.Save(doc.FilePath);
            SetStatusMessage($"Saved: {doc.FilePath}");
            UpdateStatusBar();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Cannot save\n{doc.FilePath}\n\n{ex.Message}", "Save", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    public bool SaveDocumentAs(EditorDocument doc, string? path = null) => Modal.Wait(SaveDocumentAsAsync(doc, path));

    public async Task<bool> SaveDocumentAsAsync(EditorDocument doc, string? path = null)
    {
        if (path == null)
        {
            if (App.Platform.IsVirtualFileSystem) path = await AskVirtualPathAsync(doc);
            else
            {
                var types = FileTypes(FileFilterWithEcho);
                int typeIndex = doc.Language.Id switch { "simula" => 2, "algol68" => 3, "algol60" => 4, "simula1" => 5, "pascal" => 6, "echo" => 8, _ => 1 };
                var file = await SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    FileTypeChoices = types,
                    SuggestedFileType = types[Math.Min(typeIndex, types.Count) - 1],
                    SuggestedFileName = doc.FilePath != null ? System.IO.Path.GetFileName(doc.FilePath) : doc.UntitledName,
                    DefaultExtension = doc.Language.DefaultExtension,
                    ShowOverwritePrompt = true,
                    Title = "Save as",
                }, doc.Directory ?? _settings.ExplorerRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                path = file?.TryGetLocalPath();
            }
            if (path == null) return false;
        }
        var oldPath = doc.FilePath;
        try
        {
            doc.Save(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Cannot save\n{path}\n\n{ex.Message}", "Save as", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        _settings.AddRecentFile(path);
        if (oldPath == null || !PathLocator.SamePath(oldPath, path)) OnDocumentBreakpointsChanged(doc);
        SetStatusMessage($"Saved: {path}");
        UpdateStatusBar();
        UpdateTitle();
        UpdateOutline();
        if (App.Platform.IsVirtualFileSystem) OnExplorerRefresh(null, new RoutedEventArgs());
        return true;
    }

    /// <summary>Browser: asks for the path of the file in the virtual file system ("Save as").</summary>
    async Task<string?> AskVirtualPathAsync(EditorDocument doc)
    {
        var dir = doc.Directory ?? App.Platform.ImportDirectory ?? _settings.ExplorerRoot ?? "/";
        var suggested = System.IO.Path.Combine(dir, doc.FilePath != null ? System.IO.Path.GetFileName(doc.FilePath) : doc.UntitledName);
        while (true)
        {
            var answer = await MessageBox.PromptAsync(this, "File name (the files are kept in this browser):", "Save as", suggested);
            if (string.IsNullOrWhiteSpace(answer)) return null;
            var path = answer.Trim();
            if (!System.IO.Path.IsPathRooted(path)) path = System.IO.Path.Combine(dir, path);
            if (!System.IO.Path.HasExtension(path)) path += doc.Language.DefaultExtension;
            try
            {
                path = System.IO.Path.GetFullPath(path);
                if (System.IO.Directory.Exists(path)) throw new IOException("This is a folder.");
                if (System.IO.File.Exists(path) && !PathLocator.SamePath(path, doc.FilePath) &&
                    await MessageBox.ShowAsync(this, $"{path}\nalready exists. Replace it?", "Save as",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    suggested = path;
                    continue;
                }
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                return path;
            }
            catch (Exception ex)
            {
                await MessageBox.ShowAsync(this, $"Invalid file name\n{path}\n\n{ex.Message}", "Save as", MessageBoxButton.OK, MessageBoxImage.Error);
                suggested = answer;
            }
        }
    }

    async Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options, string? directory)
    {
        options.SuggestedStartLocation = await FolderAsync(directory);
        return await StorageProvider.SaveFilePickerAsync(options);
    }

    async Task SaveAllAsync()
    {
        foreach (var d in _documents.Where(d => d.IsModified).ToList())
            if (!await SaveDocumentAsync(d)) break;
    }

    /// <summary>Asks to save modified files. Returns false when the user cancels.</summary>
    bool ConfirmSaveModified(IList<EditorDocument> docs) => Modal.Wait(ConfirmSaveModifiedAsync(docs));

    async Task<bool> ConfirmSaveModifiedAsync(IList<EditorDocument> docs)
    {
        var modified = docs.Where(d => d.IsModified).ToList();
        if (modified.Count == 0) return true;
        var names = string.Join("\n", modified.Select(d => "  • " + d.DisplayName));
        var r = await MessageBox.ShowAsync(this, $"Save changes to the following files?\n\n{names}",
            "AlgolStudio", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes)
            foreach (var d in modified)
            {
                Activate(d);
                if (!await SaveDocumentAsync(d)) return false;
            }
        return true;
    }

    public bool CloseDocument(EditorDocument doc, bool force = false) => Modal.Wait(CloseDocumentAsync(doc, force));

    public async Task<bool> CloseDocumentAsync(EditorDocument doc, bool force = false)
    {
        if (!force && !App.Options.Automated && !await ConfirmSaveModifiedAsync(new[] { doc })) return false;
        if (doc.FilePath != null) _settings.CaretOffsets[doc.FilePath] = doc.Editor.CaretOffset;
        int idx = DocTabs.Items.IndexOf(doc.Tab);
        DocTabs.Items.Remove(doc.Tab);
        _documents.Remove(doc);
        RemoveMarkersFor(doc);
        if (DocTabs.Items.Count > 0) DocTabs.SelectedIndex = Math.Min(idx, DocTabs.Items.Count - 1);
        UpdateWelcome();
        UpdateStatusBar();
        UpdateOutline();
        RelayCommand.InvalidateAll();
        return true;
    }

    async Task CloseAllAsync()
    {
        if (!App.Options.Automated && !await ConfirmSaveModifiedAsync(_documents.ToList())) return;
        foreach (var d in _documents.ToList()) await CloseDocumentAsync(d, force: true);
    }

    // ------------------------------------------------------------------ dynamic menus

    void OnRecentMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (e.Source != RecentFilesMenu) return;
        RecentFilesMenu.Items.Clear();
        var files = _settings.RecentFiles.Where(System.IO.File.Exists).ToList();
        int i = 1;
        foreach (var f in files)
        {
            var mi = new MenuItem { Header = $"_{i++} {System.IO.Path.GetFileName(f).Replace("_", "__")}     {ShortDir(f).Replace("_", "__")}" };
            ToolTip.SetTip(mi, f);
            mi.Click += (_, _) => OpenFile(f);
            RecentFilesMenu.Items.Add(mi);
        }
        if (files.Count == 0) RecentFilesMenu.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
        else
        {
            RecentFilesMenu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "Clear the list" };
            clear.Click += (_, _) => { _settings.RecentFiles.Clear(); UpdateWelcome(); };
            RecentFilesMenu.Items.Add(clear);
        }
    }

    void OnRecentFoldersMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (e.Source != RecentFoldersMenu) return;
        RecentFoldersMenu.Items.Clear();
        var dirs = _settings.RecentFolders.Where(System.IO.Directory.Exists).ToList();
        foreach (var d in dirs)
        {
            var mi = new MenuItem { Header = d.Replace("_", "__") };
            mi.Click += (_, _) => SetExplorerRoot(d);
            RecentFoldersMenu.Items.Add(mi);
        }
        if (dirs.Count == 0) RecentFoldersMenu.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
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
        foreach (var (sub, ext) in new[] { ("simula67", "*.sim"), ("algol68", "*.a68"), ("algol60", "*.a60"), ("simula1", "*.s1"), ("pascal", "*.pas") })
        {
            var dir = System.IO.Path.Combine(root, sub);
            if (System.IO.Directory.Exists(dir))
                list.AddRange(System.IO.Directory.GetFiles(dir, ext, System.IO.SearchOption.AllDirectories)
                    .Where(f => !IsNonSampleDir(System.IO.Path.GetRelativePath(dir, f)))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }
        return list;
    }

    /// <summary>Subfolders that do not hold compilable examples (included files, deliberate compile errors).</summary>
    static bool IsNonSampleDir(string relPath)
    {
        var parts = relPath.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return parts.Take(parts.Length - 1).Any(p => p.Equals("include", StringComparison.OrdinalIgnoreCase) || p.Equals("errors", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Welcome page: only the main examples (feature demos and runtime-error demos stay in the Examples menu).</summary>
    static bool IsWelcomeExcluded(string samplePath)
    {
        var parts = samplePath.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return parts.Take(parts.Length - 1).Any(p => p.Equals("features", StringComparison.OrdinalIgnoreCase) || p.Equals("runtime_errors", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Adds the examples of a folder to the menu, with one submenu per subfolder.</summary>
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
            var mi = new MenuItem { Header = System.IO.Path.GetFileName(f).Replace("_", "__") };
            ToolTip.SetTip(mi, FirstCommentLine(f) ?? f);
            mi.Click += (_, _) => OpenFile(f);
            group.Items.Add(mi);
            count++;
        }
        return count;
    }

    void OnExamplesMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (e.Source != ExamplesMenu) return;
        ExamplesMenu.Items.Clear();
        var root = PathLocator.SamplesDirectory;
        if (root == null)
        {
            ExamplesMenu.Items.Add(new MenuItem { Header = "(samples folder not found)", IsEnabled = false });
            return;
        }
        foreach (var (sub, ext, title) in new[] { ("simula67", "*.sim", "Simula 67"), ("algol68", "*.a68", "Algol 68"), ("algol60", "*.a60", "Algol 60"), ("simula1", "*.s1", "Simula I"), ("pascal", "*.pas", "Pascal") })
        {
            var group = new MenuItem { Header = title };
            var dir = System.IO.Path.Combine(root, sub);
            int count = System.IO.Directory.Exists(dir) ? FillSamplesMenu(group, dir, ext) : 0;
            if (count == 0) group.Items.Add(new MenuItem { Header = "(no examples)", IsEnabled = false });
            ExamplesMenu.Items.Add(group);
        }
        ExamplesMenu.Items.Add(new Separator());
        var browse = new MenuItem { Header = "Show the examples folder in the explorer" };
        browse.Click += (_, _) => { SetExplorerRoot(root); if (!_settings.ShowExplorer) { _settings.ShowExplorer = true; ApplyPanelLayout(); } };
        ExamplesMenu.Items.Add(browse);
    }

    /// <summary>First comment line of an example (menu tooltip).</summary>
    static string? FirstCommentLine(string path)
    {
        try
        {
            foreach (var line in System.IO.File.ReadLines(path).Take(6))
            {
                var t = line.Trim();
                if (t.StartsWith("comment", StringComparison.OrdinalIgnoreCase) || t.StartsWith("'COMMENT'", StringComparison.OrdinalIgnoreCase)
                    || t.StartsWith('!') || t.StartsWith('#') || t.StartsWith("CO") || t.StartsWith('{') || t.StartsWith("(*"))
                    return t.Length > 120 ? t[..120] + "…" : t;
            }
        }
        catch { }
        return null;
    }

    // ------------------------------------------------------------------ navigation

    /// <summary>Goes to (file, line, column): the matching open tab, otherwise opens the file.</summary>
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

    /// <summary>Document matching a compiler path (full path, temporary name or file name).</summary>
    public EditorDocument? FindDocument(string? file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        return _documents.FirstOrDefault(d => PathLocator.SamePath(d.CompilePath, file))
               ?? _documents.FirstOrDefault(d => string.Equals(d.DisplayName, System.IO.Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ breakpoints

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
        SetStatusMessage("All breakpoints deleted.");
    }
}
