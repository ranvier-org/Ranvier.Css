namespace Ranvier.Css.Spec

open System
open System.Text
open System.Text.Json

/// <summary>Bridges checked numeric expressions into canonical grammar values while retaining their calculation trees.</summary>
module NumericValueGeneration =
    type private NumericMarker =
        {
            Production: string
            Marker: string
            Measure: string
            Hint: string
            Priority: int
        }

    let private text (name: string) (value: JsonElement) =
        match value.TryGetProperty name with
        | true, field when field.ValueKind = JsonValueKind.String -> field.GetString ()
        | _ -> ""

    let private items (name: string) (value: JsonElement) =
        match value.TryGetProperty name with
        | true, values when values.ValueKind = JsonValueKind.Array -> values.EnumerateArray () |> Seq.toList
        | _ -> []

    let private markers (manifest: JsonElement) =
        let candidates =
            (items "definitions" manifest
             |> List.filter (fun item ->
                 text "kind" item = "Type"
                 && (items "scopes" item).IsEmpty
                 && text "obsolete" item = "")
             |> List.map (fun item -> text "name" item, text "marker" item, 0))
            @ (items "primitiveTerminals" manifest
               |> List.map (fun item -> text "production" item, text "marker" item, 1))

        candidates
        |> List.choose (fun (name, marker, priority) ->
            let production = name.Trim('<', '>').ToLowerInvariant()

            if
                production = "zero"
                || production.EndsWith ("-token", StringComparison.Ordinal)
                || marker = ""
            then
                None
            else
                NumericRuntimeGeneration.primitive production
                |> Option.map (fun (measure, hint) ->
                    {
                        Production = production
                        Marker = marker
                        Measure = measure
                        Hint = hint
                        Priority = priority
                    }))
        |> List.sortBy (fun item -> item.Priority, item.Production, item.Marker)
        |> List.distinctBy _.Production

    let private dimensions =
        NumericSemantics.dimensions
        |> List.filter ((<>) NumericDimension.Percent)

    let private dimensionName dimension =
        (string dimension).ToLowerInvariant()

    let private unit dimension =
        "CalculationUnits." + dimensionName dimension

    let private hint dimension =
        "CalculationPercentBasis<" + unit dimension + ">"

    let private memberName dimension =
        dimensionName dimension + "Percentage"

    let private discriminator dimension =
        "CalculationValueOverloads."
        + string dimension
        + "Raw"

    /// <summary>Emits CLR overload identities distinguishing raw dimension and raw percentage expression inputs.</summary>
    /// <returns>Namespace-level F# declarations to place before the unified Css type.</returns>
    let types () =
        let source = StringBuilder ()

        source.AppendLine "/// <summary>Discriminates measured raw-percentage adapters after CLR measure erasure.</summary>"
        |> ignore

        source.AppendLine "module CalculationValueOverloads ="
        |> ignore

        for dimension in dimensions do
            source.AppendLine "    /// <summary>Identifies a dimension-valued expression retaining raw percentage provenance.</summary>"
            |> ignore

            source.AppendLine (
                "    type "
                + string dimension
                + "Raw private () = class end"
            )
            |> ignore

        source.ToString().Replace("\r\n", "\n")

    let private contextAndExpected production =
        let noPercent = "global.Ranvier.Css.Spec.NumericSemantics.dimensionOnlyContext"
        let ordinary = "global.Ranvier.Css.Spec.NumericSemantics.ordinaryContext"

        let dimension value =
            "(global.Ranvier.Css.Spec.NumericSemantics.dimension global.Ranvier.Css.Spec.NumericDimension."
            + string value
            + ")"

        match production with
        | "number"
        | "integer" -> noPercent, "global.Ranvier.Css.Spec.NumericSemantics.number"
        | "percentage" -> ordinary, dimension NumericDimension.Percent
        | name ->
            let baseName =
                if name.EndsWith ("-percentage", StringComparison.Ordinal) then
                    name.Substring (0, name.Length - "-percentage".Length)
                else
                    name

            let baseDimension =
                dimensions
                |> List.find (fun value -> dimensionName value = baseName)

            let context =
                if baseName = name then
                    noPercent
                else
                    "({ PercentagesAllowed = true; PercentageBasis = Some global.Ranvier.Css.Spec.NumericDimension."
                    + string baseDimension
                    + " } : global.Ranvier.Css.Spec.NumericContext)"

            context, dimension baseDimension

    /// <summary>Emits checked canonical numeric bridges and all dimension-percentage context adapters on Css.</summary>
    /// <param name="canonical">The qualified canonical Css module prefix, including its trailing dot.</param>
    /// <param name="manifest">The current grammar manifest containing numeric definitions and primitive terminals.</param>
    /// <returns>F# members indented for the unified Css type body.</returns>
    let members canonical (manifest: JsonElement) =
        let source = StringBuilder ()

        let line (value: string) =
            source.AppendLine value |> ignore

        let registry = markers manifest

        let wrappedMarkers =
            items "definitions" manifest
            |> List.filter (fun item ->
                text "kind" item = "Type"
                && (items "scopes" item).IsEmpty
                && text "obsolete" item = "")
            |> List.map (text "marker")
            |> Set.ofList

        let markerType marker =
            canonical + "Markers.``" + marker + "``"

        let input measure hint =
            "ICalculationExpression<"
            + measure
            + ","
            + hint
            + ">"

        let numericType item =
            if wrappedMarkers.Contains item.Marker then
                "CssValues.Numeric_" + item.Marker
            else
                "NumericValue<"
                + item.Measure
                + ","
                + markerType item.Marker
                + ","
                + item.Hint
                + ">"

        let body item =
            let context, expected = contextAndExpected item.Production

            let expression =
                "let calculation = value.Calculation in let checkedValue = global.Ranvier.Css.CalculationRuntime.declaration \"--numeric-value\" "
                + context
                + " "
                + expected
                + " calculation in NumericFactory.value<"
                + item.Measure
                + ","
                + markerType item.Marker
                + ","
                + item.Hint
                + "> (global.Ranvier.Css.Generated.value (global.Ranvier.Css.RuntimeApi.declarationValue checkedValue)) calculation"

            if wrappedMarkers.Contains item.Marker then
                "CssValues.Numeric_"
                + item.Marker
                + " ("
                + expression
                + ")"
            else
                expression

        let emit name (parameter: string) output implementation =
            line "    /// <summary>Checks a numeric expression against this grammar production and preserves its unevaluated tree.</summary>"
            line "    /// <param name=\"value\">The expression with the required dimensions and percentage provenance.</param>"

            if parameter.Contains "?raw:" then
                line "    /// <param name=\"raw\">A generated overload discriminator; callers normally omit it.</param>"

            line (
                "    static member ``"
                + name
                + "`` ("
                + parameter
                + ") : "
                + output
                + " = "
                + implementation
            )

        for item in
            registry
            |> List.filter (fun item -> not (item.Production.EndsWith ("-percentage", StringComparison.Ordinal))) do
            emit item.Production ("value:" + input item.Measure item.Hint) (numericType item) (body item)

        for dimension in dimensions do
            let production = dimensionName dimension + "-percentage"

            let canonicalMarker =
                registry
                |> List.tryFind (fun item -> item.Production = production)

            let resultMeasure, resultHint = unit dimension, hint dimension

            let output, implementation =
                match canonicalMarker with
                | Some item -> numericType item, body item
                | None ->
                    "CalculationExpression<"
                    + resultMeasure
                    + ","
                    + resultHint
                    + ">",
                    "NumericFactory.expression<"
                    + resultMeasure
                    + ","
                    + resultHint
                    + "> value.Calculation"

            for inputMeasure, inputHint, suffix in
                [
                    resultMeasure, "CalculationPlain", ""
                    resultMeasure, resultHint, ""
                    "CalculationUnits.percent", "CalculationPercentage", ""
                    resultMeasure, "CalculationPercentage", ", ?raw:" + discriminator dimension
                ] do
                emit (memberName dimension) ("value:" + input inputMeasure inputHint + suffix) output implementation

        source.ToString().Replace("\r\n", "\n")

    /// <summary>Describes canonical numeric value bridges and context adapters for static extraction.</summary>
    /// <param name="manifest">The same canonical manifest used to generate the authoring source.</param>
    /// <returns>A serializable helper registry; absent mixed grammar productions have null marker values.</returns>
    let metadata (manifest: JsonElement) : obj =
        let registry = markers manifest

        let primitive =
            registry
            |> List.filter (fun item -> not (item.Production.EndsWith ("-percentage", StringComparison.Ordinal)))
            |> List.map (fun item ->
                {|
                    memberName = item.Production
                    production = item.Production
                    marker = item.Marker
                    measure = item.Measure
                    hint = item.Hint
                    kind = "value"
                |})

        let contexts =
            dimensions
            |> List.map (fun dimension ->
                let production = dimensionName dimension + "-percentage"

                {|
                    memberName = memberName dimension
                    production = production
                    marker =
                        registry
                        |> List.tryFind (fun item -> item.Production = production)
                        |> Option.map _.Marker
                        |> Option.toObj
                    measure = unit dimension
                    hint = hint dimension
                    kind = "context"
                |})

        box {| helpers = primitive @ contexts |}
