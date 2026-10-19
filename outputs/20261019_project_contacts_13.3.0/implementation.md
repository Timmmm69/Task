# Task — контакты и компании проекта 13.3.0

Дата: 2026-10-19. Ветка: `feature/project-detail-sections`. База: `4accd66` (13.2); перед ней `283bcd4` (13.1). Точный итоговый коммит указан в `source-receipt.json` пакета. В `main` этап не опубликован.

## Поведение

- В существующей карточке проекта появился сворачиваемый раздел «Контакты и компании». Загрузка начинается при открытии раздела; повторное открытие перечитывает серверное состояние.
- Отображаются только исходящие `project_contact`. Контакт и компания имеют разные подписи типов; разрешённое имя получено через действующий CRM API. `RelatedTitle` и UUID не используются как отображаемое имя.
- Object Links не содержит тип цели. При загрузке выполняется GET контакта, и только при 404 — GET компании. При 401/403/ошибке сети перебора типов нет. При привязке и навигации точный тип уже известен и проверяется непосредственно.
- В разделе сохраняются только ID, тип и разрешённое имя цели. Телефоны, почта, адреса, заметки, налоговые и юридические сведения не копируются в проектную карточку.
- «Открыть в CRM» повторно проверяет доступность и направляет событие через существующий shell route `contacts` в `ContactCardViewModel` либо `CompaniesViewModel`. Новые CRM-карточки не создавались.
- «Связать контакт или компанию» использует `ObjectLinkTargetPickerViewModel` / `ObjectLinkTargetPickerView`. После подтверждения проверяется текущая доступность активной цели и выполняется POST с `project_contact` для обоих типов.
- «Убрать связь» удаляет только подтверждённую исходящую Object Link. Контакт, компания, их отношения и остальные связи сохраняются.
- Есть loading, empty, forbidden, server unavailable, error, partial access и offline. Empty появляется только после успешного чтения. Для недоступной цели используется нейтральная подпись, переход запрещён.
- При смене проекта/организации/прав, закрытии раздела, отключении сети, выходе из сессии и Dispose запросы отменяются. Generation-проверка исключает применение поздних ответов даже при игнорировании отмены транспортом. 401 из списка, metadata, picker либо записи завершает сессию раздела/хоста.

## Общая версия проекта

`ProjectObjectLinksState` хранит единственную монотонную CAS-версию для обоих разделов. `ProjectsViewModel` подключает к ней файлы и CRM, независимо от способа создания child view models. Подтверждённая версия обновляет выбранный проект; уже открытый editor draft сохраняет захваченную версию для обнаружения конфликта.

Одна mutation lease запрещает одновременные записи из файлов и CRM. Lease снимается и при ошибке. Запоздалый снимок со старой версией не может откатить общую версию или показать ложный успех. Конфликты обрабатываются одинаковым сообщением и требуют явного обновления раздела; автоматического повтора записи нет. Изменение одного раздела не очищает данные другого.

## Действующие API

| Операция | Контракт |
|---|---|
| Доступность проекта | `GET /api/v1/projects/{id}` — существующая проверка хоста |
| Чтение связей | `GET /api/v1/objects/{id}/links?limit=200[&cursor=...]`, все страницы, стабильный ETag |
| Поиск цели | `GET /api/v1/object-link-targets?q=...&types=contact,company` через существующий picker |
| Имя/доступность контакта | `GET /api/v1/contacts/{id}` |
| Имя/доступность компании | `GET /api/v1/companies/{id}` |
| Привязка | `POST /api/v1/objects/{id}/links`, `sourceObjectId`, `targetObjectId`, `linkType=project_contact`, `If-Match`, idempotency key |
| Удаление связи | `DELETE /api/v1/objects/{id}/links/{linkId}`, `If-Match`, существующий idempotency key |

После POST/DELETE существующий клиент перечитывает связи. Названия не предполагаются в ответе Object Links. Не добавлены API, таблицы, поля Project DTO или изменения модели контактов/компаний.

## Permissions и серверные ограничения

- Чтение: `Project.Read`, `Contact.Read`, `ObjectLink.Read` и подтверждённый сервером проект.
- Привязка: дополнительно `Project.Update`, `ObjectLink.Create`, `Contact.Update`, активные проект и цель. Picker не требует `Search.Use`.
- Удаление: `Project.Update`, `ObjectLink.Delete`; клиент не требует `Contact.Update` для удаления, поскольку сервер удаляет только связь источника.
- Навигация: разрешённое чтение и повторная CRM-проверка цели; возможность редактирования CRM не требуется.
- Capabilities используются как UI gate. Действующий сервер `PostgresProductApiLinks` проверяет видимость, lifecycle, ETag, source object action, project-level write, target `Contact.Update` при создании и uniqueness. Эти проверки не заменены клиентскими предположениями.

## Изменённые файлы

Полный точный список — `changed-files.txt` пакета. Основные группы:

- `ViewModels/ProjectContactsViewModel.cs`, `ProjectsViewModel.Contacts.cs`, `Views/ProjectContactsView.xaml(.cs)` — новый раздел и его независимый lifecycle.
- `ViewModels/ProjectObjectLinksState.cs`, `ProjectFilesViewModel.cs`, `ProjectsViewModel.Files.cs` — общий ETag и lease.
- `ProjectsViewModel.cs`, `ProjectsViewModel.Sections.cs`, `ProjectDetailSectionViewModel.cs`, `Views/ProjectsView.xaml(.cs)` — подключение и контекст; offline-текст теперь не обещает наличие очищенных данных.
- `Modes/CorporateApplicationContext.cs`, `ViewModels/MainWindowViewModel.cs` — production composition и существующая навигация.
- `Projects/ProjectContactsTests.cs`, `ProjectContactsIntegrationTests.cs`, `ProjectContactsWindowsTests.cs`, `Work/ProjectContactsTransportTests.cs` — автоматизированные проверки.
- `.project-dashboard/roadmap.json` — только evidence/note PROD-04, проценты и статус не повышались; выполнены dashboard order и validate.

## Границы готовности

Результаты и ограничения фактической проверки — в `validation-report.md`. Этап 13.3 предназначен для дальнейшей интеграции и финальной приёмки 13.4. Новые project comments, history, global search, deployment и CRM-модуль не реализовывались. Live стенд компании и визуальная ручная приёмка всех реальных ролей остаются отдельной проверкой.
