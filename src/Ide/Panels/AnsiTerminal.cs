// Small ANSI/VT terminal emulator used by the integrated console.
// It has no WPF dependency (unit-tested by tests/IdeTests): the text lives in an ITerminalBuffer
// (the AvalonEdit document in the IDE, a list of lines in the tests).

using System.Text;

namespace Studio.Ide.Panels;

/// <summary>
/// Line-oriented text storage driven by <see cref="AnsiTerminal"/>. Lines and columns are 0-based;
/// there is always at least one line (the last one has no terminator).
/// </summary>
public interface ITerminalBuffer
{
    int LineCount { get; }
    /// <summary>Length of a line, without its terminator.</summary>
    int LineLength(int line);
    /// <summary>Characters [column, column + length) of a line (within the line).</summary>
    string GetText(int line, int column, int length);
    /// <summary>Appends text at the end of the buffer (it may contain '\n').</summary>
    void Append(string text);
    /// <summary>Replaces [column, column + length) of a line (within the line) with text without '\n'.</summary>
    void Replace(int line, int column, int length, string text);
    /// <summary>Removes everything from (line, column) to the end of the buffer.</summary>
    void DeleteToEnd(int line, int column);
    /// <summary>
    /// Sets the foreground colour of the <paramref name="length"/> characters starting at (line, column),
    /// possibly spanning lines (line terminators count as one character). Colour -1 = default, 0..15 = ANSI palette.
    /// </summary>
    void SetForeground(int line, int column, int length, int color);
}

/// <summary>
/// Interprets the program output: plain text, '\n', '\r', '\b' and a subset of the ANSI/VT escape
/// sequences (cursor positioning, erase, SGR colours). The "screen" starts at the origin line:
/// the line where the program output started, kept by a full clear (ESC[2J). Writing after a
/// cursor move overwrites the text in place; when the cursor is at the end of the buffer, text
/// is simply appended (the behaviour of a program that uses no escape sequence is unchanged).
/// Sequences may be split across successive <see cref="Write"/> calls.
/// </summary>
public sealed class AnsiTerminal
{
    enum State { Normal, Escape, Charset, Csi, Osc, OscEscape }

    const int MaxParamLength = 64;

    readonly ITerminalBuffer _buffer;
    readonly StringBuilder _run = new();
    readonly StringBuilder _params = new();
    bool _runAppends;
    State _state;
    int _origin = -1;               // buffer line of screen row 1 (-1 = not yet known)
    int _line, _column;             // cursor (absolute buffer line, may be past the last line)
    int _savedRow, _savedColumn;    // ESC 7 / ESC[s (relative to the origin)
    bool _bold;

    public AnsiTerminal(ITerminalBuffer buffer) { _buffer = buffer; }

    /// <summary>Current foreground colour (-1 = default, 0..15 = ANSI palette).</summary>
    public int Foreground { get; private set; } = -1;
    /// <summary>ESC[?25l / ESC[?25h (informative only: the console shows no cursor).</summary>
    public bool CursorVisible { get; private set; } = true;
    /// <summary>Buffer line of the screen origin (-1 before the first output).</summary>
    public int OriginLine => _origin;
    public int CursorLine => _line;
    public int CursorColumn => _column;
    /// <summary>The program positions the cursor (ESC[H, ESC[row;colH...): it draws a screen,
    /// so the console should show its lines unwrapped.</summary>
    public bool ScreenMode { get; private set; }

    /// <summary>New program: forgets the screen, the parser state and the attributes.</summary>
    public void Reset()
    {
        ResetScreen();
        _state = State.Normal;
        _params.Clear();
        _run.Clear();
        Foreground = -1;
        _bold = false;
        CursorVisible = true;
        ScreenMode = false;
        _savedRow = _savedColumn = 0;
    }

    /// <summary>The buffer was emptied (or replaced): the next output defines a new origin.</summary>
    public void ResetScreen()
    {
        _origin = -1;
        _line = _column = 0;
    }

    /// <summary>Non-program text is about to be appended: the cursor goes to the end of the buffer.</summary>
    public void MoveToEnd()
    {
        _line = _buffer.LineCount - 1;
        _column = _buffer.LineLength(_line);
    }

    /// <summary>The first <paramref name="count"/> lines of the buffer were removed (size limit).</summary>
    public void LinesRemoved(int count)
    {
        if (count <= 0) return;
        if (_origin >= 0) _origin = Math.Max(0, _origin - count);
        _line = Math.Max(0, _line - count);
    }

