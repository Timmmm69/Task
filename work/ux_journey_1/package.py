from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / 'work/ux_journey_1'
OUT = ROOT / 'outputs/20261010_task_ux_journey_1.0.0'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def summary(path):
    results = ET.parse(path).getroot().findall('.//t:UnitTestResult', NS)
    assert results and all(x.get('outcome') == 'Passed' for x in results), f'Non-PASS results: {path}'
    return {x.get('testName') for x in results}

tests = summary(WORK / 'evidence/desktop-gate.trx') | summary(WORK / 'evidence/focus-gate.trx')
assert len(tests) == 747, len(tests)
native = json.loads((WORK / 'evidence/native-accepted/ui-results.json').read_text(encoding='utf-8'))
assert native['status'] == 'PASS' and not native['bindingErrors'] and len(native['checks']) == 47
assert not OUT.exists(), 'Use a new artifact version rather than overwrite an existing package.'
OUT.mkdir(parents=True)
(OUT / '.gitattributes').write_text('* binary\n', encoding='utf-8')
(OUT / 'VERSION').write_text('1.0.0\n', encoding='utf-8')

scope = subprocess.check_output(['git', 'diff', 'HEAD', '--name-only', '--', 'work/production'], cwd=ROOT, text=True, encoding='utf-8').splitlines()
scope += ['work/production/tests/Task.Desktop.Tests/Tasks/JourneyTaskTests.cs', 'work/production/tests/Task.Desktop.Tests/Personal/PersonalJourneyTests.cs']
scope += ['work/ux_journey_1/README.md', 'work/ux_journey_1/desktop.runsettings', 'work/ux_journey_1/UiProbe/UiProbe.csproj', 'work/ux_journey_1/UiProbe/Program.cs', 'work/ux_journey_1/package.py', 'work/ux_journey_1/update-dashboard.mjs']
scope = sorted(set(scope))
for name in scope:
    target = OUT / 'source-snapshot' / name
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(ROOT / name, target)
for name in ['desktop-gate.trx', 'focus-gate.trx', 'release-build.log', 'shell-contract.log']:
    target = OUT / 'evidence' / name
    target.parent.mkdir(exist_ok=True)
    shutil.copyfile(WORK / 'evidence' / name, target)
target = OUT / 'evidence/native'
target.mkdir()
for path in (WORK / 'evidence/native-accepted').glob('*.png'):
    shutil.copyfile(path, target / path.name)
