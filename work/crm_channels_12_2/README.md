# Task 12.2 — способы связи и адреса, версия 12.2.0

Продолжение `feature/crm-contacts-workspace` от `935e479` (12.1).
Изменения выполняются в существующей корпоративной карточке; личные контакты,
серверные permissions, lifecycle, OpenAPI и БД не меняются.

## Подключённый контракт

`GET /api/v1/contacts/{id}`: массивы `channels` и `addresses`, camelCase.
Каналы: `id`, `channelType`, `value`, nullable `label`, `isPrimary`.
POST/PATCH передают только `channelType`, `value`, `label`, `isPrimary`.
`ownerObjectId` определяется маршрутом; `isVerified` не отправляется.
Типы: phone/email/telegram/whatsapp/viber/other_messenger/website.
Значение: 1–1000 символов после trim; подпись: до 100.

Адреса: `id`, `addressType`, обязательный `formattedAddress` (1–1000),
nullable `countryCode` (2), `region` (200), `city` (200), `street` (500),
`postalCode` (40), `isPrimary`. Типы: work/legal/postal/other.
Необязательные пустые поля отправляются как null; пустые части адреса не выводятся.

POST channels, PATCH channels/{childId}, DELETE channels/{childId}, POST addresses
используют strong parent `If-Match: "vN"`. POST и PATCH каналов и POST адресов
получают idempotency key; DELETE ключа не требует. Сервер повышает parent version
в `PostgresProductApiRelations.Related/Bump` и возвращает её в ETag, включая DELETE 204.
Клиент проверяет strong advanced ETag и повторно читает контакт; обновление списков
и выбранного контакта происходит только после этого чтения. Ответ GET со старой
версией не подтверждает запись. Ошибка/отмена неопределённой операции блокирует
повтор до отмены формы и обновления карточки. Автоматического повтора мутации нет.

Одновременно разрешена одна мутация карточки, включая основные поля.
Обновление списка не меняет версию открытого черновика дочернего объекта.
Parent PATCH не обогащает ответ связями: его успешный CAS сохраняет неизменённую
подтверждённую проекцию каналов/адресов. Удаление канала требует подтверждения.
Отзыв чтения/сессии очищает дочерние данные и формы; поздние результаты другого
контакта игнорируются. Значения не включаются в технические сообщения ошибок.

## Ограничения

- PATCH/DELETE адресов отсутствуют в нормативных routes и не предлагаются в UI.
- `isPrimary` — разрешённый boolean. Сервер не гарантирует единственность основного
  объекта и не демотирует остальные; клиент не придумывает эти правила.
- Уникальность одинаковых значений каналов не гарантируется текущей БД; клиент
  не вводит новую бизнес-политику дублей. HTTP 409/412/422 обрабатываются без успеха.
- Contact.Read/Update управляют доступностью UI; object-level scope проверяет сервер.
- Нет карт, геокодирования, внешних вызовов/сообщений, компаний и взаимодействий.

## Проверка

Команды из корня этой рабочей копии:

```powershell
dotnet build work/production/Task.sln -c Release
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --settings work/crm_contacts_12_1/desktop.runsettings
dotnet test work/production/tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release
python work/crm_channels_12_2/package.py
```

Артефакты: `outputs/20261018_crm_channels_addresses_12.2.0` в основной папке проекта.
Точные счётчики и исходные TRX входят в validation-report и пакет.
Native WPF проверяет существующую карточку, формы канала/адреса, подтверждение удаления,
read-only и прокрутку к сохранению при 800×600; PNG сохраняются через
`TASK_CONTACT_CARD_EVIDENCE`. Сбой TaskContextActionsUiTests на `company.Activate()`
воспроизведён на чистом `935e479`. Финальный общий прогон: 1510 PASS / 0 FAIL / 0 SKIP;
API 644 PASS / 0 FAIL; Release solution build PASS. Исходные неудачные прогоны сохранены.
PostgreSQL/live Corporate acceptance не выполнены: Docker daemon недоступен,
`TASK_POSTGRES_TEST_ADMIN_CONNECTION` не задан. Прогоны, молча пропускающие
БД по отсутствию переменной, не выдаются за проверку PostgreSQL.

Основа для 12.3 подготовлена. Live приёмка остаётся открытой;
готовность релиза или завершение всего CRM не объявляются.
