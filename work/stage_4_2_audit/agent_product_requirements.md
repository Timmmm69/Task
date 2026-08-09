# Этап 4.2 — независимый продуктовый аудит требований

Область: 21 модуль, FR, BR, все AC, 25 NFR, OQ-001 и OQ-003 кандидата PRD 4.1.2.  
Проверенные нормативы: Stage 2.3.1, Stage 3.5, концепция и архитектура из `audit_input`.  
Кандидат не изменялся.

## Итог

**Продуктовый вердикт: FAIL.**

Причина: подтверждены три High-дефекта — противоречивый нормативный статус OQ-001/OQ-003 в общем PRD, несогласованная трассировка обновлённых FR и 211 неисполняемых AC без Given/When/Then.

| Severity | Count |
|---|---:|
| Critical | 0 |
| High | 3 |
| Medium | 5 |
| Low | 0 |
| Observation | 0 |

## Независимые метрики

| Метрика | Результат |
|---|---:|
| Модули | 21 |
| Уникальные FR в traceability | 279 |
| Уникальные BR в BR catalog | 113 |
| Уникальные AC | 1824 |
| Уникальные NFR | 25 |
| Дубли ID в CSV-каталогах | 0 |
| FR с пустым полем AC в traceability | 0 |
| AC с неизвестным target requirement | 0 |
| AC, непосредственно направленные на FR | 1358 |
| AC, направленные на BR | 112 |
| AC, направленные на DATA | 354 |
| AC с пустым Gherkin | 211: 96 для BR и 115 для FR |
| Traceability requirements с пустым AC | 87: DATA 3, PERM 21, ERR 21, SYNC 21, AUDIT 21 |
| BR с пустым `Related FR` | 96 из 113 |
| Неизвестные stable error codes в FR | 0 |
| Неизвестные permission codes в FR | 0; фраза `User.Block only for blocked visibility` не является новым кодом |
| Неизвестные SCR/CMP | 0 |
| FLOW | FLOW-035 сохранён для проекта; FLOW-038 локально определён DEC-060 для urgency flow; остаточной неверной urgency-ссылки на FLOW-035 не найдено |
| Ссылки на отсутствующее имя `Stage_3_Field_Traceability.csv` | 1565 записей: 260 traceability + 1305 AC |
| Unverified / provisional | минимум 1 / 1: NFR-024 явно требует будущего подтверждения и OQ-008 остаётся Open |
| OQ-001 | **Conflicted, не может считаться однозначно Fixed** |
| OQ-003 | **Conflicted, не может считаться однозначно Fixed** |

Структура каждого из 21 модулей содержит разделы A–O: паспорт, scope, jobs, FR, BR, fields/validation, permissions, states/errors, sync/conflicts, audit, AC, NFR, analytics, dependencies/risks и DoD. Структурная полнота модульных шаблонов подтверждена; дефекты ниже относятся к содержанию и трассировке.

## Findings

### AUDIT-4.2-PR-001

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-PR-001 |
| Severity | High |
| Category | Traceability / Other |
| Artifact | `Stage_4_Product_PRD_4.1.2.md` |
| Location | строка 186, §9; строка 229, §14.4 |
| Related IDs | OQ-001; OQ-003; FR-159; FR-160; FR-264; FR-270–279; BR-098–112; AC-1790–1824 |
| Source of truth | `stage_2_3_1/stage_2_3/openapi/openapi.yaml`: operationId search на строке 16860, suggestions на 17161, urgency GET/PUT/reset на 23083/23117/23202, schemas на 31819/31864; `stage_3_5/Stage_3_Contract_Delta_3.5.md`, строки 256–265; `stage_3_5/Stage_3_User_Flows_Final_3.5.md`, строки 521–552 |
| Expected | Общий PRD должен иметь один актуальный статус OQ. После появления нормативного контракта и UX baseline старое описание gap должно быть явно superseded/historical. |
| Actual | Строка 186 утверждает, что OQ-001/OQ-003 остаются High, writable/search contract отсутствует и аудит 4.2 запускать нельзя. Строка 229 того же файла утверждает `Fixed`. `Stage_4_Open_Questions_4.1.2.md`, строки 21–22, также говорит `Fixed`. |
| Defect | В одном нормативном PRD одновременно активны взаимоисключающие утверждения о наличии контракта, статусе OQ и разрешении аудита. §14 назван обновлением, но §9 не помечен superseded и не содержит перехода к §14. |
| Consequence | Утверждающий, дизайнер, разработчик и QA получают разные нормативные ответы; OQ нельзя доказуемо закрыть. По заданной шкале это повторно открывает OQ-001/OQ-003. |
| Recommended fix | Переписать §9 по состоянию 4.1.2 либо явно оформить старый текст как historical/superseded с единственной нормативной ссылкой на §14.4 и Open Questions. |
| Verification | Поиск по всем кандидатным артефактам должен давать только один актуальный статус каждого OQ; никакой активный текст не должен утверждать отсутствие уже существующих Stage 2.3.1 operations/DTO. |
| Confidence | High |
| Status | Open |

