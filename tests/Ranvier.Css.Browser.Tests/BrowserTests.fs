module Ranvier.Css.BrowserTests

open Fable.Core
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Dom.Scheduling
open Ranvier.View.Sugar

[<Emit("document.createElement('div')")>]
let private createHost () : HTMLElement = jsNative

[<Emit("document.body.appendChild($0)")>]
let private attach (host: HTMLElement) : unit = jsNative

[<Emit("$0.remove()")>]
let private remove (host: HTMLElement) : unit = jsNative

[<Emit("getComputedStyle($0).width")>]
let private computedWidth (element: HTMLElement) : string = jsNative

[<Emit("getComputedStyle($0).color")>]
let private computedColor (element: HTMLElement) : string = jsNative

[<Emit("getComputedStyle(document.documentElement).color")>]
let private initialColor () : string = jsNative

[<Emit("$0.style.color = 'rgb(255, 0, 0)'")>]
let private red (element: HTMLElement) : unit = jsNative

[<Emit("!document.defaultView.navigator.userAgent.includes('jsdom')")>]
let private browserEngine () : bool = jsNative

[<Emit("$0.querySelector($1)")>]
let private find (host: HTMLElement) (selector: string) : HTMLElement = jsNative

[<Emit("$0.style.getPropertyValue($1)")>]
let private variableValue (element: HTMLElement) (name: string) : string = jsNative

[<Emit("$0.getAttribute('class')")>]
let private classes (element: HTMLElement) : string = jsNative

[<Emit("document.querySelectorAll('style').length")>]
let private styleCount () : int = jsNative

type private UtilityState =
    {
        Enabled: Signal<bool>
        Width: Signal<float>
        Classes: Signal<string>
    }

let private gatedApplication onGateRead onValueRead =
    utility {
        Css.width (Css.px 12.)

        whenActive (fun (state: UtilityState) ->
            onGateRead ()
            state.Enabled.Value) {
            Css.width (fun (state: UtilityState) ->
                onValueRead ()
                Css.px state.Width.Value)
        }
    }

/// <summary>Static stylesheet fixture loaded before mounting; dynamic readers are not evaluated.</summary>
let cssText () =
    let fixture: Styled<unit> =
        Style.create [ Style.dynamic "width" (fun () -> Declaration.create "width" "0px") ]

    let application = gatedApplication ignore ignore

    Stylesheet.render (
        fixture.Definition.Rules
        @ application.Definition.Rules
    )

