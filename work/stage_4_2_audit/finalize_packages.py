from __future__ import annotations

import csv
import hashlib
import json
import shutil
import zipfile
from datetime import datetime
from pathlib import Path


ROOT = Path(r"C:\Users\novik\Таск")
AUDIT_DIR = ROOT / "outputs" / "stage_4_2_audit"
WORK_DIR = ROOT / "work" / "stage_4_2_audit"
REMEDIATION_DIR = WORK_DIR / "remediation_input"
AUDIT_ZIP = ROOT / "outputs" / "Organizer_Stage4_2_Audit_Report.zip"
REMEDIATION_ZIP = ROOT / "outputs" / "Organizer_Stage4_3_Remediation_Input.zip"

VERSION = "4.2-audit.1"
AUDIT_INPUT_SHA = "4CC6DF2A7CF54F3E692971BDB2A39322615442748E95AD7104A1564229CD845F"
CANDIDATE_SHA = "84260071D3917AE00AA617FDBF2E5AB540A719F7D717367B0504E36159845AF9"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content.replace("\r\n", "\n").rstrip() + "\n", encoding="utf-8")


def manifest_table(files: list[Path], base: Path) -> str:
    rows = ["| File | Bytes | SHA-256 |", "|---|---:|---|"]
    for path in sorted(files, key=lambda item: item.relative_to(base).as_posix().lower()):
        relative = path.relative_to(base).as_posix()
        rows.append(f"| `{relative}` | {path.stat().st_size} | `{sha256(path)}` |")
    return "\n".join(rows)


def create_audit_manifest() -> None:
    files = [path for path in AUDIT_DIR.iterdir() if path.is_file() and path.name != "00_MANIFEST.md"]
    content = f"""# 00_MANIFEST — Stage 4.2 Independent Audit

**Package version:** {VERSION}  
**Audit date:** 2026-07-26  
**Candidate:** Organizer Stage 4 PRD Candidate 4.1.2  
**Verdict:** **FAIL**  
**Findings:** Critical 0 / High 4 / Medium 10 / Low 2 / Observation 0

## Verified source identity

- Audit Input SHA-256: `{AUDIT_INPUT_SHA}`
- Candidate ZIP SHA-256: `{CANDIDATE_SHA}`
- Input ZIP CRC, read-to-completion, reopen and internal manifest checks: **PASS**

## Independently verified inventory

- Modules: 21
- FR / BR / AC / NFR: 279 / 113 / 1824 / 25
- OpenAPI operations structurally mapped: 244 / 244
- FR without AC: 0
- AC without direct FR linkage: 466
- Requirements orphaned from verification: 87
- Unknown permissions / stable errors / duplicate IDs: 0 / 0 / 0
- Broken local source references: 1 target / 1565 occurrences
- Unverified / provisional: 1 / 1

## Package contents

This manifest inventories the other 14 artifacts. Its own digest is intentionally excluded to avoid a recursive hash.

{manifest_table(files, AUDIT_DIR)}
"""
    write_text(AUDIT_DIR / "00_MANIFEST.md", content)


