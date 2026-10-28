# CSS to Ranvier.Css

Parses UTF-8 `.css` files with pinned [PostCSS](https://postcss.org/api/) and [CSSTree](https://github.com/csstree/csstree), then generates an F# module using the default typed property API. Compact schema 2 manifests and legacy sugar manifests are both supported. The generated module exposes `rules: Rule list` for `Stylesheet.render` or the asset compiler. Existing selectors and class names are retained.

From the repository root:

```powershell
rtk npm ci --prefix tools/Ranvier.Css.Import --ignore-scripts
rtk node tools/Ranvier.Css.Import/index.mjs --input styles.css --output Styles.Generated.fs --module ImportedStyles
```

For example:

```css
.card {
  width: 240px;
  display: block;
  color: red !important;
}
```

becomes:

```fsharp
module ImportedStyles

open Ranvier.Css

let rules: Rule list = [
    Rule.style (Selector.className "card") [
        Css.width (Css.px 240.0)
        Css.display CssValue.Display.block
        (Css.color CssValue.Color.red |> Declaration.important)
    ]
]
```

Include the generated file before its consumers in a project referencing `src/Ranvier.Css.Bindings`. To use the asset compiler, also reference `src/Ranvier.Css.Build`:

```fsharp
open Ranvier.Css.Build

let resource: CssModule = {
    Name = "imported"
    Dependencies = []
    Rules = ImportedStyles.rules
    Styles = []
}

let result =
    Compiler.compile
        { Options.defaults with Mode = OutputMode.SingleFile "app.css" }
        [resource]
```

## Coverage and preservation

Atomic numeric dimensions, percentages, numbers, strings and known keywords become typed calls only when the generated specification manifest supplies a valid conversion path. Compact conversion walks the encoded resolved grammar through references, alternatives, nullable siblings and singleton repetitions; required siblings and larger minimum cardinalities prevent atomic conversion. Numeric bounds on every admitted path, contextual unit comparisons, integer limits and decimal precision are checked before emission. Recursive references terminate without granting unsupported conversions. Keywords use property-owned factories when available, and CSS-wide aliases retain their separate static type. The importer uses actual factory owners and member names; it does not guess F# names or cast unchecked tokens to grammar markers. Legacy manifests retain their existing accepted-marker and range-guard behavior.

Custom properties, functions such as `calc()`/`var()`, compound values and descriptors retain their tokens through `Declaration.create`. Rule shapes the structured tree cannot preserve use the explicit `Rule.unsafeFromText` escape. This includes declarations interleaved with nested rules, escaped at-rule heads, and comments embedded in declaration punctuation. Opaque source slices retain delimiters and statement semicolons. Rules and repeated declarations retain their order; no rules are hoisted, imports fetched or class names renamed.

Each run also writes `Styles.Generated.report.json`, containing the input hash, typed/raw declaration counts and fallback reasons with line/column locations. `--report path` changes that destination. Add `--strict-typed` to reject declaration/rule fallbacks before writing. Standalone comments are preserved without counting as a typing fallback. Parser success checks syntax; it is not a browser-support or complete property-semantics guarantee. Syntactically parsed values outside a generated factory's range remain explicit raw declarations instead of making the F# module throw during initialization.

Opaque blocks work with regular per-module output and a single imported resource emitted as one file. The asset compiler rejects CSS Modules rewriting or lifting across multiple resources when opaque blocks prevent safe analysis. Known structured rules retain the usual output options. The caller/parser validates opaque contents; the build library cannot inspect their preambles.

## Options and verification

- `--input file.css`, `--output file.fs`: required paths.
- `--module ImportedStyles`: generated module name; defaults to `ImportedStyles`.
- `--manifest path`: shared-runtime binding manifest; defaults to the checked-in compact library manifest. Compact schema 2 requires `encodedGrammar`, `properties` and explicit `factories`. Legacy manifests with a version 2 `sugar` map remain accepted.
- `--report path`, `--strict-typed`, `--help`.

No input or manifest path may also be an output. Syntax, naming, manifest and strict-mode failures preserve existing outputs; successful writes replace each artifact atomically. Output formatting is deterministic, but source whitespace and equivalent numeric spellings may change. Fallback contents and comments retain their source tokens.

```powershell
rtk npm test --prefix tools/Ranvier.Css.Import
rtk node tools/Ranvier.Css.Import/verify-import.mjs
```

Unit tests cover both manifest formats, compact grammar admission, range rejection and the checked-in default manifest. The integration gate compiles generated F# against the real binding library, renders it back to CSS, compares normalized parser trees, checks regular asset compilation, compiles a compact custom-namespace binding fixture, and tests CLI output preservation. Build the specification generator first when running that gate alone; the normal CI CSS suite does this before import verification.
