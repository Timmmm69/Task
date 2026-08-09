# OpenAPI Validation Report — Stage 2.2

## 1. Subject

- File: `openapi/openapi.yaml`.
- OpenAPI version: `3.1.0`.
- API description version: `1.2.0-stage2.2`.
- Size: `959722` bytes.
- SHA-256: `052738F7BF1B02CAB054B92827E17E3EA79EB0C8832C0F5A6E60681E4B363161`.
- Validation date: `2026-07-26`.

## 2. Results

| Gate | Result | Evidence |
|---|---|---|
| YAML parse | PASS | PyYAML 6.0.2 |
| OpenAPI 3.1 validation | PASS | openapi-spec-validator 0.7.2 |
| Redocly lint/validation | PASS | Redocly CLI 2.40.0 |
| Unique operations | PASS | Exactly 241 |
| Method+path parity | PASS | 241 OpenAPI = 241 `catalogs/api_catalog.csv`; differences 0 |
| Unique operation IDs | PASS | 241 unique IDs |
| Local `$ref` resolution | PASS | 2741 references resolved |
| External `$ref` | PASS | 0 |
| Concrete business schemas | PASS | 232 concrete schemas; empty 0 |
| Unbounded `additionalProperties: true` | PASS | 0 |
| Concrete request bodies | PASS | Every catalog body operation has a concrete request schema |
| Concrete success responses | PASS | Every non-204 success response has a concrete schema |
| Optimistic locking | PASS | Required `If-Match`, 409/412/428 and target metadata agree with catalog |
| Versioned response metadata | PASS | Non-204 success responses expose `ETag` |
| Idempotency | PASS | Required/optional `Idempotency-Key` agrees with catalog |
| Permissions | PASS | 91 canonical codes; unknown codes 0 |
| Access policies | PASS | Anonymous/authenticated policies separated from permission codes |
| Stable errors | PASS | 44 catalog codes; unknown endpoint error codes 0 |
| DTO field catalog | PASS | 232 schemas, 1322 field rows |
| Search product contract | PASS | `contactIds`, `hasFiles`, typed `lifecycle`, typed `types`, server filtering and cursor binding |

## 3. Search-specific gate

- Свободный `status` удалён из `GET /api/v1/search`.
- `contactIds` — UUID array, 1–100, unique.
- `hasFiles` — boolean.
- `lifecycle` — unique array of `active` and/or `completed`.
- `types` — закрытый enum из девяти canonical object types.
- Array serialization: `form`, `explode=true`.
- Pagination: opaque cursor.
- Cursor bound to normalized query, filters, stable sort, authorization scope version and search-index snapshot.
- Client-side post-filtering: `forbidden`.
- Errors: `SEARCH_CURSOR_INVALID` и `SEARCH_CURSOR_EXPIRED`.

## 4. Reproducibility

Primary gate:

`qa/stage_2_2_contract_gate.py`

Supporting gates:

- `qa/validate_artifacts.py`;
- Redocly CLI log `qa/reports/stage_2_2_redocly_lint.log`;
- primary gate log `qa/reports/stage_2_2_contract_gate.log`;
- machine-readable summary `qa/stage_2_2_validation.json`.

## 5. Decision

OpenAPI является нормативным машинно-читаемым контрактом. Field-level DTO данные могут использоваться без восстановления по именам endpoint или предположений.
