// "echo" test language: a fake ILanguageService used to check the IDE, the Runner and
// exe generation without the real compilers. One statement per line:
//
//   # comment                        print text {variable}       read variable
//   set variable = value             add variable number         list variable = a, b, c
//   proc name ... end                call name                   loop n ... end
//   include file.echo                sleep milliseconds          error message
//   exit code
//
// It supports full debugging (breakpoints, stepping, call stack, variables).

using System.Text;
using System.Text.Json;
using Studio.Common;

namespace Studio.Tooling.Testing;

public sealed class EchoLanguage : ILanguageService
{
    public const string LanguageId = "echo";
    public string Id => LanguageId;
    public string DisplayName => "Echo (test language)";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".echo" };

    public CompilationResult Compile(SourceFile main, CompileOptions options)
    {
        var parser = new EchoParser(options.IncludeResolver);
        var body = parser.ParseFile(main.Path, main.Text);
        parser.CheckCalls();
        bool ok = !parser.Diagnostics.Any(d => d.Severity == Severity.Error);
        return new CompilationResult(parser.Diagnostics,
            ok ? new EchoProgram(main.Path, body, parser.Procs, parser.Sources) : null);
    }

    public ICompiledProgram Deserialize(byte[] payload)
    {
        var data = JsonSerializer.Deserialize<EchoPayload>(payload)
                   ?? throw new InvalidDataException("invalid echo payload");
        var resolver = new Func<string, string, SourceFile?>((name, from) =>
        {
            foreach (var (path, text) in data.Sources)
                if (string.Equals(path, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(path), Path.GetFileName(name), StringComparison.OrdinalIgnoreCase))
                    return new SourceFile(path, text);
            return null;
        });
        var result = Compile(new SourceFile(data.Main, data.Sources[data.Main]),
            new CompileOptions { IncludeResolver = resolver });
        return result.Program ?? throw new InvalidDataException("invalid echo payload: " +
            string.Join("; ", result.Diagnostics));
    }
}

public sealed class EchoPayload
{
    public string Main { get; set; } = "";
    public Dictionary<string, string> Sources { get; set; } = new();
}

enum EchoOp { Print, Read, Set, Add, List, Call, Loop, Include, Sleep, Error, Exit }

sealed class EchoStmt
{
    public EchoOp Op;
    public string File = "";
    public int Line, Column;
    public string Name = "";      // variable, procedure
    public string Text = "";      // text, value
    public int Number;
    public List<string> Items = new();
    public List<EchoStmt> Body = new();
}

sealed class EchoParser
{
    readonly Func<string, string, SourceFile?>? _resolver;
    public readonly List<Diagnostic> Diagnostics = new();
    public readonly Dictionary<string, EchoStmt> Procs = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> Sources = new();
    readonly List<EchoStmt> _calls = new();
    int _includeDepth;

    public EchoParser(Func<string, string, SourceFile?>? resolver) { _resolver = resolver; }

    void Error(string file, int line, int col, string msg) => Diagnostics.Add(new Diagnostic(Severity.Error, file, line, col, msg));
    void Warn(string file, int line, int col, string msg) => Diagnostics.Add(new Diagnostic(Severity.Warning, file, line, col, msg));

