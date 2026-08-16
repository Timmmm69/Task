#!/usr/bin/env python3
"""Build and validate the unified Stage 1 implementation matrix."""

from __future__ import annotations

import csv
import hashlib
import re
import sys
from collections import Counter
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
TRACEABILITY_DIR = ROOT / "work" / "production_stage_1_baseline" / "traceability"
INPUTS = (
    ("A", TRACEABILITY_DIR / "wave-a.csv"),
    ("B", TRACEABILITY_DIR / "wave-b.csv"),
    ("C", TRACEABILITY_DIR / "wave-c.csv"),
)
GAP_OVERRIDE_INPUTS = (
    ("A", TRACEABILITY_DIR / "gap_overrides_wave_a.csv"),
    ("B", TRACEABILITY_DIR / "gap_overrides_wave_b.csv"),
    ("C", TRACEABILITY_DIR / "gap_overrides_wave_c.csv"),
)
OPENAPI = ROOT / "outputs" / "stage_2_3" / "openapi" / "openapi.yaml"
OUTPUT = TRACEABILITY_DIR / "implementation_matrix.csv"
REPORT = TRACEABILITY_DIR / "MATRIX_VALIDATION.md"
MATRIX_VERSION = "1.2.0"

OPERATION_RE = re.compile(r"\b(?:GET|POST|PUT|PATCH|DELETE)_[A-Za-z0-9_]+\b")
PATH_RE = re.compile(r"^  (/[^:]+):\s*$")
METHOD_RE = re.compile(r"^    (get|post|put|patch|delete):\s*$")
OPERATION_LINE_RE = re.compile(r"^\s+operationId:\s*(\S+)\s*$")
DASH = chr(0x2014)
PLACEHOLDERS = {"", "-", DASH}
TRACEABILITY_MODES = {"single-operation", "module-wide", "unresolved"}

SOURCE_FIELDS = (
    "Requirement",
    "Type",
    "Module",
    "Module name",
    "Requirement title",
    "API operationId",
    "API path (method)",
    "Permission",
    "Server handler (planned)",
    "Screen (Stage 3.5)",
    "FLOW (Stage 3.5)",
    "Acceptance criteria (AC)",
    "Test type",
    "Priority",
    "Source",
)

OUTPUT_FIELDS = (
    "Wave",
    "Source row",
    "Requirement",
    "Type",
    "Module",
    "Module name",
    "Requirement title",
    "API status",
    "API operationId",
    "API path (method)",
    "Permission",
    "Server handler (planned)",
    "Screen (Stage 3.5)",
    "FLOW (Stage 3.5)",
    "Acceptance criteria (AC)",
    "Test type",
    "Priority",
    "Source",
    "Disposition reason",
    "Gap reference",
)

GAP_OVERRIDE_FIELDS = (
    "Matrix source row",
    "Requirement",
    "Type",
    "Module",
    "Requirement title",
    "Resolution status",
    "Traceability mode",
    "Related OpenAPI operationIds",
    "API operationId",
    "API method",
    "API path",
    "Permission",
    "Server handler planned",
    "Screen Stage 3.5",
    "FLOW Stage 3.5",
    "Test type",
    "Source evidence",
    "Resolution rationale",
)


@dataclass(frozen=True)
class SourceRow:
    wave: str
    file_name: str
    line_number: int
    values: dict[str, str]

    @property
    def reference(self) -> str:
        return f"{self.file_name}:{self.line_number}"


@dataclass(frozen=True)
class GapOverride:
    wave: str
    file_name: str
    line_number: int
    values: dict[str, str]

    @property
    def reference(self) -> str:
        return f"{self.file_name}:{self.line_number}"


def is_placeholder(value: str) -> bool:
    stripped = value.strip()
    return stripped in PLACEHOLDERS or stripped.startswith(DASH)


def split_semicolon(value: str) -> list[str]:
    return [part.strip() for part in value.split(";")]


