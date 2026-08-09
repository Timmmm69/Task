from __future__ import annotations

import csv
import hashlib
import json
import shutil
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "sources" / "stage_2_2" / "Organizer_Stage2_Technical_Specification_2.2"
OUT = ROOT / "outputs" / "stage_2_3"


def prop(type_, **extra):
    result = {"type": type_}
    result.update(extra)
    return result


def ref(name):
    return {"$ref": f"#/components/schemas/{name}"}


def problem():
    return {"description": "Problem response.", "content": {"application/problem+json": {"schema": ref("ProblemDetails")}}}


def response(schema, description="Successful response."):
    return {"description": description, "headers": {"X-Correlation-ID": {"$ref": "#/components/headers/CorrelationId"}, "ETag": {"$ref": "#/components/headers/ETag"}}, "content": {"application/json": {"schema": ref(schema)}}}


def copy_source():
    if OUT.exists():
        shutil.rmtree(OUT)
    shutil.copytree(SRC, OUT, ignore=shutil.ignore_patterns("bin", "obj", "*.dll", "*.pdb", "*.cache"))
    server_stub_generator = OUT / "qa" / "generate_server_stub.py"
    generator_text = server_stub_generator.read_text(encoding="utf-8")
    generator_text = generator_text.replace(
        "Expected 241 unique operation IDs",
        "Expected 244 unique operation IDs",
    ).replace(
        "len(operation_ids) != 241",
        "len(operation_ids) != 244",
    )
    server_stub_generator.write_text(generator_text, encoding="utf-8", newline="\n")


