import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// Installs the packed Ranvier.Css.Compile tool from a package folder into a scratch tool path, then runs it
// against the repository example and against a project that takes Ranvier.Css from packages.
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const packages = resolve(process.argv[2] || join(root, 'bin'));
const temporaryRoot = realpathSync(tmpdir());
const temporary = mkdtempSync(join(temporaryRoot, 'ranvier-css-tool-'));
const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
const version = path => {
  const match = readFileSync(join(root, path), 'utf8').match(/<Version>([^<]+)<\/Version>/);
  if (!match) throw new Error(`Missing package version in ${path}`);
  return match[1];
};
const dotnet = (args, cwd = root) => execFileSync('dotnet', args, { cwd, stdio:'inherit' });
const toolVersion = version('tools/Ranvier.Css.Compile/Ranvier.Css.Compile.fsproj');
try {
  const config = join(temporary, 'NuGet.Config');
  writeFileSync(config, `<configuration>
  <packageSources><clear/><add key="cohort" value="${xml(packages)}"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><clear/>
    <packageSource key="cohort"><package pattern="Ranvier.Css*"/></packageSource>
    <packageSource key="nuget.org"><package pattern="*"/></packageSource>
  </packageSourceMapping>
</configuration>`);
  const toolPath = join(temporary, 'tool');
  dotnet(['tool', 'install', 'Ranvier.Css.Compile', '--version', toolVersion, '--tool-path', toolPath, '--configfile', config], temporary);
  const tool = join(toolPath, process.platform === 'win32' ? 'ranvier-css-compile.exe' : 'ranvier-css-compile');
  const extract = (project, output) => {
    execFileSync(tool, ['--project', project, '--output', output, '--single', 'app.css', '--no-content-hash', '--public-path', '.'],
      { cwd:temporary, stdio:'inherit' });
    JSON.parse(readFileSync(join(output, 'css-manifest.json'), 'utf8'));
    return readFileSync(join(output, 'app.css'), 'utf8');
  };

  const example = join(root, 'examples/Ranvier.Css.Utilities.Example/Ranvier.Css.Utilities.Example.fsproj');
  dotnet(['build', example, '-c', 'Release', '-m:1', '--nologo', '-v:q']);
  const exampleCss = extract(example, join(temporary, 'example'));
  assert.match(exampleCss, /font-size: 1rem/, 'The example utility is extracted by the installed tool');
  assert.doesNotMatch(exampleCss, /purple/, 'The unused example utility is absent');
  console.log(`CSS_TOOL_PACKAGE PASS example Ranvier.Css.Compile ${toolVersion}`);

  // A project outside the checkout: Ranvier.Css comes from packages, and no --manifest is passed.
  const consumer = join(temporary, 'consumer');
  mkdirSync(consumer);
  const project = join(consumer, 'Consumer.fsproj');
  writeFileSync(project, `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Ranvier.Css" Version="${version('src/Ranvier.Css.Package/Ranvier.Css.Package.fsproj')}"/>
    <PackageReference Include="Ranvier.Css.View" Version="${version('src/Ranvier.Css.View/Ranvier.Css.View.fsproj')}"/>
    <Compile Include="Styles.fs"/>
  </ItemGroup>
</Project>`);
  writeFileSync(join(consumer, 'Styles.fs'), `module Consumer.Styles

open Ranvier.Css
open Ranvier.Css.View

let card = utility { Css.width (Css.px 12.) }
let badge = utility { Css.height (Css.px 3.) }

let view () =
    styles {
        card
        badge
    }
`);
  dotnet(['restore', project, '--configfile', config, '--packages', join(temporary, 'packages')], consumer);
  dotnet(['build', project, '-c', 'Release', '--no-restore', '--nologo', '-v:q'], consumer);
  const consumerCss = extract(project, join(temporary, 'consumer-assets'));
  assert.match(consumerCss, /width: 12px/, 'The first utility of a packaged consumer is extracted');
  assert.match(consumerCss, /height: 3px/, 'The second utility of a packaged consumer is extracted');
  console.log(`CSS_TOOL_PACKAGE PASS packaged consumer Ranvier.Css.Compile ${toolVersion}`);
} finally {
  const allocated = realpathSync(temporary);
  if (dirname(allocated) !== temporaryRoot || !allocated.split(/[\\/]/).at(-1).startsWith('ranvier-css-tool-')) {
    throw new Error('Tool check cleanup target is outside its allocated directory');
  }
  rmSync(allocated, { recursive:true, force:true });
}
