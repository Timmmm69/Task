# Task read vertical slice 0.1.0

Пакет фиксирует проверенный результат `PRODUCTION-TASK-READ-VS-01`, increment D:
WPF-раздел «Задачи» читает список и карточку из PostgreSQL через существующие
авторизованные HTTPS Task API и desktop session stack.

Implementation commit:

- `868906a857c37044e8dd31641705933b19f74f22`

Автоматические gates и реальный synthetic PostgreSQL 16 + HTTPS API + WPF E2E
пройдены. MOD-005 этим пакетом не объявляется завершённым. Подробности и
ограничения приведены в `validation-report.md`.

Проверка целостности:

```powershell
powershell -ExecutionPolicy Bypass -File .\Verify-Manifest.ps1
```
