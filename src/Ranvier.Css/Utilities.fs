namespace Ranvier.Css

open System
open System.Text.RegularExpressions

/// <summary>An ordered reusable composition with fixed declarations and browser conditions.</summary>
type Utility internal (nodes: StyleNode<unit> list) =
    /// <summary>Ordered composition nodes retained for lowering.</summary>
    member internal _.Nodes = nodes
    /// <summary>Returns static rules and the deterministic class identity.</summary>
    member _.Definition = (Style.create nodes).Definition

/// <summary>An ordered reusable composition with typed application conditions or value readers.</summary>
type Utility<'instance> internal (nodes: StyleNode<'instance> list) =
    /// <summary>Ordered composition nodes retained for lowering.</summary>
    member internal _.Nodes = nodes
    /// <summary>Returns all static alternatives without evaluating instance readers.</summary>
    member _.Definition = (Style.create nodes).Definition

/// <summary>Converts reusable compositions to component style resources.</summary>
module Utility =
    /// <summary>Returns a fixed utility's static rules and class identity.</summary>
    let staticDefinition (value: Utility) =
        value.Definition

    /// <summary>Adapts a fixed composition to any component instance type.</summary>
    let toStyled (value: Utility) : Styled<'instance> =
        Style.create (Style.contramap (fun _ -> ()) value.Nodes)

    /// <summary>Preserves the instance type of application conditions and value readers.</summary>
    let toStyledReactive (value: Utility<'instance>) =
        Style.create value.Nodes

/// <summary>A fixed computation-expression fragment retaining declaration order.</summary>
type StaticUtilityFragment internal (nodes: StyleNode<unit> list) =
    /// <summary>Ordered composition nodes retained for lowering.</summary>
    member internal _.Nodes = nodes

    /// <summary>Creates an empty fixed fragment for a conditional computation-expression branch.</summary>
    static member Empty() =
        StaticUtilityFragment []

/// <summary>A computation-expression fragment with typed instance readers.</summary>
type ReactiveUtilityFragment<'instance> internal (nodes: StyleNode<'instance> list) =
    /// <summary>Ordered composition nodes retained for lowering.</summary>
    member internal _.Nodes = nodes

    /// <summary>Creates an empty fragment preserving the expected instance type.</summary>
    static member Empty() =
        ReactiveUtilityFragment<'instance> []

