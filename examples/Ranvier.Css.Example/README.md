# Typed CSS extraction and SSR example

From the repository root, select an output method:

```powershell
rtk dotnet run --project examples/Ranvier.Css.Example -c Release -- --modules
rtk dotnet run --project examples/Ranvier.Css.Example -c Release -- --css-modules
rtk dotnet run --project examples/Ranvier.Css.Example -c Release -- --single
```

`--output directory`, `--public-path prefix`, and `--no-hash` change emission. The default output is this example's ignored `out/` directory. It contains CSS, `css-manifest.json`, and `index.html` with ordered stylesheet links and the initial reactive custom-property value. CSS Modules entries require the processing configuration documented in the build library before browser delivery.

`Styles.fs` uses the default unified property helpers (`Css.width (Css.px 42.0)` and `Css.width reader`), a reactive component style with hover/media variants, a Ranvier component, and its explicit reset dependency. `Program.fs` is the application's extraction entry point. It references checked-in generated bindings as an ordinary project; it does not run specification generation during application compilation.

The executable accepts options in order, so the final mode option wins. The browser checks live under `tests/Ranvier.Css.Browser.Tests`; they exercise the same generated width constructors and reactive helpers, updates, cleanup, and cross-runtime identities under both commit modes.
