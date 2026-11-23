---
last_commit_released: 0b74916d1f4b8bbe16dd7e79a2d71c15d4b8b10e
force_version: 0.2.0
include:
  - ../Fable.Ranvier.Css.Dom/
  - ../../Directory.Build.props
depends_on:
  - ../Ranvier.Css/
updaters:
  - xml:
      file: Ranvier.Css.Dom.fsproj
      selector: /Project/PropertyGroup/Version
  - xml:
      file: ../Fable.Ranvier.Css.Dom/Fable.Ranvier.Css.Dom.fsproj
      selector: /Project/PropertyGroup/Version
---

# Changelog

This changelog is generated from conventional commits by [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

## 0.1.0 - 2026-11-14

### 🚀 Features

* Release typed DOM style adapters against stable Ranvier.Dom 0.1.0 and Ranvier.Css.Core 0.1.1 (#19) ([70bdedf](https://github.com/ranvier-org/Ranvier.Css/commit/70bdedfd9c7b7b1881a45c4ffb6eb6d17034208b))
