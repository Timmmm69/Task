"""Safety regressions for production recovery acceptance; no infrastructure attestation."""
import argparse
import contextlib
import datetime as dt
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, "/opt/task-backup")
import acceptance
import runner


class AcceptanceTests(unittest.TestCase):
    def setUp(self):
        self.now = dt.datetime.now(dt.timezone.utc)
        self.args = argparse.Namespace(target=(self.now - dt.timedelta(seconds=60)).isoformat(),
            incident_at=self.now.isoformat(), label="20260903-100000F", minimum_database_bytes=100,
            expected_schema_sha256="a" * 64, dataset_id="dataset-1", storage_copy_id="snapshot-1",
            escrow_copy_id="escrow-a", scope="fixture")

    def test_valid_rpo_boundary(self):
        self.args.target = (self.now - dt.timedelta(seconds=900)).isoformat()
        acceptance.validate_request(self.args, self.now)

    def test_invalid_acceptance_inputs(self):
        cases = [("target", (self.now - dt.timedelta(seconds=901)).isoformat()),
                 ("target", (self.now + dt.timedelta(seconds=1)).isoformat()),
                 ("incident_at", (self.now + dt.timedelta(seconds=1)).isoformat()),
                 ("target", "2026-09-03T10:00:00"),
                 ("target", "2026-09-03T10:00:00+03:00"),
                 ("minimum_database_bytes", 0), ("expected_schema_sha256", ""),
                 ("storage_copy_id", "../secret"), ("escrow_copy_id", "password value")]
        for name, value in cases:
            with self.subTest(name=name, value=value):
                args = argparse.Namespace(**vars(self.args))
                setattr(args, name, value)
                with self.assertRaises(runner.ProtectionError):
                    acceptance.validate_request(args, self.now)

    def test_expired_incident(self):
        self.args.incident_at = (self.now - dt.timedelta(hours=4)).isoformat()
        self.args.target = (self.now - dt.timedelta(hours=4, seconds=30)).isoformat()
        with self.assertRaises(runner.ProtectionError):
            acceptance.validate_request(self.args, self.now)

    def test_storage_bypass_rejected(self):
        with patch.dict(os.environ, TASK_BACKUP_VALIDATION="1"):
            with self.assertRaises(runner.ProtectionError):
                acceptance.isolation()

    def test_isolation_requires_readonly_and_no_primary(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            data, repo1, repo2 = [root / name for name in ("data", "repo1", "repo2")]
            for path in (data, repo1, repo2):
                path.mkdir()
            with patch.object(runner, "DATA", data), patch.object(runner, "REPOS", [repo1, repo2]), \
                 patch.object(runner, "validate_storage"), patch.object(runner, "run", return_value="rw,nosuid"), \
                 patch.dict(os.environ, TASK_BACKUP_VALIDATION="0"):
                with self.assertRaises(runner.ProtectionError):
                    acceptance.isolation()
                with patch.object(runner, "run", return_value="ro,nosuid"):
                    acceptance.isolation()
                    (repo1 / "backup.info").touch()
                    with self.assertRaises(runner.ProtectionError):
                        acceptance.isolation()
                    (repo1 / "backup.info").unlink()
                    (data / "PG_VERSION").write_text("16")
                    with self.assertRaises(runner.ProtectionError):
                        acceptance.isolation()

    def test_restore_policy_and_cleanup(self):
        selected = {"label": self.args.label, "timestamp": {"stop": self.now.timestamp() - 100},
                    "info": {"size": 1000}}
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            with patch.object(runner, "RESTORE", root), patch.object(acceptance, "isolation"), \
                 patch.object(runner, "catalog", return_value=[selected]), patch.object(runner, "run") as commands:
                for valid_schema in (True, False):
                    directory = root / "drill-123"
                    directory.mkdir()
                    (directory / "assets.tar").write_text("private recovery plaintext")
                    restored = {"data": str(directory / "data"), "socket": str(directory / "socket"),
                                "assets": {"sha256": "b" * 64}}
                    with patch.object(runner, "restore", return_value=restored), \
                         patch.object(acceptance, "database_evidence", return_value={
                             "schemaSha256": ("a" if valid_schema else "c") * 64}):
                        if valid_schema:
                            result = acceptance.drill(self.args, self.now)
                            self.assertFalse(result["productionAccepted"])
                            self.assertEqual(result["requestedLossWindowSeconds"], 60)
                            self.assertNotIn("socket", result)
                        else:
                            with self.assertRaises(runner.ProtectionError):
                                acceptance.drill(self.args, self.now)
                    self.assertFalse(directory.exists())
                self.assertEqual(commands.call_count, 2)
                self.args.minimum_database_bytes = 1001
                with patch.object(runner, "restore") as restore:
                    with self.assertRaises(runner.ProtectionError):
                        acceptance.drill(self.args, self.now)
                    restore.assert_not_called()

    def test_failed_backend_writes_only_failed_redacted_receipt(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(runner, "STATE", Path(folder)), \
             patch.object(runner, "configure"), patch.object(acceptance, "drill", side_effect=RuntimeError("secret-value")):
            argv = ["drill"]
            for name, value in vars(self.args).items():
                argv.extend(["--" + name.replace("_", "-"), str(value)])
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                self.assertEqual(acceptance.main(argv), 1)
            self.assertNotIn("secret-value", output.getvalue())
            receipt = json.loads(next((Path(folder) / "acceptance").glob("*.json")).read_text())
            self.assertEqual(receipt["status"], "failed")
            self.assertNotIn("result", receipt)

    def test_budget_allows_long_replay_and_is_shared(self):
        with patch.dict(os.environ, TASK_RESTORE_TIMEOUT_SECONDS="14400"), \
             patch.object(runner.time, "monotonic", side_effect=[0, 121, 3600, 14400]):
            remaining = runner.recovery_budget()
            self.assertEqual(remaining(), 14279)
            self.assertEqual(remaining(), 10800)
            with self.assertRaises(runner.ProtectionError):
                remaining()

    def test_budget_rejects_unbounded_values(self):
        for value in ("0", "14401", "-1", "NaN"):
            with self.subTest(value=value), patch.dict(os.environ, TASK_RESTORE_TIMEOUT_SECONDS=value):
                with self.assertRaises(ValueError):
                    runner.recovery_budget()

    def test_restore_passes_remaining_budget_to_postgres_start(self):
        # Simulate 240 seconds of physical restore; startup must still receive >120 seconds.
        def physical(*args, **kwargs):
            data = Path(next(arg.split("=", 1)[1] for arg in args if arg.startswith("--pg1-path=")))
            data.mkdir()
            (data / "postgresql.auto.conf").write_text("")

        def command(args, timeout=14400):
            if args[0] == "pg_controldata":
                return "\n".join(name + ": 100" for name in (
                    "max_connections setting", "max_worker_processes setting", "max_wal_senders setting",
                    "max_prepared_xacts setting", "max_locks_per_xact setting"))
            if args[0] == "psql":
                return "f" if "pg_is_in_recovery" in args[-1] else "task"
            return ""

        with tempfile.TemporaryDirectory() as folder, patch.object(runner, "RESTORE", Path(folder)), \
             patch.object(runner, "verify_assets", return_value={}), \
             patch.object(runner, "backrest", side_effect=physical), \
             patch.object(runner, "recovery_budget", return_value=lambda: 14160), \
             patch.object(runner, "run", side_effect=command) as run:
            runner.restore(2, self.args.label, keep=True)
            start = next(call for call in run.call_args_list if call.args[0][0] == "pg_ctl")
            self.assertEqual(start.args[0][start.args[0].index("-t") + 1], "14160")
            self.assertEqual(start.args[1], 14160)


if __name__ == "__main__":
    unittest.main(verbosity=2)
