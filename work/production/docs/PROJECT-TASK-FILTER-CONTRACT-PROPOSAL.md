# Этап 8.2 — серверная выборка задач проекта

**PROPOSED — NOT APPROVED**

Версия: **8.2.0**. Дата: **2026-10-16**. Решение для согласования, не разрешение на реализацию.

## 1. Подтверждённая проблема и границы проверки

В `origin/main` на проверенной базе `deb16b3` сервер не умеет ограничивать список задач проектом. `TaskEndpoints.GetTasksAsync` / `TryReadListQuery` (`src/Task.Api/Tasks/TaskEndpoints.cs:51,272`) отвергают непустые `filter` и `sort`, допускают только отсутствие `page` либо `page=1`, передают cursor в store. Неизвестный `projectId` сейчас **не читается и не ограничивает выборку**: успешный HTTP-ответ не доказывает применение фильтра. При реализации нельзя молча игнорировать этот параметр.

`TaskReadPageRequest` (`src/Task.Application/TaskReadContracts.cs:24`) содержит organization, user, scope version и cursor, но не ProjectId. `ITaskReadStore.GetPageAsync` использует этот request. `PostgresTaskReadStore.GetPageAsync` (`src/Task.Infrastructure/Persistence/PostgresTaskReadStore.cs:91`) фильтрует tenant, тип, lifecycle, visibility и keyset, но не проект. Desktop `GetTasksAsync` (`src/Task.Desktop/Tasks/DesktopTasksApiClient.cs:232`) отправляет только cursor. В main `ProjectsViewModel.LoadRelatedTasksAsync` отбирает `task.Card?.ProjectId == projectId` после получения страниц организации.

Результаты 8.1 изучены отдельно: локальный commit `eb1044d` ветки `perf/project-related-tasks`, пакет `outputs/20261016_project_related_tasks_8.1.0/validation-report.md`, `comparison.csv`, `validation.json`, manifest и тестовые TRX. Этот commit на момент review **не входит в origin/main**. В нём `ProjectsViewModel.RelatedTasks.cs:74` запрашивает одну страницу на явное действие и на строке 97 сохраняет клиентское сравнение ProjectId. 8.1 устраняет блокирующий автоматический обход и добавляет отмену/partial states, но полный результат всё ещё требует всех страниц организации. Сервер, application/infrastructure и desktop API client между main и 8.1 не изменены.

Таким образом, дефект эффективности подтверждён и для main, и после 8.1. Нельзя приписывать main улучшения неопубликованного 8.1. Для неизменяемого набора с N доступных задач организации и M задач проекта текущий полный обход требует порядка `ceil(N/50)` запросов (минимум один), предлагаемый — `ceil(M/50)` (минимум один). Это оценка запросов, не benchmark PostgreSQL и не обещание snapshot completeness при конкурентных изменениях.

## 2. Нормативные источники и приоритет

Пути далее относительно корня репозитория; `ZIP!member` обозначает точное содержимое архива. SHA-256 входов и проверенных файлов сохранены в пакете review (`evidence/source-inventory.json`). `sources/` не изменялись.

