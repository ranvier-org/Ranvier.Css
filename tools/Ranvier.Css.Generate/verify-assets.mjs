import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { transform } from 'lightningcss';

const directory=path.dirname(fileURLToPath(import.meta.url));
const root=path.resolve(directory,'../..');
const project=path.join(root,'examples/Ranvier.Css.Example');
for(const mode of ['single','modules','css-modules']) {
  const output=path.join(directory,'out/assets',mode);
  const result=spawnSync('dotnet',['run','--project',project,'-c','Release','--',`--${mode}`,'--output',output],{cwd:root,encoding:'utf8',maxBuffer:8*1024*1024});
  if(result.status!==0) throw new Error((result.stdout??'')+(result.stderr??''));
  const manifest=JSON.parse(await fs.readFile(path.join(output,'css-manifest.json'),'utf8'));
  assert.deepEqual(manifest.orderedModules,['reset','progress']);
  assert.equal(manifest.assets.length,mode==='single'?1:2);
  const html=await fs.readFile(path.join(output,'index.html'),'utf8');
  assert.match(html,/42px/);
  for(const asset of manifest.assets) {
    assert.ok(html.includes(asset.url));
    const input=await fs.readFile(path.join(output,asset.fileName));
    const result=transform({filename:asset.fileName,code:input,cssModules:mode==='css-modules'?{animation:false,grid:false,customIdents:false}:false});
    if(mode==='css-modules') {
      for(const [alias,actual] of Object.entries(asset.classes)) {
        assert.ok(result.exports[alias]?.composes.some(item=>item.type==='global'&&item.name===actual));
        assert.ok(html.includes(actual));
      }
      await fs.writeFile(path.join(output,asset.fileName+'.compiled.css'),result.code);
    }
  }
  console.log(`${mode}: extraction, real CSS parsing, exported classes, SSR links and initial variables pass.`);
}
