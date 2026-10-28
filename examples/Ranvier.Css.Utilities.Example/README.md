# Composite utility example

This example uses the real generated CSS bindings, reusable breakpoint builders and top-level utilities. `Styles.fs` composes responsive text and width, applies a spacing utility under `md`, and adds a reactive busy condition plus a typed width reader. The component applies them through element attributes:

```fsharp
div (styles { card }) {
    "Responsive composite utilities"
}
```

The same attribute syntax works on void elements: `input (styles { field })` or `img (styles { thumbnail })`. Utility application produces ordinary Ranvier attribute parts. Combine additional attributes using the existing array API, for example `Array.append [| Attrs.attribute ("class", "user-card") |] (styles { card })`. Declared user classes are merged with generated classes.

Build the project, extract styles without running the application, then render the HTML:

```powershell
rtk dotnet build examples/Ranvier.Css.Utilities.Example -c Release
rtk dotnet run --project tools/Ranvier.Css.Compile -c Release -- --project examples/Ranvier.Css.Utilities.Example/Ranvier.Css.Utilities.Example.fsproj --output examples/Ranvier.Css.Utilities.Example/out --single app.css --no-content-hash --public-path .
rtk dotnet run --project examples/Ranvier.Css.Utilities.Example -c Release
```

The output contains `app.css`, `css-manifest.json` and `index.html`. The server reads the adjacent manifest to link the emitted asset URLs and includes the initial width variable. Extraction follows the `styles` application automatically; there is no stylesheet registry. The unused purple utility contributes no CSS. `--modules` and `--css-modules` select the other asset layouts; the same manifest-driven server entry point links those outputs too.

The entry point only renders server HTML. A browser host uses the existing Ranvier mounting API: busy changes select an existing class, width changes update a custom property, and viewport/hover changes are handled by CSS. No reactive update inserts stylesheet rules.

The extractor supports a bounded deterministic F# subset. Unsupported used computations fail with source diagnostics. See the [compiler contract](../../tools/Ranvier.Css.Compile/README.md) for supported helpers, referenced projects and binary-library limits.
