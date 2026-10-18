"""Freeze the successful 11.1-11.4 review before the authorized main push."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

repo = Path(__file__).resolve().parents[3]
evidence = Path(__file__).resolve().parent
name = '20261018_admin_user_management_main_review_1.0.0'
package = repo / 'outputs' / name


def git(*args):
    return subprocess.check_output(['git', *args], cwd=repo, text=True).strip()


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def receipt(file, allow_skips=False):
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    tree = ET.parse(evidence / file)
    c = tree.find('.//t:Counters', ns)
    assert c is not None and int(c.get('total')) > 0, file
    result = dict(total=int(c.get('total')), passed=int(c.get('passed')),
                  failed=int(c.get('failed')), skipped=int(c.get('total')) - int(c.get('executed')),
                  evidence='evidence/' + file)
    assert result['failed'] == 0, result
    if not allow_skips:
        assert result['total'] == result['passed'] and result['skipped'] == 0, result
    if file == 'desktop-serial.trx':
        admin = [r for r in tree.findall('.//t:UnitTestResult', ns)
                 if r.get('testName', '').startswith('Task.Desktop.Tests.Administration.')]
        assert len(admin) == 160 and all(r.get('outcome') == 'Passed' for r in admin)
        result['administration_passed'] = len(admin)
    return result


assert git('branch', '--show-current') == 'feature/admin-user-management'
assert not git('status', '--porcelain', '--untracked-files=no'), 'Tracked changes must be reviewed first'
source = git('rev-parse', 'HEAD')
baseline = git('rev-parse', 'origin/main')
subprocess.run(['git', 'merge-base', '--is-ancestor', baseline, source], cwd=repo, check=True)
assert not git('diff', '--name-only', 'origin/main...HEAD', '--', 'sources',
               'work/production/src/Task.Api', 'work/production/src/Task.Application',
               'work/production/src/Task.Infrastructure'), 'Server/normative scope changed'
subprocess.run(['git', 'diff', '--check', 'origin/main...HEAD'], cwd=repo, check=True)
checks = {key: receipt(file) for key, file in {
    'postgres_scoped': 'postgres-scoped.trx', 'domain_postgres': 'Task.Tests.trx',
    'api': 'Task.ServiceHosts.Tests.trx', 'desktop_serial': 'desktop-serial.trx',
}.items()}
prior = json.loads((evidence / 'prior-package-verification.json').read_text(encoding='utf-8'))
assert len(prior) == 4 and all(not item['failures'] for item in prior)
no_pg = {p.name: receipt('ci-no-postgres/' + p.name, allow_skips=True)
         for p in sorted((evidence / 'ci-no-postgres').glob('*.trx'))}
assert len(no_pg) == 3
format_debt = json.loads((evidence / 'baseline-format-findings.json').read_text(encoding='utf-8'))
assert format_debt['all_match_origin_main'] and format_debt['stage_files_format'] == 'passed'
package.mkdir(parents=True, exist_ok=True)
(package / 'evidence').mkdir(exist_ok=True)
files = ['publication-gate.log', 'build.log', 'postgres-runtime.json', 'postgres-scoped.log',
         'Task.Tests.log', 'Task.ServiceHosts.Tests.log', 'desktop-serial.log',
         'prior-package-verification.json', 'baseline-format-findings.json', 'format-final.log',
         'format-scoped-final.log', 'boundaries.log', 'ci-no-postgres-solution.log',
         'ci-no-postgres-before.trx', 'ci-no-postgres-before.log', 'ci-no-postgres-after.trx',
         'ci-no-postgres-after.log', *[Path(c['evidence']).name for c in checks.values()]]
for file in files:
    shutil.copy2(evidence / file, package / 'evidence' / file)
for file in ['Review-Publication.py', 'Run-Gate.ps1', 'desktop-serial.runsettings']:
    target = package / 'source' / file
    target.parent.mkdir(exist_ok=True)
    shutil.copy2(evidence / file, target)
shutil.copytree(evidence / 'ci-no-postgres', package / 'evidence/ci-no-postgres', dirs_exist_ok=True)
for file in ['work/production/tests/Task.Tests/PostgresAdministrationRoleTests.cs',
             'work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj',
             'work/production/tests/Task.Desktop.Tests/xunit.runner.json']:
    target = package / 'source' / file
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(repo / file, target)
(package / 'VERSION').write_text('1.0.0\n', encoding='utf-8')
write_json(package / 'validation-report.json', {
    'version': '1.0.0', 'date': '2026-10-18', 'reviewed_source_commit': source,
    'origin_main_before_publication': baseline, 'branch': 'feature/admin-user-management',
    'release_solution_build': 'passed', 'tests': checks,
    'ci_without_postgres_solution': no_pg,
    'unique_full_suite_total': sum(checks[k]['passed'] for k in ('domain_postgres', 'api', 'desktop_serial')),
    'remaining_stage_code_findings': [], 'prior_package_integrity': prior,
    'resolved_findings': ['new PostgreSQL role tests failed Windows cheap CI without DB configuration',
                          'default desktop test execution needed shared Windows focus serialization',
                          'whitespace errors in stage files'],
    'whole_solution_format': format_debt,
    'server_and_normative_contracts_changed': False,
    'decision': 'ready_for_authorized_fast_forward_to_main',
    'role_assignments': 'disabled; OQ-ADMIN-ROLES-READ remains open',
    'manual_live_windows_acceptance': 'not_run',
    'desktop_default_runner': 'xunit.runner.json copied to output; serial collections for CI and local runs',
    'production_behavior_changed_in_this_review': False,
})
(package / 'report.md').write_text(f'''# Повторная проверка 11.1–11.4 перед публикацией в main

Версия отчёта 1.0.0, 2026-10-18. Проверен `{source}`.
Исходный origin/main: `{baseline}`. Ветка содержит историю этапов и исправление условий запуска CI.

Повторный просмотр API-клиента, profile draft/PATCH, state transitions, разрешений,
generation/cancellation, idempotency/version и WPF bindings не выявил дефектов в реализованных сценариях.
Проверены подключение к Corporate session/capabilities/connectivity и отсутствие расширения
серверного API, БД, permissions и нормативных sources. Поведение production-кода не менялось;
нормализовано форматирование затронутых файлов.

Найден и исправлен дефект CI: новые role tests без PostgreSQL давали 10 FAIL. Теперь
RequiresPostgresFact/Theory явно пропускают их только при отсутствии конфигурации БД;
assertions сохранены. С реальной БД выполняются все 10 случаев без пропусков.
Также добавлен xunit.runner.json в output desktop test project, чтобы обычный запуск CI
последовательно выполнял коллекции, разделяющие Windows focus/keyboard. Workflow не изменён.
Полный обычный `dotnet test Task.sln -c Release --no-restore` без PostgreSQL прошёл;
ожидаемые PostgreSQL SKIP и все три TRX сохранены отдельно.

Единый запуск `work/verification/11_4/Run-Gate.ps1 -Full`:

| Проверка | PASS | FAIL | SKIP |
|---|---:|---:|---:|
| Domain / реальная PostgreSQL 16 | {checks['domain_postgres']['passed']} | 0 | 0 |
| API / TestHost | {checks['api']['passed']} | 0 | 0 |
| Desktop, последовательные коллекции | {checks['desktop_serial']['passed']} | 0 | 0 |
| Целевые PostgreSQL security/lifecycle | {checks['postgres_scoped']['passed']} | 0 | 0 |

160 административных тестов входят в полный desktop gate, включая настоящий Windows WPF/UIA.
Release solution build прошёл. Старые предупреждения анализаторов/ASP.NET не скрыты в build.log.
Manifest всех четырёх предыдущих пакетов проверены: 151 запись, без несовпадений SHA-256.

Форматирование файлов этапов и project boundaries: PASS. Полный solution format gate
по-прежнему сообщает замечания в {format_debt['affected_files']} неизменённых файлах origin/main.
Их идентичность baseline проверена, список и исходный log сохранены. Поэтому зелёный статус
всего GitHub CI не заявляется; исправление старого форматирования вне scope этапов не выполнялось.

Решение: безопасная реализованная часть 11.1–11.4 готова к разрешённому пользователем
fast-forward push в main. Изменения основной рабочей папки не включаются.

OQ-ADMIN-ROLES-READ остаётся открытым: назначения ролей не включены, опасного PUT нет.
Полное завершение задачи 11 и ручная live Corporate acceptance не заявляются.
Предыдущие контрактные ограничения сохраняются в отчёте 11.4.0.

Этот пакет фиксирует результат до push. Фактический remote HEAD проверяется отдельным fetch
после успешной публикации; разрешение пользователя не подменяется статусом готовности.
''', encoding='utf-8')
records = [dict(path=p.relative_to(package).as_posix(), bytes=p.stat().st_size, sha256=sha(p))
           for p in sorted(package.rglob('*')) if p.is_file() and p.name not in ('manifest.json', 'checks.sha256')]
write_json(package / 'manifest.json', dict(version='1.0.0', reviewed_source_commit=source, files=records))
(package / 'checks.sha256').write_text(''.join(f"{r['sha256']}  {r['path']}\n" for r in records)
    + f"{sha(package / 'manifest.json')}  manifest.json\n", encoding='utf-8')
archive = package.parent / (name + '.zip')
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED) as z:
    for path in sorted(package.rglob('*')):
        if path.is_file(): z.write(path, path.relative_to(package).as_posix())
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for entry in records:
        assert sha(package / entry['path']) == hashlib.sha256(z.read(entry['path'])).hexdigest() == entry['sha256']
digest = sha(archive)
archive.with_name(archive.name + '.sha256').write_text(digest + '  ' + archive.name + '\n', encoding='utf-8')
delivery = repo.parents[2] / 'outputs'
shutil.copytree(package, delivery / name, dirs_exist_ok=True)
for path in (archive, archive.with_name(archive.name + '.sha256')): shutil.copy2(path, delivery / path.name)
assert sha(delivery / archive.name) == digest
print(f'Publication review verified: {len(records)} manifest entries; ZIP SHA-256 {digest}')
