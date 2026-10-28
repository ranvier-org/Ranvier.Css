module Ranvier.Css.Compile.Tests

open System
open System.IO
open Expecto
open Ranvier.Css.Build
open Ranvier.Css.Compile

let private repository =
    Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "../.."))

let private fixture = Path.Combine (__SOURCE_DIRECTORY__, "Fixtures/App/App.fsproj")

let private extract options project =
    match Extractor.extract options project with
    | Ok result -> result
    | Error errors ->
        failtest (
            errors
            |> List.map Diagnostic.format
            |> String.concat "\n"
        )

let private temporary (source: string) action =
    let directory =
        Path.Combine (__SOURCE_DIRECTORY__, "out", Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory directory |> ignore
    let project = Path.Combine (directory, "Fixture.fsproj")
    let core = Path.Combine (repository, "src/Ranvier.Css/Ranvier.Css.fsproj")
    let view = Path.Combine (repository, "src/Ranvier.Css.View/Ranvier.Css.View.fsproj")

    let bindings =
        Path.Combine (repository, "src/Ranvier.Css.Bindings/bin/Release/net10.0/Ranvier.Css.Bindings.dll")

    File.WriteAllText (
        project,
        $"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="{core}"/><ProjectReference Include="{view}"/><Reference Include="Ranvier.Css.Bindings"><HintPath>{bindings}</HintPath></Reference><Compile Include="Fixture.fs"/></ItemGroup></Project>"""
    )

    File.WriteAllText (Path.Combine (directory, "Fixture.fs"), source)
    let start = System.Diagnostics.ProcessStartInfo ("dotnet")
    start.ArgumentList.Add "restore"
    start.ArgumentList.Add "-nr:false"
    start.ArgumentList.Add project
    start.RedirectStandardOutput <- true
    start.RedirectStandardError <- true
    start.Environment["MSBUILDDISABLENODEREUSE"] <- "1"
    start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] <- "1"
    use child = System.Diagnostics.Process.Start start
    let output = child.StandardOutput.ReadToEndAsync ()
    let errors = child.StandardError.ReadToEndAsync ()
    child.WaitForExit ()

    if child.ExitCode <> 0 then
        failtest (output.Result + errors.Result)

    action project

let private compactFixture () =
    let node id kind children : Ranvier.Css.Internal.GrammarRuntime.Node =
        {
            Id = id
            Kind = kind
            Text = ""
            Target = -1
            Children = children
            Separators = [||]
            Minimum = 0
            Maximum = -1
            CommaSeparated = false
            Lower = None
            Upper = None
        }

    let nodes =
        [|
            node 0 0 [||]
            node 1 0 [||]
            { node 2 1 [||] with Text = "auto" }
            { node 3 2 [||] with
                Target = 0
                Lower = Some (0., "px")
                Upper = Some (100., "px")
            }
            node 4 3 [| 2; 3 |]
            { node 5 1 [||] with Text = "/" }
            { node 6 4 [| 3; 5; 3 |] with
                Separators = [| ""; " "; " " |]
            }
            node 7 6 [| 2; 3 |]
            { node 8 7 [| 3 |] with
                Minimum = 1
                Maximum = 2
                CommaSeparated = true
            }
            { node 9 9 [| 6 |] with
                Text = "minmax"
            }
            { node 10 7 [| 2 |] with
                Minimum = 0
                Maximum = 1
            }
            { node 11 4 [| 10; 3 |] with
                Separators = [| ""; " " |]
            }
            { node 12 0 [||] with Text = "string" }
            { node 13 1 [||] with Text = "auto" }
            node 14 3 [| 13; 3 |]
            node 15 11 [| 4 |]
            { node 16 1 [||] with Text = "initial" }
        |]

    let field (value: string) =
        string value.Length + ":" + value

    let bound (value: (float * string) option) =
        match value with
        | None -> "", ""
        | Some (magnitude, unitName) -> magnitude.ToString ("R", Globalization.CultureInfo.InvariantCulture), unitName

    let records =
        nodes
        |> Array.collect (fun node ->
            let lower, lowerUnit = bound node.Lower
            let upper, upperUnit = bound node.Upper

            [|
                string node.Id
                string node.Kind
                node.Text
                string node.Target
                node.Children
                |> Array.map string
                |> String.concat ","
                node.Separators
                |> Array.map field
                |> String.concat ""
                string node.Minimum
                string node.Maximum
                (if node.CommaSeparated then "1" else "0")
                lower
                lowerUnit
                upper
                upperUnit
                ""
            |])

    let encoded =
        Array.append [| string nodes.Length |] records
        |> Array.map field
        |> String.concat ""

    let owners =
        [|
            4, [ "Width" ], "create"
            2, [ "Width" ], "auto"
            6, [ "Pair" ], "create"
            7, [ "Border" ], "create"
            8, [ "Widths" ], "create"
            9, [ "Minmax" ], "create"
            11, [ "OptionalPrefix" ], "create"
            0, [ "Length" ], "create"
        |]
        |> Array.map (fun (id, path, memberName) ->
            {|
                nodeId = id
                path = path
                memberName = memberName
            |})

    let factory owner memberName id kind production unitName argumentType =
        {|
            owner = owner
            memberName = memberName
            nodeId = id
            kind = kind
            production = production
            unitName = unitName
            argumentType = argumentType
        |}

    let json =
        System.Text.Json.JsonSerializer.Serialize (
            {|
                schemaVersion = 2
                namespaceName = "Probe"
                encodedGrammar = encoded
                owners = owners
                properties =
                    [|
                        {|
                            owner = "Probe.Css"
                            memberName = "width"
                            nodeId = 4
                            cssName = "width"
                        |}
                        {|
                            owner = "Probe.Css"
                            memberName = "height"
                            nodeId = 14
                            cssName = "height"
                        |}
                    |]
                factories =
                    [|
                        factory "Probe.Css" "px" 0 "number" "length" "px" "float"
                        factory "Probe.CssValue.Length" "px" 0 "number" "length" "px" "float"
                        factory "Probe.Css" "seconds" 1 "number" "time" "s" "float"
                        factory "Probe.CssValue.Length" "calc" 0 "calculation" "length" "" "expression"
                        factory "Probe.Css" "string" 12 "string" "string" "" "string"
                        factory "Probe.Css" "customIdent" 12 "customIdentifier" "custom-ident" "" "string"
                        factory "Probe.CssValue.Boolean" "not" 15 "boolean" "not" "" "value"
                        factory "Probe.CssValue.Boolean" "and" 15 "boolean" "and" "" "headTail"
                        factory "Probe.CssValue.Boolean" "or" 15 "boolean" "or" "" "headTail"
                        factory "Probe.CssValue.All" "initial" 16 "wide" "" "" "none"
                    |]
            |}
        )

    CompactMetadata.loadJson json, nodes

