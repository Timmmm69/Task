# Проверка этапа 13.1

Версия **13.1.0**, дата **2026-10-19**, ветка `feature/project-detail-sections`.
База `dcbaca241868f2afb4e6506473b8957ab72aa8b3`; повторный fetch подтвердил отсутствие расхождения с origin/main перед коммитом.

Release-сборка всего `work/production/Task.sln`: **PASS**, 0 ошибок, 11 существующих предупреждений.
Финальный общий desktop gate: **1621 PASS / 0 FAIL / 0 SKIP**.

| Прогон (TRX в evidence) | PASS | FAIL | NOT RUN |
|---|---:|---:|---:|
| baseline.trx | 85 | 1 | 0 |
| desktop-confirmation.trx | 1621 | 0 | 0 |
| desktop-final.trx | 1618 | 1 | 0 |
| projects-first.trx | 71 | 1 | 0 |
| sections-final-targeted.trx | 115 | 0 | 0 |
| sections-targeted.trx | 102 | 1 | 0 |

Baseline — код до изменений. Его единственный сбой: существующий ViewState native test,
авария дочернего CLR при shutdown. В последующих профильном и финальном общем прогонах он PASS.
В первом профильном прогоне после правок обнаружен несогласованный fake GET после успешного
создания; fixture приведён к серверному read-back (проверка сохранения результата оставлена).
Следующий неуспех — новый тест переключения: fixture GET возвращал название первого проекта
для второго Id; fixture исправлен. В первом полном desktop-прогоне один FAIL у существующего
CommandPalette Debounce_SearchesOnlyLatestValidQuery_PreservesServerRanking; код этого теста и
реализации не менялся. На финальном общем прогоне он PASS. Все исходные TRX сохранены.

Проверены AC-1–5: существующие list/selection/edit/archive/member/picker/task/create-from-project
сценарии; independent member/role failure; запрет GET без permission; серверный 403 и auth failure;
запрет offline-записей, сохранение подтверждённых результатов; cancellation и поздний ответ при
смене A→B→A, организации, logout, отзыве прав, закрытии, collapse, deactivate и offline.
Смена организации проверена также при одинаковом project id. Пустое состояние подтверждается
только успешным ответом; роли/имена могут дать PartialAccess без сбоя обзора.

AC-6: native Windows WPF tests PASS — фокус всех трёх Expander, Next focus traversal,
ExpandCollapse automation, прежние picker/editor/task actions. Это автоматизированная проверка
WPF-фокуса и доступности через automation; отдельного физического нажатия клавиш, Narrator
или ручной live-server приёмки этим прогоном не заявляется. Скриншоты в evidence/screenshots;
related-tasks-partial.png визуально проверен. Новый ключ раскрытия overview проходит persistence
round-trip; старые ключи members/tasks сохранены.

API-клиент, DTO, маршруты, permissions, БД, бизнес-статусы и алгоритм обхода задач не менялись.
Файлы, контакты/компании и история остаются неподключёнными расширениями. Комментарии не добавлены.
Постоянный offline-кэш и очередь записей не добавлены. PostgreSQL/server suites не запускались:
изменений сервера и схемы нет. Live Corporate API/NAS/Narrator/physical mixed-DPI не проверены.

Dashboard order и validate PASS; обновлён только PROD-04, progress/status не повышены.
Source diff проверен; sources/ и исходная грязная рабочая копия не изменены.
Изменённые файлы — changed-files.txt; структура, state/access matrix и пошаговое подключение
связанных файлов — implementation-and-next-stage.md.

Manifest содержит размеры и SHA-256 каждого файла пакета, включая snapshots изменённых файлов,
patch и receipts. ZIP и manifest имеют отдельные SHA-256. Пакет проверен повторным чтением.
Коммит остаётся локальным в feature-ветке; push/merge в main не выполнялся.
