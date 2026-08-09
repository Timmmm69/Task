# Этап 4.2 — независимый аудит UX, accessibility, analytics/audit, dependencies/risks и design readiness

**Объект:** PRD Candidate 4.1.2  
**Аудиторская область:** Stage 3.5 ↔ PRD; SCR-133/134/135/153; FLOW-019/035/038; CMP-001/002; STATE; accessibility; analytics/diagnostics/security audit; dependencies/risks; готовность к дизайну и разработке.  
**Режим:** read-only по `candidate`, `stage_2_3_1`, `stage_3_5`. Самопроверкам и PASS кандидата доверие не предоставлялось.

## 1. Вывод

**Вердикт по данной области: FAIL.**

Нормативные Stage 2.3.1 и Stage 3.5 содержательно закрывают OQ-001 и OQ-003, однако кандидат 4.1.2 одновременно утверждает их закрытие и сохраняет действующие формулировки о двух High-блокерах. В MOD-014 также остались противоречащие Stage 2.3.1 enum/AC, способные привести к реализации поиска без `employee`. Кроме того, `FLOW-038` используется кандидатом как downstream alias, но отсутствует как определение в нормативном Stage 3.5, где по-прежнему опубликованы два разных `FLOW-035`.

Итог по findings этой области:

| Critical | High | Medium | Low | Observation |
|---:|---:|---:|---:|---:|
| 0 | 2 | 4 | 1 | 0 |

Статусы OQ по результату независимой проверки:

| OQ | Техническое/UX-закрытие в источниках | Согласованность PRD 4.1.2 | Аудиторский статус |
|---|---|---|---|
| OQ-001 | Stage 2.3.1 GET/PUT/reset и Stage 3.5 SCR-153/CMP-001 подтверждены | Противоречие `Stage_4_Product_PRD` §9/§14 и risk register | **Не может считаться Fixed до устранения High-противоречия** |
| OQ-003 | Stage 2.3.1 `employee`/`EmployeeSearchResult` и Stage 3.5 SCR-133/134/135/CMP-002 подтверждены | Противоречащие enum и AC остались внутри MOD-014 | **Не может считаться Fixed до устранения High-противоречия** |

## 2. Методика и пересчёт заявлений Stage 3.5

### 2.1. Field traceability и controls

Файл `stage_3_5/Stage_3_Field_Traceability_Final_3.5.csv` прочитан как CSV, без доверия manifest:

- фактических data rows: **1078**;
- последние contract-delta rows: **38**;
- разложение 38 строк:
  - CMP-001 / urgency scale: **28** (`9 GET + 9 PUT + 9 reset-result + 1 reset command`);
  - CMP-002 / employee search: **10**;
- точных уникальных строк в колонке `Control`: **29**;
- после семантической нормализации одинакового control между GET/PUT/reset-result: **20 controls**:
  - CMP-001: 10 — scope, intervals editor, semantic level, min, max, display token, version, updatedAt, updatedBy, reset;
  - CMP-002: 10 — result type, employee payload/card, userId, displayName, departmentId, departmentName, jobTitle, accountStatus, deepLink, isRedacted.

Следовательно, заявления **38 новых строк**, **1078 всего** и **20 contract-dependent controls** воспроизводятся. Число 20 корректно только как число семантических controls, а не как `COUNT(DISTINCT Control)` по буквальному CSV.

### 2.2. Целевые экраны, компоненты, состояния

| Область | Stage 3.5 evidence | PRD 4.1.2 evidence | Результат |
|---|---|---|---|
| SCR-153 / CMP-001 | Screen Catalog 3.5 строки 213, 218–221; User Flows строки 537–561 | Module PRD строки 6809–6813, 6843–6856; AC-1790…1802 | Содержательно покрыто: fields, permission, validation, read-only, conflict, reset, non-color |
| SCR-133/134/135 / CMP-002 | Screen Catalog строки 214–221; FLOW-019 строки 468–535 | Module PRD строки 6814–6817, 6858–6875; AC-1804…1820 | Addendum покрывает DTO, group, redaction, cursor, deep link; основная секция MOD-014 противоречит addendum — finding 802 |
| STATE | State Matrix строки 101–108 и 119–129 | AC-1792/1798/1799/1800/1808/1814/1815/1817/1821 | Validation, conflict, precondition, read-only, stale, unavailable, partial/redaction покрыты |
| FLOW-035/038 | User Flows содержит два заголовка FLOW-035 на строках 537 и 879; FLOW-038 отсутствует | DEC-060 и PRD addendum назначают urgency alias FLOW-038 | Семантика понятна, ID traceability не нормализована в UX baseline — finding 803 |

