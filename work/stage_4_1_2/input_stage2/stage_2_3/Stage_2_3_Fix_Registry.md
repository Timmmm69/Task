
# Stage 2.3 Fix Registry

| ID | Severity | Root cause | Changed files | Verification | Status |
|---|---|---|---|---|---|
| S231-H-001 | High | Migration 005 used unqualified, nonexistent `organizations`/`users` relations and only documented defaults in comments | `db/005_stage_2_3_contract_alignment.sql`; DB tests | Clean install and 2.2→2.3 upgrade on PostgreSQL 16.10; repeated migration/seed; invalid gap rejected | Fixed |
| S231-H-002 | High | New contract referenced permission codes `Settings.Read` and `User.ReadBlocked`, absent from the canonical 91-code catalog | `openapi/openapi.yaml`; `catalogs/api_catalog.csv`; contract alignment and derived artifacts | All 230 operation permission bindings resolve; permission count remains 91; Redocly/codegen repeated | Fixed |
| S231-H-003 | High | Candidate retained Stage 2.2 generated clients/stubs and derived API files with 241 operations; server handler generator hard-coded 241 | `qa/generated/**`; `qa/generate_server_stub.py`; `endpoints_dump.txt`; API docs/diff | C#/TypeScript regeneration for 244 operations; .NET and TypeScript compilation pass; derived catalog parity pass | Fixed |

Remaining defects: Critical **0**, High **0**, Medium **0**.
