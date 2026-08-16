# Stage 1 initial gap overrides — deterministic disposition audit

This generated report audits the original unresolved overrides and their no-API dispositions.
It does not prove Desktop implementation or Stage 1 completion.

## Method

- Inputs: the three `gap_overrides_wave_*.csv` files in `traceability/`.
- Selection: exact original `Resolution status = unresolved` rows only.
- Disposition ledger: `traceability/desktop_no_api_dispositions.csv`.
- Classification uses exact CSV values; no endpoint or operationId is inferred.
- Output contains no timestamps and all groupings are sorted.

## Validation

- Validation errors: 0.
- Initial unresolved override total: 80; actual: 80.
- Reviewed no-API dispositions: 80.
- Initial unresolved rows covered by the ledger: 80.

## Totals by wave

| Wave | Rows read | resolved | unresolved |
|---:|---:|---:|---|
| A | 483 | 440 | 43 |
| B | 396 | 378 | 18 |
| C | 367 | 348 | 19 |
| **Total** | **1246** | **1166** | **80** |

## Totals by module

| Module | unresolved | Examples |
|---:|---:|---|
| MOD-001 | 3 | `wave-a.csv:277` / `wave-a.csv:287` / `wave-a.csv:307` |
| MOD-002 | 12 | `wave-a.csv:335` / `wave-a.csv:336` / `wave-a.csv:337` |
| MOD-003 | 4 | `wave-a.csv:384` / `wave-a.csv:385` / `wave-a.csv:386` |
| MOD-004 | 3 | `wave-a.csv:474` / `wave-a.csv:482` / `wave-a.csv:498` |
| MOD-005 | 9 | `wave-a.csv:651` / `wave-a.csv:652` / `wave-a.csv:653` |
| MOD-006 | 3 | `wave-a.csv:816` / `wave-a.csv:827` / `wave-a.csv:849` |
| MOD-007 | 3 | `wave-a.csv:948` / `wave-a.csv:958` / `wave-a.csv:978` |
| MOD-008 | 3 | `wave-a.csv:1065` / `wave-a.csv:1074` / `wave-a.csv:1092` |
| MOD-009 | 3 | `wave-a.csv:1209` / `wave-a.csv:1221` / `wave-a.csv:1245` |
| MOD-010 | 3 | `wave-b.csv:305` / `wave-b.csv:319` / `wave-b.csv:347` |
| MOD-011 | 6 | `wave-b.csv:497` / `wave-b.csv:498` / `wave-b.csv:514` |
| MOD-012 | 3 | `wave-b.csv:743` / `wave-b.csv:765` / `wave-b.csv:809` |
| MOD-013 | 3 | `wave-b.csv:997` / `wave-b.csv:1021` / `wave-b.csv:1069` |
| MOD-014 | 3 | `wave-c.csv:236` / `wave-c.csv:242` / `wave-c.csv:254` |
| MOD-015 | 6 | `wave-c.csv:300` / `wave-c.csv:301` / `wave-c.csv:306` |
| MOD-016 | 4 | `wave-c.csv:339` / `wave-c.csv:341` / `wave-c.csv:343` |
| MOD-017 | 3 | `wave-c.csv:381` / `wave-c.csv:384` / `wave-c.csv:390` |
| MOD-019 | 3 | `wave-c.csv:871` / `wave-c.csv:925` / `wave-c.csv:1033` |
| MOD-021 | 3 | `wave-b.csv:1121` / `wave-b.csv:1128` / `wave-b.csv:1142` |
| **Total** | **80** | |

## Totals by type

| Type | unresolved |
|---:|---|
| AC | 80 |
| **Total** | **80** |

## Groups by Wave, Module, Type and exact Resolution rationale text

| Wave | Module | Type | Resolution rationale | unresolved | Examples |
|---:|---:|---:|---:|---:|---|
| A | MOD-001 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-a.csv:277` / `wave-a.csv:287` / `wave-a.csv:307` |
| A | MOD-002 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 12 | `wave-a.csv:335` / `wave-a.csv:336` / `wave-a.csv:337` |
| A | MOD-003 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 4 | `wave-a.csv:384` / `wave-a.csv:385` / `wave-a.csv:386` |
| A | MOD-004 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-a.csv:474` / `wave-a.csv:482` / `wave-a.csv:498` |
| A | MOD-005 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 9 | `wave-a.csv:651` / `wave-a.csv:652` / `wave-a.csv:653` |
| A | MOD-006 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-a.csv:816` / `wave-a.csv:827` / `wave-a.csv:849` |
| A | MOD-007 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-a.csv:948` / `wave-a.csv:958` / `wave-a.csv:978` |
| A | MOD-008 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-a.csv:1065` / `wave-a.csv:1074` / `wave-a.csv:1092` |
| A | MOD-009 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-a.csv:1209` / `wave-a.csv:1221` / `wave-a.csv:1245` |
| B | MOD-010 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-b.csv:305` / `wave-b.csv:319` / `wave-b.csv:347` |
| B | MOD-011 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 6 | `wave-b.csv:497` / `wave-b.csv:498` / `wave-b.csv:514` |
| B | MOD-012 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-b.csv:743` / `wave-b.csv:765` / `wave-b.csv:809` |
| B | MOD-013 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-b.csv:997` / `wave-b.csv:1021` / `wave-b.csv:1069` |
| B | MOD-021 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-b.csv:1121` / `wave-b.csv:1128` / `wave-b.csv:1142` |
| C | MOD-014 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-c.csv:236` / `wave-c.csv:242` / `wave-c.csv:254` |
| C | MOD-015 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop projection behavior' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-c.csv:301` / `wave-c.csv:307` / `wave-c.csv:319` |
| C | MOD-015 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-c.csv:300` / `wave-c.csv:306` / `wave-c.csv:318` |
| C | MOD-016 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 4 | `wave-c.csv:339` / `wave-c.csv:341` / `wave-c.csv:343` |
| C | MOD-017 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-c.csv:381` / `wave-c.csv:384` / `wave-c.csv:390` |
| C | MOD-019 | AC | AC remains genuinely unresolved: parent FR is Desktop-only (api=no_api in implementation_matrix) and traceability 4.5 API cell 'Desktop-only; использует существующие read/command API при необходимости' names no concrete operationId or endpoint, so no provable operation set exists to bind this requirement to. | 3 | `wave-c.csv:871` / `wave-c.csv:925` / `wave-c.csv:1033` |
| **Total** | | | | **80** | |

## Groups whose sources state there is no confirmed operationId

Rows whose exact rationale contains "without an operationId": 0.

| Resolution rationale | unresolved |
|---|---:|
| **Total** | **0** |

## Sum checks

- By wave: 43 + 18 + 19 = 80.
- By module: 80 = 80.
- By type: 80 = 80.
- By composite group: 80 = 80.
- Total unresolved = 80; expected = 80.

## Scope and limitations

- No endpoint, operationId, permission or handler has been guessed.
- A no-API disposition makes an API link inapplicable; it does not prove a Desktop test is implemented.
- This report does not prove Stage 1 completion.
- Classification is limited to values read verbatim from the CSV inputs.
