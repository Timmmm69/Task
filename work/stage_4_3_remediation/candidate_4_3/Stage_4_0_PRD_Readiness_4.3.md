# Stage 4.0. PRD Readiness

**Status:** Historical reconstruction with Stage 4.3 remediation update  
**Reconstructed on:** 2026-07-26  
**Basis:** canonical project sources and the actually generated Stage 4.1 candidate  
**Historical readiness assessment:** 88/100  
**Purpose:** restore the missing Stage 4.0 document without retroactively inventing product or contract decisions and record the current candidate inputs without rewriting history.

## 0. Reconstruction notice

This file was not available as a complete artifact when Stage 4.1 was generated. Its historical section is reconstructed from the final concept, Stage 1 architecture, the then-current Stage 2.2/Stage 3.4 packages, the stated Stage 4.0 summary (88/100, 21 modules, three blockers), and the modular structure actually used by the Stage 4.1 candidate. Stage 2.2 and Stage 3.4 are historical provenance only and MUST NOT be used as active requirements for candidate 4.3. The current technical contract is Stage 2.3.1; the current UX baseline is Stage 3.5.

## 1. Source hierarchy

1. Final product concept: business capabilities, users and MVP boundaries (`architecture_organizer.md`).
2. Stage 1 architecture: Windows/WPF client, local server, server-authoritative writes, read-only cache, file/SMB boundaries and operational constraints (`01_core_domain_and_data.md`).
3. **Current technical baseline:** Stage 2.3.1, OpenAPI `1.2.0-stage2.3`, 244 unique operationId, DTO catalog, 91 permissions, 44 stable errors, concurrency, sync and Search Contract.
4. **Current UX baseline:** Stage 3.5, including Screen Catalog, User Flows, State Matrix, Role Interface Matrix, UX/API traceability and `Stage_3_Field_Traceability_Final_3.5.csv` (1078 rows).
5. Stage 4.0/4.1 decisions: modular decomposition and stable Stage 4 identifiers, only where evidenced by the candidate and its decision log.

Historical Stage 2.2/3.4 references below describe the original readiness moment only. For every active candidate requirement Stage 2.3.1/3.5 controls. A gap is recorded as OQ; PRD may not silently modify a prior-stage contract.

## 2. Readiness conclusion before Stage 4.1

- General PRD and module PRDs could be created from the available canonical sources.
- Historically, field-level work was considered unblocked by the then-current Stage 3.4 package. Candidate 4.3 revalidates fields only against Stage 3.5 and Stage 2.3.1.
- 21 modules were approved for modular PRD decomposition.
- Three gaps were carried into Stage 4.1: notification urgency threshold contract, stable STATE ID completeness, and employee result type in global search.
- Readiness score was 88/100: sufficient to generate a candidate in modules, insufficient to call the result final.

## 3. Approved module catalog

