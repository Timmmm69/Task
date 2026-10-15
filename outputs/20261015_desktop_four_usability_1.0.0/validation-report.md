# Task desktop: четыре улучшения, 1.0.0

Дата: 2026-10-15. Ветка: `fix/desktop-four-usability-improvements`.
Проверенный production-код: `20a43998f515a82104cf7065639ec8ef51fd550a`. База `origin/main`: `c583f200e0e60db3fe3d2b33bf798897d0d99c46`.

| Задача | Статус | Изменённые файлы | Проверки | Коммит |
|---|---|---|---|---|
| 1. Необязательный срок | Ограничение срока исправлено; полный title-only сценарий заблокирован текущей моделью production | InboxViewModel; InboxViewModelTests | 24 теста после задачи; дополнительные no-op/concurrent/title-only проверки; все в финальном suite | 2ba6552; уточнения 20a4399 |
| 2. Пагинация | Реализовано | DesktopWorkApiClient; WorkHubPagination; WorkHubSearch; WorkHubViewModel; CommandPaletteViewModel; WorkHubView; CommandPaletteView; два новых файла тестов | 85 после задачи; 50/101/201, dedupe, cursor cycles, stale query, partial failures, cancellation, expired scope | 0ee8e93; интеграционные проверки 594bff6 и 20a4399 |
| 3. Навигация поиска | Реализовано для существующих экранов; явный fallback для остальных типов | MainWindowViewModel; WorkHubSearch; WorkHubCatalogViewModel; WorkHubViewModel; SearchNavigationTests; WorkHubNavigationTests | 138 после задачи; параметризованная маршрутизация, contact after page 1, access/missing/stale selection | 2965993; уточнения 20a4399 |
| 4. Настройки | Реализовано | WorkHubView; WorkHubViewModel; WorkHubSettingsPresentation; DesktopSettingsPresentationTests; SettingsWindowsTests | 94 после задачи, включая настоящее WPF-окно, binding, клавиатуру, сохранение, reload, ошибки времени/сервера | 594bff6 |

## Подтверждённые ошибки и изменения

1. `CanSaveConversion` и `SaveConversionAsync` действительно запрещали пустой срок. Удалены оба ограничения. Существующий patch DTO сохраняет отсутствие срока как unspecified, без подстановки даты. Обязательное название, concurrency/version и штатная обработка ошибок сохранены. `BuildPatchCommand()` теперь вызывается до захвата mutation gate: no-op больше не оставляет gate захваченным. Повторное нажатие во время записи даёт единственный PATCH. Ответ, который оставляет запись во входящих, больше не сопровождается ложным сообщением о закрытом источнике.
2. `GetPageAsync` игнорировал `nextCursor`. Catalog/tree и notifications уже обходили страницы и не требовали замены. Для contacts, archive, trash и search одна страница загружается за запрос; продолжение доступно кнопкой «Загрузить ещё» на экране и в палитре. Сохраняются q/limit и серверный порядок, cursor экранируется. Объекты объединяются по ID (для поиска — type+ID). Повтор/цикл cursor останавливает продолжение; смена запроса/раздела отменяет старую операцию и проверяет поколение ответа. Сетевая ошибка не уничтожает подтверждённые страницы. Forbidden/authentication failure очищают защищённую выборку, SEARCH_CURSOR_EXPIRED прекращает и очищает search snapshot. Форматы серверных ответов, DTO сущностей и маршруты не менялись; метаданные NextCursor добавлены только в локальный результат клиента.
3. Старый код безусловно заменял ObjectId на ParentObjectId и направлял company в contacts без открытия компании. Теперь parent используется только для file_location. Task, project, calendar_event, catalog_item/file_location и contact открываются через существующую навигацию, с повторной серверной проверкой. Контакт ищется и за первой страницей, устаревшая выборка не переопределяет новую навигацию. Для company, employee_profile, interaction и неизвестных типов отображается сообщение об отсутствии экрана. Comment включён в защитные тесты как неподдерживаемый тип; текущая production search query его не генерирует. Новые экраны не создавались.
4. Язык выбирается из фактически реализованного русского интерфейса; дни недели — по названию, значения 1–7 сохраняются. Формат времени имеет понятные подписи для прежних 12h/24h. Время отображается как ЧЧ:ММ, с сохранением существующих секунд при необходимости, и сериализуется в прежний формат HH:mm:ss. Валидация работает до HTTP, включая дату-only reminder и согласованность тихих часов. Добавлены automation names/IDs и inline сообщения; клавиатурный переход между селекторами проверен в WPF.

