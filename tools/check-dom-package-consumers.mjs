import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const packages = resolve(process.argv[2] || join(root, 'bin'));
const temporaryRoot = realpathSync(tmpdir());
const temporary = mkdtempSync(join(temporaryRoot, 'ranvier-css-dom-consumer-'));
const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
const version = path => {
  const match = readFileSync(join(root, path), 'utf8').match(/<Version>([^<]+)<\/Version>/);
  if (!match) throw new Error(`Missing package version in ${path}`);
  return match[1];
};
const adapterVersion = version('src/Ranvier.Css.Dom/Ranvier.Css.Dom.fsproj');
const bindingsVersion = version('src/Ranvier.Css.Bindings/Ranvier.Css.Bindings.fsproj');
try {
  const config = join(temporary, 'NuGet.Config');
  writeFileSync(config, `<configuration>
  <packageSources><clear/><add key="cohort" value="${xml(packages)}"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><clear/>
    <packageSource key="cohort"><package pattern="Ranvier.Css*"/><package pattern="Fable.Ranvier.Css.Dom*"/></packageSource>
    <packageSource key="nuget.org"><package pattern="*"/></packageSource>
  </packageSourceMapping>
</configuration>`);
  const project = join(temporary, 'Consumer.csproj');
  writeFileSync(join(temporary, 'Program.cs'), `using System;
using System.Linq;
using System.Reflection;
var expected = bool.Parse(args[0]);
foreach (var assembly in new[] { typeof(Ranvier.Css.Declaration).Assembly, typeof(Ranvier.Css.Css).Assembly,
    typeof(Ranvier.Css.Dom.StyleSnapshot).Assembly, typeof(Ranvier.Dom.Document).Assembly })
{
    var actual = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Any(value => value.Key == "RanvierTrace" && value.Value == "true");
    if (actual != expected) throw new Exception("Mixed traced CSS dependency: " + assembly.FullName);
}
Console.WriteLine("CSS_DOM_PACKAGE_COHORT PASS traced=" + expected);
`);
  for (const suffix of ['', '.Traced']) {
    for (const prefix of ['', 'Fable.']) {
      const adapter = `${prefix}Ranvier.Css.Dom${suffix}`;
      writeFileSync(project, `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup><PackageReference Include="${adapter}" Version="${adapterVersion}"/><PackageReference Include="Ranvier.Css.Bindings${suffix}" Version="${bindingsVersion}"/></ItemGroup>
</Project>`);
      execFileSync('dotnet', ['restore', project, '--configfile', config, '--packages', join(temporary, 'packages')], { cwd:root, stdio:'inherit' });
      execFileSync('dotnet', ['run', '--project', project, '-c', 'Release', '--no-restore', '--', String(Boolean(suffix))], { cwd:root, stdio:'inherit' });
      console.log(`CSS_DOM_PACKAGE_CONSUMER PASS ${adapter}`);
    }
  }
} finally {
  const allocated = realpathSync(temporary);
  if (dirname(allocated) !== temporaryRoot || !allocated.split(/[\\/]/).at(-1).startsWith('ranvier-css-dom-consumer-')) {
    throw new Error('Consumer cleanup target is outside its allocated directory');
  }
  rmSync(allocated, { recursive:true, force:true });
}