    public List<EchoStmt> ParseFile(string path, string text)
    {
        Sources[path] = text;
        var root = new List<EchoStmt>();
        var stack = new Stack<(EchoStmt Block, List<EchoStmt> Outer)>();
        var current = root;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            int line = i + 1;
            int col = raw.Length - raw.TrimStart().Length + 1;
            int sp = trimmed.IndexOfAny(new[] { ' ', '\t' });
            string kw = (sp < 0 ? trimmed : trimmed[..sp]).ToLowerInvariant();
            string rest = sp < 0 ? "" : trimmed[(sp + 1)..].Trim();
            int restCol = sp < 0 ? col + trimmed.Length : col + sp + 1 + (trimmed[(sp + 1)..].Length - trimmed[(sp + 1)..].TrimStart().Length);
            var st = new EchoStmt { File = path, Line = line, Column = col };
            switch (kw)
            {
                case "print":
                    st.Op = EchoOp.Print; st.Text = rest;
                    if (rest.Length == 0) Warn(path, line, col, "print statement without text");
                    break;
                case "read":
                    st.Op = EchoOp.Read; st.Name = rest;
                    if (!IsIdent(rest)) { Error(path, line, restCol, "variable name expected after read"); continue; }
                    break;
                case "set":
                case "list":
                {
                    st.Op = kw == "set" ? EchoOp.Set : EchoOp.List;
                    int eq = rest.IndexOf('=');
                    if (eq < 0) { Error(path, line, restCol, "'=' expected"); continue; }
                    st.Name = rest[..eq].Trim();
                    if (!IsIdent(st.Name)) { Error(path, line, restCol, "invalid variable name"); continue; }
                    st.Text = rest[(eq + 1)..].Trim();
                    if (st.Op == EchoOp.List)
                        st.Items = st.Text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    break;
                }
                case "add":
                {
                    st.Op = EchoOp.Add;
                    var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2 || !IsIdent(parts[0]) || !int.TryParse(parts[1], out st.Number))
                    { Error(path, line, restCol, "syntax: add variable number"); continue; }
                    st.Name = parts[0];
                    break;
                }
                case "call":
                    st.Op = EchoOp.Call; st.Name = rest;
                    if (!IsIdent(rest)) { Error(path, line, restCol, "procedure name expected after call"); continue; }
                    _calls.Add(st);
                    break;
                case "proc":
                    if (stack.Count > 0) { Error(path, line, col, "a procedure can only be declared at top level"); }
                    if (!IsIdent(rest)) { Error(path, line, restCol, "procedure name expected after proc"); }
                    st.Name = rest;
                    if (Procs.ContainsKey(rest)) Error(path, line, restCol, $"procedure '{rest}' already declared");
                    else if (IsIdent(rest)) Procs[rest] = st;
                    stack.Push((st, current));
                    current = st.Body;
                    continue;
                case "loop":
                    st.Op = EchoOp.Loop;
                    if (!int.TryParse(rest, out st.Number) || st.Number < 0)
                        Error(path, line, restCol, "repeat count expected after loop");
                    current.Add(st);
                    stack.Push((st, current));
                    current = st.Body;
                    continue;
                case "end":
                    if (stack.Count == 0) { Error(path, line, col, "'end' without matching 'proc' or 'loop'"); continue; }
                    current = stack.Pop().Outer;
                    continue;
                case "include":
                {
                    if (rest.Length == 0) { Error(path, line, restCol, "file name expected after include"); continue; }
                    var src = _resolver?.Invoke(rest, path);
                    if (src == null) { Error(path, line, restCol, $"included file not found: {rest}"); continue; }
                    if (_includeDepth > 16) { Error(path, line, restCol, "includes nested too deeply"); continue; }
                    _includeDepth++;
                    var inner = ParseFile(src.Path, src.Text);
                    _includeDepth--;
                    current.AddRange(inner);
                    continue;
                }
                case "sleep":
                    st.Op = EchoOp.Sleep;
                    if (!int.TryParse(rest, out st.Number)) { Error(path, line, restCol, "duration in milliseconds expected"); continue; }
                    break;
                case "error":
                    st.Op = EchoOp.Error; st.Text = rest;
                    break;
                case "exit":
                    st.Op = EchoOp.Exit;
                    if (!int.TryParse(rest, out st.Number)) { Error(path, line, restCol, "integer exit code expected"); continue; }
                    break;
                default:
                    Error(path, line, col, $"unknown statement '{kw}'");
                    continue;
            }
            current.Add(st);
        }
        while (stack.Count > 0)
        {
            var (block, outer) = stack.Pop();
            Error(block.File, block.Line, block.Column, "unterminated block: missing 'end'");
            current = outer;
        }
        return root;
    }

    public void CheckCalls()
    {
        foreach (var c in _calls)
            if (!Procs.ContainsKey(c.Name))
                Error(c.File, c.Line, c.Column, $"unknown procedure '{c.Name}'");
    }

    static bool IsIdent(string s) =>
        s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_') && s.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
}

