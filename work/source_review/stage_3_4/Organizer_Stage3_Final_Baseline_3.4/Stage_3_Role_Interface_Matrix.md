# Stage 3. Role Interface Matrix

## Нормативная база и границы проверки

Этап 3.1 использует результат Этапа 3.0 и не повторяет полный аудит упаковки. Приоритет источников:

1. `architecture_organizer.md` — финальная концепция и бизнес-состав.
2. `01_core_domain_and_data.md` — ограничения Этапа 1: Windows/WPF, server-authoritative, online-only writes, read-only cache, metadata-only files, OS/SMB ACL, optimistic locking.
3. `06_stage_2_1_normative_corrections.md` — данные, lifecycle, права, recurrence, time model, file locations.
4. `traceability.csv`, `00_MANIFEST.md`, `STAGE_3_SOURCE_INDEX.md`, `02_api_and_concurrency.md` — API, permissions, ошибки, события, sync и сценарии.
5. `Старт UX архитектуры.txt` — решения и идентификаторы Этапа 3.0.

Этап 3.4 использует нормативный OpenAPI 3.1 `1.2.0-stage2.2` (241 операций, 232 schemas, 1322 DTO fields), `dto_field_catalog.csv`, `Search_Contract.md`, catalogs permissions/errors и validation/codegen reports. Field-level проверка завершена; нормативная трассировка находится в `Stage_3_Field_Traceability.csv`.

Колонки ролей — baseline, не локальная authorization matrix. Фактическое действие определяется capabilities, project/object relations, field set и lifecycle.

| Screen/action | Admin | Manager | Employee | Observer | Permission | Hidden/disabled/forbidden |
| --- | --- | --- | --- | --- | --- | --- |
| Open own Today | Available | Available | Available | Available/read-only commands | Calendar.Read | Primary nav shown by capability |
| Switch Today to employee/department | Global/scoped capability | Own scope | Usually hidden | Hidden | Calendar.Read + directory scope | Hide if no alternative scope |
| Create InboxItem | Own | Own | Own | Hidden without manage | Inbox.ManageOwn | Hide section without read/manage |
| Create Task | Allowed scope | Project/department scope | Allowed project/own scope | Hidden | Task.Create | Picker returns only allowed targets |
| Edit Task core fields | Audited policy | Scoped | Field-level own/assigned/project | Read-only | Task.Update | Disabled with reason when field relevant |
| Change Task status | Capability | Capability | Allowed transitions for own/assigned | Hidden/read-only | Task.ChangeStatus | Show applicable transitions only |
| Assign assignee | Capability | Usually scoped | Only explicit capability | Hidden | Task.Assign | Hide if irrelevant; disable archived/offline |
| Manage watchers | Capability | Scoped | Capability | Hidden | Task.Watch | Authorization-aware people picker |
| Subtask/checklist | Capability | Scoped | Capability | Hidden | Task.Create/Update/ChangeStatus | Disabled archived/trashed/offline |
| Recurrence | Capability | Scoped | Capability | Hidden | Task.ManageRecurrence | Scope dialog mandatory |
| Bulk transition | Capability | Scoped | Common allowed selection | Hidden | Task.ChangeStatus | Capability intersection |
| View other calendars | Global | Department/project scope | Explicit scope | Project read-only scope | Calendar.Read | No hidden user/count disclosure |
| Drag/resize schedule | Capability | Scoped | Own/assigned if allowed | Hidden | Task.Update/CalendarEvent.Update | Disable offline/archived; keyboard alternative |
| Create Project | Capability | Usually available | Only if granted | Hidden | Project.Create | Hide create without capability |
| Edit Project | Capability | Owned/managed | Editor-limited | Read-only | Project.Update | Field-level masks |
| Manage Project members | Capability | Owner/Manager | Hidden | Hidden | Project.ManageMembers | Owner invariant visible |
| Project role/overrides | Capability | Owner/Manager limited | Hidden | Hidden | Project.ManageMembers/Role.Manage | Do not expose raw policy engine |
| Complete Project | Capability | Owner/Manager | Only if explicit | Hidden | Project.Update | Separate from archive |
| Archive Project | Capability | Owner/Manager | Explicit only | Hidden | Project.Archive | Disable invalid state with reason |
| Read Catalog metadata | Policy | Project/department scope | Linked/scoped | Linked/scoped | FileCatalog.Read | Separate from open |
| Open File | Capability + OS ACL | Capability + OS ACL | Capability + OS ACL | May be allowed | FileReference.Open | OS failure after visible action is valid |
| Add/relink FileLocation | Capability | Scoped if granted | Capability, own/device scope | Hidden | FileLocation.Update | Foreign local path redacted |
| Manage network resources | Available | Hidden | Hidden | Hidden | NetworkResource.Manage | Admin nav only |
| Trash CatalogItem metadata | Capability | Scoped | Capability | Hidden | FileCatalog.Delete | Always state physical file unaffected |
| Create/update Contact | Capability | Project/department scope | Scoped capability | Hidden | Contact.Create/Update | Partial fields read-only |
| Read Contact | Global policy | Scoped | Scoped | Scoped read | Contact.Read | No hidden related counts |
| Create Interaction | Capability | Scoped | Scoped capability | Hidden | Interaction.Create | No email/message side effect |
| Create Comment | Target access | Target access | Target access | Usually hidden/read-only | Comment.Create | Target rechecked |
| Moderate Comment | Capability | Rare capability | Own only unless moderator | Hidden | Comment.UpdateOwnOrModerate/Moderate | Own vs moderator distinct |
| Read Object history | Current access; audit separate | Current access | Current access | Current access | History.Read | Redact by current rights |
| Global Search | Available | Available | Available | Available | Search.Use | Server-filtered results |
| Notification action | Recipient + target capability | Same | Same | Open/read only if allowed | Notification.ManageOwn + target | Server can forbid stale toast action |
| Unarchive | Object capability | Scoped | Scoped | Usually hidden | Archive.Restore/object permission | Object remains read-only until success |
| Restore from Trash | Capability | Scoped | Own/scoped if granted | Hidden | Trash.Restore/object Restore | Handle parent/name conflict |
| Purge | Explicit admin capability | Hidden unless explicit | Hidden | Hidden | Trash.Purge | Disabled by retention/legal hold |
| Edit own settings | Available | Available | Available | Available | Settings.UpdateOwn | Server-managed fields locked |
| Own sessions/devices | Available | Available | Available | Available | Session/Device own permissions | Current session guard |
| Create/block users | Available | Read scope unless explicit | Hidden | Hidden | User.Create/User.Block | No generic delete |
| Manage departments | Available | Read own scope | Hidden | Hidden | Department.Manage | Cycle checks |
| Manage roles/permissions | Available | Hidden unless explicit | Hidden | Hidden | Role.Manage | Dangerous permission warning |
| Explain effective permissions | Available | If granted | Hidden | Hidden | Authorization.Explain | Safe explanation only |
| Other devices/sessions | Available | If granted | Hidden | Hidden | Device.Revoke/Session.RevokeOwnOrAll | Audit mandatory |
| Health/jobs/backups/audit | Per capability | Object history only | Object history only | Object history only | System.HealthRead/Backup/Audit | Admin sections hidden |

