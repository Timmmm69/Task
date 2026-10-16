# Task — Этап 8.1, версия 8.1.0

Дата: 2026-10-16. База: `deb16b3` (`origin/main` на начало работы).
Ветка: `perf/project-related-tasks`. Изменения и отдельный коммит локальные; публикация в main не выполнялась.

## Результат

Задачи больше не входят в `LoadDetailsAsync`. Карточка и участники завершают свою
загрузку независимо от обхода задач. В существующей секции пользователь нажимает
«Загрузить задачи», затем «Продолжить поиск». Каждое нажатие выполняет ровно один
запрос существующего `GetTasksAsync(cursor)`. Сохранённое раскрытие Expander не
запускает запросы. Новый endpoint, серверный фильтр или постоянный кэш не добавлены.

После страницы совпадения сразу публикуются с дедупликацией по Id. До terminal
cursor интерфейс сообщает «Найдено пока N» и не объявляет результат полным/пустым.
Повторный или циклический cursor — ошибка с явным перезапуском, без лимита 200 страниц.
Обычная ошибка оставляет текущий cursor для повторения только неудавшейся страницы.

Загрузка принадлежит `AsyncCommand`, использует linked cancellation и проверку
поколения, выбранного объекта, прав, соединения и сессии после await. Сворачивание,
Unloaded, деактивация, Dispose и смена проекта отменяют запрос. Игнорирующий отмену
transport не может опубликовать поздний ответ. При завершении сессии очищаются задачи,
проекты и участники; существующий черновик сохраняется по действующему правилу.
При отзыве чтения / `403` / `404` задачи очищаются. Offline и server unavailable
различаются; ранее подтверждённые строки в текущей разрешённой сессии остаются для чтения.

## Нормативная совместимость

Изучены concept (проект/задачи/видимость), architecture Stage 1 (WPF/MVVM,
offline read-only, access revoked), Stage 2.2 contract recovery и действующий
`TaskEndpoints` (разрешён только cursor без filter/sort), Stage 3.4 UX Architecture
(UXP-06: разные сложные состояния; UXP-07: lazy tabs; UXP-14: не загружать весь набор
до render), screen/state catalog и Stage 4.1.1 Module PRDs (NFR-006: server pagination,
без полной предварительной загрузки). Нормативного требования автоматически сканировать
все задачи при выборе проекта нет; явная порционная загрузка совместима с baseline.
Канонический ZIP Stage 2.2 под исходным именем отсутствует: использованы существующий
распакованный `Organizer_Stage2_Technical_Specification_2.2` и его recovery report;
в `sources/` ничего не изменялось. Сохранены UI-state key `projects/v1/tasks`,
Employee Picker, логика участников, lifecycle и project/task permissions.

## Детерминированное сравнение

Это счётчики и логические контрольные точки тестов, **не миллисекунды реального сервера**.
Фикстура: 100 задач на страницу; `D` — authoritative project/member reads закончились;
`Pₙ` — вручную разрешён ответ n-й страницы. Таймауты только обнаруживают зависание.
Сервер может иметь другой размер страницы; эти числа не являются его benchmark.

Основные поля были доступны уже в `D` и в исходном коде. Подтверждённое изменение:
раньше `IsDetailLoading` оставался true до конца обхода задач; теперь false уже в `D`.
Не заявляется, что исходный UI вообще скрывал название/описание до последней страницы.

| Сценарий | Исходно: запросы / просмотрено | Теперь без просмотра задач | Теперь после 1 нажатия | При полном обходе: запросы / просмотрено |
|---|---:|---:|---:|---:|
| 20 задач, 5 совпадений в начале | 1 / 20 | 0 / 0 | 1 / 20 | 1 / 20 |
| 500 задач, 10 в начале | 5 / 500 | 0 / 0 | 1 / 100 | 5 / 500 |
| 5000 задач, 10 в начале | 50 / 5000 | 0 / 0 | 1 / 100 | 50 / 5000 |
| 5000 задач, 10 только в конце | 50 / 5000 | 0 / 0 | 1 / 100, partial 0 | 50 / 5000 |
| Пустой проект среди 500 чужих задач | 5 / 500 | 0 / 0 | 1 / 100, partial 0 | 5 / 500 |
| Нет Task.Read | 0 / 0 | 0 / 0, Forbidden | команда запрещена | обход запрещён |

