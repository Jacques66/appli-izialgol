# Simula 67 & Algol 68 Studio

A complete Windows development environment for five historic languages:

* **Simula 67** (Dahl and Nygaard, 1967) — the first object-oriented language: classes, subclasses,
  virtual procedures, coroutines, discrete-event simulation (`Simulation`, `SimSet`);
* **Algol 68** (van Wijngaarden et al., 1968) — modes, user-defined operators, orthogonality,
  structures and unions;
* **Algol 60** (Revised Report 1963, Modified Report 1976) — block structure, call by name,
  `own` variables, switches; reserved-word and quote-stropping representations;
* **Simula I** (Dahl and Nygaard, 1965) — Algol 60 extended for discrete-event simulation:
  activities, processes, elements and sets;
* **Pascal** (Wirth, 1970) — the language of the Revised Report (Jensen and Wirth, *Pascal User
  Manual and Report*) as implemented by Pascal 6000 on the CDC 6600: records with variants, sets,
  pointers, files, procedural parameters, non-local goto.

The project contains:

| Component | File | Role |
|---|---|---|
| IDE | `Simula67Studio.exe` | editor, build, console, debugger, help |
| Simula 67 compiler | `simc.exe` | command-line compilation, standalone executables |
| Algol 68 compiler | `a68c.exe` | the same for Algol 68 |
| Algol 60 compiler | `a60c.exe` | the same for Algol 60 |
| Simula I compiler | `s1c.exe` | the same for Simula I |
| Pascal compiler | `pasc.exe` | the same for Pascal |
| Launcher | `Runner.exe` | base of the generated standalone executables (not meant to be run on its own) |

The compilers produce a program that is interpreted by the language's runtime; a "standalone
executable" is a copy of `Runner.exe` with the compiled program appended to it.

## Requirements

