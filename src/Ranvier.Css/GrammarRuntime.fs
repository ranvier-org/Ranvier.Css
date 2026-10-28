namespace Ranvier.Css.Internal

open System
open System.ComponentModel
open System.Globalization
open System.Collections.Generic
open Ranvier.Css

[<EditorBrowsable(EditorBrowsableState.Never)>]
module GrammarRuntime =
    /// <summary>One resolved grammar production; child IDs and reference targets belong to the same table.</summary>
    type Node =
        {
            /// <summary>Stable nonnegative identity within the table.</summary>
            Id: int
            /// <summary>Production code: opaque 0, literal 1, reference 2, choice 3, sequence 4, all-of 5, any-of 6, repeat 7.</summary>
            /// <remarks>Required is 8, function 9, block 10, boolean template 11 and unchecked template 12.</remarks>
            Kind: int
            /// <summary>Literal tokens, function name or opening block delimiter, according to the production kind.</summary>
            Text: string
            /// <summary>Reference target ID; other kinds ignore this field.</summary>
            Target: int
            /// <summary>Ordered child IDs; unary productions require one child.</summary>
            Children: int array
            /// <summary>Prefix separator for each sequence child; the first emitted child receives no prefix.</summary>
            Separators: string array
            /// <summary>Inclusive minimum repetition count.</summary>
            Minimum: int
            /// <summary>Inclusive maximum repetition count; negative one permits unlimited occurrences.</summary>
            Maximum: int
            /// <summary>Repeats join items with a comma and space when true.</summary>
            CommaSeparated: bool
            /// <summary>Inclusive literal numeric lower bound and its unit; computed expressions defer this check.</summary>
            Lower: (float * string) option
            /// <summary>Inclusive literal numeric upper bound and its unit; computed expressions defer this check.</summary>
            Upper: (float * string) option
        }

    /// <summary>Immutable resolved grammar lookup. Creation copies caller-owned node arrays.</summary>
    type Table = private Table of Map<int, Node>

    /// <summary>Serialized grammar evidence retaining its node identity and optional numeric token metadata.</summary>
    type ValueData =
        private
        | ValueData of nodeId: int * text: string * numeric: (float * string) option

        /// <summary>Identifies the admitted grammar production; a negative ID identifies an unclassified keyword.</summary>
        member this.NodeId = let (ValueData (id, _, _)) = this in id
        /// <summary>Contains the exact serialized CSS tokens.</summary>
        member this.Text = let (ValueData (_, text, _)) = this in text
        /// <summary>Contains a literal magnitude and unit; computed expressions carry <c>None</c>.</summary>
        member this.Numeric = let (ValueData (_, _, numeric)) = this in numeric

    let private lookup (Table nodes) id =
        match Map.tryFind id nodes with
        | Some node -> node
        | None -> invalidArg "nodeId" ("Unknown CSS grammar node: " + string id)

    let private ranged node (value: ValueData) =
        match value.Numeric with
        | Some (magnitude, unitName) ->
            let numeric: Value<unit> = Generated.number magnitude unitName
            let checkedValue: Value<unit> = Generated.range node.Lower node.Upper numeric
            checkedValue |> ignore
        | None -> ()

        value

    /// <summary>Construction and portable length-prefixed decoding of resolved grammar tables.</summary>
    module Table =
        /// <summary>Copies resolved nodes and rejects duplicate IDs, invalid kinds, bounds and missing graph edges.</summary>
        let create (nodes: Node array) =
            if isNull nodes then
                nullArg "nodes"

            let copied =
                nodes
                |> Array.map (fun node ->
                    if node.Id < 0 || node.Kind < 0 || node.Kind > 12 then
                        invalidArg "nodes" "CSS grammar node IDs and kinds are outside their supported range."

                    if
                        node.Minimum < 0
                        || node.Maximum < -1
                        || (node.Maximum >= 0 && node.Maximum < node.Minimum)
                    then
                        invalidArg "nodes" "CSS grammar repetition bounds are invalid."

                    if
                        isNull node.Text
                        || isNull node.Children
                        || isNull node.Separators
                    then
                        nullArg "nodes"

                    { node with
                        Children = Array.copy node.Children
                        Separators = Array.copy node.Separators
                    })

            let indexed =
                copied
                |> Array.map (fun node -> node.Id, node)
                |> Map.ofArray

            if indexed.Count <> copied.Length then
                invalidArg "nodes" "CSS grammar node IDs must be unique."

            for node in copied do
                for child in node.Children do
                    if not (Map.containsKey child indexed) then
                        invalidArg "nodes" "CSS grammar child node is missing."

                if
                    node.Kind = 2
                    && not (Map.containsKey node.Target indexed)
                then
                    invalidArg "nodes" "CSS grammar reference target is missing."

                if
                    List.contains node.Kind [ 2; 7; 8; 11 ]
                    && node.Children.Length
                       <> (if node.Kind = 2 then 0 else 1)
                then
                    invalidArg "nodes" "CSS grammar unary production has invalid arity."

                if
                    List.contains node.Kind [ 9; 10 ]
                    && node.Children.Length > 1
                then
                    invalidArg "nodes" "CSS grammar wrapper has invalid arity."

            Table indexed

        /// <summary>Decodes a node count followed by fourteen length-prefixed fields per node; malformed input raises an argument error.</summary>
        let decode (encoded: string) =
            if isNull encoded then
                nullArg "encoded"

            let fields (input: string) =
                let result = ResizeArray<string>()
                let mutable offset = 0

                while offset < input.Length do
                    let colon = input.IndexOf (':', offset)

                    if colon < 0 then
                        invalidArg "encoded" "Missing CSS table field length."

                    let mutable length = 0

                    if
                        not (Int32.TryParse (input.Substring (offset, colon - offset), NumberStyles.None, CultureInfo.InvariantCulture, &length))
                        || length > input.Length - colon - 1
                    then
                        invalidArg "encoded" "Invalid CSS table field length."

                    result.Add (input.Substring (colon + 1, length))
                    offset <- colon + 1 + length

                result.ToArray ()

            let integer (text: string) =
                let mutable value = 0

                if not (Int32.TryParse (text, NumberStyles.Integer, CultureInfo.InvariantCulture, &value)) then
                    invalidArg "encoded" "Invalid CSS table integer."

                value

            let scalar (text: string) =
                let mutable value = 0.

                if
                    not (Double.TryParse (text, NumberStyles.Float, CultureInfo.InvariantCulture, &value))
                    || Double.IsNaN value
                    || Double.IsInfinity value
                then
                    invalidArg "encoded" "Invalid CSS table numeric bound."

                value

            let data = fields encoded

            if data.Length = 0 then
                invalidArg "encoded" "Missing CSS table node count."

            let count = integer data[0]

            if
                count < 0
                || count > (data.Length - 1) / 14
                || data.Length - 1 <> count * 14
            then
                invalidArg "encoded" "CSS table node count does not match its fields."

            Array.init count (fun i ->
                let at n =
                    data[1 + i * 14 + n]

                let bound magnitude unitName =
                    if magnitude = "" then
                        None
                    else
                        Some (scalar magnitude, unitName)

                let comma =
                    match at 8 with
                    | "0" -> false
                    | "1" -> true
                    | _ -> invalidArg "encoded" "Invalid CSS table comma flag."

                {
                    Id = integer (at 0)
                    Kind = integer (at 1)
                    Text = at 2
                    Target = integer (at 3)
                    Children =
                        if at 4 = "" then
                            [||]
                        else
                            (at 4).Split(',') |> Array.map integer
                    Separators = fields (at 5)
                    Minimum = integer (at 6)
                    Maximum = integer (at 7)
                    CommaSeparated = comma
                    Lower = bound (at 9) (at 10)
                    Upper = bound (at 11) (at 12)
                })
            |> create

    let private nullable table nodeId =
        let rec visit seen id =
            if Set.contains id seen then
                false
            else
                let node = lookup table id
                let descend = visit (Set.add id seen)

                match node.Kind with
                | 1 -> node.Text = ""
                | 2 -> descend node.Target
                | 3 -> Array.exists descend node.Children
                | 4
                | 5 -> Array.forall descend node.Children
                | 7 -> node.Minimum = 0 || descend node.Children[0]
                | 11 -> descend node.Children[0]
                | _ -> false

        visit Set.empty nodeId

    let private join (separators: string array) (parts: string array) =
        let mutable text = ""

        for i in 0 .. parts.Length - 1 do
            if parts[i] <> "" then
                let separator = if i < separators.Length then separators[i] else " "

                text <-
                    text
                    + (if text = "" then "" else separator)
                    + parts[i]

        text

    type private AdmissionAtom =
        | LiteralAtom of string
        | ProductionAtom of int

    let private language table nodeId =
        let rec collect seen atoms id =
            if Set.contains id seen then
                seen, atoms
            else
                let node = lookup table id
                let seen = Set.add id seen

                let atoms =
                    match node.Kind with
                    | 2
                    | 3
                    | 8 -> atoms
                    | 1 -> Set.add (LiteralAtom node.Text) atoms
                    | _ -> Set.add (ProductionAtom id) atoms

                let children =
                    match node.Kind with
                    | 2 -> [| node.Target |]
                    | 3
                    | 6
                    | 8
                    | 11 -> node.Children
                    | 7 when
                        node.Minimum <= 1
                        && (node.Maximum = -1 || node.Maximum >= 1)
                        ->
                        node.Children
                    | 4
                    | 5 ->
                        node.Children
                        |> Array.indexed
                        |> Array.choose (fun (index, child) ->
                            if
                                node.Children
                                |> Array.indexed
                                |> Array.forall (fun (other, sibling) -> other = index || nullable table sibling)
                            then
                                Some child
                            else
                                None)
                    | _ -> [||]

                children
                |> Array.fold (fun (seen, atoms) child -> collect seen atoms child) (seen, atoms)

        collect Set.empty Set.empty nodeId |> snd

    let private tryAdmit table targetNodeId (value: ValueData) =
        let mutable rangeFailure: ArgumentException option = None

        let rec visit seen id =
            if Set.contains id seen then
                None
            else
                let node = lookup table id

                let reclassify result =
                    result
                    |> Option.map (fun (accepted: ValueData) ->
                        try
                            Some (accepted |> ranged node)
                        with :? ArgumentException as error ->
                            rangeFailure <- Some error
                            None)
                    |> Option.flatten

                let descend = visit (Set.add id seen)

                let first children =
                    children |> Array.tryPick descend

                let candidate =
                    if value.NodeId = id then
                        Some value
                    else
                        match node.Kind with
                        | 1 when value.NodeId = -1 && value.Text = node.Text -> Some value
                        | 2 -> descend node.Target
                        | 3 -> first node.Children
                        | 4
                        | 5 ->
                            node.Children
                            |> Array.mapi (fun i child ->
                                if
                                    node.Children
                                    |> Array.mapi (fun j sibling -> j = i || nullable table sibling)
                                    |> Array.forall (fun accepted -> accepted)
                                then
                                    descend child
                                else
                                    None)
                            |> Array.tryPick (fun candidate -> candidate)
                        | 6 when not (String.IsNullOrWhiteSpace value.Text) -> first node.Children
                        | 7 when
                            node.Minimum <= 1
                            && (node.Maximum = -1 || node.Maximum >= 1)
                            ->
                            descend node.Children[0]
                        | 8 ->
                            if String.IsNullOrWhiteSpace value.Text then
                                None
                            else
                                descend node.Children[0]
                        | 11 -> descend node.Children[0]
                        | _ -> None

                let candidate =
                    match candidate, value.Numeric with
                    | None, None when value.NodeId >= 0 ->
                        let source = lookup table value.NodeId

                        if List.contains source.Kind [ 2; 3; 8 ] then
                            let sourceLanguage = language table value.NodeId

                            if
                                not (Set.isEmpty sourceLanguage)
                                && Set.isSubset sourceLanguage (language table id)
                            then
                                Some value
                            else
                                None
                        else
                            None
                    | _ -> candidate

                reclassify candidate

        match visit Set.empty targetNodeId with
        | Some accepted -> Some accepted
        | None ->
            match rangeFailure with
            | Some error -> raise error
            | None -> None

    /// <summary>Admits a value through references and nullable grammar paths; rejects incompatible identities and numeric bounds.</summary>
    let admit table targetNodeId value =
        match tryAdmit table targetNodeId value with
        | Some accepted -> accepted
        | None ->
            invalidArg
                "value"
                ("Value is not admitted by CSS grammar node "
                 + string targetNodeId
                 + ".")

    /// <summary>Creates an unclassified keyword; admission requires an exact literal token match.</summary>
    let token (text: string) =
        if isNull text then
            nullArg "text"

        ValueData (-1, text, None)

    /// <summary>Creates explicitly unchecked nonempty tokens carrying an existing grammar identity.</summary>
    let opaque table nodeId text =
        lookup table nodeId |> ignore
        let checkedValue: Value<unit> = Generated.unsafeTokens text
        ValueData (nodeId, Generated.text checkedValue, None)

    /// <summary>Creates a finite numeric token carrying a grammar identity and checks bounds attached to that node.</summary>
    let number table nodeId magnitude unitName =
        if isNull unitName then
            nullArg "unitName"

        let node = lookup table nodeId
        let value: Value<unit> = Generated.number magnitude unitName

        let compatible (name: string) =
            let name = name.Trim('<', '>').ToLowerInvariant()

            if unitName = "%" then
                List.contains name [ "percentage"; "percentage-token" ]
            elif unitName = "" then
                List.contains name [ "number"; "number-token"; "integer"; "zero" ]
            else
                List.contains
                    name
                    [
                        "length"
                        "angle"
                        "time"
                        "frequency"
                        "resolution"
                        "flex"
                        "dimension"
                        "dimension-token"
                    ]

        let rec terminals seen id =
            if Set.contains id seen then
                Set.empty
            else
                let node = lookup table id
                let seen = Set.add id seen

                match node.Kind with
                | 0 when compatible node.Text -> Set.singleton id
                | 2 -> terminals seen node.Target
                | 3
                | 8 ->
                    node.Children
                    |> Array.map (terminals seen)
                    |> Array.fold Set.union Set.empty
                | _ -> Set.empty

        let provenance = terminals Set.empty nodeId

        match Set.toList provenance with
        | [ terminal ] ->
            ValueData (terminal, Generated.text value, Some (magnitude, unitName))
            |> admit table nodeId
        | _ ->
            ValueData (nodeId, Generated.text value, Some (magnitude, unitName))
            |> ranged node

    /// <summary>Constructs a grammar node from admitted components, checking arity, optional slots, ranges and occurrence bounds.</summary>
    let construct table nodeId (values: ValueData option array) =
        if isNull values then
            nullArg "values"

        let node = lookup table nodeId

        let arity count =
            if values.Length <> count then
                invalidArg "values" "CSS grammar constructor arity does not match its production."

        let item target =
            function
            | Some value -> admit table target value
            | None -> invalidArg "values" "A CSS grammar component is required."

        let make text numeric =
            ValueData (nodeId, text, numeric) |> ranged node

        let unary target =
            arity 1
            item target values[0]

        let parts optional =
            arity node.Children.Length

            node.Children
            |> Array.mapi (fun i child ->
                match values[i] with
                | Some value -> (admit table child value).Text
                | None when optional || nullable table child -> ""
                | None -> invalidArg "values" "A nonnullable CSS grammar component is required.")

        match node.Kind with
        | 1 ->
            arity 0
            make node.Text None
        | 2 ->
            let value = unary node.Target
            value |> ranged node
        | 3 ->
            arity 1
            let value = item nodeId values[0]
            value |> ranged node
        | 4
        | 5 -> make (join node.Separators (parts false)) None
        | 6 ->
            let components =
                parts true
                |> Array.filter (String.IsNullOrWhiteSpace >> not)

            if components.Length = 0 then
                invalidArg "values" "At least one unordered CSS grammar component is required."

            make (String.concat " " components) None
        | 7 ->
            if
                values.Length < node.Minimum
                || (node.Maximum >= 0 && values.Length > node.Maximum)
            then
                invalidArg "values" "CSS repetition cardinality is outside its grammar bounds."

            let components =
                values
                |> Array.map (item node.Children[0] >> fun value -> value.Text)

            make (String.concat (if node.CommaSeparated then ", " else " ") components) None
        | 8 ->
            let value = unary node.Children[0]

            if String.IsNullOrWhiteSpace value.Text then
                invalidArg "values" "A required CSS group must contain a value."

            value |> ranged node
        | 9
        | 10 ->
            arity node.Children.Length

            let contents =
                if node.Children.Length = 0 then
                    ""
                else
                    match values[0] with
                    | None when nullable table node.Children[0] -> ""
                    | value -> (item node.Children[0] value).Text

            if node.Kind = 9 then
                make (node.Text + "(" + contents + ")") None
            else
                let closing =
                    match node.Text with
                    | "(" -> ")"
                    | "[" -> "]"
                    | "{" -> "}"
                    | _ -> invalidArg "nodeId" "Unsupported CSS block delimiter."

                make (node.Text + contents + closing) None
        | 11 ->
            let value = unary node.Children[0]
            make value.Text value.Numeric
        | _ -> invalidArg "nodeId" "This CSS production requires an explicit unchecked token factory."

    /// <summary>Constructs a boolean template expression; not takes one operand and and/or take a nonempty operand array.</summary>
    let boolean table nodeId operation (values: ValueData array) =
        let node = lookup table nodeId

        if node.Kind <> 11 then
            invalidArg "nodeId" "Expected a CSS boolean template production."

        if isNull values then
            nullArg "values"

        let operands =
            values
            |> Array.map (admit table nodeId >> fun value -> value.Text)

        let text =
            match operation, operands.Length with
            | "not", 1 -> "not (" + operands[0] + ")"
            | ("and" | "or"), count when count > 0 ->
                operands
                |> Array.map (fun text -> "(" + text + ")")
                |> String.concat (" " + operation + " ")
            | _ -> invalidArg "operation" "CSS boolean operations require not(single) or and/or(nonempty)."

        ValueData (nodeId, text, None)
