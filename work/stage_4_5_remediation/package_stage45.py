from __future__ import annotations

import hashlib
import shutil
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
CANDIDATE = WORK / "candidate_4_5"
STAGING = WORK / "final_audit_input"
OUTPUTS = ROOT / "outputs"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def manifest_for(folder: Path, title: str, exclude: set[str] | None = None) -> None:
    exclude = exclude or set()
    files = sorted(path for path in folder.rglob("*") if path.is_file() and path.relative_to(folder).as_posix() not in exclude)
    lines = [f"# {title}", "", "| File | Bytes | SHA-256 |", "|---|---:|---|"]
    for file in files:
        lines.append(f"| {file.relative_to(folder).as_posix()} | {file.stat().st_size} | `{sha256(file)}` |")
    lines += ["", f"Artifact count: {len(files)}", ""]
    (folder / "00_MANIFEST.md").write_text("\n".join(lines), encoding="utf-8")


def verify_manifest(folder: Path) -> None:
    text = (folder / "00_MANIFEST.md").read_text(encoding="utf-8")
    for file in folder.rglob("*"):
        if file.is_file() and file.name != "00_MANIFEST.md":
            relative = file.relative_to(folder).as_posix()
            if relative not in text or sha256(file) not in text:
                raise RuntimeError(f"Manifest does not attest {relative}")


def build_zip(folder: Path, target: Path) -> tuple[str, int]:
    if target.exists():
        target.unlink()
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for file in sorted(path for path in folder.rglob("*") if path.is_file()):
            archive.write(file, file.relative_to(folder).as_posix())
    with zipfile.ZipFile(target) as archive:
        failed = archive.testzip()
        if failed:
            raise RuntimeError(f"CRC failure: {failed}")
        for info in archive.infolist():
            archive.read(info.filename)
        names = archive.namelist()
        if "00_MANIFEST.md" not in names:
            raise RuntimeError("Package manifest is missing")
    with zipfile.ZipFile(target) as reopened:
        if reopened.testzip():
            raise RuntimeError("Archive failed reopening")
    digest = sha256(target)
    target.with_suffix(target.suffix + ".sha256").write_text(f"{digest}  {target.name}\n", encoding="ascii")
    return digest, len(names)


def copy(source: Path, target: Path) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)


