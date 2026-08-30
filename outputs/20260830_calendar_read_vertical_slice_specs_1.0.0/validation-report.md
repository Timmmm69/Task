# Validation report — Calendar read vertical slice specifications 1.0.0

## Результат

Подготовлен последовательный комплект из пяти самостоятельных технических заданий для production Calendar read vertical slice. Каждый промт содержит исходную процедуру, проверяемые prerequisites, канонические references, точный контракт, разрешённые и запрещённые изменения, tests, acceptance criteria, stop conditions и direct-push процедуру.

Статус проверки комплекта: **PASS**.

## Проверенная исходная точка

- Repository: `C:\Users\novik\Таск`.
- Branch: `main`.
- Baseline commit: `896cfc583c64a5e53c38b1dd1c960193c4fc0fc2`.
- Divergence с `origin/main` при подготовке: `0 0`.
- Рабочее дерево до создания package было чистым.

## Проверенные существующие foundations

- `CalendarEvent` domain aggregate, timing, attendees и lifecycle;
- `CalendarEventQueryService` и `CalendarEventDetails`;
- `ScheduleQueryService`, `SchedulePage`, `ScheduleItem`, `ScheduleConflict`;
- `PostgresCalendarEventStore`, `PostgresScheduleStore` и migration 003;
- production authentication/session/request context, permission decision engine и capabilities endpoint;
- production Task read/write API и typed Desktop client patterns;
- WPF shell, navigation lifecycle, visual foundation и accepted Stage 5 Direction 2 calendar references.

## Проверенные канонические контракты

- `GET /api/v1/calendar` с `Calendar.Read`, unified Task + Event projection и `SchedulePage`;
- `GET /api/v1/calendar-events/{id}` с full CalendarEvent details;
- `GET /api/v1/calendar/conflicts` с `ScheduleConflict[]`;
- half-open ranges, UTC instants, all-day timezone boundaries, deterministic ordering;
- max 366-day range, max 100 identifier filters и max 500 response items;
- tenant identity только из authenticated server context;
- accepted Direction 2 week layout, readonly и overlap visual states.

## Обоснование разбиения

1. Schedule endpoint сначала стабилизирует permission/capability, serialization и unified projection.
2. Details/conflicts переиспользуют принятый security/API pattern и завершают server read surface.
3. Typed Desktop client начинается только после стабилизации всех server responses.
4. WPF screen строится на готовом typed client и принятой visual truth.
5. Hardening/E2E независимо проверяет сквозной runtime и создаёт финальный evidence package.

Задания имеют семантические и файловые зависимости, поэтому параллельный запуск запрещён.

## Граница scope

Комплект реализует только read-only calendar slice. В scope: недельная unified projection, event details, conflicts, capability/security, typed transport, WPF week screen, lifecycle/accessibility/visual hardening и real E2E. Вне scope: `/calendar-events` list, create/edit/delete/archive/restore, attendees replacement/RSVP, drag/resize, recurrence expansion, notifications и offline mutations.

`departments` и `cursor` для unified schedule ещё не поддержаны текущим application/persistence contract; непустые значения должны отклоняться явно. `nextCursor` остаётся `null`. Это ограничение сформулировано во всех необходимых prompts и не маскируется как полная поддержка OpenAPI pagination.

## Проверка полноты

- `README.md` — порядок, baseline, зависимости и итог.
- `01_SERVER_SCHEDULE_READ_API.md` — server schedule endpoint.
- `02_SERVER_EVENT_DETAILS_AND_CONFLICTS_API.md` — details/conflicts endpoints.
- `03_DESKTOP_CALENDAR_READ_CLIENT.md` — typed Desktop client.
- `04_WPF_CALENDAR_WEEK_SCREEN.md` — WPF week UX.
- `05_CALENDAR_READ_HARDENING_AND_E2E.md` — hardening, real E2E и final package.
- `VERSION`, `manifest.json`, `SHA256SUMS`, `Verify-Manifest.ps1` — version/integrity controls.

## Оценка сложности

Весь этап — **выше средней сложности (примерно 7/10)**: backend foundations уже готовы, поэтому основной риск сосредоточен в строгом API mapping, timezone/DST, WPF timeline geometry, cancellation/session lifecycle и real UI E2E. Первые три задания средние; WPF и финальный E2E — medium-high. Последовательное разбиение удерживает каждый increment проверяемым.

## Известные ограничения

- PASS означает полноту и внутреннюю согласованность specifications package, а не готовность production реализации.
- Исполнители обязаны проверять актуальный `origin/main`; baseline SHA не заменяет fetch/rebase.
- Критические PostgreSQL/API/WPF E2E проверки нельзя заменять mock tests или отмечать PASS при skip.
