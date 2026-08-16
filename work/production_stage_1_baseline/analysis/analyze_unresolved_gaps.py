#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Build a deterministic audit of initially unresolved Stage 1 overrides."""

import csv
import sys
from collections import Counter, defaultdict
from pathlib import Path

ANALYSIS_DIR = Path(__file__).resolve().parent
TRACEABILITY_DIR = ANALYSIS_DIR.parent / "traceability"
REPORT_PATH = ANALYSIS_DIR / "UNRESOLVED_GAPS_REPORT.md"
WAVES = ("a", "b", "c")
EXPECTED_UNRESOLVED = 80
NO_API_DISPOSITIONS_PATH = TRACEABILITY_DIR / "desktop_no_api_dispositions.csv"
ABSENT_API_FIELDS = (
    "API operationId",
    "API method",
    "API path",
    "Permission",
    "Server handler planned",
)
NO_OPERATIONID_PHRASE = "without an operationId"


def md(value: str) -> str:
    return (value or "").replace("|", "\\|").replace("\n", " ").strip()


def ref_key(reference: str) -> tuple[str, int]:
    file_name, line_number = reference.split(":", 1)
    return file_name, int(line_number)


def examples(rows: list[dict[str, str]]) -> str:
    references = sorted((row["Matrix source row"] for row in rows), key=ref_key)[:3]
    return " / ".join(f"`{md(reference)}`" for reference in references)


def read_inputs() -> tuple[dict[str, list[dict[str, str]]], set[str], list[str]]:
    sessions: dict[str, list[dict[str, str]]] = {}
    errors: list[str] = []
    for wave in WAVES:
        path = TRACEABILITY_DIR / f"gap_overrides_wave_{wave}.csv"
        if not path.exists():
            errors.append(f"input missing: {path}")
            continue
        with path.open("r", encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream))
        sessions[wave] = rows
        for row in rows:
            status = row["Resolution status"]
            reference = row["Matrix source row"]
            if status not in {"resolved", "unresolved"}:
                errors.append(f"{wave}: {reference}: unexpected status {status!r}")
                continue
            if status != "unresolved":
                continue
            for field in ("Source evidence", "Resolution rationale"):
                if not (row[field] or "").strip():
                    errors.append(f"{wave}: {reference}: {field} is empty")
            for field in ABSENT_API_FIELDS:
                if (row[field] or "").strip() not in {"", ".", "-", "—"}:
                    errors.append(f"{wave}: {reference}: {field} is present")
    no_api_sources: set[str] = set()
    if not NO_API_DISPOSITIONS_PATH.exists():
        errors.append(f"input missing: {NO_API_DISPOSITIONS_PATH}")
    else:
        with NO_API_DISPOSITIONS_PATH.open("r", encoding="utf-8-sig", newline="") as stream:
            dispositions = list(csv.DictReader(stream))
        for row in dispositions:
            source_row = (row.get("Matrix source row") or "").strip()
            if not source_row or source_row in no_api_sources:
                errors.append(f"no-api disposition has invalid or duplicate source row: {source_row!r}")
            else:
                no_api_sources.add(source_row)
            if row.get("Verification owner") != "Task.Desktop":
                errors.append(f"no-api disposition {source_row}: owner is not Task.Desktop")
    unresolved_sources = {
        row["Matrix source row"]
        for rows in sessions.values()
        for row in rows
        if row["Resolution status"] == "unresolved"
    }
    if no_api_sources != unresolved_sources:
        errors.append(
            "no-api disposition coverage mismatch: "
            f"ledger={len(no_api_sources)} initial-unresolved={len(unresolved_sources)}"
        )
    return sessions, no_api_sources, errors


def table(lines: list[str], title: str, headers: tuple[str, ...], rows: list[str]) -> None:
    lines.extend((f"## {title}", "", "| " + " | ".join(headers) + " |"))
    lines.append("|" + "---:|" * (len(headers) - 1) + "---|")
    lines.extend(rows)
    lines.append("")


