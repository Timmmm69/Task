from pathlib import Path
from xml.etree import ElementTree as ET
import datetime
import hashlib
import json
import re
import shutil
import subprocess
import zipfile

TASK = Path(__file__).resolve().parent
REPO = TASK / 'repo'
PROJECT = TASK.parent.parent
EVIDENCE = TASK / 'evidence'
PACKAGE = TASK / 'package'
NAME = '20261022_task_14_4_independent_audit_1.0.0'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def git(*args, cwd=REPO):
    return subprocess.check_output(['git', *args], cwd=cwd)

def digest(data):
    return hashlib.sha256(data).hexdigest()

def put(path, text):
    target = PACKAGE / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding='utf-8', newline='\n')

def copy(source, target):
    dest = PACKAGE / target
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, dest)

def trx(file):
    root = ET.parse(file)
    counters = root.find('.//t:Counters', NS).attrib
    failed = [r.get('testName') for r in root.findall('.//t:UnitTestResult', NS) if r.get('outcome') == 'Failed']
    return {'file': file.relative_to(EVIDENCE).as_posix(),
            'total': int(counters['total']), 'passed': int(counters['passed']),
            'failed': int(counters['failed']), 'skipped': int(counters['notExecuted']),
            'failed_tests': failed}

head = git('rev-parse', 'HEAD').decode().strip()
base = git('rev-parse', 'e9b08b6').decode().strip()
main = git('rev-parse', 'origin/main').decode().strip()
assert not git('status', '--porcelain=v1', '--untracked-files=no').strip(), 'Commit source changes before packaging'
verified = [trx(p) for p in sorted((EVIDENCE / 'verified-release').glob('*.trx'))]
backend = next(t for t in verified if t['total'] == 905)
api = next(t for t in verified if t['total'] == 669)
desktop = trx(EVIDENCE / 'desktop-final/desktop-final.trx')
baseline_ui = trx(EVIDENCE / 'baseline-ui-recheck/baseline-ui.trx')
acceptance = [trx(p) for p in sorted((EVIDENCE / 'final-acceptance').glob('*.trx'))]
assert len(acceptance) == 3 and all(t['failed'] == t['skipped'] == 0 for t in acceptance)
assert backend['passed'] == 905 and api['passed'] == 669
assert desktop['passed'] == 1821 and desktop['failed'] == 1 and desktop['skipped'] == 0
assert baseline_ui['failed_tests'] == desktop['failed_tests'] and baseline_ui['failed'] == 1
assert not git('diff', base, head, '--', 'work/production/tests/Task.Desktop.Tests/Tasks/TaskContextActionsUiTests.cs')
runs = []
for file in sorted(EVIDENCE.glob('*/*.trx')):
    item = trx(file)
    item['receipt'] = file.parent.name
    runs.append(item)
