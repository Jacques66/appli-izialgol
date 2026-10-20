namespace Studio.Ide.Services;

/// <summary>Finds the samples/ and docs/ folders: next to the exe, then walking up the tree (development mode).</summary>
public static class PathLocator
{
    public static string? FindDirectory(string name, Func<string, bool>? accept = null)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, name);
            if (Directory.Exists(p) && (accept == null || accept(p))) return p;
        }
        return null;
    }

    public static string? SamplesDirectory =>
        FindDirectory("samples", p => Directory.Exists(Path.Combine(p, "simula")) || Directory.Exists(Path.Combine(p, "algol68")));

    public static string? DocsDirectory =>
        FindDirectory("docs", p => File.Exists(Path.Combine(p, "simula.md")) || File.Exists(Path.Combine(p, "algol68.md")));

    public static string TempDirectory
    {
        get
        {
            var p = Path.Combine(Path.GetTempPath(), "Simula67Studio");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    public static bool SamePath(string? a, string? b)
    {
        if (a == null || b == null) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
