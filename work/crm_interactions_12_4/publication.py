from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as E
import zipfile

root = Path(__file__).resolve().parents[2]
out = root / 'outputs/20261019_crm_publication_review_1.0.0'
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def git(*args):
    return subprocess.check_output(['git', *args], cwd=root, encoding='utf-8').strip()
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
def write(name, data):
    path = out / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(data, encoding='utf-8')

run = sorted((root / 'work/crm_interactions_12_4/evidence').glob('run-*'))[-1]
results = []
for path in sorted(run.glob('*.trx')):
    tree = E.parse(path)
    counts = {k: int(v) for k, v in tree.find('.//t:Counters', ns).attrib.items()}
    results.append({'receipt': path.name, **counts})
assert len(results) == 3, results
assert sorted(r['total'] for r in results) == [644, 878, 1599], results
assert all(r['total'] == r['passed'] and r['failed'] == 0 and r['notExecuted'] == 0 for r in results)
out.mkdir(parents=True, exist_ok=True)
base = git('rev-parse', 'a6ea681')
head = git('rev-parse', 'HEAD')
files = git('diff', '--name-only', base, head).splitlines()
assert all(p.startswith(('work/', '.project-dashboard/')) for p in files)
for rel in files:
    dest = out / 'source' / rel
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(root / rel, dest)
shutil.copytree(run, out / 'evidence', dirs_exist_ok=True)
(out / 'branch.patch').write_bytes(subprocess.check_output(['git', 'diff', '--binary', base, head], cwd=root))
write('source-scope.json', json.dumps({'base': base, 'head': head, 'files': files}, ensure_ascii=False, indent=2) + '\n')
write('test-results.json', json.dumps(results, ensure_ascii=False, indent=2) + '\n')
write('validation-report.md', f'''# CRM 12.1–12.4 — проверка перед публикацией, 1.0.0

Дата отчёта: 2026-10-19, Europe/Minsk. Тестовый прогон: 2026-10-18.
Проверенная ветка: feature/crm-contacts-workspace.
Base origin/main до публикации: `{base}`. Проверенный HEAD: `{head}`.

| Проверка | Результат |
|---|---|
| Общий diff 12.1–12.4 | Проверен, вне CRM/защиты черновиков расширений нет |
| Независимый read-only review | Подтверждённых оставшихся блокеров нет |
| Release solution build | PASS |
| Реальная PostgreSQL 16, domain/infrastructure | 878 PASS, 0 FAIL, 0 SKIP |
| API/контрактные/permission tests | 644 PASS, 0 FAIL, 0 SKIP |
| Desktop/WPF/Personal SQLite | 1599 PASS, 0 FAIL, 0 SKIP |
| Весь последовательный solution gate | 3121 PASS, 0 FAIL, 0 SKIP |
| git diff --check | PASS |
| dashboard:order и dashboard:validate | PASS, progress/status не повышались |

Проверены карточки/редактирование контактов, каналы и адреса в пределах API,
компании и связи, взаимодействия, версии/конфликты, идемпотентные повторы,
tenant/object scope, отсутствие команды без permission, отмена/поздние ответы,
сохранение черновиков, глобальная навигация и регрессия личного режима.
Native WPF receipts и снимки 1100×1000 / 800×600 находятся в evidence/.

При повторном review исправлен дополнительный дефект: выбранный контакт из последующей
страницы раньше удерживался при refresh первой страницы без проверки object access.
Теперь прямой GET подтверждает доступ перед сохранением выбранной строки и черновика.
При 401 очищается сессия; при 403/404/trashed удаляются старый выбор, карточка и черновик.
Смена выбора во время проверки отменяет применение старого ответа. Пять новых тестов
покрывают эти состояния; существующий тест сохранения разрешённого черновика также проходит.

Тестовые проекты запускаются последовательно (-m:1). Старые неуспешные receipts, включая
transient Window.Activate, остаются в пакете этапа 12.4 и не объявляются успешными.
Текущий единый solution gate полностью прошёл, без исключения тестов из набора.

Контрактные ограничения сохранены: страница interactions limit=50 не является полной
историей; серверного counterpartyObjectId-фильтра нет. Участники отображаются ограниченно,
команды изменения участников/переназначения/delete/restore не добавлены. Адреса остаются GET/POST.
API/DTO, схема БД, миграции и бизнес-требования в review не изменялись.
Live-приёмка установленного клиента с Corporate API, Narrator и physical mixed-DPI не выполнялась.

Ветка допущена к fast-forward push в main. Факт публикации подтверждается отдельно
совпадением remote main и проверенного HEAD, без force push.
''')
entries = [{'path': p.relative_to(out).as_posix(), 'sha256': sha(p), 'bytes': p.stat().st_size}
           for p in sorted(out.rglob('*')) if p.is_file() and p.name not in ('manifest.json', 'manifest.sha256')]
write('manifest.json', json.dumps({'version': '1.0.0', 'head': head, 'files': entries}, ensure_ascii=False, indent=2) + '\n')
write('manifest.sha256', sha(out / 'manifest.json') + '  manifest.json\n')
archive = Path(str(out) + '.zip')
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
    for path in sorted(out.rglob('*')):
        if path.is_file():
            z.write(path, path.relative_to(out).as_posix())
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for item in entries:
        assert sha(out / item['path']) == item['sha256'] == hashlib.sha256(z.read(item['path'])).hexdigest()
Path(str(archive) + '.sha256').write_text(sha(archive) + '  ' + archive.name + '\n', encoding='utf-8')
print(json.dumps({'head': head, 'passed': 3121, 'files': len(entries), 'sha256': sha(archive), 'output': str(out)}, ensure_ascii=False))
