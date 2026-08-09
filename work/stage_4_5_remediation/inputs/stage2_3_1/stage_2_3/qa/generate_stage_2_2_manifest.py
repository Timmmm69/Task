from __future__ import annotations

import hashlib
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE_21 = ROOT.with_name("Organizer_Stage2_Technical_Specification_2.1")
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
    if path == MANIFEST or relative in EXCLUDED_RELATIVE:
        return False
    return not any(part in EXCLUDED_PARTS for part in path.relative_to(ROOT).parts)


def relation(path: Path) -> str:
    previous = SOURCE_21 / path.relative_to(ROOT)
    if not previous.exists():
        return "new_2.2"
    return "unchanged_2.1" if sha256(previous) == sha256(path) else "corrected_2.2"


def status(relative: str) -> str:
    if relative.startswith(("openapi/", "catalogs/", "db/", "sources/")):
        return "canonical"
    if relative in {
        "Stage_2_2_Contract_Recovery.md",
        "Search_Contract.md",
        "Stage_2_2_Fix_Registry.md",
        "docs/00_README.md",
        "docs/01_core_domain_and_data.md",
        "docs/02_api_and_concurrency.md",
        "docs/03_runtime_operations_and_testing.md",
        "docs/04_adr_and_independent_audit.md",
        "docs/05_physical_schema_reference.md",
        "docs/06_stage_2_1_normative_corrections.md",
    }:
        return "canonical"
    if relative.startswith("qa/generated/") or relative in {
        "dto_field_catalog.csv",
        "contract_diff_against_traceability.csv",
    }:
        return "generated"
    if relative.endswith("_report.md") or relative == "source_integrity_report.md":
        return "supporting"
    return "supporting"


def purpose(relative: str) -> str:
    if relative == "openapi/openapi.yaml":
        return "Нормативный OpenAPI 3.1"
    if relative == "dto_field_catalog.csv":
        return "Field-level каталог DTO"
    if relative == "contract_diff_against_traceability.csv":
        return "Method+path contract diff"
    if relative == "Stage_2_2_Contract_Recovery.md":
        return "Происхождение и решение Contract Recovery"
    if relative == "Search_Contract.md":
        return "Нормативный Search API"
    if relative == "Stage_2_2_Fix_Registry.md":
        return "Реестр исправлений 2.2"
    if relative.startswith("catalogs/"):
        return "Канонический структурированный каталог"
    if relative.startswith("db/"):
        return "Нормативный PostgreSQL artifact"
    if relative.startswith("docs/"):
        return "Нормативная или историческая документация Этапа 2"
    if relative.startswith("sources/"):
        return "Первичный источник требований"
    if relative.startswith("qa/generated/desktop-csharp/"):
        return "Generated C# desktop SDK"
    if relative.startswith("qa/generated/server-csharp/"):
        return "Generated C# server stubs"
    if relative.startswith("qa/reports/"):
        return "Validation evidence"
    if relative.startswith("qa/"):
        return "Validator or reproducibility script"
    if relative.endswith("_report.md"):
        return "Итоговый validation report"
    return "Supporting package artifact"


def format_name(path: Path) -> str:
    return {
        ".md": "Markdown",
        ".yaml": "YAML",
        ".yml": "YAML",
        ".csv": "CSV",
        ".json": "JSON",
        ".py": "Python",
        ".ps1": "PowerShell",
        ".sql": "SQL",
        ".cs": "C#",
        ".csproj": "MSBuild XML",
        ".txt": "Text",
        ".log": "Log",
    }.get(path.suffix.lower(), path.suffix.lower().lstrip(".") or "file")


def main() -> None:
    files = sorted(
        (path for path in ROOT.rglob("*") if path.is_file() and selected(path)),
        key=lambda path: path.relative_to(ROOT).as_posix(),
    )
    payload_bytes = sum(path.stat().st_size for path in files)
    lines = [
        "# Manifest — Organizer Stage 2.2 Contract Recovery",
        "",
        "## 1. Package",
        "",
        "- Package: `Organizer_Stage2_Technical_Specification_2.2`.",
        "- Version: `2.2`.",
        "- Assembly date: `2026-07-26`.",
        f"- Files in final ZIP including this manifest: `{len(files) + 1}`.",
        f"- Payload bytes excluding this self-referential manifest: `{payload_bytes}`.",
        "- Canonical Stage 2.1 ZIP SHA-256: `A293F576D7FF781ACA75222D709F323369C950E740072921F548999C8E83A715`.",
        "- Recovered Stage 2.1 OpenAPI SHA-256: `E3D1D1D20AFB5EB34B5CB06525CF31245769CF9C6F551146E2E843BF1C0C4A37`.",
        "- Final Stage 2.2 OpenAPI SHA-256: `052738F7BF1B02CAB054B92827E17E3EA79EB0C8832C0F5A6E60681E4B363161`.",
        "",
        "## 2. Main sources",
        "",
        "1. `sources/product_concept.txt` — product requirements.",
        "2. `sources/architecture_stage1.md` — architecture baseline.",
        "3. `docs/06_stage_2_1_normative_corrections.md` — prior normative corrections.",
        "4. `Stage_2_2_Contract_Recovery.md` and `Search_Contract.md` — Stage 2.2 corrections.",
        "5. `openapi/openapi.yaml` — final machine-readable contract.",
        "6. `catalogs/api_catalog.csv`, `catalogs/permissions.csv`, `catalogs/errors.csv` — canonical operation and policy catalogs.",
        "",
        "## 3. Full file list",
        "",
        "| Relative path | Purpose | Status | Format | Bytes | SHA-256 | Relation |",
        "|---|---|---|---|---:|---|---|",
        "| `00_MANIFEST.md` | Состав и контроль поставки | generated | Markdown | self | external `.manifest.sha256` | new_2.2 |",
    ]
    for path in files:
        relative = path.relative_to(ROOT).as_posix()
        lines.append(
            f"| `{relative}` | {purpose(relative)} | {status(relative)} | {format_name(path)} | "
            f"{path.stat().st_size} | `{sha256(path)}` | {relation(path)} |"
        )
    lines.extend(
        [
            "",
            "## 4. Exclusions",
            "",
            "- Stale Stage 2.1 TypeScript generated client and server-contract outputs.",
            "- Superseded Stage 2.1 OpenAPI/codegen reports, `MANIFEST.json`, endpoint dump and JSON validation summary.",
            "- `bin`, `obj`, dependency caches, compiler binaries and temporary files.",
            "- Unrelated STOK Git repository content.",
            "",
            "## 5. Self-reference and archive hash",
            "",
            "The SHA-256 of this manifest cannot be embedded in itself without changing the file. "
            "The final manifest hash is delivered in `Organizer_Stage2_Technical_Specification_2.2.manifest.sha256`. "
            "The final ZIP hash is delivered in `Organizer_Stage2_Technical_Specification_2.2.zip.sha256`; "
            "both sidecars are outside the ZIP.",
            "",
        ]
    )
    MANIFEST.write_text("\n".join(lines), encoding="utf-8", newline="\n")
    print(f"MANIFEST_GENERATED files={len(files) + 1} payload_bytes={payload_bytes}")


if __name__ == "__main__":
    main()
