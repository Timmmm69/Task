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
