# Algol 68 — reference guide

This document describes the **Algol 68** compiler and runtime of Simula67 Studio.
The implemented language is that of the *Revised Report on the Algorithmic Language Algol 68*
(Van Wijngaarden et al., 1975), with the syntax and behaviour of **Algol 68 Genie** (a68g)
as the practical reference: a program written for a68g usually compiles unchanged, and the
output of `print`, `whole`, `fixed`, `float` and of formats is identical, character for
character, to that of a68g.

The examples quoted here are in `samples/algol68` (*Examples* menu of the IDE):
`classics/` (hello, factorial, sieve, Hanoi, quicksort, eight queens, *man or boy*),
`features/` (one program per major feature), `errors/` (expected compilation errors)
and `runtime_errors/` (expected runtime errors).

---

## 1. First program

```algol68
BEGIN
   print(("Hello, world!", newline))
END
```

A program is a *serial clause*: `BEGIN … END` is optional (as in a68g);
`print(("x = ", x, newline))` is enough.

- File extension: `.a68`.
- Compiler and runtime messages are in English, with line and column.
- Several errors are reported per compilation (at most 200).

---

## 2. Program representation

### 2.1 Bold words (stropping)

| Regime | Example | Activation |
|---|---|---|
| **UPPER** (default, as in a68g) | `BEGIN INT n := 1; ... END` | default |
| **QUOTE** | `'BEGIN' 'INT' n := 1; ... 'END'` | detected automatically, or `PR quote PR` |

With UPPER stropping, words in capitals are bold words (keywords, mode indicants,
operators) and identifiers are written in lower case (digits and `_` allowed).
With QUOTE stropping, bold words are enclosed in apostrophes and identifiers may contain
capitals; they are then case-insensitive (`Word` = `word`).
`PR upper PR` switches back to UPPER stropping.

### 2.2 Identifiers

Spaces inside an identifier are ignored: `max int`, `read int`, `long long pi` denote
`maxint`, `readint`, `longlongpi`.

### 2.3 Comments and pragmats

- Comments: `CO … CO`, `COMMENT … COMMENT`, `# … #`, `¢ … ¢`.
- Pragmats: `PR … PR`, `PRAGMAT … PRAGMAT`.
  - `PR include "file.a68" PR` (or `PR read "…" PR`) inserts a file (resolved by the IDE
    or the command line relative to the including file);
  - `PR quote PR` / `PR upper PR`: bold-word regime;
  - `PR precision = 100 PR`: number of digits of `LONG LONG INT`/`LONG LONG REAL`;
  - other pragmats are ignored.

### 2.4 Denotations

| Mode | Examples |
|---|---|
| `INT` | `0`, `42`, `1 000 000` (spaces allowed between digits) |
| `REAL` | `3.14`, `.5`, `1e3`, `1.5e-3`, `1.5E-3`, `1.5\e-3`, `2\3` |
| `BITS` | `2r1010`, `4r123`, `8r17`, `16rff` |
| `CHAR` | `"a"`, `""""` (quote character) |
| string `[]CHAR` | `"abc"`, `"a""b"`, `"con" "catenation"` (adjacent denotations, a68g extension) |
| `BOOL` | `TRUE`, `FALSE` |
| `VOID` | `EMPTY` |
| names, miscellaneous | `NIL`, `SKIP` |
| lengths | `LONG 1`, `LONG LONG 2.5`, `LONG 16rffff` |

A denotation of one length is **not** widened to another length (RR rule):
`LONG INT x := 1` is rejected; write `LONG INT x := LONG 1` or `LENG 1`.

### 2.5 Symbols

Symbolic operators are formed according to the RR: one character among `+ - ! ? % ^ & ~`
or `< > / = *`, optionally followed by one character among `< > / = *`, optionally followed
by `:=` or `=:` (`+:=`, `%*:=`, `+=:`, `<=`, `/=`, `**`, `%*`, `+*`…).
`|`, `|:` and `!` (alternative form of `|`) are used by brief clauses; `@` is equivalent to
`AT`. The brackets `[ ]` of slices may be replaced by `( )` as in the RR.
A few Unicode characters are accepted: `×` `÷` `≤` `≥` `≠` `¬` `∧` `∨`.

---

## 3. Modes

### 3.1 Primitive modes and lengths

