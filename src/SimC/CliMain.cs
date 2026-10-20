// Logique commune des compilateurs en ligne de commande simc (Simula 67) et a68c (Algol 68).
// Ce fichier est lie dans le projet A68C.

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
                    Console.WriteLine($"{toolName} ({language.DisplayName}) — Simula 67 & Algol 68 Studio {typeof(CliMain).Assembly.GetName().Version}");
                    return 0;
                case "-o":
                case "--output":
                    if (i + 1 >= args.Length) return UsageError(toolName, "nom de fichier attendu après -o");
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
                    if (a.StartsWith('-')) return UsageError(toolName, $"option inconnue : {a}");
                    if (input != null) return UsageError(toolName, "un seul fichier source est accepté");
                    input = a;
                    break;
            }
        }

        if (input == null) return UsageError(toolName, "fichier source manquant");
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"{toolName}: fichier introuvable : {input}");
            return 2;
        }

        string text;
        try { text = ReadSource(input); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{toolName}: impossible de lire {input} : {ex.Message}");
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
            Console.Error.WriteLine($"{toolName}: erreur interne du compilateur : {ex}");
            return 3;
        }

        int errors = 0, warnings = 0;
        foreach (var d in result.Diagnostics)
        {
            if (d.Severity == Severity.Error) errors++;
            else if (d.Severity == Severity.Warning) warnings++;
            // Diagnostics sur stderr pour --run (stdout reste au programme), sinon sur stdout.
            (run ? Console.Error : Console.Out).WriteLine(d.ToString());
        }

        if (!result.Success || result.Program == null)
        {
            Console.Error.WriteLine($"échec de la compilation : {errors} erreur(s), {warnings} avertissement(s)");
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
                if (!run) Console.WriteLine($"exécutable généré : {Path.GetFullPath(output)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{toolName}: impossible d'écrire {output} : {ex.Message}");
                return 4;
            }
        }

        if (run)
        {
            Console.Out.Flush();
            return ProgramHost.RunInConsole(result.Program, Environment.CurrentDirectory);
        }

        if (output == null)
            Console.WriteLine($"compilation réussie : {errors} erreur(s), {warnings} avertissement(s)");
        return 0;
    }

    /// <summary>Lit un source en UTF-8 (avec ou sans BOM), sinon en Latin-1.</summary>
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

    /// <summary>Fichiers inclus : relatifs au fichier qui inclut, puis au repertoire courant.</summary>
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
        Console.Error.WriteLine($"Essayez « {toolName} --help ».");
        return 2;
    }

    static void PrintUsage(string toolName, ILanguageService language, string ext)
    {
        Console.WriteLine($"{toolName} — compilateur {language.DisplayName} (Simula 67 & Algol 68 Studio)");
        Console.WriteLine();
        Console.WriteLine($"Usage : {toolName} fichier{ext} [-o sortie.exe] [--run] [--no-debug]");
        Console.WriteLine();
        Console.WriteLine("  -o, --output FICHIER   produit un exécutable autonome (.exe)");
        Console.WriteLine("  -r, --run              exécute le programme dans la console après compilation");
        Console.WriteLine("      --no-debug         sans informations de mise au point");
        Console.WriteLine("      --version          affiche la version");
        Console.WriteLine("  -h, --help             affiche cette aide");
        Console.WriteLine();
        Console.WriteLine("Codes de retour : 0 succès, 1 erreur de compilation, 2 erreur d'utilisation,");
        Console.WriteLine("                  3 erreur interne, 4 génération de l'exécutable impossible ;");
        Console.WriteLine("avec --run : code de sortie du programme.");
    }
}