def read_inputs(errors: list[str]) -> list[SourceRow]:
    rows: list[SourceRow] = []
    for wave, path in INPUTS:
        with path.open("r", encoding="utf-8-sig", newline="") as stream:
            reader = csv.DictReader(stream)
            if tuple(reader.fieldnames or ()) != SOURCE_FIELDS:
                errors.append(f"{path.name}: unexpected columns {reader.fieldnames!r}")
            for line_number, values in enumerate(reader, start=2):
                rows.append(SourceRow(wave, path.name, line_number, dict(values)))
    return rows


def read_gap_overrides(errors: list[str]) -> dict[str, list[GapOverride]]:
    overrides: dict[str, list[GapOverride]] = {}
    seen_links: set[tuple[str, str]] = set()
    for wave, path in GAP_OVERRIDE_INPUTS:
        with path.open("r", encoding="utf-8-sig", newline="") as stream:
            reader = csv.DictReader(stream)
            if tuple(reader.fieldnames or ()) != GAP_OVERRIDE_FIELDS:
                errors.append(f"{path.name}: unexpected columns {reader.fieldnames!r}")
            for line_number, values in enumerate(reader, start=2):
                override = GapOverride(wave, path.name, line_number, dict(values))
                source_row = values["Matrix source row"].strip()
                operation_id = values["API operationId"].strip()
                link = (source_row, operation_id)
                if not source_row:
                    errors.append(f"{override.reference}: Matrix source row is empty")
                elif link in seen_links:
                    errors.append(
                        f"{override.reference}: duplicate gap resolution for "
                        f"{source_row} and {operation_id or '<unresolved>'}"
                    )
                seen_links.add(link)
                overrides.setdefault(source_row, []).append(override)
    return overrides


def read_openapi_operations(errors: list[str]) -> dict[str, str]:
    operations: dict[str, str] = {}
    current_path: str | None = None
    current_method: str | None = None
    for line in OPENAPI.read_text(encoding="utf-8-sig").splitlines():
        if match := PATH_RE.match(line):
            current_path = match.group(1)
            current_method = None
            continue
        if match := METHOD_RE.match(line):
            current_method = match.group(1).upper()
            continue
        if match := OPERATION_LINE_RE.match(line):
            operation_id = match.group(1)
            if current_path is None or current_method is None:
                errors.append(f"OpenAPI operation without path/method: {operation_id}")
            elif operation_id in operations:
                errors.append(f"Duplicate OpenAPI operationId: {operation_id}")
            else:
                operations[operation_id] = f"{current_method} {current_path}"
    if not operations:
        errors.append("No operationId values found in OpenAPI")
    return operations


def deduplicate_all_rows(rows: list[SourceRow], errors: list[str]) -> list[tuple[SourceRow, list[SourceRow]]]:
    grouped: dict[str, list[SourceRow]] = {}
    result: list[tuple[SourceRow, list[SourceRow]]] = []
    for row in rows:
        if row.values["Module"] == "ALL":
            grouped.setdefault(row.values["Requirement"], []).append(row)
        else:
            result.append((row, [row]))

    ignored = {"Module name"}
    for requirement, group in grouped.items():
        waves = {row.wave for row in group}
        if waves != {"A", "B", "C"}:
            errors.append(f"{requirement}: ALL rule is not present exactly once in Wave A/B/C")
        baseline = group[0].values
        for candidate in group[1:]:
            differing = [
                field for field in SOURCE_FIELDS
                if field not in ignored and candidate.values[field] != baseline[field]
            ]
            if differing:
                errors.append(
                    f"{requirement}: conflicting ALL copies in {candidate.reference}: "
                    + ", ".join(differing)
                )
        result.append((group[0], group))

    return sorted(
        result,
        key=lambda item: (min(row.wave for row in item[1]), min(row.line_number for row in item[1])),
    )


def base_output(row: SourceRow, copies: list[SourceRow]) -> dict[str, str]:
    values = row.values
    is_all = values["Module"] == "ALL"
    return {
        "Wave": "ALL" if is_all else row.wave,
        "Source row": " | ".join(copy.reference for copy in copies),
        "Requirement": values["Requirement"],
        "Type": values["Type"],
        "Module": values["Module"],
        "Module name": "Все модули (Wave A/B/C)" if is_all else values["Module name"],
        "Requirement title": values["Requirement title"],
        "Permission": values["Permission"],
        "Screen (Stage 3.5)": values["Screen (Stage 3.5)"],
        "FLOW (Stage 3.5)": values["FLOW (Stage 3.5)"],
        "Acceptance criteria (AC)": values["Acceptance criteria (AC)"],
        "Test type": values["Test type"],
        "Priority": values["Priority"],
        "Source": values["Source"],
    }


