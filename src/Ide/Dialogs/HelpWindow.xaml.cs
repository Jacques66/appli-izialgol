using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Studio.Ide.Services;

namespace Studio.Ide.Dialogs;

/// <summary>Aide : rendu des fichiers docs/simula.md et docs/algol68.md (Markdig → HTML).</summary>
public partial class HelpWindow : Window
{
    public sealed record TocEntry(string Title, string Id, int Level)
    {
        public Thickness Indent => new((Level - 1) * 12, 1, 0, 1);
    }

    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    string _currentTopic = "";
    string? _pendingAnchor;

    public HelpWindow()
    {
        InitializeComponent();
        Browser.Loaded += (_, _) => SuppressScriptErrors();
    }

    public void ShowTopic(string topic)
    {
        foreach (ListBoxItem item in TopicList.Items)
            if ((string)item.Tag == topic) { TopicList.SelectedItem = item; return; }
        TopicList.SelectedIndex = 0;
    }

    void OnTopicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TopicList.SelectedItem is ListBoxItem item) Load((string)item.Tag);
    }

    void Load(string topic)
    {
        _currentTopic = topic;
        string markdown;
        string title;
        switch (topic)
        {
            case "simula":
            case "algol68":
            {
                title = topic == "simula" ? "Simula 67" : "Algol 68";
                var docs = PathLocator.DocsDirectory;
                var file = docs == null ? null : System.IO.Path.Combine(docs, topic + ".md");
                if (file != null && System.IO.File.Exists(file))
                    markdown = System.IO.File.ReadAllText(file);
                else
                    markdown = $"# {title}\n\nLa documentation `docs/{topic}.md` est introuvable.\n\n" +
                               "Elle est recherchée dans le dossier `docs` à côté de l'application, puis dans les dossiers parents.";
                break;
            }
            case "shortcuts":
                title = "Raccourcis clavier";
                markdown = ShortcutsMarkdown;
                break;
            default:
                title = "Utiliser l'environnement";
                markdown = IdeMarkdown;
                break;
        }
        Title = $"Aide — {title}";
        var doc = Markdown.Parse(markdown, Pipeline);
        var toc = new List<TocEntry>();
        foreach (var h in doc.Descendants<HeadingBlock>())
        {
            if (h.Level > 3) continue;
            var id = h.GetAttributes().Id;
            var text = InlineText(h);
            if (!string.IsNullOrEmpty(id)) toc.Add(new TocEntry(text, id, h.Level));
        }
        TocList.ItemsSource = toc;
        var body = doc.ToHtml(Pipeline);
        Browser.NavigateToString(WrapHtml(body, title));
    }

    static string InlineText(HeadingBlock h)
    {
        var sb = new StringBuilder();
        if (h.Inline != null)
            foreach (var inline in h.Inline.Descendants())
                if (inline is Markdig.Syntax.Inlines.LiteralInline lit) sb.Append(lit.Content.ToString());
                else if (inline is Markdig.Syntax.Inlines.CodeInline code) sb.Append(code.Content);
        return sb.ToString();
    }

    static string WrapHtml(string body, string title)
    {
        bool dark = App.IsDarkTheme;
        string bg = dark ? "#1E1F22" : "#FFFFFF", fg = dark ? "#D4D4D4" : "#1E1E1E", dim = dark ? "#8B919B" : "#6B7280";
        string codeBg = dark ? "#2A2C30" : "#F3F4F6", border = dark ? "#3C3F45" : "#D9DDE3", link = dark ? "#5EA4EE" : "#0063B1";
        string h = dark ? "#9CDCFE" : "#0B4F8A";
        var html = $@"<!DOCTYPE html>
<html><head><meta http-equiv=""X-UA-Compatible"" content=""IE=edge""><meta charset=""utf-8"">
<title>{title}</title>
<style>
body {{ background:{bg}; color:{fg}; font-family:'Segoe UI',sans-serif; font-size:14px; line-height:1.55; margin:18px 28px 60px 28px; }}
h1,h2,h3,h4 {{ color:{h}; font-weight:600; margin-top:1.4em; }}
h1 {{ font-size:26px; border-bottom:1px solid {border}; padding-bottom:6px; margin-top:0.4em; }}
h2 {{ font-size:20px; border-bottom:1px solid {border}; padding-bottom:4px; }}
h3 {{ font-size:16px; }}
a {{ color:{link}; }}
code {{ font-family:'Cascadia Mono',Consolas,monospace; font-size:13px; background:{codeBg}; padding:1px 4px; border-radius:3px; }}
pre {{ background:{codeBg}; border:1px solid {border}; border-radius:4px; padding:10px 12px; overflow:auto; }}
pre code {{ background:transparent; padding:0; }}
table {{ border-collapse:collapse; margin:10px 0; }}
th, td {{ border:1px solid {border}; padding:5px 10px; text-align:left; vertical-align:top; }}
th {{ background:{codeBg}; }}
blockquote {{ border-left:4px solid {border}; margin:0; padding:2px 14px; color:{dim}; }}
kbd {{ border:1px solid {border}; border-radius:3px; padding:0 4px; background:{codeBg}; font-family:Consolas,monospace; }}
</style>
<script>function goTo(id) {{ var e = document.getElementById(id); if (e) {{ e.scrollIntoView(true); }} }}</script>
</head><body>{body}</body></html>";
        // WebBrowser.NavigateToString gere mal les caracteres non ASCII : entites numeriques.
        var sb = new StringBuilder(html.Length + 1024);
        foreach (var ch in html)
            if (ch < 128) sb.Append(ch);
            else sb.Append("&#").Append((int)ch).Append(';');
        return sb.ToString();
    }

    void OnTocClick(object sender, MouseButtonEventArgs e) => GoToSelected();

    void OnTocKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) GoToSelected();
    }

    void GoToSelected()
    {
        if (TocList.SelectedItem is TocEntry entry) ScrollTo(entry.Id);
    }

    void ScrollTo(string id)
    {
        try { Browser.InvokeScript("goTo", id); }
        catch { _pendingAnchor = id; }
    }

    void OnLoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        if (_pendingAnchor != null)
        {
            var a = _pendingAnchor;
            _pendingAnchor = null;
            ScrollTo(a);
        }
    }

    void OnNavigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        var uri = e.Uri;
        if (uri == null) return;
        // Ancres internes (#id) et liens vers l'autre fichier d'aide
        var s = uri.OriginalString;
        if (s.StartsWith("about:", StringComparison.OrdinalIgnoreCase) && s.Contains('#'))
        {
            e.Cancel = true;
            ScrollTo(s[(s.IndexOf('#') + 1)..]);
            return;
        }
        if (s.EndsWith("simula.md", StringComparison.OrdinalIgnoreCase) || s.Contains("simula.md#")) { e.Cancel = true; ShowTopic("simula"); return; }
        if (s.EndsWith("algol68.md", StringComparison.OrdinalIgnoreCase) || s.Contains("algol68.md#")) { e.Cancel = true; ShowTopic("algol68"); return; }
        if (uri.Scheme is "http" or "https" or "mailto")
        {
            e.Cancel = true;
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        }
    }

    void SuppressScriptErrors()
    {
        try
        {
            var field = typeof(WebBrowser).GetField("_axIWebBrowser2", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var ax = field?.GetValue(Browser);
            ax?.GetType().InvokeMember("Silent", System.Reflection.BindingFlags.SetProperty, null, ax, new object[] { true });
        }
        catch { }
    }

    public const string ShortcutsMarkdown = @"# Raccourcis clavier

## Fichiers

| Raccourci | Action |
|---|---|
| <kbd>Ctrl+N</kbd> | Nouveau programme Simula 67 |
| <kbd>Ctrl+Maj+N</kbd> | Nouveau programme Algol 68 |
| <kbd>Ctrl+O</kbd> | Ouvrir |
| <kbd>Ctrl+S</kbd> | Enregistrer |
| <kbd>Ctrl+Maj+S</kbd> | Tout enregistrer |
| <kbd>Ctrl+W</kbd>, <kbd>Ctrl+F4</kbd> | Fermer l'onglet (clic du milieu sur l'onglet également) |

