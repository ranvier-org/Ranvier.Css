module Ranvier.Css.Spec.CalculationTests

open Ranvier.Css
open Ranvier.Css.Spec
open Expecto

let private kernelChecks () =
    let lit = CalculationRuntime.literal
    let op = CalculationRuntime.binary

    let eq expected actual =
        if expected <> actual then
            failwithf "Expected %A, got %A" expected actual

    let fails f =
        try
            f ()
            failwith "Expected ArgumentException"
        with :? System.ArgumentException ->
            ()

    let length = NumericSemantics.dimension NumericDimension.Length

    let context =
        { NumericSemantics.ordinaryContext with
            PercentageBasis = Some NumericDimension.Length
        }

    let px n =
        lit "length" "px" n

    let n x =
        lit "number" "" x

    eq "calc(2Hz)" (CalculationRuntime.render (lit "frequency" "Hz" 2.))
    eq "calc(2kHz)" (CalculationRuntime.render (lit "frequency" "kHz" 2.))
    eq "calc(2px + 3px * 4)" (CalculationRuntime.render (op "+" (px 2.) (op "*" (px 3.) (n 4.))))
    eq "calc(2px - (3px - 4px))" (CalculationRuntime.render (op "-" (px 2.) (op "-" (px 3.) (px 4.))))
    eq "calc(2px / (3px / 4px))" (CalculationRuntime.render (op "/" (px 2.) (op "/" (px 3.) (px 4.))))
    eq "calc((2px + 3px) * 4)" (CalculationRuntime.render (op "*" (op "+" (px 2.) (px 3.)) (n 4.)))
    eq "calc(-1 * (2px + 3px))" (CalculationRuntime.render (CalculationRuntime.negate (op "+" (px 2.) (px 3.))))
    let cancelled = op "/" (op "*" (px 2.) (lit "time" "s" 3.)) (lit "time" "ms" 2.)
    eq (Ok length) (CalculationRuntime.infer context cancelled)

    eq
        "width: calc(2px * 3s / 2ms);"
        (CalculationRuntime.declaration "width" context length cancelled
         |> Declaration.render)

    for production, unitName, dim in
        [
            "length", "px", NumericDimension.Length
            "angle", "deg", NumericDimension.Angle
            "time", "s", NumericDimension.Time
            "frequency", "hz", NumericDimension.Frequency
            "resolution", "dpi", NumericDimension.Resolution
            "flex", "fr", NumericDimension.Flex
        ] do
        eq (Ok (NumericSemantics.dimension dim)) (CalculationRuntime.infer context (lit production unitName 2.))

    let percentage = lit "percentage" "%" 100.
    let mixed = op "-" percentage (lit "length" "rem" 2.)
    eq (Ok (NumericSemantics.percentage (Some NumericDimension.Length))) (CalculationRuntime.infer context mixed)

    eq
        "width: calc(100% - 2rem);"
        (CalculationRuntime.declaration "width" context length mixed
         |> Declaration.render)

    eq (Ok (NumericSemantics.percentage None)) (CalculationRuntime.infer NumericSemantics.ordinaryContext percentage)
    let percentRatio = op "/" percentage percentage

    if
        CalculationRuntime.infer NumericSemantics.dimensionOnlyContext percentRatio
        |> Result.isOk
    then
        failwith "Percent cancellation lost provenance"

    let based = op "/" (lit "length-percentage" "%" 50.) (px 2.)

    CalculationRuntime.declaration "scale" context NumericSemantics.number based
    |> ignore

    fails (fun () ->
        CalculationRuntime.declaration "scale" NumericSemantics.ordinaryContext NumericSemantics.number based
        |> ignore)

    fails (fun () ->
        CalculationRuntime.declaration "width" context length (op "*" (px 2.) (px 3.))
        |> ignore)

    fails (fun () ->
        CalculationRuntime.declaration "width" context length (n 0.)
        |> ignore)

    fails (fun () ->
        CalculationRuntime.declaration "width" context length (op "+" (px 2.) (lit "angle" "deg" 2.))
        |> ignore)

    for production, unitName, value in
        [
            "length", "bogus", 1.
            "number", "px", 1.
            "length", "", 0.
            "integer", "", 1.5
            "unknown", "", 1.
            "number", "", System.Double.NaN
            "number", "", System.Double.PositiveInfinity
        ] do
        fails (fun () -> lit production unitName value |> ignore)

    fails (fun () -> op "^" (n 2.) (n 3.) |> ignore)

    eq
        "opacity: calc(2 + 3);"
        (CalculationRuntime.declaration "opacity" NumericSemantics.dimensionOnlyContext NumericSemantics.number (op "+" (n 2.) (n 3.))
         |> Declaration.render)

    eq "calc(1 / 0)" (CalculationRuntime.render (op "/" (n 1.) (n 0.)))
    let mutable current = 1.

    let reader () =
        CalculationRuntime.declaration "width" context length (op "+" (px current) (px 2.))
        |> Declaration.value

    eq "calc(1px + 2px)" (reader ())
    current <- 5.
    eq "calc(5px + 2px)" (reader ())
    "28+ kernel assertions passed"


