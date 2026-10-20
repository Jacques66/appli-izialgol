using System.Text;
using Studio.Common;
using Studio.Simula.Compiler;
using Studio.Simula.Runtime;

namespace Studio.Simula;

public sealed class SimulaLanguage : ILanguageService
{
    public string Id => "simula";
    public string DisplayName => "Simula 67";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".sim" };

    static string? prelude;
    internal static string Prelude
    {
        get
        {
            if (prelude != null) return prelude;
            using var st = typeof(SimulaLanguage).Assembly.GetManifestResourceStream("Studio.Simula.Prelude.sim")
                ?? throw new InvalidOperationException("Simula prelude not found");
            using var rd = new StreamReader(st, Encoding.UTF8);
            return prelude = rd.ReadToEnd();
        }
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
        var c = new SimCompiler();
        SimProgram? prog;
        try
        {
            prog = c.CompileProgram(main, Prelude, resolver);
        }
        catch (Exception e)
        {
            var diags = c.Diagnostics.ToList();
            diags.Add(new Diagnostic(Severity.Error, main.Path, 0, 0, "internal compiler error: " + e.Message));
            return new CompilationResult(diags, null);
        }
        if (prog == null) return new CompilationResult(c.Diagnostics, null);
        return new CompilationResult(c.Diagnostics, new SimulaProgram(prog, used));
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

sealed class SimulaProgram : ICompiledProgram
{
    readonly SimProgram prog;
    readonly List<SourceFile> sources;
    public SimulaProgram(SimProgram p, List<SourceFile> srcs) { prog = p; sources = srcs; }
    public string LanguageId => "simula";

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