* Windows 10 or 11 (x64).
* **.NET 8 Desktop Runtime** to use the IDE and the tools
  (<https://dotnet.microsoft.com/download/dotnet/8.0>, ".NET Desktop Runtime 8").
  Generated executables only need the **.NET 8 Runtime**.
* To build: **.NET 8 SDK**.

## Building

```powershell
# Full distribution in dist\ (IDE, Runner, simc, a68c, a60c, s1c, pasc, samples, documentation)
powershell -ExecutionPolicy Bypass -File .\build.ps1

# With the tests and the IDE start-up test
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

Contents of `dist\`:

```
dist\Simula67Studio.exe   dist\Runner.exe   dist\simc.exe   dist\a68c.exe   dist\a60c.exe   dist\s1c.exe   dist\pasc.exe
dist\samples\simula\*.sim dist\samples\algol68\...\*.a68 dist\samples\algol60\*.a60 dist\samples\simula1\*.s1 dist\samples\pascal\*.pas
dist\docs\simula.md       dist\docs\algol68.md       dist\docs\algol60.md   dist\docs\simula1.md   dist\docs\pascal.md   dist\README.md
```

For development, `dotnet build Simula67Studio.sln` is enough: every build of `src\Runner` also
publishes `src\Runner\bin\<Configuration>\single\Runner.exe` (single file), which the IDE and the
compilers find automatically by walking up the folder tree. The `SIMULA67_RUNNER` environment
variable forces another `Runner.exe`.

## Using the IDE

Start `Simula67Studio.exe` (optionally with files or a folder as arguments).

### Editor

* Multiple tabs; a dot `•` marks a modified file; confirmation before closing an unsaved file;
  files are reloaded when another program changes them.
* **Syntax highlighting**:
  * Simula 67 — case-insensitive keywords; comments `comment … ;`, `! … ;` and the text after
    `end` (up to `;`, `end`, `else`, `when`, `otherwise`); `%` directives; strings `"…"`;
    characters `'c'`.
  * Algol 68 — UPPER stropping (upper-case words in bold) and QUOTE stropping (`'BEGIN'`);
    comments `CO … CO`, `COMMENT … COMMENT`, `# … #`; pragmats `PR … PR`; strings.
  * Algol 60 and Simula I — reserved words or quote stropping (`'BEGIN'`); comments
    `comment … ;` and end comments; strings `"…"`, `` `…' `` and `‘…’`; the publication
    symbols `× ÷ ↑ ≤ ≥ ≠ ¬ ∧ ∨ ⊃ ≡ ⏨`.
  * Pascal — case-insensitive reserved words; comments `{ … }` and `(* … *)`; strings `'…'`
    (doubled quote); the alternative symbols `(. .)` and `@`; folding of begin/end, record/end,
    case/end and repeat/until; outline of the program, procedures, functions and types.
* Line numbers, current-line highlight, **folding** (begin/end, procedures and classes;
  BEGIN/END, IF/FI, DO/OD, CASE/ESAC, multi-line parentheses), **matching** of parentheses and
  blocks, **automatic indentation**, **completion** (Ctrl+Space: keywords, standard procedures,
  identifiers of the file).
* Find / replace (plain text, whole word, match case, regular expression), go to line,
  comment / uncomment, re-indent, zoom (Ctrl+mouse wheel).
* The language is chosen from the extension (`.sim`, `.a68`, `.a60`/`.alg`, `.s1`) and can be changed (menu
  *Build › File language* or a click on the status bar).
* Resizable panels: folder **explorer** (left), file **outline** (procedures, classes,
  PROC/OP/MODE — click to go there, filter) (right), and at the bottom **Output**, **Errors**,
  **Console**, **Call stack**, **Variables**.
* Drag and drop of files or folders; recent files and folders; **Examples** menu (`samples`
  folder); **light** and **dark** themes.
* The session (tabs, caret position, breakpoints, layout, options) is restored at start-up.
  Settings: `%APPDATA%\Simula67Studio\settings.json` (theme `light` or `dark`; technical log:
  `ide.log` in the same folder).

### Build and run

* **Build** (F7): diagnostics in the Output panel and the error list (double-click → line and
  column), squiggly underline in the editor (tooltip on hover). A background check underlines
  errors while you type (can be turned off).
* **Run** (Ctrl+F5): the program runs in the **integrated console** — output as it is produced,
  line input (Enter), end of file (Ctrl+Z or button), history (↑/↓), **Stop** (Shift+F5).
* **Run in external Windows console** option (toolbar button or Debug menu): the executable
  is generated in a temporary folder and started in a real console.
* **Generate a standalone .exe** (Ctrl+Shift+E): a single Windows executable that can be copied
  and run on its own.

### Debugging

* Breakpoints by clicking in the margin or with F9 (red dots, remembered per file).
* **F5** starts debugging / continues; **F11** step into; **F10** step over; **Shift+F11** step
  out; **Ctrl+Alt+Pause** pause; **Shift+F5** stop.
* At each stop: arrow and yellow highlight of the current line, **call stack** (double-click a
  caller to show its line and its variables), **variables** as a tree (objects and arrays expand
  on demand).
* Break on runtime error, with the message, the call stack and the variables at the time of the
  error.

### Keyboard shortcuts

| Shortcut | Action | Shortcut | Action |
|---|---|---|---|
| Ctrl+N | New Simula 67 program | Ctrl+Shift+N | New Algol 68 program |
| Ctrl+O | Open | Ctrl+S / Ctrl+Shift+S | Save / save all |
| Ctrl+W | Close tab | Ctrl+F / Ctrl+H | Find / replace |
| F3 / Shift+F3 | Find next / previous | Ctrl+G | Go to line |
| Ctrl+Space | Complete | Ctrl+K / Ctrl+Shift+K | Comment / uncomment |
| Ctrl+I | Re-indent | Ctrl+M, Ctrl+O / Ctrl+L | Collapse / expand all |
| Ctrl++ / Ctrl+- / Ctrl+0 | Zoom | F1 | Help for the current language |
| F7 | Build | Ctrl+Shift+E | Generate .exe |
| F5 | Debug / continue | Ctrl+F5 | Run |
| Shift+F5 | Stop | Ctrl+Shift+F5 | Restart |
| F9 | Breakpoint | Ctrl+Shift+F9 | Delete all breakpoints |
| F10 / F11 / Shift+F11 | Step over / into / out | Ctrl+Alt+O/E/C/P/V | Panels |

### Help

The **Help** menu shows `docs/simula.md`, `docs/algol68.md`, `docs/algol60.md`,
`docs/simula1.md` and `docs/pascal.md` (looked up next to the executable, then in the parent folders), the list of
keyboard shortcuts and a guide to the environment.

## Command line

```
simc file.sim [-o output.exe] [--run] [--no-debug]
a68c file.a68 [-o output.exe] [--run] [--no-debug]
a60c file.a60 [-o output.exe] [--run] [--no-debug]
s1c  file.s1  [-o output.exe] [--run] [--no-debug]
pasc file.pas [-o output.exe] [--run] [--no-debug]
```

| Option | Effect |
|---|---|
| (none) | compiles and prints the diagnostics |
| `-o output.exe` | produces a standalone executable (`Runner.exe` must be next to the tool) |
| `--run`, `-r` | runs the program in the console after compilation (diagnostics on stderr) |
| `--no-debug` | without debugging information |
| `--version`, `--help` | version, help |

Diagnostics have the form `file(line,column): error: message` (or `warning:`). Exit codes:
0 success, 1 compilation error, 2 usage error, 3 internal error, 4 the executable could not be
generated; with `--run`, the exit code of the program.

A generated executable prints a runtime error on standard error
(`file(l,c): runtime error: message` followed by the call stack) and returns a non-zero exit code.
Ctrl+C interrupts it.

## Tests

```powershell
dotnet run --project tests\SimulaTests           # Simula 67, Algol 60 and Simula I samples and cases
dotnet run --project tests\Algol68Tests          # Algol 68 compiler
dotnet run --project tests\PascalTests           # Pascal compiler: samples, cases, debugger
dotnet run --project tests\IdeTests              # tooling, debugger, standalone exe, CLI, editor
dotnet run --project tests\IdeTests -- --samples # + compile/run the samples
Simula67Studio.exe --smoke-test --screenshot capture.png   # start-up + screenshot + shutdown
Simula67Studio.exe --self-test --self-test-dir folder      # full IDE scenario
```

`tests\IdeTests` uses a small test language ("echo") to check the IDE, the debugger and the
standalone executables independently of the real compilers. The IDE self-test (`--self-test`)
drives the real window: compilation, errors, console, debugger, exe, editor, theme; it then
exercises the real compilers (Simula 67, Algol 68, Algol 60, Simula I, Pascal). The result is written to `self-test.log`
(exit code 0 when everything passes) together with screenshots.

## Source layout

```
src\Common      shared contract (ILanguageService, IDebugger, ExePackage…)
src\Simula      Simula 67 compiler and runtime (also the engine of Algol 60 and Simula I)
src\Algol60     Algol 60: lexer for its representations, environmental block (Modified Report)
src\SimulaI     Simula I: vocabulary, runtime (SIMULA block, processes, sets)
src\Algol68     Algol 68 compiler and runtime
src\Pascal      Pascal compiler (lexer, one-pass parser and type checker), interpreter, runtime, debugger
src\Runner      launcher of the standalone exes; Shared\ = shared code (language registry,
                console execution, Runner.exe lookup, "echo" test language)
src\SimC        simc (CliMain.cs is shared with a68c, a60c, s1c and pasc)
src\A68C        a68c
src\A60C        a60c
src\S1C         s1c
src\PasC        pasc
src\Ide         WPF IDE (AvalonEdit, Markdig)
tests\          tests of the compilers and of the tooling
samples\        samples; docs\ language documentation
```
