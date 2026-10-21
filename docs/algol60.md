# Algol 60 — reference guide

This compiler implements **Algol 60** as defined by the *Revised Report on the
Algorithmic Language ALGOL 60* (1963), with the clarifications and the
environmental block of the *Modified Report* (1976): standard functions,
`inchar`/`outchar`, `ininteger`/`outinteger`, `inreal`/`outreal`, `outstring`,
`stop`, `fault` and the environmental enquiries.

Programs are compiled by the same engine as Simula 67 (Simula is a superset of
Algol 60): the compiler checks types and specifications before anything runs,
and the program runs on the virtual machine with the full debugger of the IDE
(breakpoints, steps, call stack, variables).

Files: `.a60` or `.alg`.

---

## 1. Hardware representations

Two representations are recognised automatically.

**Reserved words** (the default): keywords are ordinary words, case-insensitive,
separated by spaces.

```algol
begin
   integer i;
   for i := 1 step 1 until 3 do outinteger(1, i)
end
```

**Quote stropping** (chosen when the text contains `'BEGIN'` or `'COMMENT'`):
keywords are written between apostrophes, and spaces inside identifiers are not
significant, as on many compilers of the 1960s.

```algol
'BEGIN' 'INTEGER' loop count;
   'FOR' loopcount := 1 'STEP' 1 'UNTIL' 3 'DO' outinteger(1, loop count)
'END'
```

### Symbols

| Symbol | Publication form | Accepted ASCII forms |
|---|---|---|
| multiplication | `×` | `*` |
| integer division | `÷` | `div`, `%` |
| exponentiation | `↑` | `^`, `**`, `power` |
| relations | `< ≤ = ≥ > ≠` | `< <= = >= > <>`, `/=`, `!=`, `~=`, `lt le eq ge gt ne`, `less notgreater equal notless greater notequal` |
| negation | `¬` | `not`, `!`, `~` |
| and, or | `∧ ∨` | `and`, `&`, `or`, `\|` |
| implication | `⊃` | `impl`, `->`, `=>` |
| equivalence | `≡` | `equiv`, `==` |
| ten (exponent) | `⏨` | `&`, `e`, `E`, `'10'` (quote stropping) |
| subscript brackets | `[ ]` | `( )` |
| go to | `go to` | `goto` |

Strings: `` `text' `` and `‘text’` (both may be nested, and the inner string
`` `NL' `` stands for a newline), or `"text"` with the escapes `\n`, `\t`,
`\"` and `\\`.

### Comments

* `comment any text ;` after `begin` or `;`. The comment ends at the first
  semicolon, so a comment cannot contain one.
* After `end`: any text up to the next `;`, `end` or `else` (end comment).
* The *fat comma*: the parameter delimiter `) letter string : (` is equivalent
  to a comma, as in `plot(x) at position: (y)`.

---

## 2. The language

Everything of the Revised Report is supported:

* blocks with declarations, compound statements, nested scopes;
* types `integer`, `real`, `Boolean`;
* `array` declarations with bounds computed at block entry (dynamic arrays),
  any number of dimensions, negative bounds;
* `own` variables and `own` arrays, whose values survive between activations
  of their block;
* `switch` declarations and designational expressions (`go to s[i]`,
  `go to if b then L1 else s[2]`);
* labels, including the unsigned integer labels of the 1963 report (`10:` and
  `go to 10`, in definitions, `go to` and switch lists; leading zeros are not
  significant);
* conditional statements and conditional expressions (arithmetic, Boolean and
  designational);
* `for` statements with list elements of the three forms: expression,
  `step … until`, `while`, in any combination
  (`for i := 1, 5, 7 step 2 until 11, 20 do`); the controlled variable may be a
  subscripted variable or a formal parameter;