## Édition

| Raccourci | Action |
|---|---|
| <kbd>Ctrl+Z</kbd> / <kbd>Ctrl+Y</kbd> | Annuler / rétablir |
| <kbd>Ctrl+F</kbd> | Rechercher (texte ou expression régulière) |
| <kbd>Ctrl+H</kbd> | Remplacer |
| <kbd>F3</kbd> / <kbd>Maj+F3</kbd> | Occurrence suivante / précédente |
| <kbd>Ctrl+G</kbd> | Aller à la ligne |
| <kbd>Ctrl+Espace</kbd> | Compléter le mot (mots-clés, procédures standard, identificateurs du fichier) |
| <kbd>Ctrl+K</kbd> / <kbd>Ctrl+Maj+K</kbd> | Commenter / décommenter les lignes sélectionnées |
| <kbd>Ctrl+I</kbd> | Réindenter la sélection |
| <kbd>Ctrl+M</kbd>, <kbd>Ctrl+O</kbd> | Replier tous les blocs |
| <kbd>Ctrl+M</kbd>, <kbd>Ctrl+L</kbd> | Déplier tous les blocs |
| <kbd>Ctrl+M</kbd>, <kbd>Ctrl+M</kbd> | Replier / déplier le bloc courant |
| <kbd>Ctrl++</kbd> / <kbd>Ctrl+-</kbd> / <kbd>Ctrl+0</kbd> | Zoom avant / arrière / normal (ou Ctrl+molette) |
| <kbd>Inser</kbd> | Mode insertion / refrappe |

## Construire, exécuter, déboguer

