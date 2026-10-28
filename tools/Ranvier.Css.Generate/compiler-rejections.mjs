export function rejectionProject(bindingsProject, cases) {
  const names = cases.map(item => item.name);
  if (new Set(names).size !== names.length) throw new Error('Duplicate compiler rejection case name.');
  if (names.some(name => !/^[A-Za-z][A-Za-z0-9]*$/.test(name))) throw new Error('Invalid compiler rejection case name.');
  const escape = text => text.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
  return `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><WarningsAsErrors>44</WarningsAsErrors><OtherFlags>$(OtherFlags) --maxerrors:${Math.max(1000, cases.length * 20)}</OtherFlags></PropertyGroup><ItemGroup><ProjectReference Include="${escape(bindingsProject)}"/>${names.map(name => `<Compile Include="${name}.fs"/>`).join('')}</ItemGroup></Project>`;
}

export function assertCompilerRejections(cases, log) {
  const diagnostics = new Map();
  for (const line of log.split(/\r?\n/)) {
    const match = /(?:^|[\\/])([^\\/]+\.fs)\(\d+,\d+(?:,\d+,\d+)?\):\s*error\s+(FS\d{4})\b/.exec(line);
    if (match) {
      const codes = diagnostics.get(match[1]) ?? new Set();
      codes.add(match[2]);
      diagnostics.set(match[1], codes);
    }
  }
  const missing = cases.filter(item => {
    const expected = Array.isArray(item.code) ? item.code : [item.code];
    return !expected.some(code => diagnostics.get(`${item.name}.fs`)?.has(code));
  });
  if (missing.length) throw new Error(`Missing expected compiler errors: ${missing.map(item => `${item.name} (${[].concat(item.code).join('/')})`).join(', ')}\n${log}`);
}
