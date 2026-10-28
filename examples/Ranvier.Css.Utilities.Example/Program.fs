module Ranvier.Css.Utilities.Example.Program

open System
open System.IO
open System.Text.Json
open Ranvier.View

[<EntryPoint>]
let main args =
    let output =
        if args.Length = 0 then
            Path.Combine (__SOURCE_DIRECTORY__, "out", "index.html")
        else
            args[0]

    let manifest =
        Path.Combine (Path.GetDirectoryName (Path.GetFullPath output), "css-manifest.json")

    let urls =
        if File.Exists manifest then
            use document = JsonDocument.Parse (File.ReadAllText manifest)

            document.RootElement.GetProperty("assets").EnumerateArray()
            |> Seq.map (fun asset -> asset.GetProperty("url").GetString())
            |> Seq.toList
        else
            [ "app.css" ]

    let shell =
        { Server.DocumentShell.defaults with
            Stylesheets = urls
        }

    let html =
        Server.renderDocument Server.Options.defaults shell Styles.``component`` 240.

    Directory.CreateDirectory (Path.GetDirectoryName (Path.GetFullPath output))
    |> ignore

    File.WriteAllText (output, html)
    printfn "%s" output
    0
