# Reader values: what one read costs, and a design to cut it

Status: proposal, 2026-11-19, branch `perf/reader-value-lowering`. Nothing under `src/` has changed. The only
code on the branch is the benchmark this note reports from. A change to a published package waits for the
owner's agreement.

Every figure below is **measured here** unless it is marked **from source** or **general knowledge**. "Measured
here" means this machine (Windows 11, .NET 10.0.15, Release build), while other sessions were building on it:
processor use was about 50% during the last runs, so read differences under 10% as noise.

## The finding in short

1. One render of an instance with six reader values costs about 12 µs and allocates 34 KB, whether a style value
   changed or not. The same instance with each style attribute written as one text costs 0.38 µs and 0.8 KB.
2. A cache of each reader's last result is not the smallest fix. About three quarters of a read is over before
   `Styles.fs` receives the reader's declaration, so a cache placed there removes a quarter at most. In every
   cache design the reader's own `Css.px` call (28% of a read) still runs, because its result is the key.
3. The repeated work is inside one read. A read of `Css.top (fun row -> Css.px row.Top.Value)` turns one number
   into text 9 times and keeps 1 text. It also runs 2 name regexes and 2 regex replacements on text that needs
   none of them.
4. **Proposed:** do each check once per read (changes 1 to 3 below). No cache, nothing kept per element or per
   instance, no public signature added or changed. A throwaway copy with these three changes renders the
   benchmark instance in 4.7 µs against 11.0 µs in the same session, with the same style text.
5. **Not proposed now:** a result cache. What each variant would keep, where, and what it would save is in
   "A cache of the last result".
6. **For a later decision:** a style slot in an effect of its own. It is the one design in which an unchanged
   reader value costs nothing, and `Ranvier.View` already has the mechanism.

## What a read does today

From source, at `851af77`.

- `CssView.partsWithInline` (`src/Ranvier.Css.View/Bindings.fs:22`) binds an element's `style` attribute to a
  reader. The reader calls `Style.variableDeclarations instance style` for each style on the element, then
  `Declaration.renderMany`.
- `Style.variableDeclarations` (`src/Ranvier.Css/Styles.fs:229`) evaluates every `whenActive` predicate, then
  runs every active reader.
- A reader made by a generated property, for example `Css.top (read)`
  (`src/Ranvier.Css.Bindings/Generated/Css.fs:89498`), does three things:
  1. It calls the user's function, which builds a typed value. `Css.px v` (`Css.fs:61124`) builds a
     `Numeric.Length` from three separate constructions of the same number: `Generated.number`,
     `CalculationRuntime.literal` and `GrammarRuntime.number`.
  2. It admits the value to the property's grammar node (`GrammarRuntime.admit`) and creates the authored
     declaration (`RuntimeApi.declaration "top" text`), which checks the name with a regex.
  3. Back in `Styles.fs:143-160`, it compares the name, runs `Declaration.isCssWideWith` (two `Regex.Replace`
     calls), reads `isImportant`, and creates the lowered declaration with `Declaration.create variable value`,
     which checks the variable's name with the same regex.
- `GrammarRuntime.admit` searches from the property's node to the value's node. On the way back, each node on
  the path calls `ranged` (`src/Ranvier.Css/GrammarRuntime.fs:152`). `ranged` calls `Generated.number`, which
  turns the number into text, to build a value from which `Generated.range` reads the number and its unit.
  The text is discarded.
- Each node visit looks the node up in an F# `Map<int, Node>` of 13,541 nodes (the count field of the encoded
  table in `Generated/Css.fs`).
- `Style.classNames` and `Style.variableDeclarations` each evaluate the `whenActive` predicates, so a predicate
  runs twice in a render of an element that has one. The benchmark has one predicate, a signal read; its cost
  was not measured apart from the rest.
- `Ranvier.View.Dom` starts one effect for a template part. The effect reads every render slot, then commits
  each value (`startRender` and `commit` in `Ranvier.View/src/Ranvier.View.Dom/TemplateDom.fs`, the workspace
  checkout at 0.3.0). A value equal to the slot's previous value is not written to the DOM. The read that
  produced it has run by then.

## The benchmark

`benchmarks/Ranvier.Css.Benchmarks`, a console project with a stopwatch harness and no new package.

```
dotnet run -c Release --project benchmarks/Ranvier.Css.Benchmarks
```

- **The instance.** Three styled elements with six reader values (`top`, `left`, `width`; `height`, `opacity`;
  `left`), one `whenActive` condition and one text slot. Every reader reads one signal.
- **The mount.** No DOM host. One core `Effect` reads every slot of the component's template and keeps a value
  that differs from the slot's previous value under `slot.Same`. This is the loop of `startRender` without the
  DOM write.
