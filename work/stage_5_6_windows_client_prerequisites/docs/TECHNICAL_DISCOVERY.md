# Compiled Windows client discovery — 0.1.1

## Decision

Electron is the minimal viable path for this repository. The client packages the exact production build from `work/stage_5_prototype`; it does not recreate the Stage 5 design. The current machine has Node.js but no .NET SDK or Rust toolchain. Electron therefore produces a real Windows executable without introducing an unverified parallel UI.

## Options considered

| Option | Current feasibility | Decision |
|---|---|---|
| Electron portable x64 | Node toolchain present; reuses React/Vite output; Chromium exposes web semantics through Windows accessibility APIs | Implemented |
| WebView2 + .NET/WinUI | .NET SDK absent; requires a technology/toolchain decision and new host implementation | Future candidate |
| Tauri + WebView2 | Rust/Cargo absent; adds a second toolchain and host implementation | Not selected |

## Risks and boundaries

- The executable is unsigned. Windows SmartScreen or enterprise policy may block it until code signing/distribution is supplied.
- Chromium/Electron UIA exposure is plausible but is not claimed as verified; Inspect and Narrator must run externally.
- The fixture is local synthetic data, not a backend, directory service, production authentication, authorization evidence, or participant evidence.
- Portable packaging is suitable for Gate execution, not a final enterprise deployment choice. MSI/MSIX, code signing, update policy, endpoint allow-listing and support ownership remain open.
- The production bundle retains the existing non-blocking >500 kB chunk warning.
