namespace Ranvier.Css.Spec

/// <summary>A base dimension in the CSS numeric exponent map.</summary>
[<RequireQualifiedAccess>]
type NumericDimension =
    | Length
    | Angle
    | Time
    | Frequency
    | Resolution
    | Flex
    | Percent

/// <summary>A CSS numeric type with integer dimension powers and retained percentage provenance.</summary>
type NumericType =
    {
        Exponents: Map<NumericDimension, int>
        PercentHint: NumericDimension option
    }

/// <summary>The percentage resolution rules of the surrounding calculation.</summary>
type NumericContext =
    {
        PercentagesAllowed: bool
        PercentageBasis: NumericDimension option
    }

/// <summary>A rounding strategy accepted by the CSS round function.</summary>
[<RequireQualifiedAccess>]
type RoundingStrategy =
    | Nearest
    | Up
    | Down
    | ToZero
    | LineWidth

/// <summary>A typed calculation or a positional keyword in a CSS math function call.</summary>
[<RequireQualifiedAccess>]
type NumericArgument =
    | Value of NumericType
    | NoneBound
    | Strategy of RoundingStrategy
    | NoClamp
    | RandomKey
    | WeightedValue of NumericType * float option

/// <summary>A failure to combine numeric types or satisfy a math function contract.</summary>
[<RequireQualifiedAccess>]
type NumericError =
    | IncompatibleHints of NumericDimension * NumericDimension
    | IncompatibleTypes of NumericType * NumericType
    | UnknownFunction of string
    | InvalidArguments of functionName: string * reason: string

/// <summary>A supported function's argument and result policies, with normative source URLs.</summary>
type NumericFunctionPolicy =
    {
        Name: string
        MinimumArguments: int
        MaximumArguments: int option
        ArgumentPolicy: string
        ResultPolicy: string
        SourceUrls: string list
    }

/// <summary>A numeric operator's exponent-map rule and normative source URLs.</summary>
type NumericOperatorPolicy =
    {
        Operator: string
        TypePolicy: string
        SourceUrls: string list
    }

