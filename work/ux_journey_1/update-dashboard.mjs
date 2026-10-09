import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { applyRecommendedOrder } from '../../.project-dashboard/execution-plan.mjs';

const path = '.project-dashboard/roadmap.json';
const before = readFileSync(path, 'utf8');
writeFileSync('work/ux_journey_1/roadmap-before.json', before);
const base = JSON.parse(execFileSync('git', ['show', `HEAD:${path}`], { encoding: 'utf8' }));
const working = JSON.parse(before.replace(/^\uFEFF/, ''));
const evidence = 'outputs/20261010_task_ux_journey_1.0.0/validation-report.md: eight journey improvements; 747 unique desktop tests and 47 native WPF checks PASS; Release build and shell contract PASS; 800x480 keyboard/day access verified.';
const note = ' 2026-10-10: реализованы восемь согласованных упрощений пользовательского пути в Personal и Corporate; названия, подсказки, поля даты, пустые состояния и результат сохранения. Проверены фильтры, черновики, отозванные write capabilities при сохранённом Task.Read, клавиатура и минимальное окно. Progress/status не повышались; серверные контракты и бизнес-правила не менялись.';
for (const data of [base, working]) {
  for (const item of data.items) {
    if (!['DESK-05', 'QA-04', 'PROD-02'].includes(item.id)) continue;
    if (!item.evidence.includes(evidence)) item.evidence.push(evidence);
    if (!item.note.includes('2026-10-10: реализованы восемь')) item.note += note;
    item.updated_at = '2026-10-10T00:00:00+03:00';
  }
}
writeFileSync(path, `${JSON.stringify(working, null, 2)}\n`);
base.items = applyRecommendedOrder(base.items);
writeFileSync('work/ux_journey_1/roadmap-for-index.json', `${JSON.stringify(base, null, 2)}\n`);
// Existing OPS/HAND working changes are retained locally and excluded from this commit.
