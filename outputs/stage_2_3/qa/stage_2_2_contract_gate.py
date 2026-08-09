from __future__ import annotations

import csv
import hashlib
import json
from pathlib import Path
from typing import Any, Iterable

import yaml
from openapi_spec_validator import validate_spec


ROOT = Path(__file__).resolve().parents[1]
OPENAPI_PATH = ROOT / "openapi" / "openapi.yaml"
API_CATALOG_PATH = ROOT / "catalogs" / "api_catalog.csv"
PERMISSIONS_PATH = ROOT / "catalogs" / "permissions.csv"
ERRORS_PATH = ROOT / "catalogs" / "errors.csv"
DTO_CATALOG_PATH = ROOT / "dto_field_catalog.csv"
DIFF_PATH = ROOT / "contract_diff_against_traceability.csv"
SUMMARY_PATH = ROOT / "qa" / "stage_2_2_validation.json"
HTTP_METHODS = {"get", "post", "put", "patch", "delete", "head", "options", "trace"}


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8-sig", newline="") as source:
        return list(csv.DictReader(source))


def resolve_ref(document: dict[str, Any], reference: str) -> Any:
    if not reference.startswith("#/"):
        raise ValueError(f"External reference is forbidden: {reference}")
    current: Any = document
    for token in reference[2:].split("/"):
        token = token.replace("~1", "/").replace("~0", "~")
        current = current[token]
    return current


def iter_references(value: Any) -> Iterable[str]:
    if isinstance(value, dict):
        reference = value.get("$ref")
        if isinstance(reference, str):
            yield reference
        for child in value.values():
            yield from iter_references(child)
    elif isinstance(value, list):
        for child in value:
            yield from iter_references(child)


def operations(document: dict[str, Any]) -> dict[tuple[str, str], dict[str, Any]]:
    result: dict[tuple[str, str], dict[str, Any]] = {}
    for path, path_item in document["paths"].items():
        for method, operation in path_item.items():
            if method in HTTP_METHODS:
                key = (method.upper(), path)
                if key in result:
                    raise ValueError(f"Duplicate operation: {key}")
                result[key] = operation
    return result


def resolved_parameters(document: dict[str, Any], operation: dict[str, Any]) -> list[dict[str, Any]]:
    result = []
    for parameter in operation.get("parameters", []):
        if "$ref" in parameter:
            parameter = resolve_ref(document, parameter["$ref"])
        result.append(parameter)
    return result


def concrete_schema(document: dict[str, Any], schema: dict[str, Any]) -> bool:
    if "$ref" in schema:
        return concrete_schema(document, resolve_ref(document, schema["$ref"]))
    if "oneOf" in schema or "anyOf" in schema or "allOf" in schema:
        branches = schema.get("oneOf") or schema.get("anyOf") or schema.get("allOf")
        return bool(branches) and all(concrete_schema(document, branch) for branch in branches)
    schema_type = schema.get("type")
    if schema_type == "object" or isinstance(schema.get("properties"), dict):
        return bool(schema.get("properties")) or isinstance(schema.get("additionalProperties"), dict)
    if schema_type == "array":
        return isinstance(schema.get("items"), dict) and concrete_schema(document, schema["items"])
    return schema_type is not None or "enum" in schema


def find_unbounded_objects(value: Any, path: str = "$") -> list[str]:
    failures: list[str] = []
    if isinstance(value, dict):
        if value.get("additionalProperties") is True:
            failures.append(path)
        for key, child in value.items():
            failures.extend(find_unbounded_objects(child, f"{path}.{key}"))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            failures.extend(find_unbounded_objects(child, f"{path}[{index}]"))
    return failures


def schema_nullable(schema: dict[str, Any]) -> bool:
    schema_type = schema.get("type")
    return (
        schema_type == "null"
        or isinstance(schema_type, list)
        and "null" in schema_type
        or None in schema.get("enum", [])
    )


def schema_type_name(schema: dict[str, Any]) -> str:
    if "$ref" in schema:
        return "ref"
    schema_type = schema.get("type", "")
    if isinstance(schema_type, list):
        return "|".join(str(item) for item in schema_type)
    if schema_type:
        return str(schema_type)
    for combinator in ("oneOf", "anyOf", "allOf"):
        if combinator in schema:
            return combinator
    return ""


