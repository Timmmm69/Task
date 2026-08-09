from __future__ import annotations

import hashlib
import json
import shutil
import zipfile
from pathlib import Path


ROOT = Path(r"C:\Users\novik\Таск")
WORK = ROOT / "work" / "stage_4_3_remediation"
CANDIDATE = WORK / "final_candidate"
OUTPUTS = ROOT / "outputs"
REAUDIT = WORK / "reaudit_input"

CANDIDATE_ZIP = OUTPUTS / "Organizer_Stage4_PRD_Candidate_4.3.zip"
REAUDIT_ZIP = OUTPUTS / "Organizer_Stage4_4_Reaudit_Input.zip"
FIXED_TIME = (2026, 7, 26, 12, 0, 0)

EXPECTED_CANDIDATE = [
    "00_MANIFEST.md",
    "Stage_4_Product_PRD_4.3.md",
    "Stage_4_Module_PRDs_4.3.md",
    "Stage_4_Business_Rules_Catalog_4.3.csv",
    "Stage_4_Acceptance_Criteria_Catalog_4.3.csv",
    "Stage_4_NFR_Catalog_4.3.csv",
    "Stage_4_Analytics_Audit_Requirements_4.3.md",
    "Stage_4_Requirements_Traceability_4.3.csv",
    "Stage_4_Dependency_Risk_Register_4.3.md",
    "Stage_4_Decision_Log_4.3.md",
    "Stage_4_Open_Questions_4.3.md",
    "Stage_4_Candidate_Validation_4.3.md",
    "Stage_4_0_PRD_Readiness_4.3.md",
    "Stage_4_3_Remediation_Registry.csv",
    "Stage_4_3_Remediation_Report.md",
    "Stage_4_3_MOD_014_Conflict_Analysis.md",
    "Stage_4_3_Reference_Repair_Report.md",
    "Stage_4_3_Independent_Precheck.md",
]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def write_sha_file(path: Path) -> Path:
    target = Path(str(path) + ".sha256")
    target.write_text(f"{sha256(path)} *{path.name}\n", encoding="utf-8")
    return target


def manifest_text(title: str, version: str, files: list[Path], extra: list[str]) -> str:
    lines = [
        f"# {title}",
        "",
        f"- Version: `{version}`",
        "- Status: candidate / independent re-audit input; not a final baseline.",
        "- Built: `2026-07-26`",
        *extra,
        "",
        "| File | Bytes | SHA-256 |",
        "|---|---:|---|",
    ]
    for path in sorted(files, key=lambda item: item.name.casefold()):
        lines.append(f"| `{path.name}` | {path.stat().st_size} | `{sha256(path)}` |")
    lines.extend(
        [
            "",
            "The manifest does not hash itself. Package verification hashes and checks every other listed member, then verifies ZIP CRC, complete reads and reopen.",
            "",
        ]
    )
    return "\n".join(lines)


def build_zip(source_dir: Path, destination: Path) -> None:
    if destination.exists():
        destination.unlink()
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(source_dir.iterdir(), key=lambda item: item.name.casefold()):
            if not path.is_file():
                continue
            info = zipfile.ZipInfo(path.name, FIXED_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, path.read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)


def verify_zip(path: Path, expected_names: set[str]) -> dict:
    with zipfile.ZipFile(path, "r") as archive:
        names = archive.namelist()
        bad_crc = archive.testzip()
        total_read = sum(len(archive.read(name)) for name in names)
    with zipfile.ZipFile(path, "r") as reopened:
        reopened_names = reopened.namelist()
    actual_names = set(names)
    return {
        "path": str(path),
        "sha256": sha256(path),
        "members": len(names),
        "crc_pass": bad_crc is None,
        "complete_read_bytes": total_read,
        "reopen_pass": reopened_names == names,
        "manifest_inventory_pass": actual_names == expected_names,
        "missing": sorted(expected_names - actual_names),
        "unexpected": sorted(actual_names - expected_names),
    }


