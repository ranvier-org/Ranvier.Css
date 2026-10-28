module Ranvier.Css.Spec.BuildTests

open Expecto
open System
open System.IO
open Ranvier.Css
open Ranvier.Css.Build

let private create name dependencies rules =
    {
        Name = name
        Dependencies = dependencies
        Rules = rules
        Styles = []
    }

let private compile mode roots =
    match Compiler.compile { Options.defaults with Mode = mode } roots with
    | Ok result -> result
    | Error errors -> failtestf "%A" errors

/// <summary>Explicit style dependency graph and asset emission contracts.</summary>
let tests =
    testList
        "CSS build assets"
        [
            testCase "opaque imported rules preserve source but cannot be silently rewritten or lifted"
            <| fun _ ->
                let css = ".a { color:red; &:hover { color:blue; } color:green; }"
                let input = create "imported" [] [ Rule.unsafeFromText css ]
                Expect.equal (compile OutputMode.PerModule [ input ]).Assets.Head.Content (css + "\n") "Opaque order is unchanged"

                Expect.isError
                    (Compiler.compile
                        { Options.defaults with
                            Mode = OutputMode.CssModules
                        }
                        [ input ])
                    "Opaque selector rewriting is explicit"

                Expect.isError
                    (Compiler.compile
                        { Options.defaults with
                            Mode = OutputMode.SingleFile "all.css"
                        }
                        [ input; create "other" [] [ Rule.style (Selector.element "body") [] ] ])
                    "Opaque preambles cannot be guessed across modules"
            testCase "license comments retain valid import preambles in CSS Modules"
            <| fun _ ->
                let input =
                    { create
                          "imports"
                          []
                          [
                              Rule.unsafeFromText "/*! license */"
                              Rule.statement "import" "url(base.css)"
                          ] with
                        Styles =
                            [
                                {
                                    ClassName = "r-test"
                                    Rules =
                                        [
                                            Rule.style (Selector.className "r-test") [ Declaration.create "color" "red" ]
                                        ]
                                }
                            ]
                    }

                let css = (compile OutputMode.CssModules [ input ]).Assets.Head.Content

                Expect.isTrue
                    (css.IndexOf ("@import", StringComparison.Ordinal) < css.IndexOf ("composes", StringComparison.Ordinal))
                    "Exports follow imports despite leading comments"
            testCase "writer preflight preserves existing files when assets collide"
            <| fun _ ->
                let directory =
                    Path.Combine (
                        Path.GetTempPath (),
                        "ranvier-css-assets-"
                        + Guid.NewGuid().ToString("N")
                    )

                Directory.CreateDirectory directory |> ignore

                try
                    let sentinel = Path.Combine (directory, "Card.css")
                    File.WriteAllText (sentinel, "original")

                    let compiled =
                        compile OutputMode.PerModule [ create "first" [] [ Rule.statement "layer" "a" ] ]

                    let asset = compiled.Assets.Head

                    let collision =
                        { compiled with
                            Assets = [ { asset with FileName = "Card.css" }; { asset with FileName = "card.css" } ]
                        }

                    Expect.throwsT<ArgumentException> (fun () -> Compiler.write directory collision) "All targets checked before writing"
                    Expect.equal (File.ReadAllText sentinel) "original" "Existing content preserved"
                    Compiler.write directory compiled
                    Expect.equal (File.ReadAllText (Path.Combine (directory, asset.FileName))) asset.Content "Actual asset output"
                    Expect.equal (File.ReadAllText (Path.Combine (directory, "css-manifest.json"))) compiled.Manifest "Actual manifest output"
                finally
                    Directory.Delete (directory, true)
            testCase "cyclic resource identities are rejected"
            <| fun _ -> Expect.isError (Compiler.compile Options.defaults [ create "a" [ create "a" [] [] ] [] ]) "Cycle diagnostic"
            testCase "late imports and incompatible namespace environments cannot be lifted"
            <| fun _ ->
                let first = create "first" [] [ Rule.style (Selector.element "body") [] ]
                let imported = create "imported" [] [ Rule.statement "import" "url(other.css)" ]

                let namespaced =
                    create
                        "svg"
                        []
                        [
                            Rule.statement "namespace" "url(http://www.w3.org/2000/svg)"
                            Rule.style (Selector.element "rect") []
                        ]

                Expect.isError
                    (Compiler.compile
                        { Options.defaults with
                            Mode = OutputMode.SingleFile "app.css"
                        }
                        [ first; imported ])
                    "Late imports require modular output"

                Expect.isError
                    (Compiler.compile
                        { Options.defaults with
                            Mode = OutputMode.SingleFile "app.css"
                        }
                        [ first; namespaced ])
                    "Namespace environments stay independent"
            testCase "adjacent identical styles deduplicate while separated repetitions preserve cascade order"
            <| fun _ ->
                let a: Styled<unit> =
                    Style.create [ Style.declarations [ Declaration.create "color" "red" ] ]

                let b: Styled<unit> =
                    Style.create [ Style.declarations [ Declaration.create "color" "blue" ] ]

                let input =
                    { create "styles" [] [] with
                        Styles = [ a.Definition; a.Definition; b.Definition; a.Definition ]
                    }

                let result = compile OutputMode.PerModule [ input ]

                Expect.equal
                    result.Assets.Head.Content
                    (Stylesheet.render (
                        a.Definition.Rules
                        @ b.Definition.Rules
                        @ a.Definition.Rules
                    ))
                    "Safe adjacent elimination"
            testCase "portable output rejects case-only filename collisions"
            <| fun _ ->
                Expect.isError
                    (Compiler.compile
                        { Options.defaults with
                            ContentHash = false
                        }
                        [ create "Card" [] []; create "card" [] [ Rule.statement "layer" "a" ] ])
                    "Portable file identity"
            testCase "CSS Modules preserve import preambles before export selectors"
            <| fun _ ->
                let style: Styled<unit> =
                    Style.create [ Style.declarations [ Declaration.create "display" "grid" ] ]

                let input =
                    { create "imported" [] [ Rule.statement "import" "url(other.css)" ] with
                        Styles = [ style.Definition ]
                    }

                let result = compile OutputMode.CssModules [ input ]
                Expect.isTrue (result.Assets.Head.Content.StartsWith "@import") "Imports precede exports"
            testCase "conflicting descendants behind equivalent parent identities are diagnosed"
            <| fun _ ->
                let first = create "parent" [ create "dependency" [] [] ] []

                let second =
                    create "parent" [ create "dependency" [] [ Rule.statement "layer" "a" ] ] []

                Expect.isError (Compiler.compile Options.defaults [ first; second ]) "All supplied graph definitions are checked"
            testCase "module names containing module do not collide in asset output"
            <| fun _ ->
                let result =
                    compile OutputMode.CssModules [ create "a.module.b" [] []; create "a.b" [] [] ]

                Expect.equal
                    (result.Assets
                     |> List.map _.FileName
                     |> List.distinct
                     |> List.length)
                    2
                    "Distinct asset names"
            testCase "lifted output preserves transitive order and deduplicates shared modules"
            <| fun _ ->
                let shared =
                    create "reset" [] [ Rule.style (Selector.element "body") [ Declaration.create "margin" "0" ] ]

                let first =
                    create
                        "first"
                        [ shared ]
                        [
                            Rule.style (Selector.className "first") [ Declaration.create "display" "grid" ]
                        ]

                let second =
                    create
                        "second"
                        [ shared ]
                        [
                            Rule.style (Selector.className "second") [ Declaration.create "display" "flex" ]
                        ]

                let result = compile (OutputMode.SingleFile "app.css") [ first; second ]
                Expect.equal result.Assets.Length 1 "One lifted asset"
                Expect.equal result.OrderedModules [ "reset"; "first"; "second" ] "Topological order"
                let css = result.Assets.Head.Content

                Expect.isTrue
                    (css.IndexOf ("body") < css.IndexOf (".first")
                     && css.IndexOf (".first") < css.IndexOf (".second"))
                    "Cascade order"
            testCase "per-module output shares dependencies and emits deterministic assets"
            <| fun _ ->
                let shared = create "shared" [] []
                let roots = [ create "first" [ shared ] []; create "second" [ shared ] [] ]
                let first = compile OutputMode.PerModule roots
                Expect.equal first.Assets.Length 3 "Three files"
                Expect.equal first (compile OutputMode.PerModule roots) "Stable compilation"

                Expect.isTrue
                    (first.Assets
                     |> List.forall (fun asset -> asset.FileName.EndsWith ".css"))
                    "CSS files"
            testCase "conflicting module identities fail instead of choosing by traversal order"
            <| fun _ ->
                let first = create "same" [] []
                let second = create "same" [] [ Rule.statement "layer" "components" ]
                Expect.isError (Compiler.compile Options.defaults [ first; second ]) "Conflicting resources"
            testCase "CSS Modules export stable generated class identities"
            <| fun _ ->
                let style: Styled<unit> =
                    Style.create [ Style.declarations [ Declaration.create "display" "grid" ] ]

                let input =
                    { create "card" [] [] with
                        Styles = [ style.Definition ]
                    }

                let result = compile OutputMode.CssModules [ input ]
                Expect.isTrue (result.Assets.Head.FileName.EndsWith ".module.css") "Module suffix"
                Expect.stringContains result.Assets.Head.Content "composes:" "CSS module class exports"
                Expect.stringContains result.Assets.Head.Content (":global(." + style.Definition.ClassName + ")") "Stable selectors"
            testCase "filenames cannot escape the output directory"
            <| fun _ ->
                Expect.isError
                    (Compiler.compile
                        { Options.defaults with
                            Mode = OutputMode.SingleFile "../app.css"
                        }
                        [ create "app" [] [] ])
                    "Traversal rejected"
        ]
