import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { transform } from 'lightningcss';
import { JSDOM } from 'jsdom';

const directory = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(directory, '../..');
const project = path.join(root, 'examples/Ranvier.Css.Utilities.Example/Ranvier.Css.Utilities.Example.fsproj');
const compiler = path.join(root, 'tools/Ranvier.Css.Compile/bin/Release/net10.0/Ranvier.Css.Compile.dll');
const application = path.join(root, 'examples/Ranvier.Css.Utilities.Example/bin/Release/net10.0/Ranvier.Css.Utilities.Example.dll');
function run(arguments_) {
  const result = spawnSync('dotnet', arguments_, { cwd: root, encoding: 'utf8', maxBuffer: 8 * 1024 * 1024 });
  if (result.error || result.status !== 0) throw new Error((result.stdout ?? '') + (result.stderr ?? '') + (result.error ?? ''));
}

for (const mode of ['single', 'modules', 'css-modules']) {
  const output = path.join(directory, 'out/utilities', mode);
  run([compiler, '--project', project, '--output', output, `--${mode}`, ...(mode === 'single' ? ['app.css'] : [])]);
  const manifest = JSON.parse(await fs.readFile(path.join(output, 'css-manifest.json'), 'utf8'));
  assert.equal(manifest.assets.length, 1);
  const html = path.join(output, 'index.html');
  run([application, html]);
  const dom = new JSDOM(await fs.readFile(html, 'utf8'));
  try {
    const element = dom.window.document.querySelector('#app > div');
    assert.ok(element);
    assert.ok(element.classList.contains('user-card'), 'Generated styles must preserve the explicit user class.');
    for (const asset of manifest.assets) {
      assert.ok(dom.window.document.querySelector(`link[href="${asset.url}"]`), 'SSR must link the emitted asset URL.');
      const code = await fs.readFile(path.join(output, asset.fileName));
      const css = code.toString('utf8');
      assert.doesNotMatch(css, /purple/, 'Unused utility contributes no CSS.');
      assert.match(css, /@media \(min-width: 48rem\)/);
      assert.match(css, /:where\(/);
      const parsed = transform({ filename: asset.fileName, code, cssModules: mode === 'css-modules' ? { animation: false, grid: false, customIdents: false } : false });
      for (const [alias, actual] of Object.entries(asset.classes)) {
        assert.ok(element.classList.contains(actual), 'Runtime and extracted root identities agree.');
        assert.equal(element.style.getPropertyValue(`--${actual}-1`), '240px');
        assert.ok(css.includes(`var(--${actual}-1)`));
        if (mode === 'css-modules') assert.ok(parsed.exports[alias]?.composes.some(item => item.type === 'global' && item.name === actual));
      }
    }
  } finally { dom.window.close(); }
  console.log(`${mode}: automatic extraction, parsed CSS, user classes, SSR asset links and variable identities pass.`);
}
