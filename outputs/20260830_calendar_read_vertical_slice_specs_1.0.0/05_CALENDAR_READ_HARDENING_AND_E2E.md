# ПРОМТ ДЛЯ НОВОГО ЧАТА — 05. Calendar read hardening, real E2E и итоговый пакет

Ты работаешь в `C:\Users\novik\Таск`. Заверши Calendar read vertical slice: независимо проверь весь server-to-WPF путь, устрани только найденные в пределах slice defects, выполни реальные security/lifecycle/accessibility/visual проверки и создай итоговый validation package. Это пятое, финальное задание; начинай только после успешного push WPF calendar week screen в `origin/main`.

## Обязательное начало

Прочитай `AGENTS.md`, `work/delegation/README.md` и все четыре предыдущих calendar implementations по code/tests. Fetch, чистый synchronized `main`, fast-forward. Не доверяй сообщениям предыдущих чатов: проверь наличие и согласованность endpoints, permission/capability, typed client, CalendarViewModel, WPF wiring, screenshots/references.

Прочитай visual references Direction 2 и reports из задания 04. Сначала составь короткий audit ledger с обнаруженными дефектами и severity. Исправляй только correctness/security/lifecycle/accessibility/visual defects внутри Calendar read slice. Если отсутствует целый prerequisite, нужен schema/public contract redesign или найден defect вне scope, остановись и опиши blocker вместо маскировки в E2E script.

## Обязательные hardening scenarios

Проверь и при необходимости исправь:

- `Calendar.Read` отсутствует: navigation может оставаться видимой, но данные не загружаются, protected cache очищен, UI честно сообщает отсутствие права; прямые server requests дают 403;
- session expired/revoked, logout-all и device revocation во время initial load, event details load и refresh: поздний response не появляется после auth transition;
- navigation `calendar → tasks → calendar`, быстрое переключение недель и закрытие окна: cancellation, subscription ownership, no ObjectDisposed/stale updates;
- network/503/TLS failure: подтверждённые данные не маркируются свежими, retry работает, credentials не раскрываются;
- malformed/unsupported server response, включая unexpected cursor: protocol error, без partial misleading rendering;
- cross-tenant event id и filters: no leakage;
- DST week boundaries, all-day event timezone, Sunday/Monday transition, year boundary;
- overlap indicator соответствует server pair/severity и имеет текстовую альтернативу;
- 500 items остаются usable без UI freeze на целевом железе; не добавлять client pagination, которой нет на server;
- keyboard-only flow, focus restoration, live announcements, automation names, contrast/high-contrast behavior и 200% Windows scaling.

## Реальный end-to-end gate

Используй реальный PostgreSQL 16, штатные migrations, production HTTPS Task.Api и production WPF build. Mock server/SQLite/in-memory store не заменяют E2E. Секреты задаются только через безопасные локальные env/config и не попадают в Git, screenshots или logs.

Создай/расширь узкий verification script, который подготавливает изолированные tenant/test rows штатными средствами и доказывает:

1. login реальной учётной записью с permission backing `Calendar.Read`;
2. `/capabilities` содержит `Calendar.Read`;
3. PostgreSQL содержит для tenant минимум timed CalendarEvent, all-day CalendarEvent, interval Task, point Task и конфликтующую пару;
4. `/api/v1/calendar` возвращает именно эти объекты в ожидаемой неделе и не возвращает другой tenant;
5. `/calendar-events/{id}` возвращает details, attendees, version и совпадающий ETag;
6. `/calendar/conflicts` возвращает ожидаемую пару/severity, filter/exclude работают;
7. WPF после входа открывает «Календарь», показывает week range, оба типа объектов, all-day lane и conflict indicator;
8. выбор event показывает реальные server details;
9. переход на следующую/предыдущую неделю делает новый запрос и не показывает stale data;
10. restart API сохраняет тот же read result из PostgreSQL;
11. пользователь без read permission получает 403, а WPF не показывает защищённые data;
12. revocation активной session очищает calendar UI и возвращает authentication workflow.

