# Публикационная проверка карточки проекта 13.1–13.4

Дата: 2026-10-19. Версия пакета: 13.4.1. Source commit: `cb50d6e30ef0360aa303bbbfdbd97eb23ecb89a4`.
Общая база: `dcbaca241868f2afb4e6506473b8957ab72aa8b3`. Ветка: `feature/project-detail-sections`.

Независимый review и полный Release gate завершены. Обнаруженная lifecycle-ошибка общей
навигации компаний исправлена. Нерешённых конкретных дефектов в проверенном scope не найдено.
Это не утверждение об отсутствии любых потенциальных ошибок продукта.

| Этап | Реализовано и проверено | Коммит |
|---|---|---|
| 13.1 | Независимые разделы карточки, состояния, отмена и права | 283bcd4 |
| 13.2 | Файлы проекта: каталог, Object Links, read/link/unlink/open | 4accd66 |
| 13.3 | Контакты/компании: связи, права и навигация | d5afec2 |
| 13.4 | История: lazy load, paging/dedup, права, авторы, ошибки | 63babc1 |
| Исправление review | Active lifecycle gate общей карточки компании и 10 regression cases | cb50d6e |

| Полный Release-набор | PASS | FAIL | SKIP |
|---|---:|---:|---:|
| Domain/infrastructure/реальная PostgreSQL 16 | 880 | 0 | 0 |
| HTTP/API/contracts/permissions | 647 | 0 | 0 |
| Desktop/native WPF/Personal SQLite | 1791 | 0 | 0 |
| Всего | **3318** | **0** | **0** |

Release solution build PASS. Тесты выполнены последовательно, без фильтрации/исключений.
Native WPF scenarios всех разделов карточки прошли, включая прежний Window.Activate тест.
Ранее зарегистрированный отказ foreground activation не скрыт: исходные receipts в пакете
13.4.0 остаются историей проверки. Дополнительно lifecycle-targeted: 101 PASS; до исправления
общая CRM integration: 275 PASS; предыдущий project/history-targeted: 268 PASS.

# Проверка перед публикацией 13.1–13.4

Пользователь уточнил, что требуется проверка и публикация именно 13.1–13.4, а не уже
опубликованных CRM 12.1–12.4. Scope: ветка feature/project-detail-sections от origin/main dcbaca2.

Проверены исходные коммиты 283bcd4, 4accd66, d5afec2, 63babc1 и их совместное состояние.
Предыдущие targeted gate: 268 PASS для Projects/history; повтор CRM-интеграции: 275 PASS.

Независимый review обнаружил lifecycle defect в общей CompaniesViewModel, используемой при
переходе из ProjectContacts: успешный GET мог вернуть archived/trashed компанию, а VM публиковала
её карточку и сохраняла выбранную компанию/черновик после refresh.

Исправлено только принятие данных в active companies workspace. Существующие серверные DTO,
маршруты, permissions, БД и политика архива не изменены. Gate active применяется к list page,
повторной проверке выбранной off-page записи, GET карточки, write response/readback и link response.
Недоступная компания очищает строку, выбор, карточку, черновик и связанные контакты/picker;
Details change очищает scope связанных взаимодействий. Неизвестное lifecycle также не публикуется.
Business Status=inactive при LifecycleState=active остаётся поддержан.

Добавлено 10 regression cases: archived/trashed navigation, refresh on/off page с открытым
черновиком, reload, save response, save readback и link response. 101 targeted test PASS после
исправления. Независимый final review исправления не нашёл оставшихся конкретных дефектов.
Основной review ранее проверил WPF bindings, cancellation/session guards, ETag/idempotency и
version synchronization всех разделов карточки. Свежий review общей CRM-интеграции проверил
POST retry payload/key, If-Match, 401/403 cleanup, stale responses, ModeSwitchGuard и навигацию.

Полный gate запускает все три test projects последовательно (-m:1), без фильтров, исключений
или пропусков. Изолированная реальная PostgreSQL 16 останавливается после завершения.
Фактические финальные counters и build/test logs добавляются в итоговый validation report.

Сохраняющиеся ограничения не маскируются под реализованный функционал:
- ISSUE-13.4-COMMENTS: нормативные generic object comments отсутствуют в исполняемом production
  API для project target; ни фиктивной формы, ни новых endpoints/БД в этом инкременте нет.
- Offset history paging не даёт server snapshot при конкурентных новых событиях; dedup/refresh
  не означают гарантии полной консистентной истории в такой гонке.
- Native WPF fixtures выполняются на Windows; ручной live-server production walkthrough,
  Narrator и physical mixed-DPI не выполнялись. Это не подменяется автоматическими тестами.
- История предыдущего full gate с Window.Activate failure и воспроизведением на baseline
  сохраняется в пакете 13.4.0; актуальный результат публикуется отдельно, без переписывания receipts.


## Итоговый gate

Release solution build PASS. Полный Release gate без фильтров/пропусков: domain/PostgreSQL 880,
API 647, desktop/native WPF 1791 — **3318 PASS / 0 FAIL / 0 SKIP**. Прежний native
Window.Activate тест прошёл. TRX и логи: work/project_history_13_4/results/publication-final/.
Нерешённых конкретных дефектов в проверенном scope не найдено. Допущено к запрошенному
fast-forward push в main с сохранением явно перечисленных contract/manual-acceptance ограничений.


Проверенные API: GET /projects/{id}, project members/roles; GET /tasks через существующий
project-related loader; GET/POST/DELETE Object Links с существующими типами связей проекта;
catalog/contacts/companies read APIs и разрешённый resolver; GET /projects/{id}/history;
optional GET /users/{id}. Administrative audit и фиктивные project comments не вызываются.
Права: Project.Read/Update/Create/Archive/ManageMembers по прежним командам; Task.Read/Create
для задач; FileCatalog/Contact/FileReference и sensitive-path capabilities в существующих loaders;
History.Read; optional User.Read. If-Match/idempotency прежних мутаций сохранены.

Полный список изменённых production/docs/test файлов: changed-files.txt; точные файлы
публикационного исходного состояния приложены в source/. Предыдущий подробный сценарный
и contract-gap отчёт: work/production/docs/PROJECT-DETAIL-ACCEPTANCE-13.4.md.

Публикация разрешена пользователем. Пакет подготовлен для fast-forward main; фактический
remote HEAD проверяется после push. Пакет не объявляет проектные комментарии реализованными
и не закрывает ручную customer/live-server приёмку. Результат push сообщается отдельно.
