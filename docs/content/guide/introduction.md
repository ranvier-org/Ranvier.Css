---
title: Introduction
order: 1
---

## Installation

```bash frame="terminal"
dotnet add package Ranvier.Css.Bindings --version 0.1.1-alpha.1
```

The bindings package includes the portable `Ranvier.Css` runtime as a dependency.
Both support .NET 8, .NET 10 and Fable.

For reactive view integration, add `Ranvier.Css.View` version `0.1.1-alpha.1`.
Static asset compilation uses the separate .NET 10 `Ranvier.Css.Build` package.
These are preview versions; the API can change before 1.0.

The View adapter's Xantham dependency requires the public
[Xantham package feed](https://nuget.cloudsmith.io/shayanhabibi/xantham/v3/index.json).
The repository's `nuget.config` already configures it.

## Usage

```fsharp
open Ranvier.Css

let width = Css.width (Css.px 42.)
let css = Declaration.render width
```

Continue with [typed CSS](../view/css.md),
[utility composition](../view/css-utilities.md), or
[static extraction](../view/css-build.md).

## Contributing

Clone [ranvier-org/Ranvier.Css](https://github.com/ranvier-org/Ranvier.Css) and
install .NET 10, the .NET 8 runtime and Node.js 22 or later. Run the same
Partas.Build workflows locally and in CI:

```bash frame="terminal"
dotnet fsi build.fsx -- dev setup --with-browser
dotnet fsi build.fsx -- ci
```

Use `dev local` to work against a sibling Ranvier checkout and `dev packages`
to return to pinned NuGet dependencies. See
[CONTRIBUTOR.md](https://github.com/ranvier-org/Ranvier.Css/blob/main/CONTRIBUTOR.md)
for the full workflow and release requirements.

:::tip
Editing the site? Serve it with live reload from the repository root:

`dotnet fsi build.fsx -- docs --watch`

The page reloads as you type.
:::
