module Ranvier.Css.Benchmarks.Program

open System
open System.Diagnostics
open System.Globalization
open Ranvier
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View
open Ranvier.View.Dsl

/// <summary>One list row: a text and the geometry of three styled elements.</summary>
type Row =
    {
        Text: Signal<string>
        Top: Signal<float>
        Left: Signal<float>
        Width: Signal<float>
        Height: Signal<float>
        Opacity: Signal<float>
        Caret: Signal<float>
        Selected: Signal<bool>
    }

module Row =
    let create (graph: Graph) =
        {
            Text = Signal (graph, "first")
            Top = Signal (graph, 120.)
            Left = Signal (graph, 48.)
            Width = Signal (graph, 640.5)
            Height = Signal (graph, 20.)
            Opacity = Signal (graph, 0.85)
            Caret = Signal (graph, 96.25)
            Selected = Signal (graph, true)
        }

/// <summary>Reader functions run since the last reset, counted by the readers themselves.</summary>
let mutable private readerRuns = 0

let private frame =
    utility {
        Css.position CssValue.Position.absolute

        Css.top (fun (row: Row) ->
            readerRuns <- readerRuns + 1
            Css.px row.Top.Value)

        Css.left (fun (row: Row) ->
            readerRuns <- readerRuns + 1
            Css.px row.Left.Value)

        Css.width (fun (row: Row) ->
            readerRuns <- readerRuns + 1
            Css.px row.Width.Value)
    }

let private bar =
    utility {
        Css.height (fun (row: Row) ->
            readerRuns <- readerRuns + 1
            Css.px row.Height.Value)

        Css.opacity (fun (row: Row) ->
            readerRuns <- readerRuns + 1
            Css.number row.Opacity.Value)

        whenActive (fun (row: Row) -> row.Selected.Value) { Css.color CssValue.Color.blue }
    }

let private caret =
    utility {
        Css.position CssValue.Position.absolute

        Css.left (fun (row: Row) ->
            readerRuns <- readerRuns + 1
            Css.px row.Caret.Value)
    }

/// <summary>Three styled elements with six reader values and one application condition, and one text slot.</summary>
let private readerValues =
    Component.define (fun _ (row: Row) -> row) (fun _ ->
        div (styles { frame }) {
            div (styles { bar }) { text (fun (row: Row) -> row.Text.Value) }
            span (styles { caret }) { "" }
        })

let private pixels (value: float) =
    value.ToString ("R", CultureInfo.InvariantCulture)
    + "px"

/// <summary>The elements of <see cref="readerValues" /> with each style attribute written as one text by its reader.</summary>
let private styleText =
    Component.define (fun _ (row: Row) -> row) (fun _ ->
        div
            [|
                div.attribute (
                    "style",
                    fun (row: Row) ->
                        Some (
                            String.Concat (
                                "position:absolute;top:",
                                pixels row.Top.Value,
                                ";left:",
                                pixels row.Left.Value,
                                ";width:" + pixels row.Width.Value
                            )
                        )
                )
            |] {
            div
                [|
                    div.attribute (
                        "style",
                        fun (row: Row) ->
                            Some (
                                String.Concat (
                                    "height:",
                                    pixels row.Height.Value,
                                    ";opacity:",
                                    row.Opacity.Value.ToString ("R", CultureInfo.InvariantCulture)
                                )
                            )
                    )
                    div.attribute ("class", fun (row: Row) -> if row.Selected.Value then Some "selected" else None)
                |] {
                text (fun (row: Row) -> row.Text.Value)
            }

            span
                [|
                    span.attribute ("style", fun (row: Row) -> Some ("position:absolute;left:" + pixels row.Caret.Value))
                |] {
                ""
            }
        })

let rec private collectSlots (slots: ResizeArray<Slot>) (node: TemplateNode) =
    match node with
    | ElementNode (_, _, _, elementSlots, _, _, children) ->
        slots.AddRange elementSlots

        for child in children do
            collectSlots slots child
    | TextSlotNode slot -> slots.Add slot
    | _ -> ()

/// <summary>Which slots of an instance share the render effect.</summary>
type private Effects =
    /// <summary>One effect reads every slot, as <c>Ranvier.View.Dom</c> does for the render slots of a template part.</summary>
    | OneRenderEffect
    /// <summary>Each <c>style</c> attribute slot has an effect of its own, as an isolated slot has; the render effect reads the rest.</summary>
    | StyleSlotsIsolated

