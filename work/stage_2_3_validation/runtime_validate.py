from __future__ import annotations

import argparse
import csv
import json
from collections import Counter
from pathlib import Path
from typing import Any

import yaml
from openapi_spec_validator import validate_spec


HTTP_METHODS = {"get", "put", "post", "patch", "delete", "head", "options", "trace"}


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def operations(document: dict[str, Any]) -> dict[str, tuple[str, str, dict[str, Any]]]:
    result: dict[str, tuple[str, str, dict[str, Any]]] = {}
    for path, path_item in document["paths"].items():
        for method, operation in path_item.items():
            if method.lower() not in HTTP_METHODS:
                continue
            operation_id = operation.get("operationId")
            if not operation_id:
                raise AssertionError(f"{method.upper()} {path}: operationId is missing")
            if operation_id in result:
                raise AssertionError(f"Duplicate operationId: {operation_id}")
            result[operation_id] = (method.upper(), path, operation)
    return result


def resolve_ref(document: dict[str, Any], reference: str) -> Any:
    if not reference.startswith("#/"):
        raise AssertionError(f"External reference is not allowed: {reference}")
    value: Any = document
    for token in reference[2:].split("/"):
        token = token.replace("~1", "/").replace("~0", "~")
        if not isinstance(value, dict) or token not in value:
            raise AssertionError(f"Unresolved reference: {reference}")
        value = value[token]
    return value


def walk(value: Any):
    yield value
    if isinstance(value, dict):
        for child in value.values():
            yield from walk(child)
    elif isinstance(value, list):
        for child in value:
            yield from walk(child)


def schema_is_concrete(document: dict[str, Any], schema: Any) -> bool:
    if not isinstance(schema, dict):
        return False
    if "$ref" in schema:
        return schema_is_concrete(document, resolve_ref(document, schema["$ref"]))
    if any(key in schema for key in ("oneOf", "anyOf", "allOf")):
        return all(
            schema_is_concrete(document, item)
            for key in ("oneOf", "anyOf", "allOf")
            for item in schema.get(key, [])
        )
    schema_type = schema.get("type")
    if schema_type == "object" or (isinstance(schema_type, list) and "object" in schema_type):
        return bool(schema.get("properties")) or isinstance(schema.get("additionalProperties"), dict)
    return schema_type is not None or "enum" in schema or "const" in schema


def parameter_names(document: dict[str, Any], operation: dict[str, Any]) -> set[str]:
    names: set[str] = set()
    for parameter in operation.get("parameters", []):
        if "$ref" in parameter:
            parameter = resolve_ref(document, parameter["$ref"])
        if isinstance(parameter, dict) and parameter.get("name"):
            names.add(str(parameter["name"]).lower())
    return names


