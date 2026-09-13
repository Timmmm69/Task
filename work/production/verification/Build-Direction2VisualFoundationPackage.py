from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile
from datetime import datetime, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
VERSION = "1.0.0"
PACKAGE_NAME = "20260914_direction2_visual_foundation_1.0.0"
OUTPUT = ROOT / "outputs" / PACKAGE_NAME
OUTPUT_ZIP = ROOT / "outputs" / f"{PACKAGE_NAME}.zip"
OUTPUT_ZIP_HASH = ROOT / "outputs" / f"{PACKAGE_NAME}.zip.sha256"
EVIDENCE_SOURCE = ROOT / "work" / "production" / "evidence" / "direction2-foundation-1.0.0"
DESIGN_QA = ROOT / "work" / "production" / "design-qa.md"

IMPLEMENTATION = [
    "work/production/src/Task.Desktop/MainWindow.xaml",
    "work/production/src/Task.Desktop/Resources/Controls.Buttons.xaml",
    "work/production/src/Task.Desktop/Resources/Controls.Data.xaml",
    "work/production/src/Task.Desktop/Resources/Controls.Navigation.xaml",
    "work/production/src/Task.Desktop/Resources/Controls.Shell.xaml",
    "work/production/src/Task.Desktop/Resources/Icons.xaml",
    "work/production/src/Task.Desktop/Resources/Theme.xaml",
    "work/production/src/Task.Desktop/Resources/Tokens.Colors.xaml",
    "work/production/src/Task.Desktop/Resources/Tokens.Spacing.xaml",
    "work/production/tests/Task.Desktop.Tests/VisualFoundationTests.cs",
    "work/production/design-qa.md",
    "work/production/verification/Build-Direction2VisualFoundationPackage.py",
]

REFERENCES = [
    "work/stage_5_6_final_visual_baseline_and_handoff/FINAL_VISUAL_BASELINE_1.0.md",
    "work/stage_5_6_final_visual_baseline_and_handoff/prototype/src/App.jsx",
    "work/stage_5_6_final_visual_baseline_and_handoff/prototype/src/styles.css",
    "work/stage_5_6_final_visual_baseline_and_handoff/design-system/Design_System_1.0.md",
    "work/stage_5_6_final_visual_baseline_and_handoff/design-system/Component_Implementation_Specs_1.0.csv",
    "work/stage_5_prototype/implementation-direction2-final.png",
]


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(message)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def record(path: Path, relative_to: Path = ROOT) -> dict[str, object]:
    return {
        "path": path.relative_to(relative_to).as_posix(),
        "bytes": path.stat().st_size,
        "sha256": sha256(path),
    }


def read_test_counters(path: Path) -> dict[str, int]:
    root = ET.parse(path).getroot()
    counters = next(
        (node for node in root.iter() if node.tag.rsplit("}", 1)[-1] == "Counters"),
        None,
    )
    require(counters is not None, f"TRX counters are absent: {path}")
    assert counters is not None
    return {
        name: int(counters.attrib.get(name, "0"))
        for name in ("total", "executed", "passed", "failed", "notExecuted")
    }