### 2.3. Accessibility

Подтверждено:

- keyboard-only и deterministic/visible focus заданы NFR-002/003;
- urgency level/bounds и employee group/status/redaction должны иметь accessible names/states;
- AC-1792 задаёт focus first invalid и сохранение draft;
- AC-1802 проверяет High Contrast, non-color и screen-reader announcement;
- AC-1805 задаёт озвучиваемую группу «Сотрудники»;
- AC-1807 задаёт Enter/deep link;
- AC-1815 задаёт возврат focus после unavailable target;
- read-only и conflict поведение тестируются AC-1797/1798/1800.

Не перенесены в атомарные PRD/AC проверки точные Stage 3.5 semantics `active descendant`, `Up/Down`, обычный `Esc focus return`, Tab/Shift+Tab для CMP-001 и adaptive window behavior ниже ~1100 logical px. Общие NFR/DoD не позволяют однозначно проверить каждое из этих действий — finding 806.

### 2.4. Analytics, diagnostics, audit, privacy

Положительно подтверждено:

- product analytics, operational diagnostics и security audit разделены (`Stage_4_Analytics_Audit_Requirements_4.1.2.md`, строки 6–13, 60–92);
- AN-043…052 имеют trigger, allowlisted properties и purpose;
- employee telemetry не содержит query, userId, displayName, department/title/status/deepLink;
- urgency telemetry не содержит interval values, displayToken, ETag/draft;
- PII, raw file paths, secrets и notification content запрещены;
- audit изменения шкалы создаётся только после commit и содержит redacted diff;
- append-only audit закреплён NFR-021.

Остаточный gap: retention product/diagnostic events оставлен в OQ-010, при этом временно разрешено хранение как structured logs — finding 807.

### 2.5. Dependencies и risks

DEP-022…024 отражают новые зависимости OQ-001/OQ-003. RISK-022…025 отражают interval integrity, search post-filter/cursor, scale conflict и FLOW collision. Однако весь risk register не содержит probability, trigger и owner; additions даже не имеют отдельного impact — finding 805.

## 3. Findings

### AUDIT-4.2-801

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-801 |
| Severity | **High** |
| Category | Other |
| Artifact | `candidate/Organizer_Stage4_PRD_Candidate_4.1.2/Stage_4_Product_PRD_4.1.2.md`; `Stage_4_Dependency_Risk_Register_4.1.2.md` |
| Location | Product PRD строка 186 и строки 208–229; Risk Register строка 63; Open Questions строки 19–23 |
| Related IDs | OQ-001, OQ-003, FR-270…279, BR-098…112, AC-1790…1824, SCR-153, SCR-133/134/135, CMP-001/002 |
| Source of truth | Stage 2.3.1 `openapi/openapi.yaml`: строки 15, 16895, 23079–23198, 28500–28506, 31862–31909; Stage 3.5 Screen Catalog строки 209–223; candidate Decision Log DEC-053…059; candidate Open Questions resolved history |
| Expected | После подтверждённого contract + UX закрытия OQ-001/OQ-003 текущий PRD и risk register должны однозначно обозначать их Fixed; историческая формулировка допускается только с явной маркировкой superseded/history. |
| Actual | Product PRD §9 утверждает, что OQ-001 и OQ-003 «остаются High», API/DTO не изменялись и аудит 4.2 не запускается; §14 того же файла утверждает Fixed. Risk Register повторяет, что оба High и блокируют аудит. |
| Defect | Кандидат одновременно открывает и закрывает два product-blocking OQ. Это не историческая запись: §9 называется «MVP boundaries и открытые блокеры», а risk statement размещён в текущем cross-cutting разделе. |
| Consequence | Разные команды получат взаимоисключающие указания: либо не реализовывать новые contract-backed возможности, либо реализовать их; gate 4.2 и итоговый статус нельзя определить однозначно. По заданной шкале повторное открытие OQ-001/OQ-003 является High. |
| Recommended fix | Переписать Product PRD §9 и Risk Register §3 как явную superseded history либо удалить только текущую blocking-семантику; оставить единственный статус Fixed со ссылками на Stage 2.3.1/3.5 и DEC-053…059. Исторические причины сохранить в resolved history. |
| Verification | Поиск текущих утверждений `OQ-001/OQ-003 remain High`, `block Stage 4.2`, `writable contract absent`, `Search Contract does not contain employee` даёт 0 вне явно маркированного historical block; все текущие status fields дают Fixed. |
| Confidence | High |
| Status | Open |

