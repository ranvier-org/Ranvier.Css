namespace Ranvier.Css

open System
open Ranvier.Css.Spec

/// <summary>A structured CSS easing curve; control coordinates and stop ranges are validated by interpolation construction.</summary>
[<RequireQualifiedAccess>]
type CalculationEasing =
    | Keyword of string
    | CubicBezier of float * float * float * float
    | Steps of count: int * position: string
    | Linear of (float * float list) list

/// <summary>A CSS random cache key or a fixed browser random base value.</summary>
[<RequireQualifiedAccess>]
type CalculationRandomKey =
    | Auto
    | Fixed of float
    | Scoped of name: string option * elementScoped: bool * propertyScope: string option

/// <summary>An opaque CSS arithmetic tree retaining terminal types, grouping and percentage provenance.</summary>
type Calculation =
    private
    | Literal of production: string * text: string
    | Binary of operation: string * left: Calculation * right: Calculation
    | Negate of Calculation
    | Constant of string
    | Size
    | Reference of production: string * name: string * fallback: Calculation option
    | Function of name: string * arguments: CalculationArgument list
    | Interpolate of CalculationProgress * CalculationEasing option * CalculationEasing option * CalculationInterpolationStop list

/// <summary>A numeric expression or a keyword occupying a CSS math function argument slot.</summary>
and [<RequireQualifiedAccess>] CalculationArgument =
    | Value of Calculation
    | NoneBound
    | Strategy of RoundingStrategy
    | NoClamp
    | RandomKey of CalculationRandomKey
    | WeightedValue of Calculation * float option

/// <summary>The numeric or browser timeline source of an interpolation map.</summary>
and [<RequireQualifiedAccess>] CalculationProgress =
    | Value of Calculation
    | Timeline of CalculationTimeline

/// <summary>A named, scroll or view animation timeline; named timelines declare their absolute dimension.</summary>
and [<RequireQualifiedAccess>] CalculationTimeline =
    | Named of name: string * dimension: NumericDimension
    | Scroll of scroller: string option * axis: string option
    | View of axis: string option * insets: CalculationInset list

/// <summary>An automatic or numeric view timeline inset.</summary>
and [<RequireQualifiedAccess>] CalculationInset =
    | Auto
    | Value of Calculation

/// <summary>An interpolation stop with one or two independent input positions and an optional preceding segment easing.</summary>
and CalculationInterpolationStop =
    {
        Positions: Calculation list
        Value: Calculation
        EasingBefore: CalculationEasing option
    }

/// <summary>An intrinsic sizing calculation admitted only by sizing destinations.</summary>
type IntrinsicCalculation = private IntrinsicCalculation of IntrinsicBasis * Calculation

/// <summary>The sizing basis whose browser-resolved value supplies the scoped size keyword.</summary>
and [<RequireQualifiedAccess>] IntrinsicBasis =
    | Any
    | Keyword of string
    | Value of Calculation
    | Nested of IntrinsicCalculation
    | FitContent of Calculation
    | Stretch of Calculation

