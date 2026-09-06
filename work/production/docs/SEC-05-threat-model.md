# SEC-05 threat model

Status: reviewed for the current production source baseline on 2026-09-06.

This model covers the Task desktop client, HTTP API, workers, database migrator, PostgreSQL
runtime boundary, backup agent and container validation topology. It does not certify a customer
deployment. Reverse proxy, corporate CA, firewall rules, mounted-secret ACLs and the backup
repository remain deployment controls owned by `SEC-03` and the applicable operations gates.

## Sources and method

The review resolves requirements in canonical order:

1. `sources/concept/Task_Concept_Final.txt`, sections 24-25;
2. `sources/stage_1/architecture_organizer.md`, sections 10 and 15;
3. Stage 2.2 runtime, API, permission and error contracts;
4. the current code and executable tests under `work/production`.

The analysis uses asset/actor/trust-boundary decomposition plus STRIDE-style abuse cases. The
resource-consumption disposition also follows OWASP API4:2023 and NIST SP 800-63B guidance: bound
input and expensive work, rate-limit the subscriber account, and use additional source/risk-based
throttling. References:

- https://owasp.org/API-Security/editions/2023/en/0xa4-unrestricted-resource-consumption/
- https://pages.nist.gov/800-63-4/sp800-63b.html#rate-limiting-throttling

## Assets and security objectives

| Asset | Required property |
|---|---|
| Passwords, pepper and signing keys | No plaintext persistence or logging; memory-hard password hashing; external key custody |
| Access and refresh tokens | Short access lifetime; one-time refresh rotation; device/session binding; secure desktop storage |
| Organization data | Server-derived organization context; no cross-organization disclosure or mutation |
| Roles, relationships and object ACL | Deny by default; explicit deny precedence; transactional recheck before mutation |
| Audit/history | Append-only security audit; safe metadata; distinct access permissions |
| PostgreSQL and migrations | Separate migration/runtime roles; no employee-facing database port; parameterized access |
| Desktop configuration and local state | HTTPS-only endpoint; DPAPI CurrentUser vault; endpoint changes clear credentials |
| Images and deployment configuration | Reproducible packages; non-root/read-only runtime; external secrets; bounded network exposure |
| Backups | Integrity, encryption, isolated restore and separate key custody |
| Availability | Bounded request size, pagination, authentication work and retry behavior |

## Actors

- ordinary employee attempting horizontal access;
- privileged employee or administrator making an unauthorized or mistaken change;
- disabled/former employee with old tokens;
- LAN/VPN attacker without an account;
- malware running in the employee's Windows account;
- attacker holding a copied refresh token, backup or database dump;
- compromised reverse proxy, file share, update source or operations account.

## Data flow and trust boundaries

```text
[Windows user]
    | Windows session / DPAPI boundary
[Task Desktop]
    | HTTPS + server-certificate validation boundary
[Reverse proxy / corporate CA]       deployment evidence pending (SEC-03)
    | loopback/private container edge
[Task.Api]
    | authenticated request context + runtime DB credential
[PostgreSQL private network]

[Task.Api] -- durable events --> [Task.Worker]
[Task.BackupAgent] -- encrypted artifact --> [Backup repository]
[Task Desktop] -- OS/SMB ACL boundary --> [approved file server]
```

Organization, user, role and permission identity are never accepted from a client header or body.
The reverse proxy may supply transport metadata only when it is explicitly trusted by deployment;
the application currently uses the socket peer address for abuse throttling and does not trust an
arbitrary forwarded address.

## Abuse-case review

