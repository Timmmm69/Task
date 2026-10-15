import fs from 'node:fs';
const path='.project-dashboard/roadmap.json';
const data=JSON.parse(fs.readFileSync(path,'utf8'));
const evidence='outputs/20261015_employee_picker_final_review_1.0.1/validation-report.md';
for (const item of data.items.filter(i=>['PROD-03','PROD-04','DESK-03'].includes(i.id))) {
 if (!item.evidence.includes(evidence)) {
  item.evidence.push(evidence);
  item.note += ' 2026-10-15: Финальный review 5.1–5.3: исправлен live-grant User.Read в открытой форме руководителя, с preselect сохранённого назначения и нулём запросов без права. Release build, 959 desktop, 259 endpoint и 233 domain/real PostgreSQL checks PASS; 206 scoped PASS. Native WPF regression пройдена; прежний нестабильный Window.Activate отмечен в истории receipts. Готово к публикации этапов в main; permission/API ограничения сохраняются, progress/status не повышены.';
  item.updated_at='2026-10-15';
 }
}
fs.writeFileSync(path, JSON.stringify(data,null,2)+'\n');
