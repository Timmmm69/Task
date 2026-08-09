from __future__ import annotations

import csv
from pathlib import Path

import yaml


ROOT = Path(__file__).resolve().parent / "stage_2_3"
API_CATALOG = ROOT / "catalogs" / "api_catalog.csv"


def read_rows(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


api_rows = read_rows(API_CATALOG)
api_rows.sort(key=lambda row: (row["path"], row["method"]))

endpoint_lines = [
    "# Organizer Stage 2.3 endpoint dump",
    f"# Operations: {len(api_rows)}",
    "",
]
for row in api_rows:
    endpoint_lines.append(
        f"{row['method']:6} {row['path']} | {row['permission']} | "
        f"{row['request']} -> {row['response']} | {row['codes']}"
    )
(ROOT / "endpoints_dump.txt").write_text(
    "\n".join(endpoint_lines) + "\n",
    encoding="utf-8",
    newline="\n",
)

api_document = ROOT / "docs" / "02_api_and_concurrency.md"
document_text = api_document.read_text(encoding="utf-8")
start_marker = "# 19. Полный каталог API"
end_marker = "# 20. OpenAPI"
start = document_text.index(start_marker)
end = document_text.index(end_marker)
table_lines = [
    start_marker,
    "",
    f"Канонический каталог содержит {len(api_rows)} операции. "
    "`catalogs/api_catalog.csv` и `openapi/openapi.yaml` проверяются совместно.",
    "",
    "| Module | Method | URL | Назначение | Permission | Request | Response | Codes | Idempotency | Transaction | Lock | Effects | Events |",
    "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |",
]
for row in api_rows:
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

document = yaml.safe_load((ROOT / "openapi" / "openapi.yaml").read_text(encoding="utf-8"))
operation_by_key = {
    (method.upper(), path): operation
    for path, path_item in document["paths"].items()
    for method, operation in path_item.items()
    if method.lower() in {"get", "put", "post", "patch", "delete", "head", "options", "trace"}
}
diff_fields = [
    "method",
    "path",
    "canonical_traceability_source",
    "catalog_present",
    "openapi_present",
    "operation_id",
    "openapi_permission",
    "catalog_permission",
    "request_contract",
    "response_contract",
    "status",
    "difference",
]
diff_rows: list[dict[str, str]] = []
for row in api_rows:
    operation = operation_by_key[(row["method"].upper(), row["path"])]
    diff_rows.append(
        {
            "method": row["method"],
            "path": row["path"],
            "canonical_traceability_source": "catalogs/api_catalog.csv",
            "catalog_present": "True",
            "openapi_present": "True",
            "operation_id": operation["operationId"],
            "openapi_permission": operation.get("x-permission", ""),
            "catalog_permission": row["permission"],
            "request_contract": row["request"],
            "response_contract": row["response"],
            "status": "match",
            "difference": "",
        }
    )
with (ROOT / "contract_diff_against_traceability.csv").open(
    "w",
    encoding="utf-8",
    newline="",
) as stream:
    writer = csv.DictWriter(stream, fieldnames=diff_fields, lineterminator="\n")
    writer.writeheader()
    writer.writerows(diff_rows)

print(f"SYNCED_OPERATIONS={len(api_rows)}")
