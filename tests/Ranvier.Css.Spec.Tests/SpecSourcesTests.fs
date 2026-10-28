module Ranvier.Css.Spec.SpecSourcesTests

open System
open System.IO
open System.Text
open System.Text.Json
open Expecto
open Ranvier.Css.Spec

let private consume label source =
    match SpecSources.consume label (Encoding.UTF8.GetBytes (source: string)) with
    | Ok catalogue -> catalogue
    | Error errors -> failtestf "Spec input failed: %A" errors

let private consolidate catalogues =
    match SpecSources.consolidate catalogues with
    | Ok result -> result
    | Error errors -> failtestf "Consolidation failed: %A" errors

let private document level series properties =
    sprintf """{"spec":{"title":"Example Level %d","url":"https://example.com/%s-%d/"},"properties":%s}""" level series level properties

/// <summary>Verifies nested source retention, latest-level precedence, explicit obsolescence, and the pinned spec corpus.</summary>
let tests =
    testList
        "Individual CSS spec sources"
        [
            testCase "nested values and unknown metadata survive"
            <| fun _ ->
                let source =
                    """{"spec":{"title":"Example Level 1","url":"https://example.com/css-example-1/"},"unknown":{"future":true},"values":[{"name":"<paint>","type":"type","value":"red","custom":{"x":1},"values":[{"name":"red","type":"value","value":"red"}]}]}"""

                let catalogue = consume "nested" source
                Expect.equal catalogue.Features.Length 2 "Every nested definition is retained."

                let parent =
                    catalogue.Features
                    |> List.find (fun feature -> feature.Id.Name = "paint")

                let child =
                    catalogue.Features
                    |> List.find (fun feature -> feature.Id.Name = "red")

                Expect.equal child.Id.Scopes [ "<paint>" ] "Nested definitions retain their owner scope."
                Expect.equal parent.Metadata["custom"] """{"x":1}""" "Unknown feature metadata stays raw JSON."
                Expect.stringContains parent.Metadata["sourceDocument"] "unknown" "Unknown root metadata survives."
                Expect.stringContains parent.Metadata["values"] "red" "The original nested definition survives."
            testCase "latest shared definitions replace older levels while older unique definitions remain"
            <| fun _ ->
                let older =
                    consume "older" (document 4 "css-example" """[{"name":"paint","value":"red"},{"name":"old-only","value":"blue"}]""")

                let latest =
                    consume "latest" (document 5 "css-example" """[{"name":"paint","value":"red | blue"}]""")

                let result = consolidate [ latest; older ]
                Expect.equal result.Current.Features.Length 2 "Level 5 extends the preceding level."

                let paint =
                    result.Current.Features
                    |> List.find (fun feature -> feature.Id.Name = "paint")

                Expect.equal paint.Metadata["specLevel"] "5" "The highest level supplies the current definition."
                Expect.equal result.Obsolete.Features.Length 1 "The preceding definition remains inspectable."
                Expect.equal result.Obsolete.Features.Head.Metadata["obsolete"] "true" "Superseded definitions are obsolete."
                Expect.isTrue (result.Obsolete.Features.Head.Metadata.ContainsKey "supersededBy") "Replacement provenance is explicit."
                Expect.equal (consolidate [ older; latest ]) result "Input order does not affect consolidation."
            testCase "explicitly deprecated definitions become obsolete"
            <| fun _ ->
                let input =
                    consume "deprecated" (document 2 "css-example" """[{"name":"retired","value":"red","deprecated":true}]""")

                let result = consolidate [ input ]
                Expect.isEmpty result.Current.Features "Deprecated source definitions are excluded from the current catalogue."
                Expect.equal result.Obsolete.Features.Length 1 "Deprecated definitions remain inspectable."
            testCase "string obsolescence and replacement annotations become obsolete"
            <| fun _ ->
                let input =
                    consume
                        "obsolete"
                        (document
                            2
                            "css-example"
                            """[{"name":"retired","value":"red","obsolete":"true"},{"name":"replaced","value":"blue","supersededBy":"https://example.com/new"}]""")

                let result = consolidate [ input ]
                Expect.isEmpty result.Current.Features "Obsolete annotations accept raw JSON strings and replacement URLs."
                Expect.equal result.Obsolete.Features.Length 2 "Both obsolete definitions remain inspectable."
            testCase "unrelated conflicting definitions produce deterministic diagnostics"
            <| fun _ ->
                let first = consume "a" (document 1 "first" """[{"name":"paint","value":"red"}]""")

                let second =
                    consume "b" (document 9 "second" """[{"name":"paint","value":"blue"}]""")

                let forward = SpecSources.consolidate [ first; second ]
                Expect.isError forward "Levels in unrelated series do not establish precedence."
                Expect.equal forward (SpecSources.consolidate [ second; first ]) "Conflict diagnostics are stable."
            testCase "canonical merged grammar resolves extensions from unrelated series"
            <| fun _ ->
                let first = consume "a" (document 1 "first" """[{"name":"paint","value":"red"}]""")

                let second =
                    consume "b" (document 9 "second" """[{"name":"paint","value":"blue"}]""")

                let canonical =
                    consume "canonical" (document 1 "merged" """[{"name":"paint","value":"red | blue"}]""")

                match SpecSources.consolidateWith canonical [ first; second ] with
                | Error errors -> failtestf "%A" errors
                | Ok result ->
                    Expect.equal
                        result.Current.Features.Head.Grammar
                        canonical.Features.Head.Grammar
                        "Canonical merged syntax supplies the extension."

                    Expect.isEmpty result.Obsolete.Features "Cross-series extensions remain current source definitions."

                    use definitions =
                        JsonDocument.Parse result.Current.Features.Head.Metadata["sourceDefinitions"]

                    Expect.equal (definitions.RootElement.GetArrayLength ()) 2 "Both raw source definitions remain inspectable."
            testCase "newer raw spec overrides older canonical level metadata or href"
            <| fun _ ->
                let older =
                    consume "canonical-level4" (document 4 "css-example" """[{"name":"paint","value":"red"}]""")

                let latest =
                    consume "raw-level5" (document 5 "css-example" """[{"name":"paint","value":"red | blue"}]""")

                let hrefOnly =
                    { older with
                        Features =
                            older.Features
                            |> List.map (fun feature -> { feature with Metadata = Map.empty })
                    }

                for canonical in [ older; hrefOnly ] do
                    match SpecSources.consolidateWith canonical [ latest ] with
                    | Error errors -> failtestf "%A" errors
                    | Ok result ->
                        Expect.equal
                            result.Current.Features.Head.Grammar
                            latest.Features.Head.Grammar
                            "Older canonical syntax cannot replace a newer spec."

                        Expect.equal result.Current.Features.Head.Href latest.Features.Head.Href "Current provenance points to the latest spec."
            testCase "equal or newer canonical level retains merged extensions"
            <| fun _ ->
                let latest =
                    consume "raw-level5" (document 5 "css-example" """[{"name":"paint","value":"red"}]""")

                for level in [ 5; 6 ] do
                    let canonical =
                        consume "canonical" (document level "css-example" """[{"name":"paint","value":"red | blue"}]""")

                    match SpecSources.consolidateWith canonical [ latest ] with
                    | Error errors -> failtestf "%A" errors
                    | Ok result ->
                        Expect.equal result.Current.Features.Head.Grammar canonical.Features.Head.Grammar "Current canonical extensions survive."
            testCase "stale canonical with conflicting cross-series extensions requires refresh"
            <| fun _ ->
                let canonical =
                    consume "canonical-level4" (document 4 "first" """[{"name":"paint","value":"red | green"}]""")

                let latest =
                    consume "raw-level5" (document 5 "first" """[{"name":"paint","value":"red | blue"}]""")

                let extension =
                    consume "extension" (document 9 "second" """[{"name":"paint","value":"green"}]""")

                match SpecSources.consolidateWith canonical [ latest; extension ] with
                | Ok _ -> failtest "Replacing stale canonical grammar must preserve or diagnose active extensions."
                | Error diagnostics ->
                    Expect.stringContains
                        diagnostics.Head.Message
                        "Refresh the canonical dataset"
                        "Ambiguous extensions require fresh canonical merging."
            testCase "obsolete replacement links stay within their source series"
            <| fun _ ->
                let older =
                    consume "first-level4" (document 4 "first" """[{"name":"paint","value":"red"}]""")

                let latest =
                    consume "first-level5" (document 5 "first" """[{"name":"paint","value":"red | blue"}]""")

                let unrelated =
                    consume "second-level9" (document 9 "second" """[{"name":"paint","value":"green"}]""")

                let canonical =
                    consume "merged" (document 1 "merged" """[{"name":"paint","value":"red | blue | green"}]""")

                match SpecSources.consolidateWith canonical [ older; latest; unrelated ] with
                | Error errors -> failtestf "%A" errors
                | Ok result ->
                    Expect.equal result.Obsolete.Features.Length 1 "Only the older level in the first series is obsolete."

                    Expect.equal
                        result.Obsolete.Features.Head.Metadata["supersededBy"]
                        (JsonSerializer.Serialize latest.Features.Head.Href)
                        "The replacement is the newest definition in the same series."
            testCase "newer canonical level archives the preceding raw definition"
            <| fun _ ->
                let older =
                    consume "raw-level1" (document 1 "css-example" """[{"name":"paint","value":"red"}]""")

                let canonical =
                    consume "canonical-level2" (document 2 "css-example" """[{"name":"paint","value":"red | blue"}]""")

                match SpecSources.consolidateWith canonical [ older ] with
                | Error diagnostics -> failtestf "%A" diagnostics
                | Ok result ->
                    Expect.equal result.Current.Features.Head.Grammar canonical.Features.Head.Grammar "The newer canonical definition stays current."
                    Expect.equal result.Obsolete.Features.Length 1 "The preceding raw definition remains in the obsolete catalogue."

                    Expect.equal
                        result.Obsolete.Features.Head.Metadata["supersededBy"]
                        (JsonSerializer.Serialize canonical.Features.Head.Href)
                        "Replacement points to the newer canonical definition."
            testCase "repeated source identities remain independently inspectable"
            <| fun _ ->
                let input =
                    consume "duplicates" (document 1 "css-example" """[{"name":"paint","value":"red"},{"name":"paint","value":"blue"}]""")

                Expect.equal input.Features.Length 2 "Both definitions survive."

                Expect.equal
                    (input.Features
                     |> List.map (fun feature -> feature.Id)
                     |> List.distinct
                     |> List.length)
                    2
                    "Source scopes prevent collisions."
            testCase "malformed source structure returns diagnostics"
            <| fun _ ->
                Expect.isError
                    (SpecSources.consume "invalid" (Encoding.UTF8.GetBytes """{"spec":{"title":"x","url":"relative"}}"""))
                    "Spec URLs are absolute."

                Expect.isError
                    (SpecSources.consume "invalid" (Encoding.UTF8.GetBytes """{"spec":{"title":"x","title":"y","url":"https://example.com/"}}"""))
                    "Duplicate metadata members are rejected."
            testCase "oversized spec levels return bounded input diagnostics"
            <| fun _ ->
                let cases =
                    [
                        "$.spec.title", """{"spec":{"title":"Example Level 9999999999999999999999999","url":"https://example.com/css-example-1/"}}"""
                        "$.spec.url", """{"spec":{"title":"Example","url":"https://example.com/css-example-9999999999999999999999999/"}}"""
                    ]

                for expectedPath, source in cases do
                    match SpecSources.consume "oversized" (Encoding.UTF8.GetBytes source) with
                    | Ok _ -> failtest "Oversized levels must be rejected."
                    | Error diagnostics -> Expect.equal diagnostics.Head.Path expectedPath "Overflow returns the offending JSON path."
            testCase "every locked spec file preserves all definitions"
            <| fun _ ->
                let root = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "../.."))
                let lockPath = Path.Combine (root, "tools/Ranvier.Css.Generate/specs.lock.json")
                use sourceLock = JsonDocument.Parse (File.ReadAllBytes lockPath)

                let files =
                    sourceLock.RootElement.GetProperty("files").EnumerateArray()
                    |> Seq.toArray

                Expect.equal files.Length 122 "The source revision contains 122 ed/css extracts."
                let mutable definitions = 0
                let mutable missing = 0
                let catalogues = ResizeArray<Catalogue>()

                for entry in files do
                    let name = entry.GetProperty("file").GetString()
                    let path = Path.Combine (root, "tools/Ranvier.Css.Generate/out/specs", name)

                    Expect.isTrue
                        (File.Exists path)
                        ("Fetch the locked spec corpus before this test: "
                         + name)

                    let bytes = File.ReadAllBytes path

                    let catalogue =
                        match SpecSources.consume name bytes with
                        | Ok result -> result
                        | Error errors -> failtestf "%s: %A" name errors

                    Expect.equal catalogue.Identity.Sha256 (entry.GetProperty("sha256").GetString()) ("Locked digest: " + name)
                    use original = JsonDocument.Parse bytes
                    catalogues.Add catalogue

                    let rec count (value: JsonElement) =
                        let own =
                            match value.TryGetProperty "name" with
                            | true, _ -> 1
                            | _ -> 0

                        own
                        + ([ "properties"; "atrules"; "selectors"; "values"; "descriptors" ]
                           |> List.sumBy (fun key ->
                               match value.TryGetProperty key with
                               | true, children when children.ValueKind = JsonValueKind.Array -> children.EnumerateArray () |> Seq.sumBy count
                               | _ -> 0))

                    Expect.equal catalogue.Features.Length (count original.RootElement) ("Every definition: " + name)
                    definitions <- definitions + catalogue.Features.Length

                    missing <-
                        missing
                        + (catalogue.Features
                           |> List.filter (fun feature -> feature.Grammar = GrammarStatus.Missing)
                           |> List.length)

                    let rejected =
                        catalogue.Features
                        |> List.choose (fun feature ->
                            match feature.Grammar with
                            | GrammarStatus.Rejected (source, error) -> Some (feature.Id, source, error)
                            | _ -> None)

                    Expect.isEmpty rejected ("All specified grammars parse: " + name)

                Expect.equal definitions 4785 "The complete source corpus definition count is stable."
                Expect.equal missing 190 "Missing grammars remain explicit."

                let canonicalPath =
                    Path.Combine (root, "tools/Ranvier.Css.Generate/node_modules/@webref/css/css.json")

                let canonical =
                    match Webref.consume "8.7.8" (File.ReadAllBytes canonicalPath) with
                    | Ok value -> value
                    | Error errors -> failtestf "Canonical Webref input failed: %A" errors

                match SpecSources.consolidateWith canonical (List.ofSeq catalogues) with
                | Error errors -> failtestf "Unresolved canonical/source conflicts: %A" errors
                | Ok result ->
                    Expect.equal result.Current.Features.Length 4596 "Supplementary raw definitions stay current."
                    Expect.equal result.Obsolete.Features.Length 286 "Superseded spec levels are explicit."

                    let resolution =
                        Resolution.resolve
                            { result.Current with
                                Features = result.Current.Features @ result.Obsolete.Features
                            }

                    let ambiguous =
                        resolution.References
                        |> List.filter (fun r ->
                            match r.Target with
                            | ReferenceTarget.Ambiguous _ -> true
                            | _ -> false)

                    let missing =
                        resolution.References
                        |> List.filter (fun r -> r.Target = ReferenceTarget.Missing)

                    Expect.isEmpty ambiguous "Equivalent source aliases do not leave ambiguity."

                    Expect.equal
                        (missing |> List.map _.Reference.Name)
                        [ "anchored-feature"; "anchored-feature" ]
                        "Only explicit upstream gaps remain."
        ]
