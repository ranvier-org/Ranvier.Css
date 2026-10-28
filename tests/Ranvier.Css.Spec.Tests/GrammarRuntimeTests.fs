module Ranvier.Css.Spec.GrammarRuntimeTests

open System
open Expecto
open Ranvier.Css.Internal.GrammarRuntime

let private node id kind children =
    {
        Id = id
        Kind = kind
        Text = ""
        Target = -1
        Children = children
        Separators = [||]
        Minimum = 0
        Maximum = -1
        CommaSeparated = false
        Lower = None
        Upper = None
    }

let private fixture () =
    [|
        { node 0 0 [||] with Text = "length" }
        { node 1 0 [||] with Text = "angle" }
        { node 2 1 [||] with Text = "auto" }
        { node 3 2 [||] with
            Target = 0
            Lower = Some (0., "px")
            Upper = Some (100., "px")
        }
        node 4 3 [| 2; 3 |]
        { node 5 7 [| 2 |] with
            Minimum = 0
            Maximum = 1
        }
        { node 6 4 [| 5; 3 |] with
            Separators = [| ""; " " |]
        }
        node 7 4 [| 2; 3 |]
        node 8 6 [| 2; 3 |]
        { node 9 7 [| 0 |] with
            Minimum = 1
            Maximum = 2
            CommaSeparated = true
        }
        { node 10 9 [| 3 |] with
            Text = "fit-content"
        }
        { node 11 10 [| 3 |] with Text = "[" }
        node 12 8 [| 5 |]
        { node 13 2 [||] with Target = 14 }
        { node 14 2 [||] with Target = 13 }
        node 15 11 [| 2 |]
        node 16 12 [| 2 |]
        { node 17 2 [||] with
            Target = 0
            Lower = Some (0., "px")
            Upper = Some (5., "px")
        }
        { node 18 2 [||] with
            Target = 0
            Lower = Some (6., "px")
            Upper = Some (100., "px")
        }
        node 19 3 [| 17; 18 |]
        { node 20 9 [||] with Text = "paint" }
        node 21 5 [| 5; 3 |]
        { node 22 1 [||] with Text = "" }
        node 23 6 [| 22 |]
        { node 24 2 [||] with Target = 25 }
        node 25 3 [| 24; 5 |]
        node 26 4 [| 24; 3 |]
        { node 27 0 [||] with
            Text = "number-token"
        }
        { node 28 2 [||] with
            Target = 27
            Text = "number-token"
        }
        { node 29 2 [||] with
            Target = 27
            Text = "number-token"
        }
        { node 30 0 [||] with
            Text = "percentage"
        }
        node 31 3 [| 0; 30 |]
        node 32 3 [| 0; 30 |]
        { node 33 2 [||] with
            Target = 30
            Lower = Some (0., "%")
            Upper = Some (100., "%")
        }
        node 34 3 [| 3; 33 |]
    |]

let private rejects action =
    Expect.throwsT<ArgumentException> action "Invalid construction must retain its runtime guard."

let private field (text: string) =
    string text.Length + ":" + text

let private encode (nodes: Node array) =
    let bound =
        function
        | None -> "", ""
        | Some (magnitude: float, unitName) -> magnitude.ToString ("R", Globalization.CultureInfo.InvariantCulture), unitName

    let fields =
        nodes
        |> Array.collect (fun value ->
            let lower, lowerUnit = bound value.Lower
            let upper, upperUnit = bound value.Upper

            [|
                string value.Id
                string value.Kind
                value.Text
                string value.Target
                value.Children
                |> Array.map string
                |> String.concat ","
                value.Separators
                |> Array.map field
                |> String.concat ""
                string value.Minimum
                string value.Maximum
                (if value.CommaSeparated then "1" else "0")
                lower
                lowerUnit
                upper
                upperUnit
                ""
            |])

    Array.append [| string nodes.Length |] fields
    |> Array.map field
    |> String.concat ""

