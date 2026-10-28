module Ranvier.Css.Spec.ViewTests

open Expecto
open Ranvier
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View

/// <summary>SSR style identity, current reactive values, and emitted asset references.</summary>
let tests =
    testList
        "CSS View integration"
        [
            testCase "SSR renders generated classes and current variable values"
            <| fun _ ->
                let style =
                    Style.create
                        [
                            Style.dynamic "width" (fun (width: float) -> Declaration.create "width" (string width + "px"))
                        ]

                let component =
                    Component.define (fun _ width -> width) (fun t -> t.element (HtmlTag "div") (CssView.apply t.Attrs style) [||])

                let html = Server.render Server.Options.defaults component 42.0
                Expect.stringContains html style.Definition.ClassName "Shared class identity"
                Expect.stringContains html "42px" "SSR current value"
                Expect.stringContains html "style=\"--r-" "Custom-property attribute"
            testCase "SSR asset references preserve dependency order without duplicate links"
            <| fun _ ->
                let shell =
                    CssView.withStylesheets [ "/assets/reset.css"; "/assets/card.css"; "/assets/reset.css" ] Server.DocumentShell.defaults

                Expect.equal shell.Stylesheets [ "/assets/reset.css"; "/assets/card.css" ] "Ordered asset references"
        ]