let private functionChecks () =
    let eq expected actual =
        if expected <> actual then
            failwithf "Expected %A got %A" expected actual

    let fails f =
        try
            f ()
            failwith "Expected ArgumentException"
        with :? System.ArgumentException ->
            ()

    let context = NumericSemantics.dimensionOnlyContext
    let number = CalculationRuntime.literal "number" ""
    let length = CalculationRuntime.literal "length" "px"
    let call = CalculationRuntime.functionCall

    for name in [ "e"; "pi"; "infinity"; "-infinity"; "NaN" ] do
        let constant = CalculationRuntime.constant name
        eq ("calc(" + name + ")") (CalculationRuntime.render constant)
        eq (Ok NumericSemantics.number) (CalculationRuntime.infer context constant)

    for policy in
        NumericSemantics.functionPolicies
        |> List.filter (fun policy -> policy.Name <> "calc-interpolate") do
        let arguments =
            match policy.Name with
            | "sibling-count"
            | "sibling-index" -> []
            | "progress" -> [ number 2.; number 0.; number 1. ]
            | "random" -> [ number 1.; number 2. ]
            | "clamp" -> [ number 1.; number 2.; number 3. ]
            | "pow"
            | "atan2"
            | "mod"
            | "rem" -> [ number 2.; number 3. ]
            | _ -> [ number 2. ]

        if
            CalculationRuntime.infer context (call policy.Name arguments)
            |> Result.isError
        then
            failwithf "Valid function call rejected: %s" policy.Name

    eq "min(2px, 3px)" (CalculationRuntime.render (call "min" [ length 2.; length 3. ]))
    eq "calc(min(2px, 3px) + 4px)" (CalculationRuntime.render (CalculationRuntime.binary "+" (call "min" [ length 2.; length 3. ]) (length 4.)))

    let clamp =
        CalculationRuntime.functionArguments
            "clamp"
            [
                CalculationArgument.NoneBound
                CalculationArgument.Value (length 2.)
                CalculationArgument.NoneBound
            ]

    eq "clamp(none, 2px, none)" (CalculationRuntime.render clamp)
    eq (Ok (NumericSemantics.dimension NumericDimension.Length)) (CalculationRuntime.infer context clamp)

    let round =
        CalculationRuntime.functionArguments
            "round"
            [
                CalculationArgument.Strategy RoundingStrategy.Up
                CalculationArgument.Value (length 3.)
                CalculationArgument.Value (length 2.)
            ]

    eq "round(up, 3px, 2px)" (CalculationRuntime.render round)
    eq (Ok (NumericSemantics.dimension NumericDimension.Length)) (CalculationRuntime.infer context round)
    let bad = call "min" [ length 2.; number 3. ]

    if
        CalculationRuntime.infer context bad
        |> Result.isOk
    then
        failwith "Mixed min dimensions accepted"

    fails (fun () -> CalculationRuntime.constant "size" |> ignore)
    fails (fun () -> call "invented" [ number 2. ] |> ignore)
    fails (fun () -> call "pow" [ number 2. ] |> ignore)

    fails (fun () ->
        CalculationRuntime.functionArguments "min" [ CalculationArgument.NoneBound ]
        |> ignore)

    "Function checks passed"


