module Ranvier.Css.Utilities.Example.Styles

open Ranvier
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View
open Ranvier.View.Sugar

/// <summary>Per-instance application state; CSS generation never samples these signals.</summary>
type State =
    {
        Busy: Signal<bool>
        Width: Signal<float>
    }

/// <summary>Small viewport condition shared by unrelated utilities.</summary>
let sm = minViewportWidth (Css.rem 40.)
/// <summary>Medium viewport condition shared by unrelated utilities.</summary>
let md = minViewportWidth (Css.rem 48.)
/// <summary>Large viewport condition shared by unrelated utilities.</summary>
let lg = minViewportWidth (Css.rem 64.)

/// <summary>Responsive typography composed using ordinary typed property sugar.</summary>
let bodyText =
    utility {
        Css.fontSize (Css.rem 1.)
        sm { Css.fontSize (Css.rem 1.0625) }
        md { Css.fontSize (Css.rem 1.125) }
        lg { Css.fontSize (Css.rem 1.25) }
    }

/// <summary>Responsive maximum width with a fluid default.</summary>
let contentWidth =
    utility {
        Css.width (Css.percent 100.)
        sm { Css.maxWidth (Css.rem 36.) }
        md { Css.maxWidth (Css.rem 48.) }
        lg { Css.maxWidth (Css.rem 64.) }
    }

/// <summary>Reusable spacing applied as a unit under other conditions.</summary>
let spacious =
    utility {
        Css.padding (Css.rem 2.)
        Css.gap (Css.rem 1.5)
    }

/// <summary>Ordered responsive composition with precompiled busy and hover branches.</summary>
let card =
    utility {
        bodyText
        contentWidth
        Css.padding (Css.rem 1.)
        md { spacious }
        hover { Css.color CssValue.Color.blue }
        whenActive (fun (state: State) -> state.Busy.Value) { Css.opacity (Css.number 0.5) }
        Css.width (fun (state: State) -> Css.px state.Width.Value)
    }

/// <summary>A definition retained in source and removed from output when it is proven unused.</summary>
let unused = utility { Css.color CssValue.Color.purple }

/// <summary>A view that applies its top-level utilities without a stylesheet registry.</summary>
let ``component`` =
    Component.define
        (fun _ initialWidth ->
            {
                Busy = Api.createSignal false
                Width = Api.createSignal initialWidth
            })
        (fun _ -> div (Array.append [| Attrs.attribute ("class", "user-card") |] (styles { card })) { "Responsive composite utilities" })
