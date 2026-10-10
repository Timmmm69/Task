import hashlib,json,shutil,subprocess,zipfile
from pathlib import Path
import xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[2]
WORK=ROOT/'work/ux_actions_6_8'
OUT=ROOT/'outputs/20261010_task_context_actions_1.0.0'
VERSION='1.0.0'
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def counters(p):
    c=ET.parse(p).find('.//t:Counters',NS)
    assert c is not None and int(c.get('executed','0'))>0
    assert int(c.get('failed','0'))==0 and int(c.get('passed','0'))==int(c.get('total','0')),str(p)
    return int(c.get('passed'))
suites=['desktop-regression-final.trx','CommandPaletteWindowsTests.trx','CompletionFeedbackUiTests.trx','ExplorerDropViewTests.trx','ViewStateUiTests.trx','InboxZeroUiTests.trx','TaskContextActionsUiTests.trx']
results={f:counters(WORK/'evidence'/f) for f in suites}
assert sum(results.values())==771
assert counters(WORK/'evidence/targeted-final.trx')==14
database=[]
for p in (WORK/'evidence/database').glob('*.trx'):
    c=ET.parse(p).find('.//t:Counters',NS)
    if c is not None and int(c.get('total','0')): database.append((p.name,counters(p)))
assert sum(n for _,n in database)==11
previous=json.loads((WORK/'evidence/previous-journeys-accepted/ui-results.json').read_text(encoding='utf-8-sig'))
assert previous['status']=='PASS' and not previous['bindingErrors']
files=json.loads((WORK/'source-scope.json').read_text(encoding='utf-8'))
assert all(f.startswith('work/production/') for f in files)
OUT.mkdir(parents=True,exist_ok=True)
(OUT/'.gitattributes').write_text('* -text\n',encoding='utf-8')
for f in files:
    dest=OUT/'source'/f; dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/f,dest)
for f in ['Package.py','README.md','Verify-Desktop.ps1','Verify-Database.ps1','Update-Dashboard.mjs','source-scope.json','roadmap-for-index.json']:
    shutil.copy2(WORK/f,OUT/f)
accepted=['targeted-final.trx','targeted-final.log','desktop-regression-final.trx','release-build.log','previous-journeys-accepted.log']
accepted += [name for suite in ['CommandPaletteWindowsTests','CompletionFeedbackUiTests','ExplorerDropViewTests','ViewStateUiTests','InboxZeroUiTests','TaskContextActionsUiTests'] for name in [suite+'.trx',suite+'.log']]
for name in accepted:
    src=WORK/'evidence'/name
    if src.exists():
        dst=OUT/'evidence'/name;dst.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src,dst)
shutil.copytree(WORK/'evidence/screenshots',OUT/'evidence/screenshots',dirs_exist_ok=True)
for src in (WORK/'evidence/database').iterdir():
    if src.is_file() and (src.suffix=='.trx' or src.name in ['tests.log','build.log','boundaries.log']):
        dst=OUT/'evidence/database'/src.name;dst.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src,dst)