let private extendedChecks () =
    let eq expected actual =
        if expected <> actual then
            failwithf "Expected %A got %A" expected actual

    let fails f =
        try
            f ()
            failwith "Expected ArgumentException"
        with :? System.ArgumentException ->
            ()

    let context = NumericSemantics.dimensionOnlyContext
    let number = CalculationRuntime.literal "number" ""
    let px = CalculationRuntime.literal "length" "px"
    let pct = CalculationRuntime.literal "percentage" "%"
    let args = CalculationRuntime.functionArguments

    let mixed =
        args
            "calc-mix"
            [
                CalculationArgument.WeightedValue (px 2., Some 25.)
                CalculationArgument.WeightedValue (px 3., None)
            ]

    eq "calc-mix(2px 25%, 3px)" (CalculationRuntime.render mixed)
    eq (Ok (NumericSemantics.dimension NumericDimension.Length)) (CalculationRuntime.infer context mixed)

    let progress =
        args
            "progress"
            [
                CalculationArgument.NoClamp
                CalculationArgument.Value (px 20.)
                CalculationArgument.Value (px 0.)
                CalculationArgument.Value (px 10.)
            ]

    eq "progress(no-clamp 20px, 0px, 10px)" (CalculationRuntime.render progress)
    eq (Ok NumericSemantics.number) (CalculationRuntime.infer context progress)

    let random =
        args
            "random"
            [
                CalculationArgument.RandomKey (CalculationRandomKey.Fixed 0.5)
                CalculationArgument.Value (px 10.)
                CalculationArgument.Value (px 20.)
                CalculationArgument.Value (px 2.)
            ]

    eq "random(fixed 0.5, 10px, 20px, 2px)" (CalculationRuntime.render random)
    eq (Ok (NumericSemantics.dimension NumericDimension.Length)) (CalculationRuntime.infer context random)

    for name in [ "sibling-count"; "sibling-index" ] do
        let value = CalculationRuntime.functionCall name []
        eq (name + "()") (CalculationRuntime.render value)
        eq (Ok NumericSemantics.number) (CalculationRuntime.infer context value)

    fails (fun () ->
        args
            "random"
            [
                CalculationArgument.RandomKey (CalculationRandomKey.Fixed 1.1)
                CalculationArgument.Value (px 1.)
                CalculationArgument.Value (px 2.)
            ]
        |> ignore)

    fails (fun () ->
        args "calc-mix" [ CalculationArgument.WeightedValue (px 1., Some -1.) ]
        |> ignore)

    let stop positions value : CalculationInterpolationStop =
        {
            Positions = positions
            Value = value
            EasingBefore = None
        }

    let stops = [ stop [ number 0. ] (px 10.); stop [ number 1. ] (px 20.) ]

    let interpolation =
        CalculationRuntime.interpolate (CalculationProgress.Value (pct 50.)) None None stops

    eq "calc-interpolate(50%, 0: 10px, 1: 20px)" (CalculationRuntime.render interpolation)
    eq (Ok (NumericSemantics.dimension NumericDimension.Length)) (CalculationRuntime.infer context interpolation)

    let absolute =
        CalculationRuntime.interpolate (CalculationProgress.Value (px 50.)) None None stops

    if
        CalculationRuntime.infer context absolute
        |> Result.isOk
    then
        failwith "Absolute progress without absolute stop accepted"

    let badPositions =
        CalculationRuntime.interpolate
            (CalculationProgress.Value (px 50.))
            None
            None
            [
                stop [ px 0. ] (px 10.)
                stop [ CalculationRuntime.literal "time" "s" 2. ] (px 20.)
            ]

    if
        CalculationRuntime.infer context badPositions
        |> Result.isOk
    then
        failwith "Incompatible absolute positions accepted"

    let timeline =
        CalculationRuntime.interpolate
            (CalculationProgress.Timeline (CalculationTimeline.View (Some "block", [ CalculationInset.Auto; CalculationInset.Value (pct 10.) ])))
            (Some (CalculationEasing.Keyword "ease"))
            None
            stops

    eq "calc-interpolate(view(block auto 10%) by ease, 0: 10px, 1: 20px)" (CalculationRuntime.render timeline)
    eq (Ok (NumericSemantics.dimension NumericDimension.Length)) (CalculationRuntime.infer context timeline)

    let scopedOutput =
        CalculationRuntime.interpolate (CalculationProgress.Value (number 0.5)) None None [ stop [ number 0. ] (pct 10.) ]

    if
        CalculationRuntime.infer context scopedOutput
        |> Result.isOk
    then
        failwith "Output percentage context ignored"

    fails (fun () ->
        CalculationRuntime.interpolate (CalculationProgress.Value (number 0.5)) (Some (CalculationEasing.CubicBezier (2., 0., 0., 1.))) None stops
        |> ignore)

    "Extended checks passed"


