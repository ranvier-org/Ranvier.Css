module Ranvier.Css.Spec.GenerationTests

open System
open System.IO
open System.Text
open System.Text.Json
open Expecto
open Ranvier.Css.Spec

let private catalogue () =
    let json =
        """{"properties":[{"name":"width","href":"https://example.test/width","syntax":"auto | <length [0,100]>"}],"types":[{"name":"length","href":"https://example.test/length"}],"functions":[],"selectors":[],"atrules":[]}"""

    match Webref.consume "generation" (Encoding.UTF8.GetBytes json) with
    | Ok value -> value
    | Error errors -> failtestf "%A" errors

let private generate catalogue =
    match Generation.generate "Example.Generated" catalogue with
    | Ok value -> value
    | Error errors -> failtestf "%A" errors

let private compactCatalogue () =
    let json =
        """{"properties":[
            {"name":"all","href":"https://example.test/all","syntax":"initial | inherit | unset | revert | revert-layer | revert-rule"},
            {"name":"display","href":"https://example.test/display","syntax":"<display-outside> | <display-box>"},
            {"name":"width","href":"https://example.test/width","syntax":"auto | <length-percentage>"},
            {"name":"color","href":"https://example.test/color-property","syntax":"<color>"},
            {"name":"background-color","href":"https://example.test/background-color","syntax":"<color>"},
            {"name":"pair","href":"https://example.test/pair","syntax":"<length> <length>"},
            {"name":"future-prose","href":"https://example.test/future-prose"},
            {"name":"unknown","href":"https://example.test/unknown","syntax":"<not-defined-in-test>"}
        ],"types":[
            {"name":"display-outside","href":"https://example.test/display-outside","syntax":"block | inline"},
            {"name":"display-box","href":"https://example.test/display-box","syntax":"contents | none"},
            {"name":"length","href":"https://example.test/length"},
            {"name":"percentage","href":"https://example.test/percentage"},
            {"name":"length-percentage","href":"https://example.test/mixed","syntax":"<length> | <percentage>"},
            {"name":"color","href":"https://example.test/color","syntax":"<named-color> | currentcolor"},
            {"name":"named-color","href":"https://example.test/named-color","syntax":"red | blue"}
        ],"functions":[],"selectors":[],"atrules":[]}"""

    Webref.consume "compact-generation" (Encoding.UTF8.GetBytes json)
    |> Result.toOption
    |> Option.defaultWith (fun () -> failtest "The compact generation fixture must be valid.")

let private compactOutput input =
    RuntimeGeneration.generate "Example.Compact" input
    |> Result.toOption
    |> Option.defaultWith (fun () -> failtest "The compact fixture must generate.")

let private manifestPath (item: JsonElement) =
    item.GetProperty("path").EnumerateArray()
    |> Seq.map _.GetString()
    |> Seq.toList

