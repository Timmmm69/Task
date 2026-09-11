from __future__ import annotations

import csv
import hashlib
import json
import shutil
import subprocess
import zipfile
from datetime import datetime, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
VERSION = "1.1.0"
PACKAGE_NAME = "20260911_qa04_accessibility_usability_1.1.0"
OUTPUT = ROOT / "outputs" / PACKAGE_NAME
OUTPUT_ZIP = ROOT / "outputs" / f"{PACKAGE_NAME}.zip"
ZIP_SUM = ROOT / "outputs" / f"{PACKAGE_NAME}.zip.sha256"
EVIDENCE = OUTPUT / "evidence"
DOCS = ROOT / "work" / "production" / "docs"
IMPLEMENTATION = [
    ".project-dashboard/roadmap.json",
    "work/production/docs/QA-04-accessibility-usability-matrix.md",
    "work/production/docs/QA-04-findings.csv",
    "work/production/docs/QA-04-usability-findings.md",
    "work/production/docs/QA-04-release-sign-off.md",
    "work/production/verification/Test-Qa04AccessibilityGate.ps1",
    "work/production/verification/Test-Qa04CompletionGate.ps1",
    "work/production/verification/Build-Qa04CompletionPackage.py",
    "work/production/verification/Test-Desk05WindowsUx.ps1",
    "work/production/verification/Test-Qa03Gate.ps1",
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


def verify_checksum_inventory() -> int:
    sums_path = OUTPUT / "SHA256SUMS"
    lines = sums_path.read_text(encoding="utf-8").splitlines()
    expected_paths: list[str] = []
    for line in lines:
        digest, relative = line.split("  ", 1)
        target = OUTPUT / relative
        require(target.is_file(), f"Checksum target is absent: {relative}")
        require(sha256(target) == digest, f"SHA-256 verification failed: {relative}")
        expected_paths.append(relative)
    actual_paths = sorted(
        path.relative_to(OUTPUT).as_posix()
        for path in OUTPUT.rglob("*")
        if path.is_file() and path.name != "SHA256SUMS"
    )
    require(sorted(expected_paths) == actual_paths, "SHA256SUMS does not cover the exact package inventory.")
    return len(lines)


def verify_zip() -> None:
    with zipfile.ZipFile(OUTPUT_ZIP, "r") as archive:
        require(archive.testzip() is None, "ZIP CRC validation failed.")
        names = sorted(info.filename for info in archive.infolist() if not info.is_dir())
        expected = sorted(
            (Path(PACKAGE_NAME) / path.relative_to(OUTPUT)).as_posix()
            for path in OUTPUT.rglob("*")
            if path.is_file()
        )
        require(names == expected, "ZIP inventory differs from the package directory.")
        for path in OUTPUT.rglob("*"):
            if path.is_file():
                archived = archive.read((Path(PACKAGE_NAME) / path.relative_to(OUTPUT)).as_posix())
                require(hashlib.sha256(archived).hexdigest() == sha256(path), f"ZIP content differs: {path}")
        manifest = json.loads(archive.read(f"{PACKAGE_NAME}/manifest.json").decode("utf-8"))
        require(manifest["validation"]["overall_qa04"] == "PASS", "Archived manifest is not PASS.")


def main() -> None:
    require(OUTPUT.is_dir() and EVIDENCE.is_dir(), "Fresh QA-04 evidence directory is absent.")
    require(not (OUTPUT / "manifest.json").exists(), "Refusing to overwrite an existing manifest.")
    require(not OUTPUT_ZIP.exists() and not ZIP_SUM.exists(), "Refusing to overwrite an existing archive.")

    required = [ROOT / path for path in IMPLEMENTATION]
    completion_path = EVIDENCE / "qa04-completion.json"
    baseline_path = EVIDENCE / "qa04-accessibility.json"
    required.extend([completion_path, baseline_path])
    missing = [str(path) for path in required if not path.is_file()]
    require(not missing, "Missing QA-04 completion inputs: " + ", ".join(missing))

    completion = json.loads(completion_path.read_text(encoding="utf-8-sig"))
    baseline = json.loads(baseline_path.read_text(encoding="utf-8-sig"))
    require(completion.get("task") == "QA-04" and completion.get("scope") == "steps-5-8", "Unexpected scope.")
    require(completion.get("result") == "PASS_WITH_NON_BLOCKING_FINDINGS", "Completion gate is not PASS.")
    require(completion.get("completion", {}).get("overallQa04") == "PASS", "QA-04 overall result is not PASS.")
    require(baseline.get("result") == "PASS", "Baseline accessibility evidence is not PASS.")

    with (DOCS / "QA-04-findings.csv").open(encoding="utf-8-sig", newline="") as handle:
        findings = list(csv.DictReader(handle))
    require(len(findings) == 4, "Expected exactly four reviewed findings.")
    require(not [row for row in findings if row["severity"] in {"Critical", "High"}], "Blocking findings exist.")
    require(not [row for row in findings if row["disposition"] not in {"fixed", "accepted", "deferred", "not_reproducible"}], "Open finding exists.")

    documents = {
        "QA-04-accessibility-usability-matrix.md": "QA-04-accessibility-usability-matrix.md",
        "QA-04-findings.csv": "QA-04-findings.csv",
        "QA-04-usability-findings.md": "USABILITY_REVIEW.md",
        "QA-04-release-sign-off.md": "RELEASE_SIGN_OFF.md",
    }
    for source, destination in documents.items():
        shutil.copy2(DOCS / source, OUTPUT / destination)
    (OUTPUT / "VERSION").write_text(VERSION + "\n", encoding="utf-8")
    (OUTPUT / "README.md").write_text(
        "# QA-04 native accessibility and usability evidence package\n\n"
        f"Version: {VERSION}\n\n"
        "Final controlled evidence for QA-04 steps 5-8, built on the validated steps 1-4 baseline. "
        "The decision is PASS_WITH_NON_BLOCKING_FINDINGS. Read VALIDATION_REPORT.md, "
        "RELEASE_SIGN_OFF.md and the matrix before interpreting the raw evidence.\n",
        encoding="utf-8",
    )

    coverage = completion["coverage"]
    finding_counts = completion["findings"]
    platform = completion["evidenceRun"]["host"]
    report = f"""# QA-04 final validation report

Result: **PASS_WITH_NON_BLOCKING_FINDINGS**

## Verified

- Steps 5-8 completion gate: PASS.
- Critical UI scenarios: {coverage['criticalUiScenariosPassed']}/{coverage['criticalUiScenariosTotal']} PASS.
- DPI cases: {coverage['dpiCasesPassed']}/{coverage['dpiCasesTotal']} PASS (auth + main at 100/125/150/200%).
- Focused desktop tests: {coverage['focusedDesktopTestsPassed']}/{coverage['focusedDesktopTestsTotal']} PASS.
- Keyboard Tab/F6 and UIA Value/Invoke/Selection: PASS.
- Findings: {finding_counts['total']} total; Critical {finding_counts['critical']}; High {finding_counts['high']}; Medium {finding_counts['medium']}; Low {finding_counts['low']}.
- Disposition: accepted {finding_counts['accepted']}; deferred {finding_counts['deferred']}; open {finding_counts['open']}.
- Internal release sign-off: APPROVED.
- Host: {platform['osVersion']}, {platform['architecture']}, native DPI {platform['nativeMainWindowDpi']}.
- Isolated runtime cleanup and original Desktop AppData restoration: PASS.

## Evidence limits

Narrator is outside the Task acceptance scope and was not executed. The non-native scale values
are logical viewport equivalents on the native PerMonitorV2 host. Physical mixed-monitor transition
remains a deployment smoke check. This package does not claim WCAG certification.

## Integrity

The builder verified every manifest and SHA256SUMS entry, exact package inventory, ZIP CRC,
full ZIP readback and byte-for-byte SHA-256 equality between the directory and archive.
"""
    (OUTPUT / "VALIDATION_REPORT.md").write_text(report, encoding="utf-8")

    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    artifact_files = sorted(path for path in OUTPUT.rglob("*") if path.is_file())
    manifest = {
        "package": "QA-04 native accessibility and usability confirmation",
        "version": VERSION,
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "base_revision": revision,
        "roadmap_ids": ["QA-04"],
        "state": "validated-complete",
        "completed_steps": [1, 2, 3, 4, 5, 6, 7, 8],
        "validation": {
            "result": "PASS_WITH_NON_BLOCKING_FINDINGS",
            "dpi_matrix": "PASS",
            "internal_usability_review": "PASS",
            "findings_disposition": "PASS",
            "regression_retest": "PASS",
            "release_sign_off": "APPROVED",
            "overall_qa04": "PASS",
        },
        "limitations": completion["limitations"],
        "implementation_files": [record(path) for path in required if path != completion_path and path != baseline_path],
        "artifact_files": [record(path, OUTPUT) for path in artifact_files],
    }
    manifest_path = OUTPUT / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    json.loads(manifest_path.read_text(encoding="utf-8"))
    for entry in manifest["implementation_files"]:
        require(sha256(ROOT / entry["path"]) == entry["sha256"], f"Implementation hash changed: {entry['path']}")
    for entry in manifest["artifact_files"]:
        require(sha256(OUTPUT / entry["path"]) == entry["sha256"], f"Artifact hash changed: {entry['path']}")

    checksum_targets = sorted(path for path in OUTPUT.rglob("*") if path.is_file() and path.name != "SHA256SUMS")
    (OUTPUT / "SHA256SUMS").write_text(
        "".join(f"{sha256(path)}  {path.relative_to(OUTPUT).as_posix()}\n" for path in checksum_targets),
        encoding="utf-8",
    )
    verified_files = verify_checksum_inventory()

    with zipfile.ZipFile(OUTPUT_ZIP, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(item for item in OUTPUT.rglob("*") if item.is_file()):
            archive.write(path, (Path(PACKAGE_NAME) / path.relative_to(OUTPUT)).as_posix())
    verify_zip()
    ZIP_SUM.write_text(f"{sha256(OUTPUT_ZIP)}  {OUTPUT_ZIP.name}\n", encoding="utf-8")
    require(sha256(OUTPUT_ZIP) == ZIP_SUM.read_text(encoding="utf-8").split()[0], "ZIP sidecar mismatch.")
    print(f"QA-04 completion package PASS: {verified_files} files verified; ZIP CRC/readback PASS.")


if __name__ == "__main__":
    main()
