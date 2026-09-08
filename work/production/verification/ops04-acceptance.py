#!/usr/bin/env python3
"""Dependency-free OPS-04 configuration and alert-delivery acceptance gate."""
from __future__ import annotations

import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import threading
import time
from urllib.request import Request, urlopen

try:
    import yaml
except ImportError as error:
    raise SystemExit("PyYAML is required by the repository validation runtime") from error

ROOT = Path(__file__).resolve().parents[1]
MONITORING = ROOT / "deployment" / "monitoring"

EXPECTED_ALERTS = {
    "TaskApiUnavailable", "TaskApiServerErrors", "TaskDatabaseUnavailable",
    "TaskDatabaseConnectionsHigh", "TaskHostDiskSpaceLow", "TaskHostDiskSpaceCritical",
    "TaskWorkerMetricsMissing", "TaskWorkerMetricsStale", "TaskWorkerPassStale",
    "TaskWorkerFailures", "TaskBackupMetricsMissing", "TaskBackupFailed", "TaskBackupOverdue",
    "TaskBackupCheckOverdue", "TaskBackupRestoreDrillOverdue", "TaskBackupCapacityLow",
    "TaskMonitoringTargetDown", "TaskAlertDeliveryFailure", "TaskAlertRouteNotProduction",
}
EXPECTED_JOBS = {
    "prometheus", "alertmanager", "blackbox-exporter", "task-api", "task-api-readiness",
    "postgres", "task-host", "task-worker", "task-backup", "alert-router",
}


class SinkHandler(BaseHTTPRequestHandler):
    requests: list[dict] = []

    def do_POST(self) -> None:
        length = int(self.headers.get("Content-Length", "0"))
        self.__class__.requests.append({
            "authorization": self.headers.get("Authorization"),
            "body": json.loads(self.rfile.read(length)),
        })
        self.send_response(204)
        self.end_headers()

    def log_message(self, _format: str, *_args: object) -> None:
        pass


def free_port() -> int:
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def wait_ready(url: str, process: subprocess.Popen, seconds: float = 10) -> None:
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise AssertionError(f"alert router exited early with code {process.returncode}")
        try:
            with urlopen(url, timeout=0.5) as response:
                if response.status == 200:
                    return
        except OSError:
            time.sleep(0.05)
    raise AssertionError("alert router did not become ready")


def validate_yaml_and_coverage() -> dict:
    documents = {}
    for name in (
        "compose.yaml", "compose.production.yaml", "compose.backup.yaml",
        "prometheus.yml", "alerts.yml", "alertmanager.yml", "blackbox.yml",
    ):
        documents[name] = yaml.safe_load((MONITORING / name).read_text(encoding="utf-8"))

    rules = [rule for group in documents["alerts.yml"]["groups"] for rule in group["rules"]]
    alerts = {rule["alert"] for rule in rules}
    assert alerts == EXPECTED_ALERTS, f"alert coverage mismatch: {sorted(alerts ^ EXPECTED_ALERTS)}"
    for rule in rules:
        assert rule.get("labels", {}).get("owner") == "operations"
        assert rule.get("labels", {}).get("severity") in {"warning", "critical"}
        assert str(rule.get("annotations", {}).get("runbook", "")).startswith("OPS-04#")

    jobs = {job["job_name"] for job in documents["prometheus.yml"]["scrape_configs"]}
    assert jobs == EXPECTED_JOBS, f"scrape coverage mismatch: {sorted(jobs ^ EXPECTED_JOBS)}"
    compose_text = (MONITORING / "compose.yaml").read_text(encoding="utf-8")
    assert "latest" not in compose_text.lower()
    assert "no-new-privileges:true" in compose_text
    assert "TASK_ALERT_OWNER" in compose_text and "TASK_SECRET_ROOT" in compose_text
    assert documents["compose.production.yaml"]["services"]["task-api"]["group_add"]
    assert documents["compose.production.yaml"]["volumes"]["task-worker-metrics"]["external"] is True
    assert documents["compose.backup.yaml"]["volumes"]["task-backup-metrics"]["external"] is True
    assert documents["alertmanager.yml"]["receivers"][0]["webhook_configs"][0]["send_resolved"] is True
    target_down = next(rule for rule in rules if rule["alert"] == "TaskMonitoringTargetDown")
    assert "alertmanager" in target_down["expr"] and "blackbox-exporter" in target_down["expr"]
    backup_capacity = next(rule for rule in rules if rule["alert"] == "TaskBackupCapacityLow")
    assert "task_backup_filesystem_size_bytes" in backup_capacity["expr"] and "== 0" in backup_capacity["expr"]
    return {"yaml_files": len(documents), "alerts": len(alerts), "scrape_jobs": len(jobs)}