| Источник | Точное основание и предел вывода |
|---|---|
| [Концепция](../../../sources/concept/Task_Concept_Final.txt), §11.7–11.8, строки 358–367; §13.1–13.4, строки 403–443; права, строки 194–198 | Представление задач по проектам и фильтр «проект» предусмотрены как бизнес-функция; карточка проекта включает задачи. Права зависят от роли и участия. HTTP-синтаксис и server query contract этого фильтра не определены. |
| [Архитектура](../../../sources/stage_1/architecture_organizer.md), §1.4 (строка 91), §5.3 (935–946), §8.14 (1617–1627) | До 2 млн задач/событий, cursor для больших лент, сохранение scope revocation и очистки запрещённых данных. Не разрешает произвольные API additions. |
| [Stage 2.2 Repacked](../../../sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2_Repacked.zip)!`Organizer_Stage2_Technical_Specification_2.2/openapi/openapi.yaml:18486–18570` | `GET /api/v1/tasks`: `Task.Read`, `filter` string maxLength=2000, `sort`, `page`, cursor maxLength=512; `x-filters: [filter]`; cursor связан с normalized filters/sort/scope. **Нет allowlist поля проекта или грамматики filter.** |
| Тот же Stage 2.2!`docs/02_api_and_concurrency.md:21–22,247`; `catalogs/api_catalog.csv`, запись GET tasks | Filtering по allowlist, произвольный SQL-like filter запрещён; каталог перечисляет generic `filter,sort,page,cursor`. Это общий контракт фильтрации, но не определение `projectId`. |
| Тот же Stage 2.2!`Search_Contract.md`, §2, §4, §6–7 | `q` длиной 2–200, `projectIds` до 100, SearchPage; фильтрация на сервере, запрет post-filtering относится к search results. Нельзя переносить search-параметры в tasks без решения. |
| [CANONICAL_BASELINE.md](../../../CANONICAL_BASELINE.md), пункты 3,5,8; [Stage 2.3 alignment](../../../sources/stage_2_3/Stage_2_3_Contract_Alignment.md) | Репозиторий обозначает Stage 2.3.1 как current technical contract. Дополнительно проверен `sources/stage_2_3/Organizer_Stage2_Technical_Specification_2.3_Final.zip!stage_2_3/openapi/openapi.yaml`, операция GET tasks, и `docs/02_api_and_concurrency.md:21–22,247`: тот же generic filter без project grammar. Targeted update касается urgency и employee search. |
| [Stage 3.4](../../../sources/stage_3_4/Organizer_Stage3_Final_Baseline_3.4.zip)!`Organizer_Stage3_Final_Baseline_3.4/Stage_3_UX_Architecture.md:147` (UXP-14); `Stage_3_Screen_Catalog.md`, SCR-060–072, SCR-133–135 | Lazy/paged UX, не загружать весь набор до первого render. Search project filters описаны для search; новый task-list query не определён. Дополнительно проверены те же разделы Stage 3.5 и targeted delta. |
| [PRD candidate 4.1.1](../../../sources/stage_4_1_1/Organizer_Stage4_PRD_Candidate_4.1.1.zip)!`Organizer_Stage4_PRD_Candidate_4.1.1/Stage_4_Module_PRDs.md`, MOD-003 / FR-026 и MOD-010 / FR-083–096; NFR-006 | TaskPage, проекты/участники, server pagination. Candidate не объявляется утверждённым PRD. |
| [Stage 4 approval](../../stage_4_6_lite/final_baseline/Stage_4_Final_Baseline_Approval.md); там же `Stage_4_Final_Baseline_Manifest.md` и `Organizer_Stage4_PRD_Candidate_4.5.zip` | Утверждено «Stage 4.5 + Stage 4.6 Lite PASS». Проверены `Stage_4_Module_PRDs_4.5.md` (те же FR) и `Stage_4_NFR_Catalog_4.5.csv:7` (NFR-006: 0 full-dataset list requests). Точный проектный query отсутствует и здесь. |

В инструкции AGENTS перечислен более ранний baseline 2.2/3.4/4.1.1, тогда как CANONICAL_BASELINE и финальный approval указывают последующие документы. По заданному приоритету концепция/архитектура и перечисленный технический контракт не переопределяются промежуточными файлами. Сопоставлены обе линии версий: **по рассматриваемому вопросу вывода о разрешённом синтаксисе они не меняют**. Искомого `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2.zip` нет: использован существующий Repacked ZIP и сопоставлен с распакованным OpenAPI.

**Доказанное расхождение:** нормативный GET tasks объявляет generic server filtering/sorting, а текущий increment отклоняет непустые filter/sort. **Не доказано**, что именно проектный фильтр уже утверждён: ни имя поля в allowlist, ни синтаксис, ни его проектные edge cases не определены. Ни `filter=projectId=...`, ни `?projectId=...` нельзя выдавать за действующий утверждённый wire contract.

## 3. Сравнение решений

