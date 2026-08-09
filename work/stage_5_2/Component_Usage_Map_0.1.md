# Stage 5.2 — Component Usage Map 0.1

**Date:** 2026-07-28  
**Status:** behavioral and traceability contract complete; visual construction awaits `VIS-001`  
**Coverage:** 45/45 component families

## Summary

| Check | Result |
|---|---:|
| Component families | 45 |
| Families with SCR usage | 45 |
| Families without SCR usage | 0 |
| Families with FLOW usage | 45 |
| Families without direct FLOW usage | 0 |

## Library tiers

| Tier | Families |
|---|---:|
| 02 Primitives | 1 |
| 03 Core Components | 8 |
| 04 State Components | 15 |
| 05 Domain Components | 12 |
| 06 Patterns | 9 |

## Contract

Each row in `Component_Usage_Map_0.1.csv` records the owning library tier, SCR and FLOW consumers, required variants, applicable canonical states, NFR ownership, accessibility contract and visual dependency.

Direct FLOW usage is evidence from the published Flow Design Inventory. A blank FLOW list does not remove the component from scope when normative SCR surfaces require it.

## Boundary

This artifact completes the pre-visual usage map. Final visual variants, dimensions, token values, component frames, accessibility screenshots and development measurements remain blocked by `VIS-001` and subsequent Gate 5.2 work.
