namespace Ranvier.Css.Dom

open System
open Ranvier.Css
open Ranvier.Dom

/// <summary>An opaque snapshot of an entire CSS declaration, including its serialized order and priorities.</summary>
type StyleSnapshot = private StyleSnapshot of string

/// <summary>Applies typed CSS declarations to browser and native DOM declaration objects.</summary>
module StyleDeclaration =
    let private requireStyle (style: CSSStyleDeclaration) =
        if isNull (box style) then
            nullArg (nameof style)

    let private requireDeclaration (declaration: Declaration) =
        if isNull (box declaration) then
            nullArg (nameof declaration)

    /// <summary>Sets a declaration's property, serialized value and importance through the engine.</summary>
    /// <param name="style">A writable declaration on its owning thread and within its lifetime.</param>
    /// <param name="declaration">A declaration constructed with the caller's chosen grammar validation.</param>
    /// <remarks>The engine retains its property validation and read-only error behaviour.</remarks>
    let apply (style: CSSStyleDeclaration) (declaration: Declaration) =
        requireStyle style
        requireDeclaration declaration

        style.setProperty (
            Declaration.name declaration,
            Declaration.value declaration,
            if Declaration.isImportant declaration then
                "important"
            else
                ""
        )

    /// <summary>Applies declarations in enumeration order, including repeated property fallbacks.</summary>
    /// <param name="style">A writable declaration on its owning thread and within its lifetime.</param>
    /// <param name="declarations">Ordered declarations; an exception preserves earlier successful changes.</param>
    let applyMany (style: CSSStyleDeclaration) (declarations: seq<Declaration>) =
        requireStyle style

        if isNull (box declarations) then
            nullArg (nameof declarations)

        for declaration in declarations do
            apply style declaration

    /// <summary>Reads the engine's serialized value and priority for a typed declaration's property.</summary>
    /// <param name="style">A declared or computed style on its owning thread and within its lifetime.</param>
    /// <param name="declaration">Selects the property name; its value and importance are ignored.</param>
    /// <returns>The current value and priority; both are empty when the property is absent.</returns>
    let read (style: CSSStyleDeclaration) (declaration: Declaration) =
        requireStyle style
        requireDeclaration declaration
        let name = Declaration.name declaration
        style.getPropertyValue name, style.getPropertyPriority name

    /// <summary>Captures the engine's complete serialized declaration for explicit restoration.</summary>
    /// <param name="style">A declared style on its owning thread and within its lifetime.</param>
    let capture (style: CSSStyleDeclaration) =
        requireStyle style
        StyleSnapshot style.cssText

    /// <summary>Replaces the complete declaration with a captured snapshot, including priority and shorthand order.</summary>
    /// <param name="style">A writable declaration on its owning thread and within its lifetime.</param>
    /// <param name="snapshot">The complete declaration to restore; later mutations are replaced.</param>
    let restore (style: CSSStyleDeclaration) (snapshot: StyleSnapshot) =
        requireStyle style

        if isNull (box snapshot) then
            nullArg (nameof snapshot)

        let (StyleSnapshot text) = snapshot
        style.cssText <- text