Повторная проверка: заново открыты Product PRD, Open Questions, Decision Log, OpenAPI и Stage 3.5 delta/flows. Связанные разделы подтверждают наличие нового контракта, но не отменяют строку 186.

### AUDIT-4.2-PR-002

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-PR-002 |
| Severity | High |
| Category | FR / AC / Traceability |
| Artifact | `Stage_4_Module_PRDs_4.1.2.md`; `Stage_4_Requirements_Traceability_4.1.2.csv`; `Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv` |
| Location | Module PRD Appendix P.2, строки 6792–6801; FR-159, FR-160, FR-243, FR-244, FR-260, FR-261, FR-265, FR-266, FR-269; traceability rows этих FR; AC-1002–1008, AC-1404, AC-1405, AC-1425, AC-1426, AC-1430, AC-1431, AC-1435 |
| Related IDs | перечисленные 9 FR; OQ-001; OQ-003 |
| Source of truth | Те же Stage 2.3.1 operations/schemas и Stage 3.5 delta/flows, что в PR-001; для OQ coverage также `Stage_4_Open_Questions_4.1.2.md`, строки 21–22 |
| Expected | После нормативного изменения FR его traceability и AC должны проверять новую формулировку, а не прежнее поведение. |
| Actual | Appendix P.2 заменяет смысл 10 FR. Только FR-264 имеет AC, соответствующие новой urgency-editor формулировке. Для остальных 9 trace/AC сохраняют старые сценарии. Примеры: FR-243 теперь про озвучиваемую employee group, но связан только с AC-1404 про восстановление безопасного маршрута; FR-244 теперь про employee deep link, но AC-1405 проверяет keyboard route глобальных команд; FR-261 теперь про presentation по organizational scale, но AC-1426 проверяет текстовую альтернативу Windows toast; FR-269 теперь про `notification_urgency_scale.changed`, но AC-1435 проверяет общую пользовательскую историю. |
| Defect | Формально FR имеют AC, но эти AC не верифицируют действующую формулировку; значение `FR without AC=0` скрывает семантическое отсутствие coverage. |
| Consequence | Реализация может пройти заявленный набор AC, не реализовав обновлённые FR. Для частей OQ-001/OQ-003 отсутствует надёжная requirement-level verification. |
| Recommended fix | Обновить canonical FR rows, trace fields и AC mapping для всех 10 changed FR; для 9 перечисленных добавить/перепривязать конкретные Gherkin AC. Не оставлять старую и новую формулировку в разных нормативных слоях без единой консолидированной строки. |
| Verification | Для каждого changed FR сопоставить действующий текст с каждым связанным AC; каждый обязательный outcome новой формулировки должен встречаться в Then/And. Legacy AC не считается coverage новой семантики. |
| Confidence | High |
| Status | Open |

Повторная проверка: проверены Appendix P.2, соответствующие исходные строки модулей, traceability, все перечисленные AC и новые AC-1790–1824. Новые FR-270–279 частично дублируют поведение, но не исправляют неверную прямую трассировку изменённых FR.

