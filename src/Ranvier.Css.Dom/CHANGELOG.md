---
last_commit_released: b2bc272d07bb190e4c69d5670283cbef5afd678d
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

## 0.2.2 - 2026-11-25

### 🔗 Dependencies

* `src/Ranvier.Css` updated to 0.3.1

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Css/compare/67b55b313953755db3c8b27b68515dbf1af009df..b2bc272d07bb190e4c69d5670283cbef5afd678d)</small></strong>

## 0.2.1 - 2026-11-23

### 🐞 Bug Fixes

* *(view)* Align Ranvier.View floor to 0.4.0 ([67b55b3](https://github.com/ranvier-org/Ranvier.Css/commit/67b55b313953755db3c8b27b68515dbf1af009df))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Css/compare/b74f6a27f79182593583cdc5ca9dd616dcaa4fbb..67b55b313953755db3c8b27b68515dbf1af009df)</small></strong>

## 0.2.0 - 2026-11-23

### 🐞 Bug Fixes

* *(release)* Pin pre-1.0 versions and unify CSS package cohort ([b74f6a2](https://github.com/ranvier-org/Ranvier.Css/commit/b74f6a27f79182593583cdc5ca9dd616dcaa4fbb))

### 🔗 Dependencies

* `src/Ranvier.Css` updated to 0.3.0

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Css/compare/0b74916d1f4b8bbe16dd7e79a2d71c15d4b8b10e..b74f6a27f79182593583cdc5ca9dd616dcaa4fbb)</small></strong>

## 0.1.0 - 2026-11-14

### 🚀 Features

* Release typed DOM style adapters against stable Ranvier.Dom 0.1.0 and Ranvier.Css.Core 0.1.1 (#19) ([70bdedf](https://github.com/ranvier-org/Ranvier.Css/commit/70bdedfd9c7b7b1881a45c4ffb6eb6d17034208b))
