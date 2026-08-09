# Реестр исправлений Этапа 2.2

## 1. Critical и High

| ID | Severity | Дефект | Исправление | Проверка | Каскадный риск |
|---|---|---|---|---|---|
| S22-H-001 | High | Stage 3 считал `openapi.yaml` Markdown и блокировал field-level acceptance | Подтверждены пять идентичных машинно-читаемых копий OpenAPI 2.1; происхождение и SHA-256 зафиксированы | YAML parse, OpenAPI validation, ZIP/content hash comparison | Low: Stage 3 требуется targeted correction ложного ограничения |
| S22-H-002 | High | Не было обязательного C# desktop codegen и compilation evidence | Добавлены NSwag C# desktop client и строгая .NET 8 compilation | `Organizer.DesktopSdk.csproj` build: 0 warnings, 0 errors | Low: generated API names могут потребовать adapter в реализации |
| S22-H-003 | High | Не было C# server stub/interface generation evidence | Добавлен abstract ASP.NET Core controller stub и compilation | `Organizer.ServerStubs.csproj` build: 0 warnings, 0 errors | Low |
| S22-H-004 | High | Search не поддерживал `contactIds`, `hasFiles` и однозначный lifecycle | Добавлены server-side filters и удалён свободный `status` | Search contract gate | Medium: server search implementation и индексы должны поддержать новые predicates |
| S22-H-005 | High | Cursor Search не был связан с filters/scope/snapshot | Добавлен `x-cursor-pagination`, stable sort и stable errors | Search cursor gate | Medium: формат cursor и snapshot retention должны реализовать контракт |

## 2. Medium

| ID | Severity | Дефект | Исправление | Проверка | Каскадный риск |
|---|---|---|---|---|---|
| S22-M-001 | Medium | Отсутствовал полный field-level DTO catalog | Сгенерирован `dto_field_catalog.csv` для 232 schemas и 1322 полей | Catalog generation gate | Low |
| S22-M-002 | Medium | Access policy ошибочно моделировалась как permission | `Authenticated` и `Anonymous*` перенесены в `x-access-policy`; `x-permission` содержит только 91 canonical permission | Permission gate | Medium: consumers extensions должны учитывать оба metadata поля |
| S22-M-003 | Medium | Stable error codes не были привязаны к operations | Добавлен `x-error-codes`, проверяемый против `errors.csv` и HTTP responses | Error metadata gate | Low |
| S22-M-004 | Medium | `FileLocation.rawPath` не имел точной redaction semantics | Добавлены nullable/readOnly, `x-redaction`, `redactedFields`; command fields отмечены writeOnly | DTO field gate | Medium: serialization и policy projection должны соблюдать redaction |
| S22-M-005 | Medium | Auth secrets не имели последовательного readOnly/writeOnly | Password, device key и refresh inputs отмечены writeOnly; issued tokens и temporary credential outputs — readOnly | DTO field catalog | Low |
| S22-M-006 | Medium | PATCH semantics были только неявными | Для Patch schemas закреплены omitted/explicit-null/readOnly/minProperties rules | Schema gate | Low |
| S22-M-007 | Medium | Не было method+path diff artifact | Создан `contract_diff_against_traceability.csv` с 241 совпадением и 0 differences | Contract parity gate | Low |
| S22-M-008 | Medium | Manifest не позволял проверить content identity | Создан manifest 2.2 с format identity, source, size и SHA-256 | Package integrity gate | Low |

## 3. Low

| ID | Severity | Дефект | Исправление | Проверка | Каскадный риск |
|---|---|---|---|---|---|
| S22-L-001 | Low | Date/time wire semantics не были повторены на каждом field contract | Добавлены RFC 3339 UTC, calendar-date и local-time descriptions | DTO catalog and OpenAPI parse | Low |
| S22-L-002 | Low | Прежние validation/fix документы могли восприниматься как текущие | `docs/00_README.md` указывает приоритет отчётов 2.2 | Source precedence review | Low |

## 4. Итог

- Открытых Critical: `0`.
- Открытых High: `0`.
- Contract differences: `0`.
- `AUD-001 / GAP-001`: допускается закрыть по выпуску 2.2.
- Field-level delta Этапа 3: разрешён.
