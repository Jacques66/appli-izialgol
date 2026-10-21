# Pascal — reference guide

This compiler implements **Pascal** as defined by Kathleen Jensen and Niklaus
Wirth, *Pascal User Manual and Report* (Revised Report, 1974; second edition
1978), as implemented by **Pascal 6000**, the compiler written at ETH Zürich for
the CDC 6000 series (CDC 6400, 6600, Cyber 70). It is the pure Pascal of the
period: no string type, no units, no `else` in case statements, no `break`, no
`halt`, none of the UCSD, Turbo or ISO extended features.

The compiler checks every declaration and every type before anything runs and
reports all the errors it finds (file, line, column), then the program runs on a
tree-walking virtual machine with runtime checks and the full debugger of the
IDE (breakpoints, step into/over/out, pause, call stack, variables including
records, arrays and pointers, break on runtime error).

Files: `.pas` or `.p`. Command-line compiler: `pasc`.

---

## 1. History

Niklaus Wirth designed Pascal in 1968–1969 as a successor of Algol W, a language
for teaching programming as a systematic discipline and for writing reliable
programs efficiently. The first compiler ran on the CDC 6600 of ETH Zürich in
1970; the language was described in *The Programming Language Pascal* (Acta
Informatica, 1971) and revised in 1973. The *Pascal User Manual and Report*
(Jensen and Wirth, Springer, 1974) became the reference definition. Pascal 6000,
written in Pascal itself, was distributed to CDC sites all over the world, and
its descendant Pascal-P (the portable P-code compiler) carried the language to
almost every computer of the 1970s. The ISO standard 7185 followed in 1983.

Pascal 6000 characteristics reproduced here: integers of 48 significant bits
(`maxint` = 2⁴⁸ − 1 = 281474976710655), sets of at most 59 elements (ordinal
values 0..58, one 60-bit word), the predefined type `alfa`
(`packed array[1..10] of char`, ten 6-bit characters per word), the default
field widths of `write`, and the floating-point output with three exponent
digits.

---

## 2. Lexical rules

* Words are **case-insensitive**: `BEGIN`, `Begin` and `begin` are the same.
  All the characters of an identifier are significant (Pascal 6000 only used the
  first 10). Identifiers are letters and digits (no `_`).
* Reserved words: `and array begin case const div do downto else end file for
  function goto if in label mod nil not of or packed procedure program record
  repeat set then to type until var while with`. The directive `forward` is
  not reserved.
* Comments: `{ ... }` and `(* ... *)`; a comment opened by `{` ends at `}`, one
  opened by `(*` ends at `*)`, comments do not nest.
* Alternative symbols: `(.` and `.)` for `[` and `]`; `@` and `↑` for `^`.
* Numbers: `123`, `3.14`, `1.5E-3`, `2e10`. A real number needs digits on both
  sides of the point (`1.` and `.5` are not numbers). Integers larger than
  `maxint` are rejected.
* Strings: `'text'`, a quote inside is doubled: `'it''s'`. A string of one
  character is a `char` constant; a string of *n* > 1 characters has the type
  `packed array[1..n] of char`. Strings cannot span lines; the empty string is
  not allowed.
* Labels are unsigned integers of at most four digits.

---

## 3. Program structure

```pascal
program name(input, output, f, g);   { program heading with its file parameters }
label 1, 99;                          { the parts appear in this order, each at most once }
const n = 10;
type index = 1..n;
var a: array[index] of real;
procedure p(...); ... ;
function f(...): real; ... ;
begin
   statements
end.
```

* The **program heading** lists the program parameters: `input` and `output`
  (the console) and the external files of the program. Using `read`, `readln`,
  `eof` or `eoln` without a file requires `input` in the heading; `write`,
  `writeln` and `page` without a file require `output`.
* Every other program parameter must be declared as a file variable of the
  program: it is the file of that name in the working directory of the program
  (see section 9).
* Declarations come in the order of the Report — labels, constants, types,
  variables, procedures and functions — and every identifier is declared before
  it is used (except pointer domain types, see below, and `forward` routines).

### Constants

`const` *identifier* `=` *constant*: an unsigned number, a constant identifier
(optionally signed when numeric), or a string. There are no constant
expressions.

```pascal
const max = 100; min = -max; pi = 3.14159265358979; title = 'Report'; star = '*';
```

### Standard identifiers

