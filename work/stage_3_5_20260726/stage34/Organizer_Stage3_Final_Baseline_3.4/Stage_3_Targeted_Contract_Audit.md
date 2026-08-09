# Stage 3.4. Targeted Contract Audit

**Объект:** только contract-dependent части Этапа 3  
**Метод:** automated contract extraction + independent consistency checks  
**Дата:** 2026-07-26

## 1. Verdict

- Critical: **0**
- High: **0**
- Medium: **2**
- Low: **0**
- `AUD-001`: **Closed**
- `GAP-001`: **Closed**
- `GAP-002`: **Closed**

Stage 3 contract baseline is sufficient for Stage 4 and wireframes. Medium observations are explicit contract/product deltas and are not hidden as provisional controls.

## 2. Evidence

| Check | Result | Evidence |
|---|---|---|
| OpenAPI YAML parse | PASS | OpenAPI `3.1.0` |
| Operations | PASS | 241 |
| Schemas | PASS | 232 |
| DTO field rows | PASS | 1322 |
| Permissions | PASS | 91 |
| Stable errors | PASS | 44 |
| Local refs | PASS | 2741 parsed textual refs; source report certifies 2741 resolved refs |
| Operation parity | PASS | 241/241, 0 differences |
| Desktop C# codegen/build | PASS | validation/codegen report |
| Server stub codegen/build | PASS | validation/codegen report |

## 3. Required targeted checks

| # | Проверка | Result | Detail |
|---:|---|---|---|
| 1 | Все используемые DTO существуют | PASS | All request/response schema references for 99 touched operations resolve. |
| 2 | Все fields существуют | PASS | 1040 rows resolve to DTO catalog/query/header/command source. |
| 3 | Type/format совпадают | PASS | Row generation uses normative DTO/query schema values. |
| 4 | Required/nullable отражены | PASS | Directly copied from DTO field catalog/OpenAPI. |
| 5 | Enum/limits сохранены | PASS | No free-text substitution for typed enums. |
| 6 | Все request bodies представлены в UX | PASS | Missing request fields: 0. |
| 7 | Significant response fields used or excluded | PASS | Version/timestamps/derived/redaction/lifecycle/capability fields traced; internal id/organizationId excluded by rule. |
| 8 | Validation errors have UX handling | PASS | Every field row has validation message; form-level stable errors remain separate. |
| 9 | Versioned writes use optimistic locking | PASS | 62 touched writes have If-Match trace rows. |
| 10 | Search matches concept and OpenAPI | PASS | q, types, projectIds, userIds, departments, contactIds, hasFiles, lifecycle, from, to, cursor, limit; server-side only. |
| 11 | No provisional controls | PASS | Contract-pending and if-supported controls removed. |
| 12 | No `unverified` in field traceability | PASS | 0 occurrences. |
| 13 | No invented UX fields | PASS | Synthetic rows limited to documented If-Match and commands. |
| 14 | No required contract fields absent in UI | PASS | All recursively expanded selected request fields traced. |
| 15 | No new Critical/High | PASS | 0 / 0. |

## 4. Search audit

- `contactIds`: present, UUID array, 1–100 unique.
- `hasFiles`: present, boolean, authorization-aware available FileLocation semantics.
- `lifecycle`: present, enum active/completed, 1–2 unique.
- `types`: 1–9 unique canonical types.
- Cursor errors: `SEARCH_CURSOR_INVALID` 400 and `SEARCH_CURSOR_EXPIRED` 410.
- Any filter change invalidates the previous cursor.
- Client post-filtering paged results is prohibited and absent from final UX artifacts.

## 5. Medium observations

| ID | Severity | Observation |
| --- | --- | --- |
| OBS-3.4-01 | Medium | Profile avatar is required by the broad concept but no request/response DTO field or operation exists. The active editor control is removed; contract change is required before implementation. |
| OBS-3.4-02 | Medium | Configurable urgency color thresholds are not present in NotificationPreferences or UserSettings. No invented control remains; a future contract extension is required if this concept option is kept. |

## 6. Audit conclusion

The former machine-contract blocker is removed. The field-level baseline is reproducible from OpenAPI hash `052738F7BF1B02CAB054B92827E17E3EA79EB0C8832C0F5A6E60681E4B363161` and `Stage_3_Field_Traceability.csv`. Stage 4 and wireframes are allowed, with the two Medium observations tracked as future product/contract decisions rather than hidden UI assumptions.
