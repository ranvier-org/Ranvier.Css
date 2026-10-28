namespace Ranvier.Css

open System
open System.Text

/// <summary>A CSS selector represented by serialized selector syntax.</summary>
type Selector = private Selector of string

/// <summary>Selector construction with explicit unchecked syntax access.</summary>
module Selector =
    /// <summary>Accepts caller-validated selector syntax; stylesheet delimiters are rejected.</summary>
    let unsafeFromText (text: string) =
        if
            String.IsNullOrWhiteSpace text
            || text.IndexOfAny ([| '{'; '}'; ';' |]) >= 0
        then
            invalidArg "text" "Expected selector syntax without a stylesheet delimiter."

        Selector text

    /// <summary>Selects an unescaped class identifier.</summary>
    let className name =
        Generated.identifier false false name |> ignore
        Selector ("." + name)

    /// <summary>Selects an unescaped element identifier.</summary>
    let element name =
        Generated.identifier false false name |> ignore
        Selector name

    /// <summary>Selects an unescaped ID identifier.</summary>
    let id name =
        Generated.identifier false false name |> ignore
        Selector ("#" + name)

    /// <summary>Returns serialized selector syntax.</summary>
    let render (Selector text) = text

    /// <summary>Constructs a descendant selector from two selectors.</summary>
    let descendant parent child =
        Selector (render parent + " " + render child)

/// <summary>An ordered stylesheet tree preserving declaration, nesting and at-rule order.</summary>
type Rule =
    | StyleRule of Selector * Declaration list * Rule list
    | GroupRule of name: string * prelude: string * Rule list
    | DescriptorRule of name: string * prelude: string * Declaration list
    | StatementRule of name: string * prelude: string
    | RawRule of text: string

/// <summary>Stylesheet construction with explicit generic at-rule support.</summary>
module Rule =
    /// <summary>Preserves unchecked complete CSS rules or comments that the structured tree cannot represent.</summary>
    /// <remarks>The caller validates syntax and ordering with a CSS parser. Opaque rules cannot be rewritten as CSS Modules.</remarks>
    let unsafeFromText text =
        if String.IsNullOrWhiteSpace text then
            invalidArg "text" "Expected a complete CSS rule or comment."

        RawRule text

    let private prelude (text: string) =
        if
            isNull text
            || text.IndexOfAny ([| '{'; '}'; ';' |]) >= 0
        then
            invalidArg "prelude" "Expected an at-rule prelude without stylesheet delimiters."

        text

    let private atName name =
        Generated.identifier false false name |> ignore
        name

    /// <summary>Creates a style rule retaining repeated declarations and their order.</summary>
    let style selector declarations =
        StyleRule (selector, declarations, [])

    /// <summary>Creates a style rule with explicit nested rules.</summary>
    let nested selector declarations children =
        StyleRule (selector, declarations, children)

    /// <summary>Creates a generic block at-rule containing ordered rules.</summary>
    let group name condition rules =
        GroupRule (atName name, prelude condition, rules)

    /// <summary>Creates a media conditional block.</summary>
    let media condition rules =
        group "media" condition rules

    /// <summary>Creates a feature-support conditional block.</summary>
    let supports condition rules =
        group "supports" condition rules

    /// <summary>Creates a container conditional block.</summary>
    let container condition rules =
        group "container" condition rules

    /// <summary>Creates an optionally named cascade layer block.</summary>
    let layer name rules =
        group "layer" (defaultArg name "") rules

    /// <summary>Creates keyframes with caller-supplied frame selectors and ordered declarations.</summary>
    let keyframes name frames =
        group
            "keyframes"
            (atName name)
            (frames
             |> List.map (fun (selector, declarations) -> style (Selector.unsafeFromText selector) declarations))

    /// <summary>Creates a generic descriptor block such as font-face, page, or property registration.</summary>
    let descriptors name head declarations =
        DescriptorRule (atName name, prelude head, declarations)

    /// <summary>Creates a statement at-rule such as import, namespace, or layer ordering.</summary>
    let statement name head =
        StatementRule (atName name, prelude head)

    /// <summary>Registers a custom property using a restricted registration syntax and an explicit initial value.</summary>
    /// <remarks>The caller ensures the initial value is computationally independent and matches the registration syntax.</remarks>
    let register (variable: CustomProperty<'syntax>) syntax inherits (initial: Value<'syntax>) =
        if
            not (
                List.contains
                    syntax
                    [
                        "*"
                        "<number>"
                        "<integer>"
                        "<length>"
                        "<length-percentage>"
                        "<percentage>"
                        "<color>"
                        "<angle>"
                        "<time>"
                        "<resolution>"
                        "<custom-ident>"
                        "<transform-function>"
                        "<transform-list>"
                        "<image>"
                        "<url>"
                    ]
            )
        then
            invalidArg "syntax" "Unsupported custom-property registration syntax."

        descriptors
            "property"
            (CustomProperty.name variable)
            [
                Declaration.create "syntax" (Generated.stringValue syntax |> Value.render)
                Declaration.create "inherits" (if inherits then "true" else "false")
                Declaration.create "initial-value" (Value.render initial)
            ]

/// <summary>Deterministic stylesheet serialization preserving source order.</summary>
module Stylesheet =
    /// <summary>Serializes a stylesheet tree with LF line endings.</summary>
    let render rules =
        let output = StringBuilder ()

        let rec write indent rule =
            let pad = String.replicate indent "  "

            let line text =
                output.Append(pad).Append(text: string).Append('\n')
                |> ignore

            let block header declarations children =
                if List.isEmpty children then
                    line (
                        header
                        + " { "
                        + Declaration.renderMany declarations
                        + " }"
                    )
                else
                    line (header + " {")

                    if not (List.isEmpty declarations) then
                        output.Append(pad).Append("  ").Append(Declaration.renderMany declarations).Append('\n')
                        |> ignore

                    for child in children do
                        write (indent + 1) child

                    line "}"

            let head name prelude =
                "@"
                + name
                + (if prelude = "" then "" else " " + prelude)

            match rule with
            | StyleRule (selector, declarations, children) -> block (Selector.render selector) declarations children
            | GroupRule (name, prelude, children) ->
                line (head name prelude + " {")

                for child in children do
                    write (indent + 1) child

                line "}"
            | DescriptorRule (name, prelude, declarations) -> block (head name prelude) declarations []
            | StatementRule (name, prelude) -> line (head name prelude + ";")
            | RawRule text -> line text

        for rule in rules do
            write 0 rule

        output.ToString ()
