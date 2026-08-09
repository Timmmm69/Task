# Архитектура десктопного органайзера компании

**Этап:** 1. Полное проектирование архитектуры системы  
**Статус:** базовая архитектура перед началом разработки  
**Целевая платформа:** Windows-клиенты, локальная сеть компании, локальный сервер  
**Источник бизнес-требований:** концепция продукта, предоставленная заказчиком  
**Версия документа:** 1.0  

---

## 0. Резюме архитектурного решения

### 0.1. Основное решение

Система строится по клиент-серверной архитектуре:

```text
Windows Desktop App
        |
        | HTTPS + WebSocket внутри локальной сети
        v
Local Application Server
        |
        +-- PostgreSQL: бизнес-данные, права, аудит, синхронизация
        +-- Background Worker: напоминания, повторения, обслуживание
        +-- Internal Update Repository: подписанные обновления клиента
        +-- Backup Agent: резервное копирование и проверка восстановления
        |
        +-- SMB/NAS/File Server: физические рабочие файлы, не принадлежащие БД
```

Сервер является единственным источником истины для общих данных. Десктопное приложение хранит локальный зашифрованный кэш только для ускорения чтения и просмотра при кратковременной недоступности сервера. В MVP изменение общих данных без связи с сервером запрещается.

Серверная часть реализуется как **модульный монолит**, а не как набор микросервисов. Все бизнес-модули развёртываются единым приложением, но имеют изолированные границы, собственные контракты и запрещённые прямые зависимости. Это оптимально для одной компании, небольшого количества пользователей и локальной эксплуатации.

### 0.2. Предлагаемый технологический профиль

Точные версии фиксируются перед началом реализации на поддерживаемых LTS-релизах.

| Слой | Рекомендуемая технология | Причина |
|---|---|---|
| Desktop | .NET LTS, WPF, MVVM | зрелая Windows-интеграция, фоновые процессы, tray, уведомления, работа с файловой системой |
| Desktop local storage | SQLite с шифрованием, ключ через Windows DPAPI/Credential Manager | быстрый read-cache, защищённая локальная сессия |
| Backend | ASP.NET Core Web API, модульный монолит | единый стек, строгая типизация, удобная Windows/Linux-эксплуатация |
| Realtime | WebSocket/SignalR-подобный канал | доставка сигналов об изменениях и уведомлений без постоянного polling |
| Database | PostgreSQL | транзакции, индексы, JSON для ограниченных метаданных, полнотекстовый поиск, надёжное резервирование |
| Reverse proxy | Nginx/Caddy/корпоративный reverse proxy | TLS termination, ограничения запросов, единая точка входа |
| Deployment | Linux VM/мини-сервер, контейнеры или systemd services | предсказуемая эксплуатация и переносимость |
| File access | SMB/UNC пути и локальные Windows-пути | соответствует модели хранения файлов компании |
| Monitoring | структурированные логи, метрики, health checks | диагностика локального контура |

### 0.3. Архитектурные принципы

1. **Server authoritative.** Общие данные считаются подтверждёнными только после записи на сервер.
2. **Deny by default.** Любое действие запрещено, пока сервер явно не разрешил его политикой доступа.
3. **Metadata, not file content.** Приложение хранит метаданные и ссылки, но не содержимое рабочих файлов.
4. **OS ACL remains authoritative for physical files.** Права приложения не могут обойти права Windows/SMB.
5. **Soft delete first.** Пользовательские сущности сначала перемещаются в корзину или архив; физическое удаление выполняется отдельным процессом по политике хранения.
6. **Audit is append-only.** История не редактируется обычными пользователями и не каскадно удаляется вместе с объектом.
7. **Optimistic concurrency.** Сервер не применяет слепой last-write-wins при конфликте версий.
8. **Local cache is disposable.** Потеря локального кэша не должна приводить к потере общих данных.
9. **No hidden file operations.** Приложение не перемещает и не удаляет физические файлы автоматически.
10. **Future-ready boundaries.** В каждой общей сущности предусматривается `organization_id`, хотя в первой версии организация одна.

---

## 0.4. Обнаруженные противоречия и принятые решения

| № | Противоречие или пробел | Риск | Принятое архитектурное решение |
|---|---|---|---|
| 1 | В перечне сущностей есть и «Пользователь», и «Сотрудник», но различие не определено | дублирование профиля и прав | `UserAccount` отвечает за вход и безопасность; `EmployeeProfile` — за ФИО, должность, отдел и рабочие данные. Связь 1:1, профиль может существовать до активации учётной записи |
| 2 | Во «Входящих» есть заметки, идеи и ссылки, но отдельной сущности нет | попытка хранить всё как задачу | вводится техническая сущность `InboxItem`, которая позже преобразуется в задачу, заметку каталога, URL или удаляется |
| 3 | Указано «последняя подтверждённая версия сохраняется», но одновременно требуется предупреждение о конфликте | потеря изменений второго пользователя | используется optimistic locking. При несовпадении версии сервер возвращает конфликт и не перезаписывает данные молча |
| 4 | При потере сервера изменения «либо блокируются, либо сохраняются локально», затем для MVP разрешён read-only | неоднозначная реализация | в MVP общие изменения блокируются. Разрешены просмотр кэша, локальные уведомления и открытие доступных файлов |
| 5 | Есть «Календарное событие», но детально описаны почти только задачи | размытая модель календаря | вводится отдельный `CalendarEvent` для встреч и событий без статуса исполнения. В UI задачи и события представлены общей проекцией `ScheduleItem` |
| 6 | Каталог содержит текстовые заметки, но отдельной сущности нет | смешение файла и текста | `CatalogItem` имеет тип: virtual_folder, file_reference, folder_reference, web_link, text_note |
| 7 | Права приложения на файл могут не совпадать с Windows ACL | ложное ощущение доступа | доступ проверяется в два этапа: сервер разрешает видеть метаданные, клиентская ОС разрешает открыть физический путь |
| 8 | Возможны пути с буквами дисков и UNC | один путь работает не на всех ПК | общие пути хранятся как UNC; буква диска допускается только как device-scoped location |
| 9 | Для одного элемента допускается несколько путей, но порядок выбора не задан | открытие неверной копии | вводится приоритет: локальный путь текущего устройства, общий UNC, другие разрешённые location по приоритету; пользователь видит выбранный путь |
| 10 | Администратор должен управлять сервером и бэкапами из приложения | опасное смешение бизнес-UI и системного администрирования | создаётся отдельный Admin module и Management API; операции ограничены, журналируются и не дают произвольный shell-доступ |
| 11 | Фотография пользователя является бинарным содержимым, хотя рабочие файлы не загружаются | формальное противоречие | аватары считаются системными вложениями малого размера и хранятся отдельно от рабочих файлов с лимитом и антивирусной проверкой |
| 12 | Удаление ссылки не должно удалять файл, но не задано поведение удаления папки каталога | риск каскадного удаления ссылок | удаление виртуальной папки перемещает в корзину только записи каталога; физические файлы не затрагиваются. Для непустой папки требуется явное подтверждение |
| 13 | Поиск указан по файлам, но содержимое файлов не индексируется | ожидание полнотекстового поиска по документам | в MVP поиск выполняется только по метаданным: имя, путь, описание, теги и связям |
| 14 | Пользователь может видеть историю «доступных объектов» | утечка данных после потери доступа | история фильтруется текущими правами. События скрытого объекта недоступны, кроме системного администратора с разрешением Audit.ReadAll |
| 15 | Смена пути файла может быть выполнена любым пользователем с доступом к объекту | подмена ссылки на вредоносный путь | изменение location требует отдельного права FileLocation.Update и проходит схему валидации/аудита |

### 0.5. Допущения, принятые из-за отсутствия технических требований

| Область | Допущение |
|---|---|
| Масштаб MVP | до 300 активных сотрудников, до 100 одновременных соединений, до 2 млн задач и событий за несколько лет |
| Доступность | целевая доступность в рабочее время 99,5%; кластер высокой доступности не обязателен в MVP |
| RPO | не более 15 минут для базы при использовании архивирования журнала транзакций |
| RTO | до 4 часов для восстановления на резервном сервере |
| ОС клиента | Windows 10/11 корпоративных редакций, фактически поддерживаемые компанией |
| Время | сервер хранит UTC; часовой пояс пользователя/компании применяется при отображении и расчёте локальных повторений |
| Язык | архитектура допускает локализацию; первая поставка может быть только русской |
| Сетевой доступ | локальный DNS-адрес, например `organizer.company.local`; IP в настройках не является основным идентификатором |
| Вирусная защита | физические файлы проверяет корпоративная защита конечных точек; сервер проверяет только системные вложения |
| SLA файлового хранилища | находится вне ответственности приложения, но его доступность мониторится и отображается |

---

# 1. Общая архитектура системы

## 1.1. Контексты выполнения

Система разделяется на три независимых контура:

1. **Desktop execution context.** Код, выполняемый на рабочем компьютере пользователя.
2. **Application server context.** Бизнес-логика и единый источник данных.
3. **File infrastructure context.** Windows File Server, NAS, SMB-папки и локальные диски устройств.

Приложение не становится файловым прокси. При открытии файла сервер не передаёт его байты. Сервер возвращает только разрешённые метаданные и location; десктоп проверяет путь и просит Windows открыть его через зарегистрированное приложение.

## 1.2. Физическая топология

```text
+--------------------------------------------------------------------------------+
|                              ЛОКАЛЬНАЯ СЕТЬ КОМПАНИИ                           |
|                                                                                |
|  +--------------------+        +--------------------+                           |
|  | Windows PC A       |        | Windows PC B       |                           |
|  | Desktop App        |        | Desktop App        |                           |
|  | Tray Agent         |        | Tray Agent         |                           |
|  | Encrypted Cache    |        | Encrypted Cache    |                           |
|  +---------+----------+        +----------+---------+                           |
|            | HTTPS/WSS                    | HTTPS/WSS                           |
|            +----------------+-------------+                                     |
|                             v                                                   |
|                +-----------------------------+                                  |
|                | Reverse Proxy / TLS         |                                  |
|                +--------------+--------------+                                  |
|                               v                                                 |
|                +-----------------------------+                                  |
|                | Organizer Application       |                                  |
|                | Modular Monolith            |                                  |
|                | REST API + Realtime Hub     |                                  |
|                | Background Workers          |                                  |
|                +----------+---------+--------+                                  |
|                           |         |                                           |
|                 SQL/TLS   |         | backup/control                            |
|                           v         v                                           |
|                 +-------------+   +------------------+                           |
|                 | PostgreSQL  |   | Backup Storage   |                           |
|                 +-------------+   +------------------+                           |
|                                                                                |
|  +----------------------+  +----------------------+  +----------------------+   |
|  | Windows File Server  |  | NAS / SMB Share      |  | Local PC Files       |   |
|  | \\server\share       |  | \\nas\documents     |  | D:\Work\...         |   |
|  +----------------------+  +----------------------+  +----------------------+   |
+--------------------------------------------------------------------------------+
```

## 1.3. Логическая архитектура сервера

```text
API Host
|
+-- Identity & Session Module
+-- Authorization Policy Module
+-- Organization & Directory Module
+-- Task & Recurrence Module
+-- Calendar Module
+-- Project Module
+-- File Catalog Module
+-- Contact & Counterparty Module
+-- Comment Module
+-- Notification Module
+-- Search Module
+-- Audit & History Module
+-- Archive & Trash Module
+-- Sync & Change Feed Module
+-- Administration Module
|
+-- Shared Infrastructure
    +-- Transaction Manager
    +-- Outbox Publisher
    +-- Job Scheduler
    +-- Realtime Connection Manager
    +-- Database Access
    +-- Structured Logging
    +-- Metrics and Health Checks
```

Модули не обращаются к таблицам других модулей напрямую. Межмодульная связь выполняется через:

- синхронные application contracts;
- доменные события внутри одной транзакции;
- integration events через outbox после фиксации транзакции;
- read-model запросы через согласованные представления.

## 1.4. Потоки данных

### 1.4.1. Командный поток

Команды изменяют состояние:

```text
UI action
  -> Desktop command handler
  -> API request with session + object version
  -> authentication
  -> authorization
  -> validation
  -> transaction
  -> aggregate update
  -> audit record
  -> change-feed record
  -> outbox event
  -> commit
  -> API response
  -> realtime invalidation to other clients
```

### 1.4.2. Запросный поток

Запросы не меняют состояние:

```text
UI query
  -> Local cache check
  -> if fresh: render immediately
  -> server query in background or on demand
  -> authorization-aware filter
  -> read model / indexed SQL query
  -> response
  -> local cache update
  -> UI refresh
```

### 1.4.3. Событийный поток

Серверные события не должны содержать полную карточку объекта. Они передают минимальную информацию для инвалидации:

```text
ObjectChanged {
  sequence,
  object_type,
  object_id,
  operation,
  version,
  occurred_at
}
```

Клиент после события запрашивает разрешённое актуальное состояние. Это исключает передачу лишних полей через общий realtime-канал и уменьшает риск утечки при изменении прав.

## 1.5. Границы ответственности

| Контур | Отвечает | Не отвечает |
|---|---|---|
| Desktop | UI, локальный кэш, tray, Windows-уведомления, проверка доступности пути от имени пользователя, запуск файла | окончательная авторизация, межпользовательская согласованность, хранение общей истины |
| Application server | бизнес-правила, права, транзакции, синхронизация, аудит, уведомительные события | чтение файлов с локальных дисков сотрудников, обход SMB ACL, автоматическое перемещение файлов |
| PostgreSQL | долговечность структурированных данных, транзакции, индексы | бизнес-решения без application layer, хранение рабочих документов |
| File infrastructure | физическое хранение и OS ACL | права внутри проектов и задач, каталогизация, история связей |
| Backup subsystem | копии БД, конфигурации, ключей и проверка restore | резервирование локальных файлов сотрудников, если они не включены в корпоративную политику backup |

## 1.6. Почему не микросервисы

Микросервисы в MVP создадут отдельные базы, распределённые транзакции, сложное развёртывание, service discovery, больше сертификатов, логов и точек отказа. Для одной компании эта цена не оправдана. Модульный монолит сохраняет:

