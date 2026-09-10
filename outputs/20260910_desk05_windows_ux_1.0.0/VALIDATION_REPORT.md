# DESK-05 validation report

Result: **PASS**

## Verified

- Release solution build: PASS, 0 errors and 0 warnings.
- Focused source/layout tests: 7/7 passed.
- Native Windows checks: 91/91 passed.
- Host: Microsoft Windows NT 10.0.26200.0, X64, 2560×1600.
- Native WPF DPI: 144 (150%); manifest: PerMonitorV2.
- Authentication and main-window logical viewport equivalents: 100/125/150/200%, all PASS.
- UI Automation Value/Invoke/Selection, accessible names and visible bounds: PASS.
- Keyboard Tab and F6 navigation cycle: PASS.
- Narrator active during named focus-target traversal: PASS.
- Eight PNG files were visually reviewed after the automated bounds and rendered-surface checks: PASS.
- Isolated PostgreSQL/API/desktop-data setup and cleanup: PASS; user Desktop state was not used.

## Evidence boundary

The Narrator run proves compatibility, names and focus traversal; spoken wording was not audio-transcribed.
The host monitor was physically at 150%. Other scale values were exercised as logical viewport equivalents
on the same native PerMonitorV2 WPF windows. A mixed-physical-monitor check remains a deployment smoke for
the particular workstation fleet and does not block DESK-05 implementation completion.
