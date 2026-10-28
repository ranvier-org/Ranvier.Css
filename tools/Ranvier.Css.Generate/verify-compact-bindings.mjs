import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { assertCompilerRejections, rejectionProject } from './compiler-rejections.mjs';

const directory = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(directory, '../..');
const arguments_ = process.argv.slice(2);
const metadataOnly = arguments_.includes('--metadata-only');
const manifestOption = arguments_.indexOf('--manifest');
const snapshotOption = arguments_.indexOf('--snapshot');
assert(arguments_.every((argument, index) => argument === '--metadata-only' || argument === '--manifest' || argument === '--snapshot' || index === manifestOption + 1 && manifestOption >= 0 || index === snapshotOption + 1 && snapshotOption >= 0), 'Usage: node verify-compact-bindings.mjs [--metadata-only] [--manifest path] [--snapshot path]');
if (manifestOption >= 0) assert(arguments_[manifestOption + 1] && !arguments_[manifestOption + 1].startsWith('--'), '--manifest requires a path');
if (snapshotOption >= 0) assert(arguments_[snapshotOption + 1] && !arguments_[snapshotOption + 1].startsWith('--'), '--snapshot requires a path');
const manifestPath = manifestOption >= 0 ? path.resolve(arguments_[manifestOption + 1])
  : path.join(root, 'src/Ranvier.Css.Bindings/Generated/Css.manifest.json');
const readJson = async file => JSON.parse(await fs.readFile(file, 'utf8'));
const manifest = await readJson(manifestPath);
const snapshot = await readJson(snapshotOption >= 0 ? path.resolve(arguments_[snapshotOption + 1]) : path.join(directory, 'out/css-model.json'));

function decodeGraph(encoded) {
  assert.equal(typeof encoded, 'string', 'Manifest must retain its encoded grammar');
  let offset = 0;
  const field = () => {
    const colon = encoded.indexOf(':', offset);
    assert(colon >= offset && /^\d+$/.test(encoded.slice(offset, colon)), 'Malformed grammar field length');
    const length = Number(encoded.slice(offset, colon));
    assert(Number.isSafeInteger(length) && colon + 1 + length <= encoded.length, 'Truncated grammar field');
    const value = encoded.slice(colon + 1, colon + 1 + length);
    offset = colon + 1 + length;
    return value;
  };
  const count = Number(field());
  assert(Number.isSafeInteger(count) && count >= 0, 'Invalid grammar node count');
  const nodes = new Map();
  for (let index = 0; index < count; index++) {
    const fields = Array.from({ length: 14 }, field);
    const id = Number(fields[0]);
    assert(Number.isSafeInteger(id) && id >= 0 && !nodes.has(id), 'Duplicate or invalid grammar node identity');
    nodes.set(id, { id, kind: Number(fields[1]), target: Number(fields[3]), children: fields[4] === '' ? [] : fields[4].split(',').map(Number) });
  }
  assert.equal(offset, encoded.length, 'Trailing grammar data');
  for (const node of nodes.values()) {
    assert(Number.isInteger(node.kind) && node.kind >= 0 && node.kind <= 12, 'Unsupported grammar node kind');
    for (const child of node.children) assert(nodes.has(child), 'Missing grammar child');
    if (node.kind === 2) assert(nodes.has(node.target), 'Missing resolved or explicit opaque reference target');
  }
  return nodes;
}