**Защита от ложного finding:** повторно открыты Product PRD, Risk Register, Open Questions, Decision Log, OpenAPI и Stage 3.5 target screens. Техническое закрытие существует; дефект именно во внутренней нормативной семантике PRD.

### AUDIT-4.2-802

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-802 |
| Severity | **High** |
| Category | UX |
| Artifact | `candidate/Organizer_Stage4_PRD_Candidate_4.1.2/Stage_4_Module_PRDs_4.1.2.md` |
| Location | MOD-014: строки 4425–4428, 4434–4447, 4505–4508; противоречащий addendum строки 6792, 6814–6817, 6862–6873 |
| Related IDs | MOD-014, FR-159, FR-275…278, BR-070, BR-105…112, AC-070, AC-1804…1820, SCR-133/134/135, CMP-002, FLOW-019, OQ-003 |
| Source of truth | Stage 2.3.1 OpenAPI: `types` содержит `employee`, maxItems=10 (строки 16893–16897); `SearchSuggestion.resultType/employee` (28500–28506); `EmployeeSearchResult` (31864–31909). Stage 3.5 Screen Catalog строки 214–221 и FLOW-019 строки 518–535. |
| Expected | Основная таблица fields MOD-014 должна содержать `types=employee`, maxItems=10; AC-070 должен однозначно тестировать только deprecation/replacement BR-070→BR-105, а не отсутствие employee в текущем contract. |
| Actual | Строка 4446 перечисляет enum без `employee`, maxItems=9; строка 4508 требует проверить, что EmployeeProfile как result type «не поддержан текущим контрактом и зафиксирован OQ». Поздний addendum утверждает противоположное. Каталог AC-070 уже переписан, но embedded AC в module PRD остался старым. |
| Defect | Один и тот же module PRD содержит две несовместимые спецификации фильтра и AC. |
| Consequence | Desktop и QA могут реализовать/принять enum без employee либо тест, который требует отсутствия функции; OQ-003 фактически повторно открыт в основном модуле. |
| Recommended fix | Обновить основную секцию MOD-014: enum `types` до 10 с `employee`; заменить embedded AC-070 содержимым канонического AC Catalog (проверка deprecation и replacement); отметить старую Stage 2.2 семантику только historical. |
| Verification | Сопоставить MOD-014 field table с OpenAPI parameter schema; `employee` присутствует, maxItems=10; embedded и CSV AC-070 идентичны по смыслу; отсутствуют утверждения о неподдерживаемом employee в текущем contract. |
| Confidence | High |
| Status | Open |

**Защита от ложного finding:** проверены OpenAPI, Stage 3.5, BR catalog, AC catalog и поздний addendum. Алиас/депрекация BR-070 не устраняет противоречащие поля и embedded AC.

