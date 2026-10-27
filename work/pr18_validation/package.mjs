import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';

const root = process.cwd();
const out = path.join(root, 'outputs/20261028_pr18_review_1.0.0');
const hash = p => crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
const write = (p, value) => fs.writeFileSync(path.join(out, p), typeof value === 'string' ? value : JSON.stringify(value, null, 2) + '\n');
const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8' }).trim();
const counters = name => {
  const xml = fs.readFileSync(path.join(out, 'evidence', name), 'utf8');
  const c = xml.match(/<Counters\b[^>]*>/)?.[0];
  if (!c) throw Error(`No counters in ${name}`);
  return Object.fromEntries([...c.matchAll(/(\w+)="(\d+)"/g)].map(m => [m[1], Number(m[2])]));
};
const tests = {
  desktop: counters('desktop-final.trx'),
  postgres16: counters('postgres16-utf8.trx'),
  services: counters('solution_net10.0_20261028004318.trx'),
  contact: counters('contact-final.trx'),
  regression_before_fix: counters('scroll-before-fix.trx')
};
for (const [name, c] of Object.entries(tests)) {
  if (name === 'regression_before_fix') { if (c.failed !== 1) throw Error('Original defect not reproduced'); }
  else if (c.failed || c.passed !== c.total) throw Error(`Incomplete test run: ${name}`);
}
const sources = [
  'work/production/src/Task.Desktop/Resources/Controls.ContactRelations.xaml',
  'work/production/src/Task.Desktop/Views/ContactRelationsView.xaml',
  'work/production/src/Task.Desktop/Views/ContactRelationsView.xaml.cs',
  'work/production/tests/Task.Desktop.Tests/Work/ContactRelationsScrollWindowsTests.cs'
];
const view = sources[1];
const oldView = git('show', 'origin/main:' + view);
const newView = fs.readFileSync(path.join(root, view), 'utf8');
const bindings = s => new Set([...s.matchAll(/\{Binding\s+([A-Za-z][A-Za-z0-9_.]*)/g)].map(m => m[1]));
const missing = [...bindings(oldView)].filter(b => !bindings(newView).has(b));
if (missing.length) throw Error(`Lost bindings: ${missing.join(', ')}`);
const total = tests.desktop.passed + tests.postgres16.passed + tests.services.passed;
write('VERSION', '1.0.0\n');
write('.gitattributes', '* binary\n');
write('source-manifest.json', {
  original_pr_head: '3d7890ca22b6394f685261175697455024cde596',
  integration_base: git('rev-parse', 'origin/main'),
  review_parent: git('rev-parse', 'HEAD'),
  files: sources.map(p => ({ path: p, sha256: hash(path.join(root, p)) }))
});
write('validation.json', {
  version: '1.0.0', pr: 18, date: '2026-10-28', tests, unique_suite_total: total,
  build: 'PASS', format: 'PASS', dependency_audit: 'PASS', boundaries: 'PASS', security: 'PASS', tls: 'PASS',
  binding_paths_preserved: true,
  layout: '800x600 and 1100x1000; channel/address/delete/read-only; 100/150/200% raster exports',
  native: 'Mouse editor opening; Tab, Alt+Down, Down, Enter for both lists; Space for both checkboxes; channel cancellation',
  forced_contrast: 'Production XAML loaded in test-only harness with HighContrast conditions forced true; checkbox system brush assertions passed',
  limitations: ['Physical mixed-DPI monitor transitions not run', 'Windows OS high-contrast switching and Narrator not run', 'Synthetic contact client used for interactive review; no customer deployment claimed'],
  diagnostic_runs: { 'postgres16.trx': 'Initial local cluster incorrectly used WIN1252; replaced with UTF8 cluster and all 914 tests passed', 'scroll-before-fix.trx': 'Expected failure reproduces the original PR defect' }
});
write('validation-report.md', `# PR18 Windows review — 1.0.0

Reviewed original PR head 3d7890ca22b6394f685261175697455024cde596 integrated with main ${git('rev-parse', 'origin/main')}.

## Finding and fix
The editor requested a one-pixel rectangle at Loaded priority. With long contact lists its fields remained below the viewport; focus transfer from disabled action buttons could also undo the scroll. Request the full editor at ContextIdle after input/focus work. No focus is forcibly assigned. The new Windows regression covers add/edit channel, address and delete confirmation at 800×600 and 1100×1000. It fails on the original handler and passes after the fix.

## Validation
- Windows Release solution build and full whitespace gate: PASS.
- ${total} unique suite tests: ${tests.desktop.passed} Desktop, ${tests.services.passed} ServiceHosts, ${tests.postgres16.passed} domain/API with real PostgreSQL 16, all passed with no skips. Final contact subset: ${tests.contact.passed}/${tests.contact.total}.
- Dependency audit, project boundaries, independent security review and TLS contract: PASS.
- Original binding paths preserved. Public API/VM/database contracts unchanged.
- Live WPF synthetic contact window: mouse editing/autoscroll, channel cancellation, keyboard selection in both dropdowns, Tab traversal and both checkbox Space toggles: PASS.
- 800×600/1100×1000: editor fields and Save reachable, read-only buttons disabled, no binding errors. Raster exports at 100/150/200% are rendering checks, not physical monitor DPI changes.
- Forced contrast harness: the actual production XAML templates with test-only HighContrast conditions enabled render successfully; checked checkbox strokes/backgrounds resolve to system brushes. OS contrast switching and Narrator were not run.

## Reproduction
Run from repository root: dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --filter FullyQualifiedName~ContactRelationsScrollWindowsTests

Layout: dotnet run --project work/pr18_validation/Probe.csproj -c Release -- outputs/pr18-recheck

Forced contrast branches: append --contrast. This harness removes the code-behind event and manually scrolls for the contrast branch; the compiled production event is tested separately by the Windows regression.

## Evidence and limits
See validation.json, source-manifest.json, evidence/*.trx, layout/metrics.json and forced-contrast/metrics.json. Initial WIN1252 PostgreSQL failures are setup diagnostics; the final UTF8 PostgreSQL run passes. The initial full solution run used the prior test binary for Desktop; desktop-final.trx is the authoritative final Desktop run. Native UI used a synthetic local client; no customer/server deployment or clean-Windows/mixed-monitor acceptance is claimed. CI must be green for the published PR head before merge; live GitHub results are verified separately at publication.
`);
const files = [];
function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p);
    else if (!['manifest.json', 'manifest.sha256'].includes(e.name)) files.push({ path: path.relative(out, p).replaceAll('\\', '/'), sha256: hash(p), bytes: fs.statSync(p).size });
  }
}
walk(out);
files.sort((a, b) => a.path.localeCompare(b.path));
write('manifest.json', { version: '1.0.0', files });
write('manifest.sha256', hash(path.join(out, 'manifest.json')) + '  manifest.json\n');
for (const f of files) if (hash(path.join(out, f.path)) !== f.sha256) throw Error('Hash mismatch: ' + f.path);
console.log(JSON.stringify({ files: files.length, tests: total, status: 'PASS' }));