const identity = definition => JSON.stringify([definition.kind, definition.name, definition.scopes ?? [], definition.owner ?? null]);
assert.equal(manifest.schemaVersion, 2, 'Compact manifest schema');
assert.equal(snapshot.features.length, 4882, 'Verification fixture must be the complete current CSS catalogue');
assert.equal(manifest.featureCount, 4882, 'Compact generation lost catalogue definitions');
assert.equal(manifest.definitions.length, snapshot.features.length, 'Every definition must remain explicit');
const sourceIds = snapshot.features.map(identity).sort();
assert.equal(new Set(sourceIds).size, sourceIds.length, 'Snapshot feature identities must be unique');
assert.deepEqual(manifest.definitions.map(identity).sort(), sourceIds, 'Definition identity or scope changed');
const missing = snapshot.features.filter(feature => feature.grammar.status === 'missing').map(identity).sort();
assert.deepEqual(manifest.definitions.filter(definition => definition.grammar === 'missing').map(identity).sort(), missing, 'Prose-only definitions must remain explicitly marked');
const properties = manifest.properties;
assert.equal(properties.length, 822, 'Every current CSS property needs a declaration adapter');
assert.equal(new Set(properties.map(property => property.cssName)).size, properties.length, 'Duplicate property adapter');
assert(Array.isArray(manifest.unresolved) && Array.isArray(manifest.ownerIssues), 'Resolution and authoring issues must remain explicit');
for (const issue of manifest.unresolved) assert(issue.reference && issue.message && issue.owner && issue.target, 'Incomplete unresolved-reference diagnostic');
const nodes = decodeGraph(manifest.encodedGrammar);
assert.equal(nodes.size, manifest.nodeCount, 'Grammar graph coverage');
for (const owner of manifest.owners) {
  assert(nodes.has(owner.nodeId) && Array.isArray(owner.path) && owner.path.length && owner.memberName, 'Invalid semantic owner');
}
const factoryKeys = new Set();
for (const factory of manifest.factories) {
  assert(nodes.has(factory.nodeId) && factory.owner && factory.memberName && factory.kind && factory.argumentType, 'Incomplete primitive factory contract');
  const key = JSON.stringify([factory.owner, factory.memberName]);
  assert(!factoryKeys.has(key), `Duplicate primitive factory: ${key}`);
  factoryKeys.add(key);
}
for (const property of properties) assert(nodes.has(property.nodeId) && property.memberName && property.owner, 'Invalid property adapter');
console.log(`Compact metadata: ${manifest.featureCount} definitions, ${properties.length} properties, ${missing.length} prose-only definitions, ${manifest.unresolved.length} unresolved references.\nManifest: ${manifestPath}`);
if (metadataOnly) process.exit(0);

