# Stage 3.5. Field-Level Contract Delta

**Статус:** нормативный targeted update  
**OpenAPI:** `1.2.0-stage2.3`  
**OpenAPI SHA-256:** `36C15DFF5ADBA0041FCFD79F5A0D203835DAC5CDD4AD24122BCD92177C13220D`  
**Scope:** только contract-dependent формы, controls, states, flows и traceability.

## 1. Итог изменения

- Полная IA, app shell, navigation, lifecycle, offline/read-only и conflict model сохранены.
- Проверены **38 экранов**, **624 controls**, **1078 field/action rows**.
- `AUD-001`, `GAP-001`, `GAP-002` закрыты.
- Field traceability не содержит `unverified`.
- Search UI полностью совпадает с OpenAPI и концепцией; client post-filtering запрещён.

## 2. Общие контрактные правила

1. **Create:** UI отправляет конкретный `*Create` DTO; required берётся только из schema.
2. **PATCH:** omitted = unchanged; explicit `null` = clear только для nullable поля.
3. **Enums:** только ComboBox/radio/typed chips; свободный текст не используется.
4. **ReadOnly/WriteOnly:** system fields не редактируются; write-only значения не отображаются после отправки.
5. **Concurrency:** versioned writes используют ETag/If-Match; missing → 428 `PRECONDITION_REQUIRED`, stale → 412 `VERSION_CONFLICT`, domain conflict → 409.
6. **Validation:** `ProblemDetails.fieldErrors` маппятся на canonical field paths; form-level errors показываются отдельно.
7. **Permissions:** control availability строится по capability, но действие всегда повторно проверяет сервер.
8. **Partial access:** redacted/filtered значения не восстанавливаются и не раскрывают hidden counts.

## 3. Task editor

### 3.1. Core fields

| Field | Type | Format | Required | Nullable | ReadOnly | Enum/Limits | Default | PATCH semantics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| projectId | string\|null | uuid | False | True | no | — | — | — |
| parentTaskId | string\|null | uuid | False | True | no | — | — | — |
| title | string | — | True | False | no | minLength=1; maxLength=500 | — | — |
| description | string\|null | — | False | True | no | maxLength=50000 | — | — |
| authorUserId | string | uuid | True | False | no | — | — | — |
| requesterUserId | string\|null | uuid | False | True | no | — | — | — |
| primaryCounterpartyObjectId | string\|null | uuid | False | True | no | — | — | — |
| status | string | — | False | False | no | enum=["new", "in_progress", "review", "completed", "cancelled"] | — | — |
| priority | string | — | False | False | no | enum=["low", "normal", "high", "critical"] | — | — |
| scheduledDate | string\|null | date | False | True | no | — | — | — |
| startTimeLocal | string\|null | time | False | True | no | — | — | — |
| scheduleTimeZone | string\|null | — | False | True | no | maxLength=64 | — | — |
| plannedDurationMinutes | integer\|null | int32 | False | True | no | min=1; max=10080; minimum=1; maximum=10080 | — | — |
| deadlineAt | string\|null | date-time | False | True | no | — | — | — |
| assigneeIds | array | — | False | False | no | maxItems=100; uniqueItems=true | — | — |
| watcherIds | array | — | False | False | no | maxItems=100; uniqueItems=true | — | — |

### 3.2. Нормативные решения

- `authorUserId` = автор; `requesterUserId` = постановщик/assigner. Они не объединяются.
- `assigneeIds` и `watcherIds` ограничены 100 уникальными UUID; отдельные quick editors используют `TaskAssigneesReplace` и `TaskWatchersReplace`.
- `scheduledDate`, `startTimeLocal`, `scheduleTimeZone` образуют локальную временную модель; `startAtUtc` read-only derived.
- `plannedDurationMinutes`: 1–10080. `deadlineAt` не размещает задачу на timeline.
- Статус: `new`, `in_progress`, `review`, `completed`, `cancelled`. Просрочка вычисляется и не является enum value.
- Приоритет: `low`, `normal`, `high`, `critical`.
- Подзадача = Task с `parentTaskId`; глубина ограничивается сервером `SUBTASK_DEPTH_EXCEEDED`.
- Checklist создаётся отдельными DTO; порядок меняется через `OrderKeys`, completion через item PATCH.
- Основной контрагент: `primaryCounterpartyObjectId`; дополнительные контакты и файлы: typed `ObjectLinkCreate`.
- Recurrence и reminders не встраиваются как произвольный JSON TaskPatch; используются отдельные editors/operations.

## 4. Project editor

