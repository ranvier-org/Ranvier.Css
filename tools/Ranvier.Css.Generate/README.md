# CSS specification consumer

Consumes consolidated and individual Webref CSS extracts into `Ranvier.Css.Spec`, resolves grammar references, and generates typed F# constructors and declarations.

From the repository root:

```powershell
rtk npm ci --prefix tools/Ranvier.Css.Generate --ignore-scripts
rtk node tools/Ranvier.Css.Generate/fetch-specs.mjs
rtk dotnet run --project tools/Ranvier.Css.Generate -c Release -- --runtime --specs-dir tools/Ranvier.Css.Generate/out/specs --bindings tools/Ranvier.Css.Generate/out/Css.Generated.fs --strict
rtk dotnet test tests/Ranvier.Css.Spec.Tests -c Release
rtk node tools/Ranvier.Css.Generate/verify-compact-bindings.mjs
```

The command writes `tools/Ranvier.Css.Generate/out/css-model.json`. To consume another consolidated snapshot:

```powershell
rtk dotnet run --project tools/Ranvier.Css.Generate -c Release -- --input path/to/css.json --output path/to/model.json --label my-snapshot --strict
```

Custom input defaults to the label `custom`. The SHA-256 always identifies the exact consumed UTF-8 bytes. Input files are bounded at 32 MiB before allocation; grammars are bounded at 65,536 UTF-16 code units and 128 nested expressions. The command reads local data without network access.

## Model

`Model.fs` defines catalogue identities, definitions, grammar nodes and diagnostics. A feature identity consists of kind, name, sorted scope set and optional descriptor owner. Descriptors inherit the enclosing at-rule's scope. Definition URLs, extension URLs, original syntax and additional metadata are retained. Metadata is a map of property names to raw JSON values; `Snapshot.toJson` emits those values as JSON, preserving their value types.

`Grammar.fs` parses value-definition syntax into explicit nodes for sequences, alternatives (`|`), any-of (`||`), all-of (`&&`), groups, functions, literal blocks, type/property/function references, parameterized types, numeric ranges, required groups and repetition. Combined multipliers such as `+#?` and `{2}?` retain nested cardinalities. Nodes carry zero-based UTF-16 offsets and lengths into the source grammar. Symbolic references remain symbolic, including recursive references.

`Webref.consume label bytes` returns a catalogue or JSON-path diagnostics for malformed input. Unknown top-level categories, misplaced descriptors and duplicate feature identities fail consumption. Every definition has one of three grammar states: `Parsed`, `Rejected` (source plus diagnostic), or `Missing` (the upstream entry supplies no syntax). Freeform spec metadata such as inheritance and computed values stays in its upstream representation.

`Snapshot.toJson catalogue` exports schema version 1 with explicit node tags, metadata, provenance and coverage, in stable feature order with LF endings. The CLI writes through a temporary file and preserves an existing output if input validation fails. Exit codes: 0 success; 1 input or I/O error; 2 rejected grammar under `--strict`; 64 invalid arguments. Strict mode still writes the complete diagnostic model. Grammar-free definitions are counted separately and do not fail strict mode.

## Pinned coverage and boundaries

The pinned dataset contains 1,839 definitions: 822 properties, 529 types, 172 functions, 159 selectors, 55 at-rules and 102 descriptors. All 1,710 supplied grammars parse; the remaining 129 definitions have `Missing` grammar. Its `css.json` SHA-256 is `30cb8daa786d6c27a1f74c5d413311c785ee0eec1dd34c65e8a4e6681c86baca`. The corpus test pins these counts and input identity, and CI installs the lockfile before running the suite.

The source lock also pins all 122 individual `ed/css` extracts from Webref revision `4bb34a8d08cb9b9c8b73f7f175703941eef5c20c`, with SHA-256 checks for each file. These contain 4,785 definitions: all 4,595 supplied grammars parse and 190 are prose-defined. Consolidation with the canonical package yields 4,596 current definitions and 286 archived obsolete definitions. All 4,882 definitions remain in the model, including 160 grammar-free definitions. Compact public owners exclude archived `#obsolete` scopes; current deprecated definitions remain available. Every nested raw value is retained; raw source definitions are also preserved as metadata when canonical definitions merge them.

## Latest definitions and obsolete APIs

`SpecSources.consume` reads individual extracts. `SpecSources.consolidate` selects the highest available level within each specification series; `consolidateWith` also accepts a canonical merged catalogue. Newer raw definitions beat older canonical definitions from the same series. Earlier definitions absent from a delta spec stay current. Superseded definitions retain their original grammar and provenance in an obsolete catalogue, including replacement URLs. The legacy generator emits `System.Obsolete` attributes for explicit upstream obsolescence and legacy aliases; compact generation currently preserves their metadata without emitting those warnings.

