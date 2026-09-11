# QA-04 final validation report

Result: **PASS_WITH_NON_BLOCKING_FINDINGS**

## Verified

- Steps 5-8 completion gate: PASS.
- Critical UI scenarios: 12/12 PASS.
- DPI cases: 8/8 PASS (auth + main at 100/125/150/200%).
- Focused desktop tests: 7/7 PASS.
- Keyboard Tab/F6 and UIA Value/Invoke/Selection: PASS.
- Findings: 4 total; Critical 0; High 0; Medium 1; Low 3.
- Disposition: accepted 2; deferred 2; open 0.
- Internal release sign-off: APPROVED.
- Host: Microsoft Windows NT 10.0.26200.0, X64, native DPI 144.
- Isolated runtime cleanup and original Desktop AppData restoration: PASS.

## Evidence limits

Narrator is outside the Task acceptance scope and was not executed. The non-native scale values
are logical viewport equivalents on the native PerMonitorV2 host. Physical mixed-monitor transition
remains a deployment smoke check. This package does not claim WCAG certification.

## Integrity

The builder verified every manifest and SHA256SUMS entry, exact package inventory, ZIP CRC,
full ZIP readback and byte-for-byte SHA-256 equality between the directory and archive.