| Вариант | Норма / изменения | Безопасность и совместимость | Полнота / pagination / клиент | Сложность и риски |
|---|---|---|---|---|
| A. Уже утверждённый project filter внутри `filter` | Общий filter есть; утверждённая project grammar не обнаружена. Реализация возможна только после определения allowlist, grammar, validation и cursor binding. | Те же Task.Read/visibility; старые cursor-only запросы сохраняются. Нельзя принять arbitrary expressions. | TaskPage может дать полный разрешённый набор при покое данных; keyset до LIMIT; desktop должен кодировать grammar. | Средняя; выбор собственной grammar под видом уже утверждённой создаёт скрытый contract delta. Условие «уже утверждён» не выполнено. |
| **B. Optional `projectId` у GET tasks** | **Предлагаемый additive contract delta**: один UUID параметр; parser → request → SQL → cursor → desktop. Отдельное утверждение обязательно. | Tenant и object ACL сохраняются; параметр только сужает набор. Старый запрос без параметра сохраняет поведение. | Полный видимый проектный набор при статичных данных; тот же TaskPage и сортировка, без текстового запроса. Меняется один вызов client из 8.1. | Низкая/средняя относительно других вариантов, но H по безопасности/API. Риски cursor domain, смешанного deployment и DB scan. **Рекомендуется.** |
| C. Существующий специализированный endpoint | В `ProductApiRoutes.Build` (`src/Task.Application/ProductData/ProductApiContracts.cs:26–116`) и OpenAPI нет project task-list endpoint. Projects get/members/history не возвращают TaskPage; task workspace привязан к одной task, subtasks — к parent task. Calendar ограничен диапазоном/составом schedule projection. | Использовать чужие разрешения вместо Task.Read нельзя. | Ни один из найденных endpoints не гарантирует полный список всех задач проекта, в том числе без дат. | Условие существования подходящего endpoint не выполнено; расширение чужого response лишь добавит контракт. |
| D. Новый `/projects/{id}/tasks` | В актуальных контрактах отсутствует. Потребуются новая операция, OpenAPI, security/existence semantics, DTO routing и клиент. | Нужно отдельно определить Task.Read и object ACL, не подменять их Project.Read. Дублирование policy повышает риск. | Может дать TaskPage с keyset; клиенту новый route. | Выше B, объективной непригодности B нет. Не рекомендуется. |
| Глобальный search | `Search.Use`, обязательный q, projectIds, SearchPage, другая семантика. `PostgresProductApiSearch.Search` ограничивает snapshot 10 000 результатами, при превышении требует сузить поиск; lifetime 15 минут. | Дополнительная search capability и те же проверки результатов; не замена Task.Read. | Текстовый матч не гарантирует все tasks; краткие search items потребуют догрузки карточек. Cursor другого домена. | Wildcard/пустой q не предусмотрены как «все задачи». Не использовать для списка проекта. |

## 4. Рекомендованный API delta B (подлежит утверждению)

```http
GET /api/v1/tasks?projectId=11111111-1111-1111-1111-111111111111
GET /api/v1/tasks?projectId=11111111-1111-1111-1111-111111111111&cursor=<opaque>
```

1. `projectId`: optional scalar UUID, одно непустое ненулевое значение. При отсутствии — прежняя выборка организации. Пустое, whitespace, Guid.Empty, malformed, повторённый key (даже одинаковый), список — HTTP 400 `VALIDATION_FAILED`, без обращения к task query. Parse до нормализации Guid в lowercase `D`; регистр корректного UUID не меняет область cursor. Это не фильтр «без проекта»: null/`none`/`all` не вводятся.
2. Новый predicate `t.project_id = $project` применяется **в SQL до LIMIT** совместно со всеми существующими predicates. Не загружать нефильтрованные страницы в application layer. Не получать связанные задачи по одной через detail endpoint.
3. Response неизменен: `items`, `nextCursor`, `total` (текущий null). Размер страницы 50, запрос lookahead 51. Порядок `o.updated_at DESC, o.id DESC`. Точные totals и новый `limit`/sort/lifecycle API не вводятся.
4. Валидный UUID отсутствующего/чужого проекта либо проекта без видимых tasks возвращает одинаковый `200 {items:[],nextCursor:null,total:null}`. Существование проекта отдельно не проверяется/не раскрывается. Если для такого пользователя сохраняются разрешённые авторские/назначенные tasks проекта, их наличие определяется прежней row policy. Нельзя без нормативного решения превратить этот фильтр в дополнительный blanket Project.Read gate.
5. `401`/`403` действующей authentication / Task.Read policy остаются. Несовместимый cursor — 400 `SEARCH_CURSOR_INVALID`. Не добавлять permissions/error codes. Непустые generic filter/sort и page>1 по-прежнему не реализуются этим узким increment; это известное отдельное расхождение, не «поддержка всего normative filtering».
6. Новый parser явно проверяет multiplicity projectId/cursor. Некорректный фильтр никогда не приводит к unfiltered fallback. Прочие query-параметры не расширять и не ужесточать попутно без анализа совместимости.

