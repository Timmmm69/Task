# Stage 4. Dependency and Risk Register

**Версия:** 4.1.2-candidate.1

## 1. Dependencies

| DEP | Module | Dependency | Verification | Status |
| --- | --- | --- | --- | --- |
| DEP-001 | MOD-001 | MOD-002, MOD-018, MOD-019, MOD-020 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-002 | MOD-002 | Все модули | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-003 | MOD-003 | MOD-005, MOD-008, MOD-009, MOD-015, MOD-020 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-004 | MOD-004 | MOD-002, MOD-005, MOD-011, MOD-012, MOD-017 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-005 | MOD-005 | MOD-003, MOD-006, MOD-007, MOD-008, MOD-009, MOD-010, MOD-013, MOD-021 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-006 | MOD-006 | MOD-005, MOD-021 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-007 | MOD-007 | MOD-005, MOD-009, MOD-019, MOD-020 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-008 | MOD-008 | MOD-003, MOD-005, MOD-009, MOD-015, MOD-018 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-009 | MOD-009 | MOD-003, MOD-005, MOD-007, MOD-008, MOD-010, MOD-020 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-010 | MOD-010 | MOD-005, MOD-009, MOD-011, MOD-012, MOD-013, MOD-021 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-011 | MOD-011 | MOD-005, MOD-010, MOD-012, MOD-017, MOD-019, MOD-020 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-012 | MOD-012 | MOD-005, MOD-010, MOD-011, MOD-013, MOD-021 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-013 | MOD-013 | MOD-005, MOD-010, MOD-011, MOD-012, MOD-021 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-014 | MOD-014 | Все предметные модули; MOD-020 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-015 | MOD-015 | MOD-005, MOD-008, MOD-009, MOD-010, MOD-013, MOD-018 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-016 | MOD-016 | Предметные модули, MOD-017, MOD-021 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-017 | MOD-017 | Все удаляемые модули, MOD-016, MOD-021 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-018 | MOD-018 | MOD-001, MOD-002, MOD-008, MOD-015, MOD-019, MOD-020 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-019 | MOD-019 | MOD-001, MOD-010, MOD-011, MOD-018, MOD-020, MOD-021 | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-020 | MOD-020 | Все модули | Contract/integration tests и end-to-end flow по связанным модулям | Required |
| DEP-021 | MOD-021 | Все модули | Contract/integration tests и end-to-end flow по связанным модулям | Required |

## 2. Risks

| RISK | Module | Risk | Impact | Mitigation | Verification |
| --- | --- | --- | --- | --- | --- |
| RISK-001 | MOD-001 | Неправильная очистка токенов/кэша после revoke приведёт к утечке данных. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-002 | MOD-002 | Восстановление небезопасного route может раскрыть объект после потери доступа. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-003 | MOD-003 | Композиционный Today может показать неполную картину без явного section failure. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-004 | MOD-004 | Частичное преобразование InboxItem создаст дубли или потерю source. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-005 | MOD-005 | Неполный transition/review flow приведёт к невозможности приёмки результата. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-006 | MOD-006 | Нарушение depth/order инвариантов создаст неконсистентное дерево. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-007 | MOD-007 | Ошибочный scope one/future/all массово изменит серию. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-008 | MOD-008 | Смешение snooze и schedule изменит рабочий план вместо уведомления. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-009 | MOD-009 | Неверная timezone/DST интерпретация сместит события. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-010 | MOD-010 | Ошибки ownership/overrides создадут потерю доступа или privilege escalation. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-011 | MOD-011 | Выбор неверного location откроет другую копию или раскроет sensitive path. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-012 | MOD-012 | Недостаточная защита PII раскроет контактные данные. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-013 | MOD-013 | ObjectLink может ошибочно восприниматься как grant доступа. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-014 | MOD-014 | Client post-filter нарушит pagination и раскроет неполноту/скрытые объекты. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-015 | MOD-015 | OS может блокировать toast; пользователь пропустит reminder без fallback center. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-016 | MOD-016 | Смешение archive и completed/trash приведёт к неверным действиям. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-017 | MOD-017 | Purge до retention или физическое удаление файла необратимо потеряет данные. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-018 | MOD-018 | UI может обещать неподдерживаемые настройки из концепции. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-019 | MOD-019 | Чрезмерный admin UI увеличит поверхность атаки и риск ошибочного restore. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-020 | MOD-020 | Stale cache/scope или silent overwrite приведут к утечке/потере изменений. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |
| RISK-021 | MOD-021 | Audit может записать секреты или показать историю после отзыва доступа. | High | Acceptance criteria, authorization/concurrency tests, redaction and recovery checks | Targeted audit + integration test |

## 3. Cross-cutting risks

- Contract drift invalidates field/operation traceability and requires regeneration.
- Local server outage blocks shared writes; this is an explicit MVP trade-off.
- Physical files can be unavailable or deleted outside the application; metadata cannot restore bytes.
- Permission mistakes are security defects; UI visibility is never an enforcement boundary.
- `OQ-002` is closed by stable-state normalization. Source-backed `OQ-001` and `OQ-003` remain High and block the Stage 4.2 audit gate until a product/contract decision is made.


## Stage 4.1.2 additions

| ID | Type | Description | Verification/Mitigation |
| --- | --- | --- | --- |
| DEP-022 | Dependency | Stage 2.3.1 urgency APIs/DTO and Stage 3.5 CMP-001/SCR-153 field map | 244-operation and field-traceability gates |
| DEP-023 | Dependency | Employee result contract and server visibility/cursor policy | Search contract/security/pagination tests |
| DEP-024 | Dependency | Notification projection resolves presentation through current organization scale | Existing/future/legacy-client projection tests |
| RISK-022 | Risk | Interval gap/overlap/order defect misclassifies presentation | Boundary tests AC-1792…1795; server authoritative validation |
| RISK-023 | Risk | Employee post-filter leaks counts or destabilizes cursor | Client post-filter prohibited; AC-1813/1818 |
| RISK-024 | Risk | Scale conflict overwrites another admin change | ETag/If-Match, draft preservation, compare/reapply |
| RISK-025 | Risk | Stage 3.5 duplicate FLOW-035 breaks traceability | Preserve project FLOW-035; normalize urgency to FLOW-038; uniqueness gate |

All four risks have implemented acceptance/validation controls; none is an unresolved Critical/High candidate defect.
