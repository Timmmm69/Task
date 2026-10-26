# Проверка PR №16 — 1.0.0

Дата: 26.10.2026 (Europe/Minsk). PR: https://github.com/Timmmm69/Task/pull/16.

Решение: блокирующих регрессий изменений PR не найдено; разрешено слияние в main с явно зафиксированными существующими проблемами базы. Полный CI не объявляется зелёным.

Проверен исходный HEAD `07db1a7a190e33aefe25c446787117c731cf92c3` и его локальная интеграция `bd4813f` с актуальным `origin/main` `cc2933a690d37323b73f64a74ecd19be96be8dac`. Интеграция без конфликтов. Рабочая копия и временные материалы: `work/pr16_review_20261026/`. Чужие untracked-файлы основной рабочей копии сохранены.

## Изменения и review

Просмотрены оба изменённых XAML и тест native WPF, сопоставлены исходные привязки, команды, AutomationId, бизнес-модель CalendarEvent и визуальные приёмы TodayView. Изменения ограничены оформлением формы и проверкой интерфейса. API/DTO, БД, права и бизнес-правила не изменены. Для all-day события модель по-прежнему использует одну EventDate; отключение времени/EndDate соответствует текущему TryBuild.

XML, уникальность свойств, сохранение исходных binding/command/automation contracts, native template parts — PASS. Manifest и все 9 SHA256SUMS активного пакета PR 1.1.0 проверены по Git blobs — PASS. Старый пакет 1.0.0 является исторической версией и не используется как manifest текущих исходников.

## Проверки Windows

- `dotnet build Task.sln -c Release`: PASS, 0 ошибок, 11 существующих предупреждений; SDK 10.0.400.
- Calendar filter: 143 PASS, 0 FAIL/SKIP, включая NativeCalendarUsesSharedPickerAndContactFallback.
- Полный Desktop: 1917 PASS, 0 FAIL/SKIP.
- ServiceHosts: 671 PASS, 0 FAIL/SKIP.
- Domain/integration с отдельным PostgreSQL 16.14: 914 PASS, 0 FAIL/SKIP. Итого 3502 уникальных теста. Календарный фильтр входит в Desktop и не прибавляется повторно.
- Первоначальный запуск решения без TASK_POSTGRES_TEST_ADMIN_CONNECTION закономерно имел 33 ошибки/22 пропуска в DB-тестах. Повтор всего Task.Tests с поднятой изолированной PostgreSQL 16 завершился без ошибок/пропусков; этот первоначальный запуск не выдан за зелёный.
- NuGet dependency audit, project boundaries, независимый static security gate и production secrets/TLS contract: PASS. Security gate запускался с SkipRestore/NoBuild/SkipTests после отдельной сборки, аудита и тестов.
- `git diff --check`: PASS.

## Интерфейс

Использованы настоящие production CalendarView/CalendarViewModel и существующие синтетические API/directory fixtures; это изолированный WPF probe, а не подключение к рабочему корпоративному серверу. Интерфейс также осмотрен через computer-use, включая раскрытие нового списка статуса.

Матрица из 8 сценариев: Corporate/Personal × New/Edit × 1120×900/900×700 DIP. Измерены реальные layout bounds; футер находится в доступной области во всех сценариях. В Corporate участники переходят вниз на узком окне; в Personal они скрыты, вторая колонка нулевая. Получены 24 RenderTargetBitmap PNG с плотностью 100/150/200%. Представительные кадры осмотрены визуально: читаемая иерархия, ровные панели/разделители, корректные выбранные checkbox, доступный футер.

Реальный монитор работает при 150% DPI. PNG 100%/200% — дополнительные raster-density renders, а не утверждение о запуске на мониторах с системным масштабом 100%/200%. High contrast и переход между мониторами не проверялись.

Native regression проверяет DatePicker/PART_Button/PART_Popup/SelectedDate, сохранение введённого времени при all-day, dropdown статуса, общий Employee Picker, добавление сотрудников и контактов, UUID fallback, смену User.Read и offline. Отдельно WPF keyboard probe подтверждает выбор статуса стрелкой и переход фокуса внутри редактора.

## Существующие проблемы базы

1. `dotnet format whitespace Task.sln --verify-no-changes --no-restore` возвращает 2. Контрольный запуск на исходных CalendarView и CalendarPickerWindowsTests из актуального main выдаёт **побайтно идентичный** лог: 672 диагностики. PR не добавляет ошибок форматирования. GitHub CI main run 40250515407 и PR run 40246461956 красные на этом gate; зависимые PostgreSQL/container jobs не выполнялись. Локальные результаты не выданы за успешный GitHub CI. Массовое форматирование несвязанных файлов не включено в UI PR.

2. Escape при открытом списке статуса закрывает весь редактор. Наблюдение сделано вручную и подтверждено одинаковым WPF PreviewKeyDown probe на PR и исходном main: editorRetained=false, IsDropDownOpen=false. Это существующее поведение OnEditorPreviewKeyDown, код которого PR не меняет; отдельное UX-улучшение, не регрессия PR.

Container packaging и полный live corporate E2E не запускались: PR не меняет серверные/контейнерные компоненты. Dashboard readiness/progress не повышались: изменение косметическое и не расширяет функциональную готовность.

## Артефакты

`validation.json` — результаты и ограничения; `source-manifest.json` — SHA-256 проверенных Git blobs; `manifest.json` и `manifest.sha256` — SHA-256 каждого файла пакета; PNG/metrics/logs/TRX — фактические результаты. Исходники probe и независимой проверки приложены для воспроизводимости. Скрипты запускаются из исходной review-папки; их ProjectReference указывает на её repo/work/production.

## Повторная интеграция после паузы

За время паузы main продвинулся до `286ee6f4d891655d8f19c8bb61ed34807182f409` (PR №15). Выполнена повторная интеграция `036165ba3d6b388573c8bf63d9697935641fc24d` без конфликтов. CalendarView/Calendar.Forms, CalendarViewModel и зависимости в main не изменялись между базами. Повторная Release-сборка всего решения — PASS. Полный Desktop на последней интеграции: 1918 PASS, 0 FAIL/SKIP; календарные тесты входят в этот набор. Серверные исходники и domain/endpoint tests между интеграциями не менялись; для них применимы уже полученные 914 PostgreSQL и 671 ServiceHosts PASS. Полный актуальный набор: 3503 уникальных PASS. Повторный formatter: 665 диагностик; контроль на актуальном main даёт идентичный результат после нормализации переводов строк. PR не добавляет ошибок.

Пакет проверяет текстовые SHA-256 с нормализацией UTF-8/LF (Verify-Package.ps1), PNG — побайтно; это сохраняет проверяемость при Git core.autocrlf=true. ZIP дополнительно имеет SHA-256 точных байтов.