- единые ACID-транзакции;
- простое резервное копирование;
- один API endpoint;
- возможность выделить Notifications, Search или Identity позже;
- меньшую нагрузку на локального администратора.

---

# 2. Архитектурные схемы

## 2.1. Общая схема

```text
[Пользователь]
      |
      v
[Desktop UI]
      |
      +--> [Local Read Cache]
      |
      +--> HTTPS/WSS
              |
              v
       [Reverse Proxy]
              |
              v
       [Application Server]
              |
      +-------+--------+----------------+
      |                |                |
      v                v                v
[PostgreSQL]    [Background Jobs]   [Realtime Hub]
      |
      v
[Backup Repository]

[Desktop File Adapter] ---> [Windows Shell] ---> [Local/SMB File]
```

## 2.2. Взаимодействие компонентов

```text
Desktop Shell
 |
 +-- Task UI -----------+
 +-- Calendar UI -------+--> Application Service Facade
 +-- Project UI --------+           |
 +-- Catalog UI --------+           +--> API Client
 +-- Contacts UI -------+           +--> Local Repository
 +-- Notifications UI --+           +--> Realtime Client
                                     +--> Command Queue (online-only)

Server API
 |
 +--> Authentication Pipeline
 +--> Authorization Pipeline
 +--> Module Application Service
 +--> Domain Model
 +--> Repository / Unit of Work
 +--> PostgreSQL Transaction
 +--> Audit + ChangeFeed + Outbox
```

## 2.3. Жизненный цикл запроса изменения

```text
1. User action
2. Client-side format validation
3. Request correlation ID generated
4. Access token attached
5. HTTPS request sent
6. Server authenticates session
7. Server loads authorization context
8. Policy engine evaluates action
9. DTO validation
10. Aggregate loaded with current version
11. Expected version compared
12. Business invariants checked
13. Transaction begins
14. Data updated
15. Audit event appended
16. Change sequence allocated
17. Outbox event inserted
18. Transaction committed
19. Response returned
20. Client updates cache
21. Outbox worker publishes realtime signal
22. Other clients invalidate and refresh
```

## 2.4. Открытие файла

```text
[User presses Open]
        |
        v
[Desktop requests FileCatalogItem]
        |
        v
[Server checks metadata permission]
        |
        v
[Server returns allowed locations]
        |
        v
[Desktop ranks locations for current device]
        |
        +--> no candidate --> "Нет подходящего пути"
        |
        v
[Path syntax and scheme validation]
        |
        v
[Async availability probe with timeout]
        |
        +--> not found --> relink actions
        +--> access denied --> permission error
        +--> resource unavailable --> network error
        |
        v
[Explicit user confirmation for risky file type]
        |
        v
[Windows ShellExecute/Open]
        |
        v
[Open attempt result logged locally; server receives non-sensitive status event]
```

## 2.5. Синхронизация

```text
[Client starts]
   |
   +--> authenticate/refresh session
   |
   +--> send last_sync_sequence
   |
   v
[Sync API]
   |
   +--> validate current access scope
   +--> read ChangeFeed after cursor
   +--> filter inaccessible objects
   +--> return upserts, tombstones, new cursor
   |
   v
[Client transactionally updates SQLite cache]
   |
   v
[Client opens realtime channel from cursor]
   |
   +--> ObjectChanged signal
   +--> fetch authoritative object
   +--> update cache and UI
```

## 2.6. Авторизация

```text
[Login form]
   |
   v
[HTTPS /auth/login]
   |
   +--> rate limit by IP/device/login
   +--> load account
   +--> verify status
   +--> verify password hash
   +--> update failed-attempt counters
   +--> create device session
   +--> issue short access token + rotating refresh token
   |
   v
[Desktop stores refresh secret in Windows Credential Manager]
   |
   v
[Every API request]
   |
   +--> token validation
   +--> session active check
   +--> account active check
   +--> authorization policy
```

## 2.7. Уведомление

```text
[Task/Reminder changed]
   |
   v
[Server commits reminder definition]
   |
   +--> change feed to clients
   +--> server job computes due recipients
   |
   v
[Notification event persisted]
   |
   v
[Realtime signal to connected device]
   |
   v
[Desktop notification scheduler]
   |
   +--> Windows toast now
   +--> local schedule for future trigger
   +--> action button sends command to server
```

---

# 3. Компоненты системы

## 3.1. Desktop Application Shell

**Назначение:** единая точка запуска UI, маршрутизации и состояния приложения.

**Ответственность:**

- загрузка конфигурации подключения;
- инициализация DI-контейнера;
- открытие главного окна и tray agent;
- отображение статусов подключения и синхронизации;
- маршрутизация deep links из уведомлений;
- завершение и безопасный logout.

**Хранит:** только runtime-state интерфейса и ссылки на сервисы. Не хранит бизнес-данные самостоятельно.

**Взаимодействует:** со всеми desktop-модулями, updater, local cache и API client.

**Запрещено:** выполнять SQL напрямую, принимать решения о серверных правах, открывать файл без File Access Adapter.

## 3.2. Desktop API Client

**Назначение:** типизированный транспорт к серверу.

**Ответственность:**

- HTTPS-запросы;
- correlation ID;
- access token;
- повтор только идемпотентных запросов;
- единая обработка 401, 403, 409, 422, 429 и 5xx;
- deadline/timeout;
- отмена запросов при закрытии экрана.

**Хранит:** временные connection pools и access token в памяти.

**Запрещено:** бесконечно повторять команды, логировать пароли/токены/полные описания задач.

## 3.3. Realtime Client

**Назначение:** получение сигналов о серверных изменениях.

**Ответственность:**

- WebSocket-соединение;
- reconnect с backoff и jitter;
- продолжение с последнего cursor;
- дедупликация sequence;
- передача событий Sync Coordinator.

**Запрещено:** считать payload realtime-события полной и авторитетной карточкой объекта.

## 3.4. Sync Coordinator

**Назначение:** согласовать локальный read-cache с сервером.

**Ответственность:** bootstrap, incremental sync, применение tombstone, пересинхронизация после смены прав, атомарное обновление cursor.

**Хранит:** `last_sync_sequence`, время последней успешной синхронизации и состояние sync.

**Запрещено:** отправлять offline-команды в MVP, сливать конфликтующие версии самостоятельно.

## 3.5. Local Cache

**Назначение:** ускорение UI и read-only режим при недоступности сервера.

**Хранит:** только доступные текущему пользователю проекции задач, проектов, календаря, контактов, каталога, уведомлений и справочников.

**Не хранит:** password hash, серверные secrets, полный аудит всей компании, объекты без текущего доступа, содержимое рабочих файлов.

**Правила:**

- отдельная база на пользователя и устройство;
- шифрование;
- TTL для чувствительных проекций;
- полное удаление при logout/отзыве устройства при следующем запуске;
- локальная БД может быть удалена и восстановлена с сервера.

## 3.6. File Access Adapter

**Назначение:** единственный модуль, взаимодействующий с Windows Shell и файловой системой.

**Ответственность:** нормализация пути, выбор location, проверка существования, различение ошибок, открытие файла/папки, безопасная обработка URL.

**Запрещено:** сканировать весь диск, автоматически искать перемещённый файл, менять ACL, копировать файл на сервер, удалять физический файл без отдельной функции и разрешения.

## 3.7. Tray and Notification Agent

**Назначение:** фоновая работа, локальные уведомления и действия из toast.

**Ответственность:** автозапуск, поддержание минимального процесса, локальное расписание ближайших уведомлений, получение realtime событий, повторная сверка после сна/гибернации.

**Запрещено:** хранить собственную независимую копию бизнес-правил дедлайна; сервер остаётся источником расчётов, клиент отвечает за отображение.

## 3.8. Server API Host

**Назначение:** единая точка бизнес-запросов.

**Ответственность:** middleware pipeline, маршрутизация в модули, единый формат ошибок, request limits, observability.

**Запрещено:** содержать бизнес-логику в контроллерах, возвращать stack trace клиенту, доверять role/department из payload клиента.

## 3.9. Identity and Session Module

**Назначение:** учётные записи, пароль, sessions, lockout.

**Хранит:** username, password hash, salt/parameters, account status, failed attempts, lockout, sessions, hashed refresh tokens, password history.

**Запрещено:** хранить пароль в обратимом виде; передавать hash клиенту; использовать employee email как неизменяемый primary key.

## 3.10. Authorization Policy Module

**Назначение:** единообразно разрешать операции над объектами.

**Ответственность:** global role, department relation, project membership, project role, ownership, assignee relation, object state, explicit grants/denies.

**Запрещено:** полагаться на скрытую кнопку в UI; разрешать доступ только по факту знания object ID.

## 3.11. Task and Recurrence Module

**Назначение:** задача, подзадача, checklist, статусы, сроки, повторения.

**Граница агрегата:** задача является корнем; checklist items и один уровень подзадач изменяются через команды задачи. Комментарии и файлы связаны отдельными агрегатами для снижения конфликтов.

**Запрещено:** сохранять статус `просрочена` как вручную редактируемый статус; он вычисляется из deadline и terminal state.

## 3.12. Calendar Module

**Назначение:** временные представления задач и независимые календарные события.

**Ответственность:** диапазонные запросы, пересечения, рабочие часы, time zone, drag/resize команды.

**Запрещено:** загружать календарь за все годы одним запросом; запрещено интерпретировать локальное время без time zone.

## 3.13. Project Module

**Назначение:** проекты, участники, проектные роли и настройки доступа.

**Ответственность:** жизненный цикл проекта, membership, owner/manager, проектные permissions, связи с задачами/контактами/файлами.

**Запрещено:** удалять участника, если это оставляет проект без владельца; каскадно удалять задачи при удалении проекта.

## 3.14. File Catalog Module

**Назначение:** виртуальное дерево и метаданные внешних файлов.

**Ответственность:** catalog hierarchy, file locations, links to domain objects, tags, last observed availability.

**Запрещено:** читать содержимое рабочего файла на сервер, считать сохранённый путь доказательством доступа, принимать произвольную executable URI scheme.

## 3.15. Contact and Counterparty Module

**Назначение:** физические лица, организации и история взаимодействий.

**Ответственность:** карточки, отношения person-company, контактные каналы, связи с задачами/проектами/файлами.

**Запрещено:** автоматически отправлять письма/сообщения в MVP; хранить секреты, платёжные реквизиты и специальные категории персональных данных без отдельного требования.

## 3.16. Notification Module

**Назначение:** определения напоминаний, события уведомлений, read/unread и действия.

**Ответственность:** дедупликация, recipient calculation, delivery state, snooze, quiet hours metadata.

**Запрещено:** гарантировать Windows toast, когда устройство выключено; считать realtime delivery подтверждением показа пользователю.

## 3.17. Search Module

**Назначение:** единый поиск по разрешённым метаданным.

**Ответственность:** индексируемые проекции, ranking, filters, snippets, access-aware query.

**Запрещено:** индексировать содержимое рабочих файлов в MVP; возвращать количество скрытых результатов.

## 3.18. Audit and History Module

**Назначение:** неизменяемая история действий.

**Ответственность:** actor, device, timestamp, action, object, old/new normalized fields, correlation ID, security event category.

**Запрещено:** хранить пароли, токены и бинарные файлы; позволять обычному приложению update/delete audit rows.

## 3.19. Change Feed and Outbox

**Назначение:** надёжная доставка событий после транзакции.

**Ответственность:** монотонный sequence, changed object pointer, tombstone, delivery retry, cleanup retention.

**Запрещено:** публиковать realtime-событие до commit; использовать realtime hub как единственное место хранения события.

## 3.20. Background Worker

**Назначение:** периодические и отложенные операции.

**Задачи:** генерация экземпляров повторений, расчёт уведомлений, очистка истёкших sessions, purge корзины, reindex, backup checks, retention.

**Запрещено:** запускать один job параллельно без distributed lock; выполнять длинную операцию внутри HTTP request.

## 3.21. Administration Module

**Назначение:** безопасные административные действия.

**Ответственность:** пользователи, отделы, роли, devices, sessions, server health summary, backup status, feature flags, configuration subsets.

**Запрещено:** произвольное выполнение команд ОС, просмотр паролей, прямое редактирование БД, скачивание полного backup через обычный UI.

## 3.22. Backup Agent

**Назначение:** резервирование и restore validation.

**Ответственность:** scheduled backups, encryption, retention, checksums, off-host copy, test restore.

**Запрещено:** считать backup успешным только по наличию файла; хранить единственную копию на том же диске, что и production DB.

---

# 4. Архитектура Desktop-приложения

## 4.1. Слои

```text
Presentation
  |-- Views
  |-- ViewModels
  |-- Navigation
  |-- UI State
Application
  |-- Commands
  |-- Queries
  |-- Use Cases
  |-- Validation
Domain Client Model
  |-- Immutable DTO/Read Models
  |-- Local display rules
Infrastructure
  |-- API Client
  |-- Realtime Client
  |-- SQLite Cache
  |-- Credential Store
  |-- Windows Notifications
  |-- File System Adapter
  |-- Logging
```

UI не обращается к API напрямую. ViewModel вызывает use case; use case решает, читать ли кэш, обращаться ли к серверу и как обработать ошибку.

## 4.2. Модуль Shell and Navigation

- единая боковая навигация;
- маршруты `today`, `inbox`, `calendar`, `tasks`, `projects`, `catalog`, `contacts`, `notifications`, `archive`, `trash`, `settings`;
- deep link вида `organizer://task/{id}`;
- сохранение последнего безопасного маршрута;
- запрет восстановления экрана, к которому пользователь потерял доступ;
- единый error boundary.

## 4.3. Authentication Module

- форма входа;
- server discovery/configuration;
- password change flow;
- device session status;
- logout and remote revocation handling;
- secure token storage;
- перевод приложения на login screen при 401/session revoked;
- очистка user-specific cache.

## 4.4. Today Module

Формирует композиционный экран из нескольких серверных проекций:

- tasks by schedule window;
- unscheduled tasks for today;
- overdue computed query;
- review queue;
- waiting queue;
- upcoming reminders.

Модуль не загружает все задачи пользователя. Запрос ограничивается окном дат и отдельными лимитированными секциями.

## 4.5. Inbox Module

