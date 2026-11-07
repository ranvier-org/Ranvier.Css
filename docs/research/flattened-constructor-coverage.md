# Flattened constructor coverage beyond `<color>`

Status: investigation, 2026-11-07. Data: checked-in `Css.manifest.json` (`@webref/css` 8.7.8).
Reproduce with `dotnet fsi tools/Ranvier.Css.Generate/constructor-coverage.fsx -- [types|detail <names>|primitives]`
after a Release build of `src/Ranvier.Css`.

Update: the planner is now `CompactFunctionGeneration`, parameterised by `FunctionFamily` (type, owner
location, noun, naming policy), and `<filter-function>` is enabled with Filter Effects names
(`radius`, `amount`). Recommendation 1 is done; the survey tables below predate it.

## The `<color>` fix

Before the typed color constructors, a value such as `oklch(60% 0.15 50deg / 0.5)` existed only as
structural constructors under `CssValue.Functions`. Callers assembled grammar sequences by hand, or
fell back to `Declaration.create` text. `<hex-color>` is prose-defined in the specification. It had
no factory at all, so `#663399` was also text only.

The fix has two parts:

1. **Flattened function constructors.** `GrammarRuntime.tryParameters` reduces a function production
   to `Required`, `Optional` and `Repeated` parameters. `GrammarRuntime.assemble` rebuilds the value
   through ordinary `construct` validation. `CompactColorGeneration` emits one constructor per function
   reachable from `<color>` on `CssValue.Color`, with a `Css` alias when the name is free. A
   `namingPolicy` table supplies channel names, because the grammar gives positional values no names.
   Static extraction evaluates the same constructors through the `assemble` factory kind.
2. **A validated prose primitive.** `Generated.hexColor` validates `<hex-color>` and is exposed as
   `Css.hex`, `CssValue.hex`, `CssValue.Color.hex` and `CssValue.HexColor.create`, with factory kind
   `hexColor`. The importer converts hash tokens through it.

Both parts are generic in the runtime. The generator binds part 1 to one root (`color`), one owner
location (`CssValue.Color`) and one naming table, and binds part 2 to one primitive.

## Types that need the same fix

The survey runs `tryParameters` over every function reachable from each shared type, using the
same acceptance rules as `GrammarGraphGeneration.acceptedNodes`. A function needs a naming policy
when its derived names repeat; the generator then falls back to positional suffixes such as
`number2`.

### Function families: flattenable, no generated constructors

| Type | Functions | Flattenable | Need a naming policy | Name already used on `Css` |
|---|---|---|---|---|
| `<transform-function>` | 21 | 21 | `matrix`, `matrix3d`, `rotate3d`, `scale3d`, `skew`, `translate`, `translate3d` | `perspective`, `rotate`, `scale`, `translate` |
| `<filter-function>` | 10 | 10 | none | `opacity` |
| `<basic-shape>` | 8 | 8 | `rect`, `xywh` | `inset` |
| `<image>` (excluding gradients) | 6 | 6 | none | none |
| `<gradient>` | 6 | 6 (one parameter each) | none | none |
| `<easing-function>` | 3 | 2 | `cubic-bezier` | none |
| `<content-list>` / `<target>` / `<counter>` | 9 | 9 | `target-counters` | `content` |
| `<track-list>` / `<box-size>` | 5 | 5 | none | none |

Notes:

- **`<transform-function>`** is the closest analogue to `<color>`: 21 positional functions, every one
  flattenable, and the most naming work. Derived names for `matrix()` are six `number` slots and for
  `translate()` two `length-percentage` slots. A policy would name them `a`–`f`, `x`/`y`/`z` and so on,
  following CSS Transforms terminology.
- **`<filter-function>`** needs no policy: every function has one or two distinct parameters
  (`blur(length)`, `drop-shadow(lengths, color)`). It is the cheapest family to enable.
- **`<gradient>`** flattens trivially, because each function takes a single `<*-gradient-syntax>`
  reference. The `-syntax` type is itself a sequence (`[ <angle> | to <side-or-corner> ]? , <color-stop-list>`),
  so constructors need flattening through that reference before they are useful. The colour planner
  stops at references (`Slot`), so this needs a planner extension, not only a new root.
- **Css alias collisions** follow the `color()` precedent: the alias is omitted and the constructor
  stays on its owner, for example `CssValue.TransformFunction.rotate`. With four colliding transform
  names, the owner-module form is the primary API for transforms.

### Functions the planner rejects

| Function | Admitted by | Reason |
|---|---|---|
| `contrast-color()` | `<color>` | top-level choice between structured alternatives |
| `linear()` | `<easing-function>` | repetition containing `&&` and a nested `{0,2}` list |
| `scroll()`, `view()` | `<single-animation-timeline>` | `\|\|` (any-of) groups |
| `ray()` | `<offset-path>` | optional bare keyword (`contain?`) holding no value |
| `anchor-size()` | sizing properties | `\|\|` (any-of) group |

These keep their structural constructors. Covering them needs named-argument records or builders,
not positional flattening.

### Prose primitives without a factory (the `<hex-color>` analogue)

Ranked by the number of properties whose grammar reaches them:

| Primitive | Properties | Example properties | Assessment |
|---|---|---|---|
| `<url-token>`, `<url-modifier>` | 39 | `filter`, `mask`, `cursor` | `url()` already flattens for quoted strings; a validated unquoted form would match `hexColor` |
| `<zero>` | 30 | `filter`, `mask` | the literal `0`; a constant suffices |
| `<hash-token>` | 25 | `mask`, `mask-image` | the only use is `<id-selector>` in `element(#id)`; shares the hash validation of `hexColor` |
| `<number-token>`, `<dimension>` | 60, 19 | legacy `-webkit-*` and sizing | compatibility grammars; low value |
| `<timeline-range-name>` | 11 | `animation-range` | identifier-shaped; reuse the `identifier` factory |
| `<decibel>`, `<semitones>` | 4, 2 | `voice-volume`, `voice-pitch` | CSS Speech dimensions missing from the numeric unit set |

## Recommended order

1. Generalise `CompactColorGeneration` into a planner parameterised by root type, owner location and
   naming policy. Enable `<filter-function>` first, because it needs no policy.
2. Add `<transform-function>` with a CSS Transforms naming policy. Owner-module constructors are the
   primary API, because of the alias collisions.
3. Add `<basic-shape>` (policy for `rect`/`xywh`), `<easing-function>` (`cubic-bezier` as `x1 y1 x2 y2`)
   and the `<image>` functions.
4. Extend the planner to flatten through single-reference `-syntax` types, then enable `<gradient>`.
5. Add an `identifier` factory for `<timeline-range-name>`, and numeric units for `<decibel>` and `<semitones>`.
