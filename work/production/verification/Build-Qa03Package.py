from __future__ import annotations

import hashlib
import json
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / "outputs" / "20260910_qa03_critical_e2e_1.0.0"
EVIDENCE = OUTPUT / "evidence"
IMPLEMENTATION = [
    ".project-dashboard/roadmap.json",
    "work/production/docs/QA-03-critical-e2e-matrix.md",
    "work/production/verification/Test-Qa03CriticalE2E.ps1",
    "work/production/verification/Test-Qa03Gate.ps1",
    "work/production/verification/Build-Qa03Package.py",
]
PACKAGE_FILES = ["README.md", "VALIDATION_REPORT.md", "VERSION"]


def record(path: Path, relative_to: Path = ROOT) -> dict[str, object]:
    data = path.read_bytes()
    return {
        "path": path.relative_to(relative_to).as_posix(),
        "bytes": len(data),
        "sha256": hashlib.sha256(data).hexdigest(),
    }


def main() -> None:
    required_evidence = [
        EVIDENCE / "api-assertions.json",
        EVIDENCE / "worker-delivery.json",
        EVIDENCE / "notifications-assertions.json",
        EVIDENCE / "product-db-assertions.json",
        EVIDENCE / "ui-assertions.json",
        EVIDENCE / "qa03-gate.log",
        EVIDENCE / "qa03-release-build.log",
        EVIDENCE / "phase-setup.stdout.log",
        EVIDENCE / "phase-cleanup.stdout.log",
        EVIDENCE / "today.png",
        EVIDENCE / "tasks-created.png",
        EVIDENCE / "tasks-conflict.png",
        EVIDENCE / "tasks-recovered.png",
        EVIDENCE / "calendar.png",
        EVIDENCE / "projects.png",
        EVIDENCE / "catalog.png",
        EVIDENCE / "contacts.png",
        EVIDENCE / "search.png",
        EVIDENCE / "notifications.png",
        EVIDENCE / "offline.png",
        EVIDENCE / "reconnected.png",
    ]
    required = [OUTPUT / name for name in PACKAGE_FILES]
    required.extend(ROOT / name for name in IMPLEMENTATION)
    required.extend(required_evidence)
    missing = [str(path) for path in required if not path.is_file()]
    if missing:
        raise SystemExit("Missing QA-03 package inputs: " + ", ".join(missing))

    evidence_json = {
        name: json.loads(path.read_text(encoding="utf-8-sig"))
        for name, path in [
            ("api", EVIDENCE / "api-assertions.json"),
            ("worker", EVIDENCE / "worker-delivery.json"),
            ("notifications", EVIDENCE / "notifications-assertions.json"),
            ("product_db", EVIDENCE / "product-db-assertions.json"),
            ("ui", EVIDENCE / "ui-assertions.json"),
        ]
    }
    for name, payload in evidence_json.items():
        if payload.get("result") != "PASS":
            raise SystemExit(f"QA-03 {name} JSON evidence is not PASS.")

    run_log = (EVIDENCE / "qa03-gate.log").read_text(encoding="utf-8-sig")
    cleanup_log = (EVIDENCE / "phase-cleanup.stdout.log").read_text(encoding="utf-8-sig")
    if "QA-03 CRITICAL E2E GATE PASSED." not in run_log:
        raise SystemExit("QA-03 gate PASS marker is absent.")
    if "original Desktop app-data restored" not in cleanup_log:
        raise SystemExit("QA-03 cleanup proof is absent.")

    ui = evidence_json["ui"]
    ui_checks = ui.get("uiChecks", {})
    for required_ui in [
        "today", "taskCreate", "taskConflict", "taskConflictRecovery", "calendar",
        "projectCreate", "catalogCreate", "contactCreate", "search", "notifications",
        "offlineShell", "reconnect",
    ]:
        if ui_checks.get(required_ui) != "PASS":
            raise SystemExit(f"QA-03 UI check '{required_ui}' is not PASS.")

    db = evidence_json["product_db"]["database"]
    revision = subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
    ).strip()
    evidence_files = sorted(path for path in EVIDENCE.iterdir() if path.is_file())
    manifest = {
        "package": "QA-03 critical user scenario E2E matrix",
        "version": "1.0.0",
        "date": "2026-09-10",
        "base_revision": revision,
        "roadmap_ids": ["QA-03"],
        "state": "validated-for-main",
        "runtime": {
            "postgresql": db["postgresVersion"],
            "schema_version": int(db["migrationVersion"]),
        },
        "validation": {
            "result": "PASS",
            "seed_profile": "qa03-deterministic-v1",
            "real_wpf_https_postgresql": "PASS",
            "worker_reminder_delivery": evidence_json["worker"]["reminderOccurrenceState"],
            "notification_final_state": evidence_json["notifications"]["finalStatus"],
            "ui_task_final_version": db["uiTaskVersion"],
            "conflict_recovery": "PASS",
            "offline_shell_and_reconnect": "PASS",
            "read_only_project_list_status": evidence_json["api"]["readerProjectListStatus"],
            "read_only_project_create_status": evidence_json["api"]["readerProjectCreateStatus"],
            "cleanup": "PASS",
        },
        "implementation_files": [record(ROOT / path) for path in IMPLEMENTATION],
        "evidence_files": [record(path) for path in evidence_files],
    }
    manifest_path = OUTPUT / "manifest.json"
    manifest_path.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    json.loads(manifest_path.read_text(encoding="utf-8"))

    checksummed = [OUTPUT / name for name in PACKAGE_FILES]
    checksummed.extend(evidence_files)
    checksummed.append(manifest_path)
    lines = [
        f"{record(path, OUTPUT)['sha256']}  {path.relative_to(OUTPUT).as_posix()}"
        for path in checksummed
    ]
    (OUTPUT / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="utf-8")

    for line in lines:
        expected, relative = line.split("  ", 1)
        actual = hashlib.sha256((OUTPUT / relative).read_bytes()).hexdigest()
        if actual != expected:
            raise SystemExit(f"SHA-256 verification failed for {relative}")
    print(f"QA-03 package PASS: {len(lines)} checksums verified.")


if __name__ == "__main__":
    main()
