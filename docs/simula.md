# Simula 67 — reference guide

This compiler implements **Simula 67** as defined by the *SIMULA Standard*
(1986, Simula Standards Group), including the system classes **SIMSET**,
**SIMULATION** and the input/output classes of **BASICIO**.

The source is compiled into intermediate code run by a virtual machine that
supports **coroutines** natively (`detach`, `resume`, `call`): every block
instance lives on the heap, so the quasi-parallel sequencing of the standard
(chapter 7) is reproduced exactly. The compiler checks types, qualifications
and visibility rules (`hidden`, `protected`) before anything runs.

---

## 1. Program structure

A program is a statement, usually a block:

```simula
begin
   integer i;
   for i := 1 step 1 until 3 do
   begin
      outint(i, 4); outimage
   end
end
```

* Keywords and identifiers are **case-insensitive** (`BEGIN` = `begin`).
* The declarations of a block precede its statements.
* The program is implicitly enclosed in `inspect sysin do inspect sysout do …`:
  `outtext`, `outint`, `inimage`, `inint`… therefore refer to `sysout` and `sysin`.
* A program may also be a **prefixed block**: `simulation begin … end`,
  `simset begin … end`, or prefixed by any user-declared class.

### Comments

| Form | Example |
|---|---|
| direct comment | `! this is a comment ;` |
| `comment` keyword | `comment so is this ;` |
| end comment | `end main loop ;` (everything after `end` up to `;`, `end`, `else`, `when`, `otherwise`) |
| directive line | a line starting with `%` in the first column |

### Directives

* `%include "file.sim"`: textual inclusion of another file (path relative to the current file).
* A line `% text` (percent followed by a space) is ignored.

---

## 2. Lexical tokens

* **Integers**: `123`, `1_000_000`, radix forms `2R1010`, `8R777`, `16RFF`.
* **Reals**: `3.14`, `.5`, `1.5&3` (= 1500), `&-2` (= 0.01), `2.0&&5` (`long real`).
* **Characters**: `'a'`, `'!65!'` (ISO code 65, that is `A`).
* **Strings**: `"Hello"`, `"he said ""yes"""`, `"!13!!10!"` (ISO codes), concatenation
  of adjacent strings `"abc" "def"`.
* Alternative operators: `lt le eq ge gt ne` for `< <= = >= > <>`; `\=` is accepted for `<>`.

---

## 3. Types

| Type | Representation |
|---|---|
| `integer` | 32-bit signed (`maxint` = 2147483647, `minint` = −2147483648); overflow wraps modulo 2³² |
| `short integer` | 16-bit, range checked on assignment |
| `real`, `long real` | IEEE 754 double precision (same representation for both) |
| `boolean` | `true`, `false` |
| `character` | a character (codes 0 to 255 for `char`/`rank`) |
| `text` | reference to a text frame (`OBJ`, `START`, `LENGTH`, `POS`) |
| `ref(C)` | reference to an object of class `C` (or a subclass), `none` |

Conversions: integer ↔ real are automatic; real → integer rounds
(`entier(x + 0.5)`); there is no implicit conversion for `boolean`, `character`, `text`.

---

## 4. Declarations

```simula
integer i, j;  real x;  long real z;  short integer s;
boolean found;  character c;  text t;  ref(person) p;
integer n = 100;  real half = 0.5;  text title = "Report";   ! constants
integer array a(1:10), m(0:n, -5:5);  array r(1:3);          ! array alone = real array
switch target := l1, l2, if x > 0 then l3 else l4;
procedure p(x, y); value x; name y; integer x, y; begin … end;
real procedure f(x); real x; f := x * x;
class point(x, y); real x, y; begin … end;
point class point3(z); real z; begin … end;
external class stack;  external integer procedure square;
```

### Procedures and transmission modes

| Parameter | by value | by reference | by name |
|---|---|---|---|
| value type (`integer`, `real`, `boolean`, `character`) | default | illegal | `name` |
| reference (`ref(C)`) | illegal | default | `name` |
| `text` | `value` (copy) | default | `name` |
| array of a value type | `value` (copy) | default | `name` |
| array of references, procedure, label, `switch` | illegal | default | `name` |

