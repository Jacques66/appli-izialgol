# Simula 67 — guide de référence

Ce compilateur implémente le langage **Simula 67** tel que défini par la norme
*SIMULA Standard* (1986, Simula Standards Group), y compris les classes système
**SIMSET**, **SIMULATION** et les classes d'entrées-sorties de **BASICIO**.

Le source est compilé en un code intermédiaire exécuté par une machine virtuelle
qui gère nativement les **coroutines** (`detach`, `resume`, `call`) : chaque instance
de bloc vit sur le tas, ce qui permet la quasi-parallélisme exact de la norme
(chapitre 7). Le compilateur vérifie les types, les qualifications et les règles
de visibilité (`hidden`, `protected`) avant toute exécution.

---

## 1. Structure d'un programme

Un programme est une instruction, en général un bloc :

```simula
begin
   integer i;
   for i := 1 step 1 until 3 do
   begin
      outint(i, 4); outimage
   end
end
```

* Les mots-clés et identificateurs sont **insensibles à la casse** (`BEGIN` = `begin`).
* Les déclarations d'un bloc précèdent ses instructions.
* Le programme est implicitement englobé dans `inspect sysin do inspect sysout do …` :
  `outtext`, `outint`, `inimage`, `inint`… s'adressent donc à `sysout` et `sysin`.
* Un programme peut aussi être un **bloc préfixé** : `simulation begin … end`,
  `simset begin … end`, ou par toute classe déclarée par l'utilisateur.

### Commentaires

