namespace Ranvier.Css.Spec

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.RegularExpressions

/// <summary>A primitive constructor's public path, input contract and exact runtime grammar identity.</summary>
type internal PrimitiveFactory =
    {
        Owner: string list
        MemberName: string
        NodeId: int
        Kind: string
        Production: string
        UnitName: string
        ArgumentType: string
    }

/// <summary>Primitive constructors grouped by public owner path, with concise root aliases and extraction metadata.</summary>
type internal PrimitiveOutput =
    {
        Declarations: (string list * string) list
        Aliases: (string * string) list
        Factories: PrimitiveFactory list
    }

/// <summary>Emits measured numeric values and validated CSS string and identifier constructors.</summary>
module internal CompactPrimitiveGeneration =
    let private quote (value: string) =
        JsonSerializer.Serialize value

    let private name (value: string) =
        Regex.Matches (value, "[A-Za-z0-9]+")
        |> Seq.cast<Match>
        |> Seq.map (fun word ->
            string (Char.ToUpperInvariant word.Value[0])
            + word.Value.Substring 1)
        |> String.concat ""

    let private units =
        Map.ofList
            [
                "number", [ "" ]
                "integer", [ "" ]
                "percentage", [ "%" ]
                "length",
                [
                    "px"
                    "cm"
                    "mm"
                    "q"
                    "in"
                    "pt"
                    "pc"
                    "em"
                    "ex"
                    "cap"
                    "ch"
                    "ic"
                    "rem"
                    "rex"
                    "rcap"
                    "rch"
                    "ric"
                    "lh"
                    "rlh"
                    "vw"
                    "vh"
                    "vi"
                    "vb"
                    "vmin"
                    "vmax"
                    "svw"
                    "svh"
                    "svi"
                    "svb"
                    "svmin"
                    "svmax"
                    "lvw"
                    "lvh"
                    "lvi"
                    "lvb"
                    "lvmin"
                    "lvmax"
                    "dvw"
                    "dvh"
                    "dvi"
                    "dvb"
                    "dvmin"
                    "dvmax"
                    "cqw"
                    "cqh"
                    "cqi"
                    "cqb"
                    "cqmin"
                    "cqmax"
                ]
                "angle", [ "deg"; "rad"; "turn"; "grad" ]
                "time", [ "s"; "ms" ]
                "frequency", [ "hz"; "khz" ]
                "resolution", [ "dpi"; "dpcm"; "dppx"; "x" ]
                "flex", [ "fr" ]
            ]

    let private context production =
        let semantics = "global.Ranvier.Css.Spec.NumericSemantics."
        let dimension = "global.Ranvier.Css.Spec.NumericDimension."

        if production = "number" || production = "integer" then
            semantics + "dimensionOnlyContext", semantics + "number"
        elif production = "percentage" then
            semantics + "ordinaryContext",
            "("
            + semantics
            + "dimension "
            + dimension
            + "Percent)"
        else
            let basis = production.Replace ("-percentage", "")

            let expected =
                "("
                + semantics
                + "dimension "
                + dimension
                + name basis
                + ")"

            if basis = production then
                semantics + "dimensionOnlyContext", expected
            else
                "({ PercentagesAllowed = true; PercentageBasis = Some "
                + dimension
                + name basis
                + " } : global.Ranvier.Css.Spec.NumericContext)",
                expected

    /// <summary>Returns constructors preserving numeric measures and grammar identities; arithmetic bridges validate CSS numeric typing.</summary>
    let render (catalogue: Catalogue) (graph: GrammarGraph) (categories: CompactCategories) : PrimitiveOutput =
        let declarations = ResizeArray<string list * string>()
        let aliases = ResizeArray<string * string>()
        let factories = ResizeArray<PrimitiveFactory>()
        let aliasNames = HashSet<string>(StringComparer.Ordinal)
        let runtime = "global.Ranvier.Css.Internal.GrammarRuntime"
        let calculation = "global.Ranvier.Css.CalculationRuntime."
        let generated = "global.Ranvier.Css.Generated."

        let add path (source: string) =
            for line in source.Replace("\r\n", "\n").TrimEnd('\n').Split('\n') do
                declarations.Add (path, line)

        let summary path text =
            add path ("/// <summary>" + text + "</summary>")

        let alias memberName owner target (factory: PrimitiveFactory) =
            if aliasNames.Add memberName then
                aliases.Add (memberName, "CssValue." + owner + ".``" + target + "``")

                factories.Add
                    { factory with
                        Owner = [ "CssValue" ]
                        MemberName = memberName
                    }

                factories.Add
                    { factory with
                        Owner = [ "Css" ]
                        MemberName = memberName
                    }

        add [ "Numeric" ] (NumericRuntimeGeneration.source ())

        let memberships id =
            Map.tryFind id categories.Implemented
            |> Option.defaultValue []
            |> List.distinct
            |> List.sort

        let storageClasses =
            categories.Categories
            |> Map.map (fun _ representative -> "CssGrammar.makeC" + string representative)

        let primitives =
            catalogue.Features
            |> List.filter (fun feature ->
                feature.Id.Kind = FeatureKind.Type
                && feature.Id.Scopes.IsEmpty)
            |> List.sortBy _.Id

        for feature in primitives do
            let production = feature.Id.Name.Trim('<', '>').ToLowerInvariant()
            let id = graph.Roots[feature.Id]
            let owner = name production
            let path = [ owner ]

            match NumericRuntimeGeneration.primitive production with
            | Some (measure, hint) when
                production <> "zero"
                && not (production.EndsWith ("-token", StringComparison.Ordinal))
                ->
                let syntax = "CssGrammar.C" + string categories.Categories[id]
                let canonical = "global.Ranvier.Css.Value<" + syntax + ">"

                summary
                    [ "Numeric" ]
                    ("A CSS "
                     + production
                     + " value retaining its arithmetic tree and grammar admission.")

                add
                    [ "Numeric" ]
                    ("type "
                     + owner
                     + " internal (value: "
                     + canonical
                     + ", calculation: global.Ranvier.Css.Calculation, data: "
                     + runtime
                     + ".ValueData) =")

                add
                    [ "Numeric" ]
                    ("    inherit NumericValue<"
                     + measure
                     + ","
                     + syntax
                     + ","
                     + hint
                     + ">(value, calculation)")

                add [ "Numeric" ] "    interface CssGrammar.IValue with"
                add [ "Numeric" ] "        member _.Data = data"

                for category in memberships id do
                    add [ "Numeric" ] ("    interface CssGrammar.C" + string category)

                let suffixes =
                    let defaults =
                        Map.tryFind production units
                        |> Option.defaultValue [ "%" ]

                    let specUnits =
                        catalogue.Features
                        |> List.choose (fun candidate ->
                            if
                                Map.containsKey production units
                                && not (List.contains production [ "number"; "integer"; "percentage" ])
                                && candidate.Id.Scopes = [ "<" + production + ">" ]
                            then
                                match candidate.Grammar with
                                | GrammarStatus.Parsed (_,
                                                        {
                                                            Expression = GrammarExpression.Literal token
                                                        }) when Regex.IsMatch (token, "^[A-Za-z]+$") -> Some (token.ToLowerInvariant ())
                                | _ -> None
                            else
                                None)

                    defaults @ specUnits |> List.distinct

                let output = "CssValue.Numeric." + owner

                for suffix in suffixes do
                    let memberName =
                        if suffix = "" then "create"
                        elif suffix = "%" then "percent"
                        else suffix

                    let argument =
                        if production = "integer" then
                            "(value: int)"
                        else
                            "(value: float)"

                    let magnitude = if production = "integer" then "(float value)" else "value"

                    let data =
                        runtime
                        + ".number CssGrammar.table "
                        + string id
                        + " "
                        + magnitude
                        + " "
                        + quote suffix

                    let literalProduction = if production = "integer" then "integer" else production

                    summary
                        path
                        ("Constructs a finite CSS "
                         + production
                         + " token.")

                    add
                        path
                        ("let ``"
                         + memberName
                         + "`` "
                         + argument
                         + " : "
                         + output
                         + " = "
                         + output
                         + "(("
                         + generated
                         + "number "
                         + magnitude
                         + " "
                         + quote suffix
                         + " : "
                         + canonical
                         + "), "
                         + calculation
                         + "literal "
                         + quote literalProduction
                         + " "
                         + quote suffix
                         + " "
                         + magnitude
                         + ", "
                         + data
                         + ")")

                    let factory =
                        {
                            Owner = "CssValue" :: path
                            MemberName = memberName
                            NodeId = id
                            Kind = if production = "integer" then "integer" else "number"
                            Production = production
                            UnitName = suffix
                            ArgumentType = if production = "integer" then "int" else "float"
                        }

                    factories.Add factory

                    if not (production.EndsWith ("-percentage", StringComparison.Ordinal)) then
                        alias
                            (if suffix = "" then production
                             elif suffix = "%" then "percent"
                             else suffix)
                            owner
                            memberName
                            factory

                if production = "number" then
                    summary path "The finite unitless zero value, retaining measured arithmetic and number grammar admission."
                    add path ("let zero : " + output + " = create 0.")

                    let factory =
                        {
                            Owner = "CssValue" :: path
                            MemberName = "zero"
                            NodeId = id
                            Kind = "number"
                            Production = production
                            UnitName = ""
                            ArgumentType = "none"
                        }

                    factories.Add factory
                    alias "zero" owner "zero" factory

                let expectedContext, expectedType = context production

                let inputMeasure =
                    measure.Replace ("CalculationUnits.", "Numeric.CalculationUnits.")

                let inputHint = hint.Replace ("CalculationUnits.", "Numeric.CalculationUnits.")
                let inputHint = "Numeric." + inputHint
                summary path "Validates a measured arithmetic expression and preserves its unevaluated CSS calculation."

                add
                    path
                    ("let calc (value: Numeric.ICalculationExpression<"
                     + inputMeasure
                     + ","
                     + inputHint
                     + ">) : "
                     + output
                     + " = let calculation = value.Calculation in let checkedValue = "
                     + calculation
                     + "declaration \"--numeric-value\" "
                     + expectedContext
                     + " "
                     + expectedType
                     + " calculation in let text = global.Ranvier.Css.RuntimeApi.declarationValue checkedValue in "
                     + output
                     + "(("
                     + generated
                     + "value text : "
                     + canonical
                     + "), calculation, "
                     + runtime
                     + ".opaque CssGrammar.table "
                     + string id
                     + " text)")

                factories.Add
                    {
                        Owner = "CssValue" :: path
                        MemberName = "calc"
                        NodeId = id
                        Kind = "calculation"
                        Production = production
                        UnitName = ""
                        ArgumentType = "calculation"
                    }

                for memberName, argument, fallback, argumentType in
                    [
                        "variable", "(name: string)", "None", "name"
                        "variableWithFallback",
                        "(name: string) (fallback: Numeric.ICalculationExpression<"
                        + inputMeasure
                        + ","
                        + inputHint
                        + ">)",
                        "(Some fallback.Calculation)",
                        "nameFallback"
                    ] do
                    summary
                        path
                        ("Constructs a measured CSS custom-property reference with "
                         + production
                         + " numeric typing.")

                    add path "/// <param name=\"name\">A dashed custom-property identifier, including its leading two hyphens.</param>"

                    if argumentType = "nameFallback" then
                        add path "/// <param name=\"fallback\">An unevaluated expression with the same dimension and percentage provenance.</param>"

                    add
                        path
                        "/// <exception cref=\"T:System.ArgumentException\">The name is invalid or the numeric expression fails CSS type validation.</exception>"

                    add
                        path
                        ("let "
                         + memberName
                         + " "
                         + argument
                         + " : "
                         + output
                         + " = let calculation = "
                         + calculation
                         + "reference "
                         + quote production
                         + " name "
                         + fallback
                         + " in let checkedValue = "
                         + calculation
                         + "declaration \"--numeric-value\" "
                         + expectedContext
                         + " "
                         + expectedType
                         + " calculation in let text = global.Ranvier.Css.RuntimeApi.declarationValue checkedValue in "
                         + output
                         + "(("
                         + generated
                         + "value text : "
                         + canonical
                         + "), calculation, "
                         + runtime
                         + ".opaque CssGrammar.table "
                         + string id
                         + " text)")

                    factories.Add
                        {
                            Owner = "CssValue" :: path
                            MemberName = memberName
                            NodeId = id
                            Kind = "numericVariable"
                            Production = production
                            UnitName = ""
                            ArgumentType = argumentType
                        }
            | _ ->
                let factory =
                    match production with
                    | "string"
                    | "string-token" -> Some ("string", "string", generated + "stringValue value")
                    | "ident"
                    | "identifier"
                    | "ident-token" -> Some ("ident", "identifier", generated + "identifier false false value")
                    | "custom-ident" -> Some ("customIdent", "customIdentifier", generated + "identifier false true value")
                    | "dashed-ident"
                    | "custom-property-name" -> Some ("dashedIdent", "dashedIdentifier", generated + "identifier true false value")
                    | _ -> None

                match factory with
                | Some (aliasName, kind, expression) ->
                    summary path "Validates and escapes the CSS token before retaining its grammar identity."

                    add
                        path
                        ("let create (value: string) = let canonical: global.Ranvier.Css.Value<obj> = "
                         + expression
                         + " in "
                         + storageClasses[id]
                         + "("
                         + runtime
                         + ".opaque CssGrammar.table "
                         + string id
                         + " ("
                         + generated
                         + "text canonical))")

                    let factory =
                        {
                            Owner = "CssValue" :: path
                            MemberName = "create"
                            NodeId = id
                            Kind = kind
                            Production = production
                            UnitName = ""
                            ArgumentType = "string"
                        }

                    factories.Add factory

                    if not (production.EndsWith ("-token", StringComparison.Ordinal)) then
                        alias aliasName owner "create" factory
                | None -> ()

        {
            Declarations = List.ofSeq declarations
            Aliases = List.ofSeq aliases
            Factories = List.ofSeq factories
        }
