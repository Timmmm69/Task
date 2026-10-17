import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';

const repo = process.cwd();
const version = '10.3.0';
const relative = 'outputs/20261017_object_link_picker_review_10.3.0';
const out = path.join(repo, relative);
fs.mkdirSync(path.join(out, 'evidence'), { recursive: true });
fs.writeFileSync(path.join(out, '.gitattributes'), '* -text\nevidence/** -diff -whitespace\n*.patch -diff -whitespace\n');
const sourceCommit = execFileSync('git', ['rev-parse', process.argv[2] ?? 'bdfb23cf7603b30db47ff345a7700aeebff7bb3f'], { encoding: 'utf8' }).trim();
const changes = execFileSync('git', ['diff-tree', '--no-commit-id', '--name-only', '-r', sourceCommit], { encoding: 'utf8' }).trim().split('\n');
const sha = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const tests = {};
for (const [key, file] of Object.entries({
  desktop: 'desktop-final.trx', scoped: 'object-links-final.trx', domain_postgres: 'domain-postgres.trx',
  api: 'servicehosts-full.trx', inherited_ui_baseline: 'context-ui-baseline.trx',
  original_cache_defect: 'access-red.trx', explicit_no_database_skip: 'postgres-explicit-skip.trx',
})) {
  const input = path.join(repo, 'work/audit_10_3/results', file);
  const xml = fs.readFileSync(input, 'utf8');
  const counters = xml.match(/<Counters\b([^>]+)>/)[1];
  tests[key] = Object.fromEntries([...counters.matchAll(/(total|executed|passed|failed|notExecuted)="(\d+)"/g)].map(m => [m[1], Number(m[2])]));
  tests[key].skipped = tests[key].total - tests[key].executed;
  fs.copyFileSync(input, path.join(out, 'evidence', file));
}
for (const file of ['build-release.log', 'domain-postgres.log', 'postgres-runtime.json'])
  fs.copyFileSync(path.join(repo, 'work/audit_10_3/results', file), path.join(out, 'evidence', file));
for (const file of fs.readdirSync(path.join(repo, 'work/audit_10_3/wpf')).filter(f => f.endsWith('.png')))
  fs.copyFileSync(path.join(repo, 'work/audit_10_3/wpf', file), path.join(out, 'evidence', file));
fs.writeFileSync(path.join(out, 'source-changes.patch'), execFileSync('git', ['show', '--format=fuller', '--binary', sourceCommit]));
const validation = {
  version, date: '2026-10-17', branch: 'feature/object-link-target-picker', source_commit: sourceCommit,
  baseline_10_1: '736a306', baseline_10_2: '5587a49', origin_main: '57fa015', changed_files: changes,
  stages: Object.fromEntries([['10.1', '736a306'], ['10.2', '5587a49'], ['10.3', sourceCommit]].map(([stage, commit]) =>
    [stage, { commit, files: execFileSync('git', ['diff-tree', '--no-commit-id', '--name-only', '-r', commit], { encoding: 'utf8' }).trim().split('\n') }])),
  status: 'REVIEWABLE_WITH_ACCEPTANCE_LIMITATIONS', full_acceptance: false, full_gate_green: false,
  build: { configuration: 'Release', solution: 'work/production/Task.sln', passed: true, errors: 0 },
  tests, real_postgresql: { version: 16, isolated: true, loopback_only: true, full_suite: true },
  native_wpf: { executed: true, real_windows: true, mocked_api: true, screenshots_inspected: true,
    physical_keyboard_tab_and_narrator: false, installed_client_live_server: false },
  blockers: [
    'Full Desktop gate: inherited TaskContextActionsUiTests Window.Activate assertion at line 62; reproduced at 5587a49.',
    'related targets network_resource, notification, inbox_item are accepted by Links but unavailable through Search; full related discovery coverage remains incomplete.',
  ],
  limitations: ['Search.Use is required; no q-list fallback.', 'Existing links adapter still rejects hasMore beyond 200 rows.',
    'No live installed Corporate client/account acceptance, Narrator, physical keyboard or mixed-DPI certification.'],
  permissions_weakened: false, server_rules_changed: false, database_or_openapi_changed: false,
  independent_final_review: 'ACCEPT; no introduced production regression identified; related discovery limitation retained explicitly',
};
fs.writeFileSync(path.join(out, 'validation-report.json'), JSON.stringify(validation, null, 2) + '\n');
const summary = `# Результаты проверки 10.3.0\n\nКоммит исправлений: \`${sourceCommit}\`.\n\n` +
  `| Этап | Статус | Изменённые файлы | Тесты | Коммиты |\n| --- | --- | --- | --- | --- |\n` +
  `| 10.1 — Target Picker | Реализован; проверен с исправлениями 10.3 | Picker VM/View, search adapter, tests/docs | текущий scoped gate ${tests.scoped.passed} PASS | 736a306 |\n` +
  `| 10.2 — Интеграция | Реализована; проверена с исправлениями 10.3 | ObjectLinks/WorkHub VM/View, adapter, tests/docs | текущий scoped gate ${tests.scoped.passed} PASS | 5587a49 |\n` +
  `| 10.3 — Проверка | Исправления проверены; полная приёмка ограничена | ${changes.length} файлов; полный список в JSON | Desktop ${tests.desktop.passed} PASS / ${tests.desktop.failed} inherited FAIL; API ${tests.api.passed} PASS; domain/PG ${tests.domain_postgres.passed} PASS | ${sourceCommit.slice(0, 7)} |\n\n` +
  `Release solution build: PASS. Scoped проверки входят в Desktop suite, их нельзя прибавлять повторно.\n` +
  `Оба новых PostgreSQL-теста фактически выполнены в domain/PG gate. Отдельная проверка без DB подтверждает явный SKIP вместо ложного PASS.\n\n`;