/// <summary>CSS Values 4 calculation typing and CSS Typed OM exponent-map algebra.</summary>
module NumericSemantics =
    /// <summary>The version of the executable numeric policy contract.</summary>
    let schemaVersion = 2

    /// <summary>The normative algorithms implemented by this module, in stable order.</summary>
    let sources =
        [
            "https://drafts.csswg.org/css-values-4/#calc-type-checking"
            "https://drafts.css-houdini.org/css-typed-om-1/#numeric-typing"
            "https://drafts.csswg.org/css-values-5/"
        ]

    /// <summary>The symbolic CSS calculation constants in stable canonical spelling.</summary>
    let constants = [ "e"; "pi"; "infinity"; "-infinity"; "NaN" ]

    /// <summary>The binary type algebra policies, in stable operator order.</summary>
    let operatorPolicies =
        [
            for symbol, algorithm in
                [
                    "+", "add-types"
                    "-", "add-types"
                    "*", "multiply-types"
                    "/", "multiply-with-inverted-right"
                ] do
                {
                    Operator = symbol
                    TypePolicy = algorithm
                    SourceUrls = sources
                }
        ]

    /// <summary>The supported base dimensions, in stable serialization order.</summary>
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

    /// <summary>The unitless type shared by CSS numbers and integer calculation inputs.</summary>
    let number =
        {
            Exponents = Map.empty
            PercentHint = None
        }

    /// <summary>Creates a single base dimension with exponent one and a null percentage hint.</summary>
    let dimension baseType =
        { number with
            Exponents = Map.ofList [ baseType, 1 ]
        }

    /// <summary>Creates a CSS percentage terminal, resolving a dimension basis and retaining its hint.</summary>
    /// <remarks>A null basis includes percentages relative to numbers, which remain percentage typed.</remarks>
    let percentage basis =
        let hint = Option.defaultValue NumericDimension.Percent basis

        {
            Exponents = Map.ofList [ hint, 1 ]
            PercentHint = Some hint
        }

    /// <summary>A calculation context accepting ordinary percentages.</summary>
    let ordinaryContext =
        {
            PercentagesAllowed = true
            PercentageBasis = None
        }

    /// <summary>A calculation context accepting exclusively percentage-free results.</summary>
    let dimensionOnlyContext =
        {
            PercentagesAllowed = false
            PercentageBasis = None
        }

    let private normalize numericType =
        { numericType with
            Exponents =
                numericType.Exponents
                |> Map.filter (fun _ exponent -> exponent <> 0)
        }

    let private power key numericType =
        Map.tryFind key numericType.Exponents
        |> Option.defaultValue 0

    let private applyHint hint numericType =
        let powers =
            if hint = NumericDimension.Percent then
                numericType.Exponents
            else
                numericType.Exponents
                |> Map.add
                    hint
                    (power hint numericType
                     + power NumericDimension.Percent numericType)
                |> Map.remove NumericDimension.Percent

        normalize
            {
                Exponents = powers
                PercentHint = Some hint
            }

    let private alignHints left right =
        match left.PercentHint, right.PercentHint with
        | Some a, Some b when a <> b -> Error (NumericError.IncompatibleHints (a, b))
        | Some hint, None -> Ok (normalize left, applyHint hint right)
        | None, Some hint -> Ok (applyHint hint left, normalize right)
        | _ -> Ok (normalize left, normalize right)

    /// <summary>Adds CSS numeric types, returning an error for incompatible powers or percentage hints.</summary>
    let add left right =
        alignHints left right
        |> Result.bind (fun (a, b) ->
            if a.Exponents = b.Exponents then
                Ok a
            elif
                a.PercentHint.IsNone
                && b.PercentHint.IsNone
                && (power NumericDimension.Percent a <> 0
                    || power NumericDimension.Percent b <> 0)
            then
                dimensions
                |> List.filter ((<>) NumericDimension.Percent)
                |> List.tryPick (fun hint ->
                    let hintedA, hintedB = applyHint hint a, applyHint hint b

                    if hintedA.Exponents = hintedB.Exponents then
                        Some hintedA
                    else
                        None)
                |> function
                    | Some result -> Ok result
                    | None -> Error (NumericError.IncompatibleTypes (a, b))
            else
                Error (NumericError.IncompatibleTypes (a, b)))

    /// <summary>Multiplies exponent maps, preserving compound powers and rejecting conflicting percentage hints.</summary>
    let multiply left right =
        alignHints left right
        |> Result.map (fun (a, b) ->
            let powers =
                b.Exponents
                |> Map.fold
                    (fun result key exponent ->
                        Map.add
                            key
                            (exponent
                             + (Map.tryFind key result |> Option.defaultValue 0))
                            result)
                    a.Exponents

            normalize
                {
                    Exponents = powers
                    PercentHint = a.PercentHint
                })

    /// <summary>Negates every exponent while retaining the percentage hint.</summary>
    let invert numericType =
        normalize
            { numericType with
                Exponents =
                    numericType.Exponents
                    |> Map.map (fun _ exponent -> -exponent)
            }

    /// <summary>Divides CSS numeric types by multiplying the numerator by the inverted denominator.</summary>
    let divide numerator denominator =
        multiply numerator (invert denominator)

    let private hintAllowed context numericType =
        match numericType.PercentHint with
        | None -> true
        | Some hint ->
            context.PercentagesAllowed
            && hint = Option.defaultValue NumericDimension.Percent context.PercentageBasis

    /// <summary>Checks whether a type matches a CSS number in its percentage resolution context.</summary>
    let matchesNumber context numericType =
        (normalize numericType).Exponents.IsEmpty
        && hintAllowed context numericType

    /// <summary>Checks whether a type matches a single CSS dimension in its percentage resolution context.</summary>
    let matchesDimension context baseType numericType =
        let powers = (normalize numericType).Exponents

        powers = Map.ofList [ baseType, 1 ]
        && (if baseType = NumericDimension.Percent then
                context.PercentagesAllowed
                && (numericType.PercentHint.IsNone
                    || numericType.PercentHint = Some NumericDimension.Percent)
            else
                numericType.PercentHint.IsNone
                || (context.PercentagesAllowed
                    && context.PercentageBasis.IsSome
                    && numericType.PercentHint = context.PercentageBasis))

    /// <summary>Checks a completed calculation against a primitive expected type and the surrounding percentage rules.</summary>
    let matches context expected actual =
        if (normalize expected).Exponents.IsEmpty then
            matchesNumber context actual
        else
            dimensions
            |> List.exists (fun baseType ->
                (normalize expected).Exponents = (dimension baseType).Exponents
                && matchesDimension context baseType actual)

    let private primitive context numericType =
        matchesNumber context numericType
        || dimensions
           |> List.exists (fun baseType -> matchesDimension context baseType numericType)

    let private consistent values =
        match values with
        | [] -> Error (NumericError.InvalidArguments ("", "At least one calculation is required."))
        | first :: rest ->
            rest
            |> List.fold
                (fun state next ->
                    state
                    |> Result.bind (fun current -> add current next))
                (Ok first)

    let private invalid name reason =
        Error (NumericError.InvalidArguments (name, reason))

    let private valuesOnly name arguments =
        arguments
        |> List.fold
            (fun state argument ->
                state
                |> Result.bind (fun values ->
                    match argument with
                    | NumericArgument.Value value -> Ok (value :: values)
                    | _ -> invalid name "Only numeric calculations are accepted in these positions."))
            (Ok [])
        |> Result.map List.rev

    let private consistentWith result input =
        { result with
            PercentHint = input.PercentHint
        }

    let private policy name minimum maximum arguments result section =
        {
            Name = name
            MinimumArguments = minimum
            MaximumArguments = maximum
            ArgumentPolicy = arguments
            ResultPolicy = result
            SourceUrls =
                [
                    "https://drafts.csswg.org/css-values-4/#"
                    + section
                    List.head sources
                ]
        }

    let private policy5 name minimum maximum arguments result section =
        { policy name minimum maximum arguments result section with
            SourceUrls =
                [
                    "https://drafts.csswg.org/css-values-5/#"
                    + section
                    List.head sources
                ]
        }

    /// <summary>All executable math function contracts, in stable name order; keyword slots count toward arity.</summary>
    let functionPolicies =
        [
            for name in [ "sibling-count"; "sibling-index" ] do
                policy5 name 0 (Some 0) "no-arguments" "number" "sibling-functions"
            policy5 "progress" 3 (Some 4) "optional-no-clamp;three-consistent-numeric" "number-consistent-with-input" "progress-func"
            policy5 "random" 2 (Some 4) "optional-leading-random-key;two-or-three-consistent-numeric" "consistent-inputs" "random-func"
            policy5 "calc-mix" 1 None "weighted-consistent-numeric" "consistent-inputs" "calc-mix"
            policy5 "calc-interpolate" 2 None "interpolation-map" "consistent-output-values" "calc-interpolate"
            policy "calc" 1 (Some 1) "one-numeric" "input" "funcdef-calc"
            policy "abs" 1 (Some 1) "one-numeric" "input" "sign-funcs"
            policy "sign" 1 (Some 1) "one-typed-calculation" "number-consistent-with-input" "sign-funcs"
            for name in [ "min"; "max"; "hypot" ] do
                policy name 1 None "consistent-numeric" "consistent-inputs" (if name = "hypot" then "exponent-funcs" else "comp-func")
            policy "clamp" 3 (Some 3) "consistent-numeric; none-allowed-only-at-bounds" "consistent-numeric-inputs" "comp-func"
            policy
                "round"
                1
                (Some 3)
                "optional-leading-strategy; consistent-numeric; omitted-step-requires-number-or-line-width; line-width-requires-length"
                "consistent-numeric-inputs"
                "round-func"
            for name in [ "mod"; "rem" ] do
                policy name 2 (Some 2) "two-consistent-numeric" "consistent-inputs" "round-func"
            for name in [ "sin"; "cos"; "tan" ] do
                policy name 1 (Some 1) "one-number-or-angle" "number-consistent-with-input" "trig-funcs"
            for name in [ "asin"; "acos"; "atan" ] do
                policy name 1 (Some 1) "one-number" "angle-consistent-with-input" "trig-funcs"
            policy "atan2" 2 (Some 2) "two-consistent-numeric" "angle-consistent-with-inputs" "trig-funcs"
            policy "pow" 2 (Some 2) "two-consistent-numbers" "consistent-inputs" "exponent-funcs"
            for name in [ "sqrt"; "exp" ] do
                policy name 1 (Some 1) "one-number" "number-consistent-with-input" "exponent-funcs"
            policy "log" 1 (Some 2) "one-or-two-consistent-numbers" "number-consistent-with-inputs" "exponent-funcs"
        ]
        |> List.sortBy _.Name

    /// <summary>Infers a supported CSS math function's result, validating arity, keyword positions and numeric restrictions.</summary>
    /// <remarks>Compound intermediate types are accepted by the algebra; completed functions must resolve to a CSS numeric production.</remarks>
    let inferFunction context (name: string) (arguments: NumericArgument list) =
        let name = name.ToLowerInvariant ()

        match
            functionPolicies
            |> List.tryFind (fun entry -> entry.Name = name)
        with
        | None -> Error (NumericError.UnknownFunction name)
        | Some entry when
            arguments.Length < entry.MinimumArguments
            || (entry.MaximumArguments
                |> Option.exists (fun maximum -> arguments.Length > maximum))
            ->
            invalid name "The argument count does not match the function contract."
        | Some _ when name = "sibling-count" || name = "sibling-index" -> Ok number
        | Some _ when name = "calc-interpolate" -> invalid name "Interpolation requires a structured progress source and stop map."
        | Some _ ->
            let calculations =
                match name, arguments with
                | "progress", _ ->
                    let numericArguments =
                        match arguments with
                        | NumericArgument.NoClamp :: rest -> rest
                        | _ -> arguments

                    if numericArguments.Length = 3 then
                        valuesOnly name numericArguments
                    else
                        invalid name "Progress requires three numeric arguments after an optional no-clamp keyword."
                | "random", _ ->
                    let numericArguments =
                        match arguments with
                        | NumericArgument.RandomKey :: rest -> rest
                        | _ -> arguments

                    if
                        numericArguments.Length = 2
                        || numericArguments.Length = 3
                    then
                        valuesOnly name numericArguments
                    else
                        invalid name "Random requires two or three numeric arguments after an optional key."
                | "calc-mix", _ ->
                    arguments
                    |> List.fold
                        (fun state argument ->
                            state
                            |> Result.bind (fun values ->
                                match argument with
                                | NumericArgument.Value value -> Ok (value :: values)
                                | NumericArgument.WeightedValue (value, weight) when
                                    weight
                                    |> Option.forall (fun n ->
                                        not (
                                            System.Double.IsNaN n
                                            || System.Double.IsInfinity n
                                        )
                                        && n >= 0.
                                        && n <= 100.)
                                    ->
                                    Ok (value :: values)
                                | _ -> invalid name "Mix items require numeric values and finite weights from zero to 100 percent."))
                        (Ok [])
                    |> Result.map List.rev
                | "clamp", [ lower; NumericArgument.Value middle; upper ] ->
                    let bound =
                        function
                        | NumericArgument.NoneBound -> Ok []
                        | NumericArgument.Value value -> Ok [ value ]
                        | _ -> invalid name "Clamp bounds must be calculations or none."

                    bound lower
                    |> Result.bind (fun lowerValues ->
                        bound upper
                        |> Result.map (fun upperValues -> lowerValues @ [ middle ] @ upperValues))
                | "clamp", _ -> invalid name "Clamp requires a numeric middle argument."
                | "round", _ ->
                    let strategy, numericArguments =
                        match arguments with
                        | NumericArgument.Strategy strategy :: rest -> strategy, rest
                        | _ -> RoundingStrategy.Nearest, arguments

                    valuesOnly name numericArguments
                    |> Result.bind (fun values ->
                        match values with
                        | [ value ] when
                            strategy = RoundingStrategy.LineWidth
                            && matchesDimension context NumericDimension.Length value
                            ->
                            Ok values
                        | [ value ] when
                            strategy <> RoundingStrategy.LineWidth
                            && matchesNumber context value
                            ->
                            Ok values
                        | [ value; _ ] when
                            strategy <> RoundingStrategy.LineWidth
                            || matchesDimension context NumericDimension.Length value
                            ->
                            Ok values
                        | _ -> invalid name "Round needs a compatible step, or a number; line-width requires a length.")
                | _ -> valuesOnly name arguments

            calculations
            |> Result.bind (fun values ->
                if
                    name <> "sign"
                    && (values |> List.exists (primitive context >> not))
                then
                    invalid name "Every argument must resolve to a CSS numeric production in this context."
                else
                    consistent values
                    |> Result.bind (fun common ->
                        match name with
                        | "sin"
                        | "cos"
                        | "tan" ->
                            if
                                matchesNumber context common
                                || matchesDimension context NumericDimension.Angle common
                            then
                                Ok (consistentWith number common)
                            else
                                invalid name "Trigonometric arguments must be numbers or angles."
                        | "asin"
                        | "acos"
                        | "atan" ->
                            if matchesNumber context common then
                                Ok (consistentWith (dimension NumericDimension.Angle) common)
                            else
                                invalid name "Inverse trigonometric arguments must be numbers."
                        | "atan2" -> Ok (consistentWith (dimension NumericDimension.Angle) common)
                        | "pow"
                        | "sqrt"
                        | "log"
                        | "exp" ->
                            if values |> List.forall (matchesNumber context) then
                                Ok (consistentWith number common)
                            else
                                invalid name "Exponential arguments must be numbers."
                        | "sign"
                        | "progress" -> Ok (consistentWith number common)
                        | _ -> Ok common))
            |> Result.bind (fun result ->
                if primitive context result then
                    Ok result
                else
                    invalid name "The result must resolve to a CSS numeric production in this context.")

    /// <summary>Validates interpolation input dimensions separately from output percentages, retaining the consistent output type.</summary>
    /// <param name="context">The output value's percentage resolution context.</param>
    /// <param name="progress">The numeric input type, or the absolute dimension of an animation timeline.</param>
    /// <param name="progressIsTimeline">Whether progress also has a proportional timeline interpretation.</param>
    /// <param name="positions">The input stop types, inferred in the ordinary percentage context.</param>
    /// <param name="outputs">The output stop types, inferred in the destination context.</param>
    let inferInterpolation context progress progressIsTimeline (positions: NumericType list) (outputs: NumericType list) =
        let proportional numeric =
            matchesNumber ordinaryContext numeric
            || matchesDimension ordinaryContext NumericDimension.Percent numeric

        let absolutePositions = positions |> List.filter (proportional >> not)

        let absolute =
            (if proportional progress then [] else [ progress ])
            @ absolutePositions

        if positions.IsEmpty || outputs.IsEmpty then
            invalid "calc-interpolate" "At least one input position and output stop is required."
        elif
            (progress :: positions)
            |> List.exists (primitive ordinaryContext >> not)
        then
            invalid "calc-interpolate" "Input positions must resolve to numeric primitives in their independent percentage context."
        elif
            not progressIsTimeline
            && not (proportional progress)
            && absolutePositions.IsEmpty
        then
            invalid "calc-interpolate" "An absolute progress source requires an absolute input stop."
        else
            (if absolute.IsEmpty then Ok number else consistent absolute)
            |> Result.bind (fun _ -> inferFunction context "min" (outputs |> List.map NumericArgument.Value))