| Kind | Identifiers |
|---|---|
| types | `integer`, `real`, `boolean`, `char`, `text`, `alfa` |
| constants | `maxint` (281474976710655), `true`, `false` |
| files | `input`, `output` |
| procedures | `read`, `readln`, `write`, `writeln`, `get`, `put`, `reset`, `rewrite`, `page`, `new`, `dispose`, `pack`, `unpack` |
| functions | `abs`, `sqr`, `sin`, `cos`, `exp`, `ln`, `sqrt`, `arctan`, `odd`, `trunc`, `round`, `ord`, `chr`, `succ`, `pred`, `eof`, `eoln` |

Standard identifiers belong to a block that surrounds the program: they may be
redeclared (`var sqr: integer;` hides the function `sqr`).

---

## 4. Types

| Type | Values | Notes |
|---|---|---|
| `integer` | −281474976710655 .. 281474976710655 | every operation is checked (overflow is a runtime error) |
| `real` | IEEE double precision | about 15 significant digits, like the 60-bit CDC real |
| `boolean` | `false`, `true` | the enumeration (false, true): `false < true` |
| `char` | ordinal values 0..255 (ISO 8859-1) | ASCII instead of the 64-character CDC display code |
| enumeration | `(red, green, blue)` | `ord(red)` = 0 |
| subrange | `1..10`, `'a'..'z'`, `mon..fri` | assignments are checked |
| `array[I] of T` | I is any ordinal type | `array[1..n, 'a'..'z'] of T` abbreviates `array[1..n] of array['a'..'z'] of T` |
| `record ... end` | fields, optional variant part | see below |
| `set of B` | B ordinal, values 0..58, or `char` | see below |
| `file of T`, `text` | sequences | see section 9 |
| `^T` | pointers to dynamic variables | `nil` is compatible with every pointer type |

`packed` may precede `array`, `record`, `set` and `file`. A packed structure
has the same representation here; the rules of the Report apply: a component
of a packed structure cannot be a var parameter, and `pack`/`unpack` copy
between packed and unpacked arrays. A **string type** is
`packed array[1..n] of char`: strings of the same length can be assigned and
compared with `= <> < <= > >=` (lexicographic order of the character codes),
and written with `write`.

### Type compatibility

The Report leaves type equivalence open; this implementation follows Pascal
6000 and the ISO standard:

* Each type definition or anonymous type denoter creates a new type; `type t = u`
  makes `t` another name for `u`. Arrays and records are assignable only to
  variables of the same type.
* Two ordinal types are compatible when they have the same host type
  (`1..10` and `-5..5` are both integer). Two set types are compatible when
  their base types are. Strings of the same length are compatible.
* Two pointer types are the same when they have the same domain type
  (`var p: ^node; q: ^node` can be assigned to each other), as in Pascal 6000.
* Assignment compatibility adds integer → real.
* A var parameter must have exactly the type of the formal parameter (not a
  subrange of it).

### Records with variant parts

```pascal
type shape = (circle, rectangle);
     figure = record
                 x, y: real;
                 case kind: shape of
                    circle:    (radius: real);
                    rectangle: (width, height: real)
              end;
```

The tag field `kind` is an ordinary field. Accessing a field of a variant
checks the tag: an **undefined tag** or a field of an **inactive variant** is a
runtime error. Assigning a new value to the tag makes the fields of the
variants undefined. A variant part may have no tag field (`case boolean of`):
the fields are then not checked. Variants may be nested; every field name is
unique in the record. Each variant has its own storage (variants do not
overlay, so a value cannot be reinterpreted through another variant).

### Sets

The base type of a set is an ordinal type whose values lie in **0..58** (59
elements: a subrange of integer within 0..58, an enumeration of at most 59
constants, `boolean`), as in Pascal 6000 where a set was one 60-bit word; a
larger base type is a compile error. Pascal 6000 allowed `set of char`
because its character set had 64 characters; here `char` has 256 values, and
the faithful rule kept is: **`set of char` and sets of subranges of char are
allowed and hold ordinal values 0..255**.

Set constructors: `[]`, `[a, b, c]`, `[lo..hi, x]` (elements are checked at run
time: 0..58, or 0..255 for characters). Operators: `+` union, `*`
intersection, `-` difference, `=` `<>`, `<=` (inclusion) `>=`, and `x in s`.
`<` and `>` are not defined for sets.

### Pointers

`new(p)` allocates a variable `p^`; `new(p, t1, ..., tn)` allocates a record
with the variants selected by the tag values t1..tn (the tag fields receive
these values). `dispose(p)` releases the variable and makes `p` undefined;
any other pointer to it becomes a dangling pointer (detected). A pointer type
may name a type declared later in the same type definition part:

```pascal
type link = ^node;
     node = record key: integer; next: link end;
```

---

## 5. Expressions

