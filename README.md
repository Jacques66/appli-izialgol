# Simula 67 & Algol 68 Studio

Environnement de développement complet pour Windows autour de deux langages historiques :

* **Simula 67** (Dahl et Nygaard, 1967) — le premier langage à objets : classes, sous-classes,
  procédures virtuelles, coroutines, simulation à événements discrets (`Simulation`, `SimSet`) ;
* **Algol 68** (van Wijngaarden et al., 1968) — modes, opérateurs définis par l'utilisateur,
  orthogonalité, structures et unions.

Le projet comprend :

| Élément | Fichier | Rôle |
|---|---|---|
| IDE | `Simula67Studio.exe` | éditeur, construction, console, débogueur, aide |
| Compilateur Simula 67 | `simc.exe` | compilation en ligne de commande, exécutables autonomes |
| Compilateur Algol 68 | `a68c.exe` | idem pour Algol 68 |
| Lanceur | `Runner.exe` | base des exécutables autonomes générés (ne s'utilise pas seul) |

Les compilateurs produisent un programme interprété par la machine d'exécution du langage ; un
« exécutable autonome » est une copie de `Runner.exe` à laquelle le programme compilé est ajouté.

## Prérequis

* Windows 10 ou 11 (x64).
* **.NET 8 Desktop Runtime** pour utiliser l'IDE et les outils
  (<https://dotnet.microsoft.com/download/dotnet/8.0>, « .NET Desktop Runtime 8 »).
  Les exécutables générés n'ont besoin que du **.NET 8 Runtime**.
* Pour construire : **SDK .NET 8**.

## Construire

```powershell
# Distribution complète dans dist\ (IDE, Runner, simc, a68c, exemples, documentation)
powershell -ExecutionPolicy Bypass -File .\build.ps1

# Avec les tests et le test de démarrage de l'IDE
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

Contenu de `dist\` :

```
dist\Simula67Studio.exe   dist\Runner.exe   dist\simc.exe   dist\a68c.exe
dist\samples\simula\*.sim dist\samples\algol68\*.a68
dist\docs\simula.md       dist\docs\algol68.md       dist\README.md
```

Pour le développement, `dotnet build Simula67Studio.sln` suffit : chaque build de `src\Runner`
publie aussi `src\Runner\bin\<Configuration>\single\Runner.exe` (mono-fichier), que l'IDE et les
compilateurs trouvent automatiquement en remontant l'arborescence. La variable d'environnement
`SIMULA67_RUNNER` permet d'imposer un autre `Runner.exe`.

## Utiliser l'IDE

Lancez `Simula67Studio.exe` (éventuellement avec des fichiers ou un dossier en argument).

### Éditeur

* Onglets multiples ; un point `•` signale un fichier modifié ; confirmation avant de fermer un
  fichier non enregistré ; fichiers rechargés s'ils sont modifiés par un autre programme.
* **Coloration syntaxique** :
  * Simula 67 — mots-clés insensibles à la casse ; commentaires `comment … ;`, `! … ;` et texte
    après `end` (jusqu'à `;`, `end`, `else`, `when`, `otherwise`) ; chaînes `"…"` ; caractères `'c'`.
  * Algol 68 — stropping UPPER (mots en majuscules en gras) et QUOTE (`'BEGIN'`) ; commentaires
    `CO … CO`, `COMMENT … COMMENT`, `# … #` ; pragmats `PR … PR` ; chaînes.
* Numéros de ligne, surlignage de la ligne courante, **repliage** (begin/end, procédures et
  classes ; BEGIN/END, IF/FI, DO/OD, CASE/ESAC, parenthèses multi-lignes), **appariement** des
  parenthèses et des blocs, **indentation automatique**, **complétion** (Ctrl+Espace : mots-clés,
  procédures standard, identificateurs du fichier).
* Rechercher / remplacer (texte, mot entier, casse, expression régulière), aller à la ligne,
  commenter / décommenter, réindenter, zoom (Ctrl+molette).
* Le langage est déterminé par l'extension (`.sim`, `.a68`) et peut être changé (menu
  *Construire › Langage du fichier* ou clic sur la barre d'état).
* Panneaux redimensionnables : **explorateur** de dossier (à gauche), **structure** du fichier
  (procédures, classes, PROC/OP/MODE — clic pour y aller, filtre), et en bas **Sortie**,
  **Erreurs**, **Console**, **Pile d'appels**, **Variables**.
* Glisser-déposer de fichiers ou de dossiers ; fichiers et dossiers récents ; menu **Exemples**
  (dossier `samples`) ; thèmes **clair** et **sombre**.
* La session (onglets, position du curseur, points d'arrêt, disposition, options) est restaurée
  au démarrage. Paramètres : `%APPDATA%\Simula67Studio\settings.json` (journal technique :
  `ide.log` dans le même dossier).

### Construire, exécuter

* **Construire** (F7) : diagnostics dans la Sortie et la liste d'erreurs (double-clic → ligne et
  colonne), soulignement ondulé dans l'éditeur (infobulle au survol). Une vérification en
  arrière-plan souligne les erreurs pendant la frappe (désactivable).
* **Exécuter** (Ctrl+F5) : le programme tourne dans la **console intégrée** — sortie en continu,
  saisie de lignes (Entrée), fin de fichier (Ctrl+Z ou bouton), historique (↑/↓), **Arrêter**
  (Maj+F5).
* Option **Exécuter dans une console Windows externe** (bouton de la barre d'outils ou menu
  Déboguer) : l'exécutable est généré dans un dossier temporaire et lancé dans une vraie console.
* **Générer un .exe autonome** (Ctrl+Maj+E) : exécutable Windows unique, copiable seul.

### Déboguer

* Points d'arrêt par clic dans la marge ou F9 (pastilles rouges, mémorisés par fichier).
* **F5** démarre le débogage / continue ; **F11** pas à pas détaillé ; **F10** pas à pas
  principal ; **Maj+F11** pas à pas sortant ; **Ctrl+Alt+Pause** pause ; **Maj+F5** arrêter.
* À chaque arrêt : flèche et surlignage jaune de la ligne courante, **pile d'appels**
  (double-clic sur un appelant pour afficher sa ligne et ses variables), **variables** en arbre
  (les objets et tableaux se déplient à la demande).
* Arrêt sur erreur d'exécution avec le message, la pile et les variables au moment de l'erreur.

### Raccourcis clavier

| Raccourci | Action | Raccourci | Action |
|---|---|---|---|
| Ctrl+N | Nouveau Simula 67 | Ctrl+Maj+N | Nouveau Algol 68 |
| Ctrl+O | Ouvrir | Ctrl+S / Ctrl+Maj+S | Enregistrer / tout |
| Ctrl+W | Fermer l'onglet | Ctrl+F / Ctrl+H | Rechercher / remplacer |
| F3 / Maj+F3 | Suivant / précédent | Ctrl+G | Aller à la ligne |
| Ctrl+Espace | Compléter | Ctrl+K / Ctrl+Maj+K | Commenter / décommenter |
| Ctrl+I | Réindenter | Ctrl+M, Ctrl+O / Ctrl+L | Replier / déplier tout |
| Ctrl++ / Ctrl+- / Ctrl+0 | Zoom | F1 | Aide du langage courant |
| F7 | Construire | Ctrl+Maj+E | Générer un .exe |
| F5 | Déboguer / continuer | Ctrl+F5 | Exécuter |
| Maj+F5 | Arrêter | Ctrl+Maj+F5 | Redémarrer |
| F9 | Point d'arrêt | Ctrl+Maj+F9 | Supprimer tous les points d'arrêt |
| F10 / F11 / Maj+F11 | Pas à pas principal / détaillé / sortant | Ctrl+Alt+O/E/C/P/V | Panneaux |

### Aide

Le menu **Aide** affiche `docs/simula.md` et `docs/algol68.md` (cherchés à côté de l'exécutable
puis dans les dossiers parents), la liste des raccourcis et un guide de l'environnement.

## Ligne de commande

```
simc fichier.sim [-o sortie.exe] [--run] [--no-debug]
a68c fichier.a68 [-o sortie.exe] [--run] [--no-debug]
```

| Option | Effet |
|---|---|
| (aucune) | compile et affiche les diagnostics |
| `-o sortie.exe` | produit un exécutable autonome (`Runner.exe` doit être à côté de l'outil) |
| `--run`, `-r` | exécute le programme dans la console après compilation (diagnostics sur stderr) |
| `--no-debug` | sans informations de mise au point |
| `--version`, `--help` | version, aide |

Les diagnostics ont la forme `fichier(ligne,colonne): erreur: message`. Codes de retour :
0 succès, 1 erreur de compilation, 2 erreur d'utilisation, 3 erreur interne, 4 génération de
l'exécutable impossible ; avec `--run`, le code de sortie du programme.

Un exécutable généré affiche une erreur d'exécution sur la sortie d'erreur
(`fichier(l,c): erreur d'execution: message` suivi de la pile d'appels) et rend un code non nul.
Ctrl+C l'interrompt.

## Tests

```powershell
dotnet run --project tests\IdeTests              # outillage, débogueur, exe autonome, CLI, éditeur
dotnet run --project tests\IdeTests -- --samples # + compilation/exécution des exemples
Simula67Studio.exe --smoke-test --screenshot capture.png   # démarrage + capture + fermeture
Simula67Studio.exe --self-test --self-test-dir dossier     # scénario complet de l'IDE
```

`tests\IdeTests` utilise un petit langage de test (« echo ») qui permet de vérifier l'IDE, le
débogueur et les exécutables autonomes indépendamment des vrais compilateurs. L'auto-test de
l'IDE (`--self-test`) pilote la vraie fenêtre : compilation, erreurs, console, débogueur, exe,
éditeur, thème ; puis il exerce les compilateurs Simula 67 et Algol 68 réels. Le résultat est
écrit dans `self-test.log` (code de sortie 0 si tout va bien) avec des captures d'écran.

## Organisation du code

```
src\Common      contrat commun (ILanguageService, IDebugger, ExePackage…)
src\Simula      compilateur et machine d'exécution Simula 67
src\Algol68     compilateur et machine d'exécution Algol 68
src\Runner      lanceur des exe autonomes ; Shared\ = code partagé (registre des langages,
                exécution console, localisation de Runner.exe, langage de test « echo »)
src\SimC        simc (CliMain.cs est partagé avec a68c)
src\A68C        a68c
src\Ide         IDE WPF (AvalonEdit, Markdig)
tests\          tests des compilateurs et de l'outillage
samples\        exemples ; docs\ documentation des langages
```