| Сценарий | Основные поля / завершение detail loading | Первые связанные задачи: исходно → теперь | Полный результат: исходно → теперь | Отмена |
|---|---|---|---|---|
| 20 / 5 | D / P₁ → D / D | P₁ → P₁ | P₁ → P₁ | теперь отменяет запрос и проверяет generation |
| 500 / 10 | D / P₅ → D / D | P₅ → P₁ | P₅ → P₅ (5 явных нажатий) | те же lifetime guards |
| 5000 / 10 | D / P₅₀ → D / D | P₅₀ → P₁ | P₅₀ → P₅₀ (50 нажатий) | те же lifetime guards |
| Поздние совпадения | D / P₅₀ → D / D | P₅₀ → P₅₀ | P₅₀ → P₅₀ | первые 49 страниц остаются partial |
| Пустой проект | D / P₅ → D / D | нет → нет | P₅ → P₅, Empty только в конце | прерывание не означает Empty |
| Нет Task.Read | D / D → D / D | нет → нет | не применимо | нет активного Task-запроса |
| Быстрый выбор другого проекта | D / до конца старого обхода → D / D | старые строки не публиковались; теперь также отменён запрос | старый обход не даёт результата новой карточке | исходный token не отменялся при deselection; новый отменяется при A→B и A→B→A |
| Ошибка промежуточной страницы | D / ошибка P₂ → D / D | исходно даже найденная первая строка скрыта → первая строка видна после P₁ | исходно пустой список без ошибки секции → ServerUnavailable, список неполный | retry повторяет P₂, не P₁ |

Ошибка/отмена baseline используют маленькую управляемую фикстуру: первая страница
с одной совпавшей задачей, затем заблокированная вторая. Исходно: 2 запроса,
1 просмотренная строка, 0 опубликованных строк; при ошибке список очищался и Message
оставался null. В новом error/retry-тесте после 2 запросов первая строка остаётся,
после третьего (retry P₂) получены 2 строки и Complete. Для отмены/переходов новый
тест задерживает первую страницу: 1 запрос, 0 принятых строк, поздний ответ игнорируется.
Baseline проверяет deselection как границу смены проекта; новые тесты также проверяют
непосредственный выбор B и возврат к A с корректными данными B.

Дополнительно новый тест проходит 201 страницу / 20100 задач с совпадениями на последней,
что проверяет устранение молчаливого обрезания. Другой тест проверяет пустой каталог
(1 явный запрос, 0 строк) и Empty только после его ответа.

## Проверки

- Baseline до изменения production: **9 PASS**, `evidence/baseline.trx`.
- Первые targeted checks нового механизма: **28 PASS**, `evidence/related-tasks.trx`.
- Полный desktop gate: **1149 PASS / 0 FAIL / 0 SKIP**, `evidence/desktop-full.trx`.
- Финальные Projects + ViewState: **82 PASS / 0 FAIL / 0 SKIP**, `evidence/projects-final.trx`.
  Этот запуск включает последнее уточнение приоритета SessionEnded над Offline
  и новый regression test; полный 1149 gate был выполнен непосредственно перед ним.
- Финальная Release solution build: PASS, 0 ошибок / 0 предупреждений, `evidence/build.log`.
  При первичной сборке существующих тестов были предупреждения obsolete WebHostBuilder / xUnit.
- Native WPF: реальный STA/Dispatcher/Window, UIA Invoke, keyboard focus,
  partial/complete text, отключение кнопки во время запроса, collapse/cancel/resume,
  Unloaded/cancel, окончание AsyncCommand. Два PNG в `evidence/wpf/` просмотрены.
