using Avalonia.Media;

namespace Studio.Ide.Editor;

/// <summary>
/// Font of the editor from the settings ("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace").
/// With a variable font (such as the Cascadia Mono of Windows 11), the Skia font manager of Avalonia 11 returns
/// one face for every weight, and the bold simulation of the first weight requested sticks to it: either the bold
/// words of Algol 68 look regular, or all the text looks bold. Such a family is skipped when a later family of
/// the list is installed.
/// </summary>
public static class CodeFont
{
    static readonly Dictionary<string, FontFamily> Cache = new(StringComparer.Ordinal);
    const uint FvarTag = 0x66766172; // 'fvar': font variations table

    public static FontFamily Resolve(string families)
    {
        if (Cache.TryGetValue(families, out var cached)) return cached;
        var names = families.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        int first = 0;
        try
        {
            while (first < names.Count - 1 && IsVariable(names[first]) && names.Skip(first + 1).Any(IsInstalled)) first++;
        }
        catch { first = 0; }
        var family = new FontFamily(string.Join(", ", names.Skip(first)));
        Cache[families] = family;
        return family;
    }

    static bool IsInstalled(string name) =>
        FontManager.Current.TryGetGlyphTypeface(new Typeface(name), out var t) &&
        string.Equals(t.FamilyName, name, StringComparison.OrdinalIgnoreCase);

    static bool IsVariable(string name) =>
        FontManager.Current.TryGetGlyphTypeface(new Typeface(name), out var t) &&
        string.Equals(t.FamilyName, name, StringComparison.OrdinalIgnoreCase) &&
        t.TryGetTable(FvarTag, out _);
}
