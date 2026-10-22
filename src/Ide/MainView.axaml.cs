using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Shape = Avalonia.Controls.Shapes.Shape;
using IconPath = Avalonia.Controls.Shapes.Path;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using Studio.Ide.Dialogs;
using Studio.Ide.Editor;
using Studio.Ide.Language;
using Studio.Ide.Panels;
using Studio.Ide.Services;
using Studio.Tooling;

namespace Studio.Ide;

public enum IdeState { Idle, Building, Running, Debugging, Paused, External }

public partial class MainView : UserControl
{
    readonly IdeSettings _settings;
    readonly ObservableCollection<EditorDocument> _documents = new();
    readonly List<OutlineEntry> _outlineAll = new();
    IdeState _state = IdeState.Idle;
    bool _checkingExternalChanges;
    bool _loaded;
    Key _pendingChord = Key.None;
    /// <summary>Last text box that had the focus (target of the Edit menu instead of the editor).</summary>
    TextBox? _lastTextBox;

    /// <summary>Modifier of the command shortcuts: Ctrl (Windows, Linux) or Cmd (macOS).</summary>
    static KeyModifiers Cmd => Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;

    // ------------------------------------------------------------------ commands

    public ICommand CmdNewSimula { get; }
    public ICommand CmdNewAlgol { get; }
    public ICommand CmdNewAlgol60 { get; }
    public ICommand CmdNewSimulaI { get; }
    public ICommand CmdNewPascal { get; }
    public ICommand CmdNewEcho { get; }
    public ICommand CmdOpen { get; }
    public ICommand CmdOpenFolder { get; }
    public ICommand CmdSave { get; }
    public ICommand CmdSaveAs { get; }
    public ICommand CmdSaveAll { get; }
    public ICommand CmdDownload { get; }
    public ICommand CmdClose { get; }
    public ICommand CmdCloseAll { get; }
    public ICommand CmdExit { get; }
    public ICommand CmdUndo { get; }
    public ICommand CmdRedo { get; }
    public ICommand CmdCut { get; }
    public ICommand CmdCopy { get; }
    public ICommand CmdPaste { get; }
    public ICommand CmdSelectAll { get; }
    public ICommand CmdFind { get; }
    public ICommand CmdReplace { get; }
    public ICommand CmdFindNext { get; }
    public ICommand CmdFindPrevious { get; }
    public ICommand CmdGoToLine { get; }
    public ICommand CmdComplete { get; }
    public ICommand CmdComment { get; }
    public ICommand CmdUncomment { get; }
    public ICommand CmdToggleComment { get; }
    public ICommand CmdReindent { get; }
    public ICommand CmdFoldAll { get; }
    public ICommand CmdUnfoldAll { get; }
    public ICommand CmdShowPanel { get; }
    public ICommand CmdZoomIn { get; }
    public ICommand CmdZoomOut { get; }
    public ICommand CmdZoomReset { get; }
    public ICommand CmdBuild { get; }
    public ICommand CmdGenerateExe { get; }
    public ICommand CmdDebug { get; }
    public ICommand CmdRun { get; }
    public ICommand CmdStop { get; }
    public ICommand CmdRestart { get; }
    public ICommand CmdPause { get; }
    public ICommand CmdStepInto { get; }
    public ICommand CmdStepOver { get; }
    public ICommand CmdStepOut { get; }
    public ICommand CmdToggleBreakpoint { get; }
    public ICommand CmdClearBreakpoints { get; }
    public ICommand CmdHelp { get; }
    public ICommand CmdAbout { get; }

