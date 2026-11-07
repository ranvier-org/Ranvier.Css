namespace Ranvier.Css.Spec

open System
open System.Text.RegularExpressions
open Ranvier.Css.Internal

/// <summary>A named flattened parameter of a generated color function constructor.</summary>
type internal ColorParameter =
    {
        Name: string
        Parameter: GrammarRuntime.Parameter
        /// <summary>Named item parameters of a repeated parameter, in runtime item order.</summary>
        Items: (string * GrammarRuntime.Parameter) list
    }

/// <summary>A CSS color function admitted by <c>&lt;color&gt;</c> with grammar-derived parameters.</summary>
type internal ColorFunction =
    {
        /// <summary>The CSS function name, such as <c>oklch</c>.</summary>
        FunctionName: string
        MemberName: string
        /// <summary>The selected function production.</summary>
        NodeId: int
        /// <summary>Parameters in runtime <c>tryParameters</c> order: required and repeated first, then optional.</summary>
        Parameters: ColorParameter list
    }

type internal ColorPlan =
    {
        ColorRoot: int option
        HexColor: int option
        Functions: ColorFunction list
        Issues: CompactOwnerIssue list
    }

/// <summary>Plans flattened constructors for every function production admitted by the shared <c>&lt;color&gt;</c> type.</summary>
/// <remarks>
/// Structure, punctuation, optional groups, repetitions, value categories and validation come from the grammar graph
/// through <c>GrammarRuntime.tryParameters</c>, the same kernel used by generated code and static extraction.
/// The grammar does not name positional channels, so a small explicit naming policy supplies those names. The generator
/// rejects a policy entry whose arity or keyword-derived names disagree with the grammar.
/// </remarks>
module internal CompactColorGeneration =
    let private camel (value: string) =
        let words =
            Regex.Matches (value, "[A-Za-z0-9]+")
            |> Seq.cast<Match>
            |> Seq.map _.Value
            |> Seq.toList

        match words with
        | [] -> ""
        | head :: tail ->
            string (Char.ToLowerInvariant head[0])
            + head.Substring 1
            + (tail
               |> List.map (fun word ->
                   string (Char.ToUpperInvariant word[0])
                   + word.Substring 1)
               |> String.concat "")

    /// <summary>Channel names in runtime parameter order, keyed by CSS function name.</summary>
    /// <remarks>Names follow CSS Color 4, CSS Color 5 and CSS Color HDR component terminology.</remarks>
    let namingPolicy =
        let modern channels =
            channels @ [ "from"; "alpha" ]

        Map.ofList
            [
                "rgb", modern [ "red"; "green"; "blue" ]
                "rgba", modern [ "red"; "green"; "blue" ]
                "hsl", modern [ "hue"; "saturation"; "lightness" ]
                "hsla", modern [ "hue"; "saturation"; "lightness" ]
                "hwb", modern [ "hue"; "whiteness"; "blackness" ]
                "lab", modern [ "lightness"; "a"; "b" ]
                "lch", modern [ "lightness"; "chroma"; "hue" ]
                "oklab", modern [ "lightness"; "a"; "b" ]
                "oklch", modern [ "lightness"; "chroma"; "hue" ]
                "ictcp", modern [ "i"; "ct"; "cp" ]
                "jzazbz", modern [ "jz"; "az"; "bz" ]
                "jzczhz", modern [ "jz"; "cz"; "hz" ]
                "device-cmyk", [ "cyan"; "magenta"; "yellow"; "black"; "alpha" ]
                "light-dark", [ "light"; "dark" ]
            ]

    let rec private nodeName (graph: GrammarGraph) id =
        let node = graph.NodesById[id]

        match node.Kind with
        | 0
        | 2 when node.Text <> "" ->
            let text =
                if node.Text.EndsWith ("-value", StringComparison.Ordinal) then
                    node.Text.Substring (0, node.Text.Length - "-value".Length)
                else
                    node.Text

            camel text
        | 3 ->
            node.Children
            |> Array.tryFind (fun child -> graph.NodesById[child].Kind <> 1)
            |> Option.map (nodeName graph)
            |> Option.defaultValue "value"
        | _ -> "value"

    let private derivedName graph parameter =
        match parameter with
        | GrammarRuntime.Required (id, keyword)
        | GrammarRuntime.Optional (id, keyword) -> if keyword <> "" then camel keyword else nodeName graph id
        | GrammarRuntime.Repeated (_, items) ->
            match Array.tryHead items with
            | Some (GrammarRuntime.Required (id, keyword))
            | Some (GrammarRuntime.Optional (id, keyword)) ->
                (if keyword <> "" then camel keyword else nodeName graph id)
                + "s"
            | _ -> "values"

    let private distinct (names: string list) =
        let counts = Collections.Generic.Dictionary<string, int>()

        names
        |> List.map (fun name ->
            let count =
                match counts.TryGetValue name with
                | true, count -> count + 1
                | _ -> 1

            counts[name] <- count
            if count = 1 then name else name + string count)

    let private named graph (parameters: GrammarRuntime.Parameter list) =
        let names =
            parameters
            |> List.map (derivedName graph)
            |> distinct

        List.zip names parameters

    let private size parameters =
        parameters
        |> Array.sumBy (function
            | GrammarRuntime.Repeated (_, items) -> items.Length
            | _ -> 1)

    /// <summary>Selects one flattenable production for each color function and names its parameters.</summary>
    let plan (graph: GrammarGraph) : ColorPlan =
        let root =
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

        let hex =
            graph.Roots
            |> Map.tryPick (fun feature id ->
                if
                    feature.Kind = FeatureKind.Type
                    && feature.Scopes.IsEmpty
                    && feature.Name = "hex-color"
                then
                    Some id
                else
                    None)

        match root with
        | None ->
            {
                ColorRoot = None
                HexColor = None
                Functions = []
                Issues = []
            }
        | Some root ->
            let table = GrammarRuntime.Table.decode graph.Encoded
            let accepted = GrammarGraphGeneration.acceptedNodes graph root
            let issues = ResizeArray<CompactOwnerIssue>()

            let issue id memberName message =
                issues.Add
                    {
                        NodeId = id
                        Path = [ "Color" ]
                        Member = memberName
                        Message = message
                    }

            let functions =
                accepted
                |> Seq.map (fun id -> graph.NodesById[id])
                |> Seq.filter (fun node -> node.Kind = 9 && node.Text <> "")
                |> Seq.groupBy _.Text
                |> Seq.sortBy fst
                |> Seq.choose (fun (functionName, productions) ->
                    let memberName = camel functionName

                    let candidates =
                        productions
                        |> Seq.choose (fun node ->
                            GrammarRuntime.tryParameters table node.Id
                            |> Option.map (fun parameters -> node.Id, parameters))
                        |> Seq.toList

                    match candidates with
                    | [] ->
                        issue
                            (productions |> Seq.map _.Id |> Seq.min)
                            memberName
                            "No production of this color function can be flattened into named parameters; use its structural constructors under CssValue.Functions."

                        None
                    | _ ->
                        let id, parameters =
                            candidates
                            |> List.sortBy (fun (id, parameters) -> -(size parameters), id)
                            |> List.head

                        let parameters = List.ofArray parameters
                        let derived = named graph parameters

                        let names =
                            match Map.tryFind functionName namingPolicy with
                            | Some policy ->
                                if policy.Length <> parameters.Length then
                                    invalidOp (
                                        "The color naming policy for "
                                        + functionName
                                        + "() lists "
                                        + string policy.Length
                                        + " parameters, but its grammar has "
                                        + string parameters.Length
                                        + "."
                                    )

                                List.zip policy parameters
                                |> List.iter (fun (name, parameter) ->
                                    match parameter with
                                    | GrammarRuntime.Required (_, keyword)
                                    | GrammarRuntime.Optional (_, keyword) when keyword <> "" && camel keyword <> name ->
                                        invalidOp (
                                            "The color naming policy for "
                                            + functionName
                                            + "() names the grammar keyword '"
                                            + keyword
                                            + "' as '"
                                            + name
                                            + "'."
                                        )
                                    | _ -> ())

                                policy
                            | None ->
                                let names = List.map fst derived
                                let raw = parameters |> List.map (derivedName graph)

                                if names <> raw then
                                    issue
                                        id
                                        memberName
                                        "Repeated color function parameters have no distinct grammar names; positional suffixes are used until an explicit naming policy is added."

                                names

                        if
                            names |> List.distinct |> List.length
                            <> names.Length
                        then
                            invalidOp (
                                "The color naming policy for "
                                + functionName
                                + "() repeats a parameter name."
                            )

                        Some
                            {
                                FunctionName = functionName
                                MemberName = memberName
                                NodeId = id
                                Parameters =
                                    List.zip names parameters
                                    |> List.map (fun (name, parameter) ->
                                        {
                                            Name = name
                                            Parameter = parameter
                                            Items =
                                                match parameter with
                                                | GrammarRuntime.Repeated (_, items) -> named graph (List.ofArray items)
                                                | _ -> []
                                        })
                            })
                |> Seq.toList

            {
                ColorRoot = Some root
                HexColor = hex
                Functions = functions
                Issues = List.ofSeq issues
            }

    /// <summary>Lists every grammar node a planned constructor references in its public signature.</summary>
    let referencedNodes (plan: ColorPlan) =
        let rec nodes parameter =
            match parameter with
            | GrammarRuntime.Required (id, _)
            | GrammarRuntime.Optional (id, _) -> [ id ]
            | GrammarRuntime.Repeated (_, items) -> items |> Array.toList |> List.collect nodes

        [
            yield! Option.toList plan.HexColor
            for colorFunction in plan.Functions do
                yield colorFunction.NodeId

                for parameter in colorFunction.Parameters do
                    yield! nodes parameter.Parameter
        ]