`InboxItem` создаётся минимальной командой с `type`, `title/content` и optional URL/path. Затем доступны команды:

- convert to task;
- convert to catalog item;
- attach to project/contact;
- archive;
- delete.

Преобразование выполняется транзакционно: создаётся целевой объект, связи и audit; inbox item получает status `converted`, а не исчезает без следа.

## 4.6. Task Module

Подмодули:

- Task List and Filters;
- Task Details;
- Assignees and Watchers;
- Checklist;
- Subtasks;
- Recurrence Editor;
- Reminder Editor;
- Linked Files;
- Comments;
- History;
- Conflict Resolver.

Редактирование карточки использует draft model. Автосохранение общих данных не рекомендуется для MVP: пользователь явно сохраняет изменения, сервер проверяет версию. Отдельные быстрые команды, например change status или complete checklist item, отправляются немедленно и меняют узкий участок агрегата.

## 4.7. Calendar Module

- virtualized day/week/month views;
- range-based loading;
- drag-and-drop generates `RescheduleTask` or `MoveCalendarEvent`;
- resize generates duration command;
- overlap warning computed locally for instant UI and подтверждается сервером;
- time zone conversion;
- separate display for date-only tasks;
- no hard five-year limitation.

## 4.8. Project Module

- project list read model;
- project dashboard;
- membership editor;
- project calendar;
- tasks/files/contacts tabs;
- permissions summary obtained from server as capabilities, not calculated from role name locally.

## 4.9. File Catalog Module

- lazy-loaded virtual tree;
- breadcrumbs;
- catalog item details;
- location list;
- availability indicator per current device;
- drag-and-drop inside virtual tree changes parent only;
- adding a Windows file creates metadata after user selection;
- no recursive import of folder contents by default;
- opening uses File Access Adapter.

## 4.10. Contacts Module

- people and companies lists;
- detail card;
- person-company relations;
- tasks/projects/files tabs;
- interaction timeline;
- duplicate warning based on normalized email/phone/name, but no automatic merge.

## 4.11. Notifications Module

- notification center;
- unread counter;
- local toast mapping;
- actions: open, complete, snooze, reschedule;
- idempotency key for action buttons;
- schedule reconciliation after reconnect, Windows sleep and clock change.

## 4.12. Search Module

- debounced server search;
- minimum query length for broad search;
- keyboard navigation;
- grouped results;
- per-type filters;
- local recent-item search while server unavailable;
- no claim that local cache search is complete.

## 4.13. Local Cache Module

Recommended local tables are implementation detail, but logical groups are:

- current user/session metadata;
- dictionary data;
- task/calendar projections;
- project summaries;
- contact summaries;
- catalog tree and file metadata;
- notifications;
- sync cursors and tombstones.

All writes to local cache occur in one local transaction per sync batch. Cursor advances only after successful application of the entire batch.

## 4.14. Settings Module

Settings are divided:

- **server-managed:** security policy, allowed network roots, retention, global thresholds;
- **user-synchronized:** working hours, first day of week, default reminder, quiet hours;
- **device-local:** device name, autostart, local cache size, notification permission state, path aliases.

The client must visually distinguish these scopes.

## 4.15. History Module

History is loaded page-by-page for a selected object. It is not permanently cached in full. Sensitive old/new values are masked according to current permission. The UI renders structured fields rather than raw JSON.

## 4.16. Desktop process model

Preferred process model:

```text
Organizer.exe
  +-- Main UI
  +-- Tray lifecycle
  +-- Realtime connection
  +-- Notification scheduler
```

Отдельный Windows service в MVP не нужен: он усложняет установку и межпроцессную авторизацию. При необходимости гарантированных уведомлений при закрытом UI можно разделить процессы позже, сохранив общий Notification Agent interface.

## 4.17. Desktop update architecture

- сервер публикует signed manifest и signed installer;
- клиент проверяет подпись издателя и hash;
- обновление скачивается только по HTTPS;
- update может быть mandatory для несовместимого API;
- поддерживается `minimum_client_version`;
- rollback выполняется установщиком, а не изменением локальной БД вручную;
- кэш мигрируется версионными миграциями и может быть пересоздан.

---

# 5. Архитектура локального сервера

## 5.1. Развёртываемые процессы

Минимальная production-конфигурация:

```text
1. reverse-proxy
2. organizer-api
3. organizer-worker
4. postgresql
5. backup-agent
6. monitoring/log collector (опционально отдельный процесс)
```

API и worker используют одну кодовую базу и общие модули, но запускаются раздельно. Это не позволяет тяжёлым job блокировать HTTP-процесс и упрощает перезапуск.

## 5.2. API-группы

| Группа | Назначение | Примеры |
|---|---|---|
| `/auth` | вход, refresh, logout, password | login, refresh, change-password |
| `/me` | текущий пользователь, capabilities, devices | profile, sessions, preferences |
| `/tasks` | команды и запросы задач | create, update, change-status, query |
| `/calendar` | диапазонные представления | day/week/month range |
| `/projects` | проекты и участники | create, members, permissions |
| `/catalog` | дерево и file locations | children, create-item, relink |
| `/contacts` | лица, компании, interactions | search, create, timeline |
| `/notifications` | центр уведомлений | unread, acknowledge, snooze |
| `/search` | глобальный поиск | query with filters |
| `/sync` | bootstrap и delta | changes after cursor |
| `/admin` | ограниченное администрирование | users, departments, health, backups |
| `/realtime` | WebSocket handshake | authenticated change notifications |

## 5.3. Контракт API

- JSON UTF-8;
- versioned base path, например `/api/v1`;
- UUID/ULID-подобные opaque identifiers;
- ISO timestamps with UTC offset/UTC;
- pagination через cursor, не offset для больших лент;
- `request_id` и `correlation_id`;
- `expected_version` для команд;
- idempotency key для повторяемых команд;
- единый Problem Details-подобный error response.

Пример ошибки конфликта:

```json
{
  "code": "CONCURRENCY_CONFLICT",
  "objectId": "...",
  "expectedVersion": 12,
  "actualVersion": 14,
  "changedFields": ["assigneeId", "deadline"],
  "correlationId": "..."
}
```

## 5.4. Pipeline обработки запроса

```text
TLS termination
 -> request size limit
 -> correlation ID
 -> structured access logging
 -> rate limit
 -> authentication
 -> session/account status check
 -> organization context
 -> endpoint authorization precheck
 -> input validation
 -> application command/query
 -> object-level authorization
 -> transaction if command
 -> response mapping
 -> security headers
```

## 5.5. Транзакционная модель

Одна пользовательская команда должна быть атомарной. В одной транзакции фиксируются:

1. изменение бизнес-сущности;
2. новая версия агрегата;
3. audit entry;
4. change feed row;
5. outbox event;
6. notification intents, если они порождены действием.

Нельзя сначала обновить задачу, затем отдельным незащищённым запросом записать историю.

## 5.6. Фоновые задачи

| Job | Триггер | Идемпотентность |
|---|---|---|
| Reminder due scan | каждую минуту | unique key reminder+scheduled time+recipient |
| Recurrence materialization | периодически и on-demand | unique occurrence key series+date |
| Session cleanup | ежедневно | удаляет только истёкшие |
| Trash purge | ежедневно | по retention и legal hold |
| Search projection rebuild | по событию/ночью reconcile | upsert by object version |
| Outbox dispatch | постоянно | mark dispatched after success |
| Backup verification | после backup | checksum + test catalog |
| Permission recalculation | при membership/role change | sequence-driven invalidation |

## 5.7. Realtime Hub

- отдельное логическое соединение на user session/device;
- группы по user ID, project ID применяются только как оптимизация, но не как единственная проверка;
- сервер повторно проверяет право перед публикацией чувствительного события либо публикует только object pointer;
- heartbeat;
- maximum connection count;
- reconnect token/cursor;
- сообщения имеют sequence и могут быть повторены;
- клиент обязан дедуплицировать.

## 5.8. Server discovery

Основной способ — DNS-имя, настроенное администратором. Дополнительно можно дать установщику конфигурационный файл. Автоматический broadcast discovery в локальной сети не рекомендуется по умолчанию из-за ложных серверов и сложностей сегментированных VLAN.

## 5.9. Health endpoints

Разделяются:

- liveness: процесс отвечает;
- readiness: доступна БД и выполнены миграции;
- dependency health: backup target, realtime, disk, certificate expiry;
- admin health summary: агрегированные безопасные показатели без секретов.

## 5.10. Database migration

- миграции версионируются вместе с сервером;
- применяются отдельной deployment-командой до запуска новой версии;
- destructive migration требует backup и rollback plan;
- API не стартует в режиме write, если schema version несовместима;
- клиентская совместимость управляется диапазоном API/client versions.

---

# 6. Архитектура базы данных

## 6.1. Общий подход

PostgreSQL является единственным долговечным хранилищем структурированных данных. Логически данные делятся по схемам/модулям, например `identity`, `directory`, `tasks`, `projects`, `catalog`, `contacts`, `notifications`, `audit`, `sync`.

Отдельная база на модуль в MVP не создаётся. Межмодульные foreign key допустимы только через согласованные ownership rules; прямые ad hoc joins из бизнес-кода ограничиваются read-model слоем.

## 6.2. Группы данных

### Identity and access

- account identity;
- employee link;
- password credential metadata;
- account status and lockout;
- sessions and devices;
- roles, permissions, grants, denies;
- department and project relationships.

### Organization directory

- organization;
- employee profiles;
- departments;
- positions and status;
- user preferences.

### Work management

- tasks;
- subtasks;
- checklists;
- assignees/watchers;
- dates, deadlines, priorities;
- recurrence series and occurrences;
- calendar events;
- reminders.

### Projects

- project identity and lifecycle;
- participants and project roles;
- project-specific permission overrides;
- object relations.

### File catalog

- catalog tree nodes;
- catalog item type;
- file/folder/web locations;
- storage endpoint/device;
- path scope and priority;
- metadata and tags;
- last observed availability;
- links to tasks/projects/contacts.

### Contacts

- person;
- counterparty company;
- communication channels;
- relationships;
- interaction history;
- domain links.

### Collaboration

- comments;
- mentions;
- notification events;
- read/unread state;
- invitations where applicable.

### Governance

- append-only audit;
- object versions;
- change feed;
- outbox;
- archive/trash state;
- retention/legal hold flags;
- backup execution metadata.

## 6.3. Основные связи

```text
Organization 1---N Departments
Organization 1---N UserAccounts
UserAccount 1---1 EmployeeProfile
Department 1---N EmployeeProfiles

Project N---N UserAccounts through ProjectMembership
Project 1---N Tasks
Task N---N UserAccounts through Assignee/Watcher relations
Task 1---N Subtasks
Task 1---N ChecklistItems
Task N---N CatalogItems through ObjectLink
Task N---N Contacts through ObjectLink

CatalogItem 1---N FileLocations
CatalogItem N---N Projects/Tasks/Contacts

ContactPerson N---1 CounterpartyCompany optional
Contact/Company 1---N InteractionEvents

Any auditable object 1---N AuditEntries by object reference
Any syncable object 1---N ChangeFeed references over time
```

## 6.4. Object identity and versioning

Каждый syncable объект имеет:

- immutable global ID;
- organization ID;
- created_at, created_by;
- updated_at, updated_by;
- integer/monotonic `version`;
- lifecycle state;
- optional deleted_at/deleted_by;
- optional archived_at.

Version увеличивается при любом изменении, видимом другим клиентам. Технические поля фонового обслуживания не всегда должны увеличивать бизнес-версию; это определяется по объекту.

## 6.5. Временные данные

- все machine timestamps хранятся в UTC;
- date-only хранится отдельным типом даты;
- локальное время повторяющейся задачи хранится вместе с timezone identifier;
- duration хранится как число минут/интервал, а не вычисляется из formatted string;
- не использовать local server time без timezone.

## 6.6. Поисковая архитектура

В MVP поиск строится на PostgreSQL:

- полнотекстовые индексы по названиям, описаниям, комментариям;
- trigram/normalized indexes для частичного совпадения имён, email, путей;
- отдельная search projection с object type, ID, tokens и permission scope hints;
- финальная проверка доступа при выдаче результата.

Отдельный Elasticsearch/OpenSearch не нужен до подтверждённой проблемы производительности или требования индексировать содержимое файлов.

## 6.7. Что нельзя хранить в базе

| Данные | Причина |
|---|---|
| Пароли в открытом или обратимо зашифрованном виде | компрометация всех аккаунтов при утечке |
| Access/refresh tokens в открытом виде | захват активных сессий; хранится только hash refresh token и session metadata |
| Содержимое рабочих файлов | противоречит продуктовой модели, раздувает backup и создаёт дублирование |
| SMB/Windows credentials пользователей | сервер не должен impersonate пользователей для обычного открытия файлов |
| Произвольные исполняемые скрипты и команды | удалённое выполнение кода |
| Полные тексты секретов в audit/logs | audit должен фиксировать факт изменения, но не распространять секрет |
| Неограниченные бинарные вложения в комментарии | не предусмотрено требованиями; создаёт скрытое файловое хранилище |
| Медицинские, биометрические и иные специальные категории данных | нет бизнес-требования и соответствующего режима защиты |
| Содержимое clipboard, список локальных файлов устройства | избыточное наблюдение и риск приватности |

## 6.8. Аватары и системные вложения

Аватар не считается рабочим файлом. Он хранится как ограниченный системный asset:

- отдельное server-side storage или small object table;
- размер, например, до 2 МБ;
- разрешённые форматы;
- декодирование и re-encoding;
- запрет SVG/исполняемого содержимого;
- malware scan;
- неизменяемый content hash;
- удаление по lifecycle пользователя.

## 6.9. Целостность

- foreign keys для обязательных связей;
- unique constraints для username, recurrence occurrence, idempotency key;
- check constraints для приоритета, статусов и диапазонов;
- exclusion/validation для tree cycles;
- transaction isolation по умолчанию read committed, для критичных операций explicit row/version check;
- advisory/distributed locks для singleton jobs.

## 6.10. Retention

Предлагаемые значения должны быть утверждены компанией:

- корзина пользовательских объектов: 30–90 дней;
- audit: минимум 3 года или по корпоративной политике;
- notifications: 12 месяцев после прочтения;
- change feed: пока все поддерживаемые клиенты гарантированно синхронизировались, затем архив/compaction;
- sessions: истёкшие записи 90 дней для security audit;
- application logs: 30–90 дней;
- backup: отдельная политика.


# 7. Архитектура файлов

## 7.1. Базовая модель

Приложение хранит не файл, а **каталожную запись** и один или несколько **вариантов расположения**.

```text
CatalogItem
  |-- title
  |-- type = file_reference | folder_reference | web_link | text_note | virtual_folder
  |-- virtual_parent_id
  |-- description
  |-- tags
  |-- relations to tasks/projects/contacts
  |
  +-- FileLocation 1
  |     |-- scope = shared_network
  |     |-- path = \\FILES01\Clients\Alpha\Contract.docx
  |     |-- priority = 100
  |
  +-- FileLocation 2
        |-- scope = device_local
        |-- device_id = ACCOUNTING-01
        |-- path = D:\Work\Alpha\Contract.docx
        |-- priority = 50
```

Одна каталожная запись представляет логический рабочий документ. Несколько locations представляют доступные способы найти этот документ, но система не гарантирует, что содержимое всех location идентично. При добавлении второго location пользователь должен явно подтвердить, что это тот же логический документ.

## 7.2. Типы location

| Тип | Пример | Область действия | Правило |
|---|---|---|---|
| Shared UNC file | `\\server\share\a.docx` | все устройства с сетевым и ACL-доступом | предпочтительный общий путь |
| Shared UNC folder | `\\nas\docs\project` | все устройства | открывается как папка |
| Device local file | `D:\Work\a.docx` | только конкретный `device_id` | не рассматривается на других устройствах |
| Device local folder | `C:\Users\...` | только конкретное устройство | точный путь не обязан показываться другим пользователям |
| Mapped drive | `Z:\a.docx` | устройство/профиль Windows | хранится как device-scoped; желательно дополнительно преобразовать в UNC |
| Web URL | `https://...` | все устройства | только разрешённые схемы `https`, опционально `http` по политике |

## 7.3. Нормализация пути

При сохранении клиент передаёт:

- original path для отображения;
- normalized path для сравнения;
- path type;
- device ID или storage endpoint;
- observed filename, extension, size, modified time;
- optional file identity hint.

Правила нормализации:

- UNC server/share приводятся к единому регистру для сравнения, но исходный вид сохраняется;
- завершающие разделители каталогов нормализуются;
- `.` и `..` разрешаются локально до сохранения;
- environment variables и `%USERPROFILE%` не сохраняются как общий путь;
- mapped drive не считается общим location;
- URI scheme валидируется по allowlist;
- максимальная длина и недопустимые символы проверяются до записи.

## 7.4. Выбор пути для открытия

Алгоритм:

```text
1. Получить все locations, доступные по правам приложения.
2. Исключить disabled/deleted locations.
3. Отобрать device-local locations текущего device_id.
4. Добавить shared locations.
5. Применить admin policy allowed roots.
6. Отсортировать:
   a. user-selected preferred location for device;
   b. current-device local;
   c. shared UNC by priority;
   d. other compatible locations.
7. Проверить кандидаты последовательно с коротким timeout.
8. Выбрать первый доступный.
9. Показать пользователю, какой location будет открыт, если их несколько.
10. Передать путь Windows Shell.
```

Приложение не должно автоматически открывать вторую копию без визуального указания, если выбранный пользователем location недоступен и fallback может содержать другую версию файла.

## 7.5. Проверка доступности

Доступность проверяется **на клиенте**, потому что:

- сервер может иметь другой Windows/SMB account;
- сервер не видит локальный диск сотрудника;
- доступ пользователя определяется его Windows token и ACL;
- сетевой ресурс может быть доступен из одного VLAN и недоступен из другого.

Проверка выполняется асинхронно и не блокирует UI. Возможные результаты:

| Статус | Значение |
|---|---|
| Available | объект существует и доступен для базовой операции |
| NotFound | путь разрешился, но объект отсутствует |
| AccessDenied | ОС вернула отказ в доступе |
| ResourceUnavailable | сервер/шара/диск недоступны |
| DeviceMismatch | location принадлежит другому устройству |
| InvalidPath | путь синтаксически неверен или запрещён политикой |
| Timeout | проверка не завершилась за заданное время |
| Unknown | ещё не проверялся или результат устарел |

Нельзя проверять все тысячи файлов при каждом открытии каталога. Используется ленивый probe:

- при открытии карточки;
- по явной команде пользователя;
- для видимых строк с ограничением параллелизма;
- перед открытием;
- периодически только для избранных/активных ссылок.

## 7.6. Открытие файла

Пошагово:

1. Клиент получает актуальную запись и capabilities.
2. Проверяет `FileReference.Open`.
3. Выбирает location.
4. Проверяет, что путь находится в разрешённой root-политике, если такая политика включена.
5. Выполняет lightweight existence/access probe.
6. Для потенциально опасных расширений показывает предупреждение.
7. Вызывает стандартное открытие Windows без передачи shell-командной строки через интерпретатор.
8. Локально фиксирует результат запуска.
9. Опционально отправляет на сервер событие `file_open_attempted` без содержимого файла.

Приложение не открывает файл автоматически при получении уведомления или realtime-события.

## 7.7. Открытие расположения

Для файла открывается родительская папка с выделением файла, если Windows поддерживает это. Для недоступного файла можно попытаться открыть ближайшую существующую родительскую папку, но только после явного действия пользователя.

## 7.8. Файл удалён

Если путь не существует:

- каталожная запись сохраняется;
- location получает последнее наблюдаемое состояние `NotFound` для текущего устройства;
- показывается время последней успешной доступности;
- доступны команды relink, remove location, keep broken reference, open parent;
- другие locations продолжают проверяться;
- физическое удаление не меняет автоматически связанные задачи и проекты;
- сервер не объявляет файл глобально удалённым только по наблюдению одного клиента.

## 7.9. Путь изменился

В MVP автоматического глобального поиска нет. Relink flow:

1. Пользователь выбирает «Указать новое расположение».
2. File picker возвращает новый путь.
3. Клиент сравнивает имя, размер, modified time и optional fingerprint.
4. Пользователь выбирает:
   - заменить текущий location;
   - добавить location для текущего устройства;
   - добавить общий network location.
5. Сервер проверяет право `FileLocation.Update`.
6. Старая и новая величина попадают в audit.
7. Связи с задачами/проектами сохраняются, потому что меняется location, а не CatalogItem.

## 7.10. Нет доступа

`AccessDenied` не равен `NotFound`. UI не предлагает удалить ссылку как основной сценарий. Он показывает:

- файл существует или ресурс отвечает, но доступ запрещён;
- обратиться к владельцу папки/администратору;
- проверить вход под нужной Windows-учётной записью;
- выбрать другой location.

Система приложения не запрашивает и не хранит SMB-пароль для обхода отказа.

## 7.11. Файл только на одном компьютере

- location обязательно привязан к registered device ID;
- другим пользователям показывается logical record и безопасный статус «Доступен только на устройстве X»;
- точный локальный путь отображается только владельцу location, администратору с отдельным правом или пользователю, которому он нужен по политике;
- другой компьютер не делает probe локального пути;
- если устройство переименовано, server device identity не меняется; display name обновляется отдельно;
- при списании устройства locations переводятся в `orphaned` до relink или удаления записи.

## 7.12. Сетевая папка

Для общих документов предпочтителен UNC. Server хранит storage endpoint:

- canonical server/share;
- display name;
- allowed root;
- owner/admin contact;
- last health status from server and clients;
- optional DFS alias.

Серверный health probe проверяет только сетевую доступность ресурса от service account и не заменяет клиентскую ACL-проверку.

## 7.13. Несколько копий и версии

Система не является DMS и не выполняет version control содержимого. Поэтому:

- несколько locations не называются версиями автоматически;
- нельзя обещать, что содержимое синхронизировано;
- optional metadata fingerprint помогает предупреждать о расхождении;
- при обнаружении разных size/mtime клиент показывает «Возможны разные копии»;
- разрешение выполняет пользователь вне приложения.

## 7.14. Связи файла

Связь является отдельной сущностью:

```text
FileObjectLink
  - catalog_item_id
  - target_type: task | project | contact | company
  - target_id
  - relation_type: attachment | source | result | reference | contract | other
  - created_by
  - created_at
```

Удаление связи не удаляет CatalogItem. Удаление CatalogItem в корзину делает связи невидимыми, но сохраняет их для восстановления.

## 7.15. Безопасность путей

- allowlist URI schemes;
- запрет `javascript:`, `data:`, shell pseudo-URLs;
- запрет передачи пути в `cmd.exe /c` или PowerShell;
- проверка file type перед launch;
- предупреждение для `.exe`, `.msi`, `.bat`, `.cmd`, `.ps1`, `.js`, `.vbs`, `.lnk`;
- защита от UNC path, направленного на неразрешённый внешний SMB-host, если компания включает root restrictions;
- аудит изменения shared location;
- никаких автоматических previews неизвестных файлов в MVP.

---

# 8. Синхронизация

## 8.1. Цели

Синхронизация должна:

- быстро отражать изменения коллег;
- не терять данные;
- не раскрывать объекты после отзыва прав;
- переживать разрыв сети;
- не требовать полной перезагрузки всей базы;
- позволять восстановить локальный кэш.

## 8.2. Источник истины

Только server database. Локальный кэш — materialized read model. В MVP клиент не является peer и не имеет самостоятельной версии общей сущности, которую можно позже незаметно слить.

## 8.3. Что синхронизируется

- доступные пользователю task/project/calendar/contact/catalog projections;
- справочники статусов, отделов и доступных сотрудников;
- capabilities и membership changes;
- уведомления;
- архив/корзина tombstones;
- user-synchronized preferences;
- объектные версии;
- минимальные audit summaries по запросу.

## 8.4. Что не синхронизируется

- содержимое рабочих файлов;
- Windows ACL;
- SMB credentials;
- полный локальный список файлов;
- локальные window positions, если решено хранить их device-only;
- локальные логи;
- временные drafts, если пользователь не нажал Save;
- данные объектов, к которым доступ отозван.

## 8.5. Bootstrap sync

При первом входе:

1. Получить user profile и access scope version.
2. Создать пустую локальную БД.
3. Запрашивать snapshot страницами по bounded datasets.
4. Для крупных данных применять по диапазонам/курсорам.
5. Записать `snapshot_sequence`.
6. Запросить изменения после snapshot sequence, возникшие во время загрузки.
7. Атомарно переключить кэш в ready.
8. Подключить realtime с актуального cursor.

UI может открыться частично после загрузки Today/справочников, но должен показывать, какие разделы ещё синхронизируются.

## 8.6. Incremental sync

`ChangeFeed` содержит sequence, object type, ID, operation, version и access-relevant metadata. Клиент запрашивает batch после cursor. Сервер:

- повторно фильтрует по текущим правам;
- возвращает upsert payload либо tombstone;
- при потере доступа возвращает `remove_from_cache`;
- не раскрывает причину исчезновения, если это создаёт утечку;
- возвращает следующий cursor.

## 8.7. Realtime

Realtime не заменяет sync. Он сокращает задержку:

- событие пришло — клиент ставит объект в refresh queue;
- одинаковые события coalesce;
- если WebSocket пропущен, cursor sync восстановит состояние;
- после reconnect всегда выполняется delta sync;
- heartbeat failure меняет индикатор подключения.

## 8.8. Когда выполняется синхронизация

- после login;
- при запуске приложения;
- после wake from sleep/hibernation;
- после восстановления сети;
- по realtime signal;
- периодический reconcile, например раз в 5–15 минут;
- вручную по кнопке;
- после локальной успешной команды для подтверждения связанных проекций;
- немедленно после события изменения прав.

## 8.9. Запись данных

В online-only MVP:

```text
Client draft -> API command -> server commit -> response -> cache update
```

Клиент может показывать optimistic UI только для обратимых узких команд, но обязан откатить отображение при ошибке. Для создания/редактирования карточки лучше показывать состояние «Сохранение» и считать объект созданным после server response.

## 8.10. Конфликт изменений

Каждый command передаёт expected version.

### Сценарий

1. A открыл task version 10.
2. B изменил deadline, server version 11.
3. A сохраняет assignee с expected version 10.
4. Server обнаруживает 10 != 11.
5. Server возвращает 409 и changed fields.
6. Client показывает server current state и local draft.
7. Пользователь выбирает:
   - отменить свой draft;
   - повторно применить разрешённые поля к новой версии;
   - открыть подробное сравнение.
8. Новая команда отправляется с expected version 11.

### Автоматическое слияние

В MVP допускается только для независимых специализированных команд, например `AddComment`, `MarkNotificationRead`, `CompleteChecklistItem`, которые не заменяют всю карточку. Универсальный field-level auto-merge не выполняется без подтверждения.

## 8.11. Потеря сети

При обнаружении:

- connection status меняется на `Offline/Server unavailable`;
- realtime отключается;
- общие command buttons становятся disabled;
- draft может оставаться в памяти UI, но не считается сохранённым;
- доступен read-cache с отметкой времени актуальности;
- локальные файлы открываются;
- сетевые файлы зависят от самой сети/SMB;
- локальные заранее поставленные уведомления продолжают показываться, но actions, меняющие сервер, требуют восстановления связи.

## 8.12. Восстановление сети

1. Проверить DNS/TLS/server health.
2. Refresh session.
3. Если session revoked — login.
4. Delta sync from last committed cursor.
5. Удалить объекты с отозванными правами.
6. Пересчитать локальные ближайшие notifications.
7. Подключить realtime.
8. Разблокировать commands.
9. Не отправлять несохранённые drafts автоматически; предложить пользователю повторно сохранить после обновления версии.

## 8.13. Большой разрыв и compaction

Если cursor слишком старый и change feed уже compacted, сервер возвращает `SYNC_RESET_REQUIRED`. Клиент:

- сохраняет только безопасные device settings;
- удаляет user cache;
- выполняет bootstrap snapshot;
- не пытается угадывать изменения.

## 8.14. Смена прав

Изменение роли/membership повышает `access_scope_version`. Клиент сравнивает версию при любом sync. При изменении:

- сервер отдаёт explicit cache revocation list или требует scoped resync;
- UI закрывает запрещённые карточки;
- search cache очищается;
- notifications о скрытых объектах становятся недоступны;
- локальная история не сохраняет скрытый текст.

## 8.15. Идемпотентность

Команды из notification actions и потенциально повторяемые сетевым retry имеют idempotency key. Server хранит результат ограниченное время. Одинаковая команда не должна дважды завершить задачу или создать два комментария.

---

# 9. Авторизация

## 9.1. Термины

- **Authentication:** кто пользователь.
- **Session:** разрешённый вход с конкретного устройства.
- **Authorization:** что пользователь может сделать.
- **Device registration:** логическая запись установки приложения; не доказательство доверенного железа.

## 9.2. Создание учётной записи

1. Администратор создаёт EmployeeProfile или выбирает существующий.
2. Создаёт UserAccount с уникальным login.
3. Назначает initial role и department relations.
4. Генерирует одноразовый временный пароль или активационный код.
5. Передаёт его сотруднику вне приложения.
6. При первом входе сервер требует задать новый пароль.
7. Временный credential немедленно становится недействительным.

## 9.3. Вход

1. Клиент устанавливает TLS-соединение и проверяет сертификат сервера.
2. Отправляет login, password, device identifier/name и client version.
3. Server применяет rate limit.
4. Нормализует login без изменения пароля.
5. Загружает account по постоянному времени ответа, насколько возможно.
6. Проверяет active/blocked/locked status.
7. Проверяет password hash с per-user salt и memory-hard algorithm.
8. При ошибке увеличивает failed count и записывает security audit без пароля.
9. При успехе сбрасывает failed count согласно политике.
10. Создаёт session и привязывает refresh token family к device.
11. Возвращает короткоживущий access token и refresh token.
12. Клиент хранит refresh secret в Windows Credential Manager/DPAPI и access token в памяти.

## 9.4. Password hashing

- memory-hard algorithm, например Argon2id;
- уникальная случайная соль;
- параметры хеширования хранятся с hash;
- server-side pepper хранится вне БД в secret store;
- параметры повышаются со временем;
- успешный login может rehash старый credential;
- сравнение выполняется constant-time функцией библиотеки.

## 9.5. Сессия

Предлагаемая модель:

- access token: 10–15 минут;
- refresh token: случайный 256-bit secret, ротация при каждом использовании;
- server хранит только hash refresh token;
- session содержит user, device, created, last_seen, expires, revoked, IP metadata;
- reuse старого refresh token отзывает всю token family как признак кражи;
- принудительный logout revokes session;
- account block revokes все sessions.

## 9.6. Истечение

- idle timeout, например 8–12 часов, настраивается;
- absolute session lifetime, например 30 дней;
- sensitive admin operations могут требовать recent authentication;
- после истечения клиент возвращается на login screen, сохраняя только несекретные device settings;
- локальный кэш блокируется до успешного входа того же пользователя.

## 9.7. Смена пароля

1. Пользователь вводит текущий пароль.
2. Server повторно аутентифицирует.
3. Проверяет policy и password history.
4. Записывает новый hash.
5. Инкрементирует `credential_version`.
6. Отзывает все sessions, кроме текущей или включая текущую по политике.
7. Создаёт security audit.
8. Текущая session получает новые tokens.

## 9.8. Сброс пароля администратором

Администратор не задаёт постоянный пароль пользователя и не видит старый. Он:

- запускает reset;
- все sessions пользователя отзываются;
- создаётся временный одноразовый credential с коротким сроком;
- при первом входе требуется новый пароль;
- событие журналируется;
- пользователю показывается, что reset выполнен администратором.

## 9.9. Блокировка

Есть два разных состояния:

- `temporary_lockout` после неудачных входов;
- `administratively_blocked` по решению администратора.

При блокировке:

- новые login запрещены;
- текущие sessions revokes;
- realtime connections закрываются;
- sync возвращает authentication failure;
- кэш становится недоступным после получения события/следующего запроса;
- audit сохраняется.

## 9.10. Защита от brute force

- progressive delay;
- rate limit по login, IP и device;
- временная блокировка;
- единое сообщение «Неверные учётные данные»;
- alert администратору при аномалии;
- отсутствие публичного endpoint перечисления пользователей.

## 9.11. Server certificate

- HTTPS обязателен даже внутри LAN;
- сертификат выдан корпоративным CA или доверенным локальным CA;
- клиент доверяет CA, а не отключает validation;
- допускается pinning public key/CA с механизмом ротации;
- expiry мониторится заранее;
- self-signed leaf без управляемого доверия не является production-решением.

---

# 10. Права доступа

## 10.1. Модель

Используется гибрид:

```text
RBAC: глобальная роль
 + ReBAC: отношения к объекту
 + ABAC: атрибуты пользователя, объекта и состояния
 + Explicit project grants/denies
 = Authorization Decision
```

Пример разрешения:

```text
Can(Task.Update, user, task) =
  account_active
  AND organization_match
  AND task_not_purged
  AND (
       user has global Task.UpdateAll
       OR user is project member with Task.Update
       OR user is assignee with Task.UpdateOwn
      )
  AND NOT explicit_deny
  AND state_allows_update
```

## 10.2. Единица разрешения

Permission имеет вид `Resource.Action`, например:

- `Task.Read`;
- `Task.Create`;
- `Task.ChangeStatus`;
- `Task.Assign`;
- `Project.ManageMembers`;
- `FileReference.Open`;
- `FileLocation.Update`;
- `Audit.ReadAll`;
- `Backup.Execute`.

Не использовать проверки вида `if role == Manager` внутри модулей. Роль разворачивается в capabilities централизованно.

## 10.3. Scope

Разрешение применяется в scope:

- organization;
- department;
- project;
- own/assigned;
- explicit object;
- system administration.

## 10.4. Decision pipeline

```text
1. Authenticate session.
2. Confirm account active.
3. Load immutable AuthorizationContext snapshot.
4. Confirm organization boundary.
5. Resolve requested capability.
6. Load minimal object security attributes.
7. Evaluate explicit deny.
8. Evaluate global grant.
9. Evaluate department/project relationships.
10. Evaluate object relationship: owner/assignee/watcher/creator.
11. Evaluate object state restrictions.
12. Return allow/deny + reason code.
13. Audit sensitive denies and all admin allows.
```

## 10.5. Query authorization

Нельзя загрузить все строки и отфильтровать в памяти. Query layer строит access-aware SQL predicate. Для сложных правил используются:

- membership tables;
- materialized access projection;
- security scope IDs;
- post-filter only as дополнительная защита для малого result set.

## 10.6. Command authorization

Проверка выполняется дважды по смыслу:

- endpoint-level: есть ли capability в принципе;
- object-level: можно ли над конкретным объектом.

После загрузки объекта и перед commit проверка может выполняться повторно, если команда меняет security attributes, например project или assignee.

## 10.7. Field-level restrictions

Пользователь может иметь право изменить статус, но не исполнителя или проект. Поэтому update API не принимает универсальный JSON patch без policy mapping. Используются специализированные команды или field authorization map.

## 10.8. Project role

ProjectMembership содержит role template и optional overrides. Effective permissions рассчитываются сервером. При конфликте:

1. explicit deny;
2. security/state deny;
3. explicit grant;
4. project role grant;
5. global role grant;
6. default deny.

Для системного администратора bypass должен быть явной permission, а не скрытым hardcode, и каждое использование журналируется.

## 10.9. Department scope

Department hierarchy, если появится, не должна автоматически давать доступ родительскому отделу без явно заданной политики. В MVP лучше плоские отделы. Руководитель отдела получает scope через relation `DepartmentManager`, а не по тексту должности.

## 10.10. File dual authorization

```text
Application permission: можно видеть/открыть ссылку?
             AND
Windows/SMB permission: ОС разрешает открыть путь?
             =
Фактический доступ
```

Server не гарантирует второй этап. UI различает «нет права в приложении» и «Windows отказала».

## 10.11. Cache revocation

При отзыве прав:

- access scope version меняется;
- realtime отправляет security invalidation;
- следующий sync удаляет object projection;
- deep link закрывается;
- локальный search index очищается;
- cached sensitive view TTL минимален;
- полная защита от чтения уже увиденного человеком невозможна, но data-at-rest остаётся encrypted.

## 10.12. Защита от IDOR

Каждый endpoint проверяет object access независимо от ID. Ответы 403/404 выбираются по политике, чтобы не подтверждать существование скрытого объекта. Bulk endpoints проверяют каждый target и не возвращают partial hidden data без явного контракта.

---

# 11. Жизненный цикл данных

## 11.1. Задача

```text
Draft in client
 -> Created/New
 -> Planned/In progress/Waiting/Review
 -> Completed or Cancelled
 -> Archived optional
 -> Trash
 -> Retention period
 -> Purged
```

### Создание

- назначается immutable ID;
- фиксируются author и creator;
- проверяются project/assignee rights;
- сохраняется version 1;
- создаются reminders, relations, audit, change event.

### Изменение

- optimistic concurrency;
- каждое значимое поле отражается в audit;
- смена assignee создаёт notification;
- deadline change пересчитывает overdue projection и reminders.

### Завершение

- сохраняется completed_at и completed_by;
- future reminders отменяются;
- повторяющаяся серия создаёт следующий occurrence по правилам;
- completed не удаляется.

### Просрочка

- вычисляемое состояние `deadline < now AND status not terminal`;
- может индексироваться как projection, но не редактируется вручную.

### Корзина и purge

- delete переводит в trash;
- связи и audit сохраняются;
- restore возвращает предыдущий lifecycle state или безопасный default;
- purge требует retention, permission и отсутствия legal hold;
- audit хранит минимальный tombstone после purge.

## 11.2. Проект

```text
Planning -> Active <-> Paused -> Completed -> Archived -> Trash -> Purged
```

- должен иметь владельца;
- удаление участника пересчитывает доступ;
- завершение не завершает задачи автоматически без отдельной команды;
- trash проекта не удаляет задачи физически; они могут быть перемещены/архивированы по явному workflow;
- restore восстанавливает membership, если пользователи активны, иначе помечает missing members.

## 11.3. Контакт

```text
Active -> Archived -> Trash -> Purged/Anonymized
```

- duplicate detection при создании;
- связи с задачами и проектами не являются ownership;
- удаление контакта не удаляет задачи;
- при purge interaction history может быть anonymized по политике;
- email/phone нормализуются, но исходное отображение сохраняется.

## 11.4. Ссылка на файл

```text
Created -> Available/Unknown/Broken observations
 -> Relinked or Additional Location
 -> Archived
 -> Trash
 -> Purged metadata
```

Физический файл живёт независимо. `Available` не является глобальным lifecycle state, а наблюдением конкретного устройства/endpoint во времени.

## 11.5. Комментарий

```text
Created -> Edited optional -> Soft deleted -> Retained tombstone -> Purged by policy
```

- author immutable;
- edit window может быть ограничен;
- история редактирования сохраняется;
- soft-deleted comment показывает «Комментарий удалён» без текста обычным пользователям;
- admin/audit доступ к старому тексту определяется политикой;
- комментарий не меняет task version, если хранится отдельным агрегатом, но обновляет task activity projection.

## 11.6. Уведомление

```text
Intent -> Materialized -> Pending delivery -> Delivered/Failed
 -> Read/Acknowledged/Snoozed -> Expired -> Purged
```

- intent рождается из domain event;
- materialized notification имеет recipient;
- delivery attempt не равен read;
- snooze создаёт новое planned occurrence, не переписывая историю;
- duplicate key предотвращает повтор;
- истёкшее уведомление остаётся в истории ограниченный срок.

## 11.7. Пользователь и сотрудник

```text
EmployeeProfile: Planned -> Active -> On leave optional -> Terminated -> Archived
UserAccount: Invited -> Active -> Locked/Blocked -> Disabled -> Archived
```

Увольнение сотрудника:

- account disabled;
- sessions revoked;
- задачи не удаляются;
- ownership/assignee review report создаётся администратору;
- авторство и audit сохраняются;
- профиль не переиспользуется для другого человека.

## 11.8. Recurrence series

- series хранит rule и timezone;
- occurrence имеет ссылку на series и scheduled key;
- изменение current only создаёт exception;
- current and following разрезает series на две;
- entire series обновляет rule и будущие unmodified occurrences;
- завершённые past occurrences не переписываются.

---

# 12. Жизненный цикл запроса

## 12.1. Создание задачи

1. Пользователь открывает quick create.
2. Desktop загружает доступные projects/assignees из кэша и фоново актуализирует.
3. Пользователь вводит поля.
4. Client выполняет format validation: title, dates, duration.
5. Формируется `CreateTaskCommand` с idempotency key.
6. API client проверяет online state.
7. Server authenticates session.
8. Authorization проверяет `Task.Create` в выбранном project scope.
9. Проверяется право назначить выбранного исполнителя.
10. Валидируются deadline/start/timezone/recurrence.
11. Создаётся aggregate и ID.
12. В transaction записываются task, participants, reminders, audit, change feed, outbox.
13. Commit.
14. Server возвращает version 1 и normalized DTO.
15. Client upsert в локальный кэш.
16. UI показывает задачу.
17. Outbox worker создаёт notifications назначенным участникам.
18. Realtime signals вызывают refresh других клиентов.

Ошибки:

- 403: выбран недоступный project/assignee;
- 409: duplicate idempotency возвращает первоначальный result;
- 422: некорректные даты;
- 503: server unavailable, draft остаётся локально несохранённым.

## 12.2. Открытие карточки задачи

1. Route получает task ID.
2. Local cache проверяется.
3. Если есть projection, экран открывается сразу с indicator freshness.
4. Server query выполняет access-aware read.
5. При 200 client replaces cache with newer version.
6. При 403/404 локальная копия удаляется и экран закрывается.
7. Comments/history загружаются отдельными paginated запросами.
8. Linked files загружаются как metadata only.

## 12.3. Изменение статуса