def check_runtime_contract(
    document: dict[str, Any],
    permission_codes: set[str],
    stable_error_codes: set[str],
) -> dict[str, Any]:
    validate_spec(document)
    refs = 0
    unrestricted_objects: list[str] = []
    nullable_keywords: list[str] = []
    for node in walk(document):
        if not isinstance(node, dict):
            continue
        if "$ref" in node:
            resolve_ref(document, node["$ref"])
            refs += 1
        if node.get("additionalProperties") is True:
            unrestricted_objects.append(str(node.get("title", "<anonymous>")))
        if "nullable" in node:
            nullable_keywords.append(str(node.get("title", "<anonymous>")))
    if unrestricted_objects:
        raise AssertionError(f"Unrestricted additionalProperties=true: {unrestricted_objects[:10]}")
    if nullable_keywords:
        raise AssertionError(f"OpenAPI 3.0 nullable keyword used in 3.1 document: {nullable_keywords[:10]}")

    ops = operations(document)
    method_paths = [(method, path) for method, path, _ in ops.values()]
    duplicates = [item for item, count in Counter(method_paths).items() if count > 1]
    if duplicates:
        raise AssertionError(f"Duplicate method/path pairs: {duplicates}")

    request_schema_count = 0
    success_schema_count = 0
    permission_checks = 0
    stable_error_checks = 0
    for operation_id, (_, _, operation) in ops.items():
        permission = operation.get("x-permission")
        if permission and permission not in permission_codes:
            raise AssertionError(f"{operation_id}: unknown permission {permission}")
        if permission:
            permission_checks += 1
        for code in operation.get("x-error-codes", []):
            if code not in stable_error_codes:
                raise AssertionError(f"{operation_id}: unknown stable error {code}")
            stable_error_checks += 1
        request_body = operation.get("requestBody")
        if request_body:
            if "$ref" in request_body:
                request_body = resolve_ref(document, request_body["$ref"])
            content = request_body.get("content", {})
            schemas = [
                media.get("schema")
                for media in content.values()
                if isinstance(media, dict) and media.get("schema")
            ]
            if not schemas or not all(schema_is_concrete(document, schema) for schema in schemas):
                raise AssertionError(f"{operation_id}: request body does not have a concrete schema")
            request_schema_count += 1

        for status, response in operation.get("responses", {}).items():
            if not str(status).startswith("2"):
                continue
            if "$ref" in response:
                response = resolve_ref(document, response["$ref"])
            content = response.get("content", {})
            if not content:
                continue
            schemas = [
                media.get("schema")
                for media in content.values()
                if isinstance(media, dict) and media.get("schema")
            ]
            if not schemas or not all(schema_is_concrete(document, schema) for schema in schemas):
                raise AssertionError(f"{operation_id} {status}: success response is not concrete")
            success_schema_count += 1

        names = parameter_names(document, operation)
        if operation.get("x-optimistic-lock") and "required" in str(operation["x-optimistic-lock"]).lower():
            if "if-match" not in names:
                raise AssertionError(f"{operation_id}: required optimistic locking without If-Match")
        if operation.get("x-idempotency") and "required" in str(operation["x-idempotency"]).lower():
            if "idempotency-key" not in names:
                raise AssertionError(f"{operation_id}: required idempotency without Idempotency-Key")

    schemas = document["components"]["schemas"]
    empty_business_schemas = []
    for name, schema in schemas.items():
        if (
            isinstance(schema, dict)
            and schema.get("type") == "object"
            and not schema.get("properties")
            and not schema.get("allOf")
            and not isinstance(schema.get("additionalProperties"), dict)
        ):
            empty_business_schemas.append(name)
        required = schema.get("required", []) if isinstance(schema, dict) else []
        properties = schema.get("properties", {}) if isinstance(schema, dict) else {}
        missing_required = sorted(set(required) - set(properties))
        if missing_required:
            raise AssertionError(f"{name}: required fields missing from properties: {missing_required}")
    if empty_business_schemas:
        raise AssertionError(f"Empty business schemas: {empty_business_schemas}")

    urgency_patch = schemas["NotificationUrgencyScalePatch"]
    intervals = urgency_patch["properties"]["intervals"]
    if intervals.get("minItems") != 4 or intervals.get("maxItems") != 4:
        raise AssertionError("Urgency patch must contain exactly four intervals")
    search_operation = document["paths"]["/api/v1/search"]["get"]
    types_parameter = next(p for p in search_operation["parameters"] if p.get("name") == "types")
    if "employee" not in types_parameter["schema"]["items"]["enum"]:
        raise AssertionError("Employee global-search type is missing")
    if search_operation.get("x-filter-compatibility", {}).get("employee", {}).get("blockedUsers") is None:
        raise AssertionError("Employee blocked-user policy is missing")

    functional_contract_tests = [
        "urgency_default_scale_schema",
        "urgency_organization_scope",
        "urgency_exactly_four_intervals",
        "urgency_semantic_levels",
        "urgency_authorized_update_permission",
        "urgency_forbidden_contract",
        "urgency_invalid_interval_contract",
        "urgency_reset_contract",
        "urgency_if_match",
        "urgency_etag",
        "urgency_idempotency",
        "urgency_audit_event",
        "urgency_semantic_color_independence",
        "urgency_old_client_compatibility",
        "employee_separate_result_type",
        "employee_name_and_confirmed_fields",
        "employee_result_group_contract",
        "employee_mixed_type_query",
        "employee_permission_filtering",
        "employee_redaction",
        "employee_partial_access",
        "employee_blocked_user_policy",
        "employee_cursor_pagination",
        "employee_cursor_policy_binding",
        "employee_server_side_filtering",
        "employee_deep_link",
        "employee_not_admin_user_search",
    ]
    urgency_level = schemas["UrgencyLevel"]
    if set(urgency_level.get("enum", [])) != {"low", "normal", "high", "critical"}:
        raise AssertionError("Semantic urgency level enum is incomplete")
    employee = schemas["EmployeeSearchResult"]
    if not {
        "userId",
        "displayName",
        "accountStatus",
        "deepLink",
        "isRedacted",
    }.issubset(employee.get("required", [])):
        raise AssertionError("Employee search result does not require its confirmed identity/policy fields")
    urgency_path = document["paths"]["/api/v1/settings/notification-urgency-scale"]
    reset_operation = document["paths"]["/api/v1/settings/notification-urgency-scale/reset"]["post"]
    if urgency_path["get"].get("x-permission") != "Settings.ReadOwn":
        raise AssertionError("Urgency scale read permission is incorrect")
    for operation in (urgency_path["put"], reset_operation):
        if operation.get("x-permission") != "System.Configure":
            raise AssertionError("Urgency scale write permission is incorrect")
        names = parameter_names(document, operation)
        if not {"if-match", "idempotency-key"}.issubset(names):
            raise AssertionError("Urgency scale writes must carry If-Match and Idempotency-Key")
        if operation.get("x-audit") != "notification_urgency_scale.changed":
            raise AssertionError("Urgency scale audit action is missing")
        success = operation["responses"]["200"]
        if "ETag" not in success.get("headers", {}):
            raise AssertionError("Urgency scale write response does not expose ETag")
    if not {"400", "403", "409", "412", "422", "428"}.issubset(urgency_path["put"]["responses"]):
        raise AssertionError("Urgency scale validation/conflict response contract is incomplete")
    if "employee visibility policy version" not in search_operation.get("x-cursor-pagination", {}).get("boundTo", []):
        raise AssertionError("Employee cursor stability is not bound to visibility policy")
    search_description = search_operation.get("description", "").lower()
    if "filtered on the server before cursor pagination" not in search_description:
        raise AssertionError("Employee search must filter on the server before pagination")

    return {
        "operations": len(ops),
        "schemas": len(schemas),
        "refs": refs,
        "request_schemas": request_schema_count,
        "success_schemas": success_schema_count,
        "permission_checks": permission_checks,
        "stable_error_checks": stable_error_checks,
        "functional_contract_tests": functional_contract_tests,
    }


