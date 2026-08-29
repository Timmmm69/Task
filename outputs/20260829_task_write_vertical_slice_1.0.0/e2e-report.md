# Real E2E report

- Стек: release WPF + production HTTPS API + PostgreSQL 16.14
- Миграция: `6`
- Итог: `REAL TASK WRITE E2E PASSED`

## Сценарий

1. Production migrator развернул чистую PostgreSQL-базу до версии 6.
2. Реальный admin login заполнил Windows DPAPI credential vault; WPF загрузил список по HTTPS.
3. Задача создана и отредактирована через WPF; расписание введено в локальном времени.
4. Внешний PATCH изменил version; stale-save получил conflict, UI не перезатёр серверные данные и потребовал ручно повторить правку.
5. API был остановлен перед save: UI сохранил черновик, а повтор после запуска API успешно записал его.
6. Через UI пройдена цепочка `new -> in_progress -> in_review -> completed` с подтверждением терминального действия.
7. После перезапуска API задача осталась `completed`, priority `high`, version `7`.
8. Read-only account получил GET `200`, write `403`; WPF показал задачи без доступных write-действий.
9. Same-key replay дал ровно по одной task/audit/event/outbox/idempotency записи.

## Durable assertions

Для UI-задачи: 7 audit entries, 7 domain events, 7 outbox messages, 7 completed idempotency records. Точные машиночитаемые значения находятся в `evidence/db-assertions.json`.
