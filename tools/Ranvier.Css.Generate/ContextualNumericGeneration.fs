namespace Ranvier.Css.Spec

open System
open System.Text
open System.Text.Json

/// <summary>Emits typed interpolation and intrinsic-size authoring with explicit destination admission.</summary>
module ContextualNumericGeneration =
    let private literal (value: string) =
        JsonSerializer.Serialize value

    let private kernel = "global.Ranvier.Css."

    let private dimensions =
        NumericSemantics.dimensions
        |> List.filter ((<>) NumericDimension.Percent)
        |> List.map (fun dimension -> string dimension |> _.ToLowerInvariant())

    let private items (name: string) (value: JsonElement) =
        match value.TryGetProperty name with
        | true, values when values.ValueKind = JsonValueKind.Array -> values.EnumerateArray () |> Seq.toList
        | _ -> []

    let private text (name: string) (value: JsonElement) =
        match value.TryGetProperty name with
        | true, field when field.ValueKind = JsonValueKind.String -> field.GetString ()
        | _ -> ""

    let private admission (property: JsonElement) (manifest: JsonElement) =
        let admitted =
            items "ergonomicValues" property
            |> List.map (text "marker")
            |> Set.ofList

        let functions name =
            items "definitions" manifest
            |> List.filter (fun entry ->
                text "name" entry = name
                && List.contains (text "kind" entry) [ "Function"; "Type" ])
            |> List.map (text "marker")

        let wholeFunctions =
            items "ergonomicFunctions" property
            |> List.map _.GetString()
            |> Set.ofList

        let allowsFunction name =
            Set.contains name wholeFunctions
            || (functions (name + "()")
                |> List.exists (fun marker -> Set.contains marker admitted))

        if allowsFunction "calc-size" then
            let keywords =
                items "ergonomicKeywords" property
                |> List.map (text "token")
                |> List.filter (fun keyword -> not (String.Equals (keyword, "none", StringComparison.OrdinalIgnoreCase)))

            let sizingFunctions =
                [
                    for name in [ "fit-content"; "stretch" ] do
                        if allowsFunction name then
                            yield name + "()"
                ]

            Some (
                (keywords @ sizingFunctions)
                |> List.distinct
                |> List.sort
            )
        else
            None

    /// <summary>Emits opaque contextual wrappers and intrinsic arithmetic before the Css authoring type.</summary>
    /// <returns>Namespace-level F# declarations.</returns>
    let types () =
        let source = StringBuilder ()

        let line (value: string) =
            source.AppendLine value |> ignore

        line "/// <summary>Marks a calculation containing the scoped intrinsic size keyword.</summary>"
        line "type CalculationSizeScope = class end"
        line "/// <summary>Discriminates angle-valued scoped trigonometric overloads after measure erasure.</summary>"
        line "type CalculationScopedAngle private () = class end"
        line "/// <summary>Discriminates zero-number intrinsic overloads from length values after measure erasure.</summary>"
        line "type CalculationIntrinsicZero private () = class end"
        line "/// <summary>A numeric interpolation input position with its own calculation context.</summary>"

        line (
            "type CalculationPosition internal (calculation:"
            + kernel
            + "Calculation) ="
        )

        line "    /// <summary>The unevaluated position calculation.</summary>"
        line "    member _.Calculation = calculation"
        line "/// <summary>An interpolation stop retaining the output's dimension and percentage provenance.</summary>"

        line (
            "type NumericInterpolationStop<[<Measure>] 'u,'hint> internal (stop:"
            + kernel
            + "CalculationInterpolationStop) ="
        )

        line "    /// <summary>The structured stop passed to the portable calculation kernel.</summary>"
        line "    member _.Stop = stop"
        line "/// <summary>A structured intrinsic-size basis whose property admission is checked at its destination.</summary>"

        line (
            "type IntrinsicBasisValue internal (basis:"
            + kernel
            + "IntrinsicBasis) ="
        )

        line "    /// <summary>The structured intrinsic-size basis.</summary>"
        line "    member _.Basis = basis"
        line "/// <summary>An intrinsic-size calculation accepted only by properties admitting calc-size().</summary>"

        line (
            "type IntrinsicValue internal (intrinsic:"
            + kernel
            + "IntrinsicCalculation) ="
        )

        line "    /// <summary>The opaque intrinsic calculation.</summary>"
        line "    member _.Intrinsic = intrinsic"
        line "/// <summary>Arithmetic containing a scoped size keyword, excluded from ordinary property result hints.</summary>"

        line (
            "type IntrinsicExpression<[<Measure>] 'u> internal (calculation:"
            + kernel
            + "Calculation) ="
        )

        line "    /// <summary>The unevaluated scoped calculation.</summary>"
        line "    member _.Calculation = calculation"
        line "    interface ICalculationExpression<'u,CalculationSizeScope> with member _.Calculation = calculation"

        let emit op left right output leftTree rightTree =
            line "    /// <summary>Combines scoped arithmetic without evaluating layout-dependent numeric values.</summary>"
            line "    /// <param name=\"left\">The left operand.</param>"
            line "    /// <param name=\"right\">The right operand.</param>"

            line (
                "    static member ("
                + op
                + ") (left:"
                + left
                + ", right:"
                + right
                + ") : IntrinsicExpression<"
                + output
                + "> = IntrinsicExpression("
                + kernel
                + "CalculationRuntime.binary \""
                + op
                + "\" "
                + leftTree
                + " "
                + rightTree
                + ")"
            )

        for op in [ "+"; "-"; "*"; "/" ] do
            let rightUnit = if op = "+" || op = "-" then "'u" else "'v"
            let result = if op = "+" || op = "-" then "'u" else "'u" + op + "'v"

            for hint in [ "CalculationPlain"; "CalculationPercentBasis<CalculationUnits.length>" ] do
                emit
                    op
                    "IntrinsicExpression<'u>"
                    ("ICalculationExpression<"
                     + rightUnit
                     + ","
                     + hint
                     + ">")
                    result
                    "left.Calculation"
                    "right.Calculation"

                emit
                    op
                    ("ICalculationExpression<'u," + hint + ">")
                    ("IntrinsicExpression<" + rightUnit + ">")
                    result
                    "left.Calculation"
                    "right.Calculation"

            emit op "IntrinsicExpression<'u>" ("IntrinsicExpression<" + rightUnit + ">") result "left.Calculation" "right.Calculation"

            let scalar =
                "("
                + kernel
                + "CalculationRuntime.literal \"number\" \"\" "

            let unit = if op = "+" || op = "-" then "1" else "'u"
            emit op ("IntrinsicExpression<" + unit + ">") "float" unit "left.Calculation" (scalar + "right)")
            emit op "float" ("IntrinsicExpression<" + unit + ">") (if op = "/" then "1/'u" else unit) (scalar + "left)") "right.Calculation"

        for op in [ "+"; "-" ] do
            emit
                op
                "IntrinsicExpression<CalculationUnits.length>"
                "ICalculationExpression<CalculationUnits.percent,CalculationPercentage>"
                "CalculationUnits.length"
                "left.Calculation"
                "right.Calculation"

            emit
                op
                "ICalculationExpression<CalculationUnits.percent,CalculationPercentage>"
                "IntrinsicExpression<CalculationUnits.length>"
                "CalculationUnits.length"
                "left.Calculation"
                "right.Calculation"

        for op in [ "*"; "/" ] do
            emit
                op
                "IntrinsicExpression<'u>"
                "ICalculationExpression<CalculationUnits.percent,CalculationPercentage>"
                ("'u" + op + "CalculationUnits.length")
                "left.Calculation"
                "right.Calculation"

            emit
                op
                "ICalculationExpression<CalculationUnits.percent,CalculationPercentage>"
                "IntrinsicExpression<'v>"
                ("CalculationUnits.length" + op + "'v")
                "left.Calculation"
                "right.Calculation"

        line "    /// <summary>Negates scoped intrinsic arithmetic.</summary>"
        line "    /// <param name=\"value\">The scoped operand.</param>"

        line (
            "    static member (~-) (value:IntrinsicExpression<'u>) : IntrinsicExpression<'u> = IntrinsicExpression("
            + kernel
            + "CalculationRuntime.negate value.Calculation)"
        )

        source.ToString().Replace("\r\n", "\n")

    /// <summary>Emits interpolation and intrinsic constructors as members of the unified Css authoring type.</summary>
    /// <returns>F# members indented for the Css type body.</returns>
    let members () =
        let source = StringBuilder ()

        let line (value: string) =
            source.AppendLine value |> ignore

        let emit name parameters body =
            line "    /// <summary>Constructs structured contextual CSS numeric syntax for deferred browser resolution.</summary>"

            line (
                "    static member ``"
                + name
                + "`` ("
                + parameters
                + ") = "
                + body
            )

        let input unit hint =
            "ICalculationExpression<"
            + unit
            + ","
            + hint
            + ">"

        for hint in
            [
                "CalculationPlain"
                "CalculationPercentage"
                "CalculationPercentBasis<'basis>"
            ] do
            emit "interpolationPosition" ("value:" + input "'u" hint) "CalculationPosition(value.Calculation)"

            emit
                "interpolationProgress"
                ("value:" + input "'u" hint)
                (kernel
                 + "CalculationProgress.Value value.Calculation")

        let stopBody unit hint =
            "NumericInterpolationStop<"
            + unit
            + ","
            + hint
            + ">({Positions = position.Calculation :: (secondPosition |> Option.map (fun value -> [value.Calculation]) |> Option.defaultValue []); Value = value.Calculation; EasingBefore = easingBefore})"

        let stopParameters input =
            "position:CalculationPosition, value:"
            + input
            + ", ?secondPosition:CalculationPosition, ?easingBefore:"
            + kernel
            + "CalculationEasing"

        emit "interpolationStop" (stopParameters (input "'u" "'hint")) (stopBody "'u" "'hint")

        for dimension in dimensions do
            let unit = "CalculationUnits." + dimension
            let hint = "CalculationPercentBasis<" + unit + ">"

            for inputUnit, inputHint in
                [
                    unit, "CalculationPlain"
                    unit, hint
                    "CalculationUnits.percent", "CalculationPercentage"
                ] do
                emit (dimension + "PercentageStop") (stopParameters (input inputUnit inputHint)) (stopBody unit hint)

        emit
            "calcInterpolate"
            ("progress:"
             + kernel
             + "CalculationProgress, stops:NumericInterpolationStop<'u,'hint> list, ?easing:"
             + kernel
             + "CalculationEasing, ?defaultEasing:"
             + kernel
             + "CalculationEasing")
            ("NumericFactory.expression<'u,'hint> ("
             + kernel
             + "CalculationRuntime.interpolate progress easing defaultEasing (stops |> List.map (fun stop -> stop.Stop)))")

        line "    /// <summary>The scoped intrinsic size keyword, usable only within calc-size calculations.</summary>"

        line (
            "    static member intrinsicSize = IntrinsicExpression<CalculationUnits.length>("
            + kernel
            + "CalculationRuntime.size)"
        )

        line "    /// <summary>An intrinsic basis with no dependency on an underlying sizing keyword.</summary>"

        line (
            "    static member intrinsicAny = IntrinsicBasisValue("
            + kernel
            + "IntrinsicBasis.Any)"
        )

        for hint in
            [
                "CalculationPlain"
                "CalculationPercentBasis<CalculationUnits.length>"
                "CalculationSizeScope"
            ] do
            emit "intrinsic" ("value:" + input "'u" hint) "IntrinsicExpression<'u>(value.Calculation)"

        emit
            "intrinsic"
            ("value:"
             + input "CalculationUnits.percent" "CalculationPercentage")
            "IntrinsicExpression<CalculationUnits.length>(value.Calculation)"

        emit
            "intrinsicKeyword"
            "name:string"
            ("IntrinsicBasisValue("
             + kernel
             + "IntrinsicBasis.Keyword name)")

        for hint in [ "CalculationPlain"; "CalculationPercentBasis<CalculationUnits.length>" ] do
            emit
                "intrinsicBasis"
                ("value:" + input "CalculationUnits.length" hint)
                ("IntrinsicBasisValue("
                 + kernel
                 + "IntrinsicBasis.Value value.Calculation)")

        emit
            "intrinsicBasis"
            ("value:"
             + input "CalculationUnits.percent" "CalculationPercentage")
            ("IntrinsicBasisValue("
             + kernel
             + "IntrinsicBasis.Value value.Calculation)")

        emit
            "intrinsicBasis"
            ("value:"
             + input "1" "CalculationPlain"
             + ", ?zero:CalculationIntrinsicZero")
            ("IntrinsicBasisValue("
             + kernel
             + "IntrinsicBasis.Value value.Calculation)")

        emit
            "intrinsicBasis"
            "value:IntrinsicValue"
            ("IntrinsicBasisValue("
             + kernel
             + "IntrinsicBasis.Nested value.Intrinsic)")

        for hint in [ "CalculationPlain"; "CalculationPercentBasis<CalculationUnits.length>" ] do
            emit
                "intrinsicFitContent"
                ("value:" + input "CalculationUnits.length" hint)
                ("IntrinsicBasisValue("
                 + kernel
                 + "IntrinsicBasis.FitContent value.Calculation)")

        emit
            "intrinsicFitContent"
            ("value:"
             + input "CalculationUnits.percent" "CalculationPercentage")
            ("IntrinsicBasisValue("
             + kernel
             + "IntrinsicBasis.FitContent value.Calculation)")

        emit
            "intrinsicStretch"
            ("value:"
             + input "CalculationUnits.length" "CalculationPlain")
            ("IntrinsicBasisValue("
             + kernel
             + "IntrinsicBasis.Stretch value.Calculation)")

        emit
            "intrinsicStretch"
            ("value:"
             + input "CalculationUnits.length" "CalculationPercentBasis<CalculationUnits.length>")
            ("IntrinsicBasisValue("
             + kernel
             + "IntrinsicBasis.Stretch value.Calculation)")

        emit
            "intrinsicStretch"
            ("value:"
             + input "CalculationUnits.percent" "CalculationPercentage")
            ("IntrinsicBasisValue("
             + kernel
             + "IntrinsicBasis.Stretch value.Calculation)")

        for hint in
            [
                "CalculationPlain"
                "CalculationPercentBasis<CalculationUnits.length>"
                "CalculationSizeScope"
            ] do
            emit
                "calcSize"
                ("basis:IntrinsicBasisValue, calculation:"
                 + input "CalculationUnits.length" hint)
                ("IntrinsicValue("
                 + kernel
                 + "CalculationRuntime.intrinsic basis.Basis calculation.Calculation)")

        emit
            "calcSize"
            ("basis:IntrinsicBasisValue, calculation:"
             + input "CalculationUnits.percent" "CalculationPercentage")
            ("IntrinsicValue("
             + kernel
             + "CalculationRuntime.intrinsic basis.Basis calculation.Calculation)")

        emit
            "calcSize"
            ("basis:IntrinsicBasisValue, calculation:"
             + input "1" "CalculationPlain"
             + ", ?zero:CalculationIntrinsicZero")
            ("IntrinsicValue("
             + kernel
             + "CalculationRuntime.intrinsic basis.Basis calculation.Calculation)")

        let scoped unit =
            "IntrinsicExpression<" + unit + ">"

        let wrap unit body =
            scoped unit + "(" + body + ")"

        let call name args =
            kernel
            + "CalculationRuntime.functionCall "
            + literal name
            + " "
            + args

        let arguments name args =
            kernel
            + "CalculationRuntime.functionArguments "
            + literal name
            + " "
            + args

        let emit name (parameters: string) (body: string) =
            if parameters.Contains "'hint" then
                for hint in [ "CalculationPlain"; "CalculationPercentBasis<CalculationUnits.length>" ] do
                    emit name (parameters.Replace ("'hint", hint)) body

                if parameters.Contains "ICalculationExpression<'u,'hint>" then
                    let parameters =
                        parameters
                            .Replace("ICalculationExpression<'u,'hint>", "ICalculationExpression<CalculationUnits.percent,CalculationPercentage>")
                            .Replace("'u", "CalculationUnits.length")

                    emit name parameters (body.Replace ("'u", "CalculationUnits.length"))
            else
                emit name parameters body

        for name in
            [
                "calc"
                "abs"
                "sign"
                "sqrt"
                "exp"
                "asin"
                "acos"
                "atan"
                "sin"
                "cos"
                "tan"
            ] do
            let unit =
                if List.contains name [ "calc"; "abs"; "sign" ] then
                    "'u"
                else
                    "1"

            let output =
                if List.contains name [ "calc"; "abs" ] then
                    "'u"
                elif List.contains name [ "asin"; "acos"; "atan" ] then
                    "CalculationUnits.angle"
                else
                    "1"

            emit name ("value:" + scoped unit) (wrap output (call name "[value.Calculation]"))

        for name in [ "sin"; "cos"; "tan" ] do
            emit
                name
                ("value:"
                 + scoped "CalculationUnits.angle"
                 + ", ?angle:CalculationScopedAngle")
                (wrap "1" (call name "[value.Calculation]"))

        for name in [ "min"; "max"; "hypot" ] do
            emit name ("values:" + scoped "'u" + " list") (wrap "'u" (call name "(values |> List.map (fun value -> value.Calculation))"))

            emit
                name
                ("left:"
                 + scoped "'u"
                 + ", right:"
                 + input "'u" "'hint")
                (wrap "'u" (call name "[left.Calculation;right.Calculation]"))

        for name in [ "mod"; "rem"; "atan2"; "pow" ] do
            let unit = if name = "pow" then "1" else "'u"
            let output = if name = "atan2" then "CalculationUnits.angle" else unit

            emit
                name
                ("left:"
                 + scoped unit
                 + ", right:"
                 + input unit "'hint")
                (wrap output (call name "[left.Calculation;right.Calculation]"))

        emit "log" ("value:" + scoped "1") (wrap "1" (call "log" "[value.Calculation]"))

        emit
            "log"
            ("value:"
             + scoped "1"
             + ", basis:"
             + input "1" "'hint")
            (wrap "1" (call "log" "[value.Calculation;basis.Calculation]"))

        emit "round" ("value:" + scoped "1") (wrap "1" (call "round" "[value.Calculation]"))

        emit
            "round"
            ("value:"
             + scoped "'u"
             + ", step:"
             + input "'u" "'hint"
             + ", ?strategy:"
             + kernel
             + "Spec.RoundingStrategy")
            (wrap
                "'u"
                (arguments
                    "round"
                    ("["
                     + kernel
                     + "CalculationArgument.Strategy(defaultArg strategy "
                     + kernel
                     + "Spec.RoundingStrategy.Nearest);"
                     + kernel
                     + "CalculationArgument.Value value.Calculation;"
                     + kernel
                     + "CalculationArgument.Value step.Calculation]")))

        emit
            "roundLineWidth"
            ("value:" + scoped "CalculationUnits.length")
            (wrap
                "CalculationUnits.length"
                (arguments
                    "round"
                    ("["
                     + kernel
                     + "CalculationArgument.Strategy "
                     + kernel
                     + "Spec.RoundingStrategy.LineWidth;"
                     + kernel
                     + "CalculationArgument.Value value.Calculation]")))

        emit
            "clamp"
            ("minimum:"
             + input "'u" "'hint"
             + ", value:"
             + scoped "'u"
             + ", maximum:"
             + input "'u" "'hint")
            (wrap "'u" (call "clamp" "[minimum.Calculation;value.Calculation;maximum.Calculation]"))

        emit
            "clamp"
            ("value:"
             + scoped "'u"
             + ", ?minimum:"
             + input "'u" "'hint"
             + ", ?maximum:"
             + input "'u" "'hint")
            (wrap
                "'u"
                (arguments
                    "clamp"
                    ("[minimum |> Option.map (fun value -> "
                     + kernel
                     + "CalculationArgument.Value value.Calculation) |> Option.defaultValue "
                     + kernel
                     + "CalculationArgument.NoneBound;"
                     + kernel
                     + "CalculationArgument.Value value.Calculation;maximum |> Option.map (fun value -> "
                     + kernel
                     + "CalculationArgument.Value value.Calculation) |> Option.defaultValue "
                     + kernel
                     + "CalculationArgument.NoneBound]")))

        emit
            "progress"
            ("value:"
             + scoped "'u"
             + ", start:"
             + input "'u" "'hint"
             + ", finish:"
             + input "'u" "'hint"
             + ", ?noClamp:bool")
            (wrap
                "1"
                (arguments
                    "progress"
                    ("[yield! (if defaultArg noClamp false then ["
                     + kernel
                     + "CalculationArgument.NoClamp] else []);yield "
                     + kernel
                     + "CalculationArgument.Value value.Calculation;yield "
                     + kernel
                     + "CalculationArgument.Value start.Calculation;yield "
                     + kernel
                     + "CalculationArgument.Value finish.Calculation]")))

        emit
            "random"
            ("minimum:"
             + scoped "'u"
             + ", maximum:"
             + input "'u" "'hint"
             + ", ?step:"
             + input "'u" "'hint"
             + ", ?key:"
             + kernel
             + "CalculationRandomKey")
            (wrap
                "'u"
                (arguments
                    "random"
                    ("[yield! key |> Option.map ("
                     + kernel
                     + "CalculationArgument.RandomKey >> List.singleton) |> Option.defaultValue [];yield "
                     + kernel
                     + "CalculationArgument.Value minimum.Calculation;yield "
                     + kernel
                     + "CalculationArgument.Value maximum.Calculation;yield! step |> Option.map (fun value -> ["
                     + kernel
                     + "CalculationArgument.Value value.Calculation]) |> Option.defaultValue []]")))

        emit
            "calcMix"
            ("values:(" + scoped "'u" + " * float option) list")
            (wrap
                "'u"
                (arguments
                    "calc-mix"
                    ("(values |> List.map (fun (value,weight) -> "
                     + kernel
                     + "CalculationArgument.WeightedValue(value.Calculation,weight)))")))

        source.ToString().Replace("\r\n", "\n")

    /// <summary>Emits static and tracked-reader intrinsic overloads only for whole-property calc-size grammar paths.</summary>
    /// <param name="namespaceCanonical">The qualified canonical Css module prefix, including its trailing dot.</param>
    /// <param name="propertyName">The unified authoring member name.</param>
    /// <param name="property">The property's canonical manifest definition.</param>
    /// <param name="manifest">The enriched manifest containing whole-value ergonomic conversion paths.</param>
    /// <returns>Property extension members, or an empty string when calc-size is not admitted.</returns>
    let propertyMembers namespaceCanonical propertyName (property: JsonElement) (manifest: JsonElement) =
        match admission property manifest with
        | None -> ""
        | Some keywords ->
            let name = text "name" property

            let allowed =
                "["
                + (keywords |> List.map literal |> String.concat ";")
                + "]"

            let declaration value =
                kernel
                + "CalculationRuntime.declarationIntrinsic "
                + literal name
                + " "
                + allowed
                + " "
                + value

            let obsolete =
                let message = text "obsolete" property

                if message = "" then
                    ""
                else
                    "        [<System.Obsolete("
                    + literal message
                    + ")>]\n"

            "        /// <summary>Accepts an intrinsic-size calculation admitted by this property's whole-value grammar.</summary>\n"
            + obsolete
            + "        static member ``"
            + propertyName
            + "`` (value:IntrinsicValue) = "
            + declaration "value.Intrinsic"
            + "\n"
            + "        /// <summary>Tracks an intrinsic-size calculation through the property's dynamic CSS slot.</summary>\n"
            + obsolete
            + "        static member ``"
            + propertyName
            + "`` (read:'instance -> IntrinsicValue) = "
            + kernel
            + "Style.dynamicWithKeywords "
            + namespaceCanonical
            + "cssWideKeywords "
            + literal name
            + " (fun instance -> let value = read instance in "
            + declaration "value.Intrinsic"
            + ")\n"

    /// <summary>Describes structured contextual numeric helper entry points for static extraction.</summary>
    /// <returns>A serializable object with a functionHelpers allowlist.</returns>
    let metadata () : obj =
        let helpers =
            [
                "interpolationPosition", "position"
                "interpolationProgress", "progress-source"
                "interpolationStop", "interpolation-stop"
                "calcInterpolate", "calc-interpolate"
                "intrinsicSize", "size"
                "intrinsicAny", "any"
                "intrinsic", "intrinsic-expression"
                "intrinsicKeyword", "intrinsic-keyword"
                "intrinsicBasis", "intrinsic-basis"
                "intrinsicFitContent", "fit-content"
                "intrinsicStretch", "stretch"
                "calcSize", "calc-size"
            ]
            @ (dimensions
               |> List.map (fun dimension -> dimension + "PercentageStop", "interpolation-stop"))

        box
            {|
                functionHelpers =
                    helpers
                    |> List.map (fun (memberName, name) ->
                        {|
                            memberName = memberName
                            name = name
                            kind = memberName
                        |})
            |}

    /// <summary>Describes intrinsic destination admission derived from whole-property grammar conversions.</summary>
    /// <param name="manifest">The enriched canonical manifest.</param>
    /// <returns>A serializable list of property names and allowed intrinsic basis keywords.</returns>
    let sinkMetadata (manifest: JsonElement) : obj =
        items "definitions" manifest
        |> List.filter (fun property ->
            text "kind" property = "Property"
            && (items "scopes" property).IsEmpty
            && text "obsolete" property = "")
        |> List.choose (fun property ->
            admission property manifest
            |> Option.map (fun keywords ->
                {|
                    property = text "name" property
                    allowedKeywords = keywords
                |}))
        |> List.sortBy (fun value -> value.property)
        |> box
