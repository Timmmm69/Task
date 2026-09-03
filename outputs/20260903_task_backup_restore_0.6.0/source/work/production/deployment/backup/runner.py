#!/usr/bin/python3
"""Fixed operations for PostgreSQL 16/pgBackRest. Never accepts OS commands from API input."""
import argparse
import datetime as dt
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

DATA = Path("/var/lib/postgresql/data")
REPOS = [Path("/backup/local"), Path("/backup/offhost")]
STATE = Path("/var/lib/task-backup")
RESTORE = Path("/restore")
ASSETS = Path("/recovery-input")
CONFIG = Path("/run/task-backup/pgbackrest.conf")
SECRETS = Path("/run/secrets")
LABEL = re.compile(r"\d{8}-\d{6}F(?:_\d{8}-\d{6}[DI])?\Z")
ASSET_CLASSES = ("configuration", "keys", "certificates", "assets", "installers", "migrations")
MAGIC = b"TASKRECOVERY1"


class ProtectionError(ValueError):
    """A safe, fixed operator-facing validation reason."""


def run(args, timeout=14400):
    # Errors deliberately exclude arguments/output. Operator journal has safe classifications only.
    result = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout)
    if result.returncode:
        raise RuntimeError(f"{Path(args[0]).name} failed: exit {result.returncode}")
    return result.stdout.decode()


def atomic_json(path, value):
    temp = path.with_suffix(path.suffix + ".tmp")
    with temp.open("w") as file:
        json.dump(value, file, indent=2)
        file.flush()
        os.fsync(file.fileno())
    os.replace(temp, path)


def key(name):
    value = (SECRETS / name).read_bytes().strip()
    if not re.fullmatch(rb"[a-fA-F0-9]{64}", value):
        raise ProtectionError("Recovery keys must be independent 32-byte random hex secrets")
    return value.decode().lower()


def configure():
    os.umask(0o077)
    keys = [key("repo1-key"), key("repo2-key"), key("assets-key")]
    if len(set(keys)) != 3:
        raise ProtectionError("Recovery keys must be independent")
    retention = [int(os.environ.get("TASK_LOCAL_RETENTION_DAYS", "30")),
                 int(os.environ.get("TASK_OFFHOST_RETENTION_DAYS", "366"))]
    if retention[0] < 14 or retention[1] < 366 or max(retention) > 3660:
        raise ProtectionError("Retention below the recovery policy floor")
    CONFIG.parent.mkdir(parents=True, exist_ok=True)
    config = ["[global]", "log-level-console=error", "log-level-file=off", "start-fast=y",
              "archive-async=n", "archive-timeout=120", "lock-path=/run/task-backup/locks",
              "spool-path=/run/task-backup/spool",
              "process-max=2", "expire-auto=n", "[task]", f"pg1-path={DATA}",
              "pg1-socket-path=/run/postgresql", "pg1-user=task_backup"]
    for number, repo in enumerate(REPOS, 1):
        config.extend([f"repo{number}-path={repo}", f"repo{number}-cipher-type=aes-256-cbc",
                       f"repo{number}-cipher-pass={keys[number - 1]}",
                       f"repo{number}-retention-full-type=time",
                       f"repo{number}-retention-full={retention[number - 1]}"])
    CONFIG.write_text("\n".join(config) + "\n")
    CONFIG.chmod(0o600)


def backrest(*args):
    return run(["pgbackrest", f"--config={CONFIG}", "--stanza=task", *args])


def validate_storage(restore_repo=None):
    paths = [REPOS[restore_repo - 1], STATE, RESTORE] if restore_repo else [DATA, *REPOS, STATE, RESTORE, ASSETS]
    for path in paths:
        if not path.is_dir() or path.is_symlink() or path.resolve() != path:
            raise ProtectionError("Backup paths must be existing real directories")
    # The integration fixture is explicitly labelled and cannot attest off-host durability.
    if not restore_repo and os.environ.get("TASK_BACKUP_VALIDATION") != "1":
        filesystem = run(["findmnt", "-n", "-T", str(REPOS[1]), "-o", "FSTYPE"], 10).strip()
        if filesystem not in ("nfs", "nfs4", "cifs"):
            raise ProtectionError("Secondary repository must be an off-host NFS/SMB mount")
        if DATA.stat().st_dev == REPOS[0].stat().st_dev:
            raise ProtectionError("Local repository must use separate storage from PostgreSQL")


def asset_inventory():
    for name in ASSET_CLASSES:
        directory = ASSETS / name
        if not directory.is_dir() or not any(directory.iterdir()):
            raise ProtectionError(f"Recovery input category missing: {name}")
    records = []
    for path in sorted(ASSETS.rglob("*")):
        if path.relative_to(ASSETS).parts[0] not in ASSET_CLASSES:
            raise ProtectionError("Unexpected recovery input category")
        if path.is_symlink() or not (path.is_file() or path.is_dir()):
            raise ProtectionError("Recovery assets cannot contain links or special files")
        if path.is_file():
            records.append({"path": path.relative_to(ASSETS).as_posix(), "bytes": path.stat().st_size,
                            "sha256": digest(path)})
    return records