| Field | Type | Format | Required | Nullable | ReadOnly | Enum/Limits | Default | PATCH semantics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| name | string | — | True | False | no | minLength=1; maxLength=300 | — | — |
| description | string\|null | — | False | True | no | maxLength=20000 | — | — |
| ownerUserId | string | uuid | True | False | no | — | — | — |
| managerUserId | string\|null | uuid | False | True | no | — | — | — |
| status | string | — | False | False | no | enum=["planning", "active", "paused", "completed"] | — | — |
| startDate | string\|null | date | False | True | no | — | — | — |
| plannedEndDate | string\|null | date | False | True | no | — | — | — |
| actualEndAt | string\|null | date-time | False | True | no | — | — | — |
| defaultTimeZone | string\|null | — | False | True | no | maxLength=64 | — | — |
| colorCode | string\|null | — | False | True | no | maxLength=9; pattern="^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$"; maxLength=9 | — | — |

- Owner/manager/participants используют отдельные UUID selectors и member operations.
- Project roles и permission overrides редактируются только с `Project.ManageMembers` / `Role.Manage`.
- Business status не смешивается с archive/trash lifecycle.
- Archive/unarchive/delete/restore/transfer ownership являются отдельными commands; normal PATCH их не имитирует.

## 5. Calendar editor

| Field | Type | Format | Required | Nullable | ReadOnly | Enum/Limits | Default | PATCH semantics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| projectId | string\|null | uuid | False | True | no | — | — | — |
| title | string | — | True | False | no | minLength=1; maxLength=500 | — | — |
| description | string\|null | — | False | True | no | maxLength=20000 | — | — |
| eventDate | string | date | True | False | no | — | — | — |
| isAllDay | boolean | — | True | False | no | — | — | — |
| startAtUtc | string\|null | date-time | False | True | no | — | — | — |
| endAtUtc | string\|null | date-time | False | True | no | — | — | — |
| timeZone | string | — | True | False | no | maxLength=64 | — | — |
| status | string | — | False | False | no | enum=["scheduled", "cancelled"] | — | — |
| userAttendees | array | — | False | False | no | maxItems=500 | — | — |
| contactAttendees | array | — | False | False | no | maxItems=500 | — | — |

- All-day и timed modes переключают видимость совместимых fields, но не отправляют несовместимую комбинацию.
- Attendees заменяются typed `AttendeesReplace`.
- Overlap разрешён и показывается warning.
- Drag/resize выполняет versioned `CalendarEventPatch` или `TaskPatch`; visual rollback обязателен при 412.

## 6. Recurrence editor

| Field | Type | Format | Required | Nullable | ReadOnly | Enum/Limits | Default | PATCH semantics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| status | string | — | False | False | no | enum=["active", "paused", "completed", "cancelled"] | — | — |
| frequency | string | — | True | False | no | enum=["daily", "weekly", "monthly", "yearly"] | — | — |
| interval | integer | int32 | True | False | no | min=1; max=999; minimum=1; maximum=999 | — | — |
| weekdays | array | — | False | False | no | maxItems=7; uniqueItems=true | — | — |
| monthDays | array | — | False | False | no | maxItems=62; uniqueItems=true | — | — |
| monthOfYear | integer\|null | int32 | False | True | no | min=1; max=12; minimum=1; maximum=12 | — | — |
| occurrenceStartDate | string | date | True | False | no | — | — | — |
| localStartTime | string\|null | time | False | True | no | — | — | — |
| timeZone | string | — | True | False | no | minLength=1; maxLength=64 | — | — |
| untilDate | string\|null | date | False | True | no | — | — | — |
| maxOccurrences | integer\|null | int32 | False | True | no | min=1; minimum=1 | — | — |
| nextGenerationDate | string | date | False | False | no | — | — | — |
| template | ref | — | True | False | no | — | — | — |

- `frequency`: daily/weekly/monthly/yearly; `interval`: 1–999.
- Weekdays max 7 unique; monthDays max 62 unique; monthOfYear 1–12.
- End condition: `untilDate` или `maxOccurrences`; несовместимые комбинации сервер отклоняет `RECURRENCE_RULE_INVALID`.
- Timezone обязателен; localStartTime интерпретируется только с timezone.
- Один/current+future/all: `RecurrenceScopedChange.scope`.
- Exclusion одного occurrence: `skip/{occurrenceKey}`; отдельного массива exclusions нет.

## 7. Reminder editor

| Field | Type | Format | Required | Nullable | ReadOnly | Enum/Limits | Default | PATCH semantics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| targetObjectId | string | uuid | True | False | no | — | — | — |
| recipientUserId | string | uuid | True | False | no | — | — | — |
| triggerType | string | — | True | False | no | enum=["absolute", "before_start", "before_deadline", "at_start", "at_deadline"] | — | — |
| offsetMinutes | integer\|null | int32 | False | True | no | min=0; max=525600; minimum=0; maximum=525600 | — | — |
| absoluteTriggerAt | string\|null | date-time | False | True | no | — | — | — |
| nextTriggerAt | string | date-time | False | False | no | — | — | — |
| status | string | — | False | False | no | enum=["scheduled", "due", "delivered", "snoozed", "cancelled", "expired"] | — | — |
| snoozedUntil | string\|null | date-time | False | True | no | — | — | — |
| deliveredAt | string\|null | date-time | False | True | no | — | — | — |

