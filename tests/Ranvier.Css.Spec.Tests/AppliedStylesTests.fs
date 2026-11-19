module Ranvier.Css.Spec.AppliedStylesTests

open System
open System.Threading
open Expecto
open Ranvier
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View
open Ranvier.View.Dsl

let private declaration (text: string) =
    Declaration.create "--applied-styles-test" text

/// A style whose rules, and so whose class, exist once in the process.
let private fresh () : Styled<unit> =
    Style.create [ Style.declarations [ declaration (Guid.NewGuid().ToString "N") ] ]

let private apply (style: Styled<unit>) =
    let parts: Part<obj, unit> array = CssView.parts style
    ignore parts

/// The entries of the log from position <c>start</c> that belong to <c>styles</c>. Other tests apply styles in the same process.
let private appliedSince start (styles: Styled<unit> list) =
    let wanted =
        styles
        |> List.map _.Definition.ClassName
        |> Set.ofList

    AppliedStyles.after start
    |> List.filter (fun definition -> wanted.Contains definition.ClassName)

let private names (definitions: StyleDefinition list) =
    definitions |> List.map _.ClassName

let private onThreads (work: (unit -> unit) list) =
    let failures = Collections.Concurrent.ConcurrentQueue<exn>()

    let threads =
        work
        |> List.map (fun body ->
            Thread (
                ThreadStart (fun () ->
                    try
                        body ()
                    with failure ->
                        failures.Enqueue failure)
            ))

    for thread in threads do
        thread.Start ()

    for thread in threads do
        thread.Join ()

    Expect.isEmpty (List.ofSeq failures) "No thread failed"

/// <summary>The log of definitions <c>CssView</c> applies: what is recorded, its order, and reading by position.</summary>
let tests =
    testList
        "Applied styles"
        [
            testCase "a style applied through CssView.parts is in the log once"
            <| fun _ ->
                let style = fresh ()
                let start = AppliedStyles.count ()
                apply style
                apply style
                Expect.equal (appliedSince start [ style ]) [ style.Definition ] "One entry holding the definition"
                Expect.isGreaterThan (AppliedStyles.count ()) start "The count grew"

            testCase "a style that is created and left unapplied stays out of the log"
            <| fun _ ->
                let style = fresh ()
                Expect.isEmpty (appliedSince 0 [ style ]) "Creation alone records nothing"

            testCase "two styles with equal rules are one entry"
            <| fun _ ->
                let text = Guid.NewGuid().ToString "N"
                let first: Styled<unit> = Style.create [ Style.declarations [ declaration text ] ]
                let second: Styled<unit> = Style.create [ Style.declarations [ declaration text ] ]
                let start = AppliedStyles.count ()
                apply first
                apply second
                Expect.equal first.Definition.ClassName second.Definition.ClassName "Equal rules share a class"
                Expect.equal (appliedSince start [ first ] |> names) [ first.Definition.ClassName ] "One entry for the class"

            testCase "styles applied in one call are recorded in list order, and plain class names are left out"
            <| fun _ ->
                let first = fresh ()
                let second = fresh ()
                let start = AppliedStyles.count ()

                let parts: Part<obj, unit> array =
                    CssView.partsWithInline [ "manual" ] [ second; first ] (fun _ -> [])

                ignore parts

                Expect.equal (appliedSince start [ first; second ] |> names) [ second.Definition.ClassName; first.Definition.ClassName ] "List order"

                Expect.isFalse
                    (AppliedStyles.after start
                     |> List.exists (fun definition -> definition.ClassName = "manual"))
                    "A class name with no definition is absent"

            testCase "the log keeps the order of first application"
            <| fun _ ->
                let first = fresh ()
                let second = fresh ()
                let start = AppliedStyles.count ()
                apply first
                apply second
                apply first

                Expect.equal
                    (appliedSince start [ first; second ] |> names)
                    [ first.Definition.ClassName; second.Definition.ClassName ]
                    "First application decides the position"

            testCase "CssView.className records the definition it applies"
            <| fun _ ->
                let style = fresh ()
                let start = AppliedStyles.count ()

                let definition =
                    Component.define (fun _ (value: unit) -> value) (fun t ->
                        t.element (HtmlTag "div") [| CssView.className t.Attrs style.Definition |] [||])

                let html = Server.render Server.Options.defaults definition ()
                Expect.stringContains html style.Definition.ClassName "The class is on the element"
                Expect.equal (appliedSince start [ style ]) [ style.Definition ] "One entry holding the definition"

            testCase "a styles application records the class it puts on the element"
            <| fun _ ->
                let shared = utility { declaration (Guid.NewGuid().ToString "N") }
                let start = AppliedStyles.count ()

                let definition =
                    Component.define (fun _ (value: int) -> value) (fun _ -> div (styles { shared }) { "content" })

                let html = Server.render Server.Options.defaults definition 1
                Expect.stringContains html shared.Definition.ClassName "The class is on the element"

                Expect.equal
                    (AppliedStyles.after start
                     |> List.filter (fun entry -> entry.ClassName = shared.Definition.ClassName))
                    [ shared.Definition ]
                    "One entry holding the rules of the class"

            testCase "reading from a position returns the rest of the log"
            <| fun _ ->
                for style in [ fresh (); fresh (); fresh () ] do
                    apply style

                let whole = AppliedStyles.after 0

                for start in [ 0; 1; whole.Length - 1; whole.Length ] do
                    let rest = AppliedStyles.after start
                    let expected = List.skip start whole
                    Expect.equal (List.truncate expected.Length rest) expected $"The entries from position {start}"

            testCase "a position outside the log is rejected"
            <| fun _ ->
                Expect.throwsT<ArgumentOutOfRangeException> (fun () -> AppliedStyles.after -1 |> ignore) "A negative position"

                Expect.throwsT<ArgumentOutOfRangeException> (fun () -> AppliedStyles.after Int32.MaxValue |> ignore) "A position past the end"

            testCase "threads applying the same and different styles record each class once"
            <| fun _ ->
                let shared = List.init 200 (fun _ -> fresh ())
                let own = List.init 8 (fun _ -> List.init 50 (fun _ -> fresh ()))
                let start = AppliedStyles.count ()

                onThreads (
                    own
                    |> List.mapi (fun thread mine () ->
                        for style in
                            (if thread % 2 = 0 then shared else List.rev shared)
                            @ mine do
                            apply style)
                )

                let all = shared @ List.concat own
                let recorded = appliedSince start all |> names
                Expect.equal recorded.Length all.Length "Every class is recorded"
                Expect.equal (List.distinct recorded).Length all.Length "Each class is recorded once"

            testCase "a reader taking batches by position while threads apply receives the log"
            <| fun _ ->
                let styles = List.init 8 (fun _ -> List.init 150 (fun _ -> fresh ()))
                let start = AppliedStyles.count ()
                let read = ResizeArray<StyleDefinition>()
                let finished = new ManualResetEventSlim false

                let reader () =
                    let taken = ref start

                    let take () =
                        let batch = AppliedStyles.after taken.Value
                        taken.Value <- taken.Value + batch.Length
                        read.AddRange batch

                    while not finished.IsSet do
                        take ()

                    take ()

                let readerThread = Thread (ThreadStart reader)
                readerThread.Start ()

                onThreads (
                    styles
                    |> List.map (fun mine () ->
                        for style in mine do
                            apply style)
                )

                finished.Set ()
                readerThread.Join ()
                Expect.equal (List.ofSeq read) (AppliedStyles.after start) "The batches, joined, are the log from the start position"
                Expect.equal (appliedSince start (List.concat styles)).Length 1200 "Every class is in it"
        ]
