# Stage 4.3 — MOD-014 Conflict Analysis

**Статус:** Remediated in candidate 4.3; independent confirmation pending Stage 4.4  
**Дата:** 2026-07-26  
**Audit findings:** AUDIT-4.2-001, AUDIT-4.2-002, AUDIT-4.2-003  
**Область:** MOD-014 Global Search и связанные MOD-002, MOD-015, MOD-018, MOD-019, MOD-020, MOD-021

## 1. Нормативное основание

Приоритет применён без изменения источников:

1. Концепция §§20.1–20.2 требует поиск сотрудников и отдельную группу результатов.
2. Stage 2.3.1 `Stage_2_3_Contract_Alignment.md` и `openapi/openapi.yaml` задают `types=employee`, `SearchSuggestion.resultType=employee`, `SearchSuggestion.employee` и `EmployeeSearchResult`.
3. Stage 2.3.1 задаёт server-side authorization, redaction, blocked-user filtering, ranking и filtering до cursor pagination; `userIds` остаётся related-object filter.
4. Stage 3.5 `SCR-133/134/135`, `CMP-002`, `FLOW-019`, `STATE-030` задаёт отдельную доступную группу «Сотрудники», safe deep link, partial/redacted states и запрет client post-filter.
5. Stage 3.5 `Stage_3_Targeted_Audit_3.5.md` прямо подтверждает изменение `types` с maxItems 9 на 10 и добавление `employee`.

## 2. Реестр конфликтов и решений

| № | Конфликтующее утверждение candidate 4.1.2 | Файл / ID | Ожидаемое нормативное поведение | Фактическое поведение до remediation | Выбранное решение 4.3 | Почему альтернатива неверна | Затронутые артефакты |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C-01 | OQ-003 одновременно Fixed и активный High/blocker | Product PRD §9/§14.4; Open Questions OQ-003; risk/dependency evidence аудита | Один статус: contract gap закрыт Stage 2.3.1/3.5; независимое подтверждение только на Stage 4.4 | Product PRD объявлял отсутствие employee contract, а Open Questions — Fixed | Удалена active-High формулировка; OQ-003 сохранён в resolved history как Fixed in candidate 4.3, re-audit pending | Оставить Active означало бы отрицать неизменённый нормативный contract; самосертифицировать Final нельзя | Product PRD, Open Questions, Decision Log, dependency/risk remediation |
| C-02 | MOD-014 паспорт/scope не включал employee result/group | Module PRDs MOD-014 A–C | `employee` — distinct result type; отдельная группа «Сотрудники» в employee-only и mixed search | Scope перечислял только generic grouped results | Паспорт, scope и user tasks явно включают employee-only/mixed search и DTO-only card | Admin users, contacts и `userIds` не являются заменой employee search | Module PRDs, Product PRD |
| C-03 | Первичный FR-159 не содержал employee contract, хотя Appendix P.2 переопределял его | Module PRDs FR-159 | GET search принимает `types=employee`, возвращает `resultType/employee`; server filtering/redaction/ranking выполняются до cursor | Primary row оставался generic operation mapping | Primary FR-159 заменён нормативной employee semantics; appendix оставлен только как summary | Additive override не устраняет конфликт первичного требования и создаёт два authoritative текста | Module PRDs, FR/AC catalogs, requirements traceability |
| C-04 | Первичный FR-160 не объяснял permission-safe employee suggestion semantics | Module PRDs FR-160 | Optional `resultType/employee` используется только при наличии; hidden data не восстанавливается | Generic “suggestions by prefix” | Primary FR-160 и AC-1006 синхронизированы с DTO/UX | Клиентская реконструкция employee из иных полей нарушила бы redaction и DTO boundary | Module PRDs, AC catalog |
| C-05 | Query `types` исключал employee и ограничивал maxItems=9 | Module PRDs DATA-014 / AC-1693 | Enum содержит 10 значений, включая `employee`; minItems=1, maxItems=10, uniqueItems=true | Enum из девяти типов, maxItems=9, validation text “не более 9” | Поле и validation text изменены на employee/10 | Сохранение 9 противоречит OpenAPI и Stage 3.5 targeted audit | Module PRDs, field/AC traceability |
| C-06 | BR-070/AC-070 утверждали, что EmployeeProfile не поддержан текущим contract | Module PRDs BR-070, AC-070 | Историческое утверждение сохраняется только как deprecated; replacement — BR-105 | Embedded AC продолжал проверять неподдержку employee | BR-070 маркирован “deprecated since 4.1.2”; AC-070 проверяет replacement BR-105 и поддержку employee | Удалить ID означало бы потерять historical trace; оставить старую semantics — сохранить дефект | Module PRDs, BR/AC catalogs, traceability |
| C-07 | FR-243/244 и AC-1404/1405 сохраняли legacy shell/keyboard semantics, не employee group/deep-link | MOD-002 FR-243/244, AC-1404/1405 | Accessible Employees group; active descendant; Enter deep link; target recheck; neutral unavailable; Esc focus return | Primary rows относились к safe route и generic global commands | Primary FR/AC заменены employee group/deep-link semantics без изменения ID | Appendix-only update оставлял две несовместимые трактовки одних ID | Module PRDs, AC catalog, UX traceability |
| C-08 | Offline employee behavior был только generic cache banner | MOD-014 FR-260, AC-1425 | Cache не объединяется с server pages; client post-filter запрещён; completeness не заявляется | Employee-specific pagination/privacy consequence отсутствовало | Primary FR/AC расширены нормативным cache-only/partial behavior без новой operation | Client merge/post-filter нарушает cursor order и может раскрыть скрытые результаты | Module PRDs, AC catalog |
| C-09 | Связанные urgency-scale FR имели legacy primary AC, хотя Appendix P.2 менял semantics | FR-261, FR-265, FR-266, FR-269 и AC-1426,1430,1431,1435 | Current scale presentation; System.Configure ownership; no offline write; redacted audited change | Primary rows/AC оставались toast/admin/offline/history generalities | Primary rows и embedded AC синхронизированы с Stage 2.3.1/3.5 | MOD-014 нельзя согласовать изолированно: shell/settings/admin/sync/audit образуют один permission-safe flow | Module PRDs, Product PRD, AC catalog, audit requirements |

