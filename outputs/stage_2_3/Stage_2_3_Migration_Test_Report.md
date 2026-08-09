
# Stage 2.3 Migration Test Report

## Runtime

- PostgreSQL image: `postgres:16.10-alpine`.
- Database: isolated `organizer_stage_2_1`.
- Migration under test: `db/005_stage_2_3_contract_alignment.sql`.

## Scenario A — clean install

Result: **PASS**.

1. Applied `001`, `002`, `003`, `004`, `005`.
2. Loaded an organization, settings, department, employee profile, and user account fixture.
3. Reran `005` to execute idempotent defaults for the newly created organization.
4. Reran permission seed `002`.
5. Verified one scale and four default intervals covering 0..100.
6. Verified ordering/search indexes and 91 permissions.
7. Submitted an invalid gap (`normal.min_score=26`); the deferred constraint rejected commit.
8. Verified rollback preserved the valid scale.

Evidence: `qa/reports/stage_2_3_runtime/postgres_scenario_a_clean.log`.

## Scenario B — upgrade 2.2 → 2.3

Result: **PASS**.

1. Applied the complete Stage 2.2 state (`001`–`004` plus seed).
2. Inserted realistic organization and employee data.
3. Applied `005`.
4. Verified default scale creation and preserved employee data.
5. Reran `002` and `005`; both passed without duplicate rows.
6. Repeated database contract tests.

Evidence: `qa/reports/stage_2_3_runtime/postgres_scenario_b_upgrade.log`.

## Rollback strategy

The migration is additive. Production rollback uses a documented forward-fix approach; destructive down migration is intentionally not supplied.
