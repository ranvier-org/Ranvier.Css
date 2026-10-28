namespace Ranvier.Css.Build

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Collections.Generic
open Ranvier.Css

/// <summary>An explicit static style resource and the resources that must precede it.</summary>
type CssModule =
    {
        Name: string
        Dependencies: CssModule list
        Rules: Rule list
        Styles: StyleDefinition list
    }

/// <summary>CSS asset layouts supported by static extraction.</summary>
[<RequireQualifiedAccess>]
type OutputMode =
    | SingleFile of fileName: string
    | PerModule
    | CssModules

/// <summary>Asset layout, public URL prefix and content-hashed filename selection.</summary>
type Options =
    {
        Mode: OutputMode
        PublicPath: string
        ContentHash: bool
    }

/// <summary>Default deterministic extraction options.</summary>
module Options =
    /// <summary>Emits content-hashed per-module stylesheets beneath /assets/.</summary>
    let defaults =
        {
            Mode = OutputMode.PerModule
            PublicPath = "/assets/"
            ContentHash = true
        }

/// <summary>A complete stylesheet asset ready to be written without application code execution.</summary>
type Asset =
    {
        FileName: string
        PublicUrl: string
        Content: string
        Modules: string list
        Classes: Map<string, string>
    }

/// <summary>Ordered CSS assets and a deterministic dependency/class manifest.</summary>
type Compilation =
    {
        Assets: Asset list
        OrderedModules: string list
        Manifest: string
    }

