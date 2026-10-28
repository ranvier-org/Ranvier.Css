module Ranvier.Css.Example.Program

open System
open System.IO
open Ranvier.Css.Build
open Ranvier.Css.View
open Ranvier.View

[<EntryPoint>]
let main args =
    try
        let mutable output = Path.Combine (__SOURCE_DIRECTORY__, "out")
        let mutable mode = OutputMode.PerModule
        let mutable publicPath = "/assets/"
        let mutable contentHash = true
        let mutable index = 0

        while index < args.Length do
            match args[index] with
            | "--single" ->
                mode <- OutputMode.SingleFile "app.css"
                index <- index + 1
            | "--modules" ->
                mode <- OutputMode.PerModule
                index <- index + 1
            | "--css-modules" ->
                mode <- OutputMode.CssModules
                index <- index + 1
            | "--no-hash" ->
                contentHash <- false
                index <- index + 1
            | ("--output" | "--public-path") as option when index + 1 < args.Length ->
                if option = "--output" then
                    output <- args[index + 1]
                else
                    publicPath <- args[index + 1]

                index <- index + 2
            | other -> invalidArg "args" ("Unknown option or missing value: " + other)

        match
            Compiler.compile
                {
                    Mode = mode
                    PublicPath = publicPath
                    ContentHash = contentHash
                }
                Styles.resources
        with
        | Error errors -> failwith (String.concat "\n" errors)
        | Ok compilation ->
            Compiler.write output compilation

            let shell =
                CssView.withStylesheets (compilation.Assets |> List.map _.PublicUrl) Server.DocumentShell.defaults

            let html = Server.renderDocument Server.Options.defaults shell Styles.component 42.0
            File.WriteAllText (Path.Combine (output, "index.html"), html)
            printfn "%s" output
            0
    with error ->
        eprintfn "%s" error.Message
        1
