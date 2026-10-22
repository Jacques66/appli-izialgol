// Text that the browser inserts without a key press handled by Avalonia (wwwroot/main.js).
//
// Avalonia.Browser 11.3 reads typed text only from the "keydown" events of its hidden <input> (a key whose name is
// one character). Text inserted by the browser itself, an "insertText" input event without such a key, is ignored:
// on-screen and mobile keyboards (key "Unidentified"), the emoji panel, dictation, text pasted by input tools,
// and the "type" actions of browser automation (Chrome DevTools Protocol Input.insertText). main.js passes that
// text here, and it is given to the focused element as if it had been typed.

using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Studio.Ide.Browser;

static partial class TextInput
{
    /// <summary>Raises a text input event on the focused element (main browser thread). Returns true when the text
    /// was taken, so that the browser does not insert it in the hidden input element as well.</summary>
    [JSExport]
    internal static bool Insert(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (Application.Current?.ApplicationLifetime is not ISingleViewApplicationLifetime { MainView: { } view }) return false;
        if (TopLevel.GetTopLevel(view)?.FocusManager?.GetFocusedElement() is not Interactive focused) return false;
        var e = new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text };
        focused.RaiseEvent(e);
        return e.Handled;
    }
}
