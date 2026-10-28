namespace Ranvier.Css.Spec

open System
open System.Collections.Generic
open System.Text
open System.Text.Json

type internal CompactBindings = { Source: string; Manifest: string }

module internal CompactGeneration =
    let private quote (value: string) =
        JsonSerializer.Serialize value

    let private ident (value: string) =
        "``" + value.Replace ("`", "_") + "``"

    let private path parts =
        parts |> List.map ident |> String.concat "."

    let private keywordName (token: string) =
        match token with
        | "+" -> "tokenPlus"
        | ">" -> "tokenGreaterThan"
        | "||" -> "tokenDoubleBar"
        | "~" -> "tokenTilde"
        | "&" -> "tokenAmpersand"
        | "*" -> "tokenStar"
        | "|" -> "tokenBar"
        | "=" -> "tokenEquals"
        | _ -> token.Replace ("@", "at-")

    let private fallbackWideKeywords =
        set [ "initial"; "inherit"; "unset"; "revert"; "revert-layer"; "revert-rule" ]

    let private runtime = "global.Ranvier.Css.Internal.GrammarRuntime"

    type private ModuleTree =
        {
            Lines: ResizeArray<string>
            Children: SortedDictionary<string, ModuleTree>
        }

    let private tree () =
        {
            Lines = ResizeArray ()
            Children = SortedDictionary (StringComparer.Ordinal)
        }

    let render (namespaceName: string) (catalogue: Catalogue) =
        let authoringNamespace =
            if namespaceName.EndsWith (".Generated", StringComparison.Ordinal) then
                namespaceName.Substring (0, namespaceName.Length - ".Generated".Length)
            else
                namespaceName

        let resolution = Resolution.resolve catalogue
        let graph = GrammarGraphGeneration.build catalogue resolution

        let owners, issues =
            CompactOwnerGeneration.planWithIssues catalogue resolution graph

        let projected = PublicApiProjection.project catalogue resolution

        let wideKeywords =
            projected
            |> List.tryFind (fun feature ->
                feature.Id.Kind = FeatureKind.Property
                && feature.Id.Name = "all"
                && feature.Id.Scopes.IsEmpty)
            |> Option.map (fun feature ->
                GrammarGraphGeneration.acceptedNodes graph graph.Roots[feature.Id]
                |> Seq.map (fun id -> graph.NodesById[id])
                |> Seq.filter (fun node -> node.Kind = 1)
                |> Seq.map _.Text
                |> Set.ofSeq)
            |> Option.filter (Set.isEmpty >> not)
            |> Option.defaultValue fallbackWideKeywords

        let requested =
            seq {
                for owner in owners do
                    yield owner.NodeId
                    let node = graph.NodesById[owner.NodeId]

                    if node.Kind = 2 then
                        yield node.Target

                    yield! node.Children

                for feature in projected do
                    yield graph.Roots[feature.Id]
            }

        let exactCategories = CompactCategoryGeneration.plan graph requested

        let categories =
            CompactRepresentationGeneration.plan
                graph
                (projected
                 |> Seq.map (fun feature -> graph.Roots[feature.Id]))
                requested

        let storage = CompactStorageGeneration.render namespaceName graph categories
        let primitives = CompactPrimitiveGeneration.render catalogue graph categories
        let mathematics = CompactMathGeneration.render ()
        let root = tree ()
        let categoryTree = tree ()

        let rec at parts current =
            match parts with
            | [] -> current
            | head :: tail ->
                if not (current.Children.ContainsKey head) then
                    current.Children.Add (head, tree ())

                at tail current.Children[head]

        let append location text =
            (at location root).Lines.Add text

        let category id =
            "CssGrammar.C" + string categories.Categories[id]

        let canonicalOwners =
            owners
            |> List.filter (fun owner -> graph.NodesById[owner.NodeId].Kind <> 1)
            |> List.groupBy (fun owner -> categories.Categories[owner.NodeId])
            |> List.map (fun (id, candidates) ->
                id,
                candidates
                |> List.minBy (fun owner -> owner.Path.Length, owner.Path))
            |> Map.ofList

        let categoryPaths = Dictionary<int, string list>()

        for KeyValue (representative, owner) in canonicalOwners do
            categoryPaths.Add (representative, owner.Path)

        for owner in owners do
            let parent = graph.NodesById[owner.NodeId]

            let inputs =
                parent.Children
                |> Array.indexed
                |> Array.filter (fun (_, child) ->
                    List.contains parent.Kind [ 7; 8; 11 ]
                    || (List.contains parent.Kind [ 4; 5; 6; 9; 10 ]
                        && graph.NodesById[child].Kind <> 1))

            for index, child in inputs do
                match Map.tryFind child categories.Categories with
                | Some representative when not (categoryPaths.ContainsKey representative) ->
                    let role =
                        owner.Roles
                        |> List.tryPick (fun (slot, name) -> if slot = index then Some name else None)
                        |> Option.defaultValue "Value"

                    let role =
                        string (Char.ToUpperInvariant role[0])
                        + role.Substring 1

                    categoryPaths.Add (representative, owner.Path @ [ "Input"; role ])
                | _ -> ()

        let valueType id =
            let representative =
                Map.tryFind id categories.Categories
                |> Option.defaultWith (fun () ->
                    invalidOp (
                        "Public constructor references an unrequested grammar category "
                        + string id
                        + "."
                    ))

            match categoryPaths.TryGetValue representative with
            | true, location -> "Categories." + path location + ".Value"
            | _ -> category id

        for KeyValue (representative, location) in categoryPaths do
            let categoryModule = at location categoryTree
            categoryModule.Lines.Add ("/// <summary>Values admitted at this position in the CSS grammar.</summary>")

            categoryModule.Lines.Add (
                "type Value = CssGrammar.C"
                + string representative
            )

        let data name =
            "(" + name + " :> CssGrammar.IValue).Data"

        let wrap id expression =
            let factory = storage.TypeNames[id]

            factory.Substring (
                factory.LastIndexOf (".CssGrammar.", StringComparison.Ordinal)
                + 1
            )
            + "("
            + expression
            + ")"

        let construct id values =
            runtime
            + ".construct CssGrammar.table "
            + string id
            + " [|"
            + String.concat "; " values
            + "|]"

        let summary text =
            "/// <summary>" + text + "</summary>"

        let emitted = HashSet<string list * string>()
        let aliases = HashSet<string list>()
        let keywordAliases = ResizeArray<string list * string * int>()
        let booleanAliases = ResizeArray<string list * string * int>()

        let keywordNames =
            owners
            |> List.filter (fun owner -> graph.NodesById[owner.NodeId].Kind = 1)
            |> List.groupBy (fun owner -> graph.NodesById[owner.NodeId].Text)
            |> List.map (fun (token, members) -> token, (members |> List.minBy _.NodeId).Member)
            |> Map.ofList

        let keywordNodes =
            owners
            |> List.map (fun owner -> graph.NodesById[owner.NodeId])
            |> List.filter (fun node ->
                node.Kind = 1
                && Map.containsKey node.Text keywordNames)
            |> List.groupBy _.Text
            |> List.map (fun (token, nodes) -> token, (nodes |> List.minBy _.Id).Id)
            |> Map.ofList

        let colorRoot =
            graph.Roots
            |> Map.toSeq
            |> Seq.tryPick (fun (feature, id) ->
                if
                    feature.Kind = FeatureKind.Type
                    && feature.Scopes.IsEmpty
                    && feature.Name = "color"
                then
                    Some id
                else
                    None)

        let colorKeywords =
            colorRoot
            |> Option.map (GrammarGraphGeneration.acceptedNodes graph)
            |> Option.defaultValue Set.empty
            |> Seq.map (fun id -> graph.NodesById[id])
            |> Seq.filter (fun node -> node.Kind = 1)
            |> Seq.map _.Text
            |> Set.ofSeq

        for KeyValue (token, id) in keywordNodes do
            let keyword = ident (keywordName token)

            append
                [ "Keywords" ]
                (summary (
                    "The CSS "
                    + System.Security.SecurityElement.Escape token
                    + " keyword."
                ))

            if Set.contains token wideKeywords then
                append [ "Keywords" ] ("type " + keyword + " = Wide")

                append
                    [ "Keywords" ]
                    ("let "
                     + keyword
                     + " : "
                     + keyword
                     + " = Wide "
                     + quote token)
            else
                append [ "Keywords" ] ("type " + keyword + " = " + category id)

                append
                    [ "Keywords" ]
                    ("let "
                     + keyword
                     + " : "
                     + keyword
                     + " = "
                     + wrap id (runtime + ".token " + quote token))

            keywordAliases.Add ([ "Keywords" ], keywordName token, id)

        let primitiveNames = HashSet<string list * string>()

        for location, text in primitives.Declarations do
            append location (text.Replace ("CssValue.", ""))

            if text.StartsWith ("let ", StringComparison.Ordinal) then
                let finish = text.IndexOfAny ([| ' '; '(' |], 4)

                let name =
                    text.Substring(4, (if finish < 0 then text.Length else finish) - 4).Trim('`')

                primitiveNames.Add (location, name) |> ignore

        for declaration in mathematics.NumericSource.TrimEnd().Split('\n') do
            append [ "Numeric" ] declaration

        let emit (owner: CompactOwner) memberName arguments expression =
            if
                not (primitiveNames.Contains (owner.Path, memberName))
                && emitted.Add (owner.Path, memberName)
            then
                let contract =
                    if memberName = "unsafeFromTokens" then
                        "Accepts explicitly unchecked tokens for this CSS grammar."
                    else
                        "Constructs the CSS "
                        + memberName
                        + " value and validates its grammar."

                append owner.Path (summary contract)

                let resultType =
                    if graph.NodesById[owner.NodeId].Kind = 1 then
                        ""
                    else
                        " : Value"

                append
                    owner.Path
                    ("let "
                     + ident memberName
                     + " "
                     + arguments
                     + resultType
                     + " = "
                     + expression)

        for owner in owners do
            let node = graph.NodesById[owner.NodeId]

            if node.Kind <> 1 && aliases.Add owner.Path then
                let ownerModule = at owner.Path root
                ownerModule.Lines.Insert (0, "type Value = " + category node.Id)
                ownerModule.Lines.Insert (0, summary "Values admitted at this position in the CSS grammar.")

            let make arguments expression =
                emit owner owner.Member arguments (wrap node.Id expression)

            let roles = Map.ofList owner.Roles

            let arg index child =
                let name =
                    Map.tryFind index roles
                    |> Option.defaultValue ("component" + string (index + 1))

                let inputType =
                    if graph.NodesById[child].Kind = 1 then
                        ""
                    else
                        valueType child

                name, "(" + ident name + ": " + inputType + ")"

            match node.Kind with
            | 0
            | 12 ->
                emit
                    owner
                    "unsafeFromTokens"
                    "(tokens: string)"
                    (wrap
                        node.Id
                        (runtime
                         + ".opaque CssGrammar.table "
                         + string node.Id
                         + " tokens"))
            | 1 ->
                if
                    not (primitiveNames.Contains (owner.Path, owner.Member))
                    && not (emitted.Contains (owner.Path, owner.Member))
                then
                    emit owner owner.Member "" ("Keywords." + ident (keywordName node.Text))
                    keywordAliases.Add (owner.Path, owner.Member, node.Id)
            | 2
            | 8
            | 11 ->
                let child = if node.Kind = 2 then node.Target else node.Children[0]
                make ("(value: " + valueType child + ")") (construct node.Id [ "Some " + data "value" ])

                if node.Kind = 11 then
                    let boolean operation values =
                        wrap
                            node.Id
                            (runtime
                             + ".boolean CssGrammar.table "
                             + string node.Id
                             + " "
                             + quote operation
                             + " "
                             + values)

                    emit owner "not" "(value: Value)" (boolean "not" ("[|" + data "value" + "|]"))
                    booleanAliases.Add (owner.Path, "not", node.Id)

                    for operation in [ "and"; "or" ] do
                        emit
                            owner
                            operation
                            "(head: Value) (tail: Value list)"
                            (boolean
                                operation
                                ("(head :: tail |> List.map (fun value -> "
                                 + data "value"
                                 + ") |> List.toArray)"))

                        booleanAliases.Add (owner.Path, operation, node.Id)
            | 3 ->
                make
                    ("(value: #" + valueType node.Id + ")")
                    (runtime
                     + ".admit CssGrammar.table "
                     + string node.Id
                     + " "
                     + data "value")
            | 7 ->
                make
                    ("(values: #"
                     + valueType node.Children[0]
                     + " list)")
                    (runtime
                     + ".construct CssGrammar.table "
                     + string node.Id
                     + " (values |> List.map (fun value -> Some "
                     + data "value"
                     + ") |> List.toArray)")
            | 4
            | 5
            | 6
            | 9
            | 10 ->
                let args, values =
                    node.Children
                    |> Array.mapi (fun index child ->
                        let name, parameter = arg index child
                        let part = graph.NodesById[child]

                        if part.Kind = 1 && node.Kind = 6 then
                            "(" + ident name + ": bool)",
                            "(if "
                            + ident name
                            + " then Some ("
                            + runtime
                            + ".token "
                            + quote part.Text
                            + ") else None)"
                        elif part.Kind = 1 then
                            "",
                            "Some ("
                            + runtime
                            + ".token "
                            + quote part.Text
                            + ")"
                        elif node.Kind = 6 || Set.contains child graph.Nullable then
                            "("
                            + ident name
                            + ": "
                            + valueType child
                            + " option)",
                            "("
                            + ident name
                            + " |> Option.map (fun value -> "
                            + data "value"
                            + "))"
                        else
                            parameter, "Some " + data (ident name))
                    |> Array.unzip

                let args = args |> Array.filter ((<>) "")
                make (if args.Length = 0 then "()" else String.concat " " args) (construct node.Id (List.ofArray values))
            | _ -> invalidOp "Unsupported compact grammar operator."

        for feature in projected do
            let rootId = graph.Roots[feature.Id]

            if graph.NodesById[rootId].Kind <> 1 then
                let location = feature.Path |> List.tail
                let accepted = GrammarGraphGeneration.acceptedNodes graph rootId

                if
                    feature.Id.Name <> "color"
                    && location.Head <> "Color"
                    && (colorRoot
                        |> Option.exists (fun id -> Set.contains id accepted))
                then
                    append location (summary "Shared color values admitted by this grammar.")
                    append location "module Colors = Palette"

                accepted
                |> Seq.map (fun id -> graph.NodesById[id])
                |> Seq.filter (fun node ->
                    node.Kind = 1
                    && Map.containsKey node.Text keywordNames)
                |> Seq.filter (fun node ->
                    feature.Id.Name = "color"
                    || not (Set.contains node.Text colorKeywords))
                |> Seq.distinctBy _.Text
                |> Seq.iter (fun node ->
                    let memberName = keywordNames[node.Text]

                    if
                        not (primitiveNames.Contains (location, memberName))
                        && emitted.Add (location, memberName)
                    then
                        append
                            location
                            (summary (
                                "The CSS "
                                + System.Security.SecurityElement.Escape node.Text
                                + " keyword admitted by this grammar."
                            ))

                        append
                            location
                            ("let "
                             + ident memberName
                             + " = Keywords."
                             + ident (keywordName node.Text))

                        keywordAliases.Add (location, memberName, node.Id))

        let cssWide = Set.toList wideKeywords
        let source = StringBuilder (storage.Source)

        let line (text: string) =
            source.Append(text.TrimEnd ()).Append('\n')
            |> ignore

        line ""

        if authoringNamespace <> namespaceName then
            line (
                "namespace "
                + (authoringNamespace.Split ('.')
                   |> Array.map ident
                   |> String.concat ".")
            )

            line (
                "open global."
                + (namespaceName.Split ('.')
                   |> Array.map ident
                   |> String.concat ".")
            )

            line ""

        line (summary "Typed CSS values organized by grammar owner and semantic role.")
        line "module CssValue ="

        line (
            "    "
            + summary "Returns the serialized tokens of a typed CSS value."
        )

        line "    let render (value: #CssGrammar.IValue) = value.Data.Text"

        for declaration in CompactVariableGeneration.source().TrimEnd().Split('\n') do
            line ("    " + declaration)

        let rec emitTree indent current =
            for text in current.Lines do
                line (indent + text)

            for KeyValue (name, child) in current.Children do
                line (
                    indent
                    + summary ("CSS " + name + " value vocabulary.")
                )

                line (indent + "module " + ident name + " =")
                emitTree (indent + "    ") child

        line (
            "    "
            + summary "Shared semantic value categories used by constructor parameters."
        )

        line "    module Categories ="

        if
            categoryTree.Lines.Count = 0
            && categoryTree.Children.Count = 0
        then
            line "        do ()"

        emitTree "        " categoryTree

        match root.Children.TryGetValue "Keywords" with
        | true, keywords ->
            line (
                "    "
                + summary "Shared CSS keyword values with grammar-derived category admission."
            )

            line "    module Keywords ="
            emitTree "        " keywords
            root.Children.Remove "Keywords" |> ignore
        | _ -> ()

        match root.Children.TryGetValue "Numeric" with
        | true, numeric ->
            line (
                "    "
                + summary "Measured numeric values and arithmetic expressions."
            )

            line "    module Numeric ="
            emitTree "        " numeric
            root.Children.Remove "Numeric" |> ignore
        | _ -> ()

        match root.Children.TryGetValue "Color" with
        | true, colors ->
            line (
                "    "
                + summary "CSS color values and their shared palette."
            )

            line "    module Color ="
            emitTree "        " colors
            root.Children.Remove "Color" |> ignore
            line "    module Palette = Color"
        | _ -> ()

        emitTree "    " root

        for name, target in primitives.Aliases do
            line (
                "    "
                + summary ("Constructs a CSS " + name + " value.")
            )

            let constant =
                primitives.Factories
                |> List.exists (fun factory ->
                    factory.Owner = [ "CssValue" ]
                    && factory.MemberName = name
                    && factory.ArgumentType = "none")

            line (
                "    let "
                + ident name
                + (if constant then " = " else " value = ")
                + target.Substring ("CssValue.".Length)
                + (if constant then "" else " value")
            )

        line ""
        line (summary "Typed CSS declarations and tracked style readers.")
        line "type Css ="

        for declaration in mathematics.CssMembers.TrimEnd().Split('\n') do
            line declaration

        line (
            "    "
            + summary "Creates a category-preserving CSS custom-property reference."
        )

        line
            "    static member variable<'category when 'category :> CssGrammar.IValue>(name: string) : CssValue.Variable<'category> = CssValue.variable name"

        for name, target in primitives.Aliases do
            line (
                "    "
                + summary ("Constructs a CSS " + name + " value.")
            )

            let constant =
                primitives.Factories
                |> List.exists (fun factory ->
                    factory.Owner = [ "Css" ]
                    && factory.MemberName = name
                    && factory.ArgumentType = "none")

            line (
                "    static member "
                + ident name
                + (if constant then " = " else " value = ")
                + target
                + (if constant then "" else " value")
            )

        for feature in projected do
            match feature.DeclarationPath with
            | None -> ()
            | Some declaration ->
                let id = graph.Roots[feature.Id]
                let name = declaration |> List.last |> ident

                let publicType =
                    let categoryType = valueType id

                    if categoryType.StartsWith ("Categories.", StringComparison.Ordinal) then
                        "CssValue." + categoryType
                    else
                        categoryType

                let value = "(value: #" + publicType + ")"

                let admitted =
                    runtime
                    + ".admit CssGrammar.table "
                    + string id
                    + " "
                    + data "value"

                let declaration =
                    "global.Ranvier.Css.RuntimeApi.declaration "
                    + quote feature.Id.Name
                    + " ("
                    + admitted
                    + ").Text"

                line (
                    "    "
                    + summary ("Sets the CSS " + feature.Id.Name + " property.")
                )

                line (
                    "    static member "
                    + name
                    + " "
                    + value
                    + " : global.Ranvier.Css.Declaration = "
                    + declaration
                )

                line (
                    "    "
                    + summary (
                        "Uses a typed custom-property reference for CSS "
                        + feature.Id.Name
                        + "."
                    )
                )

                line (
                    "    static member "
                    + name
                    + " (variable: CssValue.Variable<#"
                    + publicType
                    + ">) : global.Ranvier.Css.Declaration = variable.Fallback |> Option.iter (fun fallback -> "
                    + runtime
                    + ".admit CssGrammar.table "
                    + string id
                    + " "
                    + data "fallback"
                    + " |> ignore); global.Ranvier.Css.RuntimeApi.declaration "
                    + quote feature.Id.Name
                    + " variable.Text"
                )

                line (
                    "    "
                    + summary (
                        "Uses a static CSS-wide keyword for CSS "
                        + feature.Id.Name
                        + "."
                    )
                )

                line (
                    "    static member "
                    + name
                    + " (value: CssValue.Wide) : global.Ranvier.Css.Declaration = global.Ranvier.Css.RuntimeApi.declaration "
                    + quote feature.Id.Name
                    + " value.Text"
                )

                line (
                    "    "
                    + summary (
                        "Tracks a reader for the CSS "
                        + feature.Id.Name
                        + " property."
                    )
                )

                line (
                    "    static member "
                    + name
                    + " (read: 'instance -> #"
                    + publicType
                    + ") : global.Ranvier.Css.StyleNode<'instance> = global.Ranvier.Css.Style.dynamicWithKeywords ["
                    + (cssWide |> List.map quote |> String.concat ";")
                    + "] "
                    + quote feature.Id.Name
                    + " (fun instance -> let value = read instance in "
                    + declaration
                    + ")"
                )

                let intrinsic =
                    CompactMathGeneration.intrinsicPropertyMembers graph id feature.Id.Name (name.Trim ('`'))

                if intrinsic <> "" then
                    for memberLine in intrinsic.TrimEnd().Split('\n') do
                        line memberLine

        let manifest =
            JsonSerializer.Serialize (
                {|
                    schemaVersion = 2
                    namespaceName = authoringNamespace
                    encodedGrammar = graph.Encoded
                    dataset = catalogue.Identity
                    featureCount = catalogue.Features.Length
                    nodeCount = graph.Nodes.Length
                    categoryCount = categories.Representatives.Length
                    sharedRepresentations =
                        categories.Categories
                        |> Map.toList
                        |> List.choose (fun (nodeId, representedBy) ->
                            if exactCategories.Accepted[exactCategories.Categories[nodeId]] = categories.Accepted[representedBy] then
                                None
                            else
                                Some
                                    {|
                                        nodeId = nodeId
                                        categoryNodeId = representedBy
                                    |})
                    owners =
                        owners
                        |> List.map (fun owner ->
                            {|
                                nodeId = owner.NodeId
                                path = owner.Path
                                memberName = owner.Member
                                roles =
                                    owner.Roles
                                    |> List.map (fun (index, name) -> {| index = index; name = name |})
                            |})
                    factories =
                        (primitives.Factories
                         |> List.map (fun factory ->
                             {|
                                 owner =
                                     authoringNamespace
                                     + "."
                                     + String.concat "." factory.Owner
                                 memberName = factory.MemberName
                                 nodeId = factory.NodeId
                                 kind = factory.Kind
                                 production = factory.Production
                                 unitName = factory.UnitName
                                 argumentType = factory.ArgumentType
                             |}))
                        @ (keywordAliases
                           |> Seq.map (fun (location, memberName, id) ->
                               {|
                                   owner =
                                       authoringNamespace
                                       + ".CssValue."
                                       + String.concat "." location
                                   memberName = memberName
                                   nodeId = id
                                   kind =
                                       if Set.contains graph.NodesById[id].Text wideKeywords then
                                           "wide"
                                       else
                                           "token"
                                   production = ""
                                   unitName = ""
                                   argumentType = "none"
                               |})
                           |> Seq.toList)
                        @ (booleanAliases
                           |> Seq.map (fun (location, operation, id) ->
                               {|
                                   owner =
                                       authoringNamespace
                                       + ".CssValue."
                                       + String.concat "." location
                                   memberName = operation
                                   nodeId = id
                                   kind = "boolean"
                                   production = operation
                                   unitName = ""
                                   argumentType = if operation = "not" then "value" else "headTail"
                               |})
                           |> Seq.toList)
                    properties =
                        projected
                        |> List.choose (fun feature ->
                            feature.DeclarationPath
                            |> Option.map (fun declaration ->
                                {|
                                    owner = authoringNamespace + ".Css"
                                    memberName = List.last declaration
                                    nodeId = graph.Roots[feature.Id]
                                    cssName = feature.Id.Name
                                |}))
                    definitions =
                        catalogue.Features
                        |> List.sortBy _.Id
                        |> List.map (fun feature ->
                            {|
                                kind = string feature.Id.Kind
                                name = feature.Id.Name
                                scopes = feature.Id.Scopes
                                owner = feature.Id.Owner
                                href = feature.Href
                                grammar =
                                    match feature.Grammar with
                                    | GrammarStatus.Parsed _ -> "parsed"
                                    | GrammarStatus.Missing -> "missing"
                                    | GrammarStatus.Rejected _ -> "rejected"
                                syntax =
                                    match feature.Grammar with
                                    | GrammarStatus.Parsed (text, _)
                                    | GrammarStatus.Rejected (text, _) -> text
                                    | GrammarStatus.Missing -> ""
                            |})
                    ownerIssues =
                        issues
                        |> List.map (fun issue ->
                            {|
                                nodeId = issue.NodeId
                                path = issue.Path
                                memberName = issue.Member
                                message = issue.Message
                            |})
                    unresolved =
                        graph.Issues
                        |> List.map (fun issue ->
                            {|
                                owner = sprintf "%A" issue.Owner
                                span = issue.Span
                                reference = issue.Reference.Name
                                target = sprintf "%A" issue.Target
                                message = issue.Message
                            |})
                |},
                JsonSerializerOptions (WriteIndented = true)
            )

        {
            Source = source.ToString ()
            Manifest = manifest
        }
