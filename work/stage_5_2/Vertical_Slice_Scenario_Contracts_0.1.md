# Stage 5.2 — Vertical Slice Scenario Contracts 0.1

**Date:** 2026-07-28  
**Status:** scenario contracts complete; frames and prototype await `VIS-001`  
**Coverage:** 10/10 planned P0 vertical-slice flows

## Slice composition

| Slice | FLOW contracts |
|---|---:|
| VS-01 Auth & Bootstrap | 2 |
| VS-02 Task Creation | 3 |
| VS-03 Search & Redaction | 1 |
| VS-04 Resilience & Conflict | 4 |

## Validation

| Check | Expected | Actual | Result |
|---|---:|---:|---|
| Planned vertical-slice flows | 10 | 10 | PASS |
| Rows without SCR references | 0 | 0 | PASS |
| Rows without required states | 0 | 0 | PASS |
| Rows without acceptance contract | 0 | 0 | PASS |

## Contract

Each row in `Vertical_Slice_Scenario_Contracts_0.1.csv` defines the source flow, roles/permissions, APIs, SCR surfaces, entry points, required states/errors, realistic fixture, critical-path acceptance, keyboard behavior and accessibility evidence.

The four slices are Auth & Bootstrap, Task Creation, Search & Redaction, and Resilience & Conflict. They are the execution contract for tasks S5-0209 through S5-0214.

## Boundary

This artifact is not a visual frame or interactive prototype. Layout, styling, component instances and final accessibility evidence remain blocked by `VIS-001`.
