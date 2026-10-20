using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Studio.Common;
using Studio.Ide.Editor;
using Studio.Ide.Language;
using Studio.Ide.Panels;
using Studio.Ide.Services;
using Studio.Tooling;

namespace Studio.Ide;

/// <summary>
/// Tests automatiques de l'IDE lance en vrai (fenetre affichee) :
///   --smoke-test [--screenshot fichier.png] : demarrage, deux fichiers, capture, fermeture.
///   --self-test [--self-test-dir dossier]   : scenario complet avec le langage de test « echo »
///     (compilation, erreurs, console, debogueur, exe autonome, editeur, theme), captures d'ecran.
/// Resultat : code de sortie 0/1 et journal selftest.log.
/// </summary>
public static class SelfTest
{
    static readonly List<string> Lines = new();
    static int _failures;

    static void Check(bool condition, string message)
    {
        Lines.Add((condition ? "ok     " : "ÉCHEC  ") + message);
        if (!condition) _failures++;
    }

    static void Info(string message) => Lines.Add("info   " + message);

    static async Task<bool> WaitFor(Func<bool> condition, string what, int timeoutMs = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
            {
                Check(false, "délai dépassé : " + what);
                return false;
            }
            await Task.Delay(25);
        }
        return true;
    }

    static string OutDir
    {
        get
        {
            var d = App.Options.SelfTestDir ?? Path.Combine(IdeSettings.Directory, "selftest");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static void Capture(Window w, string path)
    {
        try
        {
            w.UpdateLayout();
            var content = (FrameworkElement)w.Content;
            var dpi = VisualTreeHelper.GetDpi(w);
            int width = (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX);
            int height = (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY);
            var rect = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(w.Background, null, rect);
                dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, rect);
            }
            var rtb = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var fs = File.Create(path);
            enc.Save(fs);
            Info("capture : " + path);
        }
        catch (Exception ex)
        {
            Info("capture impossible : " + ex.Message);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);

    /// <summary>Capture de l'ecran a l'emplacement de la fenetre (inclut les menus deroulants).</summary>
    public static void CaptureFromScreen(Window win, string path)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            GetWindowRect(hwnd, out var r);
            int width = r.Right - r.Left, height = r.Bottom - r.Top;
            var screen = GetDC(IntPtr.Zero);
            var mem = CreateCompatibleDC(screen);
            var bmp = CreateCompatibleBitmap(screen, width, height);
            var old = SelectObject(mem, bmp);
            BitBlt(mem, 0, 0, width, height, screen, r.Left, r.Top, 0x00CC0020 /* SRCCOPY */);
            SelectObject(mem, old);
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(source));
            using var fs = File.Create(path);
            enc.Save(fs);
            Info("capture : " + path);
        }
        catch (Exception ex) { Info("capture impossible : " + ex.Message); }
    }

    /// <summary>Capture d'une fenetre avec PrintWindow (inclut les controles Win32 comme WebBrowser).</summary>
    public static void CaptureScreen(Window win, string path)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            GetWindowRect(hwnd, out var r);
            int width = r.Right - r.Left, height = r.Bottom - r.Top;
            var screen = GetDC(IntPtr.Zero);
            var mem = CreateCompatibleDC(screen);
            var bmp = CreateCompatibleBitmap(screen, width, height);
            var old = SelectObject(mem, bmp);
            PrintWindow(hwnd, mem, 2 /* PW_RENDERFULLCONTENT */);
            SelectObject(mem, old);
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(source));
            using var fs = File.Create(path);
            enc.Save(fs);
            Info("capture : " + path);
        }
        catch (Exception ex) { Info("capture impossible : " + ex.Message); }
    }

    static void Finish(MainWindow w, string name)
    {
        if (App.UnhandledErrors > 0)
        {
            foreach (var e in App.ErrorMessages) Lines.Add("EXCEPTION " + e);
            _failures += App.UnhandledErrors;
        }
        Lines.Add($"{(_failures == 0 ? "SUCCÈS" : "ÉCHEC")} : {_failures} échec(s), {_realWarnings} avertissement(s) sur les compilateurs réels");
        var log = Path.Combine(OutDir, name + ".log");
        File.WriteAllText(log, string.Join(Environment.NewLine, Lines) + Environment.NewLine, new UTF8Encoding(false));
        int code = _failures == 0 ? 0 : 1;
        w.Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown(code)));
    }

    // ------------------------------------------------------------------ test de fumee

    public const string SimulaSample =
        "begin\r\n" +
        "   comment Exemple : classes et procédures;\r\n" +
        "   class Pile(n); integer n;\r\n" +
        "   begin\r\n" +
        "      integer array t(1:n); integer sommet;\r\n" +
        "      procedure empiler(x); integer x;\r\n" +
        "      begin\r\n" +
        "         sommet := sommet + 1; t(sommet) := x\r\n" +
        "      end empiler;\r\n" +
        "      integer procedure depiler;\r\n" +
        "      begin\r\n" +
        "         depiler := t(sommet); sommet := sommet - 1\r\n" +
        "      end depiler;\r\n" +
        "   end Pile;\r\n" +
        "   ref(Pile) p;\r\n" +
        "   p :- new Pile(10);\r\n" +
        "   p.empiler(42); ! un commentaire;\r\n" +
        "   OutText(\"Sommet : \"); OutInt(p.depiler, 4); OutImage\r\n" +
        "end\r\n";

    public const string AlgolSample =
        "BEGIN\r\n" +
        "   # Exemple Algol 68 : récursivité et modes #\r\n" +
        "   MODE POINT = STRUCT(REAL x, y);\r\n" +
        "   PROC fact = (INT n) INT:\r\n" +
        "      IF n <= 1 THEN 1 ELSE n * fact(n - 1) FI;\r\n" +
        "   OP NORME = (POINT p) REAL: sqrt(x OF p ** 2 + y OF p ** 2);\r\n" +
        "   FOR i TO 5 DO\r\n" +
        "      print((\"fact(\", whole(i, 0), \") = \", whole(fact(i), 0), newline))\r\n" +
        "   OD;\r\n" +
        "   CO commentaire\r\n" +
        "      sur plusieurs lignes CO\r\n" +
        "   print((NORME POINT(3.0, 4.0), newline))\r\n" +
        "END\r\n";

    public static async Task RunSmokeAsync(MainWindow w)
    {
        try
        {
            await Task.Delay(300);
            Check(w.IsVisible, "fenêtre principale affichée");
            var sim = w.NewDocument(LanguageDefinitions.Simula, SimulaSample);
            var alg = w.NewDocument(LanguageDefinitions.Algol68, AlgolSample);
            Check(sim.Model.Tokens.Count > 20 && alg.Model.Tokens.Count > 20, "coloration : jetons Simula et Algol 68");
            Check(sim.Model.Folds.Count > 0 && alg.Model.Folds.Count > 0, "repliage calculé");
            Check(w.Documents.Count == 2, "deux onglets ouverts");
            w.Activate(sim);
            await Task.Delay(900);
            if (App.Options.Screenshot != null) Capture(w, App.Options.Screenshot);
        }
        catch (Exception ex)
        {
            Check(false, "exception : " + ex);
        }
        Finish(w, "smoke-test");
    }

    // ------------------------------------------------------------------ scenario complet

    const string EchoProgram =
        "# programme de test\n" +        // 1
        "print Bonjour\n" +              // 2
        "read nom\n" +                   // 3
        "print Salut {nom}\n" +          // 4
        "list l = a, b\n" +              // 5
        "call p\n" +                     // 6
        "print fin\n" +                  // 7
        "proc p\n" +                     // 8
        "  set x = 5\n" +                // 9
        "  print dans p {x}\n" +         // 10
        "end\n";                         // 11

    public static async Task RunAsync(MainWindow w)
    {
        var dir = OutDir;
        try
        {
            await Task.Delay(300);
            Info("dossier de travail : " + dir);
            Info("Runner : " + (RunnerLocator.Find() ?? "introuvable"));

            // ---- 1. Nouveau fichier, enregistrement
            var doc = w.NewDocument(LanguageDefinitions.Echo, EchoProgram);
            var path = Path.Combine(dir, "test.echo");
            Check(w.SaveDocumentAs(doc, path), "enregistrer sous " + path);
            Check(File.Exists(path) && !doc.IsModified, "fichier écrit, indicateur de modification remis à zéro");
            Check(doc.Title == "test.echo", "titre de l'onglet : " + doc.Title);

            // ---- 2. Construire
            var ok = await w.BuildAsync();
            Check(ok != null && ok.Result.Success, "construction réussie");
            Check(w.ErrorItems.Count == 0, "liste d'erreurs vide");

            // ---- 3. Erreur de compilation : liste, soulignement, navigation
            doc.Editor.Document.Insert(doc.Editor.Document.GetLineByNumber(3).Offset, "   bidule 1\n");
            Check(doc.IsModified && doc.Title.EndsWith("•"), "indicateur de modification");
            var bad = await w.BuildAsync();
            Check(bad == null, "construction en échec");
            var err = w.ErrorItems.FirstOrDefault(e => e.Severity == Severity.Error);
            Check(err != null && err.Line == 3 && err.Column == 4, $"erreur ligne 3 colonne 4 ({err?.Line},{err?.Column})");
            Check(doc.Markers.Count > 0, "soulignement ondulé posé");
            Check(w.BottomTabs.SelectedItem == w.ErrorsTab, "panneau Erreurs affiché");
            doc.GoTo(1, 1);
            w.ErrorList.SelectedIndex = 0;
            w.NavigateToSelectedError();
            Check(doc.Editor.TextArea.Caret.Line == 3 && doc.Editor.TextArea.Caret.Column == 4, "double-clic → ligne 3, colonne 4");
            Capture(w, Path.Combine(dir, "1-erreurs.png"));
            doc.Editor.Undo();
            Check(doc.Editor.Document.Text == EchoProgram, "annulation de la modification");
            w.SaveDocument(doc);

            // ---- 4. Execution avec console integree
            await w.RunAsync(false);
            Check(w.State == IdeState.Running, "état Exécution");
            await WaitFor(() => w.ConsoleView.Text.Contains("Bonjour"), "sortie « Bonjour » dans la console");
            w.ConsoleView.Submit("Ada");
            await WaitFor(() => w.State == IdeState.Idle, "fin du programme");
            Check(w.LastResult is { ExitCode: 0, Error: null }, "code de sortie 0");
            Check(w.ConsoleView.Text.Contains("Salut Ada") && w.ConsoleView.Text.Contains("dans p 5"), "sortie complète : " + Flat(w.ConsoleView.Text));
            Capture(w, Path.Combine(dir, "2-console.png"));

            // ---- 5. Debogueur : point d'arret, pas a pas, pile, variables
            doc.ToggleBreakpoint(6);
            Check(doc.BreakpointLines.SequenceEqual(new[] { 6 }), "point d'arrêt ligne 6");
            Check(w.Settings.Breakpoints.TryGetValue(path, out var saved) && saved.Contains(6), "point d'arrêt mémorisé dans les paramètres");
            await w.RunAsync(true);
            Check(w.State == IdeState.Debugging, "état Débogage");
            await WaitFor(() => w.ConsoleView.Text.Contains("Bonjour"), "sortie avant la saisie");
            w.ConsoleView.Submit("Bob");
            await WaitFor(() => w.State == IdeState.Paused, "pause sur le point d'arrêt");
            Check(doc.ExecutionLine == 6, $"ligne courante 6 ({doc.ExecutionLine})");
            Check(w.Session?.LastPause?.Reason == PauseReason.Breakpoint, "raison : point d'arrêt");
            Check(w.StackItems.Count == 1, "pile : 1 frame");
            Check(w.VariableNodes.Any(v => v.Name == "nom" && v.Value == "\"Bob\""), "variable nom = \"Bob\"");
            var list = w.VariableNodes.FirstOrDefault(v => v.Name == "l");
            Check(list != null && list.Children.Count == 1 && list.Children[0].Name.StartsWith("chargement"), "liste : enfants non encore chargés");
            if (list != null)
            {
                list.IsExpanded = true;
                Check(list.Children.Count == 2 && list.Children[1].Value == "\"b\"", "dépliage : enfants chargés à la demande");
            }
            Capture(w, Path.Combine(dir, "3-debogueur.png"));

            await w.StepAsync(StepKind.Into);
            await WaitFor(() => w.State == IdeState.Paused, "pause après pas à pas détaillé");
            Check(doc.ExecutionLine == 9, $"pas à pas détaillé → ligne 9 ({doc.ExecutionLine})");
            Check(w.StackItems.Count == 2 && w.StackItems[0].Name == "p", "pile : p puis programme principal");
            await w.StepAsync(StepKind.Over);
            await WaitFor(() => w.State == IdeState.Paused, "pause après pas à pas principal");
            Check(doc.ExecutionLine == 10, $"pas à pas principal → ligne 10 ({doc.ExecutionLine})");
            Check(w.VariableNodes.Any(v => v.Name == "x" && v.Value == "5"), "variable locale x = 5");
            w.SelectFrame(1);
            Check(doc.ExecutionLine == 6 && !doc.ExecutionIsTopFrame, "sélection de l'appelant → ligne 6");
            Check(w.VariableNodes.Any(v => v.Name == "nom"), "variables de l'appelant");
            Check(w.VariableNodes.FirstOrDefault(v => v.Name == "l")?.IsExpanded == true, "état déplié conservé entre deux pauses");
            w.SelectFrame(0);
            await w.StepAsync(StepKind.Out);
            await WaitFor(() => w.State == IdeState.Paused, "pause après pas à pas sortant");
            Check(doc.ExecutionLine == 7, $"pas à pas sortant → ligne 7 ({doc.ExecutionLine})");
            w.CmdDebug.Execute(null); // F5 = continuer
            await WaitFor(() => w.State == IdeState.Idle, "fin après continuer");
            Check(w.LastResult is { ExitCode: 0 }, "programme terminé");
            Check(doc.ExecutionLine == null, "ligne courante effacée");
            doc.ToggleBreakpoint(6);

            // ---- 6. Erreur d'execution (sans et avec debogueur)
            var errDoc = w.NewDocument(LanguageDefinitions.Echo, "print avant\ncall q\nproc q\n  error division par zéro\nend\n");
            await w.RunAsync(false);
            await WaitFor(() => w.State == IdeState.Idle, "fin du programme en erreur");
            Check(w.LastResult?.Error?.Line == 4, "erreur d'exécution ligne 4");
            Check(w.ErrorItems.Any(e => e.IsRuntime && e.Line == 4), "erreur d'exécution dans la liste d'erreurs");
            Check(w.ConsoleView.Text.Contains("division par zéro"), "erreur affichée dans la console");
            w.Settings.BreakOnError = true;
            await w.RunAsync(true);
            await WaitFor(() => w.State == IdeState.Paused, "arrêt sur erreur d'exécution");
            Check(w.Session?.LastPause?.Reason == PauseReason.Exception, "raison : erreur d'exécution");
            Check(errDoc.ExecutionLine == 4, "ligne de l'erreur surlignée");
            Check(w.StackItems.Count == 2, "pile au moment de l'erreur");
            w.StopExecution();
            await WaitFor(() => w.State == IdeState.Idle, "arrêt depuis la pause");

            // ---- 7. Arret d'un programme qui boucle
            var loopDoc = w.NewDocument(LanguageDefinitions.Echo, "loop 100000\n  print tour {i}\n  sleep 5\nend\n");
            await w.RunAsync(false);
            await WaitFor(() => w.ConsoleView.Text.Contains("tour 3"), "le programme tourne");
            w.StopExecution();
            await WaitFor(() => w.State == IdeState.Idle, "arrêt (Maj+F5)");
            Check(w.LastResult?.Cancelled == true, "résultat : annulé");

            // ---- 8. Pause demandee pendant l'execution
            await w.RunAsync(true);
            await WaitFor(() => w.ConsoleView.Text.Contains("tour 2"), "débogage en cours");
            w.CmdPause.Execute(null);
            await WaitFor(() => w.State == IdeState.Paused, "pause demandée");
            Check(w.Session?.LastPause?.Reason == PauseReason.PauseRequest, "raison : pause demandée");
            Check(loopDoc.ExecutionLine is 2 or 3, "ligne courante dans la boucle");
            w.StopExecution();
            await WaitFor(() => w.State == IdeState.Idle, "arrêt après pause");

            // ---- 9. Exe autonome
            w.Activate(doc);
            await Task.Delay(50);
            var exe = Path.Combine(dir, "test.exe");
            var generated = await w.GenerateExeAsync(exe);
            Check(generated != null && File.Exists(exe), "exe généré");
            if (generated != null)
            {
                var psi = new ProcessStartInfo(exe)
                {
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8,
                    StandardInputEncoding = new UTF8Encoding(false),
                };
                using var p = Process.Start(psi)!;
                var outTask = p.StandardOutput.ReadToEndAsync();
                p.StandardInput.WriteLine("Zoé");
                p.StandardInput.Close();
                bool exited = await Task.Run(() => p.WaitForExit(20000));
                var output = await outTask;
                Check(exited && p.ExitCode == 0, "exe : code de sortie 0");
                Check(output.Contains("Salut Zoé") && output.Contains("dans p 5"), "exe : sortie " + Flat(output));
            }

            // ---- 10. Editeur : Simula / Algol 68
            var sim = w.NewDocument(LanguageDefinitions.Simula, SimulaSample);
            await Task.Delay(450);
            Check(sim.Model.Outline.Select(o => o.Name).SequenceEqual(new[] { "Pile", "empiler", "depiler" }), "structure Simula : " + string.Join(",", sim.Model.Outline.Select(o => o.Name)));
            Check(w.OutlineList.Items.Count == 3, "panneau Structure rempli");
            Check(sim.Folding.AllFoldings.Count() >= 3, "repliage Simula : " + sim.Folding.AllFoldings.Count());
            sim.Editor.CaretOffset = SimulaSample.IndexOf("begin", StringComparison.Ordinal);
            Check(sim.Model.PairOf.Count > 0, "appariement des blocs");
            var alg = w.NewDocument(LanguageDefinitions.Algol68, AlgolSample);
            await Task.Delay(450);
            Check(alg.Model.Outline.Select(o => o.Name).SequenceEqual(new[] { "POINT", "fact", "NORME" }), "structure Algol 68 : " + string.Join(",", alg.Model.Outline.Select(o => o.Name)));
            Check(alg.Folding.AllFoldings.Count() >= 2, "repliage Algol 68 : " + alg.Folding.AllFoldings.Count());
            Check(alg.Model.Tokens.Any(t => t.Kind == TokenKind.Comment && alg.Model.TokenText(t).Contains("plusieurs lignes")), "commentaire CO multi-ligne");
            // Indentation automatique apres BEGIN
            alg.Editor.CaretOffset = alg.Editor.Document.GetLineByNumber(1).EndOffset;
            alg.Editor.TextArea.PerformTextInput("\n");
            var newLine = alg.Editor.Document.GetText(alg.Editor.Document.GetLineByNumber(2));
            Check(newLine == "   ", $"indentation automatique après BEGIN (« {newLine} »)");
            alg.Editor.Undo();
            // Commenter / decommenter
            w.Activate(sim);
            var before = sim.Editor.Text;
            sim.Editor.SelectAll();
            sim.ToggleComment(true);
            Check(sim.Model.Tokens.All(t => t.Kind == TokenKind.Comment), "toutes les lignes commentées");
            sim.Editor.SelectAll();
            sim.ToggleComment(false);
            Check(sim.Editor.Text == before, "décommenter restitue le texte");
            // Rechercher / remplacer
            w.Search.Text = "integer"; w.Search.Replacement = "INTEGER"; w.Search.MatchCase = true; w.Search.WholeWord = true; w.Search.UseRegex = false;
            sim.Editor.Select(0, 0);
            w.FindNext(false);
            Check(sim.Editor.SelectedText == "integer", "rechercher : occurrence sélectionnée");
            var msg = w.ReplaceAll();
            Check(msg.StartsWith("5 "), "remplacer tout : " + msg);
            sim.Editor.Undo();
            Check(sim.Editor.Text == before, "remplacer tout annulable en une étape");
            w.Search.Text = @"OutI\w+"; w.Search.UseRegex = true; w.Search.WholeWord = false;
            sim.Editor.Select(0, 0);
            w.FindNext(false);
            Check(sim.Editor.SelectedText == "OutInt", "recherche par expression régulière : " + sim.Editor.SelectedText);
            // Completion
            sim.Editor.CaretOffset = sim.Editor.Document.GetLineByNumber(17).Offset + 3;
            sim.ShowCompletion();
            Info("complétion ouverte");
            await Task.Delay(300);
            sim.CloseCompletion();
            await Task.Delay(100);
            sim.Editor.TextArea.Focus();

            // ---- 10b. Menus (rendu des menus deroulants, capture de l'ecran)
            w.Topmost = true;
            w.Activate();
            var debugMenu = w.DebugMenuItem.Parent as System.Windows.Controls.MenuItem;
            if (debugMenu != null)
            {
                debugMenu.IsSubmenuOpen = true;
                await Task.Delay(600);
                Check(debugMenu.IsSubmenuOpen, "menu Déboguer ouvert");
                CaptureFromScreen(w, Path.Combine(dir, "7-menu.png"));
                debugMenu.IsSubmenuOpen = false;
            }
            w.Topmost = false;

            // ---- 11. Theme sombre
            w.SetTheme(true);
            w.Activate(alg);
            await Task.Delay(400);
            Capture(w, Path.Combine(dir, "4-theme-sombre.png"));
            w.SetTheme(false);

            // ---- 12a. Vrais compilateurs : execution et debogueur (avertissements, non bloquants)
            await RealCompilerScenario(w, LanguageDefinitions.Simula, RealSimula, "Somme :", 5, "n");
            await RealCompilerScenario(w, LanguageDefinitions.Algol68, RealAlgol, "Somme :", 5, "n");

            // ---- 12b. Exemples (information)
            foreach (var sample in MainWindow.ListSamples().Take(300))
            {
                var d = w.OpenFile(sample);
                if (d == null) continue;
                var outcome = await w.BuildAsync(showSuccess: false);
                Info($"exemple {Path.GetFileName(sample)} : " + (outcome != null ? "compilation réussie" :
                    "échec — " + string.Join(" | ", w.ErrorItems.Take(2).Select(e => $"({e.Line},{e.Column}) {e.Message}"))));
                w.CloseDocument(d, force: true);
            }

            // ---- 12c. Aide (rendu Markdown dans le navigateur integre)
            w.CmdHelp.Execute("simula");
            await Task.Delay(2500);
            var help = Application.Current.Windows.OfType<Dialogs.HelpWindow>().FirstOrDefault();
            Check(help != null && help.IsVisible, "fenêtre d'aide ouverte");
            if (help != null)
            {
                bool hasDoc = PathLocator.DocsDirectory != null && File.Exists(Path.Combine(PathLocator.DocsDirectory, "simula.md"));
                Check(!hasDoc || help.TocList.Items.Count > 3, $"sommaire de docs/simula.md ({help.TocList.Items.Count} titres)");
                Check(help.Browser.Document != null, "page d'aide chargée");
                CaptureScreen(help, Path.Combine(dir, "6-aide.png"));
                help.ShowTopic("shortcuts");
                await Task.Delay(800);
                Check(help.TocList.Items.Count >= 5, "aide : raccourcis clavier");
                help.Close();
            }

            // ---- 13. Session
            w.SaveSession();
            Check(File.Exists(Path.Combine(IdeSettings.Directory, "settings.json")), "paramètres enregistrés");
        }
        catch (Exception ex)
        {
            Check(false, "exception : " + ex);
        }
        Finish(w, "self-test");
    }

    const string RealSimula =
        "begin\r\n" +                                        // 1
        "   integer i, s;\r\n" +                             // 2
        "   procedure ajoute(n); integer n;\r\n" +           // 3
        "   begin\r\n" +                                     // 4
        "      s := s + n\r\n" +                             // 5
        "   end;\r\n" +                                      // 6
        "   for i := 1 step 1 until 3 do\r\n" +              // 7
        "      ajoute(i);\r\n" +                             // 8
        "   OutText(\"Somme :\"); OutInt(s, 3); OutImage\r\n" + // 9
        "end\r\n";                                           // 10

    const string RealAlgol =
        "BEGIN\r\n" +                                                // 1
        "   INT s := 0;\r\n" +                                       // 2
        "   PROC ajoute = (INT n) VOID:\r\n" +                       // 3
        "   BEGIN\r\n" +                                             // 4
        "      s +:= n\r\n" +                                        // 5
        "   END;\r\n" +                                              // 6
        "   FOR i TO 3 DO ajoute(i) OD;\r\n" +                       // 7
        "   print((\"Somme :\", whole(s, 3), newline))\r\n" +        // 8
        "END\r\n";                                                   // 9
    static int _realWarnings;

    /// <summary>Avertissement « compilateur reel » : consigne, mais ne fait pas echouer l'auto-test.</summary>
    static void Real(bool condition, string message)
    {
        Lines.Add((condition ? "ok     " : "ATTENTION ") + "[compilateur réel] " + message);
        if (!condition) _realWarnings++;
    }

    static async Task<bool> RealWait(Func<bool> cond, string what, int timeoutMs = 15000)
    {
        var sw = Stopwatch.StartNew();
        while (!cond())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) { Real(false, "délai dépassé : " + what); return false; }
            await Task.Delay(25);
        }
        return true;
    }

    static async Task RealCompilerScenario(MainWindow w, LanguageDefinition lang, string source, string expected, int bpLine, string localVar)
    {
        var doc = w.NewDocument(lang, source);
        var path = Path.Combine(OutDir, "reel" + lang.DefaultExtension);
        w.SaveDocumentAs(doc, path);
        var outcome = await w.BuildAsync(showSuccess: false);
        if (outcome == null)
        {
            Real(false, $"{lang.DisplayName} : compilation impossible — " +
                string.Join(" | ", w.ErrorItems.Take(3).Select(e => $"({e.Line},{e.Column}) {e.Message}")));
            w.CloseDocument(doc, force: true);
            return;
        }
        Real(true, $"{lang.DisplayName} : compilation réussie");
        // Execution dans la console integree
        await w.RunAsync(false);
        if (await RealWait(() => w.State == IdeState.Idle, $"{lang.DisplayName} : fin de l'exécution"))
            Real(w.LastResult is { Error: null, ExitCode: 0 } && w.ConsoleView.Text.Contains(expected),
                $"{lang.DisplayName} : exécution, sortie « {Flat(w.ConsoleView.Text)} »");
        else { w.StopExecution(); await RealWait(() => w.State == IdeState.Idle, "arrêt"); }
        // Debogueur
        doc.ToggleBreakpoint(bpLine);
        await w.RunAsync(true);
        if (await RealWait(() => w.State is IdeState.Paused or IdeState.Idle, $"{lang.DisplayName} : arrêt sur le point d'arrêt") && w.State == IdeState.Paused)
        {
            Real(doc.ExecutionLine == bpLine, $"{lang.DisplayName} : ligne courante {doc.ExecutionLine} (attendue {bpLine})");
            Real(w.StackItems.Count >= 2, $"{lang.DisplayName} : pile ({string.Join(" ← ", w.StackItems.Select(s => $"{s.Name}:{s.LineText}"))})");
            Real(w.VariableNodes.Any(v => v.Name.Equals(localVar, StringComparison.OrdinalIgnoreCase)),
                $"{lang.DisplayName} : variables ({string.Join(", ", w.VariableNodes.Select(v => $"{v.Name}={v.Value}"))})");
            Capture(w, Path.Combine(OutDir, $"5-debogueur-{lang.Id}.png"));
            await w.StepAsync(StepKind.Over);
            if (await RealWait(() => w.State is IdeState.Paused or IdeState.Idle, $"{lang.DisplayName} : pas à pas principal"))
                Real(w.State == IdeState.Paused, $"{lang.DisplayName} : pas à pas principal → ligne {doc.ExecutionLine}");
            doc.ToggleBreakpoint(bpLine); // retire le point d'arret pendant l'execution
            w.CmdDebug.Execute(null);
            if (await RealWait(() => w.State == IdeState.Idle, $"{lang.DisplayName} : fin après continuer"))
                Real(w.ConsoleView.Text.Contains(expected), $"{lang.DisplayName} : sortie après débogage");
        }
        else Real(false, $"{lang.DisplayName} : le débogueur ne s'est pas arrêté sur la ligne {bpLine}");
        if (w.State != IdeState.Idle) { w.StopExecution(); await RealWait(() => w.State == IdeState.Idle, "arrêt"); }
        if (doc.BreakpointLines.Count > 0) doc.SetBreakpoints(Array.Empty<int>());
        // Exe autonome
        var exe = Path.Combine(OutDir, "reel-" + lang.Id + ".exe");
        if (await w.GenerateExeAsync(exe) != null)
        {
            var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
            using var p = Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync();
            await Task.Run(() => p.WaitForExit(20000));
            Real(output.Contains(expected) && p.ExitCode == 0, $"{lang.DisplayName} : exe autonome, sortie « {Flat(output)} »");
        }
        else Real(false, $"{lang.DisplayName} : génération de l'exe");
        w.CloseDocument(doc, force: true);
    }

    static string Flat(string s) => s.Replace("\r", "").Replace("\n", " ⏎ ").Trim();
}