### AUDIT-4.2-803

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-803 |
| Severity | **Medium** |
| Category | Traceability |
| Artifact | Stage 3.5 `Stage_3_User_Flows_Final_3.5.md`; candidate `Stage_4_Decision_Log_4.1.2.md`, `Stage_4_Requirements_Traceability_4.1.2.csv`, `Stage_4_Candidate_Validation_4.1.2.md` |
| Location | Stage 3.5 User Flows строки 537 и 879; DEC-060; candidate validation строка 18; 17 requirement-trace rows с FLOW-038 |
| Related IDs | FLOW-035, FLOW-038, SCR-153, CMP-001, FR-264, FR-266, FR-269…274, FR-279, AC-1824 |
| Source of truth | Stage 3.5 User Flows и Screen Catalog; правило запрета PRD flow без UX flow из задания аудита |
| Expected | Исторический project flow сохраняет FLOW-035; urgency flow имеет уникальное, явно определённое downstream UX-описание FLOW-038 с однозначной ссылкой на исходную ошибочно помеченную секцию; unknown flow count учитывает alias только после проверки определения. |
| Actual | Stage 3.5 содержит два разных заголовка FLOW-035 и **0** вхождений FLOW-038. Кандидат использует FLOW-038 минимум в 17 строках requirements trace и объявляет unknown UX IDs=0 только на основании DEC-060/AC-1824; отдельного полного определения FLOW-038 в candidate UX artifact нет. |
| Defect | ID collision семантически объяснена, но cross-artifact trace target FLOW-038 не существует как полноценный UX flow. Decision alias не заменяет адресуемое flow definition. |
| Consequence | Трассировка автоматическими средствами считается broken/unknown; дизайнер и QA должны вручную догадываться, что FLOW-038 — это первая секция FLOW-035, а не исторический project flow. |
| Recommended fix | Не изменяя Stage 3.5 archive, включить в remediation отдельный normative alias/errata artifact или полный downstream flow definition `FLOW-038`, с `supersedes duplicate urgency label FLOW-035 at Stage 3.5 line 537`; обновить все UX/PRD trace links на этот адресуемый объект. |
| Verification | Реестр flow имеет уникальные IDs; FLOW-035 разрешается только в project completion/archive, FLOW-038 — только в urgency scale; каждая ссылка разрешается в одно определение; duplicate/unknown/broken reference gates = 0. |
| Confidence | High |
| Status | Open |

**Перепроверка severity:** семантика urgency flow полностью опубликована, поэтому это не High-блокировка функции; Medium обусловлен существенным дефектом нормативной traceability перед финализацией.

