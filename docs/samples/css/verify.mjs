import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const output = path.join(root, 'docs/samples/css/out');
fs.mkdirSync(output, { recursive: true });
const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;');
const pages = ['css', 'css-utilities'];
for (const page of pages) {
  const markdown = fs.readFileSync(path.join(root, `docs/content/view/${page}.md`), 'utf8');
  const snippets = [...markdown.matchAll(/^```fsharp\r?\n([\s\S]*?)^```/gm)].map(match => match[1]);
  assert.ok(snippets.length > 0, `${page} must contain F# examples`);
  fs.writeFileSync(path.join(output, `${page}.fs`), `module ${page.replaceAll('-', '_')}\n\n${snippets.join('\n')}`);
}
const project = path.join(output, 'CssDocs.fsproj');
fs.writeFileSync(project, `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsPackable>false</IsPackable></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="${xml(path.join(root, 'src/Ranvier.Css.View/Ranvier.Css.View.fsproj'))}" />
    <Reference Include="Ranvier.Css.Bindings"><HintPath>${xml(path.join(root, 'src/Ranvier.Css.Bindings/bin/Release/net10.0/Ranvier.Css.Bindings.dll'))}</HintPath></Reference>
    ${pages.map(page => `<Compile Include="${page}.fs" />`).join('\n    ')}
  </ItemGroup>
</Project>
`);
function run(args) {
  const result = spawnSync('dotnet', args, { cwd: root, stdio: 'inherit' });
  if (result.error) throw result.error;
  assert.equal(result.status, 0, `Documentation check failed: dotnet ${args.join(' ')}`);
}
run(['build', project, '-c', 'Release', '--nologo']);
run([path.join(root, 'tools/Ranvier.Css.Compile/bin/Release/net10.0/Ranvier.Css.Compile.dll'),
  '--project', project, '--output', path.join(output, 'assets'), '--single', 'docs.css']);
const manifest = JSON.parse(fs.readFileSync(path.join(output, 'assets/css-manifest.json'), 'utf8'));
assert.ok(manifest, 'The documented component must extract successfully');
const css = fs.readdirSync(path.join(output, 'assets')).filter(file => file.endsWith('.css'))
  .map(file => fs.readFileSync(path.join(output, 'assets', file), 'utf8')).join('\n');
assert.match(css, /font-size:/, 'The responsive typography must be emitted');
assert.match(css, /opacity:/, 'The application condition must be emitted');
assert.match(css, /var\(--r-/, 'The reactive value must have a static variable reference');
assert.match(css, /max-width:\s*calc\(100% - var\(--card-gap,\s*1rem\)\)/,
  'The evaluation example must preserve its calculation and typed variable fallback');
console.log('Typed CSS guide examples compile and the documented component extracts static CSS.');
