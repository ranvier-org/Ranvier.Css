namespace Ranvier.Css.Spec

open System
open System.Text
open System.Text.Json

type internal ValueCategoryPlan =
    {
        Source: string
        Keywords: string
        Wrappers: Map<string, string>
        NumericWrappers: Map<string, string>
    }

module internal ValueCategoryGeneration =
    let private text (key: string) (item: JsonElement) =
        item.GetProperty(key).GetString()

    let private items (key: string) (item: JsonElement) =
        item.GetProperty(key).EnumerateArray()
        |> Seq.toList

    let private literal (value: string) =
        JsonSerializer.Serialize value

    let render canonical (manifest: JsonElement) (properties: JsonElement list) (keywordTags: Map<string, string>) =
        let source = StringBuilder ()
        let keywords = StringBuilder ()

        let line (value: string) =
            source.AppendLine value |> ignore

        let keywordLine (value: string) =
            keywords.AppendLine value |> ignore

        let wide =
            items "cssWideKeywords" manifest
            |> List.map _.GetString()
            |> Set.ofList

        let definitions = items "definitions" manifest

        let accepted =
            properties
            |> List.collect (fun property ->
                items "ergonomicValues" property
                |> List.map (text "marker"))
            |> Set.ofList

        let primitives =
            definitions
            |> List.filter (fun item ->
                text "kind" item = "Type"
                && (items "scopes" item).IsEmpty
                && not ((text "moduleName" item).Contains("_Obsolete_", StringComparison.Ordinal)))

        let numeric =
            primitives
            |> List.choose (fun item ->
                NumericRuntimeGeneration.primitive (text "name" item)
                |> Option.map (fun shape -> text "marker" item, shape))
            |> Map.ofList

        let markers =
            Set.union
                accepted
                (primitives
                 |> List.map (text "marker")
                 |> Set.ofList)
            |> Set.toList

        let ids =
            markers
            |> List.mapi (fun index marker -> marker, index)
            |> Map.ofList

        let interfaces marker =
            properties
            |> List.filter (fun property ->
                items "ergonomicValues" property
                |> List.exists (fun item -> text "marker" item = marker))
            |> List.map (fun property -> "I" + text "moduleName" property)

        let numericNames =
            primitives
            |> List.map (fun item -> text "marker" item, (text "name" item).Trim('<', '>'))
            |> Map.ofList

        let mixedMarkers =
            numericNames
            |> Map.toList
            |> List.filter (fun (_, name) -> List.contains name [ "length"; "percentage"; "length-percentage" ])
            |> List.map fst

        let mixedInterfaces =
            match mixedMarkers with
            | [] -> []
            | head :: tail ->
                tail
                |> List.fold (fun common marker -> Set.intersect common (interfaces marker |> Set.ofList)) (interfaces head |> Set.ofList)
                |> Set.toList

        line "/// <summary>Shared value categories accepted by generated CSS property helpers.</summary>"
        line "module CssValueCategories ="
        line "    /// <summary>A canonical value or keyword retaining its source identity and serialized text.</summary>"
        line "    type IValue ="
        line "        abstract Source: int"
        line "        abstract Untyped: obj"
        line "        abstract Text: string"
        line "    /// <summary>A CSS-wide keyword accepted by static property declarations.</summary>"
        line "    type ICssWide ="
        line "        inherit IValue"

        for property in properties do
            line (
                "    /// <summary>Values admitted by the "
                + System.Security.SecurityElement.Escape (text "name" property)
                + " property grammar.</summary>"
            )

            line ("    type I" + text "moduleName" property + " =")
            line "        inherit IValue"

        if not mixedMarkers.IsEmpty then
            line "    /// <summary>A length, percentage, or length-percentage value accepted in their shared property contexts.</summary>"
            line "    type ILengthPercentage ="
            line "        inherit IValue"

            for name in mixedInterfaces do
                line ("        inherit " + name)

        line "/// <summary>Adapters preserving canonical grammar values and their property admission.</summary>"
        line "module CssValues ="

        if markers.IsEmpty then
            line "    do ()"

        let wrappers =
            markers
            |> List.map (fun marker -> marker, "CssValues.Value_" + marker)
            |> Map.ofList

        let numericWrappers =
            numeric
            |> Map.toList
            |> List.map (fun (marker, _) -> marker, "CssValues.Numeric_" + marker)
            |> Map.ofList

        let memberships marker =
            for name in interfaces marker do
                line ("        interface CssValueCategories." + name)

            if List.contains marker mixedMarkers then
                line "        interface CssValueCategories.ILengthPercentage"

        let storage marker =
            line "        interface CssValueCategories.IValue with"

            line (
                "            member _.Source = "
                + string ids[marker]
            )

            line "            member _.Untyped = box value"
            line "            member _.Text = global.Ranvier.Css.RuntimeApi.renderValue value"

        for marker in markers do
            let input =
                canonical
                + "Value<"
                + canonical
                + "Markers."
                + marker
                + ">"

            line "    /// <summary>A canonical grammar value with shared property admission.</summary>"

            line (
                "    type Value_"
                + marker
                + " internal (value:"
                + input
                + ") ="
            )

            line ("        inherit " + input + "(value.Storage)")
            storage marker
            memberships marker

            match numeric.TryFind marker with
            | Some (measure, hint) ->
                let input =
                    "NumericValue<"
                    + measure
                    + ","
                    + canonical
                    + "Markers."
                    + marker
                    + ","
                    + hint
                    + ">"

                line "    /// <summary>A numeric grammar value retaining arithmetic and shared property admission.</summary>"

                line (
                    "    type Numeric_"
                    + marker
                    + " internal (value:"
                    + input
                    + ") ="
                )

                line (
                    "        inherit "
                    + input
                    + "(value,value.Calculation)"
                )

                storage marker
                memberships marker
            | None -> ()

        for item in definitions do
            let marker = text "marker" item

            if wrappers.ContainsKey marker then
                line "    /// <summary>Preserves a canonical production while exposing its property admission.</summary>"

                line (
                    "    let of"
                    + text "moduleName" item
                    + " value = Value_"
                    + marker
                    + " value"
                )

        for KeyValue (token, tag) in keywordTags do
            keywordLine (
                "    /// <summary>The CSS keyword "
                + System.Security.SecurityElement.Escape token
                + ".</summary>"
            )

            keywordLine ("    type " + tag + " internal () =")

            keywordLine (
                "        inherit global.Ranvier.Css.Value<"
                + tag
                + ">((global.Ranvier.Css.Generated.value "
                + literal token
                + " : global.Ranvier.Css.Value<"
                + tag
                + ">).Storage)"
            )

            keywordLine "        interface CssValueCategories.IValue with"
            keywordLine "            member _.Source = -1"
            keywordLine "            member _.Untyped = null"
            keywordLine ("            member _.Text = " + literal token)

            if wide.Contains token then
                keywordLine "        interface CssValueCategories.ICssWide"

            for property in properties do
                if
                    not (wide.Contains token)
                    && (items "ergonomicKeywords" property
                        |> List.exists (fun keyword -> text "token" keyword = token))
                then
                    keywordLine (
                        "        interface CssValueCategories.I"
                        + text "moduleName" property
                    )

        line "module private CssValueConversions ="

        if properties.IsEmpty then
            line "    do ()"

        line (
            "    module Canonical = "
            + canonical.TrimEnd ('.')
        )

        for property in properties do
            let name = text "moduleName" property

            line (
                "    let "
                + name
                + " (input:CssValueCategories.IValue) ="
            )

            line "        match input.Source with"

            line (
                "        | -1 -> global.Ranvier.Css.RuntimeApi.declaration "
                + literal (text "name" property)
                + " input.Text"
            )

            for conversion in items "ergonomicValues" property do
                let marker = text "marker" conversion
                line ("        | " + string ids[marker] + " ->")

                line (
                    "            let value = unbox<Canonical.Value<Canonical.Markers."
                    + marker
                    + ">> input.Untyped"
                )

                line (
                    "            Canonical."
                    + name
                    + ".declaration ("
                    + (text "expression" conversion).Replace("Syntax.", "Canonical.Syntax.")
                    + ")"
                )

            line "        | _ -> invalidArg \"input\" \"Value does not belong to this CSS grammar.\""

        {
            Source = source.ToString().Replace("\r\n", "\n")
            Keywords = keywords.ToString().Replace("\r\n", "\n")
            Wrappers = wrappers
            NumericWrappers = numericWrappers
        }
