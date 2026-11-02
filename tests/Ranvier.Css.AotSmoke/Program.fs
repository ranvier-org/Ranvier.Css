module Ranvier.Css.AotSmoke.Program

open System
open System.Globalization
open System.Text
open Ranvier.Css
open Ranvier.Css.Internal
open Ranvier.Css.Spec

let emit name (value: obj) =
    let text = if isNull value then "<null>" else value.ToString ()

    Console.WriteLine (
        name
        + "="
        + Convert.ToBase64String (Encoding.UTF8.GetBytes text)
    )

[<EntryPoint>]
let main args =
    if args.Length > 0 then
        CultureInfo.CurrentCulture <- CultureInfo.GetCultureInfo args[0]

    let literal = CalculationRuntime.literal "length" "px" 12.5
    let easing = CalculationEasing.Linear [ 0.2, [ 0.4; 0.5 ]; 1.0, [] ]

    let node: GrammarRuntime.Node =
        {
            Id = 1
            Kind = 1
            Text = "a\"b"
            Target = -1
            Children = [||]
            Separators = [||]
            Minimum = 0
            Maximum = 3
            CommaSeparated = false
            Lower = Some (2.5, "px")
            Upper = None
        }

    emit "declaration" (Declaration.create "width" "12.5px")
    emit "storage" ((Generated.number 12.5 "px": Value<unit>).Storage)
    emit "custom" (CustomProperty.create "--x": CustomProperty<unit>)
    emit "dimension" NumericDimension.Length
    emit "grammar-node" node
    emit "grammar-table" (GrammarRuntime.Table.create [| node |])
    emit "grammar-value" (GrammarRuntime.token "a\"b")

    emit
        "grammar-null-fields"
        { node with
            Text = null
            Children = null
            Separators = null
        }

    emit "grammar-many-children" { node with Children = [| 0..150 |] }

    emit
        "grammar-newlines"
        { node with
            Text = "quote\"\nnewline\tend"
            Separators = [| "\n"; "\"" |]
        }

    emit
        "grammar-map-150"
        (GrammarRuntime.Table.create
            [|
                for i in 0..149 ->
                    { node with
                        Id = i
                        Text = "entry-" + string i
                    }
            |])

    for count in [ 0; 8; 9; 10; 11 ] do
        emit ("grammar-map-" + string count) (GrammarRuntime.Table.create [| for i in 1..count -> { node with Id = i } |])

    emit "grammar-null-record" (Unchecked.defaultof<GrammarRuntime.Node>)
    emit "calculation" (CalculationRuntime.binary "+" literal literal)
    emit "argument" (CalculationArgument.WeightedValue (literal, Some 0.25))
    emit "argument-null" (CalculationArgument.Value Unchecked.defaultof<Calculation>)
    emit "easing" easing
    emit "random" (CalculationRandomKey.Scoped (Some "key", true, None))
    emit "progress" (CalculationProgress.Timeline (CalculationTimeline.Named ("main", NumericDimension.Time)))
    emit "inset" (CalculationInset.Value literal)

    emit
        "stop"
        {
            Positions = [ literal ]
            Value = literal
            EasingBefore = Some easing
        }

    emit "easing-150" (CalculationEasing.Linear [ for i in 0..149 -> float i, [ float i; float i + 0.5 ] ])

    let deep =
        [ 1..2000 ]
        |> List.fold (fun value _ -> CalculationRuntime.negate value) literal

    emit "calculation-depth-2000" deep

    let dimensions =
        [
            NumericDimension.Length
            NumericDimension.Angle
            NumericDimension.Time
            NumericDimension.Frequency
            NumericDimension.Resolution
            NumericDimension.Flex
            NumericDimension.Percent
        ]

    for index, dimension in List.indexed dimensions do
        emit ("dimension-" + string index) dimension

    for index, strategy in
        List.indexed
            [
                RoundingStrategy.Nearest
                RoundingStrategy.Up
                RoundingStrategy.Down
                RoundingStrategy.ToZero
                RoundingStrategy.LineWidth
            ] do
        emit ("strategy-" + string index) strategy

    let numericType =
        {
            Exponents = Map.ofList [ NumericDimension.Length, 1; NumericDimension.Time, -2 ]
            PercentHint = Some NumericDimension.Length
        }

    emit "numeric-type" numericType

    emit
        "numeric-null-map"
        { numericType with
            Exponents = Unchecked.defaultof<Map<NumericDimension, int>>
        }

    emit
        "numeric-context"
        {
            PercentagesAllowed = true
            PercentageBasis = Some NumericDimension.Time
        }

    let numericArguments =
        [
            NumericArgument.Value numericType
            NumericArgument.NoneBound
            NumericArgument.Strategy RoundingStrategy.Down
            NumericArgument.NoClamp
            NumericArgument.RandomKey
            NumericArgument.WeightedValue (numericType, Some 0.5)
        ]

    for index, argument in List.indexed numericArguments do
        emit ("numeric-argument-" + string index) argument

    let numericErrors =
        [
            NumericError.IncompatibleHints (NumericDimension.Length, NumericDimension.Time)
            NumericError.IncompatibleTypes (numericType, numericType)
            NumericError.UnknownFunction "fn"
            NumericError.InvalidArguments ("fn", "reason")
        ]

    for index, error in List.indexed numericErrors do
        emit ("numeric-error-" + string index) error

    for index, argument in
        List.indexed
            [
                CalculationArgument.Value literal
                CalculationArgument.NoneBound
                CalculationArgument.Strategy RoundingStrategy.Up
                CalculationArgument.NoClamp
                CalculationArgument.RandomKey CalculationRandomKey.Auto
                CalculationArgument.WeightedValue (literal, None)
            ] do
        emit ("argument-" + string index) argument

    for index, value in
        List.indexed
            [
                CalculationEasing.Keyword "ease"
                CalculationEasing.CubicBezier (0.1, 0.2, 0.3, 0.4)
                CalculationEasing.Steps (3, "start")
                easing
            ] do
        emit ("easing-" + string index) value

    for index, value in
        List.indexed
            [
                CalculationRandomKey.Auto
                CalculationRandomKey.Fixed 0.5
                CalculationRandomKey.Scoped (None, false, Some "width")
            ] do
        emit ("random-" + string index) value

    for index in 0..24 do
        let text = String.replicate (index * 7) "x"

        emit
            ("grammar-" + string index)
            { node with
                Text = text
                Children = [| 0..index |]
                Separators = [| text; " " |]
            }

    for index, policy in List.indexed NumericSemantics.functionPolicies do
        emit ("function-policy-" + string index) policy

    for index, policy in List.indexed NumericSemantics.operatorPolicies do
        emit ("operator-policy-" + string index) policy

    let failure =
        try
            let incompatible =
                CalculationRuntime.binary "+" literal (CalculationRuntime.literal "time" "s" 1.0)

            CalculationRuntime.declaration "width" NumericSemantics.ordinaryContext NumericSemantics.number incompatible
            |> ignore

            None
        with :? ArgumentException as error ->
            Some error.Message

    match failure, Ranvier.Css.RuntimeApi.tryNumericValue (Ranvier.Css.Css.px 12.5) with
    | Some error, Some (value, unitName) when
        error.StartsWith ("Invalid CSS calculation: IncompatibleTypes", StringComparison.Ordinal)
        && value = 12.5
        && unitName = "px"
        ->
        emit "error" error
        System.Console.WriteLine "CSS_AOT_PROBE_PASS"
        0
    | _ ->
        System.Console.Error.WriteLine "Typed CSS literal changed"
        1
