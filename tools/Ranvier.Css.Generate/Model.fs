namespace Ranvier.Css.Spec

/// <summary>A half-open range of UTF-16 code units in the original grammar.</summary>
type SourceSpan = { Offset: int; Length: int }

/// <summary>A numeric endpoint retains its unit as written in the specification.</summary>
[<RequireQualifiedAccess>]
type RangeBound =
    | NegativeInfinity
    | Finite of value: decimal * unitName: string
    | PositiveInfinity

/// <summary>Inclusive limits attached to a numeric type reference.</summary>
type NumericRange =
    {
        Minimum: RangeBound
        Maximum: RangeBound
    }

/// <summary>References distinguish property value grammars from named types and functions.</summary>
[<RequireQualifiedAccess>]
type ReferenceKind =
    | Type
    | Property
    | Function

/// <summary>A reference stays symbolic; expanding recursive definitions is a generator policy.</summary>
type GrammarReference =
    {
        Kind: ReferenceKind
        Name: string
        Range: NumericRange option
    }

/// <summary>Repetition bounds count occurrences; an absent maximum is unbounded.</summary>
type Repetition =
    {
        Minimum: int
        Maximum: int option
        CommaSeparated: bool
    }

/// <summary>A grammar node retains its source range, including grouping and multipliers.</summary>
type GrammarNode =
    {
        Span: SourceSpan
        Expression: GrammarExpression
    }

/// <summary>CSS value-definition operators retain their precedence and source ordering.</summary>
and [<RequireQualifiedAccess>] GrammarExpression =
    | Literal of string
    | Reference of GrammarReference
    | TypeApplication of name: string * argument: GrammarNode
    | Sequence of GrammarNode list
    | AllOf of GrammarNode list
    | AnyOf of GrammarNode list
    | Choice of GrammarNode list
    | Group of GrammarNode
    | Function of name: string * arguments: GrammarNode option
    | Block of opening: string * contents: GrammarNode option
    | Repeat of GrammarNode * Repetition
    | Required of GrammarNode

/// <summary>A grammar failure identifies a zero-based UTF-16 offset and an actionable reason.</summary>
type GrammarDiagnostic = { Offset: int; Message: string }

/// <summary>Missing grammar and rejected grammar remain distinct from successfully parsed definitions.</summary>
[<RequireQualifiedAccess>]
type GrammarStatus =
    | Missing
    | Parsed of source: string * tree: GrammarNode
    | Rejected of source: string * diagnostic: GrammarDiagnostic

/// <summary>Descriptors belong to an at-rule; all other kinds identify top-level Webref categories.</summary>
[<RequireQualifiedAccess>]
type FeatureKind =
    | Property
    | Type
    | Function
    | Selector
    | AtRule
    | Descriptor

/// <summary>Names are unique within a kind, scope set and optional descriptor owner.</summary>
type FeatureId =
    {
        Kind: FeatureKind
        Name: string
        Scopes: string list
        Owner: string option
    }

/// <summary>A CSS definition preserves spec metadata as JSON values alongside its parsed grammar.</summary>
type Feature =
    {
        Id: FeatureId
        Href: string
        Extended: string list
        Grammar: GrammarStatus
        Metadata: Map<string, string>
    }

/// <summary>Input identity records the exact bytes consumed, independently of a supplied version label.</summary>
type DatasetIdentity = { Label: string; Sha256: string }

/// <summary>A catalogue contains every input definition in stable identity order.</summary>
type Catalogue =
    {
        Identity: DatasetIdentity
        Features: Feature list
    }

/// <summary>An input error identifies the JSON path of malformed or duplicate definitions.</summary>
type InputDiagnostic = { Path: string; Message: string }
