namespace Ranvier.Css.Spec

open System
open System.IO

/// <summary>Reads a local Webref snapshot and writes a generation model; usage failures return 64.</summary>
module Cli =
    let private usage =
        "CSS spec consumer: [--input css.json] [--output model.json] [--label label] [--specs-dir directory] [--bindings Css.fs] [--namespace name] [--runtime] [--strict] [--help]"

    /// <summary>Returns 0 on success, 1 on input/I/O failure, 2 for rejected grammar in strict mode, or 64 for invalid arguments.</summary>
    /// <remarks>Strict mode writes the complete diagnostic model before returning 2. Missing grammars remain valid catalogue entries.</remarks>
    let run (args: string array) =
        let mutable input =
            Path.Combine (__SOURCE_DIRECTORY__, "node_modules", "@webref", "css", "css.json")

        let mutable output = Path.Combine (__SOURCE_DIRECTORY__, "out", "css-model.json")
        let mutable label = "@webref/css@8.7.8"
        let mutable specsDirectory = None
        let mutable bindingsPath = None
        let mutable namespaceName = "Ranvier.Css.Generated"
        let mutable sharedRuntime = false
        let mutable customInput = false
        let mutable customLabel = false
        let mutable strict = false
        let mutable help = false
        let mutable argumentError = None
        let mutable i = 0

        while i < args.Length && argumentError.IsNone do
            match args[i] with
            | "--help" ->
                help <- true
                i <- i + 1
            | "--strict" ->
                strict <- true
                i <- i + 1
            | "--runtime" ->
                sharedRuntime <- true
                i <- i + 1
            | ("--input" | "--output" | "--label" | "--specs-dir" | "--bindings" | "--namespace") as option ->
                if
                    i + 1 >= args.Length
                    || args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    || String.IsNullOrWhiteSpace args[i + 1]
                then
                    argumentError <- Some ("Expected a value for " + option)
                else
                    match option with
                    | "--input" ->
                        input <- args[i + 1]
                        customInput <- true
                    | "--output" -> output <- args[i + 1]
                    | "--specs-dir" -> specsDirectory <- Some args[i + 1]
                    | "--bindings" -> bindingsPath <- Some args[i + 1]
                    | "--namespace" -> namespaceName <- args[i + 1]
                    | _ ->
                        label <- args[i + 1]
                        customLabel <- true

                    i <- i + 2
            | other -> argumentError <- Some ("Unknown argument: " + other)

        match argumentError with
        | Some message ->
            eprintfn "%s\n%s" message usage
            64
        | None when help ->
            printfn "%s" usage
            0
        | None ->
            try
                let inputPath, outputPath = Path.GetFullPath input, Path.GetFullPath output

                let comparison =
                    if OperatingSystem.IsWindows () then
                        StringComparison.OrdinalIgnoreCase
                    else
                        StringComparison.Ordinal

                if String.Equals (inputPath, outputPath, comparison) then
                    eprintfn "Input and output must be different files."
                    64
                else
                    let outputPaths =
                        [
                            yield outputPath
                            match bindingsPath with
                            | Some path ->
                                yield Path.GetFullPath path
                                yield Path.GetFullPath (Path.ChangeExtension (path, "manifest.json"))
                            | None -> ()
                        ]

                    let inputPaths =
                        [
                            yield inputPath
                            match specsDirectory with
                            | Some directory ->
                                yield!
                                    Directory.GetFiles (directory, "*.json")
                                    |> Array.map Path.GetFullPath
                            | None -> ()
                        ]

                    let same a b =
                        String.Equals (a, b, comparison)

                    if
                        outputPaths
                        |> List.mapi (fun i path ->
                            outputPaths
                            |> List.skip (i + 1)
                            |> List.exists (same path))
                        |> List.exists id
                    then
                        invalidArg "output" "Model, bindings, and manifest outputs must be different files."

                    if
                        outputPaths
                        |> List.exists (fun path -> inputPaths |> List.exists (same path))
                    then
                        invalidArg "output" "Outputs must not overwrite any specification input."

                    let effectiveLabel = if customInput && not customLabel then "custom" else label

                    let consumed =
                        match Pipeline.read effectiveLabel inputPath with
                        | Error errors -> Error errors
                        | Ok catalogue -> Pipeline.combine catalogue specsDirectory

                    match consumed with
                    | Error diagnostics ->
                        for diagnostic in diagnostics do
                            eprintfn "%s: %s" diagnostic.Path diagnostic.Message

                        1
                    | Ok catalogue ->
                        let generated =
                            match bindingsPath with
                            | None -> None
                            | Some path ->
                                if
                                    String.Equals (Path.GetFullPath path, inputPath, comparison)
                                    || String.Equals (Path.GetFullPath path, outputPath, comparison)
                                then
                                    invalidArg "bindings" "Bindings output must differ from input and model output."

                                let generate =
                                    if sharedRuntime then
                                        RuntimeGeneration.generate
                                    else
                                        Generation.generate

                                match generate namespaceName catalogue with
                                | Error errors -> invalidArg "bindings" (errors |> List.map _.Message |> String.concat "; ")
                                | Ok result -> Some (path, result)

                        let json = Snapshot.toJson catalogue

                        Pipeline.write outputPath json

                        let parsed, rejected, missing = Snapshot.coverage catalogue

                        match generated with
                        | Some (path, result) ->
                            Pipeline.write path result.Source
                            Pipeline.write (Path.ChangeExtension (path, "manifest.json")) result.Manifest

                            printfn
                                "Generated %d bindings; %d prose/rejected definitions; %d unresolved references."
                                result.FeatureCount
                                result.OpaqueCount
                                result.UnresolvedCount
                        | None -> ()

                        printfn "%d definitions: %d parsed, %d rejected, %d missing grammar." catalogue.Features.Length parsed rejected missing
                        printfn "SHA-256 %s\n%s" catalogue.Identity.Sha256 outputPath

                        for feature in catalogue.Features do
                            match feature.Grammar with
                            | GrammarStatus.Rejected (_, diagnostic) ->
                                eprintfn "%A %s (%s), offset %d: %s" feature.Id.Kind feature.Id.Name feature.Href diagnostic.Offset diagnostic.Message
                            | _ -> ()

                        if strict && rejected > 0 then 2 else 0
            with
            | :? IOException as error ->
                eprintfn "%s" error.Message
                1
            | :? UnauthorizedAccessException as error ->
                eprintfn "%s" error.Message
                1
            | :? ArgumentException as error ->
                eprintfn "%s" error.Message
                64

module private Program =
    [<EntryPoint>]
    let main args =
        Cli.run args
