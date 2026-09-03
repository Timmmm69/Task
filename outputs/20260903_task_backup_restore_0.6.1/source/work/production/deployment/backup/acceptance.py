#!/usr/bin/python3
"""Measured, isolated recovery drills. Receipts are evidence, not deployment certification."""
import argparse
import datetime as dt
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import sys
import time
import uuid

import runner

VERSION = "0.6.1"
SMOKE_TABLES = ("iam.user_accounts", "work.tasks", "calendar.events", "governance.audit_entries")
# Deterministic application schema signature; no row contents, credentials or server paths.
SCHEMA_SQL = """
SELECT json_agg(item ORDER BY item::text)::text FROM (
 SELECT json_build_array('column', n.nspname, c.relname, a.attname,
   format_type(a.atttypid,a.atttypmod), a.attnotnull,
   pg_get_expr(d.adbin,d.adrelid))::text item
 FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid
 JOIN pg_namespace n ON n.oid=c.relnamespace
 LEFT JOIN pg_attrdef d ON d.adrelid=c.oid AND d.adnum=a.attnum
 WHERE n.nspname IN ('core','work','org','iam','governance','calendar')
 AND a.attnum>0 AND NOT a.attisdropped
 UNION ALL
 SELECT json_build_array('constraint', n.nspname, c.relname, x.conname,
   pg_get_constraintdef(x.oid))::text
 FROM pg_constraint x JOIN pg_class c ON c.oid=x.conrelid
 JOIN pg_namespace n ON n.oid=c.relnamespace
 WHERE n.nspname IN ('core','work','org','iam','governance','calendar')
 UNION ALL
 SELECT json_build_array('index', schemaname, tablename, indexname, indexdef)::text
 FROM pg_indexes WHERE schemaname IN ('core','work','org','iam','governance','calendar')
) evidence
"""


def utc(value):
    stamp = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    if stamp.tzinfo is None or stamp.utcoffset() != dt.timedelta(0):
        raise runner.ProtectionError("Use an explicit UTC timestamp")
    return stamp


def validate_request(args, now):
    target, incident = utc(args.target), utc(args.incident_at)
    if not target <= incident <= now:
        raise runner.ProtectionError("Require target <= incident <= drill start")
    if (incident - target).total_seconds() > 900:
        raise runner.ProtectionError("Requested recovery point exceeds the 15-minute RPO")
    if (now - incident).total_seconds() >= 14400:
        raise runner.ProtectionError("Incident already exceeds the 4-hour RTO")
    if args.minimum_database_bytes <= 0:
        raise runner.ProtectionError("A positive representative backup size is required")
    if not re.fullmatch(r"[a-f0-9]{64}", args.expected_schema_sha256):
        raise runner.ProtectionError("Expected schema SHA-256 is required")
    for value in (args.dataset_id, args.storage_copy_id, args.escrow_copy_id):
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,79}", value):
            raise runner.ProtectionError("Evidence identifiers must be short non-secret identifiers")
    return target, incident


def isolation():
    if os.environ.get("TASK_BACKUP_VALIDATION") == "1":
        raise runner.ProtectionError("Acceptance cannot run with the storage validation bypass")
    runner.validate_storage(2)
    if (runner.DATA / "PG_VERSION").exists() or Path("/run/postgresql/.s.PGSQL.5432").exists():
        raise runner.ProtectionError("Recovery operator must have no primary database or socket")
    if runner.REPOS[0].exists() and any(runner.REPOS[0].iterdir()):
        raise runner.ProtectionError("Recovery operator must have only the secondary repository")
    options = runner.run(["findmnt", "-n", "-T", str(runner.REPOS[1]), "-o", "VFS-OPTIONS"], 10).strip()
    if "ro" not in options.split(","):
        raise runner.ProtectionError("Recovery repository must be mounted read-only")


def database_evidence(socket):
    def sql(query):
        return runner.run(["psql", "-X", "-h", socket, "-U", "postgres", "-d", "task",
                           "-At", "-v", "ON_ERROR_STOP=1", "-c",
                           "BEGIN READ ONLY; SET LOCAL statement_timeout='30s'; " + query + "; COMMIT"], 40).strip()

    # Strip transaction command tags without ever returning business row values.
    schema = sql(SCHEMA_SQL).splitlines()
    schema = [line for line in schema if line not in ("BEGIN", "SET", "COMMIT")]
    if len(schema) != 1 or not schema[0] or schema[0] == "null":
        raise runner.ProtectionError("Application schema is missing")
    for table in SMOKE_TABLES:
        sql(f"SELECT EXISTS (SELECT 1 FROM {table} LIMIT 1)")
    return {"schemaSha256": hashlib.sha256(schema[0].encode()).hexdigest(),
            "readSmoke": list(SMOKE_TABLES)}


