from pathlib import Path
import json,hashlib,shutil,subprocess,zipfile,datetime,xml.etree.ElementTree as ET

root=Path.cwd()
work=root/'work/view_state_1'
out=root/'outputs/20261008_view_state_1.0.0'
out.mkdir(parents=True,exist_ok=True)
(out/'.gitattributes').write_text('* binary\n',encoding='utf-8')
desktop='work/production/src/Task.Desktop/'
names=['Infrastructure/ViewStateStore.cs','Infrastructure/ViewState.cs','MainWindow.xaml','MainWindow.xaml.cs',
       'Modes/CorporateApplicationContext.cs','Modes/PersonalApplicationContext.cs']
names += ['Views/'+name+'.xaml' for name in ['InboxView','ObjectLinksView','PersonalProjectsView','PersonalRemindersView',
          'PersonalTasksView','PersonalWorkspaceView','ProjectsView','TaskCardEditorView','TaskWorkspaceView','TodayView','WorkHubView']]
paths=[desktop+name for name in names]+['work/production/tests/Task.Desktop.Tests/'+name for name in
       ['ViewState/ViewStateStoreTests.cs','ViewState/ViewStateUiTests.cs','Tasks/DesktopTasksApiClientTests.cs']]
for name in paths:
    target=out/'source-snapshot'/name
    target.parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(root/name,target)
for name in ['build.log','desktop-tests.log','new-tests.log','server-tests.log']:
    shutil.copyfile(work/name,out/name)
for source in [work/'results',work/'evidence']:
    shutil.copytree(source,out/source.name,dirs_exist_ok=True)
head=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
patch=subprocess.check_output(['git','diff','--binary','--']+paths)
(out/'tracked-changes.patch').write_bytes(patch)
(out/'VERSION').write_text('1.0.0\n',encoding='utf-8')
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
counts={}
for name in ['desktop','server-lists']:
    tree=ET.parse(work/'results'/f'{name}.trx')
    counter=tree.find('t:ResultSummary/t:Counters',ns)
    counts[name]=counter.attrib
