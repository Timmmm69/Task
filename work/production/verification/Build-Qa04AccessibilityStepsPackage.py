from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import zipfile
from datetime import datetime, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
VERSION = "1.0.0"
PACKAGE_NAME = "20260911_qa04_accessibility_steps_1_3_1.0.0"
OUTPUT = ROOT / "outputs" / PACKAGE_NAME
OUTPUT_ZIP = ROOT / "outputs" / f"{PACKAGE_NAME}.zip"
EVIDENCE = OUTPUT / "evidence"
MATRIX = ROOT / "work" / "production" / "docs" / "QA-04-accessibility-usability-matrix.md"
IMPLEMENTATION = [
    ".project-dashboard/roadmap.json",
    "work/production/docs/QA-04-accessibility-usability-matrix.md",
    "work/production/docs/DESK-03-primary-sections.md",
    "work/production/docs/DESK-05-windows-ux.md",
    "work/production/docs/PROD-05-work-links-search-notifications.md",
    "work/production/verification/Test-Qa04AccessibilityGate.ps1",
    "work/production/verification/Test-Desk05WindowsUx.ps1",
    "work/production/verification/Build-Qa04AccessibilityStepsPackage.py",
    "work/production/verification/Build-Desk05Package.py",
    "work/production/verification/Build-Prod04Package.py",
    "work/production/verification/Build-Prod05Package.py",
]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def record(path: Path, relative_to: Path = ROOT) -> dict[str, object]:
    return {
        "path": path.relative_to(relative_to).as_posix(),
        "bytes": path.stat().st_size,
        "sha256": sha256(path),
    }


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(message)