sealed class EchoProgram : ICompiledProgram
{
    readonly string _main;
    internal readonly List<EchoStmt> Body;
    internal readonly Dictionary<string, EchoStmt> Procs;
    readonly Dictionary<string, string> _sources;

    public EchoProgram(string main, List<EchoStmt> body, Dictionary<string, EchoStmt> procs, Dictionary<string, string> sources)
    {
        _main = main; Body = body; Procs = procs; _sources = sources;
    }

    public string LanguageId => EchoLanguage.LanguageId;

    public byte[] Serialize() =>
        JsonSerializer.SerializeToUtf8Bytes(new EchoPayload { Main = _main, Sources = new Dictionary<string, string>(_sources) });

    public IProgramExecution CreateExecution(IProgramIO io) => new EchoExecution(this, io);
}

sealed class EchoFrame
{
    public EchoFrame(string name) { Name = name; }
    public string Name;
    public string File = "";
    public int Line, Column;
    public readonly Dictionary<string, object> Vars = new(StringComparer.OrdinalIgnoreCase);
}

sealed class EchoRuntimeException : Exception
{
    public EchoRuntimeException(string message) : base(message) { }
    public IReadOnlyList<StackFrameInfo>? Stack;
    public EchoStmt? Stmt;
}

sealed class EchoExitException : Exception
{
    public EchoExitException(int code) { Code = code; }
    public int Code { get; }
}

sealed class EchoExecution : IProgramExecution
{
    readonly EchoProgram _program;
    readonly IProgramIO _io;
    readonly EchoDebugger _debugger = new();
    internal readonly List<EchoFrame> Frames = new();   // Frames[0] = main program
    bool _eof;

    public EchoExecution(EchoProgram program, IProgramIO io)
    {
        _program = program; _io = io;
        _debugger.Owner = this;
    }

    public IDebugger Debugger => _debugger;

    public ExecutionResult Run(CancellationToken cancellationToken)
    {
        Frames.Clear();
        Frames.Add(new EchoFrame("main program"));
        EchoStmt? last = null;
        try
        {
            ExecBlock(_program.Body, cancellationToken, ref last);
            return new ExecutionResult(0, null);
        }
        catch (EchoExitException e)
        {
            return new ExecutionResult(e.Code, null);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionResult(130, null, Cancelled: true);
        }
        catch (EchoRuntimeException e)
        {
            var at = e.Stmt ?? last;
            var err = new RuntimeErrorInfo(e.Message, at?.File ?? "", at?.Line ?? 0, at?.Column ?? 0, e.Stack ?? Snapshot());
            return new ExecutionResult(1, err);
        }
    }

    internal IReadOnlyList<StackFrameInfo> Snapshot()
    {
        var list = new List<StackFrameInfo>();
        for (int i = Frames.Count - 1; i >= 0; i--)
            list.Add(new StackFrameInfo(Frames[i].Name, Frames[i].File, Frames[i].Line, Frames[i].Column));
        return list;
    }

    void ExecBlock(List<EchoStmt> body, CancellationToken ct, ref EchoStmt? last)
    {
        foreach (var st in body)
        {
            ct.ThrowIfCancellationRequested();
            var frame = Frames[^1];
            frame.File = st.File; frame.Line = st.Line; frame.Column = st.Column;
            last = st;
            _debugger.BeforeStatement(st, Frames.Count, ct);
            try
            {
                ExecStatement(st, frame, ct, ref last);
            }
            catch (EchoRuntimeException e) when (e.Stack == null)
            {
                // Error raised by this statement: stack captured before unwinding.
                e.Stack = Snapshot();
                e.Stmt = st;
                if (_debugger.BreakOnError) _debugger.Pause(PauseReason.Exception, st, e.Message, ct);
                throw;
            }
        }
    }

