# Static utility extraction

`Ranvier.Css.Compile` finds typed `styles { ... }` applications in an F# project and follows their utility dependencies, including source project references. It emits ordinary static CSS through the existing asset compiler. No utility registry is required.

## Install and run

The extractor is published as the .NET tool `Ranvier.Css.Compile`; its command is `ranvier-css-compile`. It needs the .NET 10 SDK or later on the path, because it asks MSBuild for the project's compiler arguments.

```bash
dotnet tool install --global Ranvier.Css.Compile
```

Restore and build the application and its references first, then extract:

```bash
dotnet build path/to/App.fsproj -c Release
```

```bash
ranvier-css-compile --project path/to/App.fsproj --output public/assets --single app.css
```

To pin the tool for a repository, install it into a tool manifest and run it through `dotnet`:

```bash
dotnet new tool-manifest
```

```bash
dotnet tool install Ranvier.Css.Compile
```

```bash
dotnet ranvier-css-compile --project path/to/App.fsproj --output public/assets --single app.css
```

Update the tool when you update Ranvier.Css. Class names are computed by the lowering code the tool ships, and they are guaranteed to match the application only when both come from the same Ranvier.Css release.

From a checkout of this repository, the same program runs without installing:

```bash
dotnet run --project tools/Ranvier.Css.Compile -c Release -- --project examples/Ranvier.Css.Utilities.Example/Ranvier.Css.Utilities.Example.fsproj --output public/assets --single app.css
```

The application uses the generated `Css` property sinks and `CssValue` vocabulary:

```fsharp
open Ranvier.Css
open Ranvier.Css.View

let md = minViewportWidth (Css.rem 48.)
let text = utility {
    Css.fontSize (Css.rem 1.)
    md { Css.fontSize (Css.rem 1.125) }
}
let card = utility {
    text
    Css.display CssValue.Display.block
    Css.width (Css.percent 100.)
    hover { Css.opacity (Css.number 0.8) }
}

// Within a view: div (styles { card }) { "Content" }
```

Numeric arithmetic enters a property through an explicit measured bridge. A generic variable preserves a grammar category; a measured variable also retains a calculation tree:

```fsharp
let selectedWidth usePixels =
    if usePixels then Css.px 42. :> CssValue.LengthPercentage.Value
    else Css.percent 50.

let themeWidth = Css.variable<CssValue.Width.Value> "--theme-width"
let gap = CssValue.Length.variableWithFallback "--gap" (Css.px 8.)
let layout = utility {
    Css.width (selectedWidth true)
    Css.width themeWidth
    Css.marginLeft (CssValue.Length.calc (gap + Css.px 2.))
}
```

The first conditional branch declares the common category; later compatible branches coerce to it. Extraction preserves the variable and arithmetic expressions and does not resolve their browser values. Structured values use semantic owners, for example `CssValue.Border.create (Some (Css.px 2.)) (Some CssValue.Border.Style.solid) None` supplies the width and style roles.

## Outputs

- `--single app.css`: one stylesheet.
- `--modules`: one stylesheet per source file containing applications; this is the default.
- `--css-modules`: CSS Modules retaining the runtime's stable global class identities.
- `--output directory`: required output directory; assets and `css-manifest.json` are written atomically per file.
- `--no-content-hash`: omit content hashes from filenames.
- `--public-path /assets/`: the public URL prefix recorded in the asset manifest.
- `--configuration Release`: select the MSBuild configuration.
- `--framework net10.0`: select a framework for a multi-targeted project.
- `--manifest path/to/Css.manifest.json`: use metadata matching custom generated bindings. Without it the tool reads `metadata/Css.manifest.json` from the `Ranvier.Css.Bindings` package the project references. For a project that references the bindings as a source project, it searches the ancestor directories for this repository's generated manifest.

Extraction and validation finish before any CSS output is written. Existing output files remain intact on extraction errors. Unrelated files and assets from previous compilations are retained.

## Evaluation boundary