    bool AtEnd
    {
        get
        {
            int last = _buffer.LineCount - 1;
            return _line == last && _column == _buffer.LineLength(last);
        }
    }

    /// <summary>Program output.</summary>
    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (_origin < 0)
        {
            MoveToEnd();
            _origin = _line;
        }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            switch (_state)
            {
                case State.Normal:
                    if (c == '\x1b') { FlushRun(); _state = State.Escape; }
                    else if (c == '\n')
                    {
                        if (_run.Length == 0) _runAppends = AtEnd;
                        if (_runAppends) _run.Append('\n');
                        else NewLine();
                    }
                    else if (c == '\r')
                    {
                        // "\r\n" = new line (keeps the fast append path)
                        if (i + 1 < text.Length && text[i + 1] == '\n') continue;
                        FlushRun();
                        _column = 0;
                    }
                    else if (c == '\b') { FlushRun(); if (_column > 0) _column--; }
                    else
                    {
                        if (_run.Length == 0) _runAppends = AtEnd;
                        _run.Append(c);
                    }
                    break;
                case State.Escape:
                    _state = State.Normal;
                    if (c == '[') { _params.Clear(); _state = State.Csi; }
                    else if (c == ']') _state = State.Osc;
                    else if (c == '7') SaveCursor();
                    else if (c == '8') RestoreCursor();
                    else if (c == 'c') { ClearScreen(); Home(); Foreground = -1; _bold = false; }
                    else if (c is '(' or ')' or '*' or '+' or '#' or '%') _state = State.Charset;
                    else if (c == '\x1b') _state = State.Escape;
                    // other two-character sequences (ESC =, ESC >, ...) are ignored
                    break;
                case State.Charset:
                    // ESC ( B and the like: character set selection, ignored
                    _state = State.Normal;
                    break;
                case State.Csi:
                    if (c >= 0x40 && c <= 0x7e) { _state = State.Normal; Dispatch(c); }
                    else if (c >= 0x20 && c <= 0x3f)
                    {
                        if (_params.Length < MaxParamLength) _params.Append(c);
                    }
                    else
                    {
                        // malformed sequence: abandon it and process the character normally
                        _state = State.Normal;
                        i--;
                    }
                    break;
                case State.Osc:
                    // ESC ] ... (BEL | ESC \): window title and the like, ignored
                    if (c == '\a') _state = State.Normal;
                    else if (c == '\x1b') _state = State.OscEscape;
                    break;
                case State.OscEscape:
                    _state = c == '\\' ? State.Normal : State.Osc;
                    break;
            }
        }
        FlushRun();
    }

    void FlushRun()
    {
        if (_run.Length == 0) return;
        var s = _run.ToString();
        _run.Clear();
        if (_runAppends)
        {
            // At the end of the buffer: plain append.
            int line = _buffer.LineCount - 1, col = _buffer.LineLength(line);
            _buffer.Append(s);
            if (Foreground >= 0) _buffer.SetForeground(line, col, s.Length, Foreground);
            _line = _buffer.LineCount - 1;
            _column = _buffer.LineLength(_line);
        }
        else
        {
            Put(s);
        }
    }

    /// <summary>Writes text without '\n' at the cursor, overwriting what is there.</summary>
    void Put(string s)
    {
        EnsureLine(_line);
        int len = _buffer.LineLength(_line);
        int col = _column;
        if (col >= len)
        {
            _buffer.Replace(_line, len, 0, (col > len ? new string(' ', col - len) : "") + s);
        }
        else
        {
            int overlap = Math.Min(s.Length, len - col);
            var existing = _buffer.GetText(_line, col, overlap);
            int first = 0;
            while (first < overlap && existing[first] == s[first]) first++;
            if (s.Length > overlap)
            {
                // the text goes past the end of the line: replace from the first difference to the end
                _buffer.Replace(_line, col + first, overlap - first, s.Substring(first));
            }
            else if (first < overlap)
            {
                int last = overlap - 1;
                while (existing[last] == s[last]) last--;
                _buffer.Replace(_line, col + first, last - first + 1, s.Substring(first, last - first + 1));
            }
        }
        _buffer.SetForeground(_line, col, s.Length, Foreground);
        _column = col + s.Length;
    }

    void EnsureLine(int line)
    {
        int missing = line - (_buffer.LineCount - 1);
        if (missing > 0) _buffer.Append(new string('\n', missing));
    }

    void NewLine()
    {
        FlushRun();
        if (_line >= _buffer.LineCount - 1)
        {
            EnsureLine(_line);
            _buffer.Append("\n");
        }
        _line++;
        _column = 0;
    }

    int Origin => _origin < 0 ? 0 : _origin;

    void Home() { _line = Origin; _column = 0; }

    void SaveCursor() { _savedRow = _line - Origin; _savedColumn = _column; }

    void RestoreCursor() { _line = Origin + Math.Max(0, _savedRow); _column = _savedColumn; }

    /// <summary>ESC[2J: removes the whole screen (from the origin line); the cursor does not move.</summary>
    void ClearScreen()
    {
        int o = Origin;
        if (o < _buffer.LineCount) _buffer.DeleteToEnd(o, 0);
    }

    void Dispatch(char final)
    {
        var p = _params.ToString();
        if (p.StartsWith('?'))
        {
            // private modes: ?25 = cursor visibility; the others (?1049 alternate screen, ?7 wrap...) are accepted and ignored
            if ((final == 'h' || final == 'l') && p.Substring(1).Split(';').Contains("25")) CursorVisible = final == 'h';
            return;
        }
        if (p.Length > 0 && (p[0] == '>' || p[0] == '=' || p[0] == '<')) return;
        var args = ParseParams(p);
        int Arg(int index, int def) => index < args.Count && args[index] > 0 ? args[index] : def;
        switch (final)
        {
            case 'H':
            case 'f':
                ScreenMode = true;
                _line = Origin + Arg(0, 1) - 1;
                _column = Arg(1, 1) - 1;
                break;
            case 'A': _line = Math.Max(Origin, _line - Arg(0, 1)); break;
            case 'B': _line += Arg(0, 1); break;
            case 'C': _column += Arg(0, 1); break;
            case 'D': _column = Math.Max(0, _column - Arg(0, 1)); break;
            case 'E': _line += Arg(0, 1); _column = 0; break;
            case 'F': _line = Math.Max(Origin, _line - Arg(0, 1)); _column = 0; break;
            case 'G': _column = Arg(0, 1) - 1; break;
            case 'd': _line = Origin + Arg(0, 1) - 1; break;
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
            case 'J': EraseInDisplay(args.Count > 0 ? args[0] : 0); break;
            case 'K': EraseInLine(args.Count > 0 ? args[0] : 0); break;
            case 'm': Sgr(args); break;
            // anything else (scroll regions, insert/delete...) is ignored
        }
    }

    static List<int> ParseParams(string p)
    {
        var list = new List<int>();
        if (p.Length == 0) return list;
        foreach (var part in p.Split(';', ':'))
            list.Add(int.TryParse(part, out var n) ? Math.Min(n, 9999) : 0);
        return list;
    }

    void EraseInDisplay(int mode)
    {
        switch (mode)
        {
            case 0: // cursor to end of screen
                if (_line < _buffer.LineCount)
                {
                    int len = _buffer.LineLength(_line);
                    if (_column < len) _buffer.DeleteToEnd(_line, _column);
                    else if (_line < _buffer.LineCount - 1) _buffer.DeleteToEnd(_line, len);
                }
                break;
            case 1: // start of screen to cursor
                for (int l = Origin; l < _line && l < _buffer.LineCount; l++)
                {
                    int len = _buffer.LineLength(l);
                    if (len > 0) { _buffer.Replace(l, 0, len, ""); }
                }
                BlankLineStart();
                break;
            case 2:
                ClearScreen();
                break;
            // 3 = scrollback: ignored
        }
    }

    void EraseInLine(int mode)
    {
        if (_line >= _buffer.LineCount) return;
        int len = _buffer.LineLength(_line);
        switch (mode)
        {
            case 0:
                if (_column < len) _buffer.Replace(_line, _column, len - _column, "");
                break;
            case 1:
                BlankLineStart();
                break;
            case 2:
                if (len > 0) _buffer.Replace(_line, 0, len, "");
                break;
        }
    }

    /// <summary>Blanks the characters of the cursor line up to and including the cursor.</summary>
    void BlankLineStart()
    {
        if (_line >= _buffer.LineCount) return;
        int len = _buffer.LineLength(_line);
        int n = Math.Min(len, _column + 1);
        if (n <= 0) return;
        if (n == len) _buffer.Replace(_line, 0, n, "");
        else
        {
            _buffer.Replace(_line, 0, n, new string(' ', n));
            _buffer.SetForeground(_line, 0, n, -1);
        }
    }

    void Sgr(List<int> args)
    {
        if (args.Count == 0) { Foreground = -1; _bold = false; return; }
        for (int i = 0; i < args.Count; i++)
        {
            int a = args[i];
            switch (a)
            {
                case 0: Foreground = -1; _bold = false; break;
                case 1: _bold = true; if (Foreground is >= 0 and < 8) Foreground += 8; break;
                case 22: _bold = false; break;
                case 39: Foreground = -1; break;
                case >= 30 and <= 37: Foreground = a - 30 + (_bold ? 8 : 0); break;
                case >= 90 and <= 97: Foreground = a - 90 + 8; break;
                case 38:
                case 48:
                    // extended colour: 38;5;n or 38;2;r;g;b (approximated for the foreground)
                    if (i + 1 < args.Count && args[i + 1] == 5)
                    {
                        if (a == 38 && i + 2 < args.Count) Foreground = args[i + 2] < 16 ? args[i + 2] : -1;
                        i += 2;
                    }
                    else if (i + 1 < args.Count && args[i + 1] == 2)
                    {
                        if (a == 38 && i + 4 < args.Count) Foreground = Nearest(args[i + 2], args[i + 3], args[i + 4]);
                        i += 4;
                    }
                    break;
                // background colours, underline, inverse... are accepted and ignored
            }
        }
    }

    static int Nearest(int r, int g, int b)
    {
        int best = -1, bestDist = int.MaxValue;
        for (int k = 0; k < 16; k++)
        {
            var (pr, pg, pb) = ReferencePalette[k];
            int d = (pr - r) * (pr - r) + (pg - g) * (pg - g) + (pb - b) * (pb - b);
            if (d < bestDist) { bestDist = d; best = k; }
        }
        return best;
    }

    static readonly (int R, int G, int B)[] ReferencePalette =
    {
        (0, 0, 0), (205, 49, 49), (13, 188, 121), (229, 229, 16), (36, 114, 200), (188, 63, 188), (17, 168, 205), (229, 229, 229),
        (102, 102, 102), (241, 76, 76), (35, 209, 139), (245, 245, 67), (59, 142, 234), (214, 112, 214), (41, 184, 219), (255, 255, 255),
    };
}

