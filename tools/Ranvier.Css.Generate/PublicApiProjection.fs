namespace Ranvier.Css.Spec

open System
open System.Text.Json
open System.Text.RegularExpressions

type internal PolicyNeed =
    {
        Span: SourceSpan
        Syntax: string
        Reason: string
    }

type internal Shape =
    {
        Kind: string
        Role: string option
        Category: string option
        Target: FeatureId option
        Resolution: ReferenceTarget option
        Children: Shape list
        Repeat: Repetition option
        Range: NumericRange option
        Optional: bool
        AtLeastOne: bool
        Original: GrammarNode
    }

type internal FeatureShape =
    {
        Id: FeatureId
        Path: string list
        DeclarationPath: string list option
        Name: string
        Category: string
        Grammar: Shape option
        NeedsPolicy: PolicyNeed list
    }

module internal PublicApiProjection =
    let private words (value: string) =
        Regex.Matches (value, "[A-Za-z0-9]+")
        |> Seq.cast<Match>
        |> Seq.map (fun matched -> matched.Value)
        |> Seq.toList

    let private pascal value =
        let semantic =
            match value with
            | "||" -> "ColumnCombinator"
            | ">" -> "ChildCombinator"
            | "+" -> "NextSiblingCombinator"
            | "~" -> "SubsequentSiblingCombinator"
            | "*" -> "UniversalSelector"
            | "|" -> "NamespaceSeparator"
            | "&" -> "NestingSelector"
            | _ -> value

        words semantic
        |> List.map (fun word ->
            string (Char.ToUpperInvariant word[0])
            + word.Substring 1)
        |> String.concat ""

    let private camel value =
        let name = pascal value

        if String.IsNullOrEmpty name then
            name
        else
            string (Char.ToLowerInvariant name[0])
            + name.Substring 1

    let private nameOf (value: string) =
        let value = value.Trim ()

        let value =
            if
                value.Length > 2
                && value.StartsWith ("<", StringComparison.Ordinal)
                && value.EndsWith (">", StringComparison.Ordinal)
            then
                value.Substring (1, value.Length - 2)
            else
                value

        value.Replace ("()", "")

    let private current (id: FeatureId) =
        id.Scopes
        |> List.exists (fun scope -> scope.StartsWith ("#obsolete", StringComparison.Ordinal))
        |> not

    let private core =
        set
            [
                "length"
                "color"
                "time"
                "angle"
                "percentage"
                "number"
                "integer"
                "frequency"
                "resolution"
                "flex"
                "length-percentage"
                "angle-percentage"
                "time-percentage"
                "frequency-percentage"
                "resolution-percentage"
                "flex-percentage"
                "string"
                "string-token"
                "ident"
                "identifier"
                "ident-token"
                "custom-ident"
                "dashed-ident"
                "custom-property-name"
                "number-token"
                "percentage-token"
            ]

    let private longhands (feature: Feature) =
        match Map.tryFind "longhands" feature.Metadata with
        | None -> []
        | Some json ->
            use document = JsonDocument.Parse json

            if document.RootElement.ValueKind = JsonValueKind.Array then
                document.RootElement.EnumerateArray ()
                |> Seq.choose (fun item ->
                    if item.ValueKind = JsonValueKind.String then
                        Some (item.GetString ())
                    else
                        None)
                |> Seq.toList
            else
                []

    let project (catalogue: Catalogue) (resolution: ResolutionResult) : FeatureShape list =
        let features =
            catalogue.Features
            |> List.filter (fun feature -> current feature.Id)
            |> List.sortBy (fun feature -> feature.Id)

        let properties =
            features
            |> List.filter (fun feature -> feature.Id.Kind = FeatureKind.Property)
            |> List.map (fun feature -> feature.Id.Name)
            |> Set.ofList

        let rec propertyPath (name: string) =
            let prefixes =
                properties
                |> Seq.filter (fun prefix -> name.StartsWith (prefix + "-", StringComparison.Ordinal))
                |> Seq.sortBy (fun prefix -> -prefix.Length, prefix)
                |> Seq.toList

            match prefixes with
            | prefix :: _ ->
                propertyPath prefix
                @ [ pascal (name.Substring (prefix.Length + 1)) ]
            | [] -> [ pascal name ]

        let namedPath kind name =
            let name = nameOf name

            match kind with
            | FeatureKind.Property -> propertyPath name
            | FeatureKind.Type when Set.contains name core -> [ pascal name ]
            | FeatureKind.Type -> [ "Values"; pascal name ]
            | FeatureKind.Function -> [ "Functions"; pascal name ]
            | FeatureKind.Selector -> [ "Selectors"; pascal name ]
            | FeatureKind.AtRule -> [ "AtRules"; pascal name ]
            | FeatureKind.Descriptor -> [ "Descriptors"; pascal name ]

        let path (id: FeatureId) =
            let scopes =
                id.Scopes
                |> List.filter (fun scope -> not (scope.StartsWith ("#", StringComparison.Ordinal)))
                |> List.sort

            let scopedOwner =
                match id.Owner, scopes with
                | Some owner, _ -> Some (namedPath FeatureKind.AtRule owner)
                | None, scope :: _ when Set.contains scope properties -> Some (propertyPath scope)
                | None, scope :: _ when scope.StartsWith ("<", StringComparison.Ordinal) -> Some (namedPath FeatureKind.Type scope)
                | None, scope :: _ when scope.EndsWith ("()", StringComparison.Ordinal) -> Some (namedPath FeatureKind.Function scope)
                | None, scope :: _ when scope.StartsWith ("@", StringComparison.Ordinal) -> Some (namedPath FeatureKind.AtRule scope)
                | None, scope :: _ -> Some (propertyPath scope)
                | _ -> None

            let tail =
                match scopedOwner with
                | Some owner -> owner @ [ pascal (nameOf id.Name) ]
                | None -> namedPath id.Kind id.Name

            "CssValue" :: tail

        let references =
            resolution.References
            |> List.groupBy (fun reference -> reference.Owner, reference.Span)
            |> Map.ofList

        let shapeFor (feature: Feature) =
            let needs = ResizeArray<PolicyNeed>()

            let source =
                match feature.Grammar with
                | GrammarStatus.Parsed (source, _) -> source
                | GrammarStatus.Rejected (source, _) -> source
                | GrammarStatus.Missing -> ""

            let need (node: GrammarNode) reason =
                let syntax =
                    if
                        node.Span.Offset >= 0
                        && node.Span.Length >= 0
                        && node.Span.Offset + node.Span.Length
                           <= source.Length
                    then
                        source.Substring (node.Span.Offset, node.Span.Length)
                    else
                        source

                needs.Add
                    {
                        Span = node.Span
                        Syntax = syntax
                        Reason = reason
                    }

            let roleName (reference: GrammarReference) =
                let referenceName = nameOf reference.Name
                let ownerName = nameOf feature.Id.Name

                let ownerStem =
                    if ownerName.StartsWith ("single-", StringComparison.Ordinal) then
                        ownerName.Substring 7
                    else
                        ownerName

                let stripOwner (name: string) =
                    [ ownerName; ownerStem ]
                    |> List.tryPick (fun prefix ->
                        if name.StartsWith (prefix + "-", StringComparison.Ordinal) then
                            Some (name.Substring (prefix.Length + 1))
                        else
                            None)
                    |> Option.defaultValue name

                let candidates =
                    longhands feature
                    |> List.filter (fun longhand ->
                        longhand = referenceName
                        || longhand.EndsWith ("-" + referenceName, StringComparison.Ordinal)
                        || referenceName.EndsWith ("-" + (longhand.Split '-' |> Array.last), StringComparison.Ordinal))
                    |> List.distinct

                match candidates with
                | [ longhand ] -> Some (camel (stripOwner longhand))
                | _ -> Some (camel (stripOwner referenceName))

            let rec walk (node: GrammarNode) : Shape =
                let empty kind =
                    {
                        Kind = kind
                        Role = None
                        Category = None
                        Target = None
                        Resolution = None
                        Children = []
                        Repeat = None
                        Range = None
                        Optional = false
                        AtLeastOne = false
                        Original = node
                    }

                let referenceShape reference =
                    let bound =
                        Map.tryFind (feature.Id, node.Span) references
                        |> Option.defaultValue []
                        |> List.filter (fun item -> item.Reference = reference)

                    let target =
                        match bound with
                        | [ bound ] -> Some bound.Target
                        | [] ->
                            need node "Reference has no exact owner-and-span resolution."
                            None
                        | _ ->
                            need node "Reference has multiple owner-and-span resolutions."
                            None

                    match target with
                    | Some ReferenceTarget.Missing -> need node "Reference is unresolved."
                    | Some (ReferenceTarget.Ambiguous _) -> need node "Reference has ambiguous definitions."
                    | Some (ReferenceTarget.Definition id) when not (current id) -> need node "Reference resolves to an obsolete definition."
                    | _ -> ()

                    { empty "reference" with
                        Role = roleName reference
                        Category = Some (nameOf reference.Name)
                        Target =
                            match target with
                            | Some (ReferenceTarget.Definition id) -> Some id
                            | _ -> None
                        Resolution = target
                        Range = reference.Range
                    }

                let composite kind atLeastOne optional nodes =
                    let children =
                        nodes
                        |> List.map walk
                        |> List.map (fun child ->
                            if optional && child.Kind <> "syntax" then
                                { child with Optional = true }
                            else
                                child)

                    if
                        kind = "sequence"
                        || kind = "allOf"
                        || kind = "anyOf"
                    then
                        let slots =
                            children
                            |> List.filter (fun child -> child.Kind <> "syntax")

                        for slot in slots do
                            if slot.Role.IsNone && slot.Kind <> "keyword" then
                                need slot.Original "Anonymous constructor slot requires a semantic naming policy."

                        slots
                        |> List.choose (fun child -> child.Role |> Option.map (fun role -> role, child))
                        |> List.groupBy fst
                        |> List.filter (fun (_, slots) -> slots.Length > 1)
                        |> List.iter (fun (role, slots) ->
                            for _, slot in slots do
                                need
                                    slot.Original
                                    ("Repeated role '"
                                     + role
                                     + "' requires a semantic naming policy."))

                    { empty kind with
                        Children = children
                        AtLeastOne = atLeastOne
                    }

                match node.Expression with
                | GrammarExpression.Literal literal ->
                    let keyword = Regex.IsMatch (literal, "[A-Za-z0-9_]")

                    { empty (if keyword then "keyword" else "syntax") with
                        Role = if keyword then Some (camel literal) else None
                        Category = Some literal
                    }
                | GrammarExpression.Reference reference -> referenceShape reference
                | GrammarExpression.TypeApplication (name, argument) ->
                    let reference =
                        {
                            Kind = ReferenceKind.Type
                            Name = name
                            Range = None
                        }

                    { referenceShape reference with
                        Kind = "typeApplication"
                        Children = [ walk argument ]
                    }
                | GrammarExpression.Sequence nodes -> composite "sequence" false false nodes
                | GrammarExpression.AllOf nodes -> composite "allOf" false false nodes
                | GrammarExpression.AnyOf nodes -> composite "anyOf" true true nodes
                | GrammarExpression.Choice nodes -> composite "choice" false false nodes
                | GrammarExpression.Group child ->
                    let inner = walk child

                    { empty "group" with
                        Children = [ inner ]
                        Role = inner.Role
                        Category = inner.Category
                    }
                | GrammarExpression.Function (name, arguments) ->
                    { empty "function" with
                        Role = Some (camel name)
                        Category = Some name
                        Children = arguments |> Option.map walk |> Option.toList
                    }
                | GrammarExpression.Block (opening, contents) ->
                    { empty "block" with
                        Category = Some opening
                        Children = contents |> Option.map walk |> Option.toList
                    }
                | GrammarExpression.Repeat (child, repeat) ->
                    let inner = walk child

                    { empty "repeat" with
                        Role = inner.Role
                        Category = inner.Category
                        Children = [ inner ]
                        Repeat = Some repeat
                        Optional = repeat.Minimum = 0
                        AtLeastOne = repeat.Minimum > 0
                    }
                | GrammarExpression.Required child ->
                    let inner = walk child

                    { empty "required" with
                        Role = inner.Role
                        Category = inner.Category
                        Children = [ inner ]
                        AtLeastOne = true
                    }

            let namedScopes =
                feature.Id.Scopes
                |> List.filter (fun scope -> not (scope.StartsWith ("#", StringComparison.Ordinal)))
                |> List.distinct

            if namedScopes.Length > 1 then
                needs.Add
                    {
                        Span = { Offset = 0; Length = source.Length }
                        Syntax = source
                        Reason = "Definition has multiple semantic owners; canonical owner and aliases require policy."
                    }

            let grammar =
                match feature.Grammar with
                | GrammarStatus.Parsed (_, tree) -> Some (walk tree)
                | GrammarStatus.Missing ->
                    needs.Add
                        {
                            Span = { Offset = 0; Length = 0 }
                            Syntax = ""
                            Reason = "Grammar is missing."
                        }

                    None
                | GrammarStatus.Rejected (_, diagnostic) ->
                    needs.Add
                        {
                            Span =
                                {
                                    Offset = diagnostic.Offset
                                    Length = 0
                                }
                            Syntax = source
                            Reason = "Grammar was rejected: " + diagnostic.Message
                        }

                    None

            {
                Id = feature.Id
                Path = path feature.Id
                DeclarationPath =
                    if feature.Id.Kind = FeatureKind.Property then
                        Some [ "Css"; camel feature.Id.Name ]
                    else
                        None
                Name = camel (nameOf feature.Id.Name)
                Category = string feature.Id.Kind
                Grammar = grammar
                NeedsPolicy = needs |> Seq.distinct |> Seq.sort |> Seq.toList
            }

        let projected = features |> List.map shapeFor

        let collisions =
            projected
            |> List.groupBy (fun feature -> feature.Path)
            |> List.filter (fun (_, group) -> group.Length > 1)
            |> Map.ofList

        projected
        |> List.map (fun feature ->
            match Map.tryFind feature.Path collisions with
            | None -> feature
            | Some peers ->
                let source =
                    feature.Grammar
                    |> Option.map (fun grammar -> grammar.Original.Span)
                    |> Option.defaultValue { Offset = 0; Length = 0 }

                let reason =
                    "Public owner path is shared by multiple definitions; equivalence or co-location requires policy: "
                    + (peers
                       |> List.map (fun peer -> string peer.Id.Kind + " " + peer.Id.Name)
                       |> String.concat ", ")

                let syntax =
                    features
                    |> List.find (fun original -> original.Id = feature.Id)
                    |> fun original ->
                        match original.Grammar with
                        | GrammarStatus.Parsed (syntax, _)
                        | GrammarStatus.Rejected (syntax, _) -> syntax
                        | GrammarStatus.Missing -> ""

                { feature with
                    NeedsPolicy =
                        feature.NeedsPolicy
                        @ [
                            {
                                Span = source
                                Syntax = syntax
                                Reason = reason
                            }
                        ]
                })

    let preview (features: FeatureShape list) =
        features
        |> List.map (fun feature ->
            let declaration =
                feature.DeclarationPath
                |> Option.map (String.concat ".")
                |> Option.map (fun path -> path + " -> ")
                |> Option.defaultValue ""

            declaration
            + String.concat "." feature.Path
            + " ["
            + feature.Category
            + "; policy="
            + string feature.NeedsPolicy.Length
            + "]")
        |> String.concat "\n"
