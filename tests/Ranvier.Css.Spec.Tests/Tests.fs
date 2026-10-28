module Ranvier.Css.Spec.Tests

open System
open System.IO
open System.Text
open System.Text.Json
open Ranvier.Css.Spec
open Expecto

let private equal expected actual =
    if actual <> expected then
        failwithf "Expected %A, got %A" expected actual

let private require condition message =
    if not condition then
        failwith message

let private parsed source =
    match Grammar.parse source with
    | Ok tree -> tree
    | Error diagnostic -> failwithf "%s: %A" source diagnostic

let private consume json =
    match Webref.consume "fixture" (Encoding.UTF8.GetBytes (json: string)) with
    | Ok catalogue -> catalogue
    | Error diagnostics -> failwithf "%A" diagnostics

let private fixture properties functions atrules =
    sprintf """{"properties":%s,"types":[],"functions":%s,"selectors":[],"atrules":%s}""" properties functions atrules

let private feature syntax =
    sprintf """{"name":"width","href":"https://example.test/spec#width","syntax":%s}""" (JsonSerializer.Serialize (syntax: string))

let private pureTests =
    [
        "reference names reject operators and empty function names",
        fun () ->
            for source in [ "<length|color>"; "<foo(bar)>"; "<()>"; "<foo#>" ] do
                require (Grammar.parse source |> Result.isError) ("Malformed reference accepted: " + source)

        "descriptors are accepted exclusively on at-rules",
        fun () ->
            let misplaced =
                fixture """[{"name":"width","href":"https://example.test/width","descriptors":42}]""" "[]" "[]"

            let nested =
                fixture
                    "[]"
                    "[]"
                    """[{"name":"@x","href":"https://example.test/x","descriptors":[{"name":"a","href":"https://example.test/a","descriptors":[]}]}]"""

            for json in [ misplaced; nested ] do
                require
                    (Webref.consume "fixture" (Encoding.UTF8.GetBytes json)
                     |> Result.isError)
                    "Misplaced descriptors silently lost"

        "combinators preserve precedence",
        fun () ->
            match (parsed "a | b || c && d e").Expression with
            | GrammarExpression.Choice [ _
                                         {
                                             Expression = GrammarExpression.AnyOf [ _
                                                                                    {
                                                                                        Expression = GrammarExpression.AllOf [ _
                                                                                                                               {
                                                                                                                                   Expression = GrammarExpression.Sequence [ _
                                                                                                                                                                             _ ]
                                                                                                                               } ]
                                                                                    } ]
                                         } ] -> ()
            | other -> failwithf "Wrong precedence: %A" other

        "group overrides precedence",
        fun () ->
            match (parsed "[ a | b ] c").Expression with
            | GrammarExpression.Sequence [ {
                                               Expression = GrammarExpression.Group {
                                                                                        Expression = GrammarExpression.Choice [ _; _ ]
                                                                                    }
                                           }
                                           _ ] -> ()
            | x -> failwithf "%A" x

        "numeric references preserve dimension and range",
        fun () ->
            match (parsed "<angle [-90deg,90deg]>").Expression with
            | GrammarExpression.Reference r ->
                equal ReferenceKind.Type r.Kind
                equal "angle" r.Name

                equal
                    (Some
                        {
                            Minimum = RangeBound.Finite (-90m, "deg")
                            Maximum = RangeBound.Finite (90m, "deg")
                        })
                    r.Range
            | x -> failwithf "%A" x

        "infinity is read from actual UTF8 bytes",
        fun () ->
            let input =
                Encoding.UTF8.GetString
                    [|
                        60uy
                        110uy
                        117uy
                        109uy
                        98uy
                        101uy
                        114uy
                        32uy
                        91uy
                        48uy
                        44uy
                        226uy
                        136uy
                        158uy
                        93uy
                        62uy
                    |]

            match (parsed input).Expression with
            | GrammarExpression.Reference { Range = Some range } -> equal RangeBound.PositiveInfinity range.Maximum
            | x -> failwithf "%A" x

        "property and function references remain distinct",
        fun () ->
            let kind syntax =
                match (parsed syntax).Expression with
                | GrammarExpression.Reference r -> r.Kind, r.Name
                | x -> failwithf "%A" x

            equal (ReferenceKind.Property, "width") (kind "<'width'>")
            equal (ReferenceKind.Function, "calc") (kind "<calc()>")

        "type arguments retain a grammar subtree",
        fun () ->
            match (parsed "<boolean-expr[ <if-test> ]>").Expression with
            | GrammarExpression.TypeApplication ("boolean-expr",
                                                 {
                                                     Expression = GrammarExpression.Reference r
                                                 }) -> equal "if-test" r.Name
            | x -> failwithf "%A" x

        "combined repetitions retain nested cardinalities",
        fun () ->
            match (parsed "<number>+#?").Expression with
            | GrammarExpression.Repeat ({
                                            Expression = GrammarExpression.Repeat ({
                                                                                       Expression = GrammarExpression.Repeat (_, inner)
                                                                                   },
                                                                                   comma)
                                        },
                                        optional) ->
                equal (1, None, false) (inner.Minimum, inner.Maximum, inner.CommaSeparated)
                equal (1, None, true) (comma.Minimum, comma.Maximum, comma.CommaSeparated)
                equal (0, Some 1, false) (optional.Minimum, optional.Maximum, optional.CommaSeparated)
            | x -> failwithf "%A" x

        "bounded optional repetitions preserve both constraints",
        fun () ->
            match (parsed "<number>{2,4}?").Expression with
            | GrammarExpression.Repeat ({
                                            Expression = GrammarExpression.Repeat (_, r)
                                        },
                                        o) ->
                equal (2, Some 4) (r.Minimum, r.Maximum)
                equal (0, Some 1) (o.Minimum, o.Maximum)
            | x -> failwithf "%A" x

        "required group stays explicit",
        fun () ->
            match (parsed "[ a? b? ]!").Expression with
            | GrammarExpression.Required _ -> ()
            | x -> failwithf "%A" x

        "functions retain ordered arguments and delimiters",
        fun () ->
            match (parsed "rgb( <number>, <number>, <number> )").Expression with
            | GrammarExpression.Function ("rgb",
                                          Some {
                                                   Expression = GrammarExpression.Sequence nodes
                                               }) -> equal 5 nodes.Length
            | x -> failwithf "%A" x

        "at-rule body stays a block",
        fun () ->
            match (parsed "@media <media-query-list> { <rule-list> }").Expression with
            | GrammarExpression.Sequence [ _
                                           _
                                           {
                                               Expression = GrammarExpression.Block ("{", Some _)
                                           } ] -> ()
            | x -> failwithf "%A" x

        "quoted metacharacters stay literals",
        fun () ->
            match (parsed "'+' | '['").Expression with
            | GrammarExpression.Choice [ {
                                             Expression = GrammarExpression.Literal "+"
                                         }
                                         {
                                             Expression = GrammarExpression.Literal "["
                                         } ] -> ()
            | x -> failwithf "%A" x

        "node spans locate the exact component",
        fun () ->
            let source = "a | <length>"

            match (parsed source).Expression with
            | GrammarExpression.Choice [ _; node ] -> equal "<length>" (source.Substring (node.Span.Offset, node.Span.Length))
            | x -> failwithf "%A" x

        "malformed syntax reports bounded offsets",
        fun () ->
            for source in
                [
                    ""
                    "a |"
                    "[a"
                    "f(a"
                    "a{4,2}"
                    "<length [x,1]>"
                    "a**"
                    "'bad"
                    "a ]"
                    "a & b"
                    "a..."
                    "<bad"
                    "a??"
                    "a!"
                ] do
                match Grammar.parse source with
                | Error error -> require (error.Offset >= 0 && error.Offset <= source.Length) source
                | Ok x -> failwithf "Accepted malformed %s: %A" source x

        "resource limits return diagnostics",
        fun () ->
            for source in
                [
                    null
                    String.replicate 65537 "x"
                    String.replicate 150 "["
                    + "x"
                    + String.replicate 150 "]"
                ] do
                require (Grammar.parse source |> Result.isError) "Expected bounded failure"

        "scoped names have independent identities",
        fun () ->
            let functions =
                """[{"name":"fit-content()","href":"https://example.test/a","for":["width"],"syntax":"fit-content(<length>)"},{"name":"fit-content()","href":"https://example.test/b","for":["grid"],"syntax":"fit-content(<number>)"}]"""

            let catalogue = consume (fixture "[]" functions "[]")
            equal 2 catalogue.Features.Length

            require
                (catalogue.Features[0].Id
                 <> catalogue.Features[1].Id)
                "Scope collision"

        "duplicate scope sets are rejected regardless of order",
        fun () ->
            let functions =
                """[{"name":"f()","href":"https://example.test/a","for":["a","b"]},{"name":"f()","href":"https://example.test/b","for":["b","a"]}]"""

            let result =
                Webref.consume "fixture" (Encoding.UTF8.GetBytes (fixture "[]" functions "[]"))

            match result with
            | Error [ e ] -> require (e.Message.Contains "Duplicate feature") e.Message
            | x -> failwithf "%A" x

        "descriptors retain owners and same-name definitions",
        fun () ->
            let atrules =
                """[{"name":"@a","href":"https://example.test/a","descriptors":[{"name":"src","href":"https://example.test/asrc","syntax":"<url>"}]},{"name":"@b","href":"https://example.test/b","descriptors":[{"name":"src","href":"https://example.test/bsrc"}]}]"""

            let catalogue = consume (fixture "[]" "[]" atrules)

            let descriptors =
                catalogue.Features
                |> List.filter (fun f -> f.Id.Kind = FeatureKind.Descriptor)

            equal [ Some "@a"; Some "@b" ] (descriptors |> List.map _.Id.Owner)

        "missing and rejected grammars survive export",
        fun () ->
            let properties =
                """[{"name":"a","href":"https://example.test/a","prose":"semantic primitive"},{"name":"b","href":"https://example.test/b","syntax":"["}]"""

            let catalogue = consume (fixture properties "[]" "[]")
            equal (0, 1, 1) (Snapshot.coverage catalogue)
            use json = JsonDocument.Parse (Snapshot.toJson catalogue)
            equal 2 (json.RootElement.GetProperty("features").GetArrayLength())

        "metadata retains original JSON types",
        fun () ->
            let properties =
                """[{"name":"a","href":"https://example.test/a","initial":"normal","longhands":["x"],"future":{"flag":true}}]"""

            let catalogue = consume (fixture properties "[]" "[]")
            use json = JsonDocument.Parse (Snapshot.toJson catalogue)
            let metadata = json.RootElement.GetProperty("features").[0].GetProperty("metadata")
            equal "normal" (metadata.GetProperty("initial").GetString())
            equal "x" (metadata.GetProperty("longhands").[0].GetString())
            equal true (metadata.GetProperty("future").GetProperty("flag").GetBoolean())

        "malformed inputs carry JSON paths",
        fun () ->
            for json in
                [
                    "{}"
                    """{"properties":[],"properties":[]}"""
                    fixture """[{"name":"a","href":42}]""" "[]" "[]"
                    fixture """[{"name":"a","href":"relative"}]""" "[]" "[]"
                    fixture ("[" + feature "x" + "]") "{}" "[]"
                    fixture """[{"name":"a","href":"https://example.test","syntax":null}]""" "[]" "[]"
                    fixture "[]" "[]" """[{"name":"@x","href":"https://example.test"}]"""
                ] do
                match Webref.consume "bad" (Encoding.UTF8.GetBytes json) with
                | Error diagnostics ->
                    require
                        (diagnostics
                         |> List.forall (fun e -> e.Path.StartsWith "$"))
                        "Missing JSON paths"
                | Ok _ -> failwithf "Accepted %s" json

        "unknown category is rejected",
        fun () ->
            let json =
                (fixture "[]" "[]" "[]").TrimEnd('}')
                + ""","unknown":[]}"""

            require
                (Webref.consume "bad" (Encoding.UTF8.GetBytes json)
                 |> Result.isError)
                "Unknown category silently dropped"

        "snapshot sorts independently of input feature order",
        fun () ->
            let input =
                fixture
                    ("["
                     + feature "auto | <length>"
                     + ","
                     + (feature "none").Replace("width", "height")
                     + "]")
                    "[]"
                    "[]"

            let catalogue = consume input
            equal 2 catalogue.Features.Length

            equal
                (Snapshot.toJson catalogue)
                (Snapshot.toJson
                    { catalogue with
                        Features = List.rev catalogue.Features
                    })

            require (not ((Snapshot.toJson catalogue).Contains "\r")) "CRLF leaked into snapshot"

            let expectedHash =
                Convert.ToHexStringLower (Security.Cryptography.SHA256.HashData (Encoding.UTF8.GetBytes input))

            equal expectedHash catalogue.Identity.Sha256

        "invalid CLI arguments fail",
        fun () ->
            equal 64 (Cli.run [| "--unknown" |])
            equal 64 (Cli.run [| "--input" |])
    ]

