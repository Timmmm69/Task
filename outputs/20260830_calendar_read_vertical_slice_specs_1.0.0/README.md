# Комплект технических заданий: Calendar read vertical slice 1.0.0

Комплект предназначен для последовательной передачи в пять новых чатов. Каждый файл — самостоятельный промт: передавай его целиком, без пересказа и без истории предыдущих обсуждений.

## Порядок выполнения

1. `01_SERVER_SCHEDULE_READ_API.md` — защищённый `GET /api/v1/calendar`, permission/capability `Calendar.Read` и unified Task + CalendarEvent projection.
2. `02_SERVER_EVENT_DETAILS_AND_CONFLICTS_API.md` — `GET /api/v1/calendar-events/{id}` и `GET /api/v1/calendar/conflicts`.
3. `03_DESKTOP_CALENDAR_READ_CLIENT.md` — типизированный Desktop-клиент календаря с authentication refresh и строгим mapping.
4. `04_WPF_CALENDAR_WEEK_SCREEN.md` — production WPF экран календарной недели в принятом Direction 2.
5. `05_CALENDAR_READ_HARDENING_AND_E2E.md` — lifecycle/security/accessibility hardening, реальный PostgreSQL + HTTPS API + WPF E2E и итоговый validation package.

Задания выполняются строго в этом порядке. Новый чат запускается только после успешного push предыдущего задания в `origin/main`. Параллельный запуск запрещён: задания последовательно меняют общие API, Desktop wiring и UI lifecycle.

## Исходная точка

- Проверенный baseline при подготовке: `896cfc583c64a5e53c38b1dd1c960193c4fc0fc2`.
- Ветка: `main`; baseline — контрольная точка, но исполнитель всегда начинает с актуального `origin/main`.
- Уже существуют Calendar domain/application/persistence: `CalendarEvent`, attendees, lifecycle, `CalendarEventQueryService`, `ScheduleQueryService`, `PostgresCalendarEventStore`, `PostgresScheduleStore`, migration 003.
- Уже существуют production authentication/session pipeline, permission engine, capabilities endpoint, typed Tasks Desktop client, production shell, visual foundation и Task UI lifecycle patterns.
- Календарные API endpoints и production Desktop calendar screen отсутствуют.

## Граница этапа

Этап только read-only. Он предоставляет календарную неделю с задачами и событиями, детали события и обнаружение пересечений. Он не реализует список `/calendar-events`, создание/изменение/удаление/архивирование события, attendees replacement/RSVP, drag/resize, recurrence expansion, notification delivery или offline mutation queue. Неподдерживаемые параметры нельзя молча интерпретировать как поддержанные.

В `GET /api/v1/calendar` текущий application contract поддерживает `from`, `to`, `users`, `projects`, `status`, `timezone`. `departments` и `cursor` канонически существуют, но текущий slice их не реализует: непустое значение должно давать стабильную ошибку `400 VALIDATION_FAILED`; отсутствие допустимо. `nextCursor` остаётся `null`. Максимальный диапазон — 366 дней, но WPF запрашивает только видимую неделю.

## Итог этапа

После пятого задания авторизованный пользователь с `Calendar.Read` открывает раздел «Календарь», видит server-derived недельный график задач и событий из реального PostgreSQL, может выбрать событие и увидеть детали, видит честно отмеченные пересечения, переключает неделю и повторяет загрузку после сбоя. Пользователь без права не получает данные. Завершение подтверждено тестами, реальным end-to-end прогоном, визуальной/accessibility проверкой и версионированным validation package.

## Общие правила для всех чатов

- Читать `AGENTS.md` и `work/delegation/README.md`; не изменять `sources/`.
- Перед работой: `git fetch origin`, чистый `main`, синхронизация fast-forward; при dirty/diverged состоянии остановиться.
- Проверять prerequisites по коду и тестам, а не по словам пользователя.
- Не добавлять dependencies, migrations, новые бизнес-требования или временные mock production paths.
- Использовать серверный organization/user context; не принимать tenant identity от клиента.
- Сначала targeted tests, затем достаточный общий gate; PostgreSQL-critical test не считается PASS при skip.
- Перед push: проверить scope/diff, commit только разрешённых файлов, `git fetch origin`, `git rebase origin/main`, повторить проверки и `git push origin HEAD:main`.
- При конфликте, failing check, scope overflow или расхождении канонического контракта ничего не push и сообщить blocker.
