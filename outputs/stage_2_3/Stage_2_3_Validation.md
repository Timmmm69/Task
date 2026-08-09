
# Stage 2.3 Validation

Version: `2.3.1`  
Validation date: `2026-07-26`  
Decision: **PASS — final baseline is eligible for Stage 3.5**.

## Gate summary

| Gate | Result | Evidence |
|---|---|---|
| Input ZIP CRC, manifest and SHA-256 | PASS | 352 ZIP entries; 351 manifest entries; all hashes matched |
| YAML parse and OpenAPI 3.1 schema | PASS | `qa/reports/stage_2_3_runtime/openapi_contract_validation.log` |
| Local `$ref` resolution | PASS | 2,776 references resolved |
| Redocly lint | PASS | 0 errors, 0 warnings; container exit 0 |
| Contract counts | PASS | 244 operations; 237 schemas; 91 permissions; 44 stable errors |
| Backward compatibility | PASS | All 241 Stage 2.2 operations preserved; additive changes only |
| C# desktop generation | PASS | NSwag 14.7.1; 244 operations |
| C# desktop compilation | PASS | .NET 8.0.423; 0 errors, 0 warnings |
| C# server stub generation | PASS | NSwag 14.7.1; 244 actions |
| C# server compilation | PASS | .NET 8.0.423; 0 errors, 0 warnings |
| TypeScript dependent codegen | PASS | 277 SDK files; strict compilation passed |
| PostgreSQL clean install | PASS | PostgreSQL 16.10 |
| PostgreSQL 2.2 → 2.3 upgrade | PASS | Data preserved |
| Repeated seed/migration | PASS | `002` and `005` reruns passed |
| Urgency-scale constraints | PASS | Invalid gap rejected; transaction rolled back |
| Functional contract tests | PASS | 27 checks |
| OQ-001 | CLOSED | Organization urgency-scale API/DTO/migration |
| OQ-003 | CLOSED | Employee global-search result contract |
| Critical / High remaining | 0 / 0 | All detected defects fixed and gates repeated |

## Counts

- API operations: **244**.
- DTO/schema: **237**.
- Permission catalog: **91**.
- Stable error codes: **44**.
- Concrete request schemas checked: **124**.
- Concrete success response schemas checked: **231**.
- Operation permission bindings checked: **230**.
- Stable error bindings checked: **4,579**.

## Defects

Three defects were found and fixed: two High contract/runtime defects and one High packaging/codegen consistency defect. Remaining Critical/High/Medium: **0/0/0**.
