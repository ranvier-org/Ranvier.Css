module Ranvier.Css.Spec.RuntimeTests

open Expecto
open Ranvier.Css

/// <summary>Portable stylesheet, component identity, and reactive custom-property contracts.</summary>
let tests =
    testList
        "CSS compiler runtime"
        [
            testCase "numeric token metadata is retained through grammar copies"
            <| fun _ ->
                let literal: Value<unit> = Generated.number 12.5 "px"
                Expect.equal (RuntimeApi.tryNumericValue literal) (Some (12.5, "px")) "Literal"
                Expect.equal (RuntimeApi.tryNumericValue (Generated.copy literal: Value<obj>)) (Some (12.5, "px")) "Copy"

                for text in [ "12px"; "calc(2px + 3px)"; "var(--gap)"; "auto" ] do
                    let value: Value<unit> = Value.unsafeFromTokens text
                    Expect.isNone (RuntimeApi.tryNumericValue value) "Text has no numeric-token provenance"
            testCase "hex colors accept every specified digit count and preserve digit case"
            <| fun _ ->
                for input, expected in
                    [
                        "#fff", "#fff"
                        "fff", "#fff"
                        "#FFFA", "#FFFA"
                        "#663399", "#663399"
                        "aBcDeF", "#aBcDeF"
                        "#ff000080", "#ff000080"
                    ] do
                    let value: Value<unit> = Generated.hexColor input
                    Expect.equal (Value.render value) expected "A valid hex color is serialized with one number sign."

                for input in
                    [
                        ""
                        "#"
                        "#ff"
                        "#fffff"
                        "#fffffff"
                        "#fffffffff"
                        "#ggg"
                        "##fff"
                        "#ff 00 00"
                        "#-ff"
                    ] do
                    Expect.throwsT<System.ArgumentException>
                        (fun () -> (Generated.hexColor input: Value<unit>) |> ignore)
                        "Hex colors outside the CSS digit counts or alphabet are rejected."

                Expect.throwsT<System.ArgumentNullException> (fun () -> (Generated.hexColor null: Value<unit>) |> ignore) "Null input is rejected."
            testCase "hash tokens accept identifier code point names and preserve case"
            <| fun _ ->
                for input, expected in
                    [
                        "#main", "#main"
                        "Nav-1", "#Nav-1"
                        "_x", "#_x"
                        "#1st", "#1st"
                        "été", "#été"
                    ] do
                    let value: Value<unit> = Generated.hashToken input
                    Expect.equal (Value.render value) expected "A valid hash name is serialized with one number sign."

                for input in [ ""; "#"; "##a"; "a b"; "a.b"; "a(b)"; "a\\b" ] do
                    Expect.throwsT<System.ArgumentException>
                        (fun () -> (Generated.hashToken input: Value<unit>) |> ignore)
                        "Hash names outside the identifier code points are rejected."

                Expect.throwsT<System.ArgumentNullException> (fun () -> (Generated.hashToken null: Value<unit>) |> ignore) "Null input is rejected."
            testCase "url tokens escape delimiters and whitespace inside an unquoted url()"
            <| fun _ ->
                for input, expected in
                    [
                        "dot.png", "url(dot.png)"
                        "https://example.test/a?b=c#d", "url(https://example.test/a?b=c#d)"
                        "a b(1).png", "url(a\\ b\\(1\\).png)"
                        "it's\"q\"\\", "url(it\\'s\\\"q\\\"\\\\)"
                        "a\nb\tc", "url(a\\a b\\9 c)"
                    ] do
                    let value: Value<unit> = Generated.urlToken input
                    Expect.equal (Value.render value) expected "A URL is serialized as one url token."

                for input in [ null; ""; "a\u0000b" ] do
                    Expect.throwsT<System.ArgumentException>
                        (fun () -> (Generated.urlToken input: Value<unit>) |> ignore)
                        "Empty URLs and U+0000 are rejected."
            testCase "generated dynamic slots use their spec-derived keyword registry"
            <| fun _ ->
                let style =
                    Style.create
                        [
                            Style.dynamicWithKeywords [ "future-wide" ] "color" (fun () -> Declaration.create "color" "future-wide")
                        ]

                Expect.throwsT<System.ArgumentException> (fun () -> Style.renderVariables () style |> ignore) "Future keywords keep cascade semantics"
            testCase "computation expressions preserve declarations, component nodes and stylesheet rules"
            <| fun _ ->
                let values =
                    declarations {
                        for value in [ "block"; "grid" ] do
                            yield Declaration.create "display" value
                    }

                let style: Styled<int> =
                    styled {
                        yield! [ Style.declarations values ]
                        yield Style.dynamic "width" (fun n -> Declaration.create "width" (string n + "px"))
                    }

                let rules =
                    stylesheet {
                        yield! style.Definition.Rules
                        yield Rule.media "print" []
                    }

                Expect.stringContains (Stylesheet.render rules) "display: block; display: grid;" "Declaration loop order"
                Expect.stringContains (Style.renderVariables 42 style) "42px" "Reader retained through builder"
            testCase "CSS-wide reactive values require explicit inline binding"
            <| fun _ ->
                let style =
                    Style.create [ Style.dynamic "color" (fun keyword -> Declaration.create "color" keyword) ]

                for keyword in
                    [
                        "initial"
                        "inherit"
                        "unset"
                        "revert"
                        "revert-layer"
                        " /* comment */ INHERIT "
                        "\\69 nitial"
                    ] do
                    Expect.throwsT<System.ArgumentException> (fun () -> Style.renderVariables keyword style |> ignore) "CSS-wide semantics"
            testCase "numeric tokens have portable decimal and exponent spelling"
            <| fun _ ->
                let cases =
                    [
                        0.0, "0"
                        -0.0, "0"
                        0.5, "0.5"
                        1e-6, "0.000001"
                        1e-7, "1e-7"
                        1e20, "100000000000000000000"
                        1e21, "1e21"
                        System.Double.Epsilon, "5e-324"
                    ]

                for number, expected in cases do
                    Expect.equal (Generated.number number "" |> Value.render) expected "Portable number"
            testCase "ordered declarations preserve fallbacks and importance"
            <| fun _ ->
                let rule =
                    Rule.style
                        (Selector.className "card")
                        [
                            Declaration.create "display" "block"
                            Declaration.create "display" "grid"
                            |> Declaration.important
                        ]

                Expect.equal (Stylesheet.render [ rule ]) ".card { display: block; display: grid !important; }\n" "Cascade order"
            testCase "identical component definitions have stable automatic names"
            <| fun _ ->
                let make () : Styled<unit> =
                    Style.create [ Style.declarations [ Declaration.create "display" "grid" ] ]

                let first, second = make (), make ()
                Expect.equal first.Definition.ClassName second.Definition.ClassName "Content identity"
                Expect.isTrue (first.Definition.ClassName.StartsWith "r-") "Automatic class"
                Expect.stringContains (Stylesheet.render first.Definition.Rules) ("." + first.Definition.ClassName) "Rule selector"
            testCase "reactive values change variables while static CSS and identity stay fixed"
            <| fun _ ->
                let definition =
                    Style.create
                        [
                            Style.dynamic "width" (fun n -> Declaration.create "width" (string n + "px"))
                        ]

                let before = Stylesheet.render definition.Definition.Rules

                let first, second =
                    Style.renderVariables 10 definition, Style.renderVariables 20 definition

                Expect.notEqual first second "Different instance values"
                Expect.stringContains first "10px" "First value"
                Expect.stringContains second "20px" "Updated value"
                Expect.equal (Stylesheet.render definition.Definition.Rules) before "No new rules"
                Expect.stringContains before "var(--r-" "Static variable reference"
            testCase "nested conditions and layers keep source order"
            <| fun _ ->
                let definition: Styled<unit> =
                    Style.create
                        [
                            Style.declarations [ Declaration.create "display" "grid" ]
                            Style.hover [ Style.declarations [ Declaration.create "opacity" "0.5" ] ]
                            Style.media "(width >= 768px)" [ Style.declarations [ Declaration.create "display" "flex" ] ]
                        ]

                let css =
                    Stylesheet.render [ Rule.layer (Some "components") definition.Definition.Rules ]

                Expect.stringContains css ":hover" "Pseudo selector"
                Expect.stringContains css "@media (width >= 768px)" "Media rule"
                Expect.stringContains css "@layer components" "Layer"
            testCase "declared typed variables serialize fallback and bindings"
            <| fun _ ->
                let variable: CustomProperty<obj> = CustomProperty.create "--progress"
                let initial: Value<obj> = Generated.number 0.5 ""

                Expect.equal
                    (CustomProperty.referenceWithFallback variable initial
                     |> Value.render)
                    "var(--progress, 0.5)"
                    "Fallback"

                Expect.equal
                    (CustomProperty.declaration variable initial
                     |> Declaration.render)
                    "--progress: 0.5;"
                    "Binding"
            testCase "dynamic property readers cannot change the declared property"
            <| fun _ ->
                let definition =
                    Style.create [ Style.dynamic "width" (fun () -> Declaration.create "height" "1px") ]

                Expect.throwsT<System.ArgumentException> (fun () -> Style.renderVariables () definition |> ignore) "Reader identity"
            testCase "property names are ASCII identifiers or custom properties"
            <| fun _ ->
                for name in
                    [
                        "color"
                        "-webkit-box"
                        "_x"
                        "-x"
                        "-_"
                        "A"
                        "a1"
                        "a-"
                        "a_"
                        "--x"
                        "---"
                        "--1"
                        "--a-b_c9"
                    ] do
                    Expect.equal (Declaration.create name "v" |> Declaration.name) name "A valid name is kept as written."

                for name in
                    [
                        null
                        ""
                        " "
                        "-"
                        "--"
                        "1x"
                        "-1"
                        "a b"
                        " a"
                        "a "
                        "a.b"
                        "a:b"
                        "é"
                        "aé"
                        "\na"
                        "\n"
                        "-\n"
                        "--\n"
                        "a\r"
                        "a\r\n"
                        "a\n\n"
                    ] do
                    let raised =
                        try
                            Declaration.create name "v" |> ignore
                            None
                        with :? System.ArgumentException as error ->
                            Some (error.ParamName, error.Message)

                    Expect.equal raised (Some ("name", "Expected a CSS property name. (Parameter 'name')")) "An invalid name is rejected by name."

                Expect.throwsT<System.ArgumentNullException> (fun () -> Declaration.create "color" null |> ignore) "Null value text is rejected."
            testCase "a property name may end in one line feed on .NET"
            <| fun _ ->
                for name in [ "x\n"; "-x\n"; "--x\n" ] do
                    Expect.equal (Declaration.create name "v" |> Declaration.name) name "The pattern's end anchor matches before a final line feed."
            testCase "every short name is accepted exactly when the property-name pattern matches it"
            <| fun _ ->
                let pattern =
                    System.Text.RegularExpressions.Regex @"^--[A-Za-z0-9_-]+$|^-?[A-Za-z_][A-Za-z0-9_-]*$"

                let alphabet = [ "-"; "_"; "a"; "Z"; "0"; " "; "\n"; "é"; "." ]

                let rec names length =
                    if length = 0 then
                        [ "" ]
                    else
                        [
                            for prefix in names (length - 1) do
                                for letter in alphabet -> prefix + letter
                        ]

                let disagreements =
                    [
                        for length in 1..4 do
                            for name in names length do
                                let accepted =
                                    try
                                        Declaration.create name "v" |> ignore
                                        true
                                    with :? System.ArgumentException ->
                                        false

                                if accepted <> pattern.IsMatch name then
                                    name
                    ]

                Expect.isEmpty disagreements "Acceptance follows the pattern for every name of up to four characters."
            testCase "CSS-wide keywords are recognized through case, spacing, comments and escapes"
            <| fun _ ->
                let wide text =
                    Declaration.isCssWide (Declaration.create "color" text)

                for text in
                    [
                        "initial"
                        "INHERIT"
                        "Unset"
                        "  unset  "
                        " INITIAL "
                        "\tRevert\n"
                        "revert-layer"
                        "revert-rule"
                        "/**/initial"
                        "initial/* c */"
                        "\\69 nitial"
                        "\\000069nitial"
                        "\\0069 nitial"
                        "\\49NITIAL"
                        "i\\6e itial"
                        "ini\\74 ial"
                        "\\initial"
                    ] do
                    Expect.isTrue (wide text) ("CSS-wide: " + text)

                for text in
                    [
                        ""
                        "red"
                        "120px"
                        "var(--x)"
                        "initial initial"
                        "initial;"
                        "inherit !important"
                        "in/**/herit"
                        "/* initial */"
                        "/*initial"
                        "/ * */initial"
                        "initial\\"
                        "\\i nitial"
                        "\\80 initial"
                    ] do
                    Expect.isFalse (wide text) ("Ordinary: " + text)
            testCase "a supplied keyword registry replaces the CSS-wide keywords"
            <| fun _ ->
                let declaration text =
                    Declaration.create "color" text

                Expect.isTrue
                    (Declaration.isCssWideWith [ "future-wide" ] (declaration " Future-Wide "))
                    "A registered keyword matches through case and spacing."

                Expect.isFalse (Declaration.isCssWideWith [ "future-wide" ] (declaration "initial")) "A keyword outside the registry is ordinary."
                Expect.isFalse (Declaration.isCssWideWith [] (declaration "initial")) "An empty registry recognizes no keyword."
            testCase "dynamic slots lower reader values to unimportant custom properties under a stable class"
            <| fun _ ->
                let px (value: float) =
                    Value.render (Generated.number value "px": Value<unit>)

                let style: Styled<float> =
                    Style.create
                        [
                            Style.declarations [ Declaration.create "position" "absolute" ]
                            Style.dynamic "top" (fun value -> Declaration.create "top" (px value))
                            Style.dynamicImportant "width" (fun value ->
                                Declaration.create "width" (px (value * 2.))
                                |> Declaration.important)
                            Style.whenActive
                                (fun value -> value > 0.)
                                [
                                    Style.dynamic "opacity" (fun value ->
                                        Declaration.create "opacity" (Value.render (Generated.number (value / 100.) "": Value<unit>)))
                                ]
                        ]

                Expect.equal style.Definition.ClassName "r-a8beb49194bc3121" "The class is the hash of the rendered template."

                Expect.equal
                    (Stylesheet.render style.Definition.Rules)
                    ("@property --r-a8beb49194bc3121-1 { syntax: \"*\"; inherits: false; }\n"
                     + "@property --r-a8beb49194bc3121-2 { syntax: \"*\"; inherits: false; }\n"
                     + "@property --r-a8beb49194bc3121-3 { syntax: \"*\"; inherits: false; }\n"
                     + ".r-a8beb49194bc3121 { position: absolute; }\n"
                     + ".r-a8beb49194bc3121 { top: var(--r-a8beb49194bc3121-1); }\n"
                     + ".r-a8beb49194bc3121 { width: var(--r-a8beb49194bc3121-2) !important; }\n"
                     + ".r-a8beb49194bc3121:where(.r-a8beb49194bc3121-g0) { opacity: var(--r-a8beb49194bc3121-3); }\n")
                    "Each slot is a non-inherited variable reference, with importance fixed in the rule."

                Expect.equal
                    (Style.variableDeclarations 12.5 style
                     |> List.map (fun declaration -> Declaration.name declaration, Declaration.value declaration, Declaration.isImportant declaration))
                    [
                        "--r-a8beb49194bc3121-1", "12.5px", false
                        "--r-a8beb49194bc3121-2", "25px", false
                        "--r-a8beb49194bc3121-3", "0.125", false
                    ]
                    "A lowered declaration carries the slot's variable, the reader's value text and no importance."

                Expect.equal
                    (Style.renderVariables 12.5 style)
                    "--r-a8beb49194bc3121-1: 12.5px; --r-a8beb49194bc3121-2: 25px; --r-a8beb49194bc3121-3: 0.125;"
                    "Active slots render in declaration order."

                Expect.equal
                    (Style.renderVariables -1. style)
                    "--r-a8beb49194bc3121-1: -1px; --r-a8beb49194bc3121-2: -2px;"
                    "A slot under an inactive condition is omitted."
            testCase "a slot read by the element that carries its variable is registered without inheritance"
            <| fun _ ->
                let top =
                    Style.dynamic "top" (fun (value: float) -> Declaration.create "top" (Value.render (Generated.number value "px": Value<unit>)))

                let registrations nodes =
                    let style: Styled<float> = Style.create nodes

                    style.Definition.Rules
                    |> List.choose (function
                        | DescriptorRule ("property", name, declarations) -> Some (name, Declaration.renderMany declarations)
                        | _ -> None),
                    style.Definition.ClassName

                let registered nodes =
                    match registrations nodes with
                    | [ name, descriptors ], className ->
                        Expect.equal name ("--" + className + "-1") "The registration names the slot's variable."
                        Expect.equal descriptors "syntax: \"*\"; inherits: false;" "Universal syntax, no inheritance, no initial value."
                        true
                    | [], _ -> false
                    | other, _ -> failwithf "Expected at most one registration, got %A" other

                for suffix in
                    [
                        ":hover"
                        ":focus-within"
                        ":is(.dark)"
                        ":where(.a .b)"
                        ":nth-child(2n + 1)"
                        "[data-x=\"a b\"]"
                        ".active"
                        ":hover "
                    ] do
                    Expect.isTrue (registered [ Style.nest suffix [ top ] ]) $"'{suffix}' keeps the rule on the element"

                Expect.isTrue (registered [ Style.media "(width > 10px)" [ top ] ]) "A condition keeps the rule on the element"
                Expect.isTrue (registered [ Style.whenActive (fun value -> value > 0.) [ top ] ]) "A gate keeps the rule on the element"

                for suffix in
                    [
                        " svg"
                        "\tsvg"
                        " > span"
                        "+ .next"
                        " ~ .later"
                        "::before"
                        ":after"
                        ":FIRST-LINE"
                        ", .other"
                        ":hover .child"
                        " &"
                    ] do
                    Expect.isFalse (registered [ Style.nest suffix [ top ] ]) $"'{suffix}' moves the rule to another box"

                Expect.isFalse (registered [ Style.nest " svg" [ Style.hover [ top ] ] ]) "A pseudo-class under a descendant stays on the descendant"
                Expect.isFalse (registered [ Style.media "(width > 10px)" [ Style.nest "::marker" [ top ] ] ]) "A pseudo-element under a condition"

                let progress: CustomProperty<obj> = CustomProperty.create "--progress"

                Expect.isEmpty
                    (fst (registrations [ Style.bind progress (fun (value: float) -> Generated.number value "") ]))
                    "A variable the author names keeps its own registration."

                let template: Styled<float> = Style.create [ Style.nest " svg" [ top ] ]
                let registeredStyle: Styled<float> = Style.create [ top ]

                Expect.equal
                    (Stylesheet.render template.Definition.Rules)
                    ("."
                     + template.Definition.ClassName
                     + " svg { top: var(--"
                     + template.Definition.ClassName
                     + "-1); }\n")
                    "An unregistered slot renders as before."

                Expect.equal
                    registeredStyle.Definition.ClassName
                    ("r-"
                     + Identity.hash ("& { top: var(--r-slot-1); }\n"))
                    "Registrations stay out of the hashed template, so class names are unchanged."
            testCase "dynamic slots reject importance the stylesheet did not declare and keep ordinary text verbatim"
            <| fun _ ->
                let important: Styled<unit> =
                    Style.create
                        [
                            Style.dynamic "width" (fun () ->
                                Declaration.create "width" "1px"
                                |> Declaration.important)
                        ]

                Expect.throwsT<System.ArgumentException>
                    (fun () -> Style.renderVariables () important |> ignore)
                    "Importance belongs to the static rule."

                let color: Styled<string> =
                    Style.create [ Style.dynamic "color" (fun text -> Declaration.create "color" text) ]

                for text in [ "red /* inherit */"; "" ] do
                    Expect.equal
                        (Style.renderVariables text color)
                        ("--r-65aabb2614649ebe-1: " + text + ";")
                        "Ordinary value text is lowered unchanged."
        ]