| Precedence | Operators |
|---|---|
| highest | `not` |
| multiplying | `*` `/` `div` `mod` `and` |
| adding | `+` `-` `or` (and the sign of the first term) |
| lowest | `=` `<>` `<` `<=` `>` `>=` `in` |

* `/` always yields a real. `div` truncates towards zero; `i mod j` is the
  remainder of `div`, with the sign of `i` (`-7 mod 2` = −1) — the remainder of
  the CDC integer division, as in Pascal 6000. `div 0` and `mod 0` are errors.
* A sign may only begin a simple expression: `a * -b` is a syntax error, write
  `a * (-b)`.
* Relational operators do not chain: write `(a < b) and (b < c)`.
* `and` and `or` evaluate **both operands** (no short-circuit evaluation, as in
  Pascal 6000 and Pascal-P): `(i <= n) and (a[i] <> x)` fails when `i > n`.
* Integer arithmetic is checked against ±maxint; real arithmetic is checked
  for overflow (infinite results) and undefined results.
* Function calls cannot be followed by selectors (`f(x)^` is not allowed).

---

## 6. Statements

| Statement | Form |
|---|---|
| assignment | `v := e` (also `f := e` inside function `f`) |
| procedure call | `p`, `p(a, b)` |
| compound | `begin s1; s2; ... end` (empty statements are allowed: `begin end`, `s;`) |
| if | `if c then s1 else s2` (`else` belongs to the nearest `if`) |
| case | `case e of 1, 2: s1; 3: s2 end` — no `else`: a selector without matching label is a runtime error |
| while | `while c do s` |
| repeat | `repeat s1; s2 until c` |
| for | `for i := a to b do s`, `for i := a downto b do s` |
| with | `with r1, r2 do s` (= `with r1 do with r2 do s`) |
| goto | `goto 10` |

**for**: the control variable is a variable of ordinal type declared in the
variable part of the block containing the statement; the initial and final
values are evaluated once, before the loop; the statement may not assign the
control variable (nor pass it as a var parameter, nor read into it); after the
loop the control variable is **undefined** (Revised Report 9.2.3.3) — using it
is a runtime error — unless the loop was left by a goto.

**with**: the record variable is selected once when the statement starts
(`with a[i] do` keeps the element even if `i` changes); its field names become
visible as variables. Fields of nested records and pointers work as well
(`with p^, origin do`).

**goto and labels**: labels are declared in the `label` part of the block whose
statement they prefix. A goto may lead to a statement of an enclosing statement
sequence (out of loops and compound statements) but not into a structured
statement (compile error). A goto may also leave procedures and functions and go
to a label of an enclosing block (**non-local goto**): the activations in
between are terminated; the target is the activation of the enclosing block
found through the static chain (the right one in recursive procedures). If the
labelled statement is not active at that moment (for example inside a loop that
is not running), it is a runtime error.

---

## 7. Procedures and functions

```pascal
procedure name(a, b: integer; var r: real; procedure p(x: integer); function f(x: real): real);
function name(parameters): resulttype;
```

* **Value parameters** receive a copy (arrays and records are copied);
  **var parameters** denote the actual variable.
* Files can only be var parameters. Components of packed structures and tag
  fields cannot be var parameters.
* A function returns its result by an assignment to its name, inside its block
  (or in a routine nested in it). The result type is a simple type (ordinal or
  real) or a pointer type. A function that returns without assigning its result
  is a runtime error.
* Procedures and functions may be nested and recursive; inner routines access the
  variables of the enclosing blocks.
* **forward**: `procedure p(x: integer); forward;` declares the heading; the
  block follows later as `procedure p; begin ... end;` (the parameter list and
  result type are not repeated).
* **Procedural parameters** (Revised Report 11.2.2): `procedure p` and
  `function f: real` without a parameter list are the 1974 form — the actual
  routine may have value parameters only, and the number and types of the
  arguments are checked when the call is executed. The form with a parameter
  list, `function f(x: real): real`, is checked at compile time: the actual
  routine must have the same heading (same kinds and types of parameters, same
  result type). Standard procedures and functions cannot be passed as
  parameters (write a function that calls them). An actual routine passed as a
  parameter keeps its environment: a local procedure passed down a recursion
  accesses the variables of its own activation (Knuth's "man or boy" test
  works).

---

## 8. Standard procedures and functions