def verify_manifest(directory: Path, manifest_name: str = "00_MANIFEST.md") -> dict:
    manifest = (directory / manifest_name).read_text(encoding="utf-8")
    failures: list[str] = []
    checked = 0
    for path in directory.iterdir():
        if not path.is_file() or path.name == manifest_name:
            continue
        checked += 1
        if f"`{path.name}`" not in manifest or f"`{sha256(path)}`" not in manifest:
            failures.append(path.name)
    return {"checked": checked, "pass": not failures, "failures": failures}


def main() -> None:
    OUTPUTS.mkdir(parents=True, exist_ok=True)
    manifest_path = CANDIDATE / "00_MANIFEST.md"
    if manifest_path.exists():
        manifest_path.unlink()

    current = sorted(path.name for path in CANDIDATE.iterdir() if path.is_file())
    expected_without_manifest = sorted(name for name in EXPECTED_CANDIDATE if name != "00_MANIFEST.md")
    if current != expected_without_manifest:
        raise RuntimeError(
            f"Candidate inventory mismatch before manifest. Missing={sorted(set(expected_without_manifest)-set(current))}; "
            f"unexpected={sorted(set(current)-set(expected_without_manifest))}"
        )

    candidate_files = [CANDIDATE / name for name in expected_without_manifest]
    manifest_path.write_text(
        manifest_text(
            "Organizer Stage 4 PRD Candidate 4.3 — Manifest",
            "4.3-candidate.1",
            candidate_files,
            [
                "- Artifact count: `18` including this manifest.",
                "- Counts: `21 modules / 279 FR / 113 BR / 1911 AC / 25 NFR`.",
                "- API coverage: `244/244`; finding-affected field coverage: `21/21`.",
                "- Findings: `16 Fixed / 0 Rejected / 0 Open`.",
                "- Input candidate SHA-256: `84260071D3917AE00AA617FDBF2E5AB540A719F7D717367B0504E36159845AF9`.",
                "- Audit 4.2 SHA-256: `359EFBCA60A5D84FC5FFB23469B72E46A32477331F2F2AAF229F8BE2A9BE0115`.",
                "- Remediation input SHA-256: `2495AF8559169FB7F0507E1B0ACF0B20B5EBC11C1E83708F2E8BF1DC068C6729`.",
            ],
        ),
        encoding="utf-8",
    )

    build_zip(CANDIDATE, CANDIDATE_ZIP)
    candidate_sha_file = write_sha_file(CANDIDATE_ZIP)
    candidate_check = verify_zip(CANDIDATE_ZIP, set(EXPECTED_CANDIDATE))
    candidate_manifest_check = verify_manifest(CANDIDATE)
    if not all(
        [
            candidate_check["crc_pass"],
            candidate_check["reopen_pass"],
            candidate_check["manifest_inventory_pass"],
            candidate_manifest_check["pass"],
        ]
    ):
        raise RuntimeError("Candidate package validation failed")

    reaudit_resolved = REAUDIT.resolve()
    work_resolved = WORK.resolve()
    if reaudit_resolved.parent != work_resolved:
        raise RuntimeError(f"Unsafe reaudit directory: {reaudit_resolved}")
    if REAUDIT.exists():
        shutil.rmtree(REAUDIT)
    REAUDIT.mkdir(parents=True)

    copy_map = {
        CANDIDATE_ZIP: CANDIDATE_ZIP.name,
        candidate_sha_file: candidate_sha_file.name,
        CANDIDATE / "Stage_4_3_Remediation_Registry.csv": "Stage_4_3_Remediation_Registry.csv",
        CANDIDATE / "Stage_4_3_Remediation_Report.md": "Stage_4_3_Remediation_Report.md",
        WORK / "input_remediation" / "Stage_4_2_Findings.csv": "Stage_4_2_Findings.csv",
        WORK / "stage_4_3_validation.json": "Stage_4_3_Machine_Validation.json",
        WORK / "semantic_maps.json": "Stage_4_3_Semantic_Maps.json",
        WORK / "semantic_mapping_report.md": "Stage_4_3_Semantic_Mapping_Report.md",
        WORK / "INPUT_VALIDATION.md": "Stage_4_3_Input_Validation.md",
        WORK / "input_remediation" / "Stage_4_3_Normative_Source_Index.md": "Stage_4_3_Normative_Source_Index.md",
        ROOT / "sources" / "stage_2_3" / "Organizer_Stage2_Technical_Specification_2.3_Final.validation.md": "Stage_2_3_1_Normative_Package_Validation.md",
        ROOT / "sources" / "stage_3_5" / "Organizer_Stage3_Final_Baseline_3.5.validation.md": "Stage_3_5_Normative_Package_Validation.md",
        CANDIDATE / "Stage_4_Requirements_Traceability_4.3.csv": "Stage_4_Requirements_Traceability_4.3.csv",
        CANDIDATE / "Stage_4_Business_Rules_Catalog_4.3.csv": "Stage_4_Business_Rules_Catalog_4.3.csv",
        CANDIDATE / "Stage_4_Acceptance_Criteria_Catalog_4.3.csv": "Stage_4_Acceptance_Criteria_Catalog_4.3.csv",
        CANDIDATE / "Stage_4_NFR_Catalog_4.3.csv": "Stage_4_NFR_Catalog_4.3.csv",
        CANDIDATE / "Stage_4_Open_Questions_4.3.md": "Stage_4_Open_Questions_4.3.md",
        CANDIDATE / "Stage_4_Candidate_Validation_4.3.md": "Stage_4_Candidate_Validation_4.3.md",
        CANDIDATE / "Stage_4_3_Independent_Precheck.md": "Stage_4_3_Independent_Precheck.md",
        CANDIDATE / "Stage_4_3_MOD_014_Conflict_Analysis.md": "Stage_4_3_MOD_014_Conflict_Analysis.md",
        CANDIDATE / "Stage_4_3_Reference_Repair_Report.md": "Stage_4_3_Reference_Repair_Report.md",
        CANDIDATE / "00_MANIFEST.md": "Candidate_4.3_00_MANIFEST.md",
    }
    for source, destination_name in copy_map.items():
        if not source.is_file():
            raise FileNotFoundError(source)
        shutil.copy2(source, REAUDIT / destination_name)

    reaudit_files = [path for path in REAUDIT.iterdir() if path.is_file()]
    (REAUDIT / "00_MANIFEST.md").write_text(
        manifest_text(
            "Organizer Stage 4.4 Re-audit Input — Manifest",
            "4.4-reaudit-input.1",
            reaudit_files,
            [
                f"- Embedded candidate SHA-256: `{sha256(CANDIDATE_ZIP)}`.",
                "- Purpose: independent Stage 4.4 re-audit only.",
                "- The prior 4.1.2 candidate is not embedded.",
            ],
        ),
        encoding="utf-8",
    )

    expected_reaudit = {path.name for path in REAUDIT.iterdir() if path.is_file()}
    build_zip(REAUDIT, REAUDIT_ZIP)
    reaudit_sha_file = write_sha_file(REAUDIT_ZIP)
    reaudit_check = verify_zip(REAUDIT_ZIP, expected_reaudit)
    reaudit_manifest_check = verify_manifest(REAUDIT)
    if not all(
        [
            reaudit_check["crc_pass"],
            reaudit_check["reopen_pass"],
            reaudit_check["manifest_inventory_pass"],
            reaudit_manifest_check["pass"],
        ]
    ):
        raise RuntimeError("Re-audit package validation failed")

    report = {
        "candidate": candidate_check,
        "candidate_manifest": candidate_manifest_check,
        "reaudit": reaudit_check,
        "reaudit_manifest": reaudit_manifest_check,
        "sha256_files": [str(candidate_sha_file), str(reaudit_sha_file)],
        "all_pass": True,
    }
    report_path = OUTPUTS / "Stage_4_3_Package_Validation.json"
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
