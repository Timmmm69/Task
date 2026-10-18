"""Package the scoped source and actual receipts; verify every ZIP payload by SHA-256."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

repo = Path(__file__).resolve().parents[3]
evidence = Path(__file__).resolve().parent
name = '20261018_admin_user_management_11.4.0'
package = repo / 'outputs' / name
package.mkdir(parents=True, exist_ok=True)
scope = [
    '.project-dashboard/roadmap.json',
    'work/production/src/Task.Desktop/Administration/DesktopAdministrationApiClient.cs',
    'work/production/src/Task.Desktop/ViewModels/AdministrationUserTransitions.cs',
    'work/production/src/Task.Desktop/ViewModels/AdministrationUsers.cs',
    'work/production/src/Task.Desktop/ViewModels/AdministrationViewModel.cs',
    'work/production/src/Task.Desktop/Views/AdministrationView.xaml',
    'work/production/tests/Task.Desktop.Tests/Administration/AdministrationAccessTests.cs',
    'work/production/tests/Task.Desktop.Tests/Administration/AdministrationUserEditorTests.cs',
    'work/production/tests/Task.Desktop.Tests/Administration/AdministrationUserWindowsTests.cs',
    'work/production/tests/Task.Desktop.Tests/Administration/DesktopUserManagementApiClientTests.cs',
    'work/production/tests/Task.Tests/PostgresAdministrationRoleTests.cs',
    'work/production/docs/ADMINISTRATION-FINAL-REVIEW-11.4.md',
    'work/verification/11_4/Run-Gate.ps1',
    'work/verification/11_4/Package.py',
    'work/verification/11_4/desktop-serial.runsettings',
]


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def counters(file):
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    tree = ET.parse(evidence / file)
    c = tree.find('.//t:Counters', ns)
    assert c is not None and int(c.get('total')) > 0, file
    return dict(total=int(c.get('total')), passed=int(c.get('passed')),
                failed=int(c.get('failed')), skipped=int(c.get('total')) - int(c.get('executed')),
                failures=[r.get('testName') for r in tree.findall('.//t:UnitTestResult', ns)
                          if r.get('outcome') == 'Failed'], evidence='evidence/' + file)


checks = {key: counters(file) for key, file in {
    'administration': 'administration-final.trx', 'postgres_scoped': 'postgres-scoped.trx',
    'domain_postgres': 'Task.Tests.trx', 'api': 'Task.ServiceHosts.Tests.trx',
    'desktop_first': 'Task.Desktop.Tests.trx', 'desktop_confirmation': 'desktop-confirmation.trx',
    'baseline_desktop_1f42e49': 'baseline-desktop.trx',
    'inbox_isolated': 'inbox-isolated.trx', 'context_isolated': 'task-context-isolated.trx',
    'personal_search_isolated': 'personal-search-isolated.trx', 'desktop_serial': 'desktop-serial.trx',
}.items()}
for key in ('administration', 'postgres_scoped', 'domain_postgres', 'api'):
    assert checks[key]['failed'] == checks[key]['skipped'] == 0, key
for path in scope:
    target = package / 'source' / path
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(repo / path, target)
for file in ['build-final.log', 'postgres-runtime.json', 'postgres-scoped.log', 'administration-final.log',
             'Task.Tests.log', 'Task.ServiceHosts.Tests.log', 'Task.Desktop.Tests.log',
             'desktop-confirmation.log', 'baseline-desktop.log', 'inbox-isolated.log', 'task-context-isolated.log',
             'personal-search-isolated.log', 'desktop-serial.log']:
    target = package / 'evidence' / file
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(evidence / file, target)
for check in checks.values():
    shutil.copy2(evidence / Path(check['evidence']).name, package / check['evidence'])
shutil.copytree(evidence / 'screenshots', package / 'evidence/screenshots', dirs_exist_ok=True)
shutil.copy2(repo / scope[11], package / 'report.md')
(package / 'VERSION').write_text('11.4.0\n', encoding='utf-8')
write_json(package / 'source-scope.json', scope)
(package / 'source.patch').write_bytes(subprocess.check_output(
    ['git', 'diff', '--binary', '1f42e49', '--', *scope], cwd=repo))
implementation = subprocess.check_output(['git', 'rev-parse', '780bf72'], cwd=repo, text=True).strip()
write_json(package / 'validation-report.json', {
    'version': '11.4.0', 'date': '2026-10-18', 'branch': 'feature/admin-user-management',
    'base_commit': '1f42e49', 'implementation_commit': implementation,
    'task_11_status': 'partial_blocked', 'blockers': ['OQ-ADMIN-ROLES-READ'],
    'role_replacement_ui_enabled': False, 'server_contracts_changed': False,
    'release_solution_build': 'passed', 'dashboard_order_and_validation': 'passed',
    'tests': checks,
    'latest_desktop_gate': 'passed_serial' if checks['desktop_serial']['failed'] == 0 else 'not_green',
    'initial_desktop_gate': 'failed: two native focus tests; retained, isolated passes',
    'baseline_comparison': '1f42e49 full desktop passed; inheritance of first-run failures not proven',
    'native_wpf': 'automated real Windows UIA, fake API, 1120x900 and 800x600; screenshots inspected',
    'postgres': 'real PostgreSQL 16, isolated loopback runtime, test databases/runtime roles',
    'manual_windows_acceptance': 'not_run', 'live_wpf_http_postgres_end_to_end': 'not_run',
    'full_product_acceptance': False,
    'pr_readiness': 'safe-scope draft with open OQ; not completion of task 11',
    'remaining': ['authoritative versioned role assignments read contract',
                  'UserRole normative/live response mismatch', 'credential delivery and department picker',
                  'role/resource catalog first-page limit 200', 'manual live Corporate/Narrator/mixed-DPI acceptance',
                  'physical offline and live two-administrator race not run'],
})
records = [dict(path=p.relative_to(package).as_posix(), bytes=p.stat().st_size, sha256=sha(p))
           for p in sorted(package.rglob('*')) if p.is_file() and p.name not in ('manifest.json', 'checks.sha256')]
write_json(package / 'manifest.json', dict(version='11.4.0', task='11.4',
           base_commit='1f42e49', implementation_commit=implementation, files=records))
(package / 'checks.sha256').write_text(''.join(f"{r['sha256']}  {r['path']}\n" for r in records)
    + f"{sha(package / 'manifest.json')}  manifest.json\n", encoding='utf-8')
archive = package.parent / (name + '.zip')
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED) as z:
    for path in sorted(package.rglob('*')):
        if path.is_file():
            z.write(path, path.relative_to(package).as_posix())
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for entry in records:
        assert sha(package / entry['path']) == entry['sha256'], entry['path']
        assert hashlib.sha256(z.read(entry['path'])).hexdigest() == entry['sha256'], entry['path']
    assert z.read('manifest.json') == (package / 'manifest.json').read_bytes()
archive_digest = sha(archive)
archive.with_name(archive.name + '.sha256').write_text(archive_digest + '  ' + archive.name + '\n', encoding='utf-8')
delivery_root = repo.parents[2] / 'outputs'
delivery_root.mkdir(exist_ok=True)
shutil.copytree(package, delivery_root / name, dirs_exist_ok=True)
for path in (archive, archive.with_name(archive.name + '.sha256')):
    shutil.copy2(path, delivery_root / path.name)
assert sha(delivery_root / archive.name) == archive_digest
print(f'Verified {len(records)} entries, ZIP payloads and delivery; SHA-256 {archive_digest}')
