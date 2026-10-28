namespace Ranvier.Css.Spec

open System
open System.Collections.Generic
open System.Globalization
open System.Text

type internal GraphNode =
    {
        Id: int
        Kind: int
        Text: string
        Target: int
        Children: int array
        Separators: string array
        Minimum: int
        Maximum: int
        CommaSeparated: bool
        Lower: (float * string) option
        Upper: (float * string) option
    }

type internal GraphIssue =
    {
        Owner: FeatureId
        Span: SourceSpan
        Reference: GrammarReference
        Target: ReferenceTarget
        Message: string
    }

type internal GrammarGraph =
    {
        Nodes: GraphNode list
        NodesById: Map<int, GraphNode>
        LiteralNodes: Map<string, Set<int>>
        Nullable: Set<int>
        Roots: Map<FeatureId, int>
        Encoded: string
        Issues: GraphIssue list
    }

module internal GrammarGraphGeneration =
    let private nullableNodes (nodes: GraphNode list) =
        let mutable nullable = Set.empty
        let mutable changed = true

        while changed do
            changed <- false

            for node in nodes do
                let childNullable child =
                    Set.contains child nullable

                let empty =
                    match node.Kind with
                    | 1 -> node.Text = ""
                    | 2 -> childNullable node.Target
                    | 3 -> Array.exists childNullable node.Children
                    | 4
                    | 5 -> Array.forall childNullable node.Children
                    | 7 ->
                        node.Minimum = 0
                        || Array.forall childNullable node.Children
                    | 11 -> Array.forall childNullable node.Children
                    | _ -> false

                if empty && not (Set.contains node.Id nullable) then
                    nullable <- Set.add node.Id nullable
                    changed <- true

        nullable

    let private blank id kind text =
        {
            Id = id
            Kind = kind
            Text = text
            Target = -1
            Children = [||]
            Separators = [||]
            Minimum = 0
            Maximum = -1
            CommaSeparated = false
            Lower = None
            Upper = None
        }

    let private encode (nodes: GraphNode list) =
        let output = StringBuilder ()

        let field (builder: StringBuilder) (value: string) =
            builder.Append(value.Length.ToString (CultureInfo.InvariantCulture)).Append(':').Append(value)
            |> ignore

        let number value =
            (value: float).ToString("R", CultureInfo.InvariantCulture)

        field output (string nodes.Length)

        for node in nodes do
            let separators = StringBuilder ()

            for separator in node.Separators do
                field separators separator

            [
                string node.Id
                string node.Kind
                node.Text
                string node.Target
                node.Children
                |> Array.map string
                |> String.concat ","
                separators.ToString ()
                string node.Minimum
                string node.Maximum
                if node.CommaSeparated then "1" else "0"
                node.Lower
                |> Option.map (fst >> number)
                |> Option.defaultValue ""
                node.Lower
                |> Option.map snd
                |> Option.defaultValue ""
                node.Upper
                |> Option.map (fst >> number)
                |> Option.defaultValue ""
                node.Upper
                |> Option.map snd
                |> Option.defaultValue ""
                ""
            ]
            |> List.iter (field output)

        output.ToString ()

    let build (catalogue: Catalogue) (resolution: ResolutionResult) : GrammarGraph =
        let nodes = ResizeArray<GraphNode>()
        let pending = ResizeArray<int * FeatureId * SourceSpan * GrammarReference>()
        let issues = ResizeArray<GraphIssue>()

        let allocate kind text =
            let id = nodes.Count
            nodes.Add (blank id kind text)
            id

        let rec ungroup (node: GrammarNode) =
            match node.Expression with
            | GrammarExpression.Group child -> ungroup child
            | _ -> node

        let bound =
            function
            | RangeBound.Finite (magnitude, unitName) -> Some (float magnitude, unitName)
            | _ -> None

        let rec visit owner (input: GrammarNode) =
            let node = ungroup input
            let id = allocate 0 ""
            let mutable value = nodes[id]

            let children items =
                items |> List.map (visit owner) |> List.toArray

            let reference kind name range =
                let reference =
                    {
                        Kind = kind
                        Name = name
                        Range = range
                    }

                pending.Add (id, owner, node.Span, reference)
                reference

            match node.Expression with
            | GrammarExpression.Literal token -> value <- { value with Kind = 1; Text = token }
            | GrammarExpression.Reference target ->
                reference target.Kind target.Name target.Range
                |> ignore

                value <-
                    { value with
                        Kind = 2
                        Text = target.Name
                        Lower =
                            target.Range
                            |> Option.bind (fun range -> bound range.Minimum)
                        Upper =
                            target.Range
                            |> Option.bind (fun range -> bound range.Maximum)
                    }
            | GrammarExpression.Choice items ->
                value <-
                    { value with
                        Kind = 3
                        Children = children items
                    }
            | GrammarExpression.Sequence items ->
                let separators =
                    items
                    |> List.mapi (fun index child ->
                        if index = 0 then
                            ""
                        else
                            let previous = ungroup items[index - 1]
                            let current = ungroup child

                            if previous.Span.Offset + previous.Span.Length = current.Span.Offset then
                                ""
                            else
                                " ")
                    |> List.toArray

                value <-
                    { value with
                        Kind = 4
                        Children = children items
                        Separators = separators
                    }
            | GrammarExpression.AllOf items ->
                value <-
                    { value with
                        Kind = 5
                        Children = children items
                    }
            | GrammarExpression.AnyOf items ->
                value <-
                    { value with
                        Kind = 6
                        Children = children items
                    }
            | GrammarExpression.Repeat (child, repetition) ->
                value <-
                    { value with
                        Kind = 7
                        Children = children [ child ]
                        Minimum = repetition.Minimum
                        Maximum = repetition.Maximum |> Option.defaultValue -1
                        CommaSeparated = repetition.CommaSeparated
                    }
            | GrammarExpression.Required child ->
                value <-
                    { value with
                        Kind = 8
                        Children = children [ child ]
                    }
            | GrammarExpression.Function (name, argument) ->
                value <-
                    { value with
                        Kind = 9
                        Text = name
                        Children = children (Option.toList argument)
                    }
            | GrammarExpression.Block (opening, contents) ->
                value <-
                    { value with
                        Kind = 10
                        Text = opening
                        Children = children (Option.toList contents)
                    }
            | GrammarExpression.TypeApplication (name, argument) ->
                reference ReferenceKind.Type name None |> ignore

                value <-
                    { value with
                        Kind = if name = "boolean-expr" then 11 else 12
                        Text = name
                        Children = children [ argument ]
                    }
            | GrammarExpression.Group _ -> failwith "Groups must be removed before graph allocation."

            nodes[id] <- value
            id

        let roots =
            catalogue.Features
            |> List.sortBy _.Id
            |> List.map (fun feature ->
                let id =
                    match feature.Grammar with
                    | GrammarStatus.Parsed (_, node) -> visit feature.Id node
                    | _ -> allocate 0 feature.Id.Name

                feature.Id, id)
            |> Map.ofList

        let bindings =
            resolution.References
            |> List.map (fun useSite ->
                (useSite.Owner, useSite.Span.Offset, useSite.Span.Length, useSite.Reference.Kind, useSite.Reference.Name), useSite.Target)
            |> Map.ofList

        let terminals = Dictionary<ReferenceTarget, int>()

        let terminal target name =
            match terminals.TryGetValue target with
            | true, id -> id
            | _ ->
                let id = allocate 0 name
                terminals.Add (target, id)
                id

        for id, owner, span, reference in pending do
            let key = owner, span.Offset, span.Length, reference.Kind, reference.Name

            let target =
                Map.tryFind key bindings
                |> Option.defaultValue ReferenceTarget.Missing

            let opaque message =
                issues.Add
                    {
                        Owner = owner
                        Span = span
                        Reference = reference
                        Target = target
                        Message = message
                    }

                allocate 0 reference.Name

            let destination =
                match target with
                | ReferenceTarget.Definition feature ->
                    match Map.tryFind feature roots with
                    | Some root ->
                        let node = nodes[root]

                        if
                            reference.Kind = ReferenceKind.Property
                            && node.Kind = 7
                            && node.CommaSeparated
                        then
                            node.Children[0]
                        else
                            root
                    | None -> opaque "Resolved definition is absent from the catalogue."
                | ReferenceTarget.Primitive name -> terminal target name
                | ReferenceTarget.Parameter name -> terminal target name
                | ReferenceTarget.Missing -> opaque "Missing grammar reference."
                | ReferenceTarget.Ambiguous _ -> opaque "Ambiguous grammar reference."

            nodes[id] <- { nodes[id] with Target = destination }

        let nodes = List.ofSeq nodes

        {
            Nodes = nodes
            NodesById =
                nodes
                |> List.map (fun node -> node.Id, node)
                |> Map.ofList
            LiteralNodes =
                nodes
                |> List.filter (fun node -> node.Kind = 1)
                |> List.groupBy _.Text
                |> List.map (fun (text, literals) -> text, literals |> List.map _.Id |> Set.ofList)
                |> Map.ofList
            Nullable = nullableNodes nodes
            Roots = roots
            Encoded = encode nodes
            Issues = List.ofSeq issues
        }

    let acceptedNodes (graph: GrammarGraph) (targetId: int) : Set<int> =
        let nodes = graph.NodesById

        if not (Map.containsKey targetId nodes) then
            invalidArg "targetId" "Unknown grammar node."

        let nullable = graph.Nullable

        let rec collect visited id =
            if Set.contains id visited then
                visited
            else
                let visited = Set.add id visited

                match Map.tryFind id nodes with
                | None -> visited
                | Some node ->
                    let visited =
                        if node.Kind = 1 then
                            Set.union visited graph.LiteralNodes[node.Text]
                        else
                            visited

                    let next =
                        match node.Kind with
                        | 2 -> [| node.Target |]
                        | 3
                        | 6
                        | 8
                        | 11 -> node.Children
                        | 7 when
                            node.Minimum <= 1
                            && (node.Maximum = -1 || node.Maximum >= 1)
                            ->
                            node.Children
                        | 4
                        | 5 ->
                            node.Children
                            |> Array.indexed
                            |> Array.choose (fun (index, child) ->
                                if
                                    node.Children
                                    |> Array.indexed
                                    |> Array.forall (fun (other, sibling) -> other = index || Set.contains sibling nullable)
                                then
                                    Some child
                                else
                                    None)
                        | _ -> [||]

                    next |> Array.fold collect visited

        collect Set.empty targetId
