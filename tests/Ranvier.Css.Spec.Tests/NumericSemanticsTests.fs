module Ranvier.Css.Spec.NumericSemanticsTests

open Ranvier.Css.Spec
open Expecto

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; actual %A" expected actual

let private success =
    function
    | Ok result -> result
    | Error error -> failwithf "Unexpected numeric typing failure: %A" error

let private fails result =
    equal true (Result.isError result)

let private length = NumericSemantics.dimension NumericDimension.Length
let private angle = NumericSemantics.dimension NumericDimension.Angle
let private time = NumericSemantics.dimension NumericDimension.Time
let private number = NumericSemantics.number
let private ordinary = NumericSemantics.ordinaryContext

let private mixed =
    { ordinary with
        PercentageBasis = Some NumericDimension.Length
    }

let private percent = NumericSemantics.percentage None

let private lengthPercent =
    NumericSemantics.percentage (Some NumericDimension.Length)

let private value = NumericArgument.Value

let private call context name arguments =
    NumericSemantics.inferFunction context name arguments

let private cases =
    [
        "dimension products cancel through compound intermediates",
        fun () ->
            let square = NumericSemantics.multiply length length |> success
            equal (Map.ofList [ NumericDimension.Length, 2 ]) square.Exponents
            equal length (NumericSemantics.divide square length |> success)
            equal number (NumericSemantics.divide length length |> success)

        "percentage hints survive cancellation and inversion",
        fun () ->
            let cancelled =
                NumericSemantics.divide lengthPercent lengthPercent
                |> success

            equal Map.empty cancelled.Exponents
            equal (Some NumericDimension.Length) cancelled.PercentHint
            equal cancelled (NumericSemantics.invert cancelled)
            equal false (NumericSemantics.matchesNumber NumericSemantics.dimensionOnlyContext cancelled)
            equal true (NumericSemantics.matchesNumber mixed cancelled)

        "percentage terminals preserve ordinary and relative origins",
        fun () ->
            equal (Some NumericDimension.Percent) percent.PercentHint
            equal (Map.ofList [ NumericDimension.Percent, 1 ]) percent.Exponents
            equal length.Exponents lengthPercent.Exponents
            equal (Some NumericDimension.Length) lengthPercent.PercentHint

        "compatible contextual sums retain a percentage hint",
        fun () ->
            equal
                lengthPercent
                (NumericSemantics.add length lengthPercent
                 |> success)

            equal
                lengthPercent
                (NumericSemantics.add lengthPercent length
                 |> success)

        "ordinary percentages do not authorize mixed dimensional addition",
        fun () ->
            fails (NumericSemantics.add percent length)
            fails (NumericSemantics.add percent number)
            fails (NumericSemantics.add length number)
            fails (NumericSemantics.add length time)

        "raw Typed OM percentages provisionally acquire a dimension hint",
        fun () ->
            let rawPercent = NumericSemantics.dimension NumericDimension.Percent
            equal lengthPercent (NumericSemantics.add length rawPercent |> success)

        "conflicting percentage hints fail even with cancelled powers",
        fun () ->
            let lengthNumber =
                NumericSemantics.divide lengthPercent lengthPercent
                |> success

            let anglePercent = NumericSemantics.percentage (Some NumericDimension.Angle)

            let angleNumber =
                NumericSemantics.divide anglePercent anglePercent
                |> success

            fails (NumericSemantics.add lengthNumber angleNumber)
            fails (NumericSemantics.multiply lengthNumber angleNumber)

        "zero powers compare as absent",
        fun () ->
            let zero =
                { number with
                    Exponents = Map.ofList [ NumericDimension.Time, 0 ]
                }

            equal number (NumericSemantics.add zero number |> success)
            equal length (NumericSemantics.multiply zero length |> success)

        "ordinary percentage scaling retains percentage type",
        fun () ->
            equal percent (NumericSemantics.divide percent number |> success)
            equal true (NumericSemantics.matchesDimension ordinary NumericDimension.Percent percent)

        "matching rejects compound and incompatible hinted final results",
        fun () ->
            let square = NumericSemantics.multiply length length |> success
            equal false (NumericSemantics.matches mixed length square)
            equal false (NumericSemantics.matches ordinary length lengthPercent)
            equal true (NumericSemantics.matches mixed length lengthPercent)
            equal false (NumericSemantics.matches NumericSemantics.dimensionOnlyContext length lengthPercent)

        "dimension results require a mixed basis for retained percentage hints",
        fun () ->
            let ratio = NumericSemantics.divide percent percent |> success
            let hintedLength = NumericSemantics.multiply length ratio |> success
            equal true (NumericSemantics.matchesNumber ordinary ratio)
            equal false (NumericSemantics.matchesDimension ordinary NumericDimension.Length hintedLength)
            fails (call ordinary "calc" [ value hintedLength ])
            fails (call ordinary "atan2" [ value percent; value percent ])

            let mixedRatio =
                NumericSemantics.divide lengthPercent lengthPercent
                |> success

            let mixedLength =
                NumericSemantics.multiply length mixedRatio
                |> success

            equal true (NumericSemantics.matchesDimension mixed NumericDimension.Length mixedLength)

        "calculation boundaries require primitive final types",
        fun () ->
            let square = NumericSemantics.multiply length length |> success
            fails (call ordinary "calc" [ value square ])
            equal length (call ordinary "calc" [ value length ] |> success)
            equal length (call ordinary "abs" [ value length ] |> success)

        "sign accepts typed compound calculations and preserves their percentage hint",
        fun () ->
            let square = NumericSemantics.multiply length length |> success
            equal number (call ordinary "sign" [ value square ] |> success)

            let hintedSquare =
                NumericSemantics.multiply lengthPercent lengthPercent
                |> success

            equal
                { number with
                    PercentHint = Some NumericDimension.Length
                }
                (call mixed "sign" [ value hintedSquare ]
                 |> success)

            fails (call ordinary "sign" [ value hintedSquare ])
            fails (call NumericSemantics.dimensionOnlyContext "sign" [ value hintedSquare ])

        "comparison and hypot functions require compatible nonempty inputs",
        fun () ->
            for name in [ "min"; "max"; "hypot" ] do
                equal
                    length
                    (call ordinary name [ value length; value length ]
                     |> success)

                equal
                    lengthPercent
                    (call mixed name [ value length; value lengthPercent ]
                     |> success)

                fails (call ordinary name [])
                fails (call ordinary name [ value length; value time ])

        "clamp accepts none exclusively at its bounds",
        fun () ->
            let none = NumericArgument.NoneBound

            equal
                length
                (call ordinary "clamp" [ none; value length; none ]
                 |> success)

            equal
                length
                (call ordinary "clamp" [ value length; value length; none ]
                 |> success)

            fails (call ordinary "clamp" [ value length; none; value length ])
            fails (call ordinary "clamp" [ none; value length ])
            fails (call ordinary "min" [ none ])

        "round permits omitted steps for numbers",
        fun () ->
            equal number (call ordinary "round" [ value number ] |> success)

            equal
                number
                (call ordinary "round" [ NumericArgument.Strategy RoundingStrategy.Up; value number ]
                 |> success)

            fails (call ordinary "round" [ value length ])

            equal
                length
                (call ordinary "round" [ value length; value length ]
                 |> success)

            fails (call ordinary "round" [ value length; value time ])

        "round line-width requires length and permits an omitted step",
        fun () ->
            let strategy = NumericArgument.Strategy RoundingStrategy.LineWidth

            equal
                length
                (call ordinary "round" [ strategy; value length ]
                 |> success)

            equal
                length
                (call ordinary "round" [ strategy; value length; value length ]
                 |> success)

            fails (call ordinary "round" [ strategy; value number ])
            fails (call ordinary "round" [ strategy; value time; value time ])

        "strategies are accepted exclusively in round's first slot",
        fun () ->
            let strategy = NumericArgument.Strategy RoundingStrategy.Down
            fails (call ordinary "round" [ value number; strategy ])
            fails (call ordinary "round" [ strategy ])
            fails (call ordinary "round" [ strategy; strategy; value number ])
            fails (call ordinary "abs" [ strategy ])

        "mod and rem require exactly two consistent numeric arguments",
        fun () ->
            for name in [ "mod"; "rem" ] do
                equal
                    angle
                    (call ordinary name [ value angle; value angle ]
                     |> success)

                fails (call ordinary name [ value angle ])
                fails (call ordinary name [ value angle; value time ])
                fails (call ordinary name [ value angle; value angle; value angle ])

        "trigonometry accepts numbers and angles",
        fun () ->
            for name in [ "sin"; "cos"; "tan" ] do
                equal number (call ordinary name [ value angle ] |> success)
                equal number (call ordinary name [ value number ] |> success)
                fails (call ordinary name [ value length ])
                fails (call ordinary name [ value percent ])

        "inverse trigonometry requires numbers and returns angles",
        fun () ->
            for name in [ "asin"; "acos"; "atan" ] do
                equal angle (call ordinary name [ value number ] |> success)
                fails (call ordinary name [ value angle ])
                fails (call ordinary name [])

        "atan2 returns an angle with the consistent argument hint",
        fun () ->
            equal
                angle
                (call ordinary "atan2" [ value length; value length ]
                 |> success)

            equal
                { angle with
                    PercentHint = Some NumericDimension.Length
                }
                (call mixed "atan2" [ value lengthPercent; value length ]
                 |> success)

            fails (call ordinary "atan2" [ value length; value time ])

        "exponential functions require numbers and exact supported arities",
        fun () ->
            for name, arguments in
                [
                    "pow", [ value number; value number ]
                    "sqrt", [ value number ]
                    "log", [ value number ]
                    "exp", [ value number ]
                ] do
                equal number (call ordinary name arguments |> success)
                fails (call ordinary name (value length :: List.tail arguments))
                fails (call ordinary name [])

            equal
                number
                (call ordinary "log" [ value number; value number ]
                 |> success)

            fails (call ordinary "pow" [ value number ])
            fails (call ordinary "sqrt" [ value number; value number ])

        "number-returning functions retain cancelled percentage provenance",
        fun () ->
            let hinted =
                NumericSemantics.divide lengthPercent lengthPercent
                |> success

            for name in [ "sqrt"; "exp"; "log"; "sin"; "sign" ] do
                equal hinted (call mixed name [ value hinted ] |> success)

            equal
                hinted
                (call mixed "pow" [ value hinted; value number ]
                 |> success)

            equal
                hinted
                (call mixed "sign" [ value lengthPercent ]
                 |> success)

        "function names are case insensitive and unknown functions fail explicitly",
        fun () ->
            equal length (call ordinary "CaLc" [ value length ] |> success)
            equal (Error (NumericError.UnknownFunction "future-math")) (call ordinary "future-math" [ value number ])

        "published functions enforce policy arity and retain the complete numeric surface",
        fun () ->
            for policy in NumericSemantics.functionPolicies do
                if policy.MinimumArguments > 0 then
                    fails (call ordinary policy.Name [])

                match policy.MaximumArguments with
                | Some maximum -> fails (call ordinary policy.Name (List.replicate (maximum + 1) (value number)))
                | None -> ()

            let expected =
                set
                    [
                        "calc"
                        "abs"
                        "sign"
                        "min"
                        "max"
                        "hypot"
                        "clamp"
                        "round"
                        "mod"
                        "rem"
                        "sin"
                        "cos"
                        "tan"
                        "asin"
                        "acos"
                        "atan"
                        "atan2"
                        "pow"
                        "sqrt"
                        "exp"
                        "log"
                        "progress"
                        "random"
                        "calc-mix"
                        "sibling-count"
                        "sibling-index"
                        "calc-interpolate"
                    ]

            equal
                expected
                (NumericSemantics.functionPolicies
                 |> List.map _.Name
                 |> Set.ofList)

            equal number (call ordinary "sibling-count" [] |> success)
            equal number (call ordinary "sibling-index" [] |> success)
            equal 4 NumericSemantics.operatorPolicies.Length
            equal 2 NumericSemantics.schemaVersion

        "operator algebra is commutative and inversion is involutive",
        fun () ->
            let candidates = [ number; length; angle; time; percent; lengthPercent ]

            for left in candidates do
                equal left (NumericSemantics.invert (NumericSemantics.invert left))

                for right in candidates do
                    let classify = Result.mapError (fun _ -> ())
                    equal (NumericSemantics.add left right |> classify) (NumericSemantics.add right left |> classify)
                    equal (NumericSemantics.multiply left right |> classify) (NumericSemantics.multiply right left |> classify)
    ]

/// <summary>Checks numeric type algebra, percentage provenance and CSS math function contracts.</summary>
let tests =
    testList
        "Numeric semantics"
        (cases
         |> List.map (fun (name, run) -> testCase name run))
