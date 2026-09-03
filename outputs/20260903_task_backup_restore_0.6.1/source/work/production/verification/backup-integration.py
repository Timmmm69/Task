"""Destructive tests ONLY inside Test-BackupRestore.ps1's disposable networkless fixture."""
import datetime as dt
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
from unittest.mock import patch

sys.path.insert(0, "/opt/task-backup")
import runner
import acceptance

assert os.environ.get("TASK_BACKUP_VALIDATION") == "1"
evidence = {"checks": [], "offHostPhysicalDurability": "not-tested-local-docker-fixture"}


def ok(name):
    evidence["checks"].append(name)
    print("PASS " + name, flush=True)


def diagnostic_run(args, timeout=14400):
    result = subprocess.run(args, capture_output=True, timeout=timeout)
    if result.returncode:
        # This fixture contains generated test data only; never use this wrapper in production.
        log = Path(args[args.index("-l") + 1]).read_text() if args[0] == "pg_ctl" and "-l" in args else ""
        raise RuntimeError(result.stderr.decode() + result.stdout.decode() + log)
    return result.stdout.decode()


runner.run = diagnostic_run


def sql(command, socket="/run/postgresql", database="task"):
    return runner.run(["psql", "-X", "-h", socket, "-U", "postgres", "-d", database,
                       "-At", "-v", "ON_ERROR_STOP=1", "-c", command], 30).strip()


def must_fail(action):
    try:
        action()
    except Exception:
        return
    raise AssertionError("Unsafe operation unexpectedly succeeded")


runner.configure()
runner.validate_storage()
sql("CREATE DATABASE task", database="postgres")
for migration in sorted(Path("/test-migrations").glob("*.sql")):
    runner.run(["psql", "-X", "-U", "postgres", "-d", "task", "-v", "ON_ERROR_STOP=1", "-f", str(migration)], 60)
sql("CREATE TABLE public.recovery_probe(id integer PRIMARY KEY, value text NOT NULL); INSERT INTO recovery_probe VALUES(1, 'base')")
schema = sql("SELECT md5(string_agg(table_schema || '.' || table_name, ',' ORDER BY table_schema,table_name)) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema')")
evidence["acceptanceBaseline"] = acceptance.database_evidence("/run/postgresql")
backup = runner.backup()
evidence["backup"] = backup
assert len(backup["copies"]) == 2
assert all(copy["databaseBytes"] > 0 and copy["repositoryBytes"] > 0 for copy in backup["copies"])
ok("encrypted full backup and actual isolated restore from both repositories")
labels = [copy["label"] for copy in backup["copies"]]
assert sql("SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolbypassrls FROM pg_roles WHERE rolname='task_backup'", database="postgres") == "f"
ok("dedicated backup identity has no superuser or DDL role attributes")

