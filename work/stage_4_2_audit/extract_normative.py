from __future__ import annotations

import hashlib
import json
import sys
import zipfile
from pathlib import Path

from integrity_extract import safe_extract


ROOT = Path(r"C:\Users\novik\Таск")
WORK = ROOT / "work" / "stage_4_2_audit"
PACKAGES = [
    (
        "stage_2_3_1",
        ROOT / "sources" / "stage_2_3" / "Organizer_Stage2_Technical_Specification_2.3_Final.zip",
        "75EFC3E83F09FBCC41AE7DA68A96F2EC0EBDFC74E61F62615F4DA3478AFE5019",
    ),
    (
        "stage_3_5",
        ROOT / "sources" / "stage_3_5" / "Organizer_Stage3_Final_Baseline_3.5.zip",
        "6C2447E935DD413488E482F7DB3C481C8DC6E53AEB57A07D1DF23D3ADA85381E",
    ),
]


def hash_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    rows: list[dict[str, object]] = []
    for name, archive, expected in PACKAGES:
        actual = hash_file(archive)
        destination = WORK / name
        with zipfile.ZipFile(archive, "r") as zf:
            bad = zf.testzip()
            if bad is not None:
                raise RuntimeError(f"CRC failure in {archive}: {bad}")
            if not destination.exists():
                safe_extract(zf, destination)
        with zipfile.ZipFile(archive, "r") as reopened:
            reopened_bad = reopened.testzip()
            entries = len(reopened.infolist())
        rows.append(
            {
                "name": name,
                "archive": str(archive),
                "expected_sha256": expected,
                "actual_sha256": actual,
                "sha256_pass": actual == expected,
                "crc_pass": bad is None,
                "reopen_pass": reopened_bad is None,
                "entries": entries,
                "extracted_files": sum(1 for p in destination.rglob("*") if p.is_file()),
            }
        )
    print(json.dumps(rows, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
