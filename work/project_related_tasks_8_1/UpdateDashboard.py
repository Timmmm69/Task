from pathlib import Path
import json
p=Path('.project-dashboard/roadmap.json')
s=p.read_text(encoding='utf-8-sig')
start=s.index('      "id": "PROD-04",')
end=s.index('\n    },',start)
part=s[start:end]
if 'outputs/20261016_project_related_tasks_8.1.0/validation-report.md' in part:
    print('Stage 8.1 already recorded; dashboard unchanged.')
    raise SystemExit(0)
part=part.replace('"outputs/20261015_employee_picker_final_review_1.0.1/validation-report.md"',
    '"outputs/20261015_employee_picker_final_review_1.0.1/validation-report.md",\n        "outputs/20261016_project_related_tasks_8.1.0/validation-report.md"')
note_start=part.index('      "note": ')+len('      "note": ')
note_end=part.index(',\n',note_start)
note=json.loads(part[note_start:note_end])
note+=' 2026-10-16: Этап 8.1 отделяет связанные задачи от загрузки карточки: одна cursor-страница по явной команде, partial/complete/empty, cancellation и session guards, retry/dedupe/cursor cycle detection. Без обращения к задачам 0 запросов вместо 1/5/50 в детерминированных фикстурах; полный обход не ускорен. Baseline 9, полный desktop 1149, финальный Projects/ViewState 82 PASS; native WPF и Release solution build PASS. API/DTO/БД/permissions сохранены. Локальная ветка perf/project-related-tasks, main не изменён; progress/status не повышены.'
part=part[:note_start]+json.dumps(note,ensure_ascii=False)+part[note_end:]
part=part.replace('"updated_at": "2026-10-15"','"updated_at": "2026-10-16"')
s=s[:start]+part+s[end:]
p.write_text(s,encoding='utf-8')
