# ПРОМТ ДЛЯ НОВОГО ЧАТА — 06. WPF Task write UX, conflict handling и real end-to-end gate

Ты работаешь в `C:\Users\novik\Таск`. Заверши Task write vertical slice на production WPF и создай проверенный итоговый пакет. Это шестое и финальное задание цепочки. Начинай только после того, как актуальный `origin/main` содержит server foundation, create/PATCH/transition endpoints и typed Desktop write API client.

## Обязательное начало

Прочитай `AGENTS.md`, `work/delegation/README.md` и полностью изучи принятый visual foundation package `outputs/20260825_task_wpf_visual_foundation_0.1.0`, особенно reference/comparison, validation и visual QA reports. Fetch origin, проверь чистый synchronized `main`, fast-forward. По code/tests проверь все prerequisites. Если typed client или server contracts отсутствуют/расходятся, остановись.

Для UI работы сначала проведи audit существующих `MainWindow.xaml`, `TasksViewModel`, resource dictionaries, commands, focus/automation patterns и Desktop tests. Сохрани Stage 5 Direction 2 и уже введённые tokens/styles. Не возвращай stock WPF appearance и не создавай второй design system.

## Пользовательские сценарии

### Создание

- Кнопка «Новая задача» активна только при доступном Task.Create capability и рабочей сессии.
- Открывается нативный modal/dialog или side panel, согласованный с текущим shell: title, priority, optional start, optional deadline.
- Inline validation до отправки: required title, length, UTC/local conversion, deadline not before start.
- Save блокируется от двойного клика; Escape закрывает только при отсутствии in-flight request; unsaved changes требуют понятного подтверждения.
- После success диалог закрывается, список обновляется, новая задача выбирается и inspector показывает server response.

### Изменение

- Для выбранной активной нетерминальной задачи доступна «Изменить».
- Форма предзаполнена server DTO и отправляет presence-aware PATCH только для реально изменённых fields.
- Используется version выбранного свежего DTO; после success item/details заменяются server response без optimistic fake data.
- No changes не отправляет network request.

### Смена статуса

- Показывай только допустимые действия текущего status: начать, отправить на проверку, завершить, отменить.
- Никакого generic status dropdown, допускающего запрещённые переходы.
- Для destructive/terminal actions требуется ясное confirmation; reason, если показан, не обязателен и ограничен 2000 characters.
- После success status/ETag/details обновляются из server response.

## Ошибки и concurrency UX

- `412 VERSION_CONFLICT`: не перезаписывать; показать «Задача уже изменена другим пользователем», предложить «Загрузить актуальную версию» и «Закрыть». После reload пользователь сам повторяет команду с новым idempotency key.
- `409 IDEMPOTENCY_REQUEST_IN_PROGRESS`: сохранить форму, временно disable submit и разрешить controlled retry той же команды с тем же key после delay/user action.
- `409 IDEMPOTENCY_KEY_REUSED`: protocol error; сохранить пользовательский input, создать новый key только после явного повторного действия.
- validation errors связывать с fields; forbidden скрывает/disable дальнейшие write controls и сообщает об изменившихся правах; authentication failure завершает текущий auth workflow; server unavailable сохраняет form state и позволяет retry.
- Cancellation/closing не должны позже применять stale response. Используй generation/cancellation pattern текущего `TasksViewModel`.

## Accessibility и visual requirements

- Полная keyboard navigation, видимый focus, logical tab order, Enter/Escape semantics.
- AutomationProperties.Name/HelpText для buttons, fields, errors и status announcements.
- Validation/error и success announcements через existing live-region pattern.
- Target sizes и spacing из visual foundation; никаких hard-coded competing colors/typography.
- Layout должен работать как минимум при 1487×1058, 1200×900, 1000×640 и 800×480 на текущем 150% DPI; compact mode не обрезает действия и form fields.
- Loading, disabled, validation, conflict, network error и success states должны быть визуально проверены.

## Архитектура

ViewModel зависит только от typed `IDesktopTasksApiClient`. Code-behind допускается только для WPF window ownership/focus mechanics, не для business/network logic. Commands имеют correct CanExecute и single-flight behavior. Не дублируй DTO validation. После mutation list/details остаются согласованными; если local replacement невозможно безопасно, выполняй controlled refresh и сохраняй selection по id.

## Разрешённые изменения