Каждый пункт должен иметь machine-readable либо screenshot/UI Automation evidence. Critical пункты нельзя отмечать PASS по unit/mock tests и нельзя skip.

## Visual и accessibility evidence

Сними финальные screenshots при 150% DPI минимум для 1487×1058, 1200×900, 1000×640, 800×480 и actual Windows 200% scaling: loaded mixed week, overlap, event inspector, empty, loading/refresh, retryable error, forbidden/session revoked. Выполни UI Automation keyboard traversal и зафиксируй names/roles/focus order/live states. Сравни с `implementation-direction2-calendar-week.png`, readonly и overlap references. P0/P1 должны быть 0; P2 либо исправлены, либо явно перечислены как non-blocking с обоснованием.

## Разрешённые изменения

- Calendar server/client/ViewModel/WPF/test files из заданий 01–04 только для доказанных defects;
- existing auth/session/MainWindow files только для calendar lifecycle defect с regression test;
- `work/production/verification/Test-CalendarRead.ps1` и минимальные fixtures/helpers;
- уникальный `outputs/<YYYYMMDD>_calendar_read_vertical_slice_1.0.0/**`.

Не менять migrations/schema, Calendar write/domain behavior, Task semantics, canonical sources, dependencies, deployment architecture или unrelated screens. Не добавлять create/edit/drag/resize/RSVP/recurrence. Если hardening production diff выходит за 8 файлов или 400 lines без evidence package, остановись и предложи отдельный defect increment.

## Automated/full gate

```powershell
cd work/production
dotnet format Task.sln --no-restore
dotnet test tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --filter "FullyQualifiedName~Calendar"
dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --filter "FullyQualifiedName~Calendar|FullyQualifiedName~MainWindowLifecycle"
dotnet test Task.sln -c Release
dotnet build Task.sln -c Release --no-restore
powershell -ExecutionPolicy Bypass -File verification/Test-DesktopShell.ps1
powershell -ExecutionPolicy Bypass -File verification/Test-CalendarRead.ps1
git diff --check
```

Запусти real-PostgreSQL tests с `TASK_POSTGRES_TEST_ADMIN_CONNECTION`. В отчёте укажи фактические passed/failed/skipped counts; любой skipped Calendar critical test запрещает completion.

## Итоговый validation package

Создай `outputs/<YYYYMMDD>_calendar_read_vertical_slice_1.0.0`:

- `README.md` с outcome и границей read-only slice;
- `VERSION` = `1.0.0`;
- `validation-report.md` со scope, commit, environment, командами и фактическими results;
- `e2e-report.md` с 12 сценариями и evidence mapping;
- `visual-qa-report.md` с reference comparison и severity ledger;
- `accessibility-report.md` с keyboard/UIA/scaling evidence;
- `evidence/` без secrets;
- `manifest.json` со всеми файлами, sizes и SHA-256;
- `SHA256SUMS`;
- `Verify-Manifest.ps1`, который независимо обнаруживает неверные hashes, лишние и пропущенные файлы.

После окончательного наполнения package verifier обязан PASS. Не включай сам manifest/hash list в рекурсивную самоссылку, если принятый format этого не делает; следуй существующим output package patterns.

## Критерии завершения

Calendar.Read доказан end-to-end на реальных PostgreSQL/API/WPF; week projection, event details и conflicts корректны; tenant/permission/session fail-closed; lifecycle не оставляет stale protected data; Direction 2/accessibility gates пройдены; full solution зелёный; validation package самопроверяется. Read-only boundary сформулирована честно.

## Stop conditions и публикация

Не push и не объявляй completion при mock-only E2E, skipped critical step, secret evidence, P0/P1 issue, permission/session leakage, failing tests/build/manifest, dirty tree, конфликте или scope creep.

Проверь diff, stage только scope production/test/verification и уникальный output package. Commit: `feat(calendar): complete calendar read vertical slice`. Затем fetch, rebase `origin/main`; повтори full gate, real E2E и manifest verification; push `origin HEAD:main`. В финале перечисли product outcome, test counts, real E2E result, visual/accessibility result, package path, commit SHA и факт push.
