# A log of the definitions `CssView` applies

Issue 27 asks for a way to receive the `StyleDefinition` of each class `Ranvier.Css.View` puts on an
element, so a native host can fill its stylesheet at run time without the extractor and without a
copy of `StylesBuilder`. This note compares the ways to do it and records why `AppliedStyles` has the
shape it has.

Status: implemented as `AppliedStyles.count` and `AppliedStyles.after` in `src/Ranvier.Css.View`.

## What the candidates were measured with

A scratch console program held one implementation of each candidate and ran the timings and tests
below. It is not in the repository. All figures are **measured here**: Windows 11, .NET 10.0.15,
Release, tiered compilation off, 24 logical processors, four runs for the store rows and two for the
rest, other builds running on the machine. Two earlier runs were discarded: with tiered compilation on
they timed code the JIT had not optimised yet.

A definition in these runs is a style of one declaration. "One application today" is `Style.create`
on four static declarations (0.5 to 0.8 µs) plus `CssView.parts` (0.11 to 0.17 µs); a real utility
costs more, so the shares below are upper bounds.

## Where a class is recorded

`CssView.className` and `CssView.partsWithInline` are the only functions in `src` that put a generated
class on an element. `parts`, `apply`, `applyMany`, `applyWithInline` and `StylesBuilder.Run` all reach
`partsWithInline`. Recording there covers a library's own views and direct `CssView` calls, which a
builder defined by the app cannot see.

Recording in `Style.create` was the alternative. It would also record a style that is created and
never applied, and it would run inside `Ranvier.Css.Compile`, which calls `Style.create` at build
time.

## How a host receives the definitions

| Candidate | Result |
|---|---|
| A log read by position | Passes every test below. The host keeps one number. |
| A subscription with replay, the callback under the lock | Deadlock when the callback hands the rule to the host thread and waits. Wrong order when a callback applies a style. |
| A subscription with replay, the callback after the lock | A subscriber arriving while four threads record received classes out of order in 30 of 30 trials. Wrong order when a callback applies a style. |
| A subscription with replay, deliveries through one queue | Passes the tests. The callback runs on whichever thread recorded, `subscribe` can return before its replay arrives, and a callback that throws needs handling so deliveries continue. |
| A subscription without replay | Loses every class applied before the subscription: a second window, or a sheet filled again after hot reload, receives nothing. |
| A scoped collection around the code that builds a view | In an executable a module-level value is initialised on first access (measured: after `main` is entered), so only the first scope that reaches a view module sees its classes. |
| A signal of the reactive core | Not prototyped. From the core's source: a signal belongs to one graph, and a guarded graph raises on entry from another thread. The log is written at module initialisation, from any thread, possibly before a graph exists. |

The tests behind the subscription rows:

| Test | Under the lock | After the lock | Queued |
|---|---|---|---|
| A subscriber arrives while four threads record 20,000 classes | pass | fail, 30 of 30 | pass |
| The callback hands the rule to the host thread and waits, while that thread applies a style | fail, deadlock | pass | pass |
| A callback applies another style; a second subscriber checks the order | fail | fail | pass |

For the log read by position: eight threads recording overlapping and distinct classes leave each
class in the log once, and a reader taking batches by position during that run receives the log
exactly.

A subscription also leaves the batching to the app. Issue 27 measured about 3 ms for one change to an
active stylesheet, so a host inserts once a frame; with a callback it must keep its own thread-safe
buffer to do that.

## How the log is stored

| | Lock and hash set | Concurrent dictionary, lock for a new class | Immutable snapshot, compare-and-swap |
|---|---:|---:|---:|
| Record a class already recorded, one thread | 18 to 25 ns | 10 to 13 ns | 26 to 32 ns |
| Record a class for the first time | 32 ns | 104 ns | 390 to 396 ns |
| Record an already recorded class on 8 threads at once | 441 to 604 ns | 20 ns | 57 ns |
| Memory kept per class, beside the definition | 50 B | 86 B | 74 B |
| `count`, nothing new | 9 ns | 9 ns | 1 ns |
| `after`, nothing new | 9 to 10 ns | 9 to 10 ns | 2 ns |
| `after`, 30 new of 300 | 0.13 to 0.17 µs | 0.12 to 0.17 µs | 0.20 to 0.23 µs |
| `after`, all 300 | 1.3 to 1.4 µs | 1.4 µs | 2.0 to 2.3 µs |

`AppliedStyles` uses the concurrent dictionary: it is the fastest on the path every application after
the first takes, and it stays flat across threads. Recording a known class adds 10 to 13 ns to an
application that costs at least 0.6 µs.

Measured on the implemented library, the cost could not be told from noise: `CssView.parts` took
147 and 165 ns before the change and 113 and 123 ns after it, while `Style.create`, which the change
does not touch, moved from 662 and 799 ns to 511 and 519 ns across the same runs.

Recording runs where parts are built. A render, which only runs the readers those parts hold, does
not reach it (by inspection of `partsWithInline`).

## What `AppliedStyles` guarantees

- A class is in the log once, under its name. The name is a hash of the rules.
- The order is the order of first application; styles given to one call are recorded in list order.
  Between two classes on one element, stylesheet order decides, as `docs/content/view/css.md` already
  says of class tokens.
- The log keeps each definition for the life of the process.
- `count` and `after` can be called from any thread.
- Under Fable the module and the recording are compiled out.

## Not tested

- A browser build beyond compiling it, a WebCore host, and server rendering under load.
- The candidates without a prototype of their own: recording in `Style.create`, a subscription without
  replay, and a scoped collection are judged from the code and from the initialisation test.