def create_remediation_materials() -> None:
    REMEDIATION_DIR.mkdir(parents=True, exist_ok=True)

    selected = {
        "Stage_4_2_Executive_Summary.md",
        "Stage_4_2_Audit_Report.md",
        "Stage_4_2_Findings.csv",
        "Stage_4_2_Remediation_Plan.md",
        "Stage_4_2_Traceability_Audit.csv",
        "Stage_4_2_API_Coverage_Audit.csv",
        "Stage_4_2_FR_BR_AC_Audit.csv",
        "Stage_4_2_OQ_001_Audit.md",
        "Stage_4_2_OQ_003_Audit.md",
        "Stage_4_2_NFR_Audit.md",
        "Stage_4_2_UX_Accessibility_Audit.md",
        "Stage_4_2_Permissions_Security_Audit.md",
        "Stage_4_2_Design_Readiness.md",
        "Stage_4_2_Independent_Validation.md",
    }
    for name in selected:
        shutil.copy2(AUDIT_DIR / name, REMEDIATION_DIR / name)

    candidate_manifest = (
        WORK_DIR
        / "candidate"
        / "Organizer_Stage4_PRD_Candidate_4.1.2"
        / "00_MANIFEST.md"
    )
    if candidate_manifest.exists():
        shutil.copy2(candidate_manifest, REMEDIATION_DIR / "Candidate_4.1.2_00_MANIFEST.md")

    impacted = """# Stage 4.3 — Exact Impacted PRD References

**Input version:** 4.2-audit.1  
**Purpose:** finite edit map for remediation of Candidate 4.1.2.

| Audit ID | Exact impacted reference | Required edit |
|---|---|---|
| AUDIT-4.2-001 | `Stage_4_Product_PRD_4.1.2.md:186,229`; `Stage_4_Dependency_Risk_Register_4.1.2.md:63`; OQ register entries OQ-001/OQ-003 | Establish one current status and remove contradictory blocking/open language or retract Fixed. |
| AUDIT-4.2-002 | Appendix P.2, FR-159, FR-160, FR-243, FR-244, FR-260, FR-261, FR-265, FR-266, FR-269 and their linked AC | Replace legacy AC with criteria that test the effective addendum text. Preserve identifiers or provide a migration map. |
| AUDIT-4.2-003 | MOD-014 main contract near lines 4446 and 4508; addendum near lines 6814–6817 and 6862–6873 | Consolidate search types to include `employee`, set maxItems=10, and replace obsolete AC-070 language. |
| AUDIT-4.2-004 | 211 AC identified in `Stage_4_2_FR_BR_AC_Audit.csv` | Supply explicit Given/When/Then precondition, action and observable result. |
| AUDIT-4.2-005 | 466 AC rows with blank `Direct FR` | Add direct FR linkage or classify as non-FR verification with an explicit parent rule. |
| AUDIT-4.2-006 | DATA-002, DATA-003, DATA-016 and all PERM/ERR/SYNC/AUDIT requirements flagged in traceability audit | Add verification AC or an approved verification-method reference. |
| AUDIT-4.2-007 | 96 BR rows with blank direct FR | Add Related FR mappings or document an approved BR-only hierarchy. |
| AUDIT-4.2-008 | All references to `Stage_3_Field_Traceability.csv` | Replace with `Stage_3_Field_Traceability_Final_3.5.csv`; re-run reference resolution. |
| AUDIT-4.2-009 | Requirement-trace occurrences of FLOW-038; Stage 3.5 flow catalogue | Define FLOW-038 or replace every occurrence with the intended existing FLOW identifier. |
| AUDIT-4.2-010 | Active trace source fields naming Stage 3.4 or Stage 2.2 | Repoint current normative mappings to Stage 3.5 / Stage 2.3.1, retaining historical provenance separately. |
| AUDIT-4.2-011 | NFR-001, NFR-003, NFR-006, NFR-007, NFR-015, NFR-024; OQ-008 | Add measurable thresholds, measurement method, environment and acceptance boundary; resolve provisional status. |
| AUDIT-4.2-012 | RISK-001–RISK-025, especially RISK-022–RISK-025 | Add probability, owner, trigger, impact and testable mitigation/contingency. |
| AUDIT-4.2-013 | CMP-001; combobox/listbox behavior; Esc focus return; adaptive behavior below 1100 logical px | Add atomic keyboard, focus, active-descendant, tab-order and narrow-window requirements. |
| AUDIT-4.2-014 | Product PRD line 191; Readiness report lines 107 and 127 | Replace stale 241-operation claims with independently verified 244. |
| AUDIT-4.2-015 | AC-1486, AC-1487, AC-1501, AC-1579, AC-1709, AC-1710, AC-1715, AC-1716, AC-1767 | Replace “корректно” with an observable, testable result. |
| AUDIT-4.2-016 | OQ-010 analytics-retention decision | Define retention duration, deletion/anonymization behavior, owner and approval evidence. |
"""
    write_text(REMEDIATION_DIR / "Stage_4_3_Exact_Impacted_References.md", impacted)

    source_index = f"""# Stage 4.3 — Normative Source Index

The remediation must preserve the project's precedence order. No source artifact was modified during Stage 4.2.

| Priority | Normative input | Stage 4.2 use |
|---:|---|---|
| 1 | `sources/concept/Task_Concept_Final.txt` | Business intent and scope guardrail |
| 2 | `sources/stage_1/architecture_organizer.md` | Stage 1 architecture baseline |
| 3 | Stage 2.3.1 contract embedded in Audit Input | Effective technical contract; verified ZIP SHA-256 `75EFC3E83F09FBCC41AE7DA68A96F2ECDFC74E61F62615F4DA3478AFE5019` |
| 4 | Stage 3.5 baseline embedded in Audit Input | Effective UX baseline; verified ZIP SHA-256 `6C2447E935DD413488E482F7DB3C481C8DC6E53AEB57A07D1DF23D3ADA85381E` |
| 5 | Candidate 4.1.2 | Artifact under audit; SHA-256 `{CANDIDATE_SHA}` |

## Stage 4.3 evidence rules

- Treat Stage 2.2 and Stage 3.4 references as historical provenance, not current normative targets.
- Resolve conflicts by the project precedence order and record each affected identifier.
- Do not silently change business requirements.
- Recalculate counts from the remediated artifacts rather than carrying forward declared totals.
"""
    # Correct the source hash if the prior prose typo ever appears.
    source_index = source_index.replace(
        "75EFC3E83F09FBCC41AE7DA68A96F2ECDFC74E61F62615F4DA3478AFE5019",
        "75EFC3E83F09FBCC41AE7DA68A96F2EC0EBDFC74E61F62615F4DA3478AFE5019",
    )
    write_text(REMEDIATION_DIR / "Stage_4_3_Normative_Source_Index.md", source_index)

    verification = """# Stage 4.3 — Verification Criteria

Stage 4.3 is complete only when all checks below are evidenced by regenerated artifacts.

1. Critical=0 and High=0 in the repeated independent audit.
2. OQ-001 and OQ-003 each have one non-contradictory status across PRD, OQ register, risk register and readiness report.
3. MOD-014 contains one search contract: `employee` is consistently supported, maxItems is consistently 10, and AC-070 no longer states the opposite.
4. All ten updated FR in Appendix P.2 have semantically aligned, executable AC.
5. Every AC has an approved parent; every FR and every required cross-cutting rule has verification evidence.
6. Every normative AC has explicit precondition, action and observable result; vague outcome words are eliminated.
7. All local source paths resolve; zero references remain to the nonexistent Stage 3 field-trace filename.
8. Every FLOW/SCR/STATE reference resolves to an addressable Stage 3.5 definition or an explicitly versioned replacement.
9. Unknown permission codes=0, unknown stable errors=0, and every OpenAPI operation has valid access/error references.
10. OpenAPI operation coverage remains 244/244 after regeneration; stale 241 claims are absent.
11. Unverified=0 and provisional=0, including resolution of NFR-024/OQ-008.
12. NFR thresholds state metric, environment, method, sample/window and pass boundary.
13. Risk entries contain owner, probability, impact, trigger, mitigation and contingency.
14. Accessibility verification covers active descendant, Up/Down, Esc focus return, CMP-001 tab order and sub-1100-logical-pixel behavior.
15. New package manifest, internal file hashes, external ZIP SHA-256, CRC/read-to-completion and reopen checks all pass.
"""
    write_text(REMEDIATION_DIR / "Stage_4_3_Verification_Criteria.md", verification)

    files = [path for path in REMEDIATION_DIR.iterdir() if path.is_file() and path.name != "00_MANIFEST.md"]
    content = f"""# 00_MANIFEST — Stage 4.3 Remediation Input

**Package version:** {VERSION}  
**Source candidate:** Organizer Stage 4 PRD Candidate 4.1.2  
**Candidate SHA-256:** `{CANDIDATE_SHA}`  
**Stage 4.2 verdict:** **FAIL**  
**Stage 4.3 required:** **Yes**

This is a bounded remediation input, not a replacement PRD. It contains audit evidence, exact impacted references, the normative-source index and objective re-verification criteria. Its own digest is excluded to avoid a recursive hash.

{manifest_table(files, REMEDIATION_DIR)}
"""
    write_text(REMEDIATION_DIR / "00_MANIFEST.md", content)


