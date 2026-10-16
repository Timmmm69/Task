# Task 8.1–8.3 — финальный review 8.3.1

Дата: 2026-10-16. База интеграции: origin/main deb16b3.
Изменения перенесены в чистую изолированную копию без конфликтов.
Проверено отсутствие изменений API, application, infrastructure и desktop Task API client.
Новые дефекты в production diff не найдены; выполнен независимый read-only review.

| Этап | Результат для публикации | Исходный коммит | Интеграционный коммит |
|---|---|---|---|
| 8.1 | Явная порционная загрузка, отдельное состояние, cancellation/access/session guards | eb1044d | 2958ed5 |
| 8.2 | Контрактный proposal PROPOSED — NOT APPROVED | 4829e95 | 98c2326 |
| 8.3 | Отчёт BLOCKED_CONTRACT_APPROVAL; серверная фильтрация отсутствует | локальные артефакты | публикация этим review |

## Проверки

- Release solution build PASS: 0 ошибок, 0 предупреждений.
- TaskReadCursorCodecTests: 4/4 PASS.
- TaskEndpointsTests: 166/166 PASS.
- Полный desktop gate: 1149 PASS, 1 FAIL, 0 SKIP (1150 всего).
- Единственный FAIL: NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow,
  TaskContextActionsUiTests.cs:62, Assert.True(company.Activate()).
  Такой же FAIL получен при отдельном запуске на интегрированной версии и на
  неизменённом origin/main deb16b3. Это существующее ограничение активации окна
  в текущей Windows-сессии, не регрессия этапов. Тест не отключался и не изменялся.
  Его последующие mouse/keyboard assertions этим запуском не проверены.
- Остальные 1149 тестов включают final Projects/ViewState, pagination, cancellation,
  access/session/error/refresh и новый native related-task WPF test.
- dashboard:order и dashboard:validate PASS, progress/status не повышены.
- Manifest SHA-256, SHA256SUMS, ZIP CRC и содержимое пакетов проверены.
  Перед commit отдельно проверяются байты файлов в Git index.
- При компиляции тестовых проектов видны прежние xUnit/ASPDEPR004 warnings.

## Исправления пакета 8.3

Добавлена .gitattributes (* binary) в byte-addressed пакет: Git не должен менять
окончания строк и нарушать manifest/SHA-256. Package.py теперь использует обычный
outputs-путь опубликованного пакета 8.1; прежний worktree-путь остаётся fallback
для исходной локальной копии. Скрипт и пакет 8.3 включены в публикацию.

## Границы результата

8.1 улучшает отзывчивость карточки проекта, но полный набор задач по-прежнему
требует явного обхода страниц организации. 8.2 не утверждён публикацией в main.
8.3 не реализует SQL project predicate, новый query parameter или cursor v2.
Нормативное разрешение C1–C5 не получено; данные, migrations и permissions не менялись.
Live PostgreSQL/EXPLAIN и серверные performance benchmarks не запускались.
Числа 200 запросов против 1 для N=10000/M=10 — оценка будущего контракта, не замер.
Отчёты 8.1/8.2/8.3 описывают состояние на момент их первоначального аудита;
этот review фиксирует последующую интеграцию для публикации.

Для данных изменений готовность к публикации подтверждена с указанной baseline UI
оговоркой. Готовность серверной фильтрации и Definition of Done 8.3 не подтверждены.
Следующий шаг продукта — решение по C1–C5 и реализация согласованной серверной выборки.