/// <summary>Executes a real DOM mount, reactive update, and disposal under both template commit modes.</summary>
let run () =
    let calculatedWidth =
        Css.width (CssValue.LengthPercentage.calc (Css.percent 100. - Css.rem 2.))
        |> Declaration.render

    if calculatedWidth <> "width: calc(100% - 2rem);" then
        failwith "Portable expression serialization changed."

    let minimum =
        Css.width (CssValue.LengthPercentage.calc (Css.min (Css.percent 100., Css.px 960.)))
        |> Declaration.render

    if minimum <> "width: min(100%, 960px);" then
        failwith "Portable mixed-function typing changed."

    if CssValue.render CssValue.Color.red <> "red" then
        failwith "Keyword canonical storage changed."

    let canonicalAngle =
        Css.rotate (CssValue.Angle.calc (Css.deg 30. + Css.deg 15.))
        |> Declaration.render

    if canonicalAngle <> "rotate: calc(30deg + 15deg);" then
        failwith "Portable canonical angle bridge changed."

    for actual, expected in
        [
            CssValue.render (CssValue.Number.calc (Css.number 1. + Css.number 2.)), "calc(1 + 2)"
            CssValue.render (CssValue.Integer.calc (Css.number 1.5)), "calc(1.5)"
            CssValue.render (CssValue.LengthPercentage.calc (Css.px 10. + Css.percent 50.)), "calc(10px + 50%)"
        ] do
        if actual <> expected then
            failwith "Portable canonical numeric value bridge changed."

    let gap = CssValue.Length.variableWithFallback "--portable-gap" (Css.px 8.)

    let reference =
        Css.width (CssValue.Length.calc (gap + Css.px 2.))
        |> Declaration.render

    if
        reference
        <> "width: calc(var(--portable-gap, 8px) + 2px);"
    then
        failwithf "Portable variable fallback changed: %s" reference

    for number, expected in
        [
            0.0, "0"
            -0.0, "0"
            0.5, "0.5"
            1e-6, "0.000001"
            1e-7, "1e-7"
            1e20, "100000000000000000000"
            1e21, "1e21"
            System.Double.Epsilon, "5e-324"
        ] do
        if
            (Generated.number number "" |> Value.render)
            <> expected
        then
            failwithf "Nonportable number %A" number

    for mode in
        [
            SynchronousCommits
            QueuedCommits (CollectAndContinue, fun action -> action ())
        ] do
        use graph = new Graph ()
        let host = createHost ()
        attach host
        let mutable signal: Signal<float> = Unchecked.defaultof<_>
        let mutable reads = 0

        let style =
            styled {
                yield
                    Css.width (fun (current: Signal<float>) ->
                        reads <- reads + 1
                        CssValue.Length.calc (Css.px current.Value + Css.px 2.))
            }

        let definition =
            Component.define
                (fun _ () ->
                    signal <- Api.createSignal 42.0
                    signal)
                (fun t -> t.element (HtmlTag "div") (CssView.apply t.Attrs style) [||])

        let before = Stylesheet.render style.Definition.Rules
        let sheets = styleCount ()

        let mounted =
            Mounting.mountWith mode graph None (fun errors -> failwithf "%A" errors) host None (fun context ->
                Program.run (Component.render definition ()) context.Backend)

        mounted.Flush ()
        let element = find host ("." + style.Definition.ClassName)
        let variable = "--" + style.Definition.ClassName + "-1"

        if
            variableValue element variable
            <> "calc(42px + 2px)"
        then
            failwith "Initial CSS variable is missing."

        if
            browserEngine ()
            && computedWidth element <> "44px"
        then
            failwith "Native CSS did not resolve the initial variable."

        graph.Run (fun () -> signal.Value <- 84.0)
        mounted.Flush ()

        if
            variableValue element variable
            <> "calc(84px + 2px)"
        then
            failwith "Signal update did not update the CSS variable."

        if
            browserEngine ()
            && computedWidth element <> "86px"
        then
            failwith "Native CSS did not resolve the updated variable."

        if classes element <> style.Definition.ClassName then
            failwith "Signal update changed class identity."

        if
            Stylesheet.render style.Definition.Rules <> before
            || styleCount () <> sheets
        then
            failwith "Reactive update changed stylesheet rules."

        mounted.Dispose ()
        let disposedReads = reads
        graph.Run (fun () -> signal.Value <- 126.0)

        if reads <> disposedReads then
            failwith "Disposed CSS binding still reads the signal."

        remove host
        let inlineHost = createHost ()
        attach inlineHost
        red inlineHost
        let mutable wideSignal: Signal<bool> = Unchecked.defaultof<_>

        let inlineComponent =
            Component.define
                (fun _ () ->
                    wideSignal <- Api.createSignal true
                    wideSignal)
                (fun t ->
                    t.element
                        (HtmlTag "div")
                        [|
                            CssView.inlineStyle t.Attrs (fun (signal: Signal<bool>) ->
                                [
                                    if signal.Value then
                                        Css.color CssValue.initial
                                    else
                                        Css.color CssValue.inherit'
                                ])
                        |]
                        [||])

        let inlineMount =
            Mounting.mountWith mode graph None (fun errors -> failwithf "%A" errors) inlineHost None (fun context ->
                Program.run (Component.render inlineComponent ()) context.Backend)

        inlineMount.Flush ()
        let inlineElement = find inlineHost "div"

        if
            browserEngine ()
            && computedColor inlineElement <> initialColor ()
        then
            failwith "Inline initial keyword inherited the parent color."

        graph.Run (fun () -> wideSignal.Value <- false)
        inlineMount.Flush ()

        if
            browserEngine ()
            && computedColor inlineElement
               <> computedColor inlineHost
        then
            failwith "Inline inherit keyword did not inherit the parent."

        inlineMount.Dispose ()
        remove inlineHost

        let utilityHost = createHost ()
        attach utilityHost
        let mutable state: UtilityState = Unchecked.defaultof<_>
        let mutable gateReads = 0
        let mutable valueReads = 0

        let application =
            gatedApplication (fun () -> gateReads <- gateReads + 1) (fun () -> valueReads <- valueReads + 1)

        if gateReads <> 0 || valueReads <> 0 then
            failwith "Compiling utility alternatives evaluated application state."

        let utilityComponent =
            Component.define
                (fun _ () ->
                    state <-
                        {
                            Enabled = Api.createSignal false
                            Width = Api.createSignal 24.
                            Classes = Api.createSignal "manual"
                        }

                    state)
                (fun _ ->
                    div (
                        Array.append
                            [|
                                Builders.Attrs<UtilityState>.attribute("class", fun state -> Some state.Classes.Value)
                            |]
                            (styles { application })
                    ) {
                        "Conditional utility"
                    })

        let utilityRules = Stylesheet.render application.Definition.Rules
        let utilitySheets = styleCount ()

        let utilityMount =
            Mounting.mountWith mode graph None (fun errors -> failwithf "%A" errors) utilityHost None (fun context ->
                Program.run (Component.render utilityComponent ()) context.Backend)

        utilityMount.Flush ()
        let utilityElement = find utilityHost ("." + application.Definition.ClassName)
        let utilityVariable = "--" + application.Definition.ClassName + "-1"

        if
            classes utilityElement
            <> "manual " + application.Definition.ClassName
        then
            failwith "An inactive utility retained an application gate class."

        if
            variableValue utilityElement utilityVariable <> ""
            || valueReads <> 0
        then
            failwith "An inactive utility evaluated or emitted its value reader."

        if
            browserEngine ()
            && computedWidth utilityElement <> "12px"
        then
            failwith "An inactive utility did not retain its fixed fallback width."

        graph.Run (fun () -> state.Width.Value <- 36.)
        utilityMount.Flush ()

        if valueReads <> 0 then
            failwith "An inactive utility subscribed to its value signal."

        graph.Run (fun () -> state.Enabled.Value <- true)
        utilityMount.Flush ()

        if
            classes utilityElement
            <> "manual "
               + application.Definition.ClassName
               + " "
               + application.Definition.ClassName
               + "-g0"
        then
            failwith "Utility activation did not select its precompiled gate class."

        if
            variableValue utilityElement utilityVariable
            <> "36px"
        then
            failwith "Utility activation did not read the current value."

        if
            browserEngine ()
            && computedWidth utilityElement <> "36px"
        then
            failwith "Native CSS did not resolve the gated utility width."

        graph.Run (fun () -> state.Width.Value <- 48.)
        utilityMount.Flush ()

        if
            variableValue utilityElement utilityVariable
            <> "48px"
        then
            failwith "An active utility did not track its value signal."

        if
            browserEngine ()
            && computedWidth utilityElement <> "48px"
        then
            failwith "Native CSS did not resolve the updated gated width."

        graph.Run (fun () -> state.Classes.Value <- "updated manual")
        utilityMount.Flush ()

        if
            classes utilityElement
            <> "updated manual "
               + application.Definition.ClassName
               + " "
               + application.Definition.ClassName
               + "-g0"
        then
            failwith "Reactive manual classes replaced the utility classes."

        graph.Run (fun () -> state.Enabled.Value <- false)
        utilityMount.Flush ()

        if
            classes utilityElement
            <> "updated manual "
               + application.Definition.ClassName
            || variableValue utilityElement utilityVariable <> ""
        then
            failwith "Utility deactivation retained a gate class or variable."

        if
            browserEngine ()
            && computedWidth utilityElement <> "12px"
        then
            failwith "Native CSS did not restore the fixed utility width."

        let inactiveReads = valueReads
        graph.Run (fun () -> state.Width.Value <- 60.)
        utilityMount.Flush ()

        if valueReads <> inactiveReads then
            failwith "Utility deactivation retained its value subscription."

        if
            styleCount () <> utilitySheets
            || Stylesheet.render application.Definition.Rules
               <> utilityRules
        then
            failwith "Conditional utility updates generated new stylesheet rules."

        utilityMount.Dispose ()
        let disposedGateReads, disposedValueReads = gateReads, valueReads

        graph.Run (fun () ->
            state.Enabled.Value <- true
            state.Width.Value <- 72.
            state.Classes.Value <- "disposed")

        if
            gateReads <> disposedGateReads
            || valueReads <> disposedValueReads
        then
            failwith "Disposed utility bindings retained reactive subscriptions."

        remove utilityHost

    Identity.hash "portable-css-identity"