Cross-series extensions use canonical merged definitions. If a newer raw definition would replace an older canonical merge while dropping a distinct extension from another series, consolidation returns a diagnostic requiring a refreshed canonical source. It does not silently discard the extension or invent a grammar merge. The CLI includes both current and obsolete catalogues in its output.

The consumer is offline and reproducible. “Latest” means the newest available level in the supplied input set. Refresh the pinned npm package and Webref revision/lock together when updating upstream data; `fetch-specs.mjs --refresh-lock` regenerates hashes at the revision declared in the script. The default fetch verifies the committed lock and cached/downloaded bytes. It never silently changes the version being generated.

## Reference resolution and generated bindings

`Resolution.resolve` binds kind, normalized names, descriptor owners, lexical scopes, and source-document provenance. Equivalent aliases share a deterministic representative. Dependencies and recursive components are retained without recursively expanding grammar trees. Scoped forwarding definitions use their outer production when available. The complete corpus leaves two unresolved `anchored-feature` references from the upstream anchor-position extract; the manifest records these explicitly.

`RuntimeGeneration.generate namespaceName catalogue` is the compact shared-runtime entry point used by `--runtime`. It returns generated source and a schema 2 JSON manifest. Include the generated file before its consumers and reference `Ranvier.Css`:

```fsharp
open Ranvier.Css

let width = Css.width (Css.px 42.)
let display = Css.display CssValue.Display.block
let automatic = Css.width CssValue.Width.auto
let foreground = Css.color CssValue.Color.red
let background = Css.backgroundColor CssValue.Background.Color.Colors.red
```

The CLI defaults to `Ranvier.Css.Generated` for hidden grammar storage and exposes authoring APIs in `Ranvier.Css`. A custom namespace is retained for authoring unless it ends in `.Generated`, in which case authoring uses its parent namespace. The manifest records the authoring namespace. `Css` contains property sinks and concise primitive aliases. `CssValue` contains discoverable values and constructors. Property ancestry gives paths such as `CssValue.Border.Top.Width`, `CssValue.Animation.TimingFunction`, and `CssValue.Grid.Template.Columns`. Reusable numeric and lexical categories live at `CssValue.Length`, `CssValue.LengthPercentage`, `CssValue.String`, and similar core names. Other named types live under `CssValue.Values`.

Keywords from referenced shared grammars are also available on property owners: `CssValue.Display.block` and `CssValue.Display.none`. Color properties expose a shared `Colors` palette, and `CssValue.Color` exposes that palette directly. Structured constructors stay under their semantic owners, with parameters derived from named grammar references and longhand roles. Literal punctuation is inserted by the constructor. Repetitions accept lists; unordered groups accept optional named parts and require at least one selected part. Ambiguous anonymous roles produce explicit policy diagnostics in the manifest.

For example, the three border roles are width, style and color, in that order. Omit a role with `None`; at least one must be present:

```fsharp
let border =
    CssValue.Border.create
        (Some (Css.px 2.))
        (Some CssValue.Border.Style.solid)
        (Some CssValue.Color.red)
    |> Css.border
```

Each meaningful owner exports a `Value` interface for ordinary F# annotations. A conditional needs a common category on its first branch; later compatible branches coerce implicitly:

```fsharp
let size usePixels =
    if usePixels then Css.px 42. :> CssValue.LengthPercentage.Value
    else Css.percent 50.

let width = Css.width (size false)
```

Use `CssValue.Width.Value` to mix a numeric width with `CssValue.Width.auto`. Values enter property sinks directly. Static values return declarations; readers such as `Css.width (fun state -> Css.px state.Width)` return tracked style nodes. CSS-wide keywords such as `CssValue.initial` have a separate static-only type, including their property-owned aliases.

Typed variables retain the expected value category and optional fallback:

```fsharp
let spacing = Css.variable<CssValue.LengthPercentage.Value> "--spacing"
let fallback =
    CssValue.Variable.withFallback spacing
        (Css.px 8. :> CssValue.LengthPercentage.Value)
let width = Css.width fallback
```

`CssValue.variable` and `CssValue.var` are aliases for variable creation. `CssValue.Variable.declaration` assigns a typed variable. These references preserve a grammar category; they do not supply a numeric calculation tree. For arithmetic, use a measured numeric owner's `variable` or `variableWithFallback` helper:

```fsharp
let gap = CssValue.Length.variableWithFallback "--gap" (Css.px 8.)
let expanded = CssValue.Length.calc (gap + Css.px 2.)
let width = Css.width expanded
```

The measured helpers validate dashed names and retain the fallback expression without reading a browser value. Their fallback must have the owner's dimension and percentage hint. `CssValue.render` returns a value's CSS text.