def validate_alert_delivery() -> dict:
    SinkHandler.requests.clear()
    sink_port = free_port()
    router_port = free_port()
    sink = ThreadingHTTPServer(("127.0.0.1", sink_port), SinkHandler)
    sink_thread = threading.Thread(target=sink.serve_forever, daemon=True)
    sink_thread.start()

    with tempfile.TemporaryDirectory(prefix="task-ops04-") as directory:
        root = Path(directory)
        url_file = root / "url"
        token_file = root / "token"
        receipts = root / "receipts.jsonl"
        url_file.write_text(f"http://127.0.0.1:{sink_port}/customer-alerts\n", encoding="utf-8")
        token_file.write_text("acceptance-token-secret\n", encoding="utf-8")
        env = os.environ.copy()
        env.update({
            "TASK_ALERT_DELIVERY_MODE": "production",
            "TASK_ALERT_OWNER": "acceptance-operations",
            "TASK_ALERT_WEBHOOK_URL_FILE": str(url_file),
            "TASK_ALERT_WEBHOOK_TOKEN_FILE": str(token_file),
            "TASK_ALERT_RECEIPTS_PATH": str(receipts),
            "TASK_ALERT_ALLOW_HTTP_FOR_TEST": "true",
            "TASK_ALERT_ROUTER_PORT": str(router_port),
        })
        process = subprocess.Popen(
            [sys.executable, str(MONITORING / "alert_router.py")],
            env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
        )
        try:
            wait_ready(f"http://127.0.0.1:{router_port}/health/ready", process)
            payload = {
                "receiver": "task-operations",
                "status": "firing",
                "alerts": [{
                    "status": "firing",
                    "labels": {"alertname": "TaskApiUnavailable", "severity": "critical", "service": "api", "secret": "drop-me"},
                    "annotations": {"summary": "API unavailable", "description": "Synthetic acceptance failure", "password": "drop-me"},
                    "startsAt": "2026-09-08T00:00:00Z",
                }],
            }
            request = Request(
                f"http://127.0.0.1:{router_port}/alerts",
                data=json.dumps(payload).encode(),
                headers={"Content-Type": "application/json"}, method="POST",
            )
            with urlopen(request, timeout=5) as response:
                receipt = json.loads(response.read())
                assert response.status == 200 and receipt["delivered"] is True
            deadline = time.monotonic() + 3
            while not SinkHandler.requests and time.monotonic() < deadline:
                time.sleep(0.05)
            assert len(SinkHandler.requests) == 1
            forwarded = SinkHandler.requests[0]
            assert forwarded["authorization"] == "Bearer acceptance-token-secret"
            encoded = json.dumps(forwarded["body"])
            assert "drop-me" not in encoded and "password" not in encoded and "secret" not in encoded
            receipt_text = receipts.read_text(encoding="utf-8")
            assert "acceptance-token-secret" not in receipt_text and str(sink_port) not in receipt_text
            with urlopen(f"http://127.0.0.1:{router_port}/metrics", timeout=2) as response:
                metrics = response.read().decode()
            assert "task_alert_router_deliveries_total 1" in metrics
            assert "task_alert_router_delivery_failures_total 0" in metrics
            return {"forwarded": 1, "receipts": 1, "secrets_redacted": True, "resolved_enabled": True}
        finally:
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=3)
            sink.shutdown()
            sink.server_close()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output")
    args = parser.parse_args()
    result = {
        "task": "OPS-04",
        "result": "PASS",
        "configuration": validate_yaml_and_coverage(),
        "delivery": validate_alert_delivery(),
    }
    encoded = json.dumps(result, indent=2) + "\n"
    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(encoded, encoding="utf-8")
    print(encoded, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
