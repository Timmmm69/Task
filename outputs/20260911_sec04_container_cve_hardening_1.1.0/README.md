# SEC-04 container CVE hardening 1.1.0

Final evidence package for the production-image vulnerability gate added to SEC-04.

The implementation builds the existing production container set, records immutable local image IDs, and scans the four deployable images with Trivy. The release fails on any CRITICAL vulnerability, any fixable HIGH vulnerability, an EOL operating system, an incomplete target map, a missing local image, or a scanner/database error. Unfixed HIGH findings remain visible as warnings and JSON evidence.

The validation report records local policy tests and the successful GitHub-hosted weekly and full-CI runs. No vulnerability suppression or remote-registry fallback was introduced.

Files:

- `validation-report.md` — validation scope, results, image identities and reproduction commands.
- `manifest.json` — revisions and SHA-256 inventory of implementation and package files.
- `SHA256SUMS` — integrity hashes for the package.
- `VERSION` — package version.
