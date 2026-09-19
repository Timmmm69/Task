# Validation report — Direction 2 search, notifications and lifecycle

Version: 1.0.0

Date: 2026-09-19

Result: **PASS**

## Implemented

- Канонический глобальный поиск и modal overlay с Ctrl+K, Escape, стрелками и Enter.
- Permission-safe группировка, подсветка совпадений и redaction без раскрытия скрытых объектов, количества или связей.
- Центр уведомлений с unread-маркером, critical/warning/default срочностью, фильтрацией и массовым прочтением.
- Архив и корзина с единым inspector, retention/hold-информацией, фильтрами и доступным восстановлением.
- Empty, loading, offline, limited-role и server/error feedback states.
- UIA names/ids, focus return и layout, устойчивый к Windows scaling.

## Verification

- `dotnet build Task.sln -c Release --no-restore`: PASS, 0 errors; 10 existing test-code warnings (`xUnit1031`, `ASPDEPR004`) outside this change scope.
- `dotnet test Task.sln -c Release --no-build --no-restore`: PASS — Desktop 336, core 815, service-host 590; всего 1741 passed, 4 skipped, 0 failed.
- `Test-Direction2SearchLifecycle.ps1`: PASS на изолированных PostgreSQL 16 + HTTPS API + Release WPF; normal, overlay, offline retained-results и limited-role redaction.
- Визуально проверены все четыре скриншота против frozen Direction 2 baseline; защищённые названия, совпадения, количество и связи в limited-role не показаны.
- `git diff --check`: PASS.

## Evidence

- `evidence/normal.png`
- `evidence/normal-overlay.png`
- `evidence/offline.png`
- `evidence/limited-role.png`
- `evidence/validation.json`

E2E cleanup остановил API/PostgreSQL, удалил временный runtime и восстановил исходные Desktop AppData.
