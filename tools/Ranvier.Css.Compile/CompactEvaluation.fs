namespace Ranvier.Css.Compile

open System
open System.Globalization
open Ranvier.Css
open Ranvier.Css.Spec
open Ranvier.Css.Internal

module internal CompactEvaluation =
    [<RequireQualifiedAccess>]
    type Input =
        | Scalar of obj
        | Value of GrammarRuntime.ValueData * Calculation option
        | Calculation of Calculation
        | Intrinsic of IntrinsicCalculation
        | Variable of name: string * fallback: GrammarRuntime.ValueData option
        | Wide of string
        | Items of Input list
        | Tuple of Input list
        | Omitted
        | Optional of Input
        | Reader
        | Unsupported of string

    [<RequireQualifiedAccess>]
    type Output =
        | Value of GrammarRuntime.ValueData * Calculation option
        | Calculation of Calculation
        | Intrinsic of IntrinsicCalculation
        | Variable of name: string * fallback: GrammarRuntime.ValueData option
        | Wide of string
        | Declaration of Declaration
        | Nodes of StyleNode<unit> list
        | Scalar of obj

    let private fail message =
        invalidOp ("Compact CSS extraction: " + message)

    let private scalar =
        function
        | Input.Scalar value -> value
        | Input.Unsupported reason -> fail ("Unsupported input: " + reason)
        | _ -> fail "Expected a deterministic scalar."

    let private value =
        function
        | Input.Value (value, _) -> value
        | Input.Unsupported reason -> fail ("Unsupported input: " + reason)
        | _ -> fail "Expected a grammar-typed CSS value."

    let private calculation =
        function
        | Input.Calculation calculation
        | Input.Value (_, Some calculation) -> calculation
        | Input.Scalar (:? float as magnitude) -> CalculationRuntime.literal "number" "" magnitude
        | Input.Scalar (:? int as magnitude) -> CalculationRuntime.literal "integer" "" (float magnitude)
        | _ -> fail "Expected a numeric CSS expression."

    let private optional =
        function
        | Input.Omitted
        | Input.Scalar null -> None
        | Input.Optional inner -> Some (value inner)
        | inner -> Some (value inner)

    let private single arguments =
        match arguments with
        | [ argument ] -> argument
        | _ -> fail "Expected one factory argument."

    let private zeroArguments arguments =
        match arguments with
        | []
        | [ Input.Scalar null ]
        | [ Input.Scalar (:? unit) ] -> ()
        | _ -> fail "Expected a factory without arguments."

    let private numericContext production =
        if production = "number" || production = "integer" then
            NumericSemantics.dimensionOnlyContext, NumericSemantics.number
        elif production = "percentage" then
            NumericSemantics.ordinaryContext, NumericSemantics.dimension NumericDimension.Percent
        else
            let mixed = production.EndsWith ("-percentage", StringComparison.Ordinal)

            let basis =
                if mixed then
                    production.Substring (0, production.Length - "-percentage".Length)
                else
                    production

            let dimension =
                NumericSemantics.dimensions
                |> List.tryFind (fun dimension -> dimension.ToString().ToLowerInvariant() = basis)
                |> Option.defaultWith (fun () -> fail ("Unknown numeric production " + production + "."))

            {
                PercentagesAllowed = mixed
                PercentageBasis = if mixed then Some dimension else None
            },
            NumericSemantics.dimension dimension

    let private construct (registry: CompactMetadata.Registry) (factory: CompactMetadata.Factory) arguments =
        let node = registry.Nodes[factory.NodeId]

        let components =
            if node.Kind = 7 then
                match single arguments with
                | Input.Items items -> items |> List.map (value >> Some) |> List.toArray
                | _ -> fail "A repetition factory requires a finite list."
            elif List.contains node.Kind [ 2; 3; 8; 11 ] then
                [| Some (single arguments |> value) |]
            else
                let slots =
                    factory.Slots
                    |> Option.defaultWith (fun () ->
                        node.Children
                        |> List.indexed
                        |> List.choose (fun (index, child) ->
                            if registry.Nodes[child].Kind = 1 && node.Kind <> 6 then
                                None
                            else
                                Some index))

                let arguments =
                    if slots.IsEmpty then
                        zeroArguments arguments
                        []
                    else
                        arguments

                if arguments.Length <> slots.Length then
                    fail "Factory arity does not match emitted parameter slots."

                if
                    slots |> List.distinct |> List.length
                    <> slots.Length
                then
                    fail "Factory parameter slots are duplicated."

                let supplied = List.zip slots arguments |> Map.ofList

                node.Children
                |> List.mapi (fun index child ->
                    let childNode = registry.Nodes[child]

                    if childNode.Kind = 1 && node.Kind = 6 then
                        match Map.tryFind index supplied with
                        | Some (Input.Scalar (:? bool as selected)) ->
                            if selected then
                                Some (GrammarRuntime.token childNode.Text)
                            else
                                None
                        | _ -> fail "An unordered literal slot requires a Boolean selector."
                    elif childNode.Kind = 1 then
                        Some (GrammarRuntime.token childNode.Text)
                    else
                        match Map.tryFind index supplied with
                        | Some argument -> optional argument
                        | None -> fail "Factory parameter metadata omits a nonliteral grammar slot.")
                |> List.toArray

        GrammarRuntime.construct registry.Table factory.NodeId components

    let private invokeFactory (registry: CompactMetadata.Registry) (factory: CompactMetadata.Factory) arguments =
        let id = factory.NodeId

        let production () =
            factory.Production
            |> Option.defaultWith (fun () -> fail "Factory production metadata is missing.")

        let tokens expression =
            let canonical: Value<obj> = expression

            GrammarRuntime.opaque registry.Table id (Generated.text canonical)
            |> fun data -> Output.Value (data, None)

        match factory.Kind.ToLowerInvariant () with
        | "wide" ->
            zeroArguments arguments
            Output.Wide registry.Nodes[id].Text
        | "boolean" ->
            let operation = production ()

            let operands =
                match operation, arguments with
                | "not", [ operand ] -> [| value operand |]
                | ("and" | "or"), [ head; Input.Items tail ] -> head :: tail |> List.map value |> List.toArray
                | _ -> fail "Boolean factories require not(value) or and/or(head, tail)."

            Output.Value (GrammarRuntime.boolean registry.Table id operation operands, None)
        | "token" ->
            zeroArguments arguments
            Output.Value (GrammarRuntime.token registry.Nodes[id].Text, None)
        | "construct" -> Output.Value (construct registry factory arguments, None)
        | "admit" ->
            let input = single arguments

            let retained =
                match input with
                | Input.Value (_, calculation) -> calculation
                | _ -> None

            Output.Value (GrammarRuntime.admit registry.Table id (value input), retained)
        | "opaque" ->
            let text = single arguments |> scalar |> unbox<string>
            Output.Value (GrammarRuntime.opaque registry.Table id text, None)
        | "number"
        | "integer" ->
            let magnitude =
                if factory.ArgumentType = Some "none" then
                    zeroArguments arguments
                    0.
                elif
                    factory.ArgumentType = Some "int"
                    || factory.Kind.Equals ("integer", StringComparison.OrdinalIgnoreCase)
                then
                    float (Convert.ToInt32 (single arguments |> scalar, CultureInfo.InvariantCulture))
                else
                    Convert.ToDouble (single arguments |> scalar, CultureInfo.InvariantCulture)

            let production = production ()
            let unitName = defaultArg factory.UnitName ""
            let data = GrammarRuntime.number registry.Table id magnitude unitName
            Output.Value (data, Some (CalculationRuntime.literal production unitName magnitude))
        | "string" ->
            single arguments
            |> scalar
            |> unbox<string>
            |> Generated.stringValue
            |> tokens
        | "identifier"
        | "customidentifier"
        | "dashedidentifier" ->
            let production =
                factory.Production
                |> Option.defaultWith (fun () ->
                    match factory.Kind.ToLowerInvariant () with
                    | "customidentifier" -> "custom-ident"
                    | "dashedidentifier" -> "dashed-ident"
                    | _ -> "ident")

            let dashed =
                production = "dashed-ident"
                || production = "custom-property-name"

            let custom = production = "custom-ident"

            single arguments
            |> scalar
            |> unbox<string>
            |> Generated.identifier dashed custom
            |> tokens
        | "calc"
        | "calculation" ->
            let calculation = single arguments |> calculation
            let context, expected = numericContext (production ())

            let declaration =
                CalculationRuntime.declaration "--numeric-value" context expected calculation

            Output.Value (GrammarRuntime.opaque registry.Table id (Declaration.value declaration), Some calculation)
        | "numericvariable" ->
            let name, fallback =
                match factory.ArgumentType, arguments with
                | Some "name", [ name ] -> scalar name |> unbox<string>, None
                | Some "nameFallback", [ name; fallback ] -> scalar name |> unbox<string>, Some (calculation fallback)
                | _ -> fail "Numeric variable factories require a name and their declared optional fallback expression."

            let expression = CalculationRuntime.reference (production ()) name fallback
            let context, expected = numericContext (production ())

            let checkedValue =
                CalculationRuntime.declaration "--numeric-value" context expected expression

            Output.Value (GrammarRuntime.opaque registry.Table id (Declaration.value checkedValue), Some expression)
        | kind -> fail ("Unsupported factory kind '" + kind + "'.")

    let private normalizeOwner (registry: CompactMetadata.Registry) (owner: string) =
        if owner = registry.Namespace + ".CssValue.VariableModule" then
            registry.Namespace + ".CssValue.Variable"
        else
            owner

    let private wideKeywords =
        Map.ofList
            [
                "initial", "initial"
                "inherit'", "inherit"
                "unset", "unset"
                "revert", "revert"
                "revertLayer", "revert-layer"
                "revertRule", "revert-rule"
            ]

    let private mathFunctions =
        NumericSemantics.functionPolicies
        |> List.filter (fun policy -> policy.Name <> "calc-interpolate")
        |> List.map (fun policy ->
            let memberName =
                match policy.Name with
                | "calc-mix" -> "calcMix"
                | "sibling-count" -> "siblingCount"
                | "sibling-index" -> "siblingIndex"
                | name -> name

            memberName, policy.Name)
        |> fun functions -> Map.ofList (("roundLineWidth", "round") :: functions)

    let private mathConstants =
        NumericSemantics.constants
        |> List.map (fun token ->
            (match token with
             | "-infinity" -> "negativeInfinity"
             | "NaN" -> "nan"
             | token -> token),
            token)
        |> Map.ofList

    let private mathOwner (registry: CompactMetadata.Registry) owner =
        owner = registry.Namespace + ".CssValue.Numeric.Math"
        || owner = registry.Namespace + ".Css"

    let private mathArgument =
        function
        | Input.Scalar (:? CalculationArgument as argument) -> argument
        | Input.Scalar (:? RoundingStrategy as strategy) -> CalculationArgument.Strategy strategy
        | Input.Scalar (:? CalculationRandomKey as key) -> CalculationArgument.RandomKey key
        | input -> CalculationArgument.Value (calculation input)

    let private invokeMath memberName (parameterNames: string list) (arguments: Input list) =
        let names =
            if List.length parameterNames = List.length arguments then
                parameterNames
            else
                match memberName, arguments.Length with
                | "clamp", 1 -> [ "value" ]
                | "clamp", 3 when
                    arguments
                    |> List.exists (function
                        | Input.Optional _
                        | Input.Omitted -> true
                        | _ -> false)
                    ->
                    [ "value"; "minimum"; "maximum" ]
                | "round", 3 -> [ "value"; "step"; "strategy" ]
                | "progress", 4 -> [ "value"; "start"; "finish"; "noClamp" ]
                | "random", 4 -> [ "minimum"; "maximum"; "step"; "key" ]
                | "random", 3 -> [ "minimum"; "maximum"; "key" ]
                | "calcMix", 1 -> [ "values" ]
                | _ -> []

        let named =
            if names.Length = arguments.Length then
                List.zip names arguments |> Map.ofList
            else
                Map.empty

        let argument name =
            Map.tryFind name named
            |> Option.defaultWith (fun () ->
                fail (
                    "Missing numeric function argument '"
                    + name
                    + "'."
                ))

        let optional name =
            match Map.tryFind name named with
            | None
            | Some Input.Omitted
            | Some (Input.Scalar null) -> None
            | Some (Input.Optional input) -> Some input
            | _ ->
                fail (
                    "Expected an optional numeric function argument '"
                    + name
                    + "'."
                )

        let rec flatten =
            function
            | Input.Items inputs
            | Input.Tuple inputs -> List.collect flatten inputs
            | input -> [ mathArgument input ]

        let ordinary =
            arguments
            |> List.rev
            |> List.skipWhile (function
                | Input.Omitted -> true
                | _ -> false)
            |> List.rev

        let lowered =
            match memberName with
            | "clamp" when List.tryHead names = Some "value" ->
                [
                    optional "minimum"
                    |> Option.map mathArgument
                    |> Option.defaultValue CalculationArgument.NoneBound
                    mathArgument (argument "value")
                    optional "maximum"
                    |> Option.map mathArgument
                    |> Option.defaultValue CalculationArgument.NoneBound
                ]
            | "round" when Map.containsKey "strategy" named ->
                [
                    optional "strategy"
                    |> Option.map mathArgument
                    |> Option.defaultValue (CalculationArgument.Strategy RoundingStrategy.Nearest)
                    mathArgument (argument "value")
                    mathArgument (argument "step")
                ]
            | "roundLineWidth" ->
                [
                    CalculationArgument.Strategy RoundingStrategy.LineWidth
                    mathArgument (single ordinary)
                ]
            | "progress" when not named.IsEmpty ->
                [
                    if
                        optional "noClamp"
                        |> Option.exists (scalar >> unbox<bool>)
                    then
                        yield CalculationArgument.NoClamp
                    yield mathArgument (argument "value")
                    yield mathArgument (argument "start")
                    yield mathArgument (argument "finish")
                ]
            | "random" when not named.IsEmpty ->
                [
                    yield!
                        optional "key"
                        |> Option.map (mathArgument >> List.singleton)
                        |> Option.defaultValue []
                    yield mathArgument (argument "minimum")
                    yield mathArgument (argument "maximum")
                    yield!
                        optional "step"
                        |> Option.map (mathArgument >> List.singleton)
                        |> Option.defaultValue []
                ]
            | "calcMix" ->
                match single ordinary with
                | Input.Items inputs ->
                    inputs
                    |> List.map (function
                        | Input.Tuple [ input; weight ] ->
                            let weight =
                                match weight with
                                | Input.Omitted
                                | Input.Scalar null -> None
                                | Input.Optional weight -> Some (Convert.ToDouble (scalar weight, CultureInfo.InvariantCulture))
                                | _ -> fail "Expected an optional numeric calc-mix weight."

                            CalculationArgument.WeightedValue (calculation input, weight)
                        | _ -> fail "Expected a numeric calc-mix value and optional weight.")
                | _ -> fail "Expected a finite calc-mix list."
            | "siblingCount"
            | "siblingIndex" ->
                zeroArguments ordinary
                []
            | _ -> List.collect flatten ordinary

        Output.Calculation (CalculationRuntime.functionArguments mathFunctions[memberName] lowered)

    let private variableText name fallback =
        match fallback with
        | None -> "var(" + name + ")"
        | Some (value: GrammarRuntime.ValueData) -> "var(" + name + ", " + value.Text + ")"

    let private contextualMembers =
        set (
            [
                "interpolationPosition"
                "interpolationProgress"
                "interpolationStop"
                "calcInterpolate"
                "intrinsicSize"
                "intrinsicAny"
                "intrinsic"
                "intrinsicKeyword"
                "intrinsicBasis"
                "intrinsicFitContent"
                "intrinsicStretch"
                "calcSize"
            ]
            @ (NumericSemantics.dimensions
               |> List.filter ((<>) NumericDimension.Percent)
               |> List.map (fun dimension ->
                   dimension.ToString().ToLowerInvariant()
                   + "PercentageStop"))
        )

    let private invokeContextual memberName (parameterNames: string list) (arguments: Input list) =
        let named =
            if parameterNames.Length = arguments.Length then
                List.zip parameterNames arguments |> Map.ofList
            else
                Map.empty

        let argument name index =
            Map.tryFind name named
            |> Option.defaultWith (fun () -> arguments[index])

        let optional name =
            match Map.tryFind name named with
            | None
            | Some Input.Omitted
            | Some (Input.Scalar null) -> None
            | Some (Input.Optional value) -> Some value
            | _ ->
                fail (
                    "Expected optional contextual argument '"
                    + name
                    + "'."
                )

        let optionalScalar name =
            optional name |> Option.map scalar

        match memberName with
        | "interpolationPosition"
        | "intrinsic" -> Output.Calculation (calculation (argument "value" 0))
        | "interpolationProgress" -> Output.Scalar (box (CalculationProgress.Value (calculation (argument "value" 0))))
        | "interpolationStop"
        | "lengthPercentageStop"
        | "anglePercentageStop"
        | "timePercentageStop"
        | "frequencyPercentageStop"
        | "resolutionPercentageStop"
        | "flexPercentageStop" ->
            Output.Scalar (
                box
                    {
                        Positions =
                            calculation (argument "position" 0)
                            :: (optional "secondPosition"
                                |> Option.map (calculation >> List.singleton)
                                |> Option.defaultValue [])
                        Value = calculation (argument "value" 1)
                        EasingBefore =
                            optionalScalar "easingBefore"
                            |> Option.map unbox<CalculationEasing>
                    }
            )
        | "calcInterpolate" ->
            let stops =
                match argument "stops" 1 with
                | Input.Items values ->
                    values
                    |> List.map (scalar >> unbox<CalculationInterpolationStop>)
                | _ -> fail "Expected interpolation stops."

            Output.Calculation (
                CalculationRuntime.interpolate
                    (scalar (argument "progress" 0)
                     |> unbox<CalculationProgress>)
                    (optionalScalar "easing"
                     |> Option.map unbox<CalculationEasing>)
                    (optionalScalar "defaultEasing"
                     |> Option.map unbox<CalculationEasing>)
                    stops
            )
        | "intrinsicSize" ->
            zeroArguments arguments
            Output.Calculation CalculationRuntime.size
        | "intrinsicAny" ->
            zeroArguments arguments
            Output.Scalar (box IntrinsicBasis.Any)
        | "intrinsicKeyword" -> Output.Scalar (box (IntrinsicBasis.Keyword (scalar (argument "name" 0) |> unbox<string>)))
        | "intrinsicBasis" ->
            let basis =
                match argument "value" 0 with
                | Input.Intrinsic intrinsic -> IntrinsicBasis.Nested intrinsic
                | value -> IntrinsicBasis.Value (calculation value)

            Output.Scalar (box basis)
        | "intrinsicFitContent" -> Output.Scalar (box (IntrinsicBasis.FitContent (calculation (argument "value" 0))))
        | "intrinsicStretch" -> Output.Scalar (box (IntrinsicBasis.Stretch (calculation (argument "value" 0))))
        | "calcSize" ->
            Output.Intrinsic (
                CalculationRuntime.intrinsic
                    (scalar (argument "basis" 0)
                     |> unbox<IntrinsicBasis>)
                    (calculation (argument "calculation" 1))
            )
        | _ -> fail "Unknown contextual numeric helper."

    let private intrinsicDeclaration (registry: CompactMetadata.Registry) (property: CompactMetadata.Property) intrinsic =
        let admits (node: CompactMetadata.NodeMetadata) =
            try
                GrammarRuntime.admit registry.Table property.NodeId (GrammarRuntime.opaque registry.Table node.Id "contextual")
                |> ignore

                true
            with :? ArgumentException ->
                false

        let functions =
            registry.Nodes
            |> Map.toSeq
            |> Seq.map snd
            |> Seq.filter (fun node -> node.Kind = 9 && admits node)
            |> Seq.map _.Text
            |> Set.ofSeq

        if not (Set.contains "calc-size" functions) then
            fail "The property grammar does not admit a whole calc-size expression."

        let keywords =
            [
                yield!
                    registry.Nodes
                    |> Map.toSeq
                    |> Seq.map snd
                    |> Seq.filter (fun node ->
                        node.Kind = 1
                        && node.Text <> "none"
                        && admits node)
                    |> Seq.map _.Text
                for functionName in [ "fit-content"; "stretch" ] do
                    if Set.contains functionName functions then
                        yield functionName + "()"
            ]
            |> List.distinct
            |> List.sort

        CalculationRuntime.declarationIntrinsic property.CssName keywords intrinsic

    let canInvoke (registry: CompactMetadata.Registry) owner memberName =
        let owner = normalizeOwner registry owner

        Map.containsKey (owner, memberName) registry.Properties
        || Map.containsKey (owner, memberName) registry.Factories
        || (owner = registry.Namespace + ".CssValue"
            && (List.contains memberName [ "render"; "variable"; "var" ]
                || Map.containsKey memberName wideKeywords))
        || (owner = registry.Namespace + ".Css"
            && memberName = "variable")
        || (owner = registry.Namespace + ".CssValue.Variable"
            && List.contains memberName [ "create"; "withFallback"; "declaration" ])
        || (owner.StartsWith (registry.Namespace + ".CssValue.Variable`", StringComparison.Ordinal)
            && List.contains memberName [ "Name"; "Text" ])
        || (owner = registry.Namespace + ".CssValue.CssWide"
            && Map.containsKey memberName wideKeywords)
        || (owner = registry.Namespace + ".CssValue.Wide"
            && memberName = "Text")
        || (mathOwner registry owner
            && (Map.containsKey memberName mathFunctions
                || Map.containsKey memberName mathConstants))
        || (mathOwner registry owner
            && Set.contains memberName contextualMembers)
        || (owner.StartsWith (registry.Namespace + ".CssValue.Numeric.", StringComparison.Ordinal)
            && List.contains
                memberName
                [
                    "Calculation"
                    "Intrinsic"
                    "Basis"
                    "Stop"
                    "op_UnaryNegation"
                    "op_Addition"
                    "op_Subtraction"
                    "op_Multiply"
                    "op_Division"
                ])

    let tryInvokeWithParameters (registry: CompactMetadata.Registry) owner memberName parameterNames (arguments: Input list) =
        let owner = normalizeOwner registry owner
        let primitiveFactory = Map.tryFind (owner, memberName) registry.Factories

        let scalarFactoryCall =
            match primitiveFactory with
            | Some factory ->
                let kind = factory.Kind.ToLowerInvariant ()

                let scalarKind =
                    List.contains
                        kind
                        [
                            "number"
                            "integer"
                            "string"
                            "identifier"
                            "customidentifier"
                            "dashedidentifier"
                        ]

                scalarKind
                && (match arguments with
                    | [ Input.Scalar _ ] -> true
                    | [] when factory.ArgumentType = Some "none" -> true
                    | _ -> false)
            | None -> false

        let variableCall =
            ((owner = registry.Namespace + ".CssValue"
              && List.contains memberName [ "variable"; "var" ])
             || (owner = registry.Namespace + ".Css"
                 && memberName = "variable"))
            && (match arguments with
                | [ Input.Scalar (:? string) ] -> true
                | _ -> false)

        let primitiveCall =
            primitiveFactory.IsSome
            && arguments.Length = 1
            && primitiveFactory.Value.Kind <> "calculation"

        if
            mathOwner registry owner
            && Set.contains memberName contextualMembers
        then
            Some (invokeContextual memberName parameterNames arguments)
        elif scalarFactoryCall then
            Some (invokeFactory registry primitiveFactory.Value arguments)
        elif variableCall then
            let name = single arguments |> scalar |> unbox<string>
            let validated: Value<obj> = Generated.identifier true false name
            ignore validated
            Some (Output.Variable (name, None))
        elif
            mathOwner registry owner
            && Map.containsKey memberName mathConstants
        then
            zeroArguments arguments
            Some (Output.Calculation (CalculationRuntime.constant mathConstants[memberName]))
        elif
            mathOwner registry owner
            && Map.containsKey memberName mathFunctions
            && not primitiveCall
        then
            Some (invokeMath memberName parameterNames arguments)
        else
            match Map.tryFind (owner, memberName) registry.Properties with
            | Some property ->
                match arguments with
                | [ Input.Reader ] ->
                    Some (
                        Output.Nodes
                            [
                                Style.dynamicWithKeywords
                                    [ "initial"; "inherit"; "unset"; "revert"; "revert-layer"; "revert-rule" ]
                                    property.CssName
                                    (fun () -> failwith "A build-time reader must never execute.")
                            ]
                    )
                | [ Input.Value (input, _) ] ->
                    let admitted = GrammarRuntime.admit registry.Table property.NodeId input
                    Some (Output.Declaration (Declaration.create property.CssName admitted.Text))
                | [ Input.Variable (name, fallback) ] ->
                    let fallback =
                        fallback
                        |> Option.map (GrammarRuntime.admit registry.Table property.NodeId)

                    Some (Output.Declaration (Declaration.create property.CssName (variableText name fallback)))
                | [ Input.Wide text ] -> Some (Output.Declaration (Declaration.create property.CssName text))
                | [ Input.Intrinsic intrinsic ] -> Some (Output.Declaration (intrinsicDeclaration registry property intrinsic))
                | _ -> fail "A property requires a typed value, typed variable, CSS-wide keyword, or opaque reader."
            | None ->
                match Map.tryFind (owner, memberName) registry.Factories with
                | Some factory -> Some (invokeFactory registry factory arguments)
                | None when
                    owner = registry.Namespace + ".CssValue"
                    && memberName = "render"
                    ->
                    Some (Output.Scalar (box (single arguments |> value |> fun data -> data.Text)))
                | None when
                    (owner = registry.Namespace + ".CssValue"
                     && List.contains memberName [ "variable"; "var" ])
                    || (owner = registry.Namespace + ".Css"
                        && memberName = "variable")
                    || (owner = registry.Namespace + ".CssValue.Variable"
                        && memberName = "create")
                    ->
                    let name = single arguments |> scalar |> unbox<string>
                    let validated: Value<obj> = Generated.identifier true false name
                    ignore validated
                    Some (Output.Variable (name, None))
                | None when owner = registry.Namespace + ".CssValue.Variable" ->
                    match memberName, arguments with
                    | "withFallback", [ Input.Variable (name, _); fallback ] -> Some (Output.Variable (name, Some (value fallback)))
                    | "declaration", [ Input.Variable (name, _); assigned ] ->
                        Some (Output.Declaration (Declaration.create name (value assigned).Text))
                    | _ -> None
                | None when
                    (owner = registry.Namespace + ".CssValue"
                     || owner = registry.Namespace + ".CssValue.CssWide")
                    && Map.containsKey memberName wideKeywords
                    ->
                    zeroArguments arguments
                    Some (Output.Wide wideKeywords[memberName])
                | None -> None

    let tryInvoke registry owner memberName arguments =
        tryInvokeWithParameters registry owner memberName [] arguments

    let tryOperationWithParameters (registry: CompactMetadata.Registry) (owner: string) memberName parameterNames receiver arguments =
        if owner.StartsWith (registry.Namespace + ".CssValue.Variable`", StringComparison.Ordinal) then
            match memberName, receiver, arguments with
            | "Name", Some (Input.Variable (name, _)), [] -> Some (Output.Scalar (box name))
            | "Text", Some (Input.Variable (name, fallback)), [] -> Some (Output.Scalar (box (variableText name fallback)))
            | _ -> None
        elif
            owner = registry.Namespace + ".CssValue.Wide"
            && memberName = "Text"
        then
            match receiver, arguments with
            | Some (Input.Wide text), [] -> Some (Output.Scalar (box text))
            | _ -> None
        elif owner.StartsWith (registry.Namespace + ".CssValue.Numeric.", StringComparison.Ordinal) then
            match memberName, receiver, arguments with
            | "Intrinsic", Some (Input.Intrinsic intrinsic), [] -> Some (Output.Intrinsic intrinsic)
            | ("Basis" | "Stop"), Some (Input.Scalar value), [] -> Some (Output.Scalar value)
            | "Calculation", Some input, [] -> Some (Output.Calculation (calculation input))
            | "op_UnaryNegation", _, [ input ] -> Some (Output.Calculation (CalculationRuntime.negate (calculation input)))
            | ("op_Addition" | "op_Subtraction" | "op_Multiply" | "op_Division"), _, [ left; right ] ->
                let operation =
                    match memberName with
                    | "op_Addition" -> "+"
                    | "op_Subtraction" -> "-"
                    | "op_Multiply" -> "*"
                    | _ -> "/"

                Some (Output.Calculation (CalculationRuntime.binary operation (calculation left) (calculation right)))
            | _ -> tryInvokeWithParameters registry owner memberName parameterNames arguments
        else
            tryInvokeWithParameters registry owner memberName parameterNames arguments

    let tryOperation registry owner memberName receiver arguments =
        tryOperationWithParameters registry owner memberName [] receiver arguments
