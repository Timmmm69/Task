# Task — Design System 1.0

**Status:** code-based freeze for desktop implementation.

## Foundations

The accepted Direction 2 tokens define system typography, spacing, density, borders, semantic colours, focus and layout. Detailed source values are retained in `source/Foundations_Tokens_Direction_2_0.1.md`.

## Components

45 component families are frozen in `Component_Implementation_Specs_1.0.csv` and mapped to 128 SCR / 37 FLOW through `Component_Usage_Map_1.0.csv`. Each contract includes anatomy, variants, state inputs, keyboard/UIA/scaling behavior and failure rules.

## Interaction and accessibility

The system preserves deterministic focus return, keyboard activation, non-colour meaning, programmatic names/roles/states, reduced motion, forced colours and contained scrolling. Native Windows UIA/Narrator timing and actual OS DPI certification remain external Gate evidence.

## Implementation rule

Do not invent new business logic, permissions, DTO fields or error behavior in code. Resolve any mismatch against the packaged traceability and production-policy register before implementation.
