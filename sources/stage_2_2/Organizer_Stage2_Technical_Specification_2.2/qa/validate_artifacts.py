from __future__ import annotations

import csv
import hashlib
import json
from pathlib import Path
from typing import Any

import yaml
from openapi_spec_validator import validate_spec


ROOT = Path(__file__).resolve().parents[1]
CATALOGS = ROOT / "catalogs"
OPENAPI_PATH = ROOT / "openapi" / "openapi.yaml"
REPORT_PATH = ROOT / "qa" / "validation_report.json"


def read_csv(name: str) -> list[dict[str, str]]:
    with (CATALOGS / name).open(encoding="utf-8-sig", newline="") as source:
        return list(csv.DictReader(source))


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


document = yaml.safe_load(OPENAPI_PATH.read_text(encoding="utf-8"))
api_rows = read_csv("api_catalog.csv")
permissions = read_csv("permissions.csv")
events = read_csv("events.csv")
jobs = read_csv("background_jobs.csv")
permission_codes = {row["code"] for row in permissions}
event_names = {row["event"] for row in events}
job_codes = {row["job"] for row in jobs}
checks: list[dict[str, Any]] = []


def check(name: str, condition: bool, detail: Any) -> None:
    checks.append(
        {
            "name": name,
            "status": "pass" if condition else "fail",
            "detail": detail,
        }
    )


validate_spec(document)
check("openapi_spec_validator", True, "OpenAPI 3.1 schema validation passed")
check(
    "openapi_31_version",
    document.get("openapi") == "3.1.0",
    document.get("openapi"),
)


def find_legacy_nullable(value: Any, path: str = "$") -> list[str]:
    defects: list[str] = []
    if isinstance(value, dict):
        if "nullable" in value:
            defects.append(path)
        for key, child in value.items():
            defects.extend(find_legacy_nullable(child, f"{path}.{key}"))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            defects.extend(find_legacy_nullable(child, f"{path}[{index}]"))
    return defects


legacy_nullable = find_legacy_nullable(document)
check(
    "no_openapi_30_nullable_keywords",
    not legacy_nullable,
    legacy_nullable,
)

catalog_operations = {(row["method"].lower(), row["path"]) for row in api_rows}
openapi_operations: set[tuple[str, str]] = set()
operation_values: list[dict[str, Any]] = []
for path, path_item in document["paths"].items():
    for method, operation in path_item.items():
        if method.lower() not in {"get", "post", "put", "patch", "delete"}:
            continue
        openapi_operations.add((method.lower(), path))
        operation_values.append(operation)

check(
    "api_catalog_openapi_parity",
    catalog_operations == openapi_operations,
    {
        "catalog": len(catalog_operations),
        "openapi": len(openapi_operations),
        "catalogOnly": sorted(catalog_operations - openapi_operations),
        "openapiOnly": sorted(openapi_operations - catalog_operations),
    },
)
check("api_operation_count", len(openapi_operations) == 241, len(openapi_operations))

schemas = document["components"]["schemas"]
schema_references: set[str] = set()


def visit(value: Any, path: str = "$") -> list[str]:
    defects: list[str] = []
    if isinstance(value, dict):
        reference = value.get("$ref")
        if isinstance(reference, str) and reference.startswith("#/components/schemas/"):
            schema_references.add(reference.rsplit("/", 1)[1])
        if value.get("additionalProperties") is True:
            defects.append(f"{path}: additionalProperties=true")
        if value.get("type") == "object":
            properties = value.get("properties")
            bounded_map = isinstance(value.get("additionalProperties"), dict)
            if (properties is None or properties == {}) and not bounded_map:
                defects.append(f"{path}: empty object schema")
        for key, child in value.items():
            defects.extend(visit(child, f"{path}.{key}"))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            defects.extend(visit(child, f"{path}[{index}]"))
    return defects


schema_defects = visit(document)
check("no_empty_or_unbounded_object_schemas", not schema_defects, schema_defects)
check(
    "all_schema_references_resolve",
    schema_references <= schemas.keys(),
    sorted(schema_references - schemas.keys()),
)
check("generic_object_removed", "GenericObject" not in schemas, sorted(schemas))


