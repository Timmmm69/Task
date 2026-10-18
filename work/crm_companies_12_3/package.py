from pathlib import Path
import json, subprocess, hashlib, shutil, zipfile, xml.etree.ElementTree as ET

repo = Path.cwd()
out = Path(r'C:\Users\novik\Таск\outputs\20261018_crm_companies_12.3.0')
checks = repo / 'work/verification/crm-companies-12.3'
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def counters(name):
    root = ET.parse(checks/name).getroot()
    return {k:int(v) for k,v in root.find('t:ResultSummary/t:Counters',ns).attrib.items()}
results = {k:counters(v) for k,v in {'desktop':'desktop-release-final.trx','api':'api-final.trx','product':'product-release-final.trx'}.items()}
assert all(r['failed'] == 0 for r in results.values()), results
assert results['desktop']['passed'] == 1564, results
assert not out.exists(), 'Preserve existing version; choose a new package version before replacing it'
commit = subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
assert subprocess.check_output(['git','status','--porcelain','--untracked-files=no'],text=True).strip() == ''
changed = subprocess.check_output(['git','diff-tree','--no-commit-id','--name-only','-r','HEAD'],text=True).splitlines()
out.mkdir(parents=True)
for name in changed:
    target = out/'source'/name
    target.parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(repo/name,target)
