import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import http from 'node:http';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const directory=path.dirname(fileURLToPath(import.meta.url));
const root=path.join(directory,'out');
let executablePath=process.env.CSS_CHROMIUM_EXECUTABLE;
if(!executablePath) {
  try {await fs.access(chromium.executablePath());}
  catch {
    const cache=process.env.LOCALAPPDATA?path.join(process.env.LOCALAPPDATA,'ms-playwright'):null;
    if(cache) {
      const versions=(await fs.readdir(cache)).filter(name=>/^chromium-\d+$/.test(name)).sort((a,b)=>Number(b.split('-')[1])-Number(a.split('-')[1]));
      for(const version of versions) {
        const candidate=path.join(cache,version,'chrome-win64/chrome.exe');
        try {await fs.access(candidate); executablePath=candidate; break;} catch {}
      }
    }
  }
}
const server=http.createServer(async (request,response)=>{
  try {
    const route=decodeURIComponent(new URL(request.url,'http://localhost').pathname);
    if(route==='/') {response.setHeader('Content-Type','text/html');response.end('<!doctype html><html><head></head><body></body></html>');return;}
    const target=path.resolve(root,'.'+route);
    if(!target.startsWith(root+path.sep)) {response.writeHead(403).end();return;}
    response.setHeader('Content-Type',target.endsWith('.js')?'application/javascript':target.endsWith('.css')?'text/css':'application/octet-stream');
    response.end(await fs.readFile(target));
  } catch {response.writeHead(404).end();}
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
let browser;
try {
  browser=await chromium.launch({headless:true,...(executablePath?{executablePath}:{})});
  const page=await browser.newPage();
  page.setDefaultTimeout(60000);
  await page.goto(`http://127.0.0.1:${server.address().port}/`);
  const css=await page.evaluate(async()=> (await import('/browser/BrowserTests.js')).cssText());
  await page.addStyleTag({content:css});
  const identity=await page.evaluate(async()=> (await import('/browser/BrowserTests.js')).run());
  assert.equal(identity,'f31c44a96477ddd5');
  console.log('Chromium: generated typed bindings, native computed widths, CSS-wide inline semantics, both commit modes and disposal pass.');
} finally {
  await browser?.close();
  await new Promise(resolve=>server.close(resolve));
}
