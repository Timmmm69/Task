#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Build a deterministic report for unresolved Stage 1 gap overrides."""

import csv
import sys
from collections import Counter, defaultdict
from pathlib import Path

ANALYSIS_DIR = Path(__file__).resolve().parent
TRACEABILITY_DIR = ANALYSIS_DIR.parent / "traceability"
REPORT_PATH = ANALYSIS_DIR / "UNRESOLVED_GAPS_REPORT.md"
WAVES = ("a", "b", "c")
EXPECTED_UNRESOLVED = 80
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


def read_inputs() -> tuple[dict[str, list[dict[str, str]]], list[str]]:
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
    return sessions, errors


def table(lines: list[str], title: str, headers: tuple[str, ...], rows: list[str]) -> None:
    lines.extend((f"## {title}", "", "| " + " | ".join(headers) + " |"))
    lines.append("|" + "---:|" * (len(headers) - 1) + "---|")
    lines.extend(rows)
    lines.append("")


def build_report(sessions: dict[str, list[dict[str, str]]], errors: list[str]) -> str:
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
        "# Stage 1 unresolved gap overrides — deterministic analysis",
        "",
        "This generated report is NOT a gap resolution. It neither resolves any row nor proves completion of Stage 1.",
        "",
        "## Method",
        "",
        "- Inputs: the three `gap_overrides_wave_*.csv` files in `traceability/`.",
        "- Selection: exact `Resolution status = unresolved` rows only.",
        "- Classification uses exact CSV values; no endpoint or operationId is inferred.",
        "- Output contains no timestamps and all groupings are sorted.",
        "",
        "## Validation",
        "",
        f"- Validation errors: {len(errors)}.",
        f"- Expected unresolved total: {EXPECTED_UNRESOLVED}; actual: {total}.",
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
            "- This report resolves nothing and proves nothing about Stage 1 completion.",
            "- No endpoint, operationId, permission or handler has been guessed.",
            "- Classification is limited to values read verbatim from the CSV inputs.",
            "",
        )
    )
    return "\n".join(lines)


def main() -> int:
    sessions, errors = read_inputs()
    report = build_report(sessions, errors)
    REPORT_PATH.write_text(report, encoding="utf-8", newline="\n")
    unresolved = {
        wave: sum(row["Resolution status"] == "unresolved" for row in rows)
        for wave, rows in sessions.items()
    }
    total = sum(unresolved.values())
    print("unresolved by wave: " + ", ".join(f"{wave.upper()}={count}" for wave, count in sorted(unresolved.items())))
    print(f"total unresolved: {total}")
    print(f"report: {REPORT_PATH.name}")
    if errors or total != EXPECTED_UNRESOLVED:
        for error in errors:
            print(error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
