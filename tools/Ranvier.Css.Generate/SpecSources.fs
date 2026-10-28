namespace Ranvier.Css.Spec

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

/// <summary>Current definitions and the superseded or explicitly obsolete source definitions.</summary>
type ConsolidatedSources =
    {
        Current: Catalogue
        Obsolete: Catalogue
    }

/// <summary>Consumes individual Webref ed/css extracts and reconciles definitions within specification series.</summary>
module SpecSources =
    exception private InvalidInput of InputDiagnostic

    let private json (value: string) =
        JsonSerializer.Serialize value

    /// <summary>Preserves every definition, nested value, and source metadata in an individual spec extract.</summary>
    /// <remarks>Nested values use their ancestor names as scopes. Duplicate identities gain a source-path scope.</remarks>
    let consume (label: string) (utf8: byte array) : Result<Catalogue, InputDiagnostic list> =
        let invalid path message =
            raise (InvalidInput { Path = path; Message = message })

        if
            isNull utf8
            || utf8.Length > Webref.MaximumInputBytes
        then
            Error
                [
                    {
                        Path = "$"
                        Message = "Input must contain at most 32 MiB of UTF-8 JSON."
                    }
                ]
        else
            try
                use document = JsonDocument.Parse (ReadOnlyMemory<byte> utf8)
                let root = document.RootElement

                let objectAt path (value: JsonElement) =
                    if value.ValueKind <> JsonValueKind.Object then
                        invalid path "Expected an object."

                    let names = HashSet<string>(StringComparer.Ordinal)

                    for field in value.EnumerateObject () do
                        if not (names.Add field.Name) then
                            invalid (path + "." + field.Name) "Duplicate JSON member."

                let text path (value: JsonElement) =
                    if value.ValueKind <> JsonValueKind.String then
                        invalid path "Expected a string."

                    value.GetString ()

                let required path name (value: JsonElement) =
                    match value.TryGetProperty (name: string) with
                    | true, item ->
                        let result = text (path + "." + name) item

                        if String.IsNullOrWhiteSpace result then
                            invalid (path + "." + name) "Expected a nonempty string."

                        result
                    | _ -> invalid (path + "." + name) "Missing required member."

                objectAt "$" root

                let spec =
                    match root.TryGetProperty "spec" with
                    | true, value ->
                        objectAt "$.spec" value
                        value
                    | _ -> invalid "$.spec" "Missing required member."

                let url = required "$.spec" "url" spec
                let title = required "$.spec" "title" spec

                match Uri.TryCreate (url, UriKind.Absolute) with
                | true, uri when uri.Scheme = "https" || uri.Scheme = "http" -> ()
                | _ -> invalid "$.spec.url" "Expected an absolute HTTP(S) spec URL."

                let slug =
                    Uri(url).Segments
                    |> Array.last
                    |> fun value -> value.Trim ('/')

                let levelMatch = Regex.Match (title, @"\bLevel\s+(\d+)\b", RegexOptions.IgnoreCase)
                let suffix = Regex.Match (slug, @"^(.*?)-(\d+)$")
                let series = if suffix.Success then suffix.Groups[1].Value else slug

                let parseLevel path (digits: string) =
                    match Int32.TryParse digits with
                    | true, number -> number
                    | _ -> invalid path "Specification level must fit a nonnegative 32-bit integer."

                let level =
                    if levelMatch.Success then
                        parseLevel "$.spec.title" levelMatch.Groups[1].Value
                    elif suffix.Success then
                        parseLevel "$.spec.url" suffix.Groups[2].Value
                    else
                        1

                let sourceMetadata =
                    root.EnumerateObject ()
                    |> Seq.filter (fun item -> not (Set.contains item.Name (set [ "properties"; "atrules"; "selectors"; "values" ])))
                    |> Seq.map (fun item -> json item.Name + ":" + item.Value.GetRawText ())
                    |> String.concat ","
                    |> fun fields -> "{" + fields + "}"

                let features = ResizeArray<Feature>()
                let seen = HashSet<FeatureId>()

                let strings path name (value: JsonElement) =
                    match value.TryGetProperty (name: string) with
                    | false, _ -> []
                    | true, item when item.ValueKind = JsonValueKind.String -> [ text (path + "." + name) item ]
                    | true, item when item.ValueKind = JsonValueKind.Array ->
                        [
                            for i, entry in item.EnumerateArray () |> Seq.indexed -> text (sprintf "%s.%s[%d]" path name i) entry
                        ]
                    | _ -> invalid (path + "." + name) "Expected a string or string array."

                let rec read kind owner ancestors path (value: JsonElement) =
                    objectAt path value
                    let rawName = required path "name" value

                    let name =
                        if
                            kind = FeatureKind.Type
                            && rawName.StartsWith ("<", StringComparison.Ordinal)
                            && rawName.EndsWith (">", StringComparison.Ordinal)
                        then
                            rawName.Substring (1, rawName.Length - 2)
                        else
                            rawName

                    let scopes =
                        List.append ancestors (strings path "for" value)
                        |> List.distinct
                        |> List.sort

                    let baseId =
                        {
                            Kind = kind
                            Name = name
                            Scopes = scopes
                            Owner = owner
                        }

                    let id =
                        if seen.Add baseId then
                            baseId
                        else
                            { baseId with
                                Scopes = scopes @ [ "#source:" + path ]
                            }

                    let href =
                        match value.TryGetProperty "href" with
                        | true, item -> text (path + ".href") item
                        | _ -> url

                    let grammar =
                        match value.TryGetProperty "value" with
                        | false, _ -> GrammarStatus.Missing
                        | true, item ->
                            let source = text (path + ".value") item

                            match Grammar.parse source with
                            | Ok tree -> GrammarStatus.Parsed (source, tree)
                            | Error error -> GrammarStatus.Rejected (source, error)

                    let metadata =
                        value.EnumerateObject ()
                        |> Seq.map (fun item -> item.Name, item.Value.GetRawText ())
                        |> Map.ofSeq
                        |> Map.add "sourceSpec" (spec.GetRawText ())
                        |> Map.add "sourceDocument" sourceMetadata
                        |> Map.add "sourceLabel" (json label)
                        |> Map.add "sourcePath" (json path)
                        |> Map.add "specSeries" (json series)
                        |> Map.add "specLevel" (string level)

                    features.Add
                        {
                            Id = id
                            Href = href
                            Extended = strings path "extended" value
                            Grammar = grammar
                            Metadata = metadata
                        }

                    readArray "descriptors" FeatureKind.Descriptor (Some name) ancestors path value

                    match value.TryGetProperty "values" with
                    | true, items when items.ValueKind = JsonValueKind.Array ->
                        for index, item in items.EnumerateArray () |> Seq.indexed do
                            let childPath = sprintf "%s.values[%d]" path index
                            objectAt childPath item

                            let childKind =
                                match item.TryGetProperty "type" with
                                | true, tag when tag.ValueKind = JsonValueKind.String ->
                                    match tag.GetString () with
                                    | "function" -> FeatureKind.Function
                                    | "selector" -> FeatureKind.Selector
                                    | "at-rule" -> FeatureKind.AtRule
                                    | _ -> FeatureKind.Type
                                | _ -> FeatureKind.Type

                            read childKind None (ancestors @ [ rawName ]) childPath item
                    | false, _ -> ()
                    | _ -> invalid (path + ".values") "Expected an array."

                and readArray key kind owner ancestors path (value: JsonElement) =
                    match value.TryGetProperty (key: string) with
                    | true, items when items.ValueKind = JsonValueKind.Array ->
                        for index, item in items.EnumerateArray () |> Seq.indexed do
                            read kind owner ancestors (sprintf "%s.%s[%d]" path key index) item
                    | false, _ -> ()
                    | _ -> invalid (path + "." + key) "Expected an array."

                readArray "properties" FeatureKind.Property None [] "$" root
                readArray "atrules" FeatureKind.AtRule None [] "$" root
                readArray "selectors" FeatureKind.Selector None [] "$" root

                match root.TryGetProperty "values" with
                | true, items when items.ValueKind = JsonValueKind.Array ->
                    for index, item in items.EnumerateArray () |> Seq.indexed do
                        objectAt (sprintf "$.values[%d]" index) item

                        let kind =
                            match item.TryGetProperty "type" with
                            | true, tag when tag.ValueKind = JsonValueKind.String ->
                                match tag.GetString () with
                                | "function" -> FeatureKind.Function
                                | "selector" -> FeatureKind.Selector
                                | "at-rule" -> FeatureKind.AtRule
                                | _ -> FeatureKind.Type
                            | _ -> FeatureKind.Type

                        read kind None [] (sprintf "$.values[%d]" index) item
                | false, _ -> ()
                | _ -> invalid "$.values" "Expected an array."

                Ok
                    {
                        Identity =
                            {
                                Label = label
                                Sha256 = Convert.ToHexStringLower (SHA256.HashData utf8)
                            }
                        Features =
                            features
                            |> Seq.sortBy (fun feature -> feature.Id)
                            |> Seq.toList
                    }
            with
            | InvalidInput error -> Error [ error ]
            | :? JsonException as error ->
                Error
                    [
                        {
                            Path = (if isNull error.Path then "$" else error.Path)
                            Message = error.Message
                        }
                    ]

    let private consolidateCore (canonical: Catalogue option) (catalogues: Catalogue list) : Result<ConsolidatedSources, InputDiagnostic list> =
        let metadataText key (feature: Feature) =
            feature.Metadata
            |> Map.tryFind key
            |> Option.map (fun raw ->
                use document = JsonDocument.Parse raw

                if document.RootElement.ValueKind = JsonValueKind.String then
                    document.RootElement.GetString ()
                else
                    raw)

        let hrefLocation (feature: Feature) =
            match Uri.TryCreate (feature.Href, UriKind.Absolute) with
            | true, uri ->
                let slug =
                    uri.Segments
                    |> Array.last
                    |> fun value -> value.Trim ('/')

                let suffix = Regex.Match (slug, @"^(.*?)-(\d+)$")

                if suffix.Success then
                    match Int32.TryParse suffix.Groups[2].Value with
                    | true, number -> Some (suffix.Groups[1].Value, number)
                    | _ -> None
                else
                    Some (slug, 1)
            | _ -> None

        let series feature =
            metadataText "specSeries" feature
            |> Option.orElseWith (fun () -> hrefLocation feature |> Option.map fst)

        let level feature =
            metadataText "specLevel" feature
            |> Option.bind (fun value ->
                match Int32.TryParse value with
                | true, number -> Some number
                | _ -> None)
            |> Option.orElseWith (fun () -> hrefLocation feature |> Option.map snd)
            |> Option.defaultValue 1

        let isObsolete feature =
            [ "obsolete"; "deprecated"; "supersededBy" ]
            |> List.exists (fun key ->
                match metadataText key feature with
                | Some value -> value <> "false" && value <> "null" && value <> ""
                | None -> false)

        let archive (feature: Feature) =
            let source = defaultArg (metadataText "sourceLabel" feature) feature.Href
            let sourcePath = defaultArg (metadataText "sourcePath" feature) ""

            { feature with
                Id =
                    { feature.Id with
                        Scopes =
                            feature.Id.Scopes
                            @ [ "#obsolete:" + source + ":" + sourcePath ]
                    }
                Metadata =
                    feature.Metadata
                    |> Map.add "originalIdentity" (json (sprintf "%A" feature.Id))
                    |> Map.add "obsolete" "true"
            }

        let current = ResizeArray<Feature>()
        let obsolete = ResizeArray<Feature>()
        let errors = ResizeArray<InputDiagnostic>()

        let canonicalFeatures =
            canonical
            |> Option.map (fun catalogue ->
                catalogue.Features
                |> List.map (fun feature -> feature.Id, feature)
                |> Map.ofList)
            |> Option.defaultValue Map.empty

        let inputs =
            catalogues
            |> List.sortBy (fun catalogue -> catalogue.Identity.Label, catalogue.Identity.Sha256)

        let identity suffix =
            let identityInputs = inputs @ (canonical |> Option.toList)

            let bytes =
                identityInputs
                |> List.map (fun catalogue ->
                    catalogue.Identity.Label
                    + ":"
                    + catalogue.Identity.Sha256)
                |> String.concat "\n"
                |> Encoding.UTF8.GetBytes

            {
                Label = "specs:" + suffix
                Sha256 = Convert.ToHexStringLower (SHA256.HashData bytes)
            }

        for id, definitions in
            inputs
            |> List.collect (fun catalogue -> catalogue.Features)
            |> List.groupBy (fun feature -> feature.Id)
            |> List.sortBy fst do
            let active, deprecated = definitions |> List.partition (isObsolete >> not)

            for feature in deprecated do
                obsolete.Add (archive feature)

            match active with
            | [] -> ()
            | _ ->
                let ordered =
                    active
                    |> List.sortBy (fun feature -> -(level feature), feature.Href, metadataText "sourceLabel" feature)

                let selected = List.head ordered

                let latest =
                    match canonicalFeatures |> Map.tryFind id with
                    | Some merged ->
                        match
                            ordered
                            |> List.tryFind (fun feature -> series feature = series merged)
                        with
                        | Some newest when level newest > level merged -> newest
                        | _ -> merged
                    | None -> selected

                let sourceSeries = ordered |> List.map series |> List.distinct

                let staleCanonicalDropsExtensions =
                    match canonicalFeatures |> Map.tryFind id with
                    | Some merged when
                        series merged = series latest
                        && level merged < level latest
                        ->
                        ordered
                        |> List.groupBy series
                        |> List.exists (fun (sourceSeries, definitions) ->
                            sourceSeries <> series latest
                            && definitions.Head.Grammar <> latest.Grammar)
                    | _ -> false

                if staleCanonicalDropsExtensions then
                    errors.Add
                        {
                            Path = sprintf "$.%A.%s" id.Kind id.Name
                            Message =
                                "Canonical merged definition predates the latest source level and cross-series extensions conflict. Refresh the canonical dataset before consolidating: "
                                + (ordered
                                   |> List.map (fun feature -> defaultArg (metadataText "sourceLabel" feature) feature.Href)
                                   |> List.sort
                                   |> String.concat ", ")
                        }
                elif
                    not (Map.containsKey id canonicalFeatures)
                    && sourceSeries.Length > 1
                    && (ordered
                        |> List.map (fun feature -> feature.Grammar)
                        |> List.distinct
                        |> List.length)
                        >
                        1
                then
                    errors.Add
                        {
                            Path = sprintf "$.%A.%s" id.Kind id.Name
                            Message =
                                "Conflicting definitions from unrelated spec series: "
                                + (ordered
                                   |> List.map (fun feature -> defaultArg (metadataText "sourceLabel" feature) feature.Href)
                                   |> List.sort
                                   |> String.concat ", ")
                        }
                else
                    let provenance =
                        ordered
                        |> List.map (fun feature ->
                            feature.Metadata
                            |> Map.tryFind "sourceSpec"
                            |> Option.defaultValue "{}")
                        |> List.distinct
                        |> String.concat ","

                    let definitionsJson =
                        ordered
                        |> List.map (fun feature ->
                            feature.Metadata
                            |> Map.toList
                            |> List.map (fun (key, raw) -> json key + ":" + raw)
                            |> String.concat ","
                            |> fun fields -> "{" + fields + "}")
                        |> String.concat ","

                    current.Add
                        { latest with
                            Metadata =
                                latest.Metadata
                                |> Map.add "sourceSpecs" ("[" + provenance + "]")
                                |> Map.add "sourceDefinitions" ("[" + definitionsJson + "]")
                        }

                    for feature in ordered do
                        let newestInSeries =
                            ordered
                            |> List.find (fun item -> series item = series feature)

                        let replacement =
                            if
                                series latest = series feature
                                && level latest > level newestInSeries
                            then
                                latest
                            else
                                newestInSeries

                        if level feature < level replacement then
                            obsolete.Add (
                                archive
                                    { feature with
                                        Metadata =
                                            feature.Metadata
                                            |> Map.add "supersededBy" (json replacement.Href)
                                    }
                            )

        for KeyValue (id, feature) in canonicalFeatures do
            if
                not (current |> Seq.exists (fun item -> item.Id = id))
                && not (
                    inputs
                    |> List.exists (fun catalogue ->
                        catalogue.Features
                        |> List.exists (fun item -> item.Id = id && isObsolete item))
                )
            then
                current.Add feature

        if errors.Count > 0 then
            Error (List.ofSeq errors)
        else
            Ok
                {
                    Current =
                        {
                            Identity = identity "current"
                            Features =
                                current
                                |> Seq.sortBy (fun feature -> feature.Id)
                                |> Seq.toList
                        }
                    Obsolete =
                        {
                            Identity = identity "obsolete"
                            Features = List.ofSeq obsolete
                        }
                }

    /// <summary>Selects the latest level within each series; conflicting unrelated series return diagnostics.</summary>
    let consolidate (catalogues: Catalogue list) =
        consolidateCore None catalogues

    /// <summary>Resolves cross-series extensions with canonical merged definitions and retains raw source definitions as metadata.</summary>
    /// <remarks>Canonical-only definitions remain current; earlier levels remain obsolete.
    /// Stale canonical merges with conflicting extensions require a refreshed canonical dataset.</remarks>
    let consolidateWith (canonical: Catalogue) (catalogues: Catalogue list) =
        consolidateCore (Some canonical) catalogues