- Employee Picker и UI-state regressions включены в targeted/full gate.
- `git diff --check` PASS. API/DTO клиент и TaskEndpoints сверены с базой;
  `sources/`, OpenAPI, permissions, сервер, БД и зависимости не менялись.
- Manifest и SHA-256 всех файлов, CRC ZIP и извлечённые байты проверяет
  `work/project_related_tasks_8_1/Package.py`. Результаты — `validation.json`.

AC-1 — explicit pages не меняют IsDetailLoading; AC-2 — lifetime и A→B→A;
AC-3 — late/empty partial; AC-4 — capability gating, 0 calls;
AC-5 — intermediate error/retry; AC-6 — 0 calls до нажатия;
AC-7 — terminal cursor + DistinctBy(Id); AC-8 — repeated/cyclic cursor Failed.
Проверены terminal auth, capability revoke, offline/read-only, транспортное исключение,
MalformedResponse, InvalidCursor, отмена с исключением и игнорирующий отмену fake.
Новых внешних подписок нет: обработчики принадлежат самому View, command Dispose
отменяет активное выполнение. Lifecycle-тесты проверяют завершение command; отдельный
длительный heap/leak profiling не проводился.

## Изменённые файлы

Все пути ниже относительно корня репозитория; точные копии находятся в `source/` пакета.

| Файл | Изменение |
|---|---|
| `work/production/src/Task.Desktop/ViewModels/ProjectsViewModel.cs` | отделение задач, lifetime карточки и session guards |
| `work/production/src/Task.Desktop/ViewModels/ProjectsViewModel.RelatedTasks.cs` | новый автомат состояний и одна управляемая cursor-страница |
| `work/production/src/Task.Desktop/Views/ProjectsView.xaml` | status/action, существующий Expander/UI-state key |
| `work/production/src/Task.Desktop/Views/ProjectsView.xaml.cs` | отмена при collapse/unload |
| `work/production/tests/Task.Desktop.Tests/Projects/ProjectRelatedTasksTests.cs` | deterministic pagination/lifetime/access/error tests |
| `work/production/tests/Task.Desktop.Tests/Projects/ProjectRelatedTasksWindowsTests.cs` | native WPF/UIA regression |
| `work/production/tests/Task.Desktop.Tests/Projects/ProjectsViewModelTests.cs` | явная загрузка задач, управляемый project fake |
| `work/production/tests/Task.Desktop.Tests/Projects/ProjectEmployeePickerTests.cs` | проверка очистки проекта после terminal auth |
| `work/production/docs/PROD-04-projects-and-members.md` | описание механизма и ограничений |
| `.project-dashboard/roadmap.json` | только PROD-04 evidence/note/date, без изменения progress/status |

В `work/project_related_tasks_8_1/` сохранены исходные baseline tests с инструкцией
воспроизведения и скрипты упаковки/обновления dashboard. В `outputs/` — версия,
отчёт, таблица сравнения, evidence, manifest, SHA-256 и проверенный ZIP.
`npm run dashboard:order` и `npm run dashboard:validate` PASS; общая готовность
dashboard не повышалась (`handoff_ready: false` относится к продукту в целом).

## Ограничения и готовность

Полный набор по-прежнему требует всех страниц доступных задач; серверная выборка
по проекту не ускорена. За один шаг пользователь может получить ноль совпадений,
не исчерпав каталог. Размер страницы задаёт существующий сервер. Cursor не является
новым snapshot-контрактом: изменения каталога между страницами сохраняют прежнюю
семантику API. Дедупликация устраняет повторы Id, но не добавляет snapshot isolation.
Отмена физического HTTP-запроса зависит от transport; поздняя публикация защищена локально.
Нагрузочный live-server benchmark, backend/DB suite, ручной Narrator и аппаратная DPI
матрица не запускались: серверные компоненты неизменны, эти проверки не заявляются.

Этап 8.1 готов к review/integration следующего этапа. Main не изменён. Для дальнейшего
ускорения полного результата нужен отдельно согласованный серверный контракт фильтрации;
такое изменение выходит за текущий scope.