def digest(path):
    with path.open("rb") as file:
        return hashlib.file_digest(file, "sha256").hexdigest()


def seal(source, destination):
    iv = os.urandom(12)
    encryptor = Cipher(algorithms.AES(bytes.fromhex(key("assets-key"))), modes.GCM(iv)).encryptor()
    encryptor.authenticate_additional_data(MAGIC)
    with source.open("rb") as plain, destination.open("wb") as encrypted:
        encrypted.write(MAGIC + iv)
        while chunk := plain.read(1024 * 1024):
            encrypted.write(encryptor.update(chunk))
        encrypted.write(encryptor.finalize() + encryptor.tag)
        encrypted.flush()
        os.fsync(encrypted.fileno())


def unseal(source, destination):
    with source.open("rb") as encrypted:
        if encrypted.read(len(MAGIC)) != MAGIC:
            raise ProtectionError("Unknown recovery envelope")
        iv = encrypted.read(12)
        encrypted.seek(-16, 2)
        tag = encrypted.read(16)
        remaining = encrypted.tell() - len(MAGIC) - 12 - 16
        encrypted.seek(len(MAGIC) + 12)
        decryptor = Cipher(algorithms.AES(bytes.fromhex(key("assets-key"))), modes.GCM(iv, tag)).decryptor()
        decryptor.authenticate_additional_data(MAGIC)
        try:
            with destination.open("xb") as plain:
                while remaining > 0:
                    chunk = encrypted.read(min(1024 * 1024, remaining))
                    if not chunk:
                        raise ProtectionError("Truncated recovery envelope")
                    remaining -= len(chunk)
                    plain.write(decryptor.update(chunk))
                plain.write(decryptor.finalize())
        except BaseException:
            destination.unlink(missing_ok=True)
            raise


def catalog(repo):
    info = json.loads(backrest(f"--repo={repo}", "--output=json", "info"))
    if len(info) != 1 or info[0]["status"]["code"] != 0:
        raise ProtectionError("Repository catalog is unavailable")
    backups = sorted(info[0]["backup"], key=lambda item: item["timestamp"]["stop"])
    if not backups:
        raise ProtectionError("Repository has no completed backup")
    for item in backups:
        checked_label(item["label"])
        if item["info"]["size"] <= 0 or item.get("error"):
            raise ProtectionError("Repository contains an invalid backup")
    return backups


def checked_label(label):
    if not LABEL.fullmatch(label):
        raise ProtectionError("Invalid backup label")
    return label


def verify_assets(repo, label, scratch, preserve=False):
    bundle = REPOS[repo - 1] / "task-assets" / (checked_label(label) + ".gcm")
    plain = scratch / "assets.tar"
    unseal(bundle, plain)
    # Authentication is complete before interpreting archive paths or content.
    with tarfile.open(plain) as archive:
        manifest = json.load(archive.extractfile("manifest.json"))
        members = archive.getmembers()
        expected = {item["path"]: item for item in manifest}
        if len(members) != len(expected) + 1:
            raise ProtectionError("Recovery manifest membership mismatch")
        for member in members:
            if member.name == "manifest.json":
                continue
            if not member.isfile() or member.name not in expected or Path(member.name).is_absolute() or ".." in Path(member.name).parts:
                raise ProtectionError("Unexpected recovery asset")
            record = expected[member.name]
            if member.size != record["bytes"] or hashlib.file_digest(archive.extractfile(member), "sha256").hexdigest() != record["sha256"]:
                raise ProtectionError("Recovery asset checksum mismatch")
    if not preserve:
        plain.unlink()
    return {"files": len(manifest), "sha256": digest(bundle), "archive": str(plain) if preserve else None}


