using System.Text;

namespace Studio.Common;

/// <summary>
/// Layout of a standalone .exe: [Runner.exe][payload][langId: 16 UTF-8 bytes, zero padded]
/// [payload length: int64 little-endian][magic "S67A68EX": 8 bytes].
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
        if (raw.Length > LangIdSize) throw new ArgumentException("language id too long");
        raw.CopyTo(id, 0);
        output.Write(id);
        output.Write(BitConverter.GetBytes((long)payload.Length));
        output.Write(Magic);
    }

    /// <summary>Reads the payload appended to an exe; null if there is none.</summary>
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

/// <summary>IProgramIO over the Windows console.</summary>
public sealed class ConsoleProgramIO : IProgramIO
{
    public ConsoleProgramIO(string? workingDirectory = null)
    {
        WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory;
    }
    // Console.Out flushes after each write (AutoFlush): every frame of an animation is shown at once.
    public void Write(string text) => Console.Out.Write(text);
    public string? ReadLine() { Console.Out.Flush(); return Console.In.ReadLine(); }
    public string WorkingDirectory { get; }
}
