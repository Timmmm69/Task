import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { dirname, resolve, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const output = resolve(root, 'outputs/20260904_product_api_1.0.0');
const implementation = [
  '.project-dashboard/roadmap.json',
  'work/production/deployment/containers/sql/grant-runtime.sql',
  'work/production/docs/task-product-api.md',
  'work/production/src/Task.Api/Program.cs',
  'work/production/src/Task.Api/Security/TaskPermissionAuthorization.cs',
  'work/production/src/Task.Api/ProductData/ProductEndpoints.cs',
  'work/production/src/Task.Application/ProductData/ProductApiContracts.cs',
  'work/production/src/Task.Infrastructure/Persistence/Migrations/010_product_api.sql',
  ...['Links', 'Queries', 'Relations', 'Search', 'Settings', 'Store'].map(name =>
    `work/production/src/Task.Infrastructure/Persistence/PostgresProductApi${name}.cs`),
  ...['Catalog', 'Migrator', 'Runtime'].map(name =>
    `work/production/src/Task.Infrastructure/Persistence/TaskPersistence${name === 'Catalog' ? 'MigrationCatalog' : name}.cs`),
  'work/production/tests/Task.ServiceHosts.Tests/AuthSessionEndpointsTests.cs',
  'work/production/tests/Task.ServiceHosts.Tests/ProductEndpointsTests.cs',
  'work/production/tests/Task.Tests/PostgresTaskAggregateStoreTests.cs',
  'work/production/tests/Task.Tests/PostgresProductApiTests.cs',
  'work/production/tests/Task.Tests/TaskPersistenceMigrationHistoryTests.cs',
  'work/production/verification/Build-ProductApiPackage.mjs',
];
const normalize = path => path.replaceAll('\\', '/');
const hash = buffer => createHash('sha256').update(buffer).digest('hex');
function entry(path, normalizeText = false) {
  const raw = readFileSync(resolve(root, path));
  const buffer = normalizeText ? Buffer.from(raw.toString('utf8').replaceAll('\r\n', '\n'), 'utf8') : raw;
  return { path: normalize(path), sha256: hash(buffer), bytes: buffer.length };
}
const evidence = readdirSync(resolve(output, 'evidence')).filter(name => name.endsWith('.trx')).sort();
if (evidence.length !== 3) throw new Error('Exactly three final TRX files are required.');
const runs = evidence.map(name => {
  const path = resolve(output, 'evidence', name);
  const xml = readFileSync(path, 'utf8');
  const counters = xml.match(/<Counters\s+([^>]+)\/?\s*>/)?.[1];
  if (!counters) throw new Error(`Missing counters: ${name}`);
  const attrs = Object.fromEntries([...counters.matchAll(/(\w+)="(\d+)"/g)].map(match => [match[1], Number(match[2])]));
  if (attrs.total !== attrs.passed || attrs.failed !== 0 || attrs.notExecuted !== 0)
    throw new Error(`Unsuccessful or skipped tests: ${name}`);
  return { file: normalize(relative(root, path)), total: attrs.total, passed: attrs.passed, failed: attrs.failed, skipped: attrs.notExecuted };
});
if (runs.map(run => run.total).sort((a, b) => a - b).join(',') !== '245,417,773')
  throw new Error('Unexpected test counts; review the report before rebuilding.');
const artifacts = ['.gitattributes', 'README.md', 'VALIDATION_REPORT.md', 'VERSION', ...evidence.map(name => `evidence/${name}`)];
const manifest = {
  package: 'API-04 product module APIs', version: '1.0.0', date: '2026-09-04',
  base_revision: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim(),
  roadmap_id: 'API-04', state: 'validated-for-main',
  implementation_hash_format: 'utf8-crlf-normalized-to-lf', artifact_hash_format: 'exact-bytes',
  runtime: { dotnet_sdk: '10.0.400', postgresql: '16.14', schema_version: 10 },
  validation: { total: 1435, passed: 1435, failed: 0, skipped: 0, runs },
  implementation_files: implementation.sort().map(path => entry(path, true)),
  evidence_files: artifacts.map(name => entry(normalize(relative(root, resolve(output, name))))),
};
writeFileSync(resolve(output, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
const checksums = [...artifacts, 'manifest.json'].sort().map(name =>
  `${hash(readFileSync(resolve(output, name)))}  ${name}`).join('\n') + '\n';
writeFileSync(resolve(output, 'SHA256SUMS'), checksums);
for (const [records, normalizeText] of [[manifest.implementation_files, true], [manifest.evidence_files, false]]) {
  for (const record of records)
    if (entry(record.path, normalizeText).sha256 !== record.sha256) throw new Error(`Hash mismatch: ${record.path}`);
}
for (const line of readFileSync(resolve(output, 'SHA256SUMS'), 'utf8').trim().split('\n')) {
  const [expected, name] = line.split('  ');
  if (hash(readFileSync(resolve(output, name))) !== expected) throw new Error(`Package hash mismatch: ${name}`);
}
console.log(JSON.stringify({ valid: true, implementation_files: implementation.length, package_files: artifacts.length + 1, tests: 1435 }, null, 2));