/// <summary>Checked grammar construction, semantic admission, numeric provenance and portable table decoding.</summary>
let tests =
    testList
        "CSS grammar runtime"
        [
            testCase "ranged choices try later branches and retain range diagnostics"
            <| fun _ ->
                let table = Table.create (fixture ())

                let px magnitude =
                    number table 0 magnitude "px"

                Expect.equal (admit table 19 (px 42.)).Text "42px" "The second bounded branch remains eligible."
                Expect.equal (admit table 3 (number table 0 1. "in")).Text "1in" "Absolute units use the shared conversion policy."

                for value in [ px -1.; px 101.; number table 0 2. "in"; number table 0 1. "em" ] do
                    rejects (fun () -> admit table 3 value |> ignore)

                let error =
                    try
                        admit table 19 (px 101.) |> ignore
                        failtest "A value outside both ranges must fail."
                    with :? ArgumentException as error ->
                        error

                Expect.stringContains error.Message "range" "Extraction diagnostics must identify the failed range."

            testCase "numeric aliases preserve primitive identity and raw token metadata"
            <| fun _ ->
                let table = Table.create (fixture ())
                let literal = number table 28 1. ""
                Expect.equal literal.NodeId 27 "A reference constructor records its actual numeric terminal."
                Expect.equal (admit table 29 literal).Numeric (Some (1., "")) "Equivalent aliases retain numeric metadata."
                let percentage = number table 31 50. "%"
                Expect.equal percentage.NodeId 30 "A union selects the matching primitive terminal."
                Expect.equal (admit table 34 percentage).Text "50%" "The percentage range is checked against its own unit."

                for magnitude, unitName in [ 101., "%"; 101., "px" ] do
                    rejects (fun () ->
                        admit table 34 (number table 31 magnitude unitName)
                        |> ignore)

                rejects (fun () -> number table 0 Double.NaN "px" |> ignore)

            testCase "computed aliases share equivalent languages without dimension widening"
            <| fun _ ->
                let table = Table.create (fixture ())
                let integer = opaque table 28 "calc(1 + 2)"
                Expect.equal (admit table 29 integer).Text integer.Text "Independent aliases admit the same computed language."
                let mixed = opaque table 31 "calc(100% - 24px)"
                Expect.equal (admit table 32 mixed).NodeId 31 "Admission retains the original computed source identity."
                Expect.equal (admit table 34 mixed).Numeric None "Computed bounds are deferred rather than parsed from text."
                rejects (fun () -> admit table 0 mixed |> ignore)

                rejects (fun () ->
                    admit table 0 (opaque table 1 "calc(2px)")
                    |> ignore)

                rejects (fun () -> admit table 0 (number table 1 2. "deg") |> ignore)

            testCase "admission never manufactures required siblings or function wrappers"
            <| fun _ ->
                let table = Table.create (fixture ())
                let length = number table 0 42. "px"
                Expect.equal (admit table 6 length).Text "42px" "Nullable sequence siblings may be omitted."
                Expect.equal (admit table 21 length).Text "42px" "Nullable all-of siblings may be omitted."
                Expect.equal (admit table 8 length).Text "42px" "An any-of may select one member."
                rejects (fun () -> admit table 7 length |> ignore)
                rejects (fun () -> admit table 10 length |> ignore)

                rejects (fun () ->
                    admit table 10 (opaque table 31 "calc(1px)")
                    |> ignore)

                let wrapped = construct table 10 [| Some length |]
                Expect.equal wrapped.Text "fit-content(42px)" "Only explicit construction wraps a function."
                Expect.equal (admit table 10 wrapped) wrapped "An explicitly constructed function admits itself."
                Expect.equal (construct table 20 [||]).Text "paint()" "Zero-argument functions remain constructible."
                Expect.equal (construct table 11 [| Some length |]).Text "[42px]" "Blocks retain their delimiters."

            testCase "sequences validate arity and every supplied component"
            <| fun _ ->
                let table = Table.create (fixture ())
                let length = number table 0 42. "px"
                Expect.equal (construct table 6 [| Some (token "auto"); Some length |]).Text "auto 42px" "Separators are retained."
                Expect.equal (construct table 6 [| None; Some length |]).Text "42px" "Nullable slots may be omitted."
                rejects (fun () -> construct table 6 [| Some length |] |> ignore)

                rejects (fun () ->
                    construct table 7 [| None; Some length |]
                    |> ignore)

                rejects (fun () ->
                    construct table 6 [| Some (token "bogus"); Some length |]
                    |> ignore)

            testCase "repeat cardinality and separators remain checked"
            <| fun _ ->
                let table = Table.create (fixture ())

                let length magnitude =
                    Some (number table 0 magnitude "px")

                Expect.equal (construct table 9 [| length 1.; length 2. |]).Text "1px, 2px" "Comma repetition retains order."
                Expect.equal (admit table 9 (number table 0 1. "px")).Text "1px" "A permitted singleton is admitted."

                for inputs in [ [||]; [| None |]; [| length 1.; length 2.; length 3. |] ] do
                    rejects (fun () -> construct table 9 inputs |> ignore)

            testCase "required and unordered groups reject empty results"
            <| fun _ ->
                let table = Table.create (fixture ())
                let empty = construct table 5 [||]
                Expect.equal (construct table 12 [| Some (token "auto") |]).Text "auto" "A nonempty required group succeeds."
                rejects (fun () -> construct table 12 [| Some empty |] |> ignore)
                rejects (fun () -> admit table 12 empty |> ignore)
                rejects (fun () -> construct table 8 [| None; None |] |> ignore)
                let blank = construct table 22 [||]
                rejects (fun () -> construct table 23 [| Some blank |] |> ignore)
                rejects (fun () -> admit table 23 blank |> ignore)

            testCase "cycles terminate while nullable exits remain usable"
            <| fun _ ->
                let table = Table.create (fixture ())
                let length = number table 0 42. "px"
                rejects (fun () -> admit table 13 length |> ignore)

                rejects (fun () ->
                    admit table 0 (opaque table 13 "calc(1px)")
                    |> ignore)

                Expect.equal (admit table 26 length).Text "42px" "Cycle detection must retain a productive nullable exit."

            testCase "boolean templates validate operations and opaque templates stay explicit"
            <| fun _ ->
                let table = Table.create (fixture ())
                let keyword = token "auto"
                Expect.equal (boolean table 15 "not" [| keyword |]).Text "not (auto)" "Boolean parentheses remain stable."
                Expect.equal (boolean table 15 "and" [| keyword; keyword |]).Text "(auto) and (auto)" "Boolean members retain order."
                rejects (fun () -> boolean table 15 "and" [||] |> ignore)
                rejects (fun () -> boolean table 15 "xor" [| keyword |] |> ignore)
                rejects (fun () -> construct table 16 [| Some keyword |] |> ignore)

            testCase "table decoding preserves bounds and rejects malformed structure"
            <| fun _ ->
                let table = Table.decode (encode (fixture ()))
                let length = number table 0 42. "px"
                Expect.equal (construct table 10 [| Some length |]).Text "fit-content(42px)" "Decoded children and text retain structure."
                rejects (fun () -> admit table 3 (number table 0 -1. "px") |> ignore)

                for encoded in [ "3:22"; field "1" ] do
                    rejects (fun () -> Table.decode encoded |> ignore)

                rejects (fun () -> Table.create [| node 1 4 [| 22 |] |] |> ignore)

                rejects (fun () ->
                    Table.create [| node 0 0 [||]; node 0 0 [||] |]
                    |> ignore)

            testCase "table creation copies mutable input arrays"
            <| fun _ ->
                let source = fixture ()
                let table = Table.create source
                source[6].Children[ 1 ] <- 1
                source[6].Separators[ 1 ] <- "/"
                source[0] <- node 0 1 [||]
                let length = number table 0 42. "px"

                Expect.equal
                    (construct table 6 [| Some (token "auto"); Some length |]).Text
                    "auto 42px"
                    "Caller mutations cannot change checked grammar."
        ]
