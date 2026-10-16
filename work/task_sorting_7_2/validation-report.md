# Task — фильтры и сортировка / 7.2.0 / 2026-10-16

Реализовано в `feature/task-list-filtering-sorting`, рабочая копия `work/task_filtering_7_1/repo`. Ветка готова к Pull Request с указанными ниже границами проверок. Push и создание PR не выполнялись. Изменения основной рабочей копии сохранены.

| Этап | Статус | Реализация | Тесты | Коммит |
|---|---|---|---|---|
| 7.1. Фильтрация | Реализована, повторная регрессия PASS | Четыре типизированных комбинируемых фильтра, локальные разрешённые страницы, честный partial result | В составе 1119 desktop PASS; исходный пакет содержит 986 PASS | `d302e66e02725fe27c3ee79595b4f94cc837b2a8` |
| 7.2. Сортировка | Реализована, проверки доступной среды PASS | Восемь вариантов, единая цепочка DTO → filters → sort → rows, selection/card, version-aware pagination | 33 новых случая; итоговые 1119 desktop + 174 API + 85 domain PASS; 1 PostgreSQL SKIP | `687b3877c64af98d691dc2ef23978b604cfa8f98` |

Свежий `origin/main` (`eb8e0da27c720f3b897caa3f8ecaf894e1d63465`) включён merge-коммитом `0006b85`. Этап 7.1 сохранён. В `main` эта ветка не публиковалась; изменения этапа 6 из main прошли общую desktop-регрессию совместно с этапами 7.1/7.2.

## Значения и правила

Фильтры:

- Статус: все; New, InProgress, Review, Completed, Cancelled, с русскими подписями. «Просрочена» — условие срока, не шестой статус.
- Проект: все; доступные проекты по `Card.ProjectId`; «Без проекта». Отсутствующие разрешённые имена не раскрываются.
- Приоритет: все; Low, Normal, High, Critical.
- Срок: все; просроченные; сегодня; ближайшие семь календарных дат D…D+6; без срока.

Сортировка: `UpdatedAtUtc` новые/старые; срок ближайшие/поздние; приоритет высокий/низкий; название А–Я/Я–А. По умолчанию — последнее изменение, сначала новые. Enum в интерфейс не выводится.

Все равенства разрешаются по `Id` по возрастанию, независимо от направления. Nullable updated/deadline всегда в конце. Приоритеты сравниваются явным business rank: Critical, High, Normal, Low. Названия сравниваются `ru-RU`, без учёта регистра; null безопасно приравнивается к пустой строке для сравнения. Сами DTO не изменяются.

Проверенный действующий domain rule `TaskRules.IsOverdue` даёт `DeadlineAtUtc` приоритет над `Card.DeadlineDate`. Общий helper фильтров и сортировки переводит точный момент в календарь пользователя, а DateOnly оставляет календарной датой. Сначала сравнивается календарная дата; внутри неё точные сроки сравниваются по UTC (включая DST fold), date-only следует после точных сроков этого дня, поскольку действует весь день. Задачи без срока следуют в конце в обоих направлениях. Названия и текст форматированных дат в сравнениях дат не участвуют.

Сортировка сохраняется при reset filters, refresh, повторной активации раздела и ошибках сети в активной сессии; постоянное хранение не добавлено. Локальное изменение не вызывает detail/page/write requests. Видимая выделенная строка переиспользуется, поэтому WPF binding не сбрасывает selection/card. Исчезновение выбранной задачи обрабатывается штатным безопасным поведением текущего VM; скрытый локальным фильтром выбор очищается без запроса чужой карточки.

## Подтверждённые дефекты и исправления