The tool retains FSharp.Compiler.Service typed implementation trees and interprets a bounded subset. It never loads or invokes an application assembly. Module initialization actions and unrelated top-level values are not evaluated. Reactive predicates and property readers are retained as opaque callbacks; their bodies are never sampled at build time. The runtime and extractor lower the same ordered `StyleNode` model through `Style.create`, so class, gate and variable identities agree.

Supported static forms include literals, immutable bindings and records, tuples, finite lists and arrays, ordinary source functions and closures, fixed `if` expressions, int32/float arithmetic, comparisons, string concatenation, numeric conversions, pipelines, finite integer ranges, common `List`/`Seq` mapping/filtering/folding operations, and utility computation-expression loops. Utilities can accept other utilities as parameters. Conditions include viewport/media/support/container queries, hover/focus, explicit selector scopes, descendants, pseudo-elements, themes and application predicates.

Generated property and primitive helpers are matched by resolved symbols and the matching bindings manifest. Legacy sugar metadata remains supported. Compact schema 2 manifests describe owner factories and the resolved grammar graph; extraction uses the same construction, admission, numeric, and lexical validators as runtime bindings. Generated interface factories and their module initializers are never executed. Missing factory metadata produces a source diagnostic. Explicit `Declaration.create` remains an unchecked CSS-value escape with the same contract as runtime authoring.

Default limits are 100,000 evaluated expressions, 10,000 loop iterations and 128 nested evaluation frames per extraction. Infinite recursion and oversized ranges fail with source diagnostics. Mutable state, I/O, ambient clocks, randomness, reflection, arbitrary framework calls, unsupported expressions and runtime decisions determining static declarations also fail at their F# source positions. Use `whenActive` for runtime Boolean selection and typed property readers for continuously changing values.

## Discovery limits

Every call that writes a generated class to an element is a root: a `styles { ... }` application, and `CssView.parts`, `apply`, `applyMany`, `applyWithInline`, `partsWithInline` and `className`. Roots inside components whose application-level reachability is uncertain are included. Utilities with no reachable application disappear. Both branches containing explicit applications are retained without testing application state.

Each root gets one class. `styles { card; badge }` gets the class of the composition, in authored order, and that class is emitted with the rules of both utilities; the classes `card` and `badge` would have alone are emitted only where each is applied alone.

A root whose class would have no rules in the emitted stylesheet fails extraction at its source position:

- **The style cannot be resolved.** The root takes its utility from a function parameter or other run-time input, so its class is unknown at build time. Apply the utility with `styles { ... }` where the utility is known, or pass a statically known style.
- **The root lowers to no rules.** Every branch of the application is empty at build time, for example `styles { if false then card }`. Remove the application or give it a declaration.

Source `.fsproj` references are followed with their evaluated compiler arguments and MSBuild-selected nearest target frameworks. `--framework` selects the root target without overriding a referenced library's target. If the same source project is reached under conflicting target frameworks, extraction reports that ambiguity. Binary-only libraries do not expose implementation trees: consuming their unavailable utility definitions produces a diagnostic. Bring their source project into the build to allow interpretation. Core Ranvier libraries and generated bindings use the tool's explicit intrinsic model. The tool does not execute arbitrary NuGet-library implementations, application entry points or component constructors.

MSBuild is invoked only to resolve actual compiler arguments with compiler execution disabled; normal project evaluation and configured MSBuild targets still apply. Project references must already be restored and built. Target-framework/configuration inputs remain explicit.

## Verification

```bash
dotnet fsi build.fsx -- test --group dotnet
```

```bash
dotnet fsi build.fsx -- pack
```

`test --group dotnet` runs the extraction suite. `pack` packs the tool, installs it from `bin` into a scratch tool path and extracts with the installed command, once against the repository example and once against a project that takes Ranvier.Css from packages (`tools/check-tool-package.mjs`).

The fixtures contain unused throwing definitions, module initializers that would write sentinel files, referenced utilities, ordinary helpers, finite loops, nested browser conditions and an application gate with a throwing dynamic reader. Extraction must produce CSS while leaving all those runtime effects untouched.
