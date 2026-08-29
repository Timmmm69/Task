# Task write vertical slice 1.0.0

Итоговый validation-пакет для WPF-сценария записи задач: создание, редактирование, смена статуса, optimistic concurrency, повтор после сетевого сбоя и read-only режим.

Состав:

- `validation-report.md` — итоги автотестов, сборки и проверок;
- `e2e-report.md` — протокол real end-to-end прогона;
- `evidence/` — скриншоты WPF, UI Automation evidence и DB-ассерты;
- `manifest.json` и `SHA256SUMS` — размеры и SHA-256 всех payload-файлов;
- `Verify-Manifest.ps1` — автономная проверка целостности пакета.

Проверка:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\Verify-Manifest.ps1
```
