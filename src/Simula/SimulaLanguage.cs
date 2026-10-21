using System.Text;
using Studio.Common;
using Studio.Simula.Compiler;
using Studio.Simula.Runtime;

namespace Studio.Simula;

/// <summary>
/// Shared front-end plumbing for the languages compiled by the Simula engine
/// (Simula 67, Algol 60, Simula I): dialect, user lexer and environment prelude differ.
/// </summary>
public abstract class EngineLanguage : ILanguageService
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract IReadOnlyList<string> FileExtensions { get; }
    /// <summary>Environment block (Simula source) enclosing the user program.</summary>
    protected abstract string PreludeText { get; }
    protected abstract Dialect Dialect { get; }
    /// <summary>Lexer for the user program; null means the Simula 67 lexer.</summary>
    protected virtual Func<string, int, Action<SrcPos, string>, Func<string, List<Token>?>, List<Token>>? UserLexer => null;

    protected static string ReadResource(Type owner, string name)
    {
        using var st = owner.Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"resource {name} not found");
        using var rd = new StreamReader(st, Encoding.UTF8);
        return rd.ReadToEnd();
    }

    public CompilationResult Compile(SourceFile main, CompileOptions options)
    {
        var used = new List<SourceFile> { main };
        Func<string, string, SourceFile?> resolver = (name, from) =>
        {
            SourceFile? sf = options.IncludeResolver?.Invoke(name, from) ?? DefaultResolve(name, from);
            if (sf != null) used.Add(sf);
            return sf;
        };
        var c = new SimCompiler(Dialect) { UserLexer = UserLexer };
        SimProgram? prog;
        try
        {
            prog = c.CompileProgram(main, PreludeText, resolver);
        }
        catch (Exception e)
        {
            var diags = c.Diagnostics.ToList();
            diags.Add(new Diagnostic(Severity.Error, main.Path, 0, 0, "internal compiler error: " + e.Message));
            return new CompilationResult(diags, null);
        }
        if (prog == null) return new CompilationResult(c.Diagnostics, null);
        return new CompilationResult(c.Diagnostics, new SimulaProgram(prog, used, Id));
    }

    static SourceFile? DefaultResolve(string name, string from)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(from)) ?? Environment.CurrentDirectory;
            var p = Path.IsPathRooted(name) ? name : Path.Combine(dir, name);
            if (File.Exists(p)) return new SourceFile(p, File.ReadAllText(p));
            // case-insensitive attempt
            if (Directory.Exists(dir))
            {
                var hit = Directory.GetFiles(dir).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return new SourceFile(hit, File.ReadAllText(hit));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>The .exe embeds the sources (program and modules); they are recompiled at startup.</summary>
    public ICompiledProgram Deserialize(byte[] payload)
    {
        using var ms = new MemoryStream(payload);
        using var br = new BinaryReader(ms, Encoding.UTF8);
        int n = br.ReadInt32();
        var srcs = new List<SourceFile>();
        for (int i = 0; i < n; i++) srcs.Add(new SourceFile(br.ReadString(), br.ReadString()));
        var map = srcs.Skip(1).ToDictionary(s => Path.GetFileName(s.Path), s => s, StringComparer.OrdinalIgnoreCase);
        var res = Compile(srcs[0], new CompileOptions { IncludeResolver = (name, from) => map.TryGetValue(Path.GetFileName(name), out var s) ? s : null });
        if (!res.Success) throw new InvalidOperationException("invalid embedded program: " + string.Join("; ", res.Diagnostics));
        return res.Program!;
    }
}

public sealed class SimulaLanguage : EngineLanguage
{
    public override string Id => "simula";
    public override string DisplayName => "Simula 67";
    public override IReadOnlyList<string> FileExtensions { get; } = new[] { ".sim" };
    protected override Dialect Dialect => Dialect.Simula67;

    static string? prelude;
    protected override string PreludeText => prelude ??= ReadResource(typeof(SimulaLanguage), "Studio.Simula.Prelude.sim");
}

sealed class SimulaProgram : ICompiledProgram
{
    readonly SimProgram prog;
    readonly List<SourceFile> sources;
    public SimulaProgram(SimProgram p, List<SourceFile> srcs, string languageId) { prog = p; sources = srcs; LanguageId = languageId; }
    public string LanguageId { get; }

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
        {
            var distinct = sources.GroupBy(s => s.Path).Select(g => g.First()).ToList();
            bw.Write(distinct.Count);
            foreach (var s in distinct) { bw.Write(s.Path); bw.Write(s.Text); }
        }
        return ms.ToArray();
    }

    public IProgramExecution CreateExecution(IProgramIO io) => new SimulaExecution(prog, io);
}

sealed class SimulaExecution : IProgramExecution
{
    readonly SimProgram prog;
    readonly IProgramIO io;
    readonly SimDebugger dbg = new();
    public SimulaExecution(SimProgram p, IProgramIO io) { prog = p; this.io = io; dbg.Attach(p); }
    public IDebugger Debugger => dbg;

    public ExecutionResult Run(CancellationToken cancellationToken)
    {
        using var reg = cancellationToken.Register(() => dbg.Stop());
        ExecutionResult r = default!;
        // generous execution stack for deeply recursive host code
        var t = new Thread(() =>
        {
            try { r = new Vm(prog, io, dbg).Run(cancellationToken); }
            catch (Exception e) { r = new ExecutionResult(2, new RuntimeErrorInfo("internal machine error: " + e.Message, prog.Files.FirstOrDefault() ?? "", 0, 0, Array.Empty<StackFrameInfo>())); }
        }, 64 * 1024 * 1024);
        t.Start();
        t.Join();
        return r;
    }
}
