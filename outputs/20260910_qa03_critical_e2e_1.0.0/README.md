# QA-03 critical user scenario E2E — package 1.0.0

Реализует roadmap-пункт QA-03 «Все критические пользовательские сценарии имеют E2E»
и закрывает его release-blocker: каждый критический сценарий продукта проверен
сквозным образом на чистом воспроизводимом стенде без подмен основного пути.

## Состав

- `VALIDATION_REPORT.md` — итоговый отчёт проверки.
- `manifest.json` — версия, ревизия исходников, SHA-256 всех файлов.
- `SHA256SUMS` — контрольные суммы пакета.
- `evidence/` — JSON-assertion-ы фаз, логи фаз, скриншоты реального Release WPF.

## Гейт

`work/production/verification/Test-Qa03Gate.ps1 -EvidenceDirectory <outputs>/evidence`
строит Release solution, поднимает свежий PostgreSQL 16 (schema 14) и production HTTPS
API, запускает product-фазы `Test-Qa03CriticalE2E.ps1`, прогоняет native UI Automation
по Release WPF, проверяет восстановление после потери сервера и конфликта версий,
перезапускает API и подтверждает персистентность, затем полностью убирает стенд.

Полная матрица сценариев: `work/production/docs/QA-03-critical-e2e-matrix.md`.
