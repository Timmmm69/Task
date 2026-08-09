from __future__ import annotations

import hashlib
import json
import shutil
import zipfile
from pathlib import Path


ROOT = Path(r"C:\Users\novik\Таск")
WORK = ROOT / "work" / "stage_4_4_reaudit"
AUDIT_DIR = ROOT / "outputs" / "stage_4_4_reaudit"
REMEDIATION_DIR = WORK / "remediation_input"
REPORT_ZIP = ROOT / "outputs" / "Organizer_Stage4_4_Reaudit_Report.zip"
REMEDIATION_ZIP = ROOT / "outputs" / "Organizer_Stage4_5_Remediation_Input.zip"
FIXED_TIME = (2026, 7, 26, 12, 0, 0)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def manifest(title: str, version: str, directory: Path, extra: list[str]) -> str:
    rows = [
        f"# {title}", "", f"- Version: `{version}`", "- Status: independent audit / remediation input; not a final baseline.",
        "- Built: `2026-07-26`", *extra, "", "| File | Bytes | SHA-256 |", "|---|---:|---|",
    ]
    for file in sorted((item for item in directory.iterdir() if item.is_file() and item.name != "00_MANIFEST.md"), key=lambda item: item.name.casefold()):
        rows.append(f"| `{file.name}` | {file.stat().st_size} | `{sha256(file)}` |")
    rows.extend(["", "The manifest does not hash itself. Package validation checks each listed member, CRC, full read and reopen.", ""])
    return "\n".join(rows)


def build_zip(directory: Path, destination: Path) -> None:
    if destination.exists():
        destination.unlink()
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for file in sorted((item for item in directory.iterdir() if item.is_file()), key=lambda item: item.name.casefold()):
            info = zipfile.ZipInfo(file.name, FIXED_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, file.read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)


def verify(directory: Path, archive_path: Path) -> dict:
    expected = {file.name for file in directory.iterdir() if file.is_file()}
    manifest_text = (directory / "00_MANIFEST.md").read_text(encoding="utf-8")
    manifest_failures = [
        file.name for file in directory.iterdir()
        if file.is_file() and file.name != "00_MANIFEST.md" and (f"`{file.name}`" not in manifest_text or f"`{sha256(file)}`" not in manifest_text)
    ]
    with zipfile.ZipFile(archive_path) as archive:
        names = archive.namelist()
        crc_failure = archive.testzip()
        complete_read = sum(len(archive.read(name)) for name in names)
    with zipfile.ZipFile(archive_path) as reopened:
        reopen = reopened.namelist() == names
    return {
        "path": str(archive_path), "sha256": sha256(archive_path), "members": len(names), "crc_pass": crc_failure is None,
        "complete_read_bytes": complete_read, "reopen_pass": reopen, "inventory_pass": set(names) == expected,
        "manifest_pass": not manifest_failures, "manifest_failures": manifest_failures,
    }


def write_sha(path: Path) -> None:
    Path(str(path) + ".sha256").write_text(f"{sha256(path)} *{path.name}\n", encoding="utf-8")