## 3. Единая модель employee global search

### 3.1. Тип, группа и DTO

- `employee` — самостоятельный query/result type.
- Employee-only и mixed search поддерживаются одним `GET /api/v1/search`.
- UI создаёт отдельную группу «Сотрудники» и сохраняет server order.
- DTO: `EmployeeSearchResult.userId`, `displayName`, nullable `departmentId`, `departmentName`, `jobTitle`, `accountStatus`, `deepLink`, `isRedacted`.
- `displayName` — primary accessible text; `departmentName` и `jobTitle` показываются только при наличии/разрешении.
- `accountStatus` передаётся текстом/иконкой, не только цветом.
- Avatar, email, phone, arbitrary role/title вне `jobTitle` и любые отсутствующие DTO-поля запрещены.

### 3.2. Authorization, partial access и blocked policy

- Основная capability — существующая `Search.Use`.
- Authorization, relation filtering, redaction, ranking, grouping и blocked-user policy выполняются сервером до pagination.
- `User.Block` не даёт доступ к search вообще; он влияет только на разрешённую видимость blocked employee.
- `isRedacted=true` скрывает nullable значения нейтрально; hidden values и counts не отображаются.
- `employee` не смешивается с contact, administrative user list или `userIds` filter.

### 3.3. Cursor, stale result и partial failure

- Cursor связан с normalized filters, authorization scope, index snapshot и employee visibility policy version.
- Filter change сбрасывает cursor.
- `SEARCH_CURSOR_INVALID`/`SEARCH_CURSOR_EXPIRED` удаляет cursor и перезапускает page 1 с теми же filters.
- Client post-filter и объединение cached employee results с server pages запрещены.
- Offline/cache-only и group partial failure обозначаются явно без утверждения полноты.

### 3.4. Deep link и accessibility

- Enter открывает только contract `deepLink`.
- Shell router повторно проверяет доступ к target и загружает authoritative объект.
- Stale/unavailable/forbidden target показывает neutral unavailable state без раскрытия объекта.
- Group heading, active descendant, Up/Down, Enter, Esc focus return, visible focus и screen-reader status/redaction semantics обязательны.

## 4. Verification evidence

Проверено в рабочей копии candidate 4.3:

- primary FR-159/160/243/244/260 согласованы с Appendix P.2 и Stage 2.3.1/3.5;
- primary FR-261/265/266/269 и AC-1426/1430/1431/1435 согласованы с OQ-001 cross-flow;
- query `types` содержит `employee`, maxItems=10, validation text “не более 10”;
- legacy “employee unsupported/current OQ” отсутствует;
- BR-070 сохранён как deprecated и имеет replacement BR-105;
- OQ-003 имеет один resolved status с явным independent re-audit gate;
- существующие ID сохранены; новые API, DTO, permissions и stable errors не созданы;
- активные source references в Module PRDs используют Stage 2.3.1/Stage 3.5; Stage 2.2 остаётся только в явно historical описании BR-070.

## 5. Residual risk

Содержательный конфликт MOD-014 устранён в candidate 4.3. Остаточный процессный риск — повторный независимый аудит 4.4 может выявить несогласованность в других производных артефактах. До его результата candidate не называется Final baseline.
