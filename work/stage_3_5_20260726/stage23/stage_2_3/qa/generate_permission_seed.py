from __future__ import annotations

import csv
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "catalogs" / "permissions.csv"
OUTPUT = ROOT / "db" / "002_seed_authorization.sql"


def sql_literal(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


with CATALOG.open(encoding="utf-8-sig", newline="") as source:
    permissions = sorted(csv.DictReader(source), key=lambda row: row["code"])

values = []
for index, permission in enumerate(permissions, start=1):
    permission_id = f"20000000-0000-7000-8000-{index:012d}"
    values.append(
        "("
        + ",".join(
            [
                sql_literal(permission_id),
                sql_literal(permission["code"]),
                sql_literal(permission["resource"]),
                sql_literal(permission["action"]),
                sql_literal(permission["description"]),
                permission["sensitive"].lower(),
            ]
        )
        + ")"
    )

codes = ",\n    ".join(sql_literal(permission["code"]) for permission in permissions)
body = f"""-- Organizer Stage 2.1 canonical authorization seed.
-- Generated from catalogs/permissions.csv by qa/generate_permission_seed.py.
BEGIN;

INSERT INTO iam.permissions (id, code, resource, action, description, is_sensitive)
VALUES
{",\n".join(values)}
ON CONFLICT (code) DO UPDATE SET
    resource = EXCLUDED.resource,
    action = EXCLUDED.action,
    description = EXCLUDED.description,
    is_sensitive = EXCLUDED.is_sensitive;

DELETE FROM iam.permissions
WHERE code NOT IN (
    {codes}
);

DO $$
DECLARE
    actual_count integer;
BEGIN
    SELECT count(*) INTO actual_count FROM iam.permissions;
    IF actual_count <> {len(permissions)} THEN
        RAISE EXCEPTION 'PERMISSION_CATALOG_COUNT_MISMATCH expected={len(permissions)} actual=%', actual_count;
    END IF;
END $$;

COMMIT;
"""

OUTPUT.write_text(body, encoding="utf-8", newline="\n")
