# Stage 4. Open Questions

**Версия:** 4.3-candidate.1

## 1. Active non-blocking questions

| OQ | Severity | Area | Source-backed gap | Required closure | Owner | Status |
| --- | --- | --- | --- | --- | --- | --- |
| OQ-004 | Medium | Profile | Концепция содержит фотографию пользователя, но writable avatar contract отсутствует; employee search DTO также не содержит avatar. | Подтвердить исключение из MVP или добавить отдельный contract в будущем этапе. | Product owner | Open / non-blocking for candidate 4.3 |
| OQ-005 | Medium | Notifications | Windows/user settings могут запрещать toast; приложение не гарантирует OS display. | Подтвердить Notification Center + diagnostics как fallback. | Product owner + QA | Open / non-blocking |
| OQ-006 | Medium | Files | Metadata permission не гарантирует Windows/SMB access. | Подтвердить differentiated diagnostics. | IT owner + QA | Open / non-blocking |
| OQ-007 | Medium | Retention | Organization Trash retention production value не утверждён. | Утвердить operational policy до production baseline. | Security/operations owner | Open / non-blocking |
| OQ-009 | Low | Settings | Language setting существует, но первая поставка может быть только русской. | Подтвердить locales. | Product owner | Open |

## 2. Resolved history

| OQ | Original gap | Stage 2.3.1 resolution | Stage 3.5 resolution | Updated PRD IDs | Verification | Final status |
| --- | --- | --- | --- | --- | --- | --- |
| OQ-001 | Concept required configurable urgency thresholds; Stage 2.2 had no writable contract. | GET/PUT/reset organizational scale; NotificationUrgencyScale/Patch/UrgencyScaleInterval/UrgencyLevel; organization owner; no personal override; Settings.ReadOwn/System.Configure; ETag/If-Match; Idempotency-Key; audit. | SCR-153/CMP-001; candidate normalizes the duplicate new flow reference to FLOW-038 while preserving historical project FLOW-035; states 007/014/025; accessibility and non-color semantics. | FR-261, FR-264–266, FR-269–274, FR-279; BR-098–104; AC-1426,1429–1431,1435,1790–1803,1819,1821–1824; DEC-055–057,060–061 | Primary and addendum FR/AC semantics aligned; 3 operation mapping; exact DTO fields/defaults; permission/error/state/accessibility/backward/audit checks | **Fixed in candidate 4.3; independent confirmation pending Stage 4.4** |
| OQ-003 | Concept required employee search/group; Stage 2.2 omitted employee type. | `types` includes employee with 1–10 unique values; resultType/employee; EmployeeSearchResult; server authorization/redaction/blocked/ranking/filtering before cursor; cursor binds visibility policy. | SCR-133/134/135, CMP-002, FLOW-019, STATE-030; distinct accessible group; deep-link recheck; no avatar; no client post-filter. | FR-159,160,243,244,260,275–278; BR-070 deprecated → BR-105; BR-105–112; AC-070,1002,1006,1404,1405,1425,1693,1804–1820; DEC-058–059,062 | Legacy enum/maxItems and AC-070 removed; primary/addendum FR/AC aligned; DTO/permission/redaction/cursor/deep-link/partial-failure/accessibility gates | **Fixed in candidate 4.3; independent confirmation pending Stage 4.4** |
| OQ-002 | Stable STATE documentation gap. | No contract change required. | Stable IDs retained. | Existing STATE traceability | Duplicate/missing STATE = 0 | Fixed in 4.1.1 |
| OQ-008 | Architecture included availability/RPO/RTO assumptions without a contractual owner or approval. | No technical-contract change: honest outage/read-only/recovery behavior and backup/restore mechanisms remain normative. | Existing outage, read-only, conflict and recovery flows remain; no numeric SLA is introduced. | NFR-024; DEC-064 | Product PRD contains no unapproved numeric SLA; production deployment requires a separate company-approved operational contract and outage/restore evidence. | **Fixed as external deployment-policy gate** |
| OQ-010 | External analytics platform and persistence model were unspecified. | No new API/DTO/permission: minimized product/diagnostic events use server-side structured application logs; security/business audit remains separate. | Existing privacy/accessibility surfaces unchanged. | Analytics/Audit Requirements 4.3 §5; DEC-049, DEC-065; NFR-014 | No external analytics store; retention inherits company-approved Stage 1 §6.10 range 30–90 days; exact configured duration and rotation/expiration test are mandatory before deployment. | **Fixed in candidate 4.3** |

History is retained; OQ-001/OQ-003 were not deleted. Their closure does not change business requirements or expand MVP. “Fixed in candidate” records remediation status only; Stage 4.4 remains the independent confirmation gate.
