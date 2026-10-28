namespace Ranvier.Css.Spec

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

/// <summary>Renders the default unified property surface from canonical, checked conversion paths.</summary>
module internal SugarGeneration =
    let private literal (value: string) =
        JsonSerializer.Serialize value

    let private identifier (value: string) =
        let parts =
            Regex.Matches (value, "[A-Za-z0-9]+")
            |> Seq.cast<Match>
            |> Seq.map _.Value
            |> Seq.toList

        let joined =
            parts
            |> List.map (fun part ->
                Char.ToUpperInvariant(part[0]).ToString()
                + part.Substring (1))
            |> String.concat ""

        let joined =
            if joined = "" then "Value"
            elif Char.IsDigit joined[0] then "N" + joined
            else joined

        Char.ToLowerInvariant(joined[0]).ToString()
        + joined.Substring (1)

    let private quoted (value: string) =
        "``" + value + "``"

    let private suffix (value: string) =
        Convert.ToHexStringLower(SHA256.HashData (Encoding.UTF8.GetBytes value)).Substring(0, 8)

    /// <summary>Appends typed value/reader overloads in the parent of a Generated namespace, keeping the canonical module qualified.</summary>
    let render (namespaceName: string) (manifest: JsonElement) =
        let authoringNamespace =
            if namespaceName.EndsWith (".Generated", StringComparison.Ordinal) then
                namespaceName.Substring (0, namespaceName.Length - 10)
            else
                namespaceName + ".Sugar"

        let qualifiedNamespace =
            namespaceName.Split ('.')
            |> Array.map quoted
            |> String.concat "."

        let source = StringBuilder ()
        let primitiveMetadata = ResizeArray<obj>()
        let numericPrimitives = ResizeArray<obj>()
        let numericProperties = NumericProjection.derive manifest

        let line (text: string) =
            source.Append(text).Append('\n') |> ignore

        let definitions =
            manifest.GetProperty("definitions").EnumerateArray()
            |> Seq.toList

        let text (key: string) (item: JsonElement) =
            item.GetProperty(key).GetString()

        let current =
            definitions
            |> List.filter (fun item ->
                (text "moduleName" item).Contains("_Obsolete_")
                |> not)

        let properties =
            current
            |> List.filter (fun item -> text "kind" item = "Property")

        let wide =
            manifest.GetProperty("cssWideKeywords").EnumerateArray()
            |> Seq.map _.GetString()
            |> Set.ofSeq

        let tokens =
            properties
            |> List.collect (fun item ->
                item.GetProperty("ergonomicKeywords").EnumerateArray()
                |> Seq.map (text "token")
                |> Seq.toList)
            |> Set.ofList
            |> Set.union wide
            |> Set.toList

        let names = Dictionary<string, string>()

        let reserve proposed identity =
            match names.TryGetValue proposed with
            | true, existing when existing = identity -> proposed
            | true, _ ->
                let resolved = proposed + "_" + suffix identity
                names.Add (resolved, identity)
                resolved
            | _ ->
                names.Add (proposed, identity)
                proposed

        let propertyNames =
            properties
            |> List.map (fun item ->
                let api = text "moduleName" item
                let raw = text "name" item
                let scopes = item.GetProperty("scopes").GetArrayLength()

                let proposed =
                    identifier raw
                    + (if scopes = 0 then "" else "_" + suffix api)

                api, reserve proposed ("property:" + api))
            |> Map.ofList

        let keywordNames =
            tokens
            |> List.map (fun token -> token, reserve (identifier token) ("keyword:" + token))
            |> Map.ofList

        let keywordTags =
            tokens
            |> List.map (fun token -> token, "Keyword_" + suffix token)
            |> Map.ofList

        let canonical = "global." + qualifiedNamespace + ".Css."

        let categories =
            ValueCategoryGeneration.render canonical manifest properties keywordTags

        line (
            "\nnamespace "
            + (authoringNamespace.Split ('.')
               |> Array.map quoted
               |> String.concat ".")
        )

        line (NumericRuntimeGeneration.source ())
        line categories.Source
        line (NumericValueGeneration.types ())
        line (ContextualNumericGeneration.types ())
        line (NumericSugarGeneration.overloadTypes numericProperties)
        line "/// <summary>Nominal keyword markers accepted only by properties whose grammars include the keyword.</summary>"
        line "module CssKeywords ="

        line categories.Keywords

        line "/// <summary>Default typed CSS helpers; each property accepts a static value or an instance reader.</summary>"
        line "[<AbstractClass; Sealed>]"
        line "type Css private () ="

        let mutable memberIndent = "    "

        let obsolete (item: JsonElement) =
            let item = item.GetProperty ("obsolete")

            if item.ValueKind = JsonValueKind.String then
                line (
                    memberIndent
                    + "[<System.Obsolete("
                    + literal (item.GetString ())
                    + ")>]"
                )

        let primitives =
            current
            |> List.filter (fun item ->
                text "kind" item = "Type"
                && item.GetProperty("scopes").GetArrayLength() = 0)

        for item in primitives do
            for helper in item.GetProperty("primitiveHelpers").EnumerateArray() do
                let methodName = text "methodName" helper
                let primitive = (text "name" item).Trim('<', '>')

                let proposed =
                    if
                        methodName = "create"
                        || methodName = "value"
                        || (primitive = "integer" && methodName = "fromInt")
                    then
                        if primitive = "string" then
                            "text"
                        else
                            identifier primitive
                    else
                        identifier methodName

                let methodName =
                    reserve
                        proposed
                        ("primitive:"
                         + text "moduleName" item
                         + ":"
                         + methodName)

                let call =
                    canonical
                    + text "moduleName" item
                    + "."
                    + quoted (text "methodName" helper)

                let parameters = text "parameters" helper

                primitiveMetadata.Add (
                    box
                        {|
                            memberName = methodName
                            marker = text "marker" item
                            production = primitive
                            methodName = text "methodName" helper
                            argumentType =
                                if parameters.Contains ("float") then "float"
                                elif parameters.Contains ("int") then "int"
                                elif parameters.Contains ("string") then "string"
                                else "none"
                        |}
                )

                line "    /// <summary>Constructs a typed CSS primitive using the canonical value constructor.</summary>"
                obsolete item

                let rawCall =
                    call
                    + (if helper.GetProperty("takesValue").GetBoolean() then
                           " value"
                       else
                           "")

                let numeric =
                    if
                        parameters.Contains "float"
                        || parameters.Contains "int"
                        || (primitive = "zero" && parameters = "")
                    then
                        NumericRuntimeGeneration.primitive primitive
                    else
                        None

                let body =
                    match numeric with
                    | Some (unit, hint) ->
                        let unitName =
                            if primitive = "percentage" then
                                "%"
                            elif
                                primitive = "number"
                                || primitive = "integer"
                                || primitive = "zero"
                            then
                                ""
                            else
                                text "methodName" helper

                        let production =
                            match primitive with
                            | "zero"
                            | "number-token" -> "number"
                            | "percentage-token" -> "percentage"
                            | name -> name

                        let argument =
                            if parameters = "" then "0.0"
                            elif parameters.Contains "int" then "(float value)"
                            else "value"

                        numericPrimitives.Add (
                            box
                                {|
                                    memberName = methodName
                                    production = production
                                    unitName = unitName
                                    argumentType =
                                        (if parameters = "" then "none"
                                         elif parameters.Contains "int" then "int"
                                         else "float")
                                    hint = (if primitive = "percentage" then "percentage" else "plain")
                                    basis = (null: string)
                                    marker = text "marker" item
                                |}
                        )

                        "NumericFactory.value<"
                        + unit
                        + ", _, "
                        + hint
                        + "> ("
                        + rawCall
                        + ") (global.Ranvier.Css.CalculationRuntime.literal "
                        + literal production
                        + " "
                        + literal unitName
                        + " "
                        + argument
                        + ")"
                    | None -> rawCall

                let wrapper =
                    if numeric.IsSome then
                        categories.NumericWrappers[text "marker" item]
                    else
                        categories.Wrappers[text "marker" item]

                line (
                    "    static member "
                    + quoted methodName
                    + (if parameters = "" then "" else " " + parameters)
                    + " = "
                    + wrapper
                    + " ("
                    + body
                    + ")"
                )

        for dimension in
            NumericSemantics.dimensions
            |> List.filter ((<>) NumericDimension.Percent) do
            let basis = string dimension |> _.ToLowerInvariant()
            let memberName = reserve (basis + "Percent") ("numeric-basis:" + basis)
            line "    /// <summary>Constructs a percentage with an explicit dimension basis for compound arithmetic.</summary>"

            line (
                "    static member ``"
                + memberName
                + "`` (value: float) = NumericFactory.expression<CalculationUnits."
                + basis
                + ", CalculationPercentBasis<CalculationUnits."
                + basis
                + ">> (global.Ranvier.Css.CalculationRuntime.literal "
                + literal (basis + "-percentage")
                + " \"%\" value)"
            )

            numericPrimitives.Add (
                box
                    {|
                        memberName = memberName
                        production = basis + "-percentage"
                        unitName = "%"
                        argumentType = "float"
                        hint = "basis"
                        basis = basis
                        marker = (null: string)
                    |}
            )

        for token in tokens do
            line (
                "    /// <summary>Typed CSS keyword "
                + System.Security.SecurityElement.Escape token
                + ".</summary>"
            )

            line (
                "    static member "
                + quoted keywordNames[token]
                + " = CssKeywords."
                + keywordTags[token]
                + " ()"
            )

        line "/// <summary>Unified numeric functions, opened automatically alongside the existing CSS helpers.</summary>"
        line "[<AutoOpen>]"
        line "module CssCalculations ="
        line "    type Css with"

        NumericSugarGeneration.functionMembers().Split('\n')
        |> Array.iter (fun memberLine ->
            if memberLine <> "" then
                line ("    " + memberLine))

        (NumericValueGeneration.members canonical manifest).Split('\n')
        |> Array.iter (fun memberLine ->
            if memberLine <> "" then
                line ("    " + memberLine))

        ContextualNumericGeneration.members().Split('\n')
        |> Array.iter (fun memberLine ->
            if memberLine <> "" then
                line ("    " + memberLine))

        NumericVariableGeneration.members canonical manifest
        |> fun members ->
            members.Split ('\n')
            |> Array.iter (fun memberLine ->
                if memberLine <> "" then
                    line ("    " + memberLine))

        for item in properties do
            memberIndent <- "        "
            let propertyName = propertyNames[text "moduleName" item]
            line (ContextualNumericGeneration.propertyMembers canonical propertyName item manifest)

            numericProperties
            |> List.tryFind (fun property -> property.ModuleName = text "moduleName" item)
            |> Option.iter (fun property -> line (NumericSugarGeneration.propertyMembers propertyName canonical property))

            let emit parameter body reactive =
                line (
                    if reactive then
                        memberIndent
                        + "/// <summary>Binds a typed instance reader through the property's static CSS variable slot.</summary>"
                    else
                        memberIndent
                        + "/// <summary>Creates a declaration through a checked path in this property's specification grammar.</summary>"
                )

                obsolete item

                line (
                    memberIndent
                    + "static member "
                    + quoted propertyName
                    + " ("
                    + parameter
                    + ") = "
                    + body
                )

            let input = "#CssValueCategories.I" + text "moduleName" item

            let body =
                "CssValueConversions."
                + text "moduleName" item
                + " value"

            emit ("value: " + input) body false
            emit "value: CssValueCategories.ICssWide" body false

            emit
                ("read: 'instance -> " + input)
                ("global.Ranvier.Css.Style.dynamicWithKeywords "
                 + canonical
                 + "cssWideKeywords "
                 + literal (text "name" item)
                 + " (fun instance -> let value = read instance in "
                 + body
                 + ")")
                true

        let sugar =
            JsonSerializer.SerializeToElement
                {|
                    schemaVersion = 2
                    namespaceName = authoringNamespace
                    properties =
                        properties
                        |> List.map (fun item ->
                            {|
                                cssName = text "name" item
                                memberName = propertyNames[text "moduleName" item]
                                scopes =
                                    item.GetProperty("scopes").EnumerateArray()
                                    |> Seq.map _.GetString()
                                    |> Seq.toArray
                                acceptedMarkers =
                                    item.GetProperty("ergonomicValues").EnumerateArray()
                                    |> Seq.map (text "marker")
                                    |> Seq.toArray
                                acceptedValues =
                                    item.GetProperty("ergonomicValues").EnumerateArray()
                                    |> Seq.map (fun value ->
                                        {|
                                            marker = text "marker" value
                                            ranges = value.GetProperty("ranges").Clone()
                                        |})
                                    |> Seq.toArray
                                acceptedKeywords =
                                    Set.union
                                        wide
                                        (item.GetProperty("ergonomicKeywords").EnumerateArray()
                                         |> Seq.map (text "token")
                                         |> Set.ofSeq)
                                    |> Set.toArray
                                obsolete = item.GetProperty("obsolete").ValueKind = JsonValueKind.String
                            |})
                    primitives = primitiveMetadata.ToArray ()
                    keywords =
                        tokens
                        |> List.map (fun token ->
                            {|
                                token = token
                                memberName = keywordNames[token]
                            |})
                |}

        let numericRuntime =
            JsonSerializer.SerializeToElement
                {|
                    schemaVersion = 1
                    namespaceName = authoringNamespace
                    primitiveType = "Css"
                    expressionType = "CalculationExpression"
                    primitiveHelpers = numericPrimitives.ToArray ()
                    functionHelpers = NumericSugarGeneration.functionMetadata ()
                    contextual =
                        {|
                            helpers = ContextualNumericGeneration.metadata ()
                            sinks = ContextualNumericGeneration.sinkMetadata manifest
                        |}
                    variableSources = NumericVariableGeneration.metadata manifest
                    valueAdapters = NumericValueGeneration.metadata manifest
                    constantHelpers =
                        [
                            for name in NumericSemantics.constants do
                                let memberName =
                                    match name with
                                    | "-infinity" -> "negativeInfinity"
                                    | "NaN" -> "nan"
                                    | value -> value

                                yield
                                    {|
                                        memberName = memberName
                                        name = name
                                    |}
                        ]
                |}

        source.ToString (), sugar, numericRuntime