def resolved_parameters(operation: dict[str, Any]) -> list[dict[str, Any]]:
    result = []
    for parameter in operation.get("parameters", []):
        reference = parameter.get("$ref")
        if reference and reference.startswith("#/components/parameters/"):
            result.append(
                document["components"]["parameters"][reference.rsplit("/", 1)[1]]
            )
        else:
            result.append(parameter)
    return result


idempotency_defects: list[str] = []
locking_defects: list[str] = []
used_permissions: set[str] = set()
for (method, path), row in zip(
    sorted(openapi_operations),
    sorted(api_rows, key=lambda item: (item["method"].lower(), item["path"])),
):
    operation = document["paths"][path][method]
    parameters = resolved_parameters(operation)
    idempotency_parameters = [
        parameter
        for parameter in parameters
        if parameter.get("in") == "header"
        and parameter.get("name") == "Idempotency-Key"
    ]
    declared_idempotency = row["idempotency"].startswith("Idempotency-Key")
    required_idempotency = (
        declared_idempotency and "optional" not in row["idempotency"].lower()
    )
    if declared_idempotency and len(idempotency_parameters) != 1:
        idempotency_defects.append(f"{method.upper()} {path}: header missing/duplicated")
    if required_idempotency and idempotency_parameters and not idempotency_parameters[0]["required"]:
        idempotency_defects.append(f"{method.upper()} {path}: header must be required")
    if (
        declared_idempotency
        and not required_idempotency
        and idempotency_parameters
        and idempotency_parameters[0]["required"]
    ):
        idempotency_defects.append(f"{method.upper()} {path}: optional header is required")

    required_if_match = (
        "If-Match" in row["locking"]
        and "optional" not in row["locking"]
        and "Per-item" not in row["locking"]
    )
    if_match_parameters = [
        parameter
        for parameter in parameters
        if parameter.get("in") == "header" and parameter.get("name") == "If-Match"
    ]
    if required_if_match:
        if len(if_match_parameters) != 1 or not if_match_parameters[0]["required"]:
            locking_defects.append(f"{method.upper()} {path}: required If-Match absent")
        response_codes = set(operation["responses"])
        if not {"409", "412", "428"} <= response_codes:
            locking_defects.append(
                f"{method.upper()} {path}: missing 409/412/428 responses"
            )
        if "x-if-match-target" not in operation:
            locking_defects.append(f"{method.upper()} {path}: lock target absent")

    permission = operation.get("x-permission")
    access_policy = operation.get("x-access-policy")
    if permission in permission_codes:
        used_permissions.add(permission)
    elif permission is not None:
        locking_defects.append(f"{method.upper()} {path}: unknown permission {permission}")
    elif access_policy not in {
        "Authenticated",
        "Anonymous",
        "Anonymous.SessionRefresh",
        "Anonymous/Network allowlist",
    }:
        locking_defects.append(f"{method.upper()} {path}: access policy missing")
    sensitive_permission = operation.get("x-sensitive-field-permission")
    if sensitive_permission:
        used_permissions.add(sensitive_permission)

check("idempotency_headers", not idempotency_defects, idempotency_defects)
check("optimistic_lock_headers_and_codes", not locking_defects, locking_defects)
check(
    "all_permissions_used",
    used_permissions == permission_codes,
    {
        "used": len(used_permissions),
        "canonical": len(permission_codes),
        "unused": sorted(permission_codes - used_permissions),
        "unknown": sorted(used_permissions - permission_codes),
    },
)

refresh = document["paths"]["/api/v1/auth/refresh"]["post"]
check("refresh_without_bearer", refresh.get("security") == [], refresh.get("security"))
project_statuses = schemas["Project"]["properties"]["status"]["enum"]
check(
    "project_status_canonical",
    project_statuses == ["planning", "active", "paused", "completed"],
    project_statuses,
)
check(
    "multi_version_contracts",
    all(
        property_name in schemas[schema_name]["properties"]
        for schema_name, property_name in [
            ("FileLocationPatch", "expectedCatalogItemVersion"),
            ("ProjectMemberPatch", "expectedProjectVersion"),
            ("RecurrenceScopedChange", "expectedTaskVersion"),
            ("TransferOwnershipRequest", "expectedNewOwnerMembershipVersion"),
        ]
    ),
    "all secondary versions are explicit DTO fields",
)

