#!/usr/bin/env python3
"""Bounded Alertmanager webhook router with secret-safe delivery receipts."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit
from urllib.request import Request, urlopen

MAX_BODY = 1024 * 1024
ALLOWED_LABELS = {"alertname", "category", "instance", "job", "owner", "service", "severity"}
ALLOWED_ANNOTATIONS = {"description", "runbook", "summary"}


def _read_optional(path: str | None) -> str | None:
    if not path:
        return None
    file = Path(path)
    if not file.is_file():
        return None
    value = file.read_text(encoding="utf-8").strip()
    return value or None


def _bounded_text(value: object, limit: int = 512) -> str:
    text = str(value).replace("\r", " ").replace("\n", " ")
    return text[:limit]


def sanitize(payload: dict, owner: str) -> dict:
    alerts = []
    for alert in payload.get("alerts", [])[:100]:
        labels = alert.get("labels", {}) if isinstance(alert, dict) else {}
        annotations = alert.get("annotations", {}) if isinstance(alert, dict) else {}
        alerts.append({
            "status": "resolved" if alert.get("status") == "resolved" else "firing",
            "labels": {key: _bounded_text(value) for key, value in labels.items() if key in ALLOWED_LABELS},
            "annotations": {key: _bounded_text(value) for key, value in annotations.items() if key in ALLOWED_ANNOTATIONS},
            "startsAt": _bounded_text(alert.get("startsAt", ""), 64),
            "endsAt": _bounded_text(alert.get("endsAt", ""), 64),
        })
    return {
        "version": "1",
        "status": "resolved" if payload.get("status") == "resolved" else "firing",
        "receiver": _bounded_text(payload.get("receiver", "task-operations"), 128),
        "owner": _bounded_text(owner, 128),
        "alerts": alerts,
    }


class RouterState:
    def __init__(self) -> None:
        self.mode = os.environ.get("TASK_ALERT_DELIVERY_MODE", "test").strip().lower()
        if self.mode not in {"test", "production"}:
            raise ValueError("TASK_ALERT_DELIVERY_MODE must be test or production")
        self.owner = os.environ.get("TASK_ALERT_OWNER", "").strip()
        if not self.owner:
            raise ValueError("TASK_ALERT_OWNER is required")
        self.url = _read_optional(os.environ.get("TASK_ALERT_WEBHOOK_URL_FILE"))
        self.token = _read_optional(os.environ.get("TASK_ALERT_WEBHOOK_TOKEN_FILE"))
        self.receipts = Path(os.environ.get("TASK_ALERT_RECEIPTS_PATH", "/var/lib/task-alert-router/receipts.jsonl"))
        self.receipts.parent.mkdir(parents=True, exist_ok=True)
        self.lock = threading.Lock()
        self.deliveries = 0
        self.failures = 0
        if self.mode == "production":
            if self.owner == "replace-with-company-operations-owner":
                raise ValueError("Production alert delivery requires the real company operations owner")
            self._validate_production_target()

    def _validate_production_target(self) -> None:
        if not self.url:
            raise ValueError("Production alert delivery requires TASK_ALERT_WEBHOOK_URL_FILE")
        parsed = urlsplit(self.url)
        allow_http = os.environ.get("TASK_ALERT_ALLOW_HTTP_FOR_TEST") == "true"
        if parsed.scheme not in ({"https", "http"} if allow_http else {"https"}) or not parsed.hostname:
            raise ValueError("Production alert webhook must be an HTTPS URL")
        if parsed.username or parsed.password:
            raise ValueError("Credentials are forbidden in the alert webhook URL")

    def deliver(self, payload: dict) -> tuple[bool, str]:
        safe = sanitize(payload, self.owner)
        encoded = json.dumps(safe, separators=(",", ":"), ensure_ascii=True).encode("utf-8")
        fingerprint = hashlib.sha256(encoded).hexdigest()
        delivered = self.mode == "test"
        reason = "local-test-sink"
        if self.mode == "production":
            try:
                headers = {"Content-Type": "application/json", "User-Agent": "Task-Alert-Router/1.0"}
                if self.token:
                    headers["Authorization"] = f"Bearer {self.token}"
                request = Request(self.url, data=encoded, headers=headers, method="POST")
                with urlopen(request, timeout=10) as response:
                    delivered = 200 <= response.status < 300
                    reason = f"http-{response.status}"
            except Exception as error:  # fixed classification only; never persist URL/body/token
                delivered = False
                reason = type(error).__name__

        receipt = {
            "delivered": delivered,
            "mode": self.mode,
            "owner": self.owner,
            "status": safe["status"],
            "alertCount": len(safe["alerts"]),
            "fingerprint": fingerprint,
            "result": reason,
        }
        line = json.dumps(receipt, separators=(",", ":"), ensure_ascii=True)
        with self.lock:
            self.deliveries += int(delivered)
            self.failures += int(not delivered)
            with self.receipts.open("a", encoding="utf-8") as file:
                file.write(line + "\n")
                file.flush()
                os.fsync(file.fileno())
        return delivered, line

    def metrics(self) -> str:
        with self.lock:
            deliveries, failures = self.deliveries, self.failures
        production = int(self.mode == "production")
        return (
            "# TYPE task_alert_router_deliveries_total counter\n"
            f"task_alert_router_deliveries_total {deliveries}\n"
            "# TYPE task_alert_router_delivery_failures_total counter\n"
            f"task_alert_router_delivery_failures_total {failures}\n"
            "# TYPE task_alert_router_production_mode gauge\n"
            f"task_alert_router_production_mode {production}\n"
        )


class Handler(BaseHTTPRequestHandler):
    server_version = "TaskAlertRouter/1.0"

    @property
    def state(self) -> RouterState:
        return self.server.state  # type: ignore[attr-defined]

    def do_GET(self) -> None:
        if self.path == "/health/live":
            self._reply(200, b'{"status":"alive"}', "application/json")
        elif self.path == "/health/ready":
            self._reply(200, b'{"status":"ready"}', "application/json")
        elif self.path == "/metrics":
            self._reply(200, self.state.metrics().encode(), "text/plain; version=0.0.4")
        else:
            self._reply(404, b'{"error":"not_found"}', "application/json")

    def do_POST(self) -> None:
        if self.path != "/alerts":
            self._reply(404, b'{"error":"not_found"}', "application/json")
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            length = -1
        if length < 1 or length > MAX_BODY:
            self._reply(413, b'{"error":"invalid_size"}', "application/json")
            return
        try:
            payload = json.loads(self.rfile.read(length))
            if not isinstance(payload, dict):
                raise ValueError("payload must be an object")
        except (json.JSONDecodeError, ValueError):
            self._reply(400, b'{"error":"invalid_json"}', "application/json")
            return
        delivered, receipt = self.state.deliver(payload)
        self._reply(200 if delivered else 502, receipt.encode(), "application/json")

    def log_message(self, fmt: str, *args: object) -> None:
        # Request URLs, headers and payloads are intentionally absent from logs.
        print(json.dumps({"event": "alert_router_request", "client": self.client_address[0], "message": fmt % args}))

    def _reply(self, status: int, body: bytes, content_type: str) -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.end_headers()
        self.wfile.write(body)


def serve(host: str = "0.0.0.0", port: int = 8080) -> None:
    state = RouterState()
    server = ThreadingHTTPServer((host, port), Handler)
    server.state = state  # type: ignore[attr-defined]
    print(json.dumps({"event": "alert_router_started", "mode": state.mode, "owner": state.owner, "port": port}))
    server.serve_forever()


if __name__ == "__main__":
    serve(port=int(os.environ.get("TASK_ALERT_ROUTER_PORT", "8080")))
