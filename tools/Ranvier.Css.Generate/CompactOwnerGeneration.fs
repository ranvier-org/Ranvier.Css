namespace Ranvier.Css.Spec

open System
open System.Collections.Generic
open System.Text.RegularExpressions

type internal CompactOwner =
    {
        NodeId: int
        Path: string list
        Member: string
        Roles: (int * string) list
    }

type internal CompactOwnerIssue =
    {
        NodeId: int
        Path: string list
        Member: string
        Message: string
    }

module internal CompactOwnerGeneration =
    let private pascal (value: string) =
        let value =
            match value with
            | "||" -> "ColumnCombinator"
            | ">" -> "ChildCombinator"
            | "+" -> "NextSiblingCombinator"
            | "~" -> "SubsequentSiblingCombinator"
            | "*" -> "UniversalSelector"
            | "|" -> "NamespaceSeparator"
            | "&" -> "NestingSelector"
            | _ -> value

        let value =
            if value.EndsWith ("-token", StringComparison.Ordinal) then
                [
                    "{", "LeftCurlyBracket"
                    "}", "RightCurlyBracket"
                    "[", "LeftSquareBracket"
                    "]", "RightSquareBracket"
                    "(", "LeftParenthesis"
                    ")", "RightParenthesis"
                    ":", "Colon"
                    ";", "Semicolon"
                    ",", "Comma"
                ]
                |> List.fold (fun (text: string) (symbol, name) -> text.Replace (symbol, name)) value
            else
                value

        Regex.Matches (value, "[A-Za-z0-9]+")
        |> Seq.cast<Match>
        |> Seq.map (fun part ->
            string (Char.ToUpperInvariant part.Value[0])
            + part.Value.Substring 1)
        |> String.concat ""

    let private camel value =
        let name = pascal value

        if name = "" then
            name
        else
            string (Char.ToLowerInvariant name[0])
            + name.Substring 1

    let private numericNames =
        Map.ofList
            [
                "0", "zero"
                "1", "one"
                "2", "two"
                "3", "three"
                "4", "four"
                "5", "five"
                "6", "six"
                "7", "seven"
                "8", "eight"
                "9", "nine"
                "10", "ten"
                "100", "hundred"
            ]

    let private literalMember (token: string) =
        let unsigned, negative =
            if token.StartsWith ("-", StringComparison.Ordinal) then
                token.Substring 1, true
            else
                token, false

        match Map.tryFind unsigned numericNames with
        | Some name -> Some (if negative then "negative" + pascal name else name)
        | None when Regex.IsMatch (token, "^-?[0-9]+(?:\\.[0-9]+)?$") ->
            let name = "number" + pascal unsigned
            Some (if negative then "negative" + pascal name else name)
        | None when Regex.IsMatch (token, "[A-Za-z_]") -> Some (camel token)
        | _ -> None

    let rec private ungroup (shape: Shape) =
        if shape.Kind = "group" then
            ungroup shape.Children.Head
        else
            shape

    let private fallback (node: GraphNode) =
        match node.Kind with
        | 0 -> "Value"
        | 1 ->
            literalMember node.Text
            |> Option.map pascal
            |> Option.defaultValue "Syntax"
        | 2
        | 11
        | 12 -> pascal node.Text
        | 3 -> "Alternatives"
        | 4 -> "Sequence"
        | 5 -> "Components"
        | 6 -> "Options"
        | 7 -> "Items"
        | 8 -> "Nonempty"
        | 9 -> pascal node.Text
        | 10 -> "Arguments"
        | _ -> "Value"

    let planWithIssues (catalogue: Catalogue) (resolution: ResolutionResult) (graph: GrammarGraph) : CompactOwner list * CompactOwnerIssue list =
        let projection = PublicApiProjection.project catalogue resolution
        let owners = ResizeArray<CompactOwner>()
        let issues = ResizeArray<CompactOwnerIssue>()
        let assigned = HashSet<int>()
        let occupied = Dictionary<string list * string, CompactOwner>()
        let reservedRoots = Dictionary<int, string list>()
        let contexts = Dictionary<int, string list>()

        let issue id path memberName message =
            issues.Add
                {
                    NodeId = id
                    Path = path
                    Member = memberName
                    Message = message
                }

        let rec semanticSignature id =
            let node = graph.NodesById[id]

            match node.Kind with
            | 1 ->
                match node.Text with
                | "+" -> "Plus"
                | "-" -> "Minus"
                | "/" -> "Slash"
                | "," -> "Comma"
                | "*" -> "Star"
                | _ ->
                    literalMember node.Text
                    |> Option.map pascal
                    |> Option.defaultValue ""
            | 0
            | 2
            | 9
            | 11
            | 12 when node.Text <> "" -> pascal node.Text
            | _ ->
                node.Children
                |> Array.map semanticSignature
                |> String.concat ""

        let add (node: GraphNode) path memberName roles alternatives =
            let signature = semanticSignature node.Id

            let candidates =
                path
                :: (alternatives
                    @ [
                        if signature <> "" then
                            path @ [ signature ]
                        if signature <> "" then
                            path @ [ fallback node + signature ]
                    ])
                |> List.distinct

            let free =
                candidates
                |> List.tryFind (fun candidate ->
                    match occupied.TryGetValue ((candidate, memberName)) with
                    | false, _ -> true
                    | true, existing ->
                        let previous = graph.NodesById[existing.NodeId]

                        node.Kind = 1
                        && previous.Kind = 1
                        && node.Text = previous.Text)

            let chosen =
                match free with
                | Some chosen -> chosen
                | None ->
                    issue
                        node.Id
                        path
                        memberName
                        "Public constructor path conflicts after semantic owner alternatives; explicit naming or semantic alias policy is required."

                    path

            let owner =
                {
                    NodeId = node.Id
                    Path = chosen
                    Member = memberName
                    Roles = roles
                }

            owners.Add owner
            assigned.Add node.Id |> ignore

            if not (occupied.ContainsKey (chosen, memberName)) then
                occupied.Add ((chosen, memberName), owner)

            chosen

        let roleNames (node: GraphNode) (children: Shape list) =
            let occupiedRoles = Dictionary<string, int>()

            node.Children
            |> Array.mapi (fun index childId ->
                let child = graph.NodesById[childId]
                let shape = List.tryItem index children |> Option.map ungroup

                if
                    child.Kind = 1
                    && (literalMember child.Text).IsNone
                then
                    None
                else
                    let role =
                        shape
                        |> Option.bind (fun shape -> shape.Role)
                        |> Option.bind (fun role -> literalMember role)
                        |> Option.defaultWith (fun () -> camel (fallback child))

                    let role = if role = "" then "value" else role

                    let count =
                        match occupiedRoles.TryGetValue role with
                        | true, count -> count + 1
                        | _ -> 1

                    occupiedRoles[role] <- count

                    if count > 1 then
                        issue
                            node.Id
                            []
                            role
                            "Repeated constructor slot has no distinct semantic role; the internal positional role requires explicit naming policy."

                    Some (index, if count = 1 then role else role + string count))
            |> Array.choose id
            |> List.ofArray

        let rootContext (feature: FeatureShape) =
            Map.tryFind feature.Id graph.Roots
            |> Option.map (fun root ->
                let path =
                    match feature.Path with
                    | "CssValue" :: path -> path
                    | path -> path

                let path =
                    if feature.Id.Name.EndsWith ("-token", StringComparison.Ordinal) then
                        (path |> List.take (path.Length - 1))
                        @ [ pascal feature.Id.Name ]
                    else
                        path

                root, path)

        for feature in projection do
            match rootContext feature with
            | None -> issue -1 feature.Path "create" "Projected definition has no grammar graph root."
            | Some (root, rootPath) ->
                let node = graph.NodesById[root]
                let shape = feature.Grammar |> Option.map ungroup

                let children =
                    shape
                    |> Option.map (fun shape -> shape.Children)
                    |> Option.defaultValue []

                let path =
                    if
                        node.Kind = 1
                        && (literalMember node.Text).IsSome
                        && feature.Id.Kind <> FeatureKind.Property
                        && rootPath.Length > 1
                    then
                        rootPath |> List.take (rootPath.Length - 1)
                    else
                        rootPath

                let memberName =
                    if node.Kind = 1 then
                        literalMember node.Text
                        |> Option.orElse (Some "value")
                    else
                        Some "create"

                match memberName with
                | None -> assigned.Add root |> ignore
                | Some memberName ->
                    let chosen =
                        add node path memberName (roleNames node children) [ path @ [ fallback node ]; path @ [ string feature.Id.Kind ] ]

                    reservedRoots[root] <- chosen
                    contexts[root] <- chosen

        let rec walk (feature: FeatureShape) (rootPath: string list) (parentPath: string list) isRoot id shape =
            if assigned.Contains id && not isRoot then
                ()
            else
                let node = graph.NodesById[id]
                let shape = shape |> Option.map ungroup

                let childShapes =
                    shape
                    |> Option.map (fun shape -> shape.Children)
                    |> Option.defaultValue []

                let isLiteral = node.Kind = 1

                let transparent =
                    not isRoot
                    && (node.Kind = 2 || node.Kind = 3 || node.Kind = 8)

                let memberName =
                    if transparent then None
                    elif isLiteral then literalMember node.Text
                    else Some "create"

                let semantic =
                    shape
                    |> Option.bind (fun shape -> shape.Role |> Option.orElse shape.Category)
                    |> Option.map pascal
                    |> Option.filter (fun name -> name <> "")
                    |> Option.defaultWith (fun () -> fallback node)

                let path =
                    if isRoot then
                        if
                            isLiteral
                            && feature.Id.Kind <> FeatureKind.Property
                        then
                            if rootPath.Length > 1 then
                                rootPath |> List.take (rootPath.Length - 1)
                            else
                                rootPath
                        else
                            rootPath
                    elif isLiteral || transparent then
                        parentPath
                    else
                        let segment =
                            if List.tryLast parentPath = Some semantic then
                                fallback node
                            else
                                semantic

                        parentPath @ [ segment ]

                let chosen =
                    match reservedRoots.TryGetValue id with
                    | true, chosen when isRoot -> chosen
                    | _ ->
                        match memberName with
                        | None ->
                            assigned.Add id |> ignore
                            path
                        | Some memberName ->
                            let featureKind =
                                match feature.Id.Kind with
                                | FeatureKind.Property -> "Property"
                                | FeatureKind.Type -> "Value"
                                | FeatureKind.Function -> "Function"
                                | FeatureKind.Descriptor -> "Descriptor"
                                | FeatureKind.AtRule -> "AtRule"
                                | FeatureKind.Selector -> "Selector"

                            let scope =
                                feature.Id.Scopes
                                |> List.filter (fun scope -> not (scope.StartsWith ("#", StringComparison.Ordinal)))
                                |> List.map pascal
                                |> List.filter (fun name -> name <> "")
                                |> List.distinct

                            let alternatives =
                                [
                                    path @ [ semantic ]
                                    path @ [ fallback node ]
                                    rootPath @ [ featureKind ]
                                    if not scope.IsEmpty then
                                        rootPath @ scope
                                ]

                            add node path memberName (roleNames node childShapes) alternatives

                contexts[id] <- chosen

                node.Children
                |> Array.iteri (fun index child -> walk feature rootPath chosen false child (List.tryItem index childShapes))

        for feature in projection do
            match rootContext feature with
            | Some (root, path) -> walk feature path path true root feature.Grammar
            | None -> ()

        let currentIds =
            projection
            |> List.map (fun feature -> feature.Id)
            |> Set.ofList

        let obsoleteNodes = HashSet<int>()

        let rec exclude id =
            if obsoleteNodes.Add id then
                graph.NodesById[id].Children |> Array.iter exclude

        for KeyValue (feature, root) in graph.Roots do
            if not (Set.contains feature currentIds) then
                exclude root

        for node in graph.Nodes do
            if
                not (assigned.Contains node.Id)
                && not (obsoleteNodes.Contains node.Id)
            then
                let category = pascal node.Text

                let referenceOwner =
                    graph.Nodes
                    |> List.filter (fun source -> source.Target = node.Id)
                    |> List.tryPick (fun source ->
                        match contexts.TryGetValue source.Id with
                        | true, path -> Some (path @ [ "Value" ])
                        | _ -> None)

                let path =
                    if referenceOwner.IsSome then
                        referenceOwner.Value
                    elif category = "" then
                        [ "Values" ]
                    elif
                        Set.contains
                            node.Text
                            (set
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
                                ])
                    then
                        [ category ]
                    else
                        [ "Values"; category ]

                add node path "create" [] [ path @ [ "Value" ] ]
                |> ignore

        owners
        |> Seq.sortBy (fun owner -> owner.NodeId)
        |> Seq.toList,
        issues |> Seq.distinct |> Seq.sort |> Seq.toList

    let plan catalogue resolution graph =
        let owners, issues = planWithIssues catalogue resolution graph

        let duplicate =
            owners
            |> List.groupBy (fun owner -> owner.Path, owner.Member)
            |> List.tryFind (fun (_, group) ->
                group.Length > 1
                && not (
                    group
                    |> List.forall (fun owner -> graph.NodesById[owner.NodeId].Kind = 1)
                    && (group
                        |> List.map (fun owner -> graph.NodesById[owner.NodeId].Text)
                        |> List.distinct
                        |> List.length)
                        =
                        1
                ))

        match duplicate with
        | None -> owners
        | Some ((path, memberName), _) ->
            let details =
                issues
                |> List.filter (fun issue -> issue.Path = path && issue.Member = memberName)
                |> List.map (fun issue -> issue.Message)
                |> String.concat " "

            invalidOp (
                "Cannot emit conflicting constructor "
                + String.concat "." (path @ [ memberName ])
                + ". "
                + details
            )
