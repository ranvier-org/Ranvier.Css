namespace Ranvier.Css.Spec

open System.Collections.Generic

/// <summary>Semantic admission categories, their immediate supersets and the minimal categories implemented by each raw value.</summary>
type internal CompactCategories =
    {
        Categories: Map<int, int>
        Representatives: int list
        Accepted: Map<int, Set<int>>
        Parents: Map<int, int list>
        Implemented: Map<int, int list>
    }

module internal CompactCategoryGeneration =
    /// <summary>Coalesces semantic atom languages and computes their inclusion covers; representatives precede their subsets.</summary>
    /// <remarks>References, choices and required groups preserve source provenance. Numeric ranges remain runtime checks.
    /// Uninhabited atom languages remain isolated from productive categories.</remarks>
    let plan (graph: GrammarGraph) (requestedNodeIds: seq<int>) : CompactCategories =
        let canonicalLiterals =
            graph.Nodes
            |> Seq.filter (fun node -> node.Kind = 1)
            |> Seq.groupBy _.Text
            |> Seq.map (fun (text, nodes) -> text, nodes |> Seq.map _.Id |> Seq.min)
            |> Map.ofSeq

        let atoms = Dictionary<int, int option>()

        for node in graph.Nodes do
            atoms.Add (
                node.Id,
                match node.Kind with
                | 2
                | 3
                | 8 -> None
                | 1 -> Some canonicalLiterals[node.Text]
                | _ -> Some node.Id
            )

        let languages = Dictionary<int, Set<int>>()

        for node in graph.Nodes do
            let language =
                GrammarGraphGeneration.acceptedNodes graph node.Id
                |> Seq.choose (fun sourceId -> atoms[sourceId])
                |> Set.ofSeq

            languages.Add (node.Id, language)

        let categories = ResizeArray<int * int>()
        let groups = Dictionary<Set<int>, int>()
        let admitted = Dictionary<int, Set<int>>()
        let postings = Dictionary<int, ResizeArray<int>>()

        for KeyValue (_, atom) in atoms do
            match atom with
            | Some id when not (postings.ContainsKey id) -> postings.Add (id, ResizeArray<int>())
            | _ -> ()

        for nodeId in requestedNodeIds |> Seq.distinct |> Seq.sort do
            let accepted =
                match languages.TryGetValue nodeId with
                | true, language -> language
                | _ -> invalidArg "requestedNodeIds" "Unknown CSS grammar node."

            let representative =
                match groups.TryGetValue accepted with
                | true, existing -> existing
                | _ ->
                    groups.Add (accepted, nodeId)
                    admitted.Add (nodeId, accepted)

                    for atom in accepted do
                        postings[atom].Add nodeId

                    nodeId

            categories.Add (nodeId, representative)

        let representatives =
            admitted.Keys
            |> Seq.sortBy (fun id -> -admitted[id].Count, id)
            |> Seq.toList

        let candidates (language: Set<int>) =
            if Set.isEmpty language then
                Seq.empty
            else
                language
                |> Seq.minBy (fun atom -> postings[atom].Count)
                |> fun atom -> postings[atom] :> seq<int>

        let parents = Dictionary<int, int list>()

        for representative in representatives do
            let accepted = admitted[representative]
            let mutable covers = []

            candidates accepted
            |> Seq.filter (fun candidate ->
                admitted[candidate].Count > accepted.Count
                && Set.isSubset accepted admitted[candidate])
            |> Seq.sortBy (fun candidate -> admitted[candidate].Count, candidate)
            |> Seq.iter (fun candidate ->
                if
                    covers
                    |> List.exists (fun parent -> Set.isSubset admitted[parent] admitted[candidate])
                    |> not
                then
                    covers <- candidate :: covers)

            parents.Add (representative, List.sort covers)

        let implementations = Dictionary<Set<int>, int list>()

        let implemented =
            languages
            |> Seq.map (fun pair ->
                let language = pair.Value

                let minimal =
                    match implementations.TryGetValue language with
                    | true, existing -> existing
                    | _ ->
                        let minimal =
                            match groups.TryGetValue language with
                            | true, exact -> [ exact ]
                            | _ ->
                                let containing =
                                    candidates language
                                    |> Seq.filter (fun category ->
                                        admitted[category].Count >= language.Count
                                        && Set.isSubset language admitted[category])
                                    |> Seq.toArray

                                let inherited = HashSet<int>()

                                for category in containing do
                                    for parent in parents[category] do
                                        inherited.Add parent |> ignore

                                containing
                                |> Seq.filter (inherited.Contains >> not)
                                |> Seq.sort
                                |> Seq.toList

                        implementations.Add (language, minimal)
                        minimal

                pair.Key, minimal)
            |> Map.ofSeq

        {
            Categories = Map.ofSeq categories
            Representatives = representatives
            Accepted =
                admitted
                |> Seq.map (fun pair -> pair.Key, pair.Value)
                |> Map.ofSeq
            Parents =
                parents
                |> Seq.map (fun pair -> pair.Key, pair.Value)
                |> Map.ofSeq
            Implemented = implemented
        }