def date_time_semantics(schema: dict[str, Any]) -> str:
    format_name = schema.get("format")
    if format_name == "date-time":
        return "RFC 3339 instant; UTC on the wire; preserve the instant when rendering in a user time zone"
    if format_name == "date":
        return "Calendar date without a time zone"
    if format_name == "time":
        return "Local wall-clock time; interpret only with the companion time-zone field"
    return ""


def validation_rules(schema: dict[str, Any]) -> str:
    keys = (
        "pattern",
        "minLength",
        "maxLength",
        "minimum",
        "maximum",
        "exclusiveMinimum",
        "exclusiveMaximum",
        "minItems",
        "maxItems",
        "uniqueItems",
        "minProperties",
        "maxProperties",
    )
    values = [f"{key}={json.dumps(schema[key], ensure_ascii=False)}" for key in keys if key in schema]
    return "; ".join(values)


def flatten_fields(
    schema_name: str,
    schema: dict[str, Any],
    parent_path: str = "",
    parent_required: set[str] | None = None,
) -> Iterable[dict[str, str]]:
    required = set(schema.get("required", []))
    properties = schema.get("properties", {})
    for field_name, field_schema in properties.items():
        field_path = f"{parent_path}.{field_name}" if parent_path else field_name
        reference = field_schema.get("$ref", "")
        enum_values = field_schema.get("enum", [])
        row = {
            "schema": schema_name,
            "field_path": field_path,
            "type": schema_type_name(field_schema),
            "format": str(field_schema.get("format", "")),
            "required": str(field_name in required),
            "nullable": str(schema_nullable(field_schema)),
            "enum": json.dumps(enum_values, ensure_ascii=False) if enum_values else "",
            "default": json.dumps(field_schema["default"], ensure_ascii=False)
            if "default" in field_schema
            else "",
            "minimum": str(field_schema.get("minimum", "")),
            "maximum": str(field_schema.get("maximum", "")),
            "min_length": str(field_schema.get("minLength", "")),
            "max_length": str(field_schema.get("maxLength", "")),
            "min_items": str(field_schema.get("minItems", "")),
            "max_items": str(field_schema.get("maxItems", "")),
            "unique_items": str(field_schema.get("uniqueItems", "")),
            "read_only": str(bool(field_schema.get("readOnly"))),
            "write_only": str(bool(field_schema.get("writeOnly"))),
            "referenced_schema": reference.rsplit("/", 1)[-1] if reference else "",
            "date_time_semantics": date_time_semantics(field_schema),
            "patch_semantics": (
                "omitted=unchanged; explicit null=clear when nullable"
                if schema_name.endswith("Patch")
                else ""
            ),
            "version_semantics": (
                "aggregate version or expected version"
                if field_name == "version" or field_name.lower().startswith("expected")
                and field_name.lower().endswith("version")
                else ""
            ),
            "lifecycle_semantics": (
                "canonical lifecycle/status literal"
                if field_name in {"status", "lifecycle", "archiveState", "trashState"}
                else ""
            ),
            "capability_semantics": (
                "server-derived capability/permission data"
                if "capabilit" in field_name.lower() or "permission" in field_name.lower()
                else ""
            ),
            "partial_access": str("x-redaction" in field_schema),
            "redaction": str(field_schema.get("x-redaction", "")),
            "description": str(field_schema.get("description", "")),
            "validation_rules": validation_rules(field_schema),
        }
        yield row
        if isinstance(field_schema.get("properties"), dict):
            yield from flatten_fields(schema_name, field_schema, field_path, required)
        items = field_schema.get("items")
        if isinstance(items, dict) and isinstance(items.get("properties"), dict):
            yield from flatten_fields(schema_name, items, f"{field_path}[]", set(items.get("required", [])))


def write_dto_catalog(document: dict[str, Any]) -> int:
    rows: list[dict[str, str]] = []
    for schema_name, schema in sorted(document["components"]["schemas"].items()):
        rows.extend(flatten_fields(schema_name, schema))
    fieldnames = [
        "schema",
        "field_path",
        "type",
        "format",
        "required",
        "nullable",
        "enum",
        "default",
        "minimum",
        "maximum",
        "min_length",
        "max_length",
        "min_items",
        "max_items",
        "unique_items",
        "read_only",
        "write_only",
        "referenced_schema",
        "date_time_semantics",
        "patch_semantics",
        "version_semantics",
        "lifecycle_semantics",
        "capability_semantics",
        "partial_access",
        "redaction",
        "description",
        "validation_rules",
    ]
    with DTO_CATALOG_PATH.open("w", encoding="utf-8", newline="") as target:
        writer = csv.DictWriter(target, fieldnames=fieldnames, lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)
    return len(rows)