    void ExecStatement(EchoStmt st, EchoFrame frame, CancellationToken ct, ref EchoStmt? last)
    {
        {
            switch (st.Op)
            {
                case EchoOp.Print:
                    _io.Write(Interpolate(st.Text) + "\n");
                    break;
                case EchoOp.Read:
                {
                    string? line = _eof ? null : _io.ReadLine();
                    if (line == null) _eof = true;
                    frame.Vars[st.Name] = line ?? "";
                    Frames[0].Vars["eof"] = _eof ? 1 : 0;
                    break;
                }
                case EchoOp.Set:
                {
                    var v = Interpolate(st.Text);
                    frame.Vars[st.Name] = int.TryParse(v, out var n) ? n : v;
                    break;
                }
                case EchoOp.Add:
                {
                    var cur = Lookup(st.Name);
                    int value = cur switch { int i => i, string s when int.TryParse(s, out var p) => p, null => 0, _ => throw new EchoRuntimeException($"'{st.Name}' is not an integer") };
                    SetVar(st.Name, value + st.Number);
                    break;
                }
                case EchoOp.List:
                    frame.Vars[st.Name] = st.Items.Select(Interpolate).ToList();
                    break;
                case EchoOp.Call:
                {
                    var proc = _program.Procs[st.Name];
                    if (Frames.Count > 200) throw new EchoRuntimeException("stack overflow (recursion too deep)");
                    Frames.Add(new EchoFrame(proc.Name) { File = proc.File, Line = proc.Line, Column = proc.Column });
                    try { ExecBlock(proc.Body, ct, ref last); }
                    finally { Frames.RemoveAt(Frames.Count - 1); }
                    last = st;
                    break;
                }
                case EchoOp.Loop:
                    for (int i = 0; i < st.Number; i++)
                    {
                        frame.Vars["i"] = i;
                        ExecBlock(st.Body, ct, ref last);
                    }
                    break;
                case EchoOp.Sleep:
                    if (ct.WaitHandle.WaitOne(Math.Max(0, st.Number))) ct.ThrowIfCancellationRequested();
                    break;
                case EchoOp.Error:
                    throw new EchoRuntimeException(Interpolate(st.Text));
                case EchoOp.Exit:
                    throw new EchoExitException(st.Number);
            }
        }
    }

    object? Lookup(string name)
    {
        if (Frames[^1].Vars.TryGetValue(name, out var v)) return v;
        if (Frames[0].Vars.TryGetValue(name, out v)) return v;
        return null;
    }

    void SetVar(string name, object value)
    {
        if (!Frames[^1].Vars.ContainsKey(name) && Frames[0].Vars.ContainsKey(name)) Frames[0].Vars[name] = value;
        else Frames[^1].Vars[name] = value;
    }

    string Interpolate(string text)
    {
        if (text.IndexOf('{') < 0) return text;
        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                int close = text.IndexOf('}', i + 1);
                if (close > i)
                {
                    var name = text[(i + 1)..close].Trim();
                    var v = Lookup(name);
                    sb.Append(v switch { null => "", List<string> l => string.Join(", ", l), _ => v.ToString() });
                    i = close;
                    continue;
                }
            }
            sb.Append(text[i]);
        }
        return sb.ToString();
    }
}

sealed class EchoDebugger : IDebugger
{
    enum Mode { Run, Into, Over, Out }

    readonly object _gate = new();
    readonly Dictionary<string, HashSet<int>> _breakpoints = new(StringComparer.OrdinalIgnoreCase);
    readonly SemaphoreSlim _resume = new(0);
    internal EchoExecution? Owner;
    volatile bool _pauseRequested;
    bool _paused, _started;
    Mode _mode;
    int _stepDepth, _currentDepth;
    IReadOnlyList<StackFrameInfo> _stack = Array.Empty<StackFrameInfo>();