(EVIDENCE / 'test-summary.json').write_text(json.dumps(runs, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

full = [backend, api, desktop]
total_passed = sum(t['passed'] for t in full)
table = '| Проверка | PASS | FAIL | SKIP | Evidence |\n|---|---:|---:|---:|---|\n'
for label, t in zip(['Полный Domain / PostgreSQL 16', 'Полный HTTP / ServiceHosts', 'Полный Desktop до последнего уточнения viewport (89d4142)'], full):
    table += f"| {label} | {t['passed']} | {t['failed']} | {t['skipped']} | `{t['file']}` |\n"
table += f'\n**Итого полных suites: {total_passed} PASS / 1 FAIL / 0 SKIP. Полный gate не является зелёным.** Единственный FAIL — `TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow`, `Assert.True(company.Activate())`, строка 62. Он независимо воспроизведён на неизменённом `e9b08b6` (`baseline-ui-recheck/baseline-ui.trx`); сценарий теста до действий продукта не дошёл. Тест не отключён и не ослаблен.\n\n'
table += 'После последнего уточнения компактного viewport выполнен повторный scoped acceptance на актуальном коде; новые source-изменения после него отсутствуют:\n\n'
table += '| Повторная проверка | PASS | FAIL | SKIP |\n|---|---:|---:|---:|\n'
for label, t in zip(['Domain/PostgreSQL return', 'HTTP return / shared session mapping', 'Desktop return / Today / filtering / native WPF'], acceptance):
    table += f"| {label} | {t['passed']} | {t['failed']} | {t['skipped']} |\n"
table += '\nЭти scoped прогоны перекрываются с полными suites и не прибавляются к общему числу. Сводка всех сохранённых red/green прогонов — `evidence/test-summary.json`; последние результаты — `evidence/final-acceptance/`.\n'

report = (TASK / 'Task_14_Final_Independent_Audit.md').read_text(encoding='utf-8')
report = report.replace('Полный Desktop, финальные source/test файлы', 'Полный Desktop до последнего уточнения viewport (89d4142)')
report = report.replace('@AUDIT_COMMIT@', f'{head} (основное исправление: 89d41421eb668244b72e67f2306959fdc8ef2fa2)')
report = report.replace('@BUILD_RESULTS@', '**PASS: Release solution build, 0 errors.** Исходная сборка до исправлений также прошла; для финального кода сохранён `evidence/final-acceptance/build.log`. Старые предупреждения ASPDEPR004, xUnit1031/xUnit2031 не относятся к возврату.')
report = report.replace('@TEST_RESULTS@', table)
report = report.replace('@NATIVE_GATE_LIMITATION@', 'Полный native gate содержит воспроизводимый исходный сбой активации окна (§9). Он не скрыт успешными scoped tests. Для merge нужно получить успешный интерактивный прогон этого gate либо отдельно принять и оформить ограничение окружения.')
report = report.replace('**К Pull Request готов проверенный локальный инкремент 14.2–14.4 с данным отчётом и раскрытыми ограничениями.**', '**К Draft Pull Request / code review готов проверенный локальный инкремент 14.2–14.4 с данным отчётом и раскрытыми ограничениями. Merge пока не рекомендован: полный native gate не зелёный.**')
report = report.replace('Native screenshots и hit testing обеих вертикальных границ кнопки при', 'Native screenshots и hit testing обеих вертикальных границ кнопки при логических размерах окна (DIP)')
for prefix in ['Task.Infrastructure/', 'Task.Application/', 'Task.Domain/', 'Task.Desktop/']:
    report = report.replace('`' + prefix, '`work/production/src/' + prefix)
assert not re.search(r'@[A-Z_]+@', report)
(TASK / 'Task_14_Final_Independent_Audit.md').write_text(report, encoding='utf-8')
put('Task_14_Final_Independent_Audit.md', report)
put('VERSION', '1.0.0\n')
put('.gitattributes', '* -text\n')
put('reproduction/README.md', 'These are the exact scripts used in work/status_return_14_4. They expect the report workspace layout (repo/, evidence/, sibling canonical project), not execution directly inside outputs/. Raw TRX, build logs, source blobs and patches make the recorded checks inspectable without rerunning the scripts. PostgreSQL data directories are isolated and loopback-only. No remote deployment or push is part of reproduction.\n')
copy(TASK / 'Run-AuditGate.ps1', 'reproduction/Run-AuditGate.ps1')
copy(Path(__file__), 'reproduction/build_package.py')

for receipt in ['defect-repro', 'layout-repro-and-regression', 'final-targeted', 'final-acceptance', 'baseline-ui-recheck', 'desktop-final', 'verified-release']:
    for file in (EVIDENCE / receipt).rglob('*'):
        if file.is_file() and file.suffix in {'.trx', '.json', '.png', '.log'}:
            copy(file, 'evidence/' + file.relative_to(EVIDENCE).as_posix())
for name in ['baseline-build.log', 'baseline-ui-build.log', 'independent-source-evidence.json', 'canonical-baselines-evidence.json', 'prior-package-integrity.json', 'prior-source-overlays.json', 'audited-commit-files.json', 'dashboard-validation.json', 'test-summary.json']:
    copy(EVIDENCE / name, 'evidence/' + name)

inputs = []
for path in [
    'outputs/20261022_task_14_1_status_return_contract_audit_1.0.0/Task_14_1_Status_Return_Contract_Audit.md',
    'outputs/20261022_task_14_2_status_return_contract_1.0.0/Task_14_2_Status_Return_Implementation_Contract.md',
    'outputs/20261022_task_14_2_status_return_contract_1.0.0/Task_14_1_Resolution_Addendum.md',
]:
    file = PROJECT / path
    inputs.append({'path': path, 'sha256': digest(file.read_bytes())})
    copy(file, 'evidence/inputs/' + file.name)

changed = git('diff', '--name-only', base, head).decode('utf-8').splitlines()
assert all(p.startswith('work/production/') or p == '.project-dashboard/roadmap.json' for p in changed)
source = []
for name in changed:
    raw = git('show', f'{head}:{name}')
    put_path = PACKAGE / 'source-overlay' / name
    put_path.parent.mkdir(parents=True, exist_ok=True)
    put_path.write_bytes(raw)
    source.append({'path': name, 'git_blob_sha256': digest(raw)})
(PACKAGE / 'audit-fixes.patch').write_bytes(git('diff', '--binary', base, head))
(PACKAGE / 'implementation-from-main.patch').write_bytes(git('diff', '--binary', main, head, '--', 'work/production', '.project-dashboard/roadmap.json'))
put('source-files.json', json.dumps(source, ensure_ascii=False, indent=2) + '\n')

summary = {'overall': 'PASS_WITH_LIMITATIONS', 'code_commit': head,
           'full_desktop_source_commit': '89d41421eb668244b72e67f2306959fdc8ef2fa2',
           'last_acceptance_source_commit': head,
           'full_suites': full, 'full_passed': total_passed, 'full_failed': 1,
           'full_skipped': 0, 'last_acceptance': acceptance,
           'baseline_foreground_failure': baseline_ui,
           'fixed_defects': ['SEC-14.4-01', 'UI-14.4-02', 'UI-14.4-03', 'UI-14.4-04', 'DOM-14.4-05', 'UI-14.4-06'],
           'open_limitations': ['API-14.4-07', 'DOC-14.4-08', 'foreground-native-gate', 'live-LAN-and-screen-reader-not-run'],
           'ready_for_draft_pr': True, 'merge_recommended': False}
put('validation-summary.json', json.dumps(summary, ensure_ascii=False, indent=2) + '\n')
put('validation-report.md', f'''# Validation report — Task 14.4 v1.0.0

Result: **PASS_WITH_LIMITATIONS**, source `{head}`.

- Full suites: **{total_passed} PASS / 1 FAIL / 0 SKIP**. Full Desktop source snapshot is 89d4142; last compact viewport/focus refinement is verified by the scoped acceptance on {head}. One unchanged foreground-dependent native case fails at company.Activate(), also reproduced on e9b08b6.
- Last acceptance: **{sum(t['passed'] for t in acceptance)} PASS / 0 FAIL / 0 SKIP**, including real PG16 and native WPF return with hit-testing at four supported logical window sizes.
- Release solution build: 0 errors; build logs included.
- Six implementation defects fixed; business requirements and HTTP API schemas unchanged.
- Dashboard order/validation PASS; only DESK-02/API-02 evidence/note updated; progress/status preserved.
- Prior packages: 44 SHA256SUMS checks PASS, source overlays compared to their commit.
- Current package: manifest and SHA256SUMS enumerate every payload; source overlays match exact Git blobs; ZIP CRC and member hashes must pass before publication.
- Git apply checks run after packaging against untouched e9b08b6 baseline and reverse cumulative patch against current source; receipt is stored next to this package in outputs.

Draft PR is reviewable. Merge is not recommended until the native foreground gate is green or its environment limitation is explicitly accepted. Live LAN/multi-client, physical mixed-DPI and actual screen-reader walkthrough remain unverified. See Task_14_Final_Independent_Audit.md for the full decision and defect register.
''')

manifest = {'package': NAME, 'version': '1.0.0', 'result': 'PASS_WITH_LIMITATIONS',
            'created_at': datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=3))).isoformat(timespec='seconds'),
            'repository': 'Timmmm69/Task', 'branch': 'codex/status-return-14-4-audit',
            'origin_main': main, 'server_commit': '10774455af70c2a0d5c586bd6f5af26bbbc0a695',
            'desktop_commit': base, 'audit_source_commit': head,
            'normative_openapi_sha256': digest((EVIDENCE / 'normative-openapi.yaml').read_bytes()),
            'inputs': inputs, 'source_files': source,
            'files': [{'path': f.relative_to(PACKAGE).as_posix(), 'bytes': f.stat().st_size, 'sha256': digest(f.read_bytes())}
                      for f in sorted(PACKAGE.rglob('*')) if f.is_file() and f.name not in {'manifest.json', 'SHA256SUMS'}]}
