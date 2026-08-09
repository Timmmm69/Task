# Этап 2.2 — Contract Recovery и восстановление целостности пакета

## 1. Статус

Этап 2.2 является корректирующим выпуском нормативных артефактов Этапа 2. Он не изменяет UX-архитектуру и не создаёт новую продуктовую архитектуру.

Итоговый статус: настоящий OpenAPI найден и подтверждён по нескольким независимым копиям. Design-first восстановление с нуля не выполнялось. Контракт 2.2 основан на подтверждённом OpenAPI 2.1 и содержит целевые исправления Search API, field-level metadata, endpoint metadata, C# code generation и контроля происхождения.

## 2. Результат проверки происхождения

### 2.1. Найденный нормативный OpenAPI 2.1

Исходный файл:

`C:\Users\novik\Downloads\Organizer_Stage2_Technical_Specification_2.1\openapi\openapi.yaml`

Проверенные характеристики исходного файла:

- первый ключ: `openapi: 3.1.0`;
- присутствуют `paths`, `components` и `components.schemas`;
- размер: `769190` байт;
- SHA-256: `E3D1D1D20AFB5EB34B5CB06525CF31245769CF9C6F551146E2E843BF1C0C4A37`;
- операций: `241`;
- schemas: `232`;
- локальные `$ref`: разрешаются;
- содержимое является YAML/OpenAPI, а не Markdown.

Идентичный SHA-256 подтверждён для:

1. папки Этапа 2.1;
2. записи `openapi/openapi.yaml` в `Organizer_Stage2_Technical_Specification_2.1.zip`;
3. записи OpenAPI в `Organizer_Project_Final_Baseline_Stage_2_1.zip`;
4. файла в `Organizer_Stage3_Input_Package`;
5. записи OpenAPI в `Organizer_Stage3_Input_Package.zip`.

SHA-256 канонического ZIP Этапа 2.1: `A293F576D7FF781ACA75222D709F323369C950E740072921F548999C8E83A715`.

### 2.2. Генератор и прежние доказательства

Исходный OpenAPI 2.1 воспроизводился скриптом:

`qa/build_openapi.py`

SHA-256 версии генератора 2.1: `A4B287409B8C3E10E6D052AD0F5B42088ED4333CA65ED86DCDDC1ED1D9CA59FE`.

В исходном пакете обнаружены:

- `qa/reports/openapi_lint.log` с успешной OpenAPI validation;
- `qa/reports/artifact_validation.log` с `operations=241` и `schemas=232`;
- `qa/reports/codegen_validation.log`;
- сгенерированный TypeScript desktop SDK;
- сгенерированный server contract и handlers для 241 operation ID;
- strict TypeScript compilation result `PASS`.

Эти артефакты согласованы с SHA-256 найденного OpenAPI. Утверждение Stage 3 о том, что переданный `openapi.yaml` является Markdown, не подтверждается фактическим содержимым ни одной проверенной копии.

### 2.3. Другие проверенные источники

- Старый `Organizer_Stage2_Technical_Specification.zip` содержит более ранний OpenAPI другого размера и SHA-256; он не выбран источником 2.2.
- Текущий Git-репозиторий `STOK` относится к другому продукту и не содержит истории Organizer OpenAPI.
- Локальных CI artifacts, содержащих более новый Organizer OpenAPI, не обнаружено.
- Временные каталоги code generation не содержат альтернативного нормативного контракта новее подтверждённого 2.1.

## 3. Реальные дефекты, устранённые в 2.2

1. Search API не содержал обязательные фильтры `contactIds` и `hasFiles`.
2. Search использовал неоднозначный строковый `status`, неприменимый единообразно к разным `types`.
3. Cursor contract не фиксировал связь курсора с фильтрами, authorization scope и search snapshot.
4. Не было нормативного запрета client-side post-filtering paged results.
5. Не существовало C# desktop client generation и C# server stub compilation evidence.
6. Не было отдельного field-level DTO catalog.
7. Access policies `Anonymous` и `Authenticated` были записаны в поле `x-permission`, хотя не являются permission codes.
8. Sensitive `FileLocation.rawPath` не имел явной field-level redaction semantics.
9. Auth secrets не везде имели `readOnly`/`writeOnly` metadata.
10. Отчёты и manifest не позволяли отличить фактическое содержимое OpenAPI от ошибочного внешнего описания файла.

## 4. Метод восстановления

1. Подтверждённый OpenAPI 2.1 принят как единственный исходный контракт.
2. Генератор `qa/build_openapi.py` обновлён, чтобы изменения были воспроизводимыми.
3. Каталог `catalogs/api_catalog.csv` сохранён каноническим реестром method+path из 241 операции.
4. Search contract исправлен без добавления новой продуктовой функции.
5. Новые stable error codes добавлены только для cursor mismatch и expiration.
6. Схемы DTO не заменялись пустыми объектами и не ослаблялись через `additionalProperties: true`.
7. OpenAPI и каталоги проверены автоматическим gate `qa/stage_2_2_contract_gate.py`.
8. C# desktop client и ASP.NET Core server stubs сгенерированы NSwag из финального OpenAPI и скомпилированы .NET 8.

## 5. Нормативные результаты

- OpenAPI: `openapi/openapi.yaml`.
- Версия API description: `1.2.0-stage2.2`.
- Операции: `241`.
- DTO/schemas: `232`.
- Field-level DTO rows: `1322`.
- Permissions: `91`.
- Stable errors: `44`.
- Contract differences against canonical operation catalog: `0`.
- Внешние `$ref`: `0`.
- Пустые business schemas: `0`.
- Неограниченные `additionalProperties: true`: `0`.

## 6. Приоритет документов

При конфликте внутри Этапа 2 действуют:

1. `Stage_2_2_Contract_Recovery.md`;
2. `Search_Contract.md`;
3. `openapi/openapi.yaml`;
4. `catalogs/api_catalog.csv`, `catalogs/permissions.csv`, `catalogs/errors.csv`;
5. `dto_field_catalog.csv`;
6. `docs/06_stage_2_1_normative_corrections.md`;
7. остальные документы 2.1.

Validation reports являются доказательствами, а не самостоятельными источниками бизнес-требований.

## 7. Решение по AUD-001

`AUD-001 / GAP-001` может быть закрыт после проверки архивных SHA-256 и использования именно файла `openapi/openapi.yaml` из выпуска 2.2. Контракт является машиночитаемым, DTO конкретны, C# code generation и compilation проходят, Search соответствует концепции, а field-level данные доступны в `dto_field_catalog.csv`.

Field-level delta Этапа 3 разрешён. Полный повторный UX redesign не требуется.
