namespace Ranvier.Css.Spec

open System
open System.Collections.Generic
open System.Globalization
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

/// <summary>Generated source and its machine-readable coverage and naming manifest.</summary>
type GeneratedBindings =
    {
        Source: string
        Manifest: string
        FeatureCount: int
        OpaqueCount: int
        UnresolvedCount: int
    }

/// <summary>Generates typed CSS constructors; recursive references use marker types instead of expansion.</summary>
module Generation =
    type private Plan =
        {
            Tag: string
            Factory: string
            Owner: Feature
            Node: GrammarNode option
            Children: Plan list
        }

    let private literal (value: string) =
        JsonSerializer.Serialize value

    let private name (value: string) =
        let words =
            Regex.Matches (value, "[A-Za-z0-9]+")
            |> Seq.cast<Match>
            |> Seq.map _.Value

        let joined =
            words
            |> Seq.map (fun word ->
                Char.ToUpperInvariant(word[0]).ToString()
                + word.Substring (1))
            |> String.concat ""

        if joined = "" then "Item"
        elif Char.IsDigit joined[0] then "N" + joined
        else joined

    let private digest value =
        Convert.ToHexStringLower(SHA256.HashData (Encoding.UTF8.GetBytes (value: string))).Substring(0, 12)

    let private metadata key (feature: Feature) =
        feature.Metadata
        |> Map.tryFind key
        |> Option.map (fun value ->
            use document = JsonDocument.Parse value

            if document.RootElement.ValueKind = JsonValueKind.String then
                document.RootElement.GetString ()
            else
                value)

    let private obsolete (feature: Feature) =
        [ "obsolete"; "deprecated"; "supersededBy"; "legacyAliasOf" ]
        |> List.tryPick (fun key ->
            metadata key feature
            |> Option.bind (fun value ->
                if value = "" || value = "false" || value = "null" then
                    None
                else
                    Some (
                        if key = "supersededBy" || key = "legacyAliasOf" then
                            "Use " + value + "."
                        else
                            "Obsolete CSS specification item."
                    )))

    /// <summary>Emits all feature constructors and a manifest of opaque definitions and unresolved references.</summary>
    let generate (namespaceName: string) (catalogue: Catalogue) : Result<GeneratedBindings, InputDiagnostic list> =
        if
            String.IsNullOrWhiteSpace namespaceName
            || not (Regex.IsMatch (namespaceName, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$"))
        then
            Error
                [
                    {
                        Path = "namespace"
                        Message = "Expected an F# namespace composed of identifiers."
                    }
                ]
        elif
            (catalogue.Features
             |> List.map _.Id
             |> List.distinct
             |> List.length)
            <> catalogue.Features.Length
        then
            Error
                [
                    {
                        Path = "features"
                        Message = "Duplicate feature identities must be consolidated before generation."
                    }
                ]
        else
            let features = catalogue.Features |> List.sortBy _.Id

            let rec globalLiterals (node: GrammarNode) =
                match node.Expression with
                | GrammarExpression.Literal token -> [ token ]
                | GrammarExpression.Choice nodes -> nodes |> List.collect globalLiterals
                | GrammarExpression.Group child -> globalLiterals child
                | _ -> []

            let cssWideKeywords =
                features
                |> List.tryFind (fun f ->
                    f.Id.Kind = FeatureKind.Property
                    && f.Id.Name = "all"
                    && f.Id.Scopes.IsEmpty)
                |> Option.map (fun f ->
                    match f.Grammar with
                    | GrammarStatus.Parsed (_, tree) -> globalLiterals tree
                    | _ -> [])
                |> Option.filter (fun values -> not values.IsEmpty)
                |> Option.defaultValue [ "initial"; "inherit"; "unset"; "revert"; "revert-layer"; "revert-rule" ]
                |> List.distinct
                |> List.sort

            let resolution = Resolution.resolve catalogue
            let plans = ResizeArray<Plan>()
            let mutable serial = 0

            let tagFor (id: FeatureId) =
                "T"
                + string id.Kind
                + name id.Name
                + "_"
                + digest (
                    string id.Kind
                    + "|"
                    + id.Name
                    + "|"
                    + String.concat "|" id.Scopes
                    + "|"
                    + defaultArg id.Owner ""
                )

            let rec ungroup (node: GrammarNode) =
                match node.Expression with
                | GrammarExpression.Group inner -> ungroup inner
                | _ -> node

            let rec plan (feature: Feature) path node =
                let node = node |> Option.map ungroup
                serial <- serial + 1
                let factory = "n" + string serial

                let children =
                    match node |> Option.map _.Expression with
                    | Some (GrammarExpression.Sequence nodes)
                    | Some (GrammarExpression.Choice nodes)
                    | Some (GrammarExpression.AllOf nodes)
                    | Some (GrammarExpression.AnyOf nodes) -> nodes
                    | Some (GrammarExpression.Group child)
                    | Some (GrammarExpression.Repeat (child, _))
                    | Some (GrammarExpression.Required child)
                    | Some (GrammarExpression.TypeApplication (_, child)) -> [ child ]
                    | Some (GrammarExpression.Function (_, inner))
                    | Some (GrammarExpression.Block (_, inner)) -> Option.toList inner
                    | _ -> []

                let children =
                    children
                    |> List.mapi (fun i child -> plan feature (path + "_" + string (i + 1)) (Some child))

                let value =
                    {
                        Tag = tagFor feature.Id + path
                        Factory = factory
                        Owner = feature
                        Node = node
                        Children = children
                    }

                plans.Add value
                value

            let roots =
                features
                |> List.map (fun feature ->
                    let node =
                        match feature.Grammar with
                        | GrammarStatus.Parsed (_, tree) -> Some tree
                        | _ -> None

                    feature.Id, plan feature "" node)
                |> Map.ofList

            let targetTag (reference: GrammarReference) target =
                match target with
                | ReferenceTarget.Definition id ->
                    let target = roots[id]

                    match reference.Kind, target.Node with
                    | ReferenceKind.Property,
                      Some {
                               Expression = GrammarExpression.Repeat (_, r)
                           } when r.CommaSeparated -> target.Children[0].Tag
                    | _ -> target.Tag
                | _ ->
                    "External_"
                    + digest (string reference.Kind + ":" + reference.Name)

            let bindings =
                resolution.References
                |> List.map (fun useSite ->
                    (useSite.Owner, useSite.Span.Offset, useSite.Span.Length, useSite.Reference.Kind, useSite.Reference.Name), useSite.Target)
                |> Map.ofList

            let lookup (plan: Plan) (reference: GrammarReference) =
                let span = plan.Node.Value.Span

                bindings
                |> Map.tryFind (plan.Owner.Id, span.Offset, span.Length, reference.Kind, reference.Name)
                |> Option.defaultValue ReferenceTarget.Missing

            let extras =
                resolution.References
                |> List.choose (fun useSite ->
                    match useSite.Target with
                    | ReferenceTarget.Definition _ -> None
                    | _ -> Some (targetTag useSite.Reference useSite.Target))
                |> List.distinct
                |> List.sort

            let source = StringBuilder ()

            let line (value: string) =
                source.Append(value).Append('\n') |> ignore

            let quotedName (value: string) =
                String.replicate 2 (string (char 96))
                + value.Replace (string (char 96), "_")
                + String.replicate 2 (string (char 96))

            line "// <auto-generated />"
            line "#nowarn \"44\""

            line (
                "namespace "
                + (namespaceName.Split ('.')
                   |> Array.map quotedName
                   |> String.concat ".")
            )

            line "module Css ="
            line "    /// <summary>CSS-wide keywords from the current all-property specification grammar.</summary>"

            line (
                "    let cssWideKeywords = ["
                + (cssWideKeywords
                   |> List.map literal
                   |> String.concat "; ")
                + "]"
            )

            line
                """    type Value<'syntax> = private Value of text: string * numeric: (float * string) option
    type Declaration = private Declaration of name: string * value: string

    module private Runtime =
        let text (Value(value,_)) = value
        let value text : Value<'tag> = Value(text,None)
        let copy (Value(text,numeric)) : Value<'tag> = Value(text,numeric)
        let number (number:float) unitName : Value<'tag> =
            if System.Double.IsNaN number || System.Double.IsInfinity number then invalidArg "number" "CSS numbers must be finite."
            Value(number.ToString("R",System.Globalization.CultureInfo.InvariantCulture) + unitName,Some(number,unitName))
        let stringValue (input:string) : Value<'tag> =
            if isNull input then nullArg "input"
            let escaped = input.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\d ").Replace("\n","\\a ").Replace("\u000C","\\c ").Replace("\u0000","\\fffd ")
            value ("\"" + escaped + "\"")
        let identifier dashed custom (input:string) : Value<'tag> =
            if System.String.IsNullOrEmpty input then invalidArg "input" "Expected a CSS identifier."
            if dashed && (not (input.StartsWith("--",System.StringComparison.Ordinal)) || input.Length < 3) then invalidArg "input" "Expected a dashed identifier."
            if not (System.Char.IsLetter input[0] || input[0] = '_' || input[0] = '-') ||
               not (input |> Seq.forall (fun c -> System.Char.IsLetterOrDigit c || c = '_' || c = '-')) then
                invalidArg "input" "Expected an unescaped CSS identifier."
            if input[0] = '-' && (input.Length = 1 || (input[1] <> '-' && input[1] <> '_' && not (System.Char.IsLetter input[1]))) then invalidArg "input" "Expected an identifier start after the hyphen."
            if custom && List.contains (input.ToLowerInvariant()) ("default"::cssWideKeywords) then invalidArg "input" "Reserved custom identifier."
            value input
        let sequence (parts:(string * string) list) : Value<'tag> =
            let mutable result = ""
            for separator,part in parts do
                if part <> "" then result <- result + (if result = "" then "" else separator) + part
            value result
        let repeat minimum maximum comma (values:Value<'item> list) : Value<'tag> =
            let count = List.length values
            if count < minimum || maximum |> Option.exists (fun bound -> count > bound) then invalidArg "values" "CSS repetition cardinality is outside its grammar bounds."
            value (values |> List.map text |> String.concat (if comma then ", " else " "))
        let required (value:Value<'item>) : Value<'tag> =
            if System.String.IsNullOrWhiteSpace(text value) then invalidArg "value" "A required group must contain a value."
            copy value
        let unsafeTokens (input:string) : Value<'tag> =
            if System.String.IsNullOrWhiteSpace input then invalidArg "input" "Expected CSS tokens."
            value input
        let range minimum maximum (value:Value<'item>) : Value<'tag> =
            match value with
            | Value(_,Some(number,unitName)) ->
                let convert endpointUnit =
                    if endpointUnit = "" || System.String.Equals(endpointUnit,unitName,System.StringComparison.OrdinalIgnoreCase) then number
                    else
                        let factor (unit:string) =
                            match unit.ToLowerInvariant() with
                            | "deg" -> Some(1.0,"angle") | "rad" -> Some(180.0/System.Math.PI,"angle")
                            | "turn" -> Some(360.0,"angle") | "grad" -> Some(0.9,"angle")
                            | "s" -> Some(1.0,"time") | "ms" -> Some(0.001,"time")
                            | "px" -> Some(1.0,"length") | "in" -> Some(96.0,"length")
                            | "cm" -> Some(96.0/2.54,"length") | "mm" -> Some(96.0/25.4,"length")
                            | "q" -> Some(96.0/101.6,"length")
                            | "pt" -> Some(96.0/72.0,"length") | "pc" -> Some(16.0,"length")
                            | "hz" -> Some(1.0,"frequency") | "khz" -> Some(1000.0,"frequency")
                            | "dppx" | "x" -> Some(1.0,"resolution")
                            | "dpi" -> Some(1.0/96.0,"resolution") | "dpcm" -> Some(2.54/96.0,"resolution")
                            | _ -> None
                        match factor unitName,factor endpointUnit with
                        | Some(a,kind),Some(b,targetKind) when kind = targetKind -> number*a/b
                        | _ when number = 0.0 -> 0.0
                        | _ -> invalidArg "value" "Cannot compare CSS units before computed-value resolution."
                let outside lower = function
                    | None -> false
                    | Some(bound,unit) -> if lower then convert unit < bound else convert unit > bound
                if outside true minimum || outside false maximum then invalidArg "value" "CSS numeric range exceeded."
            | _ -> ()
            copy value

    let renderValue value = Runtime.text value
    let renderDeclaration (Declaration(name,value)) = name + ": " + value + ";"
    let renderDeclarations declarations = declarations |> List.map renderDeclaration |> String.concat " "
"""

            let canonicalPlans = Dictionary<string, Plan>()
            let shapes = Dictionary<string, Plan>()

            for value in plans do
                let children =
                    value.Children
                    |> List.map (fun child -> canonicalPlans[child.Tag].Tag)
                    |> String.concat ","

                let separators =
                    value.Children
                    |> List.pairwise
                    |> List.map (fun (left, right) ->
                        match left.Node, right.Node with
                        | Some a, Some b when b.Span.Offset = a.Span.Offset + a.Span.Length -> "adjacent"
                        | _ -> "space")
                    |> String.concat ","

                let shape =
                    match value.Node |> Option.map _.Expression with
                    | None -> "opaque:" + value.Tag
                    | Some (GrammarExpression.Literal token) -> "literal:" + literal token
                    | Some (GrammarExpression.Reference reference) ->
                        "reference:"
                        + targetTag reference (lookup value reference)
                        + ":"
                        + sprintf "%A" reference.Range
                    | Some (GrammarExpression.Sequence _) -> "sequence:" + separators
                    | Some (GrammarExpression.AllOf _) -> "all:" + separators
                    | Some (GrammarExpression.AnyOf _) -> "any"
                    | Some (GrammarExpression.Choice _) -> "choice"
                    | Some (GrammarExpression.Group _) -> "group"
                    | Some (GrammarExpression.Required _) -> "required"
                    | Some (GrammarExpression.Repeat (_, repetition)) -> "repeat:" + sprintf "%A" repetition
                    | Some (GrammarExpression.Function (name, _)) -> "function:" + literal name
                    | Some (GrammarExpression.Block (opening, _)) -> "block:" + literal opening
                    | Some (GrammarExpression.TypeApplication ("boolean-expr", _)) -> "boolean"
                    | Some (GrammarExpression.TypeApplication _) -> "opaque:" + value.Tag

                let key =
                    literal (obsolete value.Owner |> Option.defaultValue "")
                    + ":"
                    + shape
                    + ":"
                    + children

                let representative =
                    match shapes.TryGetValue key with
                    | true, existing -> existing
                    | _ ->
                        shapes.Add (key, value)
                        value

                canonicalPlans.Add (value.Tag, representative)

            let originalTargetTag = targetTag

            let targetTag reference target =
                let tag = originalTargetTag reference target

                match canonicalPlans.TryGetValue tag with
                | true, representative -> representative.Tag
                | _ -> tag

            let rec normalizePlan value =
                let representative = canonicalPlans[value.Tag]

                { value with
                    Tag = representative.Tag
                    Factory = representative.Factory
                    Children = value.Children |> List.map normalizePlan
                }

            let roots =
                roots
                |> Map.map (fun _ root -> normalizePlan root)

            let sharedPlans =
                plans
                |> Seq.map normalizePlan
                |> Seq.distinctBy _.Tag
                |> Seq.toList

            plans.Clear ()

            for value in sharedPlans do
                plans.Add value

            line "    module Markers ="

            for value in plans do
                match obsolete value.Owner with
                | Some message ->
                    line (
                        "        [<System.Obsolete("
                        + literal message
                        + ")>]"
                    )
                | _ -> ()

                line (
                    "        type "
                    + value.Tag
                    + " private () = class end"
                )

            for tag in extras do
                line ("        type " + tag + " private () = class end")

            if plans.Count = 0 && extras.IsEmpty then
                line "        type Empty private () = class end"

            let valueType tag =
                "Value<Markers." + tag + ">"

            let call plan =
                "Syntax." + plan.Factory

            let arguments children =
                children
                |> List.mapi (fun i child ->
                    "(v"
                    + string (i + 1)
                    + ": "
                    + valueType child.Tag
                    + ")")
                |> String.concat " "

            let variables children =
                children
                |> List.mapi (fun i _ -> "v" + string (i + 1))
                |> String.concat " "

            let parts children =
                children
                |> List.mapi (fun i child ->
                    let separator =
                        if i = 0 then
                            ""
                        else
                            match children[i - 1].Node, child.Node with
                            | Some a, Some b when b.Span.Offset = a.Span.Offset + a.Span.Length -> ""
                            | _ -> " "

                    "("
                    + literal separator
                    + ", Runtime.text v"
                    + string (i + 1)
                    + ")")
                |> String.concat "; "
                |> fun items -> "[" + items + "]"

            let bound =
                function
                | RangeBound.Finite (number, unitName) ->
                    let text = (float number).ToString("R", CultureInfo.InvariantCulture)

                    let text =
                        if text.Contains (".") || text.Contains ("E") then
                            text
                        else
                            text + ".0"

                    "Some(" + text + ", " + literal unitName + ")"
                | _ -> "None"

            let extractionFactories = ResizeArray<obj>()

            let extractionFactory owner memberName kind inputMarker outputMarker functionName =
                extractionFactories.Add (
                    box
                        {|
                            owner = owner
                            memberName = memberName
                            kind = kind
                            inputMarker = inputMarker
                            outputMarker = outputMarker
                            functionName = functionName
                        |}
                )

            let syntaxFactory (value: Plan) suffix kind inputMarker functionName =
                extractionFactory (namespaceName + ".Css.Syntax") (value.Factory + suffix) kind inputMarker value.Tag functionName

            line "    module Syntax ="

            if plans.Count = 0 then
                line "        let private empty = ()"

            for value in plans do
                let output = valueType value.Tag

                let emit suffix args body =
                    line (
                        "        let "
                        + value.Factory
                        + suffix
                        + " "
                        + args
                        + " : "
                        + output
                        + " = "
                        + body
                    )

                let childType () =
                    valueType value.Children[0].Tag

                match value.Node |> Option.map _.Expression with
                | None -> emit "" "(tokens:string)" "Runtime.unsafeTokens tokens"
                | Some (GrammarExpression.Literal token) -> emit "" "" ("Runtime.value " + literal token)
                | Some (GrammarExpression.Reference reference) ->
                    if reference.Range.IsNone then
                        syntaxFactory value "" "copy" (targetTag reference (lookup value reference)) (None: string option)

                    let body =
                        match reference.Range with
                        | Some r ->
                            "Runtime.range ("
                            + bound r.Minimum
                            + ") ("
                            + bound r.Maximum
                            + ") value"
                        | _ -> "Runtime.copy value"

                    emit
                        ""
                        ("(value: "
                         + valueType (targetTag reference (lookup value reference))
                         + ")")
                        body
                | Some (GrammarExpression.Choice _) ->
                    for i, child in value.Children |> List.indexed do
                        emit ("_" + string (i + 1)) ("(value: " + valueType child.Tag + ")") "Runtime.copy value"
                        syntaxFactory value ("_" + string (i + 1)) "copy" child.Tag None
                | Some (GrammarExpression.Sequence _)
                | Some (GrammarExpression.AllOf _) -> emit "" (arguments value.Children) ("Runtime.sequence " + parts value.Children)
                | Some (GrammarExpression.AnyOf _) ->
                    let args =
                        value.Children
                        |> List.mapi (fun i child ->
                            "(v"
                            + string (i + 1)
                            + ": "
                            + valueType child.Tag
                            + " option)")
                        |> String.concat " "

                    let items =
                        value.Children
                        |> List.mapi (fun i _ ->
                            "(v"
                            + string (i + 1)
                            + " |> Option.map Runtime.text)")
                        |> String.concat "; "

                    emit
                        ""
                        args
                        ("let parts = ["
                         + items
                         + "] |> List.choose id |> List.filter (System.String.IsNullOrWhiteSpace >> not) in if parts.IsEmpty then invalidArg \"values\" \"At least one unordered component is required.\" else Runtime.value(String.concat \" \" parts)")
                | Some (GrammarExpression.Group _) -> emit "" ("(value: " + childType () + ")") "Runtime.copy value"
                | Some (GrammarExpression.Required _) -> emit "" ("(value: " + childType () + ")") "Runtime.required value"
                | Some (GrammarExpression.Repeat (_, r)) ->
                    let maximum =
                        r.Maximum
                        |> Option.map (fun n -> "Some " + string n)
                        |> Option.defaultValue "None"

                    emit
                        ""
                        ("(values: " + childType () + " list)")
                        ("Runtime.repeat "
                         + string r.Minimum
                         + " ("
                         + maximum
                         + ") "
                         + r.CommaSeparated.ToString().ToLowerInvariant()
                         + " values")
                | Some (GrammarExpression.Function (functionName, _)) ->
                    if value.Children.IsEmpty then
                        emit "" "" ("Runtime.value " + literal (functionName + "()"))
                    else
                        syntaxFactory value "" "function" value.Children[0].Tag (Some functionName)

                        emit
                            ""
                            ("(value: " + childType () + ")")
                            ("Runtime.value("
                             + literal (functionName + "(")
                             + " + Runtime.text value + \")\")")
                | Some (GrammarExpression.Block (opening, _)) ->
                    let closing = if opening = "{" then "}" else ")"

                    if value.Children.IsEmpty then
                        emit "" "" ("Runtime.value " + literal (opening + closing))
                    else
                        emit
                            ""
                            ("(value: " + childType () + ")")
                            ("Runtime.value("
                             + literal opening
                             + " + Runtime.text value + "
                             + literal closing
                             + ")")
                | Some (GrammarExpression.TypeApplication (generic, _)) when generic = "boolean-expr" ->
                    emit "" ("(value: " + childType () + ")") "Runtime.copy value"
                    emit "_not" ("(value: " + output + ")") "Runtime.value(\"not (\" + Runtime.text value + \")\")"

                    emit
                        "_and"
                        ("(head: "
                         + output
                         + ") (tail: "
                         + output
                         + " list)")
                        "Runtime.value(String.concat \" and \" ((head::tail) |> List.map (fun v -> \"(\"+Runtime.text v+\")\")))"

                    emit
                        "_or"
                        ("(head: "
                         + output
                         + ") (tail: "
                         + output
                         + " list)")
                        "Runtime.value(String.concat \" or \" ((head::tail) |> List.map (fun v -> \"(\"+Runtime.text v+\")\")))"
                | Some (GrammarExpression.TypeApplication _) -> emit "" "(tokens:string)" "Runtime.unsafeTokens tokens"

            let moduleBase (feature: Feature) =
                string feature.Id.Kind
                + (feature.Id.Owner
                   |> Option.map name
                   |> Option.defaultValue "")
                + name feature.Id.Name

            let groups = features |> List.groupBy moduleBase |> Map.ofList
            let entries = ResizeArray<obj>()

            let conversions root =
                let rec emptyExpression current =
                    let combineChildren () =
                        let children = current.Children |> List.map emptyExpression

                        if children |> List.forall Option.isSome then
                            Some (
                                call current
                                + " "
                                + (children
                                   |> List.map (
                                       Option.get
                                       >> fun expression -> "(" + expression + ")"
                                   )
                                   |> String.concat " ")
                            )
                        else
                            None

                    match current.Node |> Option.map _.Expression with
                    | Some (GrammarExpression.Repeat (_, repetition)) when repetition.Minimum = 0 -> Some (call current + " []")
                    | Some (GrammarExpression.Sequence _)
                    | Some (GrammarExpression.AllOf _) -> combineChildren ()
                    | Some (GrammarExpression.Choice _) ->
                        current.Children
                        |> List.indexed
                        |> List.tryPick (fun (index, child) ->
                            emptyExpression child
                            |> Option.map (fun expression ->
                                call current
                                + "_"
                                + string (index + 1)
                                + " ("
                                + expression
                                + ")"))
                    | Some (GrammarExpression.Group _) ->
                        emptyExpression current.Children[0]
                        |> Option.map (fun expression -> call current + " (" + expression + ")")
                    | _ -> None

                let values = Dictionary<string, string * NumericRange list>()
                let keywords = Dictionary<string, string>()
                let functions = HashSet<string>()
                let expanded = HashSet<string>()
                let pending = Queue<Plan * Set<string> * (string -> string) * NumericRange list>()
                values.Add (root.Tag, ("value", []))
                expanded.Add root.Tag |> ignore
                pending.Enqueue (root, Set.singleton root.Tag, id, [])

                while pending.Count > 0 do
                    let current, visited, wrap, guards = pending.Dequeue ()

                    let descend child convert =
                        pending.Enqueue (child, visited, (fun expression -> wrap (convert expression)), guards)

                    match current.Node |> Option.map _.Expression with
                    | Some (GrammarExpression.Choice _) ->
                        for index, child in current.Children |> List.indexed do
                            descend child (fun expression ->
                                call current
                                + "_"
                                + string (index + 1)
                                + " ("
                                + expression
                                + ")")
                    | Some (GrammarExpression.AnyOf _) ->
                        for index, child in current.Children |> List.indexed do
                            descend child (fun expression ->
                                let arguments =
                                    current.Children
                                    |> List.mapi (fun position _ ->
                                        if position = index then
                                            "(Some (" + expression + "))"
                                        else
                                            "None")
                                    |> String.concat " "

                                call current + " " + arguments)
                    | Some (GrammarExpression.Repeat (_, repetition)) when
                        repetition.Minimum <= 1
                        && (repetition.Maximum
                            |> Option.forall (fun maximum -> maximum >= 1))
                        ->
                        descend current.Children[0] (fun expression -> call current + " [" + expression + "]")
                    | Some (GrammarExpression.Reference reference) ->
                        let target = lookup current reference
                        let tag = targetTag reference target
                        let guards = Option.toList reference.Range @ guards

                        let convert expression =
                            wrap (call current + " (" + expression + ")")

                        match target with
                        | ReferenceTarget.Definition targetId ->
                            if not (values.ContainsKey tag) then
                                values.Add (tag, (convert "value", guards))

                            if not (Set.contains tag visited) && expanded.Add tag then
                                let targetRoot = roots[targetId]

                                let targetPlan =
                                    if targetRoot.Tag = tag then
                                        targetRoot
                                    else
                                        targetRoot.Children[0]

                                pending.Enqueue (targetPlan, Set.add tag visited, convert, guards)
                        | ReferenceTarget.Primitive _ ->
                            if not (values.ContainsKey tag) then
                                values.Add (tag, (convert "value", guards))
                        | _ -> ()
                    | Some (GrammarExpression.Literal token) when Regex.IsMatch (token, "^[A-Za-z_-][A-Za-z0-9_-]*$") ->
                        if not (keywords.ContainsKey token) then
                            keywords.Add (token, wrap (call current))
                    | Some (GrammarExpression.Function (functionName, _)) -> functions.Add functionName |> ignore
                    | Some (GrammarExpression.Required _)
                    | Some (GrammarExpression.Group _) -> descend current.Children[0] (fun expression -> call current + " (" + expression + ")")
                    | Some (GrammarExpression.Sequence _)
                    | Some (GrammarExpression.AllOf _) when current.Children.Length = 1 ->
                        descend current.Children[0] (fun expression -> call current + " (" + expression + ")")
                    | Some (GrammarExpression.Sequence _)
                    | Some (GrammarExpression.AllOf _) ->
                        let emptyChildren = current.Children |> List.map emptyExpression

                        let required =
                            emptyChildren
                            |> List.indexed
                            |> List.choose (fun (index, expression) -> if expression.IsNone then Some index else None)

                        match required with
                        | [ index ] ->
                            descend current.Children[index] (fun expression ->
                                let arguments =
                                    emptyChildren
                                    |> List.mapi (fun position empty ->
                                        "("
                                        + (if position = index then expression else Option.get empty)
                                        + ")")
                                    |> String.concat " "

                                call current + " " + arguments)
                        | _ -> ()
                    | _ -> ()

                let exportBound =
                    function
                    | RangeBound.Finite (value, unitName) ->
                        {|
                            kind = "finite"
                            value = value.ToString (CultureInfo.InvariantCulture)
                            unitName = unitName
                        |}
                    | _ ->
                        {|
                            kind = "unbounded"
                            value = ""
                            unitName = ""
                        |}

                (values
                 |> Seq.sortBy _.Key
                 |> Seq.map (fun pair ->
                     {|
                         marker = pair.Key
                         expression = fst pair.Value
                         ranges =
                             snd pair.Value
                             |> List.map (fun guard ->
                                 {|
                                     minimum = exportBound guard.Minimum
                                     maximum = exportBound guard.Maximum
                                 |})
                     |})
                 |> Seq.toList),
                (keywords
                 |> Seq.sortBy _.Key
                 |> Seq.map (fun pair ->
                     {|
                         token = pair.Key
                         expression = pair.Value
                     |})
                 |> Seq.toList),
                (functions |> Seq.sort |> Seq.toList)

            for feature in features do
                let root = roots[feature.Id]
                let baseName = moduleBase feature

                let archived =
                    feature.Id.Scopes
                    |> List.exists (fun scope -> scope.StartsWith ("#obsolete:", StringComparison.Ordinal))

                let preferred =
                    feature.Id.Scopes.IsEmpty
                    && not archived
                    && (groups[baseName]
                        |> List.filter (fun f -> f.Id.Scopes.IsEmpty)
                        |> List.length)
                        =
                        1

                let api =
                    if archived then
                        baseName
                        + "_Obsolete_"
                        + digest (sprintf "%A" feature.Id)
                    elif preferred || groups[baseName].Length = 1 then
                        baseName
                    else
                        baseName + "_" + digest (sprintf "%A" feature.Id)

                let description =
                    match feature.Grammar with
                    | GrammarStatus.Parsed (grammar, _) -> feature.Id.Name + ": " + grammar
                    | _ ->
                        feature.Id.Name
                        + ": prose-defined CSS production; unsafe tokens require caller validation."

                line (
                    "    /// <summary>"
                    + System.Security.SecurityElement.Escape (description.Replace("\n", " ").Replace("\r", " "))
                    + "</summary>"
                )

                match obsolete feature with
                | Some message -> line ("    [<System.Obsolete(" + literal message + ")>]")
                | _ -> ()

                line ("    module " + api + " =")

                match root.Node |> Option.map _.Expression with
                | Some (GrammarExpression.Function (functionName, _)) when root.Children.Length = 1 ->
                    extractionFactory (namespaceName + ".Css." + api) "create" "function" root.Children[0].Tag root.Tag (Some functionName)
                | Some (GrammarExpression.Reference reference) when reference.Range.IsNone ->
                    extractionFactory (namespaceName + ".Css." + api) "create" "copy" (targetTag reference (lookup root reference)) root.Tag None
                | _ -> ()

                line "        /// <summary>Upstream specification definition URL.</summary>"
                line ("        let specUrl = " + literal feature.Href)

                let primitiveHelpers = ResizeArray<obj>()

                let emit methodName args body =
                    if
                        feature.Id.Kind = FeatureKind.Type
                        && root.Node.IsNone
                        && methodName <> "unsafeFromTokens"
                    then
                        primitiveHelpers.Add (
                            box
                                {|
                                    methodName = methodName
                                    parameters = args
                                    takesValue = args <> ""
                                |}
                        )

                    line (
                        if methodName = "unsafeFromTokens" then
                            "        /// <summary>Accepts unchecked CSS tokens; the caller validates the specification's prose constraints.</summary>"
                        else
                            "        /// <summary>Constructs this CSS production from the typed grammar components.</summary>"
                    )

                    line (
                        "        let "
                        + quotedName methodName
                        + " "
                        + args
                        + " : "
                        + valueType root.Tag
                        + " = "
                        + body
                    )

                match root.Node |> Option.map _.Expression with
                | Some (GrammarExpression.Choice _) ->
                    let names = HashSet<string>()

                    for i, child in root.Children |> List.indexed do
                        let proposed =
                            match child.Node.Value.Expression with
                            | GrammarExpression.Literal token ->
                                if Regex.IsMatch (token, "^[A-Za-z_-][A-Za-z0-9_-]*$") then
                                    token
                                else
                                    "literal" + name token
                            | GrammarExpression.Reference r -> "from" + name r.Name
                            | _ -> "case" + string (i + 1)

                        let branchName =
                            if names.Add proposed then
                                proposed
                            else
                                proposed + "_" + string (i + 1)

                        let factory = call root + "_" + string (i + 1)

                        match child.Node.Value.Expression with
                        | GrammarExpression.Literal _ -> emit branchName "" (factory + " " + call child)
                        | GrammarExpression.Reference r ->
                            emit
                                branchName
                                ("(value: "
                                 + valueType (targetTag r (lookup child r))
                                 + ")")
                                (factory + " (" + call child + " value)")
                        | _ -> emit branchName ("(value: " + valueType child.Tag + ")") (factory + " value")
                | Some (GrammarExpression.Reference r) ->
                    emit
                        "create"
                        ("(value: "
                         + valueType (targetTag r (lookup root r))
                         + ")")
                        (call root + " value")
                | Some (GrammarExpression.Literal _) -> emit "value" "" (call root)
                | Some (GrammarExpression.Sequence _)
                | Some (GrammarExpression.AllOf _) -> emit "create" (arguments root.Children) (call root + " " + variables root.Children)
                | Some (GrammarExpression.AnyOf _) ->
                    let args =
                        root.Children
                        |> List.mapi (fun i child ->
                            "(v"
                            + string (i + 1)
                            + ": "
                            + valueType child.Tag
                            + " option)")
                        |> String.concat " "

                    emit "create" args (call root + " " + variables root.Children)
                | Some (GrammarExpression.Repeat _) ->
                    emit
                        "create"
                        ("(values: "
                         + valueType root.Children[0].Tag
                         + " list)")
                        (call root + " values")
                | Some (GrammarExpression.TypeApplication (generic, _)) when generic <> "boolean-expr" ->
                    emit "unsafeFromTokens" "(tokens:string)" (call root + " tokens")
                | Some (GrammarExpression.TypeApplication _) ->
                    emit "create" ("(value: " + valueType root.Children[0].Tag + ")") (call root + " value")
                    emit "not" ("(value: " + valueType root.Tag + ")") (call root + "_not value")

                    emit
                        "and"
                        ("(head: "
                         + valueType root.Tag
                         + ") (tail: "
                         + valueType root.Tag
                         + " list)")
                        (call root + "_and head tail")

                    emit
                        "or"
                        ("(head: "
                         + valueType root.Tag
                         + ") (tail: "
                         + valueType root.Tag
                         + " list)")
                        (call root + "_or head tail")
                | Some _ when root.Children.IsEmpty -> emit "value" "" (call root)
                | Some _ -> emit "create" ("(value: " + valueType root.Children[0].Tag + ")") (call root + " value")
                | None ->
                    emit "unsafeFromTokens" "(tokens:string)" (call root + " tokens")
                    let simpleName = feature.Id.Name.Trim('<', '>').ToLowerInvariant()

                    let numeric unitName =
                        emit (if unitName = "" then "create" else unitName) "(value:float)" ("Runtime.number value " + literal unitName)

                    let units (defaults: string list) =
                        let sourceUnits =
                            features
                            |> List.choose (fun candidate ->
                                if
                                    candidate.Id.Scopes = [ "<" + simpleName + ">" ]
                                    && (obsolete candidate).IsNone
                                then
                                    match candidate.Grammar with
                                    | GrammarStatus.Parsed (_,
                                                            {
                                                                Expression = GrammarExpression.Literal token
                                                            }) when Regex.IsMatch (token, "^[A-Za-z]+$") -> Some (if token = "Q" then "q" else token)
                                    | _ -> None
                                else
                                    None)

                        defaults @ sourceUnits
                        |> List.distinctBy (fun unitName -> unitName.ToLowerInvariant ())
                        |> List.iter numeric

                    match simpleName with
                    | "number"
                    | "number-token" -> numeric ""
                    | "integer" -> emit "create" "(value:int)" "Runtime.number (float value) \"\""
                    | "percentage"
                    | "percentage-token" -> emit "percent" "(value:float)" "Runtime.number value \"%\""
                    | "length" ->
                        units (
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
                            ]
                            @ [
                                for prefix in [ ""; "s"; "l"; "d" ] do
                                    for suffix in [ "vw"; "vh"; "vi"; "vb"; "vmin"; "vmax" ] -> prefix + suffix
                            ]
                            @ [ "cqw"; "cqh"; "cqi"; "cqb"; "cqmin"; "cqmax" ]
                        )
                    | "angle" -> units [ "deg"; "rad"; "turn"; "grad" ]
                    | "time" -> units [ "s"; "ms" ]
                    | "frequency" -> units [ "Hz"; "kHz" ]
                    | "resolution" -> units [ "dpi"; "dpcm"; "dppx"; "x" ]
                    | "flex" -> units [ "fr" ]
                    | "string"
                    | "string-token" -> emit "create" "(value:string)" "Runtime.stringValue value"
                    | "ident"
                    | "identifier"
                    | "ident-token" -> emit "create" "(value:string)" "Runtime.identifier false false value"
                    | "custom-ident" -> emit "create" "(value:string)" "Runtime.identifier false true value"
                    | "dashed-ident"
                    | "custom-property-name" -> emit "create" "(value:string)" "Runtime.identifier true false value"
                    | "zero" -> emit "value" "" "Runtime.number 0.0 \"\""
                    | _ -> ()

                if
                    feature.Id.Kind = FeatureKind.Type
                    && feature.Id.Scopes.IsEmpty
                    && feature.Id.Name.Trim ('<', '>') = "integer"
                    && root.Node.IsSome
                then
                    emit "fromInt" "(value:int)" "Runtime.number (float value) \"\""

                    primitiveHelpers.Add (
                        box
                            {|
                                methodName = "fromInt"
                                parameters = "(value:int)"
                                takesValue = true
                            |}
                    )

                if
                    feature.Id.Kind = FeatureKind.Property
                    || feature.Id.Kind = FeatureKind.Descriptor
                then
                    line (
                        "        let declaration (value: "
                        + valueType root.Tag
                        + ") = Declaration("
                        + literal feature.Id.Name
                        + ", Runtime.text value)"
                    )

                    if feature.Id.Kind = FeatureKind.Property then
                        for keyword in cssWideKeywords do
                            line (
                                "        let "
                                + quotedName ("global" + name keyword)
                                + " = Declaration("
                                + literal feature.Id.Name
                                + ", "
                                + literal keyword
                                + ")"
                            )

                let ergonomicValues, ergonomicKeywords, ergonomicFunctions =
                    if feature.Id.Kind = FeatureKind.Property then
                        conversions root
                    else
                        [], [], []

                entries.Add (
                    box
                        {|
                            kind = string feature.Id.Kind
                            name = feature.Id.Name
                            scopes = feature.Id.Scopes
                            moduleName = api
                            marker = root.Tag
                            syntaxFactory = root.Factory
                            children =
                                root.Children
                                |> List.map (fun c ->
                                    {|
                                        marker = c.Tag
                                        syntaxFactory = c.Factory
                                    |})
                            href = feature.Href
                            obsolete = obsolete feature
                            primitiveHelpers = primitiveHelpers.ToArray ()
                            ergonomicValues = ergonomicValues
                            ergonomicKeywords = ergonomicKeywords
                            ergonomicFunctions = ergonomicFunctions
                            grammar =
                                (match feature.Grammar with
                                 | GrammarStatus.Parsed _ -> "parsed"
                                 | GrammarStatus.Missing -> "missing"
                                 | _ -> "rejected")
                        |}
                )

            line "    module Unsafe ="
            line "        let externalValue<'tag> (tokens:string) : Value<'tag> = Runtime.unsafeTokens tokens"

            let unresolved =
                resolution.References
                |> List.filter (fun r ->
                    match r.Target with
                    | ReferenceTarget.Missing
                    | ReferenceTarget.Ambiguous _ -> true
                    | _ -> false)

            let opaque =
                features
                |> List.filter (fun f ->
                    match f.Grammar with
                    | GrammarStatus.Parsed _ -> false
                    | _ -> true)

            let issues =
                resolution.References
                |> List.map (fun r ->
                    {|
                        owner = sprintf "%A" r.Owner
                        name = r.Reference.Name
                        offset = r.Span.Offset
                        target = sprintf "%A" r.Target
                    |})

            let manifest =
                JsonSerializer
                    .Serialize(
                        {|
                            schemaVersion = 1
                            datasetLabel = catalogue.Identity.Label
                            cssWideKeywords = cssWideKeywords
                            datasetSha256 = catalogue.Identity.Sha256
                            featureCount = features.Length
                            opaqueCount = opaque.Length
                            unresolvedCount = unresolved.Length
                            definitions = entries.ToArray ()
                            extractionFactories = extractionFactories.ToArray ()
                            primitiveTerminals =
                                resolution.References
                                |> List.choose (fun reference ->
                                    match reference.Target with
                                    | ReferenceTarget.Primitive production ->
                                        Some
                                            {|
                                                marker = targetTag reference.Reference reference.Target
                                                production = production
                                            |}
                                    | _ -> None)
                                |> List.distinct
                                |> List.sortBy (fun terminal -> terminal.marker, terminal.production)
                            references = issues
                            recursiveComponents =
                                resolution.Cycles
                                |> List.map (List.map (sprintf "%A"))
                        |},
                        JsonSerializerOptions (WriteIndented = true)
                    )
                    .Replace("\r\n", "\n")
                + "\n"
                |> NumericProjection.enrich

            Ok
                {
                    Source = source.ToString ()
                    Manifest = manifest
                    FeatureCount = features.Length
                    OpaqueCount = opaque.Length
                    UnresolvedCount = unresolved.Length
                }
