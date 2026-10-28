namespace Ranvier.Css.Spec

open System
open System.Text
open System.Text.Json

/// <summary>Emits numeric custom-property handles and deferred var references from the canonical numeric registry.</summary>
module NumericVariableGeneration =
    type private Source =
        {
            Marker: string
            Production: string
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

    let private normalize (production: string) =
        match production.Trim('<', '>').ToLowerInvariant() with
        | "zero"
        | "number-token" -> "number"
        | "percentage-token" -> "percentage"
        | value -> value

    let private sources (manifest: JsonElement) =
        let candidates =
            (items "definitions" manifest
             |> List.filter (fun item ->
                 text "kind" item = "Type"
                 && (items "scopes" item).IsEmpty
                 && text "obsolete" item = "")
             |> List.map (fun item -> text "marker" item, text "name" item, 0))
            @ (items "knownNumericTypes" manifest
               |> List.map (fun item -> text "marker" item, text "production" item, 1))
            @ (items "primitiveTerminals" manifest
               |> List.map (fun item -> text "marker" item, text "production" item, 2))

        candidates
        |> List.choose (fun (marker, original, priority) ->
            let production = normalize original

            NumericRuntimeGeneration.primitive production
            |> Option.bind (fun (measure, hint) ->
                if marker = "" then
                    None
                else
                    Some
                        {
                            Marker = marker
                            Production = production
                            Measure = measure
                            Hint = hint
                            Priority =
                                priority * 2
                                + (if original.Trim ('<', '>') = production then 0 else 1)
                        }))
        |> List.sortBy (fun source -> source.Priority, source.Production, source.Marker)
        |> List.distinctBy _.Marker

    let private constructorName (production: string) =
        production.Split '-'
        |> Array.mapi (fun index part ->
            if index = 0 || part = "" then
                part
            else
                Char.ToUpperInvariant(part[0]).ToString()
                + part.Substring 1)
        |> String.concat ""
        |> fun name -> name + "Variable"

    let private constructors entries =
        entries
        |> List.distinctBy _.Production
        |> List.sortBy _.Production

    /// <summary>Emits typed numeric variable constructors and references on the unified Css authoring surface.</summary>
    /// <param name="canonical">The qualified canonical Css module prefix, including its trailing dot.</param>
    /// <param name="manifest">The enriched manifest containing current numeric type markers and primitive terminals.</param>
    /// <returns>Css members indented by four spaces.</returns>
    let members canonical (manifest: JsonElement) =
        let result = StringBuilder ()

        let line (value: string) =
            result.AppendLine value |> ignore

        let entries = sources manifest

        let handle source =
            "global.Ranvier.Css.CustomProperty<"
            + canonical
            + "Markers.``"
            + source.Marker
            + "``>"

        for source in constructors entries do
            line "    /// <summary>Creates a numeric custom-property handle; the browser still validates its substituted value.</summary>"
            line "    /// <remarks>This type assertion does not register the property or inspect declarations defining it.</remarks>"
            line "    /// <param name=\"name\">The CSS dashed custom-property name.</param>"

            line (
                "    static member ``"
                + constructorName source.Production
                + "`` (name:string) : "
                + handle source
                + " = global.Ranvier.Css.RuntimeApi.createCustomProperty name"
            )

        for source in
            entries
            |> List.sortBy (fun source -> source.Production, source.Marker) do
            line
                "    /// <summary>References a numeric custom property without evaluating substitution or flattening its fallback calculation.</summary>"

            line
                "    /// <remarks>The handle asserts the expected numeric production; final substituted-value validity is determined by CSS.</remarks>"

            line "    /// <param name=\"variable\">The typed custom-property handle.</param>"
            line "    /// <param name=\"fallback\">An optional calculation with matching dimensions and percentage provenance.</param>"

            line (
                "    static member variable (variable:"
                + handle source
                + ", ?fallback:ICalculationExpression<"
                + source.Measure
                + ","
                + source.Hint
                + ">) : CalculationExpression<"
                + source.Measure
                + ","
                + source.Hint
                + "> = "
                + "NumericFactory.expression<"
                + source.Measure
                + ","
                + source.Hint
                + "> (global.Ranvier.Css.CalculationRuntime.reference "
                + JsonSerializer.Serialize source.Production
                + " (global.Ranvier.Css.RuntimeApi.customPropertyName variable) (fallback |> Option.map (fun value -> value.Calculation)))"
            )

        result.ToString().Replace("\r\n", "\n")

    /// <summary>Describes the allowlisted numeric variable constructors and reference overloads for static extraction.</summary>
    /// <param name="manifest">The enriched canonical manifest used to generate the source.</param>
    /// <returns>A serializable object containing sourceHelpers and their numeric marker contracts.</returns>
    let metadata (manifest: JsonElement) : obj =
        let entries = sources manifest

        let descriptor kind memberName source =
            {|
                memberName = memberName
                production = source.Production
                marker = source.Marker
                measure = source.Measure
                hint = source.Hint
                kind = kind
            |}

        box
            {|
                sourceHelpers =
                    (constructors entries
                     |> List.map (fun source -> descriptor "create" (constructorName source.Production) source))
                    @ (entries
                       |> List.sortBy (fun source -> source.Production, source.Marker)
                       |> List.map (descriptor "variable" "variable"))
            |}