1. До 7.1 статусный фильтр зависел от представления просрочки, отсутствовали полные типизированные совместные priority/deadline filters. Исправления 7.1 проверены повторно, включая DST, D+6/D+7 и permissions.
2. Предыдущая дедупликация `group.Last()` позволяла старому DTO из следующей cursor-страницы заменить подтверждённую новую версию после save. Теперь выбирается максимальная `Version`, а при равенстве — последняя полученная запись. Дубли первой страницы тоже удаляются. Полный refresh заменяет разрешённую выборку, не удерживая исчезнувшие записи.
3. После добавления сортировки панель уменьшала viewport списка до нуля в 800×480. Для высоты менее 600 DIP подписи заменяются сохранёнными UIA names/tooltips, поля становятся компактнее (30 DIP); WrapPanel переносит строки. Проверено, что видимая строка списка и pagination доступны. Остальные разделы не рестилизованы.
4. Старые order-sensitive filter tests предполагали server order. Они проверяют состав фильтра независимо от порядка и явно выбирают целевую задачу; порядок отдельно покрыт новой матрицей. Pagination test теперь проверяет общий порядок после добавления страницы.

## Изменённые файлы этапа 7.2

- `.project-dashboard/roadmap.json`: только DESK-02, evidence/note/date; progress/status не повышены.
- `work/production/src/Task.Desktop/MainWindow.xaml`: sort ComboBox, UIA name/ID `TasksSortOrder`, Tab, локальная адаптация панели.
- `work/production/src/Task.Desktop/ViewModels/TaskFilters.cs`: общий helper календарного срока.
- `work/production/src/Task.Desktop/ViewModels/TaskSorting.cs`: восемь typed orders и чистое сравнение DTO.
- `work/production/src/Task.Desktop/ViewModels/TasksViewModel.cs`: options/selected order, единый pipeline и version-aware dedup.
- `work/production/tests/Task.Desktop.Tests/Tasks/TaskSortingTests.cs`: 33 новых детерминированных случая.
- `work/production/tests/Task.Desktop.Tests/TaskFilteringWindowsTests.cs`: реальный WPF, bindings/selection/card, восемь orders, размеры/DPI equivalents, Tab, load more, 10k measurements/virtualization.
- `work/production/tests/Task.Desktop.Tests/Tasks/TaskFilteringTests.cs`: состав и явный выбор, не зависящие от server order.
- `work/production/tests/Task.Desktop.Tests/Tasks/TasksViewModelTests.cs`: интеграция следующей страницы в default sort.

В `source.zip` находятся итоговые production/test-файлы обоих этапов, изменённые относительно актуального main. `feature.patch` содержит их совокупный diff. Отчёт, manifest, VERSION, SHA-256 и evidence входят в пакет.

## Реально выполненные проверки

| Проверка | Результат / evidence |
|---|---|
| Release build Desktop.Tests, Desktop и Domain/Application | PASS, `build-release.txt`; XAML компилируется в сборке WPF |
| Фильтры, новые sort cases, существующий TasksViewModel, native WPF | 112 PASS, `targeted-final.trx` |
| Последняя точечная проверка sorting после усиления removed-selection assertion | 35 PASS, `sorting-final.trx`; включает 33 новых случая и 2 существующих sort tests |
| Desktop regression без отдельного physical-focus теста | 1118 PASS / 0 FAIL / 0 SKIP, `desktop-final.trx` |
| Существующий physical mouse/focus тест отдельно | 1 PASS, `context-native-final.trx` |
| API endpoints, card, PATCH, transitions, permission authorization | 174 PASS / 0 FAIL / 0 SKIP, `api-integration.trx` |
| Task query/cursor, aggregate/status/card/update | 85 PASS / 0 FAIL / 1 SKIP, `domain-query-final.trx` |
| `npm run dashboard:order`, `npm run dashboard:validate` | PASS; общая готовность не повышалась |
| `git diff --check`, scope review | PASS; изменения server/API/DB/OpenAPI/permissions/personal отсутствуют |
| DesktopTasksApiClient сравнен с main по git blob | Идентичен: `74c501d1920c43f53fe0e874409c4e5629f6fb4b`; параметры sort не добавлены |
| Артефакты | Проверены CRC ZIP, список/размеры/SHA-256 manifest и source.zip против git blobs исходного коммита |

Общий результат без повторного подсчёта одних тестов: **1378 PASS, 0 FAIL, 1 SKIP**. Точечные прогоны входят в desktop-regression и не прибавляются повторно. Существующий physical-focus сценарий изолирован из-за известной конкуренции нативных окон, как на 7.1; тест не ослаблялся.

