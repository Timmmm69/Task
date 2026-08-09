from __future__ import annotations

import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "MANIFEST.json"
excluded_parts = {"__pycache__", ".pytest_cache", "node_modules"}
excluded_files = {
    MANIFEST,
    ROOT / "qa" / "reports" / "full_validation_console.log",
}

entries = []
for path in sorted(ROOT.rglob("*")):
    if not path.is_file() or path in excluded_files:
        continue
    relative = path.relative_to(ROOT)
    if any(part in excluded_parts for part in relative.parts):
        continue
    content = path.read_bytes()
    entries.append(
        {
            "path": relative.as_posix(),
            "bytes": len(content),
            "sha256": hashlib.sha256(content).hexdigest(),
        }
    )

MANIFEST.write_text(
    json.dumps(entries, ensure_ascii=False, indent=2) + "\n",
    encoding="utf-8",
    newline="\n",
)
print(f"MANIFEST_WRITTEN files={len(entries)}")
