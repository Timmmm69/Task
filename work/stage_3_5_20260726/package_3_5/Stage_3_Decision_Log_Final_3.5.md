# Stage 3. UX Decision Log

## Нормативная база и границы проверки

Этап 3.1 использует результат Этапа 3.0 и не повторяет полный аудит упаковки. Приоритет источников:

1. `architecture_organizer.md` — финальная концепция и бизнес-состав.
2. `01_core_domain_and_data.md` — ограничения Этапа 1: Windows/WPF, server-authoritative, online-only writes, read-only cache, metadata-only files, OS/SMB ACL, optimistic locking.
3. `06_stage_2_1_normative_corrections.md` — данные, lifecycle, права, recurrence, time model, file locations.
4. `traceability.csv`, `00_MANIFEST.md`, `STAGE_3_SOURCE_INDEX.md`, `02_api_and_concurrency.md` — API, permissions, ошибки, события, sync и сценарии.
5. `Старт UX архитектуры.txt` — решения и идентификаторы Этапа 3.0.

Этап 3.4 использует нормативный OpenAPI 3.1 `1.2.0-stage2.2` (241 операций, 232 schemas, 1322 DTO fields), `dto_field_catalog.csv`, `Search_Contract.md`, catalogs permissions/errors и validation/codegen reports. Field-level проверка завершена; нормативная трассировка находится в `Stage_3_Field_Traceability.csv`.

## 1. Реестр решений

| ID | Решение | Обоснование | UX-следствие | Ограничение/влияние |
| --- | --- | --- | --- | --- |
| DEC-001 | Сопоставлять источники по фактическому содержимому | Этап 3.0 выявил неверные имена файлов. | Source references используют content identity. | Снижает риск неверной нормативной ссылки. |
| DEC-002 | Продолжить без машиночитаемого OpenAPI | Traceability содержит 241 operation/permission/codes. | Endpoint-level UX traceability complete; DTO field audit pending. | Корректный OpenAPI нужен до field acceptance. |
| DEC-003 | Стабильные UX IDs начинаются с Этапа 3 | Предыдущий registry отсутствовал. | SCR/FLOW/STATE IDs не переиспользуются. | Поддерживает PRD/UI/QA traceability. |
| DEC-004 | Capability-driven UI | Hybrid RBAC/ReBAC/ABAC, default deny. | Client renders capabilities; server rechecks every command. | Role name never authorizes locally. |
| DEC-005 | Разделить status/derived/archive/trash | Источники содержат разные lifecycle concepts. | Independent badges/actions/surfaces. | Нет auto-archive/delete. |
| DEC-006 | Read-only cache, no offline queue | Stage 1/2.1 forbid offline writes. | All writes disabled when API unavailable. | Consistency over offline editing. |
| DEC-007 | Один main window MVP | Multi-window not required and complicates drafts/conflicts. | Deep links focus existing instance; panels/pages/dialogs. | Detached windows deferred. |
| DEC-008 | List-detail основной pattern | Desktop frequency of scanning and narrow actions. | Tasks/Projects/Files/CRM use master+inspector+full card. | Avoid modal CRUD maze. |
| DEC-009 | Full editor explicit save | Versioned aggregate and conflict handling. | Ctrl+S/Ctrl+Enter; narrow commands immediate. | No whole-card autosave. |
| DEC-010 | Today uses one read model | GET /today exists. | One primary query; section fallback; detail on selection. | Avoid N+1. |
| DEC-011 | Date-only separate calendar lane | scheduled_date differs from start_at. | Unscheduled row above timeline. | No midnight fiction. |
| DEC-012 | Deadline never positions timeline | Deadline is latest acceptable instant. | Separate field/badge and overdue calculation. | Avoid time ambiguity. |
| DEC-013 | Reminder separate from schedule | Reminder is recipient delivery state. | Separate editor and actions. | Snooze does not move Task. |
| DEC-014 | Overlap is warning | Domain permits overlap. | Local warning + server-confirmed write. | No hard block. |
| DEC-015 | Keyboard alternative for drag | Accessibility and Windows productivity. | Reschedule/move dialogs mirror drag. | No drag-only action. |
| DEC-016 | Single-instance deep-link routing | Multiple local editors increase confusion. | Second link focuses existing card/editor. | Reduces duplicate drafts. |
| DEC-017 | Catalog tree virtual and lazy | Filesystem is not authoritative hierarchy. | Move changes parent metadata only; no recursive scan. | Safety and performance. |
| DEC-018 | No physical file delete action | Architecture forbids hidden file operations. | Trash/purge are metadata-only with explicit text. | No endpoint/UI for disk delete. |
| DEC-019 | Location ranking visible/overridable | Multipath may choose different copy. | Open surface shows chosen class/path and alternatives. | Avoid wrong copy silently. |
| DEC-020 | Restrict foreign local path visibility | Device-scoped paths are sensitive. | Show owner/device; full path only when authorized. | Reduces leakage. |
| DEC-021 | Search hides hidden-result counts | Authorization-aware search. | Only permitted results/counts. | Prevents existence leakage. |
| DEC-022 | Toast actions delegate server commands | Toast may be stale. | Recheck target permission/version/state. | No local business mutation. |
| DEC-023 | Archived objects read-only | Normative lifecycle. | No normal Save; separate unarchive. | History/links remain by rights. |
| DEC-024 | Generic Trash dispatches object-specific restore | APIs include universal and specific operations. | UI uses correct endpoint/invariants. | No generic assumption for all entities. |
| DEC-025 | UserAccount never generic-deleted | Stage 2.1 correction. | Block/deactivate/reactivate and revoke sessions. | Audit identity retained. |
| DEC-026 | Admin exposes bounded tasks, no shell | Architecture prohibits arbitrary OS/DB control. | Allowlisted jobs/health/backup plans only. | No SQL console/password view. |
| DEC-027 | Settings label scope | Server/user/device settings differ. | Scope and save behavior visible. | No false sync expectations. |
| DEC-028 | No custom theme without effect | Appearance only if implemented. | Follow Windows theme/high contrast; local density/reduced motion if real. | No decorative settings. |
| DEC-029 | History stores route state, not snapshots | Cross-module deep links frequent. | Restore filters/scroll/selection then reauthorize. | Avoid stale data restoration. |
| DEC-030 | Hidden vs disabled policy | Permission/state/offline have different meaning. | Hide irrelevant forbidden; disable temporary known state; handle server deny. | Consistent action discoverability. |
| DEC-031 | Conflict merge user-controlled | Optimistic locking rejects stale writes. | Suggest different-field reapply; never auto-merge same field/people/path/status. | No last-write-wins. |
| DEC-032 | Scope change purges before render | Authorization scope invalidates cache. | Block/remove sensitive projections before bootstrap. | No stale authorization leak. |
| DEC-033 | Context menu mirrors command registry | Desktop convention + accessibility. | Command bar/menu/palette share commands. | No menu-only functionality. |
| DEC-034 | Bulk selection extent explicit | Cursor paging makes Ctrl+A ambiguous. | Default loaded/visible; all-filtered only explicit supported operation. | Prevents accidental broad updates. |
| DEC-035 | Errors include safe recovery and traceId | Stable error catalog exists. | No stack; retry only safe; no hidden-object leak. | Supportability with security. |

