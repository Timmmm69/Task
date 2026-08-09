# Stage 4.5 remediation plan

| Finding | Root cause | Affected artifacts | Planned fix | Verification | Status |
|---|---|---|---|---|---|
| AUDIT-4.4-001 | 87 cross-cutting criteria mixed multiple FR and independent outcomes. | AC catalog, module PRDs, traceability. | Split every affected relationship into one-FR atomic AC, retaining the old ID for its first behavior. | Atomicity analysis and precheck. | Applied |
| AUDIT-4.4-002 | 30 active numeric references were not published by Stage 3.5. | Product PRD, module PRDs, AC catalog, traceability. | Replace active references with published named behavior or stable-error/UI condition; preserve historical ledger. | State resolution and reference validation. | Applied |
| AUDIT-4.2-004 residual | Broad templates were not executable as single tests. | AC catalog and module PRDs. | Same atomic split as AUDIT-4.4-001. | No broad templates or multi-FR AC remain. | Applied |
| AUDIT-4.2-006 residual | Mechanical cross-cutting links did not demonstrate semantic verification. | Traceability and AC catalog. | Each cross-cutting requirement now maps to concrete one-FR executable AC. | Owner-to-AC and FR-to-AC checks. | Applied |
