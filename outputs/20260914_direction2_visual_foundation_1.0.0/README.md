# Direction 2 visual foundation evidence package

Version: 1.0.0

This package records the production WPF shell alignment with the frozen Direction 2 baseline.

Key evidence:

- `evidence/main-100.png` and `evidence/main-150.png` — requested native comparison captures;
- `evidence/comparison-full.png` and `evidence/comparison-shell.png` — baseline comparisons;
- `evidence/windows-ux.json` — native UIA, keyboard, and 100/125/150/200% matrix;
- `evidence/full-desktop.trx` — complete desktop regression suite;
- `design-qa.md` — visual review and accepted evidence boundary;
- `manifest.json` and `SHA256SUMS` — provenance and integrity.

Recreate native evidence with `work/production/verification/Test-Desk05WindowsUx.ps1`.
Validate this package with `python work/production/verification/Build-Direction2VisualFoundationPackage.py --validate-only`.
