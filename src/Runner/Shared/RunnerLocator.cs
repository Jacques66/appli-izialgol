// Locates Runner.exe (single-file standalone launcher) used to generate .exe files.

namespace Studio.Tooling;

public static class RunnerLocator
{
    public const string RunnerFileName = "Runner.exe";

    /// <summary>
    /// Looks for Runner.exe: SIMULA67_RUNNER variable, next to the tool, then walking up
    /// the directory tree (development mode) in src/Runner/bin/{Release,Debug}/single.
    /// Returns null if not found.
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

    // Signature of a .NET single-file bundle (SHA-256 of ".net core bundle"), preceded in
    // the apphost by the offset (int64) of the bundle header: 0 for a regular apphost.
    static readonly byte[] BundleSignature =
    {
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae,
    };

    /// <summary>True if the exe is a single-file bundle (a regular apphost needs Runner.dll next to it: unusable).</summary>
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
        "Runner.exe not found: build the solution (dotnet build) or run build.ps1 " +
        "(Runner.exe must be next to the tool).";
}
