# Validation report — automatic deadline reminders 1.0.0

Source: `bb6b903e0404565f328090ce1197679897722fd3`. Client/server assemblies: 1.0.10. Date: 2026-10-14. Personal schema 5; Corporate migration 16.

## Итог

Общий Release gate: **2249 PASS / 0 FAIL / 0 SKIP**, Desktop 789, ServiceHosts 608, Task.Tests 852. Все девять шагов Run-Gate.ps1 завершились с exit 0: restore, audit, whitespace, boundaries, build, tests, secrets/TLS, locked restore, security review. PostgreSQL 16 и PostgreSQL 15 были доступны; последний проверяет штатный отказ до DDL, а не поддержку напоминаний на PG15. Новые серверные проверки выполнялись на реальной PostgreSQL 16, без ветки «база отсутствует». После fetch/rebase origin/main (история уже актуальна) повторены 15 корпоративных и 36 desktop тестов: PASS.

## Что проверено

| Сценарий | Evidence |
|---|---|
| Сохранение со сроком/без срока, изменение/удаление, индивидуальный интервал, explicit off и frozen defaults | AutomaticDeadlineReminderTests, PostgresProductApiTests.AutomaticDeadline_* |
| Дата без времени, отдельное поле календаря, локальный час, DST gap/overlap | DateOnlyDeadline_*, AutomaticDeadline_DateOnlyAndDst*, Dst_* |
| Прошедший trigger при будущем сроке, запрет лавины overdue/backfill | ElapsedTrigger_*, обе populated upgrade проверки |
| Завершение, отмена, архив, корзина/удаление, восстановление | TerminalOrRemovedTask_*, LifecycleRightsAndAccountChanges*, Restore_*, ConcurrentManualCollisionAndRestore* |
| Смена исполнителей, несколько получателей, исключение наблюдателей, доступ/blocked account и изоляция own preference | ChangedDeadlineOrAssignees*, MultipleAssignees*, DefaultSnapshotsExplicitOffAndPrivateRecipients*, LifecycleRightsAndAccountChanges* |
| Совпадение ручного/автоматического, разные времена, snooze, история | ElapsedTrigger_*, ManualSameTimeDedupesDifferentTimeSurvives*, ConcurrentManualCollisionAndRestore* |
| Повтор команды, перезапуск, параллельная доставка, offline/reconnect | CommandReplayAndRestart*, TaskWriteTransactionAndReplay*, concurrent delivery test; полный desktop/API regression gate |
| SQLite 4 → 5 и PostgreSQL 15 → migration 16 на существующих данных, ограниченная runtime роль | UpgradeV4_*, AutomaticDeadline_UpgradeV15* (15 — версия схемы на PG16) |
| Тихие часы, desktop off, sound off | ExistingPresentationPreferences* и CorporateNotificationAgentTests |
| Сохранение → правило → worker → центр Task → Windows request | native-personal.json и native-corporate.json; повторный Corporate pass даёт один запрос |

Нативная корпоративная проверка использует реальный TaskCreate service, серверную транзакцию/idempotency, мигрированную PostgreSQL, существующий Worker, авторизованный ProductApiStore и WindowsToastPresenter. HTTP read transport в harness заменён proxy с настоящим ответом store; это не ручной вход в Corporate клиент и не отдельный live HTTPS UI acceptance. Personal harness использует настоящие SQLite store и Windows presenter. Данные изолированы; корпоративная GUID-БД удалена после пробы.

В обоих режимах Windows приняла запрос. **Видимый баннер не подтверждён**: acceptance запроса не приравнивается к визуальному показу. Exact portable smoke отдельно сохраняет задачу через реальный UI и проверяет правило/центр/журнал в изолированном хранилище (portable-smoke.json).

## Перенос существующих данных

Идемпотентный перенос выполняется серверным Migrator / локальным хранилищем. Будущие активные задачи получают по одному управляемому правилу на получателя; существующие ручные deadline правила учитываются, явное отключение сохраняется. Уже прошедшее время первичного переноса подавляется, просроченные задачи не рассылаются. Сроки и история сохранены. Restore активной задачи возобновляет недоставленное время; restore закрытой задачи не меняет терминальный статус. Дата без времени остаётся датой, выбранный час служит явно показанной точкой отсчёта интервала.

## Сборка и ограничения

Build-UnsignedPortableClient.ps1 -Version 1.0.10 -RequireCleanSourceTree: PASS. Test-UnsignedPortableClient.ps1: PASS, 7 файлов, ZIP/hash/inventory сверены. Серверные проекты dotnet publish -c Release -r linux-x64 --self-contained false -p:UseAppHost=false: PASS. Новый OCI build/runtime/scan NOT RUN: локальный Docker Linux engine отсутствует. Чистая Windows машина, физическое отключение сети и визуальный Windows banner NOT RUN. Подпись portable пакета не предусмотрена этим путём.

Для обязательного gate устранены унаследованные whitespace ошибки и устаревшее ожидание Windows TFM в boundary скрипте. Нативные WPF фикстуры получили OnExplicitShutdown вместо случайного завершения общего Application; возврат foreground после вспомогательного окна ожидается асинхронно. Проверки не удалялись и не ослаблялись. Диагностические неуспешные прогоны сохранены в work/, не представлены как PASS.

Посторонние правки в основной рабочей копии исключены из коммита. Канонические sources/ и соседние проекты не изменялись. Roadmap дополнен evidence только PROD-05/API-04/DATA-05; progress/status не повышались; dashboard:order и dashboard:validate выполнены.
