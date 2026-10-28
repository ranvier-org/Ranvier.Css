module Ranvier.Css.Spec.ResolutionTests

open Ranvier.Css.Spec
open Expecto

let private feature kind name scopes owner syntax =
    {
        Id =
            {
                Kind = kind
                Name = name
                Scopes = scopes
                Owner = owner
            }
        Href =
            "https://example.test/spec#"
            + name
            + "-"
            + String.concat "-" scopes
        Extended = []
        Grammar =
            match syntax with
            | None -> GrammarStatus.Missing
            | Some source ->
                match Grammar.parse source with
                | Ok tree -> GrammarStatus.Parsed (source, tree)
                | Error error -> failwithf "%s: %A" source error
        Metadata = Map.empty
    }

let private catalogue features =
    {
        Identity = { Label = "resolution"; Sha256 = "" }
        Features = features
    }

let private targets owner result =
    result.References
    |> List.filter (fun reference -> reference.Owner = owner.Id)
    |> List.map (fun reference -> reference.Target)

let private cases =
    [
        "reference kinds bind independently and normalize decorated names",
        fun () ->
            let source =
                feature FeatureKind.Property "source" [] None (Some "<thing> | <'thing'> | <thing()>")

            let dataType = feature FeatureKind.Type "<thing>" [] None None
            let property = feature FeatureKind.Property "thing" [] None None
            let func = feature FeatureKind.Function "thing( )" [] None None

            let result =
                catalogue [ source; dataType; property; func ]
                |> Resolution.resolve

            Expect.equal
                (targets source result)
                ([ dataType; property; func ]
                 |> List.map (fun f -> ReferenceTarget.Definition f.Id))
                "Kind-specific bindings"

        "decorated current type and inherited function scopes choose the local definition",
        fun () ->
            let source = feature FeatureKind.Type "shape" [] None (Some "<item>")
            let local = feature FeatureKind.Type "item" [ "<shape>" ] None None
            let other = feature FeatureKind.Type "item" [ "shape()" ] None None
            let globalDefinition = feature FeatureKind.Type "item" [] None None

            let result =
                catalogue [ source; local; other; globalDefinition ]
                |> Resolution.resolve

            Expect.equal (targets source result) [ ReferenceTarget.Definition local.Id ] "Current type uses angle-bracket scope"
            let nested = feature FeatureKind.Type "nested" [ "shape( )" ] None (Some "<item>")

            let inherited =
                catalogue [ nested; local; other; globalDefinition ]
                |> Resolution.resolve

            Expect.equal (targets nested inherited) [ ReferenceTarget.Definition other.Id ] "Inherited function scope uses parentheses"

        "descriptor property references prefer their at-rule owner",
        fun () ->
            let source =
                feature FeatureKind.Descriptor "source" [] (Some "@font-face") (Some "<'size'>")

            let local = feature FeatureKind.Descriptor "size" [] (Some "@font-face") None
            let other = feature FeatureKind.Descriptor "size" [] (Some "@page") None
            let property = feature FeatureKind.Property "size" [] None None

            let result =
                catalogue [ source; local; other; property ]
                |> Resolution.resolve

            Expect.equal (targets source result) [ ReferenceTarget.Definition local.Id ] "Descriptor owner binding"

            let pageSource =
                { source with
                    Id = { source.Id with Owner = Some "@page" }
                }

            let pageResult =
                catalogue [ pageSource; local; other; property ]
                |> Resolution.resolve

            Expect.equal (targets pageSource pageResult) [ ReferenceTarget.Definition other.Id ] "Equivalent descriptor grammar preserves its owner"

        "unrelated descriptors cannot satisfy a property reference",
        fun () ->
            let source = feature FeatureKind.Property "source" [] None (Some "<'size'>")
            let descriptor = feature FeatureKind.Descriptor "size" [] (Some "@page") None

            Expect.equal
                (catalogue [ source; descriptor ]
                 |> Resolution.resolve
                 |> targets source)
                [ ReferenceTarget.Missing ]
                "Owner isolation"

        "unique scoped definitions remain referenceable outside their defining context",
        fun () ->
            let source = feature FeatureKind.Function "lab()" [] None (Some "<alpha-value>")
            let alpha = feature FeatureKind.Type "alpha-value" [ "<color>" ] None None

            Expect.equal
                (catalogue [ source; alpha ]
                 |> Resolution.resolve
                 |> targets source)
                [ ReferenceTarget.Definition alpha.Id ]
                "Unique external reference"

        "ambiguous candidates retain every dependency edge in stable order",
        fun () ->
            let source = feature FeatureKind.Type "source" [] None (Some "<item>")
            let a = feature FeatureKind.Type "item" [ "a" ] None None
            let b = feature FeatureKind.Type "item" [ "b" ] None None
            let result = catalogue [ source; b; a ] |> Resolution.resolve
            let expected = [ a.Id; b.Id ] |> List.sort
            Expect.equal (targets source result) [ ReferenceTarget.Ambiguous expected ] "Ambiguity remains explicit"
            Expect.equal result.Dependencies[source.Id] expected "All candidate edges retained"
            Expect.equal result (catalogue [ a; source; b ] |> Resolution.resolve) "Input order independence"

        "terminal fallbacks are explicit and catalogue definitions take precedence",
        fun () ->
            let source =
                feature FeatureKind.Property "source" [] None (Some "<number> | <unknown-token> | <number()>")

            let result = catalogue [ source ] |> Resolution.resolve

            Expect.equal
                (targets source result)
                [
                    ReferenceTarget.Primitive "number"
                    ReferenceTarget.Missing
                    ReferenceTarget.Missing
                ]
                "Narrow primitive fallback"

            let number = feature FeatureKind.Type "number" [] None None
            let resolved = catalogue [ source; number ] |> Resolution.resolve
            Expect.equal (targets source resolved |> List.head) (ReferenceTarget.Definition number.Id) "Prose definition takes precedence"

        "type applications bind their constructor and argument independently",
        fun () ->
            let source =
                feature FeatureKind.Type "source" [] None (Some "<boolean-expr[ <test> ]>")

            let constructor = feature FeatureKind.Type "boolean-expr" [] None None
            let argument = feature FeatureKind.Type "test" [] None None

            let result =
                catalogue [ source; constructor; argument ]
                |> Resolution.resolve

            Expect.equal
                (targets source result)
                [
                    ReferenceTarget.Definition constructor.Id
                    ReferenceTarget.Definition argument.Id
                ]
                "Type application references"

            let uses =
                result.References
                |> List.filter (fun reference -> reference.Owner = source.Id)

            Expect.equal uses[0].Span.Offset 0 "Constructor source position"
            Expect.isGreaterThan uses[1].Span.Offset 0 "Argument source position"

        "mutual and self recursion produce components without expansion",
        fun () ->
            let a = feature FeatureKind.Type "a" [] None (Some "<b> | <b>")
            let b = feature FeatureKind.Type "b" [] None (Some "<a>")
            let self = feature FeatureKind.Type "self" [] None (Some "<self>")
            let leaf = feature FeatureKind.Type "leaf" [] None None

            let result =
                catalogue [ self; b; leaf; a ]
                |> Resolution.resolve

            Expect.equal
                result.Cycles
                ([ [ a.Id; b.Id ] |> List.sort; [ self.Id ] ]
                 |> List.sort)
                "Recursive components"

            Expect.equal result.Dependencies[a.Id] [ b.Id ] "Repeated uses share an edge"
            Expect.equal result.References.Length 4 "All reference occurrences retained"
            Expect.equal result.Dependencies[leaf.Id] [] "Prose leaves remain graph nodes"

        "same specification resolves duplicate names after scope preference",
        fun () ->
            let source =
                { feature FeatureKind.Type "source" [] None (Some "<item()>") with
                    Href = "https://drafts.example.test/shapes-1/#source"
                }

            let local =
                { feature FeatureKind.Function "item()" [ "<shape>" ] None None with
                    Href = "https://drafts.example.test/shapes-1/#item"
                }

            let foreign =
                { feature FeatureKind.Function "item()" [ "clip" ] None None with
                    Href = "https://drafts.example.test/masking-1/#item"
                }

            let result =
                catalogue [ source; foreign; local ]
                |> Resolution.resolve

            Expect.equal (targets source result) [ ReferenceTarget.Definition local.Id ] "Same document wins ambiguity"

            let scopedSource =
                { source with
                    Id = { source.Id with Scopes = [ "clip" ] }
                }

            let scoped =
                catalogue [ scopedSource; foreign; local ]
                |> Resolution.resolve

            Expect.equal (targets scopedSource scoped) [ ReferenceTarget.Definition foreign.Id ] "Scope outranks provenance"

        "generic helper bindings are confined to the boolean expression template",
        fun () ->
            let template =
                feature FeatureKind.Type "boolean-expr" [] None (Some "<boolean-expr-group>")

            let externalType =
                feature FeatureKind.Type "other" [] None (Some "<boolean-expr-group>")

            let property =
                feature FeatureKind.Property "boolean-expr" [] None (Some "<boolean-expr-group>")

            let result =
                catalogue [ template; externalType; property ]
                |> Resolution.resolve

            Expect.equal (targets template result) [ ReferenceTarget.Parameter "boolean-expr-group" ] "Template-local helper"
            Expect.equal (targets externalType result) [ ReferenceTarget.Missing ] "Other template owners remain unresolved"
            Expect.equal (targets property result) [ ReferenceTarget.Missing ] "Other reference kinds remain unresolved"
            Expect.equal result.Dependencies[template.Id] [] "Local parameters do not create catalogue edges"

        "equivalent aliases ignore source spans and prefer the canonical scope union",
        fun () ->
            let source = feature FeatureKind.Property "source" [] None (Some "<item>")

            let canonical =
                feature FeatureKind.Type "item" [ "a"; "b" ] None (Some "<number> | x")

            let raw =
                { feature FeatureKind.Type "item" [ "a" ] None (Some "  <number>  |  x") with
                    Href = canonical.Href
                    Metadata = Map.ofList [ "sourcePath", "\"$.values[0]\"" ]
                }

            let result =
                catalogue [ source; raw; canonical ]
                |> Resolution.resolve

            Expect.equal (targets source result) [ ReferenceTarget.Definition canonical.Id ] "Equivalent aliases collapse"
            Expect.isTrue (result.Dependencies.ContainsKey raw.Id) "Raw definition remains represented"

        "nested scopes prefer all matching ancestors",
        fun () ->
            let source =
                feature FeatureKind.Type "source" [ "@rule"; "parent()" ] None (Some "<item>")

            let shallow = feature FeatureKind.Type "item" [ "@rule" ] None (Some "a")
            let nested = feature FeatureKind.Type "item" [ "@rule"; "parent()" ] None (Some "b")

            let result =
                catalogue [ source; shallow; nested ]
                |> Resolution.resolve

            Expect.equal (targets source result) [ ReferenceTarget.Definition nested.Id ] "Both ancestors outrank one"


        "current and archived forwarding aliases prefer the external length production",
        fun () ->
            let outer =
                { feature FeatureKind.Type "length" [] None None with
                    Href = "https://example.test/values-4/#length"
                }

            let current =
                { feature FeatureKind.Type "length" [ "background-position" ] None (Some "<length>") with
                    Href = "https://example.test/backgrounds-4/#length"
                }

            let archived =
                { current with
                    Id =
                        { current.Id with
                            Scopes = [ "background-position"; "#obsolete:backgrounds-3" ]
                        }
                    Href = "https://example.test/backgrounds-3/#length"
                }

            let result =
                catalogue [ current; archived; outer ]
                |> Resolution.resolve

            for alias in [ current; archived ] do
                Expect.equal
                    (targets alias result)
                    [ ReferenceTarget.Definition outer.Id ]
                    "Peer forwarding aliases do not shadow the enclosing definition."

            Expect.isEmpty result.Cycles "The primitive aliases must not create a reference-only cycle."

            Expect.equal
                result
                (catalogue [ outer; archived; current ]
                 |> Resolution.resolve)
                "Alias resolution remains deterministic."

        "forwarding aliases retain the nearest enclosing semantic scope",
        fun () ->
            let outer = feature FeatureKind.Type "item" [ "outer" ] None None
            let globalDefinition = feature FeatureKind.Type "item" [] None None

            let local =
                feature FeatureKind.Type "item" [ "outer"; "inner" ] None (Some "<item>")

            let archived =
                { local with
                    Id =
                        { local.Id with
                            Scopes = local.Id.Scopes @ [ "#obsolete:older" ]
                        }
                    Href = "https://example.test/older/#item"
                }

            let result =
                catalogue [ local; archived; outer; globalDefinition ]
                |> Resolution.resolve

            Expect.equal (targets local result) [ ReferenceTarget.Definition outer.Id ] "The enclosing scope outranks a global definition."

            Expect.equal
                (targets archived result)
                [ ReferenceTarget.Definition outer.Id ]
                "Archive markers are provenance rather than semantic scope."

            Expect.isEmpty result.Cycles "Peers cannot replace the actual enclosing definition."

        "productive recursion survives both global and scoped forwarding rules",
        fun () ->
            for scopes in [ []; [ "garden" ] ] do
                let recursive =
                    feature FeatureKind.Type "tree" scopes None (Some "leaf | branch( <tree> )")

                let definitions =
                    if scopes.IsEmpty then
                        [ recursive ]
                    else
                        [ recursive; feature FeatureKind.Type "tree" [] None None ]

                let result = catalogue definitions |> Resolution.resolve

                Expect.equal
                    (targets recursive result)
                    [ ReferenceTarget.Definition recursive.Id ]
                    "A productive grammar keeps its recursive reference."

                Expect.equal result.Cycles [ [ recursive.Id ] ] "The genuine recursive component remains visible."

        "local forwarding specializations bind the outer definition",
        fun () ->
            let outer = feature FeatureKind.Type "custom-ident" [] None None

            let local =
                feature FeatureKind.Type "custom-ident" [ "animation-name" ] None (Some "<custom-ident>")

            let result = catalogue [ local; outer ] |> Resolution.resolve
            Expect.equal (targets local result) [ ReferenceTarget.Definition outer.Id ] "Forwarding specialization binds outer type"
            Expect.equal result.Cycles [] "Forwarding is not self-recursion"
            let recursive = catalogue [ local ] |> Resolution.resolve
            Expect.equal (targets local recursive) [ ReferenceTarget.Definition local.Id ] "Scoped recursion survives when no outer definition exists"

        "rejected grammar remains a definition target",
        fun () ->
            let source = feature FeatureKind.Type "source" [] None (Some "<target>")
            let target = feature FeatureKind.Type "target" [] None None

            let target =
                { target with
                    Grammar = GrammarStatus.Rejected ("[", { Offset = 1; Message = "incomplete" })
                }

            Expect.equal
                (catalogue [ source; target ]
                 |> Resolution.resolve
                 |> targets source)
                [ ReferenceTarget.Definition target.Id ]
                "Rejected target identity survives"
    ]

/// <summary>Reference binding, scope selection and dependency graph regression tests.</summary>
let tests =
    testList
        "CSS reference resolution"
        (cases
         |> List.map (fun (name, run) -> testCase name run))
