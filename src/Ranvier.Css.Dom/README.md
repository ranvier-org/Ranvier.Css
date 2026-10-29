# Typed CSS on shared DOM styles

`Ranvier.Css.Dom` applies existing `Ranvier.Css` declarations to the shared
`Ranvier.Dom.CSSStyleDeclaration` interface. The same functions work with a browser
declaration or a native WebCore wrapper. Install `Ranvier.Css.Bindings` for generated
property and value helpers; use matching `.Traced` packages for traced builds.
Browser projects install `Fable.Ranvier.Css.Dom`, which compiles the identical adapter
source against `Fable.Ranvier.Dom`. This keeps each target's shared DOM assembly
consistent throughout its dependency graph.

```fsharp
open Ranvier.Css
open Ranvier.Css.Dom

let original = StyleDeclaration.capture element.style
let width = Css.width (Css.px 42.) |> Declaration.important
StyleDeclaration.apply element.style width
let value, priority = StyleDeclaration.read element.style width
StyleDeclaration.restore element.style original
```

`applyMany` applies declarations in sequence order. Engines validate property values
and report writes to read-only computed styles through their existing DOM errors.
Grammar compatibility remains the responsibility of the existing typed constructors;
explicit unchecked CSS declarations retain their documented unchecked semantics.

Snapshots capture the complete engine-serialized declaration. Restoring one replaces
the entire declaration, including changes made since capture, property absence,
importance and shorthand ordering. Snapshots own no engine handles. Each operation
uses the destination declaration's existing lifetime and thread requirements.

`read` returns engine-serialized text and priority. A computed value does not acquire
an authored grammar type merely because a typed declaration selected its property.