    /// <summary>Title of the main window (desktop) or of the page (browser).</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<MainView, string>(nameof(Title), "AlgolStudio");

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public MainView()
    {
        _settings = IdeSettings.Load();
        if (App.Options.Theme != null) _settings.Theme = App.Options.Theme.StartsWith("s", StringComparison.OrdinalIgnoreCase) || App.Options.Theme.StartsWith("d", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
        App.ApplyTheme(IsDarkSetting);

        bool HasDoc(object? _) => ActiveDocument != null;
        bool NotBusy(object? _) => ActiveDocument != null && _state == IdeState.Idle;

        CmdNewSimula = new RelayCommand(_ => NewDocument(LanguageDefinitions.Simula));
        CmdNewAlgol = new RelayCommand(_ => NewDocument(LanguageDefinitions.Algol68));
        CmdNewAlgol60 = new RelayCommand(_ => NewDocument(LanguageDefinitions.Algol60));
        CmdNewSimulaI = new RelayCommand(_ => NewDocument(LanguageDefinitions.SimulaI));
        CmdNewPascal = new RelayCommand(_ => NewDocument(LanguageDefinitions.Pascal));
        CmdNewEcho = new RelayCommand(_ => NewDocument(LanguageDefinitions.Echo));
        CmdOpen = new RelayCommand(async _ => await OpenWithDialogAsync());
        CmdOpenFolder = new RelayCommand(async _ => await OpenFolderWithDialogAsync());
        CmdSave = new RelayCommand(async _ => await SaveDocumentAsync(ActiveDocument!), HasDoc);
        CmdSaveAs = new RelayCommand(async _ => await SaveDocumentAsAsync(ActiveDocument!), HasDoc);
        CmdSaveAll = new RelayCommand(async _ => await SaveAllAsync(), _ => _documents.Any(d => d.IsModified));
        CmdDownload = new RelayCommand(async _ => await DownloadAsync(ActiveDocument!), HasDoc);
        CmdClose = new RelayCommand(async _ => await CloseDocumentAsync(ActiveDocument!), HasDoc);
        CmdCloseAll = new RelayCommand(async _ => await CloseAllAsync(), _ => _documents.Count > 0);
        CmdExit = new RelayCommand(_ => (TopLevel.GetTopLevel(this) as Window)?.Close());
        CmdUndo = new RelayCommand(_ => EditAction(t => t.Undo(), e => e.Undo()));
        CmdRedo = new RelayCommand(_ => EditAction(t => t.Redo(), e => e.Redo()));
        CmdCut = new RelayCommand(_ => EditAction(t => t.Cut(), e => e.Cut()));
        CmdCopy = new RelayCommand(_ => EditAction(t => t.Copy(), e => e.Copy()));
        CmdPaste = new RelayCommand(_ => EditAction(t => t.Paste(), e => e.Paste()));
        CmdSelectAll = new RelayCommand(_ => EditAction(t => t.SelectAll(), e => e.SelectAll()));
        CmdFind = new RelayCommand(_ => ShowFind(false), HasDoc);
        CmdReplace = new RelayCommand(_ => ShowFind(true), HasDoc);
        CmdFindNext = new RelayCommand(_ => FindAgain(false), HasDoc);
        CmdFindPrevious = new RelayCommand(_ => FindAgain(true), HasDoc);
        CmdGoToLine = new RelayCommand(async _ => await GoToLineDialogAsync(), HasDoc);
        CmdComplete = new RelayCommand(_ => ActiveDocument!.ShowCompletion(), HasDoc);
        CmdComment = new RelayCommand(_ => ActiveDocument!.ToggleComment(true), HasDoc);
        CmdUncomment = new RelayCommand(_ => ActiveDocument!.ToggleComment(false), HasDoc);
        CmdToggleComment = new RelayCommand(_ => ActiveDocument!.ToggleComment(), HasDoc);
        CmdReindent = new RelayCommand(_ => ReindentSelection(), HasDoc);
        CmdFoldAll = new RelayCommand(_ => SetAllFolded(true), HasDoc);
        CmdUnfoldAll = new RelayCommand(_ => SetAllFolded(false), HasDoc);
        CmdShowPanel = new RelayCommand(p => ShowBottomPanel(p as string ?? "output"));
        CmdZoomIn = new RelayCommand(_ => Zoom(+1));
        CmdZoomOut = new RelayCommand(_ => Zoom(-1));
        CmdZoomReset = new RelayCommand(_ => Zoom(0));
        CmdBuild = new RelayCommand(async _ => await BuildAsync(), NotBusy);
        CmdGenerateExe = new RelayCommand(async _ => await GenerateExeAsync(null), p => NotBusy(p) && App.Platform.SupportsExecutables);
        CmdDebug = new RelayCommand(async _ => await DebugOrContinueAsync(), _ => (_state == IdeState.Idle && ActiveDocument != null) || _state == IdeState.Paused);
        CmdRun = new RelayCommand(async _ => await RunAsync(false), NotBusy);
        CmdStop = new RelayCommand(_ => StopExecution(), _ => _state is IdeState.Running or IdeState.Debugging or IdeState.Paused or IdeState.External);
        CmdRestart = new RelayCommand(async _ => await RestartAsync(), _ => _session != null);
        CmdPause = new RelayCommand(_ => PauseExecution(), _ => _state == IdeState.Debugging);
        CmdStepInto = new RelayCommand(async _ => await StepAsync(StepKind.Into), _ => _state == IdeState.Paused || (_state == IdeState.Idle && ActiveDocument != null));
        CmdStepOver = new RelayCommand(async _ => await StepAsync(StepKind.Over), _ => _state == IdeState.Paused || (_state == IdeState.Idle && ActiveDocument != null));
        CmdStepOut = new RelayCommand(async _ => await StepAsync(StepKind.Out), _ => _state == IdeState.Paused);
        CmdToggleBreakpoint = new RelayCommand(_ => ActiveDocument!.ToggleBreakpoint(ActiveDocument.Editor.TextArea.Caret.Line), HasDoc);
        CmdClearBreakpoints = new RelayCommand(_ => ClearAllBreakpoints());
        CmdHelp = new RelayCommand(p => ShowHelp(p as string));
        CmdAbout = new RelayCommand(_ => new Dialogs.AboutWindow().ShowDialog(this));

        InitializeComponent();
        DataContext = this;

        if (LanguageRegistry.TestLanguageEnabled) NewEchoItem.IsVisible = true;
        ApplyPlatformTexts();
        ApplyMenuChecks();
        ApplyPanelLayout();

        OutputBox.Document.UndoStack.SizeLimit = 0;
        OutputBox.Options.EnableHyperlinks = false;
        OutputBox.Options.EnableEmailHyperlinks = false;
        OutputBox.Options.AllowScrollBelowDocument = false;
        ConsoleView.ClearOnRun = _settings.ClearConsoleOnRun;
        ConsoleView.ClearOnRunChanged += v => _settings.ClearConsoleOnRun = v;
        ConsoleView.InputSubmitted += line => _session?.IO.Submit(line);
        ConsoleView.StopRequested += StopExecution;
        ConsoleView.SetFont(CodeFont.Resolve(_settings.FontFamily), _settings.FontSize);
        ExternalConsoleToggle.IsChecked = _settings.ExternalConsole;

        ErrorList.ItemsSource = _visibleErrors;
        ErrorList.AddHandler(KeyDownEvent, OnErrorKeyDown, RoutingStrategies.Tunnel);
        StackList.AddHandler(KeyDownEvent, OnStackKeyDown, RoutingStrategies.Tunnel);
        UpdateErrorCounts();

        Loaded += OnLoaded;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnPreviewDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnPreviewDrop, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnPreviewMouseWheel, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);

        _backgroundCheckTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1200) };
        _backgroundCheckTimer.Tick += async (_, _) => { _backgroundCheckTimer.Stop(); await BackgroundCheckAsync(); };

        UpdateWelcome();
        UpdateStatusBar();
    }

    void OnLoaded(object? sender, EventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        // Shortcuts of the window (desktop) or of the page (browser)
        RegisterKeyBindings(TopLevel.GetTopLevel(this)?.KeyBindings ?? KeyBindings);

        // Explorer folder
        var root = _settings.ExplorerRoot;
        if (root == null || !System.IO.Directory.Exists(root)) root = PathLocator.SamplesDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        SetExplorerRoot(root, remember: false);

        if (!App.Options.Automated)
        {
            // Restore the previous session
            foreach (var f in _settings.OpenFiles.ToList())
                if (File.Exists(f)) OpenFile(f, activate: false);
            var active = _documents.FirstOrDefault(d => PathLocator.SamePath(d.FilePath, _settings.ActiveFile));
            if (active != null) Activate(active);
            else if (_documents.Count > 0) Activate(_documents[^1]);
        }
        foreach (var f in App.Options.Files)
        {
            if (System.IO.Directory.Exists(f)) SetExplorerRoot(Path.GetFullPath(f));
            else if (File.Exists(f)) OpenFile(Path.GetFullPath(f));
        }

        AppendOutput("AlgolStudio — " + DateTime.Now.ToString("dddd, MMMM d, yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        var runner = App.Platform.FindRunner();
        if (App.Platform.SupportsExecutables)
            AppendOutput(runner != null ? $"Standalone executable launcher: {runner}" : "Warning: " + RunnerLocator.NotFoundMessage);
        AppendOutput("Languages: " + string.Join(", ", LanguageRegistry.Languages.Select(l => $"{l.DisplayName} ({string.Join(", ", l.FileExtensions)})")));

        if (App.AutomatedScenario != null) _ = App.AutomatedScenario(this);
    }

    /// <summary>Platform-dependent labels (external console, executables) and command shortcuts (Cmd on macOS).</summary>
    void ApplyPlatformTexts()
    {
        var p = App.Platform;
        bool windowsExe = p.ExecutableExtension == ".exe";
        string exeLabel = windowsExe ? ".exe" : "executable";
        GenerateExeItem.Header = $"_Generate standalone {exeLabel}…";
        GenerateExeText.Text = windowsExe ? ".exe" : "Exe";
        ToolTip.SetTip(GenerateExeButton, $"Generate standalone {exeLabel} (Ctrl+Shift+E)");
        DebugInfoItem.Header = windowsExe ? "_Debug information in .exe files" : "_Debug information in executables";
        ExternalConsoleItem.Header = "Run in e_xternal " + p.ExternalConsoleName;
        ToolTip.SetTip(ExternalConsoleToggle, "Run in an external " + p.ExternalConsoleName);
        ExternalConsoleItem.IsVisible = ExternalConsoleToggle.IsVisible = p.SupportsExternalConsole;
        GenerateExeButton.IsVisible = p.SupportsExecutables;
        GenerateExeItem.IsVisible = DebugInfoItem.IsVisible = p.SupportsExecutables;
        ExplorerRevealItem.Header = p.RevealInFileManagerText;
        if (p.IsVirtualFileSystem)
        {
            // Browser: the files live in a virtual file system (no folder dialog, no file manager, no exit);
            // Open uploads files from the computer and "Download" saves a copy on the computer.
            OpenFolderItem.IsVisible = OpenFolderButton.IsVisible = OpenFolderLink.IsVisible = false;
            ExplorerRevealItem.IsVisible = false;
            ExitItem.IsVisible = ExitSeparator.IsVisible = false;
            DownloadItem.IsVisible = true;
        }
        if (Cmd != KeyModifiers.Control)
        {
            ReplaceControlGestures(MainMenu.Items);
            WelcomeShortcuts.Text = WelcomeShortcuts.Text?.Replace("Ctrl+F5", "Cmd+F5").Replace("Ctrl+F", "Cmd+F");
        }
    }

    /// <summary>macOS: the menu shows Cmd instead of Ctrl (completion keeps Ctrl+Space).</summary>
    static void ReplaceControlGestures(ItemCollection items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            if (item.InputGesture is { } g && g.KeyModifiers.HasFlag(KeyModifiers.Control) && g.Key != Key.Space)
                item.InputGesture = new KeyGesture(g.Key, (g.KeyModifiers & ~KeyModifiers.Control) | Cmd);
            ReplaceControlGestures(item.Items);
        }
    }

    // ------------------------------------------------------------------ documents / tabs

    public EditorDocument? ActiveDocument => (DocTabs.SelectedItem as TabItem)?.Tag as EditorDocument;
    public IReadOnlyList<EditorDocument> Documents => _documents;
    public IdeSettings Settings => _settings;
    public IdeState State => _state;

    EditorDocument AddDocument(EditorDocument doc, bool activate = true)
    {
        doc.ApplySettings(_settings);
        _documents.Add(doc);

        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(EditorDocument.Title)) { Source = doc });
        var badge = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            FontSize = 10,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8,
        };
        badge.Bind(TextBlock.TextProperty, new Binding(nameof(EditorDocument.Language)) { Source = doc, Converter = LanguageBadgeConverter.Instance });
        var closeIcon = new IconPath
        {
            Data = App.Resource<Geometry>("Icon.Close"),
            Width = 16, Height = 16,
            Stretch = Stretch.None,
            RenderTransform = new ScaleTransform(0.65, 0.65),
        };
        closeIcon.Bind(Shape.FillProperty, new Binding(nameof(Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(TabItem) },
        });
        var close = new Button
        {
            Theme = (ControlTheme)this.FindResource("ToolButton")!,
            Padding = new Thickness(2),
            Margin = new Thickness(6, 0, 0, 0),
            Content = closeIcon,
        };
        ToolTip.SetTip(close, "Close (Ctrl+W)");
        close.Click += (_, _) => _ = CloseDocumentAsync(doc);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Children = { badge, title, close } };
        var tab = new TabItem
        {
            Header = header,
            Content = doc.Editor,
            Tag = doc,
            Theme = (ControlTheme)this.FindResource("DocTabItem")!,
        };
        tab.Bind(ToolTip.TipProperty, new Binding(nameof(EditorDocument.CompilePath)) { Source = doc });
        tab.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Middle) { _ = CloseDocumentAsync(doc); e.Handled = true; } };
        tab.ContextMenu = BuildTabContextMenu(doc);
        doc.Tab = tab;

        doc.CaretMoved += d => { if (d == ActiveDocument) UpdateStatusBar(); };
        doc.ModelUpdated += d => { if (d == ActiveDocument) UpdateOutline(); };
        doc.BreakpointsChanged += OnDocumentBreakpointsChanged;
        doc.TextChangedSinceAnalysis += d => ScheduleBackgroundCheck();
        doc.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(EditorDocument.IsModified)) RelayCommand.InvalidateAll(); };
        doc.Editor.TextArea.PropertyChanged += (_, e) => { if (e.Property == TextArea.OverstrikeModeProperty) UpdateStatusBar(); };
        doc.Editor.TextArea.TextView.PointerPressed += (_, e) => { if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) ClearCallerHighlight(); };

        DocTabs.Items.Add(tab);
        if (activate) Activate(doc);
        UpdateWelcome();
        RelayCommand.InvalidateAll();
        return doc;
    }

    ContextMenu BuildTabContextMenu(EditorDocument doc)
    {
        var cm = new ContextMenu();
        void Add(string header, Action action, Func<bool>? enabled = null)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            if (enabled != null) cm.Opening += (_, _) => mi.IsEnabled = enabled();
            cm.Items.Add(mi);
        }
        Add("Close", () => _ = CloseDocumentAsync(doc));
        Add("Close other tabs", async () => { foreach (var d in _documents.Where(x => x != doc).ToList()) if (!await CloseDocumentAsync(d)) break; });
        cm.Items.Add(new Separator());
        Add("Copy full path", () => _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(doc.CompilePath), () => doc.FilePath != null);
        Add("Open containing folder", () => RevealInExplorer(doc.FilePath!), () => doc.FilePath != null && !App.Platform.IsVirtualFileSystem);
        return cm;
    }

    /// <summary>Brings the main window to the front (desktop; nothing to do in the browser).</summary>
    public void Activate()
    {
        if (TopLevel.GetTopLevel(this) is Window w)
        {
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
        }
    }

    public void Activate(EditorDocument doc)
    {
        if (doc.Tab != null) DocTabs.SelectedItem = doc.Tab;
        Dispatcher.UIThread.Post(() => doc.Editor.TextArea.Focus(), DispatcherPriority.Input);
    }

    void OnDocTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source != DocTabs) return;
        (DocTabs.SelectedItem as TabItem)?.BringIntoView();
        UpdateOutline();
        UpdateStatusBar();
        UpdateTitle();
        RelayCommand.InvalidateAll();
    }

    void UpdateTitle()
    {
        var doc = ActiveDocument;
        Title = doc == null ? "AlgolStudio" : $"{doc.DisplayName} — AlgolStudio";
    }

    void UpdateWelcome()
    {
        bool empty = _documents.Count == 0;
        WelcomePanel.IsVisible = empty;
        DocTabs.IsVisible = !empty;
        if (empty)
        {
            var recent = _settings.RecentFiles.Where(File.Exists).Take(8).ToList();
            WelcomeRecent.ItemsSource = recent;
            WelcomeNoRecent.IsVisible = recent.Count == 0;
            // Welcome page: main examples only (the Examples menu lists them all)
            var samples = ListSamples().Where(f => !IsWelcomeExcluded(f)).ToList();
            WelcomeSamples.ItemsSource = samples;
            WelcomeNoSamples.IsVisible = samples.Count == 0;
            UpdateOutline();
        }
        UpdateTitle();
    }

    void OnWelcomeRecentClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string path) OpenFile(path);
    }

    // ------------------------------------------------------------------ status bar

    void UpdateStatusBar()
    {
        var doc = ActiveDocument;
        if (doc == null)
        {
            StatusPosition.Text = StatusInsert.Text = StatusEncoding.Text = "";
            StatusLanguageText.Text = "";
            StatusLanguage.IsVisible = false;
        }
        else
        {
            var caret = doc.Editor.TextArea.Caret;
            StatusPosition.Text = $"Ln {caret.Line}, Col {caret.Column}";
            StatusInsert.Text = doc.Editor.TextArea.OverstrikeMode ? "OVR" : "INS";
            StatusEncoding.Text = doc.Encoding.WebName switch
            {
                "utf-8" => doc.Encoding.GetPreamble().Length > 0 ? "UTF-8 (BOM)" : "UTF-8",
                "iso-8859-1" => "Latin-1",
                "utf-16" => "UTF-16",
                var n => n.ToUpperInvariant(),
            };
            StatusLanguageText.Text = doc.Language.DisplayName;
            StatusLanguage.IsVisible = true;
        }
        StatusZoom.Text = $"{Math.Round(_settings.FontSize / 14.0 * 100)} %";
    }

    public void SetState(IdeState state, string? message = null)
    {
        _state = state;
        StatusState.Text = state switch
        {
            IdeState.Idle => "Ready",
            IdeState.Building => "Building…",
            IdeState.Running => "Running…",
            IdeState.Debugging => "Debugging…",
            IdeState.Paused => "Paused",
            IdeState.External => "Running (external console)…",
            _ => "",
        };
        if (message != null) StatusMessage.Text = message;
        StatusBarBorder.Bind(Border.BackgroundProperty, StatusBarBorder.GetResourceObservable(state switch
        {
            IdeState.Running or IdeState.Debugging or IdeState.External => "Brush.StatusBarRunning",
            IdeState.Paused => "Brush.StatusBarPaused",
            _ => "Brush.StatusBar",
        }));
        DebugButtonText.Text = state == IdeState.Paused ? "Continue" : "Debug";
        DebugMenuItem.Header = state == IdeState.Paused ? "_Continue" : "_Start debugging";
        RelayCommand.InvalidateAll();
    }

    public void SetStatusMessage(string message) => StatusMessage.Text = message;

    /// <summary>Dark theme selected in the settings (legacy values are normalized by IdeSettings.Load).</summary>
    bool IsDarkSetting => _settings.Theme == "dark";

    void OnStatusLanguageClick(object? sender, RoutedEventArgs e)
    {
        var cm = new ContextMenu { Placement = PlacementMode.Top };
        FillLanguageItems(cm.Items);
        cm.Open(StatusLanguage);
    }

    void OnLanguageMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (e.Source != LanguageMenu) return;
        LanguageMenu.Items.Clear();
        FillLanguageItems(LanguageMenu.Items);
    }

    void FillLanguageItems(ItemCollection items)
    {
        var doc = ActiveDocument;
        foreach (var lang in LanguageDefinitions.All)
        {
            if (lang.Id == "echo" && !LanguageRegistry.TestLanguageEnabled) continue;
            var mi = new MenuItem { Header = lang.DisplayName, ToggleType = MenuItemToggleType.CheckBox, IsChecked = doc?.Language == lang, IsEnabled = doc != null };
            mi.Click += (_, _) => { if (ActiveDocument != null) { ActiveDocument.Language = lang; UpdateStatusBar(); UpdateOutline(); ScheduleBackgroundCheck(); } };
            items.Add(mi);
        }
    }

    // ------------------------------------------------------------------ panels

    ColumnDefinition LeftColumn => MainGrid.ColumnDefinitions[0];
    ColumnDefinition LeftSplitterColumn => MainGrid.ColumnDefinitions[1];
    ColumnDefinition RightSplitterColumn => MainGrid.ColumnDefinitions[3];
    ColumnDefinition RightColumn => MainGrid.ColumnDefinitions[4];
    RowDefinition BottomSplitterRow => MainGrid.RowDefinitions[1];
    RowDefinition BottomRow => MainGrid.RowDefinitions[2];

    void ApplyPanelLayout()
    {
        LeftColumn.Width = _settings.ShowExplorer ? new GridLength(Math.Max(120, _settings.LeftPanelWidth)) : new GridLength(0);
        LeftSplitterColumn.Width = _settings.ShowExplorer ? new GridLength(5) : new GridLength(0);
        ExplorerPanel.IsVisible = LeftSplitter.IsVisible = _settings.ShowExplorer;
        RightColumn.Width = _settings.ShowOutline ? new GridLength(Math.Max(120, _settings.RightPanelWidth)) : new GridLength(0);
        RightSplitterColumn.Width = _settings.ShowOutline ? new GridLength(5) : new GridLength(0);
        OutlinePanel.IsVisible = RightSplitter.IsVisible = _settings.ShowOutline;
        BottomRow.Height = _settings.ShowBottomPanel ? new GridLength(Math.Max(80, _settings.BottomPanelHeight)) : new GridLength(0);
        BottomSplitterRow.Height = _settings.ShowBottomPanel ? new GridLength(5) : new GridLength(0);
        BottomTabs.IsVisible = BottomSplitter.IsVisible = _settings.ShowBottomPanel;
        ShowExplorerItem.IsChecked = _settings.ShowExplorer;
        ShowOutlineItem.IsChecked = _settings.ShowOutline;
        ShowBottomItem.IsChecked = _settings.ShowBottomPanel;
    }

    void CapturePanelSizes()
    {
        if (_settings.ShowExplorer && LeftColumn.ActualWidth > 50) _settings.LeftPanelWidth = LeftColumn.ActualWidth;
        if (_settings.ShowOutline && RightColumn.ActualWidth > 50) _settings.RightPanelWidth = RightColumn.ActualWidth;
        if (_settings.ShowBottomPanel && BottomRow.ActualHeight > 50) _settings.BottomPanelHeight = BottomRow.ActualHeight;
    }

    void OnTogglePanels(object? sender, RoutedEventArgs e)
    {
        CapturePanelSizes();
        _settings.ShowExplorer = ShowExplorerItem.IsChecked;
        _settings.ShowOutline = ShowOutlineItem.IsChecked;
        _settings.ShowBottomPanel = ShowBottomItem.IsChecked;
        ApplyPanelLayout();
    }

    public void ShowBottomPanel(string which)
    {
        if (!_settings.ShowBottomPanel)
        {
            _settings.ShowBottomPanel = true;
            ApplyPanelLayout();
        }
        BottomTabs.SelectedItem = which switch
        {
            "errors" => ErrorsTab,
            "console" => ConsoleTab,
            "stack" => StackTab,
            "variables" => VariablesTab,
            _ => OutputTab,
        };
        if (which == "console") ConsoleView.FocusInput();
    }

    // ------------------------------------------------------------------ options

    void ApplyMenuChecks()
    {
        WordWrapItem.IsChecked = _settings.WordWrap;
        LineNumbersItem.IsChecked = _settings.ShowLineNumbers;
        WhitespaceItem.IsChecked = _settings.ShowWhitespace;
        ThemeLightItem.IsChecked = !IsDarkSetting;
        ThemeDarkItem.IsChecked = IsDarkSetting;
        BackgroundCheckItem.IsChecked = _settings.BackgroundCheck;
        DebugInfoItem.IsChecked = _settings.GenerateDebugInfo;
        StopOnEntryItem.IsChecked = _settings.StopOnEntry;
        BreakOnErrorItem.IsChecked = _settings.BreakOnError;
        ExternalConsoleItem.IsChecked = _settings.ExternalConsole;
    }

    void OnEditorOptionChanged(object? sender, RoutedEventArgs e)
    {
        _settings.WordWrap = WordWrapItem.IsChecked;
        _settings.ShowLineNumbers = LineNumbersItem.IsChecked;
        _settings.ShowWhitespace = WhitespaceItem.IsChecked;
        foreach (var d in _documents) d.ApplySettings(_settings);
    }

    void OnBuildOptionChanged(object? sender, RoutedEventArgs e)
    {
        _settings.BackgroundCheck = BackgroundCheckItem.IsChecked;
        _settings.GenerateDebugInfo = DebugInfoItem.IsChecked;
        if (_settings.BackgroundCheck) ScheduleBackgroundCheck();
    }

    void OnDebugOptionChanged(object? sender, RoutedEventArgs e)
    {
        _settings.StopOnEntry = StopOnEntryItem.IsChecked;
        _settings.BreakOnError = BreakOnErrorItem.IsChecked;
        _settings.ExternalConsole = ExternalConsoleItem.IsChecked;
        ExternalConsoleToggle.IsChecked = _settings.ExternalConsole;
    }

    void OnExternalConsoleToggle(object? sender, RoutedEventArgs e)
    {
        _settings.ExternalConsole = ExternalConsoleToggle.IsChecked == true;
        ExternalConsoleItem.IsChecked = _settings.ExternalConsole;
        SetStatusMessage(_settings.ExternalConsole
            ? $"Programs will run in an external {App.Platform.ExternalConsoleName} (Ctrl+F5)."
            : "Programs will run in the integrated console.");
    }

    void OnThemeLight(object? sender, RoutedEventArgs e) => SetTheme(false);
    void OnThemeDark(object? sender, RoutedEventArgs e) => SetTheme(true);

    public void SetTheme(bool dark)
    {
        _settings.Theme = dark ? "dark" : "light";
        // The window title bar follows the theme variant (Avalonia sets the Windows dark title bar).
        App.ApplyTheme(dark);
        ThemeLightItem.IsChecked = !dark;
        ThemeDarkItem.IsChecked = dark;
        foreach (var d in _documents) d.ApplyTheme();
        ConsoleView.ApplyTheme();
        SetState(_state);
        // Refresh resource-bound icons (error list, outline)
        ErrorList.ItemsSource = null;
        ErrorList.ItemsSource = _visibleErrors;
        UpdateOutline();
    }

    void Zoom(int direction)
    {
        double size = _settings.FontSize;
        size = direction switch { > 0 => Math.Min(48, size + 1), < 0 => Math.Max(7, size - 1), _ => 14 };
        _settings.FontSize = size;
        foreach (var d in _documents) d.Editor.FontSize = size;
        ConsoleView.SetFont(CodeFont.Resolve(_settings.FontFamily), size);
        UpdateStatusBar();
    }

    void OnPreviewMouseWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers != Cmd) return;
        if (e.Source is Visual v && IsInsideEditor(v))
        {
            Zoom(e.Delta.Y > 0 ? 1 : -1);
            e.Handled = true;
        }
    }

    static bool IsInsideEditor(Visual? v)
    {
        for (var cur = v; cur != null; cur = cur.GetVisualParent())
            if (cur is TextEditor) return true;
        return false;
    }

    // ------------------------------------------------------------------ editing commands (Edit menu, toolbar)

    void OnAnyGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (e.Source is TextBox tb) _lastTextBox = tb;
        else if (e.Source is Visual v && IsInsideEditor(v)) _lastTextBox = null;
    }

    /// <summary>Undo, cut, copy… apply to the text box that has (or last had) the focus, otherwise to the editor.</summary>
    void EditAction(Action<TextBox> onTextBox, Action<TextEditor> onEditor)
    {
        if (_lastTextBox is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } tb && TopLevel.GetTopLevel(tb) == TopLevel.GetTopLevel(this))
            onTextBox(tb);
        else if (ActiveDocument is { } doc)
        {
            onEditor(doc.Editor);
            doc.Editor.TextArea.Focus();
        }
    }

    // ------------------------------------------------------------------ keyboard

    void RegisterKeyBindings(IList<KeyBinding> keyBindings)
    {
        var cmd = Cmd;
        void K(ICommand command, Key key, KeyModifiers mods = KeyModifiers.None, object? param = null) =>
            keyBindings.Add(new KeyBinding { Command = command, Gesture = new KeyGesture(key, mods), CommandParameter = param! });

        K(CmdNewSimula, Key.N, cmd);
        K(CmdNewAlgol, Key.N, cmd | KeyModifiers.Shift);
        K(CmdOpen, Key.O, cmd);
        K(CmdSave, Key.S, cmd);
        K(CmdSaveAll, Key.S, cmd | KeyModifiers.Shift);
        K(CmdClose, Key.W, cmd);
        K(CmdClose, Key.F4, cmd);
        K(CmdFind, Key.F, cmd);
        K(CmdReplace, Key.H, cmd);
        K(CmdFindNext, Key.F3);
        K(CmdFindPrevious, Key.F3, KeyModifiers.Shift);
        K(CmdGoToLine, Key.G, cmd);
        K(CmdComment, Key.K, cmd);
        K(CmdUncomment, Key.K, cmd | KeyModifiers.Shift);
        K(CmdReindent, Key.I, cmd);
        K(CmdZoomIn, Key.OemPlus, cmd);
        K(CmdZoomIn, Key.Add, cmd);
        K(CmdZoomOut, Key.OemMinus, cmd);
        K(CmdZoomOut, Key.Subtract, cmd);
        K(CmdZoomReset, Key.D0, cmd);
        K(CmdZoomReset, Key.NumPad0, cmd);
        K(CmdBuild, Key.F7);
        K(CmdBuild, Key.B, cmd | KeyModifiers.Shift);
        K(CmdGenerateExe, Key.E, cmd | KeyModifiers.Shift);
        K(CmdDebug, Key.F5);
        K(CmdRun, Key.F5, cmd);
        K(CmdStop, Key.F5, KeyModifiers.Shift);
        K(CmdRestart, Key.F5, cmd | KeyModifiers.Shift);
        K(CmdPause, Key.Pause, KeyModifiers.Control | KeyModifiers.Alt);
        K(CmdPause, Key.Cancel, KeyModifiers.Control | KeyModifiers.Alt);
        K(CmdStepInto, Key.F11);
        K(CmdStepOver, Key.F10);
        K(CmdStepOut, Key.F11, KeyModifiers.Shift);
        K(CmdToggleBreakpoint, Key.F9);
        K(CmdClearBreakpoints, Key.F9, cmd | KeyModifiers.Shift);
        K(CmdHelp, Key.F1);
        K(CmdShowPanel, Key.O, cmd | KeyModifiers.Alt, "output");
        K(CmdShowPanel, Key.E, cmd | KeyModifiers.Alt, "errors");
        K(CmdShowPanel, Key.C, cmd | KeyModifiers.Alt, "console");
        K(CmdShowPanel, Key.P, cmd | KeyModifiers.Alt, "stack");
        K(CmdShowPanel, Key.V, cmd | KeyModifiers.Alt, "variables");
    }

    void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Chord Ctrl+M, Ctrl+O / Ctrl+L / Ctrl+M (folding)
        if (e.KeyModifiers == Cmd && ActiveDocument != null && IsInsideEditor(e.Source as Visual))
        {
            if (_pendingChord == Key.M)
            {
                _pendingChord = Key.None;
                if (e.Key == Key.O) { SetAllFolded(true); e.Handled = true; return; }
                if (e.Key == Key.L) { SetAllFolded(false); e.Handled = true; return; }
                if (e.Key == Key.M) { ToggleFoldAtCaret(); e.Handled = true; return; }
            }
            else if (e.Key == Key.M)
            {
                _pendingChord = Key.M;
                SetStatusMessage("Ctrl+M was pressed. Waiting for the second key (O, L or M)…");
                e.Handled = true;
                return;
            }
        }
        if (e.Key is not (Key.LeftCtrl or Key.RightCtrl or Key.LWin or Key.RWin)) _pendingChord = Key.None;
    }

    // ------------------------------------------------------------------ drag and drop

    void OnPreviewDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    void OnPreviewDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;
        e.Handled = true;
        foreach (var item in files)
        {
            var f = item.TryGetLocalPath();
            if (f == null)
            {
                // Browser: the dropped file is copied into the virtual file system.
                if (App.Platform.IsVirtualFileSystem) _ = ImportAndOpenAsync(item);
                continue;
            }
            if (System.IO.Directory.Exists(f)) SetExplorerRoot(f);
            else if (File.Exists(f)) OpenFile(f);
        }
    }

    async Task ImportAndOpenAsync(IStorageItem item)
    {
        if (await ImportAsync(item) is { } path) OpenFile(path);
    }

    // ------------------------------------------------------------------ explorer

    public void SetExplorerRoot(string path, bool remember = true)
    {
        if (!System.IO.Directory.Exists(path)) return;
        var root = new FileNode(path, true);
        root.IsExpanded = true;
        ExplorerTree.ItemsSource = new[] { root };
        ExplorerTitle.Text = "EXPLORER — " + Path.GetFileName(path.TrimEnd('\\', '/'));
        ToolTip.SetTip(ExplorerTitle, path);
        _settings.ExplorerRoot = path;
        if (remember) _settings.AddRecentFolder(path);
    }

    FileNode? SelectedFileNode => ExplorerTree.SelectedItem as FileNode;

    void OnExplorerDoubleClick(object? sender, TappedEventArgs e)
    {
        if (SelectedFileNode is { IsDirectory: false } n && File.Exists(n.FullPath))
        {
            OpenFile(n.FullPath);
            e.Handled = true;
        }
    }

    void OnExplorerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SelectedFileNode is { IsDirectory: false } n && File.Exists(n.FullPath)) OpenFile(n.FullPath);
        else if (e.Key == Key.F5) OnExplorerRefresh(sender, e);
    }

    void OnExplorerOpen(object? sender, RoutedEventArgs e)
    {
        if (SelectedFileNode is { } n)
        {
            if (n.IsDirectory) SetExplorerRoot(n.FullPath);
            else if (File.Exists(n.FullPath)) OpenFile(n.FullPath);
        }
    }

    void OnExplorerReveal(object? sender, RoutedEventArgs e)
    {
        var p = SelectedFileNode?.FullPath ?? _settings.ExplorerRoot;
        if (p != null) RevealInExplorer(p);
    }

    void OnExplorerRefresh(object? sender, RoutedEventArgs e)
    {
        if (_settings.ExplorerRoot != null) SetExplorerRoot(_settings.ExplorerRoot, remember: false);
    }

    void OnExplorerCollapse(object? sender, RoutedEventArgs e)
    {
        if (ExplorerTree.ItemsSource is FileNode[] { Length: 1 } roots)
            foreach (var c in roots[0].Children) Collapse(c);
        static void Collapse(FileNode n) { foreach (var c in n.Children) Collapse(c); n.IsExpanded = false; }
    }

    /// <summary>Shows a file or a folder in the file manager of the platform.</summary>
    public static void RevealInExplorer(string path)
    {
        try { App.Platform.RevealInFileManager(path); }
        catch (Exception ex) { Log.Write("reveal: " + ex.Message); }
    }

    // ------------------------------------------------------------------ outline

    void UpdateOutline()
    {
        var doc = ActiveDocument;
        _outlineAll.Clear();
        if (doc != null)
        {
            var d = doc.Editor.Document;
            foreach (var item in doc.Model.Outline)
                if (item.Offset <= d.TextLength)
                    _outlineAll.Add(new OutlineEntry(item, d.GetLineByOffset(item.Offset).LineNumber));
            OutlineTitle.Text = "OUTLINE — " + doc.DisplayName;
        }
        else OutlineTitle.Text = "OUTLINE";
        ApplyOutlineFilter();
    }

    void ApplyOutlineFilter()
    {
        var f = (OutlineFilter.Text ?? "").Trim();
        var list = f.Length == 0 ? _outlineAll : _outlineAll.Where(o => o.Name.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        OutlineList.ItemsSource = list.ToList();
        OutlineEmpty.IsVisible = list.Count == 0;
        OutlineEmpty.Text = ActiveDocument == null ? "No open file." : _outlineAll.Count == 0
            ? "No procedure, class, operator or mode." : "No item matches the filter.";
    }

    void OnOutlineFilterChanged(object? sender, TextChangedEventArgs e) => ApplyOutlineFilter();

    void OnOutlineClick(object? sender, TappedEventArgs e) => NavigateOutline();

    void OnOutlineKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { NavigateOutline(); e.Handled = true; }
    }

    void NavigateOutline()
    {
        if (OutlineList.SelectedItem is OutlineEntry entry && ActiveDocument is { } doc)
        {
            doc.GoTo(entry.Line, 1);
            int off = Math.Min(entry.Item.Offset, doc.Editor.Document.TextLength);
            doc.Editor.Select(off, 0);
            doc.Editor.TextArea.Caret.BringCaretToView();
            doc.Editor.TextArea.Focus();
        }
    }

    // ------------------------------------------------------------------ editing

    void ReindentSelection()
    {
        var doc = ActiveDocument!;
        var ed = doc.Editor;
        int first, last;
        if (ed.SelectionLength == 0) first = last = ed.TextArea.Caret.Line;
        else
        {
            first = ed.Document.GetLineByOffset(ed.SelectionStart).LineNumber;
            last = ed.Document.GetLineByOffset(ed.SelectionStart + ed.SelectionLength).LineNumber;
        }
        ed.TextArea.IndentationStrategy?.IndentLines(ed.Document, first, last);
    }

    void SetAllFolded(bool folded)
    {
        var doc = ActiveDocument;
        if (doc == null) return;
        foreach (var f in doc.Folding.AllFoldings) f.IsFolded = folded;
        SetStatusMessage(folded ? "All blocks folded." : "All blocks unfolded.");
    }

    void ToggleFoldAtCaret()
    {
        var doc = ActiveDocument;
        if (doc == null) return;
        var f = doc.Folding.GetFoldingsContaining(doc.Editor.CaretOffset).LastOrDefault()
                ?? doc.Folding.GetNextFolding(doc.Editor.Document.GetLineByOffset(doc.Editor.CaretOffset).Offset);
        if (f != null) f.IsFolded = !f.IsFolded;
    }

    async Task GoToLineDialogAsync()
    {
        var doc = ActiveDocument!;
        var dlg = new Dialogs.GoToLineWindow(doc.Editor.TextArea.Caret.Line, doc.Editor.Document.LineCount);
        if (await dlg.ShowDialog<bool>(this)) doc.GoTo(dlg.Line, 1);
        doc.Editor.TextArea.Focus();
    }

    Dialogs.FindReplaceWindow? _findWindow;
    readonly SearchOptions _search = new();
    public SearchOptions Search => _search;

    void ShowFind(bool replace)
    {
        var doc = ActiveDocument!;
        var sel = doc.Editor.SelectedText;
        if (!string.IsNullOrEmpty(sel) && !sel.Contains('\n')) _search.Text = sel;
        if (_findWindow == null)
        {
            _findWindow = new Dialogs.FindReplaceWindow(this, _search);
            _findWindow.Closed += (_, _) => _findWindow = null;
            _findWindow.Show(this);
        }
        _findWindow.ShowMode(replace);
        _findWindow.Activate();
    }

    void FindAgain(bool backwards)
    {
        if (string.IsNullOrEmpty(_search.Text)) { ShowFind(false); return; }
        FindNext(backwards);
    }

    /// <summary>Finds the next match in the active editor; returns a status-bar message.</summary>
    public string FindNext(bool backwards)
    {
        var doc = ActiveDocument;
        if (doc == null) return "No open file.";
        var rx = SearchEngine.Build(_search, out var error);
        if (rx == null) return SetSearchStatus(error!);
        bool found = SearchEngine.FindNext(doc, rx, backwards, out bool wrapped);
        return SetSearchStatus(found ? (wrapped ? "Search wrapped to the start of the file." : "") : $"'{_search.Text}' not found.");
    }

    public string ReplaceCurrent()
    {
        var doc = ActiveDocument;
        if (doc == null) return "";
        var rx = SearchEngine.Build(_search, out var error);
        if (rx == null) return SetSearchStatus(error!);
        SearchEngine.ReplaceCurrent(doc, rx, _search);
        return FindNext(false);
    }

    public string ReplaceAll()
    {
        var doc = ActiveDocument;
        if (doc == null) return "";
        var rx = SearchEngine.Build(_search, out var error);
        if (rx == null) return SetSearchStatus(error!);
        int n = SearchEngine.ReplaceAll(doc, rx, _search);
        return SetSearchStatus(n == 0 ? $"'{_search.Text}' not found." : $"{n} occurrence(s) replaced.");
    }

    string SetSearchStatus(string message)
    {
        SetStatusMessage(message);
        return message;
    }

    // ------------------------------------------------------------------ help

    Dialogs.HelpWindow? _helpWindow;

    void ShowHelp(string? topic)
    {
        topic ??= ActiveDocument?.Language.Id switch { "algol68" => "algol68", "algol60" => "algol60", "simula1" => "simula1", "pascal" => "pascal", _ => "simula" };
        if (_helpWindow == null)
        {
            _helpWindow = new Dialogs.HelpWindow();
            _helpWindow.Closed += (_, _) => _helpWindow = null;
            _helpWindow.Show();
        }
        _helpWindow.ShowTopic(topic);
        _helpWindow.Activate();
    }

    // ------------------------------------------------------------------ window

    /// <summary>Desktop: the main window is closing. Returns false when the user cancels.</summary>
    public bool CanClose()
    {
        if (_state is IdeState.Running or IdeState.Debugging or IdeState.Paused or IdeState.External && !App.Options.Automated)
        {
            var r = MessageBox.Show(this, "A program is running. Stop it and exit?", "AlgolStudio",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return false;
        }
        if (_state != IdeState.Idle) StopExecution();
        if (!App.Options.Automated && !ConfirmSaveModified(_documents.ToList())) return false;
        return true;
    }

    /// <summary>Closes the find and help dialogs (the main window is closed).</summary>
    public void CloseDialogs()
    {
        _findWindow?.Close();
        _helpWindow?.Close();
    }

    /// <summary>Saves the session (open files, carets, layout) in the settings; the main window
    /// stores its placement in <see cref="Settings"/> before.</summary>
    public void SaveSession()
    {
        CapturePanelSizes();
        _settings.OpenFiles = _documents.Where(d => d.FilePath != null).Select(d => d.FilePath!).ToList();
        _settings.ActiveFile = ActiveDocument?.FilePath;
        foreach (var d in _documents.Where(d => d.FilePath != null))
            _settings.CaretOffsets[d.FilePath!] = d.Editor.CaretOffset;
        _settings.Save();
    }

    /// <summary>Reloads files changed on disk by another program (desktop: the window is activated).</summary>
    public void CheckExternalChanges()
    {
        if (_checkingExternalChanges) return;
        _checkingExternalChanges = true;
        try
        {
            foreach (var d in _documents.ToList())
            {
                if (d.FilePath == null || !File.Exists(d.FilePath)) continue;
                var t = File.GetLastWriteTimeUtc(d.FilePath);
                if (d.LastWriteUtc == default || t == d.LastWriteUtc) continue;
                if (!d.IsModified)
                {
                    d.Reload();
                    AppendOutput($"File reloaded (changed outside the editor): {d.FilePath}");
                }
                else
                {
                    var r = MessageBox.Show(this, $"The file\n{d.FilePath}\nhas been changed outside the editor.\n\nReload it and lose your changes?",
                        "File changed", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r == MessageBoxResult.Yes) d.Reload();
                    else d.LastWriteUtc = t;
                }
            }
        }
        catch (Exception ex) { Log.Write("file check: " + ex.Message); }
        finally { _checkingExternalChanges = false; }
    }

    // ------------------------------------------------------------------ output

    public void AppendOutput(string line)
    {
        var doc = OutputBox.Document;
        doc.Insert(doc.TextLength, line + "\n");
        if (doc.TextLength > 2_000_000) doc.Remove(0, doc.TextLength - 1_000_000);
        OutputBox.ScrollToEnd();
    }

    void OnClearOutput(object? sender, RoutedEventArgs e) => OutputBox.Document.Text = "";

    /// <summary>Double-click on an output line "file(l,c): …" goes to that position.</summary>
    void OnOutputDoubleClick(object? sender, TappedEventArgs e)
    {
        int lineNumber = OutputBox.TextArea.Caret.Line;
        if (lineNumber < 1 || lineNumber > OutputBox.Document.LineCount) return;
        var line = OutputBox.Document.GetText(OutputBox.Document.GetLineByNumber(lineNumber));
        var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s*(?<f>.+?)\((?<l>\d+),(?<c>\d+)\):");
        if (m.Success)
        {
            NavigateTo(m.Groups["f"].Value, int.Parse(m.Groups["l"].Value), int.Parse(m.Groups["c"].Value));
            e.Handled = true;
        }
    }
}

/// <summary>Language badge shown in the tab (SIM, A68, A60, S1, PAS, ECHO).</summary>
public sealed class LanguageBadgeConverter : IValueConverter
{
    public static readonly LanguageBadgeConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is LanguageDefinition l ? l.Id switch { "simula" => "SIM", "algol68" => "A68", "algol60" => "A60", "simula1" => "S1", "pascal" => "PAS", _ => "ECHO" } : "";
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
