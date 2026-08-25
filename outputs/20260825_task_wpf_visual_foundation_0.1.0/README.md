# Task WPF visual foundation 0.1.0

Пакет фиксирует проверенный результат `PRODUCTION-WPF-VISUAL-FOUNDATION-01`:
production WPF shell и экран «Задачи» приведены к Stage 5 Direction 2 на общем
наборе tokens, styles, Fluent icons и state components.

Implementation commit:

- `0e5b5b7c98b9cc1487a2d62c41bc78ab1ee7a506`

Автоматические gate'ы, Release resource/auth smoke и реальный synthetic
PostgreSQL 16 + HTTPS API + production WPF E2E пройдены. Пакет не объявляет весь
desktop UI завершённым и не включает task write-функции.

Основные материалы:

- `validation-report.md` — scope, изменённые файлы и результаты проверок;
- `visual-qa-report.md` — visual gap, матрица viewport и сравнение с baseline;
- `evidence/reference/`, `evidence/before/`, `evidence/after/` — изображения и
  UI Automation trees;
- `evidence/comparison/` — side-by-side comparisons;
- `evidence/accessibility/ui-automation-summary.md` — accessibility/UIA smoke.

Проверка целостности из каталога пакета:

```powershell
powershell -ExecutionPolicy Bypass -File .\Verify-Manifest.ps1
```