def compare_contracts(
    old: dict[str, Any],
    new: dict[str, Any],
    old_root: Path,
    new_root: Path,
) -> tuple[list[dict[str, str]], dict[str, Any]]:
    old_ops = operations(old)
    new_ops = operations(new)
    missing_operations = sorted(set(old_ops) - set(new_ops))
    moved_operations = sorted(
        operation_id
        for operation_id in set(old_ops) & set(new_ops)
        if old_ops[operation_id][:2] != new_ops[operation_id][:2]
    )
    if missing_operations or moved_operations:
        raise AssertionError(
            f"Breaking operation changes: missing={missing_operations}, moved={moved_operations}"
        )

    rows: list[dict[str, str]] = []
    for operation_id in sorted(set(new_ops) - set(old_ops)):
        method, path, _ = new_ops[operation_id]
        rows.append(
            {
                "kind": "operation",
                "name": operation_id,
                "change": f"added {method} {path}",
                "compatibility": "additive",
            }
        )

    old_schemas = old["components"]["schemas"]
    new_schemas = new["components"]["schemas"]
    missing_schemas = sorted(set(old_schemas) - set(new_schemas))
    if missing_schemas:
        raise AssertionError(f"Removed schemas: {missing_schemas}")

    changed_schemas: list[str] = []
    new_fields: list[str] = []
    new_enum_values: list[str] = []
    for name in sorted(set(old_schemas) & set(new_schemas)):
        old_schema = old_schemas[name]
        new_schema = new_schemas[name]
        old_properties = old_schema.get("properties", {}) if isinstance(old_schema, dict) else {}
        new_properties = new_schema.get("properties", {}) if isinstance(new_schema, dict) else {}
        removed_properties = sorted(set(old_properties) - set(new_properties))
        added_required = sorted(set(new_schema.get("required", [])) - set(old_schema.get("required", [])))
        if removed_properties or added_required:
            raise AssertionError(
                f"Breaking schema {name}: removed={removed_properties}, added_required={added_required}"
            )
        schema_changed = old_schema != new_schema
        if schema_changed:
            changed_schemas.append(name)
        for field in sorted(set(new_properties) - set(old_properties)):
            new_fields.append(f"{name}.{field}")
            rows.append(
                {
                    "kind": "field",
                    "name": f"{name}.{field}",
                    "change": "added optional field",
                    "compatibility": "additive",
                }
            )
        for field in sorted(set(old_properties) & set(new_properties)):
            old_property = old_properties[field]
            new_property = new_properties[field]
            old_types = json.dumps(old_property.get("type"), sort_keys=True)
            new_types = json.dumps(new_property.get("type"), sort_keys=True)
            if old_types != new_types:
                raise AssertionError(f"Property type changed: {name}.{field}: {old_types} -> {new_types}")
            old_enum = set(old_property.get("enum", []))
            new_enum = set(new_property.get("enum", []))
            if not old_enum.issubset(new_enum):
                raise AssertionError(f"Enum narrowed: {name}.{field}")
            for value in sorted(new_enum - old_enum):
                item = f"{name}.{field}={value}"
                new_enum_values.append(item)
                rows.append(
                    {
                        "kind": "enum_value",
                        "name": item,
                        "change": "added",
                        "compatibility": "additive",
                    }
                )

    for name in sorted(set(new_schemas) - set(old_schemas)):
        rows.append(
            {
                "kind": "schema",
                "name": name,
                "change": "added",
                "compatibility": "additive",
            }
        )

    old_permissions = {row["code"] for row in read_csv(old_root / "catalogs" / "permissions.csv")}
    new_permissions = {row["code"] for row in read_csv(new_root / "catalogs" / "permissions.csv")}
    old_errors = {row["code"] for row in read_csv(old_root / "catalogs" / "errors.csv")}
    new_errors = {row["code"] for row in read_csv(new_root / "catalogs" / "errors.csv")}
    if not old_permissions.issubset(new_permissions):
        raise AssertionError("Existing permissions were removed")
    if not old_errors.issubset(new_errors):
        raise AssertionError("Existing stable errors were removed")
    for code in sorted(new_permissions - old_permissions):
        rows.append({"kind": "permission", "name": code, "change": "added", "compatibility": "additive"})
    for code in sorted(new_errors - old_errors):
        rows.append({"kind": "stable_error", "name": code, "change": "added", "compatibility": "additive"})

    old_migrations = {path.name for path in (old_root / "db").glob("*.sql")}
    new_migrations = {path.name for path in (new_root / "db").glob("*.sql")}
    for migration in sorted(new_migrations - old_migrations):
        rows.append(
            {
                "kind": "migration",
                "name": migration,
                "change": "added",
                "compatibility": "additive forward-fix",
            }
        )

    summary = {
        "existing_operations_preserved": len(old_ops),
        "new_operations": sorted(set(new_ops) - set(old_ops)),
        "new_schemas": sorted(set(new_schemas) - set(old_schemas)),
        "changed_schemas": changed_schemas,
        "new_fields": new_fields,
        "new_enum_values": new_enum_values,
        "new_permissions": sorted(new_permissions - old_permissions),
        "new_errors": sorted(new_errors - old_errors),
        "new_migrations": sorted(new_migrations - old_migrations),
    }
    return rows, summary


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--old-root", type=Path, required=True)
    parser.add_argument("--new-root", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    old_root = args.old_root.resolve()
    new_root = args.new_root.resolve()
    output_dir = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    old_document = yaml.safe_load((old_root / "openapi" / "openapi.yaml").read_text(encoding="utf-8"))
    new_document = yaml.safe_load((new_root / "openapi" / "openapi.yaml").read_text(encoding="utf-8"))
    permission_rows = read_csv(new_root / "catalogs" / "permissions.csv")
    error_rows = read_csv(new_root / "catalogs" / "errors.csv")
    runtime = check_runtime_contract(
        new_document,
        {row["code"] for row in permission_rows},
        {row["code"] for row in error_rows},
    )
    rows, compatibility = compare_contracts(old_document, new_document, old_root, new_root)

    api_catalog = read_csv(new_root / "catalogs" / "api_catalog.csv")
    dto_catalog = read_csv(new_root / "dto_field_catalog.csv")
    permissions = read_csv(new_root / "catalogs" / "permissions.csv")
    errors = read_csv(new_root / "catalogs" / "errors.csv")
    if len(api_catalog) != runtime["operations"]:
        raise AssertionError("API catalog count does not match OpenAPI operation count")
    new_operations = operations(new_document)
    for row in api_catalog:
        matching = [
            operation
            for method, path, operation in new_operations.values()
            if method == row["method"].upper() and path == row["path"]
        ]
        if len(matching) != 1:
            raise AssertionError(f"API catalog row does not match one OpenAPI operation: {row['method']} {row['path']}")
        openapi_permission = matching[0].get("x-permission", "")
        if openapi_permission and openapi_permission != row["permission"]:
            raise AssertionError(
                f"Permission mismatch for {row['method']} {row['path']}: "
                f"OpenAPI={openapi_permission}, catalog={row['permission']}"
            )
    if len({row["code"] for row in permissions}) != len(permissions):
        raise AssertionError("Permission codes are not unique")
    if len({row["code"] for row in errors}) != len(errors):
        raise AssertionError("Stable error codes are not unique")

    with (output_dir / "Stage_2_3_Contract_Diff.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=["kind", "name", "change", "compatibility"])
        writer.writeheader()
        writer.writerows(rows)

    result = {
        **runtime,
        "dto_catalog_rows": len(dto_catalog),
        "permissions": len(permissions),
        "stable_errors": len(errors),
        "compatibility": compatibility,
        "status": "PASS",
    }
    (output_dir / "runtime_validation.json").write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
