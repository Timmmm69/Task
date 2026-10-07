import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';

const root = 'outputs/20261008_location_manager_1.0.0';
const manifest = JSON.parse(readFileSync(`${root}/manifest.json`, 'utf8'));
for (const entry of manifest.files) {
  const bytes = execFileSync('git', ['cat-file', 'blob', `:${root}/${entry.path}`], { maxBuffer: 8 * 1024 * 1024 });
  const hash = createHash('sha256').update(bytes).digest('hex');
  if (hash !== entry.sha256 || bytes.length !== entry.size) throw new Error(`Staged artifact mismatch: ${entry.path}`);
}
const expected = readFileSync('outputs/Task-location-manager-1.0.0.zip.sha256', 'utf8').split(/\s/)[0];
const zip = execFileSync('git', ['cat-file', 'blob', ':outputs/Task-location-manager-1.0.0.zip'], { maxBuffer: 2 * 1024 * 1024 });
if (createHash('sha256').update(zip).digest('hex') !== expected) throw new Error('Staged ZIP mismatch');
console.log(`PASS: ${manifest.files.length} staged artifact hashes and ZIP SHA-256 verified.`);
