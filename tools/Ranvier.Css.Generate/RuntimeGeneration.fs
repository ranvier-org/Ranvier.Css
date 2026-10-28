namespace Ranvier.Css.Spec

open System
open System.Text
open System.Text.Json
open System.Text.Json.Nodes

/// <summary>Generates ordinary F# bindings that share the portable CSS compiler runtime.</summary>
module RuntimeGeneration =
    /// <summary>Emits shared-runtime values and generated static/reactive property methods for every property in the catalogue.</summary>
    let generateLegacy namespaceName catalogue =
        match Generation.generate namespaceName catalogue with
        | Error errors -> Error errors
        | Ok bindings ->
            let start =
                bindings.Source.IndexOf ("    type Value<'syntax>", StringComparison.Ordinal)

            let finish =
                bindings.Source.IndexOf ("    module Markers =", StringComparison.Ordinal)

            let header =
                """    type Value<'syntax> = global.Ranvier.Css.Value<'syntax>
    type Declaration = global.Ranvier.Css.Declaration
    module Runtime = global.Ranvier.Css.Generated
    let private makeDeclaration(name,value) = global.Ranvier.Css.RuntimeApi.declaration name value
    let renderValue value = global.Ranvier.Css.RuntimeApi.renderValue value
    let renderDeclaration value = global.Ranvier.Css.RuntimeApi.renderDeclaration value
    let renderDeclarations values = global.Ranvier.Css.RuntimeApi.renderDeclarations values
    let private checkCustomIdentifier (value:string) =
        if List.contains (value.ToLowerInvariant()) cssWideKeywords then invalidArg "value" "Reserved CSS-wide keyword."
        value
"""

            let source =
                bindings.Source.Substring (0, start)
                + header
                + bindings.Source.Substring (finish)

            use manifest = JsonDocument.Parse bindings.Manifest

            let properties =
                manifest.RootElement.GetProperty("definitions").EnumerateArray()
                |> Seq.choose (fun item ->
                    if item.GetProperty("kind").GetString() = "Property" then
                        Some (
                            item.GetProperty("moduleName").GetString(),
                            item.GetProperty("name").GetString(),
                            item.GetProperty("marker").GetString()
                        )
                    else
                        None)
                |> Seq.map (fun (api, name, marker) -> api, (name, marker))
                |> Map.ofSeq

            let result = StringBuilder ()
            let mutable current = None

            for line in source.Split ('\n') do
                if line.StartsWith ("    module ", StringComparison.Ordinal) then
                    let api = line.Substring(11).Trim().TrimEnd('=').Trim()
                    current <- properties |> Map.tryFind api

                let rewritten =
                    line
                        .Replace("= Declaration(", "= makeDeclaration(")
                        .Replace("Runtime.identifier false true value", "Runtime.identifier false true (checkCustomIdentifier value)")

                result.AppendLine rewritten |> ignore

                match current with
                | Some (name, marker) when line.StartsWith ("        let declaration ", StringComparison.Ordinal) ->
                    let name = JsonSerializer.Serialize name

                    result.AppendLine ("        /// <summary>Creates a static component style node from this property's typed value.</summary>")
                    |> ignore

                    result.AppendLine ("        let constant value = global.Ranvier.Css.Style.declarations [declaration value]")
                    |> ignore

                    result.AppendLine ("        /// <summary>Binds a tracked instance reader through a static CSS custom-property slot.</summary>")
                    |> ignore

                    result.AppendLine (
                        "        let reactive (read:'instance -> Value<Markers."
                        + marker
                        + ">) : global.Ranvier.Css.StyleNode<'instance> = global.Ranvier.Css.Style.dynamicWithKeywords cssWideKeywords "
                        + name
                        + " (fun instance -> declaration (read instance))"
                    )
                    |> ignore

                    result.AppendLine ("        /// <summary>Binds a tracked reader with importance fixed in the static stylesheet.</summary>")
                    |> ignore

                    result.AppendLine (
                        "        let reactiveImportant (read:'instance -> Value<Markers."
                        + marker
                        + ">) : global.Ranvier.Css.StyleNode<'instance> = global.Ranvier.Css.Style.dynamicImportantWithKeywords cssWideKeywords "
                        + name
                        + " (fun instance -> declaration (read instance))"
                    )
                    |> ignore
                | _ -> ()

            let sugarSource, sugarMap, numericRuntime =
                SugarGeneration.render namespaceName manifest.RootElement

            let enriched = JsonNode.Parse bindings.Manifest
            enriched["sugar"] <- JsonNode.Parse (sugarMap.GetRawText ())
            enriched["numericSemantics"]["runtime"] <- JsonNode.Parse (numericRuntime.GetRawText ())

            Ok
                { bindings with
                    Source =
                        result.ToString().Replace("\r\n", "\n")
                        + sugarSource
                    Manifest =
                        enriched.ToJsonString(JsonSerializerOptions (WriteIndented = true)).Replace("\r\n", "\n")
                        + "\n"
                }

    /// <summary>Emits property declarations and discoverable typed values over a shared specification grammar table.</summary>
    /// <param name="namespaceName">A nonempty dot-separated F# namespace.</param>
    /// <param name="catalogue">Consolidated definitions with unique feature identities.</param>
    /// <returns>Generated source and metadata, or diagnostics for an invalid namespace or duplicate identity.</returns>
    let generate namespaceName (catalogue: Catalogue) : Result<GeneratedBindings, InputDiagnostic list> =
        if
            String.IsNullOrWhiteSpace namespaceName
            || not (System.Text.RegularExpressions.Regex.IsMatch (namespaceName, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$"))
        then
            Error
                [
                    {
                        Path = "namespace"
                        Message = "Expected an F# namespace composed of identifiers."
                    }
                ]
        elif
            (catalogue.Features
             |> List.map _.Id
             |> List.distinct
             |> List.length)
            <> catalogue.Features.Length
        then
            Error
                [
                    {
                        Path = "features"
                        Message = "Duplicate feature identities must be consolidated before generation."
                    }
                ]
        else
            let compact = CompactGeneration.render namespaceName catalogue
            let resolution = Resolution.resolve catalogue

            Ok
                {
                    Source = compact.Source
                    Manifest = compact.Manifest
                    FeatureCount = catalogue.Features.Length
                    OpaqueCount =
                        catalogue.Features
                        |> List.filter (fun feature ->
                            match feature.Grammar with
                            | GrammarStatus.Parsed _ -> false
                            | _ -> true)
                        |> List.length
                    UnresolvedCount =
                        resolution.References
                        |> List.filter (fun reference ->
                            match reference.Target with
                            | ReferenceTarget.Missing
                            | ReferenceTarget.Ambiguous _ -> true
                            | _ -> false)
                        |> List.length
                }