### AUDIT-4.2-804

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-804 |
| Severity | **Medium** |
| Category | Traceability |
| Artifact | `Stage_4_Requirements_Traceability_4.1.2.csv`; `Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv`; `Stage_4_Business_Rules_Catalog_4.1.2.csv`; `Stage_4_0_PRD_Readiness_4.1.2.md`; `Stage_4_Module_PRDs_4.1.2.md` |
| Location | 102/497 trace rows, 108/1824 AC rows и 81/113 BR rows содержат Stage 3.4 как source; Readiness строки 11, 17–18, 101–117; примеры requirement trace: FR-242…263 |
| Related IDs | FR-242…269, BR-001…097, AC-001…1789; DEC-053, DEC-054 |
| Source of truth | DEC-053: Stage 2.3.1 — текущий technical contract; DEC-054: Stage 3.5 — текущий UX baseline; задание аудита запрещает Stage 2.2/3.4 как current normative version |
| Expected | Current source columns ссылаются на Stage 2.3.1/3.5; Stage 2.2/3.4 допускаются только как явно historical provenance/superseded evidence. |
| Actual | Сотни текущих catalog rows используют `Stage 3.4` без historical qualifier. Readiness artifact в «normative hierarchy» называет Stage 2.2/Stage 3.4 baseline, а ниже объявляет Stage 3.5 current и readiness 100/100. |
| Defect | Иерархия источников внутри candidate package неоднозначна; self-validation claim об отсутствии stale 2.2/3.4 refs не воспроизводится. |
| Consequence | Design/QA может сверять requirement с устаревшими 1040-row UX данными, пропуская 3.5 delta; регенерация traceability не имеет однозначного source baseline. |
| Recommended fix | Механически обновить current source refs на Stage 3.5/2.3.1 после содержательной сверки; исторические ссылки пометить `historical/superseded`; разделить Stage 4.0 reconstruction history и текущую 4.1.2 hierarchy. |
| Verification | Отдельный gate: ни одна current source cell не содержит Stage 2.2/3.4; исключения находятся только в помеченном historical provenance; выборочная перепроверка OQ-001/OQ-003 и desktop FR по Stage 3.5 проходит. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-805

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-805 |
| Severity | **Medium** |
| Category | Other |
| Artifact | `candidate/Organizer_Stage4_PRD_Candidate_4.1.2/Stage_4_Dependency_Risk_Register_4.1.2.md`; повторённые sections N в Module PRDs |
| Location | Risk Register строки 31–55 и 67–78; module risk sections, например MOD-018 строки 5586–5591 |
| Related IDs | DEP-001…024, RISK-001…025, OQ-001, OQ-003 |
| Source of truth | Требования аудита Part 19: probability/impact или equivalent, mitigation, trigger, owner; новые OQ decisions должны быть отражены |
| Expected | Для каждого существенного риска: probability, impact, owner/ответственная роль, trigger/early signal, конкретная mitigation и verification; для dependency — owner/readiness/status. |
| Actual | RISK-001…021 имеют только Impact=`High`, одинаковую generic mitigation и verification; probability, trigger, owner отсутствуют. RISK-022…025 имеют только description и verification/mitigation, без probability/impact/owner/trigger. Module sections повторяют тот же усечённый формат. |
| Defect | Реестр является списком тем, а не управляемым risk register; риск нельзя назначить, отслеживать или закрыть по trigger. |
| Consequence | Риски privacy, privilege escalation, data loss, cursor leakage и concurrency остаются без ответственного и момента эскалации; readiness и DoD неоперациональны. |
| Recommended fix | Расширить schema register: Probability, Impact, Owner role, Trigger/indicator, Preventive mitigation, Contingency, Verification, Status/closure evidence; заполнить все 25 risks и DEP readiness. |
| Verification | CSV/Markdown lint: у каждого active RISK непусты probability, impact, owner, trigger, mitigation, verification, status; targeted review RISK-022…025 подтверждает OQ-001/OQ-003 и FLOW-038 remediation. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-806

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-806 |
| Severity | **Medium** |
| Category | UX |
| Artifact | `Stage_4_NFR_Catalog_4.1.2.csv`; `Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv`; `Stage_4_Module_PRDs_4.1.2.md` |
| Location | NFR-002…005; AC-1805, AC-1807, AC-1815; Module addendum строки 6885–6891 |
| Related IDs | NFR-002, NFR-003, NFR-004, NFR-005, FR-243, CMP-001, CMP-002, SCR-133/134/135/153, FLOW-019 |
| Source of truth | Stage 3.5 UX Architecture строка 952 (`active descendant`, Up/Down, Enter, Esc focus return), User Flows строка 524, Screen Catalog строки 218–221; UX Architecture строки 237–263 (adaptive layout/window/keyboard) |
| Expected | Атомарные проверяемые AC должны охватывать exact key map/focus ownership для CMP-001/002 и adaptive window behavior: Up/Down, active descendant, Enter, Esc with focus return, Tab order, visible focus, below ~1100 layout adaptation, 200% without clipped primary actions. |
| Actual | Кандидат задаёт общие keyboard/focus NFR, озвучиваемую group, Enter и один special-case focus return. Нет AC на active descendant, Up/Down, обычный Esc focus return, CMP-001 Tab order и adaptive layout below ~1100. NFR-004 проверяет 200% DPI/multiple monitors, но не desktop window resizing/collapse behavior. |
| Defect | Подтверждённые Stage 3.5 interactions не доведены до однозначной PRD verification matrix. |
| Consequence | Дизайнер/desktop developer/QA могут реализовать разные keyboard models; возможны focus loss, недоступная employee group и clipped primary controls в узком окне. |
| Recommended fix | Добавить atomic accessibility AC для обоих CMP и adaptive window matrix; сослаться на конкретные SCR/FLOW; определить expected focus before/after, keys, announcement и layout outcome. |
| Verification | Keyboard-only/UIA test выполняет Tab/Shift+Tab, arrows, Enter, Esc и проверяет active descendant/focus return; resize test проходит default, <1100 logical px и minimum window при 200%, без потери status/primary commands. |
| Confidence | High |
| Status | Open |

**Перепроверка severity:** общие NFR и Stage 3.5 semantics существуют, поэтому основной flow реализуем; отсутствие atomic verification существенно для final PRD, но не является High.

### AUDIT-4.2-807

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-807 |
| Severity | **Low** |
| Category | Security |
| Artifact | `candidate/Organizer_Stage4_PRD_Candidate_4.1.2/Stage_4_Analytics_Audit_Requirements_4.1.2.md`; `Stage_4_Open_Questions_4.1.2.md` |
| Location | Analytics строки 90–92; Open Questions OQ-010 |
| Related IDs | OQ-010, AN-001…052, BR-113, AC-1823 |
| Source of truth | Privacy/minimization requirements того же analytics artifact; OQ-010 owner Product + Security |
| Expected | До production утверждены retention, access, rotation/deletion и storage boundary для product/diagnostic events. |
| Actual | Retention не определён; до закрытия OQ-010 events разрешены как structured logs с минимальными properties. Allowlist и запреты PII/query/path/secrets при этом определены хорошо. |
| Defect | Acknowledged governance gap: минимизация payload не ограничивает время хранения и круг доступа. |
| Consequence | Даже неперсональные correlation/operation metadata могут храниться дольше необходимого; production privacy/operations sign-off невозможен. |
| Recommended fix | Закрыть OQ-010 решением Product + Security: storage, access roles, retention/rotation, deletion, sampling и запрет внешней платформы либо её отдельное одобрение. |
| Verification | Config/policy test подтверждает retention/rotation/access; ни один event не выходит за allowlist; AC-1823 и log scan проходят. |
| Confidence | High |
| Status | Open |

