// Runner : lanceur des executables autonomes generes par simc, a68c et l'IDE.
// Un .exe genere = copie de Runner.exe (mono-fichier) + programme compile en fin de fichier
// (format Studio.Common.ExePackage).

using System.Text;
using Studio.Common;
using Studio.Tooling;

namespace Studio.Runner;

static class Program
{
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }

        var exePath = Environment.ProcessPath;
        (string LanguageId, byte[] Payload)? package = null;
        try
        {
            if (exePath != null) package = ExePackage.Read(exePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"impossible de lire le programme embarqué : {ex.Message}");
            return 3;
        }

        if (package == null)
        {
            PrintUsage();
            return args.Length > 0 && (args[0] is "-h" or "--help" or "/?") ? 0 : 2;
        }

        var (langId, payload) = package.Value;
        var language = LanguageRegistry.FindById(langId);
        if (language == null)
        {
            Console.Error.WriteLine($"langage inconnu dans l'exécutable : « {langId} »");
            return 3;
        }

        ICompiledProgram program;
        try
        {
            program = language.Deserialize(payload);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"programme {language.DisplayName} embarqué illisible : {ex.Message}");
            return 3;
        }

        var workDir = Environment.CurrentDirectory;
        return ProgramHost.RunInConsole(program, workDir);
    }

    static void PrintUsage()
    {
        Console.WriteLine("Runner — lanceur de programmes Simula 67 / Algol 68");
        Console.WriteLine();
        Console.WriteLine("Ce fichier ne contient aucun programme. Il sert de base aux exécutables autonomes");
        Console.WriteLine("produits par les compilateurs :");
        Console.WriteLine("  simc programme.sim -o programme.exe");
        Console.WriteLine("  a68c programme.a68 -o programme.exe");
        Console.WriteLine("ou par la commande « Générer un .exe autonome » de Simula 67 & Algol 68 Studio.");
    }
}
