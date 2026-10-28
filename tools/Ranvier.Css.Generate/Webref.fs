namespace Ranvier.Css.Spec

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text.Json

/// <summary>Consumes the consolidated css.json format published in @webref/css version 8.</summary>
module Webref =
    /// <summary>Maximum accepted UTF-8 dataset size, in bytes.</summary>
    [<Literal>]
    let MaximumInputBytes = 33554432

    exception private InvalidInput of InputDiagnostic

    /// <summary>Reads UTF-8 JSON into a stable catalogue; malformed structure returns JSON-path diagnostics.</summary>
    /// <remarks>Grammar failures remain in the catalogue. Metadata values retain their original JSON representation.</remarks>
    let consume (label: string) (utf8: byte array) : Result<Catalogue, InputDiagnostic list> =
        let invalid path message =
            raise (InvalidInput { Path = path; Message = message })

        if isNull utf8 || utf8.Length > MaximumInputBytes then
            Error
                [
                    {
                        Path = "$"
                        Message = "Input must contain at most 32 MiB of UTF-8 JSON."
                    }
                ]
        else
            try
                use document = JsonDocument.Parse (ReadOnlyMemory<byte>(utf8))
                let errors = ResizeArray<InputDiagnostic>()
                let features = ResizeArray<Feature>()

                let objectAt path (value: JsonElement) =
                    if value.ValueKind <> JsonValueKind.Object then
                        invalid path "Expected an object."

                    let names = HashSet<string>(StringComparer.Ordinal)

                    for property in value.EnumerateObject () do
                        if not (names.Add property.Name) then
                            invalid (path + "." + property.Name) "Duplicate JSON member."

                let stringAt path (value: JsonElement) =
                    if value.ValueKind <> JsonValueKind.String then
                        invalid path "Expected a string."

                    value.GetString ()

                let requiredString path name (value: JsonElement) =
                    match value.TryGetProperty (name: string) with
                    | true, field ->
                        let text = stringAt (path + "." + name) field

                        if String.IsNullOrWhiteSpace text then
                            invalid (path + "." + name) "Expected a nonempty string."

                        text
                    | _ -> invalid (path + "." + name) "Missing required member."

                let optionalArray path name (value: JsonElement) =
                    match value.TryGetProperty (name: string) with
                    | false, _ -> []
                    | true, field when field.ValueKind = JsonValueKind.Array ->
                        [
                            for i, item in field.EnumerateArray () |> Seq.indexed do
                                yield stringAt (sprintf "%s.%s[%d]" path name i) item
                        ]
                    | _ -> invalid (path + "." + name) "Expected an array of strings."

                let parseGrammar path (value: JsonElement) =
                    match value.TryGetProperty "syntax" with
                    | false, _ -> GrammarStatus.Missing
                    | true, syntax ->
                        let source = stringAt (path + ".syntax") syntax

                        match Grammar.parse source with
                        | Ok tree -> GrammarStatus.Parsed (source, tree)
                        | Error diagnostic -> GrammarStatus.Rejected (source, diagnostic)

                let seen = HashSet<FeatureId>()

                let readFeature kind owner inheritedScopes path (value: JsonElement) =
                    objectAt path value

                    if
                        kind <> FeatureKind.AtRule
                        && (value.TryGetProperty "descriptors" |> fst)
                    then
                        invalid (path + ".descriptors") "Only at-rules may contain descriptors."

                    let name = requiredString path "name" value
                    let href = requiredString path "href" value

                    match Uri.TryCreate (href, UriKind.Absolute) with
                    | true, uri when uri.Scheme = "https" || uri.Scheme = "http" -> ()
                    | _ -> invalid (path + ".href") "Expected an absolute HTTP(S) spec URL."

                    let scopes =
                        if kind = FeatureKind.Descriptor then
                            inheritedScopes
                        else
                            optionalArray path "for" value
                            |> List.distinct
                            |> List.sort

                    let id =
                        {
                            Kind = kind
                            Name = name
                            Scopes = scopes
                            Owner = owner
                        }

                    if not (seen.Add id) then
                        invalid path ("Duplicate feature identity: " + name)

                    let extended = optionalArray path "extended" value
                    let grammar = parseGrammar path value

                    let metadata =
                        value.EnumerateObject ()
                        |> Seq.filter (fun p -> not (Set.contains p.Name (set [ "name"; "href"; "extended"; "syntax"; "descriptors" ])))
                        |> Seq.map (fun p -> p.Name, p.Value.GetRawText ())
                        |> Map.ofSeq

                    let feature =
                        {
                            Id = id
                            Href = href
                            Extended = extended
                            Grammar = grammar
                            Metadata = metadata
                        }

                    features.Add feature
                    feature

                let attempt action =
                    try
                        action ()
                    with InvalidInput diagnostic ->
                        errors.Add diagnostic

                objectAt "$" document.RootElement

                let categories =
                    [
                        "properties", FeatureKind.Property
                        "types", FeatureKind.Type
                        "functions", FeatureKind.Function
                        "selectors", FeatureKind.Selector
                        "atrules", FeatureKind.AtRule
                    ]

                for property in document.RootElement.EnumerateObject () do
                    if
                        not (
                            categories
                            |> List.exists (fun (name, _) -> name = property.Name)
                        )
                    then
                        errors.Add
                            {
                                Path = "$." + property.Name
                                Message = "Unknown Webref category; expected consolidated v8 css.json."
                            }

                for category, kind in categories do
                    attempt (fun () ->
                        match document.RootElement.TryGetProperty category with
                        | true, items when items.ValueKind = JsonValueKind.Array ->
                            for i, value in items.EnumerateArray () |> Seq.indexed do
                                let path = sprintf "$.%s[%d]" category i

                                attempt (fun () ->
                                    let feature = readFeature kind None [] path value

                                    if kind = FeatureKind.AtRule then
                                        match value.TryGetProperty "descriptors" with
                                        | true, descriptors when descriptors.ValueKind = JsonValueKind.Array ->
                                            for j, descriptor in descriptors.EnumerateArray () |> Seq.indexed do
                                                attempt (fun () ->
                                                    readFeature
                                                        FeatureKind.Descriptor
                                                        (Some feature.Id.Name)
                                                        feature.Id.Scopes
                                                        (sprintf "%s.descriptors[%d]" path j)
                                                        descriptor
                                                    |> ignore)
                                        | _ -> invalid (path + ".descriptors") "Expected an array of descriptors.")
                        | _ -> invalid ("$." + category) "Expected a category array.")

                if errors.Count > 0 then
                    Error (List.ofSeq errors)
                else
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
                                |> List.ofSeq
                        }
            with
            | InvalidInput diagnostic -> Error [ diagnostic ]
            | :? JsonException as error ->
                Error
                    [
                        {
                            Path = (if isNull error.Path then "$" else error.Path)
                            Message = error.Message
                        }
                    ]
