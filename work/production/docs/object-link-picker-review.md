# Object Link Picker — независимая проверка 10.3.0

Дата: 2026-10-17. Ветка: `feature/object-link-target-picker`.
База проверки: `736a306` (10.1), `5587a49` (10.2), `origin/main=57fa015` после fetch.
Серверный контракт: `PostgresProductApiLinks.cs`, `PostgresProductApiSearch.cs`,
`ProductEndpoints.cs`, `ProductApiContracts.cs`, migration `010_product_api.sql`.
Серверные правила, БД и OpenAPI не изменялись.

## Исправления

1. После AuthenticationFailure чтения/записи сохранялись строки связей и активные команды.
   Теперь данные очищаются, сессия блокирует команды до UpdateAccess с восстановленной сессией.
   Истечение сессии при поиске также инвалидирует связи и незавершённый read-back.
2. UpdateAccess с теми же capability names сохранял строки предыдущего object scope.
   Теперь каждый access-context update отменяет запросы и очищает кэш связей и picker.
3. Отказ чтения 403/404 сохранял ранее доступные связи и выбранную цель.
   Теперь строки/цель очищаются, записи требуют обновления доступа и сверки с сервером.
4. Некооперативный клиент мог вернуть success после отмены link command.
   Теперь cancellation проверяется перед Apply; неопределённая запись требует reconciliation.
5. Клиент исключал разрешённые сервером project_contact, task_project и related.
   Добавлены эти типы; related ищет только читаемые типы, поддерживаемые глобальным поиском.
   Personal сохраняет прежнюю локальную матрицу из четырёх типов.

Регрессии: `ObjectLinksAccessRegressionTests`, дополнительные строки matrix/name-selection
в `ObjectLinkTargetPickerTests`, `ObjectLinksIntegrationTests`, `DesktopObjectLinkTargetSearchTests`,
обновлённые WorkHub/native WPF checks. `PostgresObjectLinkPickerContractTests` проверяет
семь типов, idempotency replay/collision, дубли, версии, независимые Create/Delete,
исчезновение источника/цели, archived/trashed target и trashed source на реальной PostgreSQL.

## Матрица сервера и клиента

| Тип | Источник | Цель | Дополнительная проверка цели |
| --- | --- | --- | --- |
| task_file | task | catalog_item | object-level FileCatalog.Update |
| project_file | project | catalog_item | object-level FileCatalog.Update |
| contact_file | contact/company | catalog_item | object-level FileCatalog.Update |
| task_contact | task | contact/company | object-level Contact.Update |
| project_contact | project | contact/company | object-level Contact.Update |
| task_project | task | project | read/access; отдельного target Update нет |
| related | task/project/contact/company/catalog_item/calendar_event/interaction | любой доступный active resource сервера | read/access |
| parent_reference | запрещён для Object Links | — | используется отдельный catalog move |

Для всех строк: `ObjectLink.Read` при чтении; `ObjectLink.Create` при создании;
`ObjectLink.Delete` при удалении. Create не требует Delete, Delete не требует Create.
Удаление выполняется с source version и link ID, только со стороны source.
Self-link запрещён; обе стороны должны быть видимы в текущем tenant;
source/target при создании должны быть active. Архивные связи можно читать,
trashed endpoints исключаются из списка. Завершённая/отменённая task не допускает мутации.

Source action: task — соответствующий ObjectLink.Create/Delete; project — Project.Update
плюс project membership/write; contact/company — Contact.Update; catalog_item —
FileCatalog.Update; calendar_event — CalendarEvent.Update; interaction — Interaction.Update.
Клиент проверяет известные source capabilities; object-level action окончательно проверяет сервер.
Отсутствие глобального target Update не эквивалентно запрету на конкретный объект:
клиент не вводит такое ограничение. Сервер отклоняет недоступную цель безопасным сообщением.

Поиск требует Search.Use и resource read: Task.Read, Project.Read, Contact.Read,
FileCatalog.Read, Calendar.Read (клиент поддерживает существующий alias Task.Read), Employee.Read.
Search snapshot повторно проверяет tenant, scope и lifecycle при выдаче следующих страниц.
Подтверждение хранит ObjectId конкретной строки; одинаковые названия различаются типом,
временем обновления и ID. Ручной UUID в корпоративном picker не используется.

