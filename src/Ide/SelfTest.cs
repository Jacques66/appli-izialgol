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
/// Automated tests of the IDE running for real (window shown):
///   --smoke-test [--screenshot file.png] : start-up, two files, screenshot, shutdown.
///   --self-test [--self-test-dir folder]  : full scenario with the "echo" test language
///     (compilation, errors, console, debugger, standalone exe, editor, theme), screenshots.
/// Result: exit code 0/1 and a log file (self-test.log / smoke-test.log).
/// </summary>
public static class SelfTest
{
    static readonly List<string> Lines = new();
    static int _failures;

    static void Check(bool condition, string message)
    {
        Lines.Add((condition ? "ok     " : "FAILED ") + message);
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
                Check(false, "timed out: " + what);
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
            Info("capture: " + path);
        }
        catch (Exception ex)
        {
            Info("capture failed: " + ex.Message);
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

    /// <summary>Captures the screen where the window is (includes drop-down menus).</summary>
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
            Info("capture: " + path);
        }
        catch (Exception ex) { Info("capture failed: " + ex.Message); }
    }

    /// <summary>Captures a window with PrintWindow (includes Win32 controls such as WebBrowser).</summary>
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
            Info("capture: " + path);
        }
        catch (Exception ex) { Info("capture failed: " + ex.Message); }
    }

    static void Finish(MainWindow w, string name)
    {
        if (App.UnhandledErrors > 0)
        {
            foreach (var e in App.ErrorMessages) Lines.Add("EXCEPTION " + e);
            _failures += App.UnhandledErrors;
        }
        Lines.Add($"{(_failures == 0 ? "SUCCESS" : "FAILURE")}: {_failures} failure(s), {_realWarnings} warning(s) on the real compilers");
        var log = Path.Combine(OutDir, name + ".log");
        File.WriteAllText(log, string.Join(Environment.NewLine, Lines) + Environment.NewLine, new UTF8Encoding(false));
        int code = _failures == 0 ? 0 : 1;
        w.Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown(code)));
    }

    // ------------------------------------------------------------------ smoke test

    public const string SimulaSample =
        "begin\r\n" +                                                    // 1
        "   comment Example: classes and procedures;\r\n" +              // 2
        "   class Stack(n); integer n;\r\n" +                            // 3
        "   begin\r\n" +                                                 // 4
        "      integer array t(1:n); integer top;\r\n" +                 // 5
        "      procedure push(x); integer x;\r\n" +                      // 6
        "      begin\r\n" +                                              // 7
        "         top := top + 1; t(top) := x\r\n" +                     // 8
        "      end push;\r\n" +                                          // 9
        "      integer procedure pop;\r\n" +                             // 10
        "      begin\r\n" +                                              // 11
        "         pop := t(top); top := top - 1\r\n" +                   // 12
        "      end pop;\r\n" +                                           // 13
        "   end Stack;\r\n" +                                            // 14
        "   ref(Stack) p;\r\n" +                                         // 15
        "   p :- new Stack(10);\r\n" +                                   // 16
        "   p.push(42); ! a comment;\r\n" +                              // 17
        "   OutText(\"Top: \"); OutInt(p.pop, 4); OutImage\r\n" +        // 18
        "end\r\n";                                                       // 19

    public const string AlgolSample =
        "BEGIN\r\n" +
        "   # Example Algol 68: recursion and modes #\r\n" +
        "   MODE POINT = STRUCT(REAL x, y);\r\n" +
        "   PROC fact = (INT n) INT:\r\n" +
        "      IF n <= 1 THEN 1 ELSE n * fact(n - 1) FI;\r\n" +
        "   OP NORM = (POINT p) REAL: sqrt(x OF p ** 2 + y OF p ** 2);\r\n" +
        "   FOR i TO 5 DO\r\n" +
        "      print((\"fact(\", whole(i, 0), \") = \", whole(fact(i), 0), newline))\r\n" +
        "   OD;\r\n" +
        "   CO comment\r\n" +
        "      over several lines CO\r\n" +
        "   print((NORM POINT(3.0, 4.0), newline))\r\n" +
        "END\r\n";

    public const string Algol60Sample =
        "comment Example Algol 60: call by name;\r\n" +                                        // 1
        "begin\r\n" +                                                                          // 2
        "   integer i;\r\n" +                                                                  // 3
        "   real procedure sum(i, lo, hi, term); value lo, hi; integer i, lo, hi; real term;\r\n" + // 4
        "   begin real s;\r\n" +                                                               // 5
        "      s := 0;\r\n" +                                                                  // 6
        "      for i := lo step 1 until hi do s := s + term;\r\n" +                            // 7
        "      sum := s\r\n" +                                                                 // 8
        "   end sum;\r\n" +                                                                    // 9
        "   Boolean procedure even(n); value n; integer n; even := n ÷ 2 × 2 = n;\r\n" +       // 10
        "   outstring(1, \"H(10) = \"); outreal(1, sum(i, 1, 10, 1 / i)); outstring(1, `NL')\r\n" + // 11
        "end of program\r\n";                                                                  // 12

    public const string SimulaISample =
        "comment Example Simula I: activities and a set;\r\n" +            // 1
        "SIMULA begin\r\n" +                                               // 2
        "   set queue;\r\n" +                                              // 3
        "   activity customer(n); integer n;\r\n" +                        // 4
        "   begin\r\n" +                                                   // 5
        "      include(current, queue);\r\n" +                             // 6
        "      hold(n);\r\n" +                                             // 7
        "      outstring(1, \"served at \"); outreal(1, time)\r\n" +       // 8
        "   end of customer;\r\n" +                                        // 9
        "   activate new customer(3) delay 1;\r\n" +                       // 10
        "   hold(10)\r\n" +                                                // 11
        "end of simulation model\r\n";                                     // 12

    public static async Task RunSmokeAsync(MainWindow w)
    {
        try
        {
            await Task.Delay(300);
            Check(w.IsVisible, "main window shown");
            var sim = w.NewDocument(LanguageDefinitions.Simula, SimulaSample);
            var alg = w.NewDocument(LanguageDefinitions.Algol68, AlgolSample);
            var a60 = w.NewDocument(LanguageDefinitions.Algol60, Algol60Sample);
            var s1 = w.NewDocument(LanguageDefinitions.SimulaI, SimulaISample);
            Check(sim.Model.Tokens.Count > 20 && alg.Model.Tokens.Count > 20, "highlighting: Simula and Algol 68 tokens");
            Check(a60.Model.Tokens.Count > 20 && s1.Model.Tokens.Count > 20, "highlighting: Algol 60 and Simula I tokens");
            Check(sim.Model.Folds.Count > 0 && alg.Model.Folds.Count > 0, "folding computed");
            Check(a60.Model.Folds.Count > 0 && s1.Model.Folds.Count > 0, "folding computed (Algol 60, Simula I)");
            Check(w.Documents.Count == 4, "four tabs open");
            w.Activate(sim);
            await Task.Delay(900);
            if (App.Options.Screenshot != null) Capture(w, App.Options.Screenshot);
        }
        catch (Exception ex)
        {
            Check(false, "exception: " + ex);
        }
        Finish(w, "smoke-test");
    }

    // ------------------------------------------------------------------ full scenario

    const string EchoProgram =
        "# test program\n" +             // 1
        "print Hello\n" +                // 2
        "read name\n" +                  // 3
        "print Hi {name}\n" +            // 4
        "list l = a, b\n" +              // 5
        "call p\n" +                     // 6
        "print end\n" +                  // 7
        "proc p\n" +                     // 8
        "  set x = 5\n" +                // 9
        "  print in p {x}\n" +           // 10
        "end\n";                         // 11

    public static async Task RunAsync(MainWindow w)
    {
        var dir = OutDir;
        try
        {
            await Task.Delay(300);
            Info("working folder: " + dir);
            Info("Runner: " + (RunnerLocator.Find() ?? "not found"));

            // ---- 1. New file, save
            var doc = w.NewDocument(LanguageDefinitions.Echo, EchoProgram);
            var path = Path.Combine(dir, "test.echo");
            Check(w.SaveDocumentAs(doc, path), "save as " + path);
            Check(File.Exists(path) && !doc.IsModified, "file written, modified flag reset");
            Check(doc.Title == "test.echo", "tab title: " + doc.Title);

            // ---- 2. Build
            var ok = await w.BuildAsync();
            Check(ok != null && ok.Result.Success, "build succeeded");
            Check(w.ErrorItems.Count == 0, "error list empty");

            // ---- 3. Compile error: list, squiggle, navigation
            doc.Editor.Document.Insert(doc.Editor.Document.GetLineByNumber(3).Offset, "   bogus 1\n");
            Check(doc.IsModified && doc.Title.EndsWith("•"), "modified flag");
            var bad = await w.BuildAsync();
            Check(bad == null, "build failed");
            var err = w.ErrorItems.FirstOrDefault(e => e.Severity == Severity.Error);
            Check(err != null && err.Line == 3 && err.Column == 4, $"error at line 3 column 4 ({err?.Line},{err?.Column})");
            Check(doc.Markers.Count > 0, "squiggly underline placed");
            Check(w.BottomTabs.SelectedItem == w.ErrorsTab, "Errors panel shown");
            doc.GoTo(1, 1);
            w.ErrorList.SelectedIndex = 0;
            w.NavigateToSelectedError();
            Check(doc.Editor.TextArea.Caret.Line == 3 && doc.Editor.TextArea.Caret.Column == 4, "double-click → line 3, column 4");
            Capture(w, Path.Combine(dir, "1-errors.png"));
            doc.Editor.Undo();
            Check(doc.Editor.Document.Text == EchoProgram, "edit undone");
            w.SaveDocument(doc);

            // ---- 4. Run with the integrated console
            await w.RunAsync(false);
            Check(w.State == IdeState.Running, "Running state");
            await WaitFor(() => w.ConsoleView.Text.Contains("Hello"), "\"Hello\" output in the console");
            w.ConsoleView.Submit("Ada");
            await WaitFor(() => w.State == IdeState.Idle, "end of program");
            Check(w.LastResult is { ExitCode: 0, Error: null }, "exit code 0");
            Check(w.ConsoleView.Text.Contains("Hi Ada") && w.ConsoleView.Text.Contains("in p 5"), "complete output: " + Flat(w.ConsoleView.Text));
            Capture(w, Path.Combine(dir, "2-console.png"));

            // ---- 5. Debugger: breakpoint, stepping, call stack, variables
            doc.ToggleBreakpoint(6);
            Check(doc.BreakpointLines.SequenceEqual(new[] { 6 }), "breakpoint at line 6");
            Check(w.Settings.Breakpoints.TryGetValue(path, out var saved) && saved.Contains(6), "breakpoint stored in the settings");
            await w.RunAsync(true);
            Check(w.State == IdeState.Debugging, "Debugging state");
            await WaitFor(() => w.ConsoleView.Text.Contains("Hello"), "output before input");
            w.ConsoleView.Submit("Bob");
            await WaitFor(() => w.State == IdeState.Paused, "paused on the breakpoint");
            Check(doc.ExecutionLine == 6, $"current line 6 ({doc.ExecutionLine})");
            Check(w.Session?.LastPause?.Reason == PauseReason.Breakpoint, "reason: breakpoint");
            Check(w.StackItems.Count == 1, "call stack: 1 frame");
            Check(w.VariableNodes.Any(v => v.Name == "name" && v.Value == "\"Bob\""), "variable name = \"Bob\"");
            var list = w.VariableNodes.FirstOrDefault(v => v.Name == "l");
            Check(list != null && list.Children.Count == 1 && list.Children[0].Name.StartsWith("loading"), "list: children not loaded yet");
            if (list != null)
            {
                list.IsExpanded = true;
                Check(list.Children.Count == 2 && list.Children[1].Value == "\"b\"", "expand: children loaded on demand");
            }
            Capture(w, Path.Combine(dir, "3-debugger.png"));

            await w.StepAsync(StepKind.Into);
            await WaitFor(() => w.State == IdeState.Paused, "paused after step into");
            Check(doc.ExecutionLine == 9, $"step into → line 9 ({doc.ExecutionLine})");
            Check(w.StackItems.Count == 2 && w.StackItems[0].Name == "p", "call stack: p then main program");
            await w.StepAsync(StepKind.Over);
            await WaitFor(() => w.State == IdeState.Paused, "paused after step over");
            Check(doc.ExecutionLine == 10, $"step over → line 10 ({doc.ExecutionLine})");
            Check(w.VariableNodes.Any(v => v.Name == "x" && v.Value == "5"), "local variable x = 5");
            w.SelectFrame(1);
            Check(doc.ExecutionLine == 6 && !doc.ExecutionIsTopFrame, "caller selected → line 6");
            Check(w.VariableNodes.Any(v => v.Name == "name"), "caller variables");
            Check(w.VariableNodes.FirstOrDefault(v => v.Name == "l")?.IsExpanded == true, "expanded state kept between pauses");
            w.SelectFrame(0);
            await w.StepAsync(StepKind.Out);
            await WaitFor(() => w.State == IdeState.Paused, "paused after step out");
            Check(doc.ExecutionLine == 7, $"step out → line 7 ({doc.ExecutionLine})");
            w.CmdDebug.Execute(null); // F5 = continue
            await WaitFor(() => w.State == IdeState.Idle, "end after continue");
            Check(w.LastResult is { ExitCode: 0 }, "program finished");
            Check(doc.ExecutionLine == null, "current line cleared");
            doc.ToggleBreakpoint(6);

            // ---- 6. Runtime error (without and with the debugger)
            var errDoc = w.NewDocument(LanguageDefinitions.Echo, "print before\ncall q\nproc q\n  error division by zero\nend\n");
            await w.RunAsync(false);
            await WaitFor(() => w.State == IdeState.Idle, "end of the failing program");
            Check(w.LastResult?.Error?.Line == 4, "runtime error at line 4");
            Check(w.ErrorItems.Any(e => e.IsRuntime && e.Line == 4), "runtime error in the error list");
            Check(w.ConsoleView.Text.Contains("division by zero"), "error shown in the console");
            w.Settings.BreakOnError = true;
            await w.RunAsync(true);
            await WaitFor(() => w.State == IdeState.Paused, "break on runtime error");
            Check(w.Session?.LastPause?.Reason == PauseReason.Exception, "reason: runtime error");
            Check(errDoc.ExecutionLine == 4, "error line highlighted");
            Check(w.StackItems.Count == 2, "call stack at the time of the error");
            w.StopExecution();
            await WaitFor(() => w.State == IdeState.Idle, "stopped from the pause");

            // ---- 7. Stopping a looping program
            var loopDoc = w.NewDocument(LanguageDefinitions.Echo, "loop 100000\n  print round {i}\n  sleep 5\nend\n");
            await w.RunAsync(false);
            await WaitFor(() => w.ConsoleView.Text.Contains("round 3"), "program is running");
            w.StopExecution();
            await WaitFor(() => w.State == IdeState.Idle, "stopped (Shift+F5)");
            Check(w.LastResult?.Cancelled == true, "result: cancelled");

            // ---- 8. Pause requested while running
            await w.RunAsync(true);
            await WaitFor(() => w.ConsoleView.Text.Contains("round 2"), "debugging in progress");
            w.CmdPause.Execute(null);
            await WaitFor(() => w.State == IdeState.Paused, "pause requested");
            Check(w.Session?.LastPause?.Reason == PauseReason.PauseRequest, "reason: pause requested");
            Check(loopDoc.ExecutionLine is 2 or 3, "current line inside the loop");
            w.StopExecution();
            await WaitFor(() => w.State == IdeState.Idle, "stopped after pause");

            // ---- 9. Standalone exe
            w.Activate(doc);
            await Task.Delay(50);
            var exe = Path.Combine(dir, "test.exe");
            var generated = await w.GenerateExeAsync(exe);
            Check(generated != null && File.Exists(exe), "exe generated");
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
                p.StandardInput.WriteLine("Zoë");
                p.StandardInput.Close();
                bool exited = await Task.Run(() => p.WaitForExit(20000));
                var output = await outTask;
                Check(exited && p.ExitCode == 0, "exe: exit code 0");
                Check(output.Contains("Hi Zoë") && output.Contains("in p 5"), "exe: output " + Flat(output));
            }

            // ---- 10. Editor: Simula / Algol 68
            var sim = w.NewDocument(LanguageDefinitions.Simula, SimulaSample);
            await Task.Delay(450);
            Check(sim.Model.Outline.Select(o => o.Name).SequenceEqual(new[] { "Stack", "push", "pop" }), "Simula outline: " + string.Join(",", sim.Model.Outline.Select(o => o.Name)));
            Check(w.OutlineList.Items.Count == 3, "Outline panel filled");
            Check(sim.Folding.AllFoldings.Count() >= 3, "Simula folding: " + sim.Folding.AllFoldings.Count());
            sim.Editor.CaretOffset = SimulaSample.IndexOf("begin", StringComparison.Ordinal);
            Check(sim.Model.PairOf.Count > 0, "block matching");
            var alg = w.NewDocument(LanguageDefinitions.Algol68, AlgolSample);
            await Task.Delay(450);
            Check(alg.Model.Outline.Select(o => o.Name).SequenceEqual(new[] { "POINT", "fact", "NORM" }), "Algol 68 outline: " + string.Join(",", alg.Model.Outline.Select(o => o.Name)));
            Check(alg.Folding.AllFoldings.Count() >= 2, "Algol 68 folding: " + alg.Folding.AllFoldings.Count());
            Check(alg.Model.Tokens.Any(t => t.Kind == TokenKind.Comment && alg.Model.TokenText(t).Contains("several lines")), "multi-line CO comment");
            // Algol 60 / Simula I
            var a60 = w.NewDocument(LanguageDefinitions.Algol60, Algol60Sample);
            await Task.Delay(450);
            Check(a60.Model.Outline.Select(o => o.Name).SequenceEqual(new[] { "sum", "even" }), "Algol 60 outline: " + string.Join(",", a60.Model.Outline.Select(o => o.Name)));
            Check(a60.Folding.AllFoldings.Count() >= 2, "Algol 60 folding: " + a60.Folding.AllFoldings.Count());
            Check(a60.Model.Tokens.Any(t => t.Kind == TokenKind.Comment && a60.Model.TokenText(t) == "of program"), "Algol 60 end comment");
            var s1 = w.NewDocument(LanguageDefinitions.SimulaI, SimulaISample);
            await Task.Delay(450);
            Check(s1.Model.Outline.Select(o => o.Name).SequenceEqual(new[] { "customer" }) && s1.Model.Outline[0].Kind == OutlineKind.Class,
                "Simula I outline: " + string.Join(",", s1.Model.Outline.Select(o => o.Name)));
            Check(s1.Folding.AllFoldings.Count() >= 2, "Simula I folding: " + s1.Folding.AllFoldings.Count());
            Check(s1.Model.Tokens.Any(t => t.Kind == TokenKind.Builtin && s1.Model.TokenText(t) == "hold"), "Simula I standard procedure hold");
            w.Activate(s1);
            await Task.Delay(300);
            Capture(w, Path.Combine(dir, "3b-simula1-editor.png"));
            w.CloseDocument(a60, force: true);
            w.CloseDocument(s1, force: true);
            // Automatic indentation after BEGIN
            alg.Editor.CaretOffset = alg.Editor.Document.GetLineByNumber(1).EndOffset;
            alg.Editor.TextArea.PerformTextInput("\n");
            var newLine = alg.Editor.Document.GetText(alg.Editor.Document.GetLineByNumber(2));
            Check(newLine == "   ", $"automatic indentation after BEGIN (\"{newLine}\")");
            alg.Editor.Undo();
            // Comment / uncomment
            w.Activate(sim);
            var before = sim.Editor.Text;
            sim.Editor.SelectAll();
            sim.ToggleComment(true);
            Check(sim.Model.Tokens.All(t => t.Kind == TokenKind.Comment), "all lines commented out");
            sim.Editor.SelectAll();
            sim.ToggleComment(false);
            Check(sim.Editor.Text == before, "uncomment restores the text");
            // Find / replace
            w.Search.Text = "integer"; w.Search.Replacement = "INTEGER"; w.Search.MatchCase = true; w.Search.WholeWord = true; w.Search.UseRegex = false;
            sim.Editor.Select(0, 0);
            w.FindNext(false);
            Check(sim.Editor.SelectedText == "integer", "find: occurrence selected");
            var msg = w.ReplaceAll();
            Check(msg.StartsWith("5 "), "replace all: " + msg);
            sim.Editor.Undo();
            Check(sim.Editor.Text == before, "replace all can be undone in one step");
            w.Search.Text = @"OutI\w+"; w.Search.UseRegex = true; w.Search.WholeWord = false;
            sim.Editor.Select(0, 0);
            w.FindNext(false);
            Check(sim.Editor.SelectedText == "OutInt", "regular expression search: " + sim.Editor.SelectedText);
            // Completion
            sim.Editor.CaretOffset = sim.Editor.Document.GetLineByNumber(17).Offset + 3;
            sim.ShowCompletion();
            Info("completion opened");
            await Task.Delay(300);
            sim.CloseCompletion();
            await Task.Delay(100);
            sim.Editor.TextArea.Focus();

            // ---- 10b. Menus (drop-down menu rendering, screen capture)
            w.Topmost = true;
            w.Activate();
            var debugMenu = w.DebugMenuItem.Parent as System.Windows.Controls.MenuItem;
            if (debugMenu != null)
            {
                debugMenu.IsSubmenuOpen = true;
                await Task.Delay(600);
                Check(debugMenu.IsSubmenuOpen, "Debug menu open");
                CaptureFromScreen(w, Path.Combine(dir, "7-menu.png"));
                debugMenu.IsSubmenuOpen = false;
            }
            w.Topmost = false;

            // ---- 11. Dark theme
            w.SetTheme(true);
            w.Activate(alg);
            await Task.Delay(400);
            Capture(w, Path.Combine(dir, "4-dark-theme.png"));
            w.SetTheme(false);

            // ---- 12a. Real compilers: run and debug (warnings, non-blocking)
            await RealCompilerScenario(w, LanguageDefinitions.Simula, RealSimula, "Sum:", 5, "n");
            await RealCompilerScenario(w, LanguageDefinitions.Algol68, RealAlgol, "Sum:", 5, "n");
            await RealCompilerScenario(w, LanguageDefinitions.Algol60, RealAlgol60, "Sum:", 5, "n");
            await RealCompilerScenario(w, LanguageDefinitions.SimulaI, RealSimulaI, "Sum:", 5, "n");

            // ---- 12b. Samples (information)
            foreach (var sample in MainWindow.ListSamples().Take(300))
            {
                var d = w.OpenFile(sample);
                if (d == null) continue;
                var outcome = await w.BuildAsync(showSuccess: false);
                var rel = PathLocator.SamplesDirectory != null ? Path.GetRelativePath(PathLocator.SamplesDirectory, sample) : Path.GetFileName(sample);
                Info($"sample {rel}: " + (outcome != null ? "compiled" :
                    "failed — " + string.Join(" | ", w.ErrorItems.Take(2).Select(e => $"({e.Line},{e.Column}) {e.Message}"))));
                w.CloseDocument(d, force: true);
            }

            // ---- 12c. Help (Markdown rendered in the embedded browser)
            w.CmdHelp.Execute("simula");
            await Task.Delay(2500);
            var help = Application.Current.Windows.OfType<Dialogs.HelpWindow>().FirstOrDefault();
            Check(help != null && help.IsVisible, "help window open");
            if (help != null)
            {
                bool hasDoc = PathLocator.DocsDirectory != null && File.Exists(Path.Combine(PathLocator.DocsDirectory, "simula.md"));
                Check(!hasDoc || help.TocList.Items.Count > 3, $"table of contents of docs/simula.md ({help.TocList.Items.Count} headings)");
                Check(help.Browser.Document != null, "help page loaded");
                CaptureScreen(help, Path.Combine(dir, "6-help.png"));
                help.ShowTopic("shortcuts");
                await Task.Delay(800);
                Check(help.TocList.Items.Count >= 5, "help: keyboard shortcuts");
                foreach (var (topic, file) in new[] { ("algol60", "algol60.md"), ("simula1", "simula1.md") })
                {
                    help.ShowTopic(topic);
                    await Task.Delay(800);
                    bool exists = PathLocator.DocsDirectory != null && File.Exists(Path.Combine(PathLocator.DocsDirectory, file));
                    Check(!exists || help.TocList.Items.Count > 3, $"table of contents of docs/{file} ({help.TocList.Items.Count} headings)");
                }
                help.Close();
            }

            // ---- 13. Session
            w.SaveSession();
            Check(File.Exists(Path.Combine(IdeSettings.Directory, "settings.json")), "settings saved");
        }
        catch (Exception ex)
        {
            Check(false, "exception: " + ex);
        }
        Finish(w, "self-test");
    }

    const string RealSimula =
        "begin\r\n" +                                        // 1
        "   integer i, s;\r\n" +                             // 2
        "   procedure add(n); integer n;\r\n" +              // 3
        "   begin\r\n" +                                     // 4
        "      s := s + n\r\n" +                             // 5
        "   end;\r\n" +                                      // 6
        "   for i := 1 step 1 until 3 do\r\n" +              // 7
        "      add(i);\r\n" +                                // 8
        "   OutText(\"Sum:\"); OutInt(s, 3); OutImage\r\n" + // 9
        "end\r\n";                                           // 10

    const string RealAlgol =
        "BEGIN\r\n" +                                                // 1
        "   INT s := 0;\r\n" +                                       // 2
        "   PROC add = (INT n) VOID:\r\n" +                          // 3
        "   BEGIN\r\n" +                                             // 4
        "      s +:= n\r\n" +                                        // 5
        "   END;\r\n" +                                              // 6
        "   FOR i TO 3 DO add(i) OD;\r\n" +                          // 7
        "   print((\"Sum:\", whole(s, 3), newline))\r\n" +           // 8
        "END\r\n";                                                   // 9

    const string RealAlgol60 =
        "begin\r\n" +                                                                // 1
        "   integer i, s;\r\n" +                                                     // 2
        "   procedure add(n); value n; integer n;\r\n" +                             // 3
        "   begin\r\n" +                                                             // 4
        "      s := s + n\r\n" +                                                     // 5
        "   end;\r\n" +                                                              // 6
        "   s := 0;\r\n" +                                                           // 7
        "   for i := 1 step 1 until 3 do add(i);\r\n" +                              // 8
        "   outstring(1, \"Sum:\"); outinteger(1, s); outstring(1, \"\\n\")\r\n" +   // 9
        "end\r\n";                                                                   // 10

    const string RealSimulaI =
        "SIMULA begin\r\n" +                                                         // 1
        "   integer s;\r\n" +                                                        // 2
        "   procedure add(n); value n; integer n;\r\n" +                             // 3
        "   begin\r\n" +                                                             // 4
        "      s := s + n\r\n" +                                                     // 5
        "   end;\r\n" +                                                              // 6
        "   activity adder(k); integer k;\r\n" +                                     // 7
        "   begin\r\n" +                                                             // 8
        "      hold(k); add(k)\r\n" +                                                // 9
        "   end of adder;\r\n" +                                                     // 10
        "   s := 0;\r\n" +                                                           // 11
        "   activate new adder(1); activate new adder(2); activate new adder(3);\r\n" + // 12
        "   hold(10);\r\n" +                                                         // 13
        "   outstring(1, \"Sum:\"); outinteger(1, s); outstring(1, \"\\n\")\r\n" +   // 14
        "end of simulation model\r\n";                                               // 15

    static int _realWarnings;

    /// <summary>"Real compiler" warning: logged, but does not make the self-test fail.</summary>
    static void Real(bool condition, string message)
    {
        Lines.Add((condition ? "ok     " : "WARNING ") + "[real compiler] " + message);
        if (!condition) _realWarnings++;
    }

    static async Task<bool> RealWait(Func<bool> cond, string what, int timeoutMs = 15000)
    {
        var sw = Stopwatch.StartNew();
        while (!cond())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) { Real(false, "timed out: " + what); return false; }
            await Task.Delay(25);
        }
        return true;
    }

    static async Task RealCompilerScenario(MainWindow w, LanguageDefinition lang, string source, string expected, int bpLine, string localVar)
    {
        var doc = w.NewDocument(lang, source);
        var path = Path.Combine(OutDir, "real" + lang.DefaultExtension);
        w.SaveDocumentAs(doc, path);
        var outcome = await w.BuildAsync(showSuccess: false);
        if (outcome == null)
        {
            Real(false, $"{lang.DisplayName}: compilation failed — " +
                string.Join(" | ", w.ErrorItems.Take(3).Select(e => $"({e.Line},{e.Column}) {e.Message}")));
            w.CloseDocument(doc, force: true);
            return;
        }
        Real(true, $"{lang.DisplayName}: compilation succeeded");
        // Run in the integrated console
        await w.RunAsync(false);
        if (await RealWait(() => w.State == IdeState.Idle, $"{lang.DisplayName}: end of run"))
            Real(w.LastResult is { Error: null, ExitCode: 0 } && w.ConsoleView.Text.Contains(expected),
                $"{lang.DisplayName}: run, output \"{Flat(w.ConsoleView.Text)}\"");
        else { w.StopExecution(); await RealWait(() => w.State == IdeState.Idle, "stop"); }
        // Debugger
        doc.ToggleBreakpoint(bpLine);
        await w.RunAsync(true);
        if (await RealWait(() => w.State is IdeState.Paused or IdeState.Idle, $"{lang.DisplayName}: stop on the breakpoint") && w.State == IdeState.Paused)
        {
            Real(doc.ExecutionLine == bpLine, $"{lang.DisplayName}: current line {doc.ExecutionLine} (expected {bpLine})");
            Real(w.StackItems.Count >= 2, $"{lang.DisplayName}: call stack ({string.Join(" ← ", w.StackItems.Select(s => $"{s.Name}:{s.LineText}"))})");
            Real(w.VariableNodes.Any(v => v.Name.Equals(localVar, StringComparison.OrdinalIgnoreCase)),
                $"{lang.DisplayName}: variables ({string.Join(", ", w.VariableNodes.Select(v => $"{v.Name}={v.Value}"))})");
            Capture(w, Path.Combine(OutDir, $"5-debugger-{lang.Id}.png"));
            await w.StepAsync(StepKind.Over);
            if (await RealWait(() => w.State is IdeState.Paused or IdeState.Idle, $"{lang.DisplayName}: step over"))
                Real(w.State == IdeState.Paused, $"{lang.DisplayName}: step over → line {doc.ExecutionLine}");
            doc.ToggleBreakpoint(bpLine); // remove the breakpoint while running
            w.CmdDebug.Execute(null);
            if (await RealWait(() => w.State == IdeState.Idle, $"{lang.DisplayName}: end after continue"))
                Real(w.ConsoleView.Text.Contains(expected), $"{lang.DisplayName}: output after debugging");
        }
        else Real(false, $"{lang.DisplayName}: the debugger did not stop at line {bpLine}");
        if (w.State != IdeState.Idle) { w.StopExecution(); await RealWait(() => w.State == IdeState.Idle, "stop"); }
        if (doc.BreakpointLines.Count > 0) doc.SetBreakpoints(Array.Empty<int>());
        // Standalone exe
        var exe = Path.Combine(OutDir, "real-" + lang.Id + ".exe");
        if (await w.GenerateExeAsync(exe) != null)
        {
            var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
            using var p = Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync();
            await Task.Run(() => p.WaitForExit(20000));
            Real(output.Contains(expected) && p.ExitCode == 0, $"{lang.DisplayName}: standalone exe, output \"{Flat(output)}\"");
        }
        else Real(false, $"{lang.DisplayName}: exe generation");
        w.CloseDocument(doc, force: true);
    }

    static string Flat(string s) => s.Replace("\r", "").Replace("\n", " ⏎ ").Trim();
}
