# Stage 4. Decision Log

**Версия:** 4.1.2-candidate.1

| DEC | Decision | Rationale | Status |
| --- | --- | --- | --- |
| DEC-043 | MOD identifiers are assigned MOD-001…MOD-021 in the order approved by Stage 4.0. | Stable Stage 4 modular traceability. | Accepted |
| DEC-044 | Each normative OpenAPI operation maps to exactly one API-backed FR. | Proves operation coverage without changing contracts. | Accepted |
| DEC-045 | Desktop-only FR may reference existing APIs but cannot introduce a new operation or field. | Separates product behavior from contract change. | Accepted |
| DEC-046 | Stage_3_Field_Traceability_Final_3.5.csv is the normative source for UX-relevant fields; module PRD shows selected user/UX fields only. | Avoids copying all 1322 fields. | Accepted |
| DEC-047 | Acceptance catalog contains operation-level scenarios; module text includes Gherkin for critical operations and cross-cutting failures. | Balances testability and document size. | Accepted |
| DEC-048 | Stage 4 candidate remains Candidate until independent audit; open contract gaps are OQ, not invented behavior. | Required by Stage 4 process. | Accepted |
| DEC-049 | Product analytics is limited to surface usage and command outcome metadata; no free text, paths, PII or external platform is assumed. | Privacy and MVP discipline. | Accepted |
| DEC-050 | `STATE-001…024` восстановлены из опубликованного реестра Этапа 3.0; `STATE-025…031` сохранены из 3.4; новые `STATE-032…039` назначены только уникальным security/operations semantics. | Закрывает документационный разрыв без изменения продукта и без переиспользования ID. | Accepted in 4.1.1 |
| DEC-051 | `OQ-001` не отклоняется: концепция прямо требует настраиваемые пороги цветовой срочности. | Удаление требования исказило бы §17.3, §23.2 и §27.1.20. OpenAPI не изменяется. | Accepted |
| DEC-052 | `OQ-003` не отклоняется: концепция прямо включает сотрудников в глобальный поиск и группы результатов. | Административный список пользователей не заменяет §20.1–20.2. Search API не изменяется. | Accepted |

Stage 3 decisions `DEC-001…DEC-042` remain normative and are not renumbered or restated here.


## Stage 4.1.2 decisions

| ID | Decision | Basis | Consequence | Status |
| --- | --- | --- | --- | --- |
| DEC-053 | Stage 2.3.1 is the normative technical contract | Final hash/validation and 244-operation catalog | Stage 2.2 is historical/backward only | Accepted |
| DEC-054 | Stage 3.5 is the normative UX baseline | Final baseline hash/validation | Stage 3.4 is historical | Accepted |
| DEC-055 | Urgency scale owner is organization; no personal override | x-owner/x-user-override and CMP-001 | One editor in SCR-153 | Accepted |
| DEC-056 | Semantic urgency is primary; displayToken/color is secondary | UrgencyLevel and accessibility | Text/icon/label required | Accepted |
| DEC-057 | Scale replacement/reset is versioned, atomic and audited | PUT/reset contract | If-Match, Idempotency-Key, redacted audit | Accepted |
| DEC-058 | Employee is a distinct global-search result type/group | types=employee; EmployeeSearchResult | Not admin users, contacts or userIds filter | Accepted |
| DEC-059 | Employee filtering/redaction/blocked policy is server-side before cursor | Search contract | No client post-filter; no hidden counts | Accepted |
| DEC-060 | Preserve historical project FLOW-035; normalize duplicate urgency FLOW-035 to FLOW-038 | Stage 3.5 duplicate-id defect + no-renumber rule | Unique candidate flow references without changing Stage 3.5 source | Accepted |
