# HAND-03 independent validation report — 1.0.0

**Result: PASS**

- Source revision: `6e158c8ce25c8b4040dfa8eaff7eacff6f1bbf39`
- Final manifest SHA-256: recorded in `signature/signature.json` and verified by this gate.
- Signer thumbprint: `0CA60C002A379AC5CF8FAD09FF9E5A02512457B8`
- Checks passed: 148

The verifier recalculated every manifest and SHA256SUMS entry, verified the detached CMS signature and signer pin,
validated the SPDX/license inventory, server reproducibility evidence and OCI digest map, reopened and completely read
the archive when present, validated the source binding, and ran the signed desktop release publisher/hash checks.

Trust boundary: this package uses the internal self-signed validation certificate included in the release. It proves
integrity and single-signer consistency, not corporate publisher identity. Production deployment requires rebuilding
the desktop component with the customer corporate code-signing certificate and RFC 3161 timestamp, then rerunning this gate.