| ID | Abuse case | Current control/evidence | Disposition |
|---|---|---|---|
| TM-01 | Password guessing against one account | Account lockout, account-scoped progressive limiter, uniform invalid-credential response | Controlled and regression-tested |
| TM-02 | Rotate unique login names to exhaust Argon2 memory/CPU or limiter state | 8 KiB auth-body cap; canonical field maxima; bounded account/address/global windows; maximum two concurrent password checks; bounded key cardinality | HIGH finding remediated in SEC-05 |
| TM-03 | Enumerate valid accounts by error or timing | Dummy password verification for unknown account; uniform `INVALID_CREDENTIALS` | Controlled; timing still requires deployment monitoring |
| TM-04 | Replay a rotated refresh token | Refresh-token hash only; one-time rotation; reuse revokes family/session and audits | Controlled and tested |
| TM-05 | Present a valid JWT after account/session/scope change | Handler reloads authoritative server state and compares credential/scope versions | Controlled and tested |
| TM-06 | Forge token algorithm, issuer, audience, lifetime or key id | ES256-only validation; bounded lifetime; current/previous key ring; key-pair startup validation | Controlled and tested |
| TM-07 | Override organization/user with headers or JSON | Immutable server-derived request context; identity-header rejection; DTO allowlists | Controlled and tested |
| TM-08 | Cross-organization IDOR or existence probe | Mandatory organization predicates; unauthorized/foreign objects collapse to `OBJECT_NOT_VISIBLE` | Controlled in current vertical slices and tested |
| TM-09 | Mass-assign author, status, project or protected fields | Strict JSON contracts/additional-property rejection; server actor; field-level permission checks | Controlled in current vertical slices and tested |
| TM-10 | Bypass route authorization | Authenticated fallback policy plus explicit permission metadata on business routes | Controlled; route inventory test is mandatory |
| TM-11 | SQL injection through filters, identifiers or search | Parameterized Npgsql commands and bounded/allowlisted query contracts | No exploitable path found in reviewed slices |
| TM-12 | Leak password/token/body through logs or errors | Structured safe metadata; no Authorization/body logging; sanitized Problem Details | Controlled and tested |
| TM-13 | Oversized/malformed auth JSON consumes memory before validation | Bounded stream read before deserialize; `REQUEST_TOO_LARGE`; unmapped members rejected | Remediated and tested |
| TM-14 | TLS downgrade or certificate bypass in desktop | Only absolute HTTPS endpoints; default platform certificate validation; no bypass callback | Application control present; proxy/CA evidence pending SEC-03 |
| TM-15 | Reach PostgreSQL from employee VLAN | Validation compose has no DB host port and uses an internal DB network | Static topology controlled; production firewall/DB TLS pending SEC-03 |
| TM-16 | Container privilege escalation | `USER app`, read-only FS, tmpfs, all capabilities dropped, no-new-privileges | Controlled in validation/release topology |
| TM-17 | Steal tracked secrets or image-layer credentials | File references/external mounts; startup fails on invalid key material; repository scan | Controlled in source; host ACL/key custody pending SEC-03 |
| TM-18 | Abuse administrative reset/role operations | Separate permissions, last-administrator protection, transactional audit/idempotency | Controlled and tested |
| TM-19 | Trick desktop into exposing SMB credentials or executing a file | No automatic open; server stores metadata only; allowed-root and OS ACL model | Residual feature risk; full file workflow remains separately gated |
| TM-20 | Steal or alter a backup | Checksums and isolated restore tooling exist | Encryption/key custody and real repository controls remain operations/deployment evidence |
| TM-21 | Supply-chain package with known advisory | Lock files and NuGet advisory scan over direct/transitive graph | Current graph: no known vulnerable package; repeat in CI under SEC-04 |
| TM-22 | Malicious or compromised desktop update | No uncontrolled auto-update path in current source | Signed installer/update channel remains a later release control |

## Residual and deployment risks

The review accepts no open application-code Critical or High finding in its scope. These risks do
not become accepted merely because the review is complete:

- `SEC-03` remains a hard release blocker until the real reverse proxy, TLS 1.2+ policy, corporate
  CA issuance/rotation, firewall allowlist, DB transport and mounted-secret ACLs are evidenced.
- `SEC-04` remains responsible for recurring secret/dependency/image scanning in CI. This review
  provides a reproducible one-shot dependency and source gate, not a scheduled CI control.
- malware running as the Windows user can access that user's visible data; DPAPI does not protect
  against a process already executing as that user.
- an OS/DB administrator and the backup-key custodian remain powerful trusted roles and require
  organizational separation, monitoring and tested recovery procedures.
- the final penetration pass must be repeated against the stabilized customer-like deployment;
  source/TestServer evidence cannot prove firewall, CA, proxy or physical backup controls.

Any new public endpoint, browser client, external identity provider, internet exposure, file-byte
transport or offline-write mode invalidates the affected portions of this model and requires a
delta review before release.