1. Пользователь нажимает новый статус.
2. Client проверяет advertised capability, но это только UX.
3. Отправляет specialized command с expected version.
4. Server проверяет transition graph и право.
5. При переходе в completed фиксирует completed_at.
6. Отменяет будущие reminder intents этой задачи.
7. Создаёт audit/change/outbox.
8. Commit.
9. Client обновляет row/detail.
10. Followers получают notification по политике.
11. При 409 UI загружает актуальный статус и предлагает повторить допустимое действие.

## 12.4. Перетаскивание в календаре

1. UI рассчитывает proposed start/end.
2. Локально подсвечивает пересечение.
3. На drop отправляет `RescheduleTask`.
4. Server проверяет edit permission, timezone, date bounds, recurrence context.
5. Если occurrence серии, требует edit scope: current/current+following/all.
6. Server updates schedule and reminders transactionally.
7. UI удерживает provisional position до ответа.
8. На failure возвращает элемент назад и показывает причину.

## 12.5. Добавление комментария

1. Client проверяет длину и пустое значение.
2. Отправляет `AddComment` с idempotency key.
3. Server проверяет право Read + Comment.Create на target.
4. Sanitizes control characters; rich HTML в MVP не нужен.
5. Создаёт comment отдельной транзакцией.
6. Обновляет activity timestamp/projection.
7. Audit фиксирует факт и допустимый текст/summary по политике.
8. Mentions, если поддержаны, валидируются только среди доступных участников.
9. Notification recipients вычисляются server-side.
10. Realtime refresh comments.

## 12.6. Открытие файла

1. Пользователь нажимает Open.
2. Server query возвращает только разрешённый CatalogItem и locations.
3. Client выбирает current-device/shared location.
4. Валидирует path/scheme/root.
5. Выполняет probe с timeout.
6. Различает NotFound/Denied/Unavailable.
7. При успехе вызывает Windows shell.
8. Application не читает bytes и не проксирует файл.
9. Результат открытия не считается бизнес-изменением; optional telemetry не содержит содержимого.

## 12.7. Relink файла

1. User chooses new file through OS picker.
2. Client captures metadata.
3. Displays comparison with old location.
4. User selects replace/add scope.
5. Server checks `FileLocation.Update` and project/catalog access.
6. Validates location type and allowed roots.
7. Updates location version; CatalogItem version changes.
8. Audit old/new path is protected and available only to authorized history viewers.
9. Sync distributes new metadata.

## 12.8. Вход пользователя

1. App loads server config.
2. Validates server TLS.
3. Sends login request.
4. Server rate-limits and validates credentials.
5. Session created.
6. Tokens returned.
7. Refresh token saved securely.
8. Profile/capabilities fetched.
9. Local cache for user unlocked or initialized.
10. Delta/bootstrap sync.
11. Realtime connection.
12. Tray notification schedule reconciled.
13. Main window opens.

## 12.9. Получение уведомления

1. Task/reminder domain event commits.
2. Outbox worker reads event.
3. Notification policy calculates recipients and time.
4. Notification row persisted with dedupe key.
5. If due, realtime signal sent.
6. Client fetches notification details and validates access.
7. Local scheduler maps urgency color based on current thresholds.
8. Windows toast shown.
9. User action sends idempotent command.
10. Server commits action and notification acknowledgement.
11. Other devices receive read/state update.

## 12.10. Отсрочка уведомления

1. User selects snooze interval.
2. Client sends notification ID and chosen time.
3. Server checks recipient ownership and object accessibility.
4. Marks current notification snoozed.
5. Creates new planned delivery with parent reference.
6. Audit records snooze.
7. Client removes current toast and schedules next local hint.

## 12.11. Удаление задачи

1. Client displays consequence: record moves to trash, linked physical files remain.
2. Command includes expected version.
3. Server checks delete permission and state restrictions.
4. Sets deleted_at/deleted_by and previous lifecycle state.
5. Creates tombstone/change event.
6. Notifications/reminders are cancelled.
7. Linked records remain for restore.
8. Clients remove task from active projections and add to trash if permitted.
9. Purge occurs later by retention job, not in request.

## 12.12. Восстановление из корзины

1. User chooses restore.
2. Server checks Restore permission.
3. Validates parent project/catalog folder still exists and accessible.
4. If parent missing, requires safe destination or uses fallback root.
5. Restores previous state where valid.
6. Rebuilds search/schedule projections.
7. Does not recreate deleted physical file.
8. Audit and sync events emitted.


# 13. Производительность

## 13.1. Основные потенциальные узкие места

| Узкое место | Причина | Решение |
|---|---|---|
| Calendar range queries | много задач, повторения, фильтры сотрудников | диапазонные индексы, materialized occurrences на ограниченный горизонт, cursor pagination |
| Global search | поиск по нескольким типам и permission filtering | единая search projection, полнотекстовые индексы, limit, debounce |
| Today dashboard | несколько независимых выборок | специализированный агрегирующий read endpoint, параллельные bounded queries |
| Sync after long absence | большой change feed | batch, compression, cursor, snapshot reset при слишком старом cursor |
| Realtime fan-out | массовое изменение проекта/прав | pointer events, coalescing, groups as optimization, rate limit |
| Audit growth | запись на каждое изменение | partitioning по времени, append-only indexes, retention/archive |
| Recurring tasks | генерация на годы вперёд | не материализовать бесконечную серию; rolling horizon + on-demand |
| File availability | медленные SMB timeouts | client-side async probe, short timeout, concurrency limit, cache status |
| Large catalog tree | тысячи узлов | lazy loading children, path index, no full-tree payload |
| Permission queries | гибридные сложные правила | normalized membership, access projection, query predicates, caching of auth context |

## 13.2. Целевые показатели

Предварительные технические цели для LAN при нормальной нагрузке:

| Операция | p95 target |
|---|---|
| Login excluding first sync | < 1,5 s |
| Open Today from warm cache | < 300 ms |
| Server task list query | < 500 ms |
| Create/update task | < 700 ms |
| Global search first page | < 700 ms |
| Realtime visibility of committed change | < 2 s |
| Calendar week query | < 800 ms |
| Incremental sync 1000 changes | < 5 s |

Это цели, а не гарантии. Они проверяются нагрузочными тестами на фактическом сервере компании.

## 13.3. Индексация

Нужны индексы по:

- organization/project/department scope;
- task assignee, status, deadline, scheduled date;
- project membership;
- catalog parent and type;
- normalized contact fields;
- notification recipient/read/due;
- audit object/time;
- change feed sequence;
- session token hash/status;
- deleted/archived partial predicates;
- search vector.

Индексы добавляются по query plan, а не на каждое поле. Избыточные индексы замедляют запись и backup.

## 13.4. Кэширование

### Можно кэшировать на клиенте

- справочники;
- доступные проекты и сотрудники;
- bounded task/calendar read models;
- catalog tree portions;
- contact summaries;
- notifications;
- calculated display data;
- current effective capabilities с коротким TTL/access version.

### Можно кэшировать на сервере

- permission templates;
- immutable dictionaries;
- organization settings;
- short-lived authorization context;
- compiled recurrence rules;
- frequently used read projections.

### Нельзя считать долгоживущим кэшем

- account active/blocked state;
- object-level permission после изменения membership;
- password/session secrets;
- физическую доступность файла;
- полный search result;
- конфликтную mutable карточку задачи;
- значение «не просрочено» без учёта текущего времени.

## 13.5. Calendar and recurrence

Не создавать occurrences на десятилетия. Стратегия:

- хранить recurrence rule;
- материализовать ближайший горизонт, например 6–12 месяцев;
- для запроса далёкой даты вычислять occurrence on-demand;
- сохранять exception только при изменении конкретного occurrence;
- фоново продлевать materialized horizon;
- использовать unique occurrence key.

## 13.6. Today query

Today не должен выполнять N+1. Server возвращает denormalized summaries с project name, assignee summary, priority and capability flags. Детальная карточка запрашивается отдельно.

## 13.7. Pagination

- tasks/search/audit/comments — cursor pagination;
- calendar — date range + limit/aggregation;
- catalog — children by parent;
- notification center — cursor by created_at/ID;
- запрещён unbounded `GetAll`.

## 13.8. Database connection pool

- bounded pool;
- request timeout;
- cancellation propagated to SQL;
- short transactions;
- no network/file operations inside DB transaction;
- background jobs use separate pool quota or concurrency limiter.

## 13.9. Что сильнее всего нагружает сервер

1. массовые range/calendar queries руководителей по отделам;
2. глобальный поиск с широкими permissions;
3. первичная синхронизация нового устройства;
4. массовая смена прав проекта;
5. генерация большого числа recurring notifications;
6. audit/search reindex;
7. backup I/O;
8. oversized comments/descriptions без лимитов.

## 13.10. Защита от деградации

- request size and field limits;
- max page size;
- query timeout;
- bulk operations chunking;
- job concurrency control;
- disk-space alerts;
- vacuum/analyze maintenance;
- slow-query logging without sensitive payload;
- circuit breaker только для внешних/необязательных dependencies;
- graceful degradation: realtime down — polling/sync continues.

---

# 14. Масштабирование и будущая расширяемость

## 14.1. Цель

Не проектировать SaaS, mobile и web сейчас, но избежать решений, которые делают их невозможными.

## 14.2. Несколько компаний

Сейчас нужно:

- `organization_id` во всех business roots;
- organization context в session;
- unique constraints scoped by organization;
- запрет cross-organization joins без явной system operation;
- data access layer автоматически добавляет organization predicate;
- file storage endpoint принадлежит organization;
- audit содержит organization ID;
- configuration разделена на system и organization.

В первой версии UI и provisioning жёстко работают с одной организацией. Публичная registration отсутствует.

## 14.3. Облачная версия

Сейчас нужно:

- server не зависит от конкретного LAN IP;
- HTTPS и stateless API host;
- sessions/DB вне process memory;
- background jobs имеют distributed lock abstraction;
- file locations представлены типами, а не только Windows strings;
- secrets вынесены из config files;
- health/metrics;
- deployment контейнеризуем;
- storage abstraction для system assets.

Позже потребуются tenant isolation, HA, managed DB, object storage, external identity, billing. Они не входят в MVP.

## 14.4. Web-версия

Сейчас нужно:

- business logic только на server;
- UI-agnostic API;
- REST contracts и realtime protocol;
- capabilities возвращаются сервером;
- file opening abstraction: web не сможет открыть произвольный `D:\` путь;
- web client сможет показывать metadata и network links только в поддерживаемой среде.

Не следует переносить WPF ViewModel или Windows path logic в server domain.

## 14.5. Mobile-приложение

Сейчас нужно:

- pagination;
- compact DTO;
- notification event abstraction;
- device sessions;
- timezone-safe reminders;
- API versioning;
- no assumption of always-on LAN.

Mobile физически не откроет локальный Windows file; architecture должна возвращать availability capability по platform/device.

## 14.6. Удалённая работа

Сейчас нужно:

- никакого hardcoded trust «всё внутри LAN безопасно»;
- TLS, sessions, rate limit;
- DNS name;
- reverse proxy;
- server address configurable;
- websocket reconnect;
- file locations clearly marked local/network;
- no dependency on broadcast discovery.

Позже доступ предоставляется через корпоративный VPN/Zero Trust gateway, но API не публикуется напрямую в интернет без отдельного security review.

## 14.7. Выделение сервисов

Наиболее вероятные кандидаты:

1. Notification delivery;
2. Search indexing;
3. Identity;
4. Audit archive;
5. File metadata health service.

Условие выделения — измеренная нагрузка, независимый deployment cadence или isolation requirement. Модульные contracts и outbox позволяют это сделать.

## 14.8. Database scaling

Сейчас:

- UUID-like IDs;
- partitionable audit/change tables;
- no database-specific business logic scattered in UI;
- read models separate from write aggregates;
- backup/restore automation.

Позже:

- read replicas;
- partitioning;
- connection proxy;
- separate search;
- tenant sharding, если действительно понадобится.

## 14.9. Ограничения будущей совместимости

Нельзя сейчас:

- использовать Windows username как global user ID;
- хранить абсолютный путь прямо в task row;
- завязывать права на четыре hardcoded role strings;
- помещать всю бизнес-логику в Desktop;
- использовать локальное время без timezone;
- считать realtime гарантированной доставкой;
- отдавать file bytes через API без нового бизнес-решения.

---

# 15. Безопасность

## 15.1. Модель угроз

Активы:

- задачи и договорённости;
- проекты и сроки;
- контакты/персональные данные;
- пути к корпоративным файлам;
- учётные записи;
- история действий;
- конфигурация сервера;
- backups;
- ключи/сертификаты.

Потенциальные нарушители:

- внешний пользователь, получивший доступ в LAN/VPN;
- обычный сотрудник, пытающийся увидеть чужой проект;
- уволенный сотрудник с активной session;
- malware на рабочем ПК;
- злоумышленник с украденным backup;
- ошибочный администратор;
- компрометированный file share;
- поставщик вредоносного обновления.

## 15.2. Trust boundaries

```text
[User]
  | trust boundary: Windows session
[Desktop App]
  | trust boundary: TLS network
[Reverse Proxy/API]
  | trust boundary: application/DB credentials
[Database]

[Desktop App]
  | trust boundary: OS file APIs and SMB
[File Server/NAS]

[Production]
  | trust boundary: backup encryption and transport
