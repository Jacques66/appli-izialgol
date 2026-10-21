// Tests of the ANSI/VT terminal emulator of the IDE console (src/Ide/Panels/AnsiTerminal.cs).

using Studio.Common;
using Studio.Ide.Panels;
using Studio.Tooling;

namespace Studio.IdeTests;

static partial class Program
{
    const string Esc = "\u001b";

    static (AnsiTerminal Term, StringTerminalBuffer Buf) NewTerminal()
    {
        var buf = new StringTerminalBuffer();
        return (new AnsiTerminal(buf), buf);
    }

    static void AnsiPlainText()
    {
        var (t, b) = NewTerminal();
        t.Write("Hello\nWorld\r\nLast");
        Eq("Hello\nWorld\nLast", b.ToString(), "plain text");
        t.Write("\rXY");
        Eq("Hello\nWorld\nXYst", b.ToString(), "carriage return overwrites");
        t.Write("\b\bZ");
        Eq("Hello\nWorld\nZYst", b.ToString(), "backspace");
        t.Write("\n\ttab\f");
        Eq("Hello\nWorld\nZYst\n\ttab\f", b.ToString(), "other control characters kept as before");
        // '\r' and '\n' in separate writes
        t.Write("\r");
        t.Write("\nend\n");
        Eq("Hello\nWorld\nZYst\n\ttab\f\nend\n", b.ToString(), "split CR LF");
    }

    static void AnsiSplitSequences()
    {
        var (t, b) = NewTerminal();
        t.Write("abc\ndef\n");
        foreach (var part in new[] { Esc, "[", "2;", "3", "H" }) t.Write(part);
        t.Write("x");
        Eq("abc\ndex\n", b.ToString(), "ESC[2;3H split in five writes");
        t.Write(Esc + "[3");
        t.Write("1mR" + Esc);
        Eq(1, t.Foreground, "split SGR");
        t.Write("[0m");
        Eq(-1, t.Foreground, "SGR reset");
        Eq("abc\ndexR\n", b.ToString(), "text unchanged by SGR");
        t.Write(Esc);
        t.Write("[");
        t.Write("1;1H*");
        Eq("*bc\ndexR\n", b.ToString(), "ESC and [ in separate writes");
    }

    static void AnsiCursorAndOverwrite()
    {
        var (t, b) = NewTerminal();
        t.Write("12345\n67890\n");
        t.Write(Esc + "[1;3Hab");
        Eq("12ab5\n67890\n", b.ToString(), "overwrite at row 1, column 3");
        t.Write(Esc + "[5;4Hz");
        Eq("12ab5\n67890\n\n\n   z", b.ToString(), "lines and spaces created as needed");
        t.Write(Esc + "[2;4HXYZW");
        Eq("12ab5\n678XYZW\n\n\n   z", b.ToString(), "line extended past its end");
        t.Write(Esc + "[HQ");
        Eq("Q2ab5\n678XYZW\n\n\n   z", b.ToString(), "ESC[H = home");
        t.Write(Esc + "[2;1f" + Esc + "[2C+" + Esc + "[A-" + Esc + "[B" + Esc + "[2D#");
        Eq("Q2a-5\n67#XYZW\n\n\n   z", b.ToString(), "ESC[f, A, B, C, D");
        t.Write(Esc + "[10A!");
        Eq("Q2a!5\n67#XYZW\n\n\n   z", b.ToString(), "cursor up stops at the first row");
        t.Write("\n>");
        Eq("Q2a!5\n>7#XYZW\n\n\n   z", b.ToString(), "\\n moves to the next line, column 1, without erasing");
        t.Write(Esc + "[5;1H" + Esc + "[4G" + "Z\nnew");
        Eq("Q2a!5\n>7#XYZW\n\n\n   Z\nnew", b.ToString(), "ESC[G and new line at the end");
        Eq(5, t.CursorLine, "cursor line");
        Eq(3, t.CursorColumn, "cursor column");
        t.Write(Esc + "[s" + Esc + "[1;1H@" + Esc + "[uX");
        Eq("@2a!5\n>7#XYZW\n\n\n   Z\nnewX", b.ToString(), "save / restore cursor");
    }

