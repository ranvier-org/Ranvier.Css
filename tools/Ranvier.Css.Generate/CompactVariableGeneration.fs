namespace Ranvier.Css.Spec

/// <summary>Emits category-preserving custom-property handles and static CSS-wide keyword values.</summary>
module internal CompactVariableGeneration =
    /// <summary>Returns declarations for the CssValue module; custom-property names are validated at construction.</summary>
    let source () =
        """/// <summary>A CSS custom-property reference restricted to values admitted by its category.</summary>
type Variable<'category when 'category :> CssGrammar.IValue> internal (name: string, fallback: 'category option) =
    member internal _.Name = name
    member internal _.Fallback = fallback
    member internal _.Text =
        match fallback with
        | None -> "var(" + name + ")"
        | Some value -> "var(" + name + ", " + (value :> CssGrammar.IValue).Data.Text + ")"

/// <summary>Validated CSS custom-property references, typed fallbacks and assignments.</summary>
module Variable =
    /// <summary>Creates a category-preserving handle for a nonempty dashed CSS identifier.</summary>
    let create<'category when 'category :> CssGrammar.IValue> (name: string) : Variable<'category> =
        global.Ranvier.Css.Generated.identifier true false name |> ignore
        Variable<'category>(name, None)

    /// <summary>Returns a reference with a fallback admitted by the handle's category.</summary>
    let withFallback (variable: Variable<'category>) (value: 'category) : Variable<'category> =
        Variable<'category>(variable.Name, Some value)

    /// <summary>Assigns a value admitted by the custom property's category.</summary>
    let declaration (variable: Variable<'category>) (value: 'category) : global.Ranvier.Css.Declaration =
        global.Ranvier.Css.RuntimeApi.declaration variable.Name (value :> CssGrammar.IValue).Data.Text

/// <summary>Creates a typed custom-property reference from a validated dashed CSS identifier.</summary>
/// <remarks>The category asserts the expected substituted value; external definitions remain subject to CSS validation.</remarks>
let variable<'category when 'category :> CssGrammar.IValue> (name: string) : Variable<'category> =
    Variable.create name

/// <summary>Creates a typed custom-property reference from a validated dashed CSS identifier.</summary>
let ``var``<'category when 'category :> CssGrammar.IValue> (name: string) : Variable<'category> =
    Variable.create name

/// <summary>A CSS-wide keyword accepted by static property declarations.</summary>
type Wide internal (text: string) =
    member internal _.Text = text

/// <summary>CSS-wide cascade and inheritance keywords for static declarations.</summary>
module CssWide =
    /// <summary>Uses the property's initial value.</summary>
    let initial = Wide "initial"
    /// <summary>Uses the inherited property value.</summary>
    let inherit' = Wide "inherit"
    /// <summary>Uses inheritance or the initial value according to the property.</summary>
    let unset = Wide "unset"
    /// <summary>Rolls the property back to the preceding cascade origin.</summary>
    let revert = Wide "revert"
    /// <summary>Rolls the property back to the preceding cascade layer.</summary>
    let revertLayer = Wide "revert-layer"
    /// <summary>Rolls the property back to the preceding cascade rule.</summary>
    let revertRule = Wide "revert-rule"

/// <summary>Uses the property's initial value.</summary>
let initial = CssWide.initial
/// <summary>Uses the inherited property value.</summary>
let inherit' = CssWide.inherit'
/// <summary>Uses inheritance or the initial value according to the property.</summary>
let unset = CssWide.unset
/// <summary>Rolls the property back to the preceding cascade origin.</summary>
let revert = CssWide.revert
/// <summary>Rolls the property back to the preceding cascade layer.</summary>
let revertLayer = CssWide.revertLayer
/// <summary>Rolls the property back to the preceding cascade rule.</summary>
let revertRule = CssWide.revertRule
"""
