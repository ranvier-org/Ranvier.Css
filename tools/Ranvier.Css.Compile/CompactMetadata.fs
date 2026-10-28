namespace Ranvier.Css.Compile

open System
open System.IO
open System.Globalization
open System.Text.Json
open Ranvier.Css.Internal

module internal CompactMetadata =
    type NodeMetadata =
        {
            Id: int
            Kind: int
            Text: string
            Target: int
            Children: int list
        }

    type Factory =
        {
            Owner: string
            MemberName: string
            NodeId: int
            Kind: string
            Production: string option
            UnitName: string option
            ArgumentType: string option
            Slots: int list option
        }

    type Property =
        {
            Owner: string
            MemberName: string
            NodeId: int
            CssName: string
        }

    type Registry =
        {
            Namespace: string
            Table: GrammarRuntime.Table
            Nodes: Map<int, NodeMetadata>
            Factories: Map<string * string, Factory>
            Properties: Map<string * string, Property>
        }

    let private tryProperty name (item: JsonElement) =
        item.EnumerateObject ()
        |> Seq.tryFind (fun property -> String.Equals (property.Name, name, StringComparison.OrdinalIgnoreCase))
        |> Option.map (fun property -> property.Value)

    let private required name item =
        tryProperty name item
        |> Option.defaultWith (fun () -> invalidOp ("Compact CSS metadata is missing '" + name + "'."))

    let private text name item =
        (required name item).GetString()

    let private optionalText name item =
        tryProperty name item
        |> Option.bind (fun value ->
            if value.ValueKind = JsonValueKind.Null then
                None
            elif value.ValueKind = JsonValueKind.String then
                Some (value.GetString ())
            else
                invalidOp (
                    "Compact CSS metadata '"
                    + name
                    + "' must be a string or null."
                ))

    let private integer (text: string) =
        Int32.Parse (text, NumberStyles.Integer, CultureInfo.InvariantCulture)

    let private routingNodes (encoded: string) =
        let fields = ResizeArray<string>()
        let mutable offset = 0

        while offset < encoded.Length do
            let colon = encoded.IndexOf (':', offset)
            let count = integer (encoded.Substring (offset, colon - offset))
            fields.Add (encoded.Substring (colon + 1, count))
            offset <- colon + count + 1

        let count = integer fields[0]

        [
            for index in 0 .. count - 1 do
                let at field =
                    fields[1 + index * 14 + field]

                let node =
                    {
                        Id = integer (at 0)
                        Kind = integer (at 1)
                        Text = at 2
                        Target = integer (at 3)
                        Children =
                            if at 4 = "" then
                                []
                            else
                                (at 4).Split(',')
                                |> Array.map integer
                                |> List.ofArray
                    }

                yield node.Id, node
        ]
        |> Map.ofList

    let loadJson (json: string) =
        use document = JsonDocument.Parse json
        let root = document.RootElement

        if (required "schemaVersion" root).GetInt32() <> 2 then
            invalidOp "Compact CSS metadata schema 2 is required."

        let namespaceName = text "namespaceName" root

        if String.IsNullOrWhiteSpace namespaceName then
            invalidOp "Compact CSS metadata requires a namespace."

        let encoded = text "encodedGrammar" root
        let table = GrammarRuntime.Table.decode encoded
        let nodes = routingNodes encoded

        let checkNode id =
            if not (Map.containsKey id nodes) then
                invalidOp (
                    "Compact CSS factory references unknown grammar node "
                    + string id
                    + "."
                )

        let grammarFactories =
            (required "owners" root).EnumerateArray()
            |> Seq.map (fun owner ->
                let id = (required "nodeId" owner).GetInt32()
                checkNode id
                let node = nodes[id]

                let path =
                    (required "path" owner).EnumerateArray()
                    |> Seq.map (fun part -> part.GetString ())
                    |> Seq.toList

                if
                    path.IsEmpty
                    || path |> List.exists String.IsNullOrWhiteSpace
                then
                    invalidOp "Compact CSS owner paths must contain nonempty segments."

                let emittedMember =
                    optionalText "memberName" owner
                    |> Option.orElseWith (fun () -> optionalText "member" owner)
                    |> Option.defaultWith (fun () -> invalidOp "Compact CSS owner is missing its member name.")

                let memberName, kind =
                    match node.Kind with
                    | 0
                    | 12 -> "unsafeFromTokens", "opaque"
                    | 1 -> emittedMember, "token"
                    | 3 -> emittedMember, "admit"
                    | _ -> emittedMember, "construct"

                let factory =
                    {
                        Owner =
                            namespaceName
                            + ".CssValue."
                            + String.concat "." path
                        MemberName = memberName
                        NodeId = id
                        Kind = kind
                        Production = None
                        UnitName = None
                        ArgumentType = None
                        Slots = None
                    }

                (factory.Owner, memberName), factory)
            |> Seq.toList

        let explicitFactories =
            match tryProperty "factories" root with
            | None -> []
            | Some factories ->
                factories.EnumerateArray ()
                |> Seq.map (fun item ->
                    let id = (required "nodeId" item).GetInt32()
                    checkNode id

                    let slots =
                        tryProperty "slots" item
                        |> Option.bind (fun slots ->
                            if slots.ValueKind = JsonValueKind.Null then
                                None
                            else
                                Some (
                                    slots.EnumerateArray ()
                                    |> Seq.map (fun slot -> slot.GetInt32 ())
                                    |> Seq.toList
                                ))

                    let factory =
                        {
                            Owner = text "owner" item
                            MemberName = text "memberName" item
                            NodeId = id
                            Kind = text "kind" item
                            Production = optionalText "production" item
                            UnitName = optionalText "unitName" item
                            ArgumentType = optionalText "argumentType" item
                            Slots = slots
                        }

                    (factory.Owner, factory.MemberName), factory)
                |> Seq.toList

        let properties =
            (required "properties" root).EnumerateArray()
            |> Seq.map (fun item ->
                let property =
                    {
                        Owner = text "owner" item
                        MemberName = text "memberName" item
                        NodeId = (required "nodeId" item).GetInt32()
                        CssName = text "cssName" item
                    }

                checkNode property.NodeId
                (property.Owner, property.MemberName), property)
            |> Map.ofSeq

        {
            Namespace = namespaceName
            Table = table
            Nodes = nodes
            Factories = grammarFactories @ explicitFactories |> Map.ofList
            Properties = properties
        }

    let load path =
        File.ReadAllText path |> loadJson
