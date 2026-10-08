# Inbox Zero 1.0.1 — validation report

Дата: 2026-10-08 (Europe/Minsk). Изменения реализованы локально поверх main == origin/main; commit/push не выполнялись. Посторонние изменения пользователя сохранены.

## State logic

Corporate Inbox Zero: State == Empty, полный подтверждённый snapshot, ноль capture-only active tasks, нет выполняющейся загрузки или mutation, сеть и сессия доступны. Полнота подтверждается проходом всех cursor pages; если Total задан, он должен совпадать с числом уникальных задач и не меняться между страницами. Повтор cursor, недостающая страница, несовпадающий Total, ошибка или отмена не дают положительный empty state. Items заменяются атомарно после полного чтения. Capture/convert/save временно отключены при чтении, чтобы старый ответ не перезаписал подтверждённое создание.

Преобразование использует существующий PATCH той же задачи: после серверного успеха — полный refresh. Ошибка refresh сохраняет error/offline presentation. Поздний success после потери связи или сессии не возвращает Inbox Zero. Cached empty не становится актуальным только из-за восстановления связи: нужен успешный refresh. Offline сохраняет ранее полученные записи и read-only presentation; положительный заголовок скрывается даже при ранее пустом snapshot.

UI использует существующие Task.State.Panel/Title/Body и design tokens, компактный Inbox glyph, спокойный secondary text и кнопку «Добавить запись», которая переводит фокус в существующее поле capture. Level2 heading, accessible button name, polite presentation. Fade 120 ms, без animation при reduced motion/high contrast. После удаления последней отображаемой строки/преобразования focus переходит на Create или heading при отсутствии права создания; capture draft не перехватывается.

Personal: ShowEmpty дополнительно требует успешного чтения локальной SQLite. До первой загрузки и после исключения чтения положительный empty state невозможен. Existing Personal UI и контракты сохранены.

## Existing architecture and limits

- Corporate Inbox не имеет пользовательских filters: GetTasksAsync(cursor) получает нефильтрованный набор, IsCaptureOnly определяет состав Inbox. CaptureText является черновиком создания, не поиском. Новые filters не добавлялись. Отдельный filtered-empty Inbox сценарий неприменим; существующий Personal Today empty остаётся «На сегодня задач нет» и проверен regression test.
- В Corporate Inbox нет delete command/endpoint. Новое удаление не добавлялось; disappearance/deletion в серверном snapshot проверены через authoritative refresh. Personal conversion/cancel/status lifecycle regression tests прошли.
- В Desktop нет realtime subscription/invalidation transport. Изменения другого клиента обнаруживаются существующим API через DispatcherTimer каждые 15 секунд, пока Inbox активен, без нового endpoint/entity/server flag. Add убирает Zero, delete/convert возвращает его только после полного refresh. Это polling, а не push realtime; тест вызывает тот же refresh entry point, не ждёт реальные 15 секунд.
- Cache здесь — ранее загруженные in-memory Items; persistent Inbox cache/completeness metadata отсутствуют. Неизвестные/stale данные не объявляются разобранными.
- Quick Capture уже есть в shell/command palette; новый shortcut/dependency не добавлялся.
- Live multi-client API, физическое отключение сети и ручное прослушивание NVDA/Narrator отдельно не выполнялись. Keyboard/focus и screen-reader UIA semantics проверены native WPF test.
- Serena/Semble недоступны в сессии; использованы scoped reads/searches. Canonical sources и server/API/DB контракты не изменялись.

## Checks and results

- `dotnet build work/production/Task.sln -c Release --verbosity quiet`: PASS, 0 errors; existing xUnit1031 warning in DesktopCredentialVaultTests.cs.
- `dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-build`: 696 PASS, 0 FAIL, 0 SKIP. TRX included.
- Publication gate `dotnet test work/production/Task.sln -c Release --no-build`: 2107 PASS, 0 FAIL, 4 SKIP (PostgreSQL environment-dependent tests); native WPF Inbox tests passed again. Raw per-project TRX included.
- Initial empty/loading, empty first page with continuation, classified-only first page, missing Total items, repeated cursor, later-page failure, cached zero/offline/error, stale Items, offline during request, external add/delete, confirmed conversion, concurrent add after conversion, failed post-conversion refresh, capture failure and capture/read race: PASS.
- Native isolated WPF: real InboxView and theme resources; Zero visibility, disappearance/reappearance through automatic-refresh entry point, focus from last row and conversion form, Create invoke/capture, keyboard traversal, heading UIA name/Level2, Create UIA accessible name, reduced motion static and normal fade: PASS.
- Personal initial snapshot and existing persistent conversion, lifecycle, filtered Today and zero Corporate HTTP regressions: PASS.
- `git diff --check`: PASS. Self-review fixed mutation/read race, capture failure masking Zero, and focus restoration when a remote add replaces Zero.
- Publication verification reruns the production solution tests; results are included in evidence/publication. Whole-solution formatter also fails on pre-existing files in origin/main (GitHub CI run 37802954435 failed before this change). All six changed C# files pass scoped formatting verification.

## Changed production/test files

1. work/production/src/Task.Desktop/ViewModels/InboxViewModel.cs — authoritative pagination/state, mutation refresh, access gates.
2. work/production/src/Task.Desktop/Views/InboxView.xaml — compact positive state, accessible heading and Create.
3. work/production/src/Task.Desktop/Views/InboxView.xaml.cs — lifecycle polling, focus recovery and motion setting.
4. work/production/src/Task.Desktop/Personal/PersonalTasksViewModel.cs — successful-local-snapshot guard.
5. work/production/tests/Task.Desktop.Tests/Tasks/InboxViewModelTests.cs — state/pagination/mutation/cache tests.
6. work/production/tests/Task.Desktop.Tests/Tasks/InboxZeroUiTests.cs — isolated native WPF verification.
7. work/production/tests/Task.Desktop.Tests/Personal/PersonalWorkflowTests.cs — Personal initial Zero test.

Only DESK-02 evidence/note/date updated in roadmap; progress remains 100. The pre-existing dashboard edit was preserved.

## Package

Source snapshot (including the newly added UI test), changes.patch for tracked files, build log, TRX, version and per-file SHA-256 manifest. The patch alone is not the whole deliverable: apply the source snapshot for the new test. Zip contents are independently verified against the manifest after creation. This is a source/evidence package, not a new portable Windows release.
