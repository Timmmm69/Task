import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
const git = (...args) => execFileSync('git', args, { encoding: 'utf8' }).trim();
const dashboardOnly = process.argv.includes('--dashboard-only');
if (!dashboardOnly && git('diff', '--cached', '--name-only')) throw new Error('Index must be empty before scoped staging');
const path = '.project-dashboard/roadmap.json';
const baseText = execFileSync('git', ['show', 'HEAD:' + path], { encoding: 'utf8' });
const base = JSON.parse(baseText.replace(/^\uFEFF/, ''));
const before = JSON.parse(fs.readFileSync('work/inbox_zero_1/roadmap-before.json', 'utf8').replace(/^\uFEFF/, ''));
const current = JSON.parse(fs.readFileSync(path, 'utf8').replace(/^\uFEFF/, ''));
const old = base.items.find(x => x.id === 'DESK-02');
const prior = before.items.find(x => x.id === 'DESK-02');
const next = current.items.find(x => x.id === 'DESK-02');
if (!next.note.startsWith(prior.note)) throw new Error('Unexpected dashboard note changes');
const note = old.note + next.note.slice(prior.note.length);
const evidence = next.evidence.filter(x => !prior.evidence.includes(x));
const start = baseText.lastIndexOf('{', baseText.indexOf('"id": "DESK-02"'));
const end = baseText.indexOf('\n    }', start);
let block = baseText.slice(start, end).replace(JSON.stringify(old.note), JSON.stringify(note));
const lastEvidence = JSON.stringify(old.evidence.at(-1));
block = block.replace(lastEvidence, lastEvidence + evidence.map(x => ',\n        ' + JSON.stringify(x)).join(''));
block = block.replace(JSON.stringify(old.updated_at), JSON.stringify(next.updated_at));
const staged = baseText.slice(0, start) + block + baseText.slice(end);
fs.writeFileSync('work/inbox_zero_1/roadmap-for-index.json', staged);
const blob = git('hash-object', '-w', '--path=' + path, 'work/inbox_zero_1/roadmap-for-index.json');
git('update-index', '--add', '--cacheinfo', '100644,' + blob + ',' + path);
if (dashboardOnly) process.exit(0);
const manifest = JSON.parse(fs.readFileSync('outputs/20261008_inbox_zero_1.0.1/manifest.json', 'utf8').replace(/^\uFEFF/, ''));
git('add', '--', ...manifest.scope,
    'work/inbox_zero_1/Package.ps1', 'work/inbox_zero_1/Stage.mjs',
    'outputs/20261008_inbox_zero_1.0.1', 'outputs/20261008_inbox_zero_1.0.1.zip',
    'outputs/20261008_inbox_zero_1.0.1.zip.sha256', 'outputs/20261008_inbox_zero_1.0.1.zip.validation.json');
