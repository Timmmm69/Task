# Stage 4.3 Reference Repair Report

**Статус:** downstream errata для candidate 4.3  
**Область:** AUDIT-4.2-008, AUDIT-4.2-009 и AUDIT-4.2-010  
**Ограничение:** Stage 2.3.1 и Stage 3.5 не изменяются.

## 1. Нормативные версии

| Area | Current source | Historical only |
| --- | --- | --- |
| Technical contract | Stage 2.3.1; OpenAPI `1.2.0-stage2.3`; 244 operationId; 237 schemas; 91 permissions; 44 stable errors | Stage 2.2 |
| UX baseline | Stage 3.5; `Stage_3_Field_Traceability_Final_3.5.csv`, 1078 rows | Stage 3.4 |

Active requirements and verification records MUST cite the current source. A historical reference is valid only in the form `historical Stage 2.2; superseded by Stage 2.3.1` or `historical Stage 3.4; superseded by Stage 3.5`.

## 2. Field-traceability filename repair

Canonical target: `Stage_3_Field_Traceability_Final_3.5.csv`.

The legacy shorthand formed from basename `Stage_3_Field_Traceability` plus suffix `.csv` is not a packaged Stage 3.5 file and MUST NOT be used as a trace target. All candidate references are repaired to the canonical filename. Historical prose calls it “legacy shorthand” without reproducing it as a link/source.

Verification gate:

1. search active candidate artifacts for the legacy basename immediately followed by `.csv` → zero;
2. resolve every `Stage_3_Field_Traceability_Final_3.5.csv` reference against the Stage 3.5 package → one target;
3. parse the target CSV → 1078 data rows.

## 3. FLOW identifier repair

Stage 3.5 accidentally reuses `FLOW-035`:

- the flow registry and the complete section later in `Stage_3_User_Flows_Final_3.5.md` identify `FLOW-035` as **«Завершение и архивирование проекта»**;
- an inserted section in the same file identifies urgency-scale management with the same ID.

Candidate 4.3 applies this non-destructive downstream mapping:

| Stage 3.5 text | Candidate 4.3 canonical ID | Status |
| --- | --- | --- |
| `FLOW-035. Завершение и архивирование проекта` | `FLOW-035` | Preserved |
| duplicated `FLOW-035. Управление организационной шкалой срочности` | `FLOW-038` | Renamed downstream; full definition below |

This repair does not renumber or alter Stage 3.5. `FLOW-038` is a Stage 4.3 errata ID whose provenance is the duplicated urgency section of Stage 3.5.

## 4. FLOW-038 — Управление организационной шкалой срочности

**Provenance:** Stage 3.5 `Stage_3_User_Flows_Final_3.5.md`, duplicated urgency section; Stage 3.5 `Stage_3_UX_Architecture_Final_3.5.md`, CMP-001/SCR-153; Stage 2.3.1 notification-urgency-scale contract.

**Trigger:** `SCR-153` → «Шкала срочности».  
**Surface/component:** `SCR-153`, `CMP-001`.  
**Owner/scope:** organization; personal override отсутствует.  
**Предусловия:** GET — `Settings.ReadOwn`; PUT/reset — `System.Configure`.  
**API:** GET/PUT `/api/v1/settings/notification-urgency-scale`; POST `/api/v1/settings/notification-urgency-scale/reset`.  
**DTO:** `NotificationUrgencyScale`, `NotificationUrgencyScalePatch`, `UrgencyScaleInterval`, `UrgencyLevel`.  
**Concurrency/audit:** response ETag; PUT/reset require `If-Match` and `Idempotency-Key`; successful commit produces `notification_urgency_scale.changed`.  
**Результат:** server-confirmed organization scale and new ETag; semantic urgency remains unchanged.

### Основной путь

1. GET returns exactly four ordered intervals and the current ETag.
2. UI shows immutable semantic level (`low`, `normal`, `high`, `critical`), inclusive `minScore`/`maxScore`, `displayToken` and a non-color preview.
3. Client validates 0–100 bounds, order, complete coverage, contiguity and no overlap; server remains authoritative.
4. Save sends the complete four-interval array with `If-Match` and a new `Idempotency-Key`.
5. Successful response atomically replaces the draft and ETag; existing and future notification presentation resolves through the current organization scale.

### Reset

1. User with `System.Configure` confirms reset.
2. Client sends POST `/reset` with `If-Match` and a new `Idempotency-Key`.
3. Server restores 0–24, 25–49, 50–74 and 75–100 and returns the new ETag.

### Ошибки и восстановление

- gap/overlap/order/out-of-range → `VALIDATION_FAILED` / `STATE-007`; preserve draft and focus the first invalid field;
- missing `System.Configure` → read-only explanation; server `FORBIDDEN` is authoritative;
- stale ETag → `VERSION_CONFLICT` / `STATE-014`; preserve draft, load current, compare and allow reapply/discard;
- missing `If-Match` → `STATE-025`; GET current, no blind retry;
- server unavailable → cached read-only view and Retry; no offline write queue;
- legacy 2.2 client → built-in presentation mapping; server semantic urgency remains compatible.

### Accessibility

- logical Tab/Shift+Tab order covers all four interval rows and Save/Reset/Cancel;
- every editable field has a stable accessible name containing semantic level and boundary role;
- validation announces gap/overlap/order error and focuses the first invalid field;
- High Contrast is supported; semantic label plus icon/text is mandatory, so meaning never depends on `displayToken` color alone;
- conflict/read-only/success status is announced to assistive technology;
- Esc closes transient UI and returns focus to its invoker; a dirty full editor asks before discard.

## 5. Reference rules and verification

- `FLOW-035` references are valid only for project completion/archive.
- `FLOW-038` references are valid only for organization urgency-scale management.
- `CMP-001` and `SCR-153` urgency references use `FLOW-038`.
- `FLOW-019`, `CMP-002` and `SCR-133/134/135` remain employee-search references and are not aliases of FLOW-038.
- A unique-ID lint is run over the downstream flow registry; both `FLOW-035` and `FLOW-038` resolve exactly once.
- The final candidate validation must report unknown FLOW = 0 and broken target/occurrence = 0.

The repair is ready for independent verification but is not itself an independent re-audit or a modification of the Stage 3.5 baseline.