/// <summary>A mounted instance without a DOM host: effects read the slots of the template and keep each changed value.</summary>
type private Mounted(graph: Graph, definition: Component<Row>, row: Row, effects: Effects) =
    let slots =
        let found = ResizeArray<Slot>()
        collectSlots found definition.Root
        found.ToArray ()

    let instance = box row
    let previous: obj array = Array.zeroCreate slots.Length
    let committed: bool array = Array.zeroCreate slots.Length
    let mutable runs = 0
    let mutable writes = 0

    let read index =
        let slot = slots[index]
        let value = slot.Read.Invoke instance

        if
            not committed[index]
            || not (slot.Same.Invoke (previous[index], value))
        then
            previous[index] <- value
            committed[index] <- true
            writes <- writes + 1

    let isolated (slot: Slot) =
        match effects, slot.Target with
        | StyleSlotsIsolated, AttributeTarget "style" -> true
        | _ -> false

    let started =
        [|
            new Effect (
                graph,
                fun () ->
                    runs <- runs + 1

                    for index in 0 .. slots.Length - 1 do
                        if not (isolated slots[index]) then
                            read index
            )

            for index in 0 .. slots.Length - 1 do
                if isolated slots[index] then
                    new Effect (
                        graph,
                        fun () ->
                            runs <- runs + 1
                            read index
                    )
        |]

    member _.SlotCount = slots.Length
    member _.EffectCount = started.Length
    member _.EffectRuns = runs
    member _.Writes = writes

    member _.Values =
        previous
        |> Array.map (fun value ->
            match value with
            | null -> "<absent>"
            | :? (string option) as text -> text.Value
            | other -> string other)

    interface IDisposable with
        member _.Dispose() =
            for effect in started do
                effect.Dispose ()

type private Measurement =
    {
        Name: string
        /// <summary>Median over the samples, in nanoseconds for one operation.</summary>
        Nanoseconds: float
        /// <summary>Lowest sample, in nanoseconds for one operation.</summary>
        Fastest: float
        /// <summary>Bytes allocated on the measuring thread for one operation.</summary>
        Bytes: float
    }

let private sampleCount = 15

let private measure name (operations: int) (run: int -> unit) =
    // Tiered compilation promotes the loop body before the first sample.
    for _ in 1..3 do
        run operations

    GC.Collect ()
    GC.WaitForPendingFinalizers ()
    GC.Collect ()

    let samples = Array.zeroCreate<float> sampleCount
    let mutable bytes = 0L

    for sample in 0 .. sampleCount - 1 do
        let before = GC.GetAllocatedBytesForCurrentThread ()
        let started = Stopwatch.GetTimestamp ()
        run operations
        let elapsed = Stopwatch.GetElapsedTime started
        bytes <- GC.GetAllocatedBytesForCurrentThread () - before
        samples[sample] <- elapsed.TotalNanoseconds / float operations

    Array.sortInPlace samples

    {
        Name = name
        Nanoseconds = samples[sampleCount / 2]
        Fastest = samples[0]
        Bytes = float bytes / float operations
    }

let private report (results: Measurement seq) =
    printfn "%-64s %10s %10s %10s" "operation" "median ns" "fastest ns" "bytes"

    for result in results do
        printfn "%-64s %10.1f %10.1f %10.1f" result.Name result.Nanoseconds result.Fastest result.Bytes

/// <summary>Sink for values a measured loop would otherwise discard.</summary>
let mutable private kept: obj = null

let private changes: (string * (Row -> int -> unit)) list =
    [
        "text written, no style value changed", (fun row index -> row.Text.Value <- if index % 2 = 0 then "first" else "second")
        "top written, one style value changed", (fun row index -> row.Top.Value <- if index % 2 = 0 then 120. else 140.)
        "top written, a new number each time", (fun row index -> row.Top.Value <- float (index % 4096) * 0.25)
        "selected written, condition changed", (fun row index -> row.Selected.Value <- (index % 2 = 0))
    ]

/// <summary>Times each change with the render it causes, and counts what one such render runs.</summary>
let private renders name (definition: Component<Row>) effects =
    [
        for label, change in changes do
            use graph = new Graph ()
            let row = Row.create graph
            use mounted = new Mounted (graph, definition, row, effects)

            let result =
                measure (name + ": " + label) 200_000 (fun count ->
                    for index in 1..count do
                        change row index)

            let runsBefore, writesBefore = mounted.EffectRuns, mounted.Writes
            readerRuns <- 0
            change row 1
            change row 2

            let counts =
                sprintf
                    "%d effect(s) over %d slot(s); one change runs %d effect(s) and %d reader(s), and keeps %d value(s)"
                    mounted.EffectCount
                    mounted.SlotCount
                    ((mounted.EffectRuns - runsBefore) / 2)
                    (readerRuns / 2)
                    ((mounted.Writes - writesBefore) / 2)

            result, counts
    ]

