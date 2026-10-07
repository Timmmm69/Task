import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';

// Build only this task's dashboard delta from HEAD; keep the user's working file.
const dashboard = JSON.parse(execFileSync('git', ['show', 'HEAD:.project-dashboard/roadmap.json'], { encoding: 'utf8' }));
const script = readFileSync('work/location_manager_1/Update-Dashboard.mjs', 'utf8');
const note = script.match(/const note = '([^']+)';/)[1];
const evidence = 'outputs/20261008_location_manager_1.0.0/validation-report.md';
for (const id of ['PROD-05', 'DESK-03', 'QA-03']) {
  const item = dashboard.items.find(item => item.id === id);
  if (!item) throw new Error(`Missing dashboard item ${id}`);
  if (!item.evidence.includes(evidence)) item.evidence.push(evidence);
  if (!item.note.includes('Corporate Location Manager 1.0.0')) item.note += ` ${note}`;
  item.updated_at = '2026-10-08T00:00:00+03:00';
}
writeFileSync('work/location_manager_1/dashboard-for-index.json', `${JSON.stringify(dashboard, null, 2)}\n`);
