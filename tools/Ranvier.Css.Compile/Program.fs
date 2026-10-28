module Ranvier.Css.Compile.Program

open System
open Ranvier.Css.Build
open Ranvier.Css.Compile

let private usage =
    """Ranvier.Css.Compile --project app.fsproj --output public/assets [options]
  --single filename.css  Emit one stylesheet
  --modules              Emit one stylesheet per source module (default)
  --css-modules          Emit CSS Modules with stable global identities
  --manifest path        Match the generated CSS sugar metadata
  --configuration name   MSBuild configuration (default Release)
  --framework name       Select a target framework for multi-targeted projects
  --public-path path     Asset URL prefix (default /assets/)
  --no-content-hash      Keep stable filenames without content hashes
  --help                 Show usage
"""

[<EntryPoint>]
/// <summary>Resolves a checked F# project and emits validated static assets, returning a nonzero status for diagnostics.</summary>
let main arguments =
    try
        let mutable project = None
        let mutable output = None
        let mutable options = ExtractionOptions.defaults
        let mutable help = false
        let mutable index = 0

        let value () =
            index <- index + 1

            if index >= arguments.Length then
                invalidArg "arguments" "A command option is missing its value."

            arguments[index]

        while index < arguments.Length do
            match arguments[index] with
            | "--project" -> project <- Some (value ())
            | "--output" -> output <- Some (value ())
            | "--manifest" ->
                options <-
                    { options with
                        Manifest = Some (value ())
                    }
            | "--configuration" ->
                options <-
                    { options with
                        Configuration = value ()
                    }
            | "--framework" ->
                options <-
                    { options with
                        Framework = Some (value ())
                    }
            | "--single" ->
                options <-
                    { options with
                        Assets =
                            { options.Assets with
                                Mode = OutputMode.SingleFile (value ())
                            }
                    }
            | "--modules" ->
                options <-
                    { options with
                        Assets =
                            { options.Assets with
                                Mode = OutputMode.PerModule
                            }
                    }
            | "--css-modules" ->
                options <-
                    { options with
                        Assets =
                            { options.Assets with
                                Mode = OutputMode.CssModules
                            }
                    }
            | "--public-path" ->
                options <-
                    { options with
                        Assets =
                            { options.Assets with
                                PublicPath = value ()
                            }
                    }
            | "--no-content-hash" ->
                options <-
                    { options with
                        Assets =
                            { options.Assets with
                                ContentHash = false
                            }
                    }
            | "--help"
            | "-h" -> help <- true
            | option -> invalidArg "arguments" ("Unknown option: " + option)

            index <- index + 1

        if help then
            printf "%s" usage
            0
        else
            match project, output with
            | Some project, Some output ->
                match Extractor.extract options project with
                | Ok compilation ->
                    Compiler.write output compilation
                    printfn "Emitted %d CSS assets from %d source modules." compilation.Assets.Length compilation.OrderedModules.Length
                    0
                | Error errors ->
                    errors
                    |> List.iter (Diagnostic.format >> eprintfn "%s")

                    1
            | _ ->
                eprintf "%s" usage
                2
    with error ->
        eprintfn "%s" error.Message
        1
