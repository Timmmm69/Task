import { openSync, closeSync, readSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { join, resolve, relative, isAbsolute } from 'node:path';
import { createHash } from 'node:crypto';
import { compareBuilds } from './verify-oci.mjs';

const directory = resolve(process.argv[2]);
const buildsOnly = process.argv.includes('--builds-only');
const readJson = name => JSON.parse(readFileSync(join(directory, name)));
const release = readJson('release.json');
if ((!buildsOnly && (release.status !== 'PASS' || release.images.length !== 5)) || release.independentBuilds !== 2) {
  throw new Error('Release did not pass all gates');
}
const covered = new Set();
for (const line of readFileSync(join(directory, 'SHA256SUMS'), 'utf8').trim().split(/\r?\n/)) {
  const match = /^([a-f0-9]{64})  (.+)$/.exec(line);
  if (!match) throw new Error('Invalid checksum line');
  const [, expected, name] = match;
  const path = resolve(directory, name);
  const rel = relative(directory, path);
  if (isAbsolute(rel) || rel.startsWith('..') || covered.has(name)) throw new Error('Invalid/duplicate checksum path');
  covered.add(name);
  const fd = openSync(path, 'r');
  const hash = createHash('sha256');
  const buffer = Buffer.alloc(1024 * 1024);
  try { for (let n; (n = readSync(fd, buffer)) > 0;) hash.update(buffer.subarray(0, n)); }
  finally { closeSync(fd); }
  if (hash.digest('hex') !== expected) throw new Error(`Checksum mismatch: ${name}`);
}
function walk(path, prefix = '') {
  for (const name of readdirSync(path)) {
    const local = prefix + name;
    if (statSync(join(path, name)).isDirectory()) walk(join(path, name), local + '/');
    else if (local !== 'SHA256SUMS' && !covered.has(local)) throw new Error(`Uncovered artifact: ${local}`);
  }
}
walk(directory);
const images = buildsOnly ? ['task-api', 'task-worker', 'task-backup-agent', 'task-database-migrator', 'task-container-validation'].map(target => readJson(`evidence/${target}-1.oci.json`)) : release.images;
for (const image of images) {
  const first = readJson(`evidence/${image.target}-1.oci.json`);
  const second = readJson(`evidence/${image.target}-2.oci.json`);
  compareBuilds(first, second);
  if (image.imageDigest !== first.imageDigest || image.configDigest !== first.configDigest) throw new Error('Release image mapping differs');
}
console.log(`${buildsOnly ? 'BUILDS VERIFIED (runtime approval not implied)' : 'PASS'}: ${release.version}, ${release.revision}; ${covered.size} file hashes and five independent image pairs verified.`);
