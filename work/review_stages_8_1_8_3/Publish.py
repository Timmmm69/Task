"""Package final Task 8 review and validate byte-addressed publication inputs."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
NAME = '20261016_task8_final_review_8.3.1'
OUT = ROOT / 'outputs' / NAME
PACKAGES = [
    '20261016_project_related_tasks_8.1.0',
    '20261016_project_task_filter_contract_8.2.0',
    '20261016_project_task_filter_8.3.0_blocked',
]

def sha(data):
    return hashlib.sha256(data).hexdigest()

def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

def validate_package(name, staged=False):
    folder = ROOT / 'outputs' / name
    def read(relative):
        if staged:
            return subprocess.check_output(['git', 'show', ':outputs/' + name + '/' + relative], cwd=ROOT)
        return (folder / relative).read_bytes()
    manifest = json.loads(read('manifest.json'))
    for entry in manifest['files']:
        assert sha(read(entry['path'])) == entry['sha256'], (name, entry['path'])
    for line in read('SHA256SUMS').decode('utf-8').splitlines():
        digest, filename = line.split('  ', 1)
        assert sha(read(filename)) == digest, (name, filename)
    archive_path = ROOT / 'outputs' / (name + '.zip')
    archive_hash = archive_path.with_suffix('.zip.sha256').read_text().split()[0]
    assert sha(archive_path.read_bytes()) == archive_hash
    with zipfile.ZipFile(archive_path) as archive:
        assert archive.testzip() is None
        for relative in archive.namelist():
            if relative.endswith('/'):
                continue
            # Earlier packages may put the package folder above their members.
            member = relative.removeprefix(name + '/')
            assert archive.read(relative) == read(member), (name, relative)
    return {'package': name, 'manifestFiles': len(manifest['files']), 'manifest': 'PASS', 'SHA256SUMS': 'PASS', 'ZIP': 'PASS', 'stagedGitBytes': staged}

if '--verify-index' in sys.argv:
    for package in [*PACKAGES, NAME]:
        print(json.dumps(validate_package(package, True)))
    raise SystemExit(0)

checks = [validate_package(package) for package in PACKAGES]
OUT.mkdir(parents=True, exist_ok=True)
(OUT / '.gitattributes').write_text('* binary\n', encoding='utf-8')
(OUT / 'VERSION').write_text('8.3.1-review\n', encoding='utf-8')
evidence = ROOT / 'work/review_stages_8_1_8_3/evidence'
target = OUT / 'evidence'
target.mkdir(exist_ok=True)
results = []
for trx in sorted(evidence.glob('*.trx')):
    counters = ET.parse(trx).find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters')
    assert counters is not None
    results.append({'file': trx.name, 'counters': counters.attrib})
    shutil.copy2(trx, target / trx.name)
assert len(results) == 5
for log in ['solution-review.log', 'desktop-review.log', 'cursor-review.log', 'endpoints-review.log', 'focus-isolated.log']:
    shutil.copy2(ROOT / log, target / log)
write_json(OUT / 'validation.json', {
    'version': '8.3.1-review', 'status': 'REVIEWED_WITH_BASELINE_UI_LIMITATION',
    'previousPackages': checks, 'testRuns': results,
    'releaseSolutionBuild': 'PASS, 0 warnings, 0 errors',
    'dashboardOrderAndValidation': 'PASS; no progress/status increase',
    'independentCodeReview': 'No actionable defects found in production diff eb1044d',
    'serverFilterImplemented': False, 'contractApproved': False,
    'apiDtoCursorSqlPermissionsUnchanged': True,
})
(OUT / 'validation-report.md').write_text('''# Task 8.1–8.3 — финальный review 8.3.1

Дата: 2026-10-16. База интеграции: origin/main deb16b3.
Изменения перенесены в чистую изолированную копию без конфликтов.
Проверено отсутствие изменений API, application, infrastructure и desktop Task API client.
Новые дефекты в production diff не найдены; выполнен независимый read-only review.

| Этап | Результат для публикации | Исходный коммит | Интеграционный коммит |
|---|---|---|---|
| 8.1 | Явная порционная загрузка, отдельное состояние, cancellation/access/session guards | eb1044d | 2958ed5 |
| 8.2 | Контрактный proposal PROPOSED — NOT APPROVED | 4829e95 | 98c2326 |
| 8.3 | Отчёт BLOCKED_CONTRACT_APPROVAL; серверная фильтрация отсутствует | локальные артефакты | публикация этим review |

## Проверки

- Release solution build PASS: 0 ошибок, 0 предупреждений.
- TaskReadCursorCodecTests: 4/4 PASS.
- TaskEndpointsTests: 166/166 PASS.
- Полный desktop gate: 1149 PASS, 1 FAIL, 0 SKIP (1150 всего).
- Единственный FAIL: NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow,
  TaskContextActionsUiTests.cs:62, Assert.True(company.Activate()).
  Такой же FAIL получен при отдельном запуске на интегрированной версии и на
  неизменённом origin/main deb16b3. Это существующее ограничение активации окна
  в текущей Windows-сессии, не регрессия этапов. Тест не отключался и не изменялся.
  Его последующие mouse/keyboard assertions этим запуском не проверены.
- Остальные 1149 тестов включают final Projects/ViewState, pagination, cancellation,
  access/session/error/refresh и новый native related-task WPF test.
- dashboard:order и dashboard:validate PASS, progress/status не повышены.
- Manifest SHA-256, SHA256SUMS, ZIP CRC и содержимое пакетов проверены.
  Перед commit отдельно проверяются байты файлов в Git index.
- При компиляции тестовых проектов видны прежние xUnit/ASPDEPR004 warnings.

## Исправления пакета 8.3

Добавлена .gitattributes (* binary) в byte-addressed пакет: Git не должен менять
окончания строк и нарушать manifest/SHA-256. Package.py теперь использует обычный
outputs-путь опубликованного пакета 8.1; прежний worktree-путь остаётся fallback
для исходной локальной копии. Скрипт и пакет 8.3 включены в публикацию.

## Границы результата

8.1 улучшает отзывчивость карточки проекта, но полный набор задач по-прежнему
требует явного обхода страниц организации. 8.2 не утверждён публикацией в main.
8.3 не реализует SQL project predicate, новый query parameter или cursor v2.
Нормативное разрешение C1–C5 не получено; данные, migrations и permissions не менялись.
Live PostgreSQL/EXPLAIN и серверные performance benchmarks не запускались.
Числа 200 запросов против 1 для N=10000/M=10 — оценка будущего контракта, не замер.
Отчёты 8.1/8.2/8.3 описывают состояние на момент их первоначального аудита;
этот review фиксирует последующую интеграцию для публикации.

Для данных изменений готовность к публикации подтверждена с указанной baseline UI
оговоркой. Готовность серверной фильтрации и Definition of Done 8.3 не подтверждены.
Следующий шаг продукта — решение по C1–C5 и реализация согласованной серверной выборки.
''', encoding='utf-8')
entries = [{'path': p.relative_to(OUT).as_posix(), 'sha256': sha(p.read_bytes()), 'size': p.stat().st_size}
           for p in sorted(OUT.rglob('*')) if p.is_file() and p.name not in {'manifest.json', 'SHA256SUMS'}]
write_json(OUT / 'manifest.json', {'version': '8.3.1-review', 'files': entries})
files = [p for p in sorted(OUT.rglob('*')) if p.is_file() and p.name != 'SHA256SUMS']
(OUT / 'SHA256SUMS').write_text(''.join(f'{sha(p.read_bytes())}  {p.relative_to(OUT).as_posix()}\n' for p in files), encoding='utf-8')
archive_path = ROOT / 'outputs' / (NAME + '.zip')
with zipfile.ZipFile(archive_path, 'w', zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(OUT.rglob('*')):
        if path.is_file():
            archive.write(path, path.relative_to(OUT).as_posix())
archive_path.with_suffix('.zip.sha256').write_text(f'{sha(archive_path.read_bytes())}  {archive_path.name}\n', encoding='utf-8')
print(json.dumps(validate_package(NAME)))