def extend_openapi():
    path = OUT / "openapi" / "openapi.yaml"
    spec = yaml.safe_load(path.read_text(encoding="utf-8"))
    spec["info"]["version"] = "1.2.0-stage2.3"
    spec["info"]["description"] += "\n\nStage 2.3 closes OQ-001 (organization urgency scale) and OQ-003 (employee global-search results)."
    schemas = spec["components"]["schemas"]
    schemas.update({
        "UrgencyLevel": {"type": "string", "enum": ["low", "normal", "high", "critical"], "description": "Semantic urgency. Color is presentation metadata and never the only urgency signal."},
        "UrgencyScaleInterval": {"type": "object", "additionalProperties": False, "properties": {
            "urgencyLevel": ref("UrgencyLevel"), "minScore": prop("integer", format="int32", minimum=0, maximum=100),
            "maxScore": prop("integer", format="int32", minimum=0, maximum=100), "displayToken": prop("string", minLength=1, maxLength=64)
        }, "required": ["urgencyLevel", "minScore", "maxScore", "displayToken"], "description": "Inclusive score interval. Intervals are ordered, contiguous, and non-overlapping from 0 through 100."},
        "NotificationUrgencyScale": {"type": "object", "additionalProperties": False, "properties": {
            "scope": {"type": "string", "enum": ["organization"]}, "intervals": {"type": "array", "minItems": 4, "maxItems": 4, "uniqueItems": True, "items": ref("UrgencyScaleInterval")},
            "version": prop("integer", format="int64", minimum=1), "updatedAt": prop("string", format="date-time"), "updatedByUserId": {"type": ["string", "null"], "format": "uuid"}
        }, "required": ["scope", "intervals", "version", "updatedAt"]},
        "NotificationUrgencyScalePatch": {"type": "object", "additionalProperties": False, "properties": {"intervals": {"type": "array", "minItems": 4, "maxItems": 4, "items": ref("UrgencyScaleInterval")}}, "required": ["intervals"], "description": "Exactly one interval for each semantic urgency level; scores cover 0..100 with no gaps or overlap."},
        "EmployeeSearchResult": {"type": "object", "additionalProperties": False, "properties": {
            "userId": prop("string", format="uuid"), "displayName": prop("string", minLength=1, maxLength=200), "departmentId": {"type": ["string", "null"], "format": "uuid"}, "departmentName": {"type": ["string", "null"], "maxLength": 200},
            "jobTitle": {"type": ["string", "null"], "maxLength": 200}, "accountStatus": {"type": "string", "enum": ["active", "blocked", "inactive"]}, "deepLink": prop("string", format="uri", maxLength=2048), "isRedacted": prop("boolean")
        }, "required": ["userId", "displayName", "accountStatus", "deepLink", "isRedacted"], "description": "Server-authorized employee result. Redacted fields are null; blocked users are omitted unless the caller has User.Block."}
    })
    suggestion = schemas["SearchSuggestion"]
    suggestion["properties"].update({"resultType": {"type": "string", "enum": ["object", "employee"], "default": "object"}, "employee": {"oneOf": [ref("EmployeeSearchResult"), {"type": "null"}]}})
    # New fields are optional so pre-2.3 clients retain their generic object rendering.
    search = spec["paths"]["/api/v1/search"]["get"]
    type_param = next(p for p in search["parameters"] if p.get("name") == "types")
    type_param["schema"]["items"]["enum"].append("employee")
    type_param["schema"]["maxItems"] = 10
    search["x-filter-compatibility"]["lifecycle"].append("employee")
    search["x-filter-compatibility"]["employee"] = {"allowed": ["q", "departments", "types", "cursor", "limit"], "forbidden": ["userIds", "projectIds", "contactIds", "hasFiles", "from", "to"], "blockedUsers": "omit unless User.Block; never disclose blocked existence to unauthorized callers"}
    search["x-cursor-pagination"]["boundTo"].append("employee visibility policy version")
    search["description"] = "Search is authorized and filtered on the server before cursor pagination. type=employee returns the separate Employees result group; userIds remains only a related-object filter."
    paths = spec["paths"]
    common = {"x-permission": "System.Configure", "x-required-capability": "System.Configure", "x-audit": "notification_urgency_scale.changed", "x-owner": "organization", "x-user-override": "not supported", "x-side-effects": "Changes presentation mapping for existing and future notifications; semantic urgency remains unchanged; old clients use their built-in display mapping.", "x-error-codes": ["AUTHENTICATION_REQUIRED", "FORBIDDEN", "VALIDATION_FAILED", "VERSION_CONFLICT"]}
    paths["/api/v1/settings/notification-urgency-scale"] = {
        "get": {"tags": ["settings"], "operationId": "GET_api_v1_settings_notification_urgency_scale", "summary": "Get organization notification urgency scale", "x-permission": "Settings.ReadOwn", "x-required-capability": "Settings.ReadOwn", "x-transaction": "Read-only", "parameters": [{"$ref": "#/components/parameters/CorrelationId"}], "responses": {"200": response("NotificationUrgencyScale"), "401": problem(), "403": problem()}},
        "put": {"tags": ["settings"], "operationId": "PUT_api_v1_settings_notification_urgency_scale", "summary": "Replace organization notification urgency scale", **common, "x-transaction": "Versioned write", "x-optimistic-lock": "If-Match required", "x-idempotency": "Idempotency-Key required", "parameters": [{"$ref": "#/components/parameters/CorrelationId"}, {"$ref": "#/components/parameters/IdempotencyKey"}, {"$ref": "#/components/parameters/IfMatch"}], "requestBody": {"required": True, "content": {"application/json": {"schema": ref("NotificationUrgencyScalePatch")}}}, "responses": {"200": response("NotificationUrgencyScale"), "400": problem(), "401": problem(), "403": problem(), "409": problem(), "412": problem(), "422": problem(), "428": problem()}}
    }
    paths["/api/v1/settings/notification-urgency-scale/reset"] = {"post": {"tags": ["settings"], "operationId": "POST_api_v1_settings_notification_urgency_scale_reset", "summary": "Reset organization urgency scale to defaults", **common, "x-transaction": "Versioned write", "x-optimistic-lock": "If-Match required", "x-idempotency": "Idempotency-Key required", "parameters": [{"$ref": "#/components/parameters/CorrelationId"}, {"$ref": "#/components/parameters/IdempotencyKey"}, {"$ref": "#/components/parameters/IfMatch"}], "responses": {"200": response("NotificationUrgencyScale"), "401": problem(), "403": problem(), "409": problem(), "412": problem(), "428": problem()}}}
    path.write_text(yaml.safe_dump(spec, allow_unicode=True, sort_keys=False, width=120), encoding="utf-8")
    return spec


