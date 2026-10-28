module Ranvier.Css.Example.Styles

open Ranvier
open Ranvier.View
open Ranvier.Css
open Ranvier.Css.Build
open Ranvier.Css.View

/// <summary>Static component rules with a per-instance reactive width slot.</summary>
let progress: Styled<Signal<float>> =
    styled {
        Css.width (fun (signal: Signal<float>) -> Css.px signal.Value)
        Style.hover [ Style.declarations [ Css.width (Css.px 320.0) ] ]
        Style.media "(width >= 768px)" [ Style.declarations [ Css.width (Css.px 640.0) ] ]
    }

/// <summary>A template whose width signal is tracked by the style variable binding.</summary>
let component =
    Component.define (fun _ initial -> Api.createSignal initial) (fun t ->
        t.element (HtmlTag "div") (CssView.apply t.Attrs progress) [| t.text "Reactive CSS width" |])

/// <summary>Explicit stylesheet dependencies evaluated by the extraction entry point.</summary>
let resources =
    let reset =
        {
            Name = "reset"
            Dependencies = []
            Styles = []
            Rules = [ Rule.style (Selector.element "body") [ Declaration.create "margin" "0" ] ]
        }

    [
        {
            Name = "progress"
            Dependencies = [ reset ]
            Styles = [ progress.Definition ]
            Rules =
                [
                    Rule.layer
                        (Some "components")
                        [
                            Rule.keyframes
                                "appear"
                                [
                                    "from", [ Declaration.create "opacity" "0" ]
                                    "to", [ Declaration.create "opacity" "1" ]
                                ]
                        ]
                ]
        }
    ]
