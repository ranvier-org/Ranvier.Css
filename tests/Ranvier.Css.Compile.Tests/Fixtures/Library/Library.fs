module ExtractionLibrary

open Ranvier.Css

let spacing index =
    Css.rem (float index * 0.25)

let md = minViewportWidth (Css.rem 48.)

let baseText =
    utility {
        Css.fontSize (Css.rem 1.)
        md { Css.fontSize (Css.rem 1.125) }
    }

let stack count =
    utility {
        for index in [ 1..count ] do
            Css.marginLeft (spacing index)
    }

let unused: Utility = failwith "This library declaration is not used."
do System.IO.File.WriteAllText ("library-executed.sentinel", "must never run during CSS extraction")