[Backup Repository]
```

## 15.3. Authentication risks and controls

| Риск | Контроль |
|---|---|
| brute force | rate limit, progressive lockout, monitoring |
| password database leak | Argon2id-like hash, salt, pepper, strong parameters |
| session theft | short access lifetime, rotating refresh, secure Windows storage |
| refresh replay | token family reuse detection and revoke |
| shared account | one account per employee, audit, admin policy |
| disabled user remains active | immediate session revocation and realtime close |
| login enumeration | uniform errors and timings where practical |
| admin reset abuse | separate permission, audit, one-time temporary credential |

## 15.4. Authorization risks and controls

- server-side enforcement for every query/command;
- deny by default;
- object/field-level policies;
- organization predicate;
- IDOR tests;
- bulk operation per-item validation;
- current permissions rechecked when returning history/files;
- cache invalidation after role change;
- no trust in client-supplied role, author, department or owner;
- administrative bypass explicit and audited.

## 15.5. Network security

- TLS 1.2+ according to corporate policy;
- trusted internal CA;
- firewall allows API only from company subnets/VPN;
- PostgreSQL not exposed to user VLAN;
- DB listens on private interface/socket;
- reverse proxy request limits;
- separate management access;
- no plaintext HTTP fallback;
- secure headers where relevant;
- WebSocket authenticated and origin/client constraints applied.

## 15.6. Server hardening

- dedicated service account;
- least privilege filesystem rights;
- no interactive login for service user;
- patched supported OS;
- host firewall;
- disk encryption where feasible;
- secrets outside repository;
- automatic security updates only with controlled maintenance window;
- disabled unused ports/services;
- SSH/RDP limited to admin network and keys/MFA where available;
- immutable deployment artifacts;
- container runs non-root if containers used.

## 15.7. Database security

- separate DB roles for migration, runtime read/write and backup;
- runtime role cannot drop schema;
- TLS/local socket;
- no direct employee access;
- encryption of host disk/backup;
- statement/log redaction;
- backup credentials separated;
- audit database operations for administrators;
- regular integrity and restore checks.

## 15.8. Desktop security

- signed installer and executable;
- update signature verification;
- local cache encryption;
- tokens in Credential Manager/DPAPI;
- no secrets in logs;
- auto-lock based on session policy;
- clear cache on logout/account revoke;
- validate deep links;
- no embedded browser with arbitrary privileged bridge;
- safe file opening through OS API, not shell command interpolation;
- least privilege: app runs as standard user, not administrator.

## 15.9. File-related threats

### Malicious path substitution

Контроль: permission `FileLocation.Update`, audit, allowed roots, visible location, no auto-open.

### UNC credential leakage

Открытие UNC на недоверенный host может инициировать Windows authentication. Контроль: ограничить shared locations approved SMB roots/hosts; предупреждать внешние UNC; не загружать previews автоматически.

### Dangerous file types

Контроль: explicit user action, warning, corporate endpoint protection, no automatic execution, extension and actual type checks for system attachments.

### Symlink/junction surprises

Client probe может попасть за allowed root через reparse point. Для строгой root policy проверяется resolved final path, насколько позволяет OS API. В MVP root policy должна быть защитой от случайных путей, а не заменой Windows ACL.

### Physical file deletion

Обычное удаление CatalogItem никогда не вызывает filesystem delete. Если когда-либо добавляется физическое удаление, это отдельная permission, отдельный экран, повторное подтверждение и audit; в текущем MVP функции нет.

## 15.10. Input validation

- длины title/description/comment;
- enum allowlists;
- date ranges;
- recurrence complexity limit;
- path/URL validation;
- no arbitrary SQL/filter expressions from client;
- JSON depth/size limits;
- file avatar decode and re-encode;
- control characters normalized;
- rich text sanitized or plain text only.

## 15.11. Injection

- parameterized SQL/ORM;
- no dynamic SQL from user fields;
- no command shell;
- output encoding in UI;
- URL scheme allowlist;
- logs structured, newline/control chars escaped;
- search query parser limited and parameterized.

## 15.12. CSRF, CORS and desktop specifics

Bearer-token desktop API менее подвержен browser CSRF, но:

- CORS выключен или ограничен;
- refresh token не хранится в browser cookie для desktop flow;
- localhost callback не нужен без external identity;
- API не принимает unauthenticated browser origins;
- deep links require validation and user confirmation for sensitive action.

## 15.13. Audit security

Security events:

- login success/failure;
- lockout/block/unblock;
- password reset/change;
- session create/revoke;
- role/membership change;
- permission denied for sensitive action;
- backup execution/restore;
- server configuration change;
- shared file location change;
- bulk export, если появится.

Audit rows append-only. Доступ разделён: object history и security audit — разные permissions.

## 15.14. Logging and privacy

Нельзя логировать:

- password;
- tokens;
- authorization header;
- full comment/description by default;
- full local path в общих operational logs, если он может содержать имя пользователя;
- contact email/phone без маскировки;
- query payload целиком.

Логируются IDs, operation, duration, result code, correlation ID and safe metadata.

## 15.15. Backup security

- encryption before leaving server;
- separate key custody;
- access only backup operators;
- immutable/offline copy;
- checksums;
- restore environment isolated;
- backup does not expose plaintext credentials;
- key loss tested as operational risk.

## 15.16. Supply chain

- dependency lock files;
- approved package sources;
- vulnerability scanning;
- SBOM;
- signed builds;
- CI secrets protected;
- reproducible release process;
- review of third-party encryption/notification libraries;
- no auto-update from public uncontrolled URL.

## 15.17. Security testing

До production:

- threat model review;
- authentication/session tests;
- authorization matrix tests;
- IDOR and mass assignment tests;
- path/URI fuzzing;
- concurrency tests;
- backup restore drill;
- dependency scan;
- static analysis;
- penetration test focused on LAN assumptions;
- desktop local data extraction test.

## 15.18. Остаточные риски

- malware под user account может читать то, что доступно пользователю;
- приложение не контролирует содержимое рабочих файлов;
- screenshot/фотографирование данных не предотвращается;
- локальный файл может быть утрачен без корпоративного endpoint backup;
- internal admin с OS/DB access обладает высокими возможностями;
- LAN compromise остаётся опасным, поэтому TLS и firewall обязательны.

---

# 16. Резервное копирование

## 16.1. Цели

Backup architecture должна обеспечивать:

- восстановление после удаления/повреждения БД;
- восстановление после отказа диска/сервера;
- защиту от ransomware;
- проверяемый RPO/RTO;
- отдельное резервирование file infrastructure.

## 16.2. Что копируется

### Обязательно

- PostgreSQL base backup;
- transaction/WAL archive для point-in-time recovery;
- server configuration templates;
- encrypted secrets/key references according to recovery runbook;
- internal CA/server certificates and rotation metadata;
- system assets: avatars;
- signed desktop installers/update manifests;
- migration binaries/scripts;
- backup catalog and checksums.

### Отдельным корпоративным процессом

- SMB file server shares;
- NAS snapshots;
- shared network folders;
- local workstation files, если они критичны.

Органайзер не может гарантировать backup файла, который хранится только на ноутбуке сотрудника.

### Не нужно копировать

- desktop caches;
- access tokens;
- temporary files;
- generated logs beyond retention;
- rebuildable search projections, если их можно восстановить из БД, хотя backup БД обычно уже включает их.

## 16.3. Рекомендуемая схема

```text
Production PostgreSQL
  |-- continuous WAL archive every few minutes
  |-- nightly physical base backup
  v
Local Backup Repository on separate storage
  |-- checksum + encryption
  |-- retention
  v
Secondary offline/off-host copy
  +-- NAS with immutable snapshots OR encrypted removable/offsite storage