let private compactInvoke registry owner memberName arguments =
    CompactEvaluation.tryInvoke registry owner memberName arguments
    |> Option.defaultWith (fun () ->
        failtest (
            "Unknown compact factory "
            + owner
            + "."
            + memberName
        ))

let private compactInput =
    function
    | CompactEvaluation.Output.Value (value, calculation) -> CompactEvaluation.Input.Value (value, calculation)
    | CompactEvaluation.Output.Variable (name, fallback) -> CompactEvaluation.Input.Variable (name, fallback)
    | CompactEvaluation.Output.Wide keyword -> CompactEvaluation.Input.Wide keyword
    | CompactEvaluation.Output.Calculation calculation -> CompactEvaluation.Input.Calculation calculation
    | _ -> failtest "Expected a compact value."

let private compactData =
    function
    | CompactEvaluation.Output.Value (value, _) -> value
    | _ -> failtest "Expected compact grammar evidence."

let private compactDeclaration =
    function
    | CompactEvaluation.Output.Declaration declaration -> declaration
    | _ -> failtest "Expected a compact declaration."

let private compactScalar value =
    CompactEvaluation.Input.Scalar (box value)

let private compactTests =
    testList
        "compact grammar metadata"
        [
            testCase "shares runtime identity and range checks across property roots"
            <| fun _ ->
                let registry, nodes = compactFixture ()
                let invoke = compactInvoke registry

                let px magnitude =
                    invoke "Probe.Css" "px" [ compactScalar magnitude ]

                let value = px 42.
                let table = Ranvier.Css.Internal.GrammarRuntime.Table.create nodes
                Expect.equal (compactData value) (Ranvier.Css.Internal.GrammarRuntime.number table 0 42. "px") "Runtime evidence is preserved."

                let declaration =
                    invoke "Probe.Css" "width" [ compactInput value ]
                    |> compactDeclaration

                Expect.equal (Ranvier.Css.Declaration.value declaration) "42px" "The same property sink emits the same tokens."

                Expect.throws
                    (fun () ->
                        invoke "Probe.Css" "width" [ compactInput (px -1.) ]
                        |> ignore)
                    "Negative values retain runtime range rejection."

                Expect.throws
                    (fun () ->
                        invoke "Probe.Css" "width" [ compactInput (invoke "Probe.Css" "seconds" [ compactScalar 1. ]) ]
                        |> ignore)
                    "A time value cannot enter length."

            testCase "unclassified keyword provenance admits identical tokens at different occurrences"
            <| fun _ ->
                let registry, _ = compactFixture ()
                let keyword = compactInvoke registry "Probe.CssValue.Width" "auto" []
                Expect.equal (compactData keyword).NodeId -1 "Keywords carry token provenance rather than an occurrence ID."

                let declaration =
                    compactInvoke registry "Probe.Css" "height" [ compactInput keyword ]
                    |> compactDeclaration

                Expect.equal (Ranvier.Css.Declaration.value declaration) "auto" "A second auto node admits the shared keyword."

            testCase "reconstructs punctuation lists and optional literal selectors"
            <| fun _ ->
                let registry, _ = compactFixture ()
                let invoke = compactInvoke registry

                let px magnitude =
                    invoke "Probe.Css" "px" [ compactScalar magnitude ]
                    |> compactInput

                let pair = invoke "Probe.CssValue.Pair" "create" [ px 42.; px 12. ]
                Expect.equal (compactData pair).Text "42px / 12px" "Punctuation occupies syntax slots."

                let repeated =
                    invoke "Probe.CssValue.Widths" "create" [ CompactEvaluation.Input.Items [ px 42.; px 12. ] ]

                Expect.equal (compactData repeated).Text "42px, 12px" "List separators match runtime."

                let unordered =
                    invoke "Probe.CssValue.Border" "create" [ compactScalar false; CompactEvaluation.Input.Optional (px 42.) ]

                Expect.equal (compactData unordered).Text "42px" "False omits a literal option."

                let literalOnly =
                    invoke "Probe.CssValue.Border" "create" [ compactScalar true; CompactEvaluation.Input.Omitted ]

                Expect.equal (compactData literalOnly).Text "auto" "True retains a literal option."

                Expect.throws
                    (fun () ->
                        invoke "Probe.CssValue.Border" "create" [ compactScalar false; CompactEvaluation.Input.Omitted ]
                        |> ignore)
                    "At least one option remains required."

                Expect.throws
                    (fun () ->
                        invoke "Probe.CssValue.Widths" "create" [ CompactEvaluation.Input.Items [] ]
                        |> ignore)
                    "List cardinality remains checked."

            testCase "keeps numeric calculations variables wide keywords and readers opaque"
            <| fun _ ->
                let registry, _ = compactFixture ()
                let invoke = compactInvoke registry

                let px magnitude =
                    invoke "Probe.Css" "px" [ compactScalar magnitude ]
                    |> compactInput

                let variable =
                    invoke "Probe.Css" "variable" [ compactScalar "--spacing" ]
                    |> compactInput

                let fallback =
                    invoke "Probe.CssValue.VariableModule" "withFallback" [ variable; px 42. ]
                    |> compactInput

                let declaration =
                    invoke "Probe.Css" "width" [ fallback ]
                    |> compactDeclaration

                Expect.equal (Ranvier.Css.Declaration.value declaration) "var(--spacing, 42px)" "References retain fallback bytes."

                let wide =
                    invoke "Probe.CssValue.CssWide" "revertLayer" []
                    |> compactInput

                let declaration =
                    invoke "Probe.Css" "width" [ wide ]
                    |> compactDeclaration

                Expect.equal (Ranvier.Css.Declaration.value declaration) "revert-layer" "CSS-wide keywords use the separate sink overload."

                match invoke "Probe.CssValue.All" "initial" [] with
                | CompactEvaluation.Output.Wide "initial" -> ()
                | _ -> failtest "The all-property keyword must retain its static-only wide state."

                let arithmetic =
                    CompactEvaluation.tryOperation registry "Probe.CssValue.Numeric.NumericValue" "op_Addition" None [ px 42.; px 1. ]
                    |> Option.get

                let calculated = invoke "Probe.CssValue.Length" "calc" [ compactInput arithmetic ]
                Expect.equal (compactData calculated).Text "calc(42px + 1px)" "Arithmetic is serialized without evaluation."

                Expect.throws
                    (fun () ->
                        invoke "Probe.CssValue.Length" "calc" [ compactInput (invoke "Probe.Css" "seconds" [ compactScalar 1. ]) ]
                        |> ignore)
                    "Calculations retain dimension checking."

                match invoke "Probe.Css" "width" [ CompactEvaluation.Input.Reader ] with
                | CompactEvaluation.Output.Nodes [ _ ] -> ()
                | _ -> failtest "A reader must remain an opaque dynamic style."

            testCase "boolean factories agree with the runtime kernel and require a head"
            <| fun _ ->
                let registry, nodes = compactFixture ()
                let invoke = compactInvoke registry
                let first = invoke "Probe.CssValue.Width" "auto" []
                let second = invoke "Probe.Css" "px" [ compactScalar 42. ]
                let table = Ranvier.Css.Internal.GrammarRuntime.Table.create nodes
                let negated = invoke "Probe.CssValue.Boolean" "not" [ compactInput first ]

                Expect.equal
                    (compactData negated)
                    (Ranvier.Css.Internal.GrammarRuntime.boolean table 15 "not" [| compactData first |])
                    "Negation uses the shared grammar operation."

                for operation in [ "and"; "or" ] do
                    let actual =
                        invoke "Probe.CssValue.Boolean" operation [ compactInput first; CompactEvaluation.Input.Items [ compactInput second ] ]

                    Expect.equal
                        (compactData actual)
                        (Ranvier.Css.Internal.GrammarRuntime.boolean table 15 operation [| compactData first; compactData second |])
                        "Boolean serialization and admission match the runtime kernel."

                    Expect.throws
                        (fun () ->
                            invoke "Probe.CssValue.Boolean" operation [ CompactEvaluation.Input.Items [] ]
                            |> ignore)
                        "The required head cannot be omitted."

            testCase "resolved source factories bypass throwing bodies and remain first class"
            <| fun _ ->
                let compact, _ = compactFixture ()
                let metadata = Metadata.fromCompact compact

                let checker =
                    FSharp.Compiler.CodeAnalysis.FSharpChecker.Create (keepAssemblyContents = true)

                let path =
                    Path.Combine (
                        Path.GetTempPath (),
                        "ranvier-css-compact-"
                        + Guid.NewGuid().ToString("N")
                        + ".fsx"
                    )

                let source =
                    "module Probe\n#r @\""
                    + typeof<Ranvier.Css.Declaration>.Assembly.Location
                    + "\"\n"
                    + """module CssValue =
    type Variable<'category> = private Variable of string
    module Variable =
        let withFallback (_: Variable<'category>) (_: 'category): Variable<'category> = failwith "A variable factory body must not execute."
    type Wide = private Wide of string
    module CssWide =
        let initial: Wide = failwith "A CSS-wide keyword body must not execute."
    module Width =
        let auto: int = failwith "A generated constant body must not execute."
    module Length =
        let px (_: float): int = failwith "A generated factory body must not execute."
    let render (_: int): string = failwith "A generated interface getter must not execute."
type Css =
    static member px (_: float): int = failwith "A generated primitive body must not execute."
    static member width (_: int): Ranvier.Css.StyleNode<unit> = failwith "A generated property body must not execute."
    static member width (_: unit -> int): Ranvier.Css.StyleNode<unit> = failwith "A generated reader body must not execute."
    static member width (_: CssValue.Variable<int>): Ranvier.Css.StyleNode<unit> = failwith "A generated variable sink must not execute."
    static member width (_: CssValue.Wide): Ranvier.Css.StyleNode<unit> = failwith "A generated wide sink must not execute."
    static member variable<'category> (_: string): CssValue.Variable<'category> = failwith "A variable alias body must not execute."
module App =
    let keyword = Css.width CssValue.Width.auto
    let direct = Css.width (Css.px 42.)
    let spacing: CssValue.Variable<int> = Css.variable "--spacing"
    let reference = Css.width (CssValue.Variable.withFallback spacing (Css.px 3.))
    let wide = Css.width CssValue.CssWide.initial
    let dynamic = Css.width (fun () -> failwith "A tracked reader must never execute.")
    let renderings = List.map CssValue.render (List.map CssValue.Length.px [1.; 2.])
"""

                let sourceText = FSharp.Compiler.Text.SourceText.ofString source

                let implementations =
                    File.WriteAllText (path, source)

                    try
                        let options, _ =
                            checker.GetProjectOptionsFromScript (path, sourceText, assumeDotNetFramework = false, useSdkRefs = true)
                            |> Async.RunSynchronously

                        let result =
                            checker.ParseAndCheckProject options
                            |> Async.RunSynchronously

                        let errors =
                            result.Diagnostics
                            |> Array.filter (fun diagnostic -> diagnostic.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)

                        Expect.isEmpty
                            errors
                            ("The probe must type-check before extraction: "
                             + (errors
                                |> Array.map (fun error -> error.Message)
                                |> String.concat "; "))

                        result.AssemblyContents.ImplementationFiles
                    finally
                        File.Delete path

                let project: Project.CheckedProject =
                    {
                        ProjectPath = path
                        Contents = implementations
                    }

                let definitions = Evaluation.definitions [ project ]

                let interpreter =
                    Evaluation.Interpreter (ExtractionOptions.defaults, metadata, definitions)

                let evaluate name =
                    let definition =
                        definitions.Values
                        |> Seq.tryFind (fun definition -> definition.Symbol.DisplayName = name)
                        |> Option.defaultWith (fun () ->
                            failtest (
                                "Missing "
                                + name
                                + " among "
                                + (definitions.Values
                                   |> Seq.map (fun definition ->
                                       definition.Symbol.FullName
                                       + " / "
                                       + definition.Symbol.DisplayName)
                                   |> String.concat ", ")
                            ))

                    interpreter.Evaluate ([], definition.Body)

                match evaluate "keyword", evaluate "direct", evaluate "renderings" with
                | Evaluation.Declaration keyword, Evaluation.Declaration direct, Evaluation.Items renderings ->
                    Expect.equal (Ranvier.Css.Declaration.value keyword) "auto" "Constants use metadata before source evaluation."
                    Expect.equal (Ranvier.Css.Declaration.value direct) "42px" "Static members use metadata before source evaluation."

                    let values =
                        renderings
                        |> List.map (function
                            | Evaluation.Scalar (:? string as value) -> value
                            | _ -> failtest "Expected serialized tokens.")

                    Expect.equal values [ "1px"; "2px" ] "Generated helpers remain usable as function values."
                | _ -> failtest "The compact probe produced unexpected evaluation states."

                match evaluate "reference", evaluate "wide", evaluate "dynamic" with
                | Evaluation.Declaration reference, Evaluation.Declaration wide, Evaluation.Nodes [ _ ] ->
                    Expect.equal (Ranvier.Css.Declaration.value reference) "var(--spacing, 3px)" "Typed variable aliases retain fallback tokens."
                    Expect.equal (Ranvier.Css.Declaration.value wide) "initial" "CSS-wide getters use their fixed intrinsic owners."
                | _ -> failtest "The compact probe did not retain variable, wide, and reader overloads."
        ]