def build_report(
    sessions: dict[str, list[dict[str, str]]], no_api_sources: set[str], errors: list[str]
) -> str:
    unresolved_by_wave: dict[str, list[dict[str, str]]] = {}
    resolved_by_wave: Counter[str] = Counter()
    by_module: defaultdict[str, list[dict[str, str]]] = defaultdict(list)
    by_type: defaultdict[str, list[dict[str, str]]] = defaultdict(list)
    by_group: defaultdict[tuple[str, str, str, str], list[dict[str, str]]] = defaultdict(list)
    by_no_operation: defaultdict[str, list[dict[str, str]]] = defaultdict(list)

    for wave, rows in sessions.items():
        unresolved = [row for row in rows if row["Resolution status"] == "unresolved"]
        unresolved_by_wave[wave] = unresolved
        resolved_by_wave[wave] = sum(row["Resolution status"] == "resolved" for row in rows)
        for row in unresolved:
            by_module[row["Module"]].append(row)
            by_type[row["Type"]].append(row)
            rationale = row["Resolution rationale"]
            by_group[(wave, row["Module"], row["Type"], rationale)].append(row)
            if NO_OPERATIONID_PHRASE in rationale:
                by_no_operation[rationale].append(row)

    total = sum(map(len, unresolved_by_wave.values()))
    lines = [
        "# Stage 1 initial gap overrides — deterministic disposition audit",
        "",
        "This generated report audits the original unresolved overrides and their no-API dispositions.",
        "It does not prove Desktop implementation or Stage 1 completion.",
        "",
        "## Method",
        "",
        "- Inputs: the three `gap_overrides_wave_*.csv` files in `traceability/`.",
        "- Selection: exact original `Resolution status = unresolved` rows only.",
        "- Disposition ledger: `traceability/desktop_no_api_dispositions.csv`.",
        "- Classification uses exact CSV values; no endpoint or operationId is inferred.",
        "- Output contains no timestamps and all groupings are sorted.",
        "",
        "## Validation",
        "",
        f"- Validation errors: {len(errors)}.",
        f"- Initial unresolved override total: {EXPECTED_UNRESOLVED}; actual: {total}.",
        f"- Reviewed no-API dispositions: {len(no_api_sources)}.",
        f"- Initial unresolved rows covered by the ledger: {sum(row['Matrix source row'] in no_api_sources for rows in sessions.values() for row in rows if row['Resolution status'] == 'unresolved')}.",
        "",
    ]
    lines.extend(f"  - `{md(error)}`" for error in errors)
    if errors:
        lines.append("")

    wave_rows = []
    for wave in sorted(sessions):
        wave_rows.append(
            f"| {wave.upper()} | {len(sessions[wave])} | {resolved_by_wave[wave]} | {len(unresolved_by_wave[wave])} |"
        )
    wave_rows.append(
        f"| **Total** | **{sum(map(len, sessions.values()))}** | **{sum(resolved_by_wave.values())}** | **{total}** |"
    )
    table(lines, "Totals by wave", ("Wave", "Rows read", "resolved", "unresolved"), wave_rows)

    module_rows = [
        f"| {md(key)} | {len(rows)} | {examples(rows)} |" for key, rows in sorted(by_module.items())
    ]
    module_rows.append(f"| **Total** | **{total}** | |")
    table(lines, "Totals by module", ("Module", "unresolved", "Examples"), module_rows)

    type_rows = [f"| {md(key)} | {len(rows)} |" for key, rows in sorted(by_type.items())]
    type_rows.append(f"| **Total** | **{total}** |")
    table(lines, "Totals by type", ("Type", "unresolved"), type_rows)

    group_rows = []
    for (wave, module, type_name, rationale), rows in sorted(by_group.items()):
        group_rows.append(
            f"| {wave.upper()} | {md(module)} | {md(type_name)} | {md(rationale)} | {len(rows)} | {examples(rows)} |"
        )
    group_rows.append(f"| **Total** | | | | **{total}** | |")
    table(
        lines,
        "Groups by Wave, Module, Type and exact Resolution rationale text",
        ("Wave", "Module", "Type", "Resolution rationale", "unresolved", "Examples"),
        group_rows,
    )

    no_operation_total = sum(map(len, by_no_operation.values()))
    lines.extend(
        (
            "## Groups whose sources state there is no confirmed operationId",
            "",
            f"Rows whose exact rationale contains \"{NO_OPERATIONID_PHRASE}\": {no_operation_total}.",
            "",
            "| Resolution rationale | unresolved |",
            "|---|---:|",
        )
    )
    lines.extend(
        f"| {md(rationale)} | {len(rows)} |" for rationale, rows in sorted(by_no_operation.items())
    )
    lines.extend((f"| **Total** | **{no_operation_total}** |", "", "## Sum checks", ""))
    wave_sum = " + ".join(str(len(unresolved_by_wave[wave])) for wave in sorted(unresolved_by_wave))
    lines.extend(
        (
            f"- By wave: {wave_sum} = {total}.",
            f"- By module: {sum(map(len, by_module.values()))} = {total}.",
            f"- By type: {sum(map(len, by_type.values()))} = {total}.",
            f"- By composite group: {sum(map(len, by_group.values()))} = {total}.",
            f"- Total unresolved = {total}; expected = {EXPECTED_UNRESOLVED}.",
            "",
            "## Scope and limitations",
            "",
            "- No endpoint, operationId, permission or handler has been guessed.",
            "- A no-API disposition makes an API link inapplicable; it does not prove a Desktop test is implemented.",
            "- This report does not prove Stage 1 completion.",
            "- Classification is limited to values read verbatim from the CSV inputs.",
            "",
        )
    )
    return "\n".join(lines)


def main() -> int:
    sessions, no_api_sources, errors = read_inputs()
    report = build_report(sessions, no_api_sources, errors)
    REPORT_PATH.write_text(report, encoding="utf-8", newline="\n")
    unresolved = {
        wave: sum(row["Resolution status"] == "unresolved" for row in rows)
        for wave, rows in sessions.items()
    }
    total = sum(unresolved.values())
    print("unresolved by wave: " + ", ".join(f"{wave.upper()}={count}" for wave, count in sorted(unresolved.items())))
    print(f"total unresolved: {total}")
    print(f"no-api dispositions: {len(no_api_sources)}")
    print(f"report: {REPORT_PATH.name}")
    if errors or total != EXPECTED_UNRESOLVED:
        for error in errors:
            print(error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