| Forme | Exemple |
|---|---|
| commentaire direct | `! ceci est un commentaire ;` |
| mot-clé `comment` | `comment ceci aussi ;` |
| commentaire de fin | `end boucle principale ;` (tout ce qui suit `end` jusqu'à `;`, `end`, `else`, `when`, `otherwise`) |
| ligne de directive | une ligne commençant par `%` en première colonne |

### Directives

* `%include "fichier.sim"` : inclusion textuelle d'un autre fichier (chemin relatif au fichier courant).
* Une ligne `% texte` (pourcent suivi d'un espace) est ignorée.

---

## 2. Lexique

* **Entiers** : `123`, `1_000_000`, bases `2R1010`, `8R777`, `16RFF`.
* **Réels** : `3.14`, `.5`, `1.5&3` (= 1500), `&-2` (= 0,01), `2.0&&5` (`long real`).
* **Caractères** : `'a'`, `'!65!'` (code ISO 65, soit `A`).
* **Chaînes** : `"Bonjour"`, `"il dit ""oui"""`, `"!13!!10!"` (codes ISO), concaténation
  de chaînes adjacentes `"abc" "def"`.
* Opérateurs alternatifs : `lt le eq ge gt ne` pour `< <= = >= > <>` ; `\=` est accepté pour `<>`.

---

## 3. Types

| Type | Représentation |
|---|---|
| `integer` | 32 bits signés (`maxint` = 2147483647, `minint` = −2147483648) ; dépassement : arithmétique modulo 2³² |
| `short integer` | 16 bits, contrôle d'intervalle à l'affectation |
| `real`, `long real` | IEEE 754 double précision (même représentation pour les deux) |
| `boolean` | `true`, `false` |
| `character` | caractère (codes 0 à 255 pour `char`/`rank`) |
| `text` | référence à un cadre de texte (`OBJ`, `START`, `LENGTH`, `POS`) |
| `ref(C)` | référence à un objet de la classe `C` (ou d'une sous-classe), `none` |

Conversions : entier ↔ réel automatiques ; réel → entier par arrondi
(`entier(x + 0.5)`) ; aucune conversion implicite pour `boolean`, `character`, `text`.

---

## 4. Déclarations

```simula
integer i, j;  real x;  long real z;  short integer s;
boolean trouve;  character c;  text t;  ref(personne) p;
integer n = 100;  real demi = 0.5;  text titre = "Rapport";   ! constantes
integer array a(1:10), m(0:n, -5:5);  array r(1:3);           ! array seul = real array
switch aiguille := l1, l2, if x > 0 then l3 else l4;
procedure p(x, y); value x; name y; integer x, y; begin … end;
real procedure f(x); real x; f := x * x;
class point(x, y); real x, y; begin … end;
point class point3(z); real z; begin … end;
external class pile;  external integer procedure carre;
```

### Procédures et modes de transmission

| Paramètre | par valeur | par référence | par nom |
|---|---|---|---|
| type valeur (`integer`, `real`, `boolean`, `character`) | défaut | interdit | `name` |
| référence (`ref(C)`) | interdit | défaut | `name` |
| `text` | `value` (copie) | défaut | `name` |
| tableau de type valeur | `value` (copie) | défaut | `name` |
| tableau de références, procédure, étiquette, `switch` | interdit | défaut | `name` |

* Une **procédure typée** rend la dernière valeur affectée à son identificateur.
* L'**appel par nom** réévalue l'effectif à chaque utilisation (procédé de Jensen).
* Une **procédure formelle** (`real procedure f;`) peut recevoir n'importe quelle procédure
  compatible, y compris une procédure standard (`sqrt`, `sin`…).
* Remarque de la norme : la variable contrôlée d'un `for` ne peut pas être un paramètre par nom.

### Classes

```simula
class pile(taille); integer taille;
protected contenu;                     ! visible seulement dans pile et ses sous-classes
virtual: procedure trace;              ! quantité virtuelle
begin integer array contenu(1:taille); integer sommet;
   procedure empile(x); integer x; begin sommet := sommet + 1; contenu(sommet) := x end;
   procedure trace; ;
   inner;                              ! corps des sous-classes
end pile;
```

* **Concaténation** : un objet d'une sous-classe contient les attributs de tous ses préfixes ;
  le corps s'exécute préfixe d'abord, `inner` marquant la place du corps de la sous-classe.
* **Virtuels** : procédures (avec ou sans spécification `is procedure …`), étiquettes et
  `switch` virtuels. La correspondance la plus intérieure est appelée.
* **Protection** : `protected` (visible dans la classe, ses sous-classes et les blocs préfixés
  par elle, y compris par accès distant) ; `hidden protected` (invisible dans les sous-classes).
* Toute classe a l'attribut `detach`.
* Une classe qui utilise `this` pour se désigner ne peut pas préfixer un bloc.

---

## 5. Instructions

```simula
x := y := 0;                 ! affectation multiple
t :- copy("abc");            ! affectation de référence (texte ou objet)
t := "xyz";                  ! affectation de valeur de texte (copie dans le cadre)
if a > b then … else …;
while i < n do …;
for i := 1 step 1 until 10 do …;
for i := 1, 5, 10 step 5 until 30, i * 2 while i < 1000 do …;
for p :- tete, tete.suivant while p =/= none do …;
goto fin;   go to aiguille(k);
inspect p when cercle do … when carre do … otherwise …;
inspect p do begin … end otherwise …;
activate x delay 5;  reactivate y at 10 prior;  activate z before current;
```

Les blocs (`begin` avec déclarations) peuvent être imbriqués. Une étiquette se place
devant une instruction : `fin: outimage`.

---

## 6. Expressions

Priorités (de la plus forte à la plus faible) :

1. `**` (associatif à gauche)
2. `*`  `/`  `//`
3. `+`  `-` ; concaténation de textes `&`
4. relations `<  <=  =  >=  >  <>  ==  =/=  is  in`
5. `not`
6. `and`
7. `or`
8. `imp`
9. `eqv`
10. `and then`
11. `or else`

* `/` donne toujours un réel ; `//` est la division entière (troncature vers zéro).
* `i ** j` (entiers, j ≥ 0) est entier ; sinon réel.
* `and then`, `or else` : évaluation court-circuitée.
* Expression conditionnelle : `if b then e1 else e2`.
* Objets : `new C(args)`, `this C`, `x qua C`, `x is C`, `x in C`, `none`.
* `==` / `=/=` comparent des références (objets ou textes), `=` / `<>` des valeurs.

---

## 7. Attributs des textes (chapitre 8)

| Attribut | Rôle |
|---|---|
| `length`, `pos`, `start`, `constant`, `main` | longueur, position courante, début dans le cadre, constance, cadre principal |
| `setpos(i)`, `more`, `getchar`, `putchar(c)` | accès séquentiel |
| `sub(i, n)`, `strip` | sous-texte, suppression des blancs finaux |
| `getint`, `getreal`, `getfrac` | lecture d'un nombre au début du texte |
| `putint(i)`, `putfix(r, n)`, `putreal(r, n)`, `putfrac(i, n)` | édition cadrée à droite (étoiles si trop long) |

Procédures associées : `copy(t)`, `blanks(n)`, `upcase(t)`, `lowcase(t)`, concaténation `&`.

---

## 8. Environnement standard (chapitre 9)

* Arithmétique : `abs sign entier mod rem max min addepsilon subepsilon`.
* Mathématiques : `sqrt sin cos tan cotan arcsin arccos arctan arctan2 sinh cosh tanh ln log10 exp`.
* Caractères : `char isochar rank isorank digit letter lowten decimalmark`.
* Tableaux : `lowerbound(a, i)`, `upperbound(a, i)`.
* Aléatoire (graine entière transmise par nom) : `draw randint uniform normal negexp Poisson Erlang discrete linear histd`, plus `histo`.
* Divers : `error(t)`, `terminate_program`, `sourceline`, `datetime`, `cputime`, `clocktime`,
  constantes `maxint minint maxrank maxreal minreal maxlongreal minlongreal simulaid`.
* Séquencement : `detach`, `resume(x)`, `call(x)`.

---

## 9. Coroutines (chapitre 7)

```simula
class joueur(nom); text nom;
begin ref(joueur) autre;
   detach;                        ! rend la main au créateur
   while true do begin outtext(nom); outimage; resume(autre) end
end;
```

* `new C` exécute le corps jusqu'au premier `detach` (objet détaché) ou jusqu'à la fin.
* `resume(x)` : l'objet x devient la composante active de son système quasi-parallèle.
* `call(x)` : x est rattaché à l'appelant ; son prochain `detach` revient après le `call`.
* La sémantique complète des systèmes quasi-parallèles (têtes de système, composante
  principale, chaîne opérante) est respectée ; `goto` hors d'un objet le termine.

---

## 10. SIMSET et SIMULATION (chapitres 11 et 12)

**SIMSET** : `linkage`, `link` (`suc pred prev out into follow precede`) et
`head` (`first last empty cardinal clear`).

**SIMULATION** (préfixée par SIMSET) : `process` (`idle terminated evtime nextev`),
`current`, `time`, `main`, `hold(t)`, `passivate`, `wait(file)`, `cancel(x)`,
`accum(a, b, c, d)` et les instructions `activate` / `reactivate` avec
`at`, `delay`, `before`, `after`, `prior`.

```simula
simulation begin
   process class client;
   begin outfix(time, 1, 6); outtext(" arrivee"); outimage; hold(3) end;
   activate new client delay 2;
   hold(10)
end
```

---

## 11. Entrées-sorties (chapitre 10)

Hiérarchie : `file` ⟶ `imagefile` (`infile`, `outfile` ⟶ `printfile`, `directfile`)
et `bytefile` (`inbytefile`, `outbytefile`, `directbytefile`).

* `sysin` : `infile` (image de 1024 caractères) relié au clavier / à la console de l'IDE.
* `sysout` : `printfile` (image de 132 caractères) ; les blancs de fin de ligne ne sont pas écrits.
* Sortie : `outtext outchar outint(i, w) outfix(r, n, w) outreal(r, n, w) outfrac(i, n, w) outimage outrecord breakoutimage`.
  `w > 0` : cadré à droite ; `w < 0` : cadré à gauche ; `w = 0` : largeur exacte.
* Entrée : `inimage inrecord inchar inint inreal infrac intext(n) lastitem endfile`.
* Imprimante : `line page linesperpage spacing eject`.
* Fichiers directs : `locate location lastloc maxloc deleteimage lock unlock`.
* Accès : `setaccess("append")`, `"create"`, `"nocreate"`, `"anycreate"`, `"purge"`,
  `"readonly"`, `"readwrite"`… Les noms de fichiers relatifs sont résolus dans le dossier du programme.
* Format des réels de `putreal`/`outreal` : mantisse, `&`, signe, exposant sur 2 chiffres
  (3 au-delà de 99), par exemple `outreal(1234.56, 4, 0)` donne `1.235&+03`. L'arrondi est
  exact (les cas d'égalité exacte sont arrondis au chiffre pair, comme `printf` en C).
* En fin de fichier, `inimage` place le caractère EM (`'!25!'`) dans l'image et `endfile` devient vrai.

---

## 12. Modules externes

```simula
external class pile;                ! cherche pile.sim dans le dossier du programme
external integer procedure carre;   ! cherche carre.sim
external class outils = "lib/outils.sim";
begin … end
```

Un module est un fichier ne contenant qu'une déclaration de classe ou de procédure
(et éventuellement sa propre tête `external`). Il est compilé avec le programme ;
les modules utilisés sont embarqués dans les `.exe` générés.

---

## 13. Choix d'implémentation et limites

* `long real` a la même précision que `real` (double IEEE) — la norme le permet.
* Les procédures externes écrites dans un autre langage (`external Fortran procedure …`)
  sont refusées à la compilation (aucun code natif n'est lié).
* `lock`/`unlock` des fichiers directs réussissent toujours (pas de verrou système).
* Le générateur aléatoire est multiplicatif (U ← U·5¹³ mod 2³¹) ; une graine négative
  donne les tirages antithétiques 1 − u.
* Les dépassements de capacité entiers ne sont pas signalés (arithmétique modulo 2³²) ;
  les dépassements de `short integer` et les conversions réel → entier hors limites le sont.
* Les exécutables autonomes (`.exe`) contiennent les sources, recompilées en mémoire
  au lancement (quelques millisecondes).

---

## 14. Messages d'erreur

Les erreurs de compilation et d'exécution sont en français, avec fichier, ligne et
colonne. À l'exécution, la ligne de sortie en cours est vidée avant le message, et la
pile d'appels Simula est affichée (et explorable dans le débogueur de l'IDE).