def main() -> None:
    evidence = json.loads((WORK / "audit_evidence.json").read_text(encoding="utf-8"))
    if evidence["verdict"] != "FAIL":
        raise RuntimeError("This packager is only valid for a FAIL verdict")
    audit_manifest = AUDIT_DIR / "00_MANIFEST.md"
    audit_manifest.write_text(
        manifest(
            "Organizer Stage 4.4 Independent Re-audit — Manifest", "4.4-audit.1", AUDIT_DIR,
            ["- Verdict: `FAIL`.", "- New findings: `2 Medium`.", "- Candidate 4.3 SHA-256: `952BC37316AAAAC9F1C18EA8DD8FFC1214E1490730DDB5C5AD31ADA84017691F`."],
        ), encoding="utf-8"
    )

    resolved = REMEDIATION_DIR.resolve()
    if resolved.parent != WORK.resolve():
        raise RuntimeError(f"Unsafe remediation directory: {resolved}")
    if REMEDIATION_DIR.exists():
        shutil.rmtree(REMEDIATION_DIR)
    REMEDIATION_DIR.mkdir(parents=True)
    scope = """# Stage 4.5 Remediation Scope

## Open findings

1. **AUDIT-4.4-001 (Medium):** replace AC-1825..AC-1911 broad generated templates with atomic, requirement-level criteria or an explicitly defined bounded parameterized matrix. Every replacement must identify one owner, exact contract context and one observable result.
2. **AUDIT-4.4-002 (Medium):** resolve each retained STATE-001..STATE-039 reference not published by Stage 3.5 through a source-controlled candidate-level mapping to one exact Stage 3.5 behavior; preserve historical aliases.

## Re-opened original findings

- AUDIT-4.2-004: Partially Fixed.
- AUDIT-4.2-006: Partially Fixed.

No new product scope, API, DTO, permission or stable error is authorized by this remediation input.
"""
    criteria = """# Stage 4.5 Verification Criteria

## AC remediation

- Every replacement AC has one existing primary owner and a semantically narrow Related FR set.
- Given, When and Then describe one bounded behavior and one observable result.
- A criterion must not use catch-all phrases such as `any read or command`, `each applicable error` or a module-wide FR list as its sole test scope.
- Duplicate/template-only AC are rejected unless a documented parameterized test-matrix scheme gives each row a bounded requirement and expected result.

## STATE remediation

- Each candidate STATE ID must resolve to a published Stage 3.5 behavior or an explicit downstream mapping.
- The mapping states historical ID, exact Stage 3.5 state/behavior, affected candidate references and replacement/alias policy.
- Re-scan candidate references; unresolved STATE IDs must equal zero.
"""
    (REMEDIATION_DIR / "Stage_4_5_Remediation_Scope.md").write_text(scope, encoding="utf-8")
    (REMEDIATION_DIR / "Stage_4_5_Verification_Criteria.md").write_text(criteria, encoding="utf-8")
    copies = {
        AUDIT_DIR / "Stage_4_4_Findings.csv": "Stage_4_4_Findings.csv",
        AUDIT_DIR / "Stage_4_4_Finding_Verification.csv": "Stage_4_4_Finding_Verification.csv",
        AUDIT_DIR / "Stage_4_4_Reference_Audit.csv": "Stage_4_4_Reference_Audit.csv",
        AUDIT_DIR / "Stage_4_4_Independent_Audit_Report.md": "Stage_4_4_Independent_Audit_Report.md",
        WORK / "inputs" / "candidate_4_3" / "Stage_4_Acceptance_Criteria_Catalog_4.3.csv": "Stage_4_Acceptance_Criteria_Catalog_4.3.csv",
        WORK / "inputs" / "candidate_4_3" / "Stage_4_Requirements_Traceability_4.3.csv": "Stage_4_Requirements_Traceability_4.3.csv",
        WORK / "inputs" / "candidate_4_3" / "Stage_4_3_Reference_Repair_Report.md": "Stage_4_3_Reference_Repair_Report.md",
        WORK / "inputs" / "stage3_5" / "Stage_3_State_Matrix_Final_3.5.md": "Normative_Stage_3_State_Matrix_3.5.md",
    }
    for source, destination in copies.items():
        if not source.is_file():
            raise FileNotFoundError(source)
        shutil.copy2(source, REMEDIATION_DIR / destination)
    (REMEDIATION_DIR / "00_MANIFEST.md").write_text(
        manifest("Organizer Stage 4.5 Remediation Input — Manifest", "4.5-remediation-input.1", REMEDIATION_DIR, ["- Based on Stage 4.4 independent FAIL.", "- Contains only open findings, exact affected artifacts/IDs, normative evidence and verification criteria."]),
        encoding="utf-8"
    )

    build_zip(AUDIT_DIR, REPORT_ZIP)
    build_zip(REMEDIATION_DIR, REMEDIATION_ZIP)
    write_sha(REPORT_ZIP)
    write_sha(REMEDIATION_ZIP)
    result = {"audit_report": verify(AUDIT_DIR, REPORT_ZIP), "remediation_input": verify(REMEDIATION_DIR, REMEDIATION_ZIP)}
    if not all(value["crc_pass"] and value["reopen_pass"] and value["inventory_pass"] and value["manifest_pass"] for value in result.values()):
        raise RuntimeError("Package validation failed")
    (WORK / "package_validation.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