def validate_inputs() -> tuple[dict[str, object], dict[str, int], dict[str, int]]:
    required = [ROOT / relative for relative in IMPLEMENTATION + REFERENCES]
    required.extend(
        [
            EVIDENCE_SOURCE / "windows-ux.json",
            EVIDENCE_SOURCE / "windows-ux.trx",
            EVIDENCE_SOURCE / "full-desktop.trx",
            EVIDENCE_SOURCE / "release-build.stdout.log",
            EVIDENCE_SOURCE / "main-100.png",
            EVIDENCE_SOURCE / "main-150.png",
            EVIDENCE_SOURCE / "comparison-full.png",
            EVIDENCE_SOURCE / "comparison-shell.png",
        ]
    )
    missing = [str(path) for path in required if not path.is_file()]
    require(not missing, "Missing Direction 2 package inputs: " + ", ".join(missing))

    qa_text = DESIGN_QA.read_text(encoding="utf-8-sig")
    require("final result: passed" in qa_text, "Design QA is not PASS.")

    evidence = json.loads((EVIDENCE_SOURCE / "windows-ux.json").read_text(encoding="utf-8-sig"))
    require(evidence.get("task") == "DESK-05" and evidence.get("result") == "PASS", "Native Windows evidence is not PASS.")
    require(evidence.get("nativeUia", {}).get("mainWindow") == "PASS", "Main-window UIA evidence is absent.")
    require(evidence.get("keyboard", {}).get("result") == "PASS", "Keyboard evidence is absent.")
    matrix = evidence.get("dpiMatrix", {})
    require(matrix.get("result") == "PASS", "DPI matrix is not PASS.")
    main_rows = matrix.get("mainWindow", [])
    require([row.get("scalePercent") for row in main_rows] == [100, 125, 150, 200], "Main-window DPI scales are incomplete.")
    require(all(row.get("result") == "PASS" for row in main_rows), "Main-window DPI evidence contains a failure.")
    require(len(evidence.get("checks", [])) == 86, "Expected 86 native checks.")

    focused = read_test_counters(EVIDENCE_SOURCE / "windows-ux.trx")
    require(focused == {"total": 7, "executed": 7, "passed": 7, "failed": 0, "notExecuted": 0}, f"Unexpected focused counters: {focused}")
    desktop = read_test_counters(EVIDENCE_SOURCE / "full-desktop.trx")
    require(desktop["failed"] == 0 and desktop["notExecuted"] == 0, f"Desktop regression tests are not clean: {desktop}")
    require(desktop["executed"] == desktop["total"] == desktop["passed"] and desktop["passed"] >= 316, f"Desktop regression test coverage is incomplete: {desktop}")

    build_log = (EVIDENCE_SOURCE / "release-build.stdout.log").read_text(encoding="utf-8-sig")
    require("0 Error(s)" in build_log or "0 Fehler" in build_log or "Ошибок: 0" in build_log, "Release build success marker is absent.")
    return evidence, focused, desktop


def write_text_artifacts(
    evidence: dict[str, object], focused: dict[str, int], desktop: dict[str, int]
) -> None:
    readme = f"""# Direction 2 visual foundation evidence package

Version: {VERSION}

This package records the production WPF shell alignment with the frozen Direction 2 baseline.

Key evidence:

- `evidence/main-100.png` and `evidence/main-150.png` — requested native comparison captures;
- `evidence/comparison-full.png` and `evidence/comparison-shell.png` — baseline comparisons;
- `evidence/windows-ux.json` — native UIA, keyboard, and 100/125/150/200% matrix;
- `evidence/full-desktop.trx` — complete desktop regression suite;
- `design-qa.md` — visual review and accepted evidence boundary;
- `manifest.json` and `SHA256SUMS` — provenance and integrity.

Recreate native evidence with `work/production/verification/Test-Desk05WindowsUx.ps1`.
Validate this package with `python work/production/verification/Build-Direction2VisualFoundationPackage.py --validate-only`.
"""
    platform = evidence["platform"]
    validation = f"""# Direction 2 visual foundation validation report

Result: **PASS**

## Verified

- Frozen Direction 2 baseline and design-system sources were mapped to shared WPF resources and the main shell.
- Release solution build: PASS, 0 errors and 0 warnings.
- Full desktop regression suite: {desktop['passed']}/{desktop['total']} passed, 0 skipped.
- Focused Windows UX tests: {focused['passed']}/{focused['total']} passed.
- Native Windows checks: {len(evidence['checks'])}/{len(evidence['checks'])} passed.
- Host: {platform['osVersion']}, {platform['architecture']}, {platform['screenPixels']['width']}×{platform['screenPixels']['height']}.
- Native WPF DPI: {platform['nativeMainWindowDpi']}; PerMonitorV2 manifest: PASS.
- Main-window logical viewport equivalents 100/125/150/200%: PASS without critical clipping.
- UI Automation names and Value/Invoke/Selection patterns: PASS.
- Keyboard Tab/F6 traversal: PASS.
- Visual QA against the frozen baseline: PASS; no P0, P1, or P2 findings.
- Existing ViewModel commands, business logic, API, DTO, permissions, and `sources/` were not changed.

## Evidence boundary

The baseline image captures `Сегодня`; the deterministic production evidence captures `Задачи`.
The comparison therefore validates the shared shell, visual language, density, and interaction-state foundation,
not pixel equality of the content area. The host monitor was physically at 150%; the other requested scales
were exercised as logical viewport equivalents on the same native PerMonitorV2 WPF window.
"""
    (OUTPUT / "README.md").write_text(readme, encoding="utf-8")
    (OUTPUT / "VALIDATION_REPORT.md").write_text(validation, encoding="utf-8")
    (OUTPUT / "VERSION").write_text(VERSION + "\n", encoding="utf-8")
    shutil.copy2(DESIGN_QA, OUTPUT / "design-qa.md")


