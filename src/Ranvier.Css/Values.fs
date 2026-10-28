namespace Ranvier.Css

open System
open System.Globalization
open System.Text.RegularExpressions

/// <summary>Opaque serialized CSS storage retaining its nominal grammar marker.</summary>
type ValueStorage<'syntax> = internal ValueStorage of text: string * numeric: (float * string) option

/// <summary>A serialized CSS value with a nominal grammar marker and optional numeric dimension.</summary>
type Value<'syntax>(storage: ValueStorage<'syntax>) =
    let (ValueStorage (text, numeric)) = storage

    /// <summary>The opaque content retained when a generated numeric wrapper inherits this value.</summary>
    member _.Storage = storage

    member internal _.Serialized = text
    member internal _.Numeric = numeric

    internal new(text: string, numeric: (float * string) option) = Value (ValueStorage (text, numeric))

    /// <summary>Copies a value while preserving its grammar marker and numeric token metadata.</summary>
    new(value: Value<'syntax>) = Value (value.Storage)

    override _.Equals other =
        match other with
        | :? Value<'syntax> as value -> (text, numeric) = (value.Serialized, value.Numeric)
        | _ -> false

    override _.GetHashCode() =
        hash (text, numeric)

    interface IComparable with
        member _.CompareTo other =
            match other with
            | :? Value<'syntax> as value -> compare (text, numeric) (value.Serialized, value.Numeric)
            | _ -> invalidArg "other" "Values have different CSS grammar markers."

/// <summary>A CSS declaration retaining its importance and exact value text.</summary>
type Declaration = private Declaration of name: string * text: string * important: bool

module private NumberText =
    let format (number: float) =
        if number = 0.0 then
            "0"
        else
#if FABLE_COMPILER
            let raw = string number
#else
            let raw = number.ToString ("R", CultureInfo.InvariantCulture)
#endif
            let negative = raw.StartsWith ("-", StringComparison.Ordinal)
            let raw = (if negative then raw.Substring (1) else raw).ToLowerInvariant()
            let parts = raw.Split ('e')
            let exponent = if parts.Length = 2 then int parts[1] else 0
            let mantissa = parts[0]
            let dot = mantissa.IndexOf '.'
            let original = mantissa.Replace (".", "")
            let leading = original.Length - original.TrimStart('0').Length
            let digits = original.TrimStart('0').TrimEnd('0')

            let point =
                (if dot < 0 then mantissa.Length else dot)
                + exponent
                - leading

            let power = point - 1

            let result =
                if power >= -6 && power < 21 then
                    if point <= 0 then
                        "0." + String.replicate (-point) "0" + digits
                    elif point >= digits.Length then
                        digits
                        + String.replicate (point - digits.Length) "0"
                    else
                        digits.Substring (0, point)
                        + "."
                        + digits.Substring (point)
                else
                    string digits[0]
                    + (if digits.Length = 1 then "" else "." + digits.Substring (1))
                    + "e"
                    + string power

            (if negative then "-" else "") + result

/// <summary>Factories used by generated grammar bindings; unchecked factories require specification validation by their caller.</summary>
module Generated =
    /// <summary>Returns the serialized value text.</summary>
    let text (value: Value<'syntax>) =
        value.Serialized

    /// <summary>Accepts already serialized text without validating a grammar.</summary>
    let value text : Value<'tag> =
        Value (text, None)

    /// <summary>Reclassifies a value along a generated grammar edge.</summary>
    let copy (value: Value<'syntax>) : Value<'tag> =
        Value (value.Serialized, value.Numeric)

    /// <summary>Constructs a finite numeric token with the supplied unit suffix.</summary>
    let number (number: float) unitName : Value<'tag> =
        if Double.IsNaN number || Double.IsInfinity number then
            invalidArg "number" "CSS numbers must be finite."

        Value (NumberText.format number + unitName, Some (number, unitName))

    /// <summary>Quotes a string and escapes CSS string delimiters and newlines.</summary>
    let stringValue (input: string) : Value<'tag> =
        if isNull input then
            nullArg "input"

        let escaped =
            input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\d ")
                .Replace("\n", "\\a ")
                .Replace("\u000C", "\\c ")
                .Replace("\u0000", "\\fffd ")

        value ("\"" + escaped + "\"")

    /// <summary>Validates an unescaped identifier subset, with optional dashed and custom-identifier constraints.</summary>
    let identifier dashed custom (input: string) : Value<'tag> =
        if String.IsNullOrEmpty input then
            invalidArg "input" "Expected a CSS identifier."

        if
            dashed
            && (not (input.StartsWith ("--", StringComparison.Ordinal))
                || input.Length < 3)
        then
            invalidArg "input" "Expected a dashed identifier."

        if
            not (
                Char.IsLetter input[0]
                || input[0] = '_'
                || input[0] = '-'
            )
            || not (
                input
                |> Seq.forall (fun c -> Char.IsLetterOrDigit c || c = '_' || c = '-')
            )
        then
            invalidArg "input" "Expected an unescaped CSS identifier."

        if
            input[0] = '-'
            && (input.Length = 1
                || (input[1] <> '-'
                    && input[1] <> '_'
                    && not (Char.IsLetter input[1])))
        then
            invalidArg "input" "Invalid identifier start."

        if
            custom
            && List.contains
                (input.ToLowerInvariant ())
                [
                    "initial"
                    "inherit"
                    "unset"
                    "revert"
                    "revert-layer"
                    "revert-rule"
                    "default"
                ]
        then
            invalidArg "input" "Reserved custom identifier."

        value input

    /// <summary>Joins nonempty grammar components with their source separators.</summary>
    let sequence (parts: (string * string) list) : Value<'tag> =
        let mutable result = ""

        for separator, part in parts do
            if part <> "" then
                result <-
                    result
                    + (if result = "" then "" else separator)
                    + part

        value result

    /// <summary>Checks occurrence bounds and joins repeated components.</summary>
    let repeat minimum maximum comma (values: Value<'item> list) : Value<'tag> =
        let count = List.length values

        if
            count < minimum
            || maximum
               |> Option.exists (fun bound -> count > bound)
        then
            invalidArg "values" "CSS repetition cardinality is outside its grammar bounds."

        value (
            values
            |> List.map text
            |> String.concat (if comma then ", " else " ")
        )

    /// <summary>Rejects an empty required production.</summary>
    let required (input: Value<'item>) : Value<'tag> =
        if String.IsNullOrWhiteSpace (text input) then
            invalidArg "input" "A required group must contain a value."

        copy input

    /// <summary>Accepts unchecked nonempty CSS tokens.</summary>
    let unsafeTokens (input: string) : Value<'tag> =
        if String.IsNullOrWhiteSpace input then
            invalidArg "input" "Expected CSS tokens."

        value input

    /// <summary>Checks numeric bounds, converting supported absolute units; computed values retain their text.</summary>
    let range minimum maximum (input: Value<'item>) : Value<'tag> =
        match input.Numeric with
        | Some (number, unitName) ->
            let factor (unit: string) =
                match unit.ToLowerInvariant () with
                | "deg" -> Some (1.0, "angle")
                | "rad" -> Some (180.0 / Math.PI, "angle")
                | "turn" -> Some (360.0, "angle")
                | "grad" -> Some (0.9, "angle")
                | "s" -> Some (1.0, "time")
                | "ms" -> Some (0.001, "time")
                | "px" -> Some (1.0, "length")
                | "in" -> Some (96.0, "length")
                | "cm" -> Some (96.0 / 2.54, "length")
                | "mm" -> Some (96.0 / 25.4, "length")
                | "q" -> Some (96.0 / 101.6, "length")
                | "pt" -> Some (96.0 / 72.0, "length")
                | "pc" -> Some (16.0, "length")
                | "hz" -> Some (1.0, "frequency")
                | "khz" -> Some (1000.0, "frequency")
                | "dppx"
                | "x" -> Some (1.0, "resolution")
                | "dpi" -> Some (1.0 / 96.0, "resolution")
                | "dpcm" -> Some (2.54 / 96.0, "resolution")
                | _ -> None

            let convert unit =
                if
                    unit = ""
                    || String.Equals (unit, unitName, StringComparison.OrdinalIgnoreCase)
                then
                    number
                else
                    match factor unitName, factor unit with
                    | Some (a, k), Some (b, j) when k = j -> number * a / b
                    | _ when number = 0.0 -> 0.0
                    | _ -> invalidArg "input" "Cannot compare CSS units before computed-value resolution."

            let outside lower =
                function
                | None -> false
                | Some (bound, unit) -> if lower then convert unit < bound else convert unit > bound

            if outside true minimum || outside false maximum then
                invalidArg "input" "CSS numeric range exceeded."
        | _ -> ()

        copy input

/// <summary>Rendering and explicit unchecked value construction.</summary>
module Value =
    /// <summary>Serializes a value without surrounding declaration syntax.</summary>
    let render value =
        Generated.text value

    /// <summary>Accepts unchecked CSS text with a caller-selected grammar marker.</summary>
    let unsafeFromTokens<'tag> tokens : Value<'tag> =
        Generated.unsafeTokens tokens

/// <summary>Ordered CSS declaration construction and rendering.</summary>
module Declaration =
    /// <summary>Creates a declaration from serialized tokens; value grammar validation belongs to generated factories.</summary>
    let create name text =
        if
            String.IsNullOrWhiteSpace name
            || not (Regex.IsMatch (name, @"^--[A-Za-z0-9_-]+$|^-?[A-Za-z_][A-Za-z0-9_-]*$"))
        then
            invalidArg "name" "Expected a CSS property name."

        if isNull text then
            nullArg "text"

        Declaration (name, text, false)

    /// <summary>Returns the CSS property name.</summary>
    let name (Declaration (name, _, _)) = name
    /// <summary>Returns the serialized value without importance.</summary>
    let value (Declaration (_, text, _)) = text
    /// <summary>Returns whether a declaration carries cascade importance.</summary>
    let isImportant (Declaration (_, _, important)) = important

    /// <summary>Recognizes a keyword from a supplied specification registry, including comments and identifier escapes.</summary>
    let isCssWideWith keywords (Declaration (_, text, _)) =
        let tokens = Regex.Replace (text, @"/\*[\s\S]*?\*/", " ")

        let decoded =
            Regex.Replace (
                tokens,
                @"\\([0-9a-fA-F]{1,6})(?:\r\n|[ \t\r\n\f])?|\\([^\r\n\f0-9a-fA-F])",
                MatchEvaluator (fun matched ->
                    if matched.Groups[1].Success then
                        let code = Convert.ToInt32 (matched.Groups[1].Value, 16)
                        if code > 0 && code < 128 then string (char code) else "?"
                    else
                        matched.Groups[2].Value)
            )

        List.contains (decoded.Trim().ToLowerInvariant()) keywords

    /// <summary>Recognizes the current CSS-wide keywords, including comments and identifier escapes.</summary>
    let isCssWide declaration =
        isCssWideWith [ "initial"; "inherit"; "unset"; "revert"; "revert-layer"; "revert-rule" ] declaration

    /// <summary>Marks a declaration important while preserving its position.</summary>
    let important (Declaration (name, text, _)) =
        Declaration (name, text, true)

    /// <summary>Serializes a declaration with its semicolon and importance.</summary>
    let render (Declaration (name, text, important)) =
        name
        + ": "
        + text
        + (if important then " !important" else "")
        + ";"

    /// <summary>Serializes declarations in source order, preserving duplicate property fallbacks.</summary>
    let renderMany declarations =
        declarations
        |> List.map render
        |> String.concat " "

/// <summary>A named CSS variable restricted to values of the same grammar marker.</summary>
type CustomProperty<'syntax> = private CustomProperty of string

/// <summary>Typed CSS custom-property references and declarations.</summary>
module CustomProperty =
    /// <summary>Creates a variable with a nonempty unescaped dashed identifier.</summary>
    let create name : CustomProperty<'syntax> =
        Generated.identifier true false name |> ignore
        CustomProperty name

    /// <summary>Returns the dashed variable name.</summary>
    let name (CustomProperty name) = name

    /// <summary>Constructs a variable reference preserving its grammar marker.</summary>
    let reference (variable: CustomProperty<'syntax>) : Value<'syntax> =
        Generated.value ("var(" + name variable + ")")

    /// <summary>Constructs a variable reference with a fallback of the same grammar marker.</summary>
    let referenceWithFallback (variable: CustomProperty<'syntax>) (fallback: Value<'syntax>) : Value<'syntax> =
        Generated.value (
            "var("
            + name variable
            + ", "
            + Value.render fallback
            + ")"
        )

    /// <summary>Creates a declaration assigning a value of the variable's grammar marker.</summary>
    let declaration (variable: CustomProperty<'syntax>) (value: Value<'syntax>) =
        Declaration.create (name variable) (Value.render value)

/// <summary>Unambiguous entry points used by generated shared-runtime bindings on .NET and Fable.</summary>
module RuntimeApi =
    /// <summary>Returns the magnitude and unit suffix of a numeric token; compound expressions and unchecked text return <c>None</c>.</summary>
    let tryNumericValue (value: Value<'syntax>) =
        value.Numeric

    /// <summary>Creates a typed custom-property handle from a validated dashed identifier.</summary>
    let createCustomProperty name : CustomProperty<'syntax> =
        CustomProperty.create name

    /// <summary>Returns the dashed name of a typed custom-property handle.</summary>
    let customPropertyName (variable: CustomProperty<'syntax>) =
        CustomProperty.name variable

    /// <summary>Creates a declaration from specification-generated property names and serialized values.</summary>
    let declaration name text =
        Declaration.create name text

    /// <summary>Serializes a typed grammar value.</summary>
    let renderValue value =
        Value.render value

    /// <summary>Serializes a CSS declaration.</summary>
    let renderDeclaration declaration =
        Declaration.render declaration

    /// <summary>Returns a declaration's value text without its property name or importance.</summary>
    let declarationValue (declaration: Declaration) =
        Declaration.value declaration

    /// <summary>Serializes ordered declarations.</summary>
    let renderDeclarations declarations =
        Declaration.renderMany declarations