def main() -> None:
    require(OUTPUT.is_dir(), f"QA-04 evidence directory is absent: {OUTPUT}")
    require(not (OUTPUT / "manifest.json").exists(), "Refusing to overwrite an existing QA-04 manifest.")
    require(not OUTPUT_ZIP.exists(), f"Refusing to overwrite existing archive: {OUTPUT_ZIP}")

    summary_path = EVIDENCE / "qa04-accessibility.json"
    ui_path = EVIDENCE / "qa03" / "ui-assertions.json"
    windows_path = EVIDENCE / "desk05" / "windows-ux.json"
    required = [ROOT / path for path in IMPLEMENTATION]
    required.extend([summary_path, ui_path, windows_path])
    missing = [str(path) for path in required if not path.is_file()]
    require(not missing, "Missing QA-04 steps 1-3 inputs: " + ", ".join(missing))

    summary = json.loads(summary_path.read_text(encoding="utf-8-sig"))
    ui = json.loads(ui_path.read_text(encoding="utf-8-sig"))
    windows = json.loads(windows_path.read_text(encoding="utf-8-sig"))
    require(summary.get("task") == "QA-04" and summary.get("scope") == "steps-1-3", "Unexpected QA-04 scope.")
    require(summary.get("result") == "PASS", "QA-04 steps 1-3 evidence is not PASS.")
    require(summary.get("completion", {}).get("overallQa04") == "IN_PROGRESS", "QA-04 must remain in progress.")
    require(ui.get("result") == "PASS" and ui.get("seedProfile") == "qa03-deterministic-v1", "QA-03 child evidence is invalid.")
    require(windows.get("result") == "PASS", "Native Windows child evidence is invalid.")
    require(windows.get("keyboard", {}).get("result") == "PASS", "Keyboard evidence is invalid.")
    require(windows.get("nativeUia", {}).get("authentication") == "PASS", "Authentication UIA evidence is invalid.")
    require(windows.get("nativeUia", {}).get("mainWindow") == "PASS", "Main-window UIA evidence is invalid.")
    release_path = ROOT / summary.get("releaseArtifact", {}).get("path", "")
    require(release_path.is_file(), "Recorded Release WPF executable is absent.")
    require(sha256(release_path) == summary["releaseArtifact"]["sha256"], "Release WPF executable hash changed.")

    shutil.copy2(MATRIX, OUTPUT / MATRIX.name)
    (OUTPUT / "VERSION").write_text(VERSION + "\n", encoding="utf-8")
    (OUTPUT / "README.md").write_text(
        "# QA-04 accessibility steps 1-3 evidence package\n\n"
        f"Version: {VERSION}\n\n"
        "This package validates the controlled matrix, reproducible synthetic Release stand, "
        "and native keyboard/UI Automation checks. It is an intermediate QA-04 package: "
        "manual usability review, findings disposition, and release sign-off remain open.\n",
        encoding="utf-8",
    )
    platform = summary["platform"]
    checks = windows.get("checks", [])
    report = f"""# QA-04 steps 1-3 validation report

Result: **PASS**

## Completed

- Controlled Windows/accessibility matrix: PASS.
- Reproducible PostgreSQL 16 + HTTPS API + Release WPF stand: PASS.
- Synthetic seed profile `{summary['seedProfile']}`: PASS.
- Critical Release WPF UI scenarios: {len(summary['criticalUiScenarios'])}/{len(summary['criticalUiScenarios'])} PASS.
- Native UI Automation authentication and main-window contracts: PASS.
- Keyboard `Tab` and full `F6` navigation cycle: PASS.
- Native child checks: {len(checks)}/{len(checks)} PASS.
- Host: {platform['osVersion']}, {platform['architecture']}, native DPI {platform['nativeMainWindowDpi']}.
- Isolated setup and cleanup restored the original Desktop AppData: PASS.

## Remaining QA-04 scope

QA-04 is not complete. Internal usability review, findings disposition, targeted retest,
and internal release sign-off remain open. Screen-reader testing is outside the Task product
acceptance scope.
"""
    (OUTPUT / "VALIDATION_REPORT.md").write_text(report, encoding="utf-8")

    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    evidence_files = sorted(path for path in EVIDENCE.rglob("*") if path.is_file())
    manifest = {
        "package": "QA-04 native accessibility steps 1-3",
        "version": VERSION,
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "base_revision": revision,
        "roadmap_ids": ["QA-04"],
        "state": "validated-partial",
        "completed_steps": [1, 2, 3],
        "remaining_steps": [4, 5, 6, 7, 8],
        "validation": {
            "result": "PASS",
            "matrix": "PASS",
            "reproducible_synthetic_stand": "PASS",
            "keyboard_and_native_uia": "PASS",
            "overall_qa04": "IN_PROGRESS",
        },
        "implementation_files": [record(ROOT / path) for path in IMPLEMENTATION],
        "evidence_files": [record(path, OUTPUT) for path in evidence_files],
    }
    manifest_path = OUTPUT / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    json.loads(manifest_path.read_text(encoding="utf-8"))

    checksummed = sorted(path for path in OUTPUT.rglob("*") if path.is_file() and path.name != "SHA256SUMS")
    lines = [f"{sha256(path)}  {path.relative_to(OUTPUT).as_posix()}" for path in checksummed]
    (OUTPUT / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="utf-8")
    for line in lines:
        expected, relative = line.split("  ", 1)
        require(sha256(OUTPUT / relative) == expected, f"SHA-256 verification failed for {relative}")

    with zipfile.ZipFile(OUTPUT_ZIP, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(item for item in OUTPUT.rglob("*") if item.is_file()):
            archive.write(path, (Path(PACKAGE_NAME) / path.relative_to(OUTPUT)).as_posix())
    (ROOT / "outputs" / f"{PACKAGE_NAME}.zip.sha256").write_text(
        f"{sha256(OUTPUT_ZIP)}  {OUTPUT_ZIP.name}\n", encoding="utf-8"
    )
    print(f"QA-04 steps 1-3 package PASS: {len(lines)} files verified.")


if __name__ == "__main__":
    main()
