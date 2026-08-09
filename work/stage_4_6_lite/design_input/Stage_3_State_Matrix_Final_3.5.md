# Stage 3. State Matrix

## Нормативная база и границы проверки

Этап 3.1 использует результат Этапа 3.0 и не повторяет полный аудит упаковки. Приоритет источников:

1. `architecture_organizer.md` — финальная концепция и бизнес-состав.
2. `01_core_domain_and_data.md` — ограничения Этапа 1: Windows/WPF, server-authoritative, online-only writes, read-only cache, metadata-only files, OS/SMB ACL, optimistic locking.
3. `06_stage_2_1_normative_corrections.md` — данные, lifecycle, права, recurrence, time model, file locations.
4. `traceability.csv`, `00_MANIFEST.md`, `STAGE_3_SOURCE_INDEX.md`, `02_api_and_concurrency.md` — API, permissions, ошибки, события, sync и сценарии.
5. `Старт UX архитектуры.txt` — решения и идентификаторы Этапа 3.0.

Этап 3.4 использует нормативный OpenAPI 3.1 `1.2.0-stage2.2` (241 операций, 232 schemas, 1322 DTO fields), `dto_field_catalog.csv`, `Search_Contract.md`, catalogs permissions/errors и validation/codegen reports. Field-level проверка завершена; нормативная трассировка находится в `Stage_3_Field_Traceability.csv`.

Матрица различает бизнес-состояние, lifecycle, вычисляемое состояние и техническое состояние UI. Offline write queue отсутствует.

