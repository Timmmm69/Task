# Implementation Baseline Inputs — Contract-Correction Freeze

Increment: `TASK-PROD-S1-001` (base `main` @ `52fc9f7bde21ead0b5438a2f9d6b7cfd1e80ecbf`, base SHA).
Purpose: freeze exactly two contract corrections — **notification urgency** and **employee search** — as implementation inputs, with their evidence inputs. This registry does not claim that Stage 1 or the full implementation baseline is complete.

## 1. Canonical precedence and artifact classes

Resolution order (AGENTS.md, highest first):

| # | Canonical source | Role |
|---|---|---|
| 1 | `sources/concept/Task_Concept_Final.txt` | Business requirements |
| 2 | `sources/stage_1/architecture_organizer.md` | Stage 1 architecture |
| 3 | `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2.zip` | Technical contract |
| 4 | `sources/stage_3_4/Organizer_Stage3_Final_Baseline_3.4.zip` | UX baseline |
| 5 | `sources/stage_4_1_1/Organizer_Stage4_PRD_Candidate_4.1.1.zip` | PRD candidate |

- **Immutable canonical sources**: the five `sources/**` artifacts above; read-only, never modified.
- **Later validated remediation artifacts** (validated, not canonical): Stage 2.3.1 contract-correction deliverables under `outputs/stage_2_3/` (validation PASS on 2026-07-26, version 2.3.1) and the validated Stage 4 final baseline under `work/stage_4_6_lite/final_baseline/` (validation PASS, candidate SHA-256 `F8D092F5951F378D5CEB25A7D476C9E93E7BF158E63434F0E076CD91B0A76FDF`). These carry the corrected contracts frozen below.

## 2. Direct-evidence registry (repository-relative path, SHA-256)

Recorded 2026-08-14 from the working tree at base SHA above; values match `work/stage_4_6_lite/final_baseline/Stage_4_Final_Baseline_Manifest.md` where it covers the same files.

| Path | SHA-256 |
|---|---|
| `AGENTS.md` | `0249F91A88F377F23FB343652BBFEFCC044608E3AB2AF5EA46C11A1DE5CECCB7` |
| `work/PROJECT_CONTINUATION.md` | `F89AE1DE43FA5EFAF68CD0F8FE5B2A42B7E3EFE59829BA6A9C4B6FF927F155BD` |
| `outputs/stage_2_3/openapi/openapi.yaml` | `36C15DFF5ADBA0041FCFD79F5A0D203835DAC5CDD4AD24122BCD92177C13220D` |
| `outputs/stage_2_3/Search_Contract.md` | `3E4458D893EA35F610299E87DF7684FDD78F6684E94885D84D32130D5F08E564` |
| `outputs/stage_2_3/Stage_2_3_Validation.md` | `49F37AE8BDDC40199838CAE0EECF71EB5D2FE34AEF3F9B0E14B9AB386E7846EF` |
| `work/stage_4_6_lite/inputs/candidate/Stage_4_Product_PRD_4.5.md` | `0E28E629297AB95783082ADEB417F9AB15CF9A926C510D61D3BCCAEDA60533E7` |
| `work/stage_4_6_lite/inputs/candidate/Stage_4_Module_PRDs_4.5.md` | `6F96AD8E4ABB6EA6F71D4114789B2A12EFB21B3FECBEE4937D42A169BD08018D` |
| `work/stage_4_6_lite/inputs/candidate/Stage_4_Open_Questions_4.5.md` | `83AB2C8E0E979B0C718373DC512BFEBF6ED368D270120514ADC73E2EDB44D77C` |
| `work/stage_4_6_lite/final_baseline/Stage_4_Final_Validation.md` | `A55CAE88A6A2944929D08924FC02FE3361A0BA17D43B31EB8D283E69174E03C8` |
| `work/stage_4_6_lite/final_baseline/audit_4_6_lite/Stage_4_6_Lite_Regression_Check.md` | `86CF3DCEDFD77C8E651FC08114C5B73672BC3D8C849C36E6E5ED225CCAB072B9` |

## 3. Correction A — Notification urgency (organizational urgency scale)

Gap corrected: the concept requires configurable urgency thresholds, but Stage 2.2 had no writable contract (OQ-001). Stage 2.3.1 added the organization-owned urgency-scale contract; the validated Stage 4 baseline confirmed it unchanged (OQ-001 fixed; regression PASS).

Evidenced facts (Stage 2.3.1 OpenAPI + Stage 4 PRD 4.5 + final-baseline validation):

