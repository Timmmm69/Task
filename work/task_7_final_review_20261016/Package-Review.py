import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parents[2]
work = root / 'work/task_7_final_review_20261016'
output = root / 'outputs/20261016_task7_final_review_7.2.1'
output.mkdir(parents=True, exist_ok=True)
source = 'd4b27bd'
source = subprocess.check_output(['git', 'rev-parse', source], cwd=root).decode().strip()

def write(name, text):
    (output / name).write_text(text, encoding='utf-8', newline='\n')

write('.gitattributes', '* binary\n')
write('VERSION', '7.2.1\n')
evidence = output / 'evidence'
evidence.mkdir(exist_ok=True)
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
runs = {}
for name in ['desktop-review.trx', 'context-review.trx', 'api-review.trx', 'domain-review.trx']:
    shutil.copyfile(work / 'evidence' / name, evidence / name)
    tree = ET.parse(evidence / name)
    counters = tree.find('.//t:Counters', ns).attrib
    assert int(counters['failed']) == 0
    runs[name] = {key: int(counters[key]) for key in ['total', 'passed', 'failed']}
    # VSTest's summary notExecuted counter can remain zero for an xUnit skip.
    runs[name]['skipped'] = sum(result.attrib.get('outcome') == 'NotExecuted'
                                for result in tree.findall('.//t:UnitTestResult', ns))
assert sum(run['passed'] for run in runs.values()) == 1378
assert sum(run['skipped'] for run in runs.values()) == 1
write('independent-review.txt', 'Read-only independent review: no actionable findings.\n'
      'Scope: TaskFilters.cs and TasksViewModel.cs against origin/main; typed filters, local operations, '
      'selection/card reuse, cursor-page version deduplication, authentication/permission clearing, page errors.\n'
      'Reviewer did not rerun the test suite or manual PostgreSQL/LAN scenarios.\n')
write('validation-report.md', f'''# Task 7.1–7.2 — финальная проверка перед main / 7.2.1

Дата: 2026-10-16. Проверенная ревизия: `{source}`.
Решение: допустить публикацию в main. Подтверждённых дефектов не найдено; production-код в ходе этого review не менялся.

| Проверка | Результат |
|---|---|
| Release Desktop build + общая desktop-регрессия | 1118 PASS, 0 FAIL, 0 SKIP |
| Отдельный native mouse/focus тест | 1 PASS |
| API integration: lists, permissions, PATCH, card, transitions | 174 PASS |
| Domain/query/cursor/update/status/card | 85 PASS, 1 SKIP |
| Независимое read-only ревью filters/VM | No actionable findings |
| Primary review sorting, XAML, совместимости тестов и полного diff | No actionable findings |
| Manifest/SHA-256/ZIP пакетов 7.1.0 и 7.2.0 | PASS |
| Полный branch git diff --check и dashboard:validate | PASS |

Всего: **1378 PASS, 0 FAIL, 1 SKIP**. Все четыре текущих TRX приложены; повторы не суммировались.
Существующий physical-focus тест запускался отдельно, чтобы нативные окна не конкурировали за фокус.
Остались два существующих xUnit analyzer warnings в посторонних тестах.

Проверены обе стадии как одна функциональность: типизированные фильтры и комбинации, reset, восемь направлений,
детерминированные ties/nulls, mixed deadlines/DST, обновление подтверждённых DTO, version-aware pagination,
selection/card, offline/read-only и очистка при 401/403. Реальный WPF gate входит в desktop-регрессию.
Новые endpoints, серверный sort, БД, права и personal task manager не менялись.
Сортировка по-прежнему ограничена загруженными разрешёнными страницами; partial results обозначены в UI.

SKIP: `TaskUpdateTests.RealPostgres_PatchIsAtomicDurableAndConcurrencySafe` —
`TASK_POSTGRES_TEST_ADMIN_CONNECTION` не настроен. Ручной live-server/LAN/Narrator walkthrough,
аппаратные DPI 125/200% и mixed-monitor transitions этим review не подтверждаются.
Предыдущий native gate на физическом DPI 150% с layout equivalents 125/150/200% сохранён в пакете 7.2.0.

Публикация разрешена пользователем. Этот отчёт фиксирует pre-publication gate; фактический push и
совпадение HEAD с origin/main проверяются после его коммита. Посторонние изменения исходной рабочей копии
не включаются. Подробности реализации и предыдущие measurements — в пакете
`outputs/20261016_task_filtering_sorting_7.2.0/validation-report.md`.
''')
write('validation.json', json.dumps({'version': '7.2.1', 'sourceCommit': source,
    'decision': 'ready_for_main', 'newProductionChanges': False, 'actionableFindings': [],
    'testRuns': runs, 'total': {'passed': 1378, 'failed': 0, 'skipped': 1},
    'limits': ['Real PostgreSQL connection not configured', 'Manual live server/LAN/Narrator not run',
               'Hardware mixed-DPI transitions not run']}, ensure_ascii=False, indent=2) + '\n')
entries = [{'path': p.relative_to(output).as_posix(), 'bytes': p.stat().st_size,
            'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
           for p in sorted(output.rglob('*')) if p.is_file() and p.name not in ['manifest.json', 'SHA256SUMS']]
write('manifest.json', json.dumps({'version': '7.2.1', 'sourceCommit': source, 'files': entries,
    'checksumPolicy': 'manifest excludes itself and SHA256SUMS; SHA256SUMS includes manifest, excludes itself'}, indent=2) + '\n')
lines = [f"{e['sha256']}  {e['path']}" for e in entries]
lines.append(hashlib.sha256((output / 'manifest.json').read_bytes()).hexdigest() + '  manifest.json')
write('SHA256SUMS', '\n'.join(lines) + '\n')
for line in lines:
    digest, name = line.split('  ', 1)
    assert hashlib.sha256((output / name).read_bytes()).hexdigest() == digest
print('PASS: review package manifest and SHA-256; 1378 passed, 1 disclosed skip')