### AUDIT-4.2-PR-003

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-PR-003 |
| Severity | High |
| Category | AC |
| Artifact | `Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv`; связанные разделы K в `Stage_4_Module_PRDs_4.1.2.md` |
| Location | колонка `Gherkin`: 211 пустых значений; AC-001–069, AC-071–097 и 115 FR-oriented AC, включая AC-113, AC-128, AC-130, AC-1403–1435 с отдельными пропусками в диапазоне |
| Related IDs | 96 BR и 115 FR |
| Source of truth | Связанные FR/BR; OpenAPI и Stage 3.5 UX flows; Product PRD §10 требует critical happy/validation/permission/conflict/read-only/recovery coverage |
| Expected | Каждый AC должен иметь Given, When и однозначный Then, позволяющий выполнить независимый тест. |
| Actual | 211/1824 AC имеют только короткий заголовок Scenario и пустой Gherkin. 96 из них формулируются как «Проверить правило…»; 115 включают happy/desktop cases. В Module PRD Gherkin приведён лишь для выборочных «критических» AC и не закрывает весь пустой набор. |
| Defect | Эти AC повторяют название правила/функции, но не задают предусловия, действие, точный observable result, permission/error/state и границы. |
| Consequence | QA не может получить однозначные тесты; положительный путь 115 FR и правила 96 BR могут считаться пройденными при несовместимых реализациях. |
| Recommended fix | Заполнить Gherkin всех 211 AC; разделить независимые outcomes; указать роль/capability, preconditions, точный response/UI state, stable error и boundary values там, где применимо. |
| Verification | Машинная проверка: 1824/1824 непустых Gherkin, каждый содержит строки `Given`, `When`, `Then`; ручная проверка Then на наблюдаемость и отсутствие повторения FR без конкретизации. |
| Confidence | High |
| Status | Open |

Повторная проверка: выполнен второй Import-Csv-пересчёт; открыты модульные разделы K и блоки «Критические Gherkin-сценарии». Компенсирующего полного Gherkin-набора нет.

