namespace Ranvier.Css.Spec

open System
open System.Globalization
open System.Text.RegularExpressions

/// <summary>Parses CSS value-definition grammars without expanding named references.</summary>
module Grammar =
    exception private ParseFailure of GrammarDiagnostic

    /// <summary>Consumes the entire grammar; failures retain the offset of invalid or unsupported syntax.</summary>
    let parse (source: string) : Result<GrammarNode, GrammarDiagnostic> =
        if isNull source then
            Error
                {
                    Offset = 0
                    Message = "Grammar must be a string."
                }
        elif source.Length > 65536 then
            Error
                {
                    Offset = 0
                    Message = "Grammar exceeds 65536 UTF-16 code units."
                }
        else
            let mutable pos = 0
            let mutable depth = 0

            let fail message =
                raise (ParseFailure { Offset = pos; Message = message })

            let peek () =
                if pos < source.Length then source[pos] else char 0

            let starts (text: string) =
                source.AsSpan(pos).StartsWith(text.AsSpan (), StringComparison.Ordinal)

            let skip () =
                while pos < source.Length
                      && Char.IsWhiteSpace source[pos] do
                    pos <- pos + 1

            let node start expression =
                {
                    Span = { Offset = start; Length = pos - start }
                    Expression = expression
                }

            let quote () =
                let mark = peek ()
                pos <- pos + 1
                let start = pos

                while pos < source.Length && peek () <> mark do
                    if peek () = '\\' then
                        pos <- pos + 1

                        if pos >= source.Length then
                            fail "Unterminated quoted literal."

                    pos <- pos + 1

                if pos >= source.Length then
                    fail "Unterminated quoted literal."

                let value = source.Substring (start, pos - start)
                pos <- pos + 1
                value

            let bound (text: string) =
                let text = text.Trim ()

                match text with
                | "\u221e"
                | "+\u221e"
                | "infinity"
                | "+infinity" -> RangeBound.PositiveInfinity
                | "-\u221e"
                | "-infinity" -> RangeBound.NegativeInfinity
                | _ ->
                    let m = Regex.Match (text, @"^([+-]?(?:\d+(?:\.\d*)?|\.\d+))([a-zA-Z%]*)$")

                    match
                        Decimal.TryParse (
                            m.Groups[1].Value,
                            NumberStyles.AllowLeadingSign
                            ||| NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture
                        )
                    with
                    | true, number when m.Success -> RangeBound.Finite (number, m.Groups[2].Value)
                    | _ -> fail ("Invalid numeric range endpoint: " + text)

            let count () =
                let start = pos

                while Char.IsAsciiDigit (peek ()) do
                    pos <- pos + 1

                match Int32.TryParse (source.Substring (start, pos - start)) with
                | true, value -> value
                | _ -> fail "Expected a nonnegative repetition count."

            let bounds comma =
                pos <- pos + 1
                skip ()
                let minimum = count ()
                skip ()

                let maximum =
                    if peek () = ',' then
                        pos <- pos + 1
                        skip ()
                        if peek () = '}' then None else Some (count ())
                    else
                        Some minimum

                skip ()

                if peek () <> '}' then
                    fail "Expected '}' after repetition bounds."

                pos <- pos + 1

                match maximum with
                | Some value when value < minimum -> fail "Repetition minimum exceeds maximum."
                | _ -> ()

                {
                    Minimum = minimum
                    Maximum = maximum
                    CommaSeparated = comma
                }

            let rec expression closing =
                depth <- depth + 1

                if depth > 128 then
                    fail "Grammar nesting exceeds 128 levels."

                let value = choice closing
                depth <- depth - 1
                value

            and combine closing (next: char option -> GrammarNode) operator make =
                let first = next closing
                let rest = ResizeArray<GrammarNode>()
                skip ()

                while starts operator do
                    pos <- pos + operator.Length
                    rest.Add (next closing)
                    skip ()

                if rest.Count = 0 then
                    first
                else
                    node first.Span.Offset (make (first :: List.ofSeq rest))

            and choice closing =
                combine closing anyOf "|" GrammarExpression.Choice

            and anyOf closing =
                combine closing allOf "||" GrammarExpression.AnyOf

            and allOf closing =
                combine closing sequence "&&" GrammarExpression.AllOf

            and sequence closing =
                skip ()
                let start = pos
                let items = ResizeArray<GrammarNode>()

                let stopped () =
                    pos >= source.Length
                    || Some (peek ()) = closing
                    || peek () = '|'
                    || peek () = '&'

                while not (stopped ()) do
                    items.Add (term ())
                    skip ()

                if items.Count = 0 then
                    fail "Expected a grammar component."
                elif items.Count = 1 then
                    items[0]
                else
                    node start (GrammarExpression.Sequence (List.ofSeq items))

            and enclosed start closing make =
                pos <- pos + 1
                skip ()

                let contents =
                    if peek () = closing then
                        None
                    else
                        Some (expression (Some closing))

                if peek () <> closing then
                    fail ("Expected '" + string closing + "'.")

                pos <- pos + 1
                node start (make contents)

            and reference () =
                pos <- pos + 1
                let start = pos

                while pos < source.Length
                      && peek () <> '>'
                      && peek () <> '[' do
                    pos <- pos + 1

                if pos >= source.Length then
                    fail "Unterminated type reference."

                let name = source.Substring(start, pos - start).Trim()

                if String.IsNullOrWhiteSpace name then
                    fail "A reference needs a name."

                let kind, cleanName =
                    if
                        name.StartsWith ("'")
                        && name.EndsWith ("'")
                        && name.Length > 2
                    then
                        ReferenceKind.Property, name.Substring (1, name.Length - 2)
                    elif name.EndsWith ("()", StringComparison.Ordinal) then
                        ReferenceKind.Function, name.Substring (0, name.Length - 2)
                    else
                        ReferenceKind.Type, name

                let validName =
                    cleanName.Length > 0
                    && (Char.IsLetter cleanName[0]
                        || cleanName[0] = '-'
                        || cleanName[0] = '_')
                    && (cleanName
                        |> Seq.forall (fun c -> Char.IsLetterOrDigit c || c = '-' || c = '_'))

                if not validName then
                    fail "Invalid reference name."

                let range, argument =
                    if peek () = '[' then
                        pos <- pos + 1
                        skip ()

                        if peek () = '<' || peek () = '[' then
                            if kind <> ReferenceKind.Type then
                                fail "Only a named type accepts grammar arguments."

                            let argument = expression (Some ']')

                            if peek () <> ']' then
                                fail "Expected ']' after type argument."

                            pos <- pos + 1
                            None, Some argument
                        else
                            let rangeStart = pos

                            while pos < source.Length && peek () <> ']' do
                                pos <- pos + 1

                            if pos >= source.Length then
                                fail "Unterminated numeric range."

                            let limits = source.Substring(rangeStart, pos - rangeStart).Split(',')

                            if limits.Length <> 2 then
                                fail "A numeric range needs two endpoints."

                            let minimum, maximum = bound limits[0], bound limits[1]

                            match minimum, maximum with
                            | RangeBound.PositiveInfinity, _
                            | _, RangeBound.NegativeInfinity -> fail "Invalid range limits."
                            | RangeBound.Finite (a, u), RangeBound.Finite (b, v) when u = v && a > b -> fail "Range minimum exceeds maximum."
                            | _ -> ()

                            pos <- pos + 1
                            Some { Minimum = minimum; Maximum = maximum }, None
                    else
                        None, None

                skip ()

                if peek () <> '>' then
                    fail "Expected '>' after type reference."

                pos <- pos + 1

                match argument with
                | Some value -> GrammarExpression.TypeApplication (cleanName, value)
                | None ->
                    GrammarExpression.Reference
                        {
                            Kind = kind
                            Name = cleanName
                            Range = range
                        }

            and term () =
                skip ()
                let start = pos

                let value =
                    match peek () with
                    | '<' -> node start (reference ())
                    | '\''
                    | '"' -> node start (GrammarExpression.Literal (quote ()))
                    | '[' ->
                        enclosed start ']' (fun contents ->
                            match contents with
                            | Some inner -> GrammarExpression.Group inner
                            | None -> fail "A grammar group must contain a component.")
                    | '(' -> enclosed start ')' (fun inner -> GrammarExpression.Block ("(", inner))
                    | '{' -> enclosed start '}' (fun inner -> GrammarExpression.Block ("{", inner))
                    | ']'
                    | '}' -> fail "Unexpected closing delimiter."
                    | '?'
                    | '*'
                    | '+'
                    | '#'
                    | '!' -> fail "A multiplier needs a preceding component."
                    | '&' -> fail "Expected '&&'."
                    | c when
                        Char.IsLetterOrDigit c
                        || c = '-'
                        || c = '_'
                        || c = '@'
                        ->
                        while pos < source.Length
                              && (Char.IsLetterOrDigit (peek ())
                                  || "-_@.%".Contains(peek ())) do
                            pos <- pos + 1

                        let name = source.Substring (start, pos - start)

                        if name.Contains ("...", StringComparison.Ordinal) then
                            fail "Prose ellipsis requires a semantic definition."

                        if peek () = '(' then
                            enclosed start ')' (fun inner -> GrammarExpression.Function (name, inner))
                        else
                            node start (GrammarExpression.Literal name)
                    | c ->
                        if starts "..." then
                            fail "Prose ellipsis requires a semantic definition."

                        pos <- pos + 1
                        node start (GrammarExpression.Literal (string c))

                let repeat repetition inner =
                    node start (GrammarExpression.Repeat (inner, repetition))

                let repeated =
                    match peek () with
                    | '?' ->
                        pos <- pos + 1

                        repeat
                            {
                                Minimum = 0
                                Maximum = Some 1
                                CommaSeparated = false
                            }
                            value
                    | '*' ->
                        pos <- pos + 1

                        repeat
                            {
                                Minimum = 0
                                Maximum = None
                                CommaSeparated = false
                            }
                            value
                    | '+' ->
                        pos <- pos + 1

                        repeat
                            {
                                Minimum = 1
                                Maximum = None
                                CommaSeparated = false
                            }
                            value
                    | '#' ->
                        pos <- pos + 1

                        let repetition =
                            if peek () = '{' then
                                bounds true
                            else
                                {
                                    Minimum = 1
                                    Maximum = None
                                    CommaSeparated = true
                                }

                        let inner = repeat repetition value

                        if peek () = '?' then
                            pos <- pos + 1

                            repeat
                                {
                                    Minimum = 0
                                    Maximum = Some 1
                                    CommaSeparated = false
                                }
                                inner
                        else
                            inner
                    | '{' when
                        pos + 1 < source.Length
                        && Char.IsAsciiDigit source[pos + 1]
                        ->
                        repeat (bounds false) value
                    | '!' ->
                        match value.Expression with
                        | GrammarExpression.Group _ ->
                            pos <- pos + 1
                            node start (GrammarExpression.Required value)
                        | _ -> fail "The required marker applies to a bracketed group."
                    | _ -> value

                let combined =
                    match value.Expression, repeated.Expression, peek () with
                    | _, GrammarExpression.Repeat (_, r), '#' when
                        r.Minimum = 1
                        && r.Maximum.IsNone
                        && not r.CommaSeparated
                        ->
                        pos <- pos + 1

                        let outer =
                            if peek () = '{' then
                                bounds true
                            else
                                {
                                    Minimum = 1
                                    Maximum = None
                                    CommaSeparated = true
                                }

                        let inner = repeat outer repeated

                        if peek () = '?' then
                            pos <- pos + 1

                            repeat
                                {
                                    Minimum = 0
                                    Maximum = Some 1
                                    CommaSeparated = false
                                }
                                inner
                        else
                            inner
                    | _, GrammarExpression.Repeat (_, r), '?' when r.Maximum.IsSome && r.Minimum > 0 ->
                        pos <- pos + 1

                        repeat
                            {
                                Minimum = 0
                                Maximum = Some 1
                                CommaSeparated = false
                            }
                            repeated
                    | _ -> repeated

                match peek () with
                | '?'
                | '*'
                | '+'
                | '#'
                | '!' -> fail "Unsupported multiplier combination."
                | _ -> combined

            try
                skip ()
                let tree = expression None
                skip ()

                if pos <> source.Length then
                    fail "Unexpected trailing input."

                Ok tree
            with ParseFailure diagnostic ->
                Error diagnostic
