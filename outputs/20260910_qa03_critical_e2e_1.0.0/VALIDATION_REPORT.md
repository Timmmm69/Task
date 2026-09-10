# QA-03 validation report

Version: 1.0.0. Date: 2026-09-10. Result: **PASS**.

## Проверенный чистый стенд

- Release solution build: 0 errors.
- Свежий изолированный кластер PostgreSQL 16.14, schema 14 через production migrator.
- Production `Task.Api` на HTTPS с ephemeral секретами и доверенным локальным сертификатом.
- Детерминированный администратор и read-only аккаунт через `/api/v1/auth/login`.
- Все данные синтетические (`qa03-deterministic-v1`); Desktop AppData сохранён и восстановлен.

## Критическая E2E-матрица (реальные проверки)

| Сценарий | Evidence | Результат |
|----------|----------|-----------|
| Сегодня: timed/untimed/overdue/событие на день в Release WPF | `ui-assertions.json`, `today.png` | PASS |
| Задачи: создание через UIA + строка PostgreSQL | `ui-assertions.json`, `tasks-created.png` | PASS |
| Конфликт версий: серверный PATCH → conflict UI → reload → сохранение | `tasks-conflict.png`, `tasks-recovered.png`, task version 3 | PASS |
| Календарь: диапазон, событие, серия повторений в сетке | `api-assertions.json`, `calendar.png` | PASS |
| Проекты: seed, members, создание через UI | `projects.png`, `product-db-assertions.json` | PASS |
| Файлы: запись каталога, путь, resolve, device-scope | `api-assertions.json`, `catalog.png` | PASS |
| Контакты: seed и создание через UI | `contacts.png` | PASS |
| Поиск: task/project/contact/catalog через API и UI | `search.png` | PASS |
| Уведомления: due reminder → production worker → UI read-all | `worker-delivery.json`, `notifications-assertions.json`, `notifications.png` | PASS |
| Сеть: остановка API → офлайн-оболочка и блокировка записи → refresh → восстановление | `offline.png`, `reconnected.png` | PASS |
| Перезапуск API: все сущности переживают restart | `product-db-assertions.json` | PASS |
| Права: read-only 403 на список и создание проектов | `api-assertions.json` | PASS |

Итого: 23 product API checks, worker delivery, 12 persistence checks после restart,
8 notification checks, 12 UI checks — все PASS. Очистка остановила PostgreSQL и API,
восстановила оригинальный Desktop AppData и удалила изолированный runtime.

## Evidence

- `evidence/ui-assertions.json` — все UI checks PASS, conflict task `e85264d6…` версия 3.
- `evidence/api-assertions.json` — 23 checks.
- `evidence/worker-delivery.json` — reminder→notification, change feed 10 строк.
- `evidence/notifications-assertions.json`, `evidence/product-db-assertions.json`.
- `evidence/qa03-gate.log`, `evidence/qa03-release-build.log`, `evidence/phase-*.log`.
- 12 скриншотов реального Release WPF.

## Границы готовности

Стенд использует синтетические данные и локальный доверенный сертификат. Живые
SMB/UNC-пути, уведомления на реальных рабочих местах, корпоративные CA/секреты и
формальное принятие заказчиком относятся к post-handoff deployment и не блокируют
готовность приложения.
