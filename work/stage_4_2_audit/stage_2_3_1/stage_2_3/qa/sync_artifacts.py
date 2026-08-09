from __future__ import annotations

import csv
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CATALOGS = ROOT / "catalogs"
DOCS = ROOT / "docs"


def read_csv(path: Path) -> tuple[list[str], list[dict[str, str]]]:
    with path.open(encoding="utf-8-sig", newline="") as source:
        reader = csv.DictReader(source)
        return list(reader.fieldnames or []), list(reader)


def write_csv(path: Path, fieldnames: list[str], rows: list[dict[str, str]]) -> None:
    with path.open("w", encoding="utf-8", newline="") as target:
        writer = csv.DictWriter(target, fieldnames=fieldnames, lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


api_fields, api_rows = read_csv(CATALOGS / "api_catalog.csv")

event_path = CATALOGS / "events.csv"
event_fields, event_rows = read_csv(event_path)
events_by_name = {row["event"]: row for row in event_rows}
for api_row in api_rows:
    if api_row["events"] == "—":
        continue
    for event_name in (name.strip() for name in api_row["events"].split(",")):
        events_by_name.setdefault(
            event_name,
            {
                "event": event_name,
                "source": api_row["module"],
                "payload": (
                    "eventId, organizationId, aggregateId, aggregateType, "
                    "aggregateVersion, occurredAt, actorId, correlationId, "
                    "changedFields/minimal metadata"
                ),
                "publication": "business transaction writes domain event and outbox atomically",
                "delivery": "at-least-once",
                "replay": "yes; source event ID is the consumer deduplication key",
                "retention": "90 days domain / 30 days delivered outbox",
                "consumers": (
                    "idempotent change-feed projector; audit/history; "
                    "notification/search/realtime as applicable"
                ),
            },
        )
write_csv(event_path, event_fields, sorted(events_by_name.values(), key=lambda row: row["event"]))

job_path = CATALOGS / "background_jobs.csv"
job_fields, job_rows = read_csv(job_path)
jobs_by_code = {row["job"]: row for row in job_rows}
for job in [
    {
        "job": "idempotency.cleanup",
        "schedule_trigger": "каждый час",
        "lock": "singleton lease token",
        "idempotency": "delete only expires_at below database clock",
        "retry_dlq": "3 retries; alert on persistent failure",
        "metrics": "expired rows/deletion lag",
    },
    {
        "job": "sync.snapshot-cleanup",
        "schedule_trigger": "каждые 15 минут",
        "lock": "singleton lease token",
        "idempotency": "snapshot status + expires_at",
        "retry_dlq": "3 retries; no destructive DLQ",
        "metrics": "active sessions/expired rows/oldest session",
    },
    {
        "job": "operational.retention",
        "schedule_trigger": "ежедневно 04:00",
        "lock": "singleton lease token",
        "idempotency": "bounded primary-key batches below retention cut",
        "retry_dlq": "3 retries; quarantine relation on policy violation",
        "metrics": "rows deleted/batch latency/retention lag",
    },
]:
    jobs_by_code[job["job"]] = job
write_csv(job_path, job_fields, sorted(jobs_by_code.values(), key=lambda row: row["job"]))

entity_path = CATALOGS / "entities.csv"
entity_fields, entity_rows = read_csv(entity_path)
entities_by_name = {row["technical_name"]: row for row in entity_rows}
new_entities = [
    {
        "ru_name": "Запись идемпотентности",
        "technical_name": "IdempotencyRecord",
        "purpose": "Durable replay результата mutating-команды",
        "owner_boundary": "Identity",
        "required": "organization_id,user_id,operation_id,key,request_hash,state,expires_at",
        "optional": "response_status,response_headers,response_body,resource_id,lease",
        "relations": "Organization,User",
        "invariants_lifecycle": "Unique scope; different request hash is conflict; completed response immutable",
        "history_lock": "Retention audit; row lock/lease",
        "sensitivity_volume": "Sensitive request metadata; high volume",
    },
    {
        "ru_name": "Шаблон повторяющейся задачи",
        "technical_name": "RecurrenceTaskTemplate",
        "purpose": "Полный воспроизводимый шаблон generated Task",
        "owner_boundary": "Work Management",
        "required": "series_id,title,author,priority,template_version",
        "optional": "project,description,requester,counterparty,duration,deadline_offset",
        "relations": "RecurrenceSeries,assignees,watchers,checklists,reminder rules",
        "invariants_lifecycle": "Required one-to-one with series; copied atomically into occurrence task",
        "history_lock": "Series history; If-Match series",
        "sensitivity_volume": "Internal; ≤series count",
    },
    {
        "ru_name": "Сессия sync snapshot",
        "technical_name": "SnapshotSession",
        "purpose": "Фиксированный snapshot cut и стабильное paging-состояние bootstrap",
        "owner_boundary": "Sync",
        "required": "organization,user,device,cut_sequence,scope_version,status,expires_at",
        "optional": "manifest,ready_at,completed_at",
        "relations": "SnapshotItems,ClientSyncState",
        "invariants_lifecycle": "Immutable cut/scope; expires; catch-up starts strictly after cut",
        "history_lock": "Technical audit; lease/status CAS",
        "sensitivity_volume": "Sensitive projection metadata; bounded",
    },
    {
        "ru_name": "Элемент sync snapshot",
        "technical_name": "SnapshotItem",
        "purpose": "Стабильная страница авторизованного bootstrap dataset",
        "owner_boundary": "Sync",
        "required": "session,dataset,ordinal,object_id,type,version,payload",
        "optional": "—",
        "relations": "SnapshotSession",
        "invariants_lifecycle": "Unique dataset ordinal and object; immutable until session expiry",
        "history_lock": "No history; session-owned",
        "sensitivity_volume": "Sensitive authorized payload; high but short-lived",
    },
    {
        "ru_name": "Доступность пути на устройстве",
        "technical_name": "FileLocationDeviceState",
        "purpose": "Per-device результат проверки физического пути",
        "owner_boundary": "File Catalog",
        "required": "location,device,user,status,version",
        "optional": "checked_at,latency,last_check",
        "relations": "FileLocation,Device,User",
        "invariants_lifecycle": "One current state per location/device; tenant and ownership guard",
        "history_lock": "Telemetry retention; version CAS",
        "sensitivity_volume": "Sensitive device metadata; high volume",
    },
    {
        "ru_name": "Ключ версии истории",
        "technical_name": "ObjectHistoryVersionKey",
        "purpose": "Глобальная уникальность object/version поверх partitions",
        "owner_boundary": "Governance",
        "required": "organization,object_id,object_version,history_id,changed_at",
        "optional": "—",
        "relations": "ObjectHistory",
        "invariants_lifecycle": "Insert-only unique key",
        "history_lock": "Append-only",
        "sensitivity_volume": "Internal; very high",
    },
    {
        "ru_name": "Tombstone очищенного объекта",
        "technical_name": "ObjectTombstone",
        "purpose": "Сохранение идентичности и версии после purge/redaction",
        "owner_boundary": "Governance",
        "required": "organization,object_id,type,last_version,purged_at,correlation_id",
        "optional": "legal_hold_released_at",
        "relations": "Organization",
        "invariants_lifecycle": "Never reused; no PII payload",
        "history_lock": "Append-only",
        "sensitivity_volume": "Internal metadata; high",
    },
    {
        "ru_name": "Маска редактирования истории",
        "technical_name": "HistoryRedaction",
        "purpose": "Append-only указание скрываемых PII paths",
        "owner_boundary": "Governance",
        "required": "organization,object_id,paths,reason,correlation_id",
        "optional": "requested_by",
        "relations": "Organization,User",
        "invariants_lifecycle": "Append-only; read projection applies latest masks",
        "history_lock": "Append-only",
        "sensitivity_volume": "Highly sensitive; low",
    },
]
for entity_row in new_entities:
    entities_by_name[entity_row["technical_name"]] = entity_row
write_csv(
    entity_path,
    entity_fields,
    sorted(entities_by_name.values(), key=lambda row: row["technical_name"]),
)

endpoint_lines = [
    "# Organizer Stage 2.1 endpoint dump",
    f"# Operations: {len(api_rows)}",
    "",
]
for row in sorted(api_rows, key=lambda item: (item["path"], item["method"])):
    endpoint_lines.append(
        f"{row['method']:6} {row['path']} | {row['permission']} | "
        f"{row['request']} -> {row['response']} | {row['codes']}"
    )
(ROOT / "endpoints_dump.txt").write_text("\n".join(endpoint_lines) + "\n", encoding="utf-8")

api_document = DOCS / "02_api_and_concurrency.md"
document_text = api_document.read_text(encoding="utf-8")
start_marker = "# 19. Полный каталог API"
end_marker = "# 20. OpenAPI"
start = document_text.index(start_marker)
end = document_text.index(end_marker)
table_lines = [
    start_marker,
    "",
    f"Канонический каталог содержит {len(api_rows)} операций. "
    "`catalogs/api_catalog.csv` и `openapi/openapi.yaml` генерируются и проверяются совместно.",
    "",
    "| Module | Method | URL | Назначение | Permission | Request | Response | Codes | Idempotency | Transaction | Lock | Effects | Events |",
    "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |",
]
for row in sorted(api_rows, key=lambda item: (item["path"], item["method"])):
    values = [
        row["module"],
        row["method"],
        row["path"],
        row["purpose"],
        row["permission"],
        row["request"],
        row["response"],
        row["codes"],
        row["idempotency"],
        row["transaction"],
        row["locking"],
        row["effects"],
        row["events"],
    ]
    table_lines.append("| " + " | ".join(value.replace("|", "\\|") for value in values) + " |")
table_lines.extend(["", ""])
api_document.write_text(
    document_text[:start] + "\n".join(table_lines) + document_text[end:],
    encoding="utf-8",
    newline="\n",
)
