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

/// <summary>« simula/bonjour.sim » pour un exemple.</summary>
public sealed class SampleNameConverter : IValueConverter
{
    public static readonly SampleNameConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        var parent = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(s) ?? "");
        var lang = parent switch { "simula" => "Simula 67", "algol68" => "Algol 68", _ => parent };
        return $"{lang} — {System.IO.Path.GetFileName(s)}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