| Mode | Representation | Precision / width |
|---|---|---|
| `INT` | 32-bit integer | `max int` = 2147483647, `int width` = 10 |
| `LONG INT` | multiprecision integer | 35 digits (`long max int` = 10^35 − 1), `long int width` = 36 |
| `LONG LONG INT` | multiprecision integer | 70 digits by default (`PR precision PR`), `long long int width` = 71 |
| `REAL` | IEEE 754 double | `real width` = 15, `exp width` = 3, `small real` ≈ 2.22e-16 |
| `LONG REAL` | multiprecision decimal | 35 significant digits, printed with `long real width` = 28 |
| `LONG LONG REAL` | multiprecision decimal | 70 digits (printed with 63), adjustable with `PR precision PR` |
| `BITS`, `LONG BITS`, `LONG LONG BITS` | | 32, 116, 232 bits |
| `BYTES`, `LONG BYTES` | | 32, 256 characters |
| `COMPL`, `LONG COMPL`, `LONG LONG COMPL` | `STRUCT (L REAL re, im)` | |
| `BOOL`, `CHAR`, `VOID`, `STRING` | `STRING = FLEX [1:0] CHAR` | `max abs char` = 65535 |
| `FORMAT`, `FILE`, `CHANNEL`, `SEMA` | | |

`SHORT INT`, `SHORT REAL`… are accepted and have the same length as the unqualified mode
(`int shorths` = 1). `COMPLEX` is a synonym of `COMPL`.

The transcendental functions of `LONG REAL` and `LONG LONG REAL` (`long sqrt`, `long exp`,
`long ln`, `long sin`… and `long pi`) are computed at full precision (fixed-point series
with guard digits).

### 3.2 Constructed modes

- `REF M`, `PROC M`, `PROC (M1, M2) M`, `STRUCT (M1 a, b, M2 c)`, `UNION (M1, M2, …)`,
  rows `[] M`, `[,] M`, `[1:n, 0:m] M`, `FLEX [1:0] M`, rows of rows `[3][2] INT`.
- `MODE`: `MODE POINT = STRUCT (REAL x, y)`, recursive modes
  (`MODE NODE = STRUCT (INT v, REF NODE next)`), mutual recursion
  (`MODE LIST = REF CELL, CELL = STRUCT (INT head, LIST tail)`), actual bounds
  (`MODE VEC = [1:n] REAL`: the bounds are evaluated at each generation).
- **Structural equivalence** (RR 7.3): two modes with the same structure are the same mode,
  recursive modes included; unions are flattened and the order of their components does
  not matter.
- **Unions**: a union cannot contain two firmly related components
  (`UNION (INT, REF INT)` is rejected, RR 4.7.1).
- **Well-formed modes** (RR 7.4): every cycle must go through a `REF` or `PROC` *and*
  through a `STRUCT` or `PROC`; `MODE M = STRUCT (INT i, M m)` and
  `MODE L = UNION (VOID, REF L)` are rejected.

---

## 4. Coercions and contexts

All coercions of the RR are implemented, in all five contexts:

| Coercion | Example | Contexts |
|---|---|---|
| deproceduring | `INT a = next` (`PROC INT next`) | all |
| dereferencing | `INT c = r` (`REF INT r`), several levels | weak, meek, firm, strong |
| uniting | `UNION (INT, BOOL) u = TRUE` | firm, strong |
| widening | `INT → REAL → COMPL`, `BITS → []BOOL`, `BYTES → []CHAR` (and lengths) | strong |
| rowing | `[] INT one = 7`, `[,] INT m = one`, `REF [] INT r = x` | strong |
| voiding | `x := 1;`, `p;` (call of a `PROC VOID`) | strong |

- **Contexts**: soft (destination of an assignment), weak (primary of a slice or of a
  selection), meek (conditions, subscripts, bounds, primary of a call), firm (operands),
  strong (sources, parameters, results).
- **Balancing** of conditional, case and conformity clauses in non-strong contexts:
  `(a < b | 1 | 2.5) * 2` yields `2.0`. In a strong context each branch is coerced to the
  required mode (for example in `print`, `IF … THEN 1 ELSE 2.5 FI` prints the integer `1`).
- `FLEX` is a property of names only: the mode of a value obtained by dereferencing a
  `REF FLEX [] CHAR` is `[] CHAR`.
- Identity relation (`IS`, `ISNT`, `:=:`, `:/=:`): both names are compared after
  deproceduring, following the rule of the RR and a68g; `p IS NIL` therefore compares the
  *variable* `p` (always different from `NIL`): write `REF NODE (p) IS NIL` to compare its
  value.