- `work/production/src/Task.Desktop/MainWindow.xaml` и минимальный code-behind;
- `work/production/src/Task.Desktop/ViewModels/TasksViewModel.cs`;
- максимум два новых Task editor/dialog ViewModel/XAML files;
- существующие visual resource dictionaries только для действительно reusable missing states/styles;
- `work/production/tests/Task.Desktop.Tests/Tasks/**`, `MainWindowViewModelTests.cs`, `VisualFoundationTests.cs` по необходимости;
- `work/production/verification/Test-TaskApi.ps1` и/или новый узкий Task write E2E script;
- новый итоговый каталог `outputs/<дата>_task_write_vertical_slice_1.0.0/**`.

Не меняй server/domain/migrations, canonical sources, unrelated screens, dependencies или deployment architecture. Если server defect обнаружен, остановись вместо client workaround. Production/test code ориентировочно до 12 files и 700 changed lines; если больше, сначала объясни blocker.

## Automated tests

Покрой ViewModel state machine для create/edit/transition success; CanExecute/capability; client failures; conflict reload; same-key controlled retry; no-op edit; single-flight; stale response suppression; selection/list reconciliation; session end; cancellation/disposal. Presentation tests проверяют resource keys, automation labels, focus semantics и отсутствие regression visual foundation.

## Реальный end-to-end gate

Подними реальный PostgreSQL 16, применяй миграции штатным migrator, запусти production HTTPS API и production WPF. Не заменяй это mock server. Выполни:

1. login реальной учётной записью с task.manage;
2. создать задачу через UI;
3. подтвердить PostgreSQL row, audit, domain event, outbox и completed idempotency record;
4. повторить create request с тем же key и подтвердить отсутствие дублей;
5. изменить title/priority/schedule через UI;
6. искусственно получить stale version и подтвердить conflict UX без overwrite;
7. выполнить допустимые transitions как минимум `new → in_progress → review → completed`;
8. подтвердить final GET/list state и persistence после перезапуска API;
9. проверить пользователя без write permission: read остаётся доступен, write controls недоступны, server возвращает 403 при прямом request.

Сними screenshots и UI Automation evidence для loaded, create form, validation error, edit form, transition confirmation, concurrency conflict, network error и final completed state. Реальные credentials/secrets должны быть redacted и не попадать в Git.

## Полный gate

```powershell
cd work/production
dotnet format Task.sln --no-restore
dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release
dotnet test tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --filter "FullyQualifiedName~TaskEndpoints"
dotnet test Task.sln -c Release
dotnet build Task.sln -c Release --no-restore
powershell -ExecutionPolicy Bypass -File verification/Test-DesktopShell.ps1
git diff --check
```

Также выполни real Task write E2E script. Любой skipped critical E2E пункт остаётся gap и не позволяет объявить slice завершённым.

## Итоговый validation package

Создай уникальный каталог `outputs/<YYYYMMDD>_task_write_vertical_slice_1.0.0` с:

- `README.md`;
- `VERSION` со значением `1.0.0`;
- `validation-report.md` с scope, commit, всеми командами и фактическими результатами;
- `e2e-report.md` с точными сценариями и evidence mapping;
- `evidence/` с screenshots/UIA и безопасными DB assertions;
- `manifest.json` со списком файлов, размером и SHA-256;
- `SHA256SUMS`;
- `Verify-Manifest.ps1`, который независимо подтверждает hashes и отсутствие лишних/пропущенных файлов.

Manifest verification обязан завершиться PASS после окончательного наполнения пакета.

## Критерии завершения

Все три пользовательских write flow работают в production WPF с реальным backend/database. Concurrency и retry безопасны. Audit/event/outbox/idempotency exactly-once доказаны. Read-only user не получает write. Visual/accessibility gates пройдены. Full solution tests/build PASS. Validation package проверен.

## Stop conditions и публикация

Не объявляй completion и не push при server workaround, skipped real E2E, critical/high visual issue, failing tests, secret evidence, dirty tree, конфликте, выходе за scope или невозможности создать корректный manifest.

После PASS проверь diff, stage только scope production/test/verification и уникальный output package, commit `feat(desktop): complete task write vertical slice`; fetch, rebase `origin/main`; повтори полный gate, E2E при затронутых runtime files и manifest verification; затем `git push origin HEAD:main`. В финальном ответе перечисли product outcome, tests, E2E, package path, commit SHA и факт push.
