# Stage 4. Decision Log

**Версия:** 4.1.1-candidate.1

| DEC | Decision | Rationale | Status |
| --- | --- | --- | --- |
| DEC-043 | MOD identifiers are assigned MOD-001…MOD-021 in the order approved by Stage 4.0. | Stable Stage 4 modular traceability. | Accepted |
| DEC-044 | Each normative OpenAPI operation maps to exactly one API-backed FR. | Proves operation coverage without changing contracts. | Accepted |
| DEC-045 | Desktop-only FR may reference existing APIs but cannot introduce a new operation or field. | Separates product behavior from contract change. | Accepted |
| DEC-046 | Stage_3_Field_Traceability.csv is the normative source for UX-relevant fields; module PRD shows selected user/UX fields only. | Avoids copying all 1322 fields. | Accepted |
| DEC-047 | Acceptance catalog contains operation-level scenarios; module text includes Gherkin for critical operations and cross-cutting failures. | Balances testability and document size. | Accepted |
| DEC-048 | Stage 4 candidate remains Candidate until independent audit; open contract gaps are OQ, not invented behavior. | Required by Stage 4 process. | Accepted |
| DEC-049 | Product analytics is limited to surface usage and command outcome metadata; no free text, paths, PII or external platform is assumed. | Privacy and MVP discipline. | Accepted |
| DEC-050 | `STATE-001…024` восстановлены из опубликованного реестра Этапа 3.0; `STATE-025…031` сохранены из 3.4; новые `STATE-032…039` назначены только уникальным security/operations semantics. | Закрывает документационный разрыв без изменения продукта и без переиспользования ID. | Accepted in 4.1.1 |
| DEC-051 | `OQ-001` не отклоняется: концепция прямо требует настраиваемые пороги цветовой срочности. | Удаление требования исказило бы §17.3, §23.2 и §27.1.20. OpenAPI не изменяется. | Accepted |
| DEC-052 | `OQ-003` не отклоняется: концепция прямо включает сотрудников в глобальный поиск и группы результатов. | Административный список пользователей не заменяет §20.1–20.2. Search API не изменяется. | Accepted |

Stage 3 decisions `DEC-001…DEC-042` remain normative and are not renumbered or restated here.
