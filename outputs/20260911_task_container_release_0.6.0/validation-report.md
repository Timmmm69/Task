# Container release 0.6.0 — PASS

Source commit: 63edf6f794c3c17c402d9c557978f9ab0a47d969

Two isolated pinned BuildKit builders; locked NuGet restore; SOURCE_DATE_EPOCH and rewritten layer timestamps.
Every OCI blob hash, runtime image label and provenance subject/build parameters is verified before comparison.
Runtime gate consumes the exported images by immutable OCI index IDs. PASS requires the PostgreSQL/hardening gate and cleanup.
Attestations are unsigned build evidence; no registry publication, signing or production deployment is claimed.

Failure: none