/// <summary>Static resource graph compilation and atomic artifact writing.</summary>
module Compiler =
    let private validName (name: string) =
        not (String.IsNullOrWhiteSpace name)
        && Regex.IsMatch (name, @"^[A-Za-z0-9_-][A-Za-z0-9_.-]*$")
        && name <> "."
        && name <> ".."

    let private moduleRules (input: CssModule) =
        let adjacent (styles: StyleDefinition list) =
            let mutable previous = None

            [
                for style in styles do
                    if previous <> Some style then
                        yield! style.Rules

                    previous <- Some style
            ]

        input.Rules @ adjacent input.Styles

    let private isNamespace =
        function
        | StatementRule (name, _) when name.Equals ("namespace", StringComparison.OrdinalIgnoreCase) -> true
        | _ -> false

    let private commentOnly (text: string) =
        let text = text.Trim ()

        text.StartsWith ("/*", StringComparison.Ordinal)
        && text.IndexOf ("*/", StringComparison.Ordinal) = text.Length - 2

    let rec private hasOpaque =
        function
        | RawRule text -> not (commentOnly text)
        | GroupRule (_, _, children)
        | StyleRule (_, _, children) -> List.exists hasOpaque children
        | _ -> false

    let private validatePreamble (rules: Rule list) =
        let errors = ResizeArray<string>()
        let mutable importsClosed = false
        let mutable namespacesClosed = false
        let mutable count = 0

        let rec nested rules =
            for rule in rules do
                match rule with
                | StatementRule (name, _) when List.contains (name.ToLowerInvariant ()) [ "charset"; "import"; "namespace" ] ->
                    errors.Add ("@" + name + " must be at the stylesheet root.")
                | GroupRule (_, _, children)
                | StyleRule (_, _, children) -> nested children
                | _ -> ()

        for rule in rules do
            match rule with
            | RawRule text when commentOnly text -> ()
            | RawRule _ -> ()
            | StatementRule (name, prelude) when name.Equals ("charset", StringComparison.OrdinalIgnoreCase) ->
                if
                    count <> 0
                    || not (prelude.Equals ("\"UTF-8\"", StringComparison.OrdinalIgnoreCase))
                then
                    errors.Add "Only a leading UTF-8 @charset can be emitted."
            | StatementRule (name, _) when name.Equals ("import", StringComparison.OrdinalIgnoreCase) ->
                if importsClosed then
                    errors.Add "A late @import cannot be lifted without changing cascade order; use per-module output."
            | StatementRule (name, _) when name.Equals ("namespace", StringComparison.OrdinalIgnoreCase) ->
                importsClosed <- true

                if namespacesClosed then
                    errors.Add "A late @namespace cannot be lifted; use per-module output."
            | StatementRule (name, _) when name.Equals ("layer", StringComparison.OrdinalIgnoreCase) -> ()
            | GroupRule (_, _, children)
            | StyleRule (_, _, children) ->
                importsClosed <- true
                namespacesClosed <- true
                nested children
            | _ ->
                importsClosed <- true
                namespacesClosed <- true

            match rule with
            | RawRule text when commentOnly text -> ()
            | _ -> count <- count + 1

        List.ofSeq errors

    let private signature (input: CssModule) =
        (input.Dependencies
         |> List.map _.Name
         |> String.concat "\n")
        + "\n---\n"
        + (input.Styles
           |> List.map _.ClassName
           |> String.concat "\n")
        + "\n---\n"
        + Stylesheet.render (moduleRules input)

    let private cssModuleRules rules =
        let rec rewrite frames =
            function
            | StyleRule (selector, declarations, children) ->
                let selector =
                    if frames then
                        selector
                    else
                        Selector.unsafeFromText (":global(" + Selector.render selector + ")")

                StyleRule (selector, declarations, List.map (rewrite frames) children)
            | GroupRule (name, prelude, children) ->
                GroupRule (name, prelude, List.map (rewrite (name.EndsWith ("keyframes", StringComparison.Ordinal))) children)
            | other -> other

        rules |> List.map (rewrite false)

    /// <summary>Compiles the transitive dependency graph; cycles, unsafe filenames and conflicting resource names return diagnostics.</summary>
    let compile (options: Options) (roots: CssModule list) : Result<Compilation, string list> =
        let errors = ResizeArray<string>()
        let visited = Dictionary<string, string>(StringComparer.Ordinal)
        let active = HashSet<string>(StringComparer.Ordinal)
        let ordered = ResizeArray<CssModule>()

        let rec visit trail (input: CssModule) =
            if not (validName input.Name) then
                errors.Add ("Invalid module name: " + input.Name)
            elif active.Contains input.Name then
                errors.Add (
                    "Style dependency cycle: "
                    + String.concat " -> " (trail @ [ input.Name ])
                )
            else
                let stamp = signature input

                match visited.TryGetValue input.Name with
                | true, old when old <> stamp -> errors.Add ("Conflicting CSS module identity: " + input.Name)
                | true, _ ->
                    active.Add input.Name |> ignore

                    for dependency in input.Dependencies do
                        visit (trail @ [ input.Name ]) dependency

                    active.Remove input.Name |> ignore
                | _ ->
                    active.Add input.Name |> ignore

                    for dependency in input.Dependencies do
                        visit (trail @ [ input.Name ]) dependency

                    active.Remove input.Name |> ignore
                    visited[input.Name] <- stamp
                    ordered.Add input

        for root in roots do
            visit [] root

        let environments =
            ordered
            |> Seq.filter (fun input -> not (List.isEmpty (moduleRules input)))
            |> Seq.map (fun input -> moduleRules input |> List.filter isNamespace)
            |> Seq.distinct
            |> Seq.toList

        let opaque =
            ordered
            |> Seq.exists (fun input -> moduleRules input |> List.exists hasOpaque)

        if opaque then
            match options.Mode with
            | OutputMode.CssModules -> errors.Add "Opaque CSS rules cannot be safely rewritten as CSS Modules; use regular per-module output."
            | OutputMode.SingleFile _ when ordered.Count > 1 ->
                errors.Add "Opaque CSS rules cannot be lifted across module boundaries; use per-module output."
            | _ -> ()

        match options.Mode with
        | OutputMode.SingleFile _ ->
            if environments.Length > 1 then
                errors.Add "Different namespace environments cannot share a lifted stylesheet; use per-module output."

            ordered
            |> Seq.collect moduleRules
            |> Seq.toList
            |> validatePreamble
            |> List.iter errors.Add
        | _ ->
            for input in ordered do
                moduleRules input
                |> validatePreamble
                |> List.iter errors.Add

        match options.Mode with
        | OutputMode.SingleFile name when
            not (validName name)
            || not (name.EndsWith (".css", StringComparison.OrdinalIgnoreCase))
            ->
            errors.Add "Single-file output must be a CSS filename without directory components."
        | _ -> ()

        if
            isNull options.PublicPath
            || options.PublicPath.IndexOfAny ([| '\r'; '\n'; '\u0000' |])
               >= 0
        then
            errors.Add "Invalid public asset URL prefix."

        if errors.Count > 0 then
            Error (List.ofSeq errors)
        else
            let classContents = Dictionary<string, string>(StringComparer.Ordinal)

            for input in ordered do
                for style in input.Styles do
                    if not (validName style.ClassName) then
                        errors.Add ("Invalid generated class name: " + style.ClassName)

                    let css = Stylesheet.render style.Rules

                    match classContents.TryGetValue style.ClassName with
                    | true, existing when existing <> css ->
                        errors.Add (
                            "Conflicting generated style identity: "
                            + style.ClassName
                        )
                    | _ -> classContents[style.ClassName] <- css

            if errors.Count > 0 then
                Error (List.ofSeq errors)
            else
                let url (name: string) =
                    options.PublicPath.TrimEnd ('/')
                    + "/"
                    + Uri.EscapeDataString name

                let make (fileName: string) (inputs: CssModule list) cssModules =
                    let classes =
                        inputs
                        |> List.collect (fun input ->
                            input.Styles
                            |> List.map (fun style -> style.ClassName, style.ClassName))
                        |> Map.ofList

                    let rules = inputs |> List.collect moduleRules

                    let preamble, body =
                        if cssModules then
                            rules
                            |> List.takeWhile (function
                                | StatementRule _ -> true
                                | RawRule text when commentOnly text -> true
                                | _ -> false),
                            rules
                            |> List.skipWhile (function
                                | StatementRule _ -> true
                                | RawRule text when commentOnly text -> true
                                | _ -> false)
                        else
                            [], rules

                    let css = Stylesheet.render (if cssModules then cssModuleRules body else body)

                    let exports =
                        if cssModules && not classes.IsEmpty then
                            ""
                            + (classes
                               |> Map.toList
                               |> List.map (fun (key, value) ->
                                   "."
                                   + key
                                   + " { composes: "
                                   + value
                                   + " from global; }")
                               |> String.concat " ")
                            + "\n"
                        else
                            ""

                    let content = Stylesheet.render preamble + exports + css

                    let extension = if cssModules then ".module.css" else ".css"
                    let stem = fileName.Substring (0, fileName.Length - extension.Length)

                    let fileName =
                        stem
                        + (if options.ContentHash then
                               "." + Identity.hash content
                           else
                               "")
                        + extension

                    {
                        FileName = fileName
                        PublicUrl = url fileName
                        Content = content
                        Modules = inputs |> List.map _.Name
                        Classes = classes
                    }

                let assets =
                    match options.Mode with
                    | OutputMode.SingleFile fileName when ordered.Count > 0 -> [ make fileName (List.ofSeq ordered) false ]
                    | OutputMode.SingleFile _ -> []
                    | OutputMode.PerModule ->
                        ordered
                        |> Seq.map (fun input -> make (input.Name + ".css") [ input ] false)
                        |> Seq.toList
                    | OutputMode.CssModules ->
                        ordered
                        |> Seq.map (fun input -> make (input.Name + ".module.css") [ input ] true)
                        |> Seq.toList

                let assetManifest =
                    assets
                    |> List.map (fun asset ->
                        {|
                            fileName = asset.FileName
                            url = asset.PublicUrl
                            modules = asset.Modules
                            classes = asset.Classes
                        |})

                let payload =
                    {|
                        schemaVersion = 1
                        orderedModules = (ordered |> Seq.map _.Name |> Seq.toArray)
                        assets = assetManifest
                    |}

                let manifest =
                    JsonSerializer.Serialize(payload, JsonSerializerOptions (WriteIndented = true)).Replace("\r\n", "\n")
                    + "\n"

                let filenames = HashSet<string>(StringComparer.OrdinalIgnoreCase)

                let collisions =
                    assets
                    |> List.filter (fun asset -> not (filenames.Add asset.FileName))

                if not collisions.IsEmpty then
                    Error [ "CSS asset filenames collide on a case-insensitive filesystem." ]
                else
                    Ok
                        {
                            Assets = assets
                            OrderedModules = ordered |> Seq.map _.Name |> Seq.toList
                            Manifest = manifest
                        }

    /// <summary>Writes every asset and css-manifest.json atomically per file beneath an explicit directory.</summary>
    /// <remarks>Unrelated files and assets from previous compilations remain untouched.</remarks>
    let write outputDirectory (compilation: Compilation) =
        let output = Path.GetFullPath outputDirectory

        let files =
            (compilation.Assets
             |> List.map (fun asset -> asset.FileName, asset.Content))
            @ [ "css-manifest.json", compilation.Manifest ]

        for name, _ in files do
            if not (validName name) then
                invalidArg "compilation" "Asset filenames must stay within the output directory."

        if
            (HashSet<string>(files |> List.map fst, StringComparer.OrdinalIgnoreCase)).Count
            <> files.Length
        then
            invalidArg "compilation" "Asset output paths must be distinct."

        Directory.CreateDirectory output |> ignore

        for name, content in files do
            let target = Path.Combine (output, name)

            let temporary =
                target
                + "."
                + Guid.NewGuid().ToString("N")
                + ".tmp"

            try
                File.WriteAllText (temporary, content, UTF8Encoding (false))
                File.Move (temporary, target, true)
            finally
                if File.Exists temporary then
                    File.Delete temporary
