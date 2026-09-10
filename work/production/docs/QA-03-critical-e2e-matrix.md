# QA-03 — матрица критических пользовательских E2E

Статус: закрыта на чистом воспроизводимом стенде 2026-09-10 (package
`outputs/20260910_qa03_critical_e2e_1.0.0`).

## Принцип

Каждый критический пользовательский сценарий проверяется сквозным образом без
подмен основного пути: свежий кластер PostgreSQL 16, production migrator, production
HTTPS API с ephemeral секретами и доверенным локальным сертификатом, реальный
Release WPF, управляемый через native UI Automation, и реальный production worker
для доставки напоминаний/outbox. Все данные синтетические и детерминированные.

## Матрица сценариев

| ID | Критический сценарий | Слой проверки | Gate |
|----|----------------------|---------------|------|
| E2E-01 | Сотрудник выбирает сервер и входит; сессия восстанавливается | PostgreSQL + HTTPS API + Release WPF | QA-02 clean-stand (базовый), повторно в QA-03: вход admin и сессия desktop |
| E2E-02 | Задача: создание, отображение, изменение, смена статуса | PostgreSQL + HTTPS API + Release WPF UIA | QA-03 Gate: UI create + серверная проверка строки |
| E2E-03 | Сегодня: расписание, задачи без времени, просроченные | PostgreSQL + HTTPS API + Release WPF UIA | QA-03 Gate: seeded today items видны в разделе «Сегодня» |
| E2E-04 | Календарь: сетка диапазона, события, повторения, участники | PostgreSQL + HTTPS API + Release WPF UIA | QA-03 Gate: range API, создание события/серии, видимость события в сетке |
| E2E-05 | Проекты: список, создание, детали, участники, архив | PostgreSQL + HTTPS API + Release WPF UIA | QA-03 Gate: seed через API, создание через UI, members через API |
| E2E-06 | Файлы: запись каталога, путь, resolve, открытие только после явного действия | PostgreSQL + HTTPS API + Release WPF UIA | QA-03 Gate: seed + locations + resolve через API; создание записи через UI |
| E2E-07 | Контакты: создание и отображение | PostgreSQL + HTTPS API + Release WPF UIA | QA-03 Gate: seed + создание через UI |
| E2E-08 | Глобальный поиск по задачам, проектам, контактам, файлам | PostgreSQL + HTTPS API + Release WPF UIA | QA-03 Gate: API search по каждой сущности + поиск в UI |
| E2E-09 | Уведомления: доставка worker-ом, список, чтение, прочитать все | PostgreSQL + production Worker + HTTPS API + WPF UIA | QA-03 Gate: due reminder → worker delivery → API + UI read |
| E2E-10 | Сеть: потеря сервера → офлайн-оболочка → восстановление | Release WPF UIA + остановка/запуск production API | QA-03 Gate: offline shell, write lock, явный retry |
| E2E-11 | Конфликт версий: конкурентное изменение задачи | PostgreSQL + HTTPS API + Release WPF UIA | QA-03 Gate: серверный PATCH → UI conflict → reload → сохранение |
| E2E-12 | Перезапуск API не теряет подтверждённые данные | PostgreSQL + HTTPS API + Release WPF | QA-03 Gate: restart + повторная загрузка всех разделов |
| E2E-13 | Права: read-only аккаунт не создаёт проекты/задачи | PostgreSQL + HTTPS API | QA-03 Gate: 403 на Project.Create и Task create |

## Правила стойкости к провалу

- Стенд изолирован в `%LOCALAPPDATA%\TaskE2ERuntime\task-write-e2e`, удаляется и
  восстанавливает Desktop AppData в Cleanup.
- Каждая фаза логируется отдельным файлом `phase-*.log`; все assertion-ы пишутся
  в JSON evidence и подписываются SHA-256 в пакете.
- UIA работает только с real Release WPF без mock/in-memory подмен.
- Офлайн-поведение проверяется остановкой production API и явным refresh, а не
  симуляцией транспорта.

## Граница готовности

Проверка выполнена на локальном стенде с синтетическими данными и локальным
доверенным сертификатом. Живая сеть заказчика, его CA, файловые SMB/UNC-пути,
уведомления на реальных рабочих местах и формальное принятие относятся к
post-handoff deployment и не блокируют готовность приложения.
