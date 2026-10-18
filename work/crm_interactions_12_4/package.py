from pathlib import Path
import hashlib, json, shutil, subprocess, zipfile, xml.etree.ElementTree as E

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / 'work/crm_interactions_12_4'
OUT = ROOT / 'outputs/20261018_crm_interactions_12_4_1.0.0'
NS = {'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, encoding='utf-8').strip()
def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True); path.write_text(text, encoding='utf-8')
def stats(path):
    return {k:int(v) for k,v in E.parse(path).find('.//t:Counters',NS).attrib.items()}

OUT.mkdir(parents=True, exist_ok=True)
commit = git('rev-parse','HEAD')
files = git('diff','--name-only','origin/main','HEAD').splitlines()
assert all(p.startswith(('work/','.project-dashboard/')) for p in files)
assert not any(p.startswith('sources/') for p in files)
for rel in files:
    source=ROOT/rel
    if source.is_file():
        target=OUT/'source'/rel; target.parent.mkdir(parents=True,exist_ok=True); shutil.copy2(source,target)
records=[]
for source in sorted((WORK/'evidence').rglob('*')):
    if source.is_file() and source.suffix.lower() in ('.trx','.log','.png'):
        target=OUT/'evidence'/source.relative_to(WORK/'evidence');target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
        if source.suffix.lower()=='.trx': records.append({'path':target.relative_to(OUT).as_posix(),**stats(source)})