def restore(repo, label, target=None, keep=False, target_type="time"):
    checked_label(label)
    directory = Path(tempfile.mkdtemp(prefix="drill-", dir=RESTORE))
    data = directory / "data"
    socket = directory / "socket"
    socket.mkdir()
    started = time.monotonic()
    succeeded = False
    try:
        assets = verify_assets(repo, label, directory, preserve=keep)
        args = [f"--repo={repo}", f"--set={label}", f"--pg1-path={data}",
                f"--tablespace-map-all={directory / 'tablespaces'}", "--archive-mode=off",
                "--target-action=promote", f"--type={target_type}" if target else "--type=immediate"]
        if target:
            recovery_target = target
            if target_type == "time":
                recovery_target = dt.datetime.fromisoformat(target.replace("Z", "+00:00")).strftime("%Y-%m-%d %H:%M:%S.%f+00")
            args.append(f"--target={recovery_target}")
        backrest(*args, "restore")
        # Never trust backed-up OS commands, preload libraries, HBA or listener configuration.
        recovery = (data / "postgresql.auto.conf").read_text()
        recovery = "\n".join(line for line in recovery.splitlines()
                             if line.startswith(("restore_command", "recovery_target")))
        (data / "postgresql.auto.conf").write_text(recovery + "\n")
        control = run(["pg_controldata", str(data)], 30)
        required = {"max_connections setting": "max_connections",
                    "max_worker_processes setting": "max_worker_processes",
                    "max_wal_senders setting": "max_wal_senders",
                    "max_prepared_xacts setting": "max_prepared_transactions",
                    "max_locks_per_xact setting": "max_locks_per_transaction"}
        settings = []
        for field, setting in required.items():
            match = re.search(r"^" + re.escape(field) + r":\s+(\d+)\s*$", control, re.MULTILINE)
            if not match:
                raise ProtectionError("Unrecognized PostgreSQL recovery control settings")
            settings.append(f"{setting}={match.group(1)}")
        (data / "postgresql.conf").write_text(
            "listen_addresses=''\narchive_mode=off\nshared_preload_libraries=''\n"
            "ssl=off\n" + "\n".join(settings) + f"\nunix_socket_directories='{socket}'\n")
        (data / "pg_hba.conf").write_text("local all postgres peer\n")
        run(["pg_ctl", "-D", str(data), "-l", str(directory / "postgres.log"), "-w", "-t", "120", "start"], 130)
        sql = ["psql", "-X", "-h", str(socket), "-U", "postgres", "-d", "postgres", "-At", "-v", "ON_ERROR_STOP=1"]
        deadline = time.monotonic() + 120
        while run([*sql, "-c", "SELECT pg_is_in_recovery()"], 30).strip() != "f":
            if time.monotonic() >= deadline:
                raise ProtectionError("Recovery target was not reached")
            time.sleep(0.2)
        run(["pg_amcheck", "--all", "--install-missing", "--host", str(socket), "--username", "postgres"], 3600)
        databases = run([*sql, "-c", "SELECT datname FROM pg_database WHERE datallowconn AND NOT datistemplate ORDER BY datname"], 30).splitlines()
        result = {"repo": repo, "label": label, "target": target, "assets": assets,
                  "databases": databases, "elapsedSeconds": round(time.monotonic() - started, 3),
                  "data": str(data), "socket": str(socket)}
        if not keep:
            run(["pg_ctl", "-D", str(data), "-m", "fast", "-w", "stop"], 60)
            run(["pg_checksums", "--check", "-D", str(data)], 3600)
        succeeded = True
        return result
    finally:
        if not keep or not succeeded:
            (directory / "assets.tar").unlink(missing_ok=True)
            if (data / "postmaster.pid").exists():
                run(["pg_ctl", "-D", str(data), "-m", "immediate", "-w", "stop"], 60)
            shutil.rmtree(directory)


def backup():
    records = asset_inventory()
    backrest("stanza-create")
    backrest("check")
    with tempfile.TemporaryDirectory(prefix="bundle-", dir=RESTORE) as folder:
        scratch = Path(folder)
        plain = scratch / "assets.tar"
        with tarfile.open(plain, "w") as archive:
            import io
            content = json.dumps(records).encode()
            header = tarfile.TarInfo("manifest.json")
            header.size = len(content)
            archive.addfile(header, io.BytesIO(content))
            for record in records:
                archive.add(ASSETS / record["path"], arcname=record["path"], recursive=False)
        if records != asset_inventory():
            raise ProtectionError("Recovery assets changed during capture; retry with stable inputs")
        envelope = scratch / "assets.gcm"
        seal(plain, envelope)
        plain.unlink()
        results = []
        for repo in (1, 2):
            backrest(f"--repo={repo}", "--type=full", "--no-expire-auto", "backup")
            info = catalog(repo)[-1]
            label = checked_label(info["label"])
            if info["info"]["size"] <= 0 or info.get("error"):
                raise ProtectionError("Backup size or page checksum validation failed")
            destination = REPOS[repo - 1] / "task-assets"
            destination.mkdir(exist_ok=True)
            with envelope.open("rb") as source, (destination / (label + ".gcm")).open("xb") as target:
                shutil.copyfileobj(source, target)
                target.flush()
                os.fsync(target.fileno())
            verified = restore(repo, label)
            verified.update({"databaseBytes": info["info"]["size"],
                             "repositoryBytes": info["info"]["repository"]["size"],
                             "backupCompleted": info["timestamp"]["stop"]})
            results.append(verified)
        # Both copies and both actual restores succeeded. Only now may retention remove older sets.
        for repo in (1, 2):
            backrest(f"--repo={repo}", "expire")
            live = {item["label"] for item in catalog(repo)}
            for path in (REPOS[repo - 1] / "task-assets").glob("*.gcm"):
                if LABEL.fullmatch(path.stem) and path.stem not in live:
                    path.unlink()
        atomic_json(STATE / "assets-inventory.json", records)
        return {"copies": results}


