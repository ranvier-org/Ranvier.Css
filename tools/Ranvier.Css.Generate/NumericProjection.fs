namespace Ranvier.Css.Spec

open System
open System.Globalization
open System.Text.Json
open System.Text.Json.Nodes

/// <summary>A complete numeric value accepted by a property's checked grammar conversion path.</summary>
type CalculationContext =
    {
        Production: string
        Marker: string
        ResultType: NumericType
        PercentagesAllowed: bool
        PercentageBasis: NumericDimension option
        IntegerRounding: bool
        Ranges: NumericRange list
    }

/// <summary>Calculation contexts for a property, including its specification scope and obsolescence.</summary>
type NumericProperty =
    {
        Name: string
        ModuleName: string
        Scopes: string list
        Obsolete: bool
        Contexts: CalculationContext list
    }

/// <summary>Projects checked whole-value conversions into numeric contexts for binding generation.</summary>
module NumericProjection =
    let private text (key: string) (item: JsonElement) =
        item.GetProperty(key).GetString()

    let private dimensions =
        [
            "length", NumericDimension.Length
            "angle", NumericDimension.Angle
            "time", NumericDimension.Time
            "frequency", NumericDimension.Frequency
            "resolution", NumericDimension.Resolution
            "flex", NumericDimension.Flex
        ]
        |> Map.ofList

    let private dimensionName dimension =
        string dimension |> _.ToLowerInvariant()

    let private numericType production =
        match production with
        | "number" -> Some (NumericSemantics.number, None, false)
        | "integer" -> Some (NumericSemantics.number, None, true)
        | "percentage" -> Some (NumericSemantics.percentage None, None, false)
        | _ ->
            match dimensions.TryFind production with
            | Some dimension -> Some (NumericSemantics.dimension dimension, None, false)
            | None when production.EndsWith ("-percentage", StringComparison.Ordinal) ->
                let basis = production.Substring (0, production.Length - "-percentage".Length)

                dimensions.TryFind basis
                |> Option.map (fun dimension -> NumericSemantics.percentage (Some dimension), Some dimension, false)
            | None -> None

    let private bound lower (item: JsonElement) =
        if text "kind" item = "finite" then
            RangeBound.Finite (Decimal.Parse (text "value" item, CultureInfo.InvariantCulture), text "unitName" item)
        elif lower then
            RangeBound.NegativeInfinity
        else
            RangeBound.PositiveInfinity

    /// <summary>Derives contexts exclusively from current unscoped numeric productions on complete conversion paths.</summary>
    /// <remarks>Unknown productions retain their ordinary grammar bindings and receive no inferred numeric context.</remarks>
    let derive (manifest: JsonElement) : NumericProperty list =
        let definitions =
            manifest.GetProperty("definitions").EnumerateArray()
            |> Seq.toList

        let definedMarkers =
            definitions
            |> List.filter (fun item ->
                text "kind" item = "Type"
                && item.GetProperty("scopes").GetArrayLength() = 0
                && item.GetProperty("obsolete").ValueKind = JsonValueKind.Null)
            |> List.choose (fun item ->
                let production = (text "name" item).Trim('<', '>')

                numericType production
                |> Option.map (fun numeric -> text "marker" item, (production, numeric)))

        let terminals =
            match manifest.TryGetProperty "primitiveTerminals" with
            | true, terminals ->
                terminals.EnumerateArray ()
                |> Seq.choose (fun terminal ->
                    let production = text "production" terminal

                    numericType production
                    |> Option.map (fun numeric -> text "marker" terminal, (production, numeric)))
                |> Seq.toList
            | _ -> []

        let markers = definedMarkers @ terminals |> Map.ofList

        definitions
        |> List.filter (fun item -> text "kind" item = "Property")
        |> List.map (fun property ->
            let contexts =
                property.GetProperty("ergonomicValues").EnumerateArray()
                |> Seq.choose (fun path ->
                    markers.TryFind (text "marker" path)
                    |> Option.map (fun (production, (resultType, basis, integer)) ->
                        {
                            Production = production
                            Marker = text "marker" path
                            ResultType = resultType
                            PercentagesAllowed = production = "percentage" || basis.IsSome
                            PercentageBasis = basis
                            IntegerRounding = integer
                            Ranges =
                                path.GetProperty("ranges").EnumerateArray()
                                |> Seq.map (fun range ->
                                    {
                                        Minimum = bound true (range.GetProperty "minimum")
                                        Maximum = bound false (range.GetProperty "maximum")
                                    })
                                |> Seq.toList
                        }))
                |> Seq.toList

            let percentagesAllowed = contexts |> List.exists _.PercentagesAllowed

            let bases =
                contexts
                |> List.choose _.PercentageBasis
                |> List.distinct
                |> List.sort

            let bases = if bases.IsEmpty then [ None ] else bases |> List.map Some

            {
                Name = text "name" property
                ModuleName = text "moduleName" property
                Scopes =
                    property.GetProperty("scopes").EnumerateArray()
                    |> Seq.map _.GetString()
                    |> Seq.toList
                Obsolete = property.GetProperty("obsolete").ValueKind = JsonValueKind.String
                Contexts =
                    contexts
                    |> List.collect (fun context ->
                        bases
                        |> List.map (fun basis ->
                            { context with
                                PercentagesAllowed = percentagesAllowed
                                PercentageBasis = basis
                                ResultType =
                                    if context.Production = "percentage" then
                                        NumericSemantics.percentage basis
                                    else
                                        context.ResultType
                            }))
                    |> List.sortBy (fun context -> context.Production, context.Marker, context.PercentageBasis)
            })
        |> List.sortBy (fun property -> property.Name, property.ModuleName)

    /// <summary>Checks a calculation's inferred result against a derived context; range clamping and integer rounding are deferred.</summary>
    let accepts (context: CalculationContext) numericType =
        NumericSemantics.matches
            {
                PercentagesAllowed = context.PercentagesAllowed
                PercentageBasis = context.PercentageBasis
            }
            context.ResultType
            numericType

    let private typeMetadata (numeric: NumericType) =
        {|
            exponents =
                numeric.Exponents
                |> Map.toList
                |> List.map (fun (dimension, exponent) ->
                    {|
                        dimension = dimensionName dimension
                        exponent = exponent
                    |})
            percentHint =
                numeric.PercentHint
                |> Option.map dimensionName
                |> Option.toObj
        |}

    let private boundMetadata =
        function
        | RangeBound.Finite (value, unitName) ->
            {|
                kind = "finite"
                value = value.ToString (CultureInfo.InvariantCulture)
                unitName = unitName
            |}
        | RangeBound.NegativeInfinity ->
            {|
                kind = "negativeInfinity"
                value = ""
                unitName = ""
            |}
        | RangeBound.PositiveInfinity ->
            {|
                kind = "positiveInfinity"
                value = ""
                unitName = ""
            |}

    /// <summary>Serializes the versioned normative numeric policies and their specification URLs.</summary>
    let rulesMetadata () =
        JsonSerializer.SerializeToNode
            {|
                schemaVersion = NumericSemantics.schemaVersion
                sources = NumericSemantics.sources
                dimensions =
                    NumericSemantics.dimensions
                    |> List.map dimensionName
                operators =
                    NumericSemantics.operatorPolicies
                    |> List.map (fun policy ->
                        {|
                            operator = policy.Operator
                            typePolicy = policy.TypePolicy
                            sourceUrls = policy.SourceUrls
                        |})
                functions =
                    NumericSemantics.functionPolicies
                    |> List.map (fun policy ->
                        {|
                            name = policy.Name
                            minimumArguments = policy.MinimumArguments
                            maximumArguments =
                                policy.MaximumArguments
                                |> Option.map box
                                |> Option.toObj
                            argumentPolicy = policy.ArgumentPolicy
                            resultPolicy = policy.ResultPolicy
                            sourceUrls = policy.SourceUrls
                        |})
            |}

    /// <summary>Serializes numeric contexts with deferred range checking and CSS integer rounding policies.</summary>
    let contextsMetadata properties =
        properties
        |> List.map (fun property ->
            {|
                name = property.Name
                moduleName = property.ModuleName
                scopes = property.Scopes
                obsolete = property.Obsolete
                contexts =
                    property.Contexts
                    |> List.map (fun context ->
                        {|
                            production = context.Production
                            marker = context.Marker
                            resultType = typeMetadata context.ResultType
                            percentagesAllowed = context.PercentagesAllowed
                            percentageBasis =
                                context.PercentageBasis
                                |> Option.map dimensionName
                                |> Option.toObj
                            integerRounding =
                                if context.IntegerRounding then
                                    "nearest-ties-positive-infinity"
                                else
                                    null
                            rangeChecking = "computed-value"
                            ranges =
                                context.Ranges
                                |> List.map (fun range ->
                                    {|
                                        minimum = boundMetadata range.Minimum
                                        maximum = boundMetadata range.Maximum
                                    |})
                        |})
            |})

    /// <summary>Adds numeric typing policies and derived property contexts to a generated grammar manifest.</summary>
    let enrich (manifest: string) =
        use document = JsonDocument.Parse manifest
        let properties = derive document.RootElement |> contextsMetadata
        let root = JsonNode.Parse manifest
        let semantics = rulesMetadata ()
        semantics["properties"] <- JsonSerializer.SerializeToNode properties
        root["numericSemantics"] <- semantics

        root.ToJsonString(JsonSerializerOptions (WriteIndented = true)).Replace("\r\n", "\n")
        + "\n"
