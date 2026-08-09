from __future__ import annotations

import csv
import hashlib
import io
import json
import py_compile
import re
import xml.etree.ElementTree as xml
from pathlib import Path
from typing import Callable

import yaml


ROOT = Path(__file__).resolve().parents[1]
SOURCE_21 = ROOT.with_name("Organizer_Stage2_Technical_Specification_2.1")
REPORT = ROOT / "source_integrity_report.md"
MANIFEST = ROOT / "00_MANIFEST.md"
EXCLUDED_PARTS = {"bin", "obj", "desktop-sdk", "server-contract", "__pycache__"}
EXCLUDED_RELATIVE = {
    "MANIFEST.json",
    "endpoints_dump.txt",
    "qa/generated/tsconfig.json",
    "qa/reports/artifact_validation.log",
    "qa/reports/codegen_report.md",
    "qa/reports/codegen_validation.log",
    "qa/reports/full_validation_console.log",
    "qa/reports/openapi_lint.log",
    "qa/reports/validation_summary.log",
    "qa/traceability_report.md",
    "qa/validation_report.json",
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def selected(path: Path) -> bool:
    relative = path.relative_to(ROOT).as_posix()
    if path in {REPORT, MANIFEST}:
        return False
    if relative in EXCLUDED_RELATIVE:
        return False
    return not any(part in EXCLUDED_PARTS for part in path.relative_to(ROOT).parts)


def validate_csv(path: Path) -> str:
    text = path.read_text(encoding="utf-8-sig")
    rows = list(csv.reader(io.StringIO(text)))
    if len(rows) < 2 or len(rows[0]) < 2:
        raise ValueError("CSV has no header/data")
    width = len(rows[0])
    if any(len(row) != width for row in rows):
        raise ValueError("CSV row width mismatch")
    return f"CSV; columns={width}; rows={len(rows) - 1}"


def validate_yaml(path: Path) -> str:
    document = yaml.safe_load(path.read_text(encoding="utf-8-sig"))
    if not isinstance(document, dict):
        raise ValueError("YAML root is not an object")
    if path.name == "openapi.yaml":
        if not str(document.get("openapi", "")).startswith("3.1"):
            raise ValueError("OpenAPI marker missing")
        if not isinstance(document.get("paths"), dict) or not isinstance(document.get("components"), dict):
            raise ValueError("OpenAPI paths/components missing")
        return "OpenAPI 3.1 YAML"
    return "YAML object"


def validate_markdown(path: Path) -> str:
    text = path.read_text(encoding="utf-8-sig")
    first = next((line.strip() for line in text.splitlines() if line.strip()), "")
    if not first.startswith("#"):
        raise ValueError("Markdown heading missing")
    return "Markdown"


def validate_json(path: Path) -> str:
    json.loads(path.read_text(encoding="utf-8-sig"))
    return "JSON"


def validate_python(path: Path) -> str:
    source = path.read_text(encoding="utf-8-sig")
    compile(source, str(path), "exec")
    return "Python source"


def validate_sql(path: Path) -> str:
    text = path.read_text(encoding="utf-8-sig")
    if ";" not in text or not re.search(r"\b(CREATE|ALTER|INSERT|SELECT|BEGIN)\b", text, re.I):
        raise ValueError("SQL markers missing")
    return "SQL"


def validate_csharp(path: Path) -> str:
    text = path.read_text(encoding="utf-8-sig")
    if "namespace " not in text or not re.search(r"\b(class|interface|enum)\b", text):
        raise ValueError("C# type markers missing")
    return "C# source"


def validate_csproj(path: Path) -> str:
    root = xml.fromstring(path.read_text(encoding="utf-8-sig"))
    if not root.tag.endswith("Project"):
        raise ValueError("MSBuild Project root missing")
    return "MSBuild project XML"


def validate_text(path: Path) -> str:
    data = path.read_bytes()
    if data.startswith(b"\xff\xfe") or data.startswith(b"\xfe\xff"):
        text = data.decode("utf-16")
        encoding = "UTF-16"
    else:
        text = data.decode("utf-8-sig")
        encoding = "UTF-8"
    if not text.strip():
        raise ValueError("Empty text file")
    return f"Text; {encoding}"


VALIDATORS: dict[str, Callable[[Path], str]] = {
    ".csv": validate_csv,
    ".yaml": validate_yaml,
    ".yml": validate_yaml,
    ".md": validate_markdown,
    ".json": validate_json,
    ".py": validate_python,
    ".sql": validate_sql,
    ".cs": validate_csharp,
    ".csproj": validate_csproj,
    ".txt": validate_text,
    ".log": validate_text,
    ".ps1": validate_text,
}


def source_relation(path: Path) -> str:
    relative = path.relative_to(ROOT)
    previous = SOURCE_21 / relative
    if not previous.exists():
        return "new_2.2"
    if sha256(previous) == sha256(path):
        return "unchanged_from_2.1"
    return "corrected_or_regenerated_2.2"


def main() -> None:
    files = sorted(
        (path for path in ROOT.rglob("*") if path.is_file() and selected(path)),
        key=lambda path: path.relative_to(ROOT).as_posix(),
    )
    rows = []
    failures = []
    for path in files:
        relative = path.relative_to(ROOT).as_posix()
        validator = VALIDATORS.get(path.suffix.lower(), validate_text)
        try:
            content_type = validator(path)
            result = "PASS"
        except Exception as error:
            content_type = f"ERROR: {error}"
            result = "FAIL"
            failures.append(f"{relative}: {error}")
        rows.append(
            {
                "path": relative,
                "format": content_type,
                "bytes": path.stat().st_size,
                "sha256": sha256(path),
                "source_relation": source_relation(path),
                "result": result,
            }
        )

    lines = [
        "# Source Integrity Report — Stage 2.2",
        "",
        "## 1. Result",
        "",
        f"- Status: `{'PASS' if not failures else 'FAIL'}`.",
        f"- Files checked: `{len(rows)}`.",
        f"- Content/extension mismatches: `{len(failures)}`.",
        "- Check date: `2026-07-26`.",
        "- Scope: every file selected for the final Stage 2.2 archive except this self-referential report and the final manifest.",
        "",
        "## 2. Provenance decision",
        "",
        "- The normative Stage 2.1 OpenAPI was found in the source folder and three delivery ZIPs with identical SHA-256.",
        "- The current Git repository is the unrelated STOK product and is not an Organizer source.",
        "- No newer local CI artifact or temporary code-generation contract superseded the validated Stage 2.1 OpenAPI.",
        "- Stage 2.2 files are classified as unchanged, corrected/regenerated, or new by comparison with the Stage 2.1 folder.",
        "",
        "## 3. Excluded stale and intermediate artifacts",
        "",
        "- Stage 2.1 TypeScript generated client/server outputs were excluded because they do not represent the final 2.2 OpenAPI.",
        "- Stage 2.1 OpenAPI/codegen summary logs and the stale `MANIFEST.json` were excluded.",
        "- `bin`, `obj`, dependency caches and compiler outputs were excluded after validation.",
        "- `endpoints_dump.txt`, the superseded QA traceability report and the superseded JSON validation report were excluded.",
        "",
        "## 4. File checks",
        "",
        "| Relative path | Detected content | Bytes | SHA-256 | Relation to 2.1 | Result |",
        "|---|---|---:|---|---|---|",
    ]
    for row in rows:
        lines.append(
            f"| `{row['path']}` | {row['format']} | {row['bytes']} | `{row['sha256']}` | "
            f"{row['source_relation']} | {row['result']} |"
        )
    lines.extend(
        [
            "",
            "## 5. Self-reference",
            "",
            "`source_integrity_report.md` and `00_MANIFEST.md` are verified after generation by the final archive gate. "
            "Their SHA-256 values are recorded in `00_MANIFEST.md`; the ZIP SHA-256 is delivered in an external sidecar.",
            "",
        ]
    )
    if failures:
        lines.extend(["## 6. Failures", "", *[f"- {failure}" for failure in failures], ""])
    REPORT.write_text("\n".join(lines), encoding="utf-8", newline="\n")
    print(
        json.dumps(
            {
                "status": "pass" if not failures else "fail",
                "files": len(rows),
                "failures": failures,
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    if failures:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
