---
title: Extracting and serving CSS
order: 18
---

`Ranvier.Css.Compile` discovers typed `styles { ... }` applications in an F# project and emits
static CSS. It follows source utility dependencies automatically, including source project
references. There is no manual stylesheet registry.

## Build and extract

From a repository checkout, build the application and its references, then extract:

```bash
dotnet build examples/Ranvier.Css.Utilities.Example -c Release
dotnet run --project tools/Ranvier.Css.Compile -c Release -- \
  --project examples/Ranvier.Css.Utilities.Example/Ranvier.Css.Utilities.Example.fsproj \
  --output examples/Ranvier.Css.Utilities.Example/out \
  --single app.css --no-content-hash --public-path .
dotnet run --project examples/Ranvier.Css.Utilities.Example -c Release
```

The output directory contains `app.css`, `css-manifest.json` and the example's `index.html`.
The server reads the manifest and links its emitted URLs. Serve this directory with your
usual static-file host. The example renders server HTML; its entry point does not mount a
browser application.

For an application outside the checkout, use the compiler from this checkout and pass an
absolute project path. Supply `--manifest` pointing to the metadata matching the application's
`Ranvier.Css.Bindings` version; the package includes it under `metadata/Css.manifest.json`.
The compiler is currently a repository tool, not an installed `dotnet` global tool.

## Choose an output layout

Use one of these options with the same project and output arguments:

* `--single app.css` emits one stylesheet.
* `--modules` emits one stylesheet per source file containing applications. This is the default.
* `--css-modules` emits CSS Modules while preserving the runtime's stable global class identities.

Module output is grouped by application source file, not by every utility declaration. An
unused utility proven unreachable from an application contributes no CSS. Applications whose
application-level reachability is uncertain are conservatively retained.

Content hashes are enabled by default; use the manifest instead of constructing filenames.
`--public-path /assets/` sets the public URL prefix recorded there. `--no-content-hash` is useful
for a fixed filename during development. `--configuration Release` selects the MSBuild
configuration; `--framework net10.0` selects the root framework of a multi-targeted project.

## Load the CSS

Serve emitted files and add their manifest URLs to the page. `CssView.withStylesheets urls shell`
adds ordered, deduplicated stylesheet links to a Ranvier server document shell. The complete
example demonstrates reading the manifest before rendering.

Server and browser must use the same utility definitions and matching generated bindings.
Class names depend on static CSS structure and order, not instance values or registration
order. Hydration can therefore reuse the server's classes and current variables. Static CSS
must be loaded before expecting those classes to affect appearance.

Runtime updates only change classes and custom properties. They do not insert rules, compile
CSS or rebuild stylesheets. The browser handles media queries, selectors and the cascade.

## Build-time evaluation

The extractor interprets typed F# implementation trees. It does not load an application
assembly or execute module initializers, component constructors, reactive predicates or
property readers. Normal MSBuild project evaluation still runs to resolve compiler arguments.

Supported static forms include immutable bindings, records, tuples, finite collections,
ordinary source helpers and closures, fixed conditions, basic numeric/string operations,
finite integer ranges and utility builder loops. Limits bound expression count, loop count
and recursion. Used I/O, mutation, ambient clocks, randomness, reflection and unsupported
framework calls produce source diagnostics.

Source project references are followed using their selected target frameworks. Binary-only
utility libraries lack implementation trees and cannot be interpreted; include their source
project or use definitions the compiler can see. A component parameter determining static
composition also produces a diagnostic. Use `whenActive` for runtime selection and typed
readers for changing values.

Validation finishes before CSS output is written. Extraction errors preserve existing outputs.
Old assets and unrelated files are retained; your asset deployment process controls cleanup.
See the [compiler contract](https://github.com/ranvier-org/Ranvier.Css/blob/main/tools/Ranvier.Css.Compile/README.md)
for the complete supported subset and command options.

### Static composition and runtime values

Keep the stylesheet's structure knowable at build time. A deterministic source helper can
choose declarations from fixed arguments, but a component's current signal value cannot
decide which declarations exist in the extracted stylesheet. Use `whenActive` when runtime
state selects a utility, and a property reader when runtime state changes a property's value.
Both mechanisms keep the rule structure static.

The extractor recognizes generated numeric helpers, operators, checked adapters and typed
variable references through resolved symbols and the matching bindings manifest. It uses
the shared numeric domain for dimensions and percentage rules. Ordinary source arithmetic
can calculate a literal; arithmetic on typed CSS operands builds a calculation tree. Neither
path measures DOM elements, resolves custom properties or implements browser layout.

The distinction also applies to randomness: an ambient F# random call used to construct
static declarations is unsupported. `Css.random` constructs the CSS function, whose value
is left for a supporting browser. A supported function's presence in the typed API is not
a browser-support guarantee.

### Understanding diagnostics

An F# type error such as passing an angle to `Css.width` occurs before extraction. A numeric
domain error means the expression violates a function or destination's typing rules, such
as an incompatible percentage basis. A source diagnostic from extraction means the compiler
cannot interpret a used expression within its supported subset or evaluation limits.

When extraction reports runtime-dependent static composition, move the changing decision
into `whenActive` or a typed property reader. When it reports an unsupported framework call,
replace that static construction with supported deterministic source code or compute its
fixed input outside the extraction path. Raising evaluation limits does not make unsupported
calls executable. Keep the source location in the diagnostic when tracing a helper's caller.

Successful extraction establishes that the compiler could lower the supported source to
CSS. It does not establish that the target browser supports every function, that an external
custom property's value matches its typed handle, or that substituted values remain valid at
computed-value time. Use browser tests for those behaviors. The
[numeric-expression evaluation steps](css.md#when-an-expression-is-evaluated) explain which
checks happen during authoring, extraction and browser computation.

## Import existing CSS

The separate CSS importer parses existing `.css` with an established CSS parser and generates
Ranvier CSS sugar where the metadata supports a typed conversion. It retains explicit unchecked
escapes for unsupported values instead of claiming those values have been type checked.
See the [importer commands and round-trip example](https://github.com/ranvier-org/Ranvier.Css/blob/main/tools/Ranvier.Css.Import/README.md).

The utility syntax is F# composition; there is no Tailwind class-string parser in this release.
Shared breakpoint conditions and conditional composite utilities provide the corresponding
composition mechanism directly in typed code.