fs.writeFileSync(path.join(out, 'validation-report.md'), summary + fs.readFileSync(path.join(repo, 'work/production/docs/object-link-picker-review.md'), 'utf8'));

const walk = dir => fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => entry.isDirectory()
  ? walk(path.join(dir, entry.name)) : [path.join(dir, entry.name)]);
const payload = walk(out).filter(f => !['manifest.json', 'SHA256SUMS.txt'].includes(path.basename(f))).sort();
const manifest = { product: 'Task', task: '10.3', version, source_commit: sourceCommit,
  files: payload.map(file => ({ path: path.relative(out, file).replaceAll('\\', '/'), bytes: fs.statSync(file).size, sha256: sha(file) })) };
fs.writeFileSync(path.join(out, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
const hashed = [...payload, path.join(out, 'manifest.json')];
fs.writeFileSync(path.join(out, 'SHA256SUMS.txt'), hashed.map(f => `${sha(f)}  ${path.relative(out, f).replaceAll('\\', '/')}`).join('\n') + '\n');
for (const file of manifest.files) if (sha(path.join(out, file.path)) !== file.sha256) throw new Error(`Hash mismatch: ${file.path}`);

const dashboardPath = path.join(repo, '.project-dashboard/roadmap.json');
const dashboard = JSON.parse(fs.readFileSync(dashboardPath, 'utf8'));
const item = dashboard.items.find(row => row.id === 'DESK-03');
const evidence = relative + '/validation-report.json';
if (!item.evidence.includes(evidence)) item.evidence.push(evidence);
if (!item.note.includes('Этап 10.3:')) item.note += ` 2026-10-17: Этап 10.3: независимый review Object Links; исправлены session/scope cache, invisible-source read и поздний success после cancellation, добавлены project_contact/task_project/related для searchable resources. Release build, ${tests.scoped.passed} scoped, ${tests.api.passed} API, ${tests.domain_postgres.passed} real PostgreSQL PASS. Desktop ${tests.desktop.passed} PASS / ${tests.desktop.failed} inherited Window.Activate FAIL, воспроизведён на 5587a49. related targets вне Search, no-Search.Use и adapter >200 links остаются ограничениями; полная приёмка не объявлена, progress/status не повышены.`;
fs.writeFileSync(dashboardPath, JSON.stringify(dashboard, null, 2) + '\n');
console.log(JSON.stringify({ output: relative, sourceCommit, tests, files: manifest.files.length }, null, 2));