def main() -> None:
    expected = {
        "Stage_4_Product_PRD_4.5.md", "Stage_4_Module_PRDs_4.5.md", "Stage_4_Business_Rules_Catalog_4.5.csv",
        "Stage_4_Acceptance_Criteria_Catalog_4.5.csv", "Stage_4_NFR_Catalog_4.5.csv", "Stage_4_Analytics_Audit_Requirements_4.5.md",
        "Stage_4_Requirements_Traceability_4.5.csv", "Stage_4_Dependency_Risk_Register_4.5.md", "Stage_4_Decision_Log_4.5.md",
        "Stage_4_Open_Questions_4.5.md", "Stage_4_Candidate_Validation_4.5.md", "Stage_4_0_PRD_Readiness_4.5.md",
        "Stage_4_5_Remediation_Plan.md", "Stage_4_5_Remediation_Registry.csv", "Stage_4_5_Remediation_Report.md",
        "Stage_4_5_AC_Atomicity_Analysis.csv", "Stage_4_5_STATE_Resolution.csv", "Stage_4_5_Reference_Validation.md",
        "Stage_4_5_Independent_Precheck.md",
    }
    actual_without_manifest = {path.name for path in CANDIDATE.iterdir() if path.is_file() and path.name != "00_MANIFEST.md"}
    if actual_without_manifest != expected:
        raise RuntimeError(f"Candidate artifact set mismatch: {actual_without_manifest ^ expected}")
    manifest_for(CANDIDATE, "Organizer Stage 4 PRD Candidate 4.5 manifest", {"00_MANIFEST.md"})
    verify_manifest(CANDIDATE)
    candidate_zip = OUTPUTS / "Organizer_Stage4_PRD_Candidate_4.5.zip"
    candidate_hash, candidate_entries = build_zip(CANDIDATE, candidate_zip)
    if candidate_entries != 20:
        raise RuntimeError(f"Candidate ZIP has {candidate_entries}, expected 20")
    if STAGING.exists():
        shutil.rmtree(STAGING)
    STAGING.mkdir()
    copy(candidate_zip, STAGING / candidate_zip.name)
    for name in [
        "00_MANIFEST.md", "Stage_4_5_Remediation_Registry.csv", "Stage_4_5_Remediation_Report.md",
        "Stage_4_5_AC_Atomicity_Analysis.csv", "Stage_4_5_STATE_Resolution.csv", "Stage_4_Candidate_Validation_4.5.md",
        "Stage_4_5_Independent_Precheck.md", "Stage_4_Requirements_Traceability_4.5.csv", "Stage_4_Acceptance_Criteria_Catalog_4.5.csv",
    ]:
        copy(CANDIDATE / name, STAGING / "candidate_evidence" / name)
    for name in ["Stage_4_4_Findings.csv", "Stage_4_4_Finding_Verification.csv", "Stage_4_4_FR_BR_AC_Audit.csv", "Stage_4_4_Reference_Audit.csv"]:
        copy(WORK / "inputs" / "audit_4_4" / name, STAGING / "audit_4_4" / name)
    stage2 = WORK / "inputs" / "stage2_3_1" / "stage_2_3"
    for name in ["00_MANIFEST.md", "MANIFEST.json", "catalogs/api_catalog.csv", "catalogs/permissions.csv", "catalogs/errors.csv", "catalogs/traceability.csv"]:
        copy(stage2 / name, STAGING / "normative_stage2_3_1" / name)
    for name in ["00_MANIFEST.md", "Stage_3_Screen_Catalog_Final_3.5.md", "Stage_3_User_Flows_Final_3.5.md", "Stage_3_State_Matrix_Final_3.5.md", "Stage_3_Decision_Log_Final_3.5.md", "Stage_3_UX_API_Traceability_Final_3.5.md"]:
        copy(WORK / "inputs" / "stage3_5" / name, STAGING / "normative_stage3_5" / name)
    (STAGING / "VERIFICATION_CRITERIA.md").write_text("""# Stage 4.6 final audit input — verification criteria

Verify the two remediated Medium findings without performing remediation in this package:

1. Every historical AC-1825..AC-1911 is analyzed and its final mapping is atomic; no remediated AC has more than one related FR or independent outcome.
2. Active candidate references use only published Stage 3.5 numeric State IDs, published named State Matrix behaviors, or stable-error/UI conditions; historical aliases remain fully mapped.
3. Recheck the residual findings AUDIT-4.2-004 and AUDIT-4.2-006, plus OQ-001, OQ-003 and MOD-014 regression.
4. Confirm API coverage 244/244, no orphaned requirements, no unknown permission/error/UX reference, and no broken references.
""", encoding="utf-8")
    manifest_for(STAGING, "Organizer Stage 4.6 final audit input manifest", {"00_MANIFEST.md"})
    verify_manifest(STAGING)
    audit_zip = OUTPUTS / "Organizer_Stage4_6_Final_Audit_Input.zip"
    audit_hash, audit_entries = build_zip(STAGING, audit_zip)
    validations = [
        (OUTPUTS / "Organizer_Stage4_PRD_Candidate_4.5.validation.md", candidate_zip.name, candidate_hash, candidate_entries, "20 candidate artifacts; CRC PASS; full read PASS; reopen PASS; manifest PASS."),
        (OUTPUTS / "Organizer_Stage4_6_Final_Audit_Input.validation.md", audit_zip.name, audit_hash, audit_entries, "CRC PASS; full read PASS; reopen PASS; manifest PASS."),
    ]
    for path, name, digest, entries, result in validations:
        path.write_text(f"# Package validation\n\n- Archive: `{name}`\n- SHA-256: `{digest}`\n- Entries: {entries}\n- Result: {result}\n", encoding="utf-8")
    print(f"CANDIDATE={candidate_zip.name}\nSHA256={candidate_hash}\nENTRIES={candidate_entries}\nAUDIT_INPUT={audit_zip.name}\nSHA256={audit_hash}\nENTRIES={audit_entries}")


if __name__ == "__main__":
    main()
