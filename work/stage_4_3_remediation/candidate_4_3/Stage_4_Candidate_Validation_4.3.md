# Stage 4.3 Candidate Validation

**Статус:** внутренний remediation precheck; не является повторным независимым аудитом и не утверждает final baseline.

## 1. Нормативная база

- Technical contract: Stage 2.3.1, OpenAPI `1.2.0-stage2.3`.
- UX baseline: Stage 3.5.
- Stage 2.2 и Stage 3.4: только явно помеченная historical/superseded provenance.
- Downstream errata: `Stage_4_3_Reference_Repair_Report.md`; исходный Stage 3.5 не изменяется.

## 2. Инварианты, подтверждённые до финального пакетного пересчёта

| Check | Result | Evidence |
| --- | --- | --- |
| Modules | 21 expected | Стабильный диапазон MOD-001…MOD-021 |
| OpenAPI operation inventory | 244 unique operationId | Stage 2.3.1 `openapi/openapi.yaml` |
| Permission inventory | 91 | Stage 2.3.1 `catalogs/permissions.csv` |
| Stable error inventory | 44 | Stage 2.3.1 `catalogs/errors.csv` |
| UX field/action rows | 1078 | Stage 3.5 `Stage_3_Field_Traceability_Final_3.5.csv` |
| Active technical baseline | Stage 2.3.1 | No active 2.2 source is permitted |
| Active UX baseline | Stage 3.5 | No active 3.4 source is permitted |
| Superseded API-total gate | 0 in this validation/readiness pair | Current gate is 244/244 |
| FLOW-035 | Project completion/archive only | Stage 3.5 flow registry and full project flow |
| FLOW-038 | Urgency-scale management | Addressable full definition in downstream errata |
| Analytics retention | Defined as deployment gate | Company-approved 30–90-day application logs; 1095-day canonical audit policy |
| Independent re-audit | Not performed | Must be performed in Stage 4.4 |

## 3. Accessibility atomic verification gate

The final candidate-wide AC scan must prove, with explicit Given/When/Then criteria rather than broad NFR wording:

| Surface | Required atomic behavior |
| --- | --- |
| CMP-001 / SCR-153 | Logical Tab/Shift+Tab order; keyboard editing of all four ordered rows; visible focus; screen-reader level/min/max/error announcements; focus to first invalid field; High Contrast and label/icon/text independent of color |
| CMP-002 / SCR-133/134 | Group heading; single active descendant; Up/Down navigation; Enter authorized deep link; Esc closes/clears and returns focus to invoker/result context; redaction/status announced and not color-only |
| App shell / target surfaces | Below approximately 1100 logical px details becomes overlay/drawer and navigation collapses without hiding primary command/status |
| Window/scaling | Primary actions remain visible at 200% scale; mixed-DPI reflow has no clipping/overlap; restored window remains on a visible monitor |
| Validation/errors | Plain-language field association, recovery action and status/error announcement |

Normative references: Stage 3.5 `Stage_3_UX_Architecture_Final_3.5.md` §§5.2, 6, 25.2, 26 and `Accessibility и IDs` under CMP-001/CMP-002.

## 4. Final machine-validation fields

The package assembler must populate these values from the final synchronized CSV/Markdown set, not copy counts from 4.1.2:

- FR, BR, AC and NFR totals;
- API operation coverage and field-level coverage;
- FR without AC;
- AC without valid primary owner;
- orphaned requirements;
- unknown permission/error/SCR/FLOW/STATE/CMP IDs;
- duplicate/deprecated IDs;
- broken targets and occurrences;
- requirements without current source;
- unverified and provisional;
- per-finding status for all 4 High, 10 Medium and 2 Low items.

Any non-zero High/Medium, broken target, orphan, unknown ID, unverified/provisional item, or any operation coverage below 244/244 blocks handoff to Stage 4.4. Passing this internal precheck permits packaging for independent re-audit only; it does not establish design or development readiness.