let private compactCases =
    testList
        "compact public bindings"
        [
            testCase "default generation separates declarations from discoverable value owners"
            <| fun _ ->
                let input = compactCatalogue ()
                let output = compactOutput input
                use manifest = JsonDocument.Parse output.Manifest
                let root = manifest.RootElement
                Expect.equal (root.GetProperty("schemaVersion").GetInt32()) 2 "The default runtime generator exposes the compact contract."
                Expect.equal (root.GetProperty("namespaceName").GetString()) "Example.Compact" "The requested namespace is retained."

                let properties =
                    root.GetProperty("properties").EnumerateArray()
                    |> Seq.toList

                Expect.equal properties.Length 8 "Every fixture property remains represented."

                Expect.isTrue
                    (properties
                     |> List.forall (fun property -> property.GetProperty("owner").GetString() = "Example.Compact.Css"))
                    "Declaration sinks belong to Css."

                let owners =
                    root.GetProperty("owners").EnumerateArray()
                    |> Seq.toList

                Expect.isTrue
                    (owners
                     |> List.exists (fun owner -> manifestPath owner = [ "Width" ]))
                    "Values have semantic property owners."

                Expect.isTrue
                    (owners
                     |> List.exists (fun owner -> manifestPath owner = [ "LengthPercentage" ]))
                    "Reusable mixed categories remain root values."

                Expect.stringContains output.Source "module CssValue =" "The generated public value entry point is present."
                Expect.stringContains output.Source "type Css =" "The generated public declaration entry point is present."

                Expect.isTrue
                    (output =
                        compactOutput
                            { input with
                                Features = List.rev input.Features
                            })
                    "Public artifacts are independent of catalogue ordering."

            testCase "property keywords remain discoverable through referenced shared grammars"
            <| fun _ ->
                let output = compactOutput (compactCatalogue ())
                use manifest = JsonDocument.Parse output.Manifest

                let factories =
                    manifest.RootElement.GetProperty("factories").EnumerateArray()
                    |> Seq.toList

                for keyword in [ "block"; "none" ] do
                    let factory =
                        factories
                        |> List.find (fun factory ->
                            factory.GetProperty("owner").GetString() = "Example.Compact.CssValue.Display"
                            && factory.GetProperty("memberName").GetString() = keyword)

                    Expect.equal (factory.GetProperty("kind").GetString()) "token" "Referenced keywords retain token provenance."

                let wide =
                    factories
                    |> List.find (fun factory ->
                        factory.GetProperty("owner").GetString() = "Example.Compact.CssValue.All"
                        && factory.GetProperty("memberName").GetString() = "initial")

                Expect.equal (wide.GetProperty("kind").GetString()) "wide" "All-property aliases retain the static-only CSS-wide type."

            testCase "mixed numeric owners admit both branches through the exported grammar"
            <| fun _ ->
                let output = compactOutput (compactCatalogue ())
                use manifest = JsonDocument.Parse output.Manifest
                let root = manifest.RootElement

                let table =
                    Ranvier.Css.Internal.GrammarRuntime.Table.decode (root.GetProperty("encodedGrammar").GetString())

                let mixed =
                    root.GetProperty("owners").EnumerateArray()
                    |> Seq.find (fun owner -> manifestPath owner = [ "LengthPercentage" ])
                    |> fun owner -> owner.GetProperty("nodeId").GetInt32()

                let factories =
                    root.GetProperty("factories").EnumerateArray()
                    |> Seq.toList

                for owner, memberName, suffix in
                    [
                        "Example.Compact.CssValue.Length", "px", "px"
                        "Example.Compact.CssValue.Percentage", "percent", "%"
                    ] do
                    let id =
                        factories
                        |> List.find (fun factory ->
                            factory.GetProperty("owner").GetString() = owner
                            && factory.GetProperty("memberName").GetString() = memberName)
                        |> fun factory -> factory.GetProperty("nodeId").GetInt32()

                    let value = Ranvier.Css.Internal.GrammarRuntime.number table id 42. suffix

                    Expect.equal
                        (Ranvier.Css.Internal.GrammarRuntime.admit table mixed value).Text
                        value.Text
                        "Both numeric branches enter the common named category."

            testCase "owner factory and diagnostic metadata contain their complete fields"
            <| fun _ ->
                let input = compactCatalogue ()
                let output = compactOutput input
                use manifest = JsonDocument.Parse output.Manifest
                let root = manifest.RootElement
                Expect.equal (root.GetProperty("featureCount").GetInt32()) input.Features.Length "Feature coverage is complete."
                let nodeCount = root.GetProperty("nodeCount").GetInt32()

                for owner in root.GetProperty("owners").EnumerateArray() do
                    Expect.isGreaterThan (owner.EnumerateObject () |> Seq.length) 0 "Internal records cannot serialize as empty objects."

                    Expect.isTrue
                        (manifestPath owner
                         |> List.forall (String.IsNullOrWhiteSpace >> not))
                        "Every owner segment is named."

                    Expect.isFalse (String.IsNullOrWhiteSpace (owner.GetProperty("memberName").GetString())) "Every callable is named."
                    let id = owner.GetProperty("nodeId").GetInt32()
                    Expect.isTrue (id >= 0 && id < nodeCount) "Owner identities belong to the exported graph."

                    for role in owner.GetProperty("roles").EnumerateArray() do
                        Expect.isGreaterThanOrEqual (role.GetProperty("index").GetInt32()) 0 "Roles preserve original grammar positions."
                        Expect.isFalse (String.IsNullOrWhiteSpace (role.GetProperty("name").GetString())) "Roles retain their names."

                let issues =
                    root.GetProperty("ownerIssues").EnumerateArray()
                    |> Seq.toList

                Expect.isNonEmpty issues "The ambiguous pair requires explicit naming policy."

                for issue in issues do
                    Expect.isFalse (String.IsNullOrWhiteSpace (issue.GetProperty("message").GetString())) "Policy diagnostics remain serialized."
                    issue.GetProperty("nodeId").GetInt32() |> ignore

                    issue.GetProperty("path").GetArrayLength()
                    |> ignore

                    issue.GetProperty("memberName").GetString()
                    |> ignore

                let unresolved =
                    root.GetProperty("unresolved").EnumerateArray()
                    |> Seq.toList

                Expect.equal unresolved.Length 1 "Unknown references remain explicit rather than inferred."

                Expect.equal
                    (unresolved.Head.GetProperty("reference").GetString())
                    "not-defined-in-test"
                    "The unresolved production retains its spelling."

            testCase "prose and rejected definitions remain explicitly opaque"
            <| fun _ ->
                let input = compactCatalogue ()

                let prose =
                    input.Features
                    |> List.find (fun feature -> feature.Id.Name = "future-prose")

                let rejected =
                    { prose with
                        Id =
                            { prose.Id with
                                Name = "rejected-prose"
                            }
                        Grammar = GrammarStatus.Rejected ("[", { Offset = 1; Message = "incomplete" })
                    }

                let output =
                    compactOutput
                        { input with
                            Features = rejected :: input.Features
                        }

                use manifest = JsonDocument.Parse output.Manifest

                let definitions =
                    manifest.RootElement.GetProperty("definitions").EnumerateArray()
                    |> Seq.toList

                for name, grammar in [ "future-prose", "missing"; "rejected-prose", "rejected" ] do
                    let definition =
                        definitions
                        |> List.find (fun definition -> definition.GetProperty("name").GetString() = name)

                    Expect.equal (definition.GetProperty("grammar").GetString()) grammar "Opaque coverage does not pretend to enforce prose."

                Expect.equal output.UnresolvedCount 1 "Unsupported grammar and unresolved references remain separate."
                Expect.stringContains output.Source "unsafeFromTokens" "Unchecked entry points remain explicitly named."

            testCase "color properties reuse the shared palette"
            <| fun _ ->
                let output = compactOutput (compactCatalogue ())
                use manifest = JsonDocument.Parse output.Manifest

                let red =
                    manifest.RootElement.GetProperty("factories").EnumerateArray()
                    |> Seq.find (fun factory ->
                        factory.GetProperty("owner").GetString() = "Example.Compact.CssValue.Color"
                        && factory.GetProperty("memberName").GetString() = "red")

                Expect.equal (red.GetProperty("kind").GetString()) "token" "The palette retains shared keyword evidence."
                Expect.stringContains output.Source "module Colors = Palette" "Color-accepting properties expose the shared Colors vocabulary."

                let table =
                    Ranvier.Css.Internal.GrammarRuntime.Table.decode (manifest.RootElement.GetProperty("encodedGrammar").GetString())

                let background =
                    manifest.RootElement.GetProperty("properties").EnumerateArray()
                    |> Seq.find (fun property -> property.GetProperty("cssName").GetString() = "background-color")
                    |> fun property -> property.GetProperty("nodeId").GetInt32()

                let value = Ranvier.Css.Internal.GrammarRuntime.token "red"

                Expect.equal
                    (Ranvier.Css.Internal.GrammarRuntime.admit table background value).Text
                    "red"
                    "The property and palette share runtime admission."
        ]


