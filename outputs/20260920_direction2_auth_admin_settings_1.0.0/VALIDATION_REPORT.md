# Validation report — Direction 2 auth, bootstrap, settings and administration

Version: 1.0.0

Date: 2026-09-20

Result: **PASS**

## Implemented

- Единый Direction 2 flow первого подключения, входа и отдельного fail-closed bootstrap перед открытием shell.
- Retry/logout при ошибке bootstrap без расширения session/capability контрактов.
- Настройки профиля, уведомлений и server connection в одном responsive settings shell.
- Server-backed read-only представления пользователей, ролей и сетевых ресурсов; capability-gated limited-role state.
- Offline/reconnecting, session revoked/expired, scope changed, maintenance/server unavailable, storage read-only и безопасные error/retry состояния используют существующие auth/connectivity/capability контракты.
- UIA ids/names, live feedback, детерминированный F6 focus cycle, scroll-safe layout и общие forced-colors/high-contrast ресурсы.

## Verification

- Release Desktop build: PASS, 0 warnings, 0 errors.
- Desktop tests: PASS, 340/340.
- DESK-01 auth/security gate: PASS, 1749 tests and 14 mandatory auth scenarios, 0 failed, 0 skipped, isolated PostgreSQL 16.
- Direction 2 native evidence gate: PASS, 10 primary/edge screenshots against real HTTPS API and capability-limited account.
- DESK-05 native Windows gate: PASS, 100/125/150/200%, keyboard Tab/F6, UIA Value/Invoke/Selection, critical controls unclipped.
- Forced-colors: PASS through shared `SystemParameters.HighContrast`/`SystemColors` resources and VisualFoundation coverage.
- `git diff --check`: PASS.

## Evidence

- `evidence/first-connection.png`
- `evidence/bootstrap-failure.png`
- `evidence/bootstrap-progress.png`
- `evidence/settings-profile.png`
- `evidence/settings-notifications.png`
- `evidence/settings-server.png`
- `evidence/admin-users.png`
- `evidence/admin-roles.png`
- `evidence/admin-resources.png`
- `evidence/settings-offline.png`
- `evidence/admin-limited-role.png`
- `evidence/validation.json`
- `evidence/accessibility/windows-ux.json`
- `evidence/accessibility/auth-100.png` through `auth-200.png`
- `evidence/accessibility/main-100.png` through `main-200.png`

Оба нативных E2E-прогона остановили API/PostgreSQL, удалили временный runtime и восстановили исходные Desktop AppData.