def make_deterministic_zip(source: Path, target: Path) -> None:
    fixed_time = (2026, 7, 26, 12, 0, 0)
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(source.rglob("*"), key=lambda item: item.relative_to(source).as_posix().lower()):
            if not path.is_file():
                continue
            relative = path.relative_to(source).as_posix()
            info = zipfile.ZipInfo(relative, fixed_time)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, path.read_bytes(), compresslevel=9)


def validate_csv(path: Path, expected_rows: int, expected_columns: set[str]) -> dict:
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        rows = list(reader)
        columns = set(reader.fieldnames or [])
    return {
        "rows": len(rows),
        "expected_rows": expected_rows,
        "row_count_pass": len(rows) == expected_rows,
        "required_columns_pass": expected_columns.issubset(columns),
        "blank_header_pass": "" not in columns,
    }


def validate_zip(path: Path, expected_entries: int) -> dict:
    with zipfile.ZipFile(path) as archive:
        bad = archive.testzip()
        names = archive.namelist()
        for name in names:
            with archive.open(name) as stream:
                while stream.read(1024 * 1024):
                    pass
    with zipfile.ZipFile(path) as reopened:
        reopen_names = reopened.namelist()
    return {
        "entries": len(names),
        "expected_entries": expected_entries,
        "entry_count_pass": len(names) == expected_entries,
        "crc_pass": bad is None,
        "read_to_completion_pass": True,
        "reopen_pass": names == reopen_names,
        "duplicate_names_pass": len(names) == len(set(names)),
        "unsafe_paths_pass": all(
            not name.startswith(("/", "\\"))
            and ".." not in Path(name).parts
            and ":" not in name
            for name in names
        ),
    }


