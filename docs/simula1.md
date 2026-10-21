# Simula I — reference guide

**Simula I** is the first SIMULA, designed by Ole-Johan Dahl and Kristen Nygaard
at the Norwegian Computing Center and implemented on the UNIVAC 1107 in 1965.
It is Algol 60 extended with a *SIMULA block* that describes a discrete event
system as a set of **processes** acting in quasi-parallel on a common time axis.
Simula 67 grew out of it (classes, prefixes and virtual procedures came later).

This implementation compiles Simula I with the engine of the IDE: the whole of
Algol 60 is available (see [algol60.md](algol60.md): representations, symbols,
call by name, `own`, switches, environmental block), plus the declarations and
statements described below.

Files: `.s1`.

> **Sources.** The 1965 language manual is not freely available. The syntax used
> here is reconstructed from the published descriptions by its authors:
> O.-J. Dahl and K. Nygaard, *SIMULA — an ALGOL-based simulation language*,
> Communications of the ACM 9(9), 1966, and K. Nygaard and O.-J. Dahl,
> *The development of the SIMULA languages*, HOPL, 1978. Where those papers
> leave a detail open, the choice made is documented in section 6.

---

## 1. Program structure

A simulation model is a block prefixed by `SIMULA`. Activity declarations,
element and set declarations may appear among its declarations; the statements
of the block are the **main program**, which is itself a process.

```simula1
SIMULA begin
   activity car(number); integer number;
   begin
      outstring(1, "car "); outinteger(1, number); outstring(1, "starts\n");
      hold(10);
      outstring(1, "car "); outinteger(1, number); outstring(1, "stops\n")
   end of car;

   activate new car(1);
   activate new car(2) delay 5;
   hold(100)
end of simulation model
```

When the main program ends, the simulation ends, whatever the state of the
other processes.

---

## 2. Activities and processes

```
activity name(parameters); specifications;
   statement
```

An **activity declaration** describes a class of processes: its parameters are
called by value, its body may declare local quantities, local procedures, and
contains the actions of the process. `new name(arguments)` creates a process,
which stays **passive** (not scheduled) until it is activated.

A process can be in four states:

| State | Meaning |
|---|---|
| active | it is running (it is `current`) |
| suspended | it has an event notice in the sequencing set |
| passive | it has no event notice |
| terminated | its body has reached its end |

The **sequencing set** (SQS) holds the event notices ordered by time; the first
one is the active process. `time` is the time of the current event.

### Sequencing statements

| Statement | Effect |
|---|---|
| `activate X` | schedules the passive process `X` now, before the current process (it runs at once) |
| `activate X at t` | schedules `X` at time `t` (after the processes already scheduled at `t`) |
| `activate X at t prior` | the same, but before the processes scheduled at `t` |
| `activate X delay d` | at time `time + d` (`prior` is also allowed) |
| `activate X before Y` / `after Y` | immediately before or after the event notice of `Y` |
| `reactivate X …` | the same forms, but `X` may already be scheduled: it is moved |
| `hold(t)` | the current process waits `t` time units |
| `passivate` | the current process becomes passive |
| `wait(S)` | the current process joins the set `S` and becomes passive |
| `cancel(X)` | removes the event notice of `X` (`X` becomes passive) |

`activate` has no effect on a process that is already scheduled or terminated.

### Enquiries

| Function | Value |
|---|---|
| `time` | current simulated time |
| `current` | an element referring to the active process |
| `idle(X)` | true if `X` has no event notice |
| `terminated(X)` | true if the body of `X` is finished |
| `evtime(X)` | the event time of `X` (error if idle) |
| `nextev(X)` | an element for the process after `X` in the SQS, `none` if none |

---

## 3. Elements and sets

In Simula I, processes are reached through **elements**, and they are grouped
in **sets**:

```simula1
element E, F;          comment element pointers, initially none;
set queue, road;       comment empty sets;
element array A[1:10];
```

* `E := new car(1)` makes `E` refer to the new process; elements are assigned
  with `:=` and compared with `=` and `≠`: two elements are equal when they
  refer to the **same process**.
* A process may belong to **several sets at the same time**: each `include`
  creates a new member element that refers to the same process. Removing it
  from one set does not affect the other memberships.

| Procedure | Effect |
|---|---|
| `include(X, S)` | appends a new element for the process of `X` at the end of `S` |
| `remove(X)` | removes the member element `X` from its set |
| `head(S)` | the head of `S` (to walk through it with `suc`) |
| `first(S)`, `last(S)` | the first or last member, `none` if `S` is empty |
| `suc(X)`, `pred(X)` | the next or previous member, `none` at the end |
| `exist(X)` | true if `X` is not `none` (and is not the head) |
| `empty(S)` | true if `S` has no member |
| `member(X, S)` | true if the process of `X` is a member of `S` |
| `cardinal(S)` | number of members (extension) |
| `clear(S)` | removes every member (extension) |

The classic loop over a set:

```simula1
E := head(road);
for E := suc(E) while exist(E) do
   inspect E when car do
      if speed > limit then newspeed(limit)
```

### Connection: inspect

The local quantities of a process (its parameters, variables and procedures)
are reached only through **connection**:

```
inspect X when A do S1 when B do S2 … otherwise S3
inspect X do S
```

`S1` is executed with the quantities of the process `X` visible when it belongs
to the activity `A`, and so on; `otherwise` is chosen when `X` is `none` or
belongs to none of the listed activities.

---

## 4. Random drawing

The integer variable `U` is the seed; it is called by name and updated at each
drawing, so each stream of random numbers has its own variable.

| Function | Distribution |
|---|---|
| `draw(a, U)` | Boolean, true with probability `a` |
| `randint(a, b, U)` | integer uniformly distributed in `[a, b]` |
| `uniform(a, b, U)` | real uniformly distributed in `[a, b)` |
| `normal(a, b, U)` | normal, mean `a`, standard deviation `b` |
| `negexp(a, U)` | negative exponential with rate `a` (mean `1/a`) |
| `poisson(a, U)` | Poisson with mean `a` |
| `discrete(A, U)` | index of a cumulative distribution held in the array `A` |
| `histd(A, U)` | index drawn with weights proportional to the elements of `A` |

---

## 5. Samples

The IDE menu **File ▸ Examples ▸ Simula I** opens the programs of
`samples/simula1`:

| Sample | Shows |
|---|---|
| `01_car_and_police` | the car and police model: a set, `inspect … when`, local procedures of a process |
| `02_post_office` | a queue: `wait`, `passivate`, random arrivals and service times |
| `03_two_sets` | a process in several sets at the same time |
| `04_epidemic` | a population model with `draw` and `randint`, a reporter process |

---

## 6. Implementation choices

* Elements are assigned with `:=` and compared with `=` and `≠` (in Algol 60
  notation `==` is the equivalence of Boolean values, not a reference comparison).
* A set declaration may name the activities of its members
  (`set queue (customer)`); the qualification is accepted and ignored.
* There is no `text` type: string parameters are specified `string`, as in
  Algol 60, and strings are written with `outstring`.
* `cardinal(S)` and `clear(S)` are extensions; `exist(X)` follows the CACM
  description.
* Simulated time is a `real`; event notices with the same time are served in
  the order of their scheduling (unless `prior` is given).
