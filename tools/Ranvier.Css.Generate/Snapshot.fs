namespace Ranvier.Css.Spec

open System.Text.Json
open System.Text.Json.Nodes

/// <summary>Exports a versioned JSON model with explicit node tags, provenance and grammar coverage.</summary>
module Snapshot =
    let private text (value: string) : JsonNode =
        JsonValue.Create value

    let private integer (value: int) : JsonNode =
        JsonValue.Create value

    let private boolean (value: bool) : JsonNode =
        JsonValue.Create value

    let private objectNode fields : JsonNode =
        let value = JsonObject ()

        for key, item in fields do
            value.Add (key, item)

        value

    let private arrayNode values : JsonNode =
        let value = JsonArray ()

        for item in values do
            value.Add (item)

        value

    let private optional convert value =
        value
        |> Option.map convert
        |> Option.defaultValue null

    let private bound value =
        match value with
        | RangeBound.NegativeInfinity -> objectNode [ "kind", text "negativeInfinity" ]
        | RangeBound.PositiveInfinity -> objectNode [ "kind", text "positiveInfinity" ]
        | RangeBound.Finite (number, unitName) ->
            objectNode
                [
                    "kind", text "finite"
                    "value", JsonValue.Create number
                    "unit", text unitName
                ]

    let rec private tree (node: GrammarNode) =
        let children kind items =
            [ "kind", text kind; "children", arrayNode (List.map tree items) ]

        let fields =
            match node.Expression with
            | GrammarExpression.Literal value -> [ "kind", text "literal"; "value", text value ]
            | GrammarExpression.Reference reference ->
                [
                    "kind", text "reference"
                    "referenceKind", text (string reference.Kind)
                    "name", text reference.Name
                    "range", optional (fun (r: NumericRange) -> objectNode [ "minimum", bound r.Minimum; "maximum", bound r.Maximum ]) reference.Range
                ]
            | GrammarExpression.TypeApplication (name, argument) -> [ "kind", text "typeApplication"; "name", text name; "argument", tree argument ]
            | GrammarExpression.Sequence nodes -> children "sequence" nodes
            | GrammarExpression.AllOf nodes -> children "allOf" nodes
            | GrammarExpression.AnyOf nodes -> children "anyOf" nodes
            | GrammarExpression.Choice nodes -> children "choice" nodes
            | GrammarExpression.Group inner -> [ "kind", text "group"; "child", tree inner ]
            | GrammarExpression.Function (name, arguments) ->
                [
                    "kind", text "function"
                    "name", text name
                    "arguments", optional tree arguments
                ]
            | GrammarExpression.Block (opening, contents) ->
                [
                    "kind", text "block"
                    "opening", text opening
                    "contents", optional tree contents
                ]
            | GrammarExpression.Repeat (inner, repetition) ->
                [
                    "kind", text "repeat"
                    "child", tree inner
                    "minimum", integer repetition.Minimum
                    "maximum", optional integer repetition.Maximum
                    "commaSeparated", boolean repetition.CommaSeparated
                ]
            | GrammarExpression.Required inner -> [ "kind", text "required"; "child", tree inner ]

        objectNode (
            fields
            @ [
                "span", objectNode [ "offset", integer node.Span.Offset; "length", integer node.Span.Length ]
            ]
        )

    let private grammar value =
        match value with
        | GrammarStatus.Missing -> objectNode [ "status", text "missing" ]
        | GrammarStatus.Parsed (source, node) -> objectNode [ "status", text "parsed"; "source", text source; "tree", tree node ]
        | GrammarStatus.Rejected (source, diagnostic) ->
            objectNode
                [
                    "status", text "rejected"
                    "source", text source
                    "diagnostic", objectNode [ "offset", integer diagnostic.Offset; "message", text diagnostic.Message ]
                ]

    /// <summary>Counts parsed, rejected and grammar-free definitions, in that order.</summary>
    let coverage (catalogue: Catalogue) =
        catalogue.Features
        |> List.fold
            (fun (parsed, rejected, missing) feature ->
                match feature.Grammar with
                | GrammarStatus.Parsed _ -> parsed + 1, rejected, missing
                | GrammarStatus.Rejected _ -> parsed, rejected + 1, missing
                | GrammarStatus.Missing -> parsed, rejected, missing + 1)
            (0, 0, 0)

    /// <summary>Serializes in identity order using LF endings, with every metadata value emitted as JSON.</summary>
    let toJson (catalogue: Catalogue) =
        let features =
            catalogue.Features
            |> List.sortBy _.Id
            |> List.map (fun feature ->
                objectNode
                    [
                        "kind", text (string feature.Id.Kind)
                        "name", text feature.Id.Name
                        "scopes", arrayNode (List.map text feature.Id.Scopes)
                        "owner", optional text feature.Id.Owner
                        "href", text feature.Href
                        "extended", arrayNode (List.map text feature.Extended)
                        "grammar", grammar feature.Grammar
                        "metadata",
                        objectNode (
                            feature.Metadata
                            |> Map.toList
                            |> List.map (fun (key, value) -> key, JsonNode.Parse value)
                        )
                    ])

        let parsed, rejected, missing = coverage catalogue

        let result =
            objectNode
                [
                    "schemaVersion", integer 1
                    "dataset",
                    objectNode
                        [
                            "label", text catalogue.Identity.Label
                            "sha256", text catalogue.Identity.Sha256
                        ]
                    "coverage",
                    objectNode
                        [
                            "total", integer catalogue.Features.Length
                            "parsed", integer parsed
                            "rejected", integer rejected
                            "missing", integer missing
                        ]
                    "features", arrayNode features
                    "numericSemantics", NumericProjection.rulesMetadata ()
                ]

        result.ToJsonString(JsonSerializerOptions (WriteIndented = true)).Replace("\r\n", "\n")
        + "\n"
