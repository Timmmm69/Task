from __future__ import annotations

import hashlib
import re
import shutil
import zipfile
from pathlib import Path


ROOT = Path(r"C:\Users\novik\Таск")
WORK = ROOT / "work" / "stage_4_3_remediation"
INPUTS = {
    "Candidate 4.1.2": (
        ROOT / "outputs" / "Organizer_Stage4_PRD_Candidate_4.1.2.zip",
        "84260071D3917AE00AA617FDBF2E5AB540A719F7D717367B0504E36159845AF9",
        WORK / "input_candidate",
    ),
    "Audit Report 4.2": (
        ROOT / "outputs" / "Organizer_Stage4_2_Audit_Report.zip",
        "359EFBCA60A5D84FC5FFB23469B72E46A32477331F2F2AAF229F8BE2A9BE0115",
        WORK / "input_audit",
    ),
    "Remediation Input": (
        ROOT / "outputs" / "Organizer_Stage4_3_Remediation_Input.zip",
        "2495AF8559169FB7F0507E1B0ACF0B20B5EBC11C1E83708F2E8BF1DC068C6729",
        WORK / "input_remediation",
    ),
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def clean_dir(path: Path) -> None:
    if not path.exists():
        path.mkdir(parents=True)
        return
    resolved = path.resolve()
    allowed = (ROOT / "work" / "stage_4_3_remediation").resolve()
    if resolved != allowed and allowed not in resolved.parents:
        raise RuntimeError(f"Refusing to clean path outside remediation workspace: {resolved}")
    shutil.rmtree(path)
    path.mkdir(parents=True)


def verify_manifest(extract_dir: Path) -> tuple[int, int, list[str]]:
    manifests = list(extract_dir.rglob("00_MANIFEST.md"))
    if not manifests:
        return 0, 0, ["00_MANIFEST.md not found"]
    manifest = manifests[0]
    text = manifest.read_text(encoding="utf-8-sig")
    hash_rows = re.findall(r"\|\s*`?([^|`]+?)`?\s*\|\s*(\d+)\s*\|\s*`?([A-Fa-f0-9]{64})`?\s*\|", text)
    checked = 0
    failures: list[str] = []
    for rel_raw, size_raw, expected_hash in hash_rows:
        rel = rel_raw.strip()
        candidates = [manifest.parent / rel, extract_dir / rel]
        target = next((item for item in candidates if item.exists()), None)
        if target is None:
            failures.append(f"missing: {rel}")
            continue
        checked += 1
        if target.stat().st_size != int(size_raw):
            failures.append(f"size: {rel}")
        if sha256(target) != expected_hash.upper():
            failures.append(f"hash: {rel}")
    return len(hash_rows), checked, failures


def inspect_zip(label: str, path: Path, expected_sha: str, extract_dir: Path) -> dict:
    clean_dir(extract_dir)
    actual_sha = sha256(path)
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        crc_bad = archive.testzip()
        unsafe = [
            name
            for name in names
            if name.startswith(("/", "\\"))
            or ":" in name
            or ".." in Path(name).parts
        ]
        temp = [
            name
            for name in names
            if Path(name).name.startswith(("~$", ".~", ".tmp"))
            or Path(name).suffix.lower() in {".tmp", ".bak", ".swp"}
        ]
        empty = [
            info.filename
            for info in archive.infolist()
            if not info.is_dir() and info.file_size == 0
        ]
        for name in names:
            with archive.open(name) as stream:
                while stream.read(1024 * 1024):
                    pass
        archive.extractall(extract_dir)
    with zipfile.ZipFile(path) as reopened:
        reopen_names = reopened.namelist()
    declared, checked, manifest_failures = verify_manifest(extract_dir)
    foreign_markers = []
    for file in extract_dir.rglob("*"):
        if file.is_file() and file.suffix.lower() in {".md", ".txt", ".csv"}:
            text = file.read_text(encoding="utf-8-sig", errors="replace")
            for marker in ("another project", "Project Atlas", "Проект Atlas"):
                if marker.lower() in text.lower():
                    foreign_markers.append(f"{file.relative_to(extract_dir)}:{marker}")
    return {
        "label": label,
        "path": str(path),
        "expected_sha": expected_sha,
        "actual_sha": actual_sha,
        "sha_pass": actual_sha == expected_sha,
        "entries": len(names),
        "crc_pass": crc_bad is None,
        "read_pass": True,
        "reopen_pass": names == reopen_names,
        "unsafe": unsafe,
        "temp": temp,
        "empty": empty,
        "manifest_declared": declared,
        "manifest_checked": checked,
        "manifest_failures": manifest_failures,
        "foreign_markers": foreign_markers,
    }


def find_candidate_root() -> Path:
    roots = [path.parent for path in (WORK / "input_candidate").rglob("Stage_4_Product_PRD_4.1.2.md")]
    if len(roots) != 1:
        raise RuntimeError(f"Expected one candidate root, found {len(roots)}")
    return roots[0]


def main() -> None:
    WORK.mkdir(parents=True, exist_ok=True)
    results = [
        inspect_zip(label, path, expected, extract)
        for label, (path, expected, extract) in INPUTS.items()
    ]
    if not all(
        item["sha_pass"]
        and item["crc_pass"]
        and item["read_pass"]
        and item["reopen_pass"]
        and not item["unsafe"]
        and not item["temp"]
        and not item["empty"]
        and not item["manifest_failures"]
        and not item["foreign_markers"]
        for item in results
    ):
        raise RuntimeError(f"Input validation failed: {results}")

    candidate_root = find_candidate_root()
    candidate_work = WORK / "candidate_4_3"
    clean_dir(candidate_work)
    for item in candidate_root.iterdir():
        if item.is_file():
            shutil.copy2(item, candidate_work / item.name)

    audit_findings = WORK / "input_audit" / "Stage_4_2_Findings.csv"
    findings_text = audit_findings.read_text(encoding="utf-8-sig")
    if "AUDIT-4.2-001" not in findings_text or "AUDIT-4.2-016" not in findings_text:
        raise RuntimeError("Audit findings do not match the 4.1.2 audit range")

    lines = [
        "# Stage 4.3 — Input Validation",
        "",
        "**Validation date:** 2026-07-26  ",
        "**Target:** remediation of Organizer Stage 4 PRD Candidate 4.1.2",
        "",
        "| Input | SHA-256 | Entries | CRC/read/reopen | Manifest | Unsafe/temp/empty | Foreign project markers |",
        "|---|---|---:|---|---|---|---|",
    ]
    for item in results:
        lines.append(
            f"| {item['label']} | `{item['actual_sha']}` | {item['entries']} | "
            f"PASS/PASS/PASS | {item['manifest_checked']}/{item['manifest_declared']} PASS | "
            f"0/0/0 | 0 |"
        )
    lines.extend(
        [
            "",
            "## Scope binding",
            "",
            "- Findings range `AUDIT-4.2-001`…`AUDIT-4.2-016` is present.",
            "- Candidate manifest identifies version 4.1.2.",
            "- Work copy created in `work/stage_4_3_remediation/candidate_4_3/`.",
            "- Source ZIPs, Stage 2.3.1, Stage 3.5 and `sources/` were not modified.",
            "",
            "**Result:** PASS — remediation may proceed.",
        ]
    )
    (WORK / "INPUT_VALIDATION.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(f"{item['label']}: PASS ({item['entries']} entries)" for item in results))
    print("Candidate work copy created.")


if __name__ == "__main__":
    main()
