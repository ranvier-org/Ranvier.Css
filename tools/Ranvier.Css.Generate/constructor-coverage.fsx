// Reports flattened-constructor coverage for the shared value types in a compact manifest.
//
//   dotnet build src/Ranvier.Css -c Release
//   dotnet fsi tools/Ranvier.Css.Generate/constructor-coverage.fsx -- [mode] [types]
//
// Modes:
//   types               (default) every type admitting two or more functions: function count,
//                       flattenable functions, unflattenable functions, prose primitives without a factory
//   detail a,b,c        per-function parameter list of the named types, flagging repeated derived names
//   primitives          prose-defined primitives without a factory, ranked by referencing properties
//
// The script loads the Release Ranvier.Css.dll, so a running script blocks a rebuild of that project on Windows.
#r "../../src/Ranvier.Css/bin/Release/net10.0/Ranvier.Css.dll"

open System
open System.IO
open System.Text.Json
open Ranvier.Css.Internal

let arguments =
    fsi.CommandLineArgs
    |> Array.skip 1
    |> Array.filter ((<>) "--")

let mode =
    arguments
    |> Array.tryItem 0
    |> Option.defaultValue "types"

let manifest =
    let path =
        Path.Combine (__SOURCE_DIRECTORY__, "..", "..", "src", "Ranvier.Css.Bindings", "Generated", "Css.manifest.json")

    JsonDocument.Parse(File.ReadAllText path).RootElement

let table =
    GrammarRuntime.Table.decode (manifest.GetProperty("encodedGrammar").GetString())

let nodes: Map<int, GrammarRuntime.Node> =
    let flags =
        Reflection.BindingFlags.Public
        ||| Reflection.BindingFlags.NonPublic

    let _, fields =
        Reflection.FSharpValue.GetUnionFields (table, typeof<GrammarRuntime.Table>, flags)

    unbox fields[0]

let node id = nodes[id]

/// Node IDs that a scalar or flattened factory constructs; keyword and CSS-wide tokens are excluded.
let factoryNodes =
    manifest.GetProperty("factories").EnumerateArray()
    |> Seq.filter (fun factory -> not (List.contains (factory.GetProperty("kind").GetString()) [ "token"; "wide" ]))
    |> Seq.map (fun factory -> factory.GetProperty("nodeId").GetInt32())
    |> Set.ofSeq

let nullableCache = Collections.Generic.Dictionary<int, bool>()

let rec nullable (seen: Set<int>) id =
    match nullableCache.TryGetValue id with
    | true, value -> value
    | _ when seen.Contains id -> false
    | _ ->
        let n = node id
        let seen = seen.Add id

        let value =
            match n.Kind with
            | 1 -> n.Text = ""
            | 2 -> nullable seen n.Target
            | 3
            | 6 -> Array.exists (nullable seen) n.Children
            | 4
            | 5 -> Array.forall (nullable seen) n.Children
            | 7 -> n.Minimum = 0 || nullable seen n.Children[0]
            | 11 -> nullable seen n.Children[0]
            | _ -> false

        nullableCache[id] <- value
        value

/// Nodes a single value of the target can be, following GrammarGraphGeneration.acceptedNodes.
let accepted target =
    let rec collect (visited: Set<int>) id =
        if visited.Contains id then
            visited
        else
            let n = node id

            let next =
                match n.Kind with
                | 2 -> [| n.Target |]
                | 3
                | 6
                | 8
                | 11 -> n.Children
                | 7 when
                    n.Minimum <= 1
                    && (n.Maximum = -1 || n.Maximum >= 1)
                    ->
                    n.Children
                | 4
                | 5 ->
                    n.Children
                    |> Array.indexed
                    |> Array.choose (fun (index, child) ->
                        let othersNullable =
                            n.Children
                            |> Array.indexed
                            |> Array.forall (fun (other, sibling) -> other = index || nullable Set.empty sibling)

                        if othersNullable then Some child else None)
                | _ -> [||]

            Array.fold collect (visited.Add id) next

    collect Set.empty target

/// Shared types keyed by name; scoped definitions contribute additional roots.
let typeRoots =
    nodes
    |> Map.values
    |> Seq.filter (fun n -> n.Kind = 2 && n.Text <> "")
    |> Seq.groupBy _.Text
    |> Seq.map (fun (name, references) -> name, references |> Seq.map _.Target |> Set.ofSeq)
    |> Map.ofSeq

