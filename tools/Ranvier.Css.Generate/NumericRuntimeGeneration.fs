namespace Ranvier.Css.Spec

open System
open System.Text

/// <summary>Emits measured arithmetic wrappers shared by generated numeric values and property adapters.</summary>
module NumericRuntimeGeneration =
    let private measureName =
        function
        | NumericDimension.Length -> "length"
        | NumericDimension.Angle -> "angle"
        | NumericDimension.Time -> "time"
        | NumericDimension.Frequency -> "frequency"
        | NumericDimension.Resolution -> "resolution"
        | NumericDimension.Flex -> "flex"
        | NumericDimension.Percent -> "percent"

    /// <summary>Maps a numeric production to its generated measure and retained percentage state.</summary>
    /// <param name="production">A primitive numeric production name, optionally surrounded by angle brackets.</param>
    /// <returns>The measure expression and nominal hint type, or None for a non-numeric production.</returns>
    let primitive (production: string) =
        let production = production.Trim('<', '>').ToLowerInvariant()

        let dimensions =
            NumericSemantics.dimensions
            |> List.map measureName

        let unit name =
            "CalculationUnits." + name

        match production with
        | "number"
        | "integer"
        | "zero"
        | "number-token" -> Some ("1", "CalculationPlain")
        | "percentage"
        | "percentage-token" -> Some (unit "percent", "CalculationPercentage")
        | name when List.contains name dimensions && name <> "percent" -> Some (unit name, "CalculationPlain")
        | name when name.EndsWith ("-percentage", StringComparison.Ordinal) ->
            let basis = name.Substring (0, name.Length - "-percentage".Length)

            if
                List.contains basis dimensions
                && basis <> "percent"
            then
                Some (unit basis, "CalculationPercentBasis<" + unit basis + ">")
            else
                None
        | _ -> None

    /// <summary>Emits namespace-level numeric types, opaque tree factories and measured arithmetic operators.</summary>
    /// <returns>F# declarations to place after the namespace and before generated authoring types.</returns>
    let source () =
        let result = StringBuilder ()

        let line (text: string) =
            result.AppendLine text |> ignore

        let summary text =
            line ("/// <summary>" + text + "</summary>")

        let memberSummary text =
            line ("    /// <summary>" + text + "</summary>")

        let plain = "CalculationPlain"
        let raw = "CalculationPercentage"
        let basis = "CalculationPercentBasis<'b>"

        let expression measure hint =
            "CalculationExpression<"
            + measure
            + ","
            + hint
            + ">"

        let contract measure hint =
            "ICalculationExpression<"
            + measure
            + ","
            + hint
            + ">"

        summary "Base measures used by generated CSS numeric expressions."
        line "module CalculationUnits ="

        for dimension in NumericSemantics.dimensions do
            memberSummary (
                "The CSS "
                + measureName dimension
                + " base dimension."
            )

            line ("    [<Measure>] type " + measureName dimension)

        summary "Marks an expression with no percentage provenance."
        line "type CalculationPlain = class end"
        summary "Marks an expression containing percentages without an explicit dimension basis."
        line "type CalculationPercentage = class end"
        summary "Retains the dimension against which an expression's percentages resolve."
        line "type CalculationPercentBasis<[<Measure>] 'basis> = class end"
        summary "Shares an opaque CSS calculation across primitive values and arithmetic expressions."
        line "type ICalculationExpression<[<Measure>] 'u,'hint> ="
        memberSummary "The unevaluated calculation retained by this value."
        line "    abstract Calculation : global.Ranvier.Css.Calculation"

        let operators valueType =
            let leftType measure hint =
                if valueType then
                    "NumericValue<"
                    + measure
                    + ",'syntax,"
                    + hint
                    + ">"
                else
                    expression measure hint

            let emit operation left right output leftTree rightTree =
                let memberName =
                    if
                        List.contains operation [ "*"; "/" ]
                        && left = leftType "CalculationUnits.percent" raw
                        && right = contract "'v" basis
                    then
                        (if operation = "*" then "op_Multiply" else "op_Division")
                        + "<[<Measure>] 'v,[<Measure>] 'b>"
                    else
                        "(" + operation + ")"

                memberSummary "Constructs unevaluated CSS arithmetic while retaining dimensions and percentage provenance."
                line "    /// <param name=\"left\">The left arithmetic operand.</param>"
                line "    /// <param name=\"right\">The right arithmetic operand.</param>"

                line (
                    "    static member "
                    + memberName
                    + " (left:"
                    + left
                    + ", right:"
                    + right
                    + ") : "
                    + output
                    + " = "
                    + output
                    + "(global.Ranvier.Css.CalculationRuntime.binary \""
                    + operation
                    + "\" "
                    + leftTree
                    + " "
                    + rightTree
                    + ")"
                )

            for policy in NumericSemantics.operatorPolicies do
                let operation = policy.Operator

                match operation with
                | "+"
                | "-" ->
                    for leftHint, rightHint, outputHint in
                        [
                            plain, plain, plain
                            raw, raw, raw
                            basis, basis, basis
                            plain, basis, basis
                            basis, plain, basis
                        ] do
                        emit
                            operation
                            (leftType "'u" leftHint)
                            (contract "'u" rightHint)
                            (expression "'u" outputHint)
                            "left.Calculation"
                            "right.Calculation"

                    for leftUnit, leftHint, rightUnit, rightHint, outputUnit, outputHint in
                        [
                            "'u", plain, "CalculationUnits.percent", raw, "'u", "CalculationPercentBasis<'u>"
                            "CalculationUnits.percent", raw, "'v", plain, "'v", "CalculationPercentBasis<'v>"
                            "'u", "CalculationPercentBasis<'u>", "CalculationUnits.percent", raw, "'u", "CalculationPercentBasis<'u>"
                            "CalculationUnits.percent", raw, "'v", "CalculationPercentBasis<'v>", "'v", "CalculationPercentBasis<'v>"
                        ] do
                        emit
                            operation
                            (leftType leftUnit leftHint)
                            (contract rightUnit rightHint)
                            (expression outputUnit outputHint)
                            "left.Calculation"
                            "right.Calculation"

                    let scalar = "(global.Ranvier.Css.CalculationRuntime.literal \"number\" \"\" "
                    emit operation (leftType "1" "'hint") "float" (expression "1" "'hint") "left.Calculation" (scalar + "right)")
                    emit operation "float" (leftType "1" "'hint") (expression "1" "'hint") (scalar + "left)") "right.Calculation"
                | "*"
                | "/" ->
                    for leftHint, rightHint, outputHint in
                        [
                            plain, plain, plain
                            raw, raw, raw
                            basis, basis, basis
                            plain, raw, raw
                            raw, plain, raw
                            plain, basis, basis
                            basis, plain, basis
                        ] do
                        emit
                            operation
                            (leftType "'u" leftHint)
                            (contract "'v" rightHint)
                            (expression ("'u" + operation + "'v") outputHint)
                            "left.Calculation"
                            "right.Calculation"

                    emit
                        operation
                        (leftType "'u" basis)
                        (contract "CalculationUnits.percent" raw)
                        (expression ("'u" + operation + "'b") basis)
                        "left.Calculation"
                        "right.Calculation"

                    emit
                        operation
                        (leftType "CalculationUnits.percent" raw)
                        (contract "'v" basis)
                        (expression ("'b" + operation + "'v") basis)
                        "left.Calculation"
                        "right.Calculation"

                    let scalar = "(global.Ranvier.Css.CalculationRuntime.literal \"number\" \"\" "
                    emit operation (leftType "'u" "'hint") "float" (expression "'u" "'hint") "left.Calculation" (scalar + "right)")

                    emit
                        operation
                        "float"
                        (leftType "'u" "'hint")
                        (expression (if operation = "*" then "'u" else "1/'u") "'hint")
                        (scalar + "left)")
                        "right.Calculation"
                | other -> invalidOp ("Unsupported numeric operator policy: " + other)

            memberSummary "Negates an expression without evaluating it or dropping percentage provenance."
            line "    /// <param name=\"value\">The expression to negate.</param>"

            line (
                "    static member (~-) (value:"
                + leftType "'u" "'hint"
                + ") : "
                + expression "'u" "'hint"
                + " = "
                + expression "'u" "'hint"
                + "(global.Ranvier.Css.CalculationRuntime.negate value.Calculation)"
            )

        summary "A measured CSS arithmetic expression with opaque runtime storage."
        line "type CalculationExpression<[<Measure>] 'u,'hint> internal (calculation:global.Ranvier.Css.Calculation) ="
        memberSummary "The unevaluated calculation retained by this expression."
        line "    member _.Calculation = calculation"
        line "    interface ICalculationExpression<'u,'hint> with member _.Calculation = calculation"
        operators false
        summary "A canonical typed CSS value that also participates in measured numeric arithmetic."

        line
            "type NumericValue<[<Measure>] 'u,'syntax,'hint> internal (value:global.Ranvier.Css.Value<'syntax>,calculation:global.Ranvier.Css.Calculation) ="

        line "    inherit global.Ranvier.Css.Value<'syntax>(value.Storage)"
        memberSummary "The unevaluated calculation retained by this primitive value."
        line "    member _.Calculation = calculation"
        line "    interface ICalculationExpression<'u,'hint> with member _.Calculation = calculation"
        operators true

        summary
            "Unchecked factory infrastructure used by generated typed numeric constructors; callers must supply correct measure and provenance metadata."

        line "module NumericFactory ="
        memberSummary "Preserves a canonical syntax marker while adding measured arithmetic storage."
        line "    /// <param name=\"value\">The canonical typed numeric value.</param>"
        line "    /// <param name=\"calculation\">Its matching unevaluated numeric terminal.</param>"

        line
            "    let value<[<Measure>] 'u,'syntax,'hint> (value:global.Ranvier.Css.Value<'syntax>) (calculation:global.Ranvier.Css.Calculation) : NumericValue<'u,'syntax,'hint> = NumericValue(value,calculation)"

        memberSummary
            "Wraps an opaque calculation in the measure and provenance selected by generated code; declaration sinks validate the actual numeric type."

        line "    /// <param name=\"calculation\">The unevaluated checked calculation.</param>"

        line
            "    let expression<[<Measure>] 'u,'hint> (calculation:global.Ranvier.Css.Calculation) : CalculationExpression<'u,'hint> = CalculationExpression(calculation)"

        result.ToString().Replace("\r\n", "\n")
