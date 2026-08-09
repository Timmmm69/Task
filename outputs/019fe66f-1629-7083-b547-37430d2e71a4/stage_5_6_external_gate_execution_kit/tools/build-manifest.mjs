import { createHash } from 'node:crypto';
import { readdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const skip = new Set(['manifest.json', 'MANIFEST.sha256']);
async function files(dir, prefix = '') {
  const entries = await readdir(dir, { withFileTypes: true });
  const found = [];
  for (const entry of entries) {
    const relative = path.posix.join(prefix, entry.name);
    if (entry.isDirectory()) found.push(...await files(path.join(dir, entry.name), relative));
    else if (!skip.has(relative)) found.push(relative);
  }
  return found;
}
const artifactHashes = Object.fromEntries(await Promise.all((await files(root)).sort().map(async (relative) => {
  const data = await readFile(path.join(root, relative));
  return [relative, createHash('sha256').update(data).digest('hex').toUpperCase()];
})));
const manifest = {
  package: 'Task Stage 5.6 external Gate execution kit', version: '0.2.0', date: '2026-08-09',
  status: 'PARTIAL — technical recheck captured; Gate 5.6 remains NOT_READY',
  scope: { windowsUIAKeyboardCheckpoints: 12, usabilityScenarios: 10, roleLenses: 4, evidenceRequirements: 9, namedApprovalRoles: 4, excluded: ['Narrator', 'voice control', 'DPI scaling', 'multi-monitor'] },
  gateStatus: { result: 'NOT_READY', acceptedEvidence: 0, requiredEvidence: 9 },
  artifactHashes,
  evidenceBoundaries: ['Partial technical reports are not owner acceptance', 'No participant session is claimed', 'No stakeholder approval is claimed']
};
const serialized = `${JSON.stringify(manifest, null, 2)}\n`;
await writeFile(path.join(root, 'manifest.json'), serialized);
await writeFile(path.join(root, 'MANIFEST.sha256'), `${createHash('sha256').update(serialized).digest('hex').toUpperCase()}  manifest.json\n`);
