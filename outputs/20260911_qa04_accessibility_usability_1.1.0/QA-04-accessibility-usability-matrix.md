# QA-04 — матрица нативной доступности и usability

Статус: шаги 1–8 выполнены на контролируемом Windows-стенде. Итоговый результат
`PASS_WITH_NON_BLOCKING_FINDINGS`: блокирующих замечаний нет, четыре наблюдения получили
явный disposition и не препятствуют внутреннему release sign-off.

## Зафиксированный scope

- Платформа: self-contained Windows x64 Release WPF на контролируемом Windows 11 стенде.
- Данные: только детерминированный профиль `qa03-deterministic-v1`; данные заказчика запрещены.
- Backend: свежий PostgreSQL 16, production migrator, локальный HTTPS API и production worker.
- Масштаб: 100%, 125%, 150% и 200% как логические viewport-эквиваленты на нативном
  `PerMonitorV2` окне; физическая mixed-monitor конфигурация остаётся deployment smoke.
- Ввод и автоматизация: клавиатура, Windows UI Automation, стабильные AutomationId,
  доступные имена, состояния и поддерживаемые Value/Invoke/Selection patterns.
- Экранные дикторы не входят в acceptance scope продукта Task; этот итог не является заявлением
  о полной совместимости с Narrator или WCAG.
- Внутренний usability review выполнен без участия заказчика по свежим screenshots того же run.

## Матрица шагов 1–4 (готовый baseline)

| ID | Объект | Конфигурация | Критерий PASS | Evidence |
|----|--------|--------------|---------------|----------|
| ENV-01 | Release artifact | `Task.Desktop.exe`, `Release` | Путь, размер и SHA-256 записаны | `evidence/qa04-accessibility.json` |
| ENV-02 | Чистый стенд | PostgreSQL 16 + migrator + HTTPS API + worker | Setup/Cleanup успешны, AppData восстановлен | `evidence/qa03/phase-setup.stdout.log`, `phase-cleanup.stdout.log` |
| DATA-01 | Синтетический профиль | `qa03-deterministic-v1` | Реальные данные не используются | `evidence/qa03/ui-assertions.json` |
| UIA-01 | Окно входа | Release WPF | Имена, видимость, Value/Invoke | `evidence/desk05/windows-ux.json` |
| UIA-02 | Основное окно | Release WPF | Имена, состояния, Selection/Invoke | `evidence/desk05/windows-ux.json` |
| KEY-01 | Форма входа | Клавиатура | `Tab` достигает следующего доступного действия | `evidence/desk05/windows-ux.json` |
| KEY-02 | Основное окно | Клавиатура | `F6`: навигация → команды → содержимое → навигация | `evidence/desk05/windows-ux.json` |
| SCN-01 | Сегодня и задачи | Release WPF UIA | Отображение, создание, конфликт, восстановление | `evidence/qa03/ui-assertions.json` |
| SCN-02 | Календарь и проекты | Release WPF UIA | Данные отображаются, проект создаётся | `evidence/qa03/ui-assertions.json` |
| SCN-03 | Каталог, контакты и поиск | Release WPF UIA | Создание и поиск проходят через реальные controls | `evidence/qa03/ui-assertions.json` |
| SCN-04 | Уведомления | Worker + Release WPF UIA | Доставка, отображение и прочтение | `evidence/qa03/ui-assertions.json` |
| SCN-05 | Потеря сети и возврат | Stop/start API | Offline блокирует запись, refresh восстанавливает её | `evidence/qa03/ui-assertions.json` |
| AT-01 | Narrator | Решение о scope | Не входит в acceptance; UIA не подменяется Narrator evidence | Этот документ, раздел «Ограничения» |

## Шаг 5 — DPI-матрица

| ID | Поверхность | Масштаб | Критерий PASS | Result |
|----|-------------|---------|---------------|--------|
| DPI-AUTH-100 | Первое подключение | 100% | Поля и действия не обрезаны | PASS |
| DPI-AUTH-125 | Первое подключение | 125% | Поля и действия не обрезаны | PASS |
| DPI-AUTH-150 | Первое подключение | 150% | Поля и действия не обрезаны | PASS |
| DPI-AUTH-200 | Первое подключение | 200% | Вертикальная прокрутка доступна, действия достижимы | PASS |
| DPI-MAIN-100 | Задачи | 100% | Навигация, команды, список и detail pane доступны | PASS |
| DPI-MAIN-125 | Задачи | 125% | Навигация, команды, список и detail pane доступны | PASS |
| DPI-MAIN-150 | Задачи | 150% | Адаптивный detail collapse сохраняет управление | PASS |
| DPI-MAIN-200 | Задачи | 200% | Навигация прокручивается, критические команды доступны | PASS |

Источник: `evidence/desk05/windows-ux.json` и восемь PNG `auth-*`/`main-*`.
Автоматическая проверка подтверждает допустимые размеры окон, границы критических элементов,
отсутствие clipping/overlap для них и существование непустых screenshots. Визуальный review
подтвердил ожидаемый reflow и появление прокрутки на 200%.

## Шаг 6 — внутренний usability review

| ID | Сценарий | Evidence | Успешность | Затруднения/неоднозначность |
|----|----------|----------|------------|----------------------------|
| US-01 | Сегодня | `qa03/today.png` | PASS | Нет блокирующих |
| US-02 | Создание задачи | `qa03/tasks-created.png` | PASS | Нет блокирующих |
| US-03 | Конфликт и восстановление | `tasks-conflict.png`, `tasks-recovered.png` | PASS | Сообщение заметно, восстановление успешно |
| US-04 | Календарь | `qa03/calendar.png` | PASS | Длинные synthetic titles плотно переносятся |
| US-05 | Проекты | `qa03/projects.png` | PASS | Owner отображается как UUID |
| US-06 | Каталог и контакты | `qa03/catalog.png`, `contacts.png` | PASS | Созданные записи явно подтверждены |
| US-07 | Поиск | `qa03/search.png` | PASS | После успеха виден устаревший validation hint |
| US-08 | Уведомления | `qa03/notifications.png` | PASS | Состояние read подтверждено |
| US-09 | Offline/reconnect | `qa03/offline.png`, `reconnected.png` | PASS | Причина блокировки и восстановление понятны |

Полный разбор: `QA-04-usability-findings.md` и `QA-04-findings.csv`.

## Шаг 7 — findings и retest

- Всего findings: 4; Critical/High: 0; Medium: 1; Low: 3.
- Disposition: `accepted` — 2, `deferred` — 2; без `open` и без блокирующих замечаний.
- Production-код в рамках QA-04 не менялся, поэтому fix-specific retest не требовался.
- Полный свежий regression run повторно прошёл 12/12 критических UI-сценариев,
  8/8 DPI cases, 7/7 focused tests, keyboard/UIA gate и cleanup.

## Шаг 8 — итоговое подтверждение

Финальный пакет содержит эту матрицу, raw logs, UIA/DPI JSON и PNG, usability review,
реестр findings, внутренний `RELEASE_SIGN_OFF.md`, `VALIDATION_REPORT.md`, `manifest.json`,
`VERSION`, `SHA256SUMS`, ZIP и отдельный SHA-256 ZIP. Builder перечитывает ZIP, проверяет CRC,
manifest hashes и полное соответствие checksum inventory.

## Воспроизведение

```powershell
pwsh -NoProfile -File work/production/verification/Test-Qa04AccessibilityGate.ps1 `
  -EvidenceDirectory outputs/20260911_qa04_accessibility_usability_1.1.0/evidence -SkipBuild
pwsh -NoProfile -File work/production/verification/Test-Qa04CompletionGate.ps1
python work/production/verification/Build-Qa04CompletionPackage.py
```

`-SkipBuild` допустим только для повторного UI-run уже проверенного Release artifact; его SHA-256
фиксируется в evidence. Для независимого clean build параметр следует убрать.

## Ограничения

- Фактический монитор стенда: Windows 11, 2560×1600, 150% (144 DPI). 100/125/200% проверены
  как логические viewport-эквиваленты на том же нативном PerMonitorV2 WPF-окне.
- Физический mixed-monitor переход и политики конкретного парка остаются deployment smoke.
- Narrator не запускался и не заявлен как подтверждённый этим пакетом; проверены UIA-контракты,
  но они не являются заменой screen-reader walkthrough.
