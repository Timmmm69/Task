import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
path = root / '.project-dashboard/roadmap.json'
data = json.loads(path.read_text(encoding='utf-8-sig'))

def update(node):
    if isinstance(node, dict):
        if node.get('id') == 'DESK-02':
            node['evidence'].append('outputs/20261016_task_filtering_sorting_7.2.0/validation-report.md: eight local sort orders, stable ID selection, version-aware pagination; Release desktop 1119 PASS, API 174 PASS; native WPF 125/150/200% layout equivalents and measured 10k-row sorting.')
            node['note'] += ' 2026-10-16: Этап 7.2 — восемь локальных сортировок объединены с фильтрами. Выделение/карточка и сортировка сохраняются, страницы дедуплицируются по ID с защитой новой версии, частичная выборка обозначена явно. Windows WPF: минимальное окно, клавиатура и DPI equivalents проверены; 10k строк измерены. API/DB/permissions не изменены. Progress/status сохранены.'
            node['updated_at'] = '2026-10-16T03:40:00+03:00'
            return
        for child in node.values():
            update(child)
    elif isinstance(node, list):
        for child in node:
            update(child)

update(data)
path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
