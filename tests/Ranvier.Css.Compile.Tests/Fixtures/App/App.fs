module ExtractionApp

open Ranvier.Css
open Ranvier.Css.View
open ExtractionLibrary
open ExtractionCompat

let card =
    utility {
        compat
        baseText
        stack 2
        Css.width (Css.percent 100.)

        yield!
            [
                Declaration.create "display" "-webkit-box"
                Declaration.create "display" "flex"
            ]

        md { hover { Css.paddingLeft (Css.rem 2.) } }
    }

let activeCard =
    utility {
        card

        whenActive (fun (state: bool) -> state) {
            Css.opacity (fun (_: bool) ->
                if true then
                    failwith "This reactive reader must never execute during extraction."
                else
                    Css.number 0.5)
        }
    }

let view () =
    styles { activeCard }

let unused = utility { Declaration.create "unused-never-emitted" "true" }
let unusedThrow: Utility = failwith "This unused declaration must never execute."
do System.IO.File.WriteAllText ("app-executed.sentinel", "must never run during CSS extraction")