let acceptedBy name =
    typeRoots[name]
    |> Seq.map accepted
    |> Set.unionMany

let functionsOf name =
    acceptedBy name
    |> Seq.map node
    |> Seq.filter (fun n -> n.Kind = 9 && n.Text <> "")
    |> Seq.groupBy _.Text
    |> Seq.sortBy fst
    |> Seq.map (fun (functionName, productions) ->
        let best =
            productions
            |> Seq.choose (fun production -> GrammarRuntime.tryParameters table production.Id)
            |> Seq.sortByDescending _.Length
            |> Seq.tryHead

        functionName, best)
    |> Seq.toList

let rec referenceName id =
    let n = node id

    match n.Kind with
    | 0
    | 2 when n.Text <> "" ->
        if n.Text.EndsWith "-value" then
            n.Text[.. n.Text.Length - 7]
        else
            n.Text
    | 3 ->
        n.Children
        |> Array.tryFind (fun child -> (node child).Kind <> 1)
        |> Option.map referenceName
        |> Option.defaultValue "value"
    | _ -> "value"

/// The name CompactColorGeneration derives before a naming policy applies.
let derivedName parameter =
    match parameter with
    | GrammarRuntime.Required (id, keyword)
    | GrammarRuntime.Optional (id, keyword) -> if keyword <> "" then keyword else referenceName id
    | GrammarRuntime.Repeated (_, items) ->
        match Array.tryHead items with
        | Some (GrammarRuntime.Required (id, keyword))
        | Some (GrammarRuntime.Optional (id, keyword)) ->
            (if keyword <> "" then keyword else referenceName id)
            + "s"
        | _ -> "values"

match mode with
| "types" ->
    printfn "type\tfunctions\tflattenable\tunflattenable\tprimitives-without-factory"

    for name in typeRoots.Keys do
        let functions = functionsOf name

        if functions.Length >= 2 then
            let flat, rest = functions |> List.partition (snd >> Option.isSome)

            let primitives =
                acceptedBy name
                |> Seq.map node
                |> Seq.filter (fun n ->
                    n.Kind = 0
                    && n.Text <> ""
                    && not (factoryNodes.Contains n.Id))
                |> Seq.map _.Text
                |> Seq.distinct

            printfn "%s\t%d\t%d\t%s\t%s" name functions.Length flat.Length (rest |> List.map fst |> String.concat ",") (String.concat "," primitives)
| "detail" ->
    for name in arguments[1].Split ',' do
        printfn "== <%s>" name

        for functionName, parameters in functionsOf name do
            match parameters with
            | None -> printfn "  %-26s not flattenable" (functionName + "()")
            | Some parameters ->
                let names = parameters |> Array.map derivedName
                let repeated = (Array.distinct names).Length <> names.Length

                printfn "  %-26s [%s]%s" (functionName + "()") (String.concat ", " names) (if repeated then "  repeated names" else "")
| "primitives" ->
    let rec reach (visited: Set<int>) id =
        if visited.Contains id then
            visited
        else
            let n = node id

            let next =
                if n.Kind = 2 then
                    Array.append [| n.Target |] n.Children
                else
                    n.Children

            Array.fold reach (visited.Add id) next

    manifest.GetProperty("properties").EnumerateArray()
    |> Seq.collect (fun property ->
        let cssName = property.GetProperty("cssName").GetString()

        reach Set.empty (property.GetProperty("nodeId").GetInt32())
        |> Seq.map node
        |> Seq.filter (fun n ->
            n.Kind = 0
            && n.Text <> ""
            && not (factoryNodes.Contains n.Id))
        |> Seq.map (fun n -> n.Text, cssName))
    |> Seq.distinct
    |> Seq.groupBy fst
    |> Seq.sortByDescending (fun (_, uses) -> Seq.length uses)
    |> Seq.iter (fun (primitive, uses) ->
        printfn
            "%-30s %3d properties, e.g. %s"
            primitive
            (Seq.length uses)
            (uses
             |> Seq.map snd
             |> Seq.truncate 3
             |> String.concat ", "))
| other -> failwithf "Unknown mode '%s'. Use types, detail <a,b> or primitives." other