| Function | Argument | Result |
|---|---|---|
| `abs(x)`, `sqr(x)` | integer or real | same type |
| `sin`, `cos`, `exp`, `ln`, `sqrt`, `arctan` | integer or real | real (`ln` of x ≤ 0 and `sqrt` of x < 0 are errors) |
| `odd(i)` | integer | boolean |
| `trunc(x)`, `round(x)` | real (an integer argument is converted) | integer; `round` rounds halves away from zero; a result beyond maxint is an error |
| `ord(x)` | ordinal | integer |
| `chr(i)` | integer 0..255 | char |
| `succ(x)`, `pred(x)` | ordinal | same type; no successor of the last value (error) |
| `eof`, `eof(f)` | file | boolean |
| `eoln`, `eoln(f)` | text file | boolean (error at the end of the file) |

`new`, `dispose` (section 4), `pack(a, i, z)` copies `a[i]`, `a[i+1]`… into the
packed array `z`; `unpack(z, a, i)` copies `z` into `a[i]`, `a[i+1]`…; `a` is
unpacked, `z` packed, with the same component type. File procedures: section 9.

---

## 9. Files, input and output

A file is a sequence of components of one type; `f^` is the **buffer variable**.

| Operation | Meaning |
|---|---|
| `rewrite(f)` | empties `f` and opens it for writing (generation); `eof(f)` is true |
| `put(f)` | appends `f^` to `f` |
| `reset(f)` | opens `f` for reading (inspection) at its beginning: `f^` is the first component |
| `get(f)` | advances to the next component |
| `eof(f)` | true after the last component (always true while writing) |
| `read(f, v)` | `v := f^; get(f)` |
| `write(f, e)` | `f^ := e; put(f)` |

Reading a file that is being written, or writing a file that is being read, is
a runtime error, as is `get` past the end or `put` with an undefined buffer.

### Text files

`text` is a file of characters structured in lines. `eoln(f)` is true at the end
of a line, where `f^` is a blank. `readln(f)` skips to the beginning of the next
line, `writeln(f)` ends the current line, `page(f)` starts a new page (it ends
the current line if needed and writes a form feed, `chr(12)`). When a text file
is reset, a last line without its end is completed.

* `read(f, v1, ..., vn)` for variables of type integer, real or char (and their
  subranges, checked). Numbers skip blanks and line ends first; an integer is an
  optional sign and digits; a real is an optional sign, digits, an optional
  fraction and an optional exponent (`3`, `-2.5`, `1e6`, `6.02E+23`). An error is
  reported when no digit is found or when the end of the file is reached.
  A `char` reads one character (a blank at the end of a line).
* `readln(f, ...)` reads, then skips the rest of the line.
* `write(f, p1, ..., pn)` with p = `e`, `e:w` or (reals) `e:w:d`;
  `writeln(f, ...)` ends the line.

Default field widths (Pascal 6000):

| Value | Default width | Form |
|---|---|---|
| integer | 10 | right-justified; a larger width is used when needed |
| real | 22 | floating point: ` 1.23450000000000E+003` (sign or blank, one digit, point, w−8 digits, `E`, sign, 3 exponent digits) |
| real with `:w:d` | — | fixed point, d ≥ 1 fraction digits: `x:8:2` → `    3.14` |
| boolean | 10 | `TRUE`, `FALSE` (upper case, as on the CDC); truncated to w when w is smaller |
| char | 1 | right-justified |
| string | its length | right-justified; the first w characters when w is smaller |

Reals are rounded from their shortest exact decimal representation, halves away
from zero (`0.125:5:2` → ` 0.13`). The floating-point form needs at least 9
positions (one fraction digit). A negative width or fewer than one fraction
digit is a runtime error. Enumerations, pointers and sets cannot be written.

### Files of the program

* `input` and `output` are the console (the Output panel of the IDE, or the
  Windows console). Input is **lazy**: a line is read only when the program needs
  it (`read`, `eof`, `eoln`, `input^`), so a prompt written with `write` appears
  before the program waits.
* `reset(input)` has no effect (the console cannot be rewound); `rewrite(input)`
  and `reset(output)` are errors.
* Other program parameters (`program p(output, data, result)`) are **external
  files**: the file named like the identifier (no extension) in the working
  directory of the program. `reset` reads it (a missing file is empty);
  `rewrite` writes it; the file is written to the disk at the next `reset` and
  at the end of the program. Text files are UTF-8 (characters beyond 255 read as
  `?`); other files use a binary format of this implementation (`PASF` header).
* All other file variables are **internal** (temporary) files, kept in memory and
  discarded when their block ends.
* Implementation restriction: the component type of a file may not contain
  pointers (nor files, as in the Report).

---

## 10. Runtime checks

Every error stops the program with its message, the source position and the
call stack; in the IDE the debugger can stop on it with the variables visible.

* index out of bounds, value out of a subrange (assignment, parameter, read,
  `for` bounds), set element out of range;
