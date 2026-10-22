// Start-up of AlgolStudio in the browser: functions called by .NET (globalThis.algolStudio), then the .NET runtime.
import { dotnet } from './_framework/dotnet.js';

const status = document.getElementById('status');
const progress = document.getElementById('progress');

function showError(message) {
    status.textContent = message;
    status.classList.add('error');
}

// The view never scrolls (see app.css): undo a scroll made by the browser to show the hidden text input.
const out = document.getElementById('out');
out.addEventListener('scroll', () => { if (out.scrollTop || out.scrollLeft) out.scrollTop = out.scrollLeft = 0; });

// Text inserted by the browser without a key press (TextInput.cs): Avalonia only reads the text of the keydown
// events of its hidden input element, and ignores "insertText" input events (on-screen keyboards, emoji panel,
// dictation, browser automation). A key typed normally is handled at keydown and cancelled, so it produces no
// such event; when it does (cancelled too late), its text is already taken: it is recognised and left alone.
let textInput = null;
let lastKey = null;
document.addEventListener('keydown', e => { lastKey = e.key; }, true);
document.addEventListener('keyup', () => { lastKey = null; }, true);
document.addEventListener('beforeinput', e => {
    const key = lastKey;
    lastKey = null;
    if (e.inputType !== 'insertText' || !e.data || e.data === key || !textInput) return;
    if (!e.target.classList || !e.target.classList.contains('avalonia-input-element')) return;
    if (textInput.Insert(e.data)) e.preventDefault();
}, true);

let unsaved = false;
window.addEventListener('beforeunload', e => {
    if (unsaved) { e.preventDefault(); e.returnValue = ''; }
});

globalThis.algolStudio = {
    setUnsaved: value => { unsaved = value; },
    getItem: key => { try { return localStorage.getItem(key); } catch { return null; } },
    setItem: (key, value) => { try { localStorage.setItem(key, value); return true; } catch { return false; } },
    openUrl: url => { window.open(url, '_blank', 'noopener'); },
    setTitle: title => { document.title = title; },
    prefersDark: () => window.matchMedia('(prefers-color-scheme: dark)').matches,
    hideSplash: () => {
        const splash = document.getElementById('splash');
        if (!splash) return;
        splash.classList.add('hidden');
        setTimeout(() => splash.remove(), 400);
    },
};

if (!globalThis.crossOriginIsolated) {
    // coi-serviceworker.js reloads the page once its service worker is active; nothing to do until then.
    status.textContent = 'Preparing the page…';
    setTimeout(() => showError(
        'AlgolStudio needs a cross-origin isolated page (SharedArrayBuffer, used by the threads of the .NET runtime). ' +
        'Reload the page; if this message stays, the browser blocks service workers (private window?) or is too old.'), 6000);
} else {
    try {
        const runtime = await dotnet
            .withModuleConfig({
                onDownloadResourceProgress: (loaded, total) => {
                    if (total > 0) progress.style.width = Math.round(100 * loaded / total) + '%';
                    status.textContent = `Loading… ${loaded} / ${total}`;
                },
            })
            // Web workers started in advance for the threads of .NET (a thread created on the main browser
            // thread needs a worker that is already loaded): thread pool, programs, debugger, Algol 68 PAR.
            .withConfig({ pthreadPoolSize: 16 })
            .create();
        status.textContent = 'Starting…';
        const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
        textInput = exports.Studio.Ide.Browser.TextInput;
        // Arguments of Main: the "arg" parameters of the address (files to open).
        const args = new URLSearchParams(location.search).getAll('arg');
        await runtime.runMain(runtime.getConfig().mainAssemblyName, args);
    } catch (e) {
        console.error(e);
        showError('AlgolStudio could not start: ' + (e && e.message ? e.message : e));
    }
}
