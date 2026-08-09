from __future__ import annotations

import hashlib
import json
import re
import zipfile
from pathlib import Path


PROJECT = Path(__file__).resolve().parents[2]
OUTPUTS = PROJECT / "outputs"
ARCHIVES = [
    (
        OUTPUTS / "Organizer_Stage2_Technical_Specification_2.3_Final.zip",
        "stage_2_3",
        {
            "Stage_2_3_Validation.md",
            "Stage_2_3_Runtime_Validation.md",
            "Stage_2_3_Contract_Diff.csv",
            "Stage_2_3_Fix_Registry.md",
            "Stage_2_3_Backward_Compatibility.md",
            "Stage_2_3_Migration_Test_Report.md",
            "Stage_2_3_Codegen_Report.md",
            "Stage_2_3_Redocly_Report.md",
            "00_MANIFEST.md",
        },
    ),
    (
        OUTPUTS / "Organizer_Stage3_5_Contract_Delta_Input_Final.zip",
        "stage_3_5_delta",
        {"Stage_2_3_Contract_Diff.csv", "openapi/openapi.yaml", "00_MANIFEST.md"},
    ),
]


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


results = []
for archive_path, root_name, required in ARCHIVES:
    archive_hash = digest(archive_path.read_bytes())
    sidecar = archive_path.with_suffix(archive_path.suffix + ".sha256").read_text(encoding="utf-8")
    if archive_hash not in sidecar:
        raise AssertionError(f"SHA sidecar mismatch: {archive_path.name}")
    with zipfile.ZipFile(archive_path) as archive:
        if archive.testzip() is not None:
            raise AssertionError(f"CRC failure: {archive_path.name}")
        files = {
            info.filename: archive.read(info.filename)
            for info in archive.infolist()
            if not info.is_dir()
        }
    prefix = f"{root_name}/"
    if any(not name.startswith(prefix) for name in files):
        raise AssertionError(f"Unexpected ZIP root: {archive_path.name}")
    relative_names = {name[len(prefix):] for name in files}
    missing_required = required - relative_names
    if missing_required:
        raise AssertionError(f"Missing required files in {archive_path.name}: {sorted(missing_required)}")
    forbidden_parts = {"bin", "obj", "node_modules", ".git", ".nuget"}
    for name in relative_names:
        if forbidden_parts.intersection(Path(name).parts):
            raise AssertionError(f"Forbidden build/cache path in {archive_path.name}: {name}")
        if name.endswith((".db", ".bak", ".tmp")):
            raise AssertionError(f"Forbidden temporary/database file in {archive_path.name}: {name}")

    manifest_text = files[prefix + "00_MANIFEST.md"].decode("utf-8")
    manifest_entries = re.findall(
        r"^\| `([^`]+)` \| \d+ \| `([A-F0-9]{64})` \|$",
        manifest_text,
        flags=re.MULTILINE,
    )
    if not manifest_entries:
        raise AssertionError(f"Manifest has no entries: {archive_path.name}")
    for relative, expected_hash in manifest_entries:
        archive_name = prefix + relative
        if archive_name not in files:
            raise AssertionError(f"Manifest file missing from ZIP: {archive_name}")
        actual_hash = digest(files[archive_name])
        if actual_hash != expected_hash:
            raise AssertionError(
                f"Manifest hash mismatch in {archive_path.name}: {relative}, "
                f"expected {expected_hash}, actual {actual_hash}"
            )
    manifest_paths = {relative for relative, _ in manifest_entries}
    expected_manifest_paths = relative_names - {"00_MANIFEST.md"}
    if manifest_paths != expected_manifest_paths:
        raise AssertionError(
            f"Manifest inventory mismatch in {archive_path.name}: "
            f"missing={sorted(expected_manifest_paths - manifest_paths)}, "
            f"extra={sorted(manifest_paths - expected_manifest_paths)}"
        )
    results.append(
        {
            "archive": archive_path.name,
            "sha256": archive_hash,
            "entries": len(files),
            "crc": "PASS",
            "manifest": "PASS",
            "forbiddenFiles": "PASS",
        }
    )

report = {
    "version": "2.3.1",
    "status": "PASS",
    "archives": results,
}
(OUTPUTS / "Stage_2_3_Final_Package_Validation.json").write_text(
    json.dumps(report, ensure_ascii=False, indent=2) + "\n",
    encoding="utf-8",
    newline="\n",
)
(OUTPUTS / "Stage_2_3_Final_Package_Validation.md").write_text(
    "\n".join(
        [
            "# Stage 2.3 Final Package Validation",
            "",
            "- Version: `2.3.1`.",
            "- Overall status: **PASS**.",
            "",
            "| Archive | Entries | CRC/readback | Manifest | SHA-256 |",
            "|---|---:|---|---|---|",
            *[
                f"| `{item['archive']}` | {item['entries']} | PASS | PASS | `{item['sha256']}` |"
                for item in results
            ],
            "",
            "Both archives exclude build/cache/test-database files and were reopened after creation.",
        ]
    )
    + "\n",
    encoding="utf-8",
    newline="\n",
)
print(json.dumps(report, ensure_ascii=True, indent=2))