| Module | Name | API FR | Desktop FR | SCR | FLOW |
| --- | --- | --- | --- | --- | --- |
| MOD-001 | Авторизация и сессии | 10 | 1 | SCR-001,SCR-002,SCR-006,SCR-161 | FLOW-001,FLOW-002,FLOW-003 |
| MOD-002 | App shell и навигация | 6 | 3 | SCR-004,SCR-005,SCR-007,SCR-008,SCR-200,SCR-204,SCR-205,SCR-207,SCR-208,SCR-209,SCR-211,SCR-212,SCR-213 | FLOW-002,FLOW-005,FLOW-020 |
| MOD-003 | Сегодня | 1 | 1 | SCR-010,SCR-011 | FLOW-005,FLOW-007,FLOW-008,FLOW-020,FLOW-021 |
| MOD-004 | Входящие | 8 | 1 | SCR-012,SCR-013,SCR-014 | FLOW-034 |
| MOD-005 | Задачи | 15 | 3 | SCR-020,SCR-021,SCR-022,SCR-023,SCR-024,SCR-025,SCR-029,SCR-030,SCR-031,SCR-032,SCR-034 | FLOW-004,FLOW-005,FLOW-006,FLOW-007,FLOW-008,FLOW-025,FLOW-033 |
| MOD-006 | Подзадачи и чек-листы | 11 | 1 | SCR-033 | FLOW-009 |
| MOD-007 | Повторяющиеся задачи | 10 | 1 | SCR-026,SCR-027 | FLOW-010,FLOW-011,FLOW-012 |
| MOD-008 | Напоминания | 9 | 1 | SCR-028 | FLOW-021 |
| MOD-009 | Календарь | 12 | 1 | SCR-040,SCR-041,SCR-042,SCR-043,SCR-044,SCR-045,SCR-046,SCR-047 | FLOW-031,FLOW-032 |
| MOD-010 | Проекты | 14 | 1 | SCR-060,SCR-061,SCR-062,SCR-063,SCR-064,SCR-065,SCR-066,SCR-067,SCR-068,SCR-069,SCR-070,SCR-071,SCR-072 | FLOW-013,FLOW-014,FLOW-035 |
| MOD-011 | Файловый каталог | 16 | 2 | SCR-080,SCR-081,SCR-082,SCR-083,SCR-084,SCR-085,SCR-086,SCR-087,SCR-088,SCR-089,SCR-090,SCR-210 | FLOW-015,FLOW-016,FLOW-017,FLOW-036 |
| MOD-012 | Контакты и компании | 22 | 1 | SCR-110,SCR-111,SCR-112,SCR-113,SCR-114,SCR-115,SCR-118,SCR-119 | FLOW-018 |
| MOD-013 | Комментарии и взаимодействия | 24 | 1 | SCR-035,SCR-067,SCR-116,SCR-117,SCR-202,SCR-203 | FLOW-037 |
| MOD-014 | Глобальный поиск | 2 | 1 | SCR-133,SCR-134,SCR-135,SCR-136 | FLOW-019 |
| MOD-015 | Уведомления | 5 | 1 | SCR-130,SCR-131,SCR-132,SCR-212 | FLOW-020 |
| MOD-016 | Архив | 2 | 1 | SCR-140 | FLOW-026 |
| MOD-017 | Корзина и восстановление | 3 | 1 | SCR-141,SCR-142,SCR-143 | FLOW-027,FLOW-028 |
| MOD-018 | Настройки | 6 | 1 | SCR-150,SCR-151,SCR-152,SCR-153,SCR-154,SCR-155,SCR-156,SCR-157,SCR-158,SCR-159,SCR-161 | FLOW-002,FLOW-003 |
| MOD-019 | Администрирование | 54 | 1 | SCR-170,SCR-171,SCR-172,SCR-173,SCR-174,SCR-175,SCR-176,SCR-177,SCR-178,SCR-179,SCR-180,SCR-181,SCR-182,SCR-183,SCR-184,SCR-185,SCR-186,SCR-187,SCR-188 | FLOW-029,FLOW-030 |
| MOD-020 | Синхронизация, read-only и конфликты | 4 | 3 | SCR-003,SCR-032,SCR-047,SCR-136,SCR-156,SCR-160,SCR-205,SCR-206,SCR-209 | FLOW-022,FLOW-023,FLOW-024,FLOW-025,FLOW-030 |
| MOD-021 | Аудит и история | 7 | 1 | SCR-036,SCR-068,SCR-172,SCR-186,SCR-201 | FLOW-025,FLOW-029,FLOW-030 |

## 4. Identifier rules

- `MOD-XXX`: modules; fixed range `MOD-001…MOD-021`.
- `FR-XXX`: atomic functional requirement. Each normative OpenAPI operation maps to exactly one API-backed FR; desktop behavior may use a separate FR only when it creates no endpoint/field.
- `BR-XXX`: business rule; no UI description masquerading as a rule.
- `AC-XXX`: testable acceptance criterion; critical flows include permission, validation, conflict, outage/read-only and recovery.
- `NFR-XXX`, `DATA-XXX`, `PERM-XXX`, `ERR-XXX`, `SYNC-XXX`, `AN-XXX`, `AUDIT-XXX`, `DEP-XXX`, `RISK-XXX`, `DEC-XXX`, `OQ-XXX`: stable Stage 4 catalogs.
- Existing `SCR`, `FLOW`, `STATE`, `CMP`, `UXR`, operationId, permission and error codes are preserved. IDs are never reused; deprecated requirements retain their ID.

## 5. PRD decomposition

### 5.1 Product-level document

Product summary, MVP goals, out-of-scope, users/roles, functional model, cross-cutting business rules, common UI states, measurable NFRs, blockers and product DoD.

### 5.2 Module document

Each module uses sections A–O: passport, scope, jobs, FR, BR, fields/validation, permissions, states/errors, sync/read-only/conflicts, notifications/audit, AC, NFR, analytics, dependencies/risks and module DoD.

### 5.3 Cross-cutting catalogs

Business rules, acceptance criteria, NFR, analytics/audit, requirement traceability, dependency/risk, decision log and open questions. The package includes a manifest with file hashes and candidate validation.

## 6. Cross-cutting requirements to carry into every applicable module

