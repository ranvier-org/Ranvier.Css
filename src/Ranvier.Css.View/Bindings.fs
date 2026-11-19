namespace Ranvier.Css.View

open Ranvier.Css
open Ranvier.View

#if !FABLE_COMPILER
module AppliedStyles =
    let internal record (_: StyleDefinition) = ()

    let count () = 0

    let after (_: int) : StyleDefinition list = []
#endif

/// <summary>Template style parts using Ranvier's tracked readers and component disposal.</summary>
module CssView =
    /// <summary>Applies a static generated class name.</summary>
    let className (attrs: AttributeBuilder<'instance>) (definition: StyleDefinition) : Part<'native, 'instance> =
        attrs.composableClass (attrs.attribute ("class", definition.ClassName))

    /// <summary>Binds current variable declarations through the template's tracked style attribute.</summary>
    let variables (attrs: AttributeBuilder<'instance>) (style: Styled<'instance>) : Part<'native, 'instance> =
        attrs.attribute ("style", fun instance -> Some (Style.renderVariables instance style))

    /// <summary>Binds ordered inline declarations; changes to signals read by the reader update this attribute.</summary>
    let inlineStyle (attrs: AttributeBuilder<'instance>) (read: 'instance -> Declaration list) : Part<'native, 'instance> =
        attrs.attribute ("style", fun instance -> Some (Declaration.renderMany (read instance)))

    /// <summary>Class and tracked style parts for static class names, component styles and inline declarations.</summary>
    /// <remarks>The result has one writer for the class attribute and one for the style attribute.</remarks>
    let partsWithInline extraClasses (styles: Styled<'instance> list) (read: 'instance -> Declaration list) : Part<'native, 'instance> array =
        let classes instance =
            extraClasses
            @ (styles |> List.collect (Style.classNames instance))
            |> List.distinct
            |> String.concat " "

        [|
            if
                styles
                |> List.exists Style.hasApplicationConditions
            then
                TemplateParts.composableClass (TemplateParts.readAttribute "class" (fun instance -> Some (classes instance)))
            else
                TemplateParts.composableClass (
                    TemplateParts.attribute
                        "class"
                        (extraClasses
                         @ (styles |> List.map _.Definition.ClassName)
                         |> List.distinct
                         |> String.concat " ")
                )
            TemplateParts.readAttribute "style" (fun instance ->
                Some (
                    Declaration.renderMany (
                        (styles
                         |> List.collect (Style.variableDeclarations instance))
                        @ read instance
                    )
                ))
        |]

    /// <summary>Class and tracked style parts for one component style, for an element constructor's parts.</summary>
    /// <remarks>These parts need no attribute builder, so they compose with <c>Ranvier.View.Dsl</c> element constructors.</remarks>
    let parts (style: Styled<'instance>) : Part<'native, 'instance> array =
        partsWithInline [] [ style ] (fun _ -> [])

    /// <summary>Combines static class names, component styles and inline declarations into one class and one tracked style part.</summary>
    /// <remarks>Use this method when composing styles on an element to retain a single writer for each attribute.</remarks>
    let applyWithInline (_: AttributeBuilder<'instance>) extraClasses (styles: Styled<'instance> list) read : Part<'native, 'instance> array =
        partsWithInline extraClasses styles read

    /// <summary>Applies several component styles while combining their current variables into one tracked attribute.</summary>
    let applyMany attrs styles =
        applyWithInline attrs [] styles (fun _ -> [])

    /// <summary>Applies a component's generated class and current reactive variables.</summary>
    let apply attrs style =
        applyMany attrs [ style ]

    /// <summary>Appends emitted stylesheet URLs to an SSR document shell, preserving dependency order and removing repeated URLs.</summary>
    let withStylesheets urls (shell: Ranvier.View.Server.DocumentShell) =
        { shell with
            Stylesheets = List.distinct (shell.Stylesheets @ urls)
        }
