namespace Ranvier.Css.Compile

open System
open System.IO
open System.Globalization
open System.Text.Json
open Ranvier.Css
open Ranvier.Css.Spec

module internal Metadata =
    type CssValue = { Value: Value<unit>; Marker: string }

    type Primitive =
        {
            Marker: string
            Production: string
            Method: string
            Argument: string
        }

    type Property =
        {
            Name: string
            Keywords: Set<string>
            Paths: Map<string, ((float * string) option * (float * string) option) list>
            CalculationContexts: (NumericContext * NumericType) list
            IntrinsicKeywords: string list option
        }

    type CalculationPrimitive =
        {
            Production: string
            UnitName: string
            Argument: string
        }

    type VariableSource =
        {
            Kind: string
            Production: string
            Marker: string
        }

    type ValueAdapter =
        {
            Production: string
            Marker: string option
        }

    type ExtractionFactory =
        {
            Kind: string
            InputMarker: string
            OutputMarker: string
            FunctionName: string option
        }

    type Registry =
        {
            Namespace: string
            Compact: CompactMetadata.Registry option
            Primitives: Map<string, Primitive>
            CalculationPrimitives: Map<string, CalculationPrimitive>
            CalculationPrimitiveOwner: string
            CalculationFunctions: Map<string, string>
            CalculationConstants: Map<string, string>
            ContextualHelpers: Map<string, string>
            VariableSources: Map<string, VariableSource list>
            VariableMarkers: Map<string, string>
            ValueAdapters: Map<string, ValueAdapter>
            ExtractionFactories: Map<string * string, ExtractionFactory>
            Keywords: Map<string, string>
            Properties: Map<string, Property>
        }

    let private string (name: string) (item: JsonElement) : string =
        item.GetProperty(name).GetString()

    let private loadLegacy path =
        use document = JsonDocument.Parse (File.ReadAllText path)
        let sugar = document.RootElement.GetProperty "sugar"

        if sugar.GetProperty("schemaVersion").GetInt32() <> 2 then
            failwith "CSS sugar metadata schema 2 is required. Regenerate the specification bindings."

        let bound item =
            if string "kind" item = "finite" then
                Some (Double.Parse (string "value" item, CultureInfo.InvariantCulture), string "unitName" item)
            else
                None

        let range (item: JsonElement) =
            bound (item.GetProperty "minimum"), bound (item.GetProperty "maximum")

        let dimension value =
            NumericSemantics.dimensions
            |> List.tryFind (fun dimension -> String.Equals (dimension.ToString (), value, StringComparison.OrdinalIgnoreCase))
            |> Option.defaultWith (fun () -> invalidOp ("Unknown numeric dimension: " + value))

        let optionalDimension (name: string) (item: JsonElement) =
            let value = item.GetProperty name

            if value.ValueKind = JsonValueKind.Null then
                None
            else
                Some (dimension (value.GetString ()))

        let semantics =
            match document.RootElement.TryGetProperty "numericSemantics" with
            | true, value -> Some value
            | _ -> None

        let contexts =
            semantics
            |> Option.map (fun semantics ->
                semantics.GetProperty("properties").EnumerateArray()
                |> Seq.filter (fun item ->
                    item.GetProperty("scopes").GetArrayLength() = 0
                    && not (item.GetProperty("obsolete").GetBoolean()))
                |> Seq.map (fun item ->
                    string "name" item,
                    item.GetProperty("contexts").EnumerateArray()
                    |> Seq.map (fun context ->
                        let result = context.GetProperty "resultType"

                        ({
                            PercentagesAllowed = context.GetProperty("percentagesAllowed").GetBoolean()
                            PercentageBasis = optionalDimension "percentageBasis" context
                        }
                        : NumericContext),
                        ({
                            Exponents =
                                result.GetProperty("exponents").EnumerateArray()
                                |> Seq.map (fun power -> dimension (string "dimension" power), power.GetProperty("exponent").GetInt32())
                                |> Map.ofSeq
                            PercentHint = optionalDimension "percentHint" result
                        }
                        : NumericType))
                    |> Seq.toList)
                |> Map.ofSeq)
            |> Option.defaultValue Map.empty

        let runtime =
            semantics
            |> Option.bind (fun item ->
                match item.TryGetProperty "runtime" with
                | true, runtime -> Some runtime
                | _ -> None)

        let contextual =
            runtime
            |> Option.bind (fun item ->
                match item.TryGetProperty "contextual" with
                | true, contextual -> Some contextual
                | _ -> None)

        let intrinsicSinks =
            match contextual with
            | Some contextual ->
                contextual.GetProperty("sinks").EnumerateArray()
                |> Seq.map (fun item ->
                    string "property" item,
                    item.GetProperty("allowedKeywords").EnumerateArray()
                    |> Seq.map _.GetString()
                    |> Seq.toList)
                |> Map.ofSeq
            | None -> Map.empty

        let calculationPrimitives =
            match runtime with
            | Some runtime ->
                if
                    runtime.GetProperty("schemaVersion").GetInt32()
                    <> 1
                then
                    invalidOp "CSS calculation metadata schema 1 is required."

                runtime.GetProperty("primitiveHelpers").EnumerateArray()
                |> Seq.map (fun item ->
                    string "memberName" item,
                    {
                        Production = string "production" item
                        UnitName = string "unitName" item
                        Argument = string "argumentType" item
                    })
                |> Map.ofSeq
            | None -> Map.empty

        let helpers name =
            match runtime with
            | Some runtime ->
                match runtime.TryGetProperty (name: string) with
                | true, helpers ->
                    helpers.EnumerateArray ()
                    |> Seq.map (fun helper -> string "memberName" helper, string "name" helper)
                    |> Map.ofSeq
                | _ -> Map.empty
            | None -> Map.empty

        let variableSources =
            match
                runtime
                |> Option.bind (fun item ->
                    match item.TryGetProperty "variableSources" with
                    | true, sources -> Some sources
                    | _ -> None)
            with
            | Some sources ->
                sources.GetProperty("sourceHelpers").EnumerateArray()
                |> Seq.map (fun item ->
                    string "memberName" item,
                    {
                        Kind = string "kind" item
                        Production = string "production" item
                        Marker = string "marker" item
                    })
                |> Seq.toList
            | None -> []

        let valueAdapters =
            match
                runtime
                |> Option.bind (fun item ->
                    match item.TryGetProperty "valueAdapters" with
                    | true, adapters -> Some adapters
                    | _ -> None)
            with
            | Some adapters ->
                adapters.GetProperty("helpers").EnumerateArray()
                |> Seq.map (fun item ->
                    let marker = item.GetProperty "marker"

                    string "memberName" item,
                    {
                        Production = string "production" item
                        Marker =
                            if marker.ValueKind = JsonValueKind.Null then
                                None
                            else
                                Some (marker.GetString ())
                    })
                |> Map.ofSeq
            | None -> Map.empty

        {
            Namespace = string "namespaceName" sugar
            Compact = None
            CalculationPrimitives = calculationPrimitives
            CalculationFunctions = helpers "functionHelpers"
            CalculationConstants = helpers "constantHelpers"
            ValueAdapters = valueAdapters
            ExtractionFactories =
                match document.RootElement.TryGetProperty "extractionFactories" with
                | true, factories ->
                    factories.EnumerateArray ()
                    |> Seq.map (fun item ->
                        let functionName = item.GetProperty "functionName"

                        (string "owner" item, string "memberName" item),
                        {
                            Kind = string "kind" item
                            InputMarker = string "inputMarker" item
                            OutputMarker = string "outputMarker" item
                            FunctionName =
                                if functionName.ValueKind = JsonValueKind.Null then
                                    None
                                else
                                    Some (functionName.GetString ())
                        })
                    |> Map.ofSeq
                | _ -> Map.empty
            VariableSources =
                variableSources
                |> List.groupBy fst
                |> List.map (fun (name, sources) -> name, List.map snd sources)
                |> Map.ofList
            VariableMarkers =
                variableSources
                |> List.map (fun (_, source) -> source.Marker, source.Production)
                |> Map.ofList
            ContextualHelpers =
                match contextual with
                | Some contextual ->
                    contextual.GetProperty("helpers").GetProperty("functionHelpers").EnumerateArray()
                    |> Seq.map (fun item -> string "memberName" item, string "kind" item)
                    |> Map.ofSeq
                | None -> Map.empty
            CalculationPrimitiveOwner =
                match runtime with
                | Some runtime ->
                    let primitiveType =
                        match runtime.TryGetProperty "primitiveType" with
                        | true, value -> value.GetString ()
                        | _ -> "Css"

                    string "namespaceName" runtime
                    + "."
                    + primitiveType
                | None -> ""
            Primitives =
                sugar.GetProperty("primitives").EnumerateArray()
                |> Seq.map (fun item ->
                    string "memberName" item,
                    {
                        Marker = string "marker" item
                        Production = string "production" item
                        Method = string "methodName" item
                        Argument = string "argumentType" item
                    })
                |> Map.ofSeq
            Keywords =
                sugar.GetProperty("keywords").EnumerateArray()
                |> Seq.map (fun item -> string "memberName" item, string "token" item)
                |> Map.ofSeq
            Properties =
                sugar.GetProperty("properties").EnumerateArray()
                |> Seq.filter (fun item ->
                    item.GetProperty("scopes").GetArrayLength() = 0
                    && not (item.GetProperty("obsolete").GetBoolean()))
                |> Seq.map (fun item ->
                    let definition =
                        {
                            Name = string "cssName" item
                            CalculationContexts =
                                contexts.TryFind (string "cssName" item)
                                |> Option.defaultValue []
                            IntrinsicKeywords = intrinsicSinks.TryFind (string "cssName" item)
                            Keywords =
                                item.GetProperty("acceptedKeywords").EnumerateArray()
                                |> Seq.map _.GetString()
                                |> Set.ofSeq
                            Paths =
                                item.GetProperty("acceptedValues").EnumerateArray()
                                |> Seq.map (fun path ->
                                    string "marker" path,
                                    path.GetProperty("ranges").EnumerateArray()
                                    |> Seq.map range
                                    |> Seq.toList)
                                |> Map.ofSeq
                        }

                    string "memberName" item, definition)
                |> Map.ofSeq
        }

    let fromCompact (compact: CompactMetadata.Registry) : Registry =
        {
            Namespace = compact.Namespace
            Compact = Some compact
            Primitives = Map.empty
            CalculationPrimitives = Map.empty
            CalculationPrimitiveOwner = ""
            CalculationFunctions = Map.empty
            CalculationConstants = Map.empty
            ContextualHelpers = Map.empty
            VariableSources = Map.empty
            VariableMarkers = Map.empty
            ValueAdapters = Map.empty
            ExtractionFactories = Map.empty
            Keywords = Map.empty
            Properties = Map.empty
        }

    let load path =
        use document = JsonDocument.Parse (File.ReadAllText path)

        match document.RootElement.TryGetProperty "sugar" with
        | true, _ -> loadLegacy path
        | _ ->
            CompactMetadata.loadJson (document.RootElement.GetRawText ())
            |> fromCompact

    let calculationPrimitive (definition: CalculationPrimitive) (argument: obj) =
        let value =
            match definition.Argument with
            | "none" -> 0.0
            | "float"
            | "int" -> Convert.ToDouble (argument, CultureInfo.InvariantCulture)
            | _ ->
                invalidOp (
                    "Unsupported calculation primitive argument: "
                    + definition.Argument
                )

        CalculationRuntime.literal definition.Production definition.UnitName value

    let calculationDeclaration (definition: Property) value =
        definition.CalculationContexts
        |> List.tryPick (fun (context, expected) ->
            match CalculationRuntime.infer context value with
            | Ok actual when NumericSemantics.matches context expected actual ->
                Some (CalculationRuntime.declaration definition.Name context expected value)
            | _ -> None)
        |> Option.defaultWith (fun () ->
            invalidOp (
                "Unsupported calculation result for "
                + definition.Name
            ))

    let intrinsicDeclaration (definition: Property) value =
        match definition.IntrinsicKeywords with
        | Some allowed -> CalculationRuntime.declarationIntrinsic definition.Name allowed value
        | None ->
            invalidOp (
                "Unsupported intrinsic calculation for "
                + definition.Name
            )

    let valueAdapter (definition: ValueAdapter) calculation =
        definition.Marker
        |> Option.map (fun marker ->
            let context, expected =
                match definition.Production with
                | "number"
                | "integer" -> NumericSemantics.dimensionOnlyContext, NumericSemantics.number
                | "percentage" -> NumericSemantics.ordinaryContext, NumericSemantics.dimension NumericDimension.Percent
                | production ->
                    let mixed = production.EndsWith ("-percentage", StringComparison.Ordinal)

                    let dimensionName =
                        if mixed then
                            production.Substring (0, production.Length - "-percentage".Length)
                        else
                            production

                    let dimension =
                        NumericSemantics.dimensions
                        |> List.find (fun dimension -> dimension.ToString().ToLowerInvariant() = dimensionName)

                    {
                        PercentagesAllowed = mixed
                        PercentageBasis = if mixed then Some dimension else None
                    },
                    NumericSemantics.dimension dimension

            let checkedValue =
                CalculationRuntime.declaration "--numeric-value" context expected calculation

            {
                Value = Generated.value (Declaration.value checkedValue)
                Marker = marker
            })

    let primitive (definition: Primitive) (argument: obj) =
        let value: Value<unit> =
            match definition.Argument, definition.Production with
            | "float", _ ->
                let unitName =
                    if definition.Production = "percentage" then "%"
                    elif definition.Production = "number" then ""
                    else definition.Method

                Generated.number (Convert.ToDouble (argument, CultureInfo.InvariantCulture)) unitName
            | "int", _ -> Generated.number (float (Convert.ToInt32 (argument, CultureInfo.InvariantCulture))) ""
            | "none", "zero" -> Generated.value "0"
            | "string", "string" -> Generated.stringValue (unbox argument)
            | "string", "custom-ident" -> Generated.identifier false true (unbox argument)
            | "string", "dashed-ident"
            | "string", "custom-property-name" -> Generated.identifier true false (unbox argument)
            | "string", "ident" -> Generated.identifier false false (unbox argument)
            | _ -> invalidOp ("Unsupported primitive: " + definition.Production)

        {
            Value = value
            Marker = definition.Marker
        }

    let declaration (definition: Property) (value: CssValue) =
        match definition.Paths.TryFind value.Marker with
        | Some paths ->
            let checkedValue =
                paths
                |> List.fold (fun value (minimum, maximum) -> Generated.range minimum maximum value: Value<unit>) value.Value

            Declaration.create definition.Name (Value.render checkedValue)
        | None when definition.Keywords.Contains (Value.render value.Value) -> Declaration.create definition.Name (Value.render value.Value)
        | None ->
            invalidOp (
                "Unsupported typed conversion for "
                + definition.Name
                + " from "
                + value.Marker
            )
