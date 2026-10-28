namespace Ranvier.Css

open System

/// <summary>A portable deterministic content identity shared by .NET and Fable.</summary>
module Identity =
    let private multiply first second =
        let low = (first &&& 65535u) * (second &&& 65535u)

        let cross =
            (first >>> 16) * (second &&& 65535u)
            + (first &&& 65535u) * (second >>> 16)

        (low + ((cross &&& 65535u) <<< 16)) ||| 0u

    /// <summary>Hashes UTF-16 code units using two independent 32-bit streams, rendered as sixteen lowercase hex digits.</summary>
    let hash (text: string) =
        let mutable first = 2166136261u
        let mutable second = 2246822519u

        for c in text do
            first <- multiply (first ^^^ uint32 c) 16777619u
            second <- multiply (second ^^^ uint32 c) 3266489917u

        first.ToString ("x8") + second.ToString ("x8")

/// <summary>A static style resource with a deterministic class name and ordinary CSS rules.</summary>
type StyleDefinition = { ClassName: string; Rules: Rule list }

/// <summary>Component-scoped style input, including tracked per-instance declaration readers.</summary>
[<NoEquality; NoComparison>]
type StyleNode<'instance> =
    internal
    | Declarations of Declaration list
    | Dynamic of name: string * important: bool * keywords: string list * read: ('instance -> Declaration)
    | BindVariable of name: string * read: ('instance -> Declaration)
    | Nest of suffix: string * StyleNode<'instance> list
    | Condition of name: string * prelude: string * StyleNode<'instance> list
    | ReactiveCondition of read: ('instance -> bool) * StyleNode<'instance> list

/// <summary>Static rules and per-instance variable declarations; readers run only when variable values are requested.</summary>
type Styled<'instance>
    internal
    (
        definition: StyleDefinition,
        variables: (int option * ('instance -> Declaration)) list,
        conditions: (int option * ('instance -> bool)) array,
        gateClasses: string list
    ) =
    /// <summary>Static rules and stable class identity shared by every component instance.</summary>
    member _.Definition = definition
    member internal _.Variables = variables
    /// <summary>Ordered parent indices and predicates for root class gates.</summary>
    member internal _.Conditions = conditions
    /// <summary>Generated gate classes in condition order.</summary>
    member internal _.GateClasses = gateClasses

/// <summary>Component-scoped CSS authoring and reactive custom-property lowering.</summary>
module Style =
    /// <summary>Adds declarations in source order, including repeated fallback declarations.</summary>
    let declarations values =
        Declarations values

    /// <summary>Declares a generated dynamic property using its specification's CSS-wide keyword registry.</summary>
    let dynamicWithKeywords keywords name read =
        Declaration.create name "" |> ignore
        Dynamic (name, false, keywords, read)

    /// <summary>Declares an important dynamic property with importance fixed in the static stylesheet.</summary>
    let dynamicImportantWithKeywords keywords name read =
        Declaration.create name "" |> ignore
        Dynamic (name, true, keywords, read)

    /// <summary>Declares a dynamic property whose reader preserves its name and uses ordinary property values.</summary>
    let dynamic name read =
        dynamicWithKeywords [ "initial"; "inherit"; "unset"; "revert"; "revert-layer"; "revert-rule" ] name read

    /// <summary>Declares a dynamic property with importance fixed in the static stylesheet.</summary>
    let dynamicImportant name read =
        dynamicImportantWithKeywords [ "initial"; "inherit"; "unset"; "revert"; "revert-layer"; "revert-rule" ] name read

    /// <summary>Binds a typed explicit variable to a per-instance value reader.</summary>
    let bind (variable: CustomProperty<'syntax>) (read: 'instance -> Value<'syntax>) =
        BindVariable (CustomProperty.name variable, fun instance -> CustomProperty.declaration variable (read instance))

    /// <summary>Adds a selector suffix to the current component selector.</summary>
    let nest suffix children =
        Selector.unsafeFromText ("x" + suffix) |> ignore
        Nest (suffix, children)

    /// <summary>Adds hover rules for the component selector.</summary>
    let hover children =
        nest ":hover" children

    /// <summary>Adds media-conditional component rules.</summary>
    let media condition children =
        Rule.media condition [] |> ignore
        Condition ("media", condition, children)

    /// <summary>Adds feature-support-conditional component rules.</summary>
    let supports condition children =
        Rule.supports condition [] |> ignore
        Condition ("supports", condition, children)

    /// <summary>Adds container-conditional component rules.</summary>
    let container condition children =
        Rule.container condition [] |> ignore
        Condition ("container", condition, children)

    /// <summary>Applies children while an instance predicate is true; the predicate is evaluated during binding.</summary>
    let whenActive read children =
        ReactiveCondition (read, children)

    /// <summary>Adapts every instance reader while preserving static nodes and their order.</summary>
    let rec contramap map nodes =
        nodes
        |> List.map (function
            | Declarations values -> Declarations values
            | Dynamic (name, important, keywords, read) -> Dynamic (name, important, keywords, fun value -> read (map value))
            | BindVariable (name, read) -> BindVariable (name, fun value -> read (map value))
            | Nest (suffix, children) -> Nest (suffix, contramap map children)
            | Condition (name, prelude, children) -> Condition (name, prelude, contramap map children)
            | ReactiveCondition (read, children) -> ReactiveCondition ((fun value -> read (map value)), contramap map children))

    /// <summary>Lowers ordered static rules, conditional class gates and dynamic slots without evaluating instance readers.</summary>
    let create nodes =
        let readers = ResizeArray<int option * ('instance -> Declaration)>()
        let conditions = ResizeArray<int option * ('instance -> bool)>()
        let mutable slot = 0

        let rec lower (root: string) (selector: string) variableName gateName parent nodes =
            [
                for node in nodes do
                    match node with
                    | Declarations values when not values.IsEmpty -> yield Rule.style (Selector.unsafeFromText selector) values
                    | Declarations _ -> ()
                    | Dynamic (name, important, keywords, read) ->
                        slot <- slot + 1
                        let variable = variableName slot

                        readers.Add (
                            parent,
                            fun instance ->
                                let declaration = read instance

                                if Declaration.name declaration <> name then
                                    invalidArg "read" "A dynamic declaration changed its property name."

                                if Declaration.isCssWideWith keywords declaration then
                                    invalidArg
                                        "read"
                                        "CSS-wide keywords require CssView.inlineStyle; custom-property substitution changes their cascade semantics."

                                if
                                    Declaration.isImportant declaration
                                    && not important
                                then
                                    invalidArg "read" "Use dynamicImportant to fix importance in the static stylesheet."

                                Declaration.create variable (Declaration.value declaration)
                        )

                        let declaration = Declaration.create name ("var(" + variable + ")")

                        yield
                            Rule.style
                                (Selector.unsafeFromText selector)
                                [
                                    if important then
                                        Declaration.important declaration
                                    else
                                        declaration
                                ]
                    | BindVariable (name, read) -> readers.Add (parent, read)
                    | Nest (suffix, children) -> yield! lower root (selector + suffix) variableName gateName parent children
                    | Condition (name, prelude, children) -> yield Rule.group name prelude (lower root selector variableName gateName parent children)
                    | ReactiveCondition (read, children) ->
                        let index = conditions.Count
                        conditions.Add (parent, read)
                        let gated = selector.Insert (root.Length, ":where(." + gateName index + ")")
                        yield! lower root gated variableName gateName (Some index) children
            ]

        let template =
            lower "&" "&" (fun n -> "--r-slot-" + string n) (fun n -> "r-gate-" + string n) None nodes

        let className = "r-" + Identity.hash (Stylesheet.render template)
        readers.Clear ()
        conditions.Clear ()
        slot <- 0

        let gateName n =
            className + "-g" + string n

        let rules =
            lower ("." + className) ("." + className) (fun n -> "--" + className + "-" + string n) gateName None nodes

        let gateClasses = [ for n in 0 .. conditions.Count - 1 -> gateName n ]
        Styled ({ ClassName = className; Rules = rules }, List.ofSeq readers, conditions.ToArray (), gateClasses)

    let private activeConditions instance (style: Styled<'instance>) =
        let active = Array.zeroCreate<bool> style.Conditions.Length

        for index in 0 .. style.Conditions.Length - 1 do
            let parent, read = style.Conditions[index]

            let enabled =
                parent
                |> Option.forall (fun parent -> active[parent])

            active[index] <- enabled && read instance

        active

    /// <summary>Indicates whether class selection depends on application predicates.</summary>
    let hasApplicationConditions (style: Styled<'instance>) =
        style.Conditions.Length > 0

    /// <summary>Returns the root class followed by active zero-specificity condition classes.</summary>
    let classNames instance (style: Styled<'instance>) =
        let active = activeConditions instance style

        style.Definition.ClassName
        :: (style.GateClasses
            |> List.indexed
            |> List.choose (fun (index, name) -> if active[index] then Some name else None))

    /// <summary>Evaluates active variable readers for an instance in declaration order.</summary>
    let variableDeclarations instance (style: Styled<'instance>) =
        let active = activeConditions instance style

        style.Variables
        |> List.choose (fun (parent, read) ->
            if
                parent
                |> Option.forall (fun parent -> active[parent])
            then
                Some (read instance)
            else
                None)

    /// <summary>Renders current per-instance custom properties for a style attribute.</summary>
    let renderVariables instance style =
        variableDeclarations instance style
        |> Declaration.renderMany

/// <summary>A declaration-list computation expression preserving yield and loop order.</summary>
type DeclarationBuilder() =
    /// <summary>Yields a declaration.</summary>
    member _.Yield(value: Declaration) = [ value ]
    /// <summary>Yields existing ordered declarations.</summary>
    member _.YieldFrom(values: Declaration list) = values
    /// <summary>Produces an empty declaration list.</summary>
    member _.Zero() = []

    /// <summary>Concatenates declaration lists in source order.</summary>
    member _.Combine(first, second) =
        first @ second

    /// <summary>Evaluates a declaration-list body.</summary>
    member _.Delay(read: unit -> Declaration list) = read ()

    /// <summary>Concatenates the results of a finite declaration loop.</summary>
    member _.For(items: seq<'item>, body: 'item -> Declaration list) =
        items |> Seq.collect body |> Seq.toList

    /// <summary>Returns ordered declarations.</summary>
    member _.Run(values: Declaration list) = values

/// <summary>A component-style computation expression with typed static and reactive nodes.</summary>
type StyledBuilder() =
    /// <summary>Yields a component style node.</summary>
    member _.Yield(value: StyleNode<'instance>) = [ value ]

    /// <summary>Yields a static declaration as a component style node.</summary>
    member _.Yield(value: Declaration) =
        [ Style.declarations [ value ] ]

    /// <summary>Yields existing component nodes in order.</summary>
    member _.YieldFrom(values: StyleNode<'instance> list) = values
    /// <summary>Produces an empty component node list.</summary>
    member _.Zero() : StyleNode<'instance> list = []

    /// <summary>Concatenates component nodes in source order.</summary>
    member _.Combine(first: StyleNode<'instance> list, second: StyleNode<'instance> list) =
        first @ second

    /// <summary>Evaluates the style definition body.</summary>
    member _.Delay(read: unit -> StyleNode<'instance> list) = read ()

    /// <summary>Expands a finite static style definition loop.</summary>
    member _.For(items: seq<'item>, body: 'item -> StyleNode<'instance> list) =
        items |> Seq.collect body |> Seq.toList

    /// <summary>Lowers the component nodes to static CSS and instance readers.</summary>
    member _.Run(values: StyleNode<'instance> list) =
        Style.create values

/// <summary>A stylesheet computation expression preserving ordered CSS rules.</summary>
type StylesheetBuilder() =
    /// <summary>Yields a stylesheet rule.</summary>
    member _.Yield(value: Rule) = [ value ]
    /// <summary>Yields existing stylesheet rules.</summary>
    member _.YieldFrom(values: Rule list) = values
    /// <summary>Produces an empty rule list.</summary>
    member _.Zero() = []

    /// <summary>Concatenates rules in source order.</summary>
    member _.Combine(first: Rule list, second: Rule list) =
        first @ second

    /// <summary>Evaluates the stylesheet body.</summary>
    member _.Delay(read: unit -> Rule list) = read ()

    /// <summary>Expands a finite static stylesheet loop.</summary>
    member _.For(items: seq<'item>, body: 'item -> Rule list) =
        items |> Seq.collect body |> Seq.toList

    /// <summary>Returns the ordered stylesheet rules.</summary>
    member _.Run(values: Rule list) = values

/// <summary>Declaration, component-style and stylesheet computation-expression entry points.</summary>
[<AutoOpen>]
module Authoring =
    /// <summary>Builds an ordered declaration list from yielded typed declarations.</summary>
    let declarations = DeclarationBuilder ()
    /// <summary>Builds a component-scoped style from static declarations and reactive property nodes.</summary>
    let styled = StyledBuilder ()
    /// <summary>Builds an ordered stylesheet rule list.</summary>
    let stylesheet = StylesheetBuilder ()
