# Task notifications 1.1.0 — Corporate reminder management

Дата: 09.10.2026. Статус порученной серверной части: **READY**.
Ручная Windows acceptance исключена пользователем из scope. Готовность всей
системы по прежней полной Definition of Done этим отчётом не объявляется.

## Результат

Закрыт ранее отсутствовавший Corporate reminder API: list/get/upcoming,
create/patch/cancel, snooze/dismiss и восстановление через reschedule.
Сервер владеет сроками, статусами, историей и действиями. Клиент не подменяет
их локальными данными. Добавлен доступный из центра уведомлений редактор:
поиск задачи/события, пять типов времени, создание и редактирование,
отмена, восстановление, закрытие срабатывания, отсрочка по сохранённой
настройке/5/15/30/60 минут/завтра/собственной дате. Snooze подключён к Corporate
popup callback с повторной проверкой текущего уведомления и срабатывания.

DefaultReminderOffsetMinutes и DefaultSnoozeMinutes теперь доступны в Corporate.
Пустой отступ в новой форме означает сохранённую серверную настройку.
Сохранённый default snooze читается непосредственно перед действием; несохранённый
черновик настроек не изменяет действие и не затирается. Personal остаётся отдельным.

## Контракт и защита

Нормативный Stage 2.3.1 использует Reminder.ManageOwn для всех reminder routes.
Управление разрешено только authenticated recipient, включая administrator.
Проверяются текущий source scope, active target, permissions и replay access.
Правило и срабатывание имеют независимые strong ETag. Migration 15 добавляет
occurrence version; worker updates тоже повышают её. Lock order rule→occurrence
защищает If-Match от гонки с claim/delivery.

Нет изменений прежних migrations или sources/. Migration 15 сохраняет имеющиеся
данные и добавляет capability применимым system role templates и существующим
system roles. Проверен upgrade с прежней базы, включая lifecycle/backfill.
Для rollout серверу требуется применить pending migrations существующим
migrator до запуска новой версии клиента. Deployment на сервере заказчика не выполнялся.

Неоднозначность baseline (reminder ID в URI при occurrence If-Match) закрыта
документированным opt-in includeOccurrence=true. Строгие baseline DTO сохраняют
свой набор полей; desktop запрашивает currentOccurrence и разрешённый targetTitle.
Lease tokens, worker IDs и внутренние counters не возвращаются.
Контракт: source-overlay/work/production/docs/reminder-api.md.

Create/snooze/restore используют существующий idempotency store. Different body
с тем же key отклоняется; повтор не создаёт новую запись. State, audit, event,
outbox и command result коммитятся атомарно. Абсолютное время нормализовано до
PostgreSQL microseconds, исключая лишние срабатывания из-за округления.

Доставленная история не переписывается. При snooze создаётся один successor,
текущий unread notification становится dismissed. Редактирование с прежним
сроком сохраняет occurrence. Restore возвращает never-delivered cancelled
occurrence; повторная доставка того же исторического due time запрещена.
Изменённые/завершённые/отменённые/архивные цели worker сверяет до доставки.

## Проверки

Release build: PASS.
Полный gate: **2182 passed / 0 failed / 0 skipped**:
738 Desktop + 608 ServiceHosts + 836 Domain/Infrastructure.
Использована настоящая изолированная PostgreSQL 16 на loopback с runtime role,
а не «успешный» пропуск DB tests без connection.
После последней правки nullable default offset: повторная Release build и
5 targeted desktop reminder/client tests PASS.

Команды:

    dotnet build work/production/Task.sln --configuration Release
    dotnet test work/production/Task.sln --configuration Release --no-build

Проверены пять triggers и persisted default; create/replay, patch без смены due,
смена due, stale rule/occurrence ETag, cancel/restore/replay, delivery→snooze→dismiss,
history retention, cancel после claim, completed target, malformed input,
canonical DTO, runtime source/permission revocation перед replay, atomic rollback.
Desktop tests проверяют pagination/partial failure, own recipient, разные ETag,
keys, default snooze, retained draft при conflict, отсутствие зависимости CRUD
от Notification.ReadOwn и in-flight response после отзыва доступа.
Route policy tests автоматически включили все девять новых маршрутов.

Независимая актуальная проверка security/concurrency/DTO/UI: новых Critical,
High или Medium findings не осталось. git diff --check PASS. Dashboard schema
и ordering проверены, progress не повышался. Evidence/TRX и SHA-256 — в пакете.
Прежние xUnit1031/ASPDEPR004 warnings остаются в logs.

## Границы

Ручные banner/click/sound/lock/sleep/logon проверки не выполнялись по указанию
пользователя. Изменения первой задачи (Corporate event producer, background
agent, Windows presenter, Personal actions/settings, tray lifecycle) входят
в этот совокупный source overlay и публикуются вместе с reminder management.
Самостоятельная генерация overdue событий вне настроенных reminder rules
не вводилась: это отдельная бизнес-политика, не часть закрываемого API.

Commit/push выполняется только для source scope, данного итогового пакета и
собственных dashboard изменений. Прежние несвязанные рабочие файлы не включаются.
