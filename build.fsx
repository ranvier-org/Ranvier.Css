#i "nuget: https://nuget.cloudsmith.io/shayanhabibi/shayanhabibi-partas-build/v3/index.json"
#r "nuget: Partas.Build, 0.8.2"
#r "nuget: Partas.Build.Baked, 0.3.0"
#r "nuget: Partas.Build.EasyBuild.ShipIt, 0.1.0"
#r "nuget: Fake.IO.FileSystem"

open System
open System.IO
open Partas.Build
open Fake.IO.Globbing.Operators

module Spec =
    let root = __SOURCE_DIRECTORY__
    let sources = [
        "src/Ranvier.Css/Ranvier.Css.fsproj"
        "src/Ranvier.Css.Bindings/Ranvier.Css.Bindings.fsproj"
        "src/Ranvier.Css.Build/Ranvier.Css.Build.fsproj"
        "src/Ranvier.Css.View/Ranvier.Css.View.fsproj"
    ]
    let tools = [ "tools/Ranvier.Css.Generate/Ranvier.Css.Generate.fsproj"; "tools/Ranvier.Css.Compile/Ranvier.Css.Compile.fsproj" ]
    let localTools =
        use manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".config/dotnet-tools.json")))
        manifest.RootElement.GetProperty("tools").EnumerateObject()
        |> Seq.map (fun tool -> tool.Name, tool.Value.GetRawText())
        |> Seq.toList
    let formatting =
        !! "**/*.fs"
        -- "**/obj/**"
        -- "**/bin/**"
        -- "**/out/**"
        -- "**/output/**"
        -- "**/node_modules/**"
        -- "**/fable_modules/**"
        -- "src/Ranvier.Css.Bindings/Generated/**"
        |> Seq.toList

module Options =
    let group =
        Input.option "--group"
        |> Input.mapFromAmong [ "all", "all"; "dotnet", "dotnet"; "bindings", "bindings"; "assets", "assets"; "browser", "browser" ]
        |> Input.def "all"
        |> Input.description "CSS checks to run (default: all)"
    let dryFormat = Input.option<bool> "--dry-format" |> Input.description "Check formatting without rewriting files"
    let dryPublish = Input.option<bool> "--dry-publish" |> Input.description "Build and pack without uploading packages"
    let browser = Input.option<bool> "--with-browser" |> Input.description "Install Chromium during developer setup"
    let ranvierRoot =
        Input.option<string> "--ranvier-root"
        |> Input.def (Path.GetFullPath(Path.Combine(Spec.root, "../Ranvier")))
        |> Input.description "Path to the sibling Ranvier repository"

