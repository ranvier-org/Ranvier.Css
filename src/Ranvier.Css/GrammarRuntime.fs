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

#if !FABLE_COMPILER
        member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
            let numeric c d p value =
                NativeDisplay.option
                    c
                    d
                    p
                    (fun c d _ (number, unitName) ->
                        NativeDisplay.tuple c d (box (number, unitName)) (fun n ->
                            [ NativeDisplay.float64 c n number; NativeDisplay.text c n unitName ]))
                    value

            NativeDisplay.record context depth (box this) (fun n ->
                [
                    "Id", (fun () -> NativeDisplay.int32 context n this.Id)
                    "Kind", (fun () -> NativeDisplay.int32 context n this.Kind)
                    "Text", (fun () -> NativeDisplay.text context n this.Text)
                    "Target", (fun () -> NativeDisplay.int32 context n this.Target)
                    "Children", (fun () -> NativeDisplay.array context n (fun c d _ x -> NativeDisplay.int32 c d x) this.Children)
                    "Separators", (fun () -> NativeDisplay.array context n (fun c d _ x -> NativeDisplay.text c d x) this.Separators)
                    "Minimum", (fun () -> NativeDisplay.int32 context n this.Minimum)
                    "Maximum", (fun () -> NativeDisplay.int32 context n this.Maximum)
                    "CommaSeparated", (fun () -> NativeDisplay.boolean context n this.CommaSeparated)
                    "Lower", (fun () -> numeric context n 3 this.Lower)
                    "Upper", (fun () -> numeric context n 3 this.Upper)
                ])

        override this.ToString() =
            if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
                sprintf "%+A" this
            else
                NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

    /// <summary>Immutable resolved grammar lookup. Creation copies caller-owned node arrays.</summary>
