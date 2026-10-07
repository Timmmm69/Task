import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

const file = resolve('.project-dashboard/roadmap.json');
const roadmap = JSON.parse(readFileSync(file, 'utf8'));
const report = 'outputs/20261008_location_manager_1.0.0/validation-report.md';
const note = '2026-10-08: Corporate Location Manager 1.0.0: несколько путей, POST/PATCH/DELETE, primary, enabled, UNC picker без UUID, redaction/device scope, актуальный parent ETag и retained-draft HTTP 412. Полный Release gate: 1958 PASS / 0 SKIP / 0 FAIL с PostgreSQL 16; реальный WPF/API UIA проверяет offline/reconnect и неизменность файла после удаления последней ссылки. Personal SQLite-контракт не менялся.';
for (const id of ['PROD-05', 'DESK-03', 'QA-03']) {
  const item = roadmap.items.find(item => item.id === id);
  if (!item) throw new Error(`Missing dashboard item ${id}`);
  if (!item.evidence.includes(report)) item.evidence.push(report);
  if (!item.note.includes('Corporate Location Manager 1.0.0')) item.note += ` ${note}`;
  item.updated_at = '2026-10-08T00:00:00+03:00';
}
writeFileSync(file, `${JSON.stringify(roadmap, null, 2)}\n`);