assert counts['desktop']['passed']=='711' and counts['desktop']['failed']=='0'
assert counts['server-lists']['passed']=='163' and counts['server-lists']['failed']=='0'
report='''# Device-local UI view state — 1.0.0

Отчёт фиксирует проверенное состояние до публикации в main; итоговый commit/push сообщается отдельно. Backend API, PostgreSQL, OpenAPI, domain entities и UserSettings не изменены. Полный пользовательский quality gate не объявляется закрытым: два ручных сценария ниже остаются NOT RUN.

## Хранение и политика

`%LOCALAPPDATA%\\Task\\view-state\\<sha256>.json`, либо `view-state` внутри существующего `TASK_DESKTOP_DATA_DIRECTORY`.
Существующее desktop-local хранение содержит независимые versioned файлы режима и адреса сервера; универсального UI preference store нет. Добавлен минимальный `IViewStateStore` и его desktop implementation, без новых dependencies. DPAPI vault содержит credentials, а не UI или business cache; он не используется для этой функции. Существующая реализация не содержит persistent encrypted corporate business cache; calendar cache живёт в памяти и к preferences не относится.

Filename: SHA-256 от length-prefixed authority, opaque user GUID, device namespace. Corporate authority включает endpoint и organization GUID; Personal использует отдельную authority и persistent LocalActorId. Device namespace — machine name + Windows user SID, хешируемые в filename. Login/display name, device key и tokens не используются.

UI preferences переживают обычный logout, как существующие несекретные mode/server preferences, но namespace другого authenticated user/organization/server полностью отдельный. Store создаётся заново для каждого shell; его in-memory состояние не переносится между пользователями. Credential cleanup остаётся действующим. Personal backup не включает UI preferences.

## Schema и ключи

Schema `version: 1`; `widths: {fixed-key: double}`, `sections: {fixed-key: bool}`, `sorts: {fixed-surface: {Field, Descending}}`.
Ключи имеют вид `work-links/v1/type`, `personal-links/v1/title`, `today/v1/queue`, `projects/v1/members`.
Только compile-time allowlist; object IDs, task titles, DTO, paths, comments, searches и произвольные disclosure keys отсутствуют. Размер чтения ограничен 32 KiB, JSON depth — 8; количество записей ограничено allowlist. Unknown/stale IDs игнорируются. View version входит в fixed key.

Диск работает на thread-pool workers. Resize changes только отмечаются в памяти; финальная пользовательская DIP-ширина сохраняется после завершённого жеста. Отменённые жесты и не-resizable колонки игнорируются. Auto-measure/layout не записываются. Есть 300 ms debounce, snapshot + flushed temporary file + atomic overwrite; crash не делает partial файл видимым. Orphan temporary file не восстанавливается. Current MinWidth/MaxWidth проверяются при save/restore. Колонки восстанавливаются после создания на Loaded; Star resize сохраняет ActualWidth в DIP. Левая граница учитывает предыдущую visible колонку в display order.

Повреждённое значение preference диагностируется через non-sensitive Trace warning и отбрасывается. Нечитаемый/неподдерживаемый файл удаляется, используются defaults. Ошибка записи не уничтожает предыдущий атомарный файл; повтор возможен при следующем изменении/flush. ResetAsync очищает состояние; существующей команды Reset layout нет, Reset filters не затронут.

## Surfaces

- Width/sort: полный список связей в Corporate Work Hub и Personal Workspace. Поля TypeLabel/Title, один primary sort, оба направления; сортировка повторно применяется при смене ItemsSource. Corporate links client отвергает `hasMore=true`, поэтому сортировка полного набора допустима.
- Today: очередь и inspector; стандартное исходное состояние expanded, accessible ExpandCollapse controls, размеры панелей учитывают сворачивание.
- Projects: participants и related tasks.
- Task workspace: checklist, subtasks, files, predecessors, comments, history; Task editor: дополнительное planning.
- Main Tasks/Inbox inspectors; Personal task details/planning, project storage, reminders creation, workspace actions/links/backup.
- Visibility, capabilities, object loading и permission checks не изменены. Hidden section не становится видимой через preference; restore расширения происходит только когда текущая видимость его допускает. Состояние отдельных TreeView объектов не сохраняется.

## Серверная сортировка: объективное ограничение

Текущий `/api/v1/tasks` явно отвергает non-empty `sort` и `filter`; поддерживает cursor pagination в canonical server order. Выбор сортировки для него отсутствует. Users/devices поддерживают только стабильный `id/+id`, пользовательского выбора тоже нет. Добавлять произвольную сортировку без backend change невозможно, поэтому эти списки продолжают использовать канонический серверный порядок. UI sort preferences не применяются к отдельной странице и не добавляют неподдерживаемый query.

## Validation

- Entire Task.sln Release build: PASS.
- Desktop UI/settings/list/auth regressions + all new tests: **711 PASS, 0 FAIL, 0 SKIP**.
- New store tests, native WPF lifecycle probe и cursor-query regression: **15 PASS** (включены в desktop totals). После последних guard/accessibility правок весь desktop suite повторно прошёл.
- Server list endpoint/security/pagination tests: **163 PASS, 0 FAIL, 0 SKIP**.
- Width: restart, Min/Max, changed current bounds, unknown/deleted/new IDs, corrupt values, numeric DIP schema, non-resizable, auto/star, left/right grips и cancellation.
- Sort: ascending/descending, unsupported key fallback, data refresh; отдельная query regression проверяет две страницы `/tasks`, точные запросы без sort, opaque cursor, отсутствие client reorder и отсутствие влияния другого surface.
- Sections: expanded/collapsed restart, unknown IDs, visibility/permission-hidden gate, native UIA ExpandCollapse, detach/reload.
- Isolation: separate user, device, authority, surface/version; corrupted-store recovery и restart.
- Performance/lifetime: 200 edits дают один debounced write, setter не создаёт файл синхронно, disk I/O только worker, descriptor/routed handlers detached on Unloaded, close ждёт async flush, static event subscriptions отсутствуют.
- Crash/write safety: previous committed snapshot survives denied atomic replacement; retry succeeds; orphan partial tmp ignored.
- Self-review и `git diff --check`: PASS. Сохранены исходные пользовательские правки; источники sources не менялись.
- Native Today snapshots 96/144 raster DPI: просмотрены; это offscreen native control rendering, а не физическая mixed-monitor DPI проверка.

## Непроверенные обязательные ручные сценарии

1. **NOT RUN:** ручная end-to-end инспекция paginated list с запущенным production backend/PostgreSQL. В этой сессии работающего backend нет; использованы native WPF probe, desktop HTTP/query regression и actual endpoint TestServer tests с fake persistence. Это не объявляется ручным live-server acceptance.
2. **NOT RUN:** перенос окна между физическими мониторами с разным DPI. Numeric DIP representation и 96/144 raster rendering проверены; hardware topology не проверена.

Реализация доступных текущему API сценариев проверена указанными tests; полный requested manual quality gate остаётся открытым. Screenshots не содержат реальных business data. Manifest перечисляет payload SHA-256; ZIP перечитан и хеши проверены.
'''
(out/'VALIDATION_REPORT.md').write_text(report,encoding='utf-8')
validation={'version':'1.0.0','base_commit':head,'implementation':'PASS','requested_full_quality_gate':'INCOMPLETE',
            'desktop':counts['desktop'],'server_lists':counts['server-lists'],
            'manual_live_server_list':'NOT_RUN','physical_mixed_dpi':'NOT_RUN','backend_contract_changes':False}
(out/'validation.json').write_text(json.dumps(validation,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
payload=[]
for p in sorted(out.rglob('*')):
    if p.is_file() and p.name not in ['manifest.json','SHA256SUMS.txt']:
        payload.append({'path':p.relative_to(out).as_posix(),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
manifest={'package':'Task desktop view state','version':'1.0.0','base_commit':head,'source_files':len(paths),'files':payload}
(out/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
hashes=payload+[{'path':'manifest.json','sha256':hashlib.sha256((out/'manifest.json').read_bytes()).hexdigest()}]
(out/'SHA256SUMS.txt').write_text(''.join(x['sha256']+'  '+x['path']+'\n' for x in hashes),encoding='utf-8')
archive=out.parent/(out.name+'.zip')
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
    for p in sorted(out.rglob('*')):
        if p.is_file(): z.write(p,p.relative_to(out).as_posix())
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for x in payload: assert hashlib.sha256(z.read(x['path'])).hexdigest()==x['sha256']
archive.with_suffix('.zip.sha256').write_text(hashlib.sha256(archive.read_bytes()).hexdigest()+'  '+archive.name+'\n',encoding='utf-8')
print(json.dumps({'source_files':len(paths),'payload_files':len(payload),'zip_sha256':hashlib.sha256(archive.read_bytes()).hexdigest()}))