| Surface | State | Trigger | UI behavior | Allowed actions | Message | Recovery | API/error |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Global shell | Initial | Route not initialized | Shell chrome only, no fake empty data | Login/diagnostics | Подготовка приложения… | Start auth/bootstrap | — |
| Any surface | Loading | No usable data; request pending | Skeleton matching final layout | Cancel navigation; retry after timeout | Загрузка… | Server response/retry | — |
| Any surface | Refreshing | Usable data + background read | Keep data/selection; subtle progress | Continue read | Обновляем данные… | Atomic apply | — |
| Any list | Empty | Authorized unfiltered result = 0 | Purpose-specific empty + permitted create | Create; adjust scope | Здесь пока нет элементов. | Create/navigate | 200 empty |
| Any list | FilteredEmpty | Filters yield 0 | Keep filters visible | Reset/modify | По выбранным условиям ничего не найдено. | Reset filters | 200 empty |
| Any editor | ValidationError | 422 fieldErrors | Keep draft; inline errors; focus first | Correct; cancel | Проверьте отмеченные поля. | Resubmit | VALIDATION_FAILED |
| Any command | Forbidden | 403 policy deny | Undo optimistic preview; refresh capability | Close; external access request | Действие недоступно для этого объекта. | Reload capabilities | FORBIDDEN |
| Deep link/card | ObjectUnavailable | 404 hidden/not found/access revoked | Neutral unavailable; remove sensitive detail | Back; search | Объект недоступен или больше не существует. | Navigate away | OBJECT_NOT_VISIBLE |
| Versioned editor | Conflict | If-Match stale | Keep local draft; open compare | Reload; compare; reapply; discard | Объект изменён после открытия. | Fetch current + new If-Match | VERSION_CONFLICT |
| Write surface | PreconditionRequired | Missing If-Match | Block repeat; refresh object | Refresh | Нужно обновить данные перед сохранением. | GET current version | PRECONDITION_REQUIRED |
| Archived object | Archived | Lifecycle archived | Read-only banner; no normal Save | History; unarchive if allowed | Объект в архиве и доступен только для чтения. | Unarchive | OBJECT_ARCHIVED |
| Trashed object | Trashed | Lifecycle trashed | Tombstone view; restore/purge only | Restore; back | Объект находится в корзине. | Restore | OBJECT_DELETED |
| Global shell | ServerUnavailable | REST/readiness failure | Persistent banner; cache read-only; writes disabled | Retry; diagnostics; local file open | Нет подключения. Показаны данные на {time}. | Reconnect + sync | DATABASE_UNAVAILABLE / DEPENDENCY_UNAVAILABLE |
| Global shell | Reconnecting | Server likely returns | Keep read-only until auth+sync | Diagnostics | Восстанавливаем подключение… | Auth + change feed | — |
| Global shell | Maintenance | 503 retryAfter | Writes blocked; cached read allowed | Retry later; diagnostics | Сервер временно на обслуживании. | Reconnect | MAINTENANCE_MODE |
| Global shell | StorageFull | 507 | Critical banner; all writes disabled | Admin diagnostics | На сервере недостаточно места. Изменения недоступны. | Admin frees storage | STORAGE_FULL |
| Global shell | ClientUnsupported | 426 | Blocking update page | Update; close | Требуется обновление приложения. | Install signed client | CLIENT_VERSION_UNSUPPORTED |
| Sync | SyncPending | Invalidation/startup catch-up | Current data + updating indicator; sensitive writes may wait | Sync now; diagnostics | Применяем изменения… | Ack cursor | — |
| Sync | CursorExpired | 410 compacted cursor | Clear disposable projections | Bootstrap | Локальные данные нужно обновить заново. | Bootstrap | SYNC_CURSOR_EXPIRED |
| Sync | AccessScopeChanged | 409 scopeVersion | Block sensitive routes; purge before render | Reauthenticate if needed | Права доступа изменились. Обновляем данные. | Bootstrap new scope | SYNC_SCOPE_CHANGED |
| Search | OfflinePartial | Server unavailable | Cache-only results + completeness banner | Reconnect; clear query | Показаны только сохранённые результаты. | Online search | DATABASE_UNAVAILABLE |
| Search | ZeroResults | Search returns 0 | Suggestions; no hidden count | Adjust query/filter | Ничего не найдено. | New query | 200 empty |
| Today | SectionFailure | One composed block fails | Render other blocks; inline retry | Retry block; source module | Не удалось загрузить этот блок. | Retry read model | TIMEOUT / DEPENDENCY_UNAVAILABLE |
| Today | NoItems | All Today blocks empty | Date + quick create; no celebratory noise | Create task/event; Inbox | На сегодня ничего не запланировано. | Plan work | 200 empty |
| Task | InvalidTransition | State machine rejects | Refresh status; allowed transitions only | Open; choose allowed | Переход недоступен в текущем состоянии. | Reload | INVALID_STATE_TRANSITION |
| Task | SubtaskDepthExceeded | Parent already has parent | Keep draft; offer sibling | Create sibling | Допустим только один уровень подзадач. | Change parent | SUBTASK_DEPTH_EXCEEDED |
| Task dependency | Cycle | New edge creates cycle | Highlight edge; no save | Remove/change link | Эта связь создаёт цикл. | Edit relation | DEPENDENCY_CYCLE |
| Recurrence | RuleInvalid | Rule/timezone validation | Highlight rule; preview issue | Correct | Правило повторения некорректно. | Preview/resubmit | RECURRENCE_RULE_INVALID |
| Recurrence | BackgroundApply | Series change accepted async | Affected count/progress | View status; close | Изменение серии применяется… | Events/poll | 202/background |
| Calendar | RangeTooLarge | Range exceeds API limit | Keep current range; offer valid view | Choose day/week/month | Выбран слишком большой период. | Reduce range | CALENDAR_RANGE_TOO_LARGE |
| Calendar drag | DragConflict | Move/resize stale | Visual rollback + conflict dialog | Reload; new slot | Расписание изменилось до сохранения. | Fetch/retry | VERSION_CONFLICT |
| Calendar | OverlapWarning | Visible intervals overlap | Nonblocking warning + scope caveat | Proceed; adjust; inspect | В это время уже есть другие элементы. | User choice | Warning |
| Inbox conversion | TargetForbidden | Target scope lost | Keep InboxItem unchanged; clear target | Choose another | Нет доступа к выбранному проекту. | Refresh picker | FORBIDDEN |
| File open | NoLocation | No authorized/applicable location | Do not invoke Shell; recovery | Add/relink; owner | Нет подходящего пути для этого устройства. | Add/choose location | FILE_NO_LOCATION |
| File open | NotFound | OS path missing | Categorized dialog | Retry; alternative; relink | Файл не найден по выбранному пути. | Relink/add path | FILE_NOT_FOUND |
| File open | AccessDenied | OS/SMB ACL deny | No ACL bypass UI | Alternative; contact owner | Windows не разрешила открыть этот путь. | External ACL fix | FILE_ACCESS_DENIED |
| File open | NetworkUnavailable | DNS/SMB/timeout | Keep record; show check time | Retry; alternative | Сетевой ресурс временно недоступен. | Network recovers | NETWORK_RESOURCE_UNAVAILABLE |
| File location | UnsafePath | Path/root/scheme rejected | Preserve old location; highlight new | Choose allowed path | Этот путь запрещён политикой безопасности. | Native picker | UNSAFE_PATH |
| File open | UnsafeType | File type blocked | Never launch | Close; contact admin | Этот тип файла нельзя открыть из приложения. | Policy change | UNSAFE_FILE_TYPE |
| File location | OtherDeviceLocal | Path scoped to another device | Show owner/device; redact path | Alternative; add shared path | Файл доступен только на устройстве «{device}». | Add location | FILE_NO_LOCATION |
| Project members | OwnerInvariant | Operation leaves no owner | Block save; required sequence | Transfer owner first | У проекта должен оставаться владелец. | Transfer ownership | INVALID_STATE_TRANSITION |
| Project | Completed | Business status completed | Do not label archived; read-mostly by policy | Reopen/archive | Проект завершён. | Reopen/archive | — |
| Restore | NameConflict | Unique active name/link conflict | Show existing + rename/parent | Rename; parent; cancel | Восстановление конфликтует с существующим объектом. | Resolve | DUPLICATE_RESOURCE |
| Restore | ParentUnavailable | Original parent hidden/purged | Offer allowed parent; no silent root move | Choose parent | Исходная папка или проект недоступны. | Select parent | OBJECT_NOT_VISIBLE |
| Trash purge | RetentionBlocked | Before purgeAfter/legal hold | Disable purge with reason/date | Wait; restore | Окончательное удаление пока недоступно. | Wait/release hold | INVALID_STATE_TRANSITION |
| Notification action | TargetChanged | Version/state changed | No success claim; show current state | Open; retry allowed | Объект уже изменён. | Refresh | VERSION_CONFLICT / INVALID_STATE_TRANSITION |
| Notification action | TargetForbidden | Permission revoked | No target details | Close | Действие больше недоступно. | None | FORBIDDEN |
| Admin job | BackgroundOperation | 202 accepted | Immutable request + status/audit link | Leave; refresh | Операция выполняется в фоне. | Poll/events | 202 |
| Admin backup | BackupFailed | Worker failure | Stage/safe error/last success | Retry; diagnostics | Резервное копирование завершилось с ошибкой. | Correct dependency | DEPENDENCY_UNAVAILABLE |
| Admin health | DatabaseUnavailable | Readiness false | Critical global write block | Diagnostics | База данных недоступна. | Infrastructure recovery | DATABASE_UNAVAILABLE |
| Any command | RateLimited | 429 Retry-After | Keep draft/selection | Retry later; cancel | Слишком много запросов. Повторите позже. | Retry safe reads only | RATE_LIMITED |
| Any read | Timeout | 504 | Keep old data as stale | Retry | Сервер не ответил вовремя. | Retry safe read | TIMEOUT |
| Any command | IdempotencyKeyReused | Same key/different request | No same-key retry; preserve draft | Generate new key after user action | Не удалось безопасно повторить запрос. | New key | IDEMPOTENCY_KEY_REUSED |
| Any surface | InternalError | Unhandled 500 | Sanitized text + traceId | Retry safe; copy traceId | Внутренняя ошибка. Код: {traceId}. | Support/admin | INTERNAL_ERROR |
| Any surface | PartialAccess | Fields/relations filtered | Render available; no hidden counts | Continue | Часть данных недоступна по правам. | Scope change | Filtered response/FORBIDDEN |
| Any list | StaleSelection | Selected item removed/hidden | Clear selection; neutral details | Back; select another | Выбранный объект больше недоступен. | Refresh list | OBJECT_NOT_VISIBLE |

