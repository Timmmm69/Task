# Validation report — Task 14.4 v1.0.0

Result: **PASS_WITH_LIMITATIONS**, source `18a763d50b6a600743259eaca75dd98b62225c23`.

- Full suites: **3395 PASS / 1 FAIL / 0 SKIP**. Full Desktop source snapshot is 89d4142; last compact viewport/focus refinement is verified by the scoped acceptance on 18a763d50b6a600743259eaca75dd98b62225c23. One unchanged foreground-dependent native case fails at company.Activate(), also reproduced on e9b08b6.
- Last acceptance: **197 PASS / 0 FAIL / 0 SKIP**, including real PG16 and native WPF return with hit-testing at four supported logical window sizes.
- Release solution build: 0 errors; build logs included.
- Six implementation defects fixed; business requirements and HTTP API schemas unchanged.
- Dashboard order/validation PASS; only DESK-02/API-02 evidence/note updated; progress/status preserved.
- Prior packages: 44 SHA256SUMS checks PASS, source overlays compared to their commit.
- Current package: manifest and SHA256SUMS enumerate every payload; source overlays match exact Git blobs; ZIP CRC and member hashes must pass before publication.
- Git apply checks run after packaging against untouched e9b08b6 baseline and reverse cumulative patch against current source; receipt is stored next to this package in outputs.

Draft PR is reviewable. Merge is not recommended until the native foreground gate is green or its environment limitation is explicitly accepted. Live LAN/multi-client, physical mixed-DPI and actual screen-reader walkthrough remain unverified. See Task_14_Final_Independent_Audit.md for the full decision and defect register.
