# Stage 1 unresolved gap overrides — deterministic analysis

This generated report is NOT a gap resolution. It neither resolves any row nor proves completion of Stage 1.

## Method

- Inputs: the three `gap_overrides_wave_*.csv` files in `traceability/`.
- Selection: exact `Resolution status = unresolved` rows only.
- Classification uses exact CSV values; no endpoint or operationId is inferred.
- Output contains no timestamps and all groupings are sorted.

## Validation

- Validation errors: 0.
- Expected unresolved total: 245; actual: 245.

## Totals by wave

| Wave | Rows read | resolved | unresolved |
|---:|---:|---:|---|
| A | 483 | 373 | 110 |
| B | 396 | 332 | 64 |
| C | 367 | 296 | 71 |
| **Total** | **1246** | **1001** | **245** |

## Totals by module

| Module | unresolved | Examples |
|---:|---:|---|
| MOD-001 | 18 | `wave-a.csv:19` / `wave-a.csv:20` / `wave-a.csv:21` |
| MOD-002 | 16 | `wave-a.csv:38` / `wave-a.csv:39` / `wave-a.csv:40` |
| MOD-003 | 8 | `wave-a.csv:56` / `wave-a.csv:57` / `wave-a.csv:58` |
| MOD-004 | 27 | `wave-a.csv:67` / `wave-a.csv:68` / `wave-a.csv:69` |
| MOD-005 | 13 | `wave-a.csv:84` / `wave-a.csv:85` / `wave-a.csv:86` |
| MOD-006 | 7 | `wave-a.csv:112` / `wave-a.csv:113` / `wave-a.csv:114` |
| MOD-007 | 7 | `wave-a.csv:133` / `wave-a.csv:134` / `wave-a.csv:135` |
| MOD-008 | 7 | `wave-a.csv:153` / `wave-a.csv:154` / `wave-a.csv:155` |
| MOD-009 | 7 | `wave-a.csv:171` / `wave-a.csv:172` / `wave-a.csv:173` |
| MOD-010 | 7 | `wave-b.csv:18` / `wave-b.csv:19` / `wave-b.csv:20` |
| MOD-011 | 18 | `wave-b.csv:42` / `wave-b.csv:43` / `wave-b.csv:44` |
| MOD-012 | 7 | `wave-b.csv:70` / `wave-b.csv:71` / `wave-b.csv:72` |
| MOD-013 | 25 | `wave-b.csv:101` / `wave-b.csv:102` / `wave-b.csv:103` |
| MOD-014 | 19 | `wave-c.csv:18` / `wave-c.csv:19` / `wave-c.csv:20` |
| MOD-015 | 13 | `wave-c.csv:42` / `wave-c.csv:43` / `wave-c.csv:44` |
| MOD-016 | 8 | `wave-c.csv:59` / `wave-c.csv:60` / `wave-c.csv:61` |
| MOD-017 | 12 | `wave-c.csv:70` / `wave-c.csv:71` / `wave-c.csv:72` |
| MOD-018 | 10 | `wave-c.csv:83` / `wave-c.csv:84` / `wave-c.csv:85` |
| MOD-019 | 9 | `wave-c.csv:109` / `wave-c.csv:110` / `wave-c.csv:111` |
| MOD-021 | 7 | `wave-b.csv:135` / `wave-b.csv:136` / `wave-b.csv:137` |
| **Total** | **245** | |

## Totals by type

| Type | unresolved |
|---:|---|
| AC | 165 |
| AUDIT | 20 |
| DATA | 20 |
| ERR | 20 |
| PERM | 20 |
| **Total** | **245** |

## Groups by Wave, Module, Type and exact Resolution rationale text

| Wave | Module | Type | Resolution rationale | unresolved | Examples |
|---:|---:|---:|---:|---:|---|
| A | MOD-001 | AC | FR-242 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-a.csv:277` / `wave-a.csv:287` / `wave-a.csv:307` |
| A | MOD-001 | AC | The source references only an OpenAPI schema/parameter; the field-to-operation mapping is not confirmed by the allowed sources; no operationId to report. | 11 | `wave-a.csv:253` / `wave-a.csv:254` / `wave-a.csv:255` |
| A | MOD-001 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:23` |
| A | MOD-001 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:19` |
| A | MOD-001 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:21` |
| A | MOD-001 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:20` |
| A | MOD-002 | AC | FR-243 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 4 | `wave-a.csv:335` / `wave-a.csv:343` / `wave-a.csv:351` |
| A | MOD-002 | AC | FR-244 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 4 | `wave-a.csv:336` / `wave-a.csv:344` / `wave-a.csv:352` |
| A | MOD-002 | AC | FR-245 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 4 | `wave-a.csv:337` / `wave-a.csv:345` / `wave-a.csv:353` |
| A | MOD-002 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:42` |
| A | MOD-002 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:38` |
| A | MOD-002 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:40` |
| A | MOD-002 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:39` |
| A | MOD-003 | AC | FR-246 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 4 | `wave-a.csv:384` / `wave-a.csv:385` / `wave-a.csv:386` |
| A | MOD-003 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:60` |
| A | MOD-003 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:56` |
| A | MOD-003 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:58` |
| A | MOD-003 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:57` |
| A | MOD-004 | AC | FR-247 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-a.csv:474` / `wave-a.csv:482` / `wave-a.csv:498` |
| A | MOD-004 | AC | The source references only an OpenAPI schema/parameter; the field-to-operation mapping is not confirmed by the allowed sources; no operationId to report. | 20 | `wave-a.csv:443` / `wave-a.csv:444` / `wave-a.csv:445` |
| A | MOD-004 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:71` |
| A | MOD-004 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:67` |
| A | MOD-004 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:69` |
| A | MOD-004 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:68` |
| A | MOD-005 | AC | FR-248 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-a.csv:651` / `wave-a.csv:668` / `wave-a.csv:702` |
| A | MOD-005 | AC | FR-249 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-a.csv:652` / `wave-a.csv:669` / `wave-a.csv:703` |
| A | MOD-005 | AC | FR-250 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-a.csv:653` / `wave-a.csv:670` / `wave-a.csv:704` |
| A | MOD-005 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:88` |
| A | MOD-005 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:84` |
| A | MOD-005 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:86` |
| A | MOD-005 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:85` |
| A | MOD-006 | AC | FR-251 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-a.csv:816` / `wave-a.csv:827` / `wave-a.csv:849` |
| A | MOD-006 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:116` |
| A | MOD-006 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:112` |
| A | MOD-006 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:114` |
| A | MOD-006 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:113` |
| A | MOD-007 | AC | FR-252 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-a.csv:948` / `wave-a.csv:958` / `wave-a.csv:978` |
| A | MOD-007 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:137` |
| A | MOD-007 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:133` |
| A | MOD-007 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:135` |
| A | MOD-007 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:134` |
| A | MOD-008 | AC | FR-253 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-a.csv:1065` / `wave-a.csv:1074` / `wave-a.csv:1092` |
| A | MOD-008 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:157` |
| A | MOD-008 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:153` |
| A | MOD-008 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:155` |
| A | MOD-008 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:154` |
| A | MOD-009 | AC | FR-254 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-a.csv:1209` / `wave-a.csv:1221` / `wave-a.csv:1245` |
| A | MOD-009 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:175` |
| A | MOD-009 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:171` |
| A | MOD-009 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:173` |
| A | MOD-009 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-a.csv:172` |
| B | MOD-010 | AC | FR-255 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-b.csv:305` / `wave-b.csv:319` / `wave-b.csv:347` |
| B | MOD-010 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:22` |
| B | MOD-010 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:18` |
| B | MOD-010 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:20` |
| B | MOD-010 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:19` |
| B | MOD-011 | AC | FR-256 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-b.csv:497` / `wave-b.csv:514` / `wave-b.csv:548` |
| B | MOD-011 | AC | FR-257 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-b.csv:498` / `wave-b.csv:515` / `wave-b.csv:549` |
| B | MOD-011 | AC | The source references only an OpenAPI schema/parameter; the field-to-operation mapping is not confirmed by the allowed sources; no operationId to report. | 8 | `wave-b.csv:458` / `wave-b.csv:466` / `wave-b.csv:467` |
| B | MOD-011 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:46` |
| B | MOD-011 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:42` |
| B | MOD-011 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:44` |
| B | MOD-011 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:43` |
| B | MOD-012 | AC | FR-258 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-b.csv:743` / `wave-b.csv:765` / `wave-b.csv:809` |
| B | MOD-012 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:74` |
| B | MOD-012 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:70` |
| B | MOD-012 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:72` |
| B | MOD-012 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:71` |
| B | MOD-013 | AC | FR-259 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-b.csv:997` / `wave-b.csv:1021` / `wave-b.csv:1069` |
| B | MOD-013 | AC | The source references only an OpenAPI schema/parameter; the field-to-operation mapping is not confirmed by the allowed sources; no operationId to report. | 18 | `wave-b.csv:946` / `wave-b.csv:947` / `wave-b.csv:948` |
| B | MOD-013 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:105` |
| B | MOD-013 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:101` |
| B | MOD-013 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:103` |
| B | MOD-013 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:102` |
| B | MOD-021 | AC | FR-269 is Desktop-only with no dedicated API operation; the allowed sources confirm no server operation for this AC. | 3 | `wave-b.csv:1121` / `wave-b.csv:1128` / `wave-b.csv:1142` |
| B | MOD-021 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:139` |
| B | MOD-021 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:135` |
| B | MOD-021 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:137` |
| B | MOD-021 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-b.csv:136` |
| C | MOD-014 | AC | FR-260 is a Desktop-only/Desktop-behavior requirement with no dedicated server operation; the allowed sources confirm no new server operation for this AC. | 3 | `wave-c.csv:236` / `wave-c.csv:242` / `wave-c.csv:254` |
| C | MOD-014 | AC | FR-275 is a Desktop-only/Desktop-behavior requirement with no dedicated server operation; the allowed sources confirm no new server operation for this AC. | 3 | `wave-c.csv:237` / `wave-c.csv:243` / `wave-c.csv:255` |
| C | MOD-014 | AC | FR-276 is a Desktop-only/Desktop-behavior requirement with no dedicated server operation; the allowed sources confirm no new server operation for this AC. | 3 | `wave-c.csv:238` / `wave-c.csv:244` / `wave-c.csv:256` |
| C | MOD-014 | AC | FR-277 is a Desktop-only/Desktop-behavior requirement with no dedicated server operation; the allowed sources confirm no new server operation for this AC. | 3 | `wave-c.csv:239` / `wave-c.csv:245` / `wave-c.csv:257` |
| C | MOD-014 | AC | FR-278 is a Desktop-only/Desktop-behavior requirement with no dedicated server operation; the allowed sources confirm no new server operation for this AC. | 3 | `wave-c.csv:240` / `wave-c.csv:246` / `wave-c.csv:258` |
| C | MOD-014 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:22` |
| C | MOD-014 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:18` |
| C | MOD-014 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:20` |
| C | MOD-014 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:19` |
| C | MOD-015 | AC | FR-261 is a Desktop-only/Desktop-behavior requirement with no dedicated server operation; the allowed sources confirm no new server operation for this AC. | 3 | `wave-c.csv:300` / `wave-c.csv:306` / `wave-c.csv:318` |
| C | MOD-015 | AC | FR-279 is a Desktop-only/Desktop-behavior requirement with no dedicated server operation; the allowed sources confirm no new server operation for this AC. | 3 | `wave-c.csv:301` / `wave-c.csv:307` / `wave-c.csv:319` |
| C | MOD-015 | AC | The source references only an OpenAPI schema/parameter; the field-to-operation mapping is not confirmed by the allowed sources; no operationId to report. | 3 | `wave-c.csv:287` / `wave-c.csv:288` / `wave-c.csv:289` |
| C | MOD-015 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:46` |
| C | MOD-015 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:42` |
| C | MOD-015 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:44` |
| C | MOD-015 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:43` |
| C | MOD-016 | AC | FR-262 is a Desktop-only/Desktop-behavior requirement with no dedicated server operation; the allowed sources confirm no new server operation for this AC. | 4 | `wave-c.csv:339` / `wave-c.csv:341` / `wave-c.csv:343` |
| C | MOD-016 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:63` |
| C | MOD-016 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:59` |
| C | MOD-016 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:61` |
| C | MOD-016 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:60` |
| C | MOD-017 | AC | FR-263 is a Desktop-only/Desktop-behavior requirement with no dedicated server operation; the allowed sources confirm no new server operation for this AC. | 3 | `wave-c.csv:381` / `wave-c.csv:384` / `wave-c.csv:390` |
| C | MOD-017 | AC | The source references only an OpenAPI schema/parameter; the field-to-operation mapping is not confirmed by the allowed sources; no operationId to report. | 5 | `wave-c.csv:370` / `wave-c.csv:371` / `wave-c.csv:372` |
| C | MOD-017 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:74` |
| C | MOD-017 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:70` |
| C | MOD-017 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:72` |
| C | MOD-017 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:71` |
| C | MOD-018 | AC | FR-273 is a Desktop-only/Desktop-behavior requirement with no dedicated server operation; the allowed sources confirm no new server operation for this AC. | 3 | `wave-c.csv:477` / `wave-c.csv:488` / `wave-c.csv:510` |
| C | MOD-018 | AC | FR-274 is a Desktop-only/Desktop-behavior requirement with no dedicated server operation; the allowed sources confirm no new server operation for this AC. | 3 | `wave-c.csv:478` / `wave-c.csv:489` / `wave-c.csv:511` |
| C | MOD-018 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:87` |
| C | MOD-018 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:83` |
| C | MOD-018 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:85` |
| C | MOD-018 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:84` |
| C | MOD-019 | AC | FR-265 is a Desktop-only/Desktop-behavior requirement with no dedicated server operation; the allowed sources confirm no new server operation for this AC. | 3 | `wave-c.csv:871` / `wave-c.csv:925` / `wave-c.csv:1033` |
| C | MOD-019 | AC | The source references only an OpenAPI schema/parameter; the field-to-operation mapping is not confirmed by the allowed sources; no operationId to report. | 2 | `wave-c.csv:797` / `wave-c.csv:812` |
| C | MOD-019 | AUDIT | Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:113` |
| C | MOD-019 | DATA | Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:109` |
| C | MOD-019 | ERR | Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:111` |
| C | MOD-019 | PERM | Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 1 | `wave-c.csv:110` |
| **Total** | | | | **245** | |

## Groups whose sources state there is no confirmed operationId

Rows whose exact rationale contains "without an operationId": 80.

| Resolution rationale | unresolved |
|---|---:|
| Sources name domain command + audit/history endpoints without an operationId; audit evidence is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 20 |
| Sources name module operations without an operationId; stable error handling is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 20 |
| Sources name module operations without an operationId; the DTO field contract is enforced by the module AC rows (resolved individually); no confirmed API link for the module-level row. | 20 |
| Sources name module operations without an operationId; the permission set is enforced by the module Permission-denial AC rows (resolved individually); no confirmed API link for the module-level row. | 20 |
| **Total** | **80** |

## Sum checks

- By wave: 110 + 64 + 71 = 245.
- By module: 245 = 245.
- By type: 245 = 245.
- By composite group: 245 = 245.
- Total unresolved = 245; expected = 245.

## Scope and limitations

- This report resolves nothing and proves nothing about Stage 1 completion.
- No endpoint, operationId, permission or handler has been guessed.
- Classification is limited to values read verbatim from the CSV inputs.
