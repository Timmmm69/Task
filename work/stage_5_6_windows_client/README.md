# Task Stage 5 Windows client

This directory contains the minimal compiled Windows wrapper for the existing Stage 5 React prototype. It does not introduce a second product or a backend.

Build from the repository root on Windows with:

```powershell
powershell -ExecutionPolicy Bypass -File work\stage_5_6_windows_client\build.ps1
```

The portable artifact is written to `dist\Task-Gate-5.6-Client-0.1.0-win-x64.exe`. Select a synthetic role with `--gate-account=admin`, `manager`, `employee`, or `observer`.

This client is an unsigned Gate prerequisite. It is not evidence that UIA, Narrator, DPI, participant sessions, or owner approvals passed.