def main() -> None:
    create_audit_manifest()
    create_remediation_materials()

    make_deterministic_zip(AUDIT_DIR, AUDIT_ZIP)
    make_deterministic_zip(REMEDIATION_DIR, REMEDIATION_ZIP)

    audit_sha = sha256(AUDIT_ZIP)
    remediation_sha = sha256(REMEDIATION_ZIP)
    write_text(AUDIT_ZIP.with_suffix(AUDIT_ZIP.suffix + ".sha256"), f"{audit_sha}  {AUDIT_ZIP.name}")
    write_text(
        REMEDIATION_ZIP.with_suffix(REMEDIATION_ZIP.suffix + ".sha256"),
        f"{remediation_sha}  {REMEDIATION_ZIP.name}",
    )

    csv_results = {
        "findings": validate_csv(
            AUDIT_DIR / "Stage_4_2_Findings.csv",
            16,
            {"Audit ID", "Severity", "Artifact", "Location", "Defect", "Recommended fix", "Verification"},
        ),
        "traceability": validate_csv(
            AUDIT_DIR / "Stage_4_2_Traceability_Audit.csv",
            497,
            {"Requirement", "Type", "Source present", "Status", "Notes"},
        ),
        "api": validate_csv(
            AUDIT_DIR / "Stage_4_2_API_Coverage_Audit.csv",
            244,
            {"Operation ID", "Method", "Path", "Coverage status", "Evidence"},
        ),
        "fr_br_ac": validate_csv(
            AUDIT_DIR / "Stage_4_2_FR_BR_AC_Audit.csv",
            2216,
            {"Entity ID", "Type", "Parent exists", "Source present", "Status"},
        ),
    }
    audit_files = list(AUDIT_DIR.iterdir())
    nonempty = all(path.stat().st_size > 0 for path in audit_files if path.is_file())
    validation = {
        "version": VERSION,
        "audit_zip": {
            "path": str(AUDIT_ZIP),
            "sha256": audit_sha,
            **validate_zip(AUDIT_ZIP, 15),
        },
        "remediation_zip": {
            "path": str(REMEDIATION_ZIP),
            "sha256": remediation_sha,
            **validate_zip(REMEDIATION_ZIP, len(list(REMEDIATION_DIR.iterdir()))),
        },
        "audit_output_file_count": len(audit_files),
        "audit_outputs_nonempty_pass": nonempty,
        "csv": csv_results,
    }
    validation["all_pass"] = (
        validation["audit_zip"]["entry_count_pass"]
        and validation["audit_zip"]["crc_pass"]
        and validation["audit_zip"]["reopen_pass"]
        and validation["remediation_zip"]["entry_count_pass"]
        and validation["remediation_zip"]["crc_pass"]
        and validation["remediation_zip"]["reopen_pass"]
        and nonempty
        and all(
            item["row_count_pass"] and item["required_columns_pass"] and item["blank_header_pass"]
            for item in csv_results.values()
        )
    )
    write_text(WORK_DIR / "final_validation.json", json.dumps(validation, ensure_ascii=False, indent=2))
    print(json.dumps(validation, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
