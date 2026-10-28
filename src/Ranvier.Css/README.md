# Specification-driven CSS for Ranvier

`Ranvier.Css` provides portable values, ordered declarations, a stylesheet tree, deterministic component classes and reactive custom-property slots. `Ranvier.Css.Bindings` contains the ordinary generated F# source for every definition in the pinned CSS corpus. `Ranvier.Css.View` connects those styles to Ranvier templates. `Ranvier.Css.Build` is the separate .NET asset compiler.

Install `Ranvier.Css.Bindings` and `Ranvier.Css.View` from NuGet with `--prerelease`, or reference their projects from a checkout. Use the matching runtime dependency selected by the bindings package. The view adapter requires `Ranvier.View` `0.1.0-alpha.2` or later for class contribution merging. The runtime, bindings and view adapter include Fable sources. `Ranvier.Css.Build` is .NET-only. Applications compile their normal F# references; the specification generator is a library-maintenance tool, not an application build prerequisite.

Read the [typed CSS guide](https://ranvier-org.github.io/Ranvier.Css/view/css/), [composite utility examples](https://ranvier-org.github.io/Ranvier.Css/view/css-utilities/) and [asset extraction guide](https://ranvier-org.github.io/Ranvier.Css/view/css-build/).

## Component styles

```fsharp
open Ranvier
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View

let progress : Styled<Signal<float>> =
    styled {
        Css.width (fun (signal: Signal<float>) -> Css.px signal.Value)
        Css.display CssValue.Display.block
        Style.hover [Style.declarations [Css.width (Css.px 320.0)]]
        Style.media "(width >= 768px)" [Style.declarations [Css.width (Css.px 640.0)]]
    }

let component =
    Component.define
        (fun _ initial -> Api.createSignal initial)
        (fun t ->
            t.element (HtmlTag "div")
                (CssView.apply t.Attrs progress)
                [|t.text "Reactive CSS width"|])
```

The class name depends on the static CSS structure, never on signal values, object identities or registration order. Dynamic properties become static `var(--r-...)` references. Their instance readers use Ranvier's existing tracked template attribute binding. A signal update changes the element's custom properties; it does not create rules or class names. Disposal releases the existing component binding. SSR writes the current values using the same class identity. The DOM acceptance checks run real generated property helpers under both commit modes; a Chromium gate also checks native computed values and inline CSS-wide keyword semantics.

The default `Css` type lives directly in `Ranvier.Css`. Each property has overloaded static methods: `Css.width (Css.px 42.0)` creates a declaration, while `Css.width (fun state -> Css.px state.Width.Value)` creates a tracked style node. Shared primitives include `px`, `rem`, `percent`, `number`, `integer`, `deg` and `ms`. Discover keywords under semantic owners, such as `CssValue.Width.auto`, `CssValue.Display.block` and `CssValue.Color.red`. Grammar-derived interfaces prevent an angle, unrelated keyword or raw string from being accepted as width. The shared runtime checks numeric bounds and grammar construction. Structural productions such as required pairs are not flattened into single-value overloads. Reader parameters may need an instance type annotation, as in the example.

Structured grammar constructors live under `CssValue`, alongside their values. For example, `CssValue.Border.create (Some (Css.px 2.)) (Some CssValue.Border.Style.solid) None` supplies the width and style roles and omits color. Each meaningful owner exposes a `Value` interface. A conditional can set a common category on its first branch, for example `if usePixels then Css.px 42. :> CssValue.LengthPercentage.Value else Css.percent 50.`. Later compatible branches coerce to that interface. The default generated storage remains under `Ranvier.Css.Generated`; consumers open `Ranvier.Css` for authoring.

Arithmetic and math functions retain unevaluated CSS trees. Finish them with a measured bridge such as `CssValue.Length.calc (Css.px 4. * 2.)` before assigning a property. Use `CssValue.LengthPercentage.calc` for expressions with a length percentage basis, `CssValue.Angle.calc` for angles, and `CssValue.Number.calc` or `CssValue.Integer.calc` for scalar results. These bridges also work in reactive readers. `CssValue.render` returns a typed value's serialized CSS text.

CSS-wide property keywords (`initial`, `inherit`, `unset`, `revert`, `revert-layer`, `revert-rule`) work as static declarations, for example `Css.color Css.initial`. F# reserved keyword names such as `inherit` require double-backtick escaping. For changing CSS-wide values, use `CssView.inlineStyle` with a reader returning these declarations. The sugar deliberately supplies no variable-backed reader overload for these keyword markers: substituting them through a variable changes their cascade meaning. The generated keyword registry comes from the current `all` property specification.

## Authoring methods

Use plain declaration lists, `declarations { ... }`, plain `StyleNode` lists passed to `Style.create`, or `styled { ... }`. `styled` accepts both static property declarations and reader style nodes from the unified helpers. `Style.declarations` groups static declarations inside nested node lists. Loops in these builders expand the static definition; changing collections belong in application state and variable readers.

For a global stylesheet use `Rule.style`, `Rule.media`, `Rule.supports`, `Rule.container`, `Rule.layer`, `Rule.keyframes`, `Rule.nested`, `Rule.descriptors`, and `Rule.statement`. `stylesheet { ... }` yields ordered rules. The tree preserves repeated property fallbacks, declaration importance and rule order. Generic at-rules and explicitly unchecked selector/token factories keep emerging syntax expressible. These constructors do not constitute a general CSS text parser.

## Composite utilities

Top-level utilities compose typed declarations and other utilities in authored order. Reusable conditions wrap either:

```fsharp
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View.Sugar

let md = minViewportWidth (Css.rem 48.)
let spacious = utility {
    Css.padding (Css.rem 2.)
    Css.gap (Css.rem 1.5)
}
let card = utility {
    Css.fontSize (Css.rem 1.)
    md { spacious }
    hover { Css.color CssValue.Color.blue }
}

// Within a component's view:
// div (styles { card }) { "Content" }
// input (styles { card })
```

`media`, `supports`, `container`, `hover`, `focus`, `focusWithin`, `descendant`, `pseudoElement`, `selectorScope` and `theme` also create condition builders. `minViewportWidth` and `maxViewportWidth` validate fixed nonnegative length thresholds. Nested conditions mean conjunction. Minimum-width ranges overlap; normal CSS cascade and source order determine the winning declarations. Selector scopes are explicit, and descendant selectors must retain the receiving root.

`whenActive (fun instance -> instance.Enabled.Value) { ... }` gates an entire composition. Static utilities are nongeneric values reusable across component instance types; application predicates and generated property readers infer a typed `Utility<'instance>`. Runtime gates use precompiled zero-specificity root classes. Inactive branches skip nested readers and release their reactive dependencies. Value readers retain the existing typed custom-property behavior.

`styles { ... }` returns element attribute parts, supporting regular and void constructors. Generated classes merge with declared static/reactive user classes. A composed application's private class identity reflects declaration order, so reversing conflicting utilities reverses equal-specificity winners without relying on HTML class token order.

The [project compiler](../../tools/Ranvier.Css.Compile/README.md) discovers applications and emits existing asset layouts without executing application initializers or instance readers. It interprets a documented deterministic F# subset and reports unsupported used expressions. See the [complete example](../../examples/Ranvier.Css.Utilities.Example/README.md).

## Typed custom properties

Use `Css.variable<CssValue.Width.Value> "--width"` for a generic reference retaining the destination grammar category. `CssValue.Variable.withFallback` adds a value in that category, and `CssValue.Variable.declaration` assigns it. `CssValue.variable` and `CssValue.var` provide the same reference factory. Generic references enter property sinks directly.

For arithmetic, use a measured numeric reference instead:

```fsharp
let gap = CssValue.Length.variableWithFallback "--gap" (Css.px 8.)
let spaced = CssValue.Length.calc (gap + Css.px 2.)
let width = Css.width spaced
```

`CssValue.Length.variable` omits the fallback. Measured references validate dashed names, preserve their calculation trees and require a fallback with the same dimension and percentage hint. Neither kind of reference reads the browser's current value or registers a property. `Rule.register` emits `@property` with a supported registration syntax, inheritance flag and initial value. The caller must ensure the initial value meets the registration's computational-independence and grammar requirements.

## View options

- `CssView.apply attrs style` applies a generated class and current variables.
- `CssView.applyMany attrs styles` combines several styles with one writer for each attribute.
- `CssView.applyWithInline attrs extraClasses styles reader` combines existing class names, styles and additional inline declarations.
- `CssView.className` and `CssView.variables` expose those parts separately.
- `CssView.inlineStyle attrs reader` supports fully dynamic inline declaration lists, including CSS-wide keywords.
- `CssView.withStylesheets urls shell` appends ordered, deduplicated asset links to an SSR document shell.

Compose style parts through these combined helpers rather than attach competing writers for the same `class` or `style` attribute. Signal and memo reads belong inside readers; reading their values while defining a style captures a static snapshot.

See [asset compilation](../Ranvier.Css.Build/README.md) and the [runnable example](../../examples/Ranvier.Css.Example/README.md).

## Specification boundaries

The model retains 4,882 definitions from 122 locked raw extracts plus the canonical Webref package. Current definitions prefer newer same-series specifications. Compact public owners exclude archived obsolete scopes while current deprecated definitions remain available; their provenance stays in the manifest. The manifest records 160 prose-defined productions and two unresolved upstream `anchored-feature` references. Explicit `unsafeFromTokens` constructors remain available for grammar gaps. Grammar typing does not infer every prose constraint, computed-value rule, property-specific identifier exclusion or browser support policy. Typed values remain distinct dimensions; a normal width constructor rejects an angle at compile time.
