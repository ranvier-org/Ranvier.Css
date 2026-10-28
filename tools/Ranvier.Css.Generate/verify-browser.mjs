import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { JSDOM } from 'jsdom';

const directory=path.dirname(fileURLToPath(import.meta.url));
const dom=new JSDOM('<!doctype html><body></body>');
const names=['window','document','FocusEvent','MutationObserver','Node','Event'];
for(const name of names) globalThis[name]=name==='window'?dom.window:dom.window[name];
try {
  const checks=await import(pathToFileURL(path.join(directory,'out/browser/BrowserTests.js')));
  const style=dom.window.document.createElement('style');
  style.textContent=checks.cssText();
  dom.window.document.head.append(style);
  const hash=checks.run();
  assert.equal(typeof hash,'string');
  assert.equal(hash,'f31c44a96477ddd5');
  console.log(`DOM mount/update/disposal passed in both commit modes; portable identity ${hash}.`);
} finally {
  dom.window.close();
  for(const name of names) delete globalThis[name];
}
