module Ranvier.Css.Spec.UtilityViewTests

open System
open Expecto
open Ranvier
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View
open Ranvier.View.Sugar

let private shared = utility { Declaration.create "display" "grid" }

type private Instance =
    {
        Active: Signal<bool>
        Width: Signal<int>
    }

let private attributeSlots (definition: Component<'props>) =
    match definition.Root with
    | ElementNode (_, _, _, slots, _, _, _) ->
        slots
        |> Array.choose (fun slot ->
            match slot.Target with
            | AttributeTarget name -> Some (name, slot)
            | _ -> None)
        |> Map.ofArray
    | _ -> failtest "Expected an element template"

/// <summary>Constructor style attributes, void elements, and tracked utility gates.</summary>
let tests =
    testList
        "Composite CSS view integration"
        [
            testCase "utilities apply through constructor attributes across instance types"
            <| fun _ ->
                let first =
                    Component.define (fun _ (value: int) -> value) (fun _ ->
                        div (styles { shared }) {
                            "before"
                            span () { "middle" }
                            "after"
                        })

                let second =
                    Component.define (fun _ (value: string) -> value) (fun _ -> div (styles { shared }) { "other" })

                let firstHtml = Server.render Server.Options.defaults first 1
                let secondHtml = Server.render Server.Options.defaults second "instance"
                Expect.stringContains firstHtml shared.Definition.ClassName "First instance type"
                Expect.stringContains secondHtml shared.Definition.ClassName "Second instance type"
                Expect.stringContains firstHtml "before<span>middle</span>after" "Yielded styles add no child node"
                Expect.equal (firstHtml.Split("class=\"").Length - 1) 1 "One class writer"
                Expect.equal (firstHtml.Split("style=\"").Length - 1) 1 "One custom-property writer"

                match first.Root with
                | ElementNode (_, attributes, _, slots, _, _, _) ->
                    Expect.contains attributes ("class", shared.Definition.ClassName) "Fixed classes remain static attributes"

                    Expect.isFalse
                        (slots
                         |> Array.exists (fun slot ->
                             match slot.Target with
                             | AttributeTarget "class" -> true
                             | _ -> false))
                        "Fixed classes require no tracked reader"
                | _ -> failtest "Expected an element template"

            testCase "manual classes merge with generated classes in constructor order"
            <| fun _ ->
                let definition =
                    Component.define (fun _ () -> ()) (fun t ->
                        div (
                            Array.concat
                                [
                                    [| t.Attrs.attribute ("class", "manual\tshared\u00a0token repeated") |]
                                    styles { shared }
                                    [| t.Attrs.attribute ("class", "repeated MANUAL manual") |]
                                ]
                        ) {
                            "content"
                        })

                let expected =
                    "manual shared\u00a0token repeated "
                    + shared.Definition.ClassName
                    + " MANUAL"

                let html = Server.render Server.Options.defaults definition ()
                Expect.stringContains html ("class=\"" + expected + "\"") "All tokens merge with ASCII whitespace and ordinal deduplication"
                Expect.equal (html.Split("class=\"").Length - 1) 1 "One authoritative class attribute"

            testCase "ordinary duplicate class attributes retain their existing validation"
            <| fun _ ->
                let definition =
                    Component.define (fun _ () -> ()) (fun t ->
                        div
                            [|
                                t.Attrs.attribute ("class", "first")
                                t.Props.className (fun () -> "second")
                            |] {
                            "content"
                        })

                Expect.throws
                    (fun () ->
                        Server.render Server.Options.defaults definition ()
                        |> ignore)
                    "Class merging requires an explicit composable source"

                let attrs = Builders.Attrs<unit>

                Expect.throwsT<ArgumentException>
                    (fun () ->
                        attrs.composableClass (attrs.attribute ("title", "invalid"))
                        |> ignore)
                    "Composable class parts reject unrelated attributes"

            testCase "application slots track gates and stop reading inactive values"
            <| fun _ ->
                use graph = new Graph ()

                let model =
                    {
                        Active = Signal (graph, false)
                        Width = Signal (graph, 10)
                    }

                let composition =
                    utility {
                        Declaration.create "display" "block"

                        Style.whenActive
                            (fun (instance: Instance) -> instance.Active.Value)
                            [
                                Style.dynamic "width" (fun instance -> Declaration.create "width" (string instance.Width.Value + "px"))
                            ]
                    }

                let definition =
                    Component.define (fun _ (instance: Instance) -> instance) (fun _ -> div (styles { composition }) { "content" })

                let slots = attributeSlots definition

                let read name =
                    slots[name].Read.Invoke(box model)
                    |> unbox<string option>
                    |> Option.defaultValue ""

                let classes = ResizeArray<string>()
                let variables = ResizeArray<string>()
                let classEffect = new Effect (graph, fun () -> classes.Add (read "class"))
                let variableEffect = new Effect (graph, fun () -> variables.Add (read "style"))
                let application = Utility.toStyledReactive composition
                let css = Stylesheet.render application.Definition.Rules
                model.Width.Value <- 20
                Expect.equal variables.Count 1 "Inactive values are not dependencies"
                model.Active.Value <- true
                Expect.notEqual classes[0] classes[classes.Count - 1] "Gate activation changes classes"
                Expect.stringContains variables[variables.Count - 1] "20px" "Activation reads the current value"
                model.Width.Value <- 30
                Expect.stringContains variables[variables.Count - 1] "30px" "Active values are tracked"
                model.Active.Value <- false
                let inactiveCount = variables.Count
                model.Width.Value <- 40
                Expect.equal variables.Count inactiveCount "Deactivation drops the value dependency"
                Expect.equal (Stylesheet.render application.Definition.Rules) css "Signal updates retain static CSS"
                let inactiveHtml = Server.render Server.Options.defaults definition model
                Expect.stringContains inactiveHtml application.Definition.ClassName "SSR includes the root class"
                Expect.isFalse (inactiveHtml.Contains ("40px")) "SSR skips inactive values"
                let classCount, variableCount = classes.Count, variables.Count
                classEffect.Dispose ()
                variableEffect.Dispose ()
                model.Active.Value <- true
                Expect.equal classes.Count classCount "Disposed class effect detaches"
                Expect.equal variables.Count variableCount "Disposed variable effect detaches"

            testCase "reactive manual classes and utility gates share one tracked writer in either order"
            <| fun _ ->
                for manualFirst in [ true; false ] do
                    use graph = new Graph ()
                    let enabled = Signal (graph, false)
                    let label = Signal (graph, "first")

                    let composition =
                        utility {
                            Declaration.create "display" "block"

                            Style.whenActive
                                (fun ((flag, _): Signal<bool> * Signal<string>) -> flag.Value)
                                [ Style.declarations [ Declaration.create "opacity" "0.5" ] ]
                        }

                    let definition =
                        Component.define (fun _ model -> model) (fun t ->
                            let manual =
                                [|
                                    t.Attrs.attribute ("class", fun (_, text: Signal<string>) -> Some text.Value)
                                |]

                            let generated = styles { composition }

                            div (
                                if manualFirst then
                                    Array.append manual generated
                                else
                                    Array.append generated manual
                            ) {
                                "content"
                            })

                    let slots = attributeSlots definition

                    let read () =
                        slots["class"].Read.Invoke(box (enabled, label))
                        |> unbox<string option>
                        |> Option.defaultValue ""

                    let seen = ResizeArray<string>()
                    let effect = new Effect (graph, fun () -> seen.Add (read ()))
                    let rootClass = composition.Definition.ClassName

                    let expected label active =
                        let generated =
                            rootClass
                            + (if active then " " + rootClass + "-g0" else "")

                        if manualFirst then
                            label + " " + generated
                        else
                            generated + " " + label

                    Expect.equal (read ()) (expected "first" false) "Initial classes"
                    label.Value <- "second"
                    Expect.equal seen[seen.Count - 1] (expected "second" false) "Manual class changes preserve generated classes"
                    enabled.Value <- true
                    Expect.equal seen[seen.Count - 1] (expected "second" true) "Gate changes preserve manual classes"
                    let html = Server.render Server.Options.defaults definition (enabled, label)
                    Expect.stringContains html ("class=\"" + expected "second" true + "\"") "SSR uses the same merge"
                    enabled.Value <- false
                    Expect.equal seen[seen.Count - 1] (expected "second" false) "Gate removal preserves manual classes"
                    effect.Dispose ()
                    let disposedCount = seen.Count
                    label.Value <- "third"
                    enabled.Value <- true
                    Expect.equal seen.Count disposedCount "Merged writer detaches on disposal"

            testCase "reflected class properties merge their serialized values with utility attributes"
            <| fun _ ->
                use graph = new Graph ()
                let label = Signal (graph, "current")

                let definition =
                    Component.define (fun _ model -> model) (fun t ->
                        let fixedProperty =
                            t.Props.propertyStatic ((fun _ (_: string) -> ()), "property", PropertyHtml.text "class")

                        let dynamicProperty =
                            t.Props.propertyWith (
                                (fun _ (_: string) -> ()),
                                (fun (text: Signal<string>) -> text.Value),
                                primitiveSame,
                                PropertyHtml.text "class"
                            )

                        div (Array.concat [ [| fixedProperty |]; styles { shared }; [| dynamicProperty |] ]) { "content" })

                let slots = attributeSlots definition

                let read () =
                    slots["class"].Read.Invoke(box label)
                    |> unbox<string option>
                    |> Option.defaultValue ""

                let expected =
                    "property "
                    + shared.Definition.ClassName
                    + " current"

                Expect.equal (read ()) expected "Property values use their serialization encoder"

                Expect.stringContains
                    (Server.render Server.Options.defaults definition label)
                    ("class=\"" + expected + "\"")
                    "SSR includes reflected properties"

                label.Value <- "updated"

                Expect.equal
                    (read ())
                    ("property "
                     + shared.Definition.ClassName
                     + " updated")
                    "Dynamic property values remain live"

            testCase "omitted fixed reflected class sources remain omitted after composition"
            <| fun _ ->
                let make value =
                    Component.define (fun _ () -> ()) (fun t ->
                        let part =
                            t.Props.propertyStatic ((fun _ (_: string option) -> ()), value, PropertyHtml.optional "class")

                        div [| t.Attrs.composableClass part; part |] { "content" })

                let omitted = Server.render Server.Options.defaults (make None) ()
                Expect.isFalse (omitted.Contains ("class=")) "All omitted contributors preserve absence of the class attribute"
                let empty = Server.render Server.Options.defaults (make (Some "")) ()
                Expect.stringContains empty "class=\"\"" "Explicit empty class attributes remain present"

            testCase "void input and image constructors accept utility attributes"
            <| fun _ ->
                let definition =
                    Component.define (fun _ () -> ()) (fun _ ->
                        div () {
                            input (Array.append [| Builders.Attrs<unit>.attribute("class", "manual") |] (styles { shared }))
                            img (Array.append (styles { shared }) [| Builders.Attrs<unit>.attribute("class", "manual") |])
                        })

                let html = Server.render Server.Options.defaults definition ()

                Expect.stringContains
                    html
                    ("<input class=\"manual "
                     + shared.Definition.ClassName
                     + "\"")
                    "Input receives the utility class"

                Expect.stringContains
                    html
                    ("<img class=\""
                     + shared.Definition.ClassName
                     + " manual\"")
                    "Image receives the utility class"

            testCase "style arrays compose with other attributes and child loops"
            <| fun _ ->
                let definition =
                    Component.define (fun _ () -> ()) (fun t ->
                        div (Array.append [| t.Attrs.attribute ("data-fixture", "utilities") |] (styles { shared })) {
                            for label in [ "first"; "second" ] do
                                span () { label }
                        })

                let html = Server.render Server.Options.defaults definition ()
                Expect.stringContains html "data-fixture=\"utilities\"" "Other constructor attributes are retained"
                Expect.stringContains html shared.Definition.ClassName "Style attributes are retained"
                Expect.stringContains html "<span>first</span><span>second</span>" "Loop child order"
        ]
