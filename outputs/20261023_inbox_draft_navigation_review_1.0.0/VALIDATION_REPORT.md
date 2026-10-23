# Проверка Inbox draft navigation перед main — 1.0.0

Дата: 23.10.2026. Исходный main: d71b270ed744e719ff66d5fec3b8b47ae41fc609. Проверенный код: 5ad85210d13eea557728ddc8a2abda1cd9c30388.

Результат: actionable findings не выявлены; исправление разрешено к публикации в main по прямой команде пользователя.

Проверен весь production diff: Deactivate больше не закрывает Conversion и не стирает данные повтора; CanConvertItem запрещает подменять открытый редактор; setter Conversion обновляет доступность команды. Проверены SelectedSection, команды сохранения/отмены, canonical Convert→PATCH, ModeSwitchGuard и закрытие сессии. Проверки доступа не изменены. Production diff ограничен одним ViewModel, остальные три файла — тесты.

Независимая read-only проверка: Независимый read-only review коммита 5ad8521: actionable findings нет. Проверены InboxViewModel, SelectedSection, ModeSwitchGuard и AsyncCommand; сохранение состояния, блокировка второго редактора, pending write и очистка при завершении сессии корректны. Проверяющий не запускал тесты, сборку и UI; эти проверки выполнены основным агентом.

## Выполненные проверки

- Новый полный desktop-прогон: 1831 PASS / 0 FAIL / 0 SKIP. TRX приложен. Команда: dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --no-restore.
- Release build Task.Desktop.csproj: PASS, 0 ошибок, 0 предупреждений.
- git diff --check и git apply --reverse --check source.patch: PASS.
- npm run dashboard:order и npm run dashboard:validate: PASS. Изменён только evidence PROD-01; progress/status не повышались.
- Ранее: целевые проверки 96/96; восемь из девяти новых регрессионных случаев падали при возврате исходного production InboxViewModel и проходят после исправления.

Покрыты поля и экземпляр редактора после прямой навигации и SelectedSection; сохранение исходной SourceVersion после обновления списка; успешное сохранение/явная отмена; read-only/offline/permissions; запрет открытия другого редактора; pending PATCH без принудительной отмены; повторы потерянных Convert/PATCH с прежними Idempotency-Key и телом; ModeSwitchGuard Save/Cancel и pending write; завершение сессии.

## Ограничения

Нативная визуальная проверка UI в этой задаче не запускалась. Backend/API/DTO/БД/permissions не менялись. Защита черновика при закрытии всего приложения не расширялась. В первом полном прогоне предыдущего этапа единожды упал тест debounce командной палитры; повтор того прогона и новый pre-main прогон прошли полностью. Причина того единичного падения не установлена, код палитры не изменён.

Пакет фиксирует проверку до push; факт успешной публикации проверяется отдельным fetch и сравнением origin/main с публикуемым HEAD. Исходная ветка и посторонние материалы пользователя сохраняются.