Первый промежуточный прогон выявил пять устаревших order expectations; затем native layout check поймал нехватку места. Они исправлены и повторные gate проходят. Два прежних предупреждения xUnit1031/xUnit2031 и предупреждения об устаревшем WebHostBuilder в посторонних server tests не менялись.

## Матрица итоговой регрессии задачи №7

| Сценарий | Проверка |
|---|---|
| 1–4. Статус, проект, приоритет, срок | Typed filter unit tests; UI ComboBox bindings; timezone/DST/null/terminal statuses |
| 5. Совместные фильтры | `Filter_CombinesAllConditionsPreservesSelectionAndRefreshes`, `Sort_FiltersResetSelectionDetailsAndSessionStayLocal` |
| 6. Сброс | Все четыре фильтра, сохранение sort, selection/card и отсутствие запросов |
| 7–10. Четыре поля сортировки | Все восемь направлений, ties, nulls, Cyrillic/case/empty/unusual titles, DST UTC order |
| 11. Смена порядка после выбора | VM и реальный WPF ListBox сохраняют выбранный объект/ID и details; detail count не растёт |
| 12. Следующая страница | Полный merge-sort, duplicates, stale versions после save, network error/retry, partial count/load more |
| 13. Save | Подтверждённый DTO перемещается при смене priority/title/deadline/updated; filters применяются снова, без дублей |
| 14. Между разделами | Deactivate/Activate сохраняет order; existing shell/journey desktop tests PASS |
| 15–16. Соединение | VM connectivity loss/recovery, server-unavailable page response/retry; offline sort не читает/не пишет |
| 17. Read-only | Все orders доступны при Task.Read, mutation calls = 0; existing read-only tests PASS |
| 18. Нет permissions | 401/403 очищают canonical set; sort/reset не восстанавливают недоступные задачи; API authorization tests PASS |
| 19. Loading/empty | Existing TasksViewModel states и empty page with cursor/filter-empty, pagination recovery PASS |
| 20. Keyboard | Настоящий WPF focus traversal: status → project → priority → deadline → sort → reset, UIA names/IDs |

Native Windows 10.0.26200, .NET 10.0.11, фактический DPI 150%. Проверены 800×480, 1200×900 DIP; 1536×864 / 1280×720 / 960×540 DIP — layout equivalents для 1920×1080 при 125/150/200%. Это не физическое переключение масштабов экрана. PNG сохранены; просмотрены 800 и 960 DIP. Кнопка догрузки остаётся видимой/включённой в каждом проверенном viewport.

На WPF dispatcher с 10 000 загруженными DTO один sample на направление дал 9.03–14.40 ms для filter/sort/projection + binding notification. После каждой операции UI render drain выполнен отдельно; дальние контейнеры не материализуются. Это локальный замер, не percentile benchmark и не гарантия на другом ПК. Полные значения/окружение — `sorting-performance.txt`.

## Границы и незакрытые проверки

- Real PostgreSQL PATCH atomic/durable/concurrency test **SKIP**: `TASK_POSTGRES_TEST_ADMIN_CONNECTION` не задан. API integration выше использует TestServer/fixtures, а не подтверждение реальной PostgreSQL.
- Ручной end-to-end с живым сервером/реальным отключением LAN, Narrator, аппаратные 125%/200% и mixed-monitor transitions не выполнялись. Есть автоматизированная проверка offline/connectivity и layout equivalents; они не выдаются за ручной live walkthrough.
- GET `/api/v1/tasks` по-прежнему не имеет server sort. Сортируются только загруженные разрешённые страницы; объём и наличие следующей страницы указаны пользователю. Нет автоматической полной загрузки. Глобальная сортировка всех серверных задач требует отдельного согласованного API-этапа.
- Отдельный таймер пересчёта фильтров при смене суток и постоянное хранение предпочтений не добавлялись.
- Известных незакрытых дефектов, вызванных этими изменениями, нет. Ограничения среды выше остаются deployment/manual smoke. Общая готовность всего продукта не объявляется: dashboard сохраняет существующий статус остальных этапов.

Результат: оба этапа задачи №7 реализованы, проверены в доступной Windows-среде и готовы к ревью ветки. Включение в main и публикация не выполнялись.