- Trigger types: absolute, before_start, before_deadline, at_start, at_deadline.
- `offsetMinutes`: 0–525600; absolute mode использует `absoluteTriggerAt`.
- Snooze использует `SnoozeRequest.until`; dismiss, reschedule и delete/cancel отдельны.
- Status read-only in normal editor lifecycle: scheduled/due/delivered/snoozed/cancelled/expired.

## 8. FileLocation editor

| Field | Type | Format | Required | Nullable | ReadOnly | Enum/Limits | Default | PATCH semantics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| catalogItemId | string | uuid | False | False | no | — | — | — |
| locationType | string | — | True | False | no | enum=["local_path", "unc_path", "mapped_drive"] | — | — |
| rawPath | string | — | True | False | no | minLength=1; maxLength=4096 | — | — |
| deviceId | string\|null | uuid | False | True | no | — | — | — |
| networkResourceId | string\|null | uuid | False | True | no | — | — | — |
| priority | integer | int32 | False | False | no | min=0; max=32767; minimum=0; maximum=32767 | — | — |
| isEnabled | boolean | — | False | False | no | — | — | — |
| isPrimary | boolean | — | False | False | no | — | — | — |
| deviceAvailability | array | — | False | False | no | maxItems=500 | — | — |

- locationType: local_path/unc_path/mapped_drive.
- Local path требует device binding; UNC/mapped drive согласуются с network resource.
- `rawPath` может быть redacted; UI показывает `displayPath` и не реконструирует секретные segments.
- Alternative path создаёт отдельный FileLocation; priority 0–32767; enabled/primary typed booleans.
- Availability является response/telemetry state, а не ручным полем.
- Security errors: `UNSAFE_PATH`, `UNSAFE_FILE_TYPE`, `FILE_ACCESS_DENIED`, `NETWORK_RESOURCE_UNAVAILABLE`, `FILE_NOT_FOUND`.

## 9. Contact и Company editors

### Contact

| Field | Type | Format | Required | Nullable | ReadOnly | Enum/Limits | Default | PATCH semantics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| firstName | string | — | True | False | no | minLength=1; maxLength=100 | — | — |
| lastName | string\|null | — | False | True | no | maxLength=100 | — | — |
| middleName | string\|null | — | False | True | no | maxLength=100 | — | — |
| displayName | string | — | True | False | no | minLength=1; maxLength=300 | — | — |
| notes | string\|null | — | False | True | no | maxLength=20000 | — | — |
| status | string | — | False | False | no | enum=["active", "inactive"] | — | — |

### Company

| Field | Type | Format | Required | Nullable | ReadOnly | Enum/Limits | Default | PATCH semantics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| name | string | — | True | False | no | minLength=1; maxLength=500 | — | — |
| legalName | string\|null | — | False | True | no | maxLength=500 | — | — |
| industry | string\|null | — | False | True | no | maxLength=200 | — | — |
| website | string\|null | uri | False | True | no | maxLength=2048 | — | — |
| taxIdentifier | string\|null | — | False | True | no | maxLength=100 | — | — |
| notes | string\|null | — | False | True | no | maxLength=20000 | — | — |
| status | string | — | False | False | no | enum=["active", "inactive"] | — | — |

Channels, addresses и contact-company relations используют отдельные DTO и operations. Primary values задаются contract fields, не вычисляются из порядка массива.

## 10. Notification settings

| Field | Type | Format | Required | Nullable | ReadOnly | Enum/Limits | Default | PATCH semantics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| enabled | boolean | — | False | False | no | — | — | omitted=unchanged; explicit null=clear when nullable |
| desktopEnabled | boolean | — | False | False | no | — | — | omitted=unchanged; explicit null=clear when nullable |
| soundEnabled | boolean | — | False | False | no | — | — | omitted=unchanged; explicit null=clear when nullable |
| defaultSnoozeMinutes | integer | int32 | False | False | no | min=1; max=10080; minimum=1; maximum=10080 | — | omitted=unchanged; explicit null=clear when nullable |
| quietHoursStart | string\|null | time | False | True | no | — | — | omitted=unchanged; explicit null=clear when nullable |
| quietHoursEnd | string\|null | time | False | True | no | — | — | omitted=unchanged; explicit null=clear when nullable |
| quietHoursTimeZone | string\|null | — | False | True | no | maxLength=64 | — | omitted=unchanged; explicit null=clear when nullable |