* A **typed procedure** returns the last value assigned to its identifier.
* **Call by name** re-evaluates the actual parameter at every use (Jensen's device).
* A **formal procedure** (`real procedure f;`) accepts any compatible procedure,
  including a standard procedure (`sqrt`, `sin`…).
* As the standard requires, the controlled variable of a `for` statement cannot be a name parameter.

### Classes

```simula
class stack(size); integer size;
protected contents;                    ! visible only in stack and its subclasses
virtual: procedure trace;              ! virtual quantity
begin integer array contents(1:size); integer top;
   procedure push(x); integer x; begin top := top + 1; contents(top) := x end;
   procedure trace; ;
   inner;                              ! body of the subclasses
end stack;
```

* **Concatenation**: an object of a subclass holds the attributes of all its prefixes;
  the body runs prefix first, with `inner` marking the place of the subclass body.
* **Virtuals**: procedures (with or without an `is procedure …` specification),
  virtual labels and virtual switches. The innermost match is used.
* **Protection**: `protected` (visible in the class, its subclasses and the blocks
  prefixed by it, including through remote access); `hidden protected` (invisible in subclasses).
* Every class has the attribute `detach`.
* A class that refers to itself with `this` cannot be used as a block prefix.

---

## 5. Statements

```simula
x := y := 0;                 ! multiple assignment
t :- copy("abc");            ! reference assignment (text or object)
t := "xyz";                  ! text value assignment (copy into the frame)
if a > b then … else …;
while i < n do …;
for i := 1 step 1 until 10 do …;
for i := 1, 5, 10 step 5 until 30, i * 2 while i < 1000 do …;
for p :- list_head, p.next while p =/= none do …;
goto done;   go to target(k);
inspect p when circle do … when square do … otherwise …;
inspect p do begin … end otherwise …;
activate x delay 5;  reactivate y at 10 prior;  activate z before current;
```

Blocks (`begin` with declarations) may be nested. A label is placed in front of
a statement: `done: outimage`.

---

## 6. Expressions

Precedence (strongest first):

1. `**` (left associative)
2. `*`  `/`  `//`
3. `+`  `-`; text concatenation `&`
4. relations `<  <=  =  >=  >  <>  ==  =/=  is  in`
5. `not`
6. `and`
7. `or`
8. `imp`
9. `eqv`
10. `and then`
11. `or else`

* `/` always yields a real; `//` is integer division (truncation towards zero).
* `i ** j` (integers, j ≥ 0) is an integer; otherwise the result is real.
* `and then`, `or else`: short-circuit evaluation.
* Conditional expression: `if b then e1 else e2`.
* Objects: `new C(args)`, `this C`, `x qua C`, `x is C`, `x in C`, `none`.
* `==` / `=/=` compare references (objects or texts); `=` / `<>` compare values.

---

## 7. Text attributes (chapter 8)

| Attribute | Purpose |
|---|---|
| `length`, `pos`, `start`, `constant`, `main` | length, current position, start in the frame, constancy, main frame |
| `setpos(i)`, `more`, `getchar`, `putchar(c)` | sequential access |
| `sub(i, n)`, `strip` | subtext, removal of trailing blanks |
| `getint`, `getreal`, `getfrac` | read a number at the start of the text |
| `putint(i)`, `putfix(r, n)`, `putreal(r, n)`, `putfrac(i, n)` | right-adjusted editing (asterisks if too long) |

Related procedures: `copy(t)`, `blanks(n)`, `upcase(t)`, `lowcase(t)`, concatenation `&`.

---

## 8. Standard environment (chapter 9)

* Arithmetic: `abs sign entier mod rem max min addepsilon subepsilon`.
* Mathematics: `sqrt sin cos tan cotan arcsin arccos arctan arctan2 sinh cosh tanh ln log10 exp`.
* Characters: `char isochar rank isorank digit letter lowten decimalmark`.
* Arrays: `lowerbound(a, i)`, `upperbound(a, i)`.
* Random drawing (integer seed passed by name): `draw randint uniform normal negexp Poisson Erlang discrete linear histd`, plus `histo`.
* Miscellaneous: `error(t)`, `terminate_program`, `sourceline`, `datetime`, `cputime`, `clocktime`,
  constants `maxint minint maxrank maxreal minreal maxlongreal minlongreal simulaid`.
* Sequencing: `detach`, `resume(x)`, `call(x)`.

---

## 9. Coroutines (chapter 7)

```simula
class player(pname); text pname;
begin ref(player) other;
   detach;                        ! return control to the creator
   while true do begin outtext(pname); outimage; resume(other) end
end;
```

* `new C` runs the body up to the first `detach` (detached object) or to the end.
* `resume(x)`: object x becomes the active component of its quasi-parallel system.
* `call(x)`: x is attached to the caller; its next `detach` returns after the `call`.
* The full semantics of quasi-parallel systems (system heads, main component,
  operating chain) is respected; a `goto` out of an object terminates it.

---

## 10. SIMSET and SIMULATION (chapters 11 and 12)

**SIMSET**: `linkage`, `link` (`suc pred prev out into follow precede`) and
`head` (`first last empty cardinal clear`).

**SIMULATION** (prefixed by SIMSET): `process` (`idle terminated evtime nextev`),
`current`, `time`, `main`, `hold(t)`, `passivate`, `wait(queue)`, `cancel(x)`,
`accum(a, b, c, d)` and the `activate` / `reactivate` statements with
`at`, `delay`, `before`, `after`, `prior`.

```simula
simulation begin
   process class customer;
   begin outfix(time, 1, 6); outtext(" arrival"); outimage; hold(3) end;
   activate new customer delay 2;
   hold(10)
end
```

### Simulation examples

The `samples/simula67` folder contains discrete event simulations modelled on the
classic examples of the Simula literature:

| File | Model | Source of inspiration |
|---|---|---|
| `09_bank.sim` | a bank with one teller, random arrivals | introductory SIMULATION example |
| `17_fitting_room.sim` | Sam, Sally and Andy share one fitting room | the classic example quoted in the Simula article of Wikipedia |
| `18_car_wash.sim` | cars, a waiting line and a team of washers, with `accum` statistics | *SIMULA BEGIN* (Birtwistle, Dahl, Myhrhaug, Nygaard) |
| `19_job_shop.sim` | orders routed through groups of machines | Dahl and Nygaard, *SIMULA – an ALGOL-based simulation language* (1966) |
| `20_harbour.sim` | tankers competing for jetties and a tug | the port model of Birtwistle's DEMOS package |
| `21_traffic_police.sim` | cars on a road and a police speed patrol | the car and police example of the original SIMULA manual |

---

## 11. Input/output (chapter 10)

Hierarchy: `file` ⟶ `imagefile` (`infile`, `outfile` ⟶ `printfile`, `directfile`)
and `bytefile` (`inbytefile`, `outbytefile`, `directbytefile`).

* `sysin`: an `infile` (1024-character image) connected to the keyboard / IDE console.
* `sysout`: a `printfile` (132-character image); trailing blanks of a line are not written.
* Output: `outtext outchar outint(i, w) outfix(r, n, w) outreal(r, n, w) outfrac(i, n, w) outimage outrecord breakoutimage`.
  `w > 0`: right-adjusted; `w < 0`: left-adjusted; `w = 0`: exact width.
* Input: `inimage inrecord inchar inint inreal infrac intext(n) lastitem endfile`.
* Printer: `line page linesperpage spacing eject`.
* Direct files: `locate location lastloc maxloc deleteimage lock unlock`.
* Access modes: `setaccess("append")`, `"create"`, `"nocreate"`, `"anycreate"`, `"purge"`,
  `"readonly"`, `"readwrite"`… Relative file names are resolved in the program's folder.
* Real numbers edited by `putreal`/`outreal`: mantissa, `&`, sign, 2-digit exponent
  (3 digits beyond 99), for example `outreal(1234.56, 4, 0)` gives `1.235&+03`. Rounding is
  exact (exact ties round to the even digit, like C's `printf`).
* At end of file, `inimage` puts the EM character (`'!25!'`) in the image and `endfile` becomes true.

---

## 12. External modules

```simula
external class stack;               ! looks for stack.sim in the program's folder
external integer procedure square;  ! looks for square.sim
external class tools = "lib/tools.sim";
begin … end
```

A module is a file that contains only a class or procedure declaration (and possibly
its own `external` head). It is compiled with the program; the modules used are
embedded in the generated `.exe` files.

---

## 13. Implementation choices and limitations

* `long real` has the same precision as `real` (IEEE double), which the standard allows.
* External procedures written in another language (`external Fortran procedure …`)
  are rejected at compile time (no native code is linked).
* `lock`/`unlock` on direct files always succeed (no operating system lock).
* The random generator is multiplicative (U ← U·5¹³ mod 2³¹); a negative seed
  gives the antithetic drawings 1 − u.
* Integer overflow is not reported (arithmetic modulo 2³²); `short integer` overflow
  and out-of-range real → integer conversions are reported.
* Standalone executables (`.exe`) contain the sources, which are recompiled in memory
  at startup (a few milliseconds).

---

## 14. Error messages

Compile-time and runtime errors give the file, line and column. At runtime, the
current output line is flushed before the message, and the Simula call stack is
shown (and can be explored in the IDE debugger).
