# Device-local UI view state — 1.0.0

Отчёт фиксирует проверенное состояние до публикации в main; итоговый commit/push сообщается отдельно. Backend API, PostgreSQL, OpenAPI, domain entities и UserSettings не изменены. Полный пользовательский quality gate не объявляется закрытым: два ручных сценария ниже остаются NOT RUN.

## Хранение и политика

`%LOCALAPPDATA%\Task\view-state\<sha256>.json`, либо `view-state` внутри существующего `TASK_DESKTOP_DATA_DIRECTORY`.
Существующее desktop-local хранение содержит независимые versioned файлы режима и адреса сервера; универсального UI preference store нет. Добавлен минимальный `IViewStateStore` и его desktop implementation, без новых dependencies. DPAPI vault содержит credentials, а не UI или business cache; он не используется для этой функции. Существующая реализация не содержит persistent encrypted corporate business cache; calendar cache живёт в памяти и к preferences не относится.

Filename: SHA-256 от length-prefixed authority, opaque user GUID, device namespace. Corporate authority включает endpoint и organization GUID; Personal использует отдельную authority и persistent LocalActorId. Device namespace — machine name + Windows user SID, хешируемые в filename. Login/display name, device key и tokens не используются.

UI preferences переживают обычный logout, как существующие несекретные mode/server preferences, но namespace другого authenticated user/organization/server полностью отдельный. Store создаётся заново для каждого shell; его in-memory состояние не переносится между пользователями. Credential cleanup остаётся действующим. Personal backup не включает UI preferences.

## Schema и ключи

Schema `version: 1`; `widths: {fixed-key: double}`, `sections: {fixed-key: bool}`, `sorts: {fixed-surface: {Field, Descending}}`.
Ключи имеют вид `work-links/v1/type`, `personal-links/v1/title`, `today/v1/queue`, `projects/v1/members`.
Только compile-time allowlist; object IDs, task titles, DTO, paths, comments, searches и произвольные disclosure keys отсутствуют. Размер чтения ограничен 32 KiB, JSON depth — 8; количество записей ограничено allowlist. Unknown/stale IDs игнорируются. View version входит в fixed key.

Диск работает на thread-pool workers. Resize changes только отмечаются в памяти; финальная пользовательская DIP-ширина сохраняется после завершённого жеста. Отменённые жесты и не-resizable колонки игнорируются. Auto-measure/layout не записываются. Есть 300 ms debounce, snapshot + flushed temporary file + atomic overwrite; crash не делает partial файл видимым. Orphan temporary file не восстанавливается. Current MinWidth/MaxWidth проверяются при save/restore. Колонки восстанавливаются после создания на Loaded; Star resize сохраняет ActualWidth в DIP. Левая граница учитывает предыдущую visible колонку в display order.

Повреждённое значение preference диагностируется через non-sensitive Trace warning и отбрасывается. Нечитаемый/неподдерживаемый файл удаляется, используются defaults. Ошибка записи не уничтожает предыдущий атомарный файл; повтор возможен при следующем изменении/flush. ResetAsync очищает состояние; существующей команды Reset layout нет, Reset filters не затронут.

## Surfaces

- Width/sort: полный список связей в Corporate Work Hub и Personal Workspace. Поля TypeLabel/Title, один primary sort, оба направления; сортировка повторно применяется при смене ItemsSource. Corporate links client отвергает `hasMore=true`, поэтому сортировка полного набора допустима.
- Today: очередь и inspector; стандартное исходное состояние expanded, accessible ExpandCollapse controls, размеры панелей учитывают сворачивание.
- Projects: participants и related tasks.
- Task workspace: checklist, subtasks, files, predecessors, comments, history; Task editor: дополнительное planning.
- Main Tasks/Inbox inspectors; Personal task details/planning, project storage, reminders creation, workspace actions/links/backup.
- Visibility, capabilities, object loading и permission checks не изменены. Hidden section не становится видимой через preference; restore расширения происходит только когда текущая видимость его допускает. Состояние отдельных TreeView объектов не сохраняется.

## Серверная сортировка: объективное ограничение

Текущий `/api/v1/tasks` явно отвергает non-empty `sort` и `filter`; поддерживает cursor pagination в canonical server order. Выбор сортировки для него отсутствует. Users/devices поддерживают только стабильный `id/+id`, пользовательского выбора тоже нет. Добавлять произвольную сортировку без backend change невозможно, поэтому эти списки продолжают использовать канонический серверный порядок. UI sort preferences не применяются к отдельной странице и не добавляют неподдерживаемый query.

## Validation

- Entire Task.sln Release build: PASS.
- Desktop UI/settings/list/auth regressions + all new tests: **711 PASS, 0 FAIL, 0 SKIP**.
- New store tests, native WPF lifecycle probe и cursor-query regression: **15 PASS** (включены в desktop totals). После последних guard/accessibility правок весь desktop suite повторно прошёл.
- Server list endpoint/security/pagination tests: **163 PASS, 0 FAIL, 0 SKIP**.
- Width: restart, Min/Max, changed current bounds, unknown/deleted/new IDs, corrupt values, numeric DIP schema, non-resizable, auto/star, left/right grips и cancellation.
- Sort: ascending/descending, unsupported key fallback, data refresh; отдельная query regression проверяет две страницы `/tasks`, точные запросы без sort, opaque cursor, отсутствие client reorder и отсутствие влияния другого surface.
- Sections: expanded/collapsed restart, unknown IDs, visibility/permission-hidden gate, native UIA ExpandCollapse, detach/reload.
- Isolation: separate user, device, authority, surface/version; corrupted-store recovery и restart.
- Performance/lifetime: 200 edits дают один debounced write, setter не создаёт файл синхронно, disk I/O только worker, descriptor/routed handlers detached on Unloaded, close ждёт async flush, static event subscriptions отсутствуют.
- Crash/write safety: previous committed snapshot survives denied atomic replacement; retry succeeds; orphan partial tmp ignored.
- Self-review и `git diff --check`: PASS. Сохранены исходные пользовательские правки; источники sources не менялись.
- Native Today snapshots 96/144 raster DPI: просмотрены; это offscreen native control rendering, а не физическая mixed-monitor DPI проверка.

## Непроверенные обязательные ручные сценарии

1. **NOT RUN:** ручная end-to-end инспекция paginated list с запущенным production backend/PostgreSQL. В этой сессии работающего backend нет; использованы native WPF probe, desktop HTTP/query regression и actual endpoint TestServer tests с fake persistence. Это не объявляется ручным live-server acceptance.
2. **NOT RUN:** перенос окна между физическими мониторами с разным DPI. Numeric DIP representation и 96/144 raster rendering проверены; hardware topology не проверена.

Реализация доступных текущему API сценариев проверена указанными tests; полный requested manual quality gate остаётся открытым. Screenshots не содержат реальных business data. Manifest перечисляет payload SHA-256; ZIP перечитан и хеши проверены.
