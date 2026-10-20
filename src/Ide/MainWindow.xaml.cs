using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Studio.Ide.Editor;
using Studio.Ide.Language;
using Studio.Ide.Panels;
using Studio.Ide.Services;
using Studio.Tooling;

namespace Studio.Ide;

public enum IdeState { Idle, Building, Running, Debugging, Paused, External }

public partial class MainWindow : Window
{
    readonly IdeSettings _settings;
    readonly ObservableCollection<EditorDocument> _documents = new();
    readonly List<OutlineEntry> _outlineAll = new();
    IdeState _state = IdeState.Idle;
    bool _closingConfirmed;
    bool _checkingExternalChanges;
    Key _pendingChord = Key.None;

    // ------------------------------------------------------------------ commandes

    public ICommand CmdNewSimula { get; }
    public ICommand CmdNewAlgol { get; }
    public ICommand CmdNewEcho { get; }
    public ICommand CmdOpen { get; }
    public ICommand CmdOpenFolder { get; }
    public ICommand CmdSave { get; }
    public ICommand CmdSaveAs { get; }
    public ICommand CmdSaveAll { get; }
    public ICommand CmdClose { get; }
    public ICommand CmdCloseAll { get; }
    public ICommand CmdExit { get; }
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

    public MainWindow()
    {
        _settings = IdeSettings.Load();
        if (App.Options.Theme != null) _settings.Theme = App.Options.Theme.StartsWith("s", StringComparison.OrdinalIgnoreCase) || App.Options.Theme.StartsWith("d", StringComparison.OrdinalIgnoreCase) ? "sombre" : "clair";
        App.ApplyTheme(_settings.Theme == "sombre");

        bool HasDoc(object? _) => ActiveDocument != null;
        bool NotBusy(object? _) => ActiveDocument != null && _state == IdeState.Idle;

        CmdNewSimula = new RelayCommand(_ => NewDocument(LanguageDefinitions.Simula));
        CmdNewAlgol = new RelayCommand(_ => NewDocument(LanguageDefinitions.Algol68));
        CmdNewEcho = new RelayCommand(_ => NewDocument(LanguageDefinitions.Echo));
        CmdOpen = new RelayCommand(_ => OpenWithDialog());
        CmdOpenFolder = new RelayCommand(_ => OpenFolderWithDialog());
        CmdSave = new RelayCommand(_ => SaveDocument(ActiveDocument!), HasDoc);
        CmdSaveAs = new RelayCommand(_ => SaveDocumentAs(ActiveDocument!), HasDoc);
        CmdSaveAll = new RelayCommand(_ => SaveAll(), _ => _documents.Any(d => d.IsModified));
        CmdClose = new RelayCommand(_ => CloseDocument(ActiveDocument!), HasDoc);
        CmdCloseAll = new RelayCommand(_ => CloseAll(), _ => _documents.Count > 0);
        CmdExit = new RelayCommand(_ => Close());
        CmdFind = new RelayCommand(_ => ShowFind(false), HasDoc);
        CmdReplace = new RelayCommand(_ => ShowFind(true), HasDoc);
        CmdFindNext = new RelayCommand(_ => FindAgain(false), HasDoc);
        CmdFindPrevious = new RelayCommand(_ => FindAgain(true), HasDoc);
        CmdGoToLine = new RelayCommand(_ => GoToLineDialog(), HasDoc);
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
        CmdGenerateExe = new RelayCommand(async _ => await GenerateExeAsync(null), NotBusy);
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
        CmdAbout = new RelayCommand(_ => new Dialogs.AboutWindow { Owner = this }.ShowDialog());

        InitializeComponent();
        DataContext = this;

        if (LanguageRegistry.TestLanguageEnabled) NewEchoItem.Visibility = Visibility.Visible;
        RegisterKeyBindings();
        RestoreWindowPlacement();
        ApplyMenuChecks();
        ApplyPanelLayout();

        ConsoleView.ClearOnRun = _settings.ClearConsoleOnRun;
        ConsoleView.ClearOnRunChanged += v => _settings.ClearConsoleOnRun = v;
        ConsoleView.InputSubmitted += line => _session?.IO.Submit(line);
        ConsoleView.StopRequested += StopExecution;
        ConsoleView.SetFont(new FontFamily(_settings.FontFamily), _settings.FontSize);
        ExternalConsoleToggle.IsChecked = _settings.ExternalConsole;

        ErrorList.ItemsSource = _visibleErrors;
        UpdateErrorCounts();

        Loaded += OnLoaded;
        Closing += OnClosing;
        Activated += (_, _) => CheckExternalChanges();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewDragOver += OnPreviewDragOver;
        PreviewDrop += OnPreviewDrop;
        PreviewMouseWheel += OnPreviewMouseWheel;
        SourceInitialized += (_, _) => ApplyTitleBarTheme();

        _backgroundCheckTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1200) };
        _backgroundCheckTimer.Tick += async (_, _) => { _backgroundCheckTimer.Stop(); await BackgroundCheckAsync(); };

        UpdateWelcome();
        UpdateStatusBar();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Dossier de l'explorateur
        var root = _settings.ExplorerRoot;
        if (root == null || !System.IO.Directory.Exists(root)) root = PathLocator.SamplesDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        SetExplorerRoot(root, remember: false);

        if (!App.Options.Automated)
        {
            // Restauration de la session
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

        AppendOutput($"Simula 67 & Algol 68 Studio — {DateTime.Now:dddd d MMMM yyyy HH:mm}");
        var runner = RunnerLocator.Find();
        AppendOutput(runner != null ? $"Lanceur des exécutables autonomes : {runner}" : "Attention : " + RunnerLocator.NotFoundMessage);
        AppendOutput("Langages : " + string.Join(", ", LanguageRegistry.Languages.Select(l => $"{l.DisplayName} ({string.Join(", ", l.FileExtensions)})")));

        if (App.Options.SmokeTest) _ = SelfTest.RunSmokeAsync(this);
        else if (App.Options.SelfTest) _ = SelfTest.RunAsync(this);
    }

    // ------------------------------------------------------------------ documents / onglets

    public EditorDocument? ActiveDocument => (DocTabs.SelectedItem as TabItem)?.Tag as EditorDocument;
    public IReadOnlyList<EditorDocument> Documents => _documents;
    public IdeSettings Settings => _settings;
    public IdeState State => _state;

    EditorDocument AddDocument(EditorDocument doc, bool activate = true)
    {
        doc.ApplySettings(_settings);
        _documents.Add(doc);

        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(EditorDocument.Title)) { Source = doc });
        var badge = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8,
        };
        badge.SetBinding(TextBlock.TextProperty, new Binding(nameof(EditorDocument.Language)) { Source = doc, Converter = LanguageBadgeConverter.Instance });
        var close = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Padding = new Thickness(2),
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Fermer (Ctrl+W)",
            Content = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("Icon.Close"),
                Width = 16, Height = 16,
                RenderTransform = new ScaleTransform(0.65, 0.65, 8, 8),
            },
        };
        ((System.Windows.Shapes.Path)close.Content).SetBinding(System.Windows.Shapes.Shape.FillProperty, new Binding(nameof(Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(TabItem), 1) });
        close.Click += (_, _) => CloseDocument(doc);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Children = { badge, title, close } };
        var tab = new TabItem
        {
            Header = header,
            Content = doc.Editor,
            Tag = doc,
            Style = (Style)FindResource("DocTabItem"),
        };
        tab.SetBinding(ToolTipProperty, new Binding(nameof(EditorDocument.CompilePath)) { Source = doc });
        tab.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { CloseDocument(doc); e.Handled = true; } };
        tab.ContextMenu = BuildTabContextMenu(doc);
        doc.Tab = tab;

        doc.CaretMoved += d => { if (d == ActiveDocument) UpdateStatusBar(); };
        doc.ModelUpdated += d => { if (d == ActiveDocument) UpdateOutline(); };
        doc.BreakpointsChanged += OnDocumentBreakpointsChanged;
        doc.TextChangedSinceAnalysis += d => ScheduleBackgroundCheck();
        DependencyPropertyDescriptor.FromProperty(ICSharpCode.AvalonEdit.Editing.TextArea.OverstrikeModeProperty, typeof(ICSharpCode.AvalonEdit.Editing.TextArea))
            ?.AddValueChanged(doc.Editor.TextArea, (_, _) => UpdateStatusBar());
        doc.Editor.TextArea.TextView.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) ClearCallerHighlight(); };

        DocTabs.Items.Add(tab);
        if (activate) Activate(doc);
        UpdateWelcome();
        return doc;
    }

    ContextMenu BuildTabContextMenu(EditorDocument doc)
    {
        var cm = new ContextMenu();
        void Add(string header, Action action, Func<bool>? enabled = null)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            if (enabled != null) cm.Opened += (_, _) => mi.IsEnabled = enabled();
            cm.Items.Add(mi);
        }
        Add("Fermer", () => CloseDocument(doc));
        Add("Fermer les autres onglets", () => { foreach (var d in _documents.Where(x => x != doc).ToList()) if (!CloseDocument(d)) break; });
        cm.Items.Add(new Separator());
        Add("Copier le chemin complet", () => Clipboard.SetText(doc.CompilePath), () => doc.FilePath != null);
        Add("Ouvrir le dossier contenant", () => RevealInExplorer(doc.FilePath!), () => doc.FilePath != null);
        return cm;
    }

    public void Activate(EditorDocument doc)
    {
        if (doc.Tab != null) DocTabs.SelectedItem = doc.Tab;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => doc.Editor.TextArea.Focus()));
    }

    void OnDocTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != DocTabs) return;
        (DocTabs.SelectedItem as TabItem)?.BringIntoView();
        UpdateOutline();
        UpdateStatusBar();
        UpdateTitle();
    }

    void UpdateTitle()
    {
        var doc = ActiveDocument;
        Title = doc == null ? "Simula 67 & Algol 68 Studio" : $"{doc.DisplayName} — Simula 67 & Algol 68 Studio";
    }

    void UpdateWelcome()
    {
        bool empty = _documents.Count == 0;
        WelcomePanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        DocTabs.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (empty)
        {
            var recent = _settings.RecentFiles.Where(File.Exists).Take(8).ToList();
            WelcomeRecent.ItemsSource = recent;
            WelcomeNoRecent.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var samples = ListSamples();
            WelcomeSamples.ItemsSource = samples;
            WelcomeNoSamples.Visibility = samples.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateOutline();
        }
        UpdateTitle();
    }

    void OnWelcomeRecentClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string path) OpenFile(path);
    }

    // ------------------------------------------------------------------ barre d'etat

    void UpdateStatusBar()
    {
        var doc = ActiveDocument;
        if (doc == null)
        {
            StatusPosition.Text = StatusInsert.Text = StatusEncoding.Text = "";
            StatusLanguageText.Text = "";
            StatusLanguage.Visibility = Visibility.Collapsed;
        }
        else
        {
            var caret = doc.Editor.TextArea.Caret;
            StatusPosition.Text = $"Ln {caret.Line}, Col {caret.Column}";
            StatusInsert.Text = doc.Editor.TextArea.OverstrikeMode ? "RFP" : "INS";
            StatusEncoding.Text = doc.Encoding.WebName switch
            {
                "utf-8" => doc.Encoding.GetPreamble().Length > 0 ? "UTF-8 (BOM)" : "UTF-8",
                "iso-8859-1" => "Latin-1",
                "utf-16" => "UTF-16",
                var n => n.ToUpperInvariant(),
            };
            StatusLanguageText.Text = doc.Language.DisplayName;
            StatusLanguage.Visibility = Visibility.Visible;
        }
        StatusZoom.Text = $"{Math.Round(_settings.FontSize / 14.0 * 100)} %";
    }

    public void SetState(IdeState state, string? message = null)
    {
        _state = state;
        StatusState.Text = state switch
        {
            IdeState.Idle => "Prêt",
            IdeState.Building => "Compilation…",
            IdeState.Running => "Exécution…",
            IdeState.Debugging => "Débogage…",
            IdeState.Paused => "En pause",
            IdeState.External => "Exécution (console externe)…",
            _ => "",
        };
        if (message != null) StatusMessage.Text = message;
        StatusBarBorder.Background = (Brush)FindResource(state switch
        {
            IdeState.Running or IdeState.Debugging or IdeState.External => "Brush.StatusBarRunning",
            IdeState.Paused => "Brush.StatusBarPaused",
            _ => "Brush.StatusBar",
        });
        StatusBarBorder.SetResourceReference(Border.BackgroundProperty, state switch
        {
            IdeState.Running or IdeState.Debugging or IdeState.External => "Brush.StatusBarRunning",
            IdeState.Paused => "Brush.StatusBarPaused",
            _ => "Brush.StatusBar",
        });
        DebugButtonText.Text = state == IdeState.Paused ? "Continuer" : "Déboguer";
        DebugMenuItem.Header = state == IdeState.Paused ? "_Continuer" : "_Démarrer le débogage";
        CommandManager.InvalidateRequerySuggested();
    }

    public void SetStatusMessage(string message) => StatusMessage.Text = message;

    void OnStatusLanguageClick(object sender, RoutedEventArgs e)
    {
        var cm = new ContextMenu { PlacementTarget = StatusLanguage, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        FillLanguageItems(cm.Items);
        cm.IsOpen = true;
    }

    void OnLanguageMenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != LanguageMenu) return;
        LanguageMenu.Items.Clear();
        FillLanguageItems(LanguageMenu.Items);
    }

    void FillLanguageItems(ItemCollection items)
    {
        var doc = ActiveDocument;
        foreach (var lang in LanguageDefinitions.All)
        {
            if (lang.Id == "echo" && !LanguageRegistry.TestLanguageEnabled) continue;
            var mi = new MenuItem { Header = lang.DisplayName, IsCheckable = true, IsChecked = doc?.Language == lang, IsEnabled = doc != null };
            mi.Click += (_, _) => { if (ActiveDocument != null) { ActiveDocument.Language = lang; UpdateStatusBar(); UpdateOutline(); ScheduleBackgroundCheck(); } };
            items.Add(mi);
        }
    }

    // ------------------------------------------------------------------ panneaux

    void ApplyPanelLayout()
    {
        LeftColumn.Width = _settings.ShowExplorer ? new GridLength(Math.Max(120, _settings.LeftPanelWidth)) : new GridLength(0);
        LeftSplitterColumn.Width = _settings.ShowExplorer ? new GridLength(5) : new GridLength(0);
        ExplorerPanel.Visibility = LeftSplitter.Visibility = _settings.ShowExplorer ? Visibility.Visible : Visibility.Collapsed;
        RightColumn.Width = _settings.ShowOutline ? new GridLength(Math.Max(120, _settings.RightPanelWidth)) : new GridLength(0);
        RightSplitterColumn.Width = _settings.ShowOutline ? new GridLength(5) : new GridLength(0);
        OutlinePanel.Visibility = RightSplitter.Visibility = _settings.ShowOutline ? Visibility.Visible : Visibility.Collapsed;
        BottomRow.Height = _settings.ShowBottomPanel ? new GridLength(Math.Max(80, _settings.BottomPanelHeight)) : new GridLength(0);
        BottomSplitterRow.Height = _settings.ShowBottomPanel ? new GridLength(5) : new GridLength(0);
        BottomTabs.Visibility = BottomSplitter.Visibility = _settings.ShowBottomPanel ? Visibility.Visible : Visibility.Collapsed;
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

    void OnTogglePanels(object sender, RoutedEventArgs e)
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
        ThemeLightItem.IsChecked = _settings.Theme != "sombre";
        ThemeDarkItem.IsChecked = _settings.Theme == "sombre";
        BackgroundCheckItem.IsChecked = _settings.BackgroundCheck;
        DebugInfoItem.IsChecked = _settings.GenerateDebugInfo;
        StopOnEntryItem.IsChecked = _settings.StopOnEntry;
        BreakOnErrorItem.IsChecked = _settings.BreakOnError;
        ExternalConsoleItem.IsChecked = _settings.ExternalConsole;
    }

    void OnEditorOptionChanged(object sender, RoutedEventArgs e)
    {
        _settings.WordWrap = WordWrapItem.IsChecked;
        _settings.ShowLineNumbers = LineNumbersItem.IsChecked;
        _settings.ShowWhitespace = WhitespaceItem.IsChecked;
        foreach (var d in _documents) d.ApplySettings(_settings);
    }

    void OnBuildOptionChanged(object sender, RoutedEventArgs e)
    {
        _settings.BackgroundCheck = BackgroundCheckItem.IsChecked;
        _settings.GenerateDebugInfo = DebugInfoItem.IsChecked;
        if (_settings.BackgroundCheck) ScheduleBackgroundCheck();
    }

    void OnDebugOptionChanged(object sender, RoutedEventArgs e)
    {
        _settings.StopOnEntry = StopOnEntryItem.IsChecked;
        _settings.BreakOnError = BreakOnErrorItem.IsChecked;
        _settings.ExternalConsole = ExternalConsoleItem.IsChecked;
        ExternalConsoleToggle.IsChecked = _settings.ExternalConsole;
    }

    void OnExternalConsoleToggle(object sender, RoutedEventArgs e)
    {
        _settings.ExternalConsole = ExternalConsoleToggle.IsChecked == true;
        ExternalConsoleItem.IsChecked = _settings.ExternalConsole;
        SetStatusMessage(_settings.ExternalConsole
            ? "Les programmes seront exécutés dans une console Windows externe (Ctrl+F5)."
            : "Les programmes seront exécutés dans la console intégrée.");
    }

    void OnThemeLight(object sender, RoutedEventArgs e) => SetTheme(false);
    void OnThemeDark(object sender, RoutedEventArgs e) => SetTheme(true);

    public void SetTheme(bool dark)
    {
        _settings.Theme = dark ? "sombre" : "clair";
        App.ApplyTheme(dark);
        ThemeLightItem.IsChecked = !dark;
        ThemeDarkItem.IsChecked = dark;
        foreach (var d in _documents) d.ApplyTheme();
        ConsoleView.ApplyTheme();
        SetState(_state);
        ApplyTitleBarTheme();
        // Rafraichit les icones liees aux ressources (liste d'erreurs, structure)
        ErrorList.Items.Refresh();
        UpdateOutline();
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    void ApplyTitleBarTheme()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = App.IsDarkTheme ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        }
        catch { }
    }

    void Zoom(int direction)
    {
        double size = _settings.FontSize;
        size = direction switch { > 0 => Math.Min(48, size + 1), < 0 => Math.Max(7, size - 1), _ => 14 };
        _settings.FontSize = size;
        foreach (var d in _documents) d.Editor.FontSize = size;
        ConsoleView.SetFont(new FontFamily(_settings.FontFamily), size);
        UpdateStatusBar();
    }

    void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.OriginalSource is DependencyObject d && IsInsideEditor(d))
        {
            Zoom(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }
    }

    static bool IsInsideEditor(DependencyObject d)
    {
        for (var cur = d; cur != null; cur = cur is Visual || cur is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur is ICSharpCode.AvalonEdit.TextEditor) return true;
        return false;
    }

    // ------------------------------------------------------------------ clavier

    void RegisterKeyBindings()
    {
        void K(ICommand cmd, Key key, ModifierKeys mods = ModifierKeys.None, object? param = null) =>
            InputBindings.Add(new KeyBinding(cmd, key, mods) { CommandParameter = param });

        K(CmdNewSimula, Key.N, ModifierKeys.Control);
        K(CmdNewAlgol, Key.N, ModifierKeys.Control | ModifierKeys.Shift);
        K(CmdOpen, Key.O, ModifierKeys.Control);
        K(CmdSave, Key.S, ModifierKeys.Control);
        K(CmdSaveAll, Key.S, ModifierKeys.Control | ModifierKeys.Shift);
        K(CmdClose, Key.W, ModifierKeys.Control);
        K(CmdClose, Key.F4, ModifierKeys.Control);
        K(CmdFind, Key.F, ModifierKeys.Control);
        K(CmdReplace, Key.H, ModifierKeys.Control);
        K(CmdFindNext, Key.F3);
        K(CmdFindPrevious, Key.F3, ModifierKeys.Shift);
        K(CmdGoToLine, Key.G, ModifierKeys.Control);
        K(CmdComment, Key.K, ModifierKeys.Control);
        K(CmdUncomment, Key.K, ModifierKeys.Control | ModifierKeys.Shift);
        K(CmdReindent, Key.I, ModifierKeys.Control);
        K(CmdZoomIn, Key.OemPlus, ModifierKeys.Control);
        K(CmdZoomIn, Key.Add, ModifierKeys.Control);
        K(CmdZoomOut, Key.OemMinus, ModifierKeys.Control);
        K(CmdZoomOut, Key.Subtract, ModifierKeys.Control);
        K(CmdZoomReset, Key.D0, ModifierKeys.Control);
        K(CmdZoomReset, Key.NumPad0, ModifierKeys.Control);
        K(CmdBuild, Key.F7);
        K(CmdBuild, Key.B, ModifierKeys.Control | ModifierKeys.Shift);
        K(CmdGenerateExe, Key.E, ModifierKeys.Control | ModifierKeys.Shift);
        K(CmdDebug, Key.F5);
        K(CmdRun, Key.F5, ModifierKeys.Control);
        K(CmdStop, Key.F5, ModifierKeys.Shift);
        K(CmdRestart, Key.F5, ModifierKeys.Control | ModifierKeys.Shift);
        K(CmdPause, Key.Pause, ModifierKeys.Control | ModifierKeys.Alt);
        K(CmdPause, Key.Cancel, ModifierKeys.Control | ModifierKeys.Alt);
        K(CmdStepInto, Key.F11);
        K(CmdStepOut, Key.F11, ModifierKeys.Shift);
        K(CmdToggleBreakpoint, Key.F9);
        K(CmdClearBreakpoints, Key.F9, ModifierKeys.Control | ModifierKeys.Shift);
        K(CmdHelp, Key.F1);
        K(CmdShowPanel, Key.O, ModifierKeys.Control | ModifierKeys.Alt, "output");
        K(CmdShowPanel, Key.E, ModifierKeys.Control | ModifierKeys.Alt, "errors");
        K(CmdShowPanel, Key.C, ModifierKeys.Control | ModifierKeys.Alt, "console");
        K(CmdShowPanel, Key.P, ModifierKeys.Control | ModifierKeys.Alt, "stack");
        K(CmdShowPanel, Key.V, ModifierKeys.Control | ModifierKeys.Alt, "variables");
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // F10 est une touche « systeme » (active le menu) : traitee ici.
        if (e.Key == Key.System && e.SystemKey == Key.F10 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (CmdStepOver.CanExecute(null)) CmdStepOver.Execute(null);
            e.Handled = true;
            return;
        }
        // Accord Ctrl+M, Ctrl+O / Ctrl+L / Ctrl+M (repliage)
        if (Keyboard.Modifiers == ModifierKeys.Control && ActiveDocument != null && IsInsideEditor((DependencyObject)e.OriginalSource))
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
                SetStatusMessage("Ctrl+M a été enfoncé. Attente de la deuxième touche (O, L ou M)…");
                e.Handled = true;
                return;
            }
        }
        if (e.Key is not (Key.LeftCtrl or Key.RightCtrl)) _pendingChord = Key.None;
    }

    // ------------------------------------------------------------------ glisser-deposer

    void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        e.Handled = true;
        foreach (var f in files)
        {
            if (System.IO.Directory.Exists(f)) SetExplorerRoot(f);
            else if (File.Exists(f)) OpenFile(f);
        }
    }

    // ------------------------------------------------------------------ explorateur

    public void SetExplorerRoot(string path, bool remember = true)
    {
        if (!System.IO.Directory.Exists(path)) return;
        var root = new FileNode(path, true);
        root.IsExpanded = true;
        ExplorerTree.ItemsSource = new[] { root };
        ExplorerTitle.Text = "EXPLORATEUR — " + Path.GetFileName(path.TrimEnd('\\'));
        ExplorerTitle.ToolTip = path;
        _settings.ExplorerRoot = path;
        if (remember) _settings.AddRecentFolder(path);
    }

    FileNode? SelectedFileNode => ExplorerTree.SelectedItem as FileNode;

    void OnExplorerDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedFileNode is { IsDirectory: false } n && File.Exists(n.FullPath))
        {
            OpenFile(n.FullPath);
            e.Handled = true;
        }
    }

    void OnExplorerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SelectedFileNode is { IsDirectory: false } n && File.Exists(n.FullPath)) OpenFile(n.FullPath);
        else if (e.Key == Key.F5) OnExplorerRefresh(sender, e);
    }

    void OnExplorerOpen(object sender, RoutedEventArgs e)
    {
        if (SelectedFileNode is { } n)
        {
            if (n.IsDirectory) SetExplorerRoot(n.FullPath);
            else if (File.Exists(n.FullPath)) OpenFile(n.FullPath);
        }
    }

    void OnExplorerReveal(object sender, RoutedEventArgs e)
    {
        var p = SelectedFileNode?.FullPath ?? _settings.ExplorerRoot;
        if (p != null) RevealInExplorer(p);
    }

    void OnExplorerRefresh(object sender, RoutedEventArgs e)
    {
        if (_settings.ExplorerRoot != null) SetExplorerRoot(_settings.ExplorerRoot, remember: false);
    }

    void OnExplorerCollapse(object sender, RoutedEventArgs e)
    {
        if (ExplorerTree.ItemsSource is FileNode[] { Length: 1 } roots)
            foreach (var c in roots[0].Children) Collapse(c);
        static void Collapse(FileNode n) { foreach (var c in n.Children) Collapse(c); n.IsExpanded = false; }
    }

    public static void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (System.IO.Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch { }
    }

    // ------------------------------------------------------------------ structure (outline)

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
            OutlineTitle.Text = "STRUCTURE — " + doc.DisplayName;
        }
        else OutlineTitle.Text = "STRUCTURE";
        ApplyOutlineFilter();
    }

    void ApplyOutlineFilter()
    {
        var f = OutlineFilter.Text.Trim();
        var list = f.Length == 0 ? _outlineAll : _outlineAll.Where(o => o.Name.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        OutlineList.ItemsSource = list.ToList();
        OutlineEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OutlineEmpty.Text = ActiveDocument == null ? "Aucun fichier ouvert." : _outlineAll.Count == 0
            ? "Aucune procédure, classe, opérateur ou mode." : "Aucun élément ne correspond au filtre.";
    }

    void OnOutlineFilterChanged(object sender, TextChangedEventArgs e) => ApplyOutlineFilter();

    void OnOutlineClick(object sender, MouseButtonEventArgs e) => NavigateOutline();

    void OnOutlineKeyDown(object sender, KeyEventArgs e)
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

    // ------------------------------------------------------------------ edition

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
        SetStatusMessage(folded ? "Tous les blocs ont été repliés." : "Tous les blocs ont été dépliés.");
    }

    void ToggleFoldAtCaret()
    {
        var doc = ActiveDocument;
        if (doc == null) return;
        var f = doc.Folding.GetFoldingsContaining(doc.Editor.CaretOffset).LastOrDefault()
                ?? doc.Folding.GetNextFolding(doc.Editor.Document.GetLineByOffset(doc.Editor.CaretOffset).Offset);
        if (f != null) f.IsFolded = !f.IsFolded;
    }

    void GoToLineDialog()
    {
        var doc = ActiveDocument!;
        var dlg = new Dialogs.GoToLineWindow(doc.Editor.TextArea.Caret.Line, doc.Editor.Document.LineCount) { Owner = this };
        if (dlg.ShowDialog() == true) doc.GoTo(dlg.Line, 1);
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
        if (_findWindow == null || !_findWindow.IsLoaded)
        {
            _findWindow = new Dialogs.FindReplaceWindow(this, _search) { Owner = this };
            _findWindow.Show();
        }
        _findWindow.ShowMode(replace);
        _findWindow.Activate();
    }

    void FindAgain(bool backwards)
    {
        if (string.IsNullOrEmpty(_search.Text)) { ShowFind(false); return; }
        FindNext(backwards);
    }

    /// <summary>Recherche suivante dans l'editeur actif ; rend un message pour la barre d'etat.</summary>
    public string FindNext(bool backwards)
    {
        var doc = ActiveDocument;
        if (doc == null) return "Aucun fichier ouvert.";
        var rx = SearchEngine.Build(_search, out var error);
        if (rx == null) return SetSearchStatus(error!);
        bool found = SearchEngine.FindNext(doc, rx, backwards, out bool wrapped);
        return SetSearchStatus(found ? (wrapped ? "Recherche reprise au début du fichier." : "") : $"« {_search.Text} » introuvable.");
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
        return SetSearchStatus(n == 0 ? $"« {_search.Text} » introuvable." : $"{n} occurrence(s) remplacée(s).");
    }

    string SetSearchStatus(string message)
    {
        SetStatusMessage(message);
        return message;
    }

    // ------------------------------------------------------------------ aide

    Dialogs.HelpWindow? _helpWindow;

    void ShowHelp(string? topic)
    {
        topic ??= ActiveDocument?.Language.Id switch { "algol68" => "algol68", _ => "simula" };
        if (_helpWindow == null || !_helpWindow.IsLoaded)
        {
            _helpWindow = new Dialogs.HelpWindow();
            _helpWindow.Show();
        }
        _helpWindow.ShowTopic(topic);
        _helpWindow.Activate();
    }

    // ------------------------------------------------------------------ fenetre

    void RestoreWindowPlacement()
    {
        if (App.Options.Automated) { Width = 1280; Height = 800; WindowStartupLocation = WindowStartupLocation.CenterScreen; return; }
        Width = Math.Max(MinWidth, _settings.WindowWidth);
        Height = Math.Max(MinHeight, _settings.WindowHeight);
        if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop) &&
            _settings.WindowLeft > SystemParameters.VirtualScreenLeft - 50 &&
            _settings.WindowTop > SystemParameters.VirtualScreenTop - 50 &&
            _settings.WindowLeft < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
            _settings.WindowTop < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closingConfirmed) return;
        if (_state is IdeState.Running or IdeState.Debugging or IdeState.Paused or IdeState.External && !App.Options.Automated)
        {
            var r = MessageBox.Show(this, "Un programme est en cours d'exécution. L'arrêter et quitter ?", "Simula 67 & Algol 68 Studio",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
        }
        if (_state != IdeState.Idle) StopExecution();
        if (!App.Options.Automated && !ConfirmSaveModified(_documents.ToList())) { e.Cancel = true; return; }
        SaveSession();
        _closingConfirmed = true;
        _findWindow?.Close();
        _helpWindow?.Close();
    }

    public void SaveSession()
    {
        CapturePanelSizes();
        _settings.OpenFiles = _documents.Where(d => d.FilePath != null).Select(d => d.FilePath!).ToList();
        _settings.ActiveFile = ActiveDocument?.FilePath;
        foreach (var d in _documents.Where(d => d.FilePath != null))
            _settings.CaretOffsets[d.FilePath!] = d.Editor.CaretOffset;
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }
        _settings.Save();
    }

    /// <summary>Recharge les fichiers modifies sur le disque par un autre programme.</summary>
    void CheckExternalChanges()
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
                    AppendOutput($"Fichier rechargé (modifié hors de l'éditeur) : {d.FilePath}");
                }
                else
                {
                    var r = MessageBox.Show(this, $"Le fichier\n{d.FilePath}\na été modifié hors de l'éditeur.\n\nLe recharger et perdre vos modifications ?",
                        "Fichier modifié", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r == MessageBoxResult.Yes) d.Reload();
                    else d.LastWriteUtc = t;
                }
            }
        }
        catch (Exception ex) { Log.Write("vérification des fichiers : " + ex.Message); }
        finally { _checkingExternalChanges = false; }
    }

    // ------------------------------------------------------------------ sortie

    public void AppendOutput(string line)
    {
        OutputBox.AppendText(line + Environment.NewLine);
        if (OutputBox.Text.Length > 2_000_000) OutputBox.Text = OutputBox.Text[^1_000_000..];
        OutputBox.ScrollToEnd();
    }

    void OnClearOutput(object sender, RoutedEventArgs e) => OutputBox.Clear();

    /// <summary>Double-clic sur une ligne « fichier(l,c): … » de la sortie : aller a la position.</summary>
    void OnOutputDoubleClick(object sender, MouseButtonEventArgs e)
    {
        int idx = OutputBox.GetLineIndexFromCharacterIndex(OutputBox.CaretIndex);
        if (idx < 0) return;
        var line = OutputBox.GetLineText(idx);
        var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s*(?<f>.+?)\((?<l>\d+),(?<c>\d+)\):");
        if (m.Success)
        {
            NavigateTo(m.Groups["f"].Value, int.Parse(m.Groups["l"].Value), int.Parse(m.Groups["c"].Value));
            e.Handled = true;
        }
    }
}

/// <summary>Pastille de langage dans l'onglet (S, A, E).</summary>
public sealed class LanguageBadgeConverter : IValueConverter
{
    public static readonly LanguageBadgeConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is LanguageDefinition l ? l.Id switch { "simula" => "SIM", "algol68" => "A68", _ => "ECHO" } : "";
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