| Raccourci | Action |
|---|---|
| <kbd>F7</kbd>, <kbd>Ctrl+Maj+B</kbd> | Construire (compiler le fichier courant) |
| <kbd>Ctrl+Maj+E</kbd> | Générer un .exe autonome |
| <kbd>F5</kbd> | Démarrer le débogage / continuer |
| <kbd>Ctrl+F5</kbd> | Exécuter sans débogage |
| <kbd>Maj+F5</kbd> | Arrêter |
| <kbd>Ctrl+Maj+F5</kbd> | Redémarrer |
| <kbd>Ctrl+Alt+Pause</kbd> | Pause |
| <kbd>F9</kbd> | Basculer le point d'arrêt (ou clic dans la marge) |
| <kbd>Ctrl+Maj+F9</kbd> | Supprimer tous les points d'arrêt |
| <kbd>F11</kbd> | Pas à pas détaillé |
| <kbd>F10</kbd> | Pas à pas principal |
| <kbd>Maj+F11</kbd> | Pas à pas sortant |

## Console intégrée

| Raccourci | Action |
|---|---|
| <kbd>Entrée</kbd> | Envoyer la ligne au programme |
| <kbd>Ctrl+Z</kbd> | Fin de fichier |
| <kbd>↑</kbd> / <kbd>↓</kbd> | Historique des lignes saisies |

## Panneaux

| Raccourci | Action |
|---|---|
| <kbd>Ctrl+Alt+O</kbd> | Sortie |
| <kbd>Ctrl+Alt+E</kbd> | Liste d'erreurs |
| <kbd>Ctrl+Alt+C</kbd> | Console |
| <kbd>Ctrl+Alt+P</kbd> | Pile d'appels |
| <kbd>Ctrl+Alt+V</kbd> | Variables |
| <kbd>F1</kbd> | Aide sur le langage du fichier courant |
";

    public const string IdeMarkdown = @"# Utiliser l'environnement

## Écrire un programme

* **Fichier › Nouveau programme Simula 67** (<kbd>Ctrl+N</kbd>) ou **Algol 68** (<kbd>Ctrl+Maj+N</kbd>) crée un onglet avec un gabarit.
* Le langage est déterminé par l'extension (`.sim` ou `.a68`) ; il peut être changé dans **Construire › Langage du fichier** ou en cliquant sur le nom du langage dans la barre d'état.
* Le menu **Exemples** ouvre les programmes du dossier `samples`.
* Les fichiers peuvent être glissés-déposés dans la fenêtre ; un dossier déposé devient la racine de l'explorateur.
* La session (onglets ouverts, points d'arrêt, position du curseur, disposition) est restaurée au démarrage.

## Simula 67

Mots-clés insensibles à la casse. Commentaires : `comment … ;`, `! … ;` et le texte qui suit `end` jusqu'au prochain `;`, `end`, `else`, `when` ou `otherwise`.

## Algol 68

Stropping UPPER : les mots en majuscules (`BEGIN`, `INT`, `PROC`…) sont des mots gras ; le stropping QUOTE (`'BEGIN'`) est aussi reconnu.
Commentaires : `CO … CO`, `COMMENT … COMMENT`, `# … #`.

## Construire et exécuter

* **Construire** (<kbd>F7</kbd>) compile le fichier courant : les diagnostics apparaissent dans la **Sortie**, dans la **liste d'erreurs** (double-clic pour aller à la ligne) et sont soulignés dans l'éditeur. Une vérification en arrière-plan souligne les erreurs pendant la frappe.
* **Exécuter** (<kbd>Ctrl+F5</kbd>) lance le programme dans la **console intégrée** : la sortie s'affiche au fur et à mesure, les lignes saisies sont envoyées au programme, <kbd>Ctrl+Z</kbd> envoie la fin de fichier, **Arrêter** (<kbd>Maj+F5</kbd>) interrompt l'exécution.
* L'option **Exécuter dans une console Windows externe** (bouton de la barre d'outils) ouvre une vraie fenêtre de console.
* **Générer un .exe autonome** produit un exécutable Windows qui peut être copié et lancé seul (le runtime .NET 8 doit être installé).

## Déboguer

* Cliquez dans la marge gauche (ou <kbd>F9</kbd>) pour placer un point d'arrêt (pastille rouge).
* <kbd>F5</kbd> démarre le débogage ; à l'arrêt, la ligne courante est surlignée en jaune avec une flèche.
* <kbd>F11</kbd> entre dans les procédures, <kbd>F10</kbd> passe par-dessus, <kbd>Maj+F11</kbd> sort de la procédure courante.
* La **pile d'appels** (double-clic pour afficher un appelant) et les **variables** (arbre dépliable) sont mises à jour à chaque arrêt.
* Une erreur d'exécution arrête le programme sur la ligne fautive (option **S'arrêter sur erreur d'exécution**).

## Ligne de commande

```
simc programme.sim [-o programme.exe] [--run] [--no-debug]
a68c programme.a68 [-o programme.exe] [--run] [--no-debug]
```
";
}
