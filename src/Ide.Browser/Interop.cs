using System.Runtime.InteropServices.JavaScript;

namespace Studio.Ide.Browser;

/// <summary>Functions of the page (wwwroot/main.js, globalThis.algolStudio), called on the main browser thread.</summary>
static partial class Interop
{
    [JSImport("globalThis.algolStudio.getItem")]
    internal static partial string? GetItem(string key);

    /// <summary>Returns false when the browser storage is full or not available.</summary>
    [JSImport("globalThis.algolStudio.setItem")]
    internal static partial bool SetItem(string key, string value);

    [JSImport("globalThis.algolStudio.openUrl")]
    internal static partial void OpenUrl(string url);

    [JSImport("globalThis.algolStudio.setTitle")]
    internal static partial void SetTitle(string title);

    [JSImport("globalThis.algolStudio.prefersDark")]
    internal static partial bool PrefersDark();

    /// <summary>Unsaved changes: the browser asks before leaving the page.</summary>
    [JSImport("globalThis.algolStudio.setUnsaved")]
    internal static partial void SetUnsaved(bool unsaved);

    [JSImport("globalThis.algolStudio.hideSplash")]
    internal static partial void HideSplash();
}