## Правила видимости действий

1. **Hidden:** действие не нужно для понимания поверхности и capability отсутствует; admin section целиком скрывается без admin capabilities.
2. **Disabled с причиной:** пользователь понимает действие, но оно временно невозможно из-за offline, archived/trashed state, invalid transition, mixed selection или отсутствия обязательного input.
3. **Visible read-only:** поле нужно для понимания объекта, но редактирование запрещено; рядом может быть безопасное объяснение scope/owner.
4. **Server forbidden после показа:** capability устарела или ABAC/object state изменились. Клиент откатывает preview, обновляет capabilities и показывает нейтральное объяснение.
5. **Никогда не скрывать системный режим:** offline/read-only, scope change, maintenance, storage full и conflict видимы независимо от роли.

## Самопроверка

| Проверка | Результат |
| --- | --- |
| Все четыре системные роли представлены | PASS |
| Project roles не подменены system roles | PASS |
| Object/field scope учтён | PASS |
| OS ACL отделён от app permission | PASS |
| Admin не получает произвольный shell | PASS |
| Observer остаётся read-only | PASS |


## Stage 3.4. Field/capability matrix

| Field group | Read capability | Write capability | UI rule | Server rule |
|---|---|---|---|---|
| Task core | Task.Read | Task.Create / Task.Update | Editor binds only TaskCreate/TaskPatch fields | Recheck relation/project/object state |
| Assignees | Task.Read | Task.Assign | Multi-select; primary assignee separate | Max 100; target users must be visible/eligible |
| Watchers | Task.Read | Task.ManageWatchers | Multi-select; not an edit grant | Watcher relation does not grant Task.Update |
| Recurrence | Task.Read | Task.ManageRecurrence | Rule editor and scope dialog | Versioned series/apply-change invariants |
| Reminder | Reminder.ManageOwn | Reminder.ManageOwn | Recipient/target constrained by response and scope | Server validates recipient/target/trigger combination |
| Project members | Project.Read | Project.ManageMembers | Role and overrides shown by capability | Owner invariant and explicit deny enforced |
| FileLocation | FileCatalog.Read / FileReference.Open | FileLocation.Update | Path may be redacted; edit only with capability | Application permission does not bypass OS/SMB ACL |
| Contact/Company | Contact.Read | Contact.Create / Contact.Update | Partial fields remain redacted | Server filters hidden relations |
| Notification preferences | Settings.ReadOwn | Settings.UpdateOwn | Single server-backed preference form | ETag/If-Match required on PUT |
| User/Admin | Resource-specific read | User/Role/Department/Device/NetworkResource permissions | System fields read-only; secrets write-only | Deny by default and audit |
| Search | Search.Use | — | All filters serialized to server | Authorization + filters before pagination |
