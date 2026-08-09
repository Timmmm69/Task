# Stage 4. Open Questions

**Версия:** 4.1.2-candidate.1

## 1. Active non-blocking questions

| OQ | Severity | Area | Source-backed gap | Required closure | Owner | Status |
| --- | --- | --- | --- | --- | --- | --- |
| OQ-004 | Medium | Profile | Концепция содержит фотографию пользователя, но writable avatar contract отсутствует; employee search DTO также не содержит avatar. | Подтвердить исключение из MVP или добавить отдельный contract в будущем этапе. | Product owner | Open / non-blocking for 4.1.2 |
| OQ-005 | Medium | Notifications | Windows/user settings могут запрещать toast; приложение не гарантирует OS display. | Подтвердить Notification Center + diagnostics как fallback. | Product owner + QA | Open / non-blocking |
| OQ-006 | Medium | Files | Metadata permission не гарантирует Windows/SMB access. | Подтвердить differentiated diagnostics. | IT owner + QA | Open / non-blocking |
| OQ-007 | Medium | Retention | Organization Trash retention production value не утверждён. | Утвердить operational policy до production baseline. | Security/operations owner | Open / non-blocking |
| OQ-008 | Medium | NFR | Architecture availability/RPO/RTO assumptions не являются SLA. | Утвердить production targets отдельно; PRD не изобретает SLA. | Operations owner | Open / non-blocking |
| OQ-009 | Low | Settings | Language setting существует, но первая поставка может быть только русской. | Подтвердить locales. | Product owner | Open |
| OQ-010 | Low | Analytics | Внешняя product analytics platform не задана. | Подтвердить structured logs или исключить persistence. | Product owner + security | Open |

## 2. Resolved history

| OQ | Original gap | Stage 2.3.1 resolution | Stage 3.5 resolution | Updated PRD IDs | Verification | Final status |
| --- | --- | --- | --- | --- | --- | --- |
| OQ-001 | Concept required configurable urgency thresholds; Stage 2.2 had no writable contract. | GET/PUT/reset organizational scale; NotificationUrgencyScale/Patch/UrgencyScaleInterval/UrgencyLevel; Settings.ReadOwn/System.Configure; ETag/If-Match; audit. | SCR-153/CMP-001, duplicate flow normalized downstream as FLOW-038; states 007/014/025; exact 38-row delta evidence. | FR-264, FR-270–274, FR-279; BR-098–104; AC-1790–1803,1819,1821–1824 | 3 operation mapping; DTO field gate; permission/error/state/AC/accessibility/backward tests | **Fixed** |
| OQ-003 | Concept required employee search/group; Stage 2.2 omitted employee type. | types=employee; resultType/employee; EmployeeSearchResult; server redaction/blocked/cursor policy. | SCR-133/134/135, CMP-002, FLOW-019, STATE-030; no avatar; no post-filter. | FR-159,160,260,275–278; BR-070 deprecated → BR-105; BR-105–112; AC-1804–1820 | Search DTO/field, permission/redaction/cursor/deep-link and Gherkin gates | **Fixed** |
| OQ-002 | Stable STATE documentation gap. | No contract change required. | Stable IDs retained. | Existing STATE traceability | Duplicate/missing STATE = 0 | Fixed in 4.1.1 |

History is retained; OQ-001/OQ-003 were not deleted. Their closure does not change business requirements or expand MVP.
