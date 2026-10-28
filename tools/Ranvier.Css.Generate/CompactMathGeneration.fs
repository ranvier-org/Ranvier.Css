namespace Ranvier.Css.Spec

open System.Text.RegularExpressions
open System.Text.Json

/// <summary>Measured math declarations for CssValue.Numeric and compatible members for the Css authoring type.</summary>
type internal CompactMathOutput =
    {
        NumericSource: string
        CssMembers: string
    }

/// <summary>Shares the established numeric function projections between compact math and declaration authoring.</summary>
module internal CompactMathGeneration =
    /// <summary>Returns unindented Numeric declarations and Css members indented by four spaces.</summary>
    let render () : CompactMathOutput =
        let members =
            NumericSugarGeneration.functionMembers ()
            + ContextualNumericGeneration.members ()

        let generatedIdentifiers =
            @"(?<![A-Za-z0-9_.])(?:NumericFactory|ICalculationExpression|CalculationExpression|CalculationPlain|CalculationPercentage|CalculationPercentBasis|CalculationUnits|CalculationOverloads|CalculationSizeScope|CalculationScopedAngle|CalculationIntrinsicZero|CalculationPosition|NumericInterpolationStop|IntrinsicBasisValue|IntrinsicValue|IntrinsicExpression)(?![A-Za-z0-9_])"

        let qualified =
            Regex
                .Replace(members, generatedIdentifiers, MatchEvaluator (fun matched -> "CssValue.Numeric." + matched.Value))
                .Replace("= CssValue.Numeric.NumericInterpolationStop", "= new CssValue.Numeric.NumericInterpolationStop")

        {
            NumericSource =
                NumericSugarGeneration.overloadTypes []
                + ContextualNumericGeneration.types ()
                + "/// <summary>Measured CSS math functions retaining unevaluated calculation trees.</summary>\ntype Math =\n"
                + members
            CssMembers = qualified
        }

    /// <summary>Emits intrinsic declaration and reader overloads for complete property grammars admitting calc-size.</summary>
    let intrinsicPropertyMembers (graph: GrammarGraph) nodeId (cssName: string) (memberName: string) =
        let accepted =
            GrammarGraphGeneration.acceptedNodes graph nodeId
            |> Seq.map (fun id -> graph.NodesById[id])
            |> Seq.toList

        let functions =
            accepted
            |> List.filter (fun node -> node.Kind = 9)
            |> List.map _.Text
            |> Set.ofList

        if not (Set.contains "calc-size" functions) then
            ""
        else
            let keywords =
                [
                    yield!
                        accepted
                        |> List.filter (fun node -> node.Kind = 1 && node.Text <> "none")
                        |> List.map _.Text
                    for functionName in [ "fit-content"; "stretch" ] do
                        if Set.contains functionName functions then
                            yield functionName + "()"
                ]
                |> List.distinct
                |> List.sort

            let quote (value: string) =
                JsonSerializer.Serialize value

            let allowed =
                "["
                + (keywords |> List.map quote |> String.concat ";")
                + "]"

            let declaration =
                "global.Ranvier.Css.CalculationRuntime.declarationIntrinsic "
                + quote cssName
                + " "
                + allowed
                + " value.Intrinsic"

            "    /// <summary>Validates intrinsic arithmetic and basis admission against the property grammar.</summary>\n"
            + "    static member ``"
            + memberName
            + "`` (value: CssValue.Numeric.IntrinsicValue) = "
            + declaration
            + "\n"
            + "    /// <summary>Tracks an intrinsic calculation admitted by the complete property grammar.</summary>\n"
            + "    static member ``"
            + memberName
            + "`` (read: 'instance -> CssValue.Numeric.IntrinsicValue) = global.Ranvier.Css.Style.dynamic "
            + quote cssName
            + " (fun instance -> let value = read instance in "
            + declaration
            + ")\n"