1. Server is the only source of truth; no offline business command queue.
2. Deny by default; hidden/disabled UI never replaces server authorization.
3. Optimistic locking with If-Match; no silent last-write-wins.
4. Lifecycle `active/archived/trashed/purged` is separate from business status and derived overdue.
5. Archive and Trash have different visibility, editing and recovery semantics.
6. Working files remain outside the database; metadata rights do not bypass Windows/SMB ACL.
7. File deletion/purge of metadata never deletes physical bytes.
8. Realtime is invalidation; durable recovery uses change feed/bootstrap.
9. Scope changes purge sensitive cached projections before rendering.
10. Stable ProblemDetails errors map to a recovery state without raw stack traces.
11. PATCH omission and explicit null follow DTO semantics.
12. Accessibility: keyboard-only operation, visible focus, non-color alternatives, High Contrast and 200% DPI.
13. Analytics, diagnostics and audit are separate; no secrets, free text or sensitive raw paths in usage events.
14. Client/server compatibility and unsupported-client blocking are explicit.

## 7. Pre-4.1 blockers and decisions

| Gap | Evidence at readiness time | Stage 4.0 disposition |
| --- | --- | --- |
| Notification urgency thresholds | Historical gap: the concept required configurable thresholds while Stage 2.2/3.4 lacked writable fields. Stage 2.3.1/3.5 subsequently supplied the contract. | Historical only; active candidate uses Stage 2.3.1 + Stage 3.5. |
| Stable STATE IDs | Historical gap: the Stage 3.0 registry and Stage 3.4 selected table did not provide full mechanical traceability. | Historical only; active candidate uses the Stage 3.5 registries. |
| Employee global-search result | Concept explicitly includes employees; Search Contract types omit employee/user. | Carry as OQ; do not modify Search API in Stage 4. |

## 8. Decisions evidenced by the generated candidate

- One API-backed FR per each of the 244 Stage 2.3.1 OpenAPI operations.
- Desktop-only FRs do not introduce new operations or fields.
- `Stage_3_Field_Traceability_Final_3.5.csv` remains the normative field source; the PRD selects UX-impacting fields rather than copying all DTO fields.
- Candidate status is mandatory until independent audit and subsequent fix stage.
- Product analytics is minimal and privacy-constrained; no external platform is assumed.

## 9. Entry criteria for Stage 4.1

- Source hierarchy fixed: PASS.
- Valid OpenAPI/DTO/permission/error catalogs available: PASS.
- Stage 3.5 screen/flow/field traceability available: PASS (current); Stage 3.4 is historical only.
- 21-module decomposition fixed: PASS.
- Identifier policy fixed: PASS.
- Known gaps explicit: PASS.
- Permissionless actions or fields without DTO allowed: NO.
- Candidate may be produced: YES.
- Final baseline may be declared: NO, pending gaps and independent audit.

## 10. Handoff and self-check

The candidate author must preserve all 21 modules, map all 244 operations, keep FR→AC coverage complete, avoid unknown permissions/errors, retain SCR/FLOW/STATE/CMP traceability, preserve MVP boundaries, and publish a candidate manifest/validation. Any unresolved concept-to-contract mismatch remains an OQ and is not repaired by silently deleting a concept requirement.


## 11. Stage 4.3 remediation readiness update

- Current technical baseline: Stage 2.3.1; 244 operations, 237 schemas, 91 permissions, 44 stable errors.
- Current UX baseline: Stage 3.5; 1078 field/action rows, unverified=0, provisional=0.
- Remediation input: candidate 4.1.2 plus independent audit 4.2; current work product is candidate 4.3.
- `FLOW-035` remains project completion/archive; urgency management is addressable downstream as `FLOW-038` in `Stage_4_3_Reference_Repair_Report.md`.
- Modules: 21 preserved; affected modules: MOD-002, 014, 015, 018, 019, 020, 021.
- Active source references MUST resolve to Stage 2.3.1/3.5. Stage 2.2/3.4 may occur only with an explicit `historical`/`superseded` qualifier and current replacement.
- Accessibility transfer gate covers exact Tab/Shift+Tab, Up/Down, Enter, Esc focus return, active descendant, visible focus, screen-reader group/status/error announcements, High Contrast, non-color status, below-1100-logical-px adaptation and 200% scaling.
- Internal remediation precheck is recorded separately; it is not an independent re-audit and does not establish a final baseline or final design readiness.

Open product/operations questions are tracked in the current Open Questions artifact. Their classification must not be used to conceal a provisional or unverified NFR.