def gap_reference(copies: list[SourceRow], source: str) -> str:
    refs = " | ".join(copy.reference for copy in copies)
    return f"{refs}; Source={source}"


def override_reference(override: GapOverride) -> str:
    evidence = override.values["Source evidence"].strip()
    return f"{override.reference}; {evidence}"


def validate_override_identity(
    override: GapOverride,
    base: dict[str, str],
    expected_wave: str,
    errors: list[str],
) -> None:
    if override.wave != expected_wave:
        errors.append(
            f"{override.reference}: override wave {override.wave} does not match source wave {expected_wave}"
        )
    field_mapping = {
        "Requirement": "Requirement",
        "Type": "Type",
        "Module": "Module",
        "Requirement title": "Requirement title",
        "Screen Stage 3.5": "Screen (Stage 3.5)",
        "FLOW Stage 3.5": "FLOW (Stage 3.5)",
        "Test type": "Test type",
    }
    for override_field, matrix_field in field_mapping.items():
        if override.values[override_field] != base[matrix_field]:
            errors.append(
                f"{override.reference}: {override_field} does not match {base['Source row']}"
            )


def apply_gap_overrides(
    row: SourceRow,
    base: dict[str, str],
    overrides: list[GapOverride],
    openapi_operations: dict[str, str],
    errors: list[str],
) -> tuple[list[dict[str, str]], set[str]]:
    output_rows: list[dict[str, str]] = []
    unknown_operations: set[str] = set()
    statuses = {override.values["Resolution status"].strip() for override in overrides}
    if statuses not in ({"resolved"}, {"unresolved"}):
        errors.append(
            f"{base['Source row']}: gap overrides must be uniformly resolved or unresolved; "
            f"found {sorted(statuses)}"
        )

    for override in overrides:
        validate_override_identity(override, base, row.wave, errors)
        values = override.values
        status = values["Resolution status"].strip()
        mode = values["Traceability mode"].strip()
        related = values["Related OpenAPI operationIds"].strip()
        evidence = values["Source evidence"].strip()
        rationale = values["Resolution rationale"].strip()
        if status not in {"resolved", "unresolved"}:
            errors.append(f"{override.reference}: invalid Resolution status {status!r}")
            continue
        if mode not in TRACEABILITY_MODES:
            errors.append(f"{override.reference}: invalid Traceability mode {mode!r}")
            continue
        if not evidence:
            errors.append(f"{override.reference}: Source evidence is empty")
        if not rationale:
            errors.append(f"{override.reference}: Resolution rationale is empty")

        endpoint_fields = {
            "API operationId": values["API operationId"].strip(),
            "API method": values["API method"].strip(),
            "API path": values["API path"].strip(),
            "Permission": values["Permission"].strip(),
            "Server handler planned": values["Server handler planned"].strip(),
        }
        if status == "unresolved":
            if mode != "unresolved":
                errors.append(f"{override.reference}: unresolved gap must use unresolved mode")
            if related:
                errors.append(f"{override.reference}: unresolved gap has Related OpenAPI operationIds")
            if len(overrides) != 1:
                errors.append(f"{base['Source row']}: unresolved gap must have exactly one override row")
            populated = [name for name, value in endpoint_fields.items() if value]
            if populated:
                errors.append(
                    f"{override.reference}: unresolved gap has endpoint fields: {', '.join(populated)}"
                )
            output_rows.append(
                {
                    **base,
                    "API status": "gap",
                    "API operationId": "",
                    "API path (method)": "",
                    "Server handler (planned)": "",
                    "Disposition reason": rationale,
                    "Gap reference": override_reference(override),
                }
            )
            continue

        if mode == "unresolved":
            errors.append(f"{override.reference}: resolved gap must not use unresolved mode")

        if mode == "module-wide":
            related_ops = [op.strip() for op in related.split(";")]
            if not related_ops:
                errors.append(
                    f"{override.reference}: module-wide resolved gap has empty "
                    "Related OpenAPI operationIds"
                )
            elif any(not op_id for op_id in related_ops):
                errors.append(
                    f"{override.reference}: module-wide Related OpenAPI operationIds has an empty item"
                )
            elif len(set(related_ops)) != len(related_ops):
                errors.append(
                    f"{override.reference}: module-wide Related OpenAPI operationIds has duplicates"
                )
            elif related_ops != sorted(related_ops):
                errors.append(
                    f"{override.reference}: module-wide Related OpenAPI operationIds is not sorted"
                )
            if len(overrides) != 1:
                errors.append(f"{base['Source row']}: module-wide gap must have exactly one override row")
            populated_direct = [
                name for name in ("API operationId", "API method", "API path")
                if endpoint_fields[name]
            ]
            if populated_direct:
                errors.append(
                    f"{override.reference}: module-wide gap has direct endpoint fields: "
                    + ", ".join(populated_direct)
                )
            if is_placeholder(values["Permission"]):
                errors.append(f"{override.reference}: module-wide gap has no Permission")
            if is_placeholder(values["Server handler planned"]):
                errors.append(f"{override.reference}: module-wide gap has no Server handler planned")
            for field_name in ("Screen Stage 3.5", "FLOW Stage 3.5", "Test type"):
                if is_placeholder(values[field_name]):
                    errors.append(f"{override.reference}: module-wide gap has no {field_name}")
            for op_id in related_ops:
                canonical = openapi_operations.get(op_id)
                if canonical is None:
                    unknown_operations.add(op_id)
                    operation_path = ""
                else:
                    method, _, path = canonical.partition(" ")
                    operation_path = f"{method} {path}".strip()
                output_rows.append(
                    {
                        **base,
                        "API status": "api",
                        "API operationId": op_id,
                        "API path (method)": operation_path,
                        "Permission": endpoint_fields["Permission"],
                        "Server handler (planned)": endpoint_fields["Server handler planned"],
                        "Screen (Stage 3.5)": values["Screen Stage 3.5"],
                        "FLOW (Stage 3.5)": values["FLOW Stage 3.5"],
                        "Test type": values["Test type"],
                        "Disposition reason": rationale,
                        "Gap reference": override_reference(override),
                    }
                )
            continue

        if related != endpoint_fields["API operationId"]:
            errors.append(
                f"{override.reference}: single-operation Related OpenAPI operationIds "
                "must equal API operationId"
            )
        missing = [name for name, value in endpoint_fields.items() if not value]
        if missing:
            errors.append(f"{override.reference}: resolved gap is missing {', '.join(missing)}")
        operation_id = endpoint_fields["API operationId"]
        method = endpoint_fields["API method"]
        path = endpoint_fields["API path"]
        canonical_path = openapi_operations.get(operation_id)
        actual_path = f"{method} {path}".strip()
        if canonical_path is None:
            unknown_operations.add(operation_id)
        elif actual_path != canonical_path:
            errors.append(
                f"{override.reference}: {operation_id} path is {actual_path!r}, "
                f"OpenAPI declares {canonical_path!r}"
            )
        for field_name in ("Screen Stage 3.5", "FLOW Stage 3.5", "Test type"):
            if is_placeholder(values[field_name]):
                errors.append(f"{override.reference}: resolved gap has no {field_name}")
        output_rows.append(
            {
                **base,
                "API status": "api",
                "API operationId": operation_id,
                "API path (method)": actual_path,
                "Permission": endpoint_fields["Permission"],
                "Server handler (planned)": endpoint_fields["Server handler planned"],
                "Screen (Stage 3.5)": values["Screen Stage 3.5"],
                "FLOW (Stage 3.5)": values["FLOW Stage 3.5"],
                "Test type": values["Test type"],
                "Disposition reason": rationale,
                "Gap reference": override_reference(override),
            }
        )

    return output_rows, unknown_operations


