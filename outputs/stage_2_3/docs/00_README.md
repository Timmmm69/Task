# Этап 2.2. Детальная модель данных, PostgreSQL, API, права и восстановленный контракт

**Продукт:** десктопный органайзер для одной компании  
**Статус:** нормативная техническая спецификация перед реализацией  
**Архитектурная база:** Этап 1, версия 1.0  
**Целевая БД:** PostgreSQL 16+  
**API:** REST `/api/v1`, OpenAPI 3.1.0  
**Идентификаторы:** UUIDv7, генерируются приложением  
**Конкурентность:** optimistic locking через ETag/If-Match  
**Синхронизация:** bootstrap + change feed + WebSocket invalidation  

> Нормативный приоритет: концепция определяет бизнес-функции; Этап 1 определяет архитектуру; данный пакет конкретизирует реализацию. При расхождении действует явно зафиксированное решение раздела 1.

> Корректирующий приоритет 2.2: `../Stage_2_2_Contract_Recovery.md`, `../Search_Contract.md`, `../openapi/openapi.yaml` версии `1.2.0-stage2.2` и `../Stage_2_2_Fix_Registry.md` имеют приоритет над прежними сведениями 2.1 о происхождении, Search API, DTO metadata и contract/codegen validation. Документы 07 и 08 сохранены как свидетельства проверки 2.1, но не заменяют отчёты 2.2 в корне пакета.

## Комплект поставки

| Артефакт | Файл | Назначение |
| --- | --- | --- |
| Том 1 | 01_core_domain_and_data.md | Разделы 1–17: проверка, термины, сущности, агрегаты, ER, PostgreSQL, задачи, recurrence, файлы, CRM, аудит, auth и права. |
| Том 2 | 02_api_and_concurrency.md | Разделы 18–21: API-стандарты, полный каталог операций, OpenAPI и конкурентное редактирование. |
| Том 3 | 03_runtime_operations_and_testing.md | Разделы 22–34: sync, события, jobs, поиск, транзакции, lifecycle, миграции, ошибки, 25 sequence-сценариев, безопасность, производительность, тесты. |
| Том 4 | 04_adr_and_independent_audit.md | Разделы 35–38: артефакты, 15 ADR, независимый аудит, критерии готовности. |
| Коррекции 2.1 | 06_stage_2_1_normative_corrections.md | Нормативные исправления lifecycle, idempotency, sync, tenant isolation, worker leases и файловой модели. |
| Валидация 2.1 | 07_stage_2_1_validation.md | Воспроизводимый протокол PostgreSQL/OpenAPI/codegen/traceability проверок. |
| Реестр и re-audit | 08_stage_2_1_fix_registry.md | Трассировка всех CR/High/Medium и финальное решение о готовности. |
| Схема БД | ../db/001_initial_schema.sql | Нормативный DDL 74 таблиц/партиций и 106 индексов. |
| Коррекции БД | ../db/003_audit_corrections.sql | Исправления независимого аудита: user settings и синхронизированный permission catalog. |
| Foundation 2.1 | ../db/004_stage_2_1_foundation.sql | Исполняемые production-readiness ограничения, таблицы, guards, leases и read-models. |
| OpenAPI | ../openapi/openapi.yaml | Машиночитаемая OpenAPI 3.1.0 с конкретными DTO для полного каталога. |
| Валидация | ../qa/run_validation.ps1 | Clean PostgreSQL deploy, contract tests, lint, code generation и compilation gate. |
| Contract Recovery 2.2 | ../Stage_2_2_Contract_Recovery.md | Происхождение настоящего OpenAPI, границы восстановления и итоговое решение. |
| Search contract 2.2 | ../Search_Contract.md | Нормативные server-side фильтры и cursor-safe pagination. |
| DTO field catalog | ../dto_field_catalog.csv | Field-level required/nullable/enum/limits/readOnly/writeOnly и lifecycle metadata. |
| Contract diff | ../contract_diff_against_traceability.csv | Полное method+path сопоставление 241 операций с `catalogs/api_catalog.csv`. |
| Validation 2.2 | ../openapi_validation_report.md | Результаты parse, validation, parity, refs и contract gates. |
| Code generation 2.2 | ../codegen_validation_report.md | Генерация и строгая компиляция C# desktop client и server stubs. |
| Source integrity 2.2 | ../source_integrity_report.md | Content identity, форматы, SHA-256 и проверка подмены файлов. |

## Контрольные показатели

- Доменные/технические сущности в каталоге: **66**.
- PostgreSQL tables/partitions и индексы: проверяются из live PostgreSQL catalog в отчёте валидации.
- API operations: **241**.
- Стабильные ошибки: **44**.
- Канонические permissions, фактически используемые API: **91**.
- Технические sequence-сценарии: **25**.
- ADR: **22**.

## Порядок применения разработчиками

1. Прочитать том 1 и ADR; спорные фундаментальные решения закрыты там.
2. Применить `001_initial_schema.sql`, `002_seed_authorization.sql`, `003_audit_corrections.sql`, затем `004_stage_2_1_foundation.sql`.
3. Генерировать server/client DTO из `openapi/openapi.yaml`; расширять схемы только совместимо.
4. Реализовать policy engine до бизнес-endpoints; нельзя переносить право решения на desktop.
5. Записывать domain event и outbox в business transaction; change feed строить только idempotent projector после commit.
6. Использовать QA-матрицу и каталог ошибок как основу contract/integration tests.
