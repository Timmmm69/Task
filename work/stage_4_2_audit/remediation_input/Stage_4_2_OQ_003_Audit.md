
# Stage 4.2 — OQ-003 Audit: Employees in Global Search

## Independent result

**Status: Conflicted / reopened by candidate contradiction.**

Stage 2.3.1 and Stage 3.5 substantively provide:

- distinct `employee` result type and Employees group;
- `EmployeeSearchResult` fields only;
- department/jobTitle/status only when permitted/present; no avatar;
- server filtering/redaction/blocked policy before pagination;
- ranking, mixed search, deep link, cursor stability and no client post-filter;
- separation from contacts, admin users and `userIds`.

However, MOD-014 line 4446 still defines nine types without employee and maxItems=9; embedded AC-070 line 4508 requires employee to be unsupported. This contradicts OpenAPI, the current AC catalog and the addendum. OQ-003 cannot be Fixed until AUDIT-4.2-003 and related FR/AC trace defects are corrected.
