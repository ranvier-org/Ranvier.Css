module ExtractionCompat

open Ranvier.Css

let compat = utility { Declaration.create "box-sizing" "border-box" }