def drill(args, started):
    target, incident = validate_request(args, started)
    isolation()
    runner.checked_label(args.label)
    selected = next((item for item in runner.catalog(2) if item["label"] == args.label), None)
    if not selected or target.timestamp() <= selected["timestamp"]["stop"]:
        raise runner.ProtectionError("Target must follow the selected completed backup")
    if selected["info"]["size"] < args.minimum_database_bytes:
        raise runner.ProtectionError("Backup is smaller than the declared representative dataset")
    monotonic_start = time.monotonic()
    preparation_seconds = (dt.datetime.now(dt.timezone.utc) - incident).total_seconds()
    recovered = runner.restore(2, args.label, args.target, keep=True)
    directory = Path(recovered["data"]).parent
    try:
        database = database_evidence(recovered["socket"])
        if database["schemaSha256"] != args.expected_schema_sha256:
            raise runner.ProtectionError("Recovered schema differs from the approved baseline")
        measured = time.monotonic() - monotonic_start
        rto = preparation_seconds + measured
        if rto > 14400:
            raise runner.ProtectionError("Measured recovery exceeds the 4-hour RTO")
        return {"scope": args.scope, "datasetId": args.dataset_id,
                "storageCopyId": args.storage_copy_id, "escrowCopyId": args.escrow_copy_id,
                "label": args.label, "target": args.target, "incidentAt": args.incident_at,
                "databaseBytes": selected["info"]["size"], "minimumDatabaseBytes": args.minimum_database_bytes,
                "requestedLossWindowSeconds": (incident - target).total_seconds(),
                "incidentToDatabaseReadySeconds": round(rto, 3),
                "restoreAndDatabaseSmokeSeconds": round(measured, 3),
                "assetsSha256": recovered["assets"]["sha256"], **database,
                "productionAccepted": False,
                "remainingAcceptance": ["physical storage separation and independent snapshot retention",
                    "two escrow copies on separate controlled media", "representative workload approval",
                    "restricted API login/task/audit/catalog smoke and service-ready RTO",
                    "alert delivery and ownership", "incident owner approval"]}
    finally:
        # Delete only the unique workspace returned by the trusted fixed-operation runner.
        if directory.parent != runner.RESTORE or not directory.name.startswith("drill-"):
            raise runner.ProtectionError("Unsafe recovery cleanup path")
        runner.run(["pg_ctl", "-D", recovered["data"], "-m", "fast", "-w", "stop"], 60)
        shutil.rmtree(directory)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("baseline", help="Read schema and core-table smoke from the primary Unix socket")
    recovery = commands.add_parser("drill", help="Restore only from a read-only secondary into fresh storage")
    for name in ("label", "target", "incident-at", "dataset-id", "storage-copy-id", "escrow-copy-id", "expected-schema-sha256"):
        recovery.add_argument("--" + name, required=True)
    recovery.add_argument("--minimum-database-bytes", type=int, required=True)
    recovery.add_argument("--scope", choices=("fixture", "company"), required=True)
    args = parser.parse_args(argv)
    os.umask(0o077)
    started = dt.datetime.now(dt.timezone.utc)
    event = {"version": VERSION, "started": started.isoformat(), "command": args.command,
             "sourceSha256": {Path(path).name: runner.digest(Path(path))
                              for path in (__file__, runner.__file__)}}
    try:
        if args.command == "baseline":
            event.update(status="succeeded", result=database_evidence("/run/postgresql"))
        else:
            runner.STATE.mkdir(parents=True, exist_ok=True)
            with (runner.STATE / "operation.lock").open("a") as lock:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                runner.configure()
                event.update(status="succeeded", result=drill(args, started))
    except Exception as error:
        event.update(status="failed", failureType=type(error).__name__,
                     reason=str(error) if isinstance(error, runner.ProtectionError) else "Acceptance operation failed")
    event["completed"] = dt.datetime.now(dt.timezone.utc).isoformat()
    if args.command == "drill":
        folder = runner.STATE / "acceptance"
        folder.mkdir(parents=True, exist_ok=True)
        runner.atomic_json(folder / (uuid.uuid4().hex + ".json"), event)
    print(json.dumps(event))
    return 0 if event["status"] == "succeeded" else 1


if __name__ == "__main__":
    sys.exit(main())
