# Stage 2.3 Validation

## Results

| Gate | Result |
|---|---|
| yaml_parse | PASS |
| openapi_3_1_validation | PASS |
| local_ref_resolution | PASS (2776) |
| operation_catalog_parity | PASS (244) |
| employee_search_contract | PASS |
| urgency_scale_contract | PASS |
| migration_asset_presence | PASS |
| permission_error_catalog_consistency | PASS (existing codes reused) |
| redocly_lint | NOT RUN — Redocly CLI is not installed in this isolated runtime |
| csharp_generation_and_build | NOT RUN — .NET SDK/NSwag are not installed in this isolated runtime |
| postgresql_execution | NOT RUN — PostgreSQL runtime is not available; migration is supplied as SQL for the existing Stage 2.2 harness |

## Counts

- Operations: **244** (Stage 2.2 + 3).
- DTO/schemas: **237** (Stage 2.2 + 5).
- Permissions: **91** (existing `Settings.Read`, `System.Configure`, `Search.Use`, `User.ReadBlocked` reused).
- Stable errors: **44** (existing `VALIDATION_FAILED`, `FORBIDDEN`, `VERSION_CONFLICT` reused).

## Compatibility

The original endpoints and required response fields are unchanged. Employee fields are additive and optional on `SearchSuggestion`; old clients can retain generic result rendering. Existing notification urgency semantics are unchanged.

The three unavailable executable gates require rerun in the normal Stage 2.2 CI/runtime before release promotion.