### AUDIT-4.2-PR-004

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-PR-004 |
| Severity | Medium |
| Category | Traceability / AC |
| Artifact | `Stage_4_Requirements_Traceability_4.1.2.csv` |
| Location | пустая колонка AC у DATA-002, DATA-003, DATA-016; PERM-001–021; ERR-001–021; SYNC-001–021; AUDIT-001–021 |
| Related IDs | 87 requirement IDs |
| Source of truth | OpenAPI, permissions.csv, errors.csv, architecture server-authoritative/audit/sync principles и Stage 3.5 state matrix |
| Expected | Каждая строка, объявленная requirement, должна иметь явную verification relation к одному или нескольким AC. |
| Actual | 87 requirements не имеют ни одного AC в traceability. В модульных разделах есть общий prose, но нет точного requirement→AC mapping. |
| Defect | Cross-cutting permissions, errors, sync и audit фактически orphaned от verification ledger. |
| Consequence | Невозможно доказать completeness по всем 21 модулям; permission/error/offline/audit regression может не попадать в DoD. |
| Recommended fix | Добавить явные AC links для каждой строки либо прекратить представлять эти строки как отдельные requirements и трассировать их к конкретным FR/BR/NFR. |
| Verification | Пустых AC у requirement rows = 0; каждый link разрешается в существующий AC и действительно проверяет соответствующую cross-cutting семантику. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-PR-005

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-PR-005 |
| Severity | Medium |
| Category | BR / Traceability |
| Artifact | `Stage_4_Business_Rules_Catalog_4.1.2.csv`; модульные разделы E |
| Location | колонка `Related FR`: пусто у 96 из 113 BR (BR-001–097, кроме deprecated/updated связей; только новые BR содержат систематические FR links) |
| Related IDs | 96 BR |
| Source of truth | Концепция, Stage 2.3.1 и Stage 3.5 для соответствующих модулей |
| Expected | BR должен иметь scope и явные ссылки на FR, к которым правило применяется. |
| Actual | Module и Verification заполнены, но `Related FR` пуст у 96 BR; модульная таблица E также не содержит FR relation. |
| Defect | Нельзя определить полный impact BR, порядок его применения к операциям и coverage всех затронутых FR. |
| Consequence | Изменение FR может незаметно нарушить BR; AC одного BR не доказывает применение правила ко всем операциям модуля. |
| Recommended fix | Заполнить Related FR точными ID или ввести нормализованную many-to-many таблицу BR↔FR; для глобальных BR перечислить применимость/исключения. |
| Verification | Пустых Related FR = 0, кроме явно обоснованных глобальных rule с формальной областью `ALL`; все ссылки существуют и не цикличны. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-PR-006

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-PR-006 |
| Severity | Medium |
| Category | Traceability |
| Artifact | `Stage_4_Requirements_Traceability_4.1.2.csv`; `Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv` |
| Location | 260 traceability rows и 1305 AC source values содержат `Stage_3_Field_Traceability.csv` |
| Related IDs | 1565 source references |
| Source of truth | фактический файл `stage_3_5/Stage_3_Field_Traceability_Final_3.5.csv` |
| Expected | Source reference разрешается в точный канонический файл текущей версии. |
| Actual | Указанное имя файла отсутствует; канонический файл называется `Stage_3_Field_Traceability_Final_3.5.csv`. |
| Defect | Массовая потерянная ссылка, несмотря на self-validation `lost references=0`. |
| Consequence | Автоматический evidence resolver и ручной аудитор не могут однозначно открыть источник 1565 записей. |
| Recommended fix | Заменить alias на точное имя/версию либо объявить и проверить формальный alias в manifest. |
| Verification | Поиск старого имени = 0; каждый source path существует в audit baseline; link checker PASS. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-PR-007

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-PR-007 |
| Severity | Medium |
| Category | Traceability / Other |
| Artifact | `Stage_4_Requirements_Traceability_4.1.2.csv`; `Stage_4_Business_Rules_Catalog_4.1.2.csv`; `Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv`; `Stage_4_NFR_Catalog_4.1.2.csv` |
| Location | активные Source refs: 20 FR и 81 BR в traceability, 81 BR catalog rows, 108 AC используют Stage 3.4; NFR-012 использует Stage 2.2 |
| Related IDs | FR-242, FR-245–259 с пропусками, FR-262/263/267/268; BR-016–069 и BR-071–097; связанные AC; NFR-012 |
| Source of truth | `Stage_4_Product_PRD_4.1.2.md`, строка 204: Stage 2.3.1 и Stage 3.5 текущие, Stage 2.2/3.4 только historical/backward evidence |
| Expected | Активное требование ссылается на текущий baseline; старая версия маркируется только как историческая. |
| Actual | Активные требования/AC используют старые версии как основной Source. Из подсчёта исключён явно historical BR-070. |
| Defect | Нарушена объявленная иерархия источников. |
| Consequence | При расхождении 3.4/3.5 или 2.2/2.3.1 аудитор не может доказать, какая семантика использована; обновления могут быть потеряны. |
| Recommended fix | Перепривязать активные строки к Stage 3.5/Stage 2.3.1; старые источники оставить только дополнительным historical evidence. |
| Verification | В активных Source fields нет Stage 2.2/3.4; разрешены только строки с явным `Historical`/`Deprecated` и replacement. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-PR-008

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-PR-008 |
| Severity | Medium |
| Category | NFR |
| Artifact | `Stage_4_NFR_Catalog_4.1.2.csv`; `Stage_4_Product_PRD_4.1.2.md`; `Stage_4_Open_Questions_4.1.2.md`; module NFR sections |
| Location | NFR-024; также NFR-001, NFR-003, NFR-006, NFR-007, NFR-015 по измеримости; Open Questions OQ-008 |
| Related IDs | NFR-001; NFR-003; NFR-006; NFR-007; NFR-015; NFR-024; OQ-008 |
| Source of truth | архитектура §0.5, строки 87–100; Open Questions строка 13 |
| Expected | NFR имеет фиксированный measurable pass threshold либо явно помечен provisional/open и не входит в claim `provisional=0`. |
| Actual | NFR-024 прямо называет 99.5%/RPO/RTO архитектурными assumptions, target говорит `Measure and confirm before production baseline`, а OQ-008 остаётся Open/non-blocking. При этом manifest/product DoD/validation заявляют unverified=0 и provisional=0. Дополнительно NFR-003 использует неопределённый `critical WCAG/Windows blocker`, NFR-006 — `stable interaction`, NFR-007 — лишь `no unbounded multi-year query`, NFR-015 — `approved TLS` без минимальной версии/policy reference; для них нельзя воспроизвести единый pass/fail без внешнего решения. |
| Defect | Каталог смешивает проверяемые NFR с открытыми assumptions и не имеет достаточных порогов для части требований. |
| Consequence | Readiness 100% и provisional=0 фактически неверны; performance/accessibility/transport acceptance зависит от незафиксированных решений. |
| Recommended fix | Пометить NFR-024 provisional до решения OQ-008; для перечисленных NFR установить точный threshold/policy/version и test fixture, не меняя бизнес-требование. |
| Verification | OQ-008 закрыт решением либо NFR-024 явно исключён из final target; каждый NFR имеет объективный pass/fail и полный measurement protocol. |
| Confidence | High |
| Status | Open |

