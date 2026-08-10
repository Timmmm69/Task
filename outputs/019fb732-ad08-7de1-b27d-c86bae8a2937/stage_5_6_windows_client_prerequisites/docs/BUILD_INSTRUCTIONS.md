# Reproducible Windows build

## Pinned inputs

- Windows x64
- Node.js 24.18.0 used for this build
- npm 11.16.0 used for this build
- Electron 43.3.0
- electron-builder 26.15.3
- Vite 6.4.2
- Dependency resolution is locked by both package-lock files.

## Command

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File work\stage_5_6_windows_client\build.ps1
```

The script performs clean installs, builds the Stage 5 production client, runs every prototype test, runs desktop fixture tests, and creates `work\stage_5_6_windows_client\dist\Task-Gate-5.6-Client-0.1.1-win-x64.exe`. A repeat build is reproducible from pinned inputs and commands; the SHA-256 recorded below identifies this exact produced binary and is not a claim of bit-for-bit deterministic PE/NSIS output across machines.
