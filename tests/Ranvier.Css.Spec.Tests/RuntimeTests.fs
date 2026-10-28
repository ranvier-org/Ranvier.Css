module Ranvier.Css.Spec.RuntimeTests

open Expecto
open Ranvier.Css

/// <summary>Portable stylesheet, component identity, and reactive custom-property contracts.</summary>
let tests =
    testList
        "CSS compiler runtime"
        [
            testCase "numeric token metadata is retained through grammar copies"
            <| fun _ ->
                let literal: Value<unit> = Generated.number 12.5 "px"
                Expect.equal (RuntimeApi.tryNumericValue literal) (Some (12.5, "px")) "Literal"
                Expect.equal (RuntimeApi.tryNumericValue (Generated.copy literal: Value<obj>)) (Some (12.5, "px")) "Copy"

                for text in [ "12px"; "calc(2px + 3px)"; "var(--gap)"; "auto" ] do
                    let value: Value<unit> = Value.unsafeFromTokens text
                    Expect.isNone (RuntimeApi.tryNumericValue value) "Text has no numeric-token provenance"
            testCase "generated dynamic slots use their spec-derived keyword registry"
            <| fun _ ->
                let style =
                    Style.create
                        [
                            Style.dynamicWithKeywords [ "future-wide" ] "color" (fun () -> Declaration.create "color" "future-wide")
                        ]

                Expect.throwsT<System.ArgumentException> (fun () -> Style.renderVariables () style |> ignore) "Future keywords keep cascade semantics"
            testCase "computation expressions preserve declarations, component nodes and stylesheet rules"
            <| fun _ ->
                let values =
                    declarations {
                        for value in [ "block"; "grid" ] do
                            yield Declaration.create "display" value
                    }

                let style: Styled<int> =
                    styled {
                        yield! [ Style.declarations values ]
                        yield Style.dynamic "width" (fun n -> Declaration.create "width" (string n + "px"))
                    }

                let rules =
                    stylesheet {
                        yield! style.Definition.Rules
                        yield Rule.media "print" []
                    }

                Expect.stringContains (Stylesheet.render rules) "display: block; display: grid;" "Declaration loop order"
                Expect.stringContains (Style.renderVariables 42 style) "42px" "Reader retained through builder"
            testCase "CSS-wide reactive values require explicit inline binding"
            <| fun _ ->
                let style =
                    Style.create [ Style.dynamic "color" (fun keyword -> Declaration.create "color" keyword) ]

                for keyword in
                    [
                        "initial"
                        "inherit"
                        "unset"
                        "revert"
                        "revert-layer"
                        " /* comment */ INHERIT "
                        "\\69 nitial"
                    ] do
                    Expect.throwsT<System.ArgumentException> (fun () -> Style.renderVariables keyword style |> ignore) "CSS-wide semantics"
            testCase "numeric tokens have portable decimal and exponent spelling"
            <| fun _ ->
                let cases =
                    [
                        0.0, "0"
                        -0.0, "0"
                        0.5, "0.5"
                        1e-6, "0.000001"
                        1e-7, "1e-7"
                        1e20, "100000000000000000000"
                        1e21, "1e21"
                        System.Double.Epsilon, "5e-324"
                    ]

                for number, expected in cases do
                    Expect.equal (Generated.number number "" |> Value.render) expected "Portable number"
            testCase "ordered declarations preserve fallbacks and importance"
            <| fun _ ->
                let rule =
                    Rule.style
                        (Selector.className "card")
                        [
                            Declaration.create "display" "block"
                            Declaration.create "display" "grid"
                            |> Declaration.important
                        ]

                Expect.equal (Stylesheet.render [ rule ]) ".card { display: block; display: grid !important; }\n" "Cascade order"
            testCase "identical component definitions have stable automatic names"
            <| fun _ ->
                let make () : Styled<unit> =
                    Style.create [ Style.declarations [ Declaration.create "display" "grid" ] ]

                let first, second = make (), make ()
                Expect.equal first.Definition.ClassName second.Definition.ClassName "Content identity"
                Expect.isTrue (first.Definition.ClassName.StartsWith "r-") "Automatic class"
                Expect.stringContains (Stylesheet.render first.Definition.Rules) ("." + first.Definition.ClassName) "Rule selector"
            testCase "reactive values change variables while static CSS and identity stay fixed"
            <| fun _ ->
                let definition =
                    Style.create
                        [
                            Style.dynamic "width" (fun n -> Declaration.create "width" (string n + "px"))
                        ]

                let before = Stylesheet.render definition.Definition.Rules

                let first, second =
                    Style.renderVariables 10 definition, Style.renderVariables 20 definition

                Expect.notEqual first second "Different instance values"
                Expect.stringContains first "10px" "First value"
                Expect.stringContains second "20px" "Updated value"
                Expect.equal (Stylesheet.render definition.Definition.Rules) before "No new rules"
                Expect.stringContains before "var(--r-" "Static variable reference"
            testCase "nested conditions and layers keep source order"
            <| fun _ ->
                let definition: Styled<unit> =
                    Style.create
                        [
                            Style.declarations [ Declaration.create "display" "grid" ]
                            Style.hover [ Style.declarations [ Declaration.create "opacity" "0.5" ] ]
                            Style.media "(width >= 768px)" [ Style.declarations [ Declaration.create "display" "flex" ] ]
                        ]

                let css =
                    Stylesheet.render [ Rule.layer (Some "components") definition.Definition.Rules ]

                Expect.stringContains css ":hover" "Pseudo selector"
                Expect.stringContains css "@media (width >= 768px)" "Media rule"
                Expect.stringContains css "@layer components" "Layer"
            testCase "declared typed variables serialize fallback and bindings"
            <| fun _ ->
                let variable: CustomProperty<obj> = CustomProperty.create "--progress"
                let initial: Value<obj> = Generated.number 0.5 ""

                Expect.equal
                    (CustomProperty.referenceWithFallback variable initial
                     |> Value.render)
                    "var(--progress, 0.5)"
                    "Fallback"

                Expect.equal
                    (CustomProperty.declaration variable initial
                     |> Declaration.render)
                    "--progress: 0.5;"
                    "Binding"
            testCase "dynamic property readers cannot change the declared property"
            <| fun _ ->
                let definition =
                    Style.create [ Style.dynamic "width" (fun () -> Declaration.create "height" "1px") ]

                Expect.throwsT<System.ArgumentException> (fun () -> Style.renderVariables () definition |> ignore) "Reader identity"
        ]
