using System.Text;

namespace Studio.Common;

/// <summary>
/// Format d'un .exe autonome : [Runner.exe][payload][langId : 16 octets UTF-8 completes par des 0]
/// [longueur du payload : int64 little-endian][magic "S67A68EX" : 8 octets].
/// </summary>
public static class ExePackage
{
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("S67A68EX");
    const int LangIdSize = 16;
    const int TrailerSize = LangIdSize + 8 + 8;

    public static void Write(string runnerExePath, string outputExePath, string languageId, byte[] payload)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputExePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var output = File.Create(outputExePath);
        using (var runner = File.OpenRead(runnerExePath)) runner.CopyTo(output);
        output.Write(payload);
        var id = new byte[LangIdSize];
        var raw = Encoding.UTF8.GetBytes(languageId);
        if (raw.Length > LangIdSize) throw new ArgumentException("identifiant de langage trop long");
        raw.CopyTo(id, 0);
        output.Write(id);
        output.Write(BitConverter.GetBytes((long)payload.Length));
        output.Write(Magic);
    }

    /// <summary>Lit le payload attache a un exe ; null s'il n'y en a pas.</summary>
    public static (string LanguageId, byte[] Payload)? Read(string exePath)
    {
        using var f = File.OpenRead(exePath);
        if (f.Length < TrailerSize) return null;
        var trailer = new byte[TrailerSize];
        f.Seek(-TrailerSize, SeekOrigin.End);
        f.ReadExactly(trailer);
        if (!trailer.AsSpan(TrailerSize - 8).SequenceEqual(Magic)) return null;
        long len = BitConverter.ToInt64(trailer, LangIdSize);
        string lang = Encoding.UTF8.GetString(trailer, 0, LangIdSize).TrimEnd('\0');
        if (len < 0 || len > f.Length - TrailerSize) return null;
        var payload = new byte[len];
        f.Seek(-TrailerSize - len, SeekOrigin.End);
        f.ReadExactly(payload);
        return (lang, payload);
    }
}

/// <summary>IProgramIO sur la console Windows.</summary>
public sealed class ConsoleProgramIO : IProgramIO
{
    public ConsoleProgramIO(string? workingDirectory = null)
    {
        WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory;
    }
    public void Write(string text) => Console.Out.Write(text);
    public string? ReadLine() { Console.Out.Flush(); return Console.In.ReadLine(); }
    public string WorkingDirectory { get; }
}
