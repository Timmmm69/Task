
# Stage 4.2 — UX and Accessibility Audit

**Вердикт области:** FAIL.

## Verified baseline figures

- Stage 3.5 field trace rows: **1078**.
- Added rows: **38** = 28 CMP-001 urgency + 10 CMP-002 employee.
- Contract-dependent controls: **20 after semantic normalization**; literal distinct Control strings are 29.
- SCR-133/134/135/153 and CMP-001/002 exist.

## FLOW-035 / FLOW-038

- Historical project FLOW-035 is preserved in candidate references.
- Urgency references use FLOW-038.
- Stage 3.5 still contains two FLOW-035 definitions and no FLOW-038 target; DEC-060 is not a full addressable UX definition. See AUDIT-4.2-009.

## Accessibility coverage

Covered: keyboard-only policy, visible/deterministic focus, high contrast, non-color urgency/status, screen-reader group/status, focus-first-invalid, conflict/draft preservation, neutral unavailable/redaction.

Not atomic enough: active descendant, Up/Down, normal Esc focus return, CMP-001 tab order, sub-1100 logical-pixel adaptation and minimum-window behavior. See AUDIT-4.2-013.

## Design conclusion

Design readiness is **78%**. Visual exploration may start, but production design handoff is blocked by High findings and unresolved trace/accessibility gaps.