* integer overflow beyond ±maxint, real overflow, division by zero (`div`,
  `mod`, `/`);
* use of an **undefined value** (variables are undefined until assigned; the
  `for` control variable after the loop; fields of a variant after a change of
  the tag; the buffer variable after `put`);
* nil pointer, undefined pointer, dangling pointer (disposed variable), double
  `dispose`;
* case selector without matching label;
* undefined variant tag, access to a field of an inactive variant;
* function returning without a result;
* reading past the end of a file, bad number in the input, wrong file mode;
* `chr`, `succ`, `pred`, `trunc`, `round`, `sqrt`, `ln` out of their domain;
* goto to a statement that is not active; stack overflow (recursion too deep).

---

## 11. Implementation-defined choices

| Item | Choice |
|---|---|
| `maxint` | 281474976710655 (2⁴⁸ − 1); integers are 64-bit values checked against ±maxint |
| `real` | IEEE 754 double |
| `char` | ordinal values 0..255 (ISO 8859-1, ASCII below 128) instead of the CDC display code; `ord('A')` = 65 |
| sets | base type within 0..58 (59 elements), except `set of char` (0..255) |
| `alfa` | `packed array[1..10] of char` |
| significant characters of identifiers | all (Pascal 6000: 10) |
| `mod` with negative operands | remainder of the truncating `div`, sign of the dividend |
| `and`, `or` | both operands evaluated |
| default widths | integer 10, real 22, boolean 10, char 1, string its length |
| real output | 3 exponent digits; rounding halves away from zero |
| booleans written | `TRUE` / `FALSE` |
| `page` | ends the current line, then writes a form feed `chr(12)` |
| external files | program parameters = files of the working directory named after the identifier |
| missing external file | reads as an empty file |
| variables at block entry | undefined (reading one is detected) |
| `new(p, t1..tn)` | also assigns the tag fields |
| variants | separate storage per variant (no overlay) |
| pointer type equivalence | same domain type (Pascal 6000) |
| program heading | required, with its parameter list |
| line end on the console | written as `\n` |

---

## 12. Samples

| File | Shows |
|---|---|
| `01_hello.pas` | the smallest program |
| `02_inflation.pas` | repeat loop and real output (User Manual) |
| `03_sieve_sets.pas` | sieve of Eratosthenes with an array of sets (59-element sets) |
| `04_eight_queens.pas` | backtracking, arrays with negative bounds (Wirth) |
| `05_hanoi.pas` | recursion, enumerated type, case |
| `06_linked_list.pas` | pointers, new, dispose, var parameters of pointer type |
| `07_binary_tree.pas` | binary search tree, recursive functions |
| `08_shapes_variants.pas` | records with variants, new(p, tag), with, case |
| `09_text_statistics.pas` | reading the input with eof and eoln, set of char, array indexed by char (input `09_text_statistics.in`) |
| `10_file_of_records.pas` | external file of records with f^, put, get; internal file with read/write |
| `11_expression_evaluator.pas` | recursive descent, forward, non-local goto for error recovery (input `.in`) |
| `12_quicksort.pas` | quicksort (Hoare) |
| `13_heapsort.pas` | heapsort (Floyd) on strings, local goto |
| `14_nonlocal_goto.pas` | leaving a deep recursion with goto |
| `15_integration.pas` | procedural parameters: Simpson's rule, bisection (both heading forms) |
| `16_with_statement.pas` | nested records, with on several records |
| `17_matrix.pas` | two-dimensional arrays, matrix power |
| `18_roman.pas` | Roman numerals (User Manual) |
| `19_word_index.pas` | cross reference: tree of words, lists of line numbers, pack, input^ |

Run them in the IDE (**File › Examples**) or with `pasc samples\pascal\04_eight_queens.pas --run`.

---

## 13. Differences and limits

* Not implemented: the Pascal 6000 extensions outside the Report (`clock`,
  `card`, `expo`, `undefined`, `date`, `time`, `linelimit`, `message`, `halt`,
  segmented files, `extern`/`fortran` routines, the value part, compiler options
  in comments such as `(*$T-*)` — comments are ignored), and every later
  extension (strings, `otherwise`, conformant arrays of ISO level 1).
* The CDC character set and its 64-character collating sequence are replaced by
  ISO 8859-1; Pascal 6000 programs that rely on display-code ordinal values
  (`ord('A')` = 1) must be adapted.
* The carriage-control convention of CDC line printers (the first character of
  each line) is not applied: output is plain text.
* A variable is limited to 8 million components; recursion is limited by the
  stack (a deep recursion reports a stack overflow).
* Variants do not share storage: a free union cannot reinterpret a value.