module Stage =
    let restoreTools = stage "restore tools in isolated SDK processes" {
        for name, registration in Spec.localTools do
            stage $"restore {name}" {
                run (fun _ ->
                    let directory = Path.Combine(Spec.root, ".config/.tool-restore")
                    Directory.CreateDirectory directory |> ignore
                    let json = "{\"version\":1,\"isRoot\":true,\"tools\":{" + System.Text.Json.JsonSerializer.Serialize(name) + ":" + registration + "}}"
                    File.WriteAllText(Path.Combine(directory, name + ".json"), json))
                run (Cmd.ofList "dotnet" [ "tool"; "restore"; "--tool-manifest"; Path.Combine(Spec.root, ".config/.tool-restore", name + ".json"); "--verbosity"; "q" ])
            }
    }
    let restore = input {
        let! quick = Baked.Common.quick
        and! config = Baked.Dotnet.configOrRelease
        return stage "restore" {
            when' (not quick)
            restoreTools
            stage "restore solution" {
                run (cmd $"dotnet restore Ranvier.Css.slnx -p:Configuration={config}")
            }
        }
    }

    let npm = input {
        let! quick = Baked.Common.quick
        return stage "install pinned npm dependencies" {
            when' (not quick)
            run "npm ci --prefix tools/Ranvier.Css.Generate --ignore-scripts"
            run "npm ci --prefix tools/Ranvier.Css.Import --ignore-scripts"
        }
    }

    let buildProjects (projects: string list) = input {
        let! config = Baked.Dotnet.configOrRelease
        return stage "build" {
            for project in projects do
                stage $"build {project}" {
                    run (cmd $"dotnet build {project} -c {config} -m:1 --no-restore")
                }
        }
    }
    let build = buildProjects Spec.sources
    let tools = buildProjects Spec.tools |> InputSpec.map (fun stage -> { stage with Name = "build tools" })

    let formatWith check = Baked.Stages.fantomasWith (InputSpec.ret false) check Spec.formatting
    let format = input {
        let! check = Options.dryFormat
        return stage "format" {
            run (cmd $"dotnet fantomas" |> Cmd.argIf check [ "--check" ] |> Cmd.args Spec.formatting)
        }
    }

    let fetchSpecs = stage "verify pinned CSS specifications" {
        run "node tools/Ranvier.Css.Generate/fetch-specs.mjs"
    }
    let scripts = stage "test workflow and compiler rejection helpers" {
        run "node --test .github/scripts/contrib.test.cjs tools/Ranvier.Css.Generate/compiler-rejections.test.mjs"
    }
    let dotnet = stage "test specification model and source extraction" {
        run "dotnet test tests/Ranvier.Css.Spec.Tests -c Release --no-restore -m:1"
        run "dotnet build tests/Ranvier.Css.Compile.Tests/Fixtures/App/App.fsproj -c Release -m:1"
        run "dotnet test tests/Ranvier.Css.Compile.Tests -c Release --no-restore -m:1"
    }
    let bindings = stage "verify generated bindings and type safety" {
        run "node tools/Ranvier.Css.Generate/verify-bindings.mjs"
    }
    let assets = stage "verify CSS assets, utilities, documentation and imports" {
        run "node tools/Ranvier.Css.Generate/verify-assets.mjs"
        run "dotnet build examples/Ranvier.Css.Utilities.Example -c Release -m:1 --no-restore"
        run "dotnet run --project tools/Ranvier.Css.Compile -c Release --no-build -- --project examples/Ranvier.Css.Utilities.Example/Ranvier.Css.Utilities.Example.fsproj --output examples/Ranvier.Css.Utilities.Example/out --single app.css --no-content-hash --public-path ."
        run "dotnet run --project examples/Ranvier.Css.Utilities.Example -c Release --no-build"
        run "node tools/Ranvier.Css.Generate/verify-utilities.mjs"
        run "node docs/samples/css/verify.mjs"
        run "node --test tools/Ranvier.Css.Import/import.test.mjs"
        run "node tools/Ranvier.Css.Import/verify-import.mjs"
    }
    let installBrowser = input {
        let! quick = Baked.Common.quick
        return stage "install Chromium" {
            when' (not quick)
            run "npm exec --prefix tools/Ranvier.Css.Generate -- playwright install --with-deps chromium"
        }
    }
    let compile = input {
        let! clean = Baked.Stages.cleanWith (InputSpec.ret false) [] [ "src/**/obj/**/*.nuspec" ]
        return stage "compile browser checks with fickl" {
            clean
            stage "fickl" {
                run "dotnet fickl tests/Ranvier.Css.Browser.Tests/Ranvier.Css.Browser.Tests.fsproj -o tools/Ranvier.Css.Generate/out/browser -c Release --noCache"
            }
        }
    }
    let browser = input {
        let! compile = compile
        return stage "verify DOM and native Chromium behavior" {
            compile
            stage "jsdom" { run "node tools/Ranvier.Css.Generate/verify-browser.mjs" }
            stage "Chromium" { run "node tools/Ranvier.Css.Generate/verify-chromium.mjs" }
        }
    }

    let testsWith (group: InputSpec<string>) = input {
        let! group = group
        and! skip = Baked.Common.skipTests
        and! installBrowser = installBrowser
        and! browser = browser
        return stage "CSS checks" {
            when' (not skip)
            scripts
            stage "specification inputs" {
                when' (group = "all" || group = "dotnet" || group = "bindings")
                fetchSpecs
            }
            stage "dotnet" {
                when' (group = "all" || group = "dotnet")
                dotnet
            }
            stage "bindings" {
                when' (group = "all" || group = "bindings")
                bindings
            }
            stage "assets" {
                when' (group = "all" || group = "assets")
                assets
            }
            stage "browser" {
                when' (group = "all" || group = "browser")
                installBrowser
                browser
            }
        }
    }
    let tests = testsWith (InputSpec.ofInput Options.group)
    let allTests = testsWith (InputSpec.ret "all")
    let pack = input {
        let! clean = Baked.Stages.cleanWith (InputSpec.ret false) [] [ "bin/*.nupkg"; "bin/*.snupkg" ]
        and! packages = Baked.Stages.pack "bin" Spec.sources
        return stage "pack CSS libraries" {
            clean
            packages
        }
    }

    let publish = input {
        let! key = Baked.NuGet.apiKey.option
        and! dry = Options.dryPublish
        return stage "publish" {
            when' (not dry)
            stage "push (requires NUGET_API_KEY or --nuget-key)" {
                failIfIgnored
                when' key.IsSome
                run (cmd $"dotnet nuget push bin/*.nupkg --source https://api.nuget.org/v3/index.json --skip-duplicate"
                     |> Cmd.secretOption "--api-key" (Option.defaultValue "" key))
            }
        }
    }

    let local = input {
        let! root = Options.ranvierRoot
        let root = Path.GetFullPath(root, Spec.root)
        return stage "configure sibling Ranvier checkout" {
            stage "clone if missing" {
                when' (not (Directory.Exists root))
                run (cmd $"git clone https://github.com/shayanhabibi/Ranvier.git {root}")
            }
            stage "write local configuration" {
                run (fun _ ->
                    if not (File.Exists(Path.Combine(root, "src/Ranvier.View/Ranvier.View.fsproj"))) then
                        invalidArg "ranvier-root" "The path is not a Ranvier checkout."
                    let escaped = System.Security.SecurityElement.Escape root
                    File.WriteAllText(Path.Combine(Spec.root, "Local.Build.props"), $"<Project><PropertyGroup><UseLocalRanvier>true</UseLocalRanvier><RanvierRoot>{escaped}</RanvierRoot></PropertyGroup></Project>"))
            }
        }
    }
    let packages = stage "configure published package dependencies" {
        run (fun _ -> File.WriteAllText(Path.Combine(Spec.root, "Local.Build.props"), "<Project><PropertyGroup><UseLocalRanvier>false</UseLocalRanvier></PropertyGroup></Project>"))
    }
    let optionalBrowser = input {
        let! enabled = Options.browser
        and! install = installBrowser
        return stage "optional Chromium setup" {
            when' enabled
            install
        }
    }
    let docs = input {
        let! watch = Baked.Common.watch
        return stage "documentation" {
            run (cmd $"dotnet run --project docs/docs.fsproj --" |> Cmd.arg (if watch then "watch" else "build"))
        }
    }

