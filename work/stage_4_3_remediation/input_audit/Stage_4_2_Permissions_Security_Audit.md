
# Stage 4.2 — Permissions and Security Audit

**Вердикт области:** FAIL из-за cross-artifact High/Medium, при этом отдельного неизвестного permission/error не найдено.

## Recount

- Permission catalog: **91**.
- Unknown permission codes: **0**.
- Stable errors: **44**.
- Unknown stable errors: **0**.
- API operations with FR/AC mapping: **244/244**.
- DTO field catalog: **1 340** rows; entity catalog: **66** rows.

## Confirmed controls

- Server-side authorization is consistently stated as enforcement boundary; hidden/disabled is presentation only.
- Search filtering/redaction/blocked policy precede pagination in Stage 2.3.1 and the 4.1.2 addendum.
- Settings urgency reads use `Settings.ReadOwn`; writes/reset use `System.Configure`.
- `User.Block` is reused only for blocked-employee visibility; no new permission is invented.
- If-Match/ETag, idempotency, draft preservation, no offline write queue and sensitive audit requirements are present.

## Validation boundary

OperationId set coverage and targeted semantic checks are confirmed. Полное независимое воспроизведение всех 1 340 DTO field constraints и выполнение migration SQL на PostgreSQL не проводились и не объявляются PASS.

## Material gaps

- AUDIT-4.2-003: stale MOD-014 enum and embedded AC can remove employee search despite server contract.
- AUDIT-4.2-006: 87 permission/error/sync/audit/data requirements lack AC links.
- AUDIT-4.2-012: security/data-loss risks lack owner/trigger/probability.
- AUDIT-4.2-016: analytics retention remains open.

## Conclusion

No evidence of an invented permission or stable error was found. Security implementation is not approval-ready until the normative contradictions and verification gaps are remediated.
