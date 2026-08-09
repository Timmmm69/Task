
# Stage 4.2 — Remediation Plan for Stage 4.3

## Priority 0 — approval blockers

1. Resolve AUDIT-4.2-001: one current OQ status and no active obsolete gap text.
2. Resolve AUDIT-4.2-002: consolidate all ten updated FR and repair semantic AC mappings.
3. Resolve AUDIT-4.2-003: correct MOD-014 employee enum/maxItems and embedded AC-070.
4. Resolve AUDIT-4.2-004: add complete Given/When/Then for all 211 AC.

## Priority 1 — traceability and quality

5. Normalize AC→FR, BR→FR and cross-cutting requirement→AC relations.
6. Repair 1565 source occurrences and define an addressable FLOW-038 downstream errata.
7. Replace active Stage 2.2/3.4 sources with 2.3.1/3.5 after content comparison.
8. Remove/supersede stale 241-operation gates.
9. Make risk register operable and NFR thresholds/provisional status explicit.
10. Add atomic accessibility/adaptive-window AC.

## Priority 2 — governance/polish

11. Replace nine vague AC terms.
12. Close analytics retention OQ-010 before production.

## Required Stage 4.3 verification

- All High=0 and Critical=0.
- 244/244 operations independently parsed and mapped.
- 1824/1824 AC have Given/When/Then and direct/transitive FR resolution.
- Updated FR semantic AC coverage passes manual review.
- Old field-trace filename occurrences=0; active 2.2/3.4 refs=0 unless marked historical.
- Unique flow registry resolves FLOW-035/FLOW-038.
- Unknown permission/error=0; duplicate IDs=0.
- Unverified/provisional accurately reflects outstanding decisions.
- Final package manifest, SHA-256, CRC and repeat-open pass.