## 5. DTO и границы компонентов

- `TaskReadPageRequest`: добавить **в конец** `Guid? ProjectId = null`, сохранив существующий positional cursor и source compatibility вызовов. Проверка nonempty на API и store boundary. `ITaskReadStore.GetPageAsync` сохраняет сигнатуру и семантику TaskPage.
- `TaskReadProjection`, `TaskCardContent`, HTTP TaskResponse/TaskPage не расширять: ProjectId уже есть в карточке. Новый storage model не нужен.
- Codec получает нормализованный ProjectId через новый optional argument/overload с сохранением прежних вызовов. Parser и creator используют один canonical query builder; никакого hash от raw query string.
- `IDesktopTasksApiClient`: сохранить существующий `GetTasksAsync(cursor, cancellationToken)` для общего списка. Добавить типизированный `GetProjectTasksAsync(Guid projectId, string? cursor = null, CancellationToken cancellationToken = default)`, направляющий запрос на тот же `/tasks`. Это client method, не новый endpoint. Обновить реализацию и все test doubles интерфейса.
- После интеграции 8.1 заменить только вызов в `LoadRelatedTasksPageAsync`, сохранив states, явную загрузку, retry, generation/session guards, cancellation, дедупликацию и терминальный cursor. Не возвращать автоматический полный обход. Клиентское сравнение ProjectId может остаться как fail-closed проверка ответа, но не средство реализации фильтра и не доказательство server support.

## 6. Cursor semantics и безопасность продолжения

Текущий v1 — base64url JSON с `v,o,u,s,b,t,i,q`; q — SHA-256 фиксированной строки `task-read:v1|lifecycle=active|order=updated_at:desc,id:desc|page-size=50`. User, tenant и scope сравниваются с authenticated request. Это **не подпись** и не шифрование: payload можно пересобрать, hash известен. SQL visibility, а не cursor, остаётся источником authorization. Смена literal hash не создаёт cryptographic authenticity.

Предлагаемый минимальный протокол:

| Область | Создание и приём |
|---|---|
| Без projectId | Создавать/принимать прежний v1 без изменения его normalized query. Не инвалидировать текущие unfiltered продолжения. |
| С projectId | Отдельный v2; q = base64url(SHA-256(UTF-8 canonical string `task-read:v2|project-id=<guid-D-lower>|lifecycle=active|order=updated_at:desc,id:desc|page-size=50`)). Остальные поля прежние. Не добавлять UUID повторно в payload, чтобы сохранить лимит 512; проверить worst-case serialization реальными тестами. |
| Несоответствие | v1 с projectId, v2 без projectId, A→B, other tenant/user, другая scope version, unknown version/hash, malformed/oversized — `SEARCH_CURSOR_INVALID`; не выполнять unfiltered query и не начинать автоматически с первой страницы. |
| Повтор | Повтор того же request cursor допустим для retry. Codec не делает cursor одноразовым. Зацикленный nextCursor — нарушение протокола на клиенте; 8.1 уже останавливает его. |

Этот вариант обеспечивает отклонение **переноса неизменённого** cursor A в запрос B. Он не гарантирует обнаружение намеренной перепаковки unsigned payload. Вне зависимости от этого новый SQL всегда принудительно применяет ProjectId **из проверенного request**, actor и tenant из authenticated context, актуальную visibility: cursor не расширяет права и не снимает filter.

**Условие согласования C2:** явно принять наследуемую unsigned integrity model либо потребовать аутентифицированный v2 (MAC/защищённый token с выделенным управляемым серверным ключом). Если требуется гарантия от подделки границ/проекта, unsigned v2 недостаточен: до реализации необходимо утвердить защиту, rotation/retention, общий key для экземпляров, startup validation и пересчитать размер ≤512. Не использовать JWT signing key автоматически и не хранить новый секрет в repo. MAC без такого решения не объявляется уже разрешённой частью B.

`snapshotBoundaryUtc` — timestamp верхней границы, **не MVCC snapshot через несколько HTTP-запросов**. При покое данных keyset даёт исчерпывающий набор без повторов. Новые/изменённые после boundary tasks исключаются; ещё не прочитанная task, обновлённая после первой страницы, может исчезнуть из обхода; перенос между проектами и lifecycle/ACL изменения тоже меняют набор. Refresh с первой страницы обязателен для актуального состояния. `Complete` означает конец текущего обхода, не исторический snapshot всех rows. Snapshot cache или длительную DB transaction не вводить без отдельного решения.