    static void AnsiClearAndRedraw()
    {
        var (t, b) = NewTerminal();
        b.Append("[Running prog]\n");   // text before the program output (IDE information line)
        for (int i = 1; i <= 20; i++)
            t.Write(Esc + "[2J" + Esc + "[H" + $"Counter: {i}\n" + "second line\n");
        Eq("[Running prog]\nCounter: 20\nsecond line\n", b.ToString(), "clear + home: the screen is redrawn in place");
        Eq(1, t.OriginLine, "origin = start of the program output");
        for (int i = 1; i <= 20; i++)
            t.Write(Esc + "[H" + $"Count {i}");
        Eq("[Running prog]\nCount 20 20\nsecond line\n", b.ToString(), "home only: overwrite without erasing");
        t.Write(Esc + "[H" + Esc + "[J" + "top");
        Eq("[Running prog]\ntop", b.ToString(), "ESC[J clears to the end of the screen");
        t.Write(Esc + "[2J" + "x");
        Eq("[Running prog]\n   x", b.ToString(), "ESC[2J does not move the cursor");
        t.Write(Esc + "[3J");
        Eq("[Running prog]\n   x", b.ToString(), "ESC[3J ignored");
        t.Write(Esc + "c" + "reset");
        Eq("[Running prog]\nreset", b.ToString(), "ESC c: clear and home");
    }

    static void AnsiErase()
    {
        var (t, b) = NewTerminal();
        t.Write("abcdef" + Esc + "[1;3H" + Esc + "[K");
        Eq("ab", b.ToString(), "ESC[K");
        t.Write(Esc + "[1;5H" + Esc + "[0K" + "Z");
        Eq("ab  Z", b.ToString(), "ESC[0K after the end of the line, then padding");
        t.Write("\n111\n222\n333" + Esc + "[3;2H" + Esc + "[0J");
        Eq("ab  Z\n111\n2", b.ToString(), "ESC[0J");
        t.Write(Esc + "[2;2H" + Esc + "[1K");
        Eq("ab  Z\n  1\n2", b.ToString(), "ESC[1K blanks up to the cursor");
        t.Write(Esc + "[2K");
        Eq("ab  Z\n\n2", b.ToString(), "ESC[2K");
        t.Write(Esc + "[3;1H" + Esc + "[1J");
        Eq("\n\n", b.ToString(), "ESC[1J");
    }

    static void AnsiSgrAndIgnored()
    {
        var (t, b) = NewTerminal();
        t.Write(Esc + "[31mred" + Esc + "[0mplain" + Esc + "[1;32mG" + Esc + "[m.");
        Eq("redplainG.", b.ToString(), "SGR sequences removed from the text");
        Eq(1, b.ColorAt(0, 0), "red");
        Eq(-1, b.ColorAt(0, 3), "default after reset");
        Eq(10, b.ColorAt(0, 8), "bold green = bright green");
        Eq(-1, b.ColorAt(0, 9), "ESC[m resets");
        t.Write(Esc + "[94m" + Esc + "[1;1HR" + Esc + "[39m");
        Eq(12, b.ColorAt(0, 0), "overwrite recolours (bright blue)");
        Eq(1, b.ColorAt(0, 1), "neighbour unchanged (red)");
        t.Write(Esc + "[38;5;200;4m" + Esc + "[45m" + Esc + "[2;1H" + "x" + Esc + "[0m");
        Eq(-1, b.ColorAt(1, 0), "256-colour index above 15 and background ignored");
        t.Write(Esc + "[38;2;255;80;80m" + "y" + Esc + "[0m");
        Eq(9, b.ColorAt(1, 1), "true colour approximated");
        Check(t.CursorVisible, "cursor visible by default");
        t.Write(Esc + "[?25l");
        Check(!t.CursorVisible, "ESC[?25l hides the cursor");
        t.Write(Esc + "[?25h" + Esc + "[?1049h" + Esc + "]0;window title\a" + Esc + "]2;other" + Esc + "\\" + Esc + "(B" + Esc + "[5n" + Esc + "[>c" + "z");
        Check(t.CursorVisible, "ESC[?25h shows the cursor");
        Eq("RedplainG.\nxyz", b.ToString(), "OSC, charset, private modes and unknown CSI ignored");
        t.Write(Esc + "[12\n");
        Eq("RedplainG.\nxyz\n", b.ToString(), "malformed sequence abandoned at a control character");
    }