def build_rows(
    source_rows: list[tuple[SourceRow, list[SourceRow]]],
    gap_overrides: dict[str, list[GapOverride]],
    openapi_operations: dict[str, str],
    errors: list[str],
) -> tuple[list[dict[str, str]], set[str]]:
    output_rows: list[dict[str, str]] = []
    unknown_operations: set[str] = set()

    for row, copies in source_rows:
        values = row.values
        base = base_output(row, copies)
        operation_ids = OPERATION_RE.findall(values["API operationId"])

        if not operation_ids:
            marker = values["API operationId"].strip()
            is_gap = "операции модуля" in marker or "domain command + audit/history endpoints" in marker
            if is_gap:
                source_reference = base["Source row"]
                matching_overrides = gap_overrides.pop(source_reference, [])
                if matching_overrides:
                    resolved_rows, override_unknown = apply_gap_overrides(
                        row,
                        base,
                        matching_overrides,
                        openapi_operations,
                        errors,
                    )
                    output_rows.extend(resolved_rows)
                    unknown_operations.update(override_unknown)
                    continue
                errors.append(f"{source_reference}: gap has no override")
                reason = "Source gap has no reviewed override."
                status = "gap"
                reference = gap_reference(copies, values["Source"])
            elif "Desktop-only" in marker:
                status = "no_api"
                reason = "Desktop-only requirement; source explicitly states that no new API is required."
                reference = ""
            else:
                status = "no_api"
                reason = "Source row does not associate this requirement with an API operation."
                reference = ""
            output_rows.append(
                {
                    **base,
                    "API status": status,
                    "API operationId": "",
                    "API path (method)": "",
                    "Server handler (planned)": "",
                    "Disposition reason": reason,
                    "Gap reference": reference,
                }
            )
            continue

        paths = split_semicolon(values["API path (method)"])
        handlers = split_semicolon(values["Server handler (planned)"])
        raw_operations = split_semicolon(values["API operationId"])
        reference = row.reference
        if raw_operations != operation_ids:
            errors.append(f"{reference}: API operationId cell contains unparseable text")
        if len(paths) != len(operation_ids):
            errors.append(f"{reference}: operationId/path count mismatch")
        if len(handlers) != len(operation_ids):
            errors.append(f"{reference}: operationId/handler count mismatch")

        for index, operation_id in enumerate(operation_ids):
            path = paths[index] if index < len(paths) else ""
            handler = handlers[index] if index < len(handlers) else ""
            canonical_path = openapi_operations.get(operation_id)
            if canonical_path is None:
                unknown_operations.add(operation_id)
            elif path != canonical_path:
                errors.append(
                    f"{reference}: {operation_id} path is {path!r}, OpenAPI declares {canonical_path!r}"
                )

            endpoint_fields = {
                "API path (method)": path,
                "Server handler (planned)": handler,
                "Screen (Stage 3.5)": values["Screen (Stage 3.5)"],
                "FLOW (Stage 3.5)": values["FLOW (Stage 3.5)"],
                "Test type": values["Test type"],
            }
            for field, field_value in endpoint_fields.items():
                if is_placeholder(field_value):
                    errors.append(f"{reference}: endpoint {operation_id} has no {field}")

            output_rows.append(
                {
                    **base,
                    "API status": "api",
                    "API operationId": operation_id,
                    "API path (method)": path,
                    "Server handler (planned)": handler,
                    "Disposition reason": "",
                    "Gap reference": "",
                }
            )

    for operation_id in sorted(unknown_operations):
        errors.append(f"Unknown operationId: {operation_id}")
    for source_reference, unused_overrides in sorted(gap_overrides.items()):
        refs = ", ".join(override.reference for override in unused_overrides)
        errors.append(f"{source_reference}: override does not match a source gap ({refs})")
    return output_rows, unknown_operations