shutil.copytree(WORK/'evidence/previous-journeys-accepted',OUT/'evidence/previous-journeys-accepted',ignore=shutil.ignore_patterns('corporate-fixture','personal-fixture'),dirs_exist_ok=True)
(OUT/'version.txt').write_text(VERSION+'\n',encoding='utf-8')
base=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT).decode().strip()
report=f'''# Validation report — Task UX 6–8, {VERSION}

Дата: 10.10.2026. База: `{base}`. Scope: {len(files)} production/test files, документация проверки и три evidence/note dashboard. Задачи 1–5 сохранены; задачи 9–10 не выполнялись. Серверные API/DTO, права, схема базы и допустимая вложенность не менялись.

## Выполненные проверки

- 771 desktop tests PASS, 0 failed, 0 skipped: 759 в основном прогоне и 12 в шести последовательных WPF-suite. TRX приложены. 14 targeted tests входят в этот общий итог, не прибавляются повторно.
- Release build всей Task.sln PASS. Имеется исходное предупреждение xUnit1031 в DesktopCredentialVaultTests; новых ошибок/предупреждений production нет.
- 11 PostgreSQL/API integration tests PASS (5 Task.Tests, 6 Task.ServiceHosts.Tests), на изолированном настоящем PostgreSQL с явно установленным TASK_POSTGRES_TEST_ADMIN_CONNECTION. Создание/повторное чтение project/parent, сохранность parent, видимость/tenant, запрет второго уровня, отменённый parent и архивный project, rollback невалидных связей.
- Production DesktopTasksApiClient: два transport round-trip tests проверяют обычный create command и повторный GET обеих связей. PersonalTasksClient: настоящий локальный SQLite, create/save/re-read project/parent, отсутствие наследования проекта, ошибки без потери черновика и отсутствие чужих проектов.
- Настоящие MainWindow/PersonalWindow: OS mouse double-click правильной строки и пустой области, защита вложенных интерактивных контролов/scrollbar, read-only просмотр, OS Enter/Space для project/subtask actions, title focus и возврат в список, Tab, прокрутка/выбор/очистка parent, обновление ItemsSource, обычные окна и минимум 800×480, 0 binding errors. Screenshots сохранены и осмотрены.
- Поздние options сохраняют ID, название и пользовательское изменение/очистку; CanExecute защищает busy, права, terminal/nested parents, редактор, комментарий и чек-лист. Создание идёт только через обычное подтверждение. Отмена не отправляет mutation. Validation/Forbidden сохраняют редактор и не показывают успех.
- Предыдущий WPF UiProbe: PASS, {len(previous['checks'])} checks, 0 binding errors; проверены прежние пользовательские пути, календарь и клавиатурное сохранение.
- Итоговый diff перечитан; diff --check PASS. Чужие изменения/артефакты сохранены и исключены из staging. Git fetch до публикации: HEAD совпадал с origin/main.

## Ограничения и обнаруженные проблемы

- Дополнительный старый Test-ProjectBoundaries.ps1 FAIL: он ожидает net10.0-windows, тогда как исходный main уже имеет net10.0-windows10.0.17763.0. Это не изменение данного пакета; .csproj и проверки не ослаблялись. Лог приложен. Обязательные сборка/регрессия/сценарии проходят.
- Старый ViewStateUiTests первоначально нестабильно завершался с CLR teardown assertion. Устранена гонка тестовой инфраструктуры: теперь ожидается завершение STA-потока до выхода testhost. Все исходные assertions сохранены; финальные изолированный и последовательный прогоны PASS.
- Проверки выполнены на синтетических данных и изолированных локальных базах/HTTP fixtures. Подключение к реальному серверу компании и пользовательским данным не выполнялось.
- Пакет содержит проверенный production source, тесты и evidence; отдельный portable release в scope не входил.

Dashboard: только DESK-05, QA-04, PROD-02 evidence/note/updated_at; npm run dashboard:order выполнен. Progress/status не повышены. SHA-256 всех файлов — manifest.json, ZIP hash — внешний .sha256. Commit/push проверяются после сборки пакета и сообщаются в финальном ответе.
'''
(OUT/'validation-report.md').write_text(report,encoding='utf-8')
(OUT/'validation.json').write_text(json.dumps({'version':VERSION,'required_checks':'PASS','desktop':results,'desktop_total':771,'targeted':14,'postgres':database,'previous_ui_checks':len(previous['checks']),'binding_errors':0,'optional_boundary_check':'BASELINE_FAILURE','source_files':len(files)},ensure_ascii=False,indent=2),encoding='utf-8')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
manifest={'version':VERSION,'base_commit':base,'files':[{'path':p.relative_to(OUT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(OUT.rglob('*')) if p.is_file() and p.name!='manifest.json']}
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
for entry in manifest['files']: assert sha(OUT/entry['path'])==entry['sha256']
zip_path=Path(str(OUT)+'.zip')
with zipfile.ZipFile(zip_path,'w',zipfile.ZIP_DEFLATED) as z:
    for p in sorted(OUT.rglob('*')):
        if p.is_file():z.write(p,p.relative_to(OUT.parent).as_posix())
with zipfile.ZipFile(zip_path) as z:
    assert z.testzip() is None
    for entry in manifest['files']:assert hashlib.sha256(z.read(OUT.name+'/'+entry['path'])).hexdigest()==entry['sha256']
zip_path.with_suffix('.zip.sha256').write_text(sha(zip_path)+'  '+zip_path.name+'\n',encoding='utf-8')
zip_path.with_suffix('.zip.validation.json').write_text(json.dumps({'version':VERSION,'archive_validation':'PASS','entries':len(manifest['files'])+1,'sha256':sha(zip_path)},indent=2),encoding='utf-8')
print(json.dumps({'package':str(zip_path),'source_files':len(files),'sha256':sha(zip_path),'desktop_total':771}))
