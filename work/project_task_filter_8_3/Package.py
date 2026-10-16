from pathlib import Path
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'outputs/20261016_project_task_filter_8.3.0_blocked'
OUT.mkdir(parents=True, exist_ok=True)
(OUT / '.gitattributes').write_text('* binary\n', encoding='utf-8')

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()

def save(name, value):
    (OUT / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

stage81 = ROOT / 'outputs/20261016_project_related_tasks_8.1.0'
if not stage81.is_dir():
    stage81 = ROOT / 'work/project_related_tasks_8_1/repo/outputs/20261016_project_related_tasks_8.1.0'
packages = [
    stage81,
    ROOT / 'outputs/20261016_project_task_filter_contract_8.2.0',
]
checks = []
tests = []
for package in packages:
    manifest = json.loads((package / 'manifest.json').read_text(encoding='utf-8-sig'))
    for entry in manifest['files']:
        path = package / entry['path']
        assert sha(path) == entry['sha256'], str(path)
    checks.append({'package': str(package.relative_to(ROOT)), 'verifiedFiles': len(manifest['files']), 'hashes': 'PASS'})
    for trx in sorted((package / 'evidence').glob('*.trx')):
        tree = ET.parse(trx)
        counters = tree.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters')
        assert counters is not None
        tests.append({'file': str(trx.relative_to(ROOT)), 'counters': counters.attrib, 'sha256': sha(trx), 'execution': 'Historical evidence; not rerun in 8.3'})

proposal = ROOT / 'work/production/docs/PROJECT-TASK-FILTER-CONTRACT-PROPOSAL.md'
assert 'PROPOSED — NOT APPROVED' in proposal.read_text(encoding='utf-8-sig')
assert sha(proposal) == sha(packages[1] / proposal.name)
source_paths = [
    'work/production/src/Task.Api/Tasks/TaskEndpoints.cs',
    'work/production/src/Task.Application/TaskReadContracts.cs',
    'work/production/src/Task.Application/TaskReadCursorCodec.cs',
    'work/production/src/Task.Application/ITaskReadStore.cs',
    'work/production/src/Task.Infrastructure/Persistence/PostgresTaskReadStore.cs',
    'work/production/src/Task.Desktop/Tasks/DesktopTasksApiClient.cs',
    'work/production/src/Task.Desktop/ViewModels/ProjectsViewModel.cs',
    '.project-dashboard/roadmap.json',
]
before = {p: sha(ROOT / p) for p in source_paths}
save('evidence.json', {
    'date': '2026-10-16', 'branch': git('branch', '--show-current'),
    'head': git('rev-parse', 'HEAD'), 'originMain': git('rev-parse', 'origin/main'),
    'headVsOriginMain': git('rev-list', '--left-right', '--count', 'HEAD...origin/main'),
    'stage81': git('rev-parse', 'perf/project-related-tasks'),
    'stage82': git('rev-parse', 'codex/project-task-filter-contract-8-2'),
    'proposalSha256': sha(proposal), 'priorPackageChecks': checks, 'historicalTests': tests,
    'protectedFilesSha256': before,
})
(OUT / 'VERSION').write_text('8.3.0-blocked\n', encoding='utf-8')
report = '''# Этап 8.3 — проверка допуска, 8.3.0-blocked

Дата: 2026-10-16. Статус: BLOCKED_CONTRACT_APPROVAL. Серверная выборка не реализована.

## Блокер и нормативное основание

Обязательное условие §1 задания не выполнено. PROJECT-TASK-FILTER-CONTRACT-PROPOSAL.md
имеет статус PROPOSED — NOT APPROVED; его содержимое совпадает с проверенным пакетом 8.2.
Согласно §2 предложения, нормативный GET tasks определяет generic filter, но не
project grammar; optional scalar projectId — предлагаемый delta B, не действующий контракт.
В доступных результатах этапов и актуальном origin/main отдельное утверждение не обнаружено.
Нынешнее задание прямо запрещает считать proposal разрешением.

Нужны явные решения C1–C5 (§13 предложения): B и его validation/existence/error semantics;
unsigned cursor compatibility либо защищённый v2 с правилами ключей; bounded-live keyset
и revocation/lifecycle semantics; performance budget и отдельное решение по DB-D1 при
необходимости индекса; интеграция 8.1 и server-first release gate. Миграция не разрешается
одним утверждением API. Нормативный delta следует опубликовать в рабочей редакции,
не изменяя sources/. Самостоятельный выбор этих решений означал бы обход условия начала.

## Итог этапов

| Этап | Статус | Изменённые файлы этапа | Тесты из проверенных TRX | Коммит |
|---|---|---|---|---|
| 8.1 | Локальная реализация; не в origin/main | ProjectsViewModel и RelatedTasks partial, ProjectsView XAML/code-behind, 4 Projects test files, PROD-04, roadmap/evidence | baseline 9; targeted 28; desktop 1149; final Projects/ViewState 82 — все PASS | eb1044d |
| 8.2 | Предложение; не утверждено и не в origin/main | Proposal, Package.py, review package | cursor 4 и endpoints 166 — PASS существующего поведения | 4829e95 |
| 8.3 | Заблокирован допуск к реализации | Только work/project_task_filter_8_3/Package.py и этот пакет отчёта | Проверка хешей manifest и чтение исторических TRX; новых runtime tests нет | Не создан |

Полный desktop gate 8.1 предшествовал финальной правке приоритета SessionEnded/Offline;
82 финальных теста покрывают окончательную редакцию. Счётчики не суммируются:
запуски пересекаются. Хеши и точные результаты сохранены в evidence.json.

## Состояние репозитория и сохранность

Выполнен git fetch origin. origin/main = deb16b3; текущий HEAD = 5465c4e,
ветка fix/desktop-four-usability-improvements, отстаёт от origin/main на 19 коммитов.
Рабочая копия содержит прежние изменения и untracked-артефакты; синхронизация/переключение
ветки, merge, rebase, cherry-pick, commit и push не выполнялись. Коммиты 8.1/8.2 сохранены.
На origin/main оба отсутствуют. Ничего нового в main не опубликовано.

API, DTO, cursor, SQL, БД, permissions, desktop и sources/ в этом этапе не менялись.
Permissions сохранены отсутствием изменений; это не доказательство безопасности
будущего серверного фильтра. Dashboard не изменялся: готовность продукта не повысилась.

## Производительность

| Метрика | До | После | Результат |
|---|---|---|---|
| API-запросы | Для N=10000, page=50: расчёт 200 при полном обходе | Для M=10: расчёт 1 при будущем фильтре | Только оценка, реализация отсутствует |
| Полученные записи | Расчёт 10000 доступных задач | Расчёт 10 доступных задач проекта | Не замер |
| Время до первого результата | Не измерено на live server | Не измерено | Сравнение недоступно |
| Время полной загрузки | Не измерено на live server | Не измерено | Сравнение недоступно |
| Permissions | Исторические baseline tests | Новый filter не проверен | Новый security gate обязателен |

Оценки относятся к статичному набору доступных задач. Live PostgreSQL, EXPLAIN ANALYZE,
обработанные строки, число SQL-запросов, эффективность индексов и наборы 100/1000/10000/50000
в этом этапе не измерялись. Ни сетевой, ни DB performance gain не заявляется как измеренный.
8.1 подтверждает только независимую загрузку карточки и явную пагинацию с клиентским отбором.

## Проверки, риски и следующий шаг

Проверены прежние manifest SHA-256 и TRX, статус proposal, история и membership коммитов
в origin/main. Новые backend/desktop runtime tests, нагрузка, WPF и DB gates не запускались:
реализация остановлена до изменения кода. Старые зелёные тесты не валидируют новый фильтр.

Риски: legacy server игнорирует projectId; unsigned cursor не обеспечивает MAC integrity;
bounded-live keyset не является MVCC snapshot; SQL predicate без project index может
продолжать большой scan; revocation race требует проверки; локальный 8.1 ещё не интегрирован.

Следующий шаг — утверждение конкретных C1–C5 и публикация согласованного contract delta,
затем интеграция 8.1 и реализация/проверка 8.3. Decision-complete L/M implementation task
пока нет: unresolved API/security decisions относятся к H, DELEGATION_PACKET не создаётся.
PR с серверной фильтрацией не готов. Definition of Done не выполнен.
'''
(OUT / 'validation-report.md').write_text(report, encoding='utf-8')
assert before == {p: sha(ROOT / p) for p in source_paths}
save('validation.json', {'version': '8.3.0-blocked', 'status': 'BLOCKED_CONTRACT_APPROVAL', 'implementationComplete': False, 'priorManifests': 'PASS', 'historicalTrxParsed': len(tests), 'protectedFilesUnchanged': True, 'runtimeTestsRerun': False})
entries = [{'path': p.name, 'size': p.stat().st_size, 'sha256': sha(p)} for p in sorted(OUT.iterdir()) if p.is_file() and p.name not in {'manifest.json', 'SHA256SUMS'}]
save('manifest.json', {'version': '8.3.0-blocked', 'status': 'BLOCKED_CONTRACT_APPROVAL', 'files': entries})
files = sorted(p for p in OUT.iterdir() if p.is_file() and p.name != 'SHA256SUMS')
(OUT / 'SHA256SUMS').write_text(''.join(f'{sha(p)}  {p.name}\n' for p in files), encoding='utf-8')
archive = Path(str(OUT) + '.zip')
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
    for p in sorted(OUT.iterdir()):
        z.write(p, p.name)
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for p in OUT.iterdir():
        assert z.read(p.name) == p.read_bytes()
archive.with_suffix('.zip.sha256').write_text(f'{sha(archive)}  {archive.name}\n', encoding='utf-8')
print(json.dumps({'archive': str(archive), 'sha256': sha(archive), 'previousPackages': checks, 'historicalTrx': len(tests), 'archiveValidation': 'PASS'}, ensure_ascii=True))