## Блокер полного сценария задачи 1

Stage 2.2 OpenAPI (`sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2/openapi/openapi.yaml`, InboxConvertTask, строка 26502) предусматривает отдельный POST `/api/v1/inbox-items/{id}/convert-to-task`, где требуется expectedInboxVersion, а срок не требуется. Локальный оригинальный ZIP с именем из AGENTS отсутствует; прочитан имеющийся распакованный Stage 2.2 и рядом присутствует Repacked ZIP.

В действующем `work/production` InboxViewModel использует GET/PATCH обычных tasks, а `InboxItemViewModel.IsCaptureOnly` вычисляет входящие по отсутствию срока/описания/проекта/прочей классификации. В production нет сохраняемого признака «преобразовано» и нет этого inbox conversion маршрута. У неизменённой записи только с названием BuildPatchCommand возвращает no-op; изменение только названия не меняет IsCaptureOnly. Поэтому гарантировать, что такая запись уйдёт из входящих после перезапуска, невозможно в разрешённых рамках без изменения production-модели/API. Срок теперь необязателен при реальном patch, например при выбранном проекте или введённом описании; эти поля не объявлены обязательными. Новое бизнес-правило не введено. Этот сценарий НЕ объявлен завершённым. Необходимо отдельное согласованное восстановление inbox-контракта/модели.

## Выполненные проверки

Код проверен в чистом снимке Git, извлечённом из коммита 20a43998f515a82104cf7065639ec8ef51fd550a; незакоммиченные пользовательские изменения не участвовали в финальном gate.

```powershell
dotnet restore Task.sln --locked-mode
dotnet build Task.sln -c Release --no-restore --verbosity minimal
dotnet test Task.sln -c Release --no-build --no-restore --logger trx --results-directory <results-clean>
```

Release build: 0 ошибок, 11 предупреждений в существующих тестах (WebHostBuilder ASPDEPR004 и xUnit1031/xUnit2031).

| Suite | VSTest passed | failed | skipped |
|---|---:|---:|---:|
| Task.Desktop.Tests | 851 | 0 | 0 |
| Task.Tests | 848 | 0 | 4 |
| Task.ServiceHosts.Tests | 608 | 0 | 0 |
| Всего | 2307 | 0 | 4 |

В 35 из формально успешных тестов присутствует `NOT RUN`: PostgreSQL test connection не настроен. Эти 35, а также 4 явных пропуска, НЕ являются подтверждением реальной проверки PostgreSQL. Список включён в validation.json. Полный DB gate не запускался; серверная схема/SQL/permission policies не менялись.

Дополнительно: 136 targeted integration tests PASS перед финальным снимком; `Test-ProjectBoundaries.ps1` PASS; `git diff --check origin/main...HEAD` PASS. `npm run dashboard:order` выполнен. Dashboard изменён только в evidence/note PROD-01 и PROD-05, progress/status не повышены. Все исходные незакоммиченные изменения сохранены (9 tracked файлов); WorkHubView.xaml staged по собственным изменениям, без пользовательских правок закрытия окна.

## WPF и регрессии

Windows доступна. Новый SettingsWindowsTests запускает настоящее WPF Window в изолированном тестовом процессе, проверяет bindings, selected value, focus/keyboard navigation, invalid time, сохранение и две страницы поиска. Снимки экрана находятся в ui-clean/; они просмотрены. Это автоматизированная проверка окна с fake backend, а не manual acceptance установленного клиента с корпоративным сервером.

В процессе проверки исправлены: mutation-gate leak после no-op; отключённая после поиска кнопка догрузки (не было CanExecuteChanged при завершении общей операции); блокирование refresh нового раздела, пока отменяется догрузка; сохранение прежнего SelectedContact после отзыва доступа; сохранение устаревшего search snapshot после 410. Повторный финальный gate прошёл. Известных необработанных регрессий в изменённом коде не обнаружено.

Не подтверждены: настоящий PostgreSQL gate, end-to-end корпоративный UI с live сервером, ручная проверка установленного клиента. Неподдерживаемые company/employee/interaction экраны сознательно не создавались.

## Интеграция

Ветка готова к review / draft PR. Полная приёмка задачи 1 и утверждение «все acceptance criteria выполнены» заблокированы описанным расхождением production inbox. Push, создание PR и изменения main не выполнялись. Полный список файлов по каждой задаче и SHA-256 проверенного source overlay находятся в validation.json и manifest.json.
