# Нормативный Search Contract Этапа 2.2

## 1. Endpoint

`GET /api/v1/search`

- Permission: `Search.Use`.
- Authentication: bearer session.
- Processing: read-only cursor snapshot.
- Filtering: только на сервере, до pagination.
- Client-side post-filtering paged results: запрещено.
- Response: `SearchPage`.

## 2. Query parameters

| Параметр | Тип | Ограничения | Семантика |
|---|---|---|---|
| `q` | string | `minLength=2`, `maxLength=200` | Поисковая строка. |
| `types` | array enum | 1–9 уникальных значений | `task`, `calendar_event`, `project`, `catalog_item`, `file_location`, `contact`, `company`, `interaction`, `comment`. |
| `projectIds` | UUID array | до 100 | Серверный фильтр по проектам. |
| `userIds` | UUID array | до 100 | Серверный фильтр по связанным пользователям. |
| `departments` | UUID array | до 100 | Серверный фильтр по отделам. |
| `contactIds` | UUID array | 1–100 уникальных значений | Объект должен быть связан хотя бы с одним указанным контактом. |
| `hasFiles` | boolean | `true` или `false` | `true` — существует хотя бы одна доступная пользователю `FileLocation`; `false` — доступных файловых расположений нет. |
| `lifecycle` | array enum | 1–2 уникальных значения | `active` и/или `completed`; заменяет неоднозначный `status`. |
| `from` | date-time | RFC 3339 UTC | Нижняя граница релевантной даты объекта. |
| `to` | date-time | RFC 3339 UTC | Верхняя граница релевантной даты объекта. |
| `cursor` | string | 1–512 | Непрозрачный cursor следующей страницы. |
| `limit` | integer | 1–500 | Максимальный размер уже авторизованной и отфильтрованной страницы. |

Array query parameters используют `style=form`, `explode=true`.

## 3. Комбинация фильтров

- Разные группы фильтров соединяются через `AND`.
- Значения внутри одного array-фильтра соединяются через `OR`.
- `types` ограничивает типы до применения остальных фильтров.
- `contactIds`, `hasFiles` и `lifecycle` применяются на сервере только к совместимым типам.
- Несовместимый тип исключается из result set до pagination.
- Если ни один запрошенный тип не поддерживает переданный фильтр, сервер возвращает `422 VALIDATION_FAILED`.
- Фильтрация выполняется после authorization scope derivation и до формирования cursor.

## 4. Lifecycle semantics

- `active` исключает завершённые, архивированные и находящиеся в корзине объекты.
- `completed` выбирает terminal business items, но не включает архив и корзину.
- Если `lifecycle` не задан, сервер не подменяет его скрытым client default; применяются канонические visibility и authorization rules endpoint.
- Type-specific business statuses не передаются через общий Search API как свободная строка.

## 5. File semantics

`hasFiles` проверяет доступные текущему пользователю файловые расположения, а не только существование строки metadata. Недоступный или redacted путь не должен раскрывать sensitive location. Фильтр не означает, что Windows/SMB ACL гарантируют успешное открытие файла.

## 6. Cursor safety

Cursor связан с:

- нормализованным `q`;
- всеми filter values;
- стабильным sort `relevance desc`, `updatedAt desc`, `type asc`, `id asc`;
- authorization scope version;
- search-index snapshot.

Cursor нельзя повторно использовать с изменёнными фильтрами или другим authorization scope.

- `SEARCH_CURSOR_INVALID` / HTTP 400 — cursor повреждён или не соответствует normalized filter hash.
- `SEARCH_CURSOR_EXPIRED` / HTTP 410 — search snapshot или authorization scope version больше недоступны.

В обоих случаях desktop начинает запрос с первой страницы. Клиент не восстанавливает страницу локальной постфильтрацией.

## 7. Response

`SearchPage` содержит:

- `items: SearchSuggestion[]`;
- `nextCursor: string | null`;
- `tookMs: integer >= 0`.

Каждый item уже прошёл серверную authorization filtering и все переданные фильтры. `nextCursor=null` означает конец result set.

## 8. Соответствие концепции

Контракт реализует существующие требования концепции:

- фильтр по контакту — `contactIds`;
- фильтр по наличию файлов — `hasFiles`;
- фильтр активных и завершённых элементов — `lifecycle`;
- совместимость с типами — typed `types` и нормативная compatibility semantics;
- cursor-safe pagination — opaque filter-bound cursor;
- server-side filtering — обязательна, client post-filtering запрещена.