let private corpusTests root =
    [
        "pinned corpus preserves all definitions and grammars",
        fun () ->
            let path =
                Path.Combine (root, "tools", "Ranvier.Css.Generate", "node_modules", "@webref", "css", "css.json")

            match Webref.consume "@webref/css@8.7.8" (File.ReadAllBytes path) with
            | Error errors -> failwithf "%A" errors
            | Ok catalogue ->
                equal 1839 catalogue.Features.Length
                equal (1710, 0, 129) (Snapshot.coverage catalogue)
                equal "30cb8daa786d6c27a1f74c5d413311c785ee0eec1dd34c65e8a4e6681c86baca" catalogue.Identity.Sha256
                use json = JsonDocument.Parse (Snapshot.toJson catalogue)
                equal 1839 (json.RootElement.GetProperty("features").GetArrayLength())

        "CLI strict mode exports diagnostics and preserves input",
        fun () ->
            let temporary =
                Path.Combine (Path.GetTempPath (), "ranvier-css-" + Guid.NewGuid().ToString("N"))

            Directory.CreateDirectory temporary |> ignore

            try
                let input, output =
                    Path.Combine (temporary, "input.json"), Path.Combine (temporary, "output.json")

                let json = fixture ("[" + feature "[" + "]") "[]" "[]"
                File.WriteAllText (input, json)
                equal 2 (Cli.run [| "--input"; input; "--output"; output; "--strict" |])
                equal json (File.ReadAllText input)
                use model = JsonDocument.Parse (File.ReadAllText output)
                equal "custom" (model.RootElement.GetProperty("dataset").GetProperty("label").GetString())
                equal 1 (model.RootElement.GetProperty("coverage").GetProperty("rejected").GetInt32())
                equal 64 (Cli.run [| "--input"; input; "--output"; input |])
                File.WriteAllText (input, "{}")
                let previous = File.ReadAllText output
                equal 1 (Cli.run [| "--input"; input; "--output"; output |])
                equal previous (File.ReadAllText output)
                use oversized = File.Create input
                oversized.SetLength 33554433L
                oversized.Dispose ()
                let before = GC.GetAllocatedBytesForCurrentThread ()
                equal 1 (Cli.run [| "--input"; input; "--output"; output |])
                require (GC.GetAllocatedBytesForCurrentThread () - before < 1048576L) "CLI allocated oversized input before rejecting it"
            finally
                Directory.Delete (temporary, true)
    ]

let private root =
    Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "..", ".."))

[<Tests>]
let tests =
    testList
        "CSS specs"
        ([
            ResolutionTests.tests
            NumericSemanticsTests.tests
            CalculationTests.tests
            SpecSourcesTests.tests
            GenerationTests.tests
            RuntimeTests.tests
            GrammarRuntimeTests.tests
            ConditionalRuntimeTests.tests
            UtilityTests.tests
            BuildTests.tests
            ViewTests.tests
            UtilityViewTests.tests
         ]
         @ ((pureTests @ corpusTests root)
            |> List.map (fun (name, run) -> testCase name run)))

[<EntryPoint>]
let main args =
    runTestsWithCLIArgs [] args tests
