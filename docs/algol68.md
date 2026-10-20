# Algol 68 — guide de référence

Ce document décrit le compilateur et l'environnement d'exécution **Algol 68** de Simula67 Studio.
Le langage implémenté est celui du *Revised Report on the Algorithmic Language Algol 68*
(Van Wijngaarden et al., 1975), avec la syntaxe et le comportement d'**Algol 68 Genie** (a68g)
comme référence pratique : un programme écrit pour a68g se compile en général tel quel, et les
sorties de `print`, `whole`, `fixed`, `float` et des formats sont identiques caractère pour
caractère à celles d'a68g.

Les exemples cités se trouvent dans `samples/algol68` (menu *Exemples* de l'IDE) :
`classiques/` (hello, factorielle, crible, Hanoï, tri rapide, huit reines, *man or boy*),
`fonctionnalites/` (un programme par grande fonctionnalité), `erreurs/` (erreurs de compilation
attendues) et `execution/` (erreurs d'exécution attendues).

---

## 1. Premier programme

```algol68
BEGIN
   print(("Bonjour, le monde !", newline))
END
```

Un programme est une *clause suite* : `BEGIN … END` est facultatif (comme dans a68g),
`print(("x = ", x, newline))` suffit.

- Extension de fichier : `.a68`.
- Les messages du compilateur et de l'exécution sont en français, avec ligne et colonne.
- Plusieurs erreurs sont signalées par compilation (au plus 200).

---

## 2. Représentation du programme

### 2.1 Mots gras (stropping)

| Régime | Exemple | Activation |
|---|---|---|
| **UPPER** (défaut, comme a68g) | `BEGIN INT n := 1; ... END` | par défaut |
| **QUOTE** | `'BEGIN' 'INT' n := 1; ... 'END'` | détecté automatiquement, ou `PR quote PR` |

En stropping UPPER, les mots en majuscules sont des mots gras (mots-clés, indicants de mode,
opérateurs) et les identificateurs s'écrivent en minuscules (chiffres et `_` admis).
En stropping QUOTE, les mots gras sont entre apostrophes et les identificateurs peuvent
contenir des majuscules ; ils ne distinguent alors pas la casse (`Mot` = `mot`).
`PR upper PR` revient au stropping UPPER.

### 2.2 Identificateurs

Les espaces à l'intérieur d'un identificateur sont ignorés : `max int`, `read int`,
`long long pi` désignent `maxint`, `readint`, `longlongpi`.

### 2.3 Commentaires et pragmats

- Commentaires : `CO … CO`, `COMMENT … COMMENT`, `# … #`, `¢ … ¢`.
- Pragmats : `PR … PR`, `PRAGMAT … PRAGMAT`.
  - `PR include "fichier.a68" PR` (ou `PR read "…" PR`) insère un fichier (résolu par l'IDE
    ou la ligne de commande relativement au fichier qui l'inclut) ;
  - `PR quote PR` / `PR upper PR` : régime de mots gras ;
  - `PR precision = 100 PR` : nombre de chiffres de `LONG LONG INT`/`LONG LONG REAL` ;
  - les autres pragmats sont ignorés.

### 2.4 Dénotations

| Mode | Exemples |
|---|---|
| `INT` | `0`, `42`, `1 000 000` (espaces permis entre chiffres) |
| `REAL` | `3.14`, `.5`, `1e3`, `1.5e-3`, `1.5E-3`, `1.5\e-3`, `2\3` |
| `BITS` | `2r1010`, `4r123`, `8r17`, `16rff` |
| `CHAR` | `"a"`, `""""` (guillemet) |
| chaîne `[]CHAR` | `"abc"`, `"a""b"`, `"con" "caténation"` (dénotations adjacentes, extension a68g) |
| `BOOL` | `TRUE`, `FALSE` |
| `VOID` | `EMPTY` |
| noms, divers | `NIL`, `SKIP` |
| longueurs | `LONG 1`, `LONG LONG 2.5`, `LONG 16rffff` |

Une dénotation d'une longueur n'est **pas** élargie à une autre longueur (règle du RR) :
`LONG INT x := 1` est refusé, il faut écrire `LONG INT x := LONG 1` ou `LENG 1`.

### 2.5 Symboles

Opérateurs symboliques formés selon le RR : un caractère parmi `+ - ! ? % ^ & ~` ou
`< > / = *`, éventuellement suivi d'un caractère parmi `< > / = *`, éventuellement suivi de
`:=` ou `=:` (`+:=`, `%*:=`, `+=:`, `<=`, `/=`, `**`, `%*`, `+*`…).
`|`, `|:` et `!` (forme alternative de `|`) servent aux clauses abrégées ; `@` est l'équivalent
de `AT`. Les crochets `[ ]` des tranches peuvent être remplacés par `( )` comme dans le RR.
Quelques caractères Unicode sont acceptés : `×` `÷` `≤` `≥` `≠` `¬` `∧` `∨`.

---

## 3. Modes

### 3.1 Modes primitifs et longueurs

| Mode | Représentation | Précision / largeur |
|---|---|---|
| `INT` | entier 32 bits | `max int` = 2147483647, `int width` = 10 |
| `LONG INT` | entier multiprécision | 35 chiffres (`long max int` = 10^35 − 1), `long int width` = 36 |
| `LONG LONG INT` | entier multiprécision | 70 chiffres par défaut (`PR precision PR`), `long long int width` = 71 |
| `REAL` | double IEEE 754 | `real width` = 15, `exp width` = 3, `small real` ≈ 2,22e-16 |
| `LONG REAL` | décimal multiprécision | 35 chiffres significatifs, affichés sur `long real width` = 28 |
| `LONG LONG REAL` | décimal multiprécision | 70 chiffres (affichés sur 63), réglable par `PR precision PR` |
| `BITS`, `LONG BITS`, `LONG LONG BITS` | | 32, 116, 232 bits |
| `BYTES`, `LONG BYTES` | | 32, 256 caractères |
| `COMPL`, `LONG COMPL`, `LONG LONG COMPL` | `STRUCT (L REAL re, im)` | |
| `BOOL`, `CHAR`, `VOID`, `STRING` | `STRING = FLEX [1:0] CHAR` | `max abs char` = 65535 |
| `FORMAT`, `FILE`, `CHANNEL`, `SEMA` | | |

`SHORT INT`, `SHORT REAL`… sont acceptés et ont la même longueur que le mode non qualifié
(`int shorths` = 1). `COMPLEX` est un synonyme de `COMPL`.

Les fonctions transcendantes de `LONG REAL` et `LONG LONG REAL` (`long sqrt`, `long exp`,
`long ln`, `long sin`… et `long pi`) sont calculées à pleine précision (séries en virgule fixe
avec chiffres de garde).

### 3.2 Modes construits

- `REF M`, `PROC M`, `PROC (M1, M2) M`, `STRUCT (M1 a, b, M2 c)`, `UNION (M1, M2, …)`,
  rangées `[] M`, `[,] M`, `[1:n, 0:m] M`, `FLEX [1:0] M`, rangées de rangées `[3][2] INT`.
- `MODE` : `MODE POINT = STRUCT (REAL x, y)`, modes récursifs
  (`MODE NOEUD = STRUCT (INT v, REF NOEUD suivant)`), récursion mutuelle
  (`MODE LISTE = REF CELLULE, CELLULE = STRUCT (INT tete, LISTE queue)`), bornes effectives
  (`MODE VEC = [1:n] REAL` : les bornes sont évaluées à chaque génération).
- **Équivalence structurelle** (RR 7.3) : deux modes de même structure sont le même mode,
  y compris pour les modes récursifs ; les unions sont aplaties et l'ordre de leurs
  composantes est indifférent.
- **Unions** : une union ne peut pas contenir deux composantes fermement liées
  (`UNION (INT, REF INT)` est refusé, RR 4.7.1).
- **Modes bien formés** (RR 7.4) : tout cycle doit passer par un `REF` ou `PROC` *et* par un
  `STRUCT` ou `PROC` ; `MODE M = STRUCT (INT i, M m)` et `MODE L = UNION (VOID, REF L)` sont
  refusés.

---

## 4. Coercitions et contextes

Toutes les coercitions du RR sont implémentées, dans les cinq contextes :

| Coercition | Exemple | Contextes |
|---|---|---|
| deproceduring | `INT a = suivant` (`PROC INT suivant`) | tous |
| dereferencing | `INT c = r` (`REF INT r`), plusieurs niveaux | weak, meek, firm, strong |
| uniting | `UNION (INT, BOOL) u = TRUE` | firm, strong |
| widening | `INT → REAL → COMPL`, `BITS → []BOOL`, `BYTES → []CHAR` (et longueurs) | strong |
| rowing | `[] INT un = 7`, `[,] INT m = un`, `REF [] INT r = x` | strong |
| voiding | `x := 1;`, `p;` (appel de `PROC VOID`) | strong |

- **Contextes** : soft (destination d'affectation), weak (primaire d'une tranche, d'une
  sélection), meek (conditions, indices, bornes, primaire d'un appel), firm (opérandes),
  strong (sources, paramètres, résultats).
- **Équilibrage** (balancing) des clauses conditionnelles, de cas et de conformité dans les
  contextes non forts : `(a < b | 1 | 2.5) * 2` vaut `2.0`. En contexte fort chaque branche
  est convertie vers le mode requis (par exemple dans `print`, `IF … THEN 1 ELSE 2.5 FI` imprime
  l'entier `1`).
- `FLEX` n'est une propriété que des noms : le mode d'une valeur obtenue par
  déréférencement d'un `REF FLEX [] CHAR` est `[] CHAR`.
- Relation d'identité (`IS`, `ISNT`, `:=:`, `:/=:`) : les deux noms sont comparés après
  deproceduring, selon la règle du RR et d'a68g ; `p IS NIL` compare donc la *variable* `p`
  (toujours différente de `NIL`) : écrire `REF NOEUD (p) IS NIL` pour comparer sa valeur.

---

## 5. Clauses et unités

- **Clause suite** avec déclarations, étiquettes (`l: unité`, après les déclarations) et
  `EXIT` (`(TRUE | 10 EXIT un: 20)` vaut 10).
- **Clauses collatérales** `(a, b)` (exécutées dans l'ordre), **affichages** de rangées
  et de structures, `()` rangée vide.
- **Clause parallèle** `PAR (u1, u2, …)` / `PAR BEGIN … END` : chaque unité s'exécute dans un
  **vrai fil** (thread .NET) ; voir §8.
- **Conditionnelles** `IF … THEN … ELIF … ELSE … FI` et `( c | a |: c2 | b | d )` ;
  les déclarations faites dans la condition sont visibles dans les branches.
- **Cas** `CASE n IN a, b, c OUSE m IN … OUT … ESAC` et `( n | a, b, c | d )`.
- **Conformité** `CASE u IN (INT i): …, (REAL): …, (UNION (CHAR, BOOL) x): … OUT … ESAC`,
  forme abrégée `( u | (INT i): … | … )`, `OUSE` ; une seule unité peut porter plusieurs
  modes (extension : `(INT, REAL x): …` équivaut à une union).
- **Boucles** `FOR i FROM a BY b TO c WHILE cond DO … OD`, toutes combinaisons et parties
  facultatives ; `DOWNTO` (a68g) ; `UNTIL cond` juste avant `OD` (a68g) ; boucle nue `DO … OD`.
  Le compteur est une identité locale à chaque tour.
- **Sauts** `GOTO l`, `GO TO l`, ou l'étiquette seule `l` ; un saut peut sortir de
  procédures et de récursions ; une étiquette en contexte `PROC VOID` devient une procédure
  qui saute (`appeler(sortie)`).
- **Déclarations** : identités (`INT n = 3`), variables (`INT x := 1`, `LOC`, `HEAP`, `NEW`),
  procédures (`PROC f = (INT x) INT: …`, `PROC f := …`), `MODE`, `OP`, `PRIO` ;
  déclarations jointes (`INT a = 1, REAL b = 2.0`) ; identificateurs visibles dans toute
  leur portée (récursion mutuelle).
- **Unités** : affectations (multiples `a := b := 0`, destination choisie
  `(c | a | b) := 0`), générateurs `LOC`/`HEAP`, textes de routine, appels, tranches
  (`a[2:5]`, `a[2:5 @ 0]`, `a[@0]`, `a[i, ]`, `a[, j]`), sélections (`x OF p`, y compris
  sur une rangée de structures : `x OF points` est une rangée), casts (`REAL (1)`),
  `LWB`/`UPB` monadiques et dyadiques, `ELEMS` (a68g).
- **Extensions a68g** : `ANDF`/`ANDTH` et `ORF`/`OREL` (évaluation paresseuse),
  `ASSERT (condition)`, `stop`, paramétrage partiel `f(1, )` (fournit une procédure des
  paramètres manquants).

### 5.1 Procédures et portées

Les textes de routine sont des fermetures. La **portée** (RR 2.1.3.2) est contrôlée à
l'exécution : affecter le nom d'une variable locale à une variable plus ancienne, ou rendre
le nom d'une variable locale comme résultat d'une routine ou d'un bloc, provoque l'erreur
« violation de portée ». La portée d'une routine est celle de la portée la plus interne
dont elle utilise un identificateur : une routine qui n'utilise que des globaux peut être
rendue en résultat (`PROC fabrique = (BOOL d) PROC (INT) INT: …`).

### 5.2 Noms transitoires

Une tranche ou une sélection multiple d'un nom `FLEX` (`s[2]`, `s[2:3]` pour un `STRING s`)
est un **nom transitoire** (RR 2.1.3.6.c) : il peut servir de destination
(`s[2] := "B"`), être déréférencé (`CHAR c = s[2]`), mais il ne peut être ni lié à un
identificateur (`REF CHAR c = s[2]`), ni affecté à une variable, ni rendu par une
routine, ni servir d'opérande d'un opérateur (`s[2] +:= …`), comme dans le RR et a68g.

### 5.3 Opérateurs

- `OP` symboliques et gras, monadiques et dyadiques, avec ou sans parenthèse de mode
  (`OP MAX = (INT a, b) INT: …`, `OP (INT, INT) INT MAX = …`).
- `PRIO X = n` (1 à 9) ; un opérateur dyadique sans priorité est refusé. Les opérateurs
  monadiques lient plus fort que tous les dyadiques ; à priorité égale l'association se fait
  à gauche.
- Surcharge : l'identification (RR 7.2) choisit, dans la portée la plus interne qui en
  contient une, la définition dont les opérandes conviennent en contexte ferme.
  Une redéfinition locale masque le prélude (`OP + = (INT a, b) INT: a * b` dans un bloc).

---

## 6. Prélude standard

### 6.1 Opérateurs (RR 10.2)

| Opérandes | Opérateurs |
|---|---|
| `L INT` | `+ - * / % OVER %* MOD ** ^ UP`, `= /= ~= < <= > >= EQ NE LT LE GT GE`, monadiques `- + ABS SIGN ODD REPR LENG SHORTEN BIN`, `I +*` |
| `L REAL` (et mélanges avec `L INT`) | `+ - * / **` (exposant `INT` ou `REAL`), comparaisons, `- + ABS SIGN ROUND ENTIER LENG SHORTEN`, `I +*` |
| `L COMPL` (mélanges avec `L REAL`, `L INT`) | `+ - * / **`, `= /=`, `RE IM ABS ARG CONJ LENG SHORTEN` |
| `BOOL` | `AND & OR XOR NOT ~ = /= ABS` |
| `CHAR`, chaînes | comparaisons, `+` (concaténation), `*` (répétition), `ABS`, `REPR` |
| `L BITS` | `AND & OR XOR NOT ~ = /= <= >= SHL UP SHR DOWN ABS BIN ELEM SET CLEAR LENG SHORTEN` |
| `L BYTES` | comparaisons, `ELEM`, `LENG`, `SHORTEN` |
| rangées | `LWB UPB` (monadiques et dyadiques), `ELEMS` |
| affectations combinées | `+:= -:= *:= /:= %:= %*:= +=:` et `PLUSAB MINUSAB TIMESAB DIVAB OVERAB MODAB PLUSTO` |
| `SEMA` | `LEVEL` (création et lecture), `UP`, `DOWN` |

Priorités : 1 affectations combinées ; 2 `OR` ; 3 `AND & XOR` ; 4 `= /= EQ NE` ;
5 `< <= > >= LT LE GT GE` ; 6 `+ -` ; 7 `* / % OVER %* MOD ELEM SET CLEAR` ;
8 `** ^ UP DOWN SHL SHR LWB UPB ELEMS` ; 9 `I +*`.

### 6.2 Enquêtes d'environnement et constantes

`int lengths` (3), `int shorths` (1), `real lengths`, `real shorths`, `bits lengths`,
`bytes lengths` (2), `max int`, `long max int`, `long long max int`, `max real`,
`small real`, `long small real`, `long max real`…, `int width`, `long int width`,
`real width`, `long real width`, `long long real width`, `exp width`, `bits width`,
`long bits width`, `bytes width`, `long bytes width`, `max abs char`, `max bits`,
`pi`, `long pi`, `long long pi`, `null character`, `blank`, `flip` (`T`), `flop` (`F`),
`errorchar` (`*`), `newline char`, `tab char`, `formfeed char`.

### 6.3 Fonctions

- `sqrt exp ln log sin cos tan arcsin arccos arctan` (+ `sinh cosh tanh arcsinh arccosh
  arctanh cbrt`, `arctan2`) pour chaque longueur : `long sqrt`, `long long exp`…
  (alias a68g : `asin`, `acos`, `atan`, `dsqrt`, `qsqrt`…) ;
- complexes : `complex sqrt`, `complex exp`, `complex ln`, `complex sin`, `complex cos`,
  `complex tan`, `complex arcsin`, `complex arccos`, `complex arctan` (et `long complex …`) ;
- aléatoire : `random`, `next random (REF INT)`, `first random (INT)` (générateur
  déterministe ; la suite diffère de celle d'a68g) ;
- conversions : `whole (x, largeur)`, `fixed (x, largeur, décimales)`,
  `float (x, largeur, décimales, exposant)`, `real (x, l, d, e, pas)` (a68g) ;
  largeur positive : signe toujours écrit ; négative : signe seulement si négatif ;
  nulle : largeur minimale ;
- chaînes : `char in string`, `last char in string`, `string in string`, `to upper`,
  `to lower`, `is alpha`, `is digit`… ;
- `bits pack`, `long bits pack`, `bytes pack`, `long bytes pack` ;
- divers a68g : `seconds`, `clock`, `sleep`, `stop`.

---

## 7. Transput

Toutes les entrées-sorties de la console passent par l'IDE (ou la console Windows pour un
`.exe` généré) ; les fichiers disque sont relatifs au répertoire de travail du programme.

### 7.1 Sans format (RR 10.3.3)

`print`, `write`, `put (f, …)`, `read`, `get (f, …)`. Les rangées et structures sont
« redressées » (straightening). Sorties standard, identiques à a68g :

| Valeur | `print` |
|---|---|
| `1` | `         +1` (largeur `int width + 1` = 11) |
| `-67` | `        -67` |
| `1.5` | `+1.50000000000000e  +0` |
| `LONG 1.5` | `+1.500000000000000000000000000e  +0` |
| `TRUE` | `T` |
| `"abc"` | `abc` |
| `2r101` | 32 caractères `F…FTFT` |
| `3 I 4` | les deux parties réelles à la suite |

Aucun espace n'est inséré entre deux valeurs. En lecture, les nombres et booléens sautent
les espaces et les fins de ligne ; une chaîne est lue jusqu'à la fin de la ligne (non
consommée) ou un caractère terminateur (`make term`) ; un caractère saute les fins de ligne.

### 7.2 Formaté (RR 10.3.4–10.3.5)

`printf`, `writef`, `putf`, `readf`, `getf`, formats `$ … $` et valeurs `FORMAT` :

| Élément | Signification |
|---|---|
| `d`, `z` | chiffre ; `z` remplace les zéros de tête par des espaces |
| `+`, `-` | signe (`-` : espace si positif) ; le signe glisse sur les zéros des cadres `z` |
| `.`, `e` | point décimal, exposant (`+d.3de+2d`) |
| `i` | séparateur des parties d'un complexe |
| `a` | caractère (`5a` : chaîne de 5 caractères) |
| `b` | booléen (`T`/`F`) ; `b("oui","non")` choix booléen |
| `c("lundi","mardi",…)` | choix entier |
| `2r8d`, `16r4d` | bits en base 2, 4, 8, 16 |
| `g`, `g(l)`, `g(l, d)`, `g(l, d, e)` | général : `print`, `whole`, `fixed`, `float` |
| `f(format)` | motif de format (format calculé) |
| `s` | cadre supprimé (`sd`, `s.`) |
| `x`, `y`, `l`, `p`, `k`, `q` | espace, retour arrière, nouvelle ligne, nouvelle page, position (`20k`), blanc |
| `"texte"` | insertion littérale |
| `n`, `n(expr)` | répéteurs statiques et dynamiques (`n(w)d`, `3(d" ")`) |
| `( … )` | collection répétée |

Les images sont séparées par des virgules : `$2d"/", 2d"/", 4d$` écrit trois entiers,
alors que `$3d"/"2d"/"4d$` est un seul motif de 9 chiffres. Un format épuisé recommence
(événement `on format end`). En fin de `printf`, les insertions qui suivent sont exécutées
jusqu'au motif suivant (`printf(($"Bonjour"l$))` écrit la ligne).

### 7.3 Fichiers et canaux (RR 10.3.1)

- Fichiers standard : `stand in`, `stand out`, `stand error`, `stand back` ;
  canaux : `stand in channel`, `stand out channel`, `stand back channel` (disque, lecture,
  écriture, binaire), `stand error channel`.
- `open (f, nom, canal)`, `establish (f, nom, canal)`, `create (f, canal)` (rendent 0 en cas
  de succès), `associate (f, REF STRING)` (fichier sur une chaîne), `close`, `lock`,
  `scratch` (efface le fichier), `reset`.
- Mise en page : `newline`, `newpage`, `space`, `backspace`, `set char number (f, n)`.
- Positions : `char number`, `line number`, `page number` ; `set (f, page, ligne, car)`
  (RR) ; `seek (f, n)` (a68g).
- Événements : `on logical file end`, `on physical file end`, `on line end`,
  `on page end`, `on format end`, `on value error`, `on char error (f, PROC (REF FILE,
  REF CHAR) BOOL)`, `on open error` ; la routine rend `TRUE` si l'événement est traité
  (sinon l'action par défaut ou une erreur d'exécution), et peut sortir par `GOTO`.
- `make term (f, ",;")`, `idf`, `term`, enquêtes `put possible`, `get possible`,
  `bin possible`, `set possible`, `reset possible`, `reidf possible`, `compressible`,
  `eof`, `eoln` (a68g).
- Binaire : `put bin`, `get bin`, `write bin`, `read bin` (format interne propre, relu
  exactement par `get bin`).
- Lectures simplifiées a68g : `read int`, `read real`, `read string`, `read line`,
  `get int (f)`, `print int (n)`…

---

## 8. Parallélisme

`PAR (a, b, c)` lance chaque unité dans un fil d'exécution réel et attend leur fin.
Synchronisation par sémaphores de Dijkstra (RR 10.2.4) :

```algol68
SEMA libres = LEVEL 3, pleins = LEVEL 0, exclusion = LEVEL 1;
DOWN libres; DOWN exclusion; … ; UP exclusion; UP pleins
```

- `LEVEL n` crée un sémaphore, `LEVEL s` lit son niveau, `DOWN s` attend puis décrémente,
  `UP s` incrémente et réveille.
- **Interblocage détecté** : si tous les processus attendent un `DOWN` sans qu'aucun `UP`
  ne soit possible, l'erreur « interblocage » est levée.
- Une erreur ou un saut hors d'une branche arrête les autres branches puis se propage.
- L'arrêt (bouton Stop) interrompt aussi les branches et les attentes.

---

## 9. Contrôles à l'exécution

Toutes les erreurs donnent un message français, la position (ligne, colonne) et la pile
des appels :

- indice ou trimmer hors des bornes, nombre d'indices incorrect ;
- affectation de rangées de bornes différentes (sauf `FLEX`) ;
- déréférencement ou affectation de `NIL` ;
- valeur non initialisée (variable, élément, champ, union) ;
- identificateur utilisé avant sa déclaration (détecté à la compilation quand c'est
  possible) ;
- division par zéro (`/`, `%`, `MOD`), débordement d'entier (`INT`, `LONG INT`,
  `LONG LONG INT`), débordement réel, domaine des fonctions (`sqrt`, `ln`, `arcsin`…) ;
- violations de portée ;
- union non initialisée dans une clause de conformité ;
- fin de fichier sans routine d'événement, valeur illisible ;
- interblocage, `ASSERT` faux, récursion trop profonde (100 000 appels imbriqués).

---

## 10. Mise au point (IDE)

- Points d'arrêt par ligne (y compris dans le corps d'une `PROC` réduit à une unité),
  pause, pas à pas détaillé / principal / sortie, pause à l'entrée et sur erreur
  (la pile est conservée).
- Pile d'appels avec le nom des routines (`PROC f` : « f », opérateurs : « OP + »).
- Variables visibles à la ligne courante ; les variables (noms) sont montrées par leur
  valeur ; structures, rangées (1 000 premiers éléments), unions et noms sont explorables.
- Les branches `PAR` peuvent aussi s'arrêter sur un point d'arrêt.

---

## 11. Exécutables autonomes

Le programme compilé est sérialisé sous forme de **sources** (fichier principal et fichiers
inclus) ; l'exécutable recompile ces sources au démarrage (la compilation prend quelques
millisecondes), ce qui garantit un comportement identique à l'exécution dans l'IDE.

---

## 12. Exemples

```algol68
# Liste chaînée #
MODE NOEUD = STRUCT (INT valeur, REF NOEUD suivant);
REF NOEUD tete := NIL;
FOR i TO 5 DO tete := HEAP NOEUD := (i, tete) OD;
REF NOEUD p := tete;
WHILE REF NOEUD (p) ISNT NIL DO print(valeur OF p); p := suivant OF p OD
```

```algol68
# Opérateur utilisateur et priorité #
PRIO MAX = 9;
OP MAX = (INT a, b) INT: (a > b | a | b);
print((3 MAX 7, newline))                     #          +7 #
```

```algol68
# Format #
printf(($"x = "g(0)", y = ", -d.2d l$, 42, 3.14159))   # x = 42, y =  3.14 #
```

```algol68
# Test « man or boy » de Knuth : -67 #
PROC a = (INT k, PROC INT x1, x2, x3, x4, x5) INT:
BEGIN
   INT kk := k;
   PROC b = INT: (kk -:= 1; a(kk, b, x1, x2, x3, x4));
   IF kk <= 0 THEN x4 + x5 ELSE b FI
END;
print(a(10, INT: 1, INT: -1, INT: -1, INT: 1, INT: 0))
```

---

## 13. Différences avec Algol 68 Genie et écarts connus

- `CHAR` est un caractère UTF-16 (`max abs char` = 65535) ; a68g utilise des octets
  (`max abs char` = 255).
- `LONG REAL` calcule sur 35 chiffres décimaux (a68g : 28 à 35 selon la normalisation) ;
  les affichages utilisent les mêmes largeurs qu'a68g (28 et 63 chiffres).
- Le générateur aléatoire n'est pas celui d'a68g (suites différentes).
- `set` suit le RR (`set (f, p, l, c)`) ; la variante a68g à deux paramètres s'appelle
  `seek`.
- La sortie standard n'a pas de longueur de ligne maximale (comme a68g) : l'événement
  `on line end` n'est déclenché qu'en lecture.
- Extensions a68g non reprises : graphiques (`draw …`), son, PostgreSQL, curses,
  procédures système (`system`, `fork`, `execve`…), bibliothèque GSL (fonctions spéciales,
  algèbre linéaire, FFT), motifs de format à la C (`%d`), stropping POINT.
- La clause collatérale et la clause parallèle à une seule unité s'exécutent dans le fil
  courant ; l'ordre d'évaluation des opérandes et des éléments collatéraux est de gauche à
  droite (le RR le laisse indéfini).
- Profondeur de récursion limitée à 100 000 appels imbriqués.
