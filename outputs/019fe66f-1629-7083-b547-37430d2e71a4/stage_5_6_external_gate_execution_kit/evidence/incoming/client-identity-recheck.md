# Client identity recheck — 2026-08-09

- Baseline: commit `6a16be2fb371d41af0540569c77daf59eb902a9d`; `Task-Gate-5.6-Client-0.1.1-win-x64.exe`; SHA-256 `8B047DD69E1A64269F8961FE0416727E5083E0C2B30285A73DD2E92A2D412E53`.
- The recheck started from that exact commit on a separate branch.
- Fix: authentication IPC existed but the wrapper loaded the prototype directly, so first connection had no Login or Password controls. Version 0.1.2 loads an accessible sign-in screen before the existing renderer.
- Rebuilt EXE: `Task-Gate-5.6-Client-0.1.2-win-x64.exe`; SHA-256 `7E0B7439975E8009A51A0DBB4865D5AD1DFCD9EFEA6B0C93A4141F57845DFA9F`.
- Technical evidence only; no owner acceptance is asserted.
