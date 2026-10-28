import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { assertCompilerRejections, rejectionProject } from './compiler-rejections.mjs';

const directory = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(directory, '../..');
const output = path.join(directory, 'out/checks');
await fs.mkdir(output, { recursive: true });
function dotnet(args, expected = true) {
  const result = spawnSync('dotnet', args, { cwd: root, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 });
  const log = (result.stdout ?? '') + (result.stderr ?? '');
  if ((result.status === 0) !== expected || result.error) throw new Error(log || String(result.error));
  return log;
}
dotnet(['run', '--project', directory, '-c', 'Release', '--', '--specs-dir', path.join(directory, 'out/specs'),
  '--bindings', path.join(output, 'Css.Generated.fs'), '--output', path.join(output, 'model.json'), '--runtime', '--strict']);
const generated = await fs.readFile(path.join(output, 'Css.Generated.fs'), 'utf8');
const committed = await fs.readFile(path.join(root, 'src/Ranvier.Css.Bindings/Generated/Css.fs'), 'utf8');
if (generated.replaceAll('\r\n', '\n') !== committed.replaceAll('\r\n', '\n')) throw new Error('Generated CSS bindings are stale; regenerate the library source with --runtime.');
const manifestText = await fs.readFile(path.join(output, 'Css.Generated.manifest.json'), 'utf8');
const committedManifest = await fs.readFile(path.join(root, 'src/Ranvier.Css.Bindings/Generated/Css.manifest.json'), 'utf8');
if (manifestText.replaceAll('\r\n', '\n') !== committedManifest.replaceAll('\r\n', '\n')) throw new Error('Generated CSS binding manifest is stale.');
const corpusManifest = JSON.parse(manifestText);
if (corpusManifest.schemaVersion === 2) {
  process.argv.push('--snapshot', path.join(output, 'model.json'));
  await import('./verify-compact-bindings.mjs');
  process.exit(0);
}
const propertyCount = corpusManifest.definitions.filter(item => item.kind === 'Property').length;
if ((generated.match(/let reactive \(/g) ?? []).length !== propertyCount) throw new Error('Reactive helpers do not cover every property.');
if (!corpusManifest.cssWideKeywords.includes('revert-rule') || !generated.includes('globalRevertRule')) throw new Error('Latest CSS-wide keywords are missing.');
const runtimeProject = path.join(root, 'src/Ranvier.Css/Ranvier.Css.fsproj');
const bindingsProject = path.join(root, 'src/Ranvier.Css.Bindings/Ranvier.Css.Bindings.fsproj');
const project = `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><GenerateProgramFile>false</GenerateProgramFile><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>3390</NoWarn></PropertyGroup><ItemGroup><ProjectReference Include="${runtimeProject}"/><Compile Include="Css.Generated.fs"/><Compile Include="Smoke.fs"/></ItemGroup></Project>`;
const corpusProject = `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><GenerateProgramFile>false</GenerateProgramFile><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><ProjectReference Include="${bindingsProject}"/><Compile Include="Smoke.fs"/></ItemGroup></Project>`;
await fs.writeFile(path.join(output, 'Checks.fsproj'), corpusProject);
await fs.writeFile(path.join(output, 'Smoke.fs'), `module Checks
open Ranvier.Css.Generated
module Short =
    open Ranvier.Css
    let verify () =
        if Value.render Css.red <> "red" then failwith "Keyword canonical storage."
        let chooseWidth flag =
            if flag then Css.px 42.0 :> CssValueCategories.IPropertyWidth
            else Css.percent 50.0
        if Declaration.render (Css.width (chooseWidth true)) <> "width: 42px;" then failwith "First branch interface widening."
        if Declaration.render (Css.width (chooseWidth false)) <> "width: 50%;" then failwith "Implicit second branch coercion."
        let chooseNumeric flag =
            if flag then Css.px 42.0 :> CssValueCategories.ILengthPercentage
            else Css.percent 50.0
        if Declaration.render (Css.width (chooseNumeric false)) <> "width: 50%;" then failwith "Shared length percentage category."
        let chooseKeyword flag =
            if flag then Css.px 42.0 :> CssValueCategories.IPropertyWidth
            else Css.auto
        if Declaration.render (Css.width (chooseKeyword false)) <> "width: auto;" then failwith "Mixed numeric and keyword branch."
        let conditionalStyle = styled { Css.width chooseWidth }
        if not ((Style.renderVariables false conditionalStyle).Contains("50%")) then failwith "Interface reader tracks the chosen branch."
        if Declaration.render (Css.width (Css.px 42.0)) <> "width: 42px;" then failwith "Short typed width."
        if Declaration.render (Css.width (Css.percent 50.0)) <> "width: 50%;" then failwith "Short percentage."
        if Declaration.render (Css.width Css.auto) <> "width: auto;" then failwith "Short keyword."
        if Declaration.render (Css.display Css.block) <> "display: block;" then failwith "Short display keyword."
        if Declaration.render (Css.color Css.red) <> "color: red;" then failwith "Short color keyword."
        let expression = Css.rem 2.0 + Css.px 8.0
        if Declaration.render (Css.width expression) <> "width: calc(2rem + 8px);" then failwith "Unified arithmetic."
        if Declaration.render (Css.width (Css.percent 100.0 - Css.rem 2.0)) <> "width: calc(100% - 2rem);" then failwith "Mixed percentage arithmetic."
        if Declaration.render (Css.width (Css.px 2.0 * Css.px 3.0 / Css.px 4.0)) <> "width: calc(2px * 3px / 4px);" then failwith "Compound dimension cancellation."
        if Declaration.render (Css.width (Css.calc(Css.px 2.0 * Css.px 3.0) / Css.px 4.0)) <> "width: calc(calc(2px * 3px) / 4px);" then failwith "Nested calc grouping."
        if Declaration.render (Css.opacity (Css.px 2.0 / Css.px 4.0)) <> "opacity: calc(2px / 4px);" then failwith "Unitless ratio."
        if Declaration.render (Css.zIndex (Css.number 1.5 + Css.number 1.0)) <> "z-index: calc(1.5 + 1);" then failwith "Integer destination defers rounding."
        if Declaration.render (Css.zIndex (Css.integer 3)) <> "z-index: 3;" then failwith "Typed integer helper."
        if Declaration.render (Css.rotate (Css.angle (Css.deg 30.0 + Css.deg 15.0))) <> "rotate: calc(30deg + 15deg);" then failwith "Canonical angle expression bridge."
        if Declaration.render (Css.zIndex (Css.integer (Css.number 1.5 + Css.number 1.0))) <> "z-index: calc(1.5 + 1);" then failwith "Canonical integer expression bridge."
        if Declaration.render (Css.width (Css.min [Css.px 42.0; Css.px 80.0])) <> "width: min(42px, 80px);" then failwith "Typed min."
        if Declaration.render (Css.width (Css.min(Css.percent 100.0, Css.rem 60.0))) <> "width: min(100%, 60rem);" then failwith "Mixed min."
        let mixedList = [Css.lengthPercentage (Css.percent 100.0); Css.lengthPercentage (Css.rem 60.0)]
        if Declaration.render (Css.width (Css.min mixedList)) <> "width: min(100%, 60rem);" then failwith "Mixed min list."
        if Declaration.render (Css.width (Css.clamp(Css.px 10.0, Css.px 42.0, Css.px 80.0))) <> "width: clamp(10px, 42px, 80px);" then failwith "Typed clamp."
        if Declaration.render (Css.width (Css.clamp(Css.px 42.0))) <> "width: clamp(none, 42px, none);" then failwith "Open clamp bounds."
        if Declaration.render (Css.width (Css.clamp(Css.px 42.0, Some(Css.px 10.0), None))) <> "width: clamp(10px, 42px, none);" then failwith "Inferred optional bounds."
        if Declaration.render (Css.opacity (Css.sin Css.pi)) <> "opacity: sin(pi);" then failwith "Calculation constant."
        if Declaration.render (Css.opacity (Css.sin (Css.deg 90.0))) <> "opacity: sin(90deg);" then failwith "Angle trig overload."
        if Declaration.render (Css.width (Css.round(Css.px 42.0, Css.px 5.0))) <> "width: round(nearest, 42px, 5px);" then failwith "Typed round."
        if Declaration.render (Css.opacity (Css.progress(Css.px 42.0, Css.px 0.0, Css.px 100.0))) <> "opacity: progress(42px, 0px, 100px);" then failwith "Progress."
        if Declaration.render (Css.width (Css.random(Css.px 2.0, Css.px 8.0))) <> "width: random(2px, 8px);" then failwith "Random."
        if Declaration.render (Css.width (Css.calcMix [Css.px 10.0, Some 25.0; Css.px 20.0, Some 75.0])) <> "width: calc-mix(10px 25%, 20px 75%);" then failwith "Weighted numeric mix."
        if Declaration.render (Css.zIndex (Css.siblingIndex())) <> "z-index: sibling-index();" then failwith "Sibling index."
        let gap = Css.lengthVariable "--gap"
        if Declaration.render (Css.width (Css.variable(gap, fallback=Css.px 8.0) + Css.rem 1.0)) <> "width: calc(var(--gap, 8px) + 1rem);" then failwith "Typed numeric variable."
        let intrinsic = Css.calcSize(Css.intrinsicKeyword "auto", Css.intrinsicSize + Css.px 16.0)
        if Declaration.render (Css.width intrinsic) <> "width: calc-size(auto, size + 16px);" then failwith "Intrinsic sizing."
        let fit = Css.calcSize(Css.intrinsicFitContent(Css.px 100.0), Css.intrinsicSize)
        if Declaration.render (Css.width fit) <> "width: calc-size(fit-content(100px), size);" then failwith "Inline function basis admission."
        try
            Css.maxWidth (Css.calcSize(Css.intrinsicKeyword "none", Css.intrinsicSize)) |> ignore
            failwith "Intrinsic none must be rejected."
        with :? System.ArgumentException -> ()
        let stops = [Css.interpolationStop(Css.interpolationPosition(Css.percent 0.0), Css.px 100.0); Css.interpolationStop(Css.interpolationPosition(Css.percent 100.0), Css.px 200.0)]
        let interpolation = Css.calcInterpolate(Css.interpolationProgress(Css.percent 50.0), stops)
        if not ((Declaration.render (Css.width interpolation)).Contains("calc-interpolate(")) then failwith "Structured interpolation."
        let calculated = styled { Css.width (fun (value:float) -> Css.px value + Css.rem 2.0) }
        if not ((Style.renderVariables 84.0 calculated).Contains("calc(84px + 2rem)")) then failwith "Calculated reactive reader."
        let literal = Css.px 42.0
        let sameMarker (value: Value<'syntax>) : Value<'syntax> = global.Ranvier.Css.Generated.copy value
        let copied = sameMarker literal
        if not (literal.Equals copied && copied.Equals literal && literal.GetHashCode() = copied.GetHashCode()) then failwith "Canonical value equality."
        let style = styled { Css.width (fun (value:float) -> Css.px value) }
        if not ((Style.renderVariables 84.0 style).Contains("84px")) then failwith "Short reader."
[<EntryPoint>]
let main _ =
    Short.verify ()
    let width = Css.TypeLength.px 42.0 |> Css.TypeLengthPercentage.fromLength |> Css.TypeBoxSize.fromLengthPercentage |> Css.PropertyWidth.fromBoxSize |> Css.PropertyWidth.declaration
    if Css.renderDeclaration width <> "width: 42px;" then failwith "Width serialization failed."
    let widthValue px = Css.TypeLength.px px |> Css.TypeLengthPercentage.fromLength |> Css.TypeBoxSize.fromLengthPercentage |> Css.PropertyWidth.fromBoxSize
    let reactive = Ranvier.Css.Style.create [Css.PropertyWidth.reactive widthValue]
    let rules = Ranvier.Css.Stylesheet.render reactive.Definition.Rules
    if not (rules.Contains("width: var(--r-")) then failwith "Reactive lowering failed."
    if not ((Ranvier.Css.Style.renderVariables 42.0 reactive).Contains("42px")) then failwith "Reactive reader failed."
    if not ((Ranvier.Css.Style.renderVariables 84.0 reactive).Contains("84px")) then failwith "Reactive update failed."
    if Ranvier.Css.Stylesheet.render reactive.Definition.Rules <> rules then failwith "Reactive styles changed static rules."
    0
`);
console.log(dotnet(['run', '--project', path.join(output, 'Checks.fsproj'), '-c', 'Release']));
console.log('Complete corpus bindings compile and serialize width correctly.');
const consumer = path.join(output, 'consumer');
await fs.mkdir(consumer, { recursive: true });
await fs.writeFile(path.join(consumer, 'Consumer.fsproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><WarningsAsErrors>44</WarningsAsErrors></PropertyGroup><ItemGroup><ProjectReference Include="${bindingsProject}"/><Compile Include="Consumer.fs"/></ItemGroup></Project>`);
await fs.writeFile(path.join(consumer, 'Consumer.fs'), `module Consumer\nopen Ranvier.Css\nlet width = Css.width (Css.px 42.0)\nlet reactive = Css.width (fun (value:float) -> Css.px value)\n`);
dotnet(['build', path.join(consumer, 'Consumer.fsproj'), '-c', 'Release']);
const manifest = JSON.parse(await fs.readFile(path.join(output, 'Css.Generated.manifest.json'), 'utf8'));
const obsolete = manifest.definitions.find(item => item.obsolete && item.moduleName.includes('_Obsolete_'));
if (!obsolete) throw new Error('No archived definition generated.');
const rejectionCases = [
  { name: 'AngleAsWidth', namespace: 'Ranvier.Css.Generated', expression: 'Css.TypeAngle.deg 90.0 |> Css.PropertyWidth.fromBoxSize', code: 'FS0001' },
  ...[
    ['WidthAngle', 'Css.width (Css.deg 90.0)'],
    ['WidthBlock', 'Css.width Css.block'],
    ['WidthString', 'Css.width "42px"'],
    ['ReactiveWidthAngle', 'Css.width (fun (_:float) -> Css.deg 90.0)'],
    ['ReactiveWidthGlobal', 'Css.width (fun (_:float) -> Css.initial)'],
    ['WidthAngleSum', 'Css.width (Css.deg 10.0 + Css.deg 5.0)'],
    ['WidthLengthProduct', 'Css.width (Css.px 1.0 * Css.px 2.0)'],
    ['WidthNumberSum', 'Css.width (Css.number 0.0 + Css.number 1.0)'],
    ['WidthNumberPercentageSum', 'Css.width (Css.number 1.0 + Css.percent 50.0)'],
  ].map(([name, expression]) => ({ name, expression, code: 'FS0041' })),
  ...[
    ['LengthAngleSum', 'Css.px 1.0 + Css.deg 1.0', 'FS0043'],
    ['SineLength', 'Css.sin (Css.px 1.0)', 'FS0041'],
    ['PowerLength', 'Css.pow(Css.px 1.0, Css.px 2.0)', 'FS0041'],
    ['WidthIntrinsic', 'Css.width Css.intrinsicSize', 'FS0041'],
    ['PaddingIntrinsic', 'Css.padding (Css.calcSize(Css.intrinsicKeyword "auto", Css.intrinsicSize))', 'FS0041'],
    ['ProgressIntrinsic', 'Css.interpolationProgress Css.intrinsicSize', 'FS0041'],
    ['IntrinsicAngleRatio', 'Css.intrinsic (Css.anglePercent 10.0 / Css.anglePercent 5.0)', 'FS0041'],
    ['VariableAngleFallback', 'Css.variable(Css.lengthVariable "--gap", fallback=Css.deg 90.0)', 'FS0041'],
    ['AngleLengthSum', 'Css.angle (Css.px 1.0 + Css.px 2.0)', 'FS0193'],
    ['LengthIntrinsic', 'Css.length Css.intrinsicSize', 'FS0193'],
    ['NumberPercentageRatio', 'Css.number (Css.percent 10.0 / Css.percent 5.0)', 'FS0041'],
  ].map(([name, expression, code]) => ({ name, expression, code })),
  { name: 'ArchivedApi', namespace: 'Ranvier.Css.Generated', expression: `Css.${obsolete.moduleName}.specUrl`, code: 'FS0044' },
];
const negatives = path.join(consumer, 'negative');
await fs.mkdir(negatives, { recursive: true });
await fs.writeFile(path.join(negatives, 'Negative.fsproj'), rejectionProject(bindingsProject, rejectionCases));
for (const item of rejectionCases) {
  await fs.writeFile(path.join(negatives, `${item.name}.fs`), `module ${item.name}\nopen ${item.namespace ?? 'Ranvier.Css'}\nlet invalid = ${item.expression}\n`);
}
const rejectionLog = dotnet(['build', path.join(negatives, 'Negative.fsproj'), '-c', 'Release'], false);
await fs.writeFile(path.join(output, 'compiler-rejections.log'), rejectionLog);
assertCompilerRejections(rejectionCases, rejectionLog);
console.log(`All ${rejectionCases.length} invalid consumers report their expected compiler diagnostics in one build.`);
const fixture = path.join(output, 'fixture');
await fs.mkdir(fixture, { recursive: true });
const feature = (name, syntax) => ({ name, href: `https://example.test/${name}`, ...(syntax ? { syntax } : {}) });
await fs.writeFile(path.join(fixture, 'input.json'), JSON.stringify({ properties: [feature('bounded', '<length [0,100]>'), feature('pair', '<length>{2,3}'), feature('unordered', 'a || b')],
  types: ['length', 'angle', 'string', 'custom-ident', 'ident-token'].map(name => feature(name)), functions: [], selectors: [], atrules: [] }));
dotnet(['run', '--project', directory, '-c', 'Release', '--no-build', '--', '--runtime', '--input', path.join(fixture, 'input.json'), '--bindings', path.join(fixture, 'Css.Generated.fs'), '--output', path.join(fixture, 'model.json')]);
const small = JSON.parse(await fs.readFile(path.join(fixture, 'Css.Generated.manifest.json'), 'utf8'));
const def = name => small.definitions.find(item => item.name === name);
await fs.writeFile(path.join(fixture, 'Checks.fsproj'), project);
await fs.writeFile(path.join(fixture, 'Smoke.fs'), `module Checks
open Ranvier.Css
open Ranvier.Css.Generated
let rejects action =
    try action (); failwith "Expected invalid argument."
    with :? System.ArgumentException -> ()
[<EntryPoint>]
let main _ =
    let length = Css.TypeLength.px 42.0
    if (length |> Css.PropertyBounded.create |> Css.PropertyBounded.declaration |> Css.renderDeclaration) <> "bounded: 42px;" then failwith "Bounded render."
    rejects (fun () -> Css.TypeLength.px -1.0 |> Css.PropertyBounded.create |> ignore)
    rejects (fun () -> Css.TypeLength.px 101.0 |> Css.PropertyBounded.create |> ignore)
    if (global.Ranvier.Css.Css.bounded (global.Ranvier.Css.Css.px 42.0) |> Css.renderDeclaration) <> "bounded: 42px;" then failwith "Short bounded render."
    rejects (fun () -> global.Ranvier.Css.Css.bounded (global.Ranvier.Css.Css.px -1.0) |> ignore)
    rejects (fun () -> global.Ranvier.Css.Css.bounded (global.Ranvier.Css.Css.px 101.0) |> ignore)
    let dynamicBound = global.Ranvier.Css.Style.create [global.Ranvier.Css.Css.bounded (fun (n:float) -> global.Ranvier.Css.Css.px n)]
    rejects (fun () -> global.Ranvier.Css.Style.renderVariables 101.0 dynamicBound |> ignore)
    let pairItem = Css.Syntax.${def('pair').children[0].syntaxFactory} length
    rejects (fun () -> Css.PropertyPair.create [pairItem] |> ignore)
    if (Css.PropertyPair.create [pairItem; pairItem] |> Css.renderValue) <> "42px 42px" then failwith "Repeat render."
    rejects (fun () -> Css.PropertyUnordered.create None None |> ignore)
    if (Css.PropertyUnordered.create (Some Css.Syntax.${def('unordered').children[0].syntaxFactory}) None |> Css.renderValue) <> "a" then failwith "Unordered render."
    for text in ["-"; "-1"; "inherit"; "revert-rule"] do rejects (fun () -> Css.TypeCustomIdent.create text |> ignore)
    if (Css.TypeIdentToken.create "initial" |> Css.renderValue) <> "initial" then failwith "Lexical identifier."
    let formFeed = string (char 12)
    let escaped = Css.TypeString.create formFeed |> Css.renderValue
    if escaped.Contains(formFeed) || not (escaped.Contains("\\c ")) then failwith "Form feed escaping."
    0
`);
console.log(dotnet(['run', '--project', path.join(fixture, 'Checks.fsproj'), '-c', 'Release']));
console.log('Type rejection, obsolete warnings, numeric ranges, cardinality, unordered groups, identifiers, and string escaping pass.');