## Правила применения

- `Empty` используется только после успешного авторизованного ответа; отсутствие cache или ошибка не показываются как empty.
- `Forbidden` не заменяет `ObjectUnavailable`: hidden object не раскрывается.
- `Archived` и `Trashed` блокируют normal editing, но дают lifecycle actions по capability.
- Retry автоматизируется только для safe reads или идемпотентных команд с тем же request hash/key.
- Partial access не показывает количество скрытых объектов/полей.

## Контроль

| Проверка | Результат |
| --- | --- |
| Loading/empty/error | PASS |
| Offline/stale/reconnecting | PASS |
| Conflict/precondition | PASS |
| File-specific diagnoses | PASS |
| Lifecycle/restore/purge | PASS |
| Permissions/scope change | PASS |
| Admin/background/health | PASS |


## Stage 3.4. Contract-dependent states

| State ID | Surface | Trigger | UI behavior | Recovery | Stable error |
|---|---|---|---|---|---|
| STATE-007 | Любая форма | fieldErrors / DTO constraint | Inline error at canonical field path; focus first invalid; retain draft | Correct value and resubmit | VALIDATION_FAILED |
| STATE-014 | Versioned editor | stale If-Match | Preserve draft; load server version; compare/reapply/discard | GET current, resend with new ETag | VERSION_CONFLICT |
| STATE-025 | Versioned editor | If-Match missing | Block blind retry; refresh current object | GET current ETag | PRECONDITION_REQUIRED |
| STATE-026 | Search | cursor/filter hash mismatch | Discard cursor, keep filters, restart page 1 | Repeat without cursor | SEARCH_CURSOR_INVALID |
| STATE-027 | Search | snapshot/scope cursor expired | Discard cursor, keep query/filters, restart page 1; explain refresh | Repeat without cursor | SEARCH_CURSOR_EXPIRED |
| STATE-028 | Form | nullable field explicitly cleared | Serialize `null`; do not omit field | Save | — |
| STATE-029 | Form | field unchanged in PATCH | Omit property; preserve server value | Save | — |
| STATE-030 | Partial-access card | response redacts field/relation | Show neutral redaction marker; no hidden count/value | Continue or request access externally | FORBIDDEN / filtered response |
| STATE-031 | FileLocation | unsafe path/type or inaccessible resource | Keep previous valid path; do not invoke shell | Choose allowed path/alternative | UNSAFE_PATH / UNSAFE_FILE_TYPE / FILE_ACCESS_DENIED / NETWORK_RESOURCE_UNAVAILABLE |

