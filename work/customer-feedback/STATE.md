# Состояние серии · этап 1 из 6

Этап 1 добавляет модель месяца и выборку в Personal/Corporate. Следующий этап — месячный экран.
Основание работы — свежий `origin/main` на момент ответвления, `b5b1952`.
Статус интеграции и CI проверять по PR и текущему main. Ниже записаны локальные проверки.

## Реализованный контракт и пути

Все пути к коду и тестам ниже относительны к `work/production/`.
- `src/Task.Domain/TaskCardContent.cs` — `PlanningMonth`, проверка `yyyy-MM`, merge PATCH и `EffectivePlanningMonth`.
- Проекция использует явный месяц → ScheduledDate → DeadlineDate → null («Без месяца»).
- В сохранённом JSON пустой месяц опускается для совместимости в том числе с fingerprint команд Personal.
- `src/Task.Api/Tasks/TaskEndpoints.cs` и `TaskCardFields.cs` — одно поле в POST/PATCH и DTO записи, detail и list.
- `src/Task.Infrastructure/Persistence/PostgresTaskAggregateStore.cs` и `PostgresTaskReadStore.cs` читают и пишут JSONB карточки.
- `src/Task.Desktop/Tasks/DesktopTasksApiClient.cs` читает месяц из DTO. `ViewModels/TaskCardEditor.cs` сохраняет его при пересборке.
- `src/Task.Desktop/Tasks/MonthlyTaskQuery.cs` — общая точка чтения для этапа 2, `GetMonthlyTasksAsync(month, token)`.
- Выборка читает все страницы с ограничениями видимости текущего клиента, включая завершённые и отменённые задачи.
- Ошибка любой страницы возвращает ошибку всей выборки. В Corporate стоимость чтения зависит от числа видимых задач.
- `src/Task.Desktop/Personal/PersonalTaskStore.cs` — создание, PATCH, смена статуса, список и повторное открытие SQLite.

## Совместимость и восстановление

- Personal v6 добавляет nullable TEXT `tasks.planning_month` с CHECK. Миграция 5→6 выполняется в транзакции.
- `PersonalDatabase.cs` создаёт safety copy для старой схемы. `PersonalDatabaseValidation.cs` ожидает новую колонку только с v6.
- Версии 1–5 мигрируют тем же путём при открытии или после восстановления `.taskbackup` формата 1.
- `PersonalBackupService.cs` сверяет версию metadata с реальной схемой. Старым строкам не назначаются месяц или день.
- PostgreSQL остаётся на миграции 020. Поле хранится в `work.tasks.card_content` из миграции 011.
- Проверены production migrator apply/status на PostgreSQL 16 и реальная запись/чтение в migrated database.
- `pg_dump -Fc` и `pg_restore -e` в отдельную БД сохранили две полные строки, с явным месяцем и со старым JSON без месяца.

## Проверки этапа

- Windows, .NET 10, Release. Сборка `dotnet build Task.sln -c Release --no-restore` успешна.
- 22 доменных и интеграционных теста: TaskCardContentTests, TaskPersistenceMigrationHistoryTests и два сценария PostgreSQL.
- PostgreSQL-тесты запущены с `TASK_POSTGRES_TEST_ADMIN_CONNECTION` на выделенном экземпляре версии 16.
- 99 desktop-тестов, включая PersonalPlanningTests, PersonalSafetyTests, AutomaticDeadlineReminderTests, MonthlyQuery, создание похожей задачи и нативное открытие карточки.
- 4 HTTP-теста PlanningMonth покрывают POST с названием и месяцем и PATCH с новым, пустым и пропущенным полем.
- Проверены переход года, строгий формат, приоритет дат, перезапуск, отсутствие ложных напоминаний и полная выборка за первой страницей.
- Проверены восстановление v1–v5, rollback при сбое, сохранение даты, описания и нового месяца в backup.
- Форматирование решения и `verification/Test-ProjectBoundaries.ps1` прошли. Команды обязательного CI перечислены в CONTRACT.

- Независимое review проверило пересборку карточек. При редактировании месяц сохраняется; новая задача по образцу начинает с чистого расписания. Оба поведения покрыты тестами.
- В первом полном локальном прогоне Windows один тест двойного щелчка зарегистрировал два одиночных. Адресный повтор прошёл; полный gate кандидата проверяется отдельно в CI.

## Следующие этапы

- Этап 2 использует общую выборку и открывает обычную карточку задачи по ID. Изменения WPF потребуют Windows-приёмки.
- Этапы 3–4 используют существующие чек-листы, каталог и `Task.Application/Calendar/RecurrenceService.cs`, `Task.Desktop/Personal/PersonalRecurrenceClient.cs` в src.
- Этап 5 требует сверки исходной точки. На `b5b1952` в дереве production и его тестах путь к экрану «Материалы», поставщикам и FIFO подтвердить не удалось.
- Проверены имена файлов и текст исходников/тестов в Git. Перед этапом 5 сверить актуальный main и указать здесь фактические пути.
- Условие R9–R11 сохраняется: модифицировать интегрированный модуль в доступных ему режимах. Старые ветки и прототип не использовать.
- Этап 6 включает сквозную приёмку и упаковку. Модель без месячного UI не закрывает пользовательскую готовность экрана.