---

## 5. Clauses and units

- **Serial clause** with declarations, labels (`l: unit`, after the declarations) and
  `EXIT` (`(TRUE | 10 EXIT one: 20)` yields 10).
- **Collateral clauses** `(a, b)` (executed in order), **displays** of rows and
  structures, `()` empty row.
- **Parallel clause** `PAR (u1, u2, …)` / `PAR BEGIN … END`: each unit runs on a
  **real thread** (.NET thread); see §8.
- **Conditionals** `IF … THEN … ELIF … ELSE … FI` and `( c | a |: c2 | b | d )`;
  declarations made in the condition are visible in the branches.
- **Case clauses** `CASE n IN a, b, c OUSE m IN … OUT … ESAC` and `( n | a, b, c | d )`.
- **Conformity** `CASE u IN (INT i): …, (REAL): …, (UNION (CHAR, BOOL) x): … OUT … ESAC`,
  brief form `( u | (INT i): … | … )`, `OUSE`; a single unit may carry several modes
  (extension: `(INT, REAL x): …` is equivalent to a union).
- **Loops** `FOR i FROM a BY b TO c WHILE cond DO … OD`, all combinations and optional
  parts; `DOWNTO` (a68g); `UNTIL cond` just before `OD` (a68g); bare loop `DO … OD`.
  The counter is an identity local to each iteration.
- **Jumps** `GOTO l`, `GO TO l`, or the label alone `l`; a jump can leave procedures and
  recursions; a label in a `PROC VOID` context becomes a procedure that jumps
  (`call(escape)`).
- **Declarations**: identities (`INT n = 3`), variables (`INT x := 1`, `LOC`, `HEAP`, `NEW`),
  procedures (`PROC f = (INT x) INT: …`, `PROC f := …`), `MODE`, `OP`, `PRIO`;
  joined declarations (`INT a = 1, REAL b = 2.0`); identifiers are visible in their whole
  range (mutual recursion).
- **Units**: assignments (multiple `a := b := 0`, chosen destination `(c | a | b) := 0`),
  `LOC`/`HEAP` generators, routine texts, calls, slices (`a[2:5]`, `a[2:5 @ 0]`, `a[@0]`,
  `a[i, ]`, `a[, j]`), selections (`x OF p`, also on a row of structures: `x OF points` is
  a row), casts (`REAL (1)`), monadic and dyadic `LWB`/`UPB`, `ELEMS` (a68g).
- **a68g extensions**: `ANDF`/`ANDTH` and `ORF`/`OREL` (lazy evaluation),
  `ASSERT (condition)`, `stop`, partial parametrisation `f(1, )` (yields a procedure of the
  missing parameters).

### 5.1 Procedures and scopes

Routine texts are closures. The **scope** (RR 2.1.3.2) is checked at run time: assigning
the name of a local variable to an older variable, or yielding the name of a local
variable as the result of a routine or a block, raises the error "scope violation". The
scope of a routine is that of the innermost range whose identifiers it uses: a routine
that only uses globals can be yielded as a result (`PROC make = (BOOL d) PROC (INT) INT: …`).

### 5.2 Transient names

A slice or a multiple selection of a `FLEX` name (`s[2]`, `s[2:3]` for a `STRING s`) is a
**transient name** (RR 2.1.3.6.c): it can be used as a destination (`s[2] := "B"`) or be
dereferenced (`CHAR c = s[2]`), but it can be neither bound to an identifier
(`REF CHAR c = s[2]`), nor assigned to a variable, nor yielded by a routine, nor used as
the operand of an operator (`s[2] +:= …`), as in the RR and a68g.

### 5.3 Operators

- Symbolic and bold `OP`s, monadic and dyadic, with or without a mode parenthesis
  (`OP MAX = (INT a, b) INT: …`, `OP (INT, INT) INT MAX = …`).
- `PRIO X = n` (1 to 9); a dyadic operator without a priority is rejected. Monadic
  operators bind more tightly than all dyadic ones; at equal priority, association is to
  the left.
- Overloading: identification (RR 7.2) chooses, in the innermost range containing one,
  the definition whose operands fit in a firm context.
  A local redefinition hides the prelude (`OP + = (INT a, b) INT: a * b` in a block).

---

## 6. Standard prelude

