from __future__ import annotations

import hashlib
import json
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / "outputs" / "20260907_qa02_critical_task_auth_e2e_1.0.0"
EVIDENCE = OUTPUT / "evidence"
IMPLEMENTATION = [
    ".project-dashboard/roadmap.json",
    "work/production/verification/Test-TaskWriteE2E.ps1",
    "work/production/verification/Test-Qa02CleanStand.ps1",
    "work/production/verification/Build-Qa02Package.py",
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
        EVIDENCE / "db-assertions.json",
        EVIDENCE / "ui-assertions.json",
        EVIDENCE / "qa02-clean-stand.log",
        EVIDENCE / "qa02-release-build.log",
        EVIDENCE / "phase-setup.stdout.log",
        EVIDENCE / "phase-verifycritical.stdout.log",
        EVIDENCE / "phase-seedreadonlydesktop.stdout.log",
        EVIDENCE / "phase-cleanup.stdout.log",
    ]
    required = [OUTPUT / name for name in PACKAGE_FILES]
    required.extend(ROOT / name for name in IMPLEMENTATION)
    required.extend(required_evidence)
    missing = [str(path) for path in required if not path.is_file()]
    if missing:
        raise SystemExit("Missing QA-02 package inputs: " + ", ".join(missing))

    db = json.loads((EVIDENCE / "db-assertions.json").read_text(encoding="utf-8-sig"))
    ui = json.loads((EVIDENCE / "ui-assertions.json").read_text(encoding="utf-8-sig"))
    run_log = (EVIDENCE / "qa02-clean-stand.log").read_text(encoding="utf-8-sig")
    cleanup_log = (EVIDENCE / "phase-cleanup.stdout.log").read_text(encoding="utf-8-sig")
    if db.get("result") != "PASS" or ui.get("result") != "PASS":
        raise SystemExit("QA-02 JSON evidence is not PASS.")
    if "QA-02 CRITICAL TASK/AUTH E2E BACKEND VERIFICATION PASSED." not in run_log:
        raise SystemExit("QA-02 backend PASS marker is absent.")
    if "original Desktop app-data restored" not in cleanup_log:
        raise SystemExit("QA-02 cleanup proof is absent.")

    revision = subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
    ).strip()
    evidence_files = sorted(path for path in EVIDENCE.iterdir() if path.is_file())
    manifest = {
        "package": "QA-02 critical task/auth clean-stand E2E",
        "version": "1.0.0",
        "date": "2026-09-07",
        "base_revision": revision,
        "roadmap_ids": ["QA-02"],
        "state": "validated-for-main",
        "runtime": {"postgresql": db["postgresVersion"], "schema_version": int(db["migrationVersion"])},
        "validation": {
            "result": "PASS",
            "seed_profile": db["seedProfile"],
            "real_wpf_https_postgresql": "PASS",
            "api_restart_persistence": db["apiRestartPersistence"],
            "read_only_get_status": db["readerGetStatus"],
            "read_only_write_status": db["readerWriteStatus"],
            "cleanup": "PASS",
        },
        "implementation_files": [record(ROOT / path) for path in IMPLEMENTATION],
        "evidence_files": [record(path) for path in evidence_files],
    }
    manifest_path = OUTPUT / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    json.loads(manifest_path.read_text(encoding="utf-8"))

    checksummed = [OUTPUT / name for name in PACKAGE_FILES]
    checksummed.extend(evidence_files)
    checksummed.append(manifest_path)
    lines = [f"{record(path, OUTPUT)['sha256']}  {path.relative_to(OUTPUT).as_posix()}" for path in checksummed]
    (OUTPUT / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="utf-8")

    for line in lines:
        expected, relative = line.split("  ", 1)
        actual = hashlib.sha256((OUTPUT / relative).read_bytes()).hexdigest()
        if actual != expected:
            raise SystemExit(f"SHA-256 verification failed for {relative}")
    print(f"QA-02 package PASS: {len(lines)} checksums verified.")


if __name__ == "__main__":
    main()
