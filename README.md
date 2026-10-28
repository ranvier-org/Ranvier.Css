# Ranvier.Css

Typed CSS values, specification-generated bindings, static CSS extraction and reactive Ranvier.View integration.

[Repository](https://github.com/ranvier-org/Ranvier.Css) · [Documentation](https://ranvier-org.github.io/Ranvier.Css/)

The CSS projects, tools and checks were copied from Ranvier. Published Ranvier.View dependencies are pinned so this repository builds independently. A sibling checkout is supported for coordinated development.

## Projects

- `src/Ranvier.Css`: typed values, calculations, stylesheets and composable utilities.
- `src/Ranvier.Css.Bindings`: generated property/value bindings and their metadata manifest.
- `src/Ranvier.Css.Build`: deterministic CSS asset compilation.
- `src/Ranvier.Css.View`: style and utility bindings for Ranvier.View.
- `tools/Ranvier.Css.Generate`: pinned Webref consumer and binding generator.
- `tools/Ranvier.Css.Compile`: static F# source extractor.
- `tools/Ranvier.Css.Import`: CSS-to-F# importer.

See the [CSS API guide](src/Ranvier.Css/README.md), [generator guide](tools/Ranvier.Css.Generate/README.md), and [contributor guide](CONTRIBUTOR.md).

## Setup and checks

Install .NET 10, the .NET 8 runtime, and Node.js 22 or later. All workflows run through Partas.Build:

```powershell
rtk dotnet fsi build.fsx -- dev setup --with-browser
rtk dotnet fsi build.fsx -- ci
```

`ci` restores dependencies, checks formatting, builds libraries and tools in dependency order, runs every CSS check, and packs the four libraries. It uploads nothing.

For individual CI shards:

```powershell
rtk dotnet fsi build.fsx -- test --group dotnet
rtk dotnet fsi build.fsx -- test --group bindings
rtk dotnet fsi build.fsx -- test --group assets
rtk dotnet fsi build.fsx -- test --group browser
```

The groups cover specification/runtime/view and extraction tests; generated source freshness and compiler rejections; asset modes, utility extraction, documentation samples and importer round trips; and Fable/jsdom/Chromium behavior. Bare `test` runs all groups. `--quick` skips dependency/browser installation after setup.

Other commands are `build`, `compile` (browser checks), `format`, `format --dry-format`, `pack`, `docs`, `bump` and `shipit`. `publish --dry-publish` validates and packs without uploading; `publish` additionally requires a NuGet key. Run `--help` for details.

## Working across repositories

```powershell
rtk dotnet fsi build.fsx -- dev local
rtk dotnet fsi build.fsx -- test --group dotnet
```

`dev local` uses `../Ranvier`, cloning it only if the directory is missing, and writes ignored `Local.Build.props`. Supply `--ranvier-root <path>` for another location. Existing checkouts are never reset or updated implicitly.

`dev packages` switches back to pinned NuGet dependencies. For one-off builds without a local configuration file, use `dotnet build -p:UseLocalRanvier=true -p:RanvierRoot=<path>`. Use `Local.Build.props.example` if configuring an IDE manually.

CI uses package mode and the same Partas.Build commands. Its matrix checks all four groups, formatting and NuGet packages; `CI result` fails if any check fails. The ShipIt and publish workflows run only after the complete CI workflow succeeds.

Pack staging metadata is written under `bin/nuspec`, away from assembly output.
The browser compile stage removes legacy nuspec files from CSS intermediate
directories to avoid [Fickl #137](https://github.com/shayanhabibi/fickl/issues/137),
which also reproduces in upstream Fable 5.24.0.

## Releases

Each library retains its version from Ranvier and has a ShipIt changelog with a project version updater. Configure trusted NuGet publishing and GitHub permissions as described in [CONTRIBUTOR.md](CONTRIBUTOR.md#maintainer-setup). Fable source packaging and optional `.Traced` package identities are preserved.

For a change spanning View and CSS, validate explicit source revisions in local mode first, release View, then update and validate CSS against that published version before releasing CSS. A View release alone does not require a CSS release.
