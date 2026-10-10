import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { applyRecommendedOrder } from '../../.project-dashboard/execution-plan.mjs';
const path = '.project-dashboard/roadmap.json';
const before = readFileSync(path, 'utf8');
writeFileSync('work/ux_actions_6_8/roadmap-before.json', before);
const base = JSON.parse(execFileSync('git', ['show', `HEAD:${path}`], { encoding: 'utf8' }));
const working = JSON.parse(before.replace(/^\uFEFF/, ''));
const evidence = 'outputs/20261010_task_context_actions_1.0.0/validation-report.md: Task UX 6–8, double-click, create from project, add subtask in Personal/Corporate; 771 desktop tests, native WPF mouse/keyboard/screenshots, PostgreSQL relations, Release build. Existing boundary-script framework mismatch recorded.';
const note = ' 2026-10-10: реализованы задачи UX 6–8: двойной щелчок по задаче, создание из проекта, создание подзадачи. Обычная форма, защита черновиков и прав, сохранение выбранных связей при поздней загрузке. Progress/status не повышались.';
const stamp = new Date().toISOString();
for (const data of [base, working]) {
  for (const item of data.items) {
    if (!['DESK-05', 'QA-04', 'PROD-02'].includes(item.id)) continue;
    if (!item.evidence.includes(evidence)) item.evidence.push(evidence);
    if (!item.note.includes('реализованы задачи UX 6–8')) item.note += note;
    item.updated_at = stamp;
  }
}
writeFileSync(path, `${JSON.stringify(working, null, 2)}\n`);
base.items = applyRecommendedOrder(base.items);
writeFileSync('work/ux_actions_6_8/roadmap-for-index.json', `${JSON.stringify(base, null, 2)}\n`);
