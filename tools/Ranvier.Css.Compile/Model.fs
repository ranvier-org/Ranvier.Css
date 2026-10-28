namespace Ranvier.Css.Compile

open FSharp.Compiler.Text
open Ranvier.Css.Build

/// <summary>A build failure located at the F# expression that could not be statically resolved.</summary>
type Diagnostic =
    {
        File: string
        Line: int
        Column: int
        Message: string
    }

/// <summary>Formats extraction diagnostics for terminals and editor build output.</summary>
module Diagnostic =
    /// <summary>Formats a source position and the deterministic extraction failure.</summary>
    let format diagnostic =
        $"{diagnostic.File}({diagnostic.Line},{diagnostic.Column}): error RVCSS001: {diagnostic.Message}"

    let internal at (position: range) message =
        {
            File = position.FileName
            Line = position.StartLine
            Column = position.StartColumn + 1
            Message = message
        }

/// <summary>Asset layout and bounded interpreter inputs for project extraction.</summary>
type ExtractionOptions =
    {
        Assets: Options
        Configuration: string
        Framework: string option
        Manifest: string option
        MaxExpressions: int
        MaxIterations: int
        MaxDepth: int
    }

/// <summary>Conservative bounds for deterministic compile-time style evaluation.</summary>
module ExtractionOptions =
    /// <summary>Uses Release project resolution and finite evaluation limits without sampling application state.</summary>
    let defaults =
        {
            Assets = Options.defaults
            Configuration = "Release"
            Framework = None
            Manifest = None
            MaxExpressions = 100000
            MaxIterations = 10000
            MaxDepth = 128
        }

exception internal ExtractionFailure of Diagnostic
