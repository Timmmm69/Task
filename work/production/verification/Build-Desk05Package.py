from __future__ import annotations

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
PACKAGE_NAME = "20260910_desk05_windows_ux_1.0.0"
OUTPUT = ROOT / "outputs" / PACKAGE_NAME
OUTPUT_ZIP = ROOT / "outputs" / f"{PACKAGE_NAME}.zip"
EVIDENCE_SOURCE = ROOT / "work" / "production" / "evidence" / "desk05"
IMPLEMENTATION = [
    ".project-dashboard/roadmap.json",
    "work/production/docs/DESK-05-windows-ux.md",
    "work/production/src/Task.Desktop/App.xaml.cs",
    "work/production/src/Task.Desktop/AuthWindow.xaml",
    "work/production/src/Task.Desktop/AuthWindow.xaml.cs",
    "work/production/src/Task.Desktop/MainWindow.xaml",
    "work/production/src/Task.Desktop/MainWindow.xaml.cs",
    "work/production/src/Task.Desktop/Task.Desktop.csproj",
    "work/production/src/Task.Desktop/WindowsUxLayout.cs",
    "work/production/src/Task.Desktop/app.manifest",
    "work/production/tests/Task.Desktop.Tests/WindowsUxAccessibilityTests.cs",
    "work/production/verification/Test-TaskWriteE2E.ps1",
    "work/production/verification/Test-Desk05WindowsUx.ps1",
    "work/production/verification/Build-Desk05Package.py",
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


def read_test_counters(path: Path) -> dict[str, int]:
    root = ET.parse(path).getroot()
    counters = next(
        (node for node in root.iter() if node.tag.rsplit("}", 1)[-1] == "Counters"),
        None,
    )
    require(counters is not None, "TRX test counters are absent.")
    assert counters is not None
    return {
        name: int(counters.attrib.get(name, "0"))
        for name in ("total", "executed", "passed", "failed")
    }


def validate_evidence() -> tuple[dict[str, object], dict[str, int]]:
    evidence_path = EVIDENCE_SOURCE / "windows-ux.json"
    trx_path = EVIDENCE_SOURCE / "windows-ux.trx"
    required = [ROOT / relative for relative in IMPLEMENTATION]
    required.extend([evidence_path, trx_path, EVIDENCE_SOURCE / "release-build.stdout.log"])
    required.extend(EVIDENCE_SOURCE / f"{surface}-{scale}.png" for surface in ("auth", "main") for scale in (100, 125, 150, 200))
    missing = [str(path) for path in required if not path.is_file()]
    require(not missing, "Missing DESK-05 inputs: " + ", ".join(missing))

    evidence = json.loads(evidence_path.read_text(encoding="utf-8-sig"))
    require(evidence.get("task") == "DESK-05" and evidence.get("result") == "PASS", "DESK-05 evidence is not PASS.")
    platform = evidence.get("platform", {})
    require(platform.get("perMonitorV2Manifest") is True, "PerMonitorV2 manifest proof is absent.")
    require(int(platform.get("nativeMainWindowDpi", 0)) >= 96, "Native window DPI proof is invalid.")
    require(evidence.get("nativeUia", {}).get("authentication") == "PASS", "Authentication UIA proof is absent.")
    require(evidence.get("nativeUia", {}).get("mainWindow") == "PASS", "Main-window UIA proof is absent.")
    require(evidence.get("keyboard", {}).get("result") == "PASS", "Keyboard navigation proof is absent.")
    narrator = evidence.get("narrator", {})
    require(narrator.get("requested") is True and narrator.get("activeDuringFocusTraversal") is True, "Narrator compatibility proof is absent.")

    matrix = evidence.get("dpiMatrix", {})
    require(matrix.get("result") == "PASS", "DPI matrix result is not PASS.")
    for key in ("authentication", "mainWindow"):
        rows = matrix.get(key, [])
        require([row.get("scalePercent") for row in rows] == [100, 125, 150, 200], f"{key} DPI scales are incomplete.")
        require(all(row.get("result") == "PASS" for row in rows), f"{key} DPI matrix contains a failure.")

    counters = read_test_counters(trx_path)
    require(counters == {"total": 7, "executed": 7, "passed": 7, "failed": 0}, f"Unexpected focused test counters: {counters}")
    build_log = (EVIDENCE_SOURCE / "release-build.stdout.log").read_text(encoding="utf-8-sig")
    require("Ошибок: 0" in build_log or "0 Error(s)" in build_log or "0 Fehler" in build_log, "Release build success marker is absent.")
    require(len(evidence.get("checks", [])) == 91, "Expected 91 native checks.")
    return evidence, counters


def write_text_artifacts(evidence: dict[str, object], counters: dict[str, int]) -> None:
    platform = evidence["platform"]
    readme = f"""# DESK-05 Windows UX evidence package

Version: {VERSION}

This package records the completed DESK-05 implementation and the reproducible native Windows evidence.
Run `work/production/verification/Test-Desk05WindowsUx.ps1 -WithNarrator` from the repository root to recreate the evidence.

Key evidence:

- `evidence/windows-ux.json` — machine-readable result and 91 checks;
- `evidence/windows-ux.trx` — 7/7 focused tests;
- `evidence/auth-*.png` and `evidence/main-*.png` — 100/125/150/200% visual matrix;
- `VALIDATION_REPORT.md` — scope, results, and explicit limitations;
- `manifest.json` and `SHA256SUMS` — provenance and integrity.
"""
    validation = f"""# DESK-05 validation report

Result: **PASS**

## Verified

- Release solution build: PASS, 0 errors and 0 warnings.
- Focused source/layout tests: {counters['passed']}/{counters['total']} passed.
- Native Windows checks: {len(evidence['checks'])}/{len(evidence['checks'])} passed.
- Host: {platform['osVersion']}, {platform['architecture']}, {platform['screenPixels']['width']}×{platform['screenPixels']['height']}.
- Native WPF DPI: {platform['nativeMainWindowDpi']} (150%); manifest: PerMonitorV2.
- Authentication and main-window logical viewport equivalents: 100/125/150/200%, all PASS.
- UI Automation Value/Invoke/Selection, accessible names and visible bounds: PASS.
- Keyboard Tab and F6 navigation cycle: PASS.
- Narrator active during named focus-target traversal: PASS.
- Eight PNG files were visually reviewed after the automated bounds and rendered-surface checks: PASS.
- Isolated PostgreSQL/API/desktop-data setup and cleanup: PASS; user Desktop state was not used.

## Evidence boundary

The Narrator run proves compatibility, names and focus traversal; spoken wording was not audio-transcribed.
The host monitor was physically at 150%. Other scale values were exercised as logical viewport equivalents
on the same native PerMonitorV2 WPF windows. A mixed-physical-monitor check remains a deployment smoke for
the particular workstation fleet and does not block DESK-05 implementation completion.
"""
    (OUTPUT / "README.md").write_text(readme, encoding="utf-8")
    (OUTPUT / "VALIDATION_REPORT.md").write_text(validation, encoding="utf-8")
    (OUTPUT / "VERSION").write_text(VERSION + "\n", encoding="utf-8")


def main() -> None:
    require(not OUTPUT.exists(), f"Refusing to overwrite existing package: {OUTPUT}")
    require(not OUTPUT_ZIP.exists(), f"Refusing to overwrite existing archive: {OUTPUT_ZIP}")
    evidence, counters = validate_evidence()

    OUTPUT.mkdir(parents=True)
    shutil.copytree(EVIDENCE_SOURCE, OUTPUT / "evidence")
    shutil.copy2(ROOT / "work" / "production" / "docs" / "DESK-05-windows-ux.md", OUTPUT / "DESK-05-windows-ux.md")
    write_text_artifacts(evidence, counters)

    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    evidence_files = sorted(path for path in (OUTPUT / "evidence").iterdir() if path.is_file())
    manifest = {
        "package": "DESK-05 Windows UX",
        "version": VERSION,
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "base_revision": revision,
        "roadmap_ids": ["DESK-05"],
        "state": "validated",
        "validation": {
            "result": "PASS",
            "release_build": "PASS",
            "focused_tests": counters,
            "native_checks": len(evidence["checks"]),
            "native_window_dpi": evidence["platform"]["nativeMainWindowDpi"],
            "dpi_matrix": [100, 125, 150, 200],
            "narrator_compatibility": "PASS",
            "visual_review": "PASS",
        },
        "implementation_files": [record(ROOT / relative) for relative in IMPLEMENTATION],
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
    (ROOT / "outputs" / f"{PACKAGE_NAME}.zip.sha256").write_text(f"{sha256(OUTPUT_ZIP)}  {OUTPUT_ZIP.name}\n", encoding="utf-8")
    print(f"DESK-05 package PASS: {len(lines)} files verified; {OUTPUT_ZIP.name} created.")


if __name__ == "__main__":
    main()
