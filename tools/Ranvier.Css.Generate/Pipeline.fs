namespace Ranvier.Css.Spec

open System
open System.IO
open System.Text

/// <summary>Loads local input, reconciles complete spec corpora, and writes generated artifacts atomically per file.</summary>
module Pipeline =
    /// <summary>Reads a bounded file and selects the consolidated or individual-spec schema from its root metadata.</summary>
    let read (label: string) (path: string) =
        use input = File.OpenRead path
        let length = input.Length

        if length > int64 Webref.MaximumInputBytes then
            Error
                [
                    {
                        Path = path
                        Message = "Input exceeds 32 MiB."
                    }
                ]
        else
            let bytes = Array.zeroCreate<byte>(int length)
            input.ReadExactly bytes

            if input.ReadByte () <> -1 then
                Error
                    [
                        {
                            Path = path
                            Message = "Input changed while being read."
                        }
                    ]
            else
                try
                    use document = System.Text.Json.JsonDocument.Parse (ReadOnlyMemory<byte> bytes)

                    match document.RootElement.ValueKind, document.RootElement.TryGetProperty "spec" with
                    | System.Text.Json.JsonValueKind.Object, (true, _) -> SpecSources.consume label bytes
                    | _ -> Webref.consume label bytes
                with
                | :? System.Text.Json.JsonException as error -> Error [ { Path = path; Message = error.Message } ]
                | :? InvalidOperationException ->
                    Error
                        [
                            {
                                Path = path
                                Message = "Expected an input object."
                            }
                        ]

    /// <summary>Combines canonical input with every JSON file in a spec directory, including obsolete archived definitions.</summary>
    let combine (canonical: Catalogue) (directory: string option) =
        match directory with
        | None -> Ok canonical
        | Some directory ->
            let files =
                Directory.GetFiles (directory, "*.json")
                |> Array.sortWith (fun a b -> StringComparer.Ordinal.Compare (a, b))

            if files.Length = 0 then
                Error
                    [
                        {
                            Path = directory
                            Message = "Spec directory contains no JSON extracts."
                        }
                    ]
            else
                let inputs =
                    files
                    |> Array.map (fun path -> read (Path.GetFileName path) path)

                let errors =
                    inputs
                    |> Array.collect (function
                        | Error errors -> List.toArray errors
                        | _ -> [||])

                if errors.Length > 0 then
                    Error (List.ofArray errors)
                else
                    let catalogues =
                        inputs
                        |> Array.choose (function
                            | Ok c -> Some c
                            | _ -> None)
                        |> List.ofArray

                    match SpecSources.consolidateWith canonical catalogues with
                    | Error errors -> Error errors
                    | Ok combined ->
                        Ok
                            {
                                Identity = combined.Current.Identity
                                Features =
                                    (combined.Current.Features
                                     @ combined.Obsolete.Features)
                                    |> List.sortBy _.Id
                            }

    /// <summary>Replaces an output through a sibling temporary file; UTF-8 has no BOM and line endings stay unchanged.</summary>
    let write (path: string) (contents: string) =
        let fullPath = Path.GetFullPath path

        Directory.CreateDirectory (Path.GetDirectoryName fullPath)
        |> ignore

        let temporary =
            fullPath
            + "."
            + Guid.NewGuid().ToString("N")
            + ".tmp"

        try
            File.WriteAllText (temporary, contents, UTF8Encoding (false))
            File.Move (temporary, fullPath, true)
        finally
            if File.Exists temporary then
                File.Delete temporary