let private intrinsicChecks () =
    let eq expected actual =
        if expected <> actual then
            failwithf "Expected %A got %A" expected actual

    let fails f =
        try
            f ()
            failwith "Expected ArgumentException"
        with :? System.ArgumentException ->
            ()

    let px = CalculationRuntime.literal "length" "px"
    let n = CalculationRuntime.literal "number" ""
    let make = CalculationRuntime.intrinsic
    let size = CalculationRuntime.size

    let declare allowed value =
        CalculationRuntime.declarationIntrinsic "width" allowed value
        |> Declaration.render

    let auto =
        make (IntrinsicBasis.Keyword "auto") (CalculationRuntime.binary "+" size (px 10.))

    eq "width: calc-size(auto, size + 10px);" (declare [ "auto" ] auto)
    eq [ "auto" ] (CalculationRuntime.usedIntrinsicKeywords auto)
    fails (fun () -> declare [ "min-content" ] auto |> ignore)

    for keyword in [ "none"; "NONE"; "None" ] do
        fails (fun () ->
            declare [ keyword; "none" ] (make (IntrinsicBasis.Keyword keyword) size)
            |> ignore)

    fails (fun () ->
        declare [] (make IntrinsicBasis.Any size)
        |> ignore)

    eq "width: calc-size(any, 2px);" (declare [] (make IntrinsicBasis.Any (px 2.)))
    let nested = make (IntrinsicBasis.Nested (make IntrinsicBasis.Any (px 2.))) size
    eq "width: calc-size(calc-size(any, 2px), size);" (declare [] nested)
    eq "width: calc-size(auto, 0px);" (declare [ "auto" ] (make (IntrinsicBasis.Keyword "auto") (n 0.)))

    fails (fun () ->
        declare [ "auto" ] (make (IntrinsicBasis.Keyword "auto") (n 1.))
        |> ignore)

    if
        CalculationRuntime.infer NumericSemantics.ordinaryContext size
        |> Result.isOk
    then
        failwith "Size escaped intrinsic scope"

    let fit = make (IntrinsicBasis.FitContent (px 100.)) size
    eq [ "fit-content()" ] (CalculationRuntime.usedIntrinsicKeywords fit)
    eq "width: calc-size(fit-content(100px), size);" (declare [ "fit-content()" ] fit)
    fails (fun () -> declare [] fit |> ignore)
    let pct = CalculationRuntime.literal "percentage" "%" 50.
    eq "width: calc-size(50%, size);" (declare [] (make (IntrinsicBasis.Value pct) size))
    "Intrinsic checks passed"