- **One operation** is one signal write and the effects it runs. A row reports the median and the fastest of 15
  samples, and the bytes allocated on the thread per operation.
- **Three variants.** `reader values` is the instance above. `style text` has the same elements with each
  `style` attribute written as one text by its reader, which is what `Ranvier.Plume.View` does now.
  `reader values, style slots isolated` gives each `style` slot an effect of its own.
- **Not measured:** a DOM write, a browser, and the Fable build.

### Renders

One run of the committed benchmark. Times are medians.

| Instance | Signal written | µs | Bytes | Readers run |
|---|---|---:|---:|---:|
| reader values | text (no style value changes) | 11.8 | 33,969 | 6 |
| reader values | `top` (one style value changes) | 12.3 | 33,969 | 6 |
| reader values | `top`, a new number each time | 13.0 | 34,168 | 6 |
| reader values | `selected` (the condition changes) | 12.3 | 33,781 | 6 |
| style text | text | 0.38 | 824 | |
| style text | `top` | 0.38 | 824 | |
| style text | `top`, a new number each time | 0.38 | 837 | |
| style text | `selected` | 0.37 | 812 | |
| reader values, style slots isolated | text | 0.15 | 640 | 0 |
| reader values, style slots isolated | `top` | 6.0 | 16,696 | 3 |
| reader values, style slots isolated | `top`, a new number each time | 6.3 | 16,895 | 3 |
| reader values, style slots isolated | `selected` | 4.2 | 11,324 | 2 |

The first row was 11.6 µs in a second run, and 12.2, 12.9 and 12.4 µs in three earlier runs with samples ten
times longer.

Bytes allocated to create one row's signals, start its effects and render it once: 35,977 for reader values,
2,832 for style text, 37,257 with the style slots isolated. Three more effects cost 1,280 bytes, 427 each.

### Parts of one read

One operation is one call. Each time is the middle of three runs with samples four times longer than the
committed default; the committed default gave the same order with more scatter.