- Единственный source of truth: GET/PUT `/api/v1/notifications/preferences`.
- Поддержанный delivery channel: desktop (`desktopEnabled`). Email/SMS/push channels не показываются.
- DND: quietHoursStart, quietHoursEnd, quietHoursTimeZone.
- Default snooze: 1–10080 minutes.
- `notificationType` приходит в response, но отсутствует в PATCH: selector read-only/contextual, per-type editing не выдумывается.

## 11. User и Administration editors

- User create/edit: `UserCreate`/`UserPatch`; login 3–100, email max 320, status typed.
- Activation/block/deactivation/reactivation — отдельные audited commands.
- Roles заменяются `UserRolesReplace`; permissions — canonical codes only.
- Department hierarchy uses `DepartmentCreate/Patch`; cycle errors map to inline parent selector.
- Device system fields, lastSeenAt and version read-only; secrets write-only.
- Sessions can only be inspected/revoked.
- Network resource UNC root: 3–4096; status typed; probe separate.
- Organization/User settings each have their own endpoint and ETag source.

## 12. Search

| Parameter | Type | Format | Required | Nullable | ReadOnly | Enum/Limits | Default | PATCH semantics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| contactIds | array | uuid | False | False | False | minItems=1; maxItems=100; uniqueItems=true | — | query omitted=no filter |
| cursor | string | — | False | False | False | minLength=1; maxLength=512 | — | query omitted=no filter |
| departments | array | uuid | False | False | False | maxItems=100 | — | query omitted=no filter |
| from | string | date-time | False | False | False | — | — | query omitted=no filter |
| hasFiles | boolean | — | False | False | False | — | — | query omitted=no filter |
| lifecycle | array | — | False | False | False | enum=["active", "completed"]; minItems=1; maxItems=2; uniqueItems=true | — | query omitted=no filter |
| limit | integer | int32 | False | False | False | min=1; max=500 | — | query omitted=no filter |
| projectIds | array | uuid | False | False | False | maxItems=100 | — | query omitted=no filter |
| q | string | — | False | False | False | minLength=2; maxLength=200 | — | query omitted=no filter |
| to | string | date-time | False | False | False | — | — | query omitted=no filter |
| types | array | — | False | False | False | enum=["task", "calendar_event", "project", "catalog_item", "file_location", "contact", "company", "interaction", "comment"]; minItems=1; maxItems=9; uniqueItems=true | — | query omitted=no filter |
| userIds | array | uuid | False | False | False | maxItems=100 | — | query omitted=no filter |

Compatibility: filter groups use AND; values within one array use OR. Incompatible types are excluded server-side before pagination; 422 only if no requested type supports the filter. Cursor is bound to all filters, authorization scope and snapshot.

## 13. Источники и связанные артефакты

- `Stage_3_Field_Traceability_Final_3.5.csv` — normative row-level map.
- `Stage_3_Targeted_Audit_3.5.md` — independent targeted verification.
- `Stage_3_Final_Validation_3.5.md` — DoD gate.
- OpenAPI hash: `36C15DFF5ADBA0041FCFD79F5A0D203835DAC5CDD4AD24122BCD92177C13220D`.

## 14. Stage 3.5 delta from 3.4

### Normative change

- Technical baseline: Stage 2.2 → Stage 2.3.1 (`1.2.0-stage2.3`).
- Added operations: GET/PUT notification urgency scale; POST urgency scale reset.
- Added schemas: `NotificationUrgencyScale`, `NotificationUrgencyScalePatch`, `UrgencyScaleInterval`, `UrgencyLevel`, `EmployeeSearchResult`.
- Search adds optional `SearchSuggestion.resultType`, optional/nullable `employee`, and `types=employee`.
- Permissions remain 91; stable errors remain 44.

| Area | 3.4 | 3.5 |
|---|---|---|
| Urgency scale | Gap; no invented control | CMP-001 in SCR-153, FLOW-035, exact organization editor |
| Employee search | Generic search without confirmed type/group | CMP-002 in SCR-133/134, filter in SCR-135, expanded FLOW-019 |
| IDs | Published baseline | Existing IDs preserved; only CMP-001/CMP-002/FLOW-035 added |
| Field traceability | 1040 rows, Stage 2.2 | Targeted 2.3.1 rows added; `unverified=0` |
| OQ | OQ-001/OQ-003 open at UX level | Both Fixed after targeted audit |

No redesign of shell, primary modules, navigation, file model, lifecycle, read-only, generic conflicts or unrelated screens/flows. Stage 4.1.1 remains candidate until separate Stage 4.1.2.
