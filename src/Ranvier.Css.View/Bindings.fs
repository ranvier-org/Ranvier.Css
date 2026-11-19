namespace Ranvier.Css.View

open Ranvier.Css
open Ranvier.View

#if !FABLE_COMPILER
/// <summary>
/// The definitions of the classes <c>CssView</c> has put on elements in this process, in order of first application.
/// </summary>
/// <remarks>
/// <para>A class enters the log once, by name, when <c>CssView</c> first builds parts that carry it. Every thread shares the log,
/// and it keeps each definition for the life of the process.</para>
/// <para>Compiled for .NET only. A Fable build takes its stylesheet from the extractor.</para>
/// </remarks>
module AppliedStyles =
    let private gate = obj ()

    let private names =
        System.Collections.Concurrent.ConcurrentDictionary<string, bool>(System.StringComparer.Ordinal)

    let private log = ResizeArray<StyleDefinition>()

    let internal record (definition: StyleDefinition) =
        if not (names.ContainsKey definition.ClassName) then
            lock gate (fun () ->
                if names.TryAdd (definition.ClassName, true) then
                    log.Add definition)

    /// <summary>The number of distinct classes applied so far.</summary>
    let count () =
        lock gate (fun () -> log.Count)

    /// <summary>The definitions applied after the first <c>count</c>, in order of first application.</summary>
    /// <remarks>A host passes the number of definitions it has taken and receives the rest; <c>after 0</c> is the whole log.</remarks>
    /// <exception cref="T:System.ArgumentOutOfRangeException"><c>count</c> is negative or exceeds the number of classes applied.</exception>
    let after (count: int) : StyleDefinition list =
        lock gate (fun () ->
            if count < 0 || count > log.Count then
                raise (System.ArgumentOutOfRangeException (nameof count, count, "The position is outside the log."))

            [ for index in count .. log.Count - 1 -> log[index] ])
#endif

/// <summary>Template style parts using Ranvier's tracked readers and component disposal.</summary>
module CssView =
    /// <summary>Applies a static generated class name.</summary>
    let className (attrs: AttributeBuilder<'instance>) (definition: StyleDefinition) : Part<'native, 'instance> =
#if !FABLE_COMPILER
        AppliedStyles.record definition
#endif
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
#if !FABLE_COMPILER
        for style in styles do
            AppliedStyles.record style.Definition
#endif

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