const output = await fs.mkdtemp(path.join(os.tmpdir(), 'ranvier-compact-check-'));
console.log(`Consumer projects and compiler logs: ${output}`);
const runtimeProject = path.join(root, 'src/Ranvier.Css/Ranvier.Css.fsproj');
const bindingsProject = path.join(root, 'src/Ranvier.Css.Bindings/Ranvier.Css.Bindings.fsproj');
const xml = text => text.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
function project(reference, files, executable = true) {
  return `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>${executable ? '<OutputType>Exe</OutputType>' : ''}<GenerateProgramFile>false</GenerateProgramFile><EnableDefaultCompileItems>false</EnableDefaultCompileItems><WarningsAsErrors>44</WarningsAsErrors></PropertyGroup><ItemGroup><ProjectReference Include="${xml(reference)}"/>${files.map(file => `<Compile Include="${xml(file)}"/>`).join('')}</ItemGroup></Project>`;
}
async function dotnet(name, args, success = true) {
  const result = spawnSync('dotnet', args, { cwd: root, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const log = (result.stdout ?? '') + (result.stderr ?? '');
  await fs.writeFile(path.join(output, `${name}.log`), log);
  if (result.error || result.signal || result.status === null || (result.status === 0) !== success) {
    throw new Error(`${name} failed (exit ${result.status}, signal ${result.signal ?? 'none'}).\n${log}\n${result.error ?? ''}`);
  }
  return log;
}
const smoke = String.raw`module CompactChecks
open System
open Ranvier.Css
open Ranvier.Css.Generated
let equal label expected actual = if expected <> actual then failwithf "%s: expected %A; got %A" label expected actual
let contains label expected (actual: string) = if not (actual.Contains(expected: string)) then failwithf "%s: missing %A in %A" label expected actual
let rejects label action =
    let mutable rejected = false
    try action () with :? ArgumentException -> rejected <- true
    if not rejected then failwith (label + ": expected an argument error")
let css (declaration: Declaration) = Declaration.render declaration
[<EntryPoint>]
let main _ =
    contains "width" "width: 42px;" (css (Css.width(CssValue.px 42.)))
    contains "display block" "display: block;" (css (Css.display CssValue.Display.block))
    contains "display none" "display: none;" (css (Css.display CssValue.Display.none))
    contains "background color" "background-color: red;" (css (Css.backgroundColor CssValue.Color.red))
    equal "value rendering" "42px" (CssValue.render (CssValue.px 42.))
    let choose flag =
        if flag then CssValue.px 42. :> CssValue.LengthPercentage.Value
        else CssValue.percent 50.
    equal "first branch annotation" "42px" (CssValue.render (choose true))
    equal "implicit second branch widening" "50%" (CssValue.render (choose false))
    contains "chosen property category" "width: 50%;" (css (Css.width (choose false)))
    let mixed = CssValue.LengthPercentage.calc (CssValue.percent 100. - CssValue.px 24.)
    equal "percentage arithmetic" "calc(100% - 24px)" (CssValue.render mixed)
    contains "calculated width" "width: calc(100% - 24px);" (css (Css.width mixed))
    equal "integer alias admission" "1" (CssValue.Values.SignedInteger.create (CssValue.integer 1) |> CssValue.render)
    equal "calculated integer alias admission" "calc(1.5 + 1)" (CssValue.Values.SignedInteger.create (CssValue.Integer.calc (CssValue.number 1.5 + CssValue.number 1.)) |> CssValue.render)
    equal "zero constant" "0" (CssValue.render CssValue.zero)
    let numericVariable = CssValue.Length.variableWithFallback "--gap" (CssValue.px 8.)
    equal "numeric variable arithmetic" "calc(var(--gap, 8px) + 2px)" (CssValue.Length.calc (numericVariable + CssValue.px 2.) |> CssValue.render)
    equal "resolution constructor/property collision" "2x" (Css.x 2. |> CssValue.render)
    let border = CssValue.Border.create (Some(CssValue.px 2.)) (Some CssValue.Border.Style.solid) (Some CssValue.Color.red)
    equal "border composition" "2px solid red" (CssValue.render border)
    equal "border omitted slots" "red" (CssValue.render (CssValue.Border.create None None (Some CssValue.Color.red)))
    rejects "empty anyOf" (fun () -> CssValue.Border.create None None None |> ignore)
    let reader = Style.create [Css.width (fun (width: float) -> CssValue.px width)]
    let stableRules = Stylesheet.render reader.Definition.Rules
    contains "reader lowering" "width: var(--r-" stableRules
    contains "first reader value" "42px" (Style.renderVariables 42. reader)
    contains "updated reader value" "84px" (Style.renderVariables 84. reader)
    equal "reader keeps static rules" stableRules (Stylesheet.render reader.Definition.Rules)
    let typedReader = Style.create [Css.width choose]
    contains "category reader" "50%" (Style.renderVariables false typedReader)
    let widthVar : CssValue.Variable<CssValue.Width.Value> = Css.variable "--width"
    let sameVar : CssValue.Variable<CssValue.Width.Value> = CssValue.variable "--width"
    let otherAlias : CssValue.Variable<CssValue.Width.Value> = CssValue.var "--width"
    for variable in [widthVar; sameVar; otherAlias] do contains "typed variable aliases" "width: var(--width);" (css (Css.width variable))
    let fallback = CssValue.Variable.withFallback widthVar (CssValue.px 24.)
    contains "typed variable fallback" "width: var(--width, 24px);" (css (Css.width fallback))
    equal "typed variable assignment" "--width: 24px;" (CssValue.Variable.declaration widthVar (CssValue.px 24.) |> Declaration.render)
    rejects "variable identifier" (fun () -> CssValue.variable<CssValue.Width.Value> "width" |> ignore)
    contains "static CSS-wide" "width: initial;" (css (Css.width CssValue.initial))
    contains "static cascade layer" "width: revert-layer;" (css (Css.width CssValue.revertLayer))
    for value in [Double.NaN; Double.PositiveInfinity; Double.NegativeInfinity] do rejects "nonfinite number" (fun () -> CssValue.px value |> ignore)
    let formFeed = string (char 12)
    let escaped = CssValue.String.create formFeed |> CssValue.render
    contains "string escape" "\c " escaped
    if escaped.Contains formFeed then failwith "Raw form-feed escaped into CSS"
    for identifier in ["-"; "-1"; "inherit"; "revert-rule"] do rejects "custom identifier" (fun () -> CssValue.CustomIdent.create identifier |> ignore)
    rejects "negative animation duration" (fun () -> Css.animationDuration (CssValue.s -1.) |> ignore)
    rejects "empty duration list" (fun () -> CssValue.Animation.Duration.create [] |> ignore)
    printfn "Compact public API runtime checks passed."
    0
`;
await fs.writeFile(path.join(output, 'Consumer.fsproj'), project(bindingsProject, ['Smoke.fs']));
await fs.writeFile(path.join(output, 'Smoke.fs'), smoke);
await dotnet('public-api', ['run', '--project', path.join(output, 'Consumer.fsproj'), '-c', 'Release']);

const rejections = [
  ['WidthAngle', 'Css.width (CssValue.deg 90.)', ['FS0041', 'FS0001', 'FS0193']],
  ['WidthColor', 'Css.width CssValue.Color.red', ['FS0041', 'FS0001', 'FS0193']],
  ['WidthString', 'Css.width "42px"', ['FS0041', 'FS0001']],
  ['WidthVariableAsColor', 'let variable = CssValue.variable<CssValue.Width.Value> "--width" in Css.color variable', ['FS0041', 'FS0001', 'FS0193']],
  ['ColorFallbackForWidth', 'let variable = CssValue.variable<CssValue.Width.Value> "--width" in CssValue.Variable.withFallback variable CssValue.Color.red', ['FS0001', 'FS0193']],
  ['MixedDimensions', 'CssValue.px 1. + CssValue.deg 1.', ['FS0043', 'FS0041']],
  ['AngleCalculationAsWidth', 'Css.width (CssValue.Angle.calc (CssValue.deg 1. + CssValue.deg 2.))', ['FS0041', 'FS0001', 'FS0193']],
  ['LengthSquaredBridge', 'CssValue.Length.calc (CssValue.px 2. * CssValue.px 3.)', ['FS0001', 'FS0193']],
  ['WideReader', 'Css.width (fun (_:unit) -> CssValue.initial)', ['FS0041', 'FS0001', 'FS0193']],
  ['AllWideReader', 'Css.width (fun (_:unit) -> CssValue.All.initial)', ['FS0041', 'FS0001', 'FS0193']],
  ['AngleReaderAsWidth', 'Css.width (fun (_:float) -> CssValue.deg 90.)', ['FS0041', 'FS0001', 'FS0193']],
].map(([name, expression, code]) => ({ name, expression, code }));
const negative = path.join(output, 'negative');
await fs.mkdir(negative);
await fs.writeFile(path.join(negative, 'Negative.fsproj'), rejectionProject(bindingsProject, rejections));
for (const item of rejections) await fs.writeFile(path.join(negative, `${item.name}.fs`), `module ${item.name}\nopen Ranvier.Css\nlet invalid = ${item.expression}\n`);
const rejectionLog = await dotnet('compiler-rejections', ['build', path.join(negative, 'Negative.fsproj'), '-c', 'Release'], false);
assertCompilerRejections(rejections, rejectionLog);
assert(!/error\s+(?:FS0039|FS0010|FS0222|FS0225|MSB\d+|NU\d+)\b/.test(rejectionLog), `Rejection project has an unrelated missing-symbol, syntax, restore or project error.\n${rejectionLog}`);
console.log(`${rejections.length} invalid consumers rejected with their intended type diagnostics.`);

const fixture = path.join(output, 'fixture');
await fs.mkdir(fixture);
const feature = (name, syntax) => ({ name, href: `https://example.test/${name}`, ...(syntax ? { syntax } : {}) });
await fs.writeFile(path.join(fixture, 'input.json'), JSON.stringify({
  properties: [feature('bounded', '<length [0px,100px]>'), feature('pair', '<length>{2,3}'), feature('unordered', 'a || b'), feature('independently-mixed', '<length> | <percentage>')],
  types: ['length', 'percentage', 'string', 'custom-ident'].map(name => feature(name)).concat([feature('length-percentage', '<length> | <percentage>')]), functions: [], selectors: [], atrules: [],
}));
await dotnet('generate-fixture', ['run', '--project', directory, '-c', 'Release', '--', '--runtime', '--strict', '--input', path.join(fixture, 'input.json'), '--bindings', path.join(fixture, 'Css.Generated.fs'), '--output', path.join(fixture, 'model.json')]);
const fixtureManifest = await readJson(path.join(fixture, 'Css.Generated.manifest.json'));
assert.equal(fixtureManifest.schemaVersion, 2, 'Default runtime generation must use the compact schema');
assert.equal(fixtureManifest.featureCount, 9, 'Fixture definitions were lost');
await fs.writeFile(path.join(fixture, 'Fixture.fsproj'), project(runtimeProject, ['Css.Generated.fs', 'Checks.fs']));
const fixtureSource = String.raw`module FixtureChecks
open System
open Ranvier.Css
let rejects label action =
    let mutable rejected = false
    try action () with :? ArgumentException -> rejected <- true
    if not rejected then failwith (label + ": expected an argument error")
let equal expected actual = if expected <> actual then failwithf "Expected %A; got %A" expected actual
[<EntryPoint>]
let main _ =
    equal "42px" (CssValue.Bounded.create (CssValue.px 42.) |> CssValue.render)
    equal "1in" (CssValue.Bounded.create (CssValue.INCH_HELPER 1.) |> CssValue.render)
    for magnitude in [-1.;101.] do rejects "numeric range" (fun () -> CssValue.Bounded.create (CssValue.px magnitude) |> ignore)
    rejects "converted absolute range" (fun () -> CssValue.Bounded.create (CssValue.INCH_HELPER 2.) |> ignore)
    rejects "static sink range" (fun () -> Css.bounded (CssValue.px 101.) |> ignore)
    let reader = Ranvier.Css.Style.create [Css.bounded (fun (value:float) -> CssValue.px value)]
    rejects "reader range" (fun () -> Ranvier.Css.Style.renderVariables 101. reader |> ignore)
    equal "1px 2px" (CssValue.Pair.create [CssValue.px 1.;CssValue.px 2.] |> CssValue.render)
    rejects "repeat minimum" (fun () -> CssValue.Pair.create [CssValue.px 1.] |> ignore)
    rejects "repeat maximum" (fun () -> CssValue.Pair.create [CssValue.px 1.;CssValue.px 2.;CssValue.px 3.;CssValue.px 4.] |> ignore)
    rejects "empty anyOf" (fun () -> CssValue.Unordered.create false false |> ignore)
    equal "a" (CssValue.Unordered.create true false |> CssValue.render)
    equal "a b" (CssValue.Unordered.create true true |> CssValue.render)
    equal "independently-mixed: calc(100% - 24px);" (Css.independentlyMixed (CssValue.LengthPercentage.calc (CssValue.percent 100. - CssValue.px 24.)) |> Declaration.render)
    equal "independently-mixed: 50%;" (Css.independentlyMixed (CssValue.LengthPercentage.percent 50.) |> Declaration.render)
    let boundedVariable : CssValue.Variable<CssValue.Bounded.Value> = CssValue.variable "--bounded"
    let invalidFallback = CssValue.Variable.withFallback boundedVariable (CssValue.px 101.)
    rejects "typed variable fallback range" (fun () -> Css.bounded invalidFallback |> ignore)
    printfn "Compact bounds, cardinality, unordered composition and reactive validation passed."
    0
`;
await fs.writeFile(path.join(fixture, 'Checks.fs'), fixtureSource.replaceAll('INCH_HELPER', '``in``'));
await dotnet('fixture-runtime', ['run', '--project', path.join(fixture, 'Fixture.fsproj'), '-c', 'Release']);
console.log(`Compact bindings verified. Logs and consumer projects: ${output}`);
