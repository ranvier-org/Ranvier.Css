---
title: Typed CSS
order: 16
---

Write CSS with the same F# values you use to compose views. The short `Css` helpers check
property/value compatibility, utilities compose those declarations, and `styles` applies them
as element attributes. The build compiler emits static stylesheets.

```fsharp
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View.Sugar

let field = utility {
    Css.width (Css.percent 100.)
    Css.padding (Css.rem 0.75)
    Css.color CssValue.Color.black
    focus { Css.color CssValue.Color.blue }
}

// Inside a component's view:
// input (styles { field })
// div (styles { field }) { "Content" }
```

`styles` belongs in the constructor's attributes, so it works on void elements such as `input`
and `img` as well as elements with children. It generates stable class names and merges them
with declared user classes.

## Install

The CSS libraries target .NET 8 and .NET 10 and include Fable sources. Reference both the
generated property bindings and the view adapter:

```bash
dotnet add package Ranvier.Css.Bindings --prerelease
dotnet add package Ranvier.Css.View --prerelease
```

`Ranvier.Css` is their portable runtime dependency. `Ranvier.Css.Build` is the separate .NET
asset compiler library. The view adapter also depends on the view packages; retain the
[Xantham package source](https://ranvier-org.github.io/Ranvier.View/) required by those packages.

For a source checkout, reference `src/Ranvier.Css.Bindings/Ranvier.Css.Bindings.fsproj` and
`src/Ranvier.Css.View/Ranvier.Css.View.fsproj`. See [extracting and serving CSS](css-build.md)
for the build command: adding a class to HTML does not load its stylesheet.

## Typed properties

Open `Ranvier.Css` for the default, shorter API:

```fsharp
let panel = utility {
    Css.display CssValue.Display.block
    Css.width (Css.px 320.)
    Css.maxWidth (Css.percent 100.)
    Css.padding (Css.rem 1.)
    Css.fontSize (Css.rem 1.125)
    Css.opacity (Css.number 0.9)
}
```

Values carry grammar types: `Css.width (Css.deg 90.)` does not compile. Constructors also
validate supported numeric ranges. Values such as lengths, percentages, numbers and keywords
are shared where the property's grammar permits them; arbitrary strings are not silently
accepted as typed values.

Use `CssValue` to discover keywords and structured constructors by their semantic owner.
For example, border construction exposes the width, style and color roles in that order:

```fsharp
let bordered = utility {
    Css.border (
        CssValue.Border.create
            (Some (Css.px 2.))
            (Some CssValue.Border.Style.solid)
            (Some CssValue.Color.red))
}
```

Use `None` to omit an unordered role; at least one must be present. Constructors preserve
required groups, repetitions and other structures from the grammar. Each meaningful owner
also exports a `Value` interface for annotations and common branches:

```fsharp
let flexibleSize usePixels =
    if usePixels then Css.px 42. :> CssValue.LengthPercentage.Value
    else Css.percent 50.

let flexiblePanel = utility { Css.width (flexibleSize false) }
```

The first branch declares the common category; later compatible branches coerce to it.

## Numeric expressions

Use `Css` helpers for numeric literals, arithmetic and math functions. Finish an expression
with the destination numeric owner's `calc` bridge before passing it to a property:

```fsharp
let calculated = utility {
    Css.width (CssValue.LengthPercentage.calc (Css.percent 100. - Css.rem 2.))
    Css.maxWidth (CssValue.Length.calc (Css.min [Css.px 960.; Css.px 1280.]))
    Css.fontSize (CssValue.Length.calc (Css.clamp(Css.rem 1.25, Some (Css.rem 1.), Some (Css.rem 2.))))
    Css.opacity (CssValue.Number.calc (Css.sin Css.pi))
}
```

The surface includes comparison, rounding, remainder, trigonometric, exponential and sign
functions, together with `e`, `pi`, `infinity`, `negativeInfinity` and `nan`. Function arguments
retain their dimensions. For example, `Css.sin (Css.px 10.)` does not compile.

Mixed length/percentage arguments work directly in tuple helpers, such as
`Css.min(Css.percent 100., Css.rem 60.)`. Finish that result with the length-percentage bridge.
F# lists need one element type; homogeneous length lists can use the list overload above:

```fsharp
let capped = utility {
    Css.width (CssValue.LengthPercentage.calc (Css.min(Css.percent 100., Css.rem 60.)))
}
```

Operators preserve dimensions. Multiplication and division support compound intermediate
dimensions, so `Css.px 20. / Css.px 40.` is a number, while a length multiplied by a length
cannot be assigned to `width`. An angle cannot be added to a length. The generated expression
types and property overloads come from the same generator and package as the other bindings.

For typed property and grammar inputs, use checked bridges such as `CssValue.Angle.calc`,
`CssValue.Length.calc`, `CssValue.Number.calc` or `CssValue.Integer.calc`. The bridge preserves the calculation:

```fsharp
let turn = CssValue.Angle.calc (Css.deg 30. + Css.deg 15.)
let turned = utility {
    Css.rotate turn
}
```

These bridges also work inside reactive readers. They check the destination production's
dimensions and percentage rules, and retain browser evaluation of the expression.

Calculations remain CSS expressions. The browser evaluates relative units, layout-dependent
percentages, special constants, integer rounding and computed-value range handling. A negative
literal can fail a constructor's range check; a calculation retains its expression for CSS's
computed-value rules.

Percentages retain their provenance even when division cancels their dimensions. For compound
arithmetic that needs an explicit percentage basis, use a basis helper:

```fsharp
let proportional = utility {
    Css.width (CssValue.LengthPercentage.calc (CssValue.LengthPercentage.percent 50. * Css.px 2. / Css.px 1.))
}
```

The property must permit that percentage basis. A number-only property does not acquire
percentage support merely because an expression eventually produces a number.

### When an expression is evaluated

There are three distinct steps:

1. **Authoring checks the expression.** F# types reject incompatible operands and property
   arguments. The shared numeric domain checks function typing, compound dimensions and
   percentage hints when a calculation is constructed or assigned. A checked bridge such as
   `CssValue.Length.calc` applies the destination production's rules; it does not compute a pixel value.
2. **Extraction emits the expression.** The build compiler interprets supported source code
   and serializes the same calculation tree as the runtime. For example, it retains
   `calc(100% - 2rem)` rather than substituting a viewport width or a root font size. It never
   samples a reactive reader to guess its possible values.
3. **The browser computes the result.** CSS resolves variables, relative units, percentage
   bases and layout, then applies the function's computed-value rules. CSS integer rounding,
   range handling and special values are part of this step. Browser support still determines
   whether a generated draft function can be used.

Ordinary F# arithmetic still computes ordinary F# numbers: `Css.px (10. + 5.)` constructs a
literal. Arithmetic on typed CSS values, such as `Css.px 10. + Css.px 5.`, constructs a CSS
calculation. This distinction matters for relative units, CSS rounding and special values;
the typed layer is not a host-side layout engine.

Typed numeric custom properties can participate in the same expressions:

```fsharp
let gutter = CssValue.Length.variableWithFallback "--gutter" (Css.rem 1.)

let variableWidth = utility {
    Css.width (CssValue.Length.calc (gutter + Css.px 8.))
}
```

The measured reference declares the expected numeric type. Use `CssValue.Length.variable`
when no fallback is needed. A generic reference such as
`Css.variable<CssValue.Width.Value> "--width"` preserves a grammar category for direct property
assignment; it does not supply an arithmetic tree. Neither form registers an `@property` rule or
prove that every external declaration assigning the variable follows that type; substitution
and invalid-at-computed-value behavior remain browser responsibilities.

Newer draft forms include `Css.progress`, `Css.random`, `Css.calcMix`, `Css.siblingCount()`,
`Css.siblingIndex()`, structured interpolation and intrinsic sizing:

```fsharp
let intrinsicPanel = utility {
    Css.width (Css.calcSize(
        Css.intrinsicKeyword "auto",
        Css.intrinsicSize + Css.px 16.
    ))
}
```

`intrinsicSize` represents the scoped `size` keyword. It cannot be assigned directly as a
normal numeric result. The generated property overload must admit `calc-size()`, and its
destination grammar must admit the chosen intrinsic basis. Browser support for draft functions
varies; generating valid syntax does not add browser support.

## Reactive values

Use a reader when a property varies per component instance:

```fsharp
open Ranvier

type Meter = { Width: Signal<float> }

let meter = utility {
    Css.width (fun (state: Meter) -> CssValue.Length.calc (Css.px state.Width.Value + Css.rem 2.))
    Css.maxWidth (Css.percent 100.)
}
```

The emitted rule uses a generated custom property, for example `width:var(--r-…-0)`.
The view binding writes the current value to the element and tracks the signals read by that
reader. Updating `Width` updates the variable; it does not create a stylesheet rule. Disposal
releases the binding. Server rendering writes the current value with the same generated class
identity used by the browser.

Read signals **inside** the reader. Reading a signal while creating a top-level declaration
captures a snapshot instead of establishing a per-instance binding.

For example, this complete component combines a static calculation, a typed variable and a
reactive calculation:

```fsharp
open Ranvier.View

type EvaluatedCard = { Width: Signal<float> }

let cardGap = CssValue.Length.variableWithFallback "--card-gap" (Css.rem 1.)

let evaluatedCard = utility {
    Css.width (fun (state: EvaluatedCard) -> CssValue.Length.calc (Css.px state.Width.Value + Css.rem 2.))
    Css.maxWidth (CssValue.LengthPercentage.calc (Css.percent 100. - cardGap))
}

let evaluatedCardComponent =
    Component.define
        (fun _ initialWidth -> { Width = Api.createSignal initialWidth })
        (fun _ ->
            div (styles { evaluatedCard }) { "Calculated card" })
```

Extraction emits the `max-width` calculation and a `width` rule referring to a generated
custom property. Mounting the component runs its width reader and writes the calculation
to that property. Signal changes rerun the reader; CSS computes the resulting width.
The `--card-gap` fallback is used by CSS variable substitution when applicable. Defining the
typed handle neither assigns that custom property nor registers its syntax with the browser.

Animation uses these same phase boundaries: type compatibility does not guarantee that two
CSS values can be interpolated by an animation engine. Intrinsic keywords, unresolved
variables and layout-dependent calculations may require browser resolution or a discrete
transition. See [build-time evaluation](css-build.md#build-time-evaluation) for the supported
source-code subset and diagnostics.

## User classes

Combine ordinary attributes and style attributes using the existing array API:

```fsharp
// Inside a component's view:
// div (Array.append [| Attrs.attribute ("class", "card analytics-target") |] (styles { panel })) {
//     "Both user classes and the generated style class are retained"
// }
```

The class merger preserves static and reactive user classes, deduplicates repeated tokens, and
updates the combined value when a contributing reader changes. Removing a conditional utility
class preserves the user's classes. CSS precedence comes from rule specificity and stylesheet
order, not the order of tokens in the HTML `class` attribute.

## CSS-wide keywords and escape hatches

Static CSS-wide declarations such as `Css.color Css.initial` are supported. For a reactive
choice of `initial`, `inherit`, `unset` or other CSS-wide keywords, use `CssView.inlineStyle`
with a reader returning declarations: routing those keywords through a custom property changes
their cascade meaning. Reserved F# names such as `inherit` require double-backtick escaping.

`Declaration.create` and explicitly named `unsafeFromTokens` constructors allow unchecked CSS tokens.
Use them deliberately when the typed grammar cannot express the value. They do not validate
the value's CSS grammar.

## What the specs guarantee

Bindings are generated from the locked Webref corpus. Consolidation chooses the latest
same-series specification and retains superseded definitions in the model. Compact public owners
exclude archived obsolete scopes; current deprecated definitions remain available. The manifest
records provenance, unresolved references and prose-defined productions.

The current corpus contains 4,882 definitions, including 160 productions without a parsed
grammar and two unresolved upstream references. Consuming every specification does not mean
every prose constraint or browser-support rule can be checked by F# types. Runtime rendering,
computed values and browser support remain CSS concerns.

Continue with [composite utilities](css-utilities.md) or [extracting and serving CSS](css-build.md).
