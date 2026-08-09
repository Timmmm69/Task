
# Stage 4.2 — Independent Comprehensive Audit Report

**Версия:** 4.2-audit.1  
**Дата:** 2026-07-26  
**Вердикт:** **FAIL**

## 1. Область и метод

Проверены исходные ZIP, candidate manifest, концепция, Stage 1, полный Stage 2.3.1 OpenAPI/catalogs/database evidence, Stage 3.5 UX baseline и все 15 файлов кандидата. Заявленные PASS и totals не принимались без пересчёта. Существенные findings повторно проверены в кандидатском артефакте, связанном разделе и source of truth.

Роли аудита: product, solution/backend/desktop/data architects, UX/accessibility, QA, security/permissions и requirements writing.

## 2. Целостность

- Audit Input SHA-256: `4CC6DF2A7CF54F3E692971BDB2A39322615442748E95AD7104A1564229CD845F` — PASS.
- Candidate SHA-256: `84260071D3917AE00AA617FDBF2E5AB540A719F7D717367B0504E36159845AF9` — PASS.
- CRC, повторное открытие, path traversal, пустые/временные файлы — PASS.
- Audit Input manifest: 23/23 files, size/hash PASS.
- Candidate manifest: 14/14 hashed files, size/hash PASS.
- Stage 2.3.1 и Stage 3.5 normative ZIP: SHA/CRC/reopen PASS.

## 3. Независимые метрики

| Метрика | Независимый результат |
|---|---:|
| Модули | 21 |
| Уникальные FR | 279 |
| Уникальные BR | 113 |
| Уникальные AC | 1824 |
| Уникальные NFR | 25 |
| API operationId trace coverage | 244/244 |
| FR без AC | 0 |
| AC без прямой FR-связи | 466 |
| Orphaned от verification requirements | 87 |
| Unknown permissions / stable errors | 0 / 0 |
| Duplicate IDs | 0 |
| Broken source target | 1 target / 1565 occurrences |
| Unverified / provisional | 1 / 1 |

API coverage здесь означает operation-level подтверждение: каждый из 244 operationId существует в OpenAPI и имеет FR+AC mapping. Это не является сертификацией всех 1 340 DTO field constraints или выполнением PostgreSQL migrations и не отменяет High-дефекты семантической трассировки обновлённых FR.

## 4. Модульная структура

Все 21 module block имеют разделы A–O. Структурная форма полна; содержательная проверка выявила несогласованные updated FR/AC, stale current sources и incomplete cross-cutting verification.

| Module | FR | BR | AC | AC без Gherkin |
|---|---:|---:|---:|---:|
| MOD-001 | 11 | 4 | 55 | 8 |
| MOD-002 | 9 | 4 | 17 | 13 |
| MOD-003 | 2 | 4 | 9 | 6 |
| MOD-004 | 9 | 3 | 74 | 6 |
| MOD-005 | 18 | 5 | 134 | 11 |
| MOD-006 | 12 | 4 | 97 | 8 |
| MOD-007 | 11 | 4 | 85 | 8 |
| MOD-008 | 10 | 3 | 74 | 7 |
| MOD-009 | 13 | 4 | 101 | 9 |
| MOD-010 | 15 | 4 | 120 | 9 |
| MOD-011 | 18 | 5 | 130 | 11 |
| MOD-012 | 23 | 3 | 168 | 8 |
| MOD-013 | 25 | 4 | 160 | 14 |
| MOD-014 | 7 | 12 | 42 | 6 |
| MOD-015 | 7 | 5 | 33 | 7 |
| MOD-016 | 3 | 3 | 13 | 5 |
| MOD-017 | 4 | 4 | 27 | 6 |
| MOD-018 | 12 | 9 | 73 | 7 |
| MOD-019 | 55 | 4 | 302 | 24 |
| MOD-020 | 7 | 5 | 53 | 11 |
| MOD-021 | 8 | 4 | 41 | 12 |
| ALL | — | 16 | 16 | 15 |

## 5. Source hierarchy