The compact manifest records the exact namespace, encoded grammar, property identities and sinks, semantic owners and parameter roles, explicit factories, naming-policy diagnostics, unresolved references, and definition coverage. Factory metadata identifies numeric units and productions, lexical constructors, keywords, CSS-wide values, boolean operations, and grammar constructors. Static extraction uses this contract to call the same runtime grammar kernel without executing generated source bodies or reactive readers. Internal category sharing can change without changing semantic public paths.

Numeric constructors require finite numbers. Repetition counts, required groups, unordered nonempty groups, and numeric reference bounds are checked at construction. String constructors escape CSS string newlines and quotes; identifier constructors accept a restricted unescaped subset. Missing or rejected productions expose explicitly named `unsafeFromTokens` entry points; unresolved references remain diagnostics. These entry points accept unchecked text. Grammar cannot determine prose-only exclusions, complete computed-value behavior, or browser support.

`RuntimeGeneration.generateLegacy` is the explicit programmatic compatibility entry point for the previous canonical-plus-sugar shared-runtime API and its legacy manifest. `Generation.generate` retains the standalone canonical API used when the CLI omits `--runtime`. Legacy source, numeric, sugar, and obsolescence tests remain separate from the compact contract tests. The [CSS source importer](../Ranvier.Css.Import/README.md) accepts compact schema 2 manifests and legacy versioned sugar maps; it derives atomic conversions from resolved grammar and explicit factory metadata.

## Numeric expression semantics

`NumericSemantics.fs` implements the numeric type rules from [CSS Values 4](https://drafts.csswg.org/css-values-4/#calc-type-checking) and [CSS Typed OM](https://drafts.css-houdini.org/css-typed-om-1/#numeric-typing). These algorithms are specified in prose, so they are encoded explicitly alongside the consumed grammar. The independently versioned `numericSemantics` metadata retains their source URLs in the model snapshot and legacy binding manifest. Compact factories identify their numeric production and unit alongside the exported grammar.

A `NumericType` contains dimension exponents and a percentage hint. `add` checks compatible dimensions; `multiply`, `invert`, and `divide` retain compound intermediate units and support cancellation. `inferFunction` checks the argument and result policies for `calc`, comparison, rounding, trigonometric, exponential, absolute-value, and sign functions. Unknown functions return an explicit error. The model checks types; numerical evaluation, browser support, computed values, and non-finite value handling belong to later expression evaluation and rendering.

`NumericProjection.derive` supplies the legacy property's calculation contexts from the same checked whole-value conversion paths used by sugar. Each context retains numeric bounds, percentage availability and basis, and integer rounding policy. Required siblings and function arguments cannot authorize an unrelated whole-property calculation. Unknown productions remain in the grammar model with no invented numeric semantics.

For example, `width` has a length percentage basis, while `opacity` accepts independent number and percentage results. That distinction rejects adding an ordinary percentage to a number. Percentage hints survive unit cancellation. A number result can satisfy an integer property after rounding to the nearest integer, with ties toward positive infinity. Math range checks are deferred to computed-value handling; they do not reuse literal-token rejection.

Compact numeric helpers retain calculation trees through arithmetic. Use the destination category's `calc` constructor to check the expression's dimension and percentage context before passing it to a property:

```fsharp
let doubled = CssValue.Length.calc (Css.px 4. * 2.)
let width = Css.width doubled
```

Built-in prose primitives provide typed numeric/string/identifier helpers while their definitions retain the original opaque grammar status in metadata. Full computed-value semantics and property-specific prose exclusions remain outside grammar validation.

The CLI emits a model, bindings and adjacent `.manifest.json`, writing each file atomically. Output paths must be distinct and cannot overwrite specification inputs. `--namespace` changes the generated namespace. `verify-compact-bindings.mjs` exercises caller typing and runtime grammar contracts against the checked-in bindings. `verify-bindings.mjs` regenerates and checks source/manifest freshness before dispatching to the compact verifier; it retains the legacy checks for legacy manifests. `verify-assets.mjs` checks asset modes with Lightning CSS; `verify-browser.mjs` runs the Fable DOM checks.

To regenerate the checked-in bindings after a generator/spec update and validate them:

```powershell
rtk proxy dotnet run --project tools/Ranvier.Css.Generate -c Release -- --runtime --specs-dir tools/Ranvier.Css.Generate/out/specs --bindings src/Ranvier.Css.Bindings/Generated/Css.fs --strict
rtk node tools/Ranvier.Css.Generate/verify-bindings.mjs
```

Sources: [Webref consolidated format](https://github.com/w3c/webref/blob/main/packages/css/README.md), [CSS value-definition syntax](https://drafts.csswg.org/css-values-4/#value-defs).