def check():
    inventory = STATE / "assets-inventory.json"
    if not inventory.exists() or json.loads(inventory.read_text()) != asset_inventory():
        return backup()
    backrest("check")  # Forces a WAL switch and verifies receipt by every configured repository.
    records = []
    for repo in (1, 2):
        latest = catalog(repo)[-1]
        if time.time() - latest["timestamp"]["stop"] > 26 * 3600:
            raise ProtectionError("Latest backup is overdue")
        if not (REPOS[repo - 1] / "task-assets" / (latest["label"] + ".gcm")).is_file():
            raise ProtectionError("Recovery assets are missing")
        records.append({"repo": repo, "latest": latest["label"]})
    return {"repositories": records}


def verify():
    name = "task_verify_" + dt.datetime.now(dt.timezone.utc).strftime("%Y%m%d%H%M%S%f")
    run(["psql", "-X", "-U", "task_backup", "-d", "postgres", "-v", "ON_ERROR_STOP=1",
         "-c", f"SELECT pg_create_restore_point('{name}')"], 30)
    backrest("check")
    return {"copies": [restore(repo, catalog(repo)[-1]["label"], name, target_type="name") for repo in (1, 2)]}


def health():
    try:
        state = json.loads((STATE / "status.json").read_text())
        now = dt.datetime.now(dt.timezone.utc)
        limits = {"LastBackup": 26 * 3600, "LastCheck": 600, "LastRestoreTest": 8 * 86400}
        for field, limit in limits.items():
            timestamp = dt.datetime.fromisoformat(state[field])
            age = (now - timestamp).total_seconds()
            if age < 0 or age > limit:
                raise ProtectionError("Backup protection is overdue")
        if state["FailedOperation"]:
            raise ProtectionError("Backup protection failed")
        print(json.dumps({"status": "healthy", **state}))
        return 0
    except Exception:
        print(json.dumps({"status": "unhealthy", "code": "BackupProtectionAtRisk"}))
        return 1


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument("operation", choices=["backup", "check", "verify", "restore", "plan", "health"])
    parser.add_argument("--repo", type=int, choices=[1, 2], default=2)
    parser.add_argument("--label")
    parser.add_argument("--target")
    args = parser.parse_args(argv)
    if args.operation == "health":
        return health()
    os.umask(0o077)
    started = dt.datetime.now(dt.timezone.utc)
    STATE.mkdir(parents=True, exist_ok=True)
    with (STATE / "operation.lock").open("a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return 75
        try:
            configure()
            validate_storage(args.repo if args.operation in ("restore", "plan") else None)
            if args.operation in ("restore", "plan"):
                if not args.label or not args.target:
                    raise ProtectionError("Restore requires an explicit backup label and UTC target time")
                checked_label(args.label)
                target = dt.datetime.fromisoformat(args.target.replace("Z", "+00:00"))
                if target.tzinfo is None or target.utcoffset() != dt.timedelta(0) or target > started:
                    raise ProtectionError("Recovery target must be a past UTC time")
                selected = next(item for item in catalog(args.repo) if item["label"] == args.label)
                if target.timestamp() <= selected["timestamp"]["stop"]:
                    raise ProtectionError("Recovery target must follow completion of the selected backup")
                result = {"repo": args.repo, "label": args.label, "target": args.target,
                          "productionOverwrite": False, "destinationRoot": str(RESTORE)}
                if args.operation == "restore":
                    result = restore(args.repo, args.label, args.target, keep=True)
            else:
                result = {"backup": backup, "check": check, "verify": verify}[args.operation]()
            event = {"status": "succeeded", "operation": args.operation, "started": started.isoformat(),
                     "completed": dt.datetime.now(dt.timezone.utc).isoformat(), "result": result}
            code = 0
        except Exception as error:
            event = {"status": "failed", "operation": args.operation, "started": started.isoformat(),
                     "failureType": type(error).__name__, "reason": str(error) if isinstance(error, ProtectionError) else "Backend operation failed"}
            code = 1
        journal = STATE / "journal"
        journal.mkdir(exist_ok=True)
        atomic_json(journal / (started.strftime("%Y%m%dT%H%M%S%f") + ".json"), event)
        atomic_json(STATE / "last-operation.json", event)
        print(json.dumps(event))
        return code


if __name__ == "__main__":
    sys.exit(main())