let private referenceChecks () =
    let eq expected actual =
        if expected <> actual then
            failwithf "Expected %A got %A" expected actual

    let fails f =
        try
            f ()
            failwith "Expected ArgumentException"
        with :? System.ArgumentException ->
            ()

    let px = CalculationRuntime.literal "length" "px"
    let ref = CalculationRuntime.reference
    let ordinary = NumericSemantics.dimensionOnlyContext

    let mixed =
        { NumericSemantics.ordinaryContext with
            PercentageBasis = Some NumericDimension.Length
        }

    let length = NumericSemantics.dimension NumericDimension.Length
    let gap = ref "length" "--gap" None
    eq (Ok length) (CalculationRuntime.infer ordinary gap)

    eq
        "width: calc(var(--gap) + 1px);"
        (CalculationRuntime.declaration "width" ordinary length (CalculationRuntime.binary "+" gap (px 1.))
         |> Declaration.render)

    let fallback =
        ref "length" "--gap" (Some (CalculationRuntime.binary "+" (px 2.) (px 3.)))

    eq "calc(var(--gap, calc(2px + 3px)))" (CalculationRuntime.render fallback)
    eq (Ok length) (CalculationRuntime.infer ordinary fallback)
    let nested = ref "length" "--outer" (Some (ref "length" "--inner" (Some (px 2.))))
    eq "calc(var(--outer, var(--inner, 2px)))" (CalculationRuntime.render nested)
    eq (Ok NumericSemantics.number) (CalculationRuntime.infer ordinary (ref "integer" "--count" (Some (CalculationRuntime.literal "number" "" 1.5))))

    let invalidFallback =
        ref "length" "--gap" (Some (CalculationRuntime.literal "angle" "deg" 3.))

    if
        CalculationRuntime.infer ordinary invalidFallback
        |> Result.isOk
    then
        failwith "Incompatible reference fallback accepted"

    let percentage = ref "percentage" "--p" None

    if
        CalculationRuntime.infer ordinary percentage
        |> Result.isOk
    then
        failwith "Percentage reference admitted in percentage-free context"

    eq (Ok (NumericSemantics.percentage (Some NumericDimension.Length))) (CalculationRuntime.infer mixed percentage)

    let based =
        ref "length-percentage" "--p" (Some (CalculationRuntime.literal "percentage" "%" 50.))

    eq (Ok (NumericSemantics.percentage (Some NumericDimension.Length))) (CalculationRuntime.infer mixed based)
    fails (fun () -> ref "length" "gap" None |> ignore)
    fails (fun () -> ref "length" "--gap); color:red" None |> ignore)
    fails (fun () -> ref "color" "--x" None |> ignore)
    fails (fun () -> ref null "--x" None |> ignore)
    "Reference checks passed"


let private absoluteRangeChecks () =
    let equal expected actual =
        if expected <> actual then
            failwithf "Expected %A got %A" expected actual

    let fails f =
        try
            f ()
            failwith "Expected ArgumentException"
        with :? System.ArgumentException ->
            ()

    let check value sourceUnit boundary boundaryUnit =
        let input: Value<obj> = Generated.number value sourceUnit

        let bounded: Value<obj> =
            Generated.range (Some (boundary, boundaryUnit)) (Some (boundary, boundaryUnit)) input

        equal (Value.render input) (Value.render bounded)

        fails (fun () ->
            Generated.range None (Some (boundary - 0.001, boundaryUnit)) input
            |> ignore)

        fails (fun () ->
            Generated.range (Some (boundary + 0.001, boundaryUnit)) None input
            |> ignore)

    check 101.6 "q" 96. "px"
    check 96. "PX" 101.6 "Q"
    check 1. "kHz" 1000. "Hz"
    check 1000. "HZ" 1. "KHZ"
    check 1. "KHZ" 1. "khz"
    check 96. "dpi" 1. "dppx"
    check 1. "x" 96. "DPI"
    check 1. "dpcm" 2.54 "dpi"
    check 1. "DPCM" 1. "dpcm"

    for sourceUnit, targetUnit in [ "rem", "px"; "cqw", "px"; "Hz", "dpi"; "fr", "px" ] do
        fails (fun () ->
            let input: Value<obj> = Generated.number 1. sourceUnit in

            Generated.range None (Some (1., targetUnit)) input
            |> ignore)

    let computed: Value<obj> = Generated.value "calc(1q + 2px)"

    equal
        "calc(1q + 2px)"
        (Generated.range None (Some (0., "px")) computed
         |> Value.render)

    "Absolute range checks passed"


