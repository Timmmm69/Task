import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync, readdirSync, existsSync } from 'node:fs';
import { dirname, resolve, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const output = resolve(root, 'outputs/20260904_task_product_completion_1.0.0');
const manifestPath = resolve(output, 'manifest.json');
const git = args => execFileSync('git', ['-c', 'core.quotepath=false', '-c', 'core.safecrlf=false', ...args], { cwd: root, encoding: 'utf8' }).trim();
const normalize = path => path.replaceAll('\\', '/');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const previous = existsSync(manifestPath) ? JSON.parse(readFileSync(manifestPath, 'utf8')) : null;
const changed = [...git(['diff', '--name-only']).split('\n'), ...git(['ls-files', '--others', '--exclude-standard']).split('\n')];
const implementation = previous?.implementation_files.map(entry => entry.path) ?? [...new Set(changed)].filter(path =>
  path === '.project-dashboard/roadmap.json' || path.startsWith('work/production/') && !path.startsWith('work/production/evidence/')).sort();
if (!implementation.some(path => path.endsWith('011_task_card.sql'))) throw new Error('Task card migration is missing from the package scope.');
function record(path, normalizeText) {
  const raw = readFileSync(resolve(root, path));
  const bytes = normalizeText ? Buffer.from(raw.toString('utf8').replaceAll('\r\n', '\n')) : raw;
  return { path: normalize(path), bytes: bytes.length, sha256: hash(bytes) };
}
const trx = readdirSync(resolve(output, 'evidence')).filter(name => name.endsWith('.trx')).sort();
if (trx.length !== 3) throw new Error('Exactly three final test result files are required.');
const runs = trx.map(name => {
  const text = readFileSync(resolve(output, 'evidence', name), 'utf8');
  const counters = text.match(/<Counters\s+([^>]+)>/)?.[1];
  if (!counters) throw new Error(`Missing test counters: ${name}`);
  const values = Object.fromEntries([...counters.matchAll(/(\w+)="(\d+)"/g)].map(match => [match[1], Number(match[2])]));
  if (values.total !== values.passed || values.failed !== 0 || values.notExecuted !== 0) throw new Error(`Failed/skipped tests: ${name}`);
  return { file: `evidence/${name}`, total: values.total, passed: values.passed, failed: values.failed, skipped: values.notExecuted };
});
if (runs.map(run => run.total).sort((a, b) => a - b).join(',') !== '247,432,779') throw new Error('Review changed test counts before packaging.');
const e2e = JSON.parse(readFileSync(resolve(output, 'evidence/db-assertions.json'), 'utf8'));
if (!e2e.extendedCardVerified || e2e.finalStatus !== 'completed' || e2e.finalVersion !== 8 ||
    !['auditCount', 'domainEventCount', 'outboxCount', 'completedIdempotencyCount'].every(key => e2e[key] === 8)) throw new Error('Incomplete real task-card E2E evidence.');
const evidence = readdirSync(resolve(output, 'evidence')).sort().map(name => `evidence/${name}`);
const artifacts = ['.gitattributes', 'README.md', 'VALIDATION_REPORT.md', 'VERSION', ...evidence];
const manifest = {
  package: 'Task PROD-01 task lifecycle completion', version: '1.0.0', date: '2026-09-04',
  base_revision: previous?.base_revision ?? git(['rev-parse', 'HEAD']), roadmap_ids: ['PROD-01', 'DESK-02'],
  state: 'validated-for-main', runtime: { dotnet_sdk: '10.0.400', postgresql: '16.14', schema_version: 11 },
  implementation_hash_format: 'utf8-crlf-normalized-to-lf', artifact_hash_format: 'exact-bytes',
  validation: { total: 1458, passed: 1458, failed: 0, skipped: 0, runs, real_wpf_https_postgresql: 'PASS' },
  implementation_files: implementation.map(path => record(path, true)),
  evidence_files: artifacts.map(path => record(normalize(relative(root, resolve(output, path))), false)),
};
writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n');
writeFileSync(resolve(output, 'SHA256SUMS'), [...artifacts, 'manifest.json'].sort().map(path => `${hash(readFileSync(resolve(output, path)))}  ${path}`).join('\n') + '\n');
for (const [entries, normalized] of [[manifest.implementation_files, true], [manifest.evidence_files, false]])
  for (const entry of entries) if (record(entry.path, normalized).sha256 !== entry.sha256) throw new Error(`Hash mismatch: ${entry.path}`);
console.log(`PROD-01 package verified: ${implementation.length} implementation files, ${artifacts.length} artifacts, 1458 passed tests and real WPF lifecycle.`);
