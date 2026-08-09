# Gate 5.6 Windows machine runbook

## Exact executable

Use `bin\Task-Gate-5.6-Client-0.1.0-win-x64.exe` from this package. Before execution, compare its SHA-256 with `ARTIFACT.sha256`. This is a portable unsigned x64 executable and does not require installation or administrator rights. Do not rename or replace it after recording evidence.

## Synthetic test accounts

| Role | Login | Password | Launcher | Effective local scope |
|---|---|---|---|---|
| Admin | gate.admin | Task-Gate-Local-2026! | launch\Run-As-Admin.cmd | Task write + Admin + Operations |
| Manager | gate.manager | Task-Gate-Local-2026! | launch\Run-As-Manager.cmd | Task/team write; no Admin/Operations |
| Employee | gate.employee | Task-Gate-Local-2026! | launch\Run-As-Employee.cmd | Task write; no Admin/Operations |
| Observer | gate.observer | Task-Gate-Local-2026! | launch\Run-As-Observer.cmd | Task read-only; no Admin/Operations |

The launcher fixes the selected local account. After signing out, only that selected account can authenticate in that process. The identities and password are synthetic and must not be replaced with real personal data.

## Required Windows tooling

- A real Windows x64 machine and an approved copy of this exact executable.
- Microsoft Inspect.exe from the Windows SDK accessibility tools for UIA inspection.
- Windows Narrator enabled through Windows accessibility settings.
- Real display scaling at 100/125/150/175/200% and the required multi-monitor topology; browser zoom is not a substitute.
- PowerShell `Get-FileHash -Algorithm SHA256` for binary identity.
- The existing `stage_5_6_external_gate_execution_kit` protocols and evidence templates.

## Execution boundary

This package proves only that a compiled client can be built and launched with four synthetic roles. It does not close Gate 5.6. UIA/Inspect properties, Narrator output, actual multi-monitor DPI behavior, moderated sessions and Product/Design/Desktop/QA approvals remain external. The portable build is unsigned and has no real LAN backend; TLS, server sync, production authorization and directory authentication are simulated prototype states.
