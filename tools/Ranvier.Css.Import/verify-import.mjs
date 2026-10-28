import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import postcss from 'postcss';
import { parse as parseValue, generate as valueText, walk } from 'css-tree';
import { generate, run } from './index.mjs';

const directory = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(directory, '../..');
const output = path.join(directory, 'out/verification');
await fs.mkdir(output, { recursive: true });
const manifestPath = path.join(root, 'src/Ranvier.Css.Bindings/Generated/Css.manifest.json');
const manifest = JSON.parse(await fs.readFile(manifestPath, 'utf8'));
assert.equal(manifest.schemaVersion, 2, 'The default binding library uses the compact manifest.');
assert.equal(typeof manifest.encodedGrammar, 'string');
assert.ok(manifest.factories.length > 0);
const css = `@charset "UTF-8";
/*! retained license */
@import url("base;a.css");
@im\\port url("escaped.css");
@import url("second.css");
@layer reset, components;
.card {
  width: 240px;
  width: 50%;
  display: block;
  color: red !important;
  opacity: .5;
  font-family: "A\\n font";
  --wide: calc(100% - 1px);
  width: calc(var(--wide) / 2);
  &:hover { color: blue; }
}
@media (width >= 600px) { .card { width: 320px; } }
@supports (display: grid) { .card { display: grid; } }
@container panel (width > 1px) { .card { opacity: 1; } }
@layer components { .card { color: green; } }
@font-face { font-family: "Example"; src: url("font.woff2"); }
@keyframes fade { from { opacity: 0; } 50% { opacity: .5; } to { opacity: 1; } }
.interleaved { color: red; &:hover { color: blue; } color: green; }
[data-x=";"] { width: 1px; }
.unicode { --🍏: 1px; }
@future x { token { x: y; } tail: z; }
@m\\65 dia screen { .escaped { width: 1px; } }
.bounded { width: -1px; font-weight: 1001; }
`;
const generated = generate(css, manifest, { moduleName: 'ImportedStyles' });
assert.equal(generated.report.typedDeclarations, 14, JSON.stringify(generated.report));
assert.ok(generated.report.fallbacks.some(item => item.kind === 'rule'));
assert.match(generated.source, /Css\.width \(Css\.px 240\.0\)/);
assert.match(generated.source, /Css\.display CssValue\.Display\.block/);
assert.match(generated.source, /Css\.color CssValue\.Color\.red/);
assert.match(generated.source, /Declaration\.create "width" "-1px"/);
assert.match(generated.source, /Declaration\.create "font-weight" "1001"/);
await fs.writeFile(path.join(output, 'Imported.fs'), generated.source);
const bindings = path.join(root, 'src/Ranvier.Css.Bindings/Ranvier.Css.Bindings.fsproj');
const build = path.join(root, 'src/Ranvier.Css.Build/Ranvier.Css.Build.fsproj');
const escapeXml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
await fs.writeFile(path.join(output, 'RoundTrip.fsproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><GenerateProgramFile>false</GenerateProgramFile><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><ProjectReference Include="${escapeXml(bindings)}"/><ProjectReference Include="${escapeXml(build)}"/><Compile Include="Imported.fs"/><Compile Include="Program.fs"/></ItemGroup></Project>`);
await fs.writeFile(path.join(output, 'Program.fs'), `module ImportRoundTrip
open System.IO
open Ranvier.Css
open Ranvier.Css.Build
[<EntryPoint>]
let main args =
    let css = Stylesheet.render ImportedStyles.rules
    File.WriteAllText(args[0], css)
    let resource = { Name = "imported"; Dependencies = []; Rules = ImportedStyles.rules; Styles = [] }
    match Compiler.compile { Options.defaults with Mode = OutputMode.PerModule } [resource] with
    | Error errors -> failwith (String.concat "\\n" errors)
    | Ok compiled ->
        if compiled.Assets.Head.Content <> css then failwith "Asset compiler changed imported CSS."
    0
`);
const result = spawnSync('dotnet', ['run', '--project', path.join(output, 'RoundTrip.fsproj'), '-c', 'Release', '--', path.join(output, 'roundtrip.css')], { cwd: root, encoding: 'utf8', maxBuffer: 1024 * 1024 });
if (result.status !== 0 || result.error) throw new Error((result.stdout ?? '') + (result.stderr ?? '') + String(result.error ?? ''));

function normalized(source) {
  const normalize = node => {
    if (node.type === 'comment') return { type: 'comment', text: node.text };
    if (node.type === 'decl') {
      let value = node.raws.value?.raw ?? node.value;
      try {
        const tree = parseValue(value, { context: 'value' });
        walk(tree, token => {
          if (['Number', 'Percentage', 'Dimension'].includes(token.type) && Number.isFinite(Number(token.value))) token.value = String(Number(token.value));
        });
        value = valueText(tree);
      } catch { /* Unknown value syntax stays lexical. */ }
      return { type: 'decl', name: node.prop, value, important: Boolean(node.important) };
    }
    return { type: node.type, selector: node.selector, name: node.name, params: node.params, children: node.nodes?.map(normalize) };
  };
  return normalize(postcss.parse(source));
}
const restored = await fs.readFile(path.join(output, 'roundtrip.css'), 'utf8');
assert.deepEqual(normalized(restored), normalized(css));

const custom = path.join(output, 'custom');
await fs.mkdir(custom, { recursive: true });
await fs.writeFile(path.join(custom, 'input.json'), JSON.stringify({ properties: [{ name: 'width', href: 'https://example.test/width', syntax: '<length [0,100]>' }], types: [{ name: 'length', href: 'https://example.test/length' }], functions: [], selectors: [], atrules: [] }));
const generator = path.join(root, 'tools/Ranvier.Css.Generate/bin/Release/net10.0/Ranvier.Css.Generate.dll');
const generatedCustom = spawnSync('dotnet', [generator, '--input', path.join(custom, 'input.json'), '--output', path.join(custom, 'model.json'), '--bindings', path.join(custom, 'Bindings.fs'), '--namespace', 'Example.Generated', '--runtime'], { cwd: root, encoding: 'utf8' });
if (generatedCustom.status !== 0) throw new Error(generatedCustom.stdout + generatedCustom.stderr);
const customManifest = JSON.parse(await fs.readFile(path.join(custom, 'Bindings.manifest.json'), 'utf8'));
assert.equal(customManifest.schemaVersion, 2);
const guardedCustom = generate('.custom { width: -1px; width: 101px; width: 12px; }', customManifest);
assert.equal(guardedCustom.report.typedDeclarations, 1);
assert.equal(guardedCustom.report.rawDeclarations, 2);
await fs.writeFile(path.join(custom, 'Imported.fs'), generate('.custom { width: 12px; }', customManifest, { moduleName: 'CustomImported', strictTyped: true }).source);
const core = path.join(root, 'src/Ranvier.Css/Ranvier.Css.fsproj');
await fs.writeFile(path.join(custom, 'Checks.fsproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><GenerateProgramFile>false</GenerateProgramFile><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><ProjectReference Include="${escapeXml(core)}"/><Compile Include="Bindings.fs"/><Compile Include="Imported.fs"/><Compile Include="Program.fs"/></ItemGroup></Project>`);
await fs.writeFile(path.join(custom, 'Program.fs'), 'module CustomChecks\n[<EntryPoint>]\nlet main _ =\n    let css = Ranvier.Css.Stylesheet.render CustomImported.rules\n    if css <> ".custom { width: 12px; }\\n" then failwith css\n    0\n');
const checkedCustom = spawnSync('dotnet', ['run', '--project', path.join(custom, 'Checks.fsproj'), '-c', 'Release'], { cwd: root, encoding: 'utf8' });
if (checkedCustom.status !== 0) throw new Error(checkedCustom.stdout + checkedCustom.stderr);

const input = path.join(output, 'input.css');
const target = path.join(output, 'Cli.fs');
const report = path.join(output, 'Cli.report.json');
await fs.writeFile(input, '.card { width: 42px; }');
await run(['--input', input, '--output', target, '--module', 'CliStyles', '--strict-typed']);
assert.match(await fs.readFile(target, 'utf8'), /Css\.width/);
assert.equal(JSON.parse(await fs.readFile(report, 'utf8')).fallbacks.length, 0);
await fs.writeFile(target, 'preserved-output');
await fs.writeFile(report, 'preserved-report');
await fs.writeFile(input, '.card { width: calc(1px * 2); }');
await assert.rejects(run(['--input', input, '--output', target, '--strict-typed']), /Strict typed/);
await fs.writeFile(input, '.card { width: 1px;');
await assert.rejects(run(['--input', input, '--output', target]), /Unclosed/i);
assert.equal(await fs.readFile(target, 'utf8'), 'preserved-output');
assert.equal(await fs.readFile(report, 'utf8'), 'preserved-report');
await assert.rejects(run(['--input', input, '--output', input]), /distinct paths/);
console.log('Compiled compact default/custom namespace F#, equivalent CSS AST/asset round trip, strict coverage, graph numeric guards, and CLI output preservation pass.');