/// <summary>Bytes allocated to create one row's signals and start its effects.</summary>
let private mountBytes (definition: Component<Row>) effects =
    use graph = new Graph ()
    let count = 2_000
    let held = Array.zeroCreate<Mounted> count

    let mount () =
        for index in 0 .. count - 1 do
            held[index] <- new Mounted (graph, definition, Row.create graph, effects)

    let release () =
        for mounted in held do
            (mounted :> IDisposable).Dispose()

    mount ()
    release ()
    let before = GC.GetAllocatedBytesForCurrentThread ()
    mount ()
    let bytes = GC.GetAllocatedBytesForCurrentThread () - before
    release ()
    float bytes / float count

let private parts () =
    let pixelsValue = 120.
    let length = Css.px pixelsValue

    let topReader: Styled<float> =
        utility { Css.top (fun (value: float) -> Css.px value) }
        |> Utility.toStyledReactive

    let lowered = Declaration.create "--r-0123456789abcdef-1" "120px"
    let authored = Css.top length

    let six =
        [
            for index in 1..6 -> Declaration.create ("--r-0123456789abcdef-" + string index) "120px"
        ]

    [
        measure "Generated.number value \"px\" (one number to text)" 1_000_000 (fun count ->
            for _ in 1..count do
                kept <- box (Generated.number pixelsValue "px": Value<unit>))
        measure "Css.px value (the reader's own work)" 1_000_000 (fun count ->
            for _ in 1..count do
                kept <- box (Css.px pixelsValue))
        measure "Css.top length (admission and declaration of a built value)" 1_000_000 (fun count ->
            for _ in 1..count do
                kept <- box (Css.top length))
        measure "Css.top (Css.px value)" 1_000_000 (fun count ->
            for _ in 1..count do
                kept <- box (Css.top (Css.px pixelsValue)))
        measure "Style.variableDeclarations, one Css.top reader" 1_000_000 (fun count ->
            for _ in 1..count do
                kept <- box (Style.variableDeclarations pixelsValue topReader))
        measure "Css.opacity (Css.number value)" 1_000_000 (fun count ->
            for _ in 1..count do
                kept <- box (Css.opacity (Css.number 0.85)))
        measure "Css.position CssValue.Position.absolute (a keyword)" 1_000_000 (fun count ->
            for _ in 1..count do
                kept <- box (Css.position CssValue.Position.absolute))
        measure "Declaration.isCssWide \"120px\"" 1_000_000 (fun count ->
            for _ in 1..count do
                kept <- box (Declaration.isCssWide authored))
        measure "Declaration.create variable \"120px\"" 1_000_000 (fun count ->
            for _ in 1..count do
                kept <- box (Declaration.create "--r-0123456789abcdef-1" "120px"))
        measure "Declaration.render, one declaration" 1_000_000 (fun count ->
            for _ in 1..count do
                kept <- box (Declaration.render lowered))
        measure "Declaration.renderMany, six declarations" 1_000_000 (fun count ->
            for _ in 1..count do
                kept <- box (Declaration.renderMany six))
    ]

[<EntryPoint>]
let main _ =
    printfn "Ranvier.Css reader values: one render of one instance"
    printfn "runtime %s, median of %d samples per row" Runtime.InteropServices.RuntimeInformation.FrameworkDescription sampleCount
    printfn ""

    let preview =
        use graph = new Graph ()
        let row = Row.create graph
        use mounted = new Mounted (graph, readerValues, row, OneRenderEffect)
        mounted.Values

    printfn "slot values of the reader-value instance:"

    for value in preview do
        printfn "  %s" value

    let measured =
        renders "reader values" readerValues OneRenderEffect
        @ renders "style text" styleText OneRenderEffect
        @ renders "reader values, style slots isolated" readerValues StyleSlotsIsolated

    printfn ""
    printfn "renders (one operation is one signal write and the effects it runs)"
    report (measured |> List.map fst)
    printfn ""

    for result, counts in measured do
        printfn "%-64s %s" result.Name counts

    printfn ""
    printfn "mount (bytes allocated for one row's signals and effects)"
    printfn "%-64s %10.1f" "reader values" (mountBytes readerValues OneRenderEffect)
    printfn "%-64s %10.1f" "style text" (mountBytes styleText OneRenderEffect)
    printfn "%-64s %10.1f" "reader values, style slots isolated" (mountBytes readerValues StyleSlotsIsolated)
    printfn ""
    printfn "parts (one operation is one call)"
    report (parts ())
    0