## 7. Permissions, visibility и отзыв прав

Инвариант: `Filtered(org,user,scope,P) ⊆ ExistingVisibleActiveTasks(org,user,scope)` и каждая возвращённая строка имеет `project_id=P`.

- Сохранить endpoint `Task.Read` → `task.read`: `TaskPermissionAuthorization.cs:42,71,239`. Старый XML-комментарий про `task.manage` и имя legacy test не отражают фактическую действующую mapping. Новых permissions нет; Search.Use/Project.ManageMembers для этой операции не требуются.
- OrganizationId/UserAccountId/AuthorizationScopeVersion только из authenticated context, не query или cursor. Сохранить join по organization_id и task id, object_type='task', lifecycle_state='active', `work.task_visible(org,task,actor)`.
- Миграция `013_object_authorization.sql:72–115`: сохранить scope_allows/scope_denied, project_denied (deny precedence), owner/manager/member и role overrides, административный доступ и прямые author/requester/assignee/watcher основания. Участие в проекте не заменяет Task.Read и не даёт безусловную видимость.
- `TaskJwtAuthenticationHandler.cs:56–91` проверяет session по claims; `PostgresSessionRepository.GetSessionRequestState:274–320` сравнивает claims scope с текущей `iam.authorization_scope_versions`. После изменения прав старый access token может получить **401 до проверки cursor**. После действительного обновления identity старый scope-bound cursor отклоняется 400. Не обещать один и тот же status для этих разных случаев.
- Каждая страница повторно проверяет текущие row ACL в SQL; никакого чтения по кешированным старым permissions. Отзыв membership/role/deny между страницами не должен возвращать запрещённые строки, а клиент очищает закрытые данные. Timestamp boundary не замораживает права.
- Отдельно проверить гонку между authentication и DB SELECT. Нельзя считать scope из claims вечно актуальным: при доказанном race повторно проверить current scope/permission в согласованной границе чтения до формирования ответа; mismatch не должен превращаться в успешный empty/terminal response. Гарантию линейного отзыва после начала SQL statement новый фильтр сам по себе не создаёт; её усиление требует отдельного security решения.
- Task business status `completed`/`cancelled` не равен lifecycle archived: при active object эти tasks сейчас входят в список. Archived/trashed tasks исключены. Parent project lifecycle не подменять новым `JOIN ... lifecycle='active'`: текущая `task_project_visible` допускает не-trashed project, а direct relationships/admin — отдельные ветви `task_visible`. Нельзя заявлять, что любая task архивного/удалённого проекта автоматически скрывается всеми ветвями. Зафиксировать и проверить фактическую политику; если выявлен конфликт с более высоким источником, отдельное решение до её изменения.

## 8. PostgreSQL: факт хранения, запрос и отдельное DB-решение

`TaskCardContent.cs:10,33,42` использует `JsonSerializerDefaults.Web` и ToJson: поле **`projectId`**, не `ProjectId`/`project_id` в JSON. Миграция `011_task_card.sql:2–8` создаёт JSONB card_content и **STORED generated `project_id uuid` = `(card_content->>'projectId')::uuid`**, composite FK `(organization_id,project_id)` в projects.projects. Фильтровать следует typed `t.project_id`, а не повторять JSON cast/LIKE/containment. NULL в task без проекта не совпадёт с заданным UUID.

Предлагаемый predicate для **отдельной filtered ветви SQL**:

```sql
-- Все текущие tenant/type/lifecycle/visibility/snapshot/keyset clauses сохраняются.
AND t.project_id = $7::uuid
ORDER BY o.updated_at DESC, o.id DESC
LIMIT $5; -- 51: одна строка lookahead
```

`$7` передавать `NpgsqlParameter<Guid>`/Uuid, не конкатенировать пользовательский текст; нумерацию проверить на полном запросе (ныне actor=$6). Для unfiltered сохранить прежний SQL. Предпочтительнее две фиксированные параметризованные ветви, чем nullable OR, который может ухудшить selectivity/generic plan. Порядок и continuation сравнивают **тот же** o.updated_at и o.id; не сортировать по project_id и не делать OFFSET.

