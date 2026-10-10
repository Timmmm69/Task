import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { applyRecommendedOrder } from '../../.project-dashboard/execution-plan.mjs';
const path = '.project-dashboard/roadmap.json';
const before = readFileSync(path, 'utf8');
writeFileSync('work/ux_five_1/roadmap-before.json', before);
const base = JSON.parse(execFileSync('git', ['show', `HEAD:${path}`], { encoding: 'utf8' }));
const working = JSON.parse(before.replace(/^\uFEFF/, ''));
const evidence = 'outputs/20261010_task_ux_five_1.0.0/validation-report.md: calendar/Today/Tomorrow, form keyboard, pinned corporate footer, confirmed checklist checkbox, Personal terminal filter; 756 desktop tests PASS, native WPF keyboard/screenshots and Release build PASS.';
const note = ' 2026-10-10: реализованы пять улучшений форм Task: календарь и быстрые дни, Enter/Ctrl+Enter, закреплённые кнопки корпоративной задачи, checkbox с подтверждённым состоянием, фильтр завершённых/отменённых Personal (по умолчанию выключен). Проверены ошибки, черновики, минимальные окна и системный фокус. Progress/status не повышались.';
for (const data of [base, working]) {
  for (const item of data.items) {
    if (!['DESK-05', 'QA-04', 'PROD-02'].includes(item.id)) continue;
    if (!item.evidence.includes(evidence)) item.evidence.push(evidence);
    if (!item.note.includes('реализованы пять улучшений форм Task')) item.note += note;
    item.updated_at = new Date().toISOString();
  }
}
writeFileSync(path, `${JSON.stringify(working, null, 2)}\n`);
base.items = applyRecommendedOrder(base.items);
writeFileSync('work/ux_five_1/roadmap-for-index.json', `${JSON.stringify(base, null, 2)}\n`);