## 2. Правила изменения решений

- Published IDs are never reused. A superseded decision remains in the log with status `Superseded` and a link to the replacement.
- A business-function change requires concept update; an architecture-boundary change requires ADR and Stage 1/2.1 correction before UX adoption.
- A new mutable action requires data contract, permission, error model, concurrency behavior and traceability row before it appears in UI.
- Field-level UX decisions изменяются только после изменения нормативного OpenAPI и повторной генерации traceability.

## 3. Open issues carried forward

| ID | Severity | Issue | Impact | Required closure |
| --- | --- | --- | --- | --- |
| GAP-001 | Closed | Валидный OpenAPI 3.1 получен | Field traceability построена без provisional полей | Closed in 3.4 |
| GAP-002 | Closed | Search UI подтверждён контрактом | contactIds/hasFiles/lifecycle/types фильтруются server-side | Closed in 3.4 |
| GAP-003 | Medium | Windows toast availability depends on OS/user settings | Delivery cannot be guaranteed by application | Expose diagnostics and notification permission state |
| GAP-004 | Medium | Physical file access depends on current Windows/SMB ACL and device state | Metadata permission cannot guarantee open | Keep differentiated path diagnostics and alternate-location flow |


## 4. Решения Этапа 3.4

| ID | Решение | Обоснование | UX-следствие | Статус |
|---|---|---|---|---|
| DEC-036 | OpenAPI 3.1 `1.2.0-stage2.2` является нормативным field contract | Validation/codegen PASS и 241/241 parity | Никаких provisional fields | Accepted |
| DEC-037 | PATCH: omitted и null имеют разные значения | DTO catalog фиксирует semantics | Dirty-field serialization; clear только nullable | Accepted |
| DEC-038 | Concurrency разделяется на 412/428/409 | OpenAPI response contract | Reload/compare/reapply; никакого silent overwrite | Accepted |
| DEC-039 | Field errors привязываются к canonical field paths | ProblemDetails + DTO field catalog | Inline errors и focus first invalid | Accepted |
| DEC-040 | Search filtering выполняется только сервером до pagination | Search Contract 2.2 | cursor reset при любом filter change; client post-filter forbidden | Accepted |
| DEC-041 | `Stage_3_Field_Traceability.csv` является нормативной картой controls | DoD Этапа 3.4 | Contract change требует regeneration/re-audit | Accepted |
| DEC-042 | UI не показывает contract-unsupported settings | Нет writable DTO fields для avatar, urgency thresholds и дополнительных notification channels | Controls удалены; бизнес-gap не маскируется invented field | Accepted |

## 5. Решения Этапа 3.5

| ID | Решение | Контрактное основание | UX-следствие | Статус |
|---|---|---|---|---|
| DEC-043 | Stage 2.3.1 — нормативный technical contract | OpenAPI `1.2.0-stage2.3`, validation/codegen/runtime PASS | Delta только OQ-001/OQ-003; 2.2 — history/backward compatibility | Accepted |
| DEC-044 | Scale owner — organization; personal override нет | `x-owner`, `x-user-override` | Один editor в SCR-153; view Settings.ReadOwn, write System.Configure | Accepted |
| DEC-045 | Semantic urgency первична; displayToken вторичен | UrgencyLevel + schema descriptions | Label/icon/text; HEX picker не изобретается | Accepted |
| DEC-046 | Scale заменяется целиком и versioned | PUT Patch DTO, If-Match, Idempotency-Key, ETag | Four intervals; compare/reapply conflict; audited reset | Accepted |
| DEC-047 | Employee — отдельный search type | `types=employee`, resultType, EmployeeSearchResult | Separate group; не admin users/contacts/userIds filter | Accepted |
| DEC-048 | Employee filtering/redaction — server before cursor | Search extensions | No post-filter; blocked omission; nullable redaction; cursor reset | Accepted |
| DEC-049 | Новые SCR/STATE не нужны | Existing IDs полностью покрывают surface/state semantics | Только CMP-001/CMP-002 и FLOW-035 | Accepted |

`DEC-042` исторически корректен для 3.4 и superseded только в части urgency thresholds решениями DEC-044–046; общий запрет unsupported controls сохраняется.
