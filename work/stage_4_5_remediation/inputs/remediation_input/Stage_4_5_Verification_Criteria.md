# Stage 4.5 Verification Criteria

## AC remediation

- Every replacement AC has one existing primary owner and a semantically narrow Related FR set.
- Given, When and Then describe one bounded behavior and one observable result.
- A criterion must not use catch-all phrases such as `any read or command`, `each applicable error` or a module-wide FR list as its sole test scope.
- Duplicate/template-only AC are rejected unless a documented parameterized test-matrix scheme gives each row a bounded requirement and expected result.

## STATE remediation

- Each candidate STATE ID must resolve to a published Stage 3.5 behavior or an explicit downstream mapping.
- The mapping states historical ID, exact Stage 3.5 state/behavior, affected candidate references and replacement/alias policy.
- Re-scan candidate references; unresolved STATE IDs must equal zero.