let private stretchChecks () =
    let equal expected actual =
        if expected <> actual then
            failwithf "Expected %A got %A" expected actual

    let fails f =
        try
            f ()
            failwith "Expected ArgumentException"
        with :? System.ArgumentException ->
            ()

    let px = CalculationRuntime.literal "length" "px"

    let stretch value =
        CalculationRuntime.intrinsic (IntrinsicBasis.Stretch value) CalculationRuntime.size

    let declare allowed value =
        CalculationRuntime.declarationIntrinsic "width" allowed value
        |> Declaration.render

    let value = stretch (px 100.)
    equal [ "stretch()" ] (CalculationRuntime.usedIntrinsicKeywords value)
    equal "width: calc-size(stretch(100px), size);" (declare [ "stretch()" ] value)
    fails (fun () -> declare [ "stretch" ] value |> ignore)
    fails (fun () -> declare [] value |> ignore)

    fails (fun () ->
        declare [ "stretch()" ] (stretch (CalculationRuntime.literal "angle" "deg" 1.))
        |> ignore)

    fails (fun () ->
        declare [ "stretch()" ] (stretch CalculationRuntime.size)
        |> ignore)

    equal "width: calc-size(stretch(0px), size);" (declare [ "stretch()" ] (stretch (CalculationRuntime.literal "number" "" 0.)))
    let percentage = stretch (CalculationRuntime.literal "percentage" "%" 50.)
    equal "width: calc-size(stretch(50%), size);" (declare [ "stretch()" ] percentage)
    "Stretch basis checks passed"


let private nestedCalcChecks () =
    let equal expected actual =
        if expected <> actual then
            failwithf "Expected %A got %A" expected actual

    let fails f =
        try
            f ()
            failwith "Expected ArgumentException"
        with :? System.ArgumentException ->
            ()

    let px = CalculationRuntime.literal "length" "px"
    let binary = CalculationRuntime.binary

    let calc value =
        CalculationRuntime.functionCall "calc" [ value ]

    let context = NumericSemantics.dimensionOnlyContext
    let length = NumericSemantics.dimension NumericDimension.Length
    let compound = binary "*" (px 2.) (px 3.)
    let direct = binary "/" compound (px 1.)
    let nested = binary "/" (calc compound) (px 1.)
    equal (Ok length) (CalculationRuntime.infer context direct)
    equal (CalculationRuntime.infer context direct) (CalculationRuntime.infer context nested)

    equal
        "width: calc(calc(2px * 3px) / 1px);"
        (CalculationRuntime.declaration "width" context length nested
         |> Declaration.render)

    fails (fun () ->
        CalculationRuntime.declaration "width" context length (calc compound)
        |> ignore)

    fails (fun () ->
        CalculationRuntime.declaration "width" context length compound
        |> ignore)

    let atan2 = CalculationRuntime.functionCall "atan2" [ compound; compound ]

    if
        CalculationRuntime.infer context atan2
        |> Result.isOk
    then
        failwith "Atan2 primitive argument validation was weakened"

    let scoped = binary "/" (calc (binary "*" CalculationRuntime.size (px 2.))) (px 1.)
    let intrinsic = CalculationRuntime.intrinsic (IntrinsicBasis.Keyword "auto") scoped

    equal
        "width: calc-size(auto, calc(size * 2px) / 1px);"
        (CalculationRuntime.declarationIntrinsic "width" [ "auto" ] intrinsic
         |> Declaration.render)

    "Nested calc checks passed"


let tests =
    testList
        "portable calculations"
        [
            testCase "nested calc preserves grouping typing until the completed numeric boundary" (fun () -> nestedCalcChecks () |> ignore)
            testCase "stretch intrinsic basis requires function admission and length context" (fun () -> stretchChecks () |> ignore)
            testCase "absolute unit range boundaries preserve suffixes and reject unresolved conversions" (fun () -> absoluteRangeChecks () |> ignore)
            testCase "typed numeric custom-property references and compatible fallbacks" (fun () -> referenceChecks () |> ignore)
            testCase "intrinsic sizing scope and destination admission" (fun () -> intrinsicChecks () |> ignore)
            testCase "Values 5 structured numeric functions and interpolation contexts" (fun () -> extendedChecks () |> ignore)
            testCase "numeric math functions, constants and positional keywords" (fun () -> functionChecks () |> ignore)
            testCase "grouping, units, contextual percentages, cancellation and deferred values" (fun () -> kernelChecks () |> ignore)
        ]
