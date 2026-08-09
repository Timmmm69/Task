from __future__ import annotations

import hashlib
import shutil
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
INPUTS = WORK / "inputs"

PACKAGES = {
    "candidate_4_3": (ROOT / "outputs" / "Organizer_Stage4_PRD_Candidate_4.3.zip", "952BC37316AAAAC9F1C18EA8DD8FFC1214E1490730DDB5C5AD31ADA84017691F"),
    "audit_4_4": (ROOT / "outputs" / "Organizer_Stage4_4_Reaudit_Report.zip", "A568C8437E37703CBB46E8F9DC15BC7812004E7E81FCCA48B911A7CCEF0FB003"),
    "remediation_input": (ROOT / "outputs" / "Organizer_Stage4_5_Remediation_Input.zip", "18377B19BF48159F322C228CCB938C5089751A12180CD048F5CBC82B492479B4"),
    "stage2_3_1": (ROOT / "sources" / "stage_2_3" / "Organizer_Stage2_Technical_Specification_2.3_Final.zip", None),
    "stage3_5": (ROOT / "sources" / "stage_3_5" / "Organizer_Stage3_Final_Baseline_3.5.zip", None),
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def extract_checked(label: str, archive: Path, expected: str | None) -> list[str]:
    actual = sha256(archive)
    if expected and actual != expected:
        raise RuntimeError(f"{label}: SHA-256 mismatch: {actual}")
    destination = INPUTS / label
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    with zipfile.ZipFile(archive) as zf:
        bad = zf.testzip()
        if bad:
            raise RuntimeError(f"{label}: CRC error in {bad}")
        names = zf.namelist()
        for info in zf.infolist():
            if info.is_dir():
                continue
            # Full read before extraction catches corrupt content beyond CRC metadata.
            zf.read(info.filename)
        zf.extractall(destination)
    with zipfile.ZipFile(archive) as reopened:
        if reopened.testzip():
            raise RuntimeError(f"{label}: archive failed reopen")
    return [actual, str(len(names)), ", ".join(n for n in names if n.upper().endswith("MANIFEST.MD")) or "not present"]


def main() -> None:
    INPUTS.mkdir(parents=True, exist_ok=True)
    result = []
    for label, (archive, expected) in PACKAGES.items():
        if not archive.exists():
            raise FileNotFoundError(archive)
        actual, count, manifests = extract_checked(label, archive, expected)
        result.append((label, archive.name, actual, count, manifests, "PASS"))
    body = [
        "# Stage 4.5 input validation",
        "",
        "All ZIP inputs were hash-checked where a supplied hash exists, CRC-tested, fully read, extracted into the isolated work area, and reopened.",
        "",
        "| Input | Archive | SHA-256 | Entries | Manifest entries | Result |",
        "|---|---|---|---:|---|---|",
    ]
    body += [f"| {label} | {name} | `{digest}` | {count} | {manifests} | {status} |" for label, name, digest, count, manifests, status in result]
    body += ["", "No source archive was modified.", ""]
    (WORK / "INPUT_VALIDATION.md").write_text("\n".join(body), encoding="utf-8")


if __name__ == "__main__":
    main()
