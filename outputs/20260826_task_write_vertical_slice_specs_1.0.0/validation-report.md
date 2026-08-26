# Validation report — Task write vertical slice specifications 1.0.0

## Результат

Комплект технических заданий подготовлен как последовательная цепочка из шести самостоятельных промтов. Каждый промт содержит собственный контекст, prerequisites, канонические reference files, production scope, запреты, acceptance criteria, tests, stop conditions и процедуру commit/rebase/push.

Статус проверки: **PASS**.

## Проверенная исходная точка

- Repository: `C:\Users\novik\Таск`.
- Branch: `main`.
- Baseline commit: `2b6a7685788c29e36a8b0ecec3bafc569c42d949`.
- На момент подготовки divergence с `origin/main`: `0 0`.
- Рабочее дерево до создания этого documentation package было чистым.

## Проверенные контракты

- существующие `TaskAggregate`, `TaskLifecycleService`, aggregate/read stores и PostgreSQL migration catalog;
- текущие protected read-only Task endpoints и Task permission bridge;
- существующий Desktop Task DTO/API client/ViewModel;
- Stage 2.2 Task create, patch и transition paths;
- strong ETag `"vN"`, `If-Match`, `Idempotency-Key`, replay headers и stable errors;
- normative durable idempotency protocol из Stage 2.2 corrections;
- canonical `iam.idempotency_records`, `governance.domain_events` и `governance.outbox_messages` schema definitions;
- visual foundation completion report и объявленный следующий increment.

## Проверка разбиения

- Серверная transaction foundation является общей последовательной зависимостью и вынесена первым заданием.
- Create, patch и transition выполняются последовательно, чтобы разные чаты не изменяли одновременно общие endpoint, permission и command contracts.
- Desktop transport начинается только после стабилизации всех server responses.
- WPF UX и реальный end-to-end gate завершают цепочку и формируют итоговый validation package.
- Параллельный запуск явно запрещён во всех координационных инструкциях комплекта.

## Граница scope

Комплект не объявляет полной реализацией всего Stage 2.2 Task object. Write vertical slice намеренно ограничен уже поддерживаемыми production persistence полями: title, priority, start, deadline и status. Неподдерживаемые fields должны отклоняться, а не игнорироваться. Project hierarchy, description, assignees, watchers, recurrence и остальные поля остаются отдельными будущими increments.

## Проверка полноты файлов

- `README.md` — порядок, зависимости, граница и ожидаемый итог.
- `01_SERVER_TRANSACTION_FOUNDATION.md` — самостоятельный prompt.
- `02_SERVER_TASK_CREATE.md` — самостоятельный prompt.
- `03_SERVER_TASK_PATCH.md` — самостоятельный prompt.
- `04_SERVER_TASK_TRANSITION.md` — самостоятельный prompt.
- `05_DESKTOP_WRITE_API_CLIENT.md` — самостоятельный prompt.
- `06_DESKTOP_WRITE_UX_AND_E2E.md` — самостоятельный prompt.
- `VERSION` — версия комплекта.
- `manifest.json`, `SHA256SUMS`, `Verify-Manifest.ps1` — integrity verification.

## Известные ограничения

- Технические задания не являются выполненной реализацией; PASS относится к полноте и внутренней согласованности пакета спецификаций.
- Первый server foundation шаг остаётся высокосложной и высокорисковой задачей; его нельзя запускать параллельно с остальными.
- Каждый следующий чат обязан проверять prerequisites по актуальному коду `origin/main`, а не доверять только сообщению предыдущего исполнителя.
