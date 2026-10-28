# Static CSS asset compilation

Declare application-owned `CssModule` records containing a name, ordered rules, component `StyleDefinition`s and explicit dependencies. Pass the entry modules to `Compiler.compile`; call `Compiler.write` with its result and an output directory.

```fsharp
let result =
    Compiler.compile
        { Options.defaults with Mode = OutputMode.SingleFile "app.css" }
        entryModules
```

The modes are `PerModule` (separate regular CSS files), `CssModules` (separate `.module.css` entries with exported class aliases), and `SingleFile name` (lifted regular CSS). Options also control the public URL prefix and content hashes. The result contains ordered asset URLs, class mappings, module order and a JSON manifest. Imports in browser HTML use that asset order.

Dependencies precede their consumers and shared modules are emitted once. Conflicting definitions using the same resource name fail. Adjacent equal component-style resources are safely deduplicated; separated repetitions are retained because removing them can change the cascade. Class-content conflicts and portable filename collisions fail before emission. Files are replaced atomically per file; stale/unrelated files are preserved. The writer accepts only output filenames within the requested directory.

The compiler preserves stylesheet preambles. A late import cannot be moved ahead of rules without changing cascade order, and distinct namespace environments cannot share a lifted stylesheet. These cases return diagnostics directing the caller to per-module output. Nested imports/namespaces/charsets and nonleading/non-UTF-8 charsets are also diagnosed. The compiler does not fetch and inline arbitrary external stylesheets.

The [CSS importer](../../tools/Ranvier.Css.Import/README.md) can create this tree from existing stylesheets. `Rule.unsafeFromText` preserves complete opaque rules when a parser encounters contents that the structured tree cannot represent. Their contents/preambles require caller validation. Regular per-module output and single-resource single-file output retain them; CSS Modules rewriting and lifting across multiple resources reject opaque blocks explicitly. Standalone comment rules preserve license text and remain part of valid preambles.

## CSS Modules

Each generated class is global and deterministic. A module also exports a local alias composing that global class, so an ordinary CSS Modules importer receives a usable class mapping while SSR can use the stable generated class directly. The manifest's class keys are the original generated class names; JavaScript can use bracket lookup.

The acceptance pipeline uses Lightning CSS with:

```javascript
transform({
  filename,
  code,
  cssModules: { animation: false, grid: false, customIdents: false }
})
```

Keep those shared identifiers global when consuming the explicit dependency graph. Enable local identifier rewriting only when the application also supplies the corresponding cross-module mapping. Ordinary modular CSS and lifted CSS need no CSS Modules processing. `verify-assets.mjs` runs all three modes through the actual parser and checks exported global compositions, SSR links and initial instance variables.

## Extraction

The [example](../../examples/Ranvier.Css.Example/README.md) is an explicit F# extraction entry point. Its style definitions are ordinary application code; the extractor sees their declared static resource graph and never evaluates dynamic readers to discover possible signal values. Use the same pattern in an application's build entry point. This avoids a source compiler transform or browser rule-injection runtime.