## Проверенные сценарии и уровни доказательств

- ViewModel/transport: быстрый ввод/debounce; смена типа/source, включая ABA; закрытие/dispose;
  отмена; завершение сессии; revoke/scope update; повторяющийся cursor; пустая страница;
  ошибка следующей страницы; недоступность/восстановление; ограничение страниц и dedupe.
- Create/delete: подтверждённый ID, одинаковые названия, несовместимые типы, дубли/self-link,
  отсутствующие source/target, серверная validation, 403/401/404/409/412/503,
  неуспешный read-back после успешного POST, повторная команда, отмена, поздний ответ.
  UI изменяет строки только после подтверждённого успешного чтения; неопределённые записи
  требуют обновления перед повторной отправкой. Если запрос повторяет transport, используются
  те же исходные If-Match и idempotency key; новая пользовательская команда получает новый key.
- Native WPF: реальные Window/UserControl на Windows, bindings, Enter/Down/Escape,
  focus, automation names, embedding, кнопка Связать, смена типа, narrow layout.
  PNG evidence сохранены в output package и визуально проверены.
- Регрессии: общий Desktop suite включает WorkHub, поиск/навигацию/пагинацию,
  Employee Picker, Personal SQLite selection/save/delete и работу без Corporate клиента.
- Сервер: endpoint/API/permission suite плюс полный domain/PostgreSQL suite на изолированном
  loopback PostgreSQL 16. Это не сквозной тест установленного клиента с live Corporate API.

## Ограничения и приёмка

1. Серверный related допускает также network_resource, notification и inbox_item как targets,
   но глобальный Search API их не выдаёт. Picker охватывает все восемь доступных поиску
   resource types: task/project/contact/company/catalog_item/calendar_event/interaction/employee_profile.
   Полное покрытие произвольных related targets не подтверждено и требует отдельного
   согласованного механизма discovery. Серверные правила не сужались.
2. Без Search.Use корпоративный picker недоступен. Разрешения не расширялись;
   отдельный q-list fallback не реализован. Personal глобальный серверный поиск не использует.
3. Унаследованный GetLinks adapter отклоняет списки с hasMore после первых 200 строк.
   Полная пагинация существующих связей остаётся ограничением прежнего адаптера.
4. Полный Desktop gate имеет унаследованное падение TaskContextActionsUiTests на
   `Assert.True(company.Activate())`, строка 62. Оно воспроизведено на чистом `5587a49`.
   Этот тест не изменялся; общий gate не объявляется зелёным.
5. Narrator, физические Tab/keyboard прогоны, mixed-DPI и ручной установленный клиент
   с реальной корпоративной сессией не проверялись. Native tests используют программные
   WPF events/focus/automation providers и тестовые клиенты. Tenant/access проверены серверными
   тестами, но не матрицей реальных корпоративных учётных записей через UI.

| DoD | Результат |
| --- | --- |
| Выбор по названию; UUID не нужен при доступном Search | PASS для восьми searchable target types |
| Сохраняется реальный выбранный ID | PASS |
| Матрица соответствует всему контракту | PARTIAL: семь link types, но related targets вне Search недоступны |
| Permissions не ослаблены; Create/Delete независимы | PASS, client + server |
| Строки после подтверждения сервера; version conflict | PASS |
| Сеть/авторизация; нет устаревших результатов | PASS в автоматизированных сценариях |
| Personal не сломан | PASS в общем Desktop suite |
| Тесты добавлены и выполнены | PASS; точные счётчики в validation-report.json |
| Нет известных новых необработанных регрессий | PASS в проверенном scope; inherited UI gate и discovery ограничения выше |

Изменения можно представить на PR для review. Полную функциональную приёмку всех
related targets и безусловно зелёный общий gate объявлять нельзя. Точные результаты,
список файлов, SHA-256 и commit исправлений находятся в отдельном output package.