`STATE-026` и `STATE-027` запрещают локальную постфильтрацию ранее полученных страниц.

## Stage 3.5 — применимость состояний

Новые STATE ID не созданы: подтверждённые случаи полностью покрываются опубликованной семантикой.

| Requested case | Existing state / behavior | Application |
|---|---|---|
| interval overlap/order/missing coverage | STATE-007 | Inline boundary error; focus first invalid; keep draft; `VALIDATION_FAILED` |
| reset pending | Any command/Loading | Disable duplicate command; это transient, не новый durable state |
| version conflict | STATE-014 | Preserve draft; GET current; compare/reapply/discard |
| missing If-Match | STATE-025 | Refresh scale/ETag; no blind retry |
| urgency settings unavailable | ServerUnavailable | Cached scale read-only; retry; no offline writes |
| employee redacted | STATE-030 | Neutral placeholders; no hidden value/count |
| employee blocked | server-filtered absence | Omitted unless `User.Block`; не discoverable state |
| employee unavailable/stale | ObjectUnavailable / StaleSelection | Clear sensitive details; neutral unavailable; focus results |
| mixed search partial failure | OfflinePartial / ServerUnavailable | Permitted stale results + banner; no invented per-group success contract |

`STATE-026/027` cursor recovery также учитывает employee visibility policy version; client post-filter остаётся запрещён.
