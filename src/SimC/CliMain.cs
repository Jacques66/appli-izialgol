// Shared logic of the command-line compilers simc (Simula 67), a68c (Algol 68),
// a60c (Algol 60), s1c (Simula I) and pasc (Pascal). This file is linked into the other CLI projects.

using System.Text;
using Studio.Common;

namespace Studio.Tooling;

public static class CliMain
{
    public static int Run(string[] args, ILanguageService language, string toolName)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }

        string ext = language.FileExtensions.FirstOrDefault() ?? "";
        string? input = null, output = null;
        bool run = false, debug = true;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "-h":
                case "--help":
                case "/?":
                    PrintUsage(toolName, language, ext);
                    return 0;
                case "--version":
                    Console.WriteLine($"{toolName} ({language.DisplayName}) — AlgolStudio {typeof(CliMain).Assembly.GetName().Version}");
                    return 0;
                case "-o":
                case "--output":
                    if (i + 1 >= args.Length) return UsageError(toolName, "output file name expected after -o");
                    output = args[++i];
                    break;
                case "--run":
                case "-r":
                    run = true;
                    break;
                case "--no-debug":
                    debug = false;
                    break;
                default:
                    if (a.StartsWith("-o", StringComparison.Ordinal) && a.Length > 2) { output = a[2..]; break; }
                    if (a.StartsWith('-')) return UsageError(toolName, $"unknown option: {a}");
                    if (input != null) return UsageError(toolName, "only one source file is accepted");
                    input = a;
                    break;
            }
        }

        if (input == null) return UsageError(toolName, "missing source file");
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"{toolName}: file not found: {input}");
            return 2;
        }

        string text;
        try { text = ReadSource(input); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{toolName}: cannot read {input}: {ex.Message}");
            return 2;
        }

        var options = new CompileOptions { Debug = debug, IncludeResolver = ResolveInclude };
        CompilationResult result;
        try
        {
            result = language.Compile(new SourceFile(input, text), options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{toolName}: internal compiler error: {ex}");
            return 3;
        }

        int errors = 0, warnings = 0;
        foreach (var d in result.Diagnostics)
        {
            if (d.Severity == Severity.Error) errors++;
            else if (d.Severity == Severity.Warning) warnings++;
            // Diagnostics go to stderr with --run (stdout belongs to the program), otherwise to stdout.
            (run ? Console.Error : Console.Out).WriteLine(d.ToString());
        }

        if (!result.Success || result.Program == null)
        {
            Console.Error.WriteLine($"compilation failed: {errors} error(s), {warnings} warning(s)");
            return 1;
        }

        if (output != null)
        {
            var runner = RunnerLocator.Find();
            if (runner == null)
            {
                Console.Error.WriteLine($"{toolName}: {RunnerLocator.NotFoundMessage}");
                return 4;
            }
            try
            {
                if (!output.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) output += ".exe";
                ExePackage.Write(runner, output, language.Id, result.Program.Serialize());
                if (!run) Console.WriteLine($"executable generated: {Path.GetFullPath(output)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{toolName}: cannot write {output}: {ex.Message}");
                return 4;
            }
        }

        if (run)
        {
            Console.Out.Flush();
            return ProgramHost.RunInConsole(result.Program, Environment.CurrentDirectory);
        }

        if (output == null)
            Console.WriteLine($"compilation succeeded: {errors} error(s), {warnings} warning(s)");
        return 0;
    }

    /// <summary>Reads a source file as UTF-8 (with or without BOM), otherwise as Latin-1.</summary>
    public static string ReadSource(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>Included files: relative to the including file, then to the current directory.</summary>
    static SourceFile? ResolveInclude(string name, string includingFile)
    {
        var candidates = new List<string>();
        if (Path.IsPathRooted(name)) candidates.Add(name);
        else
        {
            var dir = Path.GetDirectoryName(includingFile);
            candidates.Add(string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name));
            candidates.Add(Path.Combine(Environment.CurrentDirectory, name));
        }
        foreach (var c in candidates)
            if (File.Exists(c))
                return new SourceFile(c, ReadSource(c));
        return null;
    }

    static int UsageError(string toolName, string message)
    {
        Console.Error.WriteLine($"{toolName}: {message}");
        Console.Error.WriteLine($"Try '{toolName} --help'.");
        return 2;
    }

    static void PrintUsage(string toolName, ILanguageService language, string ext)
    {
        Console.WriteLine($"Usage: {toolName} file{ext} [-o output.exe] [--run] [--no-debug]");
        Console.WriteLine();
        Console.WriteLine($"{toolName} — {language.DisplayName} compiler (AlgolStudio)");
        Console.WriteLine();
        Console.WriteLine("  -o, --output FILE      produce a standalone executable (.exe)");
        Console.WriteLine("  -r, --run              run the program in the console after compiling");
        Console.WriteLine("      --no-debug         without debugging information");
        Console.WriteLine("      --version          show the version");
        Console.WriteLine("  -h, --help             show this help");
        Console.WriteLine();
        Console.WriteLine("Exit codes: 0 success, 1 compilation error, 2 usage error,");
        Console.WriteLine("            3 internal error, 4 executable could not be generated;");
        Console.WriteLine("with --run: the program's exit code.");
    }
}
