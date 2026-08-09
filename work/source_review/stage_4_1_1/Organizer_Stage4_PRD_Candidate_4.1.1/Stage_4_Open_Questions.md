# Stage 4. Open Questions

**Версия:** 4.1.1-candidate.1

## 1. Active questions

| OQ | Severity | Area | Source-backed gap | Required closure | Owner | Status |
| --- | --- | --- | --- | --- | --- | --- |
| OQ-001 | High | Notifications | Концепция прямо требует configurable thresholds: §17.3 «Пороговые значения должны настраиваться», §23.2 «цветовые интервалы», §27.1 item 20 «Настраиваемую цветовую шкалу». NotificationPreferences/UserSettings contract не содержит соответствующих writable fields. | Product owner must either retain requirement and approve prior-stage contract correction, or explicitly change the concept. Stage 4.1.1 does not alter OpenAPI. | Product owner + API owner | Open |
| OQ-003 | High | Search | Концепция §20.1 включает сотрудников в search scope, а §20.2 требует отдельную группу результатов «сотрудники». Search Contract types omit employee/user; userIds is only a relation filter. | Product owner must either retain requirement and approve prior-stage contract correction, or explicitly change the concept. Administrative user filtering is not a substitute. | Product owner + API owner | Open |
| OQ-004 | Medium | Profile | Концепция содержит фотографию пользователя, но writable avatar contract отсутствует и Stage 3.4 удалил control. | Подтвердить исключение из MVP или добавить отдельный system asset contract. | Product owner | Open |
| OQ-005 | Medium | Notifications | Windows/user settings могут запрещать toast; приложение не может гарантировать OS display. | Зафиксировать fallback через Notification Center и диагностический статус как приемлемое ограничение. | Product owner + QA | Open |
| OQ-006 | Medium | Files | Metadata permission не гарантирует Windows/SMB access и доступность ресурса. | Подтвердить differentiated diagnostics как критерий готовности вместо гарантии открытия на каждом устройстве. | IT owner + QA | Open |
| OQ-007 | Medium | Retention | Stage 2.2 использует default Trash retention 30 дней и configurable 7–365 как допущение. | Утвердить организационное значение и owner policy до production baseline. | Security/operations owner | Open |
| OQ-008 | Medium | NFR | 99.5%, RPO 15 min и RTO 4 h являются architecture target assumptions, не подтверждённым SLA. | Подтвердить или заменить измеримыми production targets. | Operations owner | Open |
| OQ-009 | Low | Settings | Language setting существует, но первая поставка может быть только русской. | Подтвердить доступные locale, чтобы не показывать недоступные варианты. | Product owner | Open |
| OQ-010 | Low | Analytics | В источниках нет внешней product analytics platform. | Подтвердить хранение AN только в diagnostic structured logs либо исключить usage persistence. | Product owner + security | Open |

## 2. Resolved during 4.1.1

| OQ | Area | Classification | Resolution | Final status |
| --- | --- | --- | --- | --- |
| OQ-002 | Cross-cutting states | Documentation defect | Restored STATE-001…024; retained STATE-025…031; added STATE-032…039 only for unique semantics; updated PRD, module error tables, AC and traceability. | Closed / Fixed |

`OQ-001` and `OQ-003` are not artificial scope extensions: both have direct concept sources. Critical = 0; High = 2, therefore Stage 4.2 audit gate remains closed.