* procedures, function designators (typed procedures), recursion;
* parameters called **by name** (the default, with Jensen's device) or **by
  value** (`value` part, including value arrays, which are copied);
* formal parameters of every kind: simple variables, arrays, `procedure`,
  typed procedures, `label`, `switch` and `string`.

As required by the Modified Report, every formal parameter must be
**specified**. Integer and real values are converted on assignment and on
parameter transmission; `real` to `integer` rounds (`entier(x + 0.5)`).

```algol
real procedure sum(i, lo, hi, term); value lo, hi; integer i, lo, hi; real term;
begin real s;
   s := 0;
   for i := lo step 1 until hi do s := s + term;
   sum := s
end sum;

outreal(1, sum(i, 1, 10, 1 / i))      comment harmonic number H(10);
```

### Arithmetic

* `integer`: 32 bits (`maxint` = 2147483647); integer overflow is not detected
  (the value wraps around in two's complement).
* `real`: IEEE double precision (`epsilon` = 2.220446049250313⏨-16).
* `/` always gives a real; `÷` is the integer division truncated towards zero
  (`-17 ÷ 5 = -3`).
* `↑`: `integer ↑ integer` gives an integer (negative exponent: error),
  otherwise a real, following the rules of the report.

---

## 3. Environmental block

### Standard functions

`abs(E)`, `iabs(E)`, `sign(E)`, `sqrt(E)`, `sin(E)`, `cos(E)`, `arctan(E)`,
`ln(E)`, `exp(E)`, `entier(E)`, `length(str)`.

`sqrt` of a negative number, `ln` of a non positive number and `exp` overflow
call `fault`, as in the Modified Report.

### Input and output

The procedures take a channel number. In this implementation **every channel is
the console**: output goes to the output pane of the IDE (or to the standard
output of a compiled program), and input comes from the input console (or the
standard input).

| Procedure | Effect |
|---|---|
| `outstring(channel, str)` | writes the string |
| `outchar(channel, str, int)` | writes the character number `int` of `str` |
| `inchar(channel, str, int)` | reads one character and sets `int` to its position in `str` (0 if absent) |
| `outinteger(channel, int)` | writes the integer followed by a space |
| `ininteger(channel, int)` | reads an integer (leading spaces and newlines are skipped) |
| `outreal(channel, re)` | writes the real with 16 significant digits, followed by a space |
| `inreal(channel, re)` | reads a real (`3.5`, `-4`, `1.5e3`, `1.5⏨3`) |
| `outterminator(channel)` | writes the separator (a space) |

`outreal` uses the layout of the Modified Report: the decimal point after the
integer part when the decimal exponent is between -2 and 14 (`12.00000000000000`,
`0.3333333333333333`), no point for an exponent of 15 (`1000000000000000`), otherwise one digit before the point and an exponent
written with `e` (`1.000000000000000e301`); zero is written `0.0`. The digits
are correctly rounded (the specimen body given in the report truncates them,
which in binary arithmetic prints 12 as `11.99999999999999`).

### Control and enquiries

| Name | Meaning |
|---|---|
| `stop` | ends the program |
| `fault(str, r)` | ends the program with a runtime error showing `str` and `r` |
| `maxreal`, `minreal` | largest and smallest positive normalised reals |
| `maxint` | largest integer |
| `epsilon` | smallest `x` such that `1 + x ≠ 1` |

---

## 4. Samples

The IDE menu **File ▸ Examples ▸ Algol 60** opens the programs of
`samples/algol60`:

| Sample | Shows |
|---|---|
| `01_hello` | output procedures |
| `02_jensen` | call by name, Jensen's device |
| `03_man_or_boy` | Knuth's man or boy test in quote stropping (prints -67) |
| `04_sieve` | dynamic Boolean array |
| `05_eight_queens` | backtracking with Boolean arrays and negative bounds |
| `06_hanoi` | recursion with value parameters |
| `07_own_random` | `own` variables: a random generator |
| `08_gauss` | arrays as parameters, Gaussian elimination |
| `09_input` | `ininteger` and `inreal` |
| `10_switch_scanner` | a state machine with a `switch` and `inchar` |

---

## 5. Differences and limits

* Every channel is the console; there are no files.
* Identifiers may not contain spaces in the reserved-word representation, and
  the keywords (including `to`, `step`, `until`, `value`…) cannot be used as
  identifiers there. Use quote stropping to write programs whose identifiers
  collide with keywords.
* Integer labels are recognised where a label is defined, after `go to` and in
  switch lists; an integer label cannot be passed as an actual parameter
  (pass a switch or use an identifier label).
* Side effects of function designators are evaluated from left to right,
  which the report leaves undefined.