def write_csv(rows: list[dict[str, str]]) -> None:
    with OUTPUT.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=OUTPUT_FIELDS, extrasaction="raise")
        writer.writeheader()
        writer.writerows(rows)


def sha256(path: Path) -> str:
    # Source inputs are text tracked by Git and may be checked out as CRLF on
    # Windows. Hash their canonical LF representation so the validation report
    # remains reproducible across supported worktrees.
    content = path.read_bytes().replace(b"\r\n", b"\n")
    return hashlib.sha256(content).hexdigest()


def write_report(
    rows: list[dict[str, str]],
    source_count: int,
    override_count: int,
    openapi_count: int,
    unknown_operations: set[str],
    errors: list[str],
) -> None:
    counts = Counter(row["API status"] for row in rows)
    requirements = {row["Requirement"] for row in rows}
    api_operations = [row["API operationId"] for row in rows if row["API status"] == "api"]
    no_api_rows = counts["no_api"] + counts["gap"]
    resolved_override_rows = sum(
        row["API status"] == "api" and bool(row["Gap reference"])
        for row in rows
    )
    status = "PASS" if not errors else "FAIL"
    unknown_text = ", ".join(sorted(unknown_operations)) if unknown_operations else "none"
    error_lines = "\n".join(f"- {error}" for error in errors) if errors else "- None."
    input_manifest = "\n".join(
        f"- `{path.relative_to(ROOT).as_posix()}` — SHA-256 `{sha256(path)}`"
        for _, path in INPUTS
    )
    report = f"""# Stage 1 implementation matrix validation

## Result

**{status}**

## Counts

| Metric | Count |
|---|---:|
| Source rows read | {source_count} |
| Gap override rows read | {override_count} |
| Output rows | {len(rows)} |
| Requirements | {len(requirements)} |
| API-operation rows | {counts['api']} |
| Unique API operations used | {len(set(api_operations))} |
| Rows without API | {no_api_rows} |
| `no_api` rows | {counts['no_api']} |
| Documented `gap` rows | {counts['gap']} |
| Gaps resolved to API rows | {resolved_override_rows} |
| Unknown operations found | {len(unknown_operations)} |
| Operations declared by OpenAPI | {openapi_count} |

Unknown operations: {unknown_text}.

## Validation performed

- Every source requirement is represented; the 57 universal `ALL` rules are stored once after equality checks across Wave A/B/C.
- Every multi-operation source cell is split into one row per requirement-to-operation link, with positional path and handler mapping.
- Every API operation is checked against `outputs/stage_2_3/openapi/openapi.yaml`, including its HTTP method and path.
- Every endpoint row is checked for a planned server handler, screen, FLOW and test type.
- Every source gap has exactly one reviewed unresolved override or one or more reviewed resolved API links.
- Every resolved override is checked against OpenAPI method/path and promoted to an `api` row with evidence.
- Every unresolved override remains `API status=gap` with its rationale and exact evidence reference.

## Manifest

- Matrix version: `{MATRIX_VERSION}`
{input_manifest}
{chr(10).join(f"- `{path.relative_to(ROOT).as_posix()}` — SHA-256 `{sha256(path)}`" for _, path in GAP_OVERRIDE_INPUTS)}
- `{OPENAPI.relative_to(ROOT).as_posix()}` — SHA-256 `{sha256(OPENAPI)}`
- `{OUTPUT.relative_to(ROOT).as_posix()}` — SHA-256 `{sha256(OUTPUT)}`

## Errors

{error_lines}
"""
    REPORT.write_text(report, encoding="utf-8", newline="\n")


def main() -> int:
    errors: list[str] = []
    input_rows = read_inputs(errors)
    gap_overrides = read_gap_overrides(errors)
    override_count = sum(len(overrides) for overrides in gap_overrides.values())
    openapi_operations = read_openapi_operations(errors)
    source_rows = deduplicate_all_rows(input_rows, errors)
    rows, unknown_operations = build_rows(source_rows, gap_overrides, openapi_operations, errors)
    write_csv(rows)
    write_report(
        rows,
        len(input_rows),
        override_count,
        len(openapi_operations),
        unknown_operations,
        errors,
    )
    print(f"Built {OUTPUT.relative_to(ROOT)} with {len(rows)} rows")
    print(f"Validation: {'PASS' if not errors else 'FAIL'}; report: {REPORT.relative_to(ROOT)}")
    return 0 if not errors else 1


if __name__ == "__main__":
    sys.exit(main())