final_desktop=next(r for r in records if '/desktop-final/' in r['path'])
final_api=next(r for r in records if '/release-final/' in r['path'] and r['total']==644)
final_db=next(r for r in records if '/release-final/' in r['path'] and r['total']==878)
assert [r['passed'] for r in [final_desktop,final_api,final_db]]==[1594,644,878]
assert all(r['failed']==0 and r['notExecuted']==0 for r in [final_desktop,final_api,final_db])
write(OUT/'test-runs.json',json.dumps(records,ensure_ascii=False,indent=2)+'\n')
write(OUT/'source-scope.json',json.dumps({'branch':git('branch','--show-current'),'commit':commit,'base':git('rev-parse','origin/main'),'files':files},ensure_ascii=False,indent=2)+'\n')
write(OUT/'commit-log.txt',git('log','--oneline','origin/main..HEAD')+'\n')
patch=subprocess.check_output(['git','diff','--binary','origin/main','HEAD'],cwd=ROOT)
(OUT/'branch.patch').write_bytes(patch)
report=f'''# Task CRM 12.4 — validation report 1.0.0

Дата: 2026-10-18 (Europe/Minsk). Ветка: `feature/crm-contacts-workspace`.
Коммит 12.4: `{commit}`. Основа: `{git('rev-parse','origin/main')}`.

Реализация основных AC-1–AC-7 проверена. Ветка готова к Pull Request в пределах описанного
контрактного сценария. Публикация/merge в main в этом задании не выполнялись.
Полная live-приёмка установленного клиента не заявляется.

| Этап | Статус | Основные изменённые файлы | Проверка | Коммит |
|---|---|---|---|---|
| 12.1 Контакты | Реализован, регрессия проверена | ContactCardViewModel, WorkHubContacts, DesktopContactDetailsClient, WorkHubView | Contact card/client/nav + native WPF, общий финальный gate | 935e479 |
| 12.2 Каналы/адреса | Реализован в API-границах | ContactCardRelations, DesktopContactRelationsClient, ContactRelationsView | CRUD каналов, GET/POST адресов, версии, endpoint tests, native WPF | 8764a51 |
| 12.3 Компании | Реализован, реальная БД теперь проверена | CompaniesViewModel, CompanyContactRelations, DesktopCompaniesClient, CompaniesView, PostgresProductApiRelations | create/PATCH, связь/unlink, object scope, native WPF + PostgreSQL | 3c66251 |
| 12.4 Взаимодействия | Реализован основной согласованный сценарий | DesktopInteractionsClient, InteractionsViewModel, WorkHubInteractions, InteractionsView, WorkHubSearch, ModeSwitchGuard, тесты | read/create/PATCH/idempotency/CAS/scopes/navigation/WPF, 3116 PASS по финальным запускам | {commit[:7]} |

Полный перечень файлов всей ветки — `source-scope.json`, конечные версии — `source/`, полный diff — `branch.patch`.

## Реально выполненные проверки

- Release solution build в рамках `dotnet test work/production/Task.sln -c Release --no-restore` — PASS.
- Domain/infrastructure + реальная PostgreSQL 16: **878 PASS / 0 FAIL / 0 SKIP** (`{final_db['path']}`).
- API/route/permission/контрактные tests: **644 PASS / 0 FAIL / 0 SKIP** (`{final_api['path']}`).
- Последний полный desktop/WPF/Personal SQLite набор: **1594 PASS / 0 FAIL / 0 SKIP** (`{final_desktop['path']}`).
- Итого **3116 PASS**, без суммирования повторных запусков. Это результаты финальных запусков трёх проектов;
  один единый solution-запуск не был полностью зелёным из-за transient Window.Activate теста (ниже).
- Native WPF: contact/company/interaction, view/edit/create/conflict, 1100×1000 и 800×600,
  привязки без System.Windows.Data Error, достижимость кнопки сохранения. Снимки — `evidence/desktop-final/`.
- Проверены DI/runtime interfaces реального partial DesktopWorkApiClient, владение дочерними VM,
  отмена/Dispose, capability/session invalidation, personal adapter isolation.
- `git diff --check` — PASS; diff ограничен CRM и сопутствующей защитой черновиков/навигацией.
- `npm run dashboard:order`, `npm run dashboard:validate` — PASS; изменён только PROD-05, progress/status сохранены.
- Пакет проверяется побайтово по SHA-256 manifest, архив также проверяется после создания.

## Независимый review и исправления

Отдельный агент выполнил read-only review предыдущих этапов и новой реализации.
Исправлены потеря черновиков каналов/адресов/подтверждения удаления канала и компаний при смене режима;
добавлены mode-switch tests, включая взаимодействия. Добавлена защита после вложенной навигации к
контрагенту; тест подтверждает приоритет нового перехода между двумя взаимодействиями одного контрагента.
Подключён глобальный маршрут `interaction -> contacts` и regression test через shell/search.
Корпоративный поиск открывает контакт прямым GET вместо обхода до 500 страниц.
Для настоящего PersonalWorkClient сохранён локальный сценарий и добавлен SQLite regression test.

## История неуспешных запусков

Исходные receipts сохранены в `test-runs.json` и `evidence/`, а не заменены успешными.
Первый PostgreSQL initdb не поддержал кириллический путь; исправлено использованием Windows short path.
Первый общий gate: domain 876 PASS/2 FAIL (новые тесты ожидали неверные 400/422 вместо фактических 422/404);
API 644 PASS; desktop 1586 PASS/6 FAIL (старые page-based навигационные fixtures, прежнее отсутствие маршрута
interaction и transient Window.Activate). Ожидания/fixtures приведены к подтверждённому прямому GET,
новый маршрут явно проверен. Профильный followup: 61 PASS/1 FAIL — счётчик list-запросов включал
автоматическую активацию списка; тест теперь сравнивает счётчик до/после навигации.
Повторный solution gate: domain 878 PASS, API 644 PASS, desktop 1592 PASS/1 FAIL:
`TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow`, строка 62,
Windows отказала в активации окна (`company.Activate()`). Отдельный baseline 3c66251 тест прошёл 1/1;
последний полный desktop с финальным кодом прошёл 1594/1594. Устойчивый дефект продукта не воспроизведён;
полученные FAIL не скрыты и ранние общие прогоны не объявляются успешными.
Одна промежуточная попытка пересборки конфликтовала с ещё работающим testhost (MSB3027);
последующая последовательная сборка/полный desktop прогон успешно завершены.
Существующие analyzer/deprecation warnings тестовой инфраструктуры сохранены, новых ошибок сборки нет.

## Контрактные и функциональные границы

1. В GET interactions нет `counterpartyObjectId` filter. UI показывает точные совпадения только
   текущей страницы `limit=50`, с явной подписью неполноты и ручным переходом к следующей странице.
   Частичная/пустая выборка не выдаётся за полную историю. Неограниченной фоновой загрузки нет.
2. Имена участников — только доступные активные контакты/компании первых 20 связей, без UUID
   и раскрытия hidden объектов. Остальные типы/связи помечены как не загруженные.
3. Создание отправляет пустых участников. Смена участников/контрагента, delete/restore редактора
   не реализованы в этом инкременте; существование соответствующих endpoints/UX не отрицается.
4. PATCH сохраняет идентичность/участников, version/If-Match; конфликт не перезаписывается.
   POST повторяется с неизменными payload/key. Success показывается только по проверенному ответу сервера.
5. Permissions: `Contact.Read`, `Interaction.Create`, `Interaction.Update`; другие CRM-команды
   используют `Contact.Create/Update`. Без соответствующих capabilities команды недоступны;
   объектные ограничения/tenant isolation перепроверяются сервером и интеграционными tests.
6. Корпоративных persistent cache/offline write queue нет. Следующий шаг остаётся полем Interaction.
   Воронки/сделки и синхронизация с задачами, событиями, комментариями/уведомлениями не добавлялись.
7. Адреса — только существующие GET/POST. Бизнес-требования, API/DTO и схема/миграции БД в 12.4 не менялись.
   Во всей ветке 12.3 усилил серверный scope-check при unlink контакта без изменения контракта.

## Что не выполнялось

- Установленный Windows-клиент с live Corporate HTTP API/реальной организацией как единый end-to-end сеанс.
- Физический mixed-DPI, Narrator и ручная пользовательская приёмка.
- Бенчмарк production-объёма и полный обход истории: вместо него проверены page/cursor границы,
  bounded navigation и явно ограниченный сценарий.
- UI delete/restore/reassignment/participant editing; они не выдаются за реализованные функции.

Блокирующих дефектов в проверенном основном сценарии не осталось. Это не заявление о выполнении
перечисленной live/manual приёмки и дополнительных команд редактора. Общая готовность всего продукта
не повышалась по результату этого пакета.
'''
write(OUT/'validation-report.md',report)
write(OUT/'validation.json',json.dumps({'version':'1.0.0','commit':commit,'coreAcceptance':'passed_with_documented_limits','finalProjects':{'desktop':final_desktop,'api':final_api,'postgres':final_db},'totalFinalPassed':3116,'singleSolutionRunAllGreen':False,'liveCorporateAcceptance':'not_run','manualAcceptance':'not_run'},ensure_ascii=False,indent=2)+'\n')
entries=[{'path':p.relative_to(OUT).as_posix(),'bytes':p.stat().st_size,'sha256':digest(p)} for p in sorted(OUT.rglob('*')) if p.is_file() and p.name not in ('manifest.json','manifest.sha256')]
write(OUT/'manifest.json',json.dumps({'version':'1.0.0','commit':commit,'files':entries},ensure_ascii=False,indent=2)+'\n')
write(OUT/'manifest.sha256',digest(OUT/'manifest.json')+'  manifest.json\n')
for item in entries:
    assert digest(OUT/item['path'])==item['sha256']
archive=Path(str(OUT)+'.zip')
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
    for p in sorted(OUT.rglob('*')):
        if p.is_file():z.write(p,p.relative_to(OUT).as_posix())
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for item in entries: assert hashlib.sha256(z.read(item['path'])).hexdigest()==item['sha256']
write(Path(str(archive)+'.sha256'),digest(archive)+'  '+archive.name+'\n')
print(json.dumps({'output':str(OUT),'archive':str(archive),'files':len(entries),'sha256':digest(archive),'verified':True},ensure_ascii=False))
