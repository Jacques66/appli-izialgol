using System.Globalization;
using System.Windows.Data;

namespace Studio.Ide.Panels;

public sealed class FileNameConverter : IValueConverter
{
    public static readonly FileNameConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        var dir = System.IO.Path.GetDirectoryName(s) ?? "";
        return $"{System.IO.Path.GetFileName(s)}   —   {dir}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Display name of a sample: "Algol 68 — classics/primes.a68" (path relative to
/// samples/simula or samples/algol68, which may contain subfolders).</summary>
public sealed class SampleNameConverter : IValueConverter
{
    public static readonly SampleNameConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        var parts = s.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        // Last "simula" / "algol68" folder segment (excluding the file name itself)
        int idx = -1;
        for (int i = parts.Length - 2; i >= 0; i--)
            if (parts[i].Equals("simula", StringComparison.OrdinalIgnoreCase) || parts[i].Equals("algol68", StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        if (idx < 0) return System.IO.Path.GetFileName(s);
        var lang = parts[idx].Equals("simula", StringComparison.OrdinalIgnoreCase) ? "Simula 67" : "Algol 68";
        var relative = string.Join("/", parts.Skip(idx + 1));
        return $"{lang} — {relative}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
