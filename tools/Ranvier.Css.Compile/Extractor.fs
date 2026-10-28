namespace Ranvier.Css.Compile

open System
open System.IO
open System.Text.RegularExpressions
open Ranvier.Css.Build

/// <summary>Discovers typed styles applications and interprets their dependencies without executing application assemblies.</summary>
module Extractor =
    let private manifest (options: ExtractionOptions) (projectPath: string) =
        match options.Manifest with
        | Some path -> Path.GetFullPath path
        | None ->
            let rec search (directory: DirectoryInfo) =
                if isNull directory then
                    failwith "Cannot locate generated CSS metadata. Pass --manifest with the bindings' Css.manifest.json."

                let candidate =
                    Path.Combine (directory.FullName, "src", "Ranvier.Css.Bindings", "Generated", "Css.manifest.json")

                if File.Exists candidate then
                    candidate
                else
                    search directory.Parent

            search (DirectoryInfo (Path.GetDirectoryName (Path.GetFullPath projectPath)))

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

            let metadata = Metadata.load (manifest options projectPath)
            let projects = Project.load options projectPath
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