Существующие индексы (`001_task_persistence_foundation.sql:69–107`): objects `(organization_id,object_type,lifecycle_state,updated_at DESC)` без id tie-breaker; task PK/unique `(organization_id,id)`, индексы status/deadline/schedule. `011` добавляет parent index. Во всех текущих 001–017 production migrations **нет индекса с project_id**. Индекс FK referenced table не является индексом referencing tasks.

Без schema change predicate корректен и сокращает сетевой трафик, но planner может по-прежнему сканировать N rows/вызывать visibility до получения 51 подходящей строки. Для малого проекта среди миллионов чужих задач latency может остаться высокой. Не заявлять O(M) DB cost только из факта server filtering.

**DB-D1 — отдельное PROPOSED — NOT APPROVED решение:** после согласования API и измерения утвердить B-tree на `work.tasks(organization_id,project_id,id) WHERE project_id IS NOT NULL`. Это кандидат для селективного доступа к проекту, не готовая миграция и не гарантия отсутствия sort: updated_at хранится в другой таблице. При крупном проекте planner может предпочесть ordered objects scan; оценить top-N sort, visibility cost и lookahead. Дополнение objects index колонкой id — только отдельный измеренный вариант, не автоматическая часть D1. Denormalization updated_at в work.tasks не рекомендуется для данного increment.

Стоимость: для небольшого N существующих индексов может хватить; при N≈5 000/50 000/2 000 000 и M=0/1/50/51/5 000/большая доля N проверить первую, среднюю и terminal страницы, много hidden rows, одинаковые timestamps и холодный/прогретый cache. Зафиксировать actual rows, rows removed, loops, buffers, sort/temp usage, planning/execution time для обоих SQL branches и prepared parameters. Сравнить без/с индексом только в отдельной disposable DB после разрешения D1. Учитывать цену индекса на inserts и переносе ProjectId.

На этапе 8.2 EXPLAIN ANALYZE **не выполнялся**: переменная `TASK_POSTGRES_TEST_ADMIN_CONNECTION` отсутствует, Docker daemon недоступен. Обнаружен listener 5432, но ни назначение БД, ни безопасное тестовое подключение не подтверждены; чужая БД не использовалась. Существующий PostgresTaskReadStoreTests без env делает early return: его зелёный status нельзя выдавать за DB evidence. Миграции/индексы/данные не создавались и не менялись.