| Call | ns | Bytes |
|---|---:|---:|
| `Generated.number value "px"` (one number to text) | 82 | 312 |
| `Css.px value` (the reader's own work) | 481 | 2,048 |
| `Css.top length` (admission and declaration of a built value) | 778 | 2,768 |
| `Css.top (Css.px value)` | 1,383 | 4,816 |
| `Style.variableDeclarations`, one `Css.top` reader (one read) | 1,704 | 5,032 |
| `Css.opacity (Css.number value)` | 1,297 | 4,136 |
| `Css.position CssValue.Position.absolute` (a keyword) | 203 | 728 |
| `Declaration.isCssWide "120px"` | 128 | 280 |
| `Declaration.create variable "120px"` | 64 | 40 |
| `Declaration.render`, one declaration | 18 | 152 |
| `Declaration.renderMany`, six declarations | 193 | 1,600 |

One read of 1,704 ns divides into:

| Part of the read | ns | Share |
|---|---:|---:|
| The user's function builds the typed value (`Css.px`) | 481 | 28% |
| The generated reader admits it and creates the authored declaration | 778 | 46% |
| `Styles.fs` checks it and creates the lowered declaration, and the list is built | 445 | 26% |

### How often each check runs in one read

Counted in a throwaway copy of the branch with a counter in each function. The copy is not on any branch.

| Call | Number to text | Table lookups | Admission visits | Range checks | Name regexes | CSS-wide checks |
|---|---:|---:|---:|---:|---:|---:|
| `Css.px value` | 4 | 3 | 1 | 1 | 0 | 0 |
| `Css.top length` | 5 | 6 | 6 | 5 | 1 | 0 |
| One read (`Css.top` reader) | 9 | 9 | 7 | 6 | 2 | 1 |
| One render of the instance | 57 | 56 | 44 | 39 | 12 | 6 |

The instance has six numbers. 57 conversions to text produce the 6 texts it needs. Each CSS-wide check is two
`Regex.Replace` calls.

## Proposed design: each check once per read

### Change 1: the range check reads the number

`GrammarRuntime.ranged` returns the value at once when the node has no lower and no upper bound. With a bound,
it passes the number and its unit to the comparison `Generated.range` performs, through an internal function
that `Generated.range` also calls.

The result is the same for every input:

- `ValueData` is a private union, constructed only in `GrammarRuntime`. A numeric one is constructed after
  `Generated.number` accepted the number, so the finiteness check `ranged` repeats today cannot fail.
- `Generated.range` reads the number and the unit of its input, and nothing else.
- A value outside a bound raises the same exception from the same comparison.

### Change 2: declarations are checked without a regex where the regex cannot differ

- `Declaration.create` scans the characters of the name. A name the scan accepts is valid under the regex. A
  name the scan rejects goes to the regex as it does today, so the set of accepted names is unchanged. That
  includes a name ending in a line feed, which `$` accepts on .NET.
- `Declaration.isCssWideWith` skips its two `Regex.Replace` calls for text that contains neither `\` nor `/*`.
  Both calls return such text unchanged. The trim, the lower-casing and the comparison with the keywords stay.
- The lowered declaration is built by an internal function in `Values.fs` from the slot's variable name and the
  reader's value text. `Style.create` validates the variable name once, when it makes the slot.

### Change 3: a grammar node is found by index

`Table.create` fills an array indexed by node id beside the map, and `lookup` reads the array. `Table.create`
uses the array when the ids are dense and keeps the map lookup otherwise, since it is public and accepts any
nonnegative ids.

### What the design keeps, and where

Nothing between reads. No state per element, per instance or per style. No shared mutable state, so rendering
on several server threads is unaffected.

One array per grammar table for the life of the process: 13,541 references, about 108 KB on a 64-bit runtime
(general knowledge: 8 bytes a reference).

### Public signatures

None added, changed or removed. Two internal functions are added in `Values.fs`: the range comparison on a
number and unit, and the constructor of a lowered declaration. `Table` is a private union and gains a field.

All three changes are in `src/Ranvier.Css`. `Ranvier.Css.Bindings`, `Ranvier.Css.View` and the generated
`Css.fs` are untouched.

### Figures from a throwaway copy

The copy has each change behind a switch, so one build ran every row in one session. Times are medians of the
committed benchmark's rows. The copy is not on any branch; it exists to choose between designs, and the
implementation would be written again with tests first.

| Changes switched on | Render, text written (µs) | Bytes per render | One read, fastest sample (ns) |
|---|---:|---:|---:|
| none | 11.0 | 34,041 | 1,636 |
| 1 | 7.2 | 22,528 | 1,092 |
| 1 and 3 | 6.3 | 22,520 | 922 |
| 1 and 2 | 5.5 | 22,144 | 778 |
| **1, 2 and 3 (the proposal)** | **4.7** | **22,136** | **602** |
| 1, 2, 3 and 4 | 4.4 | 21,880 | 580 |
| 1, 2, 3, 4 and 5 | 3.9 | 19,720 | 600 |

- With change 1 alone, one render converts a number to text 18 times, down from 57.
- The slot values printed by the benchmark are identical in every row.
- In a separate pair of runs with samples ten times longer, the branch rendered in 12.9 µs and 34.0 KB, and
  the copy with all five changes in 4.2 µs and 19.7 KB.
- With the proposal and the style slots isolated, the `top` row is 2.5 µs against 7.2 µs in the same session.
- `tests/Ranvier.Css.Spec.Tests` ran on the copy with all five changes on: 196 of 198 passed. The two failures
  are the spec corpus tests, which read `tools/Ranvier.Css.Generate/node_modules/@webref/css` and the fetched
  spec files; that worktree has neither. The Compile tests and the browser tests were not run on the copy.

Changes 4 and 5 are left out of the proposal:

- **Change 4**, `Declaration.renderMany` in one pass over a `StringBuilder`: 0.3 µs a render here. It is exact
  and small, and can ride along if the owner wants it.
- **Change 5**, `NumberText.format` returns the round-trip text when that text has no exponent: 0.5 µs a
  render here. A class name is a hash of the rendered rules, so one differing number text would rename a class
  and separate the runtime from a stylesheet extracted by another version. It needs a test against the current
  function over many numbers, on .NET and under Fable, before it is considered.

### Tests written before the change

1. Range: a number below a lower bound and above an upper bound at the value's node and at an ancestor node
   raises the message it raises today; a number inside the bounds is admitted; a value with a unit that the
   comparison cannot convert raises as today.
2. Names: a table of accepted and rejected names, including `--x`, `-x`, `_x`, `-`, `--`, `1x`, a name with a
   space and a name ending in a line feed, with the result recorded from the current build.
3. CSS-wide keywords: the existing cases with comments and escapes, and plain text in upper and lower case
   with surrounding spaces.
4. Lookup: an unknown id raises as today, and a table with sparse ids resolves every node.
5. Identity: the class names and rendered variables of the example styles are equal before and after.
6. The benchmark, run again on the same machine, for the after figures.

The gate is `dotnet fsi build.fsx -- test`, unfiltered, and the browser group for the Fable build.

## A cache of the last result

The task that led here expected a cache. These are the three places one could go.

### (a) Per slot of a `Styled`: the last declaration and its lowered form

- **Keeps:** one reference per dynamic slot to an immutable pair of declarations. A pair is two declarations
  and their two texts.
- **Where:** in the closure `Style.create` makes for the slot, shared by every instance and every thread that
  uses the style. A write replaces the reference, so no lock is needed.
- **Hit:** the reader returns a declaration equal to the previous one from any instance. Rows with different
  values that render in turn miss each other.
- **Saves:** the CSS-wide check and the second name check, 192 ns of a 1,704 ns read today (11%), and about
  40 ns after change 2.
- **Signature:** none.

Change 2 removes the same work with no state.

### (b) Per bound element and instance: the last declarations and the rendered text

- **Keeps:** for each styled element of each instance, the list of lowered declarations of the last read and
  the text rendered from it. The view keeps that text as the slot's previous value as well.
- **Where:** a table from instance to state in the closure `partsWithInline` builds. On .NET that is a
  `ConditionalWeakTable`, which takes reference types only; an instance of a value type would get no cache.
  Under Fable it is a `WeakMap`. The alternative is a cell in the view's own state for the slot, which needs
  a `Ranvier.View` API that hands a reader its previous value.
- **Saves:** `Declaration.renderMany`, 193 ns for six declarations, plus what (a) saves.
- **Signature:** none in `Ranvier.Css`; a new one in `Ranvier.View` for the alternative.

It keeps the most memory of the three and skips the cheapest part of the read.

### (c) Before admission: the last typed value and its lowered declaration

- **Keeps:** per dynamic slot, the last value data and the declaration lowered from it.
- **Where:** the typed value exists only inside the generated reader, so the cache needs a new public entry
  point in `Ranvier.Css` that receives the value before admission, a regenerated `Css.fs` (822 lines call
  `dynamicWithKeywords` or `dynamicImportantWithKeywords`), and a change to the extractor, which recognises
  those two calls by name (`tools/Ranvier.Css.Compile/Evaluation.fs`).
- **Saves:** admission and what (a) saves: at most 1,223 ns of a 1,704 ns read today, and at most 280 ns of a
  600 ns read after the proposal (fastest samples), less the comparison of two values.
- **Signature:** one public addition, and new generated code for every reader overload.

### What no cache removes

The reader runs first, because its result is the key. `Css.px` is 481 ns today and about 320 ns after the
proposal (fastest sample). Six readers pay it on every render in which any value of the instance changed.

## A style slot in an effect of its own

A slot marked isolated has its own effect, so it is read when a signal read by its readers or its conditions
changes, and at no other time. The core's dependency tracking does what a cache would do, with the reader
functions not run at all for an unrelated change.

- `Ranvier.View` 0.1.49, the version `Ranvier.Css.View` builds against, has it: `isolated` on a tag builder
  (from the package's XML documentation), over `TemplateParts.isolatedPart`.
- In the benchmark's model of it, a write to the text runs no reader (0.15 µs), a write to `top` runs the three
  readers of its element (6.0 µs today, 2.5 µs with the proposal), and each isolated slot costs 427 bytes more
  at mount. The model uses a core `Effect` for each style slot. An isolated slot in `Ranvier.View.Dom` keeps
  its own state besides, which this figure does not include.
- `styles { ... }` returns a class part and a style part. An application can isolate the style part today by
  matching on the array, which is awkward. One additive function in `CssView` would make it one call.

It is not part of this proposal:

- It adds an effect to every styled element that uses it, in a view that reads all slots of a part in one
  effect by design. A list of 10,000 rows would start 10,000 more effects.
- The slot commits from its own effect, apart from the render effect of its part.
- It is a public addition to `Ranvier.Css.View`.

It is the right second step if a reader value must cost nothing while its signals are unchanged, and it
follows `Ranvier.View/.claude/rules/view-uses-core.md`: change detection by a core primitive, not by a cache
in the view.

## What is left after the proposal

A read of about 600 ns (fastest sample, throwaway copy), in two places this design does not touch:

- **The typed constructor.** `Css.px` converts its number to text three times, in three constructions the
  generated code makes side by side (`Css.fs:61124`). Sharing one text needs a change to the generator and to
  `CalculationRuntime`.
- **The admission search.** `Css.top length` visits six nodes and builds a set at each visit to reach a node
  that is the same for every length. The path from a property's node to a value's node could be found once
  for the pair.

Each is a proposal of its own, with its own figures from the same benchmark.

## Decisions for the owner

1. Changes 1 to 3: go ahead on this branch, tests first?
2. Change 4 with them, or leave it?
3. Change 5: is the gain worth the test it needs?
4. An isolated style slot as a `CssView` function: wanted as a separate proposal?