lifecycle_paths = document["paths"]
check(
    "lifecycle_endpoints",
    all(
        path in lifecycle_paths
        for path in [
            "/api/v1/roles/{id}/activate",
            "/api/v1/recurrence-series/{id}/resume",
            "/api/v1/reminders/{id}/reschedule",
        ]
    )
    and all(
        path not in lifecycle_paths
        for path in [
            "/api/v1/roles/{id}/restore",
            "/api/v1/recurrence-series/{id}/restore",
            "/api/v1/reminders/{id}/restore",
        ]
    ),
    "status-owned resources no longer use universal restore",
)
check("today_endpoint", "/api/v1/today" in lifecycle_paths, "/api/v1/today")
check(
    "recurrence_template_contract",
    "template" in schemas["RecurrenceSeriesCreate"]["properties"]
    and "template" in schemas["RecurrenceSeriesCreate"]["required"],
    schemas["RecurrenceSeriesCreate"],
)
check(
    "sync_snapshot_contract",
    schemas["SyncBatch"].get("discriminator", {}).get("propertyName") == "mode"
    and {"SnapshotPage", "IncrementalSyncBatch"} <= schemas.keys(),
    "snapshot/incremental discriminated contract",
)

declared_events = {
    event
    for operation in operation_values
    for event in operation.get("x-domain-events", [])
}
check(
    "event_catalog_coverage",
    declared_events <= event_names,
    sorted(declared_events - event_names),
)
check(
    "required_background_jobs",
    {
        "idempotency.cleanup",
        "sync.snapshot-cleanup",
        "operational.retention",
        "outbox.publish",
        "reminders.materialize",
    }
    <= job_codes,
    sorted(job_codes),
)

ddl_text = "\n".join(
    path.read_text(encoding="utf-8")
    for path in sorted((ROOT / "db").glob("*.sql"))
)
ddl_requirements = [
    "iam.idempotency_records",
    "work.recurrence_task_templates",
    "sync.snapshot_sessions",
    "source_event_id",
    "lock_token",
    "organizer_runtime",
    "governance.object_history_version_keys",
    "files.file_location_device_states",
    "calendar.today_read_model",
]
check(
    "ddl_stage_2_1_objects",
    all(requirement in ddl_text for requirement in ddl_requirements),
    ddl_requirements,
)
check(
    "archive_ddl_syntax_regression",
    "restored_at timestamptz,\n);" not in ddl_text,
    "no trailing comma before archive_entries close",
)

sources = {
    "concept": ROOT / "sources" / "product_concept.txt",
    "architecture": ROOT / "sources" / "architecture_stage1.md",
    "stage_2_1_acceptance": ROOT / "sources" / "stage_2_1_acceptance_criteria.txt",
}
source_hashes = {name: sha256(path) for name, path in sources.items()}
check(
    "source_files_packaged",
    all(path.is_file() for path in sources.values()),
    source_hashes,
)

failed = [entry for entry in checks if entry["status"] == "fail"]
report = {
    "stage": "2.1",
    "status": "pass" if not failed else "fail",
    "source_hashes": source_hashes,
    "metrics": {
        "api_operations": len(openapi_operations),
        "openapi_schemas": len(schemas),
        "permissions": len(permission_codes),
        "events": len(event_names),
        "background_jobs": len(job_codes),
    },
    "checks": checks,
}
REPORT_PATH.write_text(
    json.dumps(report, ensure_ascii=False, indent=2) + "\n",
    encoding="utf-8",
    newline="\n",
)

if failed:
    for failure in failed:
        print(f"FAIL {failure['name']}: {failure['detail']}")
    raise SystemExit(1)

print(
    "ARTIFACT_VALIDATION_PASSED "
    f"operations={len(openapi_operations)} schemas={len(schemas)} "
    f"permissions={len(permission_codes)} events={len(event_names)} jobs={len(job_codes)}"
)