Повторная проверка: NFR catalog сверен с Product PRD, всеми модульными упоминаниями, Open Questions и архитектурой §0.5. Кандидат многократно говорит, что availability/RPO/RTO требуют закрытия OQ-008, поэтому `provisional=0` не подтверждается.

## Оценка OQ-001 / OQ-003

Содержательно Stage 2.3.1 и Stage 3.5 действительно предоставляют недостающие contract/UX элементы:

- OQ-001: три urgency operations, DTO, ETag/If-Match, permissions, CMP-001 и новый набор FR/BR/AC присутствуют.
- OQ-003: employee search type/result, redaction/blocked/cursor policy, CMP-002 и новый набор FR/BR/AC присутствуют.

Однако статус **Fixed не принимается как однозначный**, пока не устранены PR-001–PR-003: общий PRD прямо оставляет оба OQ High, часть changed FR имеет старую verification trace, а значимая часть AC неисполняема. Рекомендуемый аудит-статус обоих OQ: **Conflicted / closure evidence requires remediation and revalidation**.

## Модульные итоги

| Module | FR | BR | AC | Empty Gherkin | Trace requirements without AC |
|---|---:|---:|---:|---:|---:|
| MOD-001 | 11 | 4 | 55 | 8 | 4 |
| MOD-002 | 9 | 4 | 17 | 13 | 5 |
| MOD-003 | 2 | 4 | 9 | 6 | 5 |
| MOD-004 | 9 | 3 | 74 | 6 | 4 |
| MOD-005 | 18 | 5 | 134 | 11 | 4 |
| MOD-006 | 12 | 4 | 97 | 8 | 4 |
| MOD-007 | 11 | 4 | 85 | 8 | 4 |
| MOD-008 | 10 | 3 | 74 | 7 | 4 |
| MOD-009 | 13 | 4 | 101 | 9 | 4 |
| MOD-010 | 15 | 4 | 120 | 9 | 4 |
| MOD-011 | 18 | 5 | 130 | 11 | 4 |
| MOD-012 | 23 | 3 | 168 | 8 | 4 |
| MOD-013 | 25 | 4 | 160 | 14 | 4 |
| MOD-014 | 7 | 12 | 42 | 6 | 4 |
| MOD-015 | 7 | 5 | 33 | 7 | 4 |
| MOD-016 | 3 | 3 | 13 | 5 | 5 |
| MOD-017 | 4 | 4 | 27 | 6 | 4 |
| MOD-018 | 12 | 9 | 73 | 7 | 4 |
| MOD-019 | 55 | 4 | 302 | 24 | 4 |
| MOD-020 | 7 | 5 | 53 | 11 | 4 |
| MOD-021 | 8 | 4 | 41 | 12 | 4 |
| ALL | — | 16 | 16 | 16 | — |