### 6.1 Operators (RR 10.2)

| Operands | Operators |
|---|---|
| `L INT` | `+ - * / % OVER %* MOD ** ^ UP`, `= /= ~= < <= > >= EQ NE LT LE GT GE`, monadic `- + ABS SIGN ODD REPR LENG SHORTEN BIN`, `I +*` |
| `L REAL` (and mixed with `L INT`) | `+ - * / **` (`INT` or `REAL` exponent), comparisons, `- + ABS SIGN ROUND ENTIER LENG SHORTEN`, `I +*` |
| `L COMPL` (mixed with `L REAL`, `L INT`) | `+ - * / **`, `= /=`, `RE IM ABS ARG CONJ LENG SHORTEN` |
| `BOOL` | `AND & OR XOR NOT ~ = /= ABS` |
| `CHAR`, strings | comparisons, `+` (concatenation), `*` (repetition), `ABS`, `REPR` |
| `L BITS` | `AND & OR XOR NOT ~ = /= <= >= SHL UP SHR DOWN ABS BIN ELEM SET CLEAR LENG SHORTEN` |
| `L BYTES` | comparisons, `ELEM`, `LENG`, `SHORTEN` |
| rows | `LWB UPB` (monadic and dyadic), `ELEMS` |
| combined assignments | `+:= -:= *:= /:= %:= %*:= +=:` and `PLUSAB MINUSAB TIMESAB DIVAB OVERAB MODAB PLUSTO` |
| `SEMA` | `LEVEL` (creation and reading), `UP`, `DOWN` |

Priorities: 1 combined assignments; 2 `OR`; 3 `AND & XOR`; 4 `= /= EQ NE`;
5 `< <= > >= LT LE GT GE`; 6 `+ -`; 7 `* / % OVER %* MOD ELEM SET CLEAR`;
8 `** ^ UP DOWN SHL SHR LWB UPB ELEMS`; 9 `I +*`.

### 6.2 Environment enquiries and constants

`int lengths` (3), `int shorths` (1), `real lengths`, `real shorths`, `bits lengths`,
`bytes lengths` (2), `max int`, `long max int`, `long long max int`, `max real`,
`small real`, `long small real`, `long max real`…, `int width`, `long int width`,
`real width`, `long real width`, `long long real width`, `exp width`, `bits width`,
`long bits width`, `bytes width`, `long bytes width`, `max abs char`, `max bits`,
`pi`, `long pi`, `long long pi`, `null character`, `blank`, `flip` (`T`), `flop` (`F`),
`errorchar` (`*`), `newline char`, `tab char`, `formfeed char`.

### 6.3 Functions

- `sqrt exp ln log sin cos tan arcsin arccos arctan` (+ `sinh cosh tanh arcsinh arccosh
  arctanh cbrt`, `arctan2`) for each length: `long sqrt`, `long long exp`…
  (a68g aliases: `asin`, `acos`, `atan`, `dsqrt`, `qsqrt`…);
- complex: `complex sqrt`, `complex exp`, `complex ln`, `complex sin`, `complex cos`,
  `complex tan`, `complex arcsin`, `complex arccos`, `complex arctan` (and
  `long complex …`);
- random numbers: `random`, `next random (REF INT)`, `first random (INT)` (deterministic
  generator; the sequence differs from that of a68g);
- conversions: `whole (x, width)`, `fixed (x, width, decimals)`,
  `float (x, width, decimals, exponent)`, `real (x, w, d, e, radix)` (a68g);
  positive width: the sign is always written; negative: the sign only if negative;
  zero: minimal width;
- strings: `char in string`, `last char in string`, `string in string`, `to upper`,
  `to lower`, `is alpha`, `is digit`…;
- `bits pack`, `long bits pack`, `bytes pack`, `long bytes pack`;
- miscellaneous a68g: `seconds`, `clock`, `sleep`, `stop`.

---

## 7. Transput

All console input and output goes through the IDE (or the Windows console for a generated
`.exe`); disk files are relative to the program's working directory.

### 7.1 Unformatted (RR 10.3.3)

`print`, `write`, `put (f, …)`, `read`, `get (f, …)`. Rows and structures are
straightened. Standard output, identical to a68g:

| Value | `print` |
|---|---|
| `1` | `         +1` (width `int width + 1` = 11) |
| `-67` | `        -67` |
| `1.5` | `+1.50000000000000e  +0` |
| `LONG 1.5` | `+1.500000000000000000000000000e  +0` |
| `TRUE` | `T` |
| `"abc"` | `abc` |
| `2r101` | 32 characters `F…FTFT` |
| `3 I 4` | both real parts one after the other |

No space is inserted between two values. On input, numbers and booleans skip spaces and
line ends; a string is read up to the end of the line (not consumed) or a terminator
character (`make term`); a character skips line ends.

### 7.2 Formatted (RR 10.3.4–10.3.5)

`printf`, `writef`, `putf`, `readf`, `getf`, formats `$ … $` and `FORMAT` values:

| Item | Meaning |
|---|---|
| `d`, `z` | digit; `z` replaces leading zeros with spaces |
| `+`, `-` | sign (`-`: space if positive); the sign floats over the zeros of `z` frames |
| `.`, `e` | decimal point, exponent (`+d.3de+2d`) |
| `i` | separator of the parts of a complex number |
| `a` | character (`5a`: string of 5 characters) |
| `b` | boolean (`T`/`F`); `b("yes","no")` boolean choice |
| `c("monday","tuesday",…)` | integral choice |
| `2r8d`, `16r4d` | bits in radix 2, 4, 8, 16 |
| `g`, `g(w)`, `g(w, d)`, `g(w, d, e)` | general: `print`, `whole`, `fixed`, `float` |
| `f(format)` | format pattern (computed format) |
| `s` | suppressed frame (`sd`, `s.`) |
| `x`, `y`, `l`, `p`, `k`, `q` | space, backspace, new line, new page, position (`20k`), blank |
| `"text"` | literal insertion |
| `n`, `n(expr)` | static and dynamic replicators (`n(w)d`, `3(d" ")`) |
| `( … )` | repeated collection |

Pictures are separated by commas: `$2d"/", 2d"/", 4d$` writes three integers, whereas
`$3d"/"2d"/"4d$` is a single pattern of 9 digits. An exhausted format starts again
(`on format end` event). At the end of `printf`, the insertions that follow are performed
up to the next pattern (`printf(($"Hello"l$))` writes the line).

### 7.3 Files and channels (RR 10.3.1)

- Standard files: `stand in`, `stand out`, `stand error`, `stand back`;
  channels: `stand in channel`, `stand out channel`, `stand back channel` (disk, read,
  write, binary), `stand error channel`.
- `open (f, name, channel)`, `establish (f, name, channel)`, `create (f, channel)` (yield 0
  on success), `associate (f, REF STRING)` (file on a string), `close`, `lock`,
  `scratch` (deletes the file), `reset`.
- Layout: `newline`, `newpage`, `space`, `backspace`, `set char number (f, n)`.
- Positions: `char number`, `line number`, `page number`; `set (f, page, line, char)`
  (RR); `seek (f, n)` (a68g).
- Events: `on logical file end`, `on physical file end`, `on line end`,
  `on page end`, `on format end`, `on value error`, `on char error (f, PROC (REF FILE,
  REF CHAR) BOOL)`, `on open error`; the routine yields `TRUE` if the event is handled
  (otherwise the default action or a runtime error follows), and may leave with `GOTO`.
- `make term (f, ",;")`, `idf`, `term`, enquiries `put possible`, `get possible`,
  `bin possible`, `set possible`, `reset possible`, `reidf possible`, `compressible`,
  `eof`, `eoln` (a68g).
- Binary: `put bin`, `get bin`, `write bin`, `read bin` (own internal format, read back
  exactly by `get bin`).
- Simplified a68g input: `read int`, `read real`, `read string`, `read line`,
  `get int (f)`, `print int (n)`…

---

## 8. Parallelism

`PAR (a, b, c)` runs each unit on a real thread and waits for them all to finish.
Synchronisation uses Dijkstra semaphores (RR 10.2.4):

```algol68
SEMA free = LEVEL 3, full = LEVEL 0, mutex = LEVEL 1;
DOWN free; DOWN mutex; … ; UP mutex; UP full
```

- `LEVEL n` creates a semaphore, `LEVEL s` reads its level, `DOWN s` waits then
  decrements, `UP s` increments and wakes up a waiting process.
- **Deadlock detection**: if all processes are waiting on a `DOWN` and no `UP` is possible,
  the error "deadlock" is raised.
- An error or a jump out of a branch stops the other branches, then propagates.
- Stopping (Stop button) also interrupts the branches and the waits.