/// <summary>Combines typed declarations and reusable compositions in authored order.</summary>
type UtilityBuilderBase() =
    /// <summary>Yields a fixed declaration.</summary>
    member _.Yield(value: Declaration) =
        StaticUtilityFragment [ Style.declarations [ value ] ]

    /// <summary>Yields an instance-dependent declaration or condition.</summary>
    member _.Yield(value: StyleNode<'instance>) =
        ReactiveUtilityFragment [ value ]

    /// <summary>Includes a fixed utility.</summary>
    member _.Yield(value: Utility) =
        StaticUtilityFragment value.Nodes

    /// <summary>Includes a utility with typed instance readers.</summary>
    member _.Yield(value: Utility<'instance>) =
        ReactiveUtilityFragment value.Nodes

    /// <summary>Includes fixed declarations in source order.</summary>
    member _.YieldFrom(values: Declaration list) =
        StaticUtilityFragment [ Style.declarations values ]

    /// <summary>Includes fixed utilities in source order.</summary>
    member _.YieldFrom(values: Utility list) =
        StaticUtilityFragment (values |> List.collect _.Nodes)

    /// <summary>Includes instance-dependent utilities in source order.</summary>
    member _.YieldFrom(values: Utility<'instance> list) =
        ReactiveUtilityFragment (values |> List.collect _.Nodes)

    /// <summary>Includes existing instance-dependent nodes in source order.</summary>
    member _.YieldFrom(values: StyleNode<'instance> list) =
        ReactiveUtilityFragment values

    /// <summary>Produces an empty fragment matching the surrounding fixed or reactive branch.</summary>
    member inline _.Zero() : ^fragment =
        ((^fragment): (static member Empty: unit -> ^fragment) ())

    /// <summary>Concatenates fixed fragments.</summary>
    member _.Combine(first: StaticUtilityFragment, second: StaticUtilityFragment) =
        StaticUtilityFragment (first.Nodes @ second.Nodes)

    /// <summary>Appends typed instance-dependent nodes to fixed nodes.</summary>
    member _.Combine(first: StaticUtilityFragment, second: ReactiveUtilityFragment<'instance>) =
        ReactiveUtilityFragment (
            Style.contramap (fun _ -> ()) first.Nodes
            @ second.Nodes
        )

    /// <summary>Appends fixed nodes to typed instance-dependent nodes.</summary>
    member _.Combine(first: ReactiveUtilityFragment<'instance>, second: StaticUtilityFragment) =
        ReactiveUtilityFragment (
            first.Nodes
            @ Style.contramap (fun _ -> ()) second.Nodes
        )

    /// <summary>Concatenates fragments sharing the same instance type.</summary>
    member _.Combine(first: ReactiveUtilityFragment<'instance>, second: ReactiveUtilityFragment<'instance>) =
        ReactiveUtilityFragment (first.Nodes @ second.Nodes)

    /// <summary>Evaluates a fixed definition body.</summary>
    member _.Delay(read: unit -> StaticUtilityFragment) = read ()
    /// <summary>Evaluates an instance-dependent definition body without reading an instance.</summary>
    member _.Delay(read: unit -> ReactiveUtilityFragment<'instance>) = read ()

    /// <summary>Expands a finite loop of fixed fragments.</summary>
    member _.For(items: seq<'item>, body: 'item -> StaticUtilityFragment) =
        StaticUtilityFragment (
            items
            |> Seq.collect (fun item -> (body item).Nodes)
            |> Seq.toList
        )

    /// <summary>Expands a finite loop retaining typed readers.</summary>
    member _.For(items: seq<'item>, body: 'item -> ReactiveUtilityFragment<'instance>) =
        ReactiveUtilityFragment (
            items
            |> Seq.collect (fun item -> (body item).Nodes)
            |> Seq.toList
        )

/// <summary>Builds reusable utilities from ordered fixed or instance-dependent fragments.</summary>
type UtilityBuilder() =
    inherit UtilityBuilderBase()

    /// <summary>Creates a reusable fixed composition.</summary>
    member _.Run(values: StaticUtilityFragment) =
        Utility values.Nodes

    /// <summary>Creates a reusable instance-dependent composition.</summary>
    member _.Run(values: ReactiveUtilityFragment<'instance>) =
        Utility<'instance> values.Nodes

type private BrowserCondition =
    | Query of name: string * prelude: string
    | Suffix of string

module private BrowserCondition =
    let apply condition nodes =
        match condition with
        | Query ("media", text) -> [ Style.media text nodes ]
        | Query ("supports", text) -> [ Style.supports text nodes ]
        | Query (_, text) -> [ Style.container text nodes ]
        | Suffix suffix -> [ Style.nest suffix nodes ]

/// <summary>Wraps complete compositions in a reusable browser condition.</summary>
type UtilityConditionBuilder private (condition: BrowserCondition) =
    inherit UtilityBuilderBase()

    /// <summary>Creates a fixed utility under the browser condition.</summary>
    member _.Run(values: StaticUtilityFragment) =
        Utility (BrowserCondition.apply condition values.Nodes)

    /// <summary>Retains application readers under the browser condition.</summary>
    member _.Run(values: ReactiveUtilityFragment<'instance>) =
        Utility<'instance>(BrowserCondition.apply condition values.Nodes)

    static member internal Query(name, text) =
        Rule.group name text [] |> ignore

        if String.IsNullOrWhiteSpace text then
            invalidArg "text" "Expected a nonempty browser condition."

        UtilityConditionBuilder (Query (name, text))

    static member internal Suffix(text) =
        Selector.unsafeFromText ("x" + text) |> ignore
        UtilityConditionBuilder (Suffix text)

/// <summary>Applies a complete composition while a typed application predicate is true.</summary>
type ActiveUtilityBuilder<'instance> internal (read: 'instance -> bool) =
    inherit UtilityBuilderBase()

    /// <summary>Gates fixed nodes using an instance predicate.</summary>
    member _.Run(values: StaticUtilityFragment) =
        Utility<'instance> [ Style.whenActive read (Style.contramap (fun _ -> ()) values.Nodes) ]

    /// <summary>Gates readers and nested conditions using an instance predicate.</summary>
    member _.Run(values: ReactiveUtilityFragment<'instance>) =
        Utility<'instance> [ Style.whenActive read values.Nodes ]

/// <summary>Default short utility and condition authoring entry points.</summary>
[<AutoOpen>]
module UtilityAuthoring =
    let private hasSelectorList (text: string) =
        let mutable depth = 0
        let mutable quote = '\000'
        let mutable escaped = false
        let mutable comma = false
        let mutable comment = false
        let mutable index = 0

        while index < text.Length do
            let character = text[index]
            let next = if index + 1 < text.Length then text[index + 1] else '\000'

            if comment then
                if character = '*' && next = '/' then
                    comment <- false
                    index <- index + 1
            elif escaped then
                escaped <- false
            elif character = '\\' then
                escaped <- true
            elif quote <> '\000' then
                if character = quote then
                    quote <- '\000'
            elif character = '/' && next = '*' then
                comment <- true
                index <- index + 1
            elif character = '\'' || character = '"' then
                quote <- character
            elif character = '(' || character = '[' then
                depth <- depth + 1
            elif character = ')' || character = ']' then
                depth <- depth - 1
            elif character = ',' && depth = 0 then
                comma <- true

            index <- index + 1

        comma

    let private viewport feature value =
        let text = Value.render value

        let matched =
            Regex.Match (text, @"^([+]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)([A-Za-z]+)?$")

        let lengths =
            [
                "px"
                "cm"
                "mm"
                "q"
                "in"
                "pc"
                "pt"
                "em"
                "rem"
                "ex"
                "rex"
                "ch"
                "rch"
                "cap"
                "rcap"
                "ic"
                "ric"
                "lh"
                "rlh"
                "vw"
                "vh"
                "vi"
                "vb"
                "vmin"
                "vmax"
                "svw"
                "svh"
                "svi"
                "svb"
                "svmin"
                "svmax"
                "lvw"
                "lvh"
                "lvi"
                "lvb"
                "lvmin"
                "lvmax"
                "dvw"
                "dvh"
                "dvi"
                "dvb"
                "dvmin"
                "dvmax"
            ]

        if not matched.Success then
            invalidArg "value" "Expected a fixed nonnegative CSS length for a viewport threshold."

        let number =
#if FABLE_COMPILER
            Double.Parse matched.Groups[1].Value
#else
            Double.Parse (matched.Groups[1].Value, Globalization.CultureInfo.InvariantCulture)
#endif

        let unitName = matched.Groups[2].Value.ToLowerInvariant()

        if
            Double.IsNaN number
            || Double.IsInfinity number
            || (not (List.contains unitName lengths)
                && not (number = 0. && unitName = ""))
        then
            invalidArg "value" "Expected a fixed nonnegative CSS length for a viewport threshold."

        UtilityConditionBuilder.Query ("media", "(" + feature + ": " + text + ")")

    /// <summary>Builds a reusable ordered composition.</summary>
    let utility = UtilityBuilder ()

    /// <summary>Applies compositions at or above a fixed nonnegative viewport length.</summary>
    let minViewportWidth value =
        viewport "min-width" value

    /// <summary>Applies compositions at or below a fixed nonnegative viewport length.</summary>
    let maxViewportWidth value =
        viewport "max-width" value

    /// <summary>Applies compositions under caller-supplied media query syntax.</summary>
    let media text =
        UtilityConditionBuilder.Query ("media", text)

    /// <summary>Applies compositions under caller-supplied feature query syntax.</summary>
    let supports text =
        UtilityConditionBuilder.Query ("supports", text)

    /// <summary>Applies compositions under caller-supplied container query syntax.</summary>
    let container text =
        UtilityConditionBuilder.Query ("container", text)

    /// <summary>Applies compositions while the receiving element is hovered.</summary>
    let hover = UtilityConditionBuilder.Suffix ":hover"
    /// <summary>Applies compositions while the receiving element has focus.</summary>
    let focus = UtilityConditionBuilder.Suffix ":focus"
    /// <summary>Applies compositions while the receiving element or a descendant has focus.</summary>
    let focusWithin = UtilityConditionBuilder.Suffix ":focus-within"

    /// <summary>Applies compositions to descendants matching an explicit selector.</summary>
    /// <remarks>Top-level selector lists require separate descendant blocks to retain root scope and specificity.</remarks>
    let descendant selector =
        let text = Selector.render selector

        if hasSelectorList text then
            invalidArg "selector" "Use separate descendant blocks for a selector list."

        UtilityConditionBuilder.Suffix (" " + text)

    /// <summary>Applies compositions to a named unescaped pseudo-element.</summary>
    let pseudoElement name =
        Generated.identifier false false name |> ignore
        UtilityConditionBuilder.Suffix ("::" + name)

    /// <summary>Appends caller-validated selector syntax to the receiving element.</summary>
    let selectorScope suffix =
        UtilityConditionBuilder.Suffix suffix

    /// <summary>Applies compositions when the receiving element matches an explicit theme selector.</summary>
    let theme selector =
        UtilityConditionBuilder.Suffix (":is(" + Selector.render selector + ")")

    /// <summary>Selects a precompiled composition using a typed application predicate.</summary>
    let whenActive read =
        ActiveUtilityBuilder read
