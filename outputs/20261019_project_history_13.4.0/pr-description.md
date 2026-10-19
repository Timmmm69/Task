# Project detail: history and acceptance for stages 13.1–13.4

Adds lazy project history with existing authenticated API, page deduplication/retry, cancellation and stale response guards. Authors resolve through User.Read; forbidden names are scrubbed. History failures remain isolated from other sections.

Project comments remain blocked by ISSUE-13.4-COMMENTS: normative generic object comments are absent from the production API. No server contracts or database changes.

Validation: Release solution build PASS. Final targeted gate: 268 PASS (250 desktop, 14 domain/Postgres, 4 HTTP). Full regression: service hosts 644 PASS, domain 879 PASS/1 corrected test failure, desktop 1780 PASS/1 existing native foreground failure. Corrected history tests passed in final gate. The same Window.Activate failure reproduces on clean d5afec2. Native project WPF tests and screenshots PASS; manual live-server walkthrough NOT RUN. See validation-report.md for receipts and limits. Ready for PR review with these known limits; unconditional final acceptance is open.