/// <summary>Checks deterministic generation, coverage, obsolescence, and output collision protection.</summary>
let tests =
    testList
        "CSS binding generation"
        [
            compactCases

            testCase "shared runtime emits value categories instead of keyword overload products"
            <| fun _ ->
                let output =
                    RuntimeGeneration.generateLegacy "Example.Generated" (catalogue ())
                    |> Result.toOption
                    |> Option.get

                Expect.stringContains output.Source "module CssValueCategories" "Values expose acceptance interfaces"
                Expect.stringContains output.Source "IPropertyWidth" "Width has a common representation"
                Expect.stringContains output.Source "CssKeywords.Keyword_" "Keywords retain stable type identities"
                Expect.isFalse (output.Source.Contains "(_: global.Ranvier.Css.Value<CssKeywords.") "Property helpers share keyword admission"

            testCase "deprecated primitive constructors retain their value category adapters"
            <| fun _ ->
                let input = catalogue ()

                let deprecated =
                    { input with
                        Features =
                            input.Features
                            |> List.map (fun feature ->
                                if feature.Id.Kind = FeatureKind.Type then
                                    { feature with
                                        Metadata = feature.Metadata.Add ("deprecated", "true")
                                    }
                                else
                                    feature)
                    }

                let output = RuntimeGeneration.generate "Example.Generated" deprecated
                Expect.isOk output "Deprecated types still generate their constructors"

            testCase "identical grammars share marker classes and distinct ranges remain separate"
            <| fun _ ->
                let input = catalogue ()

                let property =
                    input.Features
                    |> List.find (fun feature -> feature.Id.Kind = FeatureKind.Property)

                let duplicate =
                    { property with
                        Id = { property.Id with Name = "height" }
                        Href = "https://example.test/height"
                    }

                let countMarkers (output: GeneratedBindings) =
                    output.Source.Split ('\n')
                    |> Array.filter (fun line -> line.Contains (" private () = class end"))
                    |> Array.length

                let originalCount = generate input |> countMarkers

                let shared =
                    generate
                        { input with
                            Features = duplicate :: input.Features
                        }

                Expect.equal (countMarkers shared) originalCount "Equivalent grammars share their CLR markers"
                use sharedManifest = JsonDocument.Parse shared.Manifest

                let propertyDefinitions =
                    sharedManifest.RootElement.GetProperty("definitions").EnumerateArray()
                    |> Seq.filter (fun definition -> definition.GetProperty("kind").GetString() = "Property")
                    |> Seq.toList

                for field in [ "marker"; "syntaxFactory" ] do
                    let identities =
                        propertyDefinitions
                        |> List.map (fun definition -> definition.GetProperty(field).GetString())
                        |> List.distinct

                    Expect.equal identities.Length 1 "Manifest identities match the shared runtime representation"

                let bounded =
                    { duplicate with
                        Grammar =
                            GrammarStatus.Parsed (
                                "auto | <length [0,50]>",
                                Grammar.parse "auto | <length [0,50]>"
                                |> Result.toOption
                                |> Option.get
                            )
                    }

                let separate =
                    generate
                        { input with
                            Features = bounded :: input.Features
                        }

                Expect.isGreaterThan (countMarkers separate) originalCount "Different bounds have distinct markers"

            testCase "canonical extraction factories follow exact unrestricted grammar nodes"
            <| fun _ ->
                let json =
                    """{"properties":[{"name":"guarded","href":"https://example.test/guarded","syntax":"<angle [0,90]>"}],"types":[{"name":"angle","href":"https://example.test/angle"}],"functions":[{"name":"rotate()","href":"https://example.test/rotate","syntax":"rotate(<angle> | <zero>)"}],"selectors":[],"atrules":[]}"""

                let output =
                    Webref.consume "canonical-extraction" (Encoding.UTF8.GetBytes json)
                    |> Result.toOption
                    |> Option.get
                    |> generate

                use manifest = JsonDocument.Parse output.Manifest

                let field (name: string) (item: JsonElement) =
                    item.GetProperty(name).GetString()

                let factories =
                    manifest.RootElement.GetProperty("extractionFactories").EnumerateArray()
                    |> Seq.toList

                Expect.isNonEmpty factories "The grammar supplies safe factories"

                Expect.isTrue
                    (factories
                     |> List.forall (fun item -> List.contains (field "kind" item) [ "copy"; "function" ]))
                    "The interpreter receives a bounded operation set"

                let rotate =
                    factories
                    |> List.find (fun item ->
                        field "owner" item = "Example.Generated.Css.FunctionRotate"
                        && field "memberName" item = "create")

                Expect.equal (field "functionName" rotate) "rotate" "Function metadata follows the grammar name"

                let guarded =
                    manifest.RootElement.GetProperty("definitions").EnumerateArray()
                    |> Seq.find (fun item -> field "name" item = "guarded")

                Expect.isFalse
                    (factories
                     |> List.exists (fun item -> field "memberName" item = field "syntaxFactory" guarded))
                    "Ranged references cannot become unguarded copies"

                Expect.isFalse
                    (factories
                     |> List.exists (fun item -> field "owner" item = "Example.Generated.Css.PropertyGuarded"))
                    "Ranged canonical aliases fail closed too"
            testCase "intrinsic admission follows whole-value inline functions and excludes none"
            <| fun _ ->
                let json =
                    """{"properties":[{"name":"maximum","href":"https://example.test/max","syntax":"none | auto | fit-content(<length>) | calc-size(auto, <length>)"},{"name":"future-size","href":"https://example.test/stretch","syntax":"auto | stretch(<length>) | calc-size(auto, <length>)"},{"name":"required-pair","href":"https://example.test/pair","syntax":"calc-size(auto, <length>) fit-content(<length>)"}],"types":[{"name":"length","href":"https://example.test/length"}],"functions":[],"selectors":[],"atrules":[]}"""

                let input =
                    Webref.consume "inline-intrinsic" (Encoding.UTF8.GetBytes json)
                    |> Result.toOption
                    |> Option.get

                let output =
                    RuntimeGeneration.generateLegacy "Example.Generated" input
                    |> Result.toOption
                    |> Option.get

                use manifest = JsonDocument.Parse output.Manifest

                let sinks =
                    manifest.RootElement
                        .GetProperty("numericSemantics")
                        .GetProperty("runtime")
                        .GetProperty("contextual")
                        .GetProperty("sinks")
                        .EnumerateArray()
                    |> Seq.toList

                let allowed name =
                    sinks
                    |> List.find (fun item -> item.GetProperty("property").GetString() = name)
                    |> fun item ->
                        item.GetProperty("allowedKeywords").EnumerateArray()
                        |> Seq.map _.GetString()
                        |> Set.ofSeq

                Expect.contains (allowed "maximum") "fit-content()" "An inline sizing function is admitted through the complete grammar path"
                Expect.isFalse ((allowed "maximum").Contains "none") "A property keyword cannot override the normative intrinsic exclusion"
                Expect.contains (allowed "future-size") "stretch()" "New sizing functions retain their separate admission"

                Expect.isFalse
                    (sinks
                     |> List.exists (fun item -> item.GetProperty("property").GetString() = "required-pair"))
                    "A function requiring a sibling cannot stand alone"
            testCase "numeric primitives cover current source unit sets in canonical and sugar APIs"
            <| fun _ ->
                let groups =
                    [
                        "length",
                        [
                            "em"
                            "rem"
                            "ex"
                            "rex"
                            "cap"
                            "rcap"
                            "ch"
                            "rch"
                            "ic"
                            "ric"
                            "lh"
                            "rlh"
                            "vw"
                            "svw"
                            "lvw"
                            "dvw"
                            "vh"
                            "svh"
                            "lvh"
                            "dvh"
                            "vi"
                            "svi"
                            "lvi"
                            "dvi"
                            "vb"
                            "svb"
                            "lvb"
                            "dvb"
                            "vmin"
                            "svmin"
                            "lvmin"
                            "dvmin"
                            "vmax"
                            "svmax"
                            "lvmax"
                            "dvmax"
                            "cm"
                            "mm"
                            "q"
                            "in"
                            "pc"
                            "pt"
                            "px"
                            "cqw"
                            "cqh"
                            "cqi"
                            "cqb"
                            "cqmin"
                            "cqmax"
                        ]
                        "angle", [ "deg"; "grad"; "rad"; "turn" ]
                        "time", [ "s"; "ms" ]
                        "frequency", [ "Hz"; "kHz" ]
                        "resolution", [ "dpi"; "dpcm"; "dppx"; "x" ]
                        "flex", [ "fr" ]
                    ]

                let input =
                    let types =
                        groups
                        |> List.map (fun (production, _) ->
                            {|
                                name = production
                                href = "https://example.test/" + production
                            |})

                    JsonSerializer.Serialize
                        {|
                            properties = [||]
                            types = types
                            functions = [||]
                            selectors = [||]
                            atrules = [||]
                        |}
                    |> Encoding.UTF8.GetBytes
                    |> Webref.consume "numeric-unit-coverage"
                    |> Result.toOption
                    |> Option.get

                for output in
                    [
                        generate input
                        RuntimeGeneration.generateLegacy "Example.Generated" input
                        |> Result.toOption
                        |> Option.get
                    ] do
                    use manifest = JsonDocument.Parse output.Manifest

                    for production, expected in groups do
                        let definition =
                            manifest.RootElement.GetProperty("definitions").EnumerateArray()
                            |> Seq.find (fun item -> item.GetProperty("name").GetString() = production)

                        let methods =
                            definition.GetProperty("primitiveHelpers").EnumerateArray()
                            |> Seq.map (fun item -> item.GetProperty("methodName").GetString())
                            |> Set.ofSeq

                        Expect.equal methods (Set.ofList expected) (production + " exposes every supported suffix")

                        for unitName in expected do
                            Expect.stringContains
                                output.Source
                                ("Runtime.number value "
                                 + JsonSerializer.Serialize unitName)
                                (production + " preserves its suffix")
            testCase "numeric contexts retain primitive fallbacks and leave unknown dimensions unclassified"
            <| fun _ ->
                let json =
                    """{"properties":[{"name":"measure","href":"https://example.test/measure","syntax":"<length>"},{"name":"opaque","href":"https://example.test/opaque","syntax":"<dimension>"},{"name":"future","href":"https://example.test/future","syntax":"<future-type>"}],"types":[],"functions":[],"selectors":[],"atrules":[]}"""

                let input =
                    Webref.consume "fallbacks" (Encoding.UTF8.GetBytes json)
                    |> Result.toOption
                    |> Option.get

                let output = generate input
                use manifest = JsonDocument.Parse output.Manifest
                let properties = NumericProjection.derive manifest.RootElement

                let contexts name =
                    properties
                    |> List.find (fun property -> property.Name = name)
                    |> _.Contexts

                Expect.equal ((contexts "measure").Length) 1 "Resolved lexical fallbacks retain known numeric semantics"
                Expect.isEmpty (contexts "opaque") "A generic dimension cannot imply length"
                Expect.isEmpty (contexts "future") "Unknown numeric semantics stay unknown"
            testCase "numeric contexts follow whole-value conversion paths and explicit percentage bases"
            <| fun _ ->
                let json =
                    """{"properties":[{"name":"width","href":"https://example.test/width","syntax":"<length-percentage [0,100]>"},{"name":"opacity","href":"https://example.test/opacity","syntax":"<number> | <percentage>"},{"name":"z-index","href":"https://example.test/z-index","syntax":"auto | <integer>"},{"name":"pair","href":"https://example.test/pair","syntax":"<length> <length>"},{"name":"color","href":"https://example.test/color","syntax":"rgb(<number>)"}],"types":[{"name":"length-percentage","href":"https://example.test/mixed","syntax":"<length> | <percentage>"},{"name":"length","href":"https://example.test/length"},{"name":"number","href":"https://example.test/number"},{"name":"percentage","href":"https://example.test/percentage"},{"name":"integer","href":"https://example.test/integer","syntax":"<number-token>"},{"name":"number-token","href":"https://example.test/token"}],"functions":[],"selectors":[],"atrules":[]}"""

                let input =
                    Webref.consume "numeric-contexts" (Encoding.UTF8.GetBytes json)
                    |> Result.toOption
                    |> Option.get

                let output = generate input
                use manifest = JsonDocument.Parse output.Manifest
                let semantics = manifest.RootElement.GetProperty "numericSemantics"
                Expect.equal (semantics.GetProperty("schemaVersion").GetInt32()) NumericSemantics.schemaVersion "Independent semantic schema"

                use snapshot = JsonDocument.Parse (Snapshot.toJson input)
                let rules = snapshot.RootElement.GetProperty "numericSemantics"

                Expect.equal
                    (rules.GetProperty("functions").GetArrayLength())
                    NumericSemantics.functionPolicies.Length
                    "The exported domain includes executable function policies"

                Expect.equal
                    (rules.GetProperty("operators").GetArrayLength())
                    NumericSemantics.operatorPolicies.Length
                    "The exported domain includes arithmetic rules"

                let contexts name =
                    semantics.GetProperty("properties").EnumerateArray()
                    |> Seq.find (fun property -> property.GetProperty("name").GetString() = name)
                    |> fun property ->
                        property.GetProperty("contexts").EnumerateArray()
                        |> Seq.toList

                let width = contexts "width"

                let mixed =
                    width
                    |> List.find (fun context -> context.GetProperty("production").GetString() = "length-percentage")

                Expect.equal (mixed.GetProperty("percentageBasis").GetString()) "length" "Explicit mixed production resolves percentages"
                let mixedRange = mixed.GetProperty("ranges")[0]
                Expect.equal (mixedRange.GetProperty("maximum").GetProperty("value").GetString()) "100" "Bounds retained"

                for context in width do
                    Expect.equal
                        (context.GetProperty("percentageBasis").GetString())
                        "length"
                        "The percentage basis applies throughout the property's calculations"

                for context in contexts "opacity" do
                    Expect.equal
                        (context.GetProperty("percentageBasis").ValueKind)
                        JsonValueKind.Null
                        "Independent alternatives cannot authorize mixed arithmetic"

                let integer = contexts "z-index" |> List.exactlyOne

                Expect.equal
                    (integer.GetProperty("integerRounding").GetString())
                    "nearest-ties-positive-infinity"
                    "Calculations round at the integer sink"

                Expect.isEmpty (contexts "pair") "A descendant dimension cannot satisfy required siblings"
                Expect.isEmpty (contexts "color") "Function arguments cannot become whole-property calculations"

                let projected = NumericProjection.derive manifest.RootElement

                let property name =
                    projected
                    |> List.find (fun property -> property.Name = name)

                let mixedLength = NumericSemantics.percentage (Some NumericDimension.Length)

                Expect.isTrue
                    ((property "width").Contexts
                     |> List.exists (fun context -> NumericProjection.accepts context mixedLength))
                    "Width accepts a length-relative percentage result"

                Expect.isFalse
                    ((property "opacity").Contexts
                     |> List.exists (fun context -> NumericProjection.accepts context mixedLength))
                    "Opacity rejects a length percentage hint"

                let percent = NumericSemantics.percentage None

                let canceledPercent =
                    NumericSemantics.divide percent percent
                    |> Result.toOption
                    |> Option.get

                Expect.isTrue
                    ((property "opacity").Contexts
                     |> List.exists (fun context -> NumericProjection.accepts context canceledPercent))
                    "Number results retain the property's ordinary percentage context"

                let shared =
                    RuntimeGeneration.generateLegacy "NumericTest.Generated" input
                    |> Result.toOption
                    |> Option.get

                use sharedManifest = JsonDocument.Parse shared.Manifest

                for name in [ "properties"; "operators"; "functions" ] do
                    Expect.equal
                        (sharedManifest.RootElement.GetProperty("numericSemantics").GetProperty(name).GetRawText())
                        (semantics.GetProperty(name).GetRawText())
                        ("Both generators share authoritative numeric "
                         + name)

                Expect.equal
                    output
                    (generate
                        { input with
                            Features = List.rev input.Features
                        })
                    "Semantic output is deterministic"
            testCase "unified runtime manifest describes numeric expressions and the extraction allowlist"
            <| fun _ ->
                let json =
                    """{"properties":[{"name":"width","href":"https://example.test/width","syntax":"<length-percentage>"}],"types":[{"name":"length","href":"https://example.test/length"},{"name":"percentage","href":"https://example.test/percentage"},{"name":"length-percentage","href":"https://example.test/mixed","syntax":"<length> | <percentage>"}],"functions":[],"selectors":[],"atrules":[]}"""

                let input =
                    Webref.consume "numeric-runtime" (Encoding.UTF8.GetBytes json)
                    |> Result.toOption
                    |> Option.get

                let output =
                    RuntimeGeneration.generateLegacy "Example.Generated" input
                    |> Result.toOption
                    |> Option.get

                use manifest = JsonDocument.Parse output.Manifest

                let runtime =
                    manifest.RootElement.GetProperty("numericSemantics").GetProperty("runtime")

                Expect.equal (runtime.GetProperty("namespaceName").GetString()) "Example" "Math helpers share the short authoring namespace"
                Expect.equal (runtime.GetProperty("primitiveType").GetString()) "Css" "Numeric primitives use the unified entry point"
                Expect.isFalse (output.Source.Contains "type Calc ") "There is no separate Calc API"

                let primitives =
                    runtime.GetProperty("primitiveHelpers").EnumerateArray()
                    |> Seq.map (fun item -> item.GetProperty("memberName").GetString())
                    |> Set.ofSeq

                for name in [ "px"; "rem"; "cap"; "rcap"; "q"; "svmin"; "lengthPercent" ] do
                    Expect.isTrue
                        (primitives.Contains name)
                        (name
                         + " is available to arithmetic and extraction")

                let functions =
                    runtime.GetProperty("functionHelpers").EnumerateArray()
                    |> Seq.map (fun item -> item.GetProperty("name").GetString())
                    |> Set.ofSeq

                let policies =
                    NumericSemantics.functionPolicies
                    |> List.map _.Name
                    |> Set.ofList

                let contextualFunctions =
                    runtime.GetProperty("contextual").GetProperty("helpers").GetProperty("functionHelpers").EnumerateArray()
                    |> Seq.map (fun item -> item.GetProperty("name").GetString())
                    |> Seq.filter policies.Contains
                    |> Set.ofSeq

                Expect.equal (Set.union functions contextualFunctions) policies "Every numeric policy has an extraction allowlist entry"

                let constants =
                    runtime.GetProperty("constantHelpers").EnumerateArray()
                    |> Seq.map (fun item -> item.GetProperty("name").GetString())
                    |> Set.ofSeq

                Expect.equal constants (set [ "e"; "pi"; "infinity"; "-infinity"; "NaN" ]) "Symbolic constants have an explicit extraction allowlist"
                Expect.stringContains output.Source "CalculationExpression<CalculationUnits.length" "Property overloads accept generated expressions"
            testCase "sugar fills provably empty optional sequence components through canonical factories"
            <| fun _ ->
                let json =
                    """{"properties":[{"name":"gap","href":"https://example.test/gap","syntax":"<length [0,100]> <length [0,100]>?"},{"name":"prefix","href":"https://example.test/prefix","syntax":"auto? <length>"},{"name":"nested","href":"https://example.test/nested","syntax":"<length> [auto? none?]"},{"name":"choice","href":"https://example.test/choice","syntax":"<length> [auto? | none]"},{"name":"unordered","href":"https://example.test/unordered","syntax":"auto? && <length>"},{"name":"pair","href":"https://example.test/pair","syntax":"<length> <length>"},{"name":"required","href":"https://example.test/required","syntax":"<length> [auto?]!"}],"types":[{"name":"length","href":"https://example.test/length"}],"functions":[],"selectors":[],"atrules":[]}"""

                let input =
                    Webref.consume "optional-components" (Encoding.UTF8.GetBytes json)
                    |> Result.toOption
                    |> Option.get

                let output =
                    RuntimeGeneration.generateLegacy "Example.Generated" input
                    |> Result.toOption
                    |> Option.get

                use manifest = JsonDocument.Parse output.Manifest

                let definition name =
                    manifest.RootElement.GetProperty("definitions").EnumerateArray()
                    |> Seq.find (fun item -> item.GetProperty("name").GetString() = name)

                for name in [ "gap"; "prefix"; "nested"; "choice"; "unordered" ] do
                    let paths = (definition name).GetProperty("ergonomicValues")
                    Expect.equal (paths.GetArrayLength ()) 2 (name + " accepts a standalone length")

                    let path =
                        paths.EnumerateArray ()
                        |> Seq.find (fun item ->
                            item.GetProperty("expression").GetString()
                            <> "value")

                    Expect.stringContains (path.GetProperty("expression").GetString()) " []" "Empty repetition uses its checked canonical factory"

                for name in [ "pair"; "required" ] do
                    Expect.equal
                        ((definition name).GetProperty("ergonomicValues").GetArrayLength())
                        1
                        "Required components cannot be silently omitted"

                let gapPath =
                    (definition "gap").GetProperty("ergonomicValues").EnumerateArray()
                    |> Seq.find (fun item -> item.GetProperty("ranges").GetArrayLength() > 0)

                let gapRange = gapPath.GetProperty("ranges")[0]
                Expect.equal (gapRange.GetProperty("maximum").GetProperty("value").GetString()) "100" "Numeric guard remains authoritative"

                let gapSugar =
                    manifest.RootElement.GetProperty("sugar").GetProperty("properties").EnumerateArray()
                    |> Seq.find (fun item -> item.GetProperty("cssName").GetString() = "gap")

                Expect.equal (gapSugar.GetProperty("acceptedValues").GetArrayLength()) 2 "Importer receives the same safe conversion coverage"
            testCase "sugar manifest exports canonical numeric guards for CSS import"
            <| fun _ ->
                let output =
                    RuntimeGeneration.generateLegacy "Example.Generated" (catalogue ())
                    |> Result.toOption
                    |> Option.get

                use manifest = JsonDocument.Parse output.Manifest
                let sugar = manifest.RootElement.GetProperty ("sugar")
                Expect.equal (sugar.GetProperty("schemaVersion").GetInt32()) 2 "Guard-aware schema"

                let width =
                    sugar.GetProperty("properties").EnumerateArray()
                    |> Seq.find (fun property -> property.GetProperty("cssName").GetString() = "width")

                let bounded =
                    width.GetProperty("acceptedValues").EnumerateArray()
                    |> Seq.find (fun value -> value.GetProperty("ranges").GetArrayLength() > 0)

                let range = bounded.GetProperty("ranges")[0]
                Expect.equal (range.GetProperty("minimum").GetProperty("value").GetString()) "0" "Lower bound retained"
                Expect.equal (range.GetProperty("maximum").GetProperty("value").GetString()) "100" "Upper bound retained"
            testCase "unified sugar exposes typed values and readers in the shorter namespace"
            <| fun _ ->
                let output =
                    match RuntimeGeneration.generateLegacy "Example.Generated" (catalogue ()) with
                    | Ok output -> output
                    | Error errors -> failtestf "%A" errors

                Expect.stringContains output.Source "namespace ``Example``\n" "Default short namespace"
                Expect.stringContains output.Source "static member ``px``" "Shared typed length primitive"
                Expect.stringContains output.Source "static member ``width`` (value:" "Unified static overload"
                Expect.stringContains output.Source "static member ``width`` (read:" "Unified reader overload"
                Expect.stringContains output.Source "static member ``auto``" "Shared typed keyword"
                use manifest = JsonDocument.Parse output.Manifest

                let width =
                    manifest.RootElement.GetProperty("definitions").EnumerateArray()
                    |> Seq.find (fun item -> item.GetProperty("name").GetString() = "width")

                let paths =
                    width.GetProperty("ergonomicValues").EnumerateArray()
                    |> Seq.toList

                Expect.equal paths.Length 2 "Root and referenced length have safe conversion paths"

                Expect.isTrue
                    (paths
                     |> List.exists (fun path -> path.GetProperty("expression").GetString().Contains("Syntax.")))
                    "Conversion goes through checked canonical factories"
            testCase "sugar terminates recursive aliases and preserves structural requirements"
            <| fun _ ->
                let json =
                    """{"properties":[{"name":"recursive","href":"https://example.test/recursive","syntax":"<a>"},{"name":"pair","href":"https://example.test/pair","syntax":"<length>{2}"}],"types":[{"name":"a","href":"https://example.test/a","syntax":"auto | <b>"},{"name":"b","href":"https://example.test/b","syntax":"<a> | <length>"},{"name":"length","href":"https://example.test/length"}],"functions":[],"selectors":[],"atrules":[]}"""

                let input =
                    Webref.consume "recursive-sugar" (Encoding.UTF8.GetBytes json)
                    |> Result.toOption
                    |> Option.get

                let output = generate input
                use manifest = JsonDocument.Parse output.Manifest

                let definitions =
                    manifest.RootElement.GetProperty("definitions").EnumerateArray()
                    |> Seq.toList

                let paths name =
                    definitions
                    |> List.find (fun item -> item.GetProperty("name").GetString() = name)
                    |> fun item -> item.GetProperty("ergonomicValues").GetArrayLength()

                Expect.equal (paths "recursive") 4 "Root, aliases and primitive remain finite and reachable"
                Expect.equal (paths "pair") 1 "A required pair never accepts a lone length"

                Expect.equal
                    output
                    (generate
                        { input with
                            Features = List.rev input.Features
                        })
                    "Stable conversion paths"
            testCase "sugar permits singleton alternatives and repetitions only when the grammar does"
            <| fun _ ->
                let json =
                    """{"properties":[{"name":"display","href":"https://example.test/display","syntax":"block || flex"},{"name":"padding","href":"https://example.test/padding","syntax":"<length>{1,4}"}],"types":[{"name":"length","href":"https://example.test/length"}],"functions":[],"selectors":[],"atrules":[]}"""

                let input =
                    Webref.consume "singletons" (Encoding.UTF8.GetBytes json)
                    |> Result.toOption
                    |> Option.get

                use manifest = JsonDocument.Parse (generate input).Manifest

                let definitions =
                    manifest.RootElement.GetProperty("definitions").EnumerateArray()
                    |> Seq.toList

                let display =
                    definitions
                    |> List.find (fun item -> item.GetProperty("name").GetString() = "display")

                Expect.equal (display.GetProperty("ergonomicKeywords").GetArrayLength()) 2 "Either unordered keyword can stand alone"

                let padding =
                    definitions
                    |> List.find (fun item -> item.GetProperty("name").GetString() = "padding")

                Expect.equal (padding.GetProperty("ergonomicValues").GetArrayLength()) 2 "A one-to-four repetition accepts one length"
            testCase "short property naming collisions remain deterministic and distinct"
            <| fun _ ->
                let json =
                    """{"properties":[{"name":"foo-bar","href":"https://example.test/a","syntax":"auto"},{"name":"fooBar","href":"https://example.test/b","syntax":"none"}],"types":[],"functions":[],"selectors":[],"atrules":[]}"""

                let input =
                    Webref.consume "collisions" (Encoding.UTF8.GetBytes json)
                    |> Result.toOption
                    |> Option.get

                let short catalogue =
                    RuntimeGeneration.generateLegacy "Example.Generated" catalogue
                    |> Result.toOption
                    |> Option.get

                let output = short input
                Expect.stringContains output.Source "``fooBar`` (value:" "First normalized property remains short"
                Expect.stringContains output.Source "``fooBar_" "Colliding property has a distinct stable name"

                Expect.equal
                    output
                    (short
                        { input with
                            Features = List.rev input.Features
                        })
                    "Input ordering cannot change collision names"
            testCase "CSS-wide keyword helpers follow the latest all-property grammar"
            <| fun _ ->
                let input = catalogue ()

                let tree =
                    match Grammar.parse "initial | future-wide" with
                    | Ok tree -> tree
                    | Error error -> failtestf "%A" error

                let all =
                    { input.Features.Head with
                        Id =
                            { input.Features.Head.Id with
                                Name = "all"
                            }
                        Grammar = GrammarStatus.Parsed ("initial | future-wide", tree)
                    }

                let output =
                    generate
                        { input with
                            Features = all :: input.Features
                        }

                Expect.stringContains output.Source "globalFutureWide" "Future spec keyword helper"
                use manifest = JsonDocument.Parse output.Manifest

                Expect.equal
                    (manifest.RootElement.GetProperty("cssWideKeywords").EnumerateArray()
                     |> Seq.map _.GetString()
                     |> Seq.toList)
                    [ "future-wide"; "initial" ]
                    "Registry derives from current all grammar"
            testCase "generation is deterministic across catalogue ordering"
            <| fun _ ->
                let input = catalogue ()

                Expect.equal
                    (generate input)
                    (generate
                        { input with
                            Features = List.rev input.Features
                        })
                    "Stable artifacts"
            testCase "manifest exposes opaque definitions and dependencies"
            <| fun _ ->
                let output = generate (catalogue ())
                Expect.equal output.FeatureCount 2 "Definitions"
                Expect.equal output.OpaqueCount 1 "Prose primitive"
                Expect.equal output.UnresolvedCount 0 "Length resolves"
                use manifest = JsonDocument.Parse output.Manifest
                Expect.equal (manifest.RootElement.GetProperty("references").GetArrayLength()) 1 "Reference map"
            testCase "superseded features emit compiler obsolescence"
            <| fun _ ->
                let input = catalogue ()

                let old =
                    { input.Features.Head with
                        Metadata = Map.ofList [ "supersededBy", "\"https://example.test/latest\"" ]
                    }

                let output =
                    generate
                        { input with
                            Features = old :: input.Features.Tail
                        }

                Expect.stringContains output.Source "System.Obsolete(" "F# warnings"
            testCase "namespace keywords are escaped and malformed namespaces rejected"
            <| fun _ ->
                match Generation.generate "type.module" (catalogue ()) with
                | Ok output -> Expect.stringContains output.Source "namespace ``type``.``module``" "Escaped F# segments"
                | Error errors -> failtestf "%A" errors

                for ns in [ null; ""; "A.B;printfn"; "A..B" ] do
                    Expect.isError (Generation.generate ns (catalogue ())) "Invalid namespace"
            testCase "duplicate identities cannot produce conflicting generated types"
            <| fun _ ->
                let input = catalogue ()

                Expect.isError
                    (Generation.generate
                        "Example"
                        { input with
                            Features = input.Features @ input.Features
                        })
                    "Duplicates"
            testCase "manifest collisions preserve all inputs and outputs"
            <| fun _ ->
                let directory =
                    Path.Combine (Path.GetTempPath (), "ranvier-css-" + Guid.NewGuid().ToString("N"))

                Directory.CreateDirectory directory |> ignore

                try
                    let input = Path.Combine (directory, "input.json")
                    let output = Path.Combine (directory, "model.manifest.json")
                    let bindings = Path.Combine (directory, "model.fs")
                    File.WriteAllText (input, "original-input")
                    File.WriteAllText (output, "original-output")
                    Expect.equal (Cli.run [| "--input"; input; "--output"; output; "--bindings"; bindings |]) 64 "Manifest/model collision"
                    Expect.equal (File.ReadAllText output) "original-output" "Output preserved"
                    let manifestInput = Path.Combine (directory, "input.manifest.json")
                    File.WriteAllText (manifestInput, "original-input")

                    Expect.equal
                        (Cli.run
                            [|
                                "--input"
                                manifestInput
                                "--output"
                                output
                                "--bindings"
                                Path.Combine (directory, "input.fs")
                            |])
                        64
                        "Manifest/input collision"

                    Expect.equal (File.ReadAllText manifestInput) "original-input" "Input preserved"
                finally
                    Directory.Delete (directory, true)
        ]
