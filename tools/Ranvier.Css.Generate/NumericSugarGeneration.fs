namespace Ranvier.Css.Spec

open System
open System.Text
open System.Text.Json

/// <summary>Projects the numeric domain into the unified primitive, function and property helpers.</summary>
module internal NumericSugarGeneration =
    let private literal (value: string) =
        JsonSerializer.Serialize value

    let private dimensionName dimension =
        string dimension |> _.ToLowerInvariant()

    let private unitName dimension =
        if dimension = NumericDimension.Percent then
            "CalculationUnits.percent"
        else
            "CalculationUnits." + dimensionName dimension

    let private measure (numeric: NumericType) =
        if numeric.Exponents.IsEmpty then
            "1"
        else
            numeric.Exponents
            |> Map.toList
            |> List.map (fun (dimension, power) ->
                unitName dimension
                + (if power = 1 then "" else "^" + string power))
            |> String.concat " * "

    let private contextCode (context: CalculationContext) =
        "({ PercentagesAllowed = "
        + (if context.PercentagesAllowed then "true" else "false")
        + "; PercentageBasis = "
        + (context.PercentageBasis
           |> Option.map (fun basis ->
               "Some global.Ranvier.Css.Spec.NumericDimension."
               + string basis)
           |> Option.defaultValue "None")
        + " } : global.Ranvier.Css.Spec.NumericContext)"

    let private expectedCode (numeric: NumericType) =
        if numeric.Exponents.IsEmpty then
            "global.Ranvier.Css.Spec.NumericSemantics.number"
        else
            match numeric.Exponents |> Map.toList with
            | [ dimension, 1 ] ->
                "(global.Ranvier.Css.Spec.NumericSemantics.dimension global.Ranvier.Css.Spec.NumericDimension."
                + string dimension
                + ")"
            | _ -> invalidOp "A property context must have one primitive numeric result."

    let private inputs (context: CalculationContext) =
        let result = measure context.ResultType

        [
            if context.Production <> "percentage" then
                yield result, "CalculationPlain"
            if context.PercentagesAllowed then
                match context.PercentageBasis with
                | Some basis ->
                    yield result, "CalculationPercentBasis<" + unitName basis + ">"
                    yield result, "CalculationPercentage"
                    yield "CalculationUnits.percent", "CalculationPercentage"
                | None ->
                    if
                        context.Production = "percentage"
                        || context.ResultType.Exponents.IsEmpty
                    then
                        yield result, "CalculationPercentage"
        ]
        |> List.distinct

    /// <summary>Returns the unique statically accepted result types and their authoritative runtime contexts.</summary>
    let cases (property: NumericProperty) =
        property.Contexts
        |> List.collect (fun context ->
            inputs context
            |> List.map (fun (unit, hint) -> unit, hint, context))
        |> List.distinctBy (fun (unit, hint, _) -> unit, hint)

    let private tag property index =
        property.ModuleName + "_" + string index

    /// <summary>Emits overload discriminators so CLR measure erasure retains unambiguous F# property calls.</summary>
    let overloadTypes properties =
        let source = StringBuilder ()

        source.AppendLine "/// <summary>Generated overload identities preserving distinct numeric measures after CLR erasure.</summary>"
        |> ignore

        source.AppendLine "module CalculationOverloads ="
        |> ignore

        source.AppendLine "    /// <summary>Discriminates angle and unitless trigonometric arguments.</summary>"
        |> ignore

        source.AppendLine "    type Angle private () = class end"
        |> ignore

        for property in properties do
            cases property
            |> List.iteri (fun index _ ->
                source.AppendLine "    /// <summary>Identifies one numeric result accepted by a specification property.</summary>"
                |> ignore

                source.AppendLine (
                    "    type ``"
                    + tag property index
                    + "`` private () = class end"
                )
                |> ignore)

        source.ToString ()

    /// <summary>Emits static and tracked-reader property overloads that validate through the shared domain.</summary>
    let propertyMembers propertyName canonical (property: NumericProperty) =
        let source = StringBuilder ()

        cases property
        |> List.iteri (fun index (unit, hint, context) ->
            let input =
                "CalculationExpression<"
                + unit
                + ", "
                + hint
                + ">"

            let dummy =
                ", ?numeric: CalculationOverloads.``"
                + tag property index
                + "``"

            let body =
                "global.Ranvier.Css.CalculationRuntime.declaration "
                + literal property.Name
                + " "
                + contextCode context
                + " "
                + expectedCode context.ResultType
                + " value.Calculation"

            let emit parameter expression =
                source.AppendLine
                    "        /// <summary>Accepts a calculation whose numeric result matches this property's specification context.</summary>"
                |> ignore

                if property.Obsolete then
                    source.AppendLine "        [<System.Obsolete(\"This specification property is obsolete.\")>]"
                    |> ignore

                source.AppendLine (
                    "        static member inline ``"
                    + propertyName
                    + "`` ("
                    + parameter
                    + dummy
                    + ") = "
                    + expression
                )
                |> ignore

            emit ("value: " + input) body

            emit
                ("read: 'instance -> " + input)
                ("global.Ranvier.Css.Style.dynamicWithKeywords "
                 + canonical
                 + "cssWideKeywords "
                 + literal property.Name
                 + " (fun instance -> let value = read instance in "
                 + body
                 + ")"))

        source.ToString ()

    /// <summary>Emits the math functions on Css using their domain policies for validation and serialization.</summary>
    let functionMembers () =
        let supported =
            Set.ofList
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
                    "log"
                    "exp"
                    "progress"
                    "random"
                    "calc-mix"
                    "calc-interpolate"
                    "sibling-count"
                    "sibling-index"
                ]

        let unsupported =
            NumericSemantics.functionPolicies
            |> List.filter (fun policy -> not (Set.contains policy.Name supported))

        if not unsupported.IsEmpty then
            invalidOp (
                "Numeric function policies require binding projections: "
                + (unsupported
                   |> List.map _.Name
                   |> String.concat ", ")
            )

        let source = StringBuilder ()

        let line (value: string) =
            source.AppendLine value |> ignore

        let expression unit hint body =
            "NumericFactory.expression<"
            + unit
            + ", "
            + hint
            + "> ("
            + body
            + ")"

        let input unit hint =
            "ICalculationExpression<"
            + unit
            + ", "
            + hint
            + ">"

        let call name args =
            "global.Ranvier.Css.CalculationRuntime.functionCall "
            + literal name
            + " "
            + args

        let arguments name args =
            "global.Ranvier.Css.CalculationRuntime.functionArguments "
            + literal name
            + " "
            + args

        let emit name parameters unit hint body =
            line "    /// <summary>Constructs a CSS numeric expression; the shared numeric domain validates its contextual argument types.</summary>"

            line (
                "    static member ``"
                + name
                + "`` ("
                + parameters
                + ") = "
                + expression unit hint body
            )

        let unary name unit result =
            emit name ("value: " + input unit "'hint") result "'hint" (call name "[value.Calculation]")

        let rec combinations count =
            if count = 0 then
                [ [] ]
            else
                [
                    for tail in combinations (count - 1) do
                        for hint in [ "plain"; "basis"; "raw" ] do
                            yield hint :: tail
                ]

        let mixedWith name (argumentNames: string list) output extraParameters makeCall =
            for hints in combinations argumentNames.Length do
                if List.distinct hints |> List.length > 1 then
                    let hasRaw = List.contains "raw" hints
                    let basis = if hasRaw then "'unit" else "'basis"
                    let resultHint = "CalculationPercentBasis<" + basis + ">"

                    let parameters =
                        List.zip argumentNames hints
                        |> List.map (fun (argument, hint) ->
                            let unit = if hint = "raw" then "CalculationUnits.percent" else "'unit"

                            let hintType =
                                match hint with
                                | "raw" -> "CalculationPercentage"
                                | "plain" -> "CalculationPlain"
                                | _ -> resultHint

                            argument + ": " + input unit hintType)
                        |> String.concat ", "

                    emit
                        name
                        (parameters + extraParameters)
                        output
                        resultHint
                        (makeCall (
                            "["
                            + (argumentNames
                               |> List.map (fun name -> name + ".Calculation")
                               |> String.concat "; ")
                            + "]"
                        ))

        let mixed name names output =
            mixedWith name names output "" (call name)

        for token in NumericSemantics.constants do
            let name =
                match token with
                | "-infinity" -> "negativeInfinity"
                | "NaN" -> "nan"
                | value -> value

            line "    /// <summary>A symbolic CSS calculation constant, evaluated by the browser.</summary>"

            line (
                "    static member ``"
                + name
                + "`` = "
                + expression
                    "1"
                    "CalculationPlain"
                    ("global.Ranvier.Css.CalculationRuntime.constant "
                     + literal token)
            )

        for name in [ "calc"; "abs" ] do
            unary name "'unit" "'unit"

        unary "sign" "'unit" "1"

        for name in [ "sqrt"; "exp"; "asin"; "acos"; "atan" ] do
            unary
                name
                "1"
                (if List.contains name [ "asin"; "acos"; "atan" ] then
                     "CalculationUnits.angle"
                 else
                     "1")

        for name in [ "sin"; "cos"; "tan" ] do
            unary name "1" "1"

            emit
                name
                ("value: "
                 + input "CalculationUnits.angle" "'hint"
                 + ", ?angle: CalculationOverloads.Angle")
                "1"
                "'hint"
                (call name "[value.Calculation]")

        for name in [ "min"; "max"; "hypot" ] do
            emit
                name
                ("values: #" + input "'unit" "'hint" + " list")
                "'unit"
                "'hint"
                (call name "(values |> List.map (fun value -> value.Calculation))")

            emit
                name
                ("left: "
                 + input "'unit" "'hint"
                 + ", right: "
                 + input "'unit" "'hint")
                "'unit"
                "'hint"
                (call name "[left.Calculation; right.Calculation]")

            mixed name [ "left"; "right" ] "'unit"

        for name in [ "mod"; "rem"; "atan2"; "pow" ] do
            let unit = if name = "pow" then "1" else "'unit"
            let result = if name = "atan2" then "CalculationUnits.angle" else unit

            emit
                name
                ("left: "
                 + input unit "'hint"
                 + ", right: "
                 + input unit "'hint")
                result
                "'hint"
                (call name "[left.Calculation; right.Calculation]")

            if name <> "pow" then
                mixed name [ "left"; "right" ] result

        unary "log" "1" "1"

        emit
            "log"
            ("value: "
             + input "1" "'hint"
             + ", basis: "
             + input "1" "'hint")
            "1"
            "'hint"
            (call "log" "[value.Calculation; basis.Calculation]")

        emit
            "clamp"
            ("minimum: "
             + input "'unit" "'hint"
             + ", value: "
             + input "'unit" "'hint"
             + ", maximum: "
             + input "'unit" "'hint")
            "'unit"
            "'hint"
            (call "clamp" "[minimum.Calculation; value.Calculation; maximum.Calculation]")

        mixed "clamp" [ "minimum"; "value"; "maximum" ] "'unit"

        emit
            "clamp"
            ("value: " + input "'unit" "'hint")
            "'unit"
            "'hint"
            (arguments
                "clamp"
                "[global.Ranvier.Css.CalculationArgument.NoneBound; global.Ranvier.Css.CalculationArgument.Value value.Calculation; global.Ranvier.Css.CalculationArgument.NoneBound]")

        emit
            "clamp"
            ("value: "
             + input "'unit" "'hint"
             + ", minimum: #"
             + input "'unit" "'hint"
             + " option, maximum: #"
             + input "'unit" "'hint"
             + " option")
            "'unit"
            "'hint"
            (arguments
                "clamp"
                "[minimum |> Option.map (fun value -> global.Ranvier.Css.CalculationArgument.Value value.Calculation) |> Option.defaultValue global.Ranvier.Css.CalculationArgument.NoneBound; global.Ranvier.Css.CalculationArgument.Value value.Calculation; maximum |> Option.map (fun value -> global.Ranvier.Css.CalculationArgument.Value value.Calculation) |> Option.defaultValue global.Ranvier.Css.CalculationArgument.NoneBound]")

        emit "round" ("value: " + input "1" "'hint") "1" "'hint" (call "round" "[value.Calculation]")

        emit
            "round"
            ("value: "
             + input "'unit" "'hint"
             + ", step: "
             + input "'unit" "'hint"
             + ", ?strategy: global.Ranvier.Css.Spec.RoundingStrategy")
            "'unit"
            "'hint"
            (arguments
                "round"
                "[(strategy |> Option.defaultValue global.Ranvier.Css.Spec.RoundingStrategy.Nearest |> global.Ranvier.Css.CalculationArgument.Strategy); global.Ranvier.Css.CalculationArgument.Value value.Calculation; global.Ranvier.Css.CalculationArgument.Value step.Calculation]")

        mixedWith "round" [ "value"; "step" ] "'unit" ", ?strategy: global.Ranvier.Css.Spec.RoundingStrategy" (fun values ->
            arguments
                "round"
                ("(global.Ranvier.Css.CalculationArgument.Strategy(defaultArg strategy global.Ranvier.Css.Spec.RoundingStrategy.Nearest) :: ("
                 + values
                 + " |> List.map global.Ranvier.Css.CalculationArgument.Value))"))

        emit
            "roundLineWidth"
            ("value: "
             + input "CalculationUnits.length" "'hint")
            "CalculationUnits.length"
            "'hint"
            (arguments
                "round"
                "[global.Ranvier.Css.CalculationArgument.Strategy global.Ranvier.Css.Spec.RoundingStrategy.LineWidth; global.Ranvier.Css.CalculationArgument.Value value.Calculation]")

        emit
            "progress"
            ("value: "
             + input "'unit" "'hint"
             + ", start: "
             + input "'unit" "'hint"
             + ", finish: "
             + input "'unit" "'hint"
             + ", ?noClamp: bool")
            "1"
            "'hint"
            (arguments
                "progress"
                "[yield! (if defaultArg noClamp false then [global.Ranvier.Css.CalculationArgument.NoClamp] else []); yield global.Ranvier.Css.CalculationArgument.Value value.Calculation; yield global.Ranvier.Css.CalculationArgument.Value start.Calculation; yield global.Ranvier.Css.CalculationArgument.Value finish.Calculation]")

        mixedWith "progress" [ "value"; "start"; "finish" ] "1" ", ?noClamp: bool" (fun values ->
            arguments
                "progress"
                ("((if defaultArg noClamp false then [global.Ranvier.Css.CalculationArgument.NoClamp] else []) @ ("
                 + values
                 + " |> List.map global.Ranvier.Css.CalculationArgument.Value))"))

        emit
            "random"
            ("minimum: "
             + input "'unit" "'hint"
             + ", maximum: "
             + input "'unit" "'hint"
             + ", ?step: "
             + input "'unit" "'hint"
             + ", ?key: global.Ranvier.Css.CalculationRandomKey")
            "'unit"
            "'hint"
            (arguments
                "random"
                "[yield! key |> Option.map (global.Ranvier.Css.CalculationArgument.RandomKey >> List.singleton) |> Option.defaultValue []; yield global.Ranvier.Css.CalculationArgument.Value minimum.Calculation; yield global.Ranvier.Css.CalculationArgument.Value maximum.Calculation; yield! step |> Option.map (fun value -> [global.Ranvier.Css.CalculationArgument.Value value.Calculation]) |> Option.defaultValue []]")

        mixedWith "random" [ "minimum"; "maximum" ] "'unit" ", ?key: global.Ranvier.Css.CalculationRandomKey" (fun values ->
            arguments
                "random"
                ("((key |> Option.map (global.Ranvier.Css.CalculationArgument.RandomKey >> List.singleton) |> Option.defaultValue []) @ ("
                 + values
                 + " |> List.map global.Ranvier.Css.CalculationArgument.Value))"))

        emit
            "calcMix"
            ("values: (#"
             + input "'unit" "'hint"
             + " * float option) list")
            "'unit"
            "'hint"
            (arguments
                "calc-mix"
                "(values |> List.map (fun (value, weight) -> global.Ranvier.Css.CalculationArgument.WeightedValue(value.Calculation, weight)))")

        for name, cssName in [ "siblingCount", "sibling-count"; "siblingIndex", "sibling-index" ] do
            emit name "" "1" "CalculationPlain" (call cssName "[]")

        source.ToString ()

    /// <summary>Describes the allowlisted math member names for static extraction.</summary>
    let functionMetadata () =
        (NumericSemantics.functionPolicies
         |> List.filter (fun policy -> policy.Name <> "calc-interpolate")
         |> List.map (fun policy ->
             let memberName =
                 match policy.Name with
                 | "calc-mix" -> "calcMix"
                 | "sibling-count" -> "siblingCount"
                 | "sibling-index" -> "siblingIndex"
                 | value -> value

             {|
                 memberName = memberName
                 name = policy.Name
             |}))
        @ [
            {|
                memberName = "roundLineWidth"
                name = "round"
            |}
        ]
