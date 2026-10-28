module Ranvier.Css.Spec.UtilityTests

open System
open Expecto
open Ranvier.Css

let private declaration name value =
    Declaration.create name value

let private css (value: Utility) =
    Stylesheet.render value.Definition.Rules

let private length value : Value<unit> =
    Generated.number value "rem"

let private sm = minViewportWidth (length 40.)
let private md = minViewportWidth (length 48.)
let private lg = minViewportWidth (length 64.)

let private bodyText =
    utility {
        declaration "font-size" "1rem"
        md { declaration "font-size" "1.125rem" }
        lg { declaration "font-size" "1.25rem" }
    }

let private contentWidth =
    utility {
        declaration "width" "100%"
        sm { declaration "max-width" "36rem" }
        md { declaration "max-width" "48rem" }
    }

let private spacious =
    utility {
        declaration "padding" "2rem"
        declaration "gap" "1.5rem"
    }

let private card =
    utility {
        bodyText
        contentWidth
        declaration "padding" "1rem"
        md { spacious }
    }

/// <summary>Reusable utility authoring, condition nesting and typed instance composition contracts.</summary>
let tests =
    testList
        "Composite CSS utilities"
        [
            testCase "top-level responsive utilities avoid generic value restrictions"
            <| fun _ ->
                let text = css card
                Expect.stringContains text "@media (min-width: 48rem)" "Reusable breakpoint"
                Expect.stringContains text "font-size: 1.125rem;" "Text utility retained"
                Expect.stringContains text "max-width: 36rem;" "Width utility retained"
                Expect.stringContains text "gap: 1.5rem;" "Whole composite wrapped"
                let first: Styled<string> = Utility.toStyled card
                let second: Styled<int> = Utility.toStyled card
                Expect.equal first.Definition second.Definition "Static utility adapts to multiple component types"
            testCase "application order yields different identities and source-order winners"
            <| fun _ ->
                let blue = utility { declaration "color" "blue" }
                let red = utility { declaration "color" "red" }

                let forward =
                    utility {
                        blue
                        red
                    }

                let backward =
                    utility {
                        red
                        blue
                    }

                Expect.notEqual forward.Definition.ClassName backward.Definition.ClassName "Order is part of identity"
                let text = css forward

                Expect.isTrue
                    (text.IndexOf ("blue", StringComparison.Ordinal) < text.IndexOf ("red", StringComparison.Ordinal))
                    "Later red wins equal specificity"

                let reversed = css backward

                Expect.isTrue
                    (reversed.IndexOf ("red", StringComparison.Ordinal) < reversed.IndexOf ("blue", StringComparison.Ordinal))
                    "Later blue wins equal specificity"
            testCase "fallbacks shorthands and importance retain authored order"
            <| fun _ ->
                let value =
                    utility {
                        declaration "display" "block"
                        declaration "display" "grid"
                        declaration "padding" "1rem"

                        declaration "padding-left" "2rem"
                        |> Declaration.important
                    }

                let text = css value

                Expect.isTrue
                    (text.IndexOf ("display: block;", StringComparison.Ordinal) < text.IndexOf ("display: grid;", StringComparison.Ordinal))
                    "Fallback sequence"

                Expect.isTrue
                    (text.IndexOf ("padding: 1rem;", StringComparison.Ordinal) <
                        text.IndexOf ("padding-left: 2rem !important;", StringComparison.Ordinal))
                    "Shorthand and importance"
            testCase "browser conditions nest as conjunction and retain explicit scopes"
            <| fun _ ->
                let value =
                    utility {
                        md { hover { spacious } }
                        descendant (Selector.className "icon") { pseudoElement "before" { declaration "content" "\"!\"" } }
                        supports "(display: grid)" { container "(min-width: 30rem)" { declaration "display" "grid" } }
                        theme (Selector.unsafeFromText "[data-theme=dark]") { declaration "color" "white" }
                    }

                let text = css value
                Expect.stringContains text ":hover" "Hover selector"
                Expect.stringContains text " .icon::before" "Explicit descendant and pseudo-element"
                Expect.stringContains text "@supports (display: grid)" "Feature condition"
                Expect.stringContains text "@container (min-width: 30rem)" "Container condition"
                Expect.stringContains text ":is([data-theme=dark])" "Explicit element theme match"
            testCase "fixed helper loops preserve ordered utility composition"
            <| fun _ ->
                let fixedSpacing spacing =
                    utility { declaration "gap" (string spacing + "rem") }

                let value =
                    utility {
                        for amount in [ 1; 2; 3 ] do
                            fixedSpacing amount

                        yield! [ declaration "color" "red"; declaration "color" "blue" ]
                        yield! [ spacious ]
                    }

                let text = css value

                Expect.isTrue
                    (text.IndexOf ("gap: 1rem;", StringComparison.Ordinal) < text.IndexOf ("gap: 3rem;", StringComparison.Ordinal))
                    "Loop order"

                Expect.stringContains text "padding: 2rem;" "Yielded utility list"
            testCase "static and reactive fragments compose in either order"
            <| fun _ ->
                let dynamicWidth =
                    utility { Style.dynamic "width" (fun (size: int) -> declaration "width" (string size + "px")) }

                let value =
                    utility {
                        spacious
                        dynamicWidth
                        declaration "color" "red"
                    }

                let styled = Utility.toStyledReactive value
                Expect.stringContains (Style.renderVariables 32 styled) "32px" "Typed instance survives composition"
                Expect.stringContains (Stylesheet.render styled.Definition.Rules) "padding: 2rem;" "Static nodes survive composition"
            testCase "inactive application condition skips reactive readers"
            <| fun _ ->
                let mutable reads = 0

                let value =
                    utility {
                        spacious

                        whenActive (fun (active, _: int) -> active) {
                            md {
                                Style.dynamic "width" (fun (_, size) ->
                                    reads <- reads + 1
                                    declaration "width" (string size + "px"))
                            }
                        }
                    }

                let styled = Utility.toStyledReactive value
                Expect.equal (Style.variableDeclarations (false, 10) styled) [] "Inactive branch has no variables"
                Expect.equal reads 0 "Inactive reader not called"
                Expect.equal (Style.classNames (false, 10) styled).Length 1 "Root only"
                Expect.equal (Style.classNames (true, 10) styled).Length 2 "Root and condition gate"
                Expect.stringContains (Style.renderVariables (true, 10) styled) "10px" "Active reader"
                Expect.equal reads 1 "Only value rendering reads the dynamic property"
            testCase "nested application conditions require every predicate"
            <| fun _ ->
                let value =
                    utility { whenActive (fun (first, _) -> first) { whenActive (fun (_, second) -> second) { declaration "color" "red" } } }

                let styled = Utility.toStyledReactive value

                let classes state =
                    Style.classNames state styled

                Expect.equal (classes (false, false)).Length 1 "Both inactive"
                Expect.equal (classes (false, true)).Length 1 "Inactive parent suppresses child"
                Expect.equal (classes (true, true)).Length 3 "Both gate classes selected"
                Expect.stringContains (Stylesheet.render styled.Definition.Rules) ":where(" "Gate adds zero specificity"
            testCase "fixed conditional helpers can omit reactive fragments"
            <| fun _ ->
                let optionalWidth enabled =
                    utility {
                        if enabled then
                            Style.dynamic "width" (fun (size: int) -> declaration "width" (string size + "px"))
                    }

                let absent = Utility.toStyledReactive (optionalWidth false)
                let present = Utility.toStyledReactive (optionalWidth true)
                Expect.equal (Style.variableDeclarations 12 absent) [] "Implicit empty branch retains the instance type"
                Expect.stringContains (Style.renderVariables 12 present) "12px" "Selected branch retains its reader"

                let optionalStatic enabled =
                    utility {
                        if enabled then
                            spacious
                    }

                Expect.equal (css (optionalStatic false)) "" "Fixed empty branch remains nongeneric"
                let empty = utility { () }
                Expect.equal (css empty) "" "Entirely empty utility remains nongeneric"
            testCase "descendant selector lists cannot escape the receiving root"
            <| fun _ ->
                for text in [ ".icon, body"; ".icon/* ) */, body" ] do
                    Expect.throwsT<ArgumentException>
                        (fun () ->
                            descendant (Selector.unsafeFromText text)
                            |> ignore)
                        "Separate blocks preserve each selector's root scope"

                let nested = descendant (Selector.unsafeFromText ".icon:is(.a,.b)[data-label=',']")
                let value = utility { nested { declaration "color" "red" } }
                Expect.stringContains (css value) " .icon:is(.a,.b)[data-label=',']" "Nested commas remain scoped"
            testCase "viewport thresholds reject incompatible values and stylesheet injection"
            <| fun _ ->
                for text in [ "-1rem"; "10%"; "red"; "var(--width)"; "NaNpx"; "10px); color: red"; "1s" ] do
                    Expect.throwsT<ArgumentException>
                        (fun () ->
                            minViewportWidth (Value.unsafeFromTokens text)
                            |> ignore)
                        ("Reject " + text)

                Expect.throwsT<ArgumentException> (fun () -> media "screen { }" |> ignore) "Query delimiters rejected"
                let zero = minViewportWidth (Value.unsafeFromTokens "0")
                let value = utility { zero { declaration "width" "100%" } }
                Expect.stringContains (css value) "(min-width: 0)" "Zero threshold accepted"
        ]
