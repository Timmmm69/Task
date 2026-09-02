# Task container release 0.5.0

Date: 2026-09-02. Result: **PASS**.

- Image source commit: `2a50126b089a533fb78e9b80981a008f3795f0b4`.
- Runtime verification commit: `a01ee0e` (full SHA in `release.json`).
- Platform: `linux/amd64`.
- Manifest and version: `release.json`; source/toolchain: `source.json`.
- Integrity: `SHA256SUMS` covers every package file except itself, using raw file bytes. `.gitattributes` prevents Git line-ending conversions in this package.
- Binary payload: `source.tar` and ten `images/*.oci.tar` files, retained locally and excluded from Git. Copy the whole directory to transfer the complete release.
- Evidence: two independent pinned BuildKit builders, raw build logs/metadata, extracted OCI manifests/configs and SLSA provenance in `evidence/`.
- Exact runtime validation scripts, Compose topology and SQL grants: `runtime-inputs/`. These are bound to the verification commit rather than the earlier image-source commit.

| Deployable image | Reproduced image manifest digest |
|---|---|
| Task.Api | `sha256:b251305ddaba399d48bf0584b2ff8ecf64200b24d3555273e244d8333516ef73` |
| Task.Worker | `sha256:99ba74054cda24a3186fee550b3233e864bd78eb0205d8862822218823fdb901` |
| Task.BackupAgent | `sha256:eb292e2d6ec92e81d2eb2fb49ff15bdfc2fb1f2e359e761a2dfaef9c72c1efa7` |
| Task.DatabaseMigrator | `sha256:6c539e7e17c04e425c1eaa373812141f02b3e7723b492f01a0532b8dfb91f90b` |

The fifth target is validation-only; its digest and archives are included in the machine-readable manifest. `image-map.json` contains first-pass OCI index IDs for Docker's containerd store, which differ from the image manifest digests above because the index also contains provenance.

Validation: all five image manifests/configs/layers match between independent builds; all OCI blobs and provenance bindings were verified. The exported images passed the real PostgreSQL 16, role isolation, current task-write/calendar privileges, health/readiness, task-store concurrency, network and SIGTERM checks. See `evidence/runtime-gate.txt` and `validation-report.md`.

Additional checks: eight OCI verifier tests passed; changed dependencies were rejected by NuGet locked restore with NU1004; two source archives made with explicit commit timestamps had identical SHA-256; PowerShell syntax checks passed. The original saved source archive was checked file-by-file against its Git tree during resumed verification.

Run from the repository root:

```powershell
node work/production/deployment/containers/verify-release.mjs outputs/20260902_task_container_release_0.5.0
```

Provenance is unsigned. Attestation timestamps and enclosing index/archive hashes can differ between builds; reproducibility is asserted for executable image manifests, configs and layers. Production network/TLS, registry/signing, backup implementation and production deployment remain separate work.
