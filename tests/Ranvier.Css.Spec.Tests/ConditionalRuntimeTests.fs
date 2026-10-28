module Ranvier.Css.Spec.ConditionalRuntimeTests

open Expecto
open Ranvier.Css

/// <summary>Root-scoped conditional classes, inactive readers and linear static lowering.</summary>
let tests =
    testList
        "Conditional utility lowering"
        [
            testCase "application gates target the receiving root before descendant scopes"
            <| fun _ ->
                let style =
                    Style.create
                        [
                            Style.nest
                                " .icon"
                                [
                                    Style.whenActive id [ Style.declarations [ Declaration.create "color" "red" ] ]
                                ]
                        ]

                let root = style.Definition.ClassName
                let css = Stylesheet.render style.Definition.Rules
                Expect.stringContains css ("." + root + ":where(." + root + "-g0) .icon") "Root owns the gate"
                Expect.equal (Style.classNames false style) [ root ] "Inactive gate"
                Expect.equal (Style.classNames true style) [ root; root + "-g0" ] "Active gate"

            testCase "inactive ancestors skip nested predicates and variable readers"
            <| fun _ ->
                let mutable predicateReads = 0
                let mutable valueReads = 0

                let style =
                    Style.create
                        [
                            Style.whenActive
                                (fun (n: int) -> n > 0)
                                [
                                    Style.whenActive
                                        (fun _ ->
                                            predicateReads <- predicateReads + 1
                                            true)
                                        [
                                            Style.dynamic "width" (fun n ->
                                                valueReads <- valueReads + 1
                                                Declaration.create "width" (string n + "px"))
                                        ]
                                ]
                        ]

                Expect.equal (Style.classNames 0 style).Length 1 "Only root remains"
                Expect.equal (Style.renderVariables 0 style) "" "Inactive variables absent"
                Expect.equal predicateReads 0 "Inner predicate skipped"
                Expect.equal valueReads 0 "Variable reader skipped"
                Expect.stringContains (Style.renderVariables 12 style) "12px" "Active value"
                Expect.equal valueReads 1 "One active value read"

            testCase "independent flags produce linear rules and stable identities"
            <| fun _ ->
                let nodes =
                    [
                        for bit in 0..7 do
                            Style.whenActive
                                (fun (flags: int) -> (flags &&& (1 <<< bit)) <> 0)
                                [ Style.declarations [ Declaration.create ("--flag-" + string bit) "1" ] ]
                    ]

                let style = Style.create nodes
                let css = Stylesheet.render style.Definition.Rules
                Expect.equal style.Definition.Rules.Length 8 "One rule per flag, not 256 combinations"

                for state in 0..255 do
                    let active =
                        [
                            for bit in 0..7 do
                                if (state &&& (1 <<< bit)) <> 0 then
                                    bit
                        ]

                    Expect.equal (Style.classNames state style).Length (active.Length + 1) "Exactly active classes"

                Expect.equal (Stylesheet.render style.Definition.Rules) css "Selection never changes CSS"

            testCase "contramap preserves CSS identity and adapts conditional and value readers"
            <| fun _ ->
                let original =
                    [
                        Style.whenActive (fun (n: int) -> n > 0) [ Style.dynamic "opacity" (fun n -> Declaration.create "opacity" (string n)) ]
                    ]

                let first = Style.create original

                let adapted =
                    original
                    |> Style.contramap String.length
                    |> Style.create

                Expect.equal adapted.Definition first.Definition "Only readers change"
                Expect.equal (Style.classNames "" adapted).Length 1 "Adapted false condition"
                Expect.equal (Style.renderVariables "abc" adapted) (Style.renderVariables 3 first) "Adapted value"
        ]
