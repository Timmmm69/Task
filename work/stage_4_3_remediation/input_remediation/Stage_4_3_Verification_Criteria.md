# Stage 4.3 — Verification Criteria

Stage 4.3 is complete only when all checks below are evidenced by regenerated artifacts.

1. Critical=0 and High=0 in the repeated independent audit.
2. OQ-001 and OQ-003 each have one non-contradictory status across PRD, OQ register, risk register and readiness report.
3. MOD-014 contains one search contract: `employee` is consistently supported, maxItems is consistently 10, and AC-070 no longer states the opposite.
4. All ten updated FR in Appendix P.2 have semantically aligned, executable AC.
5. Every AC has an approved parent; every FR and every required cross-cutting rule has verification evidence.
6. Every normative AC has explicit precondition, action and observable result; vague outcome words are eliminated.
7. All local source paths resolve; zero references remain to the nonexistent Stage 3 field-trace filename.
8. Every FLOW/SCR/STATE reference resolves to an addressable Stage 3.5 definition or an explicitly versioned replacement.
9. Unknown permission codes=0, unknown stable errors=0, and every OpenAPI operation has valid access/error references.
10. OpenAPI operation coverage remains 244/244 after regeneration; stale 241 claims are absent.
11. Unverified=0 and provisional=0, including resolution of NFR-024/OQ-008.
12. NFR thresholds state metric, environment, method, sample/window and pass boundary.
13. Risk entries contain owner, probability, impact, trigger, mitigation and contingency.
14. Accessibility verification covers active descendant, Up/Down, Esc focus return, CMP-001 tab order and sub-1100-logical-pixel behavior.
15. New package manifest, internal file hashes, external ZIP SHA-256, CRC/read-to-completion and reopen checks all pass.
