namespace Ranvier.Css.Spec

/// <summary>Chooses shared named representations for anonymous grammar positions while preserving existing value admissions.</summary>
module internal CompactRepresentationGeneration =
    /// <summary>Returns categories with exact named sinks and safely broadened anonymous representations.</summary>
    /// <remarks>Named nodes are emitted even when absent from publicNodeIds. Only public node IDs occur in the returned Categories map.
    /// Empty languages remain exact. Refinement restores exact source representations until all retained target capabilities survive.</remarks>
    let plan (graph: GrammarGraph) (namedNodeIds: seq<int>) (publicNodeIds: seq<int>) : CompactCategories =
        let named = Set.ofSeq namedNodeIds
        let exposed = Set.ofSeq publicNodeIds
        let all = Set.union named exposed
        let original = CompactCategoryGeneration.plan graph all
        let namedOnly = CompactCategoryGeneration.plan graph named

        let language nodeId =
            original.Accepted[original.Categories[nodeId]]

        let mutable representation =
            exposed
            |> Seq.map (fun nodeId ->
                let target =
                    if
                        Set.contains nodeId named
                        || Set.isEmpty (language nodeId)
                    then
                        nodeId
                    else
                        match namedOnly.Implemented[nodeId] with
                        | [ target ] -> target
                        | _ -> nodeId

                nodeId, target)
            |> Map.ofSeq

        let requested () =
            representation
            |> Map.toSeq
            |> Seq.map snd
            |> Set.ofSeq
            |> Set.union named

        let mutable current = CompactCategoryGeneration.plan graph (requested ())
        let mutable changed = true

        while changed do
            let restore =
                representation
                |> Map.toList
                |> List.choose (fun (sourceId, representedBy) ->
                    if sourceId = representedBy then
                        None
                    else
                        let represented = current.Accepted[current.Categories[representedBy]]

                        let losesCapability =
                            current.Implemented[sourceId]
                            |> List.exists (fun target -> not (Set.isSubset represented current.Accepted[target]))

                        if losesCapability then Some sourceId else None)

            changed <- not restore.IsEmpty

            if changed then
                for sourceId in restore do
                    representation <- Map.add sourceId sourceId representation

                current <- CompactCategoryGeneration.plan graph (requested ())

        { current with
            Categories =
                representation
                |> Map.map (fun _ representedBy -> current.Categories[representedBy])
        }