[<Tests>]
/// <summary>Checks source discovery, deterministic lowering, side-effect isolation and evaluation diagnostics.</summary>
let tests =
    testSequenced
    <| testList
        "CSS source extraction"
        [
            compactTests
            testCase "extracts referenced helpers and retains every application alternative without executing initializers"
            <| fun _ ->
                let result = extract ExtractionOptions.defaults fixture

                let css =
                    result.Assets
                    |> List.map _.Content
                    |> String.concat "\n"

                Expect.stringContains css "font-size: 1rem" "Referenced library utility is reachable."
                Expect.stringContains css "margin-left: 0.5rem" "Finite helpers are evaluated."
                Expect.stringContains css "min-width: 48rem" "Responsive conditions are retained."
                Expect.stringContains css ":hover" "Nested condition is retained."
                Expect.stringContains css "opacity: var(" "Reactive values become static variable slots."
                Expect.stringContains css ":where(" "All reactive alternatives are precompiled."
                Expect.isFalse (css.Contains "unused-never-emitted") "Unused utilities are removed."

                Expect.isFalse
                    (File.Exists (Path.Combine (Environment.CurrentDirectory, "app-executed.sentinel")))
                    "Application initialization never runs."

                Expect.isFalse
                    (File.Exists (Path.Combine (Environment.CurrentDirectory, "library-executed.sentinel")))
                    "Library initialization never runs."

                let declaration name value =
                    Ranvier.Css.Style.declarations [ Ranvier.Css.Declaration.create name value ]

                let expected =
                    Ranvier.Css.Style.create
                        [
                            declaration "box-sizing" "border-box"
                            declaration "font-size" "1rem"
                            Ranvier.Css.Style.media "(min-width: 48rem)" [ declaration "font-size" "1.125rem" ]
                            declaration "margin-left" "0.25rem"
                            declaration "margin-left" "0.5rem"
                            declaration "width" "100%"
                            Ranvier.Css.Style.declarations
                                [
                                    Ranvier.Css.Declaration.create "display" "-webkit-box"
                                    Ranvier.Css.Declaration.create "display" "flex"
                                ]
                            Ranvier.Css.Style.media "(min-width: 48rem)" [ Ranvier.Css.Style.hover [ declaration "padding-left" "2rem" ] ]
                            Ranvier.Css.Style.whenActive
                                (fun () -> true)
                                [
                                    Ranvier.Css.Style.dynamic "opacity" (fun () -> failwith "Never evaluate runtime readers.")
                                ]
                        ]

                Expect.stringContains css ("." + expected.Definition.ClassName) "Extraction uses exactly the runtime's composition identity."
            testCase "repeated extraction is byte deterministic"
            <| fun _ ->
                let first = extract ExtractionOptions.defaults fixture
                let second = extract ExtractionOptions.defaults fixture
                Expect.equal second first "Source discovery and class identities are deterministic."
            testCase "calculation operators match runtime output in every asset mode without sampling readers"
            <| fun _ ->
                temporary
                    """module Fixture
open Ranvier.Css
open Ranvier.Css.View
let spacing value = utility { Css.width (CssValue.Length.calc (Css.rem value + Css.px 8.)) }
let example = utility {
    spacing 2.
    Css.height (CssValue.LengthPercentage.calc (Css.percent 100. - Css.rem 2.))
    Css.opacity (CssValue.Number.calc (Css.rem 4. / Css.rem 8.))
    Css.zIndex (CssValue.Integer.calc (Css.number 2. + Css.number 1.))
    Css.marginLeft (CssValue.Length.calc (-(Css.px 4. * (Css.number 2. + Css.number 1.))))
    Css.paddingLeft (CssValue.Length.calc (2. * Css.rem 1. / 2.))
    Css.width (CssValue.LengthPercentage.calc (CssValue.LengthPercentage.percent 50. * Css.rem 4. / Css.rem 2.))
    Css.width (CssValue.Length.calc (Css.min (Css.px 2., Css.px 3.)))
    Css.height (CssValue.Length.calc (Css.clamp (Css.px 4., Some (Css.px 2.), None)))
    Css.paddingLeft (CssValue.Length.calc (Css.round (Css.px 3., Css.px 2., strategy = Ranvier.Css.Spec.RoundingStrategy.Up)))
    Css.marginLeft (CssValue.Length.calc (Css.roundLineWidth (Css.px 1.2)))
    Css.opacity (CssValue.Number.calc (Css.sin Css.pi))
    Css.opacity (CssValue.Number.calc (Css.progress (Css.px 1., Css.px 0., Css.px 2., noClamp = true)))
    Css.width (CssValue.Length.calc (Css.random (Css.px 1., Css.px 2., key = CalculationRandomKey.Fixed 0.5)))
    Css.width (CssValue.Length.calc (Css.calcMix [ Css.px 1., Some 50.; Css.px 3., None ]))
    Css.zIndex (CssValue.Integer.calc (Css.siblingIndex ()))
    Css.width (CssValue.Length.calc (Css.calcInterpolate (Css.interpolationProgress (Css.number 0.5), [
        Css.interpolationStop (Css.interpolationPosition (Css.number 0.), Css.px 10.)
        Css.interpolationStop (Css.interpolationPosition (Css.number 1.), Css.px 20.)
    ], easing = CalculationEasing.Keyword "ease")))
    Css.width (Css.calcSize (Css.intrinsicKeyword "auto", Css.intrinsicSize + Css.px 2.))
    Css.width (Css.calcSize (Css.intrinsicFitContent (Css.px 20.), Css.intrinsicSize + Css.px 2.))
    Css.width (Css.calcSize (Css.intrinsicStretch (Css.px 20.), Css.intrinsicSize + Css.px 2.))
    Css.width (fun (_: bool) -> if true then failwith "Reader executed during extraction" else CssValue.Length.calc (Css.rem 3. + Css.px 1.))
}
let view () = styles { example }
do failwith "Initializer executed during extraction"
"""
                <| fun project ->
                    let literal production unitName value =
                        Ranvier.Css.CalculationRuntime.literal production unitName value

                    let binary operation left right =
                        Ranvier.Css.CalculationRuntime.binary operation left right

                    let call name values =
                        Ranvier.Css.CalculationRuntime.functionCall name values

                    let arguments name values =
                        Ranvier.Css.CalculationRuntime.functionArguments name values

                    let arg value =
                        Ranvier.Css.CalculationArgument.Value value

                    let px value =
                        literal "length" "px" value

                    let length =
                        Ranvier.Css.Spec.NumericSemantics.dimension Ranvier.Css.Spec.NumericDimension.Length

                    let number = Ranvier.Css.Spec.NumericSemantics.number
                    let context = Ranvier.Css.Spec.NumericSemantics.dimensionOnlyContext

                    let lengthContext =
                        { context with
                            PercentagesAllowed = true
                            PercentageBasis = Some Ranvier.Css.Spec.NumericDimension.Length
                        }

                    let declaration name context expected value =
                        Ranvier.Css.Style.declarations [ Ranvier.Css.CalculationRuntime.declaration name context expected value ]

                    let expected =
                        Ranvier.Css.Style.create
                            [
                                declaration "width" lengthContext length (binary "+" (literal "length" "rem" 2.) (literal "length" "px" 8.))
                                declaration "height" lengthContext length (binary "-" (literal "percentage" "%" 100.) (literal "length" "rem" 2.))
                                declaration "opacity" context number (binary "/" (literal "length" "rem" 4.) (literal "length" "rem" 8.))
                                declaration "z-index" context number (binary "+" (literal "integer" "" 2.) (literal "integer" "" 1.))
                                declaration
                                    "margin-left"
                                    lengthContext
                                    length
                                    (Ranvier.Css.CalculationRuntime.negate (
                                        binary "*" (literal "length" "px" 4.) (binary "+" (literal "number" "" 2.) (literal "number" "" 1.))
                                    ))
                                declaration
                                    "padding-left"
                                    lengthContext
                                    length
                                    (binary "/" (binary "*" (literal "number" "" 2.) (literal "length" "rem" 1.)) (literal "number" "" 2.))
                                declaration
                                    "width"
                                    lengthContext
                                    length
                                    (binary
                                        "/"
                                        (binary "*" (literal "length-percentage" "%" 50.) (literal "length" "rem" 4.))
                                        (literal "length" "rem" 2.))
                                declaration "width" lengthContext length (call "min" [ px 2.; px 3. ])
                                declaration
                                    "height"
                                    lengthContext
                                    length
                                    (arguments "clamp" [ arg (px 2.); arg (px 4.); Ranvier.Css.CalculationArgument.NoneBound ])
                                declaration
                                    "padding-left"
                                    lengthContext
                                    length
                                    (arguments
                                        "round"
                                        [
                                            Ranvier.Css.CalculationArgument.Strategy Ranvier.Css.Spec.RoundingStrategy.Up
                                            arg (px 3.)
                                            arg (px 2.)
                                        ])
                                declaration
                                    "margin-left"
                                    lengthContext
                                    length
                                    (arguments
                                        "round"
                                        [
                                            Ranvier.Css.CalculationArgument.Strategy Ranvier.Css.Spec.RoundingStrategy.LineWidth
                                            arg (px 1.2)
                                        ])
                                declaration "opacity" context number (call "sin" [ Ranvier.Css.CalculationRuntime.constant "pi" ])
                                declaration
                                    "opacity"
                                    context
                                    number
                                    (arguments
                                        "progress"
                                        [
                                            Ranvier.Css.CalculationArgument.NoClamp
                                            arg (px 1.)
                                            arg (px 0.)
                                            arg (px 2.)
                                        ])
                                declaration
                                    "width"
                                    lengthContext
                                    length
                                    (arguments
                                        "random"
                                        [
                                            Ranvier.Css.CalculationArgument.RandomKey (Ranvier.Css.CalculationRandomKey.Fixed 0.5)
                                            arg (px 1.)
                                            arg (px 2.)
                                        ])
                                declaration
                                    "width"
                                    lengthContext
                                    length
                                    (arguments
                                        "calc-mix"
                                        [
                                            Ranvier.Css.CalculationArgument.WeightedValue (px 1., Some 50.)
                                            Ranvier.Css.CalculationArgument.WeightedValue (px 3., None)
                                        ])
                                declaration "z-index" context number (call "sibling-index" [])
                                declaration
                                    "width"
                                    lengthContext
                                    length
                                    (Ranvier.Css.CalculationRuntime.interpolate
                                        (Ranvier.Css.CalculationProgress.Value (literal "number" "" 0.5))
                                        (Some (Ranvier.Css.CalculationEasing.Keyword "ease"))
                                        None
                                        [
                                            {
                                                Positions = [ literal "number" "" 0. ]
                                                Value = px 10.
                                                EasingBefore = None
                                            }
                                            {
                                                Positions = [ literal "number" "" 1. ]
                                                Value = px 20.
                                                EasingBefore = None
                                            }
                                        ])
                                Ranvier.Css.Style.declarations
                                    [
                                        Ranvier.Css.CalculationRuntime.declarationIntrinsic
                                            "width"
                                            [ "auto" ]
                                            (Ranvier.Css.CalculationRuntime.intrinsic
                                                (Ranvier.Css.IntrinsicBasis.Keyword "auto")
                                                (binary "+" Ranvier.Css.CalculationRuntime.size (px 2.)))
                                    ]
                                Ranvier.Css.Style.declarations
                                    [
                                        Ranvier.Css.CalculationRuntime.declarationIntrinsic
                                            "width"
                                            [ "fit-content()" ]
                                            (Ranvier.Css.CalculationRuntime.intrinsic
                                                (Ranvier.Css.IntrinsicBasis.FitContent (px 20.))
                                                (binary "+" Ranvier.Css.CalculationRuntime.size (px 2.)))
                                    ]
                                Ranvier.Css.Style.declarations
                                    [
                                        Ranvier.Css.CalculationRuntime.declarationIntrinsic
                                            "width"
                                            [ "stretch()" ]
                                            (Ranvier.Css.CalculationRuntime.intrinsic
                                                (Ranvier.Css.IntrinsicBasis.Stretch (px 20.))
                                                (binary "+" Ranvier.Css.CalculationRuntime.size (px 2.)))
                                    ]
                                Ranvier.Css.Style.dynamicWithKeywords
                                    [ "initial"; "inherit"; "unset"; "revert"; "revert-layer"; "revert-rule" ]
                                    "width"
                                    (fun () -> failwith "Runtime comparison must not sample a reader")
                            ]

                    let expectedModule =
                        {
                            Name =
                                "Fixture-"
                                + Ranvier.Css.Identity.hash "Fixture/Fixture.fs"
                            Dependencies = []
                            Rules = []
                            Styles = [ expected.Definition ]
                        }

                    for mode in
                        [
                            Options.defaults.Mode
                            OutputMode.SingleFile "app.css"
                            OutputMode.CssModules
                        ] do
                        let assets = { Options.defaults with Mode = mode }

                        let result =
                            extract
                                { ExtractionOptions.defaults with
                                    Assets = assets
                                }
                                project

                        match Compiler.compile assets [ expectedModule ] with
                        | Ok runtime -> Expect.equal result runtime "Extracted bytes and identities match the runtime calculation tree."
                        | Error errors -> failtest (String.concat "\n" errors)
            testCase "checked numeric adapters reach canonical unary functions in every asset mode"
            <| fun _ ->
                use manifest =
                    System.Text.Json.JsonDocument.Parse (
                        File.ReadAllText (Path.Combine (repository, "src/Ranvier.Css.Bindings/Generated/Css.manifest.json"))
                    )

                let field (name: string) (item: System.Text.Json.JsonElement) =
                    item.GetProperty(name).GetString()

                let rotate =
                    manifest.RootElement.GetProperty("owners").EnumerateArray()
                    |> Seq.find (fun item ->
                        field "memberName" item = "create"
                        && (item.GetProperty("path").EnumerateArray()
                            |> Seq.map _.GetString()
                            |> Seq.toList)
                            =
                            [ "Transform"; "Rotate" ])

                let angleAdapter =
                    manifest.RootElement.GetProperty("factories").EnumerateArray()
                    |> Seq.find (fun item ->
                        field "kind" item = "calculation"
                        && field "production" item = "angle"
                        && field "memberName" item = "calc")

                let argument =
                    "global."
                    + field "owner" angleAdapter
                    + ".calc (Css.deg 10. + Css.deg 20.)"

                let factory =
                    "global."
                    + manifest.RootElement.GetProperty("namespaceName").GetString()
                    + ".CssValue.Transform.Rotate."
                    + field "memberName" rotate
                    + " ("
                    + argument
                    + ")"

                temporary
                    $"""module Fixture
open Ranvier.Css
open Ranvier.Css.View
let example = utility {{
    Css.transform ({factory})
    Css.rotate (CssValue.Angle.calc (Css.deg 10. + Css.deg 20.))
    Css.zIndex (CssValue.Integer.calc (Css.number 2. + Css.number 1.))
}}
let view () = styles {{ example }}
do failwith "Canonical numeric bridge initializers must never execute"
"""
                <| fun project ->
                    let expected =
                        Ranvier.Css.Style.create
                            [
                                Ranvier.Css.Style.declarations [ Ranvier.Css.Declaration.create "transform" "rotate(calc(10deg + 20deg))" ]
                                Ranvier.Css.Style.declarations [ Ranvier.Css.Declaration.create "rotate" "calc(10deg + 20deg)" ]
                                Ranvier.Css.Style.declarations [ Ranvier.Css.Declaration.create "z-index" "calc(2 + 1)" ]
                            ]

                    let expectedModule =
                        {
                            Name =
                                "Fixture-"
                                + Ranvier.Css.Identity.hash "Fixture/Fixture.fs"
                            Dependencies = []
                            Rules = []
                            Styles = [ expected.Definition ]
                        }

                    for mode in
                        [
                            Options.defaults.Mode
                            OutputMode.SingleFile "app.css"
                            OutputMode.CssModules
                        ] do
                        let assets = { Options.defaults with Mode = mode }

                        let result =
                            extract
                                { ExtractionOptions.defaults with
                                    Assets = assets
                                }
                                project

                        match Compiler.compile assets [ expectedModule ] with
                        | Ok runtime -> Expect.equal result runtime "Canonical adapters preserve calculation bytes and style identities."
                        | Error errors -> failtest (String.concat "\n" errors)
            testCase "supports single file and CSS module asset modes"
            <| fun _ ->
                for mode in [ OutputMode.SingleFile "app.css"; OutputMode.CssModules ] do
                    let result =
                        extract
                            { ExtractionOptions.defaults with
                                Assets = { Options.defaults with Mode = mode }
                            }
                            fixture

                    Expect.isNonEmpty result.Assets "Existing asset modes are used."
            testCase "typed numeric custom properties retain references and fallbacks without sampling values"
            <| fun _ ->
                use manifest =
                    System.Text.Json.JsonDocument.Parse (
                        File.ReadAllText (Path.Combine (repository, "src/Ranvier.Css.Bindings/Generated/Css.manifest.json"))
                    )

                let lengthVariable =
                    manifest.RootElement.GetProperty("factories").EnumerateArray()
                    |> Seq.find (fun item ->
                        item.GetProperty("kind").GetString() = "numericVariable"
                        && item.GetProperty("production").GetString() = "length"
                        && item.GetProperty("argumentType").GetString() = "name")
                    |> fun item -> item.GetProperty("owner").GetString()

                temporary
                    $"""module Fixture
open Ranvier.Css
open Ranvier.Css.View
let spacing = global.{lengthVariable}.variableWithFallback "--spacing" (Css.rem 2.)
let coreSpacing: CssValue.Variable<CssValue.Length.Value> = CssValue.variable "--core-spacing"
let alias = coreSpacing
let example = utility {{
    Css.width (CssValue.Length.calc (spacing + Css.px 8.))
    Css.height alias
}}
let view () = styles {{ example }}
do failwith "Numeric variable initializers must never execute"
"""
                <| fun project ->
                    let result = extract ExtractionOptions.defaults project

                    let length =
                        Ranvier.Css.Spec.NumericSemantics.dimension Ranvier.Css.Spec.NumericDimension.Length

                    let context = Ranvier.Css.Spec.NumericSemantics.dimensionOnlyContext

                    let width =
                        Ranvier.Css.CalculationRuntime.binary
                            "+"
                            (Ranvier.Css.CalculationRuntime.reference
                                "length"
                                "--spacing"
                                (Some (Ranvier.Css.CalculationRuntime.literal "length" "rem" 2.)))
                            (Ranvier.Css.CalculationRuntime.literal "length" "px" 8.)

                    let height = Ranvier.Css.Declaration.create "height" "var(--core-spacing)"

                    let expected =
                        Ranvier.Css.Style.create
                            [
                                Ranvier.Css.Style.declarations [ Ranvier.Css.CalculationRuntime.declaration "width" context length width ]
                                Ranvier.Css.Style.declarations [ height ]
                            ]

                    let css =
                        result.Assets
                        |> List.map _.Content
                        |> String.concat "\n"

                    Expect.stringContains css ("." + expected.Definition.ClassName) "Reference identity matches the runtime tree"

                    Expect.stringContains
                        css
                        (Ranvier.Css.CalculationRuntime.render width)
                        "Fallback arithmetic is serialized without sampling the variable"

                    Expect.stringContains
                        css
                        (Ranvier.Css.Declaration.value height)
                        "Core custom-property constructors and names remain deterministic"
            testCase "used I/O returns a source diagnostic"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet live = utility { Declaration.create \"color\" (System.IO.File.ReadAllText \"missing\") }\nlet view () = styles { live }\n"
                <| fun project ->
                    match Extractor.extract ExtractionOptions.defaults project with
                    | Ok _ -> failtest "I/O cannot be interpreted."
                    | Error errors ->
                        Expect.stringContains errors.Head.Message "ReadAllText" "The unsupported operation is named."
                        Expect.equal errors.Head.Line 4 "The expression has its source line."
            testCase "numeric specification guards remain enforced"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet invalid = utility { Css.width (Css.px -1.) }\nlet view () = styles { invalid }\n"
                <| fun project ->
                    match Extractor.extract ExtractionOptions.defaults project with
                    | Ok _ -> failtest "Negative width must fail the generated numeric guard."
                    | Error errors -> Expect.stringContains errors.Head.Message "range" "Canonical numeric guards run."
            testCase "unbounded recursion is diagnosed"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet rec forever value = forever value\nlet invalid = utility { Declaration.create \"width\" (forever \"x\") }\nlet view () = styles { invalid }\n"
                <| fun project ->
                    match Extractor.extract ExtractionOptions.defaults project with
                    | Ok _ -> failtest "Recursive helpers must be bounded."
                    | Error errors -> Expect.stringContains errors.Head.Message "depth" "The recursion guard prevents nontermination."
            testCase "mutable state is never sampled"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet mutable size = 1.\nlet invalid = utility { Css.width (Css.px size) }\nlet view () = styles { invalid }\n"
                <| fun project ->
                    match Extractor.extract ExtractionOptions.defaults project with
                    | Ok _ -> failtest "Mutable initial values cannot stand in for application state."
                    | Error errors -> Expect.stringContains errors.Head.Message "Mutable" "Mutable inputs are identified."
            testCase "static conditional empty fragments and utility parameters compile"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet baseStyle = utility { Css.width (Css.px 12.) }\nlet wrap (selected: bool) (item: Utility) = utility { if selected then item }\nlet composed = wrap true baseStyle\nlet view () = styles { composed }\n"
                <| fun project ->
                    let result = extract ExtractionOptions.defaults project
                    Expect.stringContains result.Assets.Head.Content "width: 12px" "Source helpers accept utilities and fixed control flow."
            testCase "oversized finite loops produce bounded source diagnostics"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet invalid = utility { for index in [1 .. 10001] do Css.marginLeft (Css.px (float index)) }\nlet view () = styles { invalid }\n"
                <| fun project ->
                    match Extractor.extract ExtractionOptions.defaults project with
                    | Ok _ -> failtest "Loop bounds must prevent excessive static work."
                    | Error errors -> Expect.stringContains errors.Head.Message "bound" "Finite bounds are enforced."
            testCase "CLI failures leave prior assets untouched"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet invalid = utility { Css.width (Css.px -1.) }\nlet view () = styles { invalid }\n"
                <| fun project ->
                    let output = Path.Combine (Path.GetDirectoryName project, "assets")
                    Directory.CreateDirectory output |> ignore
                    let sentinel = Path.Combine (output, "app.css")
                    File.WriteAllText (sentinel, "prior CSS")

                    let result =
                        Program.main [| "--project"; project; "--output"; output; "--single"; "app.css" |]

                    Expect.equal result 1 "Extraction errors fail the command."
                    Expect.equal (File.ReadAllText sentinel) "prior CSS" "No output is written until validation succeeds."
            testCase "float to integer helpers preserve F# truncation"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet trunc value = float (int value)\nlet example = utility { for value in [1.8; -1.8] do Css.marginLeft (Css.px (trunc value)) }\nlet view () = styles { example }\n"
                <| fun project ->
                    let result = extract ExtractionOptions.defaults project
                    let css = result.Assets.Head.Content
                    Expect.stringContains css "margin-left: 1px" "Positive conversion truncates."
                    Expect.stringContains css "margin-left: -1px" "Negative conversion truncates toward zero."
            testCase "float ranges are diagnosed rather than rounded"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet example = utility { for value in [1.5 .. 2.5] do Css.marginLeft (Css.px value) }\nlet view () = styles { example }\n"
                <| fun project ->
                    match Extractor.extract ExtractionOptions.defaults project with
                    | Ok _ -> failtest "Only supported int32 ranges may be interpreted."
                    | Error errors -> Expect.stringContains errors.Head.Message "int32" "Non-integer range semantics are explicit."
            testCase "zero primitives and keywords retain their serialized values"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet breakpoint = minViewportWidth Css.zero\nlet example = utility { Css.opacity (Css.number 0.); Css.color CssValue.Color.tan; Css.cursor CssValue.Cursor.progress; Css.borderImageRepeat CssValue.Border.Image.Repeat.round; breakpoint { Css.width CssValue.Width.auto } }\nlet view () = styles { example }\n"
                <| fun project ->
                    let result = extract ExtractionOptions.defaults project
                    Expect.stringContains result.Assets.Head.Content "opacity: 0" "Zero numeric tokens are serialized correctly."
                    Expect.stringContains result.Assets.Head.Content "min-width: 0" "The zero production retains its literal token."
                    Expect.stringContains result.Assets.Head.Content "width: auto" "Generated keyword tokens are serialized correctly."
                    Expect.stringContains result.Assets.Head.Content "color: tan" "A keyword getter is distinct from the tangent function."
                    Expect.stringContains result.Assets.Head.Content "cursor: progress" "A keyword getter is distinct from the progress function."

                    Expect.stringContains
                        result.Assets.Head.Content
                        "border-image-repeat: round"
                        "A keyword getter is distinct from the rounding function."
            testCase "root framework selection preserves referenced library targets"
            <| fun _ ->
                let result =
                    extract
                        { ExtractionOptions.defaults with
                            Framework = Some "net10.0"
                        }
                        fixture

                let css =
                    result.Assets
                    |> List.map _.Content
                    |> String.concat "\n"

                Expect.stringContains css "box-sizing: border-box" "The net8 library retains its evaluated target when the root selects net10."
        ]

[<EntryPoint>]
/// <summary>Runs the complete dedicated source-extraction suite.</summary>
let main arguments =
    runTestsWithCLIArgs [] arguments tests
