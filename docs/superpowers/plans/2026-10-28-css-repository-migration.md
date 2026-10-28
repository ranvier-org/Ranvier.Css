# CSS repository migration

**Goal:** Copy Ranvier's CSS libraries, tools, examples and test gates into this independent repository.

**Architecture:** Preserve CSS source and generated bindings. Use pinned Ranvier.View packages by default and conditional sibling project references with `UseLocalRanvier=true`. Keep ShipIt and trusted publishing from the project template.

## Tasks

- [x] Copy the four CSS libraries, generator/compiler/importer, specification/compiler/browser tests, examples, CSS documentation samples, shared Fable packaging settings and package icon. Exclude all build output and dependencies.
- [x] Replace the template placeholder with the copied projects, preserve their versions, configure changelog updaters, and add conditional package/local references. Verify package restore and local project paths.
- [x] Adapt the build CLI to explicit build ordering and real test projects. Add the specification, asset/import and browser gates from Ranvier's CI with one aggregate result before publishing.
- [x] Document contributor setup, local references and test commands. Run the complete CSS CI gates, package checks and workflow validation.

## Validation

`dotnet fsi build.fsx -- ci --quick --report artifacts/ci.json` passed after setup:
187 specification/runtime/view tests, 23 source-extraction tests, 8 workflow/helper
tests, 18 importer tests, 11 expected compiler rejections, source/manifest freshness,
CSS asset and documentation round trips, jsdom and Chromium checks, formatting,
and all four NuGet packages. Restore of the pinned View/Dom packages also passed
with an empty package cache and the repository's explicit feed configuration.

The pack-then-Fable discovery issue was reproduced independently in Fickl 0.7.2
and upstream Fable 5.24.0 and filed as https://github.com/shayanhabibi/fickl/issues/137.
Nuspec staging now uses `bin/nuspec`; browser compilation clears legacy CSS nuspec
intermediates before invoking Fickl. The CSS source code and generated bindings
were copied unchanged from Ranvier; project references and build configuration
were adapted for this repository.

## Review focus

- Package mode must build without a sibling checkout; local mode must use matching source references throughout the dependency graph.
- Fable source packaging and traced package identities must be retained.
- The generated bindings are large: build dependent projects in order and exclude generated/test output from discovery and formatting.
- Specification/npm inputs must retain their locks and verification scripts.
- Failed browser, compiler-rejection or importer checks must fail the complete CI workflow used by publishing.