## 4. Готовность к дизайну

Методика: 8 взвешенных областей; каждой присваивается 0–100% внутренней полноты по наличию однозначного, source-backed и проверяемого требования. Итог — сумма `вес × полнота`. Self-score кандидата не используется.

| Область | Вес | Полнота | Балл | Основание |
|---|---:|---:|---:|---|
| Screens, components, flows, transitions | 15 | 73% | 11 | Target screens/CMP описаны, но FLOW-038 не имеет адресуемого UX definition |
| Fields, controls, DTO | 15 | 80% | 12 | 38/1078 и 20 controls подтверждены; MOD-014 содержит старый enum |
| States, empty/loading/error/read-only/conflict | 15 | 93% | 14 | Target AC/STATE покрыты; точечные focus/accessibility gaps |
| Roles, permissions, partial access | 10 | 100% | 10 | Settings.ReadOwn/System.Configure/Search.Use/User.Block и server recheck определены |
| Validation, errors, recovery | 15 | 93% | 14 | Boundary, conflict, stale, unavailable, cursor recovery проверяемы |
| Accessibility и adaptive desktop | 15 | 67% | 10 | Базовые NFR сильные; exact key/focus/resize AC отсутствуют |
| UX writing readiness | 5 | 60% | 3 | Семантика сообщений есть, но нет явного writing backlog/copy inventory |
| Source freshness/traceability | 10 | 40% | 4 | Много current Stage 3.4 refs и unresolved FLOW alias |
| **Итого** | **100** |  | **78%** |  |

**Design readiness: 78%.** Визуальный exploration возможен, но утверждать дизайн к production handoff нельзя до закрытия High и Medium findings. Отдельной UX-writing проработки требуют: read-only explanation, interval validation, reset confirmation, compare/reapply/discard, neutral redaction placeholder, partial group failure, stale/unavailable result и cursor restart.

## 5. Готовность к будущей разработке

Методика: 7 дисциплин; тот же принцип `вес × полнота`, оценивалась способность начать реализацию без самостоятельного выбора несовместимой бизнес-семантики.

| Дисциплина | Вес | Полнота | Балл | Основание |
|---|---:|---:|---:|---|
| Backend/API | 15 | 93% | 14 | Stage 2.3.1 target contract однозначен |
| Desktop client | 20 | 70% | 14 | Target behavior богатое, но MOD-014 и flow/accessibility contradictions |
| Database/data | 10 | 90% | 9 | Target DTO/data semantics прослеживаются |
| QA/testability | 20 | 70% | 14 | Сильные AC, но embedded AC conflict и keyboard/resize gaps |
| Security/privacy | 15 | 87% | 13 | Permission/redaction/minimization сильные; retention открыт |
| DevOps/diagnostics/observability | 10 | 70% | 7 | Event model определён; retention/operations policy не закрыта |
| Dependencies/risk management | 10 | 30% | 3 | Нет owner/probability/trigger |
| **Итого** | **100** |  | **74%** |  |

**Development readiness: 74%.** Backend contract достаточно зрелый, но единый implementation baseline отсутствует до устранения двух High findings; desktop/QA/risk governance требуют remediation.

## 6. Рекомендуемый порядок remediation

1. Устранить AUDIT-4.2-801 и 802: единые статусы OQ и единый current employee contract во всех PRD sections/catalogs.
2. Формализовать FLOW-038 как адресуемый downstream UX alias/errata и повторить broken-reference gate.
3. Обновить current source references Stage 3.4 → Stage 3.5 после содержательной сверки.
4. Добавить atomic accessibility/adaptive-window AC.
5. Сделать risk register управляемым: probability, impact, owner, trigger, mitigation, verification/status.
6. Закрыть OQ-010 до production baseline.

