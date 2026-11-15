namespace Ranvier.Css.Compile

open System
open System.IO
open System.Text.RegularExpressions
open Ranvier.Css.Build

/// <summary>Discovers typed styles applications and interprets their dependencies without executing application assemblies.</summary>
module Extractor =
    /// <summary>Finds the manifest packed with a referenced Ranvier.Css.Bindings package, preferring the root project's reference.</summary>
    /// <remarks>The package holds the assembly at <c>lib/framework/</c> and the manifest at <c>metadata/Css.manifest.json</c>.</remarks>
    let private packagedManifest (projectPath: string) (projects: Project.CheckedProject list) =
        let root = Path.GetFullPath projectPath

        projects
        |> List.sortBy (fun project -> not (String.Equals (project.ProjectPath, root, StringComparison.OrdinalIgnoreCase)))
        |> Seq.collect _.References
        |> Seq.filter (fun reference -> String.Equals (Path.GetFileName reference, "Ranvier.Css.Bindings.dll", StringComparison.OrdinalIgnoreCase))
        |> Seq.choose (fun reference ->
            let framework = Directory.GetParent reference

            if
                isNull framework
                || isNull framework.Parent
                || isNull framework.Parent.Parent
            then
                None
            else
                Some (Path.Combine (framework.Parent.Parent.FullName, "metadata", "Css.manifest.json")))
        |> Seq.tryFind File.Exists

    let private manifest (options: ExtractionOptions) (projectPath: string) (projects: Project.CheckedProject list) =
        match options.Manifest with
        | Some path -> Path.GetFullPath path
        | None ->
            let rec search (directory: DirectoryInfo) =
                if isNull directory then
                    failwith
                        "Cannot locate generated CSS metadata. Reference the Ranvier.Css.Bindings package, or pass --manifest with the bindings' Css.manifest.json."

                let candidate =
                    Path.Combine (directory.FullName, "src", "Ranvier.Css.Bindings", "Generated", "Css.manifest.json")

                if File.Exists candidate then
                    candidate
                else
                    search directory.Parent

            match packagedManifest projectPath projects with
            | Some path -> path
            | None -> search (DirectoryInfo (Path.GetDirectoryName (Path.GetFullPath projectPath)))

    /// <summary>Compiles every discovered styles application and all supported source dependencies to static CSS assets.</summary>
    /// <remarks>Requires restored project references. Unsupported used computations return source diagnostics before any output is written.</remarks>
    let extract (options: ExtractionOptions) (projectPath: string) : Result<Compilation, Diagnostic list> =
        try
            if
                options.MaxDepth <= 0
                || options.MaxExpressions <= 0
                || options.MaxIterations <= 0
            then
                invalidArg "options" "Extraction bounds must be positive."

            let projects = Project.load options projectPath
            let metadata = Metadata.load (manifest options projectPath projects)
            let definitions = Evaluation.definitions projects
            let interpreter = Evaluation.Interpreter (options, metadata, definitions)

            let modules =
                definitions.Values
                |> Seq.sortBy (fun definition -> definition.Body.Range.FileName, definition.Body.Range.StartLine, definition.Body.Range.StartColumn)
                |> Seq.collect (fun definition ->
                    interpreter.Discover (definition.Body, definition.Parameters)
                    |> List.map (fun style -> definition.Body.Range.FileName, style))
                |> Seq.groupBy fst
                |> Seq.map (fun (file, styles) ->
                    let project =
                        projects
                        |> List.find (fun project ->
                            project.Contents
                            |> List.exists (fun contents -> String.Equals (contents.FileName, file, StringComparison.OrdinalIgnoreCase)))

                    let identity =
                        Path.GetFileNameWithoutExtension (project.ProjectPath)
                        + "/"
                        + Path.GetRelativePath(Path.GetDirectoryName project.ProjectPath, file).Replace('\\', '/')

                    let name =
                        Regex.Replace (Path.GetFileNameWithoutExtension file, "[^A-Za-z0-9_.-]", "_")
                        + "-"
                        + Ranvier.Css.Identity.hash identity

                    {
                        Name = name
                        Dependencies = []
                        Rules = []
                        Styles =
                            styles
                            |> Seq.map snd
                            |> Seq.distinctBy _.ClassName
                            |> Seq.toList
                    })
                |> Seq.toList

            match Compiler.compile options.Assets modules with
            | Ok compilation -> Ok compilation
            | Error errors ->
                Error (
                    errors
                    |> List.map (fun message ->
                        {
                            File = projectPath
                            Line = 1
                            Column = 1
                            Message = message
                        })
                )
        with
        | ExtractionFailure diagnostic -> Error [ diagnostic ]
        | error ->
            Error
                [
                    {
                        File = projectPath
                        Line = 1
                        Column = 1
                        Message = error.Message
                    }
                ]
