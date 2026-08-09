from __future__ import annotations

import hashlib
import json
import os
import shutil
import sys
import zipfile
from pathlib import Path, PurePosixPath


ROOT = Path(r"C:\Users\novik\Таск")
WORK = ROOT / "work" / "stage_4_2_audit"
INPUTS = [
    {
        "name": "audit_input",
        "path": ROOT / "outputs" / "Organizer_Stage4_2_Audit_Input.zip",
        "expected_sha256": "4CC6DF2A7CF54F3E692971BDB2A39322615442748E95AD7104A1564229CD845F",
    },
    {
        "name": "candidate",
        "path": ROOT / "outputs" / "Organizer_Stage4_PRD_Candidate_4.1.2.zip",
        "expected_sha256": "84260071D3917AE00AA617FDBF2E5AB540A719F7D717367B0504E36159845AF9",
    },
]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def is_unsafe(name: str) -> bool:
    posix = PurePosixPath(name.replace("\\", "/"))
    return posix.is_absolute() or ".." in posix.parts or (
        len(posix.parts) > 0 and ":" in posix.parts[0]
    )


def safe_extract(zf: zipfile.ZipFile, destination: Path) -> None:
    destination_resolved = destination.resolve()
    for info in zf.infolist():
        if is_unsafe(info.filename):
            raise RuntimeError(f"Unsafe ZIP member: {info.filename}")
        target = (destination / info.filename).resolve()
        if os.path.commonpath([destination_resolved, target]) != str(destination_resolved):
            raise RuntimeError(f"ZIP member escapes destination: {info.filename}")
        if info.is_dir():
            target.mkdir(parents=True, exist_ok=True)
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        with zf.open(info, "r") as source, target.open("xb") as sink:
            shutil.copyfileobj(source, sink)


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    results: list[dict[str, object]] = []
    for item in INPUTS:
        path = item["path"]
        assert isinstance(path, Path)
        actual_sha = sha256(path)
        result: dict[str, object] = {
            "name": item["name"],
            "path": str(path),
            "size_bytes": path.stat().st_size,
            "expected_sha256": item["expected_sha256"],
            "actual_sha256": actual_sha,
            "sha256_pass": actual_sha == item["expected_sha256"],
        }
        destination = WORK / str(item["name"])
        with zipfile.ZipFile(path, "r") as zf:
            infos = zf.infolist()
            result.update(
                {
                    "entry_count": len(infos),
                    "crc_test_bad_member": zf.testzip(),
                    "crc_pass": zf.testzip() is None,
                    "unsafe_members": [i.filename for i in infos if is_unsafe(i.filename)],
                    "empty_files": [
                        i.filename for i in infos if not i.is_dir() and i.file_size == 0
                    ],
                    "temporary_files": [
                        i.filename
                        for i in infos
                        if Path(i.filename).name.startswith(("~$", ".~", "tmp"))
                        or Path(i.filename).suffix.lower() in {".tmp", ".bak", ".swp"}
                    ],
                    "members": [
                        {
                            "name": i.filename,
                            "size": i.file_size,
                            "compressed_size": i.compress_size,
                            "crc32": f"{i.CRC:08X}",
                        }
                        for i in infos
                    ],
                }
            )
            if result["unsafe_members"]:
                raise RuntimeError(f"Unsafe ZIP members in {path}")
            if not destination.exists():
                safe_extract(zf, destination)

        with zipfile.ZipFile(path, "r") as reopened:
            result["reopen_pass"] = reopened.testzip() is None
            result["reopened_entry_count"] = len(reopened.infolist())
        extracted_files = [p for p in destination.rglob("*") if p.is_file()]
        result["extracted_file_count"] = len(extracted_files)
        result["extracted_empty_files"] = [
            str(p.relative_to(destination)).replace("\\", "/")
            for p in extracted_files
            if p.stat().st_size == 0
        ]
        results.append(result)

    print(json.dumps(results, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
