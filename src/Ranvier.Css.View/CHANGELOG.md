---
last_commit_released: 67b55b313953755db3c8b27b68515dbf1af009df
include:
  - ../../Directory.Build.props
depends_on:
  - ../Ranvier.Css/
updaters:
  - xml:
      file: Ranvier.Css.View.fsproj
      selector: /Project/PropertyGroup/Version
---

# Changelog

## 0.3.1 - 2026-11-23

### 🐞 Bug Fixes

* *(view)* Align Ranvier.View floor to 0.4.0 ([67b55b3](https://github.com/ranvier-org/Ranvier.Css/commit/67b55b313953755db3c8b27b68515dbf1af009df))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Css/compare/b74f6a27f79182593583cdc5ca9dd616dcaa4fbb..67b55b313953755db3c8b27b68515dbf1af009df)</small></strong>

## 0.3.0 - 2026-11-23

### 🐞 Bug Fixes

* *(release)* Pin pre-1.0 versions and unify CSS package cohort ([b74f6a2](https://github.com/ranvier-org/Ranvier.Css/commit/b74f6a27f79182593583cdc5ca9dd616dcaa4fbb))

### 🔗 Dependencies

* `src/Ranvier.Css` updated to 0.3.0

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Css/compare/0b74916d1f4b8bbe16dd7e79a2d71c15d4b8b10e..b74f6a27f79182593583cdc5ca9dd616dcaa4fbb)</small></strong>

## 0.2.0 - 2026-11-19

### 🚀 Features

* *(view)* Let a host read the definitions of the classes CssView applies (#32) ([0b74916](https://github.com/ranvier-org/Ranvier.Css/commit/0b74916d1f4b8bbe16dd7e79a2d71c15d4b8b10e))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Css/compare/eb4422e7d8a3cf2f401a5713ac85248aed112870..0b74916d1f4b8bbe16dd7e79a2d71c15d4b8b10e)</small></strong>

## 0.1.1 - 2026-11-14

### 🐞 Bug Fixes

* *(deps)* Build Ranvier.Css.View against stable Ranvier.View 0.1.49 and Ranvier.View.Dsl (#22) ([eb4422e](https://github.com/ranvier-org/Ranvier.Css/commit/eb4422e7d8a3cf2f401a5713ac85248aed112870))

<strong><small>[View changes on Github](https://github.com/ranvier-org/Ranvier.Css/compare/d4b413b68d4142ef0237552d868dd031b3fe9926..eb4422e7d8a3cf2f401a5713ac85248aed112870)</small></strong>

## 0.1.1-alpha.1 - 2026-10-28

### Fixes

- Initialize trusted publishing from the independent ranvier-org/Ranvier.Css repository.

## 0.1.0-alpha.2