---

## 9. Runtime checks

Every error gives an English message, the position (line, column) and the call stack:

- subscript or trimmer out of bounds, wrong number of subscripts;
- assignment of rows with different bounds (except `FLEX`);
- dereferencing of, or assignment to, `NIL`;
- uninitialised value (variable, element, field, union);
- identifier used before its declaration (detected at compile time when possible);
- division by zero (`/`, `%`, `MOD`), integer overflow (`INT`, `LONG INT`,
  `LONG LONG INT`), real overflow, function domain (`sqrt`, `ln`, `arcsin`…);
- scope violations;
- uninitialised union in a conformity clause;
- end of file without an event routine, unreadable value;
- deadlock, false `ASSERT`, recursion too deep (100,000 nested calls).

---

## 10. Debugging (IDE)

- Line breakpoints (including in the body of a `PROC` made of a single unit), pause,
  step into / step over / step out, pause on entry and on error (the stack is kept).
- Call stack with routine names (`PROC f`: "f", operators: "OP +", outermost level:
  "main program").
- Variables visible at the current line; variables (names) are shown by their value;
  structures, rows (first 1,000 elements), unions and names can be expanded.
- `PAR` branches can also stop on a breakpoint.

---

## 11. Standalone executables

The compiled program is serialised as **sources** (main file and included files); the
executable recompiles these sources at start-up (compilation takes a few milliseconds),
which guarantees the same behaviour as when running in the IDE.

---

## 12. Examples

```algol68
# Linked list #
MODE NODE = STRUCT (INT value, REF NODE next);
REF NODE head := NIL;
FOR i TO 5 DO head := HEAP NODE := (i, head) OD;
REF NODE p := head;
WHILE REF NODE (p) ISNT NIL DO print(value OF p); p := next OF p OD
```

```algol68
# User-defined operator and priority #
PRIO MAX = 9;
OP MAX = (INT a, b) INT: (a > b | a | b);
print((3 MAX 7, newline))                     #          +7 #
```

```algol68
# Format #
printf(($"x = "g(0)", y = ", -d.2d l$, 42, 3.14159))   # x = 42, y =  3.14 #
```

```algol68
# Knuth's "man or boy" test: -67 #
PROC a = (INT k, PROC INT x1, x2, x3, x4, x5) INT:
BEGIN
   INT kk := k;
   PROC b = INT: (kk -:= 1; a(kk, b, x1, x2, x3, x4));
   IF kk <= 0 THEN x4 + x5 ELSE b FI
END;
print(a(10, INT: 1, INT: -1, INT: -1, INT: 1, INT: 0))
```

---

## 13. Deviations and limitations

- `CHAR` is a UTF-16 character (`max abs char` = 65535); a68g uses bytes
  (`max abs char` = 255).
- `LONG REAL` computes with 35 decimal digits (a68g: 28 to 35 depending on
  normalisation); output uses the same widths as a68g (28 and 63 digits).
- The random number generator is not that of a68g (different sequences).
- `set` follows the RR (`set (f, p, l, c)`); the two-parameter a68g variant is called
  `seek`.
- Standard output has no maximum line length (as in a68g): the `on line end` event is
  only raised on input.
- a68g extensions not provided: graphics (`draw …`), sound, PostgreSQL, curses,
  system procedures (`system`, `fork`, `execve`…), the GSL library (special functions,
  linear algebra, FFT), C-style format patterns (`%d`), POINT stropping.
- A collateral or parallel clause with a single unit runs on the current thread; operands
  and collateral elements are evaluated from left to right (the RR leaves the order
  undefined).
- Recursion depth is limited to 100,000 nested calls.

---

## 14. Tests

`dotnet run --project tests/Algol68Tests` compiles and runs every program of
`samples/algol68` and compares:

- the output with the `.out` file (standard input is read from `.in` if it exists);
- the expected diagnostics with `.err` (one line per error: `line: message fragment`);
- the expected runtime error with `.rte` (`line: message fragment`).

Internal tests also check the debugger (breakpoints, stepping, stack, variables,
pause request, pause on error), cancellation (infinite loop, `PAR` branches, pause),
serialisation, performance (10 million iterations), the error stack and the robustness
of the compiler on altered programs.
Options: `--filter text` (selection by path), `--show file.a68` (runs a program and
prints its output), `-v` (detailed list).