/// <summary>In-memory <see cref="ITerminalBuffer"/> (tests, headless use).</summary>
public sealed class StringTerminalBuffer : ITerminalBuffer
{
    readonly List<StringBuilder> _lines = new() { new StringBuilder() };
    readonly List<List<int>> _colors = new() { new List<int>() };

    public int LineCount => _lines.Count;
    public int LineLength(int line) => _lines[line].Length;
    public string GetText(int line, int column, int length) => _lines[line].ToString(column, length);
    /// <summary>Number of Replace/Append/DeleteToEnd calls (the tests check that redraws are minimal).</summary>
    public int Edits { get; private set; }

    public void Append(string text)
    {
        Edits++;
        foreach (var c in text)
        {
            if (c == '\n') { _lines.Add(new StringBuilder()); _colors.Add(new List<int>()); }
            else { _lines[^1].Append(c); _colors[^1].Add(-1); }
        }
    }

    public void Replace(int line, int column, int length, string text)
    {
        Edits++;
        if (column < 0 || length < 0 || column + length > _lines[line].Length || text.Contains('\n'))
            throw new ArgumentOutOfRangeException(nameof(column));
        _lines[line].Remove(column, length).Insert(column, text);
        _colors[line].RemoveRange(column, length);
        _colors[line].InsertRange(column, Enumerable.Repeat(-1, text.Length));
    }

    public void DeleteToEnd(int line, int column)
    {
        Edits++;
        _lines[line].Length = column;
        _colors[line].RemoveRange(column, _colors[line].Count - column);
        _lines.RemoveRange(line + 1, _lines.Count - line - 1);
        _colors.RemoveRange(line + 1, _colors.Count - line - 1);
    }

    public void SetForeground(int line, int column, int length, int color)
    {
        while (length > 0 && line < _lines.Count)
        {
            if (column < _colors[line].Count) _colors[line][column] = color;
            column++;
            length--;
            if (column > _colors[line].Count) { line++; column = 0; }
        }
    }

    /// <summary>Removes the first lines (like the size limit of the console).</summary>
    public void RemoveFirstLines(int count)
    {
        count = Math.Min(count, _lines.Count - 1);
        _lines.RemoveRange(0, count);
        _colors.RemoveRange(0, count);
    }

    public int ColorAt(int line, int column) => _colors[line][column];

    public string Line(int line) => _lines[line].ToString();

    public override string ToString() => string.Join("\n", _lines);
}