(out/'changes.patch').write_bytes(subprocess.check_output(['git','show','--format=fuller','--binary','HEAD']))
shutil.copytree(checks,out/'evidence')
(out/'VERSION').write_text('12.3.0\n',encoding='utf-8')
(out/'changed-files.txt').write_text('\n'.join(changed)+'\n',encoding='utf-8')
report = f'''# Task 12.3 — validation report

Версия 12.3.0. Ветка feature/crm-contacts-workspace. Коммит `{commit}`.
База 8764a51 (12.2), включает 935e479 (12.1). Коммит локальный: push/merge в main не выполнялись.

Реализованы список компаний с серверными q/cursor, выбор и карточка, создание, отмена и PATCH, безопасные POST/DELETE связи контакта, дополнительные поля связи, переходы company/contact и открытие company из глобального поиска. Все сценарии встроены в существующий WorkHub; личные контакты не изменены.

## Проверки

- Release solution build: PASS, 0 errors, 2 существующих xUnit analyzer warnings. Evidence: evidence/build-final.log.
- Финальный полный Desktop: {results['desktop']['passed']} PASS / 0 FAIL / 0 SKIP. Включает регрессии 12.1/12.2 и native company probe.
- Полный API/ServiceHosts: {results['api']['passed']} PASS / 0 FAIL / 0 SKIP.
- Product contract runner: {results['product']['passed']} reported PASS / 0 FAIL / 14 SKIP. Реального SQL исполнения без БД это не доказывает: часть исторических tests возвращается до assertions при отсутствии подключения. Новый unlink security test явно SKIP через RequiresPostgresFact.
- Native WPF: 1100×1000 и 800×600; company selection/view/edit/save, добавление связи, открытие прежней contact card, возврат к выбранной company, удаление связи без удаления contact, create и read-only PASS. Binding errors отсутствуют; четыре PNG в evidence/native. Скриншоты просмотрены; кнопки сохранения видимы в компактном окне.
- Негативные сценарии: 401/403/404/409/412/422/offline/malformed, права read/create/update, недоступные контакты, смена selection, очистка selection при pending GET, поздние list/card/picker/save, отмена, duplicate/double-click, замороженный payload и прежний Idempotency-Key при повторе create.
- git diff --cached --check: PASS перед коммитом. Dashboard order/validate: PASS, progress/status сохранены; изменены только evidence/note PROD-05 и DESK-03.

Промежуточный общий Desktop прогон: 1550 PASS / 1 FAIL. Единственная ошибка — прежнее ожидание, что company не имеет маршрута. SearchNavigationTests обновлён под реализованный маршрут contacts; следующий общий прогон 1562 PASS. Финальный прогон выше включает ещё два добавленных регрессионных теста. Исходные TRX сохранены в evidence.

## Контракт, права и ограничения

Использованы существующие GET/POST companies, GET/PATCH companies/{{id}}, POST companies/{{id}}/contacts, DELETE companies/{{id}}/contacts/{{childId}}, GET contacts?q и contacts/{{id}}. Permissions: Contact.Read, Contact.Create, Contact.Update плюс серверный object-scope компании и независимый Contact.Read scope контакта. Strong If-Match компании обязателен для PATCH и связи; create/link используют Idempotency-Key. Проверяется точный strong ETag и рост версии; после изменения связи перечитывается компания.

В server unlink закрыт пробел: активность/организация/Contact.Read scope контакта проверяются до удаления, как при link. Невидимая цель даёт generic validation response, без удаления или bump версии. Компания остаётся самостоятельной company, связь — company_contacts.

Поля компании: name 1–500 trim, legalName ≤500, industry ≤200, website ≤2048, taxIdentifier ≤100, notes ≤20000, status active/inactive. URL соответствует существующему правилу API: абсолютный HTTP(S) без credentials. Связь: jobTitle/departmentName ≤200, isPrimary, validFrom/validTo ISO date и допустимый порядок.

Поиск компаний ограничен серверными name/notes. PATCH связи отсутствует, адресов компаний и истории взаимодействий нет. Новых API/миграций/сделок/счетов/воронки/изменения личной модели не добавлено. Ключ повторного создания сохраняется в текущей форме; восстановление формы после перезапуска не входит в контракт.

## Открытая проверка и известные дефекты

Подтверждённых оставшихся дефектов в выполненных проверках нет. Реальные PostgreSQL и live Corporate acceptance НЕ ВЫПОЛНЕНЫ: Docker daemon недоступен, TASK_POSTGRES_TEST_ADMIN_CONNECTION не настроен. Новую SQL-проверку unlink нужно выполнить на стенде; permissions/транзакционный эффект/rollback реальной БД нельзя считать принятыми только по unit/native тестам.

Разработка 12.4 возможна на этой ветке. Полная интеграционная приёмка 12.3 остаётся открытой до прогона PostgreSQL/live Corporate.

## Изменённые файлы

'''+'\n'.join('- `'+p+'`' for p in changed)+'\n'
(out/'validation-report.md').write_text(report,encoding='utf-8')
(out/'validation-report.json').write_text(json.dumps({'version':'12.3.0','commit':commit,'branch':'feature/crm-contacts-workspace','results':results,'release_build':'PASS','native_wpf':'PASS','dashboard':'PASS','postgres_sql_execution':'NOT_RUN','live_corporate_acceptance':'NOT_RUN','stage_12_4':'DEVELOPMENT_READY; integration acceptance remains open'},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
files = [{'path':p.relative_to(out).as_posix(),'size':p.stat().st_size,'sha256':sha(p)} for p in sorted(out.rglob('*')) if p.is_file()]
manifest={'version':'12.3.0','commit':commit,'files':files}
(out/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(out/'SHA256SUMS').write_text(''.join(sha(p)+'  '+p.relative_to(out).as_posix()+'\n' for p in sorted(out.rglob('*')) if p.is_file()),encoding='utf-8')
for item in files: assert sha(out/item['path']) == item['sha256']
archive=Path(str(out)+'.zip')
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
    for p in sorted(out.rglob('*')):
        if p.is_file(): z.write(p,out.name+'/'+p.relative_to(out).as_posix())
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for item in files: assert hashlib.sha256(z.read(out.name+'/'+item['path'])).hexdigest()==item['sha256']
Path(str(archive)+'.sha256').write_text(sha(archive)+'  '+archive.name+'\n',encoding='ascii')
print(json.dumps({'package':str(archive),'sha256':sha(archive),'manifest_files':len(files),'commit':commit,'results':results},ensure_ascii=False))