    public bool StopOnEntry { get; set; }
    public bool BreakOnError { get; set; }
    public event Action<PauseInfo>? Paused;

    static string Key(string file)
    {
        try { return Path.GetFullPath(file); } catch { return file; }
    }

    public void SetBreakpoints(string file, IEnumerable<int> lines)
    {
        lock (_gate) _breakpoints[Key(file)] = new HashSet<int>(lines);
    }

    bool HasBreakpoint(string file, int line)
    {
        lock (_gate) return _breakpoints.TryGetValue(Key(file), out var set) && set.Contains(line);
    }

    public void RequestPause() => _pauseRequested = true;

    internal void BeforeStatement(EchoStmt st, int depth, CancellationToken ct)
    {
        _currentDepth = depth;
        PauseReason? reason = null;
        if (!_started)
        {
            _started = true;
            if (StopOnEntry) reason = PauseReason.Entry;
        }
        if (reason == null && _pauseRequested) reason = PauseReason.PauseRequest;
        if (reason == null)
        {
            Mode mode; int stepDepth;
            lock (_gate) { mode = _mode; stepDepth = _stepDepth; }
            if (mode == Mode.Into || (mode == Mode.Over && depth <= stepDepth) || (mode == Mode.Out && depth < stepDepth))
                reason = PauseReason.Step;
        }
        if (reason == null && HasBreakpoint(st.File, st.Line)) reason = PauseReason.Breakpoint;
        if (reason != null) Pause(reason.Value, st, null, ct);
    }

    internal void Pause(PauseReason reason, EchoStmt st, string? message, CancellationToken ct)
    {
        lock (_gate)
        {
            _paused = true;
            _pauseRequested = false;
            _mode = Mode.Run;
            _stack = Owner?.Snapshot() ?? Array.Empty<StackFrameInfo>();
        }
        Paused?.Invoke(new PauseInfo(reason, st.File, st.Line, st.Column, message));
        try
        {
            _resume.Wait(ct);
        }
        finally
        {
            lock (_gate) _paused = false;
        }
    }

    void Resume(Mode mode)
    {
        lock (_gate)
        {
            if (!_paused) return;
            _mode = mode;
            _stepDepth = _currentDepth;
            _paused = false;
        }
        _resume.Release();
    }

    public void Continue() => Resume(Mode.Run);
    public void StepInto() => Resume(Mode.Into);
    public void StepOver() => Resume(Mode.Over);
    public void StepOut() => Resume(Mode.Out);

    public IReadOnlyList<StackFrameInfo> GetStack()
    {
        lock (_gate) return _stack;
    }

    public IReadOnlyList<VariableInfo> GetVariables(int frameIndex)
    {
        var owner = Owner;
        if (owner == null) return Array.Empty<VariableInfo>();
        lock (_gate)
        {
            int idx = owner.Frames.Count - 1 - frameIndex;
            if (idx < 0 || idx >= owner.Frames.Count) return Array.Empty<VariableInfo>();
            return owner.Frames[idx].Vars.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => Describe(kv.Key, kv.Value)).ToList();
        }
    }

    static VariableInfo Describe(string name, object value) => value switch
    {
        int i => new VariableInfo(name, "integer", i.ToString()),
        string s => new VariableInfo(name, "text", "\"" + s + "\""),
        List<string> l => new VariableInfo(name, $"list[{l.Count}]", "(" + string.Join(", ", l) + ")", l.Count > 0, l),
        _ => new VariableInfo(name, value.GetType().Name, value.ToString() ?? ""),
    };

    public IReadOnlyList<VariableInfo> GetChildren(VariableInfo variable)
    {
        if (variable.Handle is List<string> l)
            lock (_gate) return l.Select((s, i) => new VariableInfo($"[{i}]", "text", "\"" + s + "\"")).ToList();
        return Array.Empty<VariableInfo>();
    }
}