```

Принцип: минимум три копии данных, два типа носителя/контекста, одна копия вне production host. Внешнее облако не обязательно; offsite может быть физическим защищённым носителем или вторым офисом.

## 16.4. Расписание

Предложение:

- WAL/archive: непрерывно, цель RPO 15 минут или меньше;
- base backup: ежедневно ночью;
- full backup verification: ежедневно checksum/catalog;
- test restore: ежемесячно;
- disaster recovery drill: ежеквартально;
- configuration backup: после каждого изменения и ежедневно;
- NAS/file server snapshots: по отдельной политике, например каждые 4 часа + daily.

## 16.5. Retention

Пример grandfather-father-son:

- daily: 14–30 дней;
- weekly: 8–12 недель;
- monthly: 12 месяцев;
- yearly: по юридической/корпоративной необходимости;
- WAL: достаточно для PITR внутри доступного base backup window.

## 16.6. Шифрование и ключи

- backup шифруется отдельным ключом;
- ключ не хранится только на production server;
- минимум две контролируемые копии recovery key;
- доступ документирован и ограничен;
- rotation не делает старые backups невосстановимыми;
- restore drill проверяет доступность ключа.

## 16.7. Успешность backup

Backup считается успешным, только если:

1. процесс завершился без ошибки;
2. размер правдоподобен;
3. checksum совпадает;
4. backup catalog записан;
5. secondary copy подтверждена;
6. периодический restore реально стартует и проходит consistency checks.

## 16.8. Восстановление БД

Runbook:

1. Объявить incident и остановить write traffic.
2. Зафиксировать desired recovery time.
3. Подготовить чистый compatible PostgreSQL host.
4. Восстановить base backup.
5. Применить WAL до target time.
6. Запустить DB consistency checks.
7. Проверить schema/application version.
8. Запустить API в restricted readiness mode.
9. Выполнить smoke tests: login, task query, audit, catalog metadata.
10. Сменить DNS/service endpoint или вернуть production.
11. Клиенты выполняют sync reset при необходимости.
12. Документировать data loss window.

## 16.9. Восстановление отдельного объекта

PostgreSQL backup не предназначен для прямого восстановления одной task в production. Процедура:

- restore backup во временную изолированную DB;
- экспортировать нужный object graph;
- провести security review;
- восстановить через специальную admin/import command с audit;
- не выполнять ручной `INSERT` в production без runbook.

В обычном случае используется корзина, а не backup.

## 16.10. File backup coordination

Catalog metadata и физические файлы могут восстанавливаться на разные моменты. Для снижения несогласованности:

- хранить file metadata observed modified time/size;
- документировать независимые RPO;
- после restore запускать catalog health scan по активным shared links с ограничением;
- broken links не удалять автоматически;
- NAS snapshot и DB backup по возможности координировать по времени.

---

# 17. Каталог отказов и ожидаемое поведение

## 17.1. Сеть и сервер

| Отказ | Обнаружение | Поведение клиента | Поведение сервера/оператора |
|---|---|---|---|
| API server выключен | timeout/connection refused | read-only cache, banner, commands disabled | alert, restart/failover runbook |
| DNS не разрешается | name resolution error | показать server unavailable, не предлагать отключить TLS | проверить DNS/VLAN |
| TLS certificate expired | validation error | блокировать соединение, показать конкретную причину | заменить сертификат; не советовать «продолжить небезопасно» |
| TLS certificate mismatch | validation error | блокировать | проверить spoofing/config |
| Reverse proxy работает, API нет | 502/503 | read-only, retry with backoff | health check/restart API |
| Realtime hub недоступен | websocket error | продолжить REST + periodic sync, indicator degraded | восстановить hub/API |
| Packet loss/high latency | timeouts | cancel/retry idempotent reads, no blind command retry | network diagnostics |
| Сеть пропала во время команды | ambiguous result | запросить command result by idempotency key после reconnect | server returns stored outcome |
| VPN разорван | network error | read-only cache, local files only | user restores VPN |
| Server overloaded | 429/503 | backoff, show degraded state | inspect metrics, throttle jobs |

## 17.2. База данных

| Отказ | Поведение |
|---|---|
| PostgreSQL недоступен | readiness false; API read/write returns 503; client read-only cache |
| DB connection pool exhausted | bounded wait then 503; alert; no unbounded threads |
| Deadlock | transaction rolled back; server retries safe transaction limited times or returns retryable error |
| Constraint violation | 409/422 domain error, no partial write |
| Disk full | writes blocked; critical alert; API enters degraded mode; backup/log rotation checked |
| WAL disk full | stop writes safely; do not delete WAL blindly; operator runbook |
| Corrupted index | detect query errors; rebuild index; DB remains protected |
| Suspected data corruption | stop writes, snapshot evidence, restore/DB repair runbook |
| Migration failed | new API does not become ready; previous version remains/rollback |
| Schema/client incompatibility | server enforces minimum client and clear update message |
| Long transaction/blocking | timeout/cancel, metrics and slow query alert |
| Backup interferes with performance | throttle/schedule; do not disable backup permanently |

## 17.3. Авторизация и сессии

| Отказ/событие | Поведение |
|---|---|
| Неверный пароль | generic error, attempt counter |
| Account locked | generic/controlled message, no access |
| Account blocked while app open | sessions revoked, realtime closed, next request 401, cache locked |
| Refresh token expired | login required |
| Refresh token replay | revoke family, security alert, login required on device |
| Clock skew on client | server timestamps authoritative; warning if notification scheduling affected |
| Role revoked | security invalidation, cache purge, open screens close |
| Admin loses access mid-operation | transaction checks current permission; operation denied/rolled back |
| Credential store unavailable | session cannot persist; allow current memory session or require login, no plaintext fallback |

## 17.4. Синхронизация и конфликты

| Отказ | Поведение |
|---|---|
| Change batch partially fails locally | rollback local transaction, cursor unchanged, retry |
| Duplicate realtime event | deduplicate by sequence/version |
| Gap in sequence | trigger delta sync |
| Cursor too old | full scoped resync |
| Cache corrupted | quarantine/delete cache and bootstrap; server data unaffected |
| Two users edit same task | 409 conflict; no silent overwrite |
| Same notification action sent twice | idempotency key returns same result |
| Client crashes after server commit | on restart sync retrieves committed result |
| Client crashes before send | draft not saved; optional local unsaved draft recovery clearly marked |
| Permissions change during sync | scope version invalidates batch; restart scoped sync |
| Server clock corrected | UTC source; recompute pending notifications, avoid duplicate keys |

## 17.5. Файлы и хранилища

| Отказ | Поведение |
|---|---|
| Файл удалён | broken reference retained, relink options |
| Файл переименован | NotFound; manual relink |
| Файл перемещён | NotFound; manual relink/add location |
| Нет Windows ACL | AccessDenied distinct from missing |
| NAS выключен | ResourceUnavailable, no deletion suggestion |
| SMB share name changed | all related locations unavailable; admin bulk relink tool may be added |
| Mapped drive not connected | device-local unavailable; try UNC if stored |
| Локальный файл на другом PC | DeviceMismatch status |
| Device renamed | stable device ID retains location; display name updates |
| Device retired | locations orphaned, admin report |
| File locked by another program | opening may succeed read-only or fail via target app; organizer shows OS result if available |
| Path too long/invalid | validation error/relink |
| Malicious external UNC | blocked by allowed host/root policy or warning |
| Executable file | explicit warning, no auto-open |
| Different content in two locations | show possible copy divergence based on metadata; user resolves |
| File server restored to older snapshot | metadata mismatch warning; links remain |
| Folder deleted with many linked files | individual references become unavailable; no automatic catalog purge |

## 17.6. Desktop and Windows

| Отказ | Поведение |
|---|---|
| App crash | crash log without secrets; next start cache integrity check |
| Tray agent crash | main app detects/restarts within same process architecture or user notified |
| Windows notification disabled | in-app notification center remains; settings show OS limitation |
| App not in autostart | warning if user expects background reminders |
| Windows sleep at reminder time | on wake reconcile and show missed/overdue notification according to policy |
| System time changed | reschedule local timers from server UTC definitions |
| Local disk full | stop cache writes, show error, allow cache cleanup; no server data loss |
| Cache key unavailable | require re-login/recreate cache |
| Antivirus blocks executable/update | show signed file/admin diagnostic; no security bypass |
| Update download corrupted | signature/hash fails, retain current version |
| Update installation fails | rollback installer; server may allow old compatible version |
| Client too old | read-only or mandatory update according to compatibility policy |
| Multiple app instances | single-instance mutex; deep link routed to existing process |
| UI freeze due SMB check | prohibited by async adapter and timeouts |

## 17.7. Background jobs and notifications

| Отказ | Поведение |
|---|---|
| Worker выключен | API works, reminders/recurrence delayed; health alert |
| Reminder job runs twice | unique dedupe key prevents duplicate notification |
| Outbox stuck | committed data remains; realtime delayed; retry/alert |
| Notification delivery fails | mark attempt failed; client sync can still retrieve center item |
| Device offline at due time | server notification remains; local pre-schedule may fire; on reconnect reconcile |
| Quiet hours misconfigured | user settings visible; server uses stored timezone |
| Recurrence rule invalid after update | reject update transactionally |
| Recurrence generator falls behind | on-demand materialization + backlog metric |
| Purge job fails | data retained longer; alert, no emergency delete |

## 17.8. Backup and restore

| Отказ | Поведение |
|---|---|
| Backup target unavailable | production continues; critical alert; retry; RPO risk displayed |
| Backup disk full | backup fails, no deletion of last valid copy before new one succeeds |
| Corrupt backup | checksum/test restore fails; mark unusable |
| Encryption key missing | backup unusable; disaster runbook and key escrow |
| Restore fails | do not overwrite production; use isolated environment |
| Backup contains malware in file share | restore follows corporate malware scanning; organizer DB itself has metadata only |
| Ransomware encrypts production and mounted backup | immutable/offline copy used |
| Operator restores wrong point | target-time confirmation and dry-run metadata report |
| DB restored, file server not restored | links may be broken; health reconciliation report |

## 17.9. Административные ошибки

| Ошибка | Контроль/поведение |
|---|---|
| Удалён последний project owner | server rejects |
| Заблокирован единственный system admin | require second admin or break-glass procedure |
| Неверно назначена роль | audit, effective permission preview, rollback |
| Shared root changed | impact preview and confirmation |
| Массовое удаление | bulk preview, permission, typed confirmation, soft delete |
| Backup retention set too low | minimum policy guardrail |
| Server address changed | signed/configured rollout; clients display mismatch |
| Timezone changed | impact warning for recurrence/reminders |
| Department deleted with users | reject until reassignment or controlled migration |
| User deleted instead of disabled | physical delete prohibited; disable/archive workflow |

## 17.10. Ошибки данных и UX

| Случай | Поведение |
|---|---|
| Deadline раньше start | reject 422 with field errors |
| Negative duration | reject |
| Circular catalog folder | reject transactionally |
| Task assigned to inactive employee | reject or require explicit future activation policy |
| Project completed with active tasks | warning and explicit decision; no implicit completion |
| Delete contact with active tasks | warning; links retain tombstone/require replacement |
| Duplicate contact | warning, allow with reason; no auto-merge |
| Too many watchers/participants | configurable limit |
| Oversized comment/description | field limit and clear error |
| Unsupported URL scheme | reject |
| Empty task title | reject |
| Recurrence explosion | complexity/occurrence limit |

## 17.11. Incident states visible to user

Standard connection states:

- Online;
- Synchronizing;
- Degraded: realtime unavailable;
- Offline: cached view only;
- Authentication required;
- Client update required;
- Server maintenance;
- Local cache error.

Нельзя показывать только общий текст «Что-то пошло не так». Каждая ошибка имеет code, safe message, correlation ID and retry guidance.

---

# 18. Финальная критическая оценка архитектуры

## 18.1. Сильные стороны

1. Server-authoritative модель минимизирует потерю и рассинхронизацию общих данных.
2. Модульный монолит соответствует реальному масштабу и эксплуатационным возможностям одной компании.
3. Файлы не дублируются и не проходят через application server.
4. Права приложения и Windows ACL разделены корректно.
5. Optimistic concurrency предотвращает молчаливое перезаписывание.
6. Change feed + outbox + realtime дают надёжность без зависимости от WebSocket.
7. Локальный read-cache поддерживает приемлемую работу при кратком отказе сервера.
8. Архитектура допускает web/mobile/cloud без переноса бизнес-логики из desktop.
9. Backup и audit являются частью системы, а не послепроектным дополнением.

## 18.2. Слабые места

### 1. Single server остаётся единой точкой отказа

В MVP это осознанный компромисс. При отказе общие изменения остановятся. Улучшение: резервный подготовленный host, автоматизированный restore, позже PostgreSQL replication/HA.

### 2. Online-only write mode ограничивает работу при нестабильной сети

Это безопаснее для MVP, но снижает удобство удалённой работы. Полноценный offline write потребует command log, conflict semantics по каждому aggregate и security model для отозванных прав. Его нельзя добавлять как простой «локальный флаг».

### 3. File links хрупки к ручным перемещениям

Это фундаментальное следствие отказа от DMS и file agent. Улучшения позже: approved roots watcher, file ID/fingerprint, bulk relink, DFS namespace. Полный автоматический поиск по дискам не рекомендуется без отдельного privacy/performance проекта.

### 4. Несколько locations могут указывать на разные копии

Система не контролирует содержимое. Улучшение: optional checksum/version metadata и явная primary location policy. Но вычисление hash больших файлов дорого и не решает совместное редактирование.

### 5. Гибридная authorization model сложна

Она нужна требованиям, но является зоной высокого риска. Требуются централизованный policy engine, matrix tests и effective permission inspector для администраторов. Нельзя размазывать проверки по UI и SQL.

### 6. Локальный кэш создаёт остаточный риск данных на устройстве

Шифрование и revocation снижают риск, но malware/пользователь с активной Windows session может видеть доступные данные. Для строгой среды потребуется device management, BitLocker, EDR и remote wipe вне приложения.

### 7. WPF привязывает desktop к Windows

Это соответствует продукту. Будущая web/mobile версия будет отдельным клиентом. Попытка выбрать cross-platform UI только ради гипотетического будущего может ухудшить Windows integration сейчас.

### 8. PostgreSQL полнотекстового поиска может не хватить

Для metadata search MVP достаточно. При миллионах комментариев, сложной морфологии и содержимом документов потребуется отдельный search engine. Граница Search module уже предусмотрена.

### 9. Уведомления не имеют абсолютной гарантии показа

Windows settings, sleep и закрытое приложение могут препятствовать toast. Компенсация: tray autostart, in-app center, server-side durable notifications, reconciliation on wake.

### 10. Backup физических файлов находится вне organizer

Это необходимо явно донести компании. Наличие ссылки в каталоге не означает, что файл защищён. Нужна отдельная корпоративная backup policy для NAS/SMB и особенно локальных PC.

## 18.3. Компромиссы

| Решение | Выгода | Цена |
|---|---|---|
| Модульный монолит | простота deployment и транзакций | нельзя независимо масштабировать модули без выделения |
| Online-only writes | простая согласованность | нет полноценной офлайн-работы |
| Metadata-only files | нет дублирования и больших storage costs | broken links и отсутствие version control |
| PostgreSQL search | минимум инфраструктуры | ограниченная морфология/масштаб относительно search engine |
| Local notification agent | нет облачной зависимости | зависит от Windows/app lifecycle |
| Opaque sessions with server state | простой revoke | дополнительная server lookup/cache |
| Soft delete | восстановимость | рост БД и сложнее retention |
| Detailed audit | контроль и расследование | storage, privacy и query complexity |

## 18.4. Улучшения, которые стоит включить до начала кодирования

1. Утвердить точный permission catalog и state transition diagrams.
2. Утвердить SLA, RPO, RTO и retention.
3. Провести инвентаризацию реальных file shares, UNC roots, ACL и device naming.
4. Утвердить, нужен ли независимый CalendarEvent в MVP или только task-based calendar.
5. Утвердить plain text или rich text для descriptions/comments.
6. Утвердить количество пользователей и объём исторических данных.
7. Утвердить политику удаления/архива и срок корзины.
8. Утвердить, кому видны точные локальные пути других устройств.
9. Создать ADR для server OS/deployment mode.
10. Выполнить proof of concept: WPF tray notifications, TLS internal CA, SMB error classification, delta sync.

## 18.5. Решения, которые нельзя откладывать

- разделение UserAccount и EmployeeProfile;
- organization ID;
- object versioning;
- audit/outbox/change feed в одной транзакции;
- permission engine;
- file location scope/device identity;
- UTC/timezone model;
- soft delete;
- signed update process;
- backup restore test.

Если эти решения добавить после реализации основных экранов, потребуется существенная переработка БД, API и клиента.

## 18.6. Итоговая оценка

Архитектура пригодна как основа разработки MVP и последующего промышленного внедрения при следующих условиях:

- сервер размещается на выделенном постоянно работающем host;
- локальная сеть и файловые shares администрируются отдельно;
- security/backup не вырезаются из MVP;
- offline editing не добавляется без отдельного архитектурного этапа;
- права реализуются server-side до построения основных UI-сценариев;
- команда начинает с architecture spikes и contract tests, а не только с экранов.

Основной технический риск продукта находится не в CRUD задач, а в четырёх областях: гибкие права, синхронизация кэша, повторяющиеся задачи/уведомления и корректная работа со ссылками на файлы в неоднородной Windows-инфраструктуре.

---

# Приложение A. Рекомендуемые архитектурные модули и зависимости

```text
Identity <---------------- Administration
   |
   v
Directory
   |
   +------> Authorization <---------------- Project Membership
   |              ^
   |              |
   +--> Tasks ----+----> Calendar
   |      |
   |      +-----------> Notifications
   |      +-----------> Comments
   |      +-----------> Object Links
   |
   +--> Projects ------> Object Links
   +--> Contacts ------> Object Links
   +--> Catalog -------> Object Links

All write modules ---> Audit
All write modules ---> ChangeFeed
All event producers -> Outbox -> Worker -> Realtime/Notifications/Search
```

Запрещённые зависимости:

- Identity не зависит от Tasks/Projects;
- Authorization не зависит от Desktop;
- Catalog не зависит от Windows APIs на сервере;
- Audit не вызывает business modules;
- Search не является source of truth;
- Notification delivery не изменяет task напрямую, только вызывает explicit command при user action.

# Приложение B. Предварительный набор серверных внутренних событий

| Событие | Основные потребители |
|---|---|
| UserAccountBlocked | Sessions, Sync, Audit, Realtime |
| UserRoleChanged | Authorization cache, Sync, Audit |
| ProjectMembershipChanged | Authorization, Sync, Notifications |
| TaskCreated | Search, Notifications, Sync |
| TaskUpdated | Search, Sync, Notifications policy |
| TaskAssigned | Notifications, Audit |
| TaskStatusChanged | Notifications, Calendar projection |
| TaskScheduleChanged | Calendar, Reminder scheduler |
| ReminderChanged | Notification scheduler |
| CommentAdded | Notifications, Search, Activity projection |
| CatalogItemChanged | Search, Sync |
| FileLocationChanged | Audit, Sync |
| ContactChanged | Search, Sync |
| ObjectMovedToTrash | Search, Sync, Reminder cancel |
| ObjectRestored | Search, Sync |
| PermissionScopeChanged | Cache revocation, Realtime |

# Приложение C. Нефункциональные требования к реализации

## Надёжность

- ни одна команда не создаёт partial state;
- все side effects после commit идут через outbox;
- client cache rebuildable;
- retries bounded and idempotent;
- backup restore проверяется.

## Поддерживаемость

- module ownership;
- architecture tests запрещают зависимости;
- API contract tests;
- database migration tests;
- structured error codes;
- ADR repository.

## Наблюдаемость

- request rate/latency/error;
- DB connections/slow queries;
- realtime connections/reconnects;
- sync lag and batch failures;
- outbox backlog;
- reminder job lag;
- disk usage;
- backup age and restore test age;
- certificate expiry;
- client version distribution.

## Тестирование

- unit tests domain transitions;
- policy matrix tests;
- integration tests with real PostgreSQL;
- desktop API contract tests;
- file adapter tests on local/UNC/denied/not found;
- concurrency tests;
- migration/rollback tests;
- load tests;
- disaster recovery drills.

# Приложение D. Минимальные Architecture Decision Records

1. ADR-001: Modular Monolith instead of Microservices.
2. ADR-002: Windows WPF Desktop Client.
3. ADR-003: PostgreSQL as authoritative database.
4. ADR-004: Online-only writes in MVP.
5. ADR-005: Incremental sync via change sequence.
6. ADR-006: Optimistic concurrency, no blind last-write-wins.
7. ADR-007: File metadata and multiple scoped locations.
8. ADR-008: Hybrid RBAC/ReBAC/ABAC authorization.
9. ADR-009: Outbox for post-commit effects.
10. ADR-010: UTC plus explicit timezone.
11. ADR-011: Soft delete and retention.
12. ADR-012: Internal TLS and certificate management.
13. ADR-013: Signed desktop updates.
14. ADR-014: Database PITR and separate file backup.

# Приложение E. Definition of Architecture Ready

Разработка feature-модулей может начинаться после выполнения условий:

- утверждены ADR 001–014;
- поднят local development environment;
- работает TLS dev/prod model;
- определены API/error conventions;
- создан permission catalog;
- реализован skeleton identity/session;
- реализованы transaction + audit + outbox primitives;
- реализован change feed prototype;
- доказано открытие local/UNC files с корректной классификацией ошибок;
- создан backup/restore prototype;
- проведён threat model review;
- созданы migration and architecture dependency tests.