def write_contract_diff(
    contract_operations: dict[tuple[str, str], dict[str, Any]],
    catalog_rows: list[dict[str, str]],
) -> int:
    catalog = {(row["method"], row["path"]): row for row in catalog_rows}
    keys = sorted(set(contract_operations) | set(catalog), key=lambda item: (item[1], item[0]))
    rows = []
    differences = 0
    for method, path in keys:
        operation = contract_operations.get((method, path))
        catalog_row = catalog.get((method, path))
        difference_items = []
        if operation is None:
            difference_items.append("missing_in_openapi")
        if catalog_row is None:
            difference_items.append("missing_in_api_catalog")
        operation_access = (
            operation.get("x-permission") or operation.get("x-access-policy")
            if operation
            else ""
        )
        if operation and catalog_row and operation_access != catalog_row["permission"]:
            difference_items.append("permission_mismatch")
        if difference_items:
            differences += 1
        rows.append(
            {
                "method": method,
                "path": path,
                "canonical_traceability_source": "catalogs/api_catalog.csv",
                "catalog_present": str(catalog_row is not None),
                "openapi_present": str(operation is not None),
                "operation_id": operation.get("operationId", "") if operation else "",
                "openapi_permission": operation_access,
                "catalog_permission": catalog_row["permission"] if catalog_row else "",
                "request_contract": catalog_row["request"] if catalog_row else "",
                "response_contract": catalog_row["response"] if catalog_row else "",
                "status": "match" if not difference_items else "difference",
                "difference": ";".join(difference_items),
            }
        )
    with DIFF_PATH.open("w", encoding="utf-8", newline="") as target:
        writer = csv.DictWriter(target, fieldnames=list(rows[0]), lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)
    return differences


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def main() -> None:
    document = yaml.safe_load(OPENAPI_PATH.read_text(encoding="utf-8"))
    validate_spec(document)
    contract_operations = operations(document)
    catalog_rows = read_csv(API_CATALOG_PATH)
    catalog_operations = {(row["method"], row["path"]): row for row in catalog_rows}
    permissions = {row["code"] for row in read_csv(PERMISSIONS_PATH)}
    errors = {row["code"]: int(row["http"]) for row in read_csv(ERRORS_PATH)}
    failures: list[str] = []

    if document.get("openapi") != "3.1.0":
        failures.append(f"OpenAPI version is {document.get('openapi')}")
    if len(contract_operations) != 241:
        failures.append(f"Expected 241 operations, found {len(contract_operations)}")
    if set(contract_operations) != set(catalog_operations):
        failures.append("method+path parity with catalogs/api_catalog.csv failed")

    unresolved = []
    for reference in iter_references(document):
        try:
            resolve_ref(document, reference)
        except (KeyError, TypeError, ValueError):
            unresolved.append(reference)
    if unresolved:
        failures.append(f"Unresolved references: {sorted(set(unresolved))}")

    schemas = document["components"]["schemas"]
    empty_schemas = [
        name for name, schema in schemas.items() if not concrete_schema(document, schema)
    ]
    if empty_schemas:
        failures.append(f"Non-concrete schemas: {empty_schemas}")
    unbounded = find_unbounded_objects(schemas, "$.components.schemas")
    if unbounded:
        failures.append(f"Unbounded additionalProperties=true: {unbounded}")

    operation_ids = [operation.get("operationId") for operation in contract_operations.values()]
    if None in operation_ids or len(operation_ids) != len(set(operation_ids)):
        failures.append("Operation IDs are missing or duplicated")

    for key, operation in contract_operations.items():
        method, path = key
        catalog = catalog_operations[key]
        parameters = resolved_parameters(document, operation)
        parameter_names = {(parameter["in"], parameter["name"]) for parameter in parameters}
        request_body = operation.get("requestBody")
        if request_body:
            request_schema = (
                request_body.get("content", {})
                .get("application/json", {})
                .get("schema")
            )
            if not isinstance(request_schema, dict) or not concrete_schema(document, request_schema):
                failures.append(f"{method} {path}: request body is not concrete")
        elif not catalog["request"].startswith(("query:", "path:")) and catalog["request"] != "—":
            failures.append(f"{method} {path}: catalog declares a body but OpenAPI does not")

        success_codes = [code for code in operation["responses"] if code.isdigit() and 200 <= int(code) < 300]
        if not success_codes:
            failures.append(f"{method} {path}: no success response")
        for code in success_codes:
            if code == "204":
                continue
            response = operation["responses"][code]
            response_schema = (
                response.get("content", {})
                .get("application/json", {})
                .get("schema")
            )
            if not isinstance(response_schema, dict) or not concrete_schema(document, response_schema):
                failures.append(f"{method} {path}: {code} response is not concrete")
            if "ETag" not in response.get("headers", {}):
                failures.append(f"{method} {path}: {code} response has no ETag")

        if (
            "If-Match" in catalog["locking"]
            and "optional" not in catalog["locking"]
            and "Per-item" not in catalog["locking"]
        ):
            if ("header", "If-Match") not in parameter_names:
                failures.append(f"{method} {path}: required If-Match missing")
        if catalog["idempotency"].startswith("Idempotency-Key") and "optional" not in catalog["idempotency"].lower():
            if ("header", "Idempotency-Key") not in parameter_names:
                failures.append(f"{method} {path}: required Idempotency-Key missing")

        permission = operation.get("x-permission")
        access_policy = operation.get("x-access-policy")
        if (permission or access_policy) != catalog["permission"]:
            failures.append(f"{method} {path}: permission differs from catalog")
        if permission is not None and permission not in permissions:
            failures.append(f"{method} {path}: unknown permission {permission}")
        if permission is None and access_policy not in {
            "Authenticated",
            "Anonymous",
            "Anonymous.SessionRefresh",
            "Anonymous/Network allowlist",
        }:
            failures.append(f"{method} {path}: missing permission or access policy")

        operation_error_codes = operation.get("x-error-codes")
        if not isinstance(operation_error_codes, list):
            failures.append(f"{method} {path}: x-error-codes missing")
        else:
            response_codes = {int(code) for code in operation["responses"] if code.isdigit()}
            for error_code in operation_error_codes:
                if error_code not in errors:
                    failures.append(f"{method} {path}: unknown error code {error_code}")
                elif errors[error_code] not in response_codes:
                    failures.append(
                        f"{method} {path}: error {error_code} has undeclared HTTP {errors[error_code]}"
                    )

    search = contract_operations[("GET", "/api/v1/search")]
    search_parameters = {
        parameter["name"]: parameter for parameter in resolved_parameters(document, search)
    }
    required_search_parameters = {
        "q",
        "types",
        "projectIds",
        "userIds",
        "departments",
        "contactIds",
        "hasFiles",
        "lifecycle",
        "from",
        "to",
        "cursor",
        "limit",
    }
    missing_search_parameters = required_search_parameters - set(search_parameters)
    if missing_search_parameters:
        failures.append(f"Search parameters missing: {sorted(missing_search_parameters)}")
    if "status" in search_parameters:
        failures.append("Search still exposes ambiguous status filter")
    if search.get("x-server-side-filtering") is not True:
        failures.append("Search server-side filtering is not normative")
    if search.get("x-client-post-filtering") != "forbidden":
        failures.append("Search client post-filtering is not forbidden")
    if search.get("x-pagination", {}).get("style") != "cursor":
        failures.append("Search pagination is not cursor-based")
    cursor_contract = search.get("x-cursor-pagination", {})
    for token in ("normalized query", "types", "contactIds", "hasFiles", "lifecycle", "authorization scope version"):
        if token not in cursor_contract.get("boundTo", []):
            failures.append(f"Search cursor is not bound to {token}")

    field_count = write_dto_catalog(document)
    differences = write_contract_diff(contract_operations, catalog_rows)
    if differences:
        failures.append(f"Contract diff contains {differences} differences")

    summary = {
        "stage": "2.2",
        "status": "pass" if not failures else "fail",
        "openapi": document["openapi"],
        "openapi_sha256": sha256(OPENAPI_PATH),
        "operations": len(contract_operations),
        "schemas": len(schemas),
        "dto_fields": field_count,
        "permissions": len(permissions),
        "errors": len(errors),
        "references": sum(1 for _ in iter_references(document)),
        "contract_differences": differences,
        "failures": failures,
    }
    SUMMARY_PATH.write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    if failures:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
