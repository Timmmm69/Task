# Product Policies 1.0 — user-approved policy record

Increment: `TASK-PROD-S1-002` (base `main` @ `7fd48019c421c257b6cb7113f92799cbcfaa2045`).
Status: user-approved policy record for the Stage 1 production baseline. The policies below
record decisions the product owner approved in `work/TASK_PRODUCTION_EXECUTION_PROMPT.md`
section 5 («Зафиксированные решения», lines 72–89).

## 1. Nature of this document

- This is a **user-approved policy record**, not an edit to any immutable source
  (`sources/**`) and not a new API, DTO, database, permission, deployment implementation,
  or release claim.
- It freezes Product 1.0 policy inputs only; it does not declare Stage 1 or the
  implementation baseline complete.
- `work/TASK_PRODUCTION_EXECUTION_PROMPT.md` and the Stage 4 candidate documents were
  read but not modified.

## 2. Approved Product 1.0 policies

| # | Policy | Approved decision | Source (prompt §5) |
|---|---|---|---|
| P1 | Full canonical MVP | all 21 modules | line 74 |
| P2 | First-release language | Russian only — релиз 1.0 только на русском языке | line 83 |
| P3 | Avatar | no avatar | line 83 |
| P4 | Desktop platform | WPF/MVVM on current .NET LTS, Windows 10/11 x64 | line 75 |
| P5 | Server platform | ASP.NET Core on current .NET LTS, Windows Server 2022 | line 76 |
| P6 | Background processes | API, worker and backup/restore agent as Windows Services | line 77 |
| P7 | Shared durable store | PostgreSQL as the sole shared durable store | line 78 |
| P8 | TLS reverse proxy | Caddy as a TLS reverse proxy Windows Service | line 79 |
| P9 | Desktop cache | SQLite cache only for permitted read models | line 80 |
| P10 | Authority | server authoritative for data and final authorization | line 81 |
| P11 | Offline behavior | read-only offline; no queued commands | line 82 |
| P12 | Toast fallback | Notification Center mandatory fallback for Windows toast | line 84 |
| P13 | File access | Windows/SMB ACL authoritative for physical file access | line 85 |
| P14 | Trash retention | default 30 days | line 86 |
| P15 | Deployment topology | one server without automatic failover; availability target 99.5%, RPO 15 minutes, RTO 4 hours | line 87 |
| P16 | Installation | idempotent PowerShell installation; no MSI/GPO in 1.0 | line 88 |
| P17 | Acceptance | customer-like staging before production | line 89 |

## 3. Mapping to candidate open questions

| OQ | Policy | Closure |
|---|---|---|
| OQ-004 | P3 (no avatar) | confirmed as MVP exclusion — resolves the open avatar contract question for 1.0 |
| OQ-005 | P12 (Notification Center fallback) | confirmed as the mandatory fallback — toast display cannot be guaranteed by the OS |
| OQ-007 | P14 (Trash retention 30 days) | operational policy approved before the production baseline |
| OQ-008 | P15 (single server; 99.5% / 15 min / 4 h) | company-approved operational contract supplying the numeric values the candidate left unapproved |
| OQ-009 | P2 (Russian-only release) | locales confirmed as Russian-only for the 1.0 release |

The mapping covers exactly the five candidate questions above; no other open question is
mapped by this record.

## 4. Constraints

- Every policy above is traceable verbatim to `work/TASK_PRODUCTION_EXECUTION_PROMPT.md`
  section 5; nothing was invented by this record.
- This record adds or changes no API, DTO, database, permission, error, code, deployment
  script, source or output artifact.
- This record does not change business requirements and does not constitute a release.