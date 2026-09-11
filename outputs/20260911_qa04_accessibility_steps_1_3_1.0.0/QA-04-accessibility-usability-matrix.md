# QA-04 — матрица нативной доступности и usability

Статус: шаги 1–3 реализованы и автоматизированы. Внутренний usability review,
разбор findings и итоговый release sign-off выполняются следующими шагами QA-04.

## Зафиксированный scope

- Платформа: self-contained Windows x64 Release WPF на контролируемом Windows 11 стенде.
- Данные: только детерминированный профиль `qa03-deterministic-v1`; данные заказчика запрещены.
- Backend: свежий PostgreSQL 16, production migrator, локальный HTTPS API и production worker.
- Масштаб: 100%, 125%, 150% и 200% как логические viewport-эквиваленты на нативном
  `PerMonitorV2` окне; физическая mixed-monitor конфигурация остаётся deployment smoke.
- Ввод и автоматизация: клавиатура, Windows UI Automation, стабильные AutomationId,
  доступные имена, состояния и поддерживаемые Value/Invoke/Selection patterns.
- Экранные дикторы не входят в acceptance scope продукта Task.

## Матрица шагов 1–3

| ID | Объект | Конфигурация | Критерий PASS | Evidence |
|----|--------|--------------|---------------|----------|
| ENV-01 | Release artifact | `Task.Desktop.exe`, конфигурация `Release` | Реальный executable существует; путь, размер и SHA-256 записаны | `qa04-accessibility.json` |
| ENV-02 | Чистый стенд | PostgreSQL 16 + migrator + HTTPS API + worker | Setup и Cleanup успешны, Desktop AppData восстановлен | `qa03/phase-setup.stdout.log`, `qa03/phase-cleanup.stdout.log` |
| DATA-01 | Синтетический профиль | `qa03-deterministic-v1` | Профиль записан в QA-03 и QA-04 JSON; реальные данные не используются | `qa03/ui-assertions.json`, `qa04-accessibility.json` |
| UIA-01 | Окно подключения и входа | Release WPF | Поля и действия видимы в UIA, имеют имена и нужные Value/Invoke patterns | `desk05/windows-ux.json` |
| UIA-02 | Основное окно | Release WPF | Навигация, команды, список, статус и содержимое видимы; Selection/Invoke доступны | `desk05/windows-ux.json` |
| KEY-01 | Форма входа | Клавиатура | `Tab` переводит фокус на следующее доступное действие | `desk05/windows-ux.json` |
| KEY-02 | Основное окно | Клавиатура | `F6` проходит цикл «навигация → команды → содержимое → навигация» | `desk05/windows-ux.json` |
| SCN-01 | Сегодня и задачи | Release WPF UIA + synthetic seed | Отображение, создание, конфликт и восстановление — PASS | `qa03/ui-assertions.json` |
| SCN-02 | Календарь и проекты | Release WPF UIA + synthetic seed | Данные отображаются, проект создаётся — PASS | `qa03/ui-assertions.json` |
| SCN-03 | Каталог, контакты и поиск | Release WPF UIA + synthetic seed | Создание и поиск доступны через реальные UI controls | `qa03/ui-assertions.json` |
| SCN-04 | Уведомления | Production worker + Release WPF UIA | Доставка, отображение и прочтение — PASS | `qa03/ui-assertions.json` |
| SCN-05 | Потеря сети и возврат | Остановка/запуск production API | Запись блокируется offline и восстанавливается после явного refresh | `qa03/ui-assertions.json` |

## Воспроизведение

```powershell
pwsh -NoProfile -File work/production/verification/Test-Qa04AccessibilityGate.ps1
```

Gate последовательно создаёт чистый QA-03 стенд, выполняет критические сценарии через
реальный Release WPF, полностью очищает runtime, затем на новом изолированном стенде
проверяет клавиатуру, UIA patterns, доступные имена и viewport-матрицу. Итоговый JSON
создаётся только после проверки обоих независимых наборов evidence.

## Граница текущего инкремента

Шаги 1–3 считаются выполненными, когда `qa04-accessibility.json` имеет `result: PASS`,
оба дочерних gate завершились успешно, а обязательные UIA и keyboard assertions присутствуют.
Это не закрывает QA-04 целиком: ручной usability review, disposition замечаний и внутренний
release sign-off ещё не выполнены.