sql("INSERT INTO recovery_probe VALUES(2, 'committed-after-base')")
time.sleep(1.1)
target = sql("SELECT to_char(clock_timestamp() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"')")
time.sleep(1.1)
incident = sql("SELECT to_char(clock_timestamp() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"')")
sql("DELETE FROM recovery_probe; INSERT INTO recovery_probe VALUES(3, 'after-incident')")
runner.check()
restored = runner.restore(2, labels[1], target, keep=True)
try:
    socket = restored["socket"]
    assert sql("SELECT string_agg(id::text, ',' ORDER BY id) FROM recovery_probe", socket) == "1,2"
    assert sql("SELECT string_agg(id::text, ',' ORDER BY id) FROM recovery_probe") == "3"
    restored_schema = sql("SELECT md5(string_agg(table_schema || '.' || table_name, ',' ORDER BY table_schema,table_name)) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema')", socket)
    assert restored_schema == schema
    assert sql("SHOW listen_addresses", socket) == ""
    assert sql("SHOW archive_mode", socket) == "off"
    assert Path(restored["assets"]["archive"]).is_file()
    evidence["pitr"] = {"target": target, "incident": incident, "restoredIds": [1, 2], "productionIds": [3],
                        "schemaFingerprint": schema, "elapsedSeconds": restored["elapsedSeconds"]}
    ok("PITR recovers post-backup commits before deletion; production and schema preserved")
finally:
    runner.run(["pg_ctl", "-D", restored["data"], "-m", "fast", "-w", "stop"])
    shutil.rmtree(Path(restored["data"]).parent)

assert len(runner.verify()["copies"]) == 2
ok("scheduled WAL restore point is replayed in both repositories")

bundle = runner.REPOS[1] / "task-assets" / (labels[1] + ".gcm")
original = bundle.read_bytes()
try:
    corrupted = bytearray(original)
    corrupted[-20] ^= 1
    bundle.write_bytes(corrupted)
    must_fail(lambda: runner.restore(2, labels[1], target, keep=True))
    assert not list(runner.RESTORE.glob("drill-*"))
finally:
    bundle.write_bytes(original)
ok("tampered encrypted assets rejected and failed restore plaintext cleaned")

with tempfile.TemporaryDirectory(dir=runner.RESTORE) as temp:
    with patch.object(runner, "key", return_value="00" * 32):
        must_fail(lambda: runner.unseal(bundle, Path(temp) / "unsealed.tar"))
        assert not (Path(temp) / "unsealed.tar").exists()
ok("missing/wrong recovery key cannot produce plaintext")

files = list((runner.REPOS[1] / "backup" / "task" / labels[1] / "pg_data" / "base").rglob("*.gz"))
assert files
damaged = files[0]
original = damaged.read_bytes()
try:
    damaged.write_bytes(b"corrupted database backup")
    must_fail(lambda: runner.restore(2, labels[1]))
finally:
    damaged.write_bytes(original)
ok("corrupt physical database copy rejected by restore checksum/decryption")

commands = []
original_backrest = runner.backrest


def reject_secondary(*args):
    commands.append(args)
    if "backup" in args and "--repo=2" in args:
        raise OSError("simulated off-host outage")
    return original_backrest(*args)


with patch.object(runner, "backrest", side_effect=reject_secondary):
    must_fail(runner.backup)
assert not any("expire" in command for command in commands)
assert bundle.is_file()
ok("off-host failure never expires the last verified recovery set")

with patch.dict(os.environ, {"TASK_BACKUP_VALIDATION": "0"}):
    must_fail(runner.validate_storage)
ok("production configuration rejects local-only secondary storage")

for setting, value in (("TASK_LOCAL_RETENTION_DAYS", "13"), ("TASK_OFFHOST_RETENTION_DAYS", "365")):
    with patch.dict(os.environ, {setting: value}):
        must_fail(runner.configure)
with tempfile.TemporaryDirectory(dir=runner.RESTORE) as temp:
    secrets = Path(temp)
    (secrets / "repo1-key").write_text("ab" * 32)
    (secrets / "repo2-key").write_text("AB" * 32)
    (secrets / "assets-key").write_text("cd" * 32)
    with patch.object(runner, "SECRETS", secrets):
        must_fail(runner.configure)
        (secrets / "repo2-key").unlink()
        must_fail(runner.configure)
runner.configure()
ok("retention floors, duplicate key bytes and missing escrow keys are rejected")

import fcntl
with (runner.STATE / "operation.lock").open("a") as lock:
    fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    assert runner.main(["backup"]) == 75
ok("overlapping operator runs cannot execute a second backup")

for invalid in ("../production", "20200101;rm -rf", "/var/lib/postgresql/data"):
    must_fail(lambda: runner.checked_label(invalid))
assert runner.main(["plan", "--label", labels[1], "--target", "2099-01-01T00:00:00Z"]) == 1
assert runner.main(["plan", "--label", labels[1], "--target", target]) == 0
ok("restore plan requires a valid label and explicit past UTC target")

evidence["versions"] = {"postgres": runner.run(["postgres", "--version"]).strip(),
                        "pgbackrest": runner.run(["pgbackrest", "version"]).strip()}

# Exercise the actual production .NET hosted service, persistence and clean shutdown.
env = dict(os.environ, Backup__Enabled="true", Backup__CheckIntervalSeconds="2")
agent = subprocess.Popen(["dotnet", "/app/Task.BackupAgent.dll"], env=env,
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
try:
    deadline = time.monotonic() + 300
    while time.monotonic() < deadline:
        status = runner.STATE / "status.json"
        if status.exists():
            state = json.loads(status.read_text())
            if state.get("LastRestoreTest") and state.get("LastCheck") and not state.get("FailedOperation"):
                break
        if agent.poll() is not None:
            raise AssertionError("Hosted backup agent exited unexpectedly")
        time.sleep(1)
    else:
        raise AssertionError("Hosted backup agent did not complete backup and WAL verification")
    assert runner.health() == 0
    evidence["scheduler"] = state
finally:
    agent.terminate()
    assert agent.wait(timeout=10) == 0
ok("real hosted agent schedules backup and PITR verification, reports health and stops cleanly")

for changes in ({"FailedOperation": "backup"}, {"LastCheck": "2000-01-01T00:00:00+00:00"},
                {"LastBackup": "2099-01-01T00:00:00+00:00"}):
    runner.atomic_json(runner.STATE / "status.json", {**state, **changes})
    assert runner.health() == 1
runner.atomic_json(runner.STATE / "status.json", state)
ok("health rejects failed, stale and future-dated protection status")

before = runner.catalog(1)
assert len(before) >= 3
runner.backrest("--repo=1", "--repo1-retention-full-type=time", "--repo1-retention-full=14", "expire")
assert [item["label"] for item in runner.catalog(1)] == [item["label"] for item in before]
ok("time retention preserves every backup inside the minimum recovery window")
runner.backrest("--repo=1", "--repo1-retention-full-type=count", "--repo1-retention-full=2", "expire")
after = runner.catalog(1)
assert len(after) == 2 and after[-1]["label"] == before[-1]["label"]
runner.restore(1, after[-1]["label"])
ok("accelerated retention removes old sets and leaves newest backup plus WAL restorable")

runner.atomic_json(runner.STATE / "integration-evidence.json", evidence)
print("BACKUP_INTEGRATION_PASSED", flush=True)
