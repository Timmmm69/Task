# Stage 4.5 candidate validation

**Result: PASS.** This is a remediation validation, not a final baseline or independent Stage 4.6 audit.

| Check | Result |
|---|---:|
| 87 analyses | PASS |
| remediated atomic AC | PASS |
| broad original AC | PASS |
| Given/When/Then | PASS |
| unjustified multi-FR | PASS |
| multi-outcome remediated AC | PASS |
| invalid primary owner | PASS |
| FR without AC | PASS |
| duplicate AC | PASS |
| orphaned cross-cutting requirement | PASS |
| unknown STATE | PASS |
| unknown UX (none) | PASS |
| unresolved aliases | PASS |
| broken targets | PASS |

## Counters

| Metric | Value |
|---|---:|
| modules | 21 |
| fr | 279 |
| br | 113 |
| ac | 2954 |
| nfr | 25 |
| api_covered | 244 |
| original_87_analyzed | 87 |
| atomic_ac_for_87 | 1130 |
| new_atomic_ac | 1043 |
| rewritten_original | 87 |
| split_original | 87 |
| max_related_fr | 10 |
| multi_fr_justified | 386 |
| multi_fr_unjustified | 0 |
| multi_outcome_remediated | 0 |
| missing_gwt | 0 |
| invalid_owner | 0 |
| fr_without_ac | 0 |
| duplicate_ac | 0 |
| orphaned_cross | 0 |
| unknown_state | 0 |
| unknown_ux | 0 |
| unresolved_alias | 0 |
| broken_targets | 0 |
| broken_occurrences | 0 |
| state_alias | 20 |
| state_non_state | 10 |
| state_direct_canonical | 0 |
| unknown_permission | 0 |
| unknown_stable_error | 0 |
| unverified | 0 |
| provisional | 0 |

Stage 2.3.1 and Stage 3.5 were read-only inputs. OQ-001, OQ-003 and MOD-014 remain Fixed.