Текущие baseline корректно названы в §14, но активные catalog rows продолжают использовать Stage 2.2/3.4, старое имя field traceability и stale count 241. Поэтому source hierarchy реализована не полностью.

## 6. FR / BR / AC

- 279 unique FR; формально FR blank AC=0.
- Семантический аудит выявил 9 updated FR с legacy AC.
- 113 BR; 96 без Related FR.
- 1824 AC; 211 без Given/When/Then; 466 без direct FR.
- 87 cross-cutting requirement rows не имеют AC.

## 7. API, permissions, errors, data and security

- OpenAPI: 244 unique operations; 244 operationId mapped to FR and AC.
- Normative DTO field catalog: 1 340 rows; entity catalog: 66 rows. Выполнены set-level и targeted semantic checks, но не заявляется полный PASS всех 1 340 field constraints или исполнения migration SQL.
- Permissions catalog: 91; unknown permission codes in PRD: 0.
- Stable errors catalog: 44; unknown codes: 0.
- Idempotency/ETag/If-Match/server-side filtering references сохранены в operation audit.
- OQ-003 основная MOD-014 field table противоречит фактическому OpenAPI employee enum.
- Отдельных Critical data-loss/privilege-escalation defects не подтверждено.

## 8. UX, accessibility and FLOW

Stage 3.5 field traceability: 1078 rows; 38-row delta воспроизводится как 28 urgency + 10 employee rows; 20 controls воспроизводятся только после semantic normalization. FLOW collision explained by DEC-060, but FLOW-038 lacks an addressable UX definition. Target accessibility behavior в целом сильное, однако atomic keyboard/focus/resize AC неполны.

## 9. Analytics, NFR, dependencies and risks

Analytics/diagnostics/security audit разделены, raw query/PII/path/secrets ограничены. Retention остаётся OQ-010. NFR-024 и OQ-008 доказывают минимум один provisional/unverified item. Risk register не имеет probability/owner/trigger.

## 10. Findings

