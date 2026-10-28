namespace Ranvier.Css.Compile

open System
open System.IO
open System.Diagnostics
open System.Collections.Generic
open System.Text.Json
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Symbols

module internal Project =
    type CheckedProject =
        {
            ProjectPath: string
            Contents: FSharpImplementationFileContents list
        }

    let private evaluated (options: ExtractionOptions) (projectPath: string) =
        let start = ProcessStartInfo ("dotnet")
        start.WorkingDirectory <- Path.GetDirectoryName projectPath
        start.UseShellExecute <- false
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        start.CreateNoWindow <- true
        start.Environment["MSBUILDDISABLENODEREUSE"] <- "1"
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] <- "1"

        [
            "msbuild"
            projectPath
            "-nologo"
            "-nr:false"
            "-v:q"
            "-t:Compile"
            "-p:ProvideCommandLineArgs=true"
            "-p:SkipCompilerExecution=true"
            "-p:BuildProjectReferences=false"
            "-p:NonExistentFile=__ranvier_css_extraction_missing__"
            "-p:Configuration=" + options.Configuration
            "-getItem:FscCommandLineArgs,_MSBuildProjectReferenceExistent"
            "-getProperty:TargetPath,TargetFramework,TargetFrameworks"
        ]
        |> List.iter start.ArgumentList.Add

        options.Framework
        |> Option.iter (fun framework -> start.ArgumentList.Add ("-p:TargetFramework=" + framework))

        use child = Process.Start start
        let output = child.StandardOutput.ReadToEndAsync ()
        let errors = child.StandardError.ReadToEndAsync ()

        if not (child.WaitForExit 120000) then
            child.Kill (true)
            failwith "MSBuild project resolution exceeded two minutes."

        if not (System.Threading.Tasks.Task.WhenAll(output, errors).Wait 10000) then
            failwith "MSBuild output streams did not close after project resolution. Disable custom persistent child processes in build targets."

        let output = output.GetAwaiter().GetResult()
        let errors = errors.GetAwaiter().GetResult()

        if child.ExitCode <> 0 then
            failwith (
                "Project resolution failed. Restore and build the project first.\n"
                + output
                + errors
            )

        let first = output.IndexOf '{'

        if first < 0 then
            failwith (
                "MSBuild did not return compiler arguments: "
                + output
            )

        use document = JsonDocument.Parse (output.Substring first)
        let root = document.RootElement
        let properties = root.GetProperty "Properties"
        let items = root.GetProperty "Items"
        let target = properties.GetProperty("TargetPath").GetString()

        let arguments =
            items.GetProperty("FscCommandLineArgs").EnumerateArray()
            |> Seq.map (fun item -> item.GetProperty("Identity").GetString())
            |> Seq.toArray

        if arguments.Length = 0 then
            failwith "No F# compiler arguments were produced. For a multi-targeted project, specify --framework."

        let references =
            items.GetProperty("_MSBuildProjectReferenceExistent").EnumerateArray()
            |> Seq.map (fun item ->
                let framework =
                    match item.TryGetProperty "NearestTargetFramework" with
                    | true, value when not (String.IsNullOrEmpty (value.GetString ())) -> Some (value.GetString ())
                    | _ -> None

                item.GetProperty("FullPath").GetString(), framework)
            |> Seq.filter (fun (path, _) -> path.EndsWith (".fsproj", StringComparison.OrdinalIgnoreCase))
            |> Seq.toList

        target, arguments, references, properties.GetProperty("TargetFramework").GetString()

    let private coreProject (path: string) =
        List.contains
            (Path.GetFileNameWithoutExtension path)
            [
                "Ranvier.Css"
                "Ranvier.Css.Build"
                "Ranvier.Css.View"
                "Ranvier.Css.Bindings"
                "Ranvier"
                "Ranvier.View"
                "Ranvier.Views"
            ]

    let load options projectPath =
        let checker = FSharpChecker.Create (keepAssemblyContents = true)

        let cache =
            Dictionary<string, string * FSharpProjectOptions * CheckedProject>(StringComparer.OrdinalIgnoreCase)

        let active = HashSet<string>(StringComparer.OrdinalIgnoreCase)

        let selectedFrameworks =
            Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

        let rec visit framework projectPath =
            let path = Path.GetFullPath projectPath

            match cache.TryGetValue path with
            | true, loaded ->
                if
                    framework
                    |> Option.exists (fun requested -> requested <> selectedFrameworks[path])
                then
                    failwith (
                        "Source project is referenced with multiple target frameworks: "
                        + path
                    )

                loaded
            | _ ->
                if not (active.Add path) then
                    failwith ("Cyclic project reference: " + path)

                let target, arguments, references, selectedFramework =
                    evaluated { options with Framework = framework } path

                selectedFrameworks[path] <- selectedFramework
                let directory = Path.GetDirectoryName path

                let normalize (argument: string) =
                    if
                        not (argument.StartsWith "-")
                        && (argument.EndsWith ".fs"
                            || argument.EndsWith ".fsi")
                    then
                        Path.GetFullPath (argument, directory)
                    elif argument.StartsWith "-r:" then
                        "-r:"
                        + Path.GetFullPath (argument.Substring(3).Trim('"'), directory)
                    else
                        argument

                let sourceReferences =
                    references
                    |> List.filter (fst >> coreProject >> not)
                    |> List.map (fun (path, selectedFramework) -> visit selectedFramework path)

                let projectOptions =
                    checker.GetProjectOptionsFromCommandLineArgs (path, Array.map normalize arguments)

                let projectOptions =
                    { projectOptions with
                        ReferencedProjects =
                            sourceReferences
                            |> List.map (fun (target, referenced, _) -> FSharpReferencedProject.FSharpReference (target, referenced))
                            |> List.toArray
                    }

                let results =
                    checker.ParseAndCheckProject projectOptions
                    |> Async.RunSynchronously

                let errors =
                    results.Diagnostics
                    |> Array.filter (fun item -> item.Severity = FSharpDiagnosticSeverity.Error)

                if errors.Length > 0 then
                    errors
                    |> Array.map (fun item -> Diagnostic.at item.Range item.Message)
                    |> Array.toList
                    |> List.head
                    |> ExtractionFailure
                    |> raise

                let loaded =
                    target,
                    projectOptions,
                    {
                        ProjectPath = path
                        Contents = results.AssemblyContents.ImplementationFiles
                    }

                cache[path] <- loaded
                active.Remove path |> ignore
                loaded

        visit options.Framework projectPath |> ignore

        cache.Values
        |> Seq.map (fun (_, _, checkedProject) -> checkedProject)
        |> Seq.sortBy _.ProjectPath
        |> Seq.toList