def extend_catalogs(spec):
    api = OUT / "catalogs" / "api_catalog.csv"
    rows = list(csv.DictReader(api.open(encoding="utf-8-sig")))
    fields = list(rows[0])
    added = [
        {"module": "settings", "method": "GET", "path": "/api/v1/settings/notification-urgency-scale", "purpose": "Получить организационную шкалу срочности уведомлений", "permission": "Settings.ReadOwn", "request": "—", "response": "NotificationUrgencyScale", "codes": "200,401,403", "idempotency": "Safe", "transaction": "Read-only", "locking": "—", "effects": "—", "events": "—"},
        {"module": "settings", "method": "PUT", "path": "/api/v1/settings/notification-urgency-scale", "purpose": "Заменить организационную шкалу срочности", "permission": "System.Configure", "request": "NotificationUrgencyScalePatch", "response": "NotificationUrgencyScale", "codes": "200,400,401,403,409,412,422,428", "idempotency": "Idempotency-Key", "transaction": "Versioned write", "locking": "If-Match", "effects": "notification presentation mapping", "events": "notification_urgency_scale.changed"},
        {"module": "settings", "method": "POST", "path": "/api/v1/settings/notification-urgency-scale/reset", "purpose": "Сбросить шкалу срочности к defaults", "permission": "System.Configure", "request": "—", "response": "NotificationUrgencyScale", "codes": "200,401,403,409,412,428", "idempotency": "Idempotency-Key", "transaction": "Versioned write", "locking": "If-Match", "effects": "notification presentation mapping", "events": "notification_urgency_scale.changed"},
    ]
    for item in added:
        row = {f: "" for f in fields}
        for key, value in item.items():
            row[key] = value
        rows.append(row)
    with api.open("w", encoding="utf-8", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=fields); w.writeheader(); w.writerows(rows)
    dto = OUT / "dto_field_catalog.csv"
    dto_rows = list(csv.DictReader(dto.open(encoding="utf-8-sig"))); dto_fields = list(dto_rows[0])
    for name in ["UrgencyLevel", "UrgencyScaleInterval", "NotificationUrgencyScale", "NotificationUrgencyScalePatch", "EmployeeSearchResult"]:
        schema = spec["components"]["schemas"][name]
        for pname, pdef in schema.get("properties", {}).items():
            row = {f: "" for f in dto_fields}; row[dto_fields[0]] = name; row[dto_fields[1]] = pname
            row[dto_fields[2]] = pdef.get("type", "reference" if "$ref" in pdef else "oneOf") if isinstance(pdef, dict) else ""
            row[dto_fields[4]] = str(pname in schema.get("required", [])).lower(); dto_rows.append(row)
    with dto.open("w", encoding="utf-8", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=dto_fields); w.writeheader(); w.writerows(dto_rows)

    endpoint_lines = [
        "# Organizer Stage 2.3 endpoint dump",
        f"# Operations: {len(rows)}",
        "",
    ]
    for row in sorted(rows, key=lambda item: (item["path"], item["method"])):
        endpoint_lines.append(
            f"{row['method']:6} {row['path']} | {row['permission']} | "
            f"{row['request']} -> {row['response']} | {row['codes']}"
        )
    (OUT / "endpoints_dump.txt").write_text(
        "\n".join(endpoint_lines) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def write_sql_and_docs():
    migration = OUT / "db" / "005_stage_2_3_contract_alignment.sql"
    migration.write_text('''-- Stage 2.3: organization-owned notification urgency scale (PostgreSQL 16).
BEGIN;

CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TABLE IF NOT EXISTS notify.notification_urgency_scales (
    organization_id uuid PRIMARY KEY REFERENCES core.organizations(id) ON DELETE CASCADE,
    version bigint NOT NULL DEFAULT 1 CHECK (version >= 1),
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by_user_id uuid NULL REFERENCES iam.user_accounts(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS notify.notification_urgency_scale_intervals (
    organization_id uuid NOT NULL REFERENCES notify.notification_urgency_scales(organization_id) ON DELETE CASCADE,
    urgency_level text NOT NULL CHECK (urgency_level IN ('low','normal','high','critical')),
    min_score integer NOT NULL CHECK (min_score BETWEEN 0 AND 100),
    max_score integer NOT NULL CHECK (max_score BETWEEN 0 AND 100),
    display_token varchar(64) NOT NULL CHECK (length(btrim(display_token)) > 0),
    PRIMARY KEY (organization_id, urgency_level),
    CHECK (min_score <= max_score),
    EXCLUDE USING gist (organization_id WITH =, int4range(min_score, max_score, '[]') WITH &&)
);

CREATE INDEX IF NOT EXISTS ix_notification_urgency_scale_intervals_order
    ON notify.notification_urgency_scale_intervals (organization_id, min_score);

CREATE OR REPLACE FUNCTION notify.enforce_notification_urgency_scale_complete()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    target_organization_id uuid := COALESCE(NEW.organization_id, OLD.organization_id);
    interval_count integer;
    first_score integer;
    last_score integer;
    gap_count integer;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM notify.notification_urgency_scales
        WHERE organization_id = target_organization_id
    ) THEN
        RETURN NULL;
    END IF;

    SELECT count(*), min(min_score), max(max_score)
    INTO interval_count, first_score, last_score
    FROM notify.notification_urgency_scale_intervals
    WHERE organization_id = target_organization_id;

    SELECT count(*)
    INTO gap_count
    FROM (
        SELECT min_score,
               lag(max_score) OVER (ORDER BY min_score, urgency_level) AS previous_max
        FROM notify.notification_urgency_scale_intervals
        WHERE organization_id = target_organization_id
    ) ordered_intervals
    WHERE previous_max IS NOT NULL
      AND min_score <> previous_max + 1;

    IF interval_count <> 4 OR first_score <> 0 OR last_score <> 100 OR gap_count <> 0 THEN
        RAISE EXCEPTION
            'Notification urgency scale for organization % must contain four contiguous intervals covering 0..100',
            target_organization_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS trg_notification_urgency_scale_complete
    ON notify.notification_urgency_scale_intervals;
CREATE CONSTRAINT TRIGGER trg_notification_urgency_scale_complete
AFTER INSERT OR UPDATE OR DELETE ON notify.notification_urgency_scale_intervals
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION notify.enforce_notification_urgency_scale_complete();

INSERT INTO notify.notification_urgency_scales (organization_id)
SELECT id
FROM core.organizations
ON CONFLICT (organization_id) DO NOTHING;

INSERT INTO notify.notification_urgency_scale_intervals
    (organization_id, urgency_level, min_score, max_score, display_token)
SELECT organizations.id, defaults.urgency_level, defaults.min_score, defaults.max_score, defaults.display_token
FROM core.organizations AS organizations
CROSS JOIN (
    VALUES
        ('low', 0, 24, 'urgency.low'),
        ('normal', 25, 49, 'urgency.normal'),
        ('high', 50, 74, 'urgency.high'),
        ('critical', 75, 100, 'urgency.critical')
) AS defaults(urgency_level, min_score, max_score, display_token)
ON CONFLICT (organization_id, urgency_level) DO NOTHING;

-- Versioned PUT/reset records audit action notification_urgency_scale.changed in the existing audit_entries history.
-- Search employee projection uses existing users/departments indexes; authorization and blocked-user policy run before cursor pagination.

COMMIT;
''', encoding="utf-8")
    (OUT / "Stage_2_3_Contract_Alignment.md").write_text('''# Stage 2.3 — Contract Alignment

## OQ-001: organization notification urgency scale

The scale is organization-owned; no per-user override is introduced because the concept requires a common configurable scale, not personal urgency semantics. Four semantic levels (`low`, `normal`, `high`, `critical`) remain explicit and color/display tokens are secondary presentation metadata. Scores are inclusive 0–100 and the four intervals must be ordered, contiguous, complete, and non-overlapping. Defaults are 0–24, 25–49, 50–74, 75–100. `PUT` and reset require `System.Configure`, `If-Match`, and `Idempotency-Key`, emit audit action `notification_urgency_scale.changed`, and return ETag. Existing notifications keep their semantic urgency; both existing and future notifications resolve presentation from the current scale. A 2.2 client remains compatible because the old notification DTO is unchanged and it uses its existing display mapping.

## OQ-003: employees in global search

`employee` is a new value of the existing `types` filter and is returned as `SearchSuggestion.resultType=employee` with concrete `EmployeeSearchResult`. It supplies display name, department, optional job title (only where modeled), account status, deep link, and redaction marker. The server authorizes, redacts, ranks, groups as “Employees”, and filters before cursor pagination; cursor binding adds employee visibility policy version. `userIds` remains a related-object filter and is not an employee-search substitute. Blocked users are omitted unless the caller has the existing sensitive `User.Block` capability; unauthorized callers cannot infer their existence.

## Errors and permissions

No new stable error code is needed: `VALIDATION_FAILED`, `FORBIDDEN`, and `VERSION_CONFLICT` cover the additions. Existing `Settings.ReadOwn`, `System.Configure`, `Search.Use`, and `User.Block` are reused.
''', encoding="utf-8")
    (OUT / "Stage_2_3_Fix_Registry.md").write_text("# Stage 2.3 Fix Registry\n\n| ID | Status | Resolution |\n|---|---|---|\n| OQ-001 | Closed | Organization urgency-scale DTO/API, migration, validation and audit contract added. |\n| OQ-003 | Closed | Employee search type, concrete DTO, policy, cursor and redaction contract added. |\n", encoding="utf-8")


def main():
    copy_source(); spec = extend_openapi(); extend_catalogs(spec); write_sql_and_docs()
    print(OUT)


if __name__ == "__main__":
    main()
