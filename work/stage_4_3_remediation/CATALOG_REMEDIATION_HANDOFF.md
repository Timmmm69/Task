# Stage 4.3 — handoff по CSV-каталогам

Статус: исходные каталоги не изменены; итоговые `*_4.3.csv` ещё не выпущены.

## Подтверждённые входные объёмы

| Каталог | Строк | Схема |
|---|---:|---|
| Stage_4_Business_Rules_Catalog_4.1.2.csv | 113 | BR ID, Module, Rule, Source, Related FR, Verification |
| Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv | 1824 | AC ID, Module, FR/BR, Scenario, Priority, Test type, Source, Gherkin |
| Stage_4_NFR_Catalog_4.1.2.csv | 25 | NFR ID, Area, Requirement, Target, Measurement, Source/Assumption, Modules |
| Stage_4_Requirements_Traceability_4.1.2.csv | 497 | Requirement, Module, Concept, SCR, FLOW, STATE, API, DTO field, Permission, Error, AC, Source |

## Нормативная логика remediation

1. Сохранять идентификаторы и историю; выпускать новые файлы с суффиксом `4.3`.
2. Для 112 BR-owned AC сохранять BR как valid primary owner; FR-связь выводить через поле `Related FR` соответствующего BR.
3. Для 354 DATA-owned AC сохранять DATA как valid primary owner. Транзитивную FR-связь выбирать только доказуемой цепочкой:
   `DTO field / source -> operationId -> FR` по requirements traceability и API coverage.
4. Если цепочка не даёт единственного обоснованного FR, не назначать случайный FR. Оставить DATA primary owner и зафиксировать отдельную validated transitive relation/evidence.
5. Для 87 cross-cutting requirements заполнять AC существующими проверками либо добавлять новые AC с сохранением монотонной нумерации; не использовать фиктивную связь.
6. Для 96 BR без Related FR заполнять только существующими FR, которые действительно реализуют правило.
7. Заменить broken target `Stage_3_Field_Traceability.csv` на `Stage_3_Field_Traceability_Final_3.5.csv`.
8. Активные ссылки Stage 3.4 / Stage 2.2 заменить на актуальные Stage 3.5 / Stage 2.3.1 при сохранении исторической provenance там, где это нужно.
9. NFR-024 нельзя переводить из provisional/unverified только механической заменой текста: требуется согласованный контракт OQ-001/MOD-014.
10. `241` менять на `244` только в контексте числа API operationId, а не как глобальную замену.
11. Девять AC со словом `корректно` требуют наблюдаемого результата: AC-1486, AC-1487, AC-1501, AC-1579, AC-1709, AC-1710, AC-1715, AC-1716, AC-1767.

## Технический блокер

Обязательный вызов загрузки bundled spreadsheet runtime завис и был прерван через ~19 минут. В проекте существует ранее подтверждённый junction runtime Этапа 4.2:

`work/stage_4_2_audit/sheet_runtime/node_modules -> C:\Users\novik\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\node_modules`

До подтверждения повторного использования этого loader-provided runtime каталоги не авторились альтернативными библиотеками.