shutil.copyfile(WORK / 'evidence/native-accepted/ui-results.json', target / 'ui-results.json')
patch = subprocess.check_output(['git', 'diff', 'HEAD', '--binary', '--', 'work/production'], cwd=ROOT)
(OUT / 'changes.patch').write_bytes(patch)
(OUT / 'README.md').write_text('# Task: упрощение пользовательского пути — 1.0.0\n\nРеализованы все восемь согласованных пунктов. Состав изменений и границы проверки — в `validation-report.md`. SHA-256 всех файлов указан в manifest и SHA256SUMS. Source snapshot — overlay к указанному base commit, не отдельная полная копия репозитория.\n', encoding='utf-8')
(OUT / 'validation-report.md').write_text('''# Validation report — Task UX journey 1.0.0

Статус: PASS по scope пользовательского пути. Дата: 2026-10-10 (Europe/Minsk).

| Пункт | Реализация | Проверка |
|---|---|---|
| Выбор пространства | Русские названия, понятное объяснение входа в компанию, заголовки окон | Native mode-selector и настоящий startup test |
| Назначение разделов | Однострочные объяснения Сегодня, Входящих и Задач; видимость на узком окне | Native shell, отсутствие ошибок bindings |
| Обязательность полей | Название обязательно, пример; необязательные поля помечены; обычный приоритет предвыбран | Native editors и существующая validation suite |
| Планирование на день | Поле поднято из advanced в обоих режимах; сохранены формат, блокировка при точном начале и календарный контракт | Date ownership/validation regression, native Tab/focus и 800x480 |
| Создание и сохранение | «Создать задачу» и «Сохранить изменения», корректные accessible names; retry не потерян | SubmitText regression и native create/edit |
| Пустые экраны | Создание в пустых Задачах; переход к Задачам из Сегодня; существующий Inbox Zero и сброс фильтров сохранены | Native empty states и переходы модели |
| Недоступные действия | Понятные объяснения отсутствия разрешений на исполнителей/наблюдателей; причина создания в tooltip | Native rights visibility и capability/session suite |
| Результат сохранения | Видимое сообщение с местом сохранения; открытие задачи, включая скрытую фильтром | Native save/open и регрессии Personal/Corporate |

## Итоговые проверки

- Release build Task.Desktop: PASS, 0 предупреждений, 0 ошибок.
- Desktop: 747 уникальных тестов PASS, 0 FAIL, 0 SKIP в итоговых запусках: 746 в desktop-gate.trx и 1 в focus-gate.trx.
- Native WPF: 47 проверок PASS, 0 binding errors. 24 PNG при 96/144 render DPI; окно 1200x900 и 800x480, selector 580x480 и 440x340.
- Статический контракт shell: PASS. Старое ожидание ConnectionStatus исправлено на существующий реальный binding ConnectionTitle.
- Git diff проверен; источники sources/ и серверные контракты не менялись.

Первичные общие прогоны выявили нестабильность тестов OS focus/animation при активации окон. Итоговый gate выполнялся последовательно; один системный keyboard focus test вынесен в отдельный запуск на той же сборке. Production-код палитры команд и анимации не менялся. При компиляции тестов остаётся существовавшее ранее xUnit1031 в DesktopCredentialVaultTests.cs:347; production Release build не имеет предупреждений.

## Границы evidence

Native probe использует production WPF и SQLite с синтетическими данными; Corporate presentation adapter проверяет интерфейс и сохранение/навигацию без живого корпоративного сервера. Серверный E2E, физический mixed-DPI walkthrough и пользовательское исследование для этого UI-инкремента не запускались. 144 DPI означает отдельный render, не проверку физического монитора. Source snapshot требует base repository для сборки.

API, DTO, бизнес-статусы, схема базы и права не изменены. Проверка отзыва write capabilities подтверждает сохранённый доступ к открытию при Task.Read; завершённая сессия блокирует новое чтение. Dashboard progress/status не повышались. Чужие изменения OPS/HAND в рабочем dashboard сохранены и не включены в commit.
''', encoding='utf-8')

digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
payload = [{'path': p.relative_to(OUT).as_posix(), 'size': p.stat().st_size, 'sha256': digest(p)} for p in sorted(OUT.rglob('*')) if p.is_file()]
manifest = {'name': 'Task UX journey', 'version': '1.0.0', 'base_commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(), 'desktop_tests_passed': len(tests), 'native_checks_passed': len(native['checks']), 'source_scope': scope, 'files': payload}
(OUT / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
for entry in payload:
    assert digest(OUT / entry['path']) == entry['sha256']
files = [*payload, {'path': 'manifest.json', 'sha256': digest(OUT / 'manifest.json')}]
(OUT / 'SHA256SUMS').write_text(''.join(f"{p['sha256']}  {p['path']}\n" for p in files), encoding='utf-8')
archive = Path(str(OUT) + '.zip')
with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED) as z:
    for path in sorted(OUT.rglob('*')):
        if path.is_file(): z.write(path, path.relative_to(OUT).as_posix())
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for entry in files: assert hashlib.sha256(z.read(entry['path'])).hexdigest() == entry['sha256']
archive.with_name(archive.name + '.sha256').write_text(f'{digest(archive)}  {archive.name}\n', encoding='utf-8')
archive.with_name(archive.name + '.validation.json').write_text(json.dumps({'status': 'PASS', 'version': '1.0.0', 'archive_sha256': digest(archive), 'payload_files_verified': len(files), 'desktop_tests_passed': len(tests), 'native_checks_passed': len(native['checks'])}, indent=2) + '\n', encoding='utf-8')
(WORK / 'source-scope.json').write_text(json.dumps(scope, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(f'Package PASS: {len(files)} hashed files, {archive.stat().st_size} bytes')
