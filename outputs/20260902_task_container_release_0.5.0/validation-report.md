# Container release 0.5.0 — PASS

Source commit: 2a50126b089a533fb78e9b80981a008f3795f0b4

Two isolated pinned BuildKit builders; locked NuGet restore; SOURCE_DATE_EPOCH and rewritten layer timestamps.
Every OCI blob hash, runtime image label and provenance subject/build parameters is verified before comparison.
Runtime gate consumes the exported images by immutable OCI index IDs. PASS requires the PostgreSQL/hardening gate and cleanup.
Attestations are unsigned build evidence; no registry publication, signing or production deployment is claimed.

Failure: none
