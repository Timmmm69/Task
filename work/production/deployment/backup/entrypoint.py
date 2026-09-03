#!/usr/bin/python3
"""Privileged backup operator image. No Docker socket or application credentials."""
import os
import sys
from pathlib import Path
import runner

os.umask(0o077)
runner.configure()
mode = sys.argv[1] if len(sys.argv) > 1 else "agent"
if mode == "database":
    data = runner.DATA
    if not (data / "PG_VERSION").exists():
        runner.run(["initdb", "-D", str(data), "--data-checksums", "--auth-local=peer",
                    "--auth-host=scram-sha-256", "--pwfile=/run/secrets/postgres-password"])
        with (data / "pg_ident.conf").open("a") as file:
            file.write("\ntask_backup postgres task_backup\n")
        with (data / "pg_hba.conf").open("a") as file:
            file.write("\nlocal all task_backup peer map=task_backup\n")
        # Put the mapped identity before the generic peer rule.
        hba = data / "pg_hba.conf"
        hba.write_text("local all task_backup peer map=task_backup\n" + hba.read_text())
        runner.run(["pg_ctl", "-D", str(data), "-l", "/tmp/task-init.log", "-o", "-c listen_addresses=''", "-w", "start"])
        try:
            runner.run(["psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", "postgres", "-d", "postgres",
                        "-f", "/opt/task-backup/initialize.sql"])
        finally:
            runner.run(["pg_ctl", "-D", str(data), "-m", "fast", "-w", "stop"])
    os.execvp("postgres", ["postgres", "-D", str(data), "-c", "listen_addresses=*",
              "-c", "archive_mode=on", "-c", "archive_timeout=60s",
              "-c", "archive_command=pgbackrest --config=/run/task-backup/pgbackrest.conf --stanza=task archive-push %p"])
elif mode == "agent":
    os.environ["Backup__Enabled"] = "true"
    os.execvp("dotnet", ["dotnet", "/app/Task.BackupAgent.dll"])
elif mode == "run":
    sys.exit(runner.main(sys.argv[2:]))
elif mode == "operator":
    os.execvp("sleep", ["sleep", "infinity"])
else:
    raise SystemExit("Allowed modes: database, agent, run, operator")