#if !FABLE_COMPILER
    [<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
    type Table =
        private
        | Table of Map<int, Node>

#if !FABLE_COMPILER
        member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
            let (Table nodes) = this

            NativeDisplay.union context depth precedence (box this) (fun n ->
                "Table",
                [
                    Some (fun p ->
                        NativeDisplay.map
                            context
                            n
                            p
                            (fun c d _ value -> NativeDisplay.int32 c d value)
                            (fun c d p value -> NativeDisplay.known c d value (fun (value: Node) -> value.NativeDisplay (c, d, p)))
                            nodes)
                ])

        override this.ToString() =
            if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
                sprintf "%+A" this
            else
                NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

    /// <summary>Serialized grammar evidence retaining its node identity and optional numeric token metadata.</summary>
#if !FABLE_COMPILER
    [<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
    type ValueData =
        private
        | ValueData of nodeId: int * text: string * numeric: (float * string) option

#if !FABLE_COMPILER
        member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
            let (ValueData (id, text, numeric)) = this

            NativeDisplay.union context depth precedence (box this) (fun n ->
                "ValueData",
                [
                    Some (fun _ -> NativeDisplay.int32 context n id)
                    Some (fun _ -> NativeDisplay.text context n text)
                    Some (fun p ->
                        NativeDisplay.option
                            context
                            n
                            p
                            (fun c d _ (number, unitName) ->
                                NativeDisplay.tuple c d (box (number, unitName)) (fun inner ->
                                    [ NativeDisplay.float64 c inner number; NativeDisplay.text c inner unitName ]))
                            numeric)
                ])

        override this.ToString() =
            if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
                sprintf "%+A" this
            else
                NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

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

#if !FABLE_COMPILER
    [<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
    type private AdmissionAtom =
        | LiteralAtom of string
        | ProductionAtom of int

#if !FABLE_COMPILER
        override this.ToString() =
            if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
                sprintf "%+A" this
            else
                NativeDisplay.format (fun context depth precedence ->
                    NativeDisplay.union context depth precedence (box this) (fun n ->
                        match this with
                        | LiteralAtom text -> "LiteralAtom", [ Some (fun _ -> NativeDisplay.text context n text) ]
                        | ProductionAtom id -> "ProductionAtom", [ Some (fun _ -> NativeDisplay.int32 context n id) ]))
#endif

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

        let comma index =
            let child = lookup table node.Children[index]
            child.Kind = 1 && child.Text = ","

        // Serializes commas canonically: no separator before a comma and one space after it.
        let joinCommas (parts: string array) =
            let separators =
                parts
                |> Array.mapi (fun index _ ->
                    let previous =
                        seq { index - 1 .. -1 .. 0 }
                        |> Seq.tryFind (fun earlier -> parts[earlier] <> "")

                    if comma index then
                        ""
                    elif previous |> Option.exists comma then
                        " "
                    elif index < node.Separators.Length then
                        node.Separators[index]
                    else
                        " ")

            join separators parts

        let omitCommas (parts: string array) =

            let content index =
                parts[index] <> "" && not (comma index)

            let kept = Array.copy parts
            let mutable previousComma = false

            for index in 0 .. kept.Length - 1 do
                if kept[index] <> "" then
                    if comma index then
                        let before = Seq.exists content (seq { 0 .. index - 1 })
                        let after = Seq.exists content (seq { index + 1 .. kept.Length - 1 })

                        if not before || not after || previousComma then
                            kept[index] <- ""
                        else
                            previousComma <- true
                    else
                        previousComma <- false

            kept

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
        | 4 -> make (joinCommas (omitCommas (parts false))) None
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

    /// <summary>One flattened constructor parameter derived from a function production.</summary>
    /// <remarks>Required and repeated parameters precede optional parameters; each group retains grammar order.</remarks>
#if !FABLE_COMPILER
    [<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
    type Parameter =
        /// <summary>A value admitted at the node; the keyword is a word literal that precedes it in the grammar, or empty.</summary>
        | Required of nodeId: int * keyword: string
        /// <summary>An omissible value admitted at the node; omission also omits the literals of its optional group.</summary>
        | Optional of nodeId: int * keyword: string
        /// <summary>A list for a repetition node; each item supplies the item parameters in their declared order.</summary>
        | Repeated of nodeId: int * items: Parameter array

#if !FABLE_COMPILER
        member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
            NativeDisplay.union context depth precedence (box this) (fun n ->
                match this with
                | Required (id, keyword) ->
                    "Required",
                    [
                        Some (fun _ -> NativeDisplay.int32 context n id)
                        Some (fun _ -> NativeDisplay.text context n keyword)
                    ]
                | Optional (id, keyword) ->
                    "Optional",
                    [
                        Some (fun _ -> NativeDisplay.int32 context n id)
                        Some (fun _ -> NativeDisplay.text context n keyword)
                    ]
                | Repeated (id, items) ->
                    "Repeated",
                    [
                        Some (fun _ -> NativeDisplay.int32 context n id)
                        Some (fun _ -> NativeDisplay.array context n (fun c d p (item: Parameter) -> item.NativeDisplay (c, d, p)) items)
                    ])

        override this.ToString() =
            if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
                sprintf "%+A" this
            else
                NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

    /// <summary>An argument for one flattened constructor parameter.</summary>
#if !FABLE_COMPILER
    [<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
    type Argument =
        /// <summary>A value for a required or optional parameter.</summary>
        | Supplied of ValueData
        /// <summary>The omission of an optional parameter.</summary>
        | Omitted
        /// <summary>The items of a repeated parameter, each listing its item arguments.</summary>
        | Items of Argument array list

#if !FABLE_COMPILER
        member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
            NativeDisplay.union context depth precedence (box this) (fun n ->
                match this with
                | Supplied value -> "Supplied", [ Some (fun p -> value.NativeDisplay (context, n, p)) ]
                | Omitted -> "Omitted", []
                | Items items ->
                    "Items",
                    [
                        Some (fun _ ->
                            NativeDisplay.list
                                context
                                n
                                (fun c d _ (item: Argument array) ->
                                    NativeDisplay.array c d (fun c d p (argument: Argument) -> argument.NativeDisplay (c, d, p)) item)
                                items)
                    ])

        override this.ToString() =
            if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
                sprintf "%+A" this
            else
                NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

    /// <summary>Wraps an optional value as a flattened constructor argument.</summary>
    let optional (value: ValueData option) =
        match value with
        | Some value -> Supplied value
        | None -> Omitted

    type private Template =
        | Literal of string
        | Slot of int
        | Build of int * Template array
        | Maybe of int * Template
        | Fixed of int * Template array
        | Many of int * Template

    let rec private plan table id =
        let node = lookup table id

        let all (templates: Template option array) =
            if Array.forall Option.isSome templates then
                Some (Array.map Option.get templates)
            else
                None

        match node.Kind with
        | 1 -> Some (Literal node.Text)
        | 0
        | 2
        | 11
        | 12 -> Some (Slot id)
        | 3 when
            node.Children
            |> Array.forall (fun child -> List.contains (lookup table child).Kind [ 0; 1; 2 ])
            ->
            Some (Slot id)
        | 4
        | 5
        | 8
        | 9
        | 10 ->
            node.Children
            |> Array.map (plan table)
            |> all
            |> Option.map (fun children -> Build (id, children))
        | 7 ->
            plan table node.Children[0]
            |> Option.bind (fun child ->
                // Counts values a template consumes and whether it already contains optional groups or lists.
                let rec count template =
                    match template with
                    | Literal _ -> 0, false, false
                    | Slot _ -> 1, false, false
                    | Maybe (_, inner) ->
                        let size, _, lists = count inner
                        size, true, lists
                    | Many _ -> 1, false, true
                    | Build (_, children)
                    | Fixed (_, children) ->
                        children
                        |> Array.fold
                            (fun (total, optional, lists) child ->
                                let size, innerOptional, innerLists = count child
                                total + size, optional || innerOptional, lists || innerLists)
                            (0, false, false)

                let size, optional, lists = count child

                if node.Minimum = 0 && node.Maximum = 1 then
                    if size = 1 && not optional && not lists then
                        Some (Maybe (id, child))
                    else
                        None
                elif node.Minimum = node.Maximum && node.Minimum > 0 then
                    if size > 0 then
                        Some (Fixed (id, Array.replicate node.Minimum child))
                    else
                        None
                elif size > 0 && not lists then
                    Some (Many (id, child))
                else
                    None)
        | _ -> None

    let private word (text: string) =
        text <> ""
        && Char.IsLetter text[0]
        && text
           |> Seq.forall (fun c -> Char.IsLetterOrDigit c || c = '-')

    let rec private leaves template =
        match template with
        | Literal _ -> []
        | Slot id -> [ Required (id, "") ]
        | Maybe (_, inner) ->
            leaves inner
            |> List.map (function
                | Required (id, keyword) -> Optional (id, keyword)
                | other -> other)
        | Many (id, item) -> [ Repeated (id, publicOrder (leaves item) |> List.toArray) ]
        | Fixed (_, children) -> children |> Array.toList |> List.collect leaves
        | Build (_, children) ->
            let mutable keyword = ""

            [
                for child in children do
                    match child with
                    | Literal text -> keyword <- if word text then text else ""
                    | Slot id ->
                        yield Required (id, keyword)
                        keyword <- ""
                    | other ->
                        yield! leaves other
                        keyword <- ""
            ]

    /// <summary>Lists grammar positions in public order: required and repeated values first, then optional values.</summary>
    and private publicPositions (parameters: Parameter list) =
        let required, optional =
            parameters
            |> List.indexed
            |> List.partition (function
                | _, Optional _ -> false
                | _ -> true)

        required @ optional |> List.map fst

    and private publicOrder (parameters: Parameter list) =
        let parameters = List.toArray parameters

        publicPositions (List.ofArray parameters)
        |> List.map (fun position -> parameters[position])

    let private grammarOrder (parameters: Parameter list) (arguments: Argument array) =
        let positions = publicPositions parameters

        if arguments.Length <> positions.Length then
            invalidArg "arguments" "Flattened CSS constructor arity does not match its parameters."

        let ordered = Array.create positions.Length Omitted

        positions
        |> List.iteri (fun publicIndex grammarIndex -> ordered[grammarIndex] <- arguments[publicIndex])

        ordered

    /// <summary>Returns the flattened public parameters of a production, or <c>None</c> when its grammar needs structural constructors.</summary>
    /// <remarks>Optional groups must contain one value and repetitions may not nest lists or optional groups.</remarks>
    let tryParameters table nodeId =
        plan table nodeId
        |> Option.map (leaves >> publicOrder >> List.toArray)

    /// <summary>Constructs a production from flattened arguments in <see cref="tryParameters" /> order.</summary>
    /// <remarks>Every inserted literal and supplied value is checked by <c>construct</c> and its grammar admissions.</remarks>
    let assemble table nodeId (arguments: Argument array) =
        if isNull arguments then
            nullArg "arguments"

        let template =
            plan table nodeId
            |> Option.defaultWith (fun () -> invalidArg "nodeId" "This CSS production cannot be constructed from flattened arguments.")

        let rec evaluate template (cursor: int ref) (arguments: Argument array) =
            let next () =
                if cursor.Value >= arguments.Length then
                    invalidArg "arguments" "Flattened CSS constructor arguments are missing."

                let argument = arguments[cursor.Value]
                cursor.Value <- cursor.Value + 1
                argument

            match template with
            | Literal text -> Some (token text)
            | Slot _ ->
                match next () with
                | Supplied value -> Some value
                | Omitted -> invalidArg "arguments" "A required CSS constructor argument was omitted."
                | Items _ -> invalidArg "arguments" "A CSS constructor value cannot be a list."
            | Build (id, children) ->
                Some (
                    construct
                        table
                        id
                        (children
                         |> Array.map (fun child -> evaluate child cursor arguments))
                )
            | Maybe (id, inner) ->
                if cursor.Value >= arguments.Length then
                    invalidArg "arguments" "Flattened CSS constructor arguments are missing."

                match arguments[cursor.Value] with
                | Omitted ->
                    cursor.Value <- cursor.Value + 1
                    None
                | _ -> Some (construct table id [| evaluate inner cursor arguments |])
            | Fixed (id, children) ->
                Some (
                    construct
                        table
                        id
                        (children
                         |> Array.map (fun child -> evaluate child cursor arguments))
                )
            | Many (id, item) ->
                match next () with
                | Items items ->
                    let parameters = leaves item

                    items
                    |> List.map (fun itemArguments ->
                        if isNull itemArguments then
                            nullArg "arguments"

                        let ordered = grammarOrder parameters itemArguments
                        let itemCursor = ref 0
                        let value = evaluate item itemCursor ordered

                        if itemCursor.Value <> ordered.Length then
                            invalidArg "arguments" "Flattened CSS list item arguments were not consumed."

                        value)
                    |> List.toArray
                    |> construct table id
                    |> Some
                | _ -> invalidArg "arguments" "A repeated CSS constructor argument requires a list."

        let ordered = grammarOrder (leaves template) arguments
        let cursor = ref 0

        match evaluate template cursor ordered with
        | Some value when cursor.Value = ordered.Length -> value
        | Some _ -> invalidArg "arguments" "Flattened CSS constructor arguments were not consumed."
        | None -> invalidArg "nodeId" "A flattened CSS production must produce a value."