Техническая сверка с официальной документацией PostgreSQL 16: [multicolumn indexes](https://www.postgresql.org/docs/16/indexes-multicolumn.html), [ORDER BY](https://www.postgresql.org/docs/16/indexes-ordering.html), [FK indexes](https://www.postgresql.org/docs/16/ddl-constraints.html), [EXPLAIN](https://www.postgresql.org/docs/16/using-explain.html). Эти источники поясняют планирование, но не заменяют измерение фактической схемы/версии сервера.

## 9. Обратная совместимость и deployment

Сначала утвердить delta и обновить нормативный API/OpenAPI/traceability отдельным изменением, затем сервер, затем desktop. В этом review OpenAPI не меняется. Старый desktop с новым сервером продолжает unfiltered v1 запросы; общий список не меняется. Filtered cursor domain не смешивается с unfiltered.

Новый desktop со старым сервером **опасен**: projectId сейчас молча игнорируется. HTTP 200/наличие совпадений/проверка возвращённых ProjectId не доказывают полноту и поддержку (первая страница может случайно содержать только совпадения). В 8.3 нужен согласованный release gate минимальной версии сервера/совместная поставка; при неподтверждённой поддержке не включать новый project-specific режим. Если необходим mixed-version rollout, отдельно утвердить support negotiation; автоматического fallback к незаметному полному обходу организации нет. Существующий 8.1 может оставаться явным legacy режимом с честным partial статусом.

## 10. Тестовая матрица для 8.3

| Группа | Обязательные сценарии и проверяемый результат |
|---|---|
| Query | omitted projectId сохраняет v1; корректный UUID/регистр; empty, whitespace, zero, malformed, duplicate, array, injection → 400; ни один invalid filter не расширяет запрос; filter/sort/page legacy regression. |
| Объём | P пустой, 1, 50, 51, 100, 101 и тысячи tasks; много чужих проектов до/между/после совпадений; tasks без проекта; одинаковый updated_at. Страницы содержат только P, порядок стабилен, terminal верен; static union равен авторизованной SQL-выборке, без потерь/дубликатов. |
| Tenant/user | Две организации, два пользователя одной org и разных org; foreign project UUID и неизвестный UUID не раскрывают existence; cursor не переносится между user/tenant. |
| Permission | Нет Task.Read → 403; действующие admin, owner, manager, active/removed member, author, requester, assignee, watcher; allow/deny и scoped roles. Filter не меняет множество разрешённых отношений. UI сохраняет собственные Project.Read/Task.Read gates. |
| Revocation | Revoke Task.Read, project membership/role, deny override, block user/device/logout между страницами; stale token и refreshed identity+old cursor проверяются отдельно; revoke между auth и SQL; кеш/показанные строки очищаются по действующим правилам. |
| Cursor | v1↔filtered v2, P=A/cursor B в обе стороны, P→omitted, malformed, >512, неизвестные поля/version, неверный hash/boundary/id, other scope; roundtrip worst-case ≤512; UUID normalization. Повтор request cursor — допустимый retry; repeated/cyclic nextCursor — остановка UI без ложного Complete. Перепаковка unsigned payload проверяется против принятого C2; тест не должен выдавать hash за MAC. |
| Изменения | Создание/обновление после boundary, задача с прежним/равным timestamp, перенос A↔B, архивирование/удаление/восстановление между страницами. Ожидания соответствуют bounded live keyset, не вымышленному snapshot; refresh возвращает актуальное разрешённое состояние. |
| Lifecycle | active task каждого business status, completed/cancelled при active; archived/trashed task; active/completed/archived/trashed parent project для каждой применимой ветви visibility. Не смешивать status и lifecycle. |
| Faults | DB connection failure/timeout/transient error без утечки SQL/PII и без fake empty page; CancellationToken отменяет OpenConnection/ExecuteReader/Read, OperationCanceledException не превращается в 500/Complete. Текущий list не имеет явного catch DB exceptions: проверить global middleware, при необходимости отдельно согласовать нормализацию 503 INTERNAL_ERROR. |
| Client | URI кодирует projectId+cursor; fake interface implementations обновлены; запросы для P не сканируют чужие pages; одна явная команда — один запрос; A→B→A, collapse/unload/dispose, late responses, offline/retry, terminal auth, session generation, Employee Picker/ViewState regressions 8.1. Нет hidden full-scan fallback. |
| Performance | Real disposable PostgreSQL с реальными ACL и generated column/FK; EXPLAIN ANALYZE BUFFERS на матрице N/M, статике и изменениях; API requests/bytes зависят от M, DB стоимость измерена отдельно; обе SQL branches и старые unfiltered запросы. |

## 11. План файлов этапа 8.3

Все пути ниже от `work/production/`; список — план, не уже выполненный diff:

| Файлы | Изменение после утверждения |
|---|---|
| `src/Task.Api/Tasks/TaskEndpoints.cs` | Parse/validation scalar projectId, передача request, ошибки. |
| `src/Task.Application/TaskReadContracts.cs`, `TaskReadCursorCodec.cs` | Optional ProjectId, v1/v2 domains, canonical binding. `ITaskReadStore.cs` — уточнить комментарий; интерфейс метода прежний. |
| `src/Task.Infrastructure/Persistence/PostgresTaskReadStore.cs` | Filtered SQL branch, typed UUID, unchanged ACL/keyset; scope race check по согласованному security contract. |
| `src/Task.Desktop/Tasks/DesktopTasksApiClient.cs` | Типизированный project list method, URI и сохранение старого метода. |
| `src/Task.Desktop/ViewModels/ProjectsViewModel.RelatedTasks.cs` после интеграции 8.1 | Один project-specific вызов с сохранением lifetime/state guards. В main такого partial пока нет; не писать поверх старого автоматического обхода без интеграционного решения. |
| `tests/Task.Tests/TaskReadCursorCodecTests.cs`, `PostgresTaskReadStoreTests.cs`, при необходимости `PostgresAuthorizationScenarioTests.cs` | Domain/DB/ACL/cost cases. |
| `tests/Task.ServiceHosts.Tests/TaskEndpointsTests.cs` и соответствующие partial tests; `tests/Task.Desktop.Tests/Tasks/DesktopTasksApiClientTests.cs`, Projects tests 8.1 и интерфейсные fakes | Parser/compatibility/URI/lifetime/отзыв прав. Точные client-test файлы подтвердить по актуальной базе перед edit. |
| Нормативный OpenAPI/API catalog/DTO traceability в новой рабочей редакции контракта; `docs/PROD-04-projects-and-members.md` | Утверждённые request semantics, ограничения и rollout. `sources/` не редактировать. |
| `src/Task.Infrastructure/Persistence/Migrations/018_…sql` (номер перепроверить) | **Только при отдельном утверждении DB-D1**. Сейчас не создавать. |

## 12. Риски и открытые вопросы

R1: отсутствие принятой project grammar — нельзя назвать B исправлением уже определённого query. R2: unsigned cursor допускает deliberate rewriting; C2 требует явного выбора. R3: updated_at keyset не даёт MVCC completeness. R4: фильтр без project index улучшит network, но может не ускорить DB. R5: старый сервер игнорирует новый параметр. R6: revocation между authentication и SQL требует отдельной race проверки. R7: lifecycle/direct relationship branches нельзя незаметно ужесточить. R8: 8.1 ещё локальный; не потерять его UI/security guards при интеграции.

Открыты: (Q1) утверждение scalar projectId вместо расширения grammar generic filter; (Q2) принятие unsigned compatibility model либо защищённого v2; (Q3) необходимость точного межстраничного snapshot — предложение сохраняет прежний bounded live keyset; (Q4) performance budget и утверждение DB-D1 по измерениям; (Q5) порядок интеграции 8.1 и release gate версии сервера. Ни один вопрос не разрешён молчаливо этим документом.

## 13. Условия согласования

- C1: владелец контракта явно утверждает B, параметры/валидацию, empty/existence semantics, ошибки, legacy filter/sort limitation и отсутствие новых permissions.
- C2: принято решение об integrity model cursor из §6; если нужен tamper-proof token — утверждены защита/ключи/rotation/512 limit до implementation.
- C3: подтверждены bounded-live semantics, business status/lifecycle/visibility и поведение при отзыве прав; новый snapshot contract отдельно не вводится.
- C4: DB-D1 рассматривается отдельно; API approval не разрешает миграцию. Для performance claim требуются реальные планы/замеры и согласованный budget.
- C5: согласованы интеграция 8.1 и безопасный server-first rollout. Общий gate не должен признавать новый client совместимым со старым silently-ignoring server.

## 14. Критерии готовности реализации и следующий шаг

Этап 8.3 начинается после C1–C5 в применимой части: опубликовать утверждённый contract delta отдельной рабочей версией, интегрировать/выбрать базу 8.1, реализовать parser→request→SQL→codec, затем desktop, выполнить матрицу §10 и targeted/general gates по фактическому diff. Реальный DB gate обязан исполняться, не early-return при отсутствии env. Индекс — лишь после DB-D1. Проверить diff, подготовить version/manifest/SHA-256/validation; roadmap менять только по evidence фактической готовности.

Готовность: нет чужих tenant/tasks, нет unfiltered fallback, фильтр работает до pagination; static dataset перечисляется полностью; cursor domains изолированы в рамках согласованного C2; старые запросы/cursors работают; изменение прав закрывает данные; UI не блокируется/не публикует поздние ответы; DB performance соответствует утверждённому budget и подтверждён планами. До выполнения этого нельзя объявлять серверную выборку реализованной.

## 15. Выполненная валидация этапа 8.2

В изолированной ветке `codex/project-task-filter-contract-8-2` на `deb16b3` проверены **4/4 TaskReadCursorCodecTests** и **166/166 TaskEndpointsTests** (Release, 0 fail/skip). Это baseline tests существующего поведения, не тесты ещё не написанного фильтра. TRX находятся в пакете review. При сборке service-host tests были существующие ASPDEPR004 warnings.

Сопоставлены ключевые исходники с main и 8.1; просмотрены нормативные ZIP members, schema, serialization, authorization и validation 8.1. `git diff --check`, контроль scope документа, hash/ZIP/manifest validation выполняются при упаковке review. Не запускались real PostgreSQL gate/EXPLAIN, новый сервер/клиент, нагрузочные или WPF tests этого предложения; их результатов нет. Готовность продукта и dashboard не повышались. Изменяется только этот proposal и артефакты review; endpoints, DTO implementation, БД, OpenAPI и permissions не изменялись.
