from pathlib import Path
import hashlib, json, shutil, subprocess, zipfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
out = root / 'outputs/20261010_task_ux_five_1.0.0'
assert not out.exists(), 'Use a new version; never overwrite evidence.'
out.mkdir(parents=True)
(out / '.gitattributes').write_bytes(b'* binary\n')
scope = [p for p in subprocess.check_output(['git', 'diff', '--cached', '--name-only'], cwd=root).decode().splitlines() if not p.startswith('outputs/')]
assert scope and all(p.startswith(('work/production/', 'work/ux_journey_1/UiProbe/', 'work/ux_five_1/')) or p == '.project-dashboard/roadmap.json' for p in scope)
for name in scope:
    dest = out / 'source-snapshot' / name
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(subprocess.check_output(['git', 'show', ':' + name], cwd=root))
(out / 'changes.patch').write_bytes(subprocess.check_output(['git', 'diff', '--cached', '--binary', '--', *scope], cwd=root))
evidence = root / 'work/ux_five_1/evidence'
for name in ['desktop-final.trx', 'focus-final.trx', 'targeted-final2.trx', 'release-build.log', 'shell-contract.log', 'native-publish.log']:
    dest = out / 'evidence' / name
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(evidence / name, dest)
native = json.loads((evidence / 'native-publish/ui-results.json').read_text(encoding='utf-8-sig'))
assert native['status'] == 'PASS' and not native['bindingErrors']
dest = out / 'evidence/native'
dest.mkdir()
for file in (evidence / 'native-publish').iterdir():
    if file.suffix == '.png' or file.name == 'ui-results.json': shutil.copyfile(file, dest / file.name)
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
counts = {}
for name in ['desktop-final', 'focus-final', 'targeted-final2']:
    counters = ET.parse(evidence / (name + '.trx')).find('.//t:Counters', ns).attrib
    assert int(counters['failed']) == 0 and counters['total'] == counters['passed']
    counts[name] = int(counters['passed'])
assert counts['desktop-final'] + counts['focus-final'] == 756 and counts['targeted-final2'] == 9
(out / 'VERSION').write_text('1.0.0\n', encoding='utf-8')
(out / 'README.md').write_text('Task — five form improvements. Source overlay and verified evidence; not a portable release. See validation-report.md.\n', encoding='utf-8')
(out / 'validation-report.md').write_text(f'''# Task: пять улучшений форм — validation 1.0.0

PASS. Проверен итоговый diff; scope — только эти улучшения. Sources не менялись.

- Календарь WPF, ручной ввод, Сегодня/Завтра: дата без времени, пустое значение, неверный день без исправления, ScheduleZone/local timezone и переходы DST; точное начало ограничивает выбор дня.
- Enter/Ctrl+Enter: команды и CanExecute активной видимой формы, multiline Enter в задачах/проектах обоих пространств, busy/repeat и IME rejection; ошибка и новый черновик во время запроса сохраняются. Отложенный focus не входит в скрытый view/изменённый context.
- Corporate footer находится вне ScrollViewer и в границах окна 800x480; поля прокручиваются. Существующие retry/conflict/discard команды сохранены.
- Checkbox использует прежний versioned command. Native Space, возврат в невыполненное состояние и реальная SQLite-ошибка подтверждают отсутствие ложной галочки. Удаление отдельно; доступное имя берётся из текста, завершённый текст зачёркнут без снижения контраста.
- Personal filter default off, только «Задачи»: completed/cancelled, счётчик/объяснение/показ скрытых, сохранность редактора/capture/checklist drafts. Настройка не сохраняется; существующие установки показывают все задачи при запуске.

Фактические проверки: 9 targeted tests (входят в общий набор); 755 desktop regression + 1 OS-focus test отдельным последовательным процессом; Release build 0 errors/0 warnings; shell contract PASS. Native WPF: {len(native['checks'])} checks PASS, {len(list(dest.glob('*.png')))} PNG, bindingErrors=[]; реальные системные клавиши, размеры 1200x900 и 800x480, 96/144 DPI renders. Основные normal/minimum/error/checkbox/filter screenshots визуально осмотрены.

Границы: synthetic local fixtures; Corporate presentation adapters проверяют интерфейс и существующие VM/commands без живого сервера компании/credentials. 144 DPI — render, не физический mixed-monitor тест. IME/repeat проверены через классификацию клавиш, не установкой каждого IME. High contrast: checkbox использует стандартные WPF цвета и зачёркивание без opacity; отдельное переключение темы Windows не выполнялось. Existing xUnit1031 warning в DesktopCredentialVaultTests не изменялся. Пакет не содержит installer/portable binary.

Dashboard: только evidence/note/timestamp DESK-05, QA-04, PROD-02; progress/status без повышения. npm run dashboard:order и dashboard:validate PASS. Чужие локальные dashboard правки исключены из index.
''', encoding='utf-8')
def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()
entries = [{'path': p.relative_to(out).as_posix(), 'size': p.stat().st_size, 'sha256': digest(p)} for p in sorted(out.rglob('*')) if p.is_file()]
(out / 'manifest.json').write_text(json.dumps({'version': '1.0.0', 'status': 'PASS', 'baseCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root).decode().strip(), 'files': entries}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
entries.append({'path': 'manifest.json', 'size': (out/'manifest.json').stat().st_size, 'sha256': digest(out/'manifest.json')})
(out / 'SHA256SUMS').write_text(''.join(f"{e['sha256']}  {e['path']}\n" for e in entries), encoding='utf-8')
archive = out.parent / (out.name + '.zip')
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
    for file in sorted(out.rglob('*')):
        if file.is_file(): z.write(file, file.relative_to(out).as_posix())
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for e in entries:
        assert digest(out / e['path']) == e['sha256']
        assert hashlib.sha256(z.read(e['path'])).hexdigest() == e['sha256']
sha = digest(archive)
Path(str(archive) + '.sha256').write_text(sha + '  ' + archive.name + '\n', encoding='utf-8')
Path(str(archive) + '.validation.json').write_text(json.dumps({'status': 'PASS', 'version': '1.0.0', 'sha256': sha, 'zipCrc': 'PASS', 'entriesVerified': len(entries), 'desktopTests': 756, 'nativeChecks': len(native['checks'])}, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'package': out.name, 'sha256': sha, 'verifiedFiles': len(entries), 'nativeChecks': len(native['checks'])}))
