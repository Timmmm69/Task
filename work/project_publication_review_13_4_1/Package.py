from pathlib import Path
import hashlib,json,subprocess,shutil,zipfile,xml.etree.ElementTree as E
root=Path.cwd();out=root/'outputs/20261019_project_detail_publication_13.4.1'
assert not out.exists();out.mkdir(parents=True)
source_commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
base='dcbaca241868f2afb4e6506473b8957ab72aa8b3'
paths=[p for p in subprocess.check_output(['git','diff','--name-only',base,'HEAD'],text=True,encoding='utf-8').splitlines() if p.startswith('work/production/') or p.startswith('work/project_publication_review_13_4_1/') or p=='work/project_history_13_4/Run-Gate.ps1' or p=='.project-dashboard/roadmap.json']
for path in paths:
 target=out/'source'/path;target.parent.mkdir(parents=True,exist_ok=True)
 target.write_bytes(subprocess.check_output(['git','show','HEAD:'+path]))
shutil.copytree(root/'work/project_history_13_4/results/publication-final',out/'evidence/full-release')
shutil.copytree(root/'work/project_history_13_4/results/publication-fix',out/'evidence/lifecycle-targeted')
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};runs=[]
for p in sorted((out/'evidence/full-release').glob('*.trx')):
 tree=E.parse(p); c=tree.find('.//t:Counters',ns)
 runs.append({'file':p.relative_to(out).as_posix(),**{k:int(c.get(k,0)) for k in ['total','passed','failed','notExecuted']}})
assert len(runs)==3 and sum(r['passed'] for r in runs)==3318 and all(r['failed']==0 and r['notExecuted']==0 for r in runs),runs
review=(root/'work/project_publication_review_13_4_1/Review.md').read_text(encoding='utf-8-sig')
report=f'''# Публикационная проверка карточки проекта 13.1–13.4

Дата: 2026-10-19. Версия пакета: 13.4.1. Source commit: `{source_commit}`.
Общая база: `{base}`. Ветка: `feature/project-detail-sections`.

Независимый review и полный Release gate завершены. Обнаруженная lifecycle-ошибка общей
навигации компаний исправлена. Нерешённых конкретных дефектов в проверенном scope не найдено.
Это не утверждение об отсутствии любых потенциальных ошибок продукта.

| Этап | Реализовано и проверено | Коммит |
|---|---|---|
| 13.1 | Независимые разделы карточки, состояния, отмена и права | 283bcd4 |
| 13.2 | Файлы проекта: каталог, Object Links, read/link/unlink/open | 4accd66 |
| 13.3 | Контакты/компании: связи, права и навигация | d5afec2 |
| 13.4 | История: lazy load, paging/dedup, права, авторы, ошибки | 63babc1 |
| Исправление review | Active lifecycle gate общей карточки компании и 10 regression cases | {source_commit[:7]} |

| Полный Release-набор | PASS | FAIL | SKIP |
|---|---:|---:|---:|
| Domain/infrastructure/реальная PostgreSQL 16 | 880 | 0 | 0 |
| HTTP/API/contracts/permissions | 647 | 0 | 0 |
| Desktop/native WPF/Personal SQLite | 1791 | 0 | 0 |
| Всего | **3318** | **0** | **0** |

Release solution build PASS. Тесты выполнены последовательно, без фильтрации/исключений.
Native WPF scenarios всех разделов карточки прошли, включая прежний Window.Activate тест.
Ранее зарегистрированный отказ foreground activation не скрыт: исходные receipts в пакете
13.4.0 остаются историей проверки. Дополнительно lifecycle-targeted: 101 PASS; до исправления
общая CRM integration: 275 PASS; предыдущий project/history-targeted: 268 PASS.

{review}

Проверенные API: GET /projects/{{id}}, project members/roles; GET /tasks через существующий
project-related loader; GET/POST/DELETE Object Links с существующими типами связей проекта;
catalog/contacts/companies read APIs и разрешённый resolver; GET /projects/{{id}}/history;
optional GET /users/{{id}}. Administrative audit и фиктивные project comments не вызываются.
Права: Project.Read/Update/Create/Archive/ManageMembers по прежним командам; Task.Read/Create
для задач; FileCatalog/Contact/FileReference и sensitive-path capabilities в существующих loaders;
History.Read; optional User.Read. If-Match/idempotency прежних мутаций сохранены.

Полный список изменённых production/docs/test файлов: changed-files.txt; точные файлы
публикационного исходного состояния приложены в source/. Предыдущий подробный сценарный
и contract-gap отчёт: work/production/docs/PROJECT-DETAIL-ACCEPTANCE-13.4.md.

Публикация разрешена пользователем. Пакет подготовлен для fast-forward main; фактический
remote HEAD проверяется после push. Пакет не объявляет проектные комментарии реализованными
и не закрывает ручную customer/live-server приёмку. Результат push сообщается отдельно.
'''
(out/'validation-report.md').write_text(report,encoding='utf-8')
(out/'validation-report.json').write_text(json.dumps({'version':'13.4.1','date':'2026-10-19','sourceCommit':source_commit,'base':base,'build':'PASS','gate':'full Release, sequential, real PostgreSQL16','runs':runs,'passed':3318,'failed':0,'skipped':0,'independentReview':'Completed; lifecycle defect fixed; no unresolved concrete defects found','comments':'ISSUE-13.4-COMMENTS remains: generic normative comments not implemented in production project API','manualLiveServerWalkthrough':'NOT RUN','nativeWpf':'PASS','userAuthorizedMainPublication':True,'files':paths},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(out/'changed-files.txt').write_text('\n'.join(paths)+'\n',encoding='utf-8')
(out/'source-commit.txt').write_text(source_commit+'\n',encoding='utf-8')
(out/'version.txt').write_text('13.4.1\n',encoding='utf-8')
(out/'source.patch').write_bytes(subprocess.check_output(['git','diff','--binary',base,'HEAD','--','work/production']))
entries=[{'path':p.relative_to(out).as_posix(),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(out.rglob('*')) if p.is_file()]
(out/'manifest.json').write_text(json.dumps({'version':'13.4.1','sourceCommit':source_commit,'files':entries},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
archive=Path(str(out)+'.zip')
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
 for p in sorted(out.rglob('*')):
  if p.is_file():z.write(p,p.relative_to(out))
with zipfile.ZipFile(archive) as z:
 assert z.testzip() is None
 assert all(hashlib.sha256(z.read(e['path'])).hexdigest()==e['sha256'] for e in entries)
digest=hashlib.sha256(archive.read_bytes()).hexdigest()
Path(str(archive)+'.sha256').write_text(digest+'  '+archive.name+'\n',encoding='utf-8')
print(json.dumps({'sourceCommit':source_commit,'manifestFiles':len(entries),'passed':3318,'verified':True,'zipSha256':digest}))
