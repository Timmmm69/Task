# OPS-05 validation report — 1.0.0

Result: SOURCE / SIGNED WINDOWS ACCEPTANCE PASS. Base commit: bedc70434471933ab196460f5bc93cd12beb4941.

The Windows x64 gate built self-contained 1.0.0 and 1.1.0 clients with an ephemeral in-memory
code-signing publisher. It verified Authenticode publisher pins, detached signatures, every SHA-256,
idempotent initial install, mandatory update overriding a 0% rollout, rejection of a local channel
without the explicit acceptance switch, tamper rejection, direct-downgrade rejection and atomic
rollback to the retained validated release. No test certificate was written to a certificate store.

The full Release solution passed 1668 tests with zero failures; 4 PostgreSQL integration
tests were skipped because TASK_TEST_POSTGRES is not configured. Assemblies: [{"assembly": "task.tests", "passed": 798, "skipped": 4, "file": "ops05-full-release_net10.0_20260910184019.trx"}, {"assembly": "task.desktop.tests", "passed": 298, "skipped": 0, "file": "ops05-full-release_net10.0_20260910184021.trx"}, {"assembly": "task.servicehosts.tests", "passed": 572, "skipped": 0, "file": "ops05-full-release_net10.0_20260910184607.trx"}].

Production boundary: the repository contains no private signing key and no customer URL. The final
operator must use the corporate code-signing certificate and RFC 3161 timestamp service, publish the
ZIP and signed channel on a controlled HTTPS origin, distribute OS trust through company PKI and
retain pilot-PC antivirus/launch/update/restart/rollback evidence as described in the runbook.
