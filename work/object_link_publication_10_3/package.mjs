import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';

const output = 'outputs/20261017_object_links_publication_10.3.1';
const root = process.cwd();
const out = path.join(root, output);
const sourceCommit = execFileSync('git', ['rev-parse', '402483b'], { encoding: 'utf8' }).trim();
const checklist = 'work/production/docs/OBJECT-LINKS-MANUAL-ACCEPTANCE.md';
fs.mkdirSync(path.join(out, 'evidence'), { recursive: true });
fs.writeFileSync(path.join(out, '.gitattributes'), '* -text\nevidence/** -diff -whitespace\n');
const tests = {};
for (const [name, file] of Object.entries({ desktop: 'desktop.trx', api: 'api.trx', domain_postgres: 'domain-postgres.trx' })) {
  const source = path.join(root, 'work/object_link_publication_10_3/results', file);
  const xml = fs.readFileSync(source, 'utf8');
  const counters = xml.match(/<Counters\b([^>]+)>/)[1];
  tests[name] = Object.fromEntries([...counters.matchAll(/(total|executed|passed|failed)="(\d+)"/g)].map(m => [m[1], Number(m[2])]));
  tests[name].skipped = tests[name].total - tests[name].executed;
  fs.copyFileSync(source, path.join(out, 'evidence', file));
}
if (Object.values(tests).some(test => test.failed || test.skipped)) throw new Error('Publication gate has failures or skipped tests.');
for (const file of ['build.log', 'postgres-runtime.json'])
  fs.copyFileSync(path.join(root, 'work/object_link_publication_10_3/results', file), path.join(out, 'evidence', file));
const report = {
  version: '10.3.1', date: '2026-10-17', source_commit: sourceCommit,
  branch: 'feature/object-link-target-picker', publication_authorized_by_user: true,
  full_acceptance_passed: false, manual_acceptance_status: 'OPEN_NOT_PASSED', manual_acceptance_checklist: checklist,
  tests, release_solution_build: { passed: true, errors: 0, warnings: 11 },
  previous_limitations_fixed: ['All 11 related target resource types', 'Discovery without Search.Use under ObjectLink.Create', 'Complete existing-links cursor traversal'],
  independent_security_review: 'ACCEPT; no concrete permissions, tenant/own-scope or global-search regression found',
  manual_unverified: ['Installed Desktop against live Corporate API', 'Physical keyboard and audible Narrator', 'Physical mixed-DPI monitor movement'],
  deployment_requirement: 'Deploy API and Desktop together; new picker uses /api/v1/object-link-targets',
  historical_report: 'outputs/20261017_object_link_picker_review_10.3.0/validation-report.json',
};
fs.writeFileSync(path.join(out, 'validation-report.json'), JSON.stringify(report, null, 2) + '\n');
fs.writeFileSync(path.join(out, 'validation-report.md'), `# Object Links 10.1–10.3 — публикация 10.3.1\n\n` +
  `Исходный код: \`${sourceCommit}\`. Пользователь разрешил публикацию в main с открытой ручной приёмкой.\n\n` +
  `**Полная приёмка НЕ ПРОЙДЕНА. Статус ручной приёмки: OPEN.**\n\n` +
  `Закрыты прежние ограничения: полный набор related targets, discovery без Search.Use, чтение всех страниц связей.\n\n` +
  `| Проверка | PASS | FAIL | SKIP |\n| --- | --- | --- | --- |\n` +
  Object.entries(tests).map(([name, t]) => `| ${name} | ${t.passed} | ${t.failed} | ${t.skipped} |`).join('\n') + '\n\n' +
  `Release solution build: PASS, 0 errors, 11 warnings. PostgreSQL 16 использована фактически на изолированном loopback-стенде.\n\n` +
  `Новый endpoint прошёл независимый security review; Desktop и API необходимо обновлять совместно.\n\n` +
  `После публикации открыть [чек-лист ручной приёмки](../../${checklist}). Не закрывать его по результатам автоматических тестов.\n`);
const files = fs.readdirSync(out, { withFileTypes: true }).flatMap(entry => entry.isDirectory()
  ? fs.readdirSync(path.join(out, entry.name)).map(file => path.join(entry.name, file)) : [entry.name])
  .filter(file => !['manifest.json', 'SHA256SUMS.txt'].includes(file)).sort();
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const manifest = { product: 'Task', version: '10.3.1', source_commit: sourceCommit,
  files: files.map(file => ({ path: file.replaceAll('\\', '/'), sha256: hash(path.join(out, file)), bytes: fs.statSync(path.join(out, file)).size })) };
fs.writeFileSync(path.join(out, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
fs.writeFileSync(path.join(out, 'SHA256SUMS.txt'), [...files, 'manifest.json'].map(file => `${hash(path.join(out, file))}  ${file.replaceAll('\\', '/')}`).join('\n') + '\n');
const roadmapPath = path.join(root, '.project-dashboard/roadmap.json');
const roadmap = JSON.parse(fs.readFileSync(roadmapPath, 'utf8'));
for (const id of ['DESK-03', 'PROD-05', 'API-04']) {
  const item = roadmap.items.find(row => row.id === id);
  const evidence = output + '/validation-report.json';
  item.evidence = [...new Set(item.evidence.map(value => value.replace('outputs/20261017_object_links_acceptance_10.3.0/validation-report.json', evidence)).concat([evidence, checklist]))];
  item.note = item.note.replaceAll('Изменения в локальной feature-ветке; main не обновлён.', 'Публикация в main разрешена пользователем с открытой ручной приёмкой.');
  if (!item.note.includes('Публикация 10.3.1:')) item.note += ` 2026-10-17: Публикация 10.3.1: ${sourceCommit.slice(0, 7)} закрывает discovery всех related targets, отсутствие Search.Use и лимит списка связей. Desktop ${tests.desktop.passed} PASS / ${tests.desktop.failed} FAIL; API ${tests.api.passed} PASS; real PostgreSQL ${tests.domain_postgres.passed} PASS. Полная ручная приёмка НЕ ПРОЙДЕНА / OPEN: установленный клиент с live Corporate API, Narrator и physical mixed-DPI. Открыть ${checklist}; автоматические тесты не закрывают этот пункт. Progress/status не повышены.`;
  const reminder = 'Открыть OBJECT-LINKS-MANUAL-ACCEPTANCE.md и закрыть ручную приёмку Object Links с evidence.';
  if (!item.next_action.includes(reminder)) item.next_action += ' ' + reminder;
  item.updated_at = '2026-10-17';
}
fs.writeFileSync(roadmapPath, JSON.stringify(roadmap, null, 2) + '\n');
console.log(JSON.stringify({ output, tests, manual_acceptance: 'OPEN_NOT_PASSED' }, null, 2));
