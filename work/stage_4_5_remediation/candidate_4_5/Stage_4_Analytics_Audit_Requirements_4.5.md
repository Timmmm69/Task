# Stage 4. Analytics and Audit Requirements

**Версия:** 4.5-candidate.1  
**Статус:** Remediated candidate; повторный независимый аудит не выполнен
**Текущие нормативные источники:** Stage 2.3.1 (`docs/03_runtime_operations_and_testing.md`, `catalogs/background_jobs.csv`, `db/004_stage_2_1_foundation.sql`) и Stage 3.5. Stage 2.2/3.4 используются только как историческая provenance.

## 1. Принципы

- Внешняя analytics platform не предполагается.
- Product/diagnostic events не содержат title, description, comment body, search query, contact data, credentials или raw file path.
- Audit events не являются product analytics и имеют отдельный retention/access policy.
- Correlation/trace identifiers допустимы; user/object identifiers используются только в защищённом audit, а не в usage analytics.

## 2. Product и diagnostic events

| AN | Module | Kind | Event | Trigger | Properties | Purpose | Privacy |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AN-001 | MOD-001 | Product/usage | auth.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-002 | MOD-001 | Diagnostic | auth.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-003 | MOD-002 | Product/usage | shell.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-004 | MOD-002 | Diagnostic | shell.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-005 | MOD-003 | Product/usage | today.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-006 | MOD-003 | Diagnostic | today.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-007 | MOD-004 | Product/usage | inbox.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-008 | MOD-004 | Diagnostic | inbox.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-009 | MOD-005 | Product/usage | tasks.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-010 | MOD-005 | Diagnostic | tasks.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-011 | MOD-006 | Product/usage | subtasks.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-012 | MOD-006 | Diagnostic | subtasks.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-013 | MOD-007 | Product/usage | recurrence.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-014 | MOD-007 | Diagnostic | recurrence.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-015 | MOD-008 | Product/usage | reminders.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-016 | MOD-008 | Diagnostic | reminders.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-017 | MOD-009 | Product/usage | calendar.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-018 | MOD-009 | Diagnostic | calendar.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-019 | MOD-010 | Product/usage | projects.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-020 | MOD-010 | Diagnostic | projects.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-021 | MOD-011 | Product/usage | files.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-022 | MOD-011 | Diagnostic | files.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-023 | MOD-012 | Product/usage | crm.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-024 | MOD-012 | Diagnostic | crm.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-025 | MOD-013 | Product/usage | collaboration.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-026 | MOD-013 | Diagnostic | collaboration.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-027 | MOD-014 | Product/usage | search.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-028 | MOD-014 | Diagnostic | search.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-029 | MOD-015 | Product/usage | notifications.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-030 | MOD-015 | Diagnostic | notifications.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-031 | MOD-016 | Product/usage | archive.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-032 | MOD-016 | Diagnostic | archive.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-033 | MOD-017 | Product/usage | trash.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-034 | MOD-017 | Diagnostic | trash.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-035 | MOD-018 | Product/usage | settings.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-036 | MOD-018 | Diagnostic | settings.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-037 | MOD-019 | Product/usage | admin.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-038 | MOD-019 | Diagnostic | admin.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-039 | MOD-020 | Product/usage | sync.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-040 | MOD-020 | Diagnostic | sync.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |
| AN-041 | MOD-021 | Product/usage | audit.surface_opened | Открыта основная поверхность модуля | moduleId, surfaceId, entryPoint, connectionMode | Проверить фактическое использование MVP surface | Без title, description, path, contact data и search query |
| AN-042 | MOD-021 | Diagnostic | audit.command_outcome | Завершена server command или recovery flow | moduleId, operationId, outcome, stableErrorCode, durationBucket, retryKind | Диагностика ошибок и recoverability | Без payload, credentials, raw path, free text и object content |

## 3. Audit requirements

| AUDIT | Module | Requirement | Source |
| --- | --- | --- | --- |
| AUDIT-001 | MOD-001 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-002 | MOD-002 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-003 | MOD-003 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-004 | MOD-004 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-005 | MOD-005 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-006 | MOD-006 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-007 | MOD-007 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-008 | MOD-008 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-009 | MOD-009 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-010 | MOD-010 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-011 | MOD-011 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-012 | MOD-012 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-013 | MOD-013 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-014 | MOD-014 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-015 | MOD-015 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-016 | MOD-016 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-017 | MOD-017 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-018 | MOD-018 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-019 | MOD-019 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-020 | MOD-020 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |
| AUDIT-021 | MOD-021 | Успешные изменяющие и чувствительные отказанные команды фиксируют actor, object, outcome, timestamp, correlationId и redacted diff; secrets и полный sensitive path не записываются. | Architecture audit principles; OpenAPI side effects |

## 4. Разделение событий

Product usage отвечает на вопрос, какие surface/actions используются. Diagnostic event отвечает, где операция завершилась ошибкой/медленно/повторно. Audit event доказывает, кто и с каким outcome выполнил sensitive business/security action. Одно событие не заменяет другое.

## 5. Privacy, storage, access и retention

Решение по `OQ-010` для MVP:

