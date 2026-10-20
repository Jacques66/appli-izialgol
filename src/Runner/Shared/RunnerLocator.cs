// Localisation de Runner.exe (lanceur autonome mono-fichier) pour la generation des .exe.

namespace Studio.Tooling;

public static class RunnerLocator
{
    public const string RunnerFileName = "Runner.exe";

    /// <summary>
    /// Cherche Runner.exe : variable SIMULA67_RUNNER, a cote de l'outil, puis en remontant
    /// l'arborescence (mode developpement) dans src/Runner/bin/{Release,Debug}/single.
    /// Rend null si introuvable.
    /// </summary>
    public static string? Find(string? startDirectory = null)
    {
        var env = Environment.GetEnvironmentVariable("SIMULA67_RUNNER");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return Path.GetFullPath(env);

        var baseDir = startDirectory ?? AppContext.BaseDirectory;
        var beside = Path.Combine(baseDir, RunnerFileName);
        if (IsSingleFile(beside)) return beside;

        var candidates = new List<string>();
        for (var dir = new DirectoryInfo(baseDir); dir != null; dir = dir.Parent)
        {
            foreach (var cfg in new[] { "Release", "Debug" })
            {
                var p = Path.Combine(dir.FullName, "src", "Runner", "bin", cfg, "single", RunnerFileName);
                if (IsSingleFile(p)) candidates.Add(p);
            }
            var dist = Path.Combine(dir.FullName, "dist", RunnerFileName);
            if (IsSingleFile(dist)) candidates.Add(dist);
            if (candidates.Count > 0) break;
        }
        return candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    // Signature d'un bundle mono-fichier .NET (SHA-256 de ".net core bundle"), precedee dans
    // l'apphost par l'offset (int64) de l'en-tete du bundle : 0 pour un apphost classique.
    static readonly byte[] BundleSignature =
    {
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae,
    };

    /// <summary>Vrai si l'exe est un bundle mono-fichier (un apphost classique a besoin de Runner.dll : inutilisable).</summary>
    public static bool IsSingleFile(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return false;
            var bytes = File.ReadAllBytes(exePath);
            int idx = bytes.AsSpan().IndexOf(BundleSignature);
            if (idx < 8) return !File.Exists(Path.ChangeExtension(exePath, ".dll"));
            return BitConverter.ToInt64(bytes, idx - 8) != 0;
        }
        catch
        {
            return false;
        }
    }

    public static string NotFoundMessage =>
        "Runner.exe introuvable : construisez la solution (dotnet build) ou lancez build.ps1 " +
        "(Runner.exe doit se trouver à côté de l'outil).";
}
