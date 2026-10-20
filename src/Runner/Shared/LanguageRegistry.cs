// Registre des langages disponibles, partage par Runner, simc, a68c, l'IDE et les tests
// (fichier lie dans ces projets).

using Studio.Algol68;
using Studio.Common;
using Studio.Simula;
using Studio.Tooling.Testing;

namespace Studio.Tooling;

public static class LanguageRegistry
{
    static readonly object Gate = new();
    static ILanguageService[]? _languages;
    static EchoLanguage? _echo;

    /// <summary>Langages reels (Simula 67, Algol 68).</summary>
    public static IReadOnlyList<ILanguageService> Languages
    {
        get
        {
            lock (Gate)
            {
                _languages ??= new ILanguageService[] { new SimulaLanguage(), new Algol68Language() };
                var list = new List<ILanguageService>(_languages);
                if (TestLanguageEnabled) list.Add(Echo);
                return list;
            }
        }
    }

    /// <summary>Active le langage de test « echo » (tests, auto-test de l'IDE).</summary>
    public static bool TestLanguageEnabled { get; set; }

    public static EchoLanguage Echo
    {
        get { lock (Gate) return _echo ??= new EchoLanguage(); }
    }

    /// <summary>Recherche par identifiant. Le langage de test est toujours trouvable par son Id
    /// (pour que Runner puisse executer les .exe generes par les tests).</summary>
    public static ILanguageService? FindById(string id)
    {
        if (string.Equals(id, EchoLanguage.LanguageId, StringComparison.OrdinalIgnoreCase)) return Echo;
        return Languages.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static ILanguageService? FindByExtension(string? pathOrExtension)
    {
        if (string.IsNullOrEmpty(pathOrExtension)) return null;
        var ext = pathOrExtension.StartsWith('.') && pathOrExtension.IndexOfAny(new[] { '\\', '/' }) < 0
            ? pathOrExtension
            : Path.GetExtension(pathOrExtension);
        ext = ext.ToLowerInvariant();
        return Languages.FirstOrDefault(l => l.FileExtensions.Contains(ext));
    }
}