| Контур | Storage boundary | Доступ | Retention и rotation | Удаление |
| --- | --- | --- | --- | --- |
| Product usage (`Kind=Product/usage` и `Kind=Product`) | Только структурированные журналы application server внутри локальной сети организации; передача во внешнюю analytics platform, запись на desktop-клиент и отдельное долговременное analytics-хранилище запрещены. | Только Operations для диагностики сервиса и Security для расследования; пользовательский UI и предметные роли доступа не получают. Доступ оператора журналируется средствами ОС/инфраструктуры. | Deployment выбирает и документирует утверждённый компанией срок `30…90` календарных дней; product default отсутствует. Rotation должна быть настроена по времени и/или размеру в соответствии с OS/deployment policy. | Retention-задача удаляет поколения старше утверждённого срока; legal hold для product usage не поддерживается. |
| Operational diagnostics (`Kind=Diagnostic` и `Kind=Security diagnostic`) | Те же server-side structured logs; локальный desktop diagnostic buffer допустим только до отправки/закрытия процесса и не переживает logout. | Operations; Security — только для security diagnostic. Ни разработчик, ни Product не получают прямого production-доступа. | Тот же company-approved deployment parameter `30…90` дней и та же time/size rotation policy. | Автоматическое bounded deletion после утверждённого срока; logout/revoke очищает desktop buffer. |
| Security/business audit (`AUDIT-001…AUDIT-021` и `notification_urgency_scale.changed`) | Только `governance.audit_entries`/нормативный audit-контур PostgreSQL, отдельно от product/diagnostic logs. | Permission-controlled audit API/UI, server-side authorization и административный access audit; product analytics не читает audit. | 1095 дней, monthly partitioning, batch 10 000, legal hold поддерживается. | Retention worker удаляет истёкшие partitions/rows только при отсутствии legal hold. |

Основание для application logs: Stage 1 architecture §6.10 задаёт диапазон 30–90 дней и требует утверждения компанией. Основание для audit-контура: Stage 2.3.1 `db/004_stage_2_1_foundation.sql`, строки 1307–1327 (`governance.audit_entries=1095`, `monthly`, batch `10000`, legal hold); для log rotation — Stage 2.3.1 `catalogs/background_jobs.csv`, job `logs.rotate`; для bounded retention — job `operational.retention`. Выбранное deployment-значение в диапазоне 30–90 не является product SLA, API/DTO field или общим фиксированным default.

Для всех трёх контуров запрещены: полный поисковый запрос, title/description/comment body, contact data, employee identity, raw/UNC file path, credentials, secrets, токены, request/response payload, notification content, ETag и пользовательский draft. Allowlist полей в разделах 2 и 6 является закрытым: неуказанное поле не записывается.


## 6. Stage 4.5 targeted events

| ID | Kind | Event | Trigger | Allowlisted properties | Privacy |
| --- | --- | --- | --- | --- | --- |
| AN-043 | Product | `urgency_scale.opened` | CMP-001 opened | moduleId,surfaceId,connectionMode | Без interval values/PII |
| AN-044 | Product | `urgency_scale.updated` | PUT success | operationId,outcome | Без boundaries/displayToken/userId |
| AN-045 | Diagnostic | `urgency_scale.validation_failed` | VALIDATION_FAILED | operationId,stableErrorCode,fieldPathClass | Без field values |
| AN-046 | Product | `urgency_scale.reset` | Reset outcome | operationId,outcome | Без scale payload |
| AN-047 | Diagnostic | `urgency_scale.conflict` | VERSION_CONFLICT | operationId,recoveryKind | Без ETag/draft |
| AN-048 | Security diagnostic | `urgency_scale.permission_denied` | FORBIDDEN | operationId,outcome | Без actor PII; audit отдельно |
| AN-049 | Product | `search.executed` | Search with employee type | typesContainsEmployee,resultMode,outcome | Полный q и PII запрещены |
| AN-050 | Product | `search.employee_selected` | Employee result selected | surfaceId,resultType | Без userId/displayName/deepLink |
| AN-051 | Product | `search.employee_empty` | Employee group empty | resultMode,filterClass | Без q/departments |
| AN-052 | Diagnostic | `search.group_partial_failure` | One result group failed | groupType,stableErrorCode,retryKind | Без results/query/PII |

## 7. Targeted audit requirements

- PUT/reset urgency scale создаёт `notification_urgency_scale.changed` только после commit; actor, organization, outcome, timestamp, correlationId и redacted diff разрешены.
- Permission denial для sensitive write фиксируется security audit по существующей политике; product event не заменяет audit.
- Employee search read не пишет query, employee identity, department, title, status или deepLink в product/diagnostic telemetry.
- Full query, PII, file paths, secrets, ETag/draft, interval values, displayToken и notification content запрещены во всех AN-043…AN-052.

## 8. Проверяемые governance gates

1. Конфигурационный тест подтверждает server-only storage boundary и отсутствие внешнего analytics exporter.
2. Schema/allowlist test отклоняет любое свойство, отсутствующее в колонке `Properties`/`Allowlisted properties`.
3. Privacy test передаёт search query, PII, raw path, secret, payload и notification content и подтверждает их отсутствие во всех трёх контурах.
4. Access test подтверждает разделение Operations/Security/audit permission и отсутствие product-role доступа.
5. Deployment configuration test подтверждает company approval, значение application-log retention в диапазоне 30–90 дней и настроенную time/size rotation.
6. Retention test подтверждает удаление product/diagnostic generations старше фактически утверждённого deployment-срока и отсутствие единого скрытого product default.
7. Audit retention test подтверждает 1095 дней, monthly partitioning, batch 10 000 и блокировку удаления при legal hold.
8. Logout/revoke test подтверждает очистку временного desktop diagnostic buffer.

`OQ-010` может быть перенесён в resolved history только при совпадении формулировки Open Questions с этой политикой. Фактическая production-конфигурация и execution evidence остаются implementation/re-audit gates; настоящий документ не объявляет независимый реаудит выполненным.