    static void AnsiOriginAndEdits()
    {
        var (t, b) = NewTerminal();
        string Frame(char c) => Esc + "[H" + new string(c, 110) + "\n" + new string('-', 110) + "\n" + new string(c, 110) + "\n";
        t.Write(Frame('a'));
        int edits = b.Edits;
        t.Write(Frame('a'));
        Eq(edits, b.Edits, "identical frame: no edit");
        t.Write(Esc + "[H" + new string('a', 50) + "b" + new string('a', 59));
        Eq(edits + 1, b.Edits, "one character changed: one edit");
        Eq(new string('a', 50) + "b" + new string('a', 59), b.Line(0), "changed line");
        // Text appended outside the terminal (input echo, messages): the cursor goes to the end
        b.Append("typed\n");
        t.MoveToEnd();
        t.Write("after");
        Eq("typed", b.Line(3), "input echo kept");
        Eq("after", b.Line(4), "output continues after the echo");
        // Size limit: lines removed at the start
        var (t2, b2) = NewTerminal();
        b2.Append("x\ny\n");
        t2.Write("A\nB\n");
        Eq(2, t2.OriginLine, "origin after the previous text");
        b2.RemoveFirstLines(1);
        t2.LinesRemoved(1);
        t2.Write(Esc + "[HZ");
        Eq("y\nZ\nB\n", b2.ToString(), "origin follows the removed lines");
        // Reset (new run): the next output defines a new origin
        t2.Write(Esc + "[31m");
        t2.Reset();
        Eq(-1, t2.Foreground, "attributes reset");
        t2.Write("run 2" + Esc + "[H!");
        Eq("y\nZ\nB\n!un 2", b2.ToString(), "new origin for the new run");
        // Speed: 200 frames of 30 lines x 110 columns
        var (t3, b3) = NewTerminal();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int f = 0; f < 200; f++)
        {
            var sb = new System.Text.StringBuilder(Esc + "[H");
            for (int r = 0; r < 30; r++) sb.Append(new string((char)('a' + (f + r) % 26), 110)).Append('\n');
            t3.Write(sb.ToString());
        }
        Eq(31, b3.LineCount, "30 lines + last empty line");
        Check(sw.ElapsedMilliseconds < 2000, $"200 frames in {sw.ElapsedMilliseconds} ms");
    }

    // samples/simula67/22_crossroads.sim, run without the pause between frames and fed to the
    // terminal model: every frame must overwrite the previous one, leaving one screen and the statistics.
    static void AnsiCrossroadsSample()
    {
        var root = FindUp("samples");
        Check(root != null, "samples folder not found");
        var path = Path.Combine(root!, "simula67", "22_crossroads.sim");
        var src = File.ReadAllText(path);
        Check(src.Contains("framedelay := 0.2;"), "the sample sets framedelay := 0.2");
        src = src.Replace("framedelay := 0.2;", "framedelay := 0.0;");
        var lang = LanguageRegistry.FindByExtension(".sim")!;
        var r = lang.Compile(new SourceFile(path, src), new CompileOptions());
        Check(r.Success, "compilation: " + string.Join(" | ", r.Diagnostics.Take(3)));
        var io = new FakeIO(new string?[] { null });
        var res = ProgramHost.RunOnLargeStack(r.Program!.CreateExecution(io), new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
        Check(res.Error == null, "runtime error: " + res.Error);
        var (t, b) = NewTerminal();
        t.Write(io.Output);
        var screen = b.ToString();
        Eq(1, CountOf(screen, "CROSSROADS"), "one title: the frames are redrawn in place");
        Eq(1, CountOf(screen, "PID NAME"), "one process table");
        Check(screen.Contains("tick 200 / 200"), "last frame shown");
        Check(screen.Contains("Simulation finished at tick 200"), "statistics after the last frame");
        Check(screen.Contains("[") && screen.Contains("]"), "traffic lights drawn");
        Check(!screen.Contains(Esc), "no escape character left on the screen");
        Check(t.ScreenMode, "cursor positioning switches the console to screen mode (no wrapping)");
    }

    static int CountOf(string s, string what)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
        return n;
    }
}