### AUDIT-4.2-001 — High / Other

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-001 |
| Severity | High |
| Category | Other |
| Artifact | Stage_4_Product_PRD_4.1.2.md; Stage_4_Dependency_Risk_Register_4.1.2.md |
| Location | Product PRD §9 line 186 and §14.4 line 229; Risk Register §3 line 63; Open Questions OQ-001/OQ-003 |
| Related IDs | OQ-001; OQ-003; FR-159; FR-264; FR-270–279; BR-098–112; AC-1790–1824; CMP-001; CMP-002 |
| Source of truth | Stage 2.3.1 OpenAPI urgency GET/PUT/reset and employee search schemas; Stage 3.5 SCR-153 and SCR-133/134/135; candidate DEC-053–059 |
| Expected | Current PRD and risk register use one current OQ status. Superseded gap text is explicitly historical. |
| Actual | §9 and the current risk section say OQ-001/OQ-003 remain High and block Stage 4.2; §14.4 and Open Questions say Fixed. |
| Defect | The package simultaneously opens and closes both product-blocking OQ. |
| Consequence | Approvers and implementation teams receive mutually exclusive normative instructions; the audit gate and feature scope are indeterminate. |
| Recommended fix | Replace current blocking wording with explicit resolved history and one Fixed status linked to Stage 2.3.1/3.5 evidence. |
| Verification | No current occurrence says remain High/block Stage 4.2/contract absent; every current status resolves to Fixed and the resolved-history chain remains. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-002 — High / FR

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-002 |
| Severity | High |
| Category | FR |
| Artifact | Stage_4_Module_PRDs_4.1.2.md; Stage_4_Requirements_Traceability_4.1.2.csv; Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv |
| Location | Appendix P.2 lines 6792–6801; FR-159, FR-160, FR-243, FR-244, FR-260, FR-261, FR-265, FR-266, FR-269 and their mapped AC |
| Related IDs | FR-159; FR-160; FR-243; FR-244; FR-260; FR-261; FR-265; FR-266; FR-269; AC-1002–1008; AC-1404; AC-1405; AC-1425; AC-1426; AC-1430; AC-1431; AC-1435 |
| Source of truth | Stage 2.3.1 OpenAPI 1.2.0-stage2.3; Stage 3.5 delta/flows; candidate Appendix P.2 |
| Expected | Each updated FR has AC that directly verifies its current formulation. |
| Actual | Nine of ten updated FR retain legacy AC mappings. Examples: FR-243 employee group → AC-1404 safe route; FR-261 urgency projection → AC-1426 toast text alternative; FR-269 scale audit event → AC-1435 generic history. |
| Defect | Formal FR→AC links exist but do not test the effective normative FR text. |
| Consequence | The candidate can pass its AC suite while omitting updated OQ-001/OQ-003 behavior. |
| Recommended fix | Consolidate each changed FR into its primary module row and remap/add atomic Gherkin AC for every new outcome. |
| Verification | A semantic FR→AC review confirms that every mandatory outcome in each changed FR appears in observable Then/And steps. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-003 — High / UX

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-003 |
| Severity | High |
| Category | UX |
| Artifact | Stage_4_Module_PRDs_4.1.2.md |
| Location | MOD-014 field table line 4446 and embedded AC-070 line 4508; conflicting addendum lines 6814–6817 and 6862–6873 |
| Related IDs | MOD-014; OQ-003; FR-159; FR-275–278; BR-070; BR-105–112; AC-070; AC-1804–1820; CMP-002 |
| Source of truth | Stage 2.3.1 OpenAPI query.types includes employee with maxItems=10; SearchSuggestion/EmployeeSearchResult; Stage 3.5 SCR-133/134/135 and FLOW-019 |
| Expected | The main module table contains employee/maxItems=10 and embedded AC-070 tests only BR-070 deprecation/replacement. |
| Actual | MOD-014 still lists nine types without employee and maxItems=9; embedded AC-070 says employee is unsupported and remains an OQ. The later addendum says the opposite. |
| Defect | One module contains two incompatible current search contracts. |
| Consequence | Desktop and QA may legally implement and accept search without employees; OQ-003 is reopened. |
| Recommended fix | Update the main MOD-014 field table and embedded AC-070 to match OpenAPI and the canonical AC catalog; mark old text historical. |
| Verification | employee is present, maxItems=10, embedded/catalog AC-070 agree, and no current statement says employee is unsupported. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-004 — High / AC

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-004 |
| Severity | High |
| Category | AC |
| Artifact | Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv; module sections K |
| Location | 211 rows with empty Gherkin: 96 BR-oriented and 115 FR-oriented; examples AC-001–069, AC-113, AC-128, AC-130, AC-1403–1435 |
| Related IDs | 96 BR; 115 FR; 211 AC |
| Source of truth | Stage 4.2 audit charter Part 7; linked FR/BR; Product PRD §10 test coverage gate |
| Expected | Every AC contains Given, When and an observable, unambiguous Then. |
| Actual | 211/1824 AC contain only a short Scenario title and a blank Gherkin field. |
| Defect | The criteria repeat a rule or happy-path label without executable preconditions, action and result. |
| Consequence | QA cannot derive deterministic tests; incompatible implementations can pass the same criterion. |
| Recommended fix | Add atomic Gherkin for all 211, including role/capability, state/error, exact response/UI outcome and boundaries where applicable. |
| Verification | 1824/1824 Gherkin cells are non-empty and contain Given/When/Then; manual spot-check confirms observable outcomes. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-005 — Medium / Traceability

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-005 |
| Severity | Medium |
| Category | Traceability |
| Artifact | Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv |
| Location | 466 AC without a direct FR reference: 112 BR-only and 354 DATA-only |
| Related IDs | AC-001–097 with exceptions; AC-1436–1789; BR-*; DATA-001–021 |
| Source of truth | Stage 4.2 audit charter Part 7 requires each AC to link to an existing FR; catalog column is named FR/BR |
| Expected | Every AC resolves to an effective FR, directly or through an explicit normalized relation. |
| Actual | 466 AC have no FR token. DATA-only rows also violate the declared FR/BR column domain. |
| Defect | The verification graph cannot mechanically demonstrate FR coverage for these AC. |
| Consequence | AC counts overstate effective FR verification and make impact analysis unreliable. |
| Recommended fix | Add direct FR links or a normalized AC↔requirement↔FR relation with validated transitive resolution. |
| Verification | AC without resolvable FR=0; all intermediate requirement links exist and resolve deterministically. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-006 — Medium / Traceability

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-006 |
| Severity | Medium |
| Category | Traceability |
| Artifact | Stage_4_Requirements_Traceability_4.1.2.csv |
| Location | 87 rows with empty AC: DATA-002/003/016; PERM-001–021; ERR-001–021; SYNC-001–021; AUDIT-001–021 |
| Related IDs | 87 cross-cutting requirement IDs |
| Source of truth | OpenAPI; permissions.csv; errors.csv; Stage 1 server-authoritative/sync/audit rules; Stage 3.5 state matrix |
| Expected | Every row presented as a requirement has explicit verification criteria. |
| Actual | 87 requirements have no AC link; related prose elsewhere does not provide requirement-level traceability. |
| Defect | Cross-cutting requirements are orphaned from the verification ledger. |
| Consequence | Permission, error, sync and audit regressions can escape module DoD. |
| Recommended fix | Link each row to concrete AC or fold it into an FR/BR/NFR with explicit verification. |
| Verification | Requirement rows with blank AC=0 and every link resolves to an AC that tests the stated behavior. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-007 — Medium / BR

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-007 |
| Severity | Medium |
| Category | BR |
| Artifact | Stage_4_Business_Rules_Catalog_4.1.2.csv; module sections E |
| Location | 96/113 BR have an empty Related FR field |
| Related IDs | BR-001–097 except the explicitly related/deprecated subset |
| Source of truth | Concept and current contract/UX sources for each module; Stage 4.2 audit charter Part 6 |
| Expected | Each BR identifies the FR scope to which it applies, including exceptions and priority. |
| Actual | Module and Verification are present, but Related FR is blank for 96 BR. |
| Defect | The BR↔FR applicability graph is missing. |
| Consequence | A changed FR can silently violate a rule and one BR-level AC cannot prove application to all affected functions. |
| Recommended fix | Populate a many-to-many BR↔FR mapping; formalize scope/exceptions for global rules. |
| Verification | No unexplained empty Related FR; all links exist and rule priority is unambiguous. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-008 — Medium / Traceability

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-008 |
| Severity | Medium |
| Category | Traceability |
| Artifact | Stage_4_Requirements_Traceability_4.1.2.csv; Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv |
| Location | 260 trace rows and 1305 AC source cells reference Stage_3_Field_Traceability.csv |
| Related IDs | 1 missing target; 1565 reference occurrences |
| Source of truth | Actual Stage 3.5 file Stage_3_Field_Traceability_Final_3.5.csv and Stage 3.5 manifest |
| Expected | Every source reference resolves to the exact canonical local file or to a declared manifest alias. |
| Actual | The referenced filename does not exist and no alias is declared. |
| Defect | Mass broken reference contradicts lost references=0. |
| Consequence | Automated evidence resolution and manual review cannot open the claimed source. |
| Recommended fix | Replace the old filename with the exact 3.5 filename or define and validate a formal alias. |
| Verification | Old-name occurrences=0; link checker resolves every source path. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-009 — Medium / Traceability

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-009 |
| Severity | Medium |
| Category | Traceability |
| Artifact | Stage 3.5 User Flows; candidate Decision Log, Traceability CSV and validation |
| Location | Stage 3.5 User Flows has FLOW-035 at lines 537 and 879 and no FLOW-038; candidate has 17 trace rows with FLOW-038 |
| Related IDs | FLOW-035; FLOW-038; FR-264; FR-266; FR-269–274; FR-279; CMP-001; AC-1824 |
| Source of truth | Stage 3.5 User Flows; audit rule forbidding PRD flow without an addressable UX flow |
| Expected | FLOW-035 resolves only to project completion; FLOW-038 has an addressable downstream definition for urgency management. |
| Actual | DEC-060 explains the alias, but the normative UX package still has two FLOW-035 definitions and no FLOW-038 definition. |
| Defect | The semantic correction is not a resolvable cross-artifact trace target. |
| Consequence | Automated trace treats FLOW-038 as unknown and designers must infer which FLOW-035 section is intended. |
| Recommended fix | Add a downstream errata/alias artifact with a full FLOW-038 definition, without changing the Stage 3.5 source ZIP. |
| Verification | Unique flow registry; all FLOW-035 references are project-only and FLOW-038 references urgency-only. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-010 — Medium / Traceability

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-010 |
| Severity | Medium |
| Category | Traceability |
| Artifact | Requirements, BR, AC and NFR catalogs; Readiness artifact |
| Location | Active sources: 20 FR, 81 BR trace rows, 81 BR catalog rows, 108 AC rows use Stage 3.4; NFR-012 uses Stage 2.2 |
| Related IDs | FR-242…269 subset; BR-016…097 subset; related AC; NFR-012 |
| Source of truth | Candidate Product PRD line 204 and CANONICAL_BASELINE.md: Stage 2.3.1/3.5 current; 2.2/3.4 historical only |
| Expected | Active source fields cite current baselines; historical versions are explicitly qualified and secondary. |
| Actual | Hundreds of active rows cite 3.4 or 2.2 as their operative source without historical qualification. |
| Defect | The declared source hierarchy is not consistently applied. |
| Consequence | Regeneration or design review can use stale 1040-row UX/241-operation contract semantics. |
| Recommended fix | Revalidate and relink active rows to 3.5/2.3.1; retain old versions only as marked provenance. |
| Verification | No active source cell uses 2.2/3.4 without explicit historical/superseded qualification and current replacement. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-011 — Medium / NFR

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-011 |
| Severity | Medium |
| Category | NFR |
| Artifact | Stage_4_NFR_Catalog_4.1.2.csv; Stage_4_Open_Questions_4.1.2.md |
| Location | NFR-024 and OQ-008; measurement gaps in NFR-001/003/006/007/015 |
| Related IDs | NFR-001; NFR-003; NFR-006; NFR-007; NFR-015; NFR-024; OQ-008 |
| Source of truth | Architecture §0.5; candidate Open Questions OQ-008; Stage 4.2 NFR measurement gate |
| Expected | Every NFR has an objective pass threshold or is explicitly provisional/unverified. |
| Actual | NFR-024 says availability/RPO/RTO must be measured and confirmed, while OQ-008 remains open; several other targets use undefined terms such as approved/stable/critical. |
| Defect | The catalog contains at least one explicit provisional/unverified NFR despite candidate claims of zero. |
| Consequence | 100% readiness and a final NFR pass cannot be reproduced. |
| Recommended fix | Mark NFR-024 provisional until OQ-008 closes and define objective policies/thresholds for the listed NFR. |
| Verification | Every NFR has reproducible pass/fail criteria; provisional/unverified ledger accurately reports outstanding decisions. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-012 — Medium / Other

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-012 |
| Severity | Medium |
| Category | Other |
| Artifact | Stage_4_Dependency_Risk_Register_4.1.2.md; module risk sections |
| Location | RISK-001–025 |
| Related IDs | DEP-001–024; RISK-001–025; OQ-001; OQ-003 |
| Source of truth | Stage 4.2 audit charter Part 19 |
| Expected | Each risk has probability, impact, owner, trigger, mitigation, verification and status. |
| Actual | All 25 lack probability, owner and trigger; RISK-022–025 also lack a separate impact field; older risks repeat generic mitigation. |
| Defect | The file is a topic list, not an operable risk register. |
| Consequence | Material privacy, privilege, data-loss and concurrency risks cannot be owned, monitored or closed. |
| Recommended fix | Extend the schema and populate probability, impact, owner role, trigger, preventive/contingency actions, verification and status. |
| Verification | Risk lint confirms all required fields are non-empty and closure evidence is traceable. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-013 — Medium / UX

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-013 |
| Severity | Medium |
| Category | UX |
| Artifact | NFR catalog; AC catalog; Module PRDs |
| Location | NFR-002–005; AC-1805/1807/1815; target CMP accessibility sections |
| Related IDs | CMP-001; CMP-002; SCR-133/134/135/153; FLOW-019; NFR-002–005 |
| Source of truth | Stage 3.5 UX Architecture active-descendant/Up/Down/Enter/Esc semantics and adaptive desktop layout rules |
| Expected | Atomic AC cover exact keyboard/focus behavior and adaptive window resizing for CMP-001/CMP-002. |
| Actual | General NFR exist, but no atomic AC covers active descendant, Up/Down, normal Esc focus return, CMP-001 tab order, or below-1100 logical-pixel adaptation. |
| Defect | Published UX interactions are not fully transferred into the PRD verification matrix. |
| Consequence | Desktop implementations can diverge and remain inaccessible or clipped while satisfying broad NFR wording. |
| Recommended fix | Add exact keyboard/UIA/focus and resize AC for both components. |
| Verification | Keyboard-only and resize matrix passes Tab/Shift+Tab/arrows/Enter/Esc, focus return, minimum window and 200% scaling. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-014 — Medium / API

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-014 |
| Severity | Medium |
| Category | API |
| Artifact | Stage_4_Product_PRD_4.1.2.md; Stage_4_0_PRD_Readiness_4.1.2.md |
| Location | Product PRD line 191; Readiness lines 107 and 127 |
| Related IDs | OpenAPI operation inventory; product DoD |
| Source of truth | Stage 2.3.1 openapi.yaml: 244 unique operationId values |
| Expected | Current DoD/readiness uses 244/244. |
| Actual | Current-looking sections retain 241/241 or all 241 while later sections say 244. |
| Defect | The release gate has incompatible API totals. |
| Consequence | A three-operation regression can still satisfy the stale DoD. |
| Recommended fix | Update or clearly supersede all active 241-operation gates. |
| Verification | No current count uses 241; independently parsed operationId count and coverage both equal 244. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-015 — Low / AC

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-015 |
| Severity | Low |
| Category | AC |
| Artifact | Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv |
| Location | AC-1486, AC-1487, AC-1501, AC-1579, AC-1709, AC-1710, AC-1715, AC-1716, AC-1767 |
| Related IDs | 9 AC |
| Source of truth | Stage 4.2 audit charter Part 7 |
| Expected | Expected results use measurable wording. |
| Actual | Nine AC use the undefined word корректно. |
| Defect | A local test oracle is ambiguous. |
| Consequence | Reviewers may accept different results. |
| Recommended fix | Replace each occurrence with an exact state/value/ordering/error outcome. |
| Verification | Undefined-term scan=0 and each affected Then is objectively assertable. |
| Confidence | High |
| Status | Open |

### AUDIT-4.2-016 — Low / Security

| Поле | Содержание |
|---|---|
| Audit ID | AUDIT-4.2-016 |
| Severity | Low |
| Category | Security |
| Artifact | Stage_4_Analytics_Audit_Requirements_4.1.2.md; Stage_4_Open_Questions_4.1.2.md |
| Location | Analytics §5 line 92; OQ-010 |
| Related IDs | OQ-010; AN-001–052; BR-113; AC-1823 |
| Source of truth | Candidate privacy/minimization requirements and Product+Security ownership of OQ-010 |
| Expected | Production retention, access, rotation/deletion and storage boundary are approved. |
| Actual | Retention remains open and temporary structured-log storage is allowed, although payload minimization is well specified. |
| Defect | Acknowledged governance gap. |
| Consequence | Operational metadata may be retained longer or more broadly than necessary. |
| Recommended fix | Close OQ-010 with an explicit storage/access/retention/rotation/deletion policy before production. |
| Verification | Policy/config tests confirm retention and access while event allowlists and PII bans remain. |
| Confidence | High |
| Status | Open |

## 11. Verdict rule

Есть High findings → **FAIL**. Этап 4.3 обязателен; после исправления требуется повторный независимый audit, а не только внутренняя self-validation.
