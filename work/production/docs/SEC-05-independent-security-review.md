# SEC-05 independent security review

Status: CLOSED for the current source baseline. Product release security is **not** cleared while
`SEC-03` remains blocked.

Review date: 2026-09-06. Scope: current tracked production source, tests, container validation
topology and dependency graph. The review combined an independent read-only code pass, canonical
threat-model reconciliation, negative HTTP tests, dependency-advisory lookup, configuration/secret
scans and static network-boundary assertions.

## Result

One High application finding was discovered and remediated before closure. No open Critical or
High application finding remains in the reviewed source scope. Authentication bypass, broken
object-level authorization, SQL injection and mass assignment were not reproduced in the current
vertical slices.

### SEC05-F-001 — anonymous login resource exhaustion

- Initial severity: High (availability).
- Attack: an unauthenticated LAN peer could submit many unique non-empty login values. The old key
  was `ip|login`, so rotating the login bypassed the ten-attempt threshold. Every value created an
  unbounded in-memory dictionary entry and unknown accounts still performed the timing-equivalent
  64 MiB/three-iteration Argon2id verification. Concurrent requests could exhaust memory and CPU.
- Root cause: no auth-body/field maxima at the HTTP boundary, unbounded limiter cardinality, no
  address/global budget and no cap on simultaneous memory-hard checks.
- Fix: `AuthEndpoints` now reads at most 8 KiB before deserialization and enforces canonical Stage
  2.2 lengths. `LoginAbuseProtector` charges account, socket-address and process-wide windows and
  admits at most two simultaneous password checks. `LoginRateLimiter` fails closed at a bounded
  number of tracked keys and reclaims expired entries.
- Regression evidence: overlong login and oversized body never reach the password hasher; rotating
  accounts from one address is throttled; exhausted password-check capacity returns correlated 429;
  key-cardinality saturation cannot grow state.
- Final status: Fixed and verified.

The remediation follows OWASP API4:2023 guidance to bound payloads/parameters and interaction rate,
and NIST SP 800-63B guidance to rate-limit per subscriber account while using additional source and
risk signals.

## Verified controls

- Fallback authorization is authenticated-by-default; only login, refresh, liveness and readiness
  are anonymous.
- JWT validation is ES256-only and binds issuer, audience, lifetime, key id, server session,
  account state, credential version, scope version and organization.
- Business routes declare permission policies. Current persistence paths bind organization/user
  context and recheck object/field scope in the transaction.
- Foreign and absent objects use the same not-visible result; identity headers and client-supplied
  organization/author fields cannot become request identity.
- Malformed, duplicate/unsupported and oversized request shapes are rejected before mutation in
  covered endpoints; stable Problem Details do not expose exception, SQL, credential or token data.
- Desktop accepts HTTPS endpoints only, uses normal certificate validation, clears credentials on
  endpoint change and protects persistent refresh/device material with DPAPI CurrentUser.
- Container validation exposes the API on loopback only, keeps PostgreSQL on an internal network,
  and applies non-root/read-only/capability-drop/no-new-privileges controls.
- NuGet locked restore succeeds for both solution and linux-x64 publish shapes. The current direct
  and transitive dependency graph has no package reported by NuGet as vulnerable.

## Explicit non-closure of deployment controls

The following is not an open SEC-05 review action; it is an evidenced release blocker owned by
`SEC-03`/operations:

- the API image listens on HTTP behind an expected reverse proxy;
- validation-only DB connections use `SSL Mode=Disable` on an internal container network;
- no customer-like reverse-proxy configuration, internal-CA certificate chain, firewall policy,
  secret-file ACL or rotation execution evidence exists in this source package.

Therefore `SEC-05` may be `done` because the independent review, finding remediation and evidence
are complete, while `GATE-SECURITY` remains blocked by `SEC-03`. A final network penetration pass
must be repeated after deployment stabilization.

## Reproduction

Run from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File work/production/verification/Test-IndependentSecurityReview.ps1
```

The command performs locked restore, dependency advisory review, static source/topology assertions
and the complete solution test suite, then writes evidence under `work/production/evidence/sec05`.
Use `-SkipRestore` only after a successful current locked restore. No customer database or deployed
infrastructure is modified by this review gate.
