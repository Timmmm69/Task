import fs from 'node:fs';
const path = '.project-dashboard/roadmap.json';
const data = JSON.parse(fs.readFileSync(path, 'utf8'));
const evidence = 'outputs/20261015_project_employee_picker_5_2_1.0.0/validation-report.md';
for (const item of data.items.filter(item => ['PROD-04', 'DESK-03'].includes(item.id))) {
  if (!item.evidence.includes(evidence)) item.evidence.push(evidence);
  item.note += ' 2026-10-15: Этап 5.2 подключает общий Employee Picker к участникам и необязательному руководителю проектов; имена через разрешённый User.Read, повторная проверка активности, защита от дублей и устаревших ответов, существующие DTO/If-Match/idempotency. Реальные WPF-формы и негативные сценарии проверены. Без User.Read сохранён UUID-сценарий; полный role coverage требует отдельного решения по permissions. Progress/status не повышены.';
  item.updated_at = '2026-10-15';
}
fs.writeFileSync(path, JSON.stringify(data, null, 2) + '\n');