- **Owner**: organization only; no personal/user override.
- **Operations**: `GET`/`PUT /api/v1/settings/notification-urgency-scale` (`GET_api_v1_settings_notification_urgency_scale`, `PUT_api_v1_settings_notification_urgency_scale`); `POST /api/v1/settings/notification-urgency-scale/reset` (`POST_api_v1_settings_notification_urgency_scale_reset`).
- **DTO names**: `NotificationUrgencyScale` (scope `organization`; `intervals` exactly 4; `version` int64 ≥1; `updatedAt`; nullable `updatedByUserId`), `NotificationUrgencyScalePatch` (`intervals` exactly 4, required), `UrgencyScaleInterval` (`urgencyLevel`, `minScore`/`maxScore` 0–100, `displayToken` 1–64), `UrgencyLevel`.
- **Concurrency rules**: versioned write; `If-Match` required; `Idempotency-Key` required; ETag responses; `VERSION_CONFLICT`, 412, 428; writes online-only, never queued.
- **Permissions**: `Settings.ReadOwn` (read); `System.Configure` (PUT/reset).
- **Semantic levels**: `low`, `normal`, `high`, `critical`; exactly one interval per level; scores cover 0..100 with no gaps or overlap.
- **Presentation behavior**: scale changes presentation mapping for existing and future notifications; semantic urgency unchanged; color is never the only urgency signal (text/icon required).
- **Audit behavior**: `notification_urgency_scale.changed` on successful PUT/reset and permission-sensitive denials, with actor, outcome, correlationId and redacted diff; no interval payload or secrets; separate from product analytics/diagnostics.
- **Compatibility behavior**: old clients (Stage 2.2) keep their built-in display mapping.

Evidence: `outputs/stage_2_3/openapi/openapi.yaml` lines 23079–23260 (paths) and 31785–31863 (schemas); `work/stage_4_6_lite/inputs/candidate/Stage_4_Product_PRD_4.5.md` lines 168, 170, 192–199; `work/stage_4_6_lite/inputs/candidate/Stage_4_Module_PRDs_4.5.md` FR-261/264/265/266/269–274/279, BR-103 (lines 4668, 5368, 5711, 6272, 6566, 6798–6857) and the online-only-writes constraint (line 6883); `work/stage_4_6_lite/inputs/candidate/Stage_4_Open_Questions_4.5.md` OQ-001 (line 19); `work/stage_4_6_lite/final_baseline/Stage_4_Final_Validation.md`; `work/stage_4_6_lite/final_baseline/audit_4_6_lite/Stage_4_6_Lite_Regression_Check.md` (OQ-001 Fixed).

## 4. Correction B — Employee global search

Gap corrected: the concept requires employee search/group, but Stage 2.2 omitted the `employee` type (OQ-003). Stage 2.3.1 added `employee` to the global-search contract with `EmployeeSearchResult`; the validated Stage 4 baseline confirmed it unchanged (OQ-003 fixed; regression PASS).

Evidenced facts (same evidence set):

- **Search type**: distinct type `employee` in `types` (1–10 unique values); separate result group (`resultType: employee`).
- **DTO name**: `EmployeeSearchResult` — `userId`, `displayName`, nullable `departmentId`/`departmentName`/`jobTitle`, `accountStatus` (`active`/`blocked`/`inactive`), `deepLink`, `isRedacted`; required: `userId`, `displayName`, `accountStatus`, `deepLink`, `isRedacted`; no avatar, email, phone or arbitrary role fields.
- **Authorization**: `Search.Use` on `GET /api/v1/search`; server-side authorization before pagination; blocked users omitted unless the caller has `User.Block`; blocked existence never disclosed.
- **Redaction**: redacted nullable fields are null with `isRedacted=true`; neutral placeholder; no hidden values/counts (STATE-030).
- **Filtering and ranking order**: server-side filtering (employee-compatible filters `q`, `departments`, `types`, `cursor`, `limit`; forbidden `userIds`, `projectIds`, `contactIds`, `hasFiles`, `from`, `to`) → authorization/redaction/blocked-user policy → ranking (`relevance desc`, `updatedAt desc`, `type asc`, `id asc`) → cursor pagination; client post-filtering forbidden.
- **Cursor visibility binding**: cursor bound to normalized query, filters, stable sort, authorization scope version, search-index snapshot and employee visibility policy version; invalid `SEARCH_CURSOR_INVALID` / expired `SEARCH_CURSOR_EXPIRED` → restart from page 1 with the same filters.
- **Accessible grouping**: separate Employees group; keyboard navigation; screen-reader group/status/redaction semantics; non-color status.
- **Deep-link recheck**: `deepLink` opens only after a repeated server-side recheck (Stage 4 PRD line 209); Enter uses the DTO `deepLink` (Stage 3.5 SCR-133/CMP-002).
- **Avatar exclusion**: no avatar in the DTO; writable avatar contract out of scope (OQ-004).

Evidence: `outputs/stage_2_3/openapi/openapi.yaml` lines 16856–17156 (path, filter compatibility, cursor binding) and 31864–31909 (`EmployeeSearchResult`); `outputs/stage_2_3/Search_Contract.md` (Stage 2.2 contract that omitted `employee` — the corrected gap); `work/stage_4_6_lite/inputs/candidate/Stage_4_Product_PRD_4.5.md` lines 169, 204–208 and OQ closure row (line 144); `work/stage_4_6_lite/inputs/candidate/Stage_4_Open_Questions_4.5.md` OQ-003 (line 20); final-baseline validation and regression check (OQ-003 Fixed).

## 5. Freeze constraints

- No field, endpoint, permission, error, threshold or policy was invented; every recorded item is evidenced in section 2 files.
- This increment changes no architecture, API, DTO, database, permission, dependency or business requirement.
- This increment freezes only the two contract corrections and their evidence inputs; Stage 1 and the full implementation baseline remain open.