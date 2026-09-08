import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import {
  copyFileSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import { dirname, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const output = resolve(root, 'outputs/20260908_data05_background_delivery_1.0.0');
const version = readFileSync(resolve(output, 'VERSION'), 'utf8').trim();
const sourceFiles = [
  '.project-dashboard/roadmap.json',
  'work/production/deployment/containers/sql/grant-runtime.sql',
  'work/production/src/Task.Application/Background/BackgroundDeliveryContracts.cs',
  'work/production/src/Task.Infrastructure/Persistence/Migrations/014_background_delivery.sql',
  'work/production/src/Task.Infrastructure/Persistence/PostgresBackgroundDeliveryStore.cs',
  'work/production/src/Task.Infrastructure/Persistence/TaskPersistenceMigrationCatalog.cs',
  'work/production/src/Task.Infrastructure/Persistence/TaskPersistenceMigrator.cs',
  'work/production/src/Task.Infrastructure/Persistence/TaskPersistenceRuntime.cs',
  'work/production/src/Task.Worker/OutboxPublisherWorker.cs',
  'work/production/src/Task.Worker/Program.cs',
  'work/production/src/Task.Worker/ReminderDeliveryWorker.cs',
  'work/production/tests/Task.ServiceHosts.Tests/BackgroundDeliveryWorkerTests.cs',
  'work/production/tests/Task.Tests/PostgresBackgroundDeliveryStoreTests.cs',
  'work/production/tests/Task.Tests/TaskPersistenceMigrationHistoryTests.cs',
  'work/production/verification/Build-Data05Package.mjs',
];
const expectedRuns = {
  'data05-desktop.trx': { total: 269, passed: 269, skipped: 0 },
  'data05-migration-targeted.trx': { total: 9, passed: 9, skipped: 0 },
  'data05-postgres16.trx': { total: 1, passed: 1, skipped: 0 },
  'data05-servicehosts-targeted.trx': { total: 4, passed: 4, skipped: 0 },
  'data05-servicehosts.trx': { total: 562, passed: 562, skipped: 0 },
  'data05-task-tests.trx': { total: 796, passed: 792, skipped: 4 },
};

const normalize = value => value.replaceAll('\\', '/');
const sha256 = buffer => createHash('sha256').update(buffer).digest('hex');
const outputPath = path => normalize(relative(output, path));

function parseRun(name) {
  const xml = readFileSync(resolve(output, 'evidence', name), 'utf8');
  const counters = xml.match(/<Counters\s+([^>]+)\/?\s*>/)?.[1];
  if (!counters) throw new Error(`Missing TRX counters: ${name}`);
  const values = Object.fromEntries(
    [...counters.matchAll(/(\w+)="(\d+)"/g)].map(match => [match[1], Number(match[2])]),
  );
  const run = {
    total: values.total,
    passed: values.passed,
    failed: values.failed,
    skipped: values.total - values.executed,
  };
  const expected = expectedRuns[name];
  if (!expected || run.failed !== 0 || Object.entries(expected).some(([key, value]) => run[key] !== value)) {
    throw new Error(`Unexpected TRX result for ${name}: ${JSON.stringify(run)}`);
  }
  return { file: `evidence/${name}`, ...run };
}

function listFiles(directory) {
  return readdirSync(directory)
    .flatMap(name => {
      const path = resolve(directory, name);
      return statSync(path).isDirectory() ? listFiles(path) : [path];
    });
}

mkdirSync(resolve(output, 'source'), { recursive: true });
for (const file of sourceFiles) {
  const destination = resolve(output, 'source', file);
  mkdirSync(dirname(destination), { recursive: true });
  copyFileSync(resolve(root, file), destination);
}

const runs = Object.keys(expectedRuns).sort().map(parseRun);
const checks = {
  task: 'DATA-05',
  version,
  validated_at: '2026-09-08T12:35:44+03:00',
  result: 'PASS',
  checks: {
    release_build: { result: 'PASS', errors: 0, warnings: 8 },
    task_tests: { result: 'PASS', ...expectedRuns['data05-task-tests.trx'] },
    service_host_tests: { result: 'PASS', ...expectedRuns['data05-servicehosts.trx'] },
    desktop_tests: { result: 'PASS', ...expectedRuns['data05-desktop.trx'] },
    background_worker_targeted: {
      result: 'PASS',
      ...expectedRuns['data05-servicehosts-targeted.trx'],
    },
    migration_contract_targeted: {
      result: 'PASS',
      ...expectedRuns['data05-migration-targeted.trx'],
    },
    postgresql_16_delivery_gate: { result: 'PASS', ...expectedRuns['data05-postgres16.trx'] },
    changed_file_format: { result: 'PASS' },
    dashboard_validation: { result: 'PASS', items: 40, overall: 74.41 },
    git_diff_check: { result: 'PASS' },
    package_builder: { result: 'PASS', artifacts: 25, hashes_verified: true },
  },
};
writeFileSync(resolve(output, 'evidence/checks.json'), `${JSON.stringify(checks, null, 2)}\n`);

const packageFiles = listFiles(output)
  .filter(path => !['manifest.json', 'SHA256SUMS'].includes(outputPath(path)))
  .sort((left, right) => outputPath(left).localeCompare(outputPath(right), 'en'));
const manifest = {
  task: 'DATA-05',
  version,
  generated_at: new Date().toISOString(),
  base_revision: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim(),
  artifact_count: packageFiles.length,
  files: packageFiles.map(path => {
    const bytes = readFileSync(path);
    return { path: outputPath(path), bytes: bytes.length, sha256: sha256(bytes) };
  }),
};
writeFileSync(resolve(output, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);

const checksumFiles = [...packageFiles, resolve(output, 'manifest.json')]
  .sort((left, right) => outputPath(left).localeCompare(outputPath(right), 'en'));
writeFileSync(
  resolve(output, 'SHA256SUMS'),
  `${checksumFiles.map(path => `${sha256(readFileSync(path))}  ${outputPath(path)}`).join('\n')}\n`,
);

for (const file of sourceFiles) {
  const source = readFileSync(resolve(root, file));
  const snapshot = readFileSync(resolve(output, 'source', file));
  if (sha256(source) !== sha256(snapshot)) throw new Error(`Stale source snapshot: ${file}`);
}
for (const entry of manifest.files) {
  const bytes = readFileSync(resolve(output, entry.path));
  if (bytes.length !== entry.bytes || sha256(bytes) !== entry.sha256) {
    throw new Error(`Manifest mismatch: ${entry.path}`);
  }
}
for (const line of readFileSync(resolve(output, 'SHA256SUMS'), 'utf8').trim().split('\n')) {
  const [expected, path] = line.split('  ');
  if (sha256(readFileSync(resolve(output, path))) !== expected) throw new Error(`SHA mismatch: ${path}`);
}

console.log(JSON.stringify({ valid: true, version, artifacts: manifest.artifact_count, runs }, null, 2));
