---
title: Composite CSS utilities
order: 17
---

A utility is a top-level F# declaration containing typed properties, other utilities and
conditions. Components apply utilities with `styles { ... }` in their constructor attributes.
Start with [typed CSS](css.md) for package setup and value helpers.

## Share sm, md and lg across utilities

Define viewport conditions once and reuse them for text, width or whole compositions:

```fsharp
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View
open Ranvier.View.Sugar

let sm = minViewportWidth (Css.rem 40.)
let md = minViewportWidth (Css.rem 48.)
let lg = minViewportWidth (Css.rem 64.)

let bodyText = utility {
    Css.fontSize (Css.rem 1.)
    sm { Css.fontSize (Css.rem 1.0625) }
    md { Css.fontSize (Css.rem 1.125) }
    lg { Css.fontSize (Css.rem 1.25) }
}

let contentWidth = utility {
    Css.width (Css.percent 100.)
    sm { Css.maxWidth (Css.rem 36.) }
    md { Css.maxWidth (Css.rem 48.) }
    lg { Css.maxWidth (Css.rem 64.) }
}

let spacious = utility {
    Css.padding (Css.rem 2.)
    Css.gap (Css.rem 1.5)
}

let card = utility {
    bodyText
    contentWidth
    Css.padding (Css.rem 1.)
    md { spacious }
    hover { Css.color CssValue.Color.blue }
}

// Inside a component's view:
// div (styles { card }) { "Responsive card" }
```

These conditions emit minimum-width media queries. At a large viewport, all three thresholds
may match; later declarations win when their specificity and cascade context are equal.
`maxViewportWidth` defines a maximum instead. Both helpers require a fixed, nonnegative length.

## Apply conditions to complete compositions

Any condition can wrap multiple declarations or utilities:

```fsharp
let interactiveCard = utility {
    card
    md {
        hover {
            Css.opacity (Css.number 0.8)
        }
    }
    focusWithin { Css.color CssValue.Color.blue }
}
```

Nested conditions mean **and**: the opacity above applies only when the viewport matches `md`
and the root element is hovered. Browser conditions are handled by CSS; viewport and hover
changes do not run application predicates.

Available condition builders include `media`, `supports`, `container`, `hover`, `focus`,
`focusWithin`, `descendant`, `pseudoElement`, `selectorScope` and `theme`. Selector/query strings
are explicit CSS syntax rather than a typed selector language. Descendant selectors retain
the receiving root; top-level comma-separated descendants are rejected to prevent selectors
escaping that root.

## Application conditions and changing values

Use `whenActive` for a runtime Boolean, and property readers for continuously changing values:

```fsharp
open Ranvier

type State = {
    Busy: Signal<bool>
    Width: Signal<float>
}

let liveCard = utility {
    card
    whenActive (fun (state: State) -> state.Busy.Value) {
        Css.opacity (Css.number 0.5)
    }
    Css.width (fun (state: State) -> Css.px state.Width.Value)
}

let cardComponent =
    Component.define
        (fun _ initialWidth -> {
            Busy = Api.createSignal false
            Width = Api.createSignal initialWidth
        })
        (fun _ ->
            div (Array.append [| Attrs.attribute ("class", "user-card") |] (styles { liveCard })) {
                "Responsive composite utilities"
            })
```

Both branches' CSS is compiled before the application runs. A `Busy` update toggles a generated
gate class; a `Width` update writes a CSS variable. Generated gate selectors use zero-specificity
conditions so the application predicate does not increase specificity. User classes survive
both updates.

Readers inside an inactive application branch are not sampled or tracked. When the branch
becomes active they read current values; when it becomes inactive those dependencies are
released. Nested application conditions short-circuit under inactive parents.

Static utilities are reusable across instance types. Adding a predicate or property reader
produces a typed `Utility<State>` and keeps state compatibility checked at the application.

## Composition order

Utilities expand in authored order. In `utility { first; second }`, conflicting declarations
from `second` follow those from `first`. Reversing their order reverses the winner where normal
CSS cascade rules otherwise tie. Repeated declarations, fallback values and importance remain
ordered rather than being collapsed into a property map.

Put shared definitions at the top level so the build compiler can follow them. Ordinary
deterministic F# helpers can construct utilities; the compiler supports a bounded subset and
reports unsupported used expressions. Runtime state belongs in predicates and readers, not
in decisions that change a static utility's structure.

The [complete runnable example](https://github.com/ranvier-org/Ranvier.Css/tree/main/examples/Ranvier.Css.Utilities.Example)
uses these breakpoints, responsive text and width, composition, a busy gate, a typed width
reader and a manual class. Continue with [extracting and serving CSS](css-build.md).
