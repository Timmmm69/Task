# Portable release verification, 2026-10-06

Deliverable: `outputs/20261006_task_portable_release_1.0.7`.
Source: `4e679712e4cd4d8205fcb3923f72906ea17d4d81`; artifact publication is a later
commit resolved by Git history for its manifest. The ZIP remains a candidate
while the mandatory Windows environment/popup requirements are not accepted.

The final Release gate used real isolated PostgreSQL 16 and 15.19 through the
existing `work/acceptance_followup_1/Run-FullGate.ps1` and
`work/final_acceptance_stage6/Run-Gate.ps1`. Each of the 54 baseline database
scenarios is enumerated in `Db-Scenarios.json`; `Verify-DatabaseCoverage.ps1`
reads TRX paths literally and rejects missing, duplicated, skipped or NOT RUN rows.

The native probe is built from `work/final_acceptance_stage6/Probe/Probe.csproj`.
`Test-Delivery.ps1` runs the exact verified ZIP's EXE using disposable roots and
then the established replacement/persistence harness. The replacement harness
needs the preserved previous client ZIP at its documented local path; it is not
bundled into this release. `Test-CorporateDelivery.ps1` requires the existing real
isolated Task E2E fixture. It creates a separate client root, authenticates through
real HTTPS, protects the saved session with Windows DPAPI, verifies marker isolation
and native session restore, then stops the API. It never prints or packages tokens.

`Finalize-Delivery.ps1` requires successful CI for the exact source SHA, copies
sanitized factual receipts, records the portable dependency closure, and creates
the full document inventory/hash list. `Verify-DeliveryPackage.ps1` independently
reopens the ZIP, verifies payload/doc hashes, validates exact tested EXE/source/version
binding and checks receipt contents for known fixture credentials/private keys.

The application-only ZIP has seven exact runtime files. The extracted `Task/`
directory is retained locally and excluded from Git; its authoritative distribution
is the ZIP. Package `.gitattributes` disables text normalization so receipt hashes
remain valid after cloning on other platforms. Runtime data, binaries/tool caches,
fixture state/keys and earlier experimental packages are not published.

Actual Linux builds/runtime integration and Trivy are provided by the unchanged
existing CI pipeline. The local Docker Desktop engine remains unavailable.
Final CI is also checked on the artifact publication commit. Dashboard changes
preserve historical evidence, close the proven container baseline and keep current
portable readiness blocked; the official order/validation commands are required.