/// <summary>Constructs unevaluated arithmetic, infers CSS numeric types and validates completed declarations.</summary>
module CalculationRuntime =
    let private lengthUnits =
        Set.ofList
            [
                "px"
                "cm"
                "mm"
                "q"
                "in"
                "pt"
                "pc"
                "em"
                "ex"
                "cap"
                "ch"
                "ic"
                "rem"
                "rex"
                "rcap"
                "rch"
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
                "cqw"
                "cqh"
                "cqi"
                "cqb"
                "cqmin"
                "cqmax"
            ]

    let private dimensionFor production =
        match production with
        | "length" -> Some NumericDimension.Length
        | "angle" -> Some NumericDimension.Angle
        | "time" -> Some NumericDimension.Time
        | "frequency" -> Some NumericDimension.Frequency
        | "resolution" -> Some NumericDimension.Resolution
        | "flex" -> Some NumericDimension.Flex
        | _ -> None

    let private basisFor (production: string) =
        if production.EndsWith ("-percentage", StringComparison.Ordinal) then
            production.Substring (0, production.Length - "-percentage".Length)
            |> dimensionFor
        else
            None

    /// <summary>Creates a finite numeric terminal; explicit basis percentages retain their basis through arithmetic.</summary>
    /// <param name="production">A supported numeric production, or a dimension followed by <c>-percentage</c> for an explicit basis.</param>
    /// <param name="unitName">A CSS unit belonging to the production; numbers use an empty suffix and percentages use <c>%</c>.</param>
    /// <param name="value">A finite terminal value; integer terminals must have no fractional part.</param>
    /// <exception cref="T:System.ArgumentException">The production, suffix or numeric value is invalid.</exception>
    let literal (production: string) (unitName: string) (value: float) =
        let canonicalUnit = if isNull unitName then "" else unitName.ToLowerInvariant ()

        let valid =
            match production with
            | "number" -> canonicalUnit = ""
            | "integer" -> canonicalUnit = "" && Math.Truncate value = value
            | "percentage" -> canonicalUnit = "%"
            | "length" -> Set.contains canonicalUnit lengthUnits
            | "angle" -> List.contains canonicalUnit [ "deg"; "grad"; "rad"; "turn" ]
            | "time" -> List.contains canonicalUnit [ "s"; "ms" ]
            | "frequency" -> List.contains canonicalUnit [ "hz"; "khz" ]
            | "resolution" -> List.contains canonicalUnit [ "dpi"; "dpcm"; "dppx"; "x" ]
            | "flex" -> canonicalUnit = "fr"
            | _ when
                not (isNull production)
                && (basisFor production).IsSome
                ->
                canonicalUnit = "%"
            | _ -> false

        if isNull unitName || not valid then
            invalidArg "production" "Unsupported CSS numeric production or unit."

        let token: Value<obj> = Generated.number value unitName
        Literal (production, Generated.text token)

    /// <summary>Creates a numeric custom-property reference with a declared production and a checked optional fallback expression.</summary>
    /// <param name="production">A numeric production or explicit dimension-percentage production.</param>
    /// <param name="name">A dashed custom-property identifier, including its leading two hyphens.</param>
    /// <param name="fallback">An optional unevaluated expression compatible with the declared production in its destination context.</param>
    /// <exception cref="T:System.ArgumentException">The production or custom-property identifier is unsupported.</exception>
    let reference (production: string) (name: string) (fallback: Calculation option) =
        let known =
            List.contains production [ "number"; "integer"; "percentage" ]
            || (dimensionFor production).IsSome
            || (not (isNull production)
                && (basisFor production).IsSome)

        if not known then
            invalidArg "production" "Unsupported CSS numeric reference production."

        Generated.identifier true false name |> ignore
        Reference (production, name, fallback)

    /// <summary>Preserves both operands in an unevaluated binary arithmetic node.</summary>
    /// <param name="operation">One of <c>+</c>, <c>-</c>, <c>*</c> or <c>/</c>.</param>
    /// <param name="left">The left operand, retained without numeric evaluation.</param>
    /// <param name="right">The right operand, retained without numeric evaluation.</param>
    /// <exception cref="T:System.ArgumentException">The operation is unsupported.</exception>
    let binary (operation: string) (left: Calculation) (right: Calculation) =
        if not (List.contains operation [ "+"; "-"; "*"; "/" ]) then
            invalidArg "operation" "Unsupported CSS arithmetic operator."

        Binary (operation, left, right)

    /// <summary>Negates an expression without evaluating or clamping its numeric value.</summary>
    let negate (value: Calculation) =
        Negate value

    /// <summary>The length-typed size keyword, valid exclusively inside an intrinsic calculation with an established basis.</summary>
    let size = Size

    /// <summary>Creates a symbolic CSS calc constant, including non-finite constants, without host numeric evaluation.</summary>
    /// <exception cref="T:System.ArgumentException">The name is not e, pi, infinity, -infinity or NaN.</exception>
    let constant (name: string) =
        match
            NumericSemantics.constants
            |> List.tryFind (fun constant -> String.Equals (constant, name, StringComparison.OrdinalIgnoreCase))
        with
        | Some canonical -> Constant canonical
        | None -> invalidArg "name" "Unsupported CSS calculation constant."

    let private randomKeyText key =
        match key with
        | CalculationRandomKey.Auto -> "auto"
        | CalculationRandomKey.Fixed value ->
            if
                Double.IsNaN value
                || Double.IsInfinity value
                || value < 0.
                || value > 1.
            then
                invalidArg "key" "A fixed random base must be finite and between zero and one."

            "fixed "
            + (Generated.number value "" |> Generated.text)
        | CalculationRandomKey.Scoped (name, elementScoped, propertyScope) ->
            name
            |> Option.iter (fun value -> Generated.identifier true false value |> ignore)

            propertyScope
            |> Option.iter (fun value ->
                if
                    value <> "property-scoped"
                    && value <> "property-index-scoped"
                then
                    if
                        isNull value
                        || not (value.StartsWith ("ua-", StringComparison.Ordinal))
                    then
                        invalidArg "key" "Invalid random property scope."

                    Generated.identifier false true value |> ignore)

            let parts =
                [
                    yield! Option.toList name
                    if elementScoped then
                        "element-scoped"
                    yield! Option.toList propertyScope
                ]

            if parts.IsEmpty then
                invalidArg "key" "A scoped random key must specify a scope or cache name."

            String.concat " " parts

    let private validateWeight weight =
        weight
        |> Option.iter (fun value ->
            if
                Double.IsNaN value
                || Double.IsInfinity value
                || value < 0.
                || value > 100.
            then
                invalidArg "weight" "A mix weight must be finite and between zero and 100 percent.")

    /// <summary>Constructs a supported math call with positional keyword arguments; contextual numeric validation occurs during inference.</summary>
    /// <exception cref="T:System.ArgumentException">The function, argument count or keyword position is invalid.</exception>
    let functionArguments (name: string) (arguments: CalculationArgument list) =
        let name = if isNull name then "" else name.ToLowerInvariant ()

        match
            NumericSemantics.functionPolicies
            |> List.tryFind (fun policy -> policy.Name = name)
        with
        | None -> invalidArg "name" "Unsupported CSS numeric function."
        | Some policy ->
            if
                arguments.Length < policy.MinimumArguments
                || (policy.MaximumArguments
                    |> Option.exists (fun maximum -> arguments.Length > maximum))
            then
                invalidArg "arguments" "CSS numeric function argument count is invalid."

            arguments
            |> List.iteri (fun index argument ->
                match argument with
                | CalculationArgument.Value _ -> ()
                | CalculationArgument.NoClamp when name = "progress" && index = 0 -> ()
                | CalculationArgument.RandomKey key when name = "random" && index = 0 -> randomKeyText key |> ignore
                | CalculationArgument.WeightedValue (_, weight) when name = "calc-mix" -> validateWeight weight
                | CalculationArgument.NoneBound when name = "clamp" && (index = 0 || index = 2) -> ()
                | CalculationArgument.Strategy _ when name = "round" && index = 0 -> ()
                | _ -> invalidArg "arguments" "CSS numeric function keyword position is invalid.")

            Function (name, arguments)

    /// <summary>Constructs a supported math call whose argument slots all contain numeric expressions.</summary>
    /// <exception cref="T:System.ArgumentException">The function or argument count is invalid.</exception>
    let functionCall (name: string) (arguments: Calculation list) =
        functionArguments name (List.map CalculationArgument.Value arguments)

    let private easingText easing =
        let number value =
            Generated.number value "" |> Generated.text

        match easing with
        | CalculationEasing.Keyword name ->
            if
                not (
                    List.contains
                        name
                        [
                            "linear"
                            "ease"
                            "ease-in"
                            "ease-out"
                            "ease-in-out"
                            "step-start"
                            "step-end"
                        ]
                )
            then
                invalidArg "easing" "Unsupported easing keyword."

            name
        | CalculationEasing.CubicBezier (x1, y1, x2, y2) ->
            if x1 < 0. || x1 > 1. || x2 < 0. || x2 > 1. then
                invalidArg "easing" "Bezier x coordinates must be between zero and one."

            "cubic-bezier("
            + ([ x1; y1; x2; y2 ]
               |> List.map number
               |> String.concat ", ")
            + ")"
        | CalculationEasing.Steps (count, position) ->
            if
                count < 1
                || (count < 2 && position = "jump-none")
                || not (List.contains position [ "jump-start"; "jump-end"; "jump-none"; "jump-both"; "start"; "end" ])
            then
                invalidArg "easing" "Invalid stepped easing count or jump position."

            "steps(" + string count + ", " + position + ")"
        | CalculationEasing.Linear stops ->
            if stops.Length < 2 then
                invalidArg "easing" "Linear easing needs at least two stops."

            let stop (output, (positions: float list)) =
                if positions.Length > 2 then
                    invalidArg "easing" "A linear stop accepts at most two positions."

                number output
                + (positions
                   |> List.map (fun position ->
                       " "
                       + (Generated.number position "%" |> Generated.text))
                   |> String.concat "")

            "linear("
            + (stops |> List.map stop |> String.concat ", ")
            + ")"

    let private validateTimeline timeline =
        let axis value =
            value
            |> Option.iter (fun axis ->
                if not (List.contains axis [ "block"; "inline"; "x"; "y" ]) then
                    invalidArg "timeline" "Invalid timeline axis.")

        match timeline with
        | CalculationTimeline.Named (name, dimension) ->
            Generated.identifier true false name |> ignore

            if
                dimension <> NumericDimension.Length
                && dimension <> NumericDimension.Time
            then
                invalidArg "timeline" "A named timeline has length or time as its absolute dimension."
        | CalculationTimeline.Scroll (scroller, value) ->
            axis value

            scroller
            |> Option.iter (fun scroller ->
                if not (List.contains scroller [ "nearest"; "root"; "self" ]) then
                    invalidArg "timeline" "Invalid scroll timeline scroller.")
        | CalculationTimeline.View (value, insets) ->
            axis value

            if insets.Length > 2 then
                invalidArg "timeline" "A view timeline accepts at most two insets."

    /// <summary>Creates an interpolation map with independent input percentages and destination-typed output values.</summary>
    /// <param name="progress">A numeric expression or an animation timeline resolved by the browser.</param>
    /// <param name="progressEasing">An optional easing applied to the overall progress source.</param>
    /// <param name="defaultEasing">An optional default easing between output stops.</param>
    /// <param name="stops">A nonempty map; each stop has one or two positions and the first has no preceding easing.</param>
    /// <exception cref="T:System.ArgumentException">The map, timeline or easing syntax is invalid.</exception>
    let interpolate progress progressEasing defaultEasing (stops: CalculationInterpolationStop list) =
        if stops.IsEmpty then
            invalidArg "stops" "Interpolation requires at least one stop."

        stops
        |> List.iteri (fun index stop ->
            if
                stop.Positions.IsEmpty
                || stop.Positions.Length > 2
                || (index = 0 && stop.EasingBefore.IsSome)
            then
                invalidArg "stops" "Invalid interpolation positions or first segment easing."

            stop.EasingBefore
            |> Option.iter (easingText >> ignore))

        progressEasing
        |> Option.iter (easingText >> ignore)

        defaultEasing
        |> Option.iter (easingText >> ignore)

        match progress with
        | CalculationProgress.Timeline timeline -> validateTimeline timeline
        | CalculationProgress.Value _ -> ()

        Interpolate (progress, progressEasing, defaultEasing, stops)

    let private precedence =
        function
        | Literal _
        | Constant _
        | Size
        | Reference _
        | Function _
        | Interpolate _ -> 3
        | Negate _ -> 2
        | Binary (("*" | "/"), _, _) -> 2
        | Binary _ -> 1

    let rec private serialize calculation =
        match calculation with
        | Literal (_, text)
        | Constant text -> text
        | Size -> "size"
        | Reference (_, name, fallback) ->
            let fallbackText value =
                match value with
                | Literal _
                | Function _
                | Interpolate _
                | Reference _ -> serialize value
                | _ -> "calc(" + serialize value + ")"

            "var("
            + name
            + (fallback
               |> Option.map (fun value -> ", " + fallbackText value)
               |> Option.defaultValue "")
            + ")"
        | Function (name, arguments) ->
            let argumentText =
                function
                | CalculationArgument.Value value -> serialize value
                | CalculationArgument.NoneBound -> "none"
                | CalculationArgument.NoClamp -> "no-clamp"
                | CalculationArgument.RandomKey key -> randomKeyText key
                | CalculationArgument.WeightedValue (value, weight) ->
                    serialize value
                    + (weight
                       |> Option.map (fun n -> " " + (Generated.number n "%" |> Generated.text))
                       |> Option.defaultValue "")
                | CalculationArgument.Strategy strategy ->
                    match strategy with
                    | RoundingStrategy.Nearest -> "nearest"
                    | RoundingStrategy.Up -> "up"
                    | RoundingStrategy.Down -> "down"
                    | RoundingStrategy.ToZero -> "to-zero"
                    | RoundingStrategy.LineWidth -> "line-width"

            let body =
                match arguments with
                | CalculationArgument.NoClamp :: rest when name = "progress" ->
                    "no-clamp "
                    + (rest
                       |> List.map argumentText
                       |> String.concat ", ")
                | _ ->
                    arguments
                    |> List.map argumentText
                    |> String.concat ", "

            name + "(" + body + ")"
        | Interpolate (progress, progressEasing, defaultEasing, stops) ->
            let renderInput value =
                match value with
                | Literal _
                | Function _
                | Interpolate _
                | Reference _ -> serialize value
                | _ -> "calc(" + serialize value + ")"

            let timelineText timeline =
                match timeline with
                | CalculationTimeline.Named (name, _) -> name
                | CalculationTimeline.Scroll (scroller, axis) ->
                    "scroll("
                    + (Option.toList scroller @ Option.toList axis
                       |> String.concat " ")
                    + ")"
                | CalculationTimeline.View (axis, insets) ->
                    let inset =
                        function
                        | CalculationInset.Auto -> "auto"
                        | CalculationInset.Value value -> renderInput value

                    "view("
                    + (Option.toList axis @ List.map inset insets
                       |> String.concat " ")
                    + ")"

            let source =
                match progress with
                | CalculationProgress.Value value -> renderInput value
                | CalculationProgress.Timeline timeline -> timelineText timeline

            let header =
                source
                + (progressEasing
                   |> Option.map (fun easing -> " by " + easingText easing)
                   |> Option.defaultValue "")
                + (defaultEasing
                   |> Option.map (fun easing -> " " + easingText easing)
                   |> Option.defaultValue "")

            let stopText stop =
                (stop.EasingBefore
                 |> Option.map (fun easing -> easingText easing + ", ")
                 |> Option.defaultValue "")
                + (stop.Positions
                   |> List.map renderInput
                   |> String.concat " ")
                + ": "
                + serialize stop.Value

            "calc-interpolate("
            + header
            + ", "
            + (stops |> List.map stopText |> String.concat ", ")
            + ")"
        | Negate value ->
            let text = serialize value

            "-1 * "
            + (if precedence value <= 2 then "(" + text + ")" else text)
        | Binary (operation, left, right) ->
            let level = precedence calculation

            let operand isRight value =
                let text = serialize value

                let grouped =
                    precedence value < level
                    || (isRight && precedence value = level)

                if grouped then "(" + text + ")" else text

            operand false left
            + " "
            + operation
            + " "
            + operand true right

    /// <summary>Serializes one <c>calc(...)</c> expression with arithmetic grouping intact.</summary>
    let render (value: Calculation) =
        match value with
        | Function _
        | Interpolate _ -> serialize value
        | _ -> "calc(" + serialize value + ")"

    /// <summary>Infers exponent maps and percentage hints, retaining compound intermediate dimensions.</summary>
    /// <remarks>Disallowed percentages fail even when arithmetic would cancel their dimension powers.</remarks>
    let rec private inferInScope (context: NumericContext) sizeAllowed (calculation: Calculation) : Result<NumericType, NumericError> =
        match calculation with
        | Literal (production, _) ->
            match production with
            | "number"
            | "integer" -> Ok NumericSemantics.number
            | "percentage" when context.PercentagesAllowed -> Ok (NumericSemantics.percentage context.PercentageBasis)
            | _ when
                (basisFor production).IsSome
                && context.PercentagesAllowed
                ->
                Ok (NumericSemantics.percentage (basisFor production))
            | "percentage" -> Error (NumericError.InvalidArguments ("calc", "Percentages are forbidden in this context."))
            | _ when (basisFor production).IsSome -> Error (NumericError.InvalidArguments ("calc", "Percentages are forbidden in this context."))
            | _ -> Ok (NumericSemantics.dimension (dimensionFor production |> Option.get))
        | Reference (production, _, fallback) ->
            inferInScope context sizeAllowed (Literal (production, ""))
            |> Result.bind (fun declared ->
                match fallback with
                | None -> Ok declared
                | Some value ->
                    inferInScope context sizeAllowed value
                    |> Result.bind (fun actual ->
                        if NumericSemantics.matches context declared actual then
                            NumericSemantics.add declared actual
                        else
                            Error (NumericError.IncompatibleTypes (declared, actual))))
        | Interpolate (progress, _, _, stops) ->
            let collect values =
                values
                |> List.fold
                    (fun state value ->
                        state
                        |> Result.bind (fun result -> value |> Result.map (fun item -> item :: result)))
                    (Ok [])
                |> Result.map List.rev

            let source, timeline =
                match progress with
                | CalculationProgress.Value value -> inferInScope NumericSemantics.ordinaryContext sizeAllowed value, false
                | CalculationProgress.Timeline (CalculationTimeline.Named (_, dimension)) -> Ok (NumericSemantics.dimension dimension), true
                | CalculationProgress.Timeline _ -> Ok (NumericSemantics.dimension NumericDimension.Length), true

            let insets =
                match progress with
                | CalculationProgress.Timeline (CalculationTimeline.View (_, insets)) ->
                    let insetContext =
                        { NumericSemantics.ordinaryContext with
                            PercentageBasis = Some NumericDimension.Length
                        }

                    insets
                    |> List.choose (function
                        | CalculationInset.Auto -> None
                        | CalculationInset.Value value -> Some value)
                    |> List.map (fun value ->
                        inferInScope insetContext sizeAllowed value
                        |> Result.bind (fun actual ->
                            if NumericSemantics.matchesDimension insetContext NumericDimension.Length actual then
                                Ok ()
                            else
                                Error (NumericError.InvalidArguments ("view", "View insets must have length-percentage type."))))
                    |> collect
                    |> Result.map ignore
                | _ -> Ok ()

            insets
            |> Result.bind (fun () ->
                source
                |> Result.bind (fun sourceType ->
                    stops
                    |> List.collect (fun stop -> stop.Positions)
                    |> List.map (inferInScope NumericSemantics.ordinaryContext sizeAllowed)
                    |> collect
                    |> Result.bind (fun positions ->
                        stops
                        |> List.map (fun stop -> inferInScope context sizeAllowed stop.Value)
                        |> collect
                        |> Result.bind (NumericSemantics.inferInterpolation context sourceType timeline positions))))
        | Size when sizeAllowed -> Ok (NumericSemantics.dimension NumericDimension.Length)
        | Size -> Error (NumericError.InvalidArguments ("size", "The size keyword requires an intrinsic calculation with an established basis."))
        | Constant _ -> Ok NumericSemantics.number
        | Function ("calc", [ CalculationArgument.Value child ]) -> inferInScope context sizeAllowed child
        | Function (name, arguments) ->
            arguments
            |> List.fold
                (fun state argument ->
                    state
                    |> Result.bind (fun result ->
                        match argument with
                        | CalculationArgument.Value value ->
                            inferInScope context sizeAllowed value
                            |> Result.map (fun numeric -> NumericArgument.Value numeric :: result)
                        | CalculationArgument.NoClamp -> Ok (NumericArgument.NoClamp :: result)
                        | CalculationArgument.RandomKey _ -> Ok (NumericArgument.RandomKey :: result)
                        | CalculationArgument.WeightedValue (value, weight) ->
                            inferInScope context sizeAllowed value
                            |> Result.map (fun numeric ->
                                NumericArgument.WeightedValue (numeric, weight)
                                :: result)
                        | CalculationArgument.NoneBound -> Ok (NumericArgument.NoneBound :: result)
                        | CalculationArgument.Strategy strategy -> Ok (NumericArgument.Strategy strategy :: result)))
                (Ok [])
            |> Result.bind (
                List.rev
                >> NumericSemantics.inferFunction context name
            )
        | Negate value -> inferInScope context sizeAllowed value
        | Binary (operation, left, right) ->
            inferInScope context sizeAllowed left
            |> Result.bind (fun a ->
                inferInScope context sizeAllowed right
                |> Result.bind (fun b ->
                    match operation with
                    | "+"
                    | "-" -> NumericSemantics.add a b
                    | "*" -> NumericSemantics.multiply a b
                    | _ -> NumericSemantics.divide a b))

    /// <summary>Infers exponent maps and percentage hints, rejecting size keywords outside intrinsic sizing scope.</summary>
    let infer context calculation =
        inferInScope context false calculation

    let private intrinsicZero =
        function
        | Literal (("number" | "integer"), "0") -> Literal ("length", "0px")
        | value -> value

    /// <summary>Creates an intrinsic sizing expression; zero numeric terminals become explicit zero pixel lengths.</summary>
    /// <exception cref="T:System.ArgumentException">A keyword basis is malformed or is the reserved any or forbidden none keyword.</exception>
    let intrinsic basis value =
        let basis =
            match basis with
            | IntrinsicBasis.Keyword name ->
                Generated.identifier false false name |> ignore

                if String.Equals (name, "any", StringComparison.OrdinalIgnoreCase) then
                    invalidArg "basis" "Use the dedicated Any basis."

                if String.Equals (name, "none", StringComparison.OrdinalIgnoreCase) then
                    invalidArg "basis" "The none keyword cannot be used as an intrinsic sizing basis."

                basis
            | IntrinsicBasis.Value value -> IntrinsicBasis.Value (intrinsicZero value)
            | IntrinsicBasis.FitContent value -> IntrinsicBasis.FitContent (intrinsicZero value)
            | IntrinsicBasis.Stretch value -> IntrinsicBasis.Stretch (intrinsicZero value)
            | _ -> basis

        IntrinsicCalculation (basis, intrinsicZero value)

    /// <summary>Lists all intrinsic keyword and fit-content function bases, including nested bases, in stable distinct order.</summary>
    /// <remarks>Function bases are reported as <c>fit-content()</c> or <c>stretch()</c> for destination grammar admission.</remarks>
    let rec usedIntrinsicKeywords (IntrinsicCalculation (basis, _)) =
        match basis with
        | IntrinsicBasis.Keyword name -> [ name ]
        | IntrinsicBasis.Nested nested -> usedIntrinsicKeywords nested
        | IntrinsicBasis.FitContent _ -> [ "fit-content()" ]
        | IntrinsicBasis.Stretch _ -> [ "stretch()" ]
        | _ -> []

    /// <summary>Serializes an intrinsic sizing expression without resolving its browser layout basis.</summary>
    let rec renderIntrinsic (IntrinsicCalculation (basis, value)) =
        let basisText =
            match basis with
            | IntrinsicBasis.Any -> "any"
            | IntrinsicBasis.Keyword name -> name
            | IntrinsicBasis.Value value -> serialize value
            | IntrinsicBasis.Nested nested -> renderIntrinsic nested
            | IntrinsicBasis.FitContent value
            | IntrinsicBasis.Stretch value ->
                let functionName =
                    match basis with
                    | IntrinsicBasis.Stretch _ -> "stretch"
                    | _ -> "fit-content"

                let text =
                    match value with
                    | Literal _
                    | Function _
                    | Interpolate _ -> serialize value
                    | _ -> render value

                functionName + "(" + text + ")"

        "calc-size("
        + basisText
        + ", "
        + serialize value
        + ")"

    /// <summary>Validates intrinsic basis admission and scoped length arithmetic before creating a sizing declaration.</summary>
    /// <param name="name">The destination property name, validated by declaration construction.</param>
    /// <param name="allowedIntrinsicKeywords">Admitted basis keywords, with <c>fit-content()</c> or <c>stretch()</c> for function bases.</param>
    /// <param name="value">The intrinsic calculation; an Any basis forbids its scoped size keyword.</param>
    /// <exception cref="T:System.ArgumentException">A basis is inadmissible or the calculation fails length typing.</exception>
    let declarationIntrinsic name allowedIntrinsicKeywords value =
        let context =
            { NumericSemantics.ordinaryContext with
                PercentageBasis = Some NumericDimension.Length
            }

        let invalid reason =
            Error (NumericError.InvalidArguments ("calc-size", reason))

        let length scope value =
            inferInScope context scope value
            |> Result.bind (fun actual ->
                NumericSemantics.inferFunction context "calc" [ NumericArgument.Value actual ]
                |> Result.bind (fun completed ->
                    if NumericSemantics.matchesDimension context NumericDimension.Length completed then
                        Ok ()
                    else
                        invalid "Intrinsic basis and calculations must resolve to length-percentage."))

        let rec validate (IntrinsicCalculation (basis, calculation)) =
            let basisCheck =
                match basis with
                | IntrinsicBasis.Any -> Ok ()
                | IntrinsicBasis.Keyword keyword ->
                    if List.contains keyword allowedIntrinsicKeywords then
                        Ok ()
                    else
                        invalid (
                            "The destination does not admit intrinsic basis "
                            + keyword
                            + "."
                        )
                | IntrinsicBasis.Value value -> length false value
                | IntrinsicBasis.Nested nested -> validate nested
                | IntrinsicBasis.FitContent value ->
                    if List.contains "fit-content()" allowedIntrinsicKeywords then
                        length false value
                    else
                        invalid "The destination does not admit a fit-content function basis."
                | IntrinsicBasis.Stretch value ->
                    if List.contains "stretch()" allowedIntrinsicKeywords then
                        length false value
                    else
                        invalid "The destination does not admit a stretch function basis."

            basisCheck
            |> Result.bind (fun () -> length (basis <> IntrinsicBasis.Any) calculation)

        match validate value with
        | Ok () -> Declaration.create name (renderIntrinsic value)
        | Error error -> invalidArg "value" (sprintf "Invalid intrinsic CSS calculation: %A" error)

    /// <summary>Validates the completed calculation against its property context and expected primitive before serialization.</summary>
    /// <remarks>Numeric arithmetic and range handling remain deferred to CSS computed-value resolution.</remarks>
    /// <exception cref="T:System.ArgumentException">The calculation fails CSS type inference or does not match the expected primitive.</exception>
    let declaration (name: string) (context: NumericContext) (expected: NumericType) (calculation: Calculation) : Declaration =
        let completed =
            infer context calculation
            |> Result.bind (fun actual -> NumericSemantics.inferFunction context "calc" [ NumericArgument.Value actual ])

        match completed with
        | Ok actual when NumericSemantics.matches context expected actual -> Declaration.create name (render calculation)
        | Ok _ -> invalidArg "calculation" "CSS calculation does not match the property's numeric production."
        | Error error -> invalidArg "calculation" (sprintf "Invalid CSS calculation: %A" error)
