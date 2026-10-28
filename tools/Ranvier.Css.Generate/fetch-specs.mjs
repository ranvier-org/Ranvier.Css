import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const directory = path.dirname(fileURLToPath(import.meta.url));
const lockPath = path.join(directory, 'specs.lock.json');
const output = path.resolve(directory, 'out/specs');
const revision = '4bb34a8d08cb9b9c8b73f7f175703941eef5c20c';
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
async function request(url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${response.status}: ${url}`);
  return Buffer.from(await response.arrayBuffer());
}
const refresh = process.argv.includes('--refresh-lock');
let lock;
if (refresh) {
  const tree = JSON.parse(await request(`https://api.github.com/repos/w3c/webref/git/trees/${revision}?recursive=1`));
  if (tree.truncated) throw new Error('GitHub tree was truncated');
  lock = { repository: 'w3c/webref', revision, files: tree.tree.filter(item => /^ed\/css\/[^/]+\.json$/.test(item.path)).map(item => ({ file: path.basename(item.path), sha256: '' })).sort((a,b) => a.file.localeCompare(b.file, 'en')) };
} else lock = JSON.parse(await readFile(lockPath, 'utf8'));
if (!/^[a-f0-9]{40}$/.test(lock.revision) || lock.repository !== 'w3c/webref') throw new Error('Invalid source lock');
await mkdir(output, { recursive: true });
let next = 0;
await Promise.all(Array.from({length: 8}, async () => {
  while (next < lock.files.length) {
    const item = lock.files[next++];
    if (!/^[a-zA-Z0-9_.-]+\.json$/.test(item.file)) throw new Error('Unsafe source filename');
    const destination = path.join(output, item.file);
    let bytes;
    if (!refresh) {
      try { bytes = await readFile(destination); } catch (error) { if (error.code !== 'ENOENT') throw error; }
      if (bytes && digest(bytes) !== item.sha256) throw new Error(`Cached source digest mismatch: ${item.file}`);
    }
    bytes ??= await request(`https://raw.githubusercontent.com/${lock.repository}/${lock.revision}/ed/css/${item.file}`);
    JSON.parse(bytes);
    const hash = digest(bytes);
    if (refresh) item.sha256 = hash;
    else if (hash !== item.sha256) throw new Error(`Source digest mismatch: ${item.file}`);
    await writeFile(destination, bytes);
  }
}));
if (refresh) await writeFile(lockPath, JSON.stringify(lock, null, 2) + '\n');
console.log(`Verified ${lock.files.length} CSS spec extracts at ${lock.revision}`);
