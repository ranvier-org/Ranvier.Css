---
last_commit_released: b74f6a27f79182593583cdc5ca9dd616dcaa4fbb
updaters:
  - xml:
      file: Ranvier.Css.fsproj
      selector: /Project/PropertyGroup/Version
  - xml:
      file: ../Ranvier.Css.Package/Ranvier.Css.Package.fsproj
      selector: /Project/PropertyGroup/Version
---

# Changelog

This changelog is generated from conventional commits by [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

## 0.3.0 - 2026-11-23

### 🏗️ Breaking changes

* *(bindings)* Generate the grammar storage in Ranvier.Css (#35) ([3efb9ba](https://github.com/ranvier-org/Ranvier.Css/commit/3efb9ba69dddb6d8451fcf03d395452716f81c38))

### 🐞 Bug Fixes

* *(release)* Pin pre-1.0 versions and unify CSS package cohort ([b74f6a2](https://github.com/ranvier-org/Ranvier.Css/commit/b74f6a27f79182593583cdc5ca9dd616dcaa4fbb))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Css/compare/0b74916d1f4b8bbe16dd7e79a2d71c15d4b8b10e..b74f6a27f79182593583cdc5ca9dd616dcaa4fbb)</small></strong>

## 0.2.0 - 2026-11-19

### 🚀 Features

* *(view)* Let a host read the definitions of the classes CssView applies (#32) ([0b74916](https://github.com/ranvier-org/Ranvier.Css/commit/0b74916d1f4b8bbe16dd7e79a2d71c15d4b8b10e))

### ⚡ Performance Improvements

* *(css)* Do each check once in a read of a reader value (#30) ([10e9644](https://github.com/ranvier-org/Ranvier.Css/commit/10e96440bad8435af85ddc50cb83477a4396bb6e))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Css/compare/87c566a11897816672927ca9b4e36e682daf37e3..0b74916d1f4b8bbe16dd7e79a2d71c15d4b8b10e)</small></strong>

## 0.1.1 - 2026-11-14

### 🐞 Bug Fixes

* *(css)* Prepare the runtime, bindings and build packages for the stable 0.1.1 release (#17) ([87c566a](https://github.com/ranvier-org/Ranvier.Css/commit/87c566a11897816672927ca9b4e36e682daf37e3))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Css/compare/9c4d37ef26ae2881751a8f486438d8815cb795f6..87c566a11897816672927ca9b4e36e682daf37e3)</small></strong>

## 0.1.1-alpha.4 - 2026-11-07

### 🚀 Features

* *(css)* Make the Ranvier.Css package include the bindings (#15) ([9c4d37e](https://github.com/ranvier-org/Ranvier.Css/commit/9c4d37ef26ae2881751a8f486438d8815cb795f6))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Css/compare/3bc774c054f07dc8ea6dc282438c173e91cead6b..9c4d37ef26ae2881751a8f486438d8815cb795f6)</small></strong>

## 0.1.1-alpha.3 - 2026-11-07

### 🚀 Features

* *(css)* Typed color function constructors and validated hex colors (#8) ([fa8322f](https://github.com/ranvier-org/Ranvier.Css/commit/fa8322ffc40b375dee999f7d864904a07616f921))
* *(css)* Generalise function constructors and add filter functions (#11) ([22e376a](https://github.com/ranvier-org/Ranvier.Css/commit/22e376a897d18022f37cd7a0c8f99cec77242907))
* *(css)* Add typed transform function constructors (#12) ([92ecdad](https://github.com/ranvier-org/Ranvier.Css/commit/92ecdadfe541b23fda251fa8b134f7a15c694ec6))
* *(css)* Add typed basic shape and easing function constructors (#13) ([9855905](https://github.com/ranvier-org/Ranvier.Css/commit/9855905478f556dd34efe8c3657b4513456c148d))
* *(css)* Add validated factories for prose-defined primitives (#14) ([3bc774c](https://github.com/ranvier-org/Ranvier.Css/commit/3bc774c054f07dc8ea6dc282438c173e91cead6b))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Css/compare/cc8266ea5be44213209ea386e422388d41bfba3e..3bc774c054f07dc8ea6dc282438c173e91cead6b)</small></strong>

## 0.1.1-alpha.2 - 2026-11-02

### 🐞 Bug Fixes

* *(css)* Preserve typed display under native AOT ([cc8266e](https://github.com/ranvier-org/Ranvier.Css/commit/cc8266ea5be44213209ea386e422388d41bfba3e))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Css/compare/d4b413b68d4142ef0237552d868dd031b3fe9926..cc8266ea5be44213209ea386e422388d41bfba3e)</small></strong>

## 0.1.1-alpha.1 - 2026-10-28

### Fixes

- Initialize trusted publishing from the independent ranvier-org/Ranvier.Css repository.

## 0.1.0-alpha.4