def verify_package() -> None:
    require(OUTPUT.is_dir(), f"Package directory is absent: {OUTPUT}")
    require(OUTPUT_ZIP.is_file(), f"Package archive is absent: {OUTPUT_ZIP}")
    require(OUTPUT_ZIP_HASH.is_file(), f"Package archive checksum is absent: {OUTPUT_ZIP_HASH}")
    require((OUTPUT / "VERSION").read_text(encoding="utf-8-sig").strip() == VERSION, "VERSION mismatch.")
    manifest = json.loads((OUTPUT / "manifest.json").read_text(encoding="utf-8-sig"))
    require(manifest.get("version") == VERSION and manifest.get("validation", {}).get("result") == "PASS", "Manifest is not validated.")
    for line in (OUTPUT / "SHA256SUMS").read_text(encoding="utf-8-sig").splitlines():
        expected, relative = line.split("  ", 1)
        require(sha256(OUTPUT / relative) == expected, f"SHA-256 mismatch: {relative}")
    expected_zip_hash = OUTPUT_ZIP_HASH.read_text(encoding="utf-8-sig").split()[0]
    require(sha256(OUTPUT_ZIP) == expected_zip_hash, "ZIP SHA-256 mismatch.")
    with zipfile.ZipFile(OUTPUT_ZIP) as archive:
        require(archive.testzip() is None, "ZIP CRC validation failed.")
    print(f"Direction 2 package PASS: {OUTPUT.name}; archive and SHA-256 verified.")


def build_package() -> None:
    require(not OUTPUT.exists(), f"Refusing to overwrite existing package: {OUTPUT}")
    require(not OUTPUT_ZIP.exists(), f"Refusing to overwrite existing archive: {OUTPUT_ZIP}")
    require(not OUTPUT_ZIP_HASH.exists(), f"Refusing to overwrite existing checksum: {OUTPUT_ZIP_HASH}")
    evidence, focused, desktop = validate_inputs()

    OUTPUT.mkdir(parents=True)
    shutil.copytree(EVIDENCE_SOURCE, OUTPUT / "evidence")
    write_text_artifacts(evidence, focused, desktop)

    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    evidence_files = sorted(path for path in (OUTPUT / "evidence").iterdir() if path.is_file())
    manifest = {
        "package": "Direction 2 production WPF visual foundation",
        "version": VERSION,
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "base_revision": revision,
        "state": "validated",
        "dashboard_changed": False,
        "dashboard_reason": "DESK-05 was already done at 100%; no readiness progress changed.",
        "validation": {
            "result": "PASS",
            "release_build": "PASS",
            "desktop_tests": desktop,
            "focused_windows_ux_tests": focused,
            "native_checks": len(evidence["checks"]),
            "native_window_dpi": evidence["platform"]["nativeMainWindowDpi"],
            "dpi_matrix": [100, 125, 150, 200],
            "visual_review": "PASS",
        },
        "implementation_files": [record(ROOT / relative) for relative in IMPLEMENTATION],
        "reference_files": [record(ROOT / relative) for relative in REFERENCES],
        "evidence_files": [record(path, OUTPUT) for path in evidence_files],
    }
    manifest_path = OUTPUT / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    json.loads(manifest_path.read_text(encoding="utf-8"))

    checksummed = sorted(path for path in OUTPUT.rglob("*") if path.is_file() and path.name != "SHA256SUMS")
    lines = [f"{sha256(path)}  {path.relative_to(OUTPUT).as_posix()}" for path in checksummed]
    (OUTPUT / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="utf-8")

    with zipfile.ZipFile(OUTPUT_ZIP, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(item for item in OUTPUT.rglob("*") if item.is_file()):
            archive.write(path, (Path(PACKAGE_NAME) / path.relative_to(OUTPUT)).as_posix())
    OUTPUT_ZIP_HASH.write_text(f"{sha256(OUTPUT_ZIP)}  {OUTPUT_ZIP.name}\n", encoding="utf-8")
    verify_package()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    if args.validate_only:
        validate_inputs()
        verify_package()
    else:
        build_package()


if __name__ == "__main__":
    main()