exit <| rootCommand fsi.CommandLineArgs[1..] {
    description "Ranvier.Css development, validation and releases"
    workingDir Spec.root
    command "tools" {
        description "Restore pinned tools individually to avoid SDK tool-metadata collisions on fresh runners"
        Command.pipeline { Stage.restoreTools }
    }
    command "dev" {
        description "Set up dependencies and switch between published and sibling Ranvier references"
        command "setup" {
            description "Restore .NET tools, projects and pinned npm inputs (--with-browser to install Chromium)"
            Command.pipeline { Stage.restore; Stage.npm; Stage.optionalBrowser }
        }
        command "local" {
            description "Clone Ranvier if missing and configure local project references"
            Command.pipeline { Stage.local; Stage.restore }
        }
        command "packages" {
            description "Switch back to pinned published Ranvier dependencies"
            Command.pipeline { Stage.packages; Stage.restore }
        }
    }
    command "build" {
        description "Build the CSS libraries and tools in dependency order"
        Command.pipeline { Stage.restore; Stage.build; Stage.tools }
    }
    command "test" {
        alias "tests"
        description "Run CSS checks; --group selects a CI shard (default: all)"
        Command.pipeline { Stage.restore; Stage.npm; Stage.build; Stage.tools; Stage.tests }
    }
    command "ci" {
        description "Run the complete CI gates locally and pack the validated libraries"
        Command.pipeline { Stage.restore; Stage.npm; Stage.formatWith true; Stage.build; Stage.tools; Stage.allTests; Stage.pack }
    }
    command "compile" {
        description "Compile CSS browser checks to JavaScript with fickl"
        Command.pipeline { Stage.restore; Stage.compile }
    }
    command "format" {
        description "Format source files (--dry-format to check); generated bindings are excluded"
        Command.pipeline { Stage.restore; Stage.format }
    }
    command "pack" {
        description "Build and pack all four CSS libraries with Fable sources"
        Command.pipeline { Stage.restore; Stage.build; Stage.pack }
    }
    command "publish" {
        description "Validate all CSS gates and publish (--dry-publish to skip upload)"
        Command.pipeline { Stage.restore; Stage.npm; Stage.build; Stage.tools; Stage.allTests; Stage.pack; Stage.publish }
    }
    command "docs" {
        description "Build documentation (--watch to serve it)"
        Command.pipeline { Stage.restore; Stage.docs }
    }
    command "bump" {
        description "Calculate release versions with ShipIt in local mode"
        Command.pipeline { Partas.Build.EasyBuild.ShipIt.Stages.bump }
    }
    addCommand Partas.Build.EasyBuild.ShipIt.Commands.shipit
}
