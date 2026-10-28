namespace Ranvier.Css.Spec

open System
open System.Collections.Generic

/// <summary>The binding of a symbolic grammar reference within its owner's scope.</summary>
[<RequireQualifiedAccess>]
type ReferenceTarget =
    | Definition of FeatureId
    | Primitive of string
    /// <summary>A production bound within a generic grammar template.</summary>
    | Parameter of string
    | Missing
    | Ambiguous of FeatureId list

/// <summary>A grammar reference retains its source location and resolution outcome.</summary>
type ReferenceUse =
    {
        Owner: FeatureId
        Span: SourceSpan
        Reference: GrammarReference
        Target: ReferenceTarget
    }

/// <summary>References and dependency edges include every parsed definition; cycles are sorted strongly connected components.</summary>
type ResolutionResult =
    {
        References: ReferenceUse list
        Dependencies: Map<FeatureId, FeatureId list>
        Cycles: FeatureId list list
    }

/// <summary>Resolves references by kind, descriptor owner and CSS definition scope.</summary>
module Resolution =
    let private typeName (name: string) =
        let name = name.Trim ()

        if
            name.StartsWith ("<", StringComparison.Ordinal)
            && name.EndsWith (">", StringComparison.Ordinal)
        then
            (name.Substring (1, name.Length - 2)).Trim()
        else
            name

    let private functionName name =
        let name = typeName name

        if name.EndsWith (")", StringComparison.Ordinal) then
            let opening = name.LastIndexOf '('

            if
                opening >= 0
                && String.IsNullOrWhiteSpace (name.Substring (opening + 1, name.Length - opening - 2))
            then
                (name.Substring (0, opening)).TrimEnd()
            else
                name
        else
            name

    let private scopeName (name: string) =
        let name = name.Trim ()

        if
            name.StartsWith ("<", StringComparison.Ordinal)
            && name.EndsWith (">", StringComparison.Ordinal)
        then
            "<" + typeName name + ">"
        elif name.EndsWith (")", StringComparison.Ordinal) then
            functionName name + "()"
        else
            name

    // CSS Values numeric dimensions and CSS Syntax lexical value categories are terminal fallbacks.
    let private primitives =
        set
            [
                "number"
                "integer"
                "percentage"
                "dimension"
                "length"
                "angle"
                "time"
                "frequency"
                "resolution"
                "flex"
                "string"
                "ident"
                "custom-ident"
                "dashed-ident"
                "url"
                "unicode-range"
            ]

    let private components (dependencies: Map<FeatureId, FeatureId list>) =
        let indices = Dictionary<FeatureId, int>()
        let low = Dictionary<FeatureId, int>()
        let active = HashSet<FeatureId>()
        let stack = Stack<FeatureId>()
        let groups = ResizeArray<FeatureId list>()
        let mutable next = 0

        let rec visit id =
            indices[id] <- next
            low[id] <- next
            next <- next + 1
            stack.Push id
            active.Add id |> ignore

            for target in dependencies[id] do
                if not (indices.ContainsKey target) then
                    visit target
                    low[id] <- min low[id] low[target]
                elif active.Contains target then
                    low[id] <- min low[id] indices[target]

            if low[id] = indices[id] then
                let members = ResizeArray<FeatureId>()
                let mutable finished = false

                while not finished do
                    let memberId = stack.Pop ()
                    active.Remove memberId |> ignore
                    members.Add memberId
                    finished <- memberId = id

                let members = members |> Seq.sort |> List.ofSeq

                if
                    members.Length > 1
                    || List.contains id dependencies[id]
                then
                    groups.Add members

        for KeyValue (id, _) in dependencies do
            if not (indices.ContainsKey id) then
                visit id

        groups |> Seq.sort |> List.ofSeq

    /// <summary>Returns deterministic bindings, direct dependency edges and recursive components.</summary>
    /// <remarks>Definitions with prose or rejected grammar remain valid targets. Unknown names remain missing.
    /// Ambiguous bindings retain every candidate edge. Primitive fallbacks cover explicit CSS numeric and lexical categories.
    /// Aliases with matching source URLs and grammar share a representative; source spans do not affect equivalence.</remarks>
    let resolve (catalogue: Catalogue) : ResolutionResult =
        let features =
            catalogue.Features
            |> List.sortBy (fun feature -> feature.Id)

        let ids =
            features
            |> List.map (fun feature -> feature.Id)
            |> List.distinct

        let key kind name =
            match kind with
            | FeatureKind.Type -> typeName name
            | FeatureKind.Function -> functionName name
            | _ -> name.Trim ()

        let index =
            ids
            |> List.groupBy (fun id -> id.Kind, key id.Kind id.Name)
            |> Map.ofList

        let specification (href: string) =
            match Uri.TryCreate (href, UriKind.Absolute) with
            | true, uri when uri.Scheme = "https" || uri.Scheme = "http" -> Some ((uri.GetLeftPart UriPartial.Path).TrimEnd '/')
            | _ -> None

        let specifications =
            features
            |> List.map (fun feature -> feature.Id, specification feature.Href)
            |> Map.ofList

        let definitions =
            features
            |> List.map (fun feature -> feature.Id, feature)
            |> Map.ofList

        let rec semanticNode (node: GrammarNode) =
            let recurse = semanticNode

            let expression =
                match node.Expression with
                | GrammarExpression.TypeApplication (name, argument) -> GrammarExpression.TypeApplication (name, recurse argument)
                | GrammarExpression.Sequence nodes -> GrammarExpression.Sequence (List.map recurse nodes)
                | GrammarExpression.AllOf nodes -> GrammarExpression.AllOf (List.map recurse nodes)
                | GrammarExpression.AnyOf nodes -> GrammarExpression.AnyOf (List.map recurse nodes)
                | GrammarExpression.Choice nodes -> GrammarExpression.Choice (List.map recurse nodes)
                | GrammarExpression.Group inner -> GrammarExpression.Group (recurse inner)
                | GrammarExpression.Repeat (inner, repeat) -> GrammarExpression.Repeat (recurse inner, repeat)
                | GrammarExpression.Required inner -> GrammarExpression.Required (recurse inner)
                | GrammarExpression.Function (name, contents) -> GrammarExpression.Function (name, Option.map recurse contents)
                | GrammarExpression.Block (opening, contents) -> GrammarExpression.Block (opening, Option.map recurse contents)
                | expression -> expression

            {
                Span = { Offset = 0; Length = 0 }
                Expression = expression
            }

        let semantics feature =
            let grammar =
                match feature.Grammar with
                | GrammarStatus.Parsed (_, tree) -> GrammarStatus.Parsed ("", semanticNode tree)
                | grammar -> grammar

            feature.Href, grammar

        let aliases =
            features
            |> List.groupBy (fun feature -> feature.Id.Kind, key feature.Id.Kind feature.Id.Name, feature.Id.Owner, semantics feature)
            |> List.collect (fun (_, group) ->
                let scopes feature =
                    feature.Id.Scopes |> Set.ofList

                let union = group |> List.map scopes |> Set.unionMany

                let covering =
                    group
                    |> List.filter (fun feature -> scopes feature = union)

                let preferred =
                    (if covering.IsEmpty then group else covering)
                    |> List.sortBy (fun feature -> Map.containsKey "sourcePath" feature.Metadata, feature.Id)
                    |> List.head

                group
                |> List.map (fun feature -> feature.Id, preferred.Id))
            |> Map.ofList

        let references = ResizeArray<ReferenceUse>()

        let candidates kind name =
            Map.tryFind (kind, key kind name) index
            |> Option.defaultValue []

        let bind (owner: FeatureId) (reference: GrammarReference) =
            let ownScope =
                match owner.Kind with
                | FeatureKind.Type -> "<" + typeName owner.Name + ">"
                | FeatureKind.Function -> functionName owner.Name + "()"
                | _ -> owner.Name.Trim ()

            let inherited =
                owner.Scopes @ (owner.Owner |> Option.toList)
                |> List.map scopeName
                |> Set.ofList

            let rank (id: FeatureId) =
                let scopes = id.Scopes |> List.map scopeName |> Set.ofList

                if scopes.IsEmpty then
                    Some (0, 0)
                elif Set.contains ownScope scopes then
                    Some (2, Set.intersect inherited scopes |> Set.count)
                elif not (Set.intersect inherited scopes).IsEmpty then
                    Some (1, Set.intersect inherited scopes |> Set.count)
                else
                    None

            let select ids =
                let forwarding =
                    match definitions[owner].Grammar with
                    | GrammarStatus.Parsed (_,
                                            {
                                                Expression = GrammarExpression.Reference inner
                                            }) ->
                        not owner.Scopes.IsEmpty
                        && inner.Kind = reference.Kind
                        && key owner.Kind inner.Name = key owner.Kind owner.Name
                        && inner.Name = reference.Name
                    | _ -> false

                let ids =
                    if forwarding then
                        let outer =
                            ids
                            |> List.filter (fun id -> aliases[id] <> aliases[owner])

                        let ownerScopes =
                            owner.Scopes
                            |> List.filter (fun scope -> not (scope.StartsWith ("#", StringComparison.Ordinal)))
                            |> List.map scopeName
                            |> Set.ofList

                        let enclosing =
                            outer
                            |> List.filter (fun id ->
                                let scopes =
                                    id.Scopes
                                    |> List.filter (fun scope -> not (scope.StartsWith ("#", StringComparison.Ordinal)))
                                    |> List.map scopeName
                                    |> Set.ofList

                                Set.isProperSubset scopes ownerScopes)

                        if not enclosing.IsEmpty then enclosing
                        elif outer.IsEmpty then ids
                        else outer
                    else
                        ids

                let eligible =
                    ids
                    |> List.choose (fun id -> rank id |> Option.map (fun score -> score, id))

                match eligible with
                | [] -> ids |> List.sort
                | _ ->
                    let best = eligible |> List.map fst |> List.max

                    eligible
                    |> List.choose (fun (score, id) -> if score = best then Some id else None)
                    |> List.sort

            let targets =
                match reference.Kind with
                | ReferenceKind.Type ->
                    candidates FeatureKind.Type reference.Name
                    |> select
                | ReferenceKind.Function ->
                    candidates FeatureKind.Function reference.Name
                    |> select
                | ReferenceKind.Property ->
                    let descriptors =
                        candidates FeatureKind.Descriptor reference.Name
                        |> List.filter (fun id -> owner.Owner.IsSome && id.Owner = owner.Owner)
                        |> select

                    if descriptors.IsEmpty then
                        candidates FeatureKind.Property reference.Name
                        |> select
                    else
                        descriptors

            let targets =
                let ownerSpec = specifications[owner]

                let sameSpec =
                    if ownerSpec.IsNone then
                        []
                    else
                        targets
                        |> List.filter (fun id -> specifications[id] = ownerSpec)

                if sameSpec.IsEmpty then targets else sameSpec

            let targets =
                targets
                |> List.map (fun id -> aliases[id])
                |> List.distinct
                |> List.sort

            match targets with
            | _ when
                owner.Kind = FeatureKind.Type
                && typeName owner.Name = "boolean-expr"
                && reference.Kind = ReferenceKind.Type
                && typeName reference.Name = "boolean-expr-group"
                ->
                ReferenceTarget.Parameter "boolean-expr-group"
            | [ id ] -> ReferenceTarget.Definition id
            | _ :: _ -> ReferenceTarget.Ambiguous targets
            | [] when
                reference.Kind = ReferenceKind.Type
                && Set.contains (typeName reference.Name) primitives
                ->
                ReferenceTarget.Primitive (typeName reference.Name)
            | [] -> ReferenceTarget.Missing

        let add owner span reference =
            references.Add
                {
                    Owner = owner
                    Span = span
                    Reference = reference
                    Target = bind owner reference
                }

        let rec walk owner (node: GrammarNode) =
            match node.Expression with
            | GrammarExpression.Reference reference -> add owner node.Span reference
            | GrammarExpression.TypeApplication (name, argument) ->
                add
                    owner
                    node.Span
                    {
                        Kind = ReferenceKind.Type
                        Name = name
                        Range = None
                    }

                walk owner argument
            | GrammarExpression.Sequence nodes
            | GrammarExpression.AllOf nodes
            | GrammarExpression.AnyOf nodes
            | GrammarExpression.Choice nodes -> nodes |> List.iter (walk owner)
            | GrammarExpression.Group inner
            | GrammarExpression.Repeat (inner, _)
            | GrammarExpression.Required inner -> walk owner inner
            | GrammarExpression.Function (_, contents)
            | GrammarExpression.Block (_, contents) -> contents |> Option.iter (walk owner)
            | GrammarExpression.Literal _ -> ()

        for feature in features do
            match feature.Grammar with
            | GrammarStatus.Parsed (_, tree) -> walk feature.Id tree
            | _ -> ()

        let references = List.ofSeq references

        let edges =
            references
            |> List.collect (fun reference ->
                match reference.Target with
                | ReferenceTarget.Definition target -> [ reference.Owner, target ]
                | ReferenceTarget.Ambiguous targets ->
                    targets
                    |> List.map (fun target -> reference.Owner, target)
                | _ -> [])
            |> List.groupBy fst
            |> List.map (fun (owner, edges) ->
                owner,
                edges
                |> List.map snd
                |> List.distinct
                |> List.sort)
            |> Map.ofList

        let dependencies =
            ids
            |> List.map (fun id -> id, Map.tryFind id edges |> Option.defaultValue [])
            |> Map.ofList

        {
            References = references
            Dependencies = dependencies
            Cycles = components dependencies
        }
