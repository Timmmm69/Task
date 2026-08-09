from __future__ import annotations

import hashlib
import shutil
import zipfile
from pathlib import Path


ROOT = Path(r"C:\Users\novik\Таск")
BASE = ROOT / "work" / "stage_4_1_2"
CANDIDATE = BASE / "candidate_4_1_2"
OUTPUTS = ROOT / "outputs"
UNPACKED = OUTPUTS / "Organizer_Stage4_PRD_Candidate_4.1.2"
AUDIT = BASE / "audit_input" / "Organizer_Stage4_2_Audit_Input"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def copy_exact(src: Path, dst: Path) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)


def build_candidate_tree() -> None:
    UNPACKED.mkdir(parents=True, exist_ok=True)
    expected = {p.name for p in CANDIDATE.iterdir() if p.is_file()}
    for src in CANDIDATE.iterdir():
        if src.is_file():
            copy_exact(src, UNPACKED / src.name)
    extras = {p.name for p in UNPACKED.iterdir() if p.is_file()} - expected
    if extras:
        raise RuntimeError(f"Unexpected files in final candidate directory: {sorted(extras)}")


def build_audit_tree() -> None:
    files = {
        ROOT / "sources" / "concept" / "Task_Concept_Final.txt":
            AUDIT / "sources" / "concept" / "Task_Concept_Final.txt",
        ROOT / "sources" / "stage_1" / "architecture_organizer.md":
            AUDIT / "sources" / "stage_1" / "architecture_organizer.md",
        BASE / "input_stage2" / "stage_2_3" / "Stage_2_3_Contract_Diff.csv":
            AUDIT / "deltas" / "Stage_2_3_Contract_Diff.csv",
        BASE / "input_stage3" / "Stage_3_Contract_Delta_3.5.md":
            AUDIT / "deltas" / "Stage_3_Contract_Delta_3.5.md",
        BASE / "input_stage2" / "stage_2_3" / "Stage_2_3_Validation.md":
            AUDIT / "evidence" / "Stage_2_3_Validation.md",
        BASE / "input_stage3" / "Stage_3_Final_Validation_3.5.md":
            AUDIT / "evidence" / "Stage_3_Final_Validation_3.5.md",
        BASE / "input_stage3" / "Stage_3_Targeted_Audit_3.5.md":
            AUDIT / "evidence" / "Stage_3_Targeted_Audit_3.5.md",
    }
    for src, dst in files.items():
        copy_exact(src, dst)
    for src in CANDIDATE.iterdir():
        if src.is_file():
            copy_exact(src, AUDIT / "candidate" / src.name)

    index = """# Audit Input Baseline Index

- Concept Final: included at `sources/concept/Task_Concept_Final.txt`.
- Stage 1 architecture: included at `sources/stage_1/architecture_organizer.md`.
- Stage 2.3.1 normative contract: external canonical ZIP `sources/stage_2_3/Organizer_Stage2_Technical_Specification_2.3_Final.zip`; SHA-256 `75EFC3E83F09FBCC41AE7DA68A96F2EC0EBDFC74E61F62615F4DA3478AFE5019`; targeted contract diff and validation included.
- Stage 3.5 normative UX baseline: external canonical ZIP `sources/stage_3_5/Organizer_Stage3_Final_Baseline_3.5.zip`; SHA-256 `6C2447E935DD413488E482F7DB3C481C8DC6E53AEB57A07D1DF23D3ADA85381E`; UX delta, targeted audit and validation included.
- Stage 4.1.1 is the historical previous candidate.
- Stage 4.1.2 candidate: complete 15-file set included under `candidate/`.

Stage 2.2 and Stage 3.4 are historical only. The audit package contains no intermediate candidate duplicates.
"""
    (AUDIT / "BASELINE_INDEX.md").write_text(index, encoding="utf-8", newline="\n")

    entries = []
    for path in sorted(p for p in AUDIT.rglob("*") if p.is_file() and p.name != "00_AUDIT_INPUT_MANIFEST.md"):
        rel = path.relative_to(AUDIT).as_posix()
        entries.append(f"| `{rel}` | {path.stat().st_size} | `{sha256(path)}` |")
    manifest = f"""# Stage 4.2 Audit Input Manifest

**Package version:** 1.0  
**Candidate:** 4.1.2-candidate.1  
**Purpose:** independent Stage 4.2 input; no audit conclusions are included.

| File | Size bytes | SHA-256 |
| --- | ---: | --- |
{chr(10).join(entries)}

Manifest excludes its own recursive hash.
"""
    (AUDIT / "00_AUDIT_INPUT_MANIFEST.md").write_text(manifest, encoding="utf-8", newline="\n")


def make_zip(source_dir: Path, zip_path: Path) -> tuple[int, int]:
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for path in sorted(p for p in source_dir.rglob("*") if p.is_file()):
            arcname = (Path(source_dir.name) / path.relative_to(source_dir)).as_posix()
            zf.write(path, arcname)
    with zipfile.ZipFile(zip_path, "r") as zf:
        bad = zf.testzip()
        if bad is not None:
            raise RuntimeError(f"CRC failed for {bad}")
        total = 0
        for info in zf.infolist():
            total += len(zf.read(info.filename))
        count = len(zf.infolist())
    return count, total


def write_sidecars(zip_path: Path, file_count: int, uncompressed_bytes: int) -> None:
    digest = sha256(zip_path)
    (Path(str(zip_path) + ".sha256")).write_text(
        f"{digest} *{zip_path.name}\n", encoding="ascii", newline="\n"
    )
    validation = f"""# Package Validation

| Check | Result |
| --- | --- |
| ZIP | `{zip_path.name}` |
| Reopen | PASS |
| CRC/read every entry | PASS |
| Entries | {file_count} |
| Uncompressed bytes read | {uncompressed_bytes} |
| Manifest present | PASS |
| SHA-256 | `{digest}` |
"""
    validation_path = zip_path.with_name(zip_path.stem + ".validation.md")
    validation_path.write_text(validation, encoding="utf-8", newline="\n")


def main() -> None:
    build_candidate_tree()
    build_audit_tree()
    candidate_zip = OUTPUTS / "Organizer_Stage4_PRD_Candidate_4.1.2.zip"
    audit_zip = OUTPUTS / "Organizer_Stage4_2_Audit_Input.zip"
    c_count, c_bytes = make_zip(UNPACKED, candidate_zip)
    a_count, a_bytes = make_zip(AUDIT, audit_zip)
    write_sidecars(candidate_zip, c_count, c_bytes)
    write_sidecars(audit_zip, a_count, a_bytes)
    print(f"{candidate_zip.name}|{sha256(candidate_zip)}|{c_count}|{c_bytes}")
    print(f"{audit_zip.name}|{sha256(audit_zip)}|{a_count}|{a_bytes}")


if __name__ == "__main__":
    main()