put('manifest.json', json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
sums = ''.join(f'{digest(f.read_bytes())}  {f.relative_to(PACKAGE).as_posix()}\n' for f in sorted(PACKAGE.rglob('*')) if f.is_file() and f.name != 'SHA256SUMS')
put('SHA256SUMS', sums)
for line in sums.splitlines():
    checksum, relative = line.split('  ', 1)
    assert digest((PACKAGE / relative).read_bytes()) == checksum
for f in manifest['files']:
    assert digest((PACKAGE / f['path']).read_bytes()) == f['sha256']

destinations = [REPO / 'outputs' / NAME, PROJECT / 'outputs' / NAME]
for dest in destinations:
    if dest.exists():
        prior = json.loads((dest / 'manifest.json').read_text(encoding='utf-8'))
        assert prior['package'] == NAME and prior['audit_source_commit'] == head, f'Unowned output: {dest}'
        assert {p.relative_to(dest) for p in dest.rglob('*') if p.is_file()} == {p.relative_to(PACKAGE) for p in PACKAGE.rglob('*') if p.is_file()}
for dest in destinations:
    shutil.copytree(PACKAGE, dest, dirs_exist_ok=True)
    archive = dest.with_name(dest.name + '.zip')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for f in sorted(dest.rglob('*')):
            if f.is_file():
                z.write(f, (Path(NAME) / f.relative_to(dest)).as_posix())
    with zipfile.ZipFile(archive) as z:
        assert z.testzip() is None
        for f in dest.rglob('*'):
            if f.is_file():
                assert digest(z.read((Path(NAME) / f.relative_to(dest)).as_posix())) == digest(f.read_bytes())
    archive.with_name(archive.name + '.sha256').write_text(digest(archive.read_bytes()) + '  ' + archive.name + '\n', encoding='utf-8')
print(json.dumps({'package': NAME, 'files': len(manifest['files']), 'code_commit': head,
                  'full_passed': total_passed, 'full_failed': 1,
                  'last_acceptance_passed': sum(t['passed'] for t in acceptance),
                  'sha256': digest(destinations[-1].with_name(destinations[-1].name + '.zip').read_bytes())}, indent=2))
