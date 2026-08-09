from __future__ import annotations

import csv
import hashlib
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path


ROOT = Path(r"C:\Users\novik\Таск")
WORK = ROOT / "work" / "stage_4_2_audit"
CANDIDATE = (
    WORK / "candidate" / "Organizer_Stage4_PRD_Candidate_4.1.2"
)
STAGE2 = WORK / "stage_2_3_1" / "stage_2_3"
STAGE3 = WORK / "stage_3_5"

TRACE_PATH = CANDIDATE / "Stage_4_Requirements_Traceability_4.1.2.csv"
AC_PATH = CANDIDATE / "Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv"
BR_PATH = CANDIDATE / "Stage_4_Business_Rules_Catalog_4.1.2.csv"
NFR_PATH = CANDIDATE / "Stage_4_NFR_Catalog_4.1.2.csv"
MODULE_PATH = CANDIDATE / "Stage_4_Module_PRDs_4.1.2.md"
OPENAPI_PATH = STAGE2 / "openapi" / "openapi.yaml"

ID_PATTERNS = {
    "module": re.compile(r"\bMOD-\d{3}\b"),
    "fr": re.compile(r"\bFR-\d{3}\b"),
    "br": re.compile(r"\bBR-\d{3}\b"),
    "ac": re.compile(r"\bAC-\d{3,4}\b"),
    "nfr": re.compile(r"\bNFR-\d{3}\b"),
    "scr": re.compile(r"\bSCR-\d{3}\b"),
    "flow": re.compile(r"\bFLOW-\d{3}\b"),
    "state": re.compile(r"\bSTATE-\d{3}\b"),
    "cmp": re.compile(r"\bCMP-\d{3}\b"),
}


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def duplicate_values(values: list[str]) -> dict[str, int]:
    return {value: count for value, count in Counter(values).items() if count > 1}


def parse_openapi_operations(path: Path) -> list[dict[str, str]]:
    operations: list[dict[str, str]] = []
    current_path = ""
    current_method = ""
    for line_no, line in enumerate(read_text(path).splitlines(), start=1):
        path_match = re.match(r"^  (/[^\s]+):\s*$", line)
        if path_match:
            current_path = path_match.group(1)
            current_method = ""
            continue
        method_match = re.match(
            r"^    (get|post|put|patch|delete|head|options|trace):\s*$",
            line,
            flags=re.IGNORECASE,
        )
        if method_match:
            current_method = method_match.group(1).upper()
            continue
        operation_match = re.match(r"^\s+operationId:\s*([^\s#]+)", line)
        if operation_match and current_path and current_method:
            operations.append(
                {
                    "operation_id": operation_match.group(1),
                    "method": current_method,
                    "path": current_path,
                    "line": str(line_no),
                }
            )
    return operations


def all_text_files(root: Path) -> list[Path]:
    return [
        path
        for path in root.rglob("*")
        if path.is_file() and path.suffix.lower() in {".md", ".csv", ".txt", ".yaml", ".yml"}
    ]


def extract_stage3_ids() -> dict[str, set[str]]:
    combined = "\n".join(read_text(path) for path in all_text_files(STAGE3))
    return {
        kind: set(pattern.findall(combined))
        for kind, pattern in ID_PATTERNS.items()
        if kind in {"scr", "flow", "state", "cmp"}
    }


def manifest_rows() -> list[dict[str, object]]:
    manifest = read_text(CANDIDATE / "00_MANIFEST.md")
    rows: list[dict[str, object]] = []
    pattern = re.compile(
        r"^\| ([^|]+?\.(?:md|csv)) \| (\d+) \| `([A-F0-9]{64})` \|",
        flags=re.MULTILINE,
    )
    for name, size, expected_hash in pattern.findall(manifest):
        target = CANDIDATE / name.strip()
        rows.append(
            {
                "file": name.strip(),
                "exists": target.is_file(),
                "expected_size": int(size),
                "actual_size": target.stat().st_size if target.is_file() else None,
                "size_pass": target.is_file() and target.stat().st_size == int(size),
                "expected_sha256": expected_hash,
                "actual_sha256": sha256(target) if target.is_file() else None,
                "sha256_pass": target.is_file() and sha256(target) == expected_hash,
            }
        )
    return rows


def line_occurrences(pattern: str) -> list[dict[str, object]]:
    regex = re.compile(pattern, flags=re.IGNORECASE)
    matches: list[dict[str, object]] = []
    for path in all_text_files(CANDIDATE):
        for line_no, line in enumerate(read_text(path).splitlines(), start=1):
            if regex.search(line):
                matches.append(
                    {
                        "file": path.name,
                        "line": line_no,
                        "text": line[:500],
                    }
                )
    return matches


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    trace = read_csv(TRACE_PATH)
    acceptance = read_csv(AC_PATH)
    business = read_csv(BR_PATH)
    nfr = read_csv(NFR_PATH)
    module_text = read_text(MODULE_PATH)

    trace_ids = [row["Requirement"].strip() for row in trace]
    fr_rows = [row for row in trace if re.fullmatch(r"FR-\d{3}", row["Requirement"].strip())]
    br_trace_rows = [row for row in trace if re.fullmatch(r"BR-\d{3}", row["Requirement"].strip())]
    modules = sorted(set(ID_PATTERNS["module"].findall(module_text)))
    fr_ids = [row["Requirement"].strip() for row in fr_rows]
    br_ids = [row["BR ID"].strip() for row in business]
    ac_ids = [row["AC ID"].strip() for row in acceptance]
    nfr_ids = [row["NFR ID"].strip() for row in nfr]
    fr_set, br_set, ac_set = set(fr_ids), set(br_ids), set(ac_ids)

    parent_refs: dict[str, list[str]] = {}
    ac_without_valid_parent: list[str] = []
    ac_without_direct_fr: list[str] = []
    for row in acceptance:
        ac_id = row["AC ID"].strip()
        refs = ID_PATTERNS["fr"].findall(row.get("FR/BR", "")) + ID_PATTERNS["br"].findall(
            row.get("FR/BR", "")
        )
        parent_refs[ac_id] = refs
        if not any(ref in fr_set or ref in br_set for ref in refs):
            ac_without_valid_parent.append(ac_id)
        if not any(ref in fr_set for ref in refs):
            ac_without_direct_fr.append(ac_id)

    ac_missing_given = [
        row["AC ID"].strip()
        for row in acceptance
        if "given" not in row.get("Gherkin", "").lower()
    ]
    ac_missing_when = [
        row["AC ID"].strip()
        for row in acceptance
        if "when" not in row.get("Gherkin", "").lower()
    ]
    ac_missing_then = [
        row["AC ID"].strip()
        for row in acceptance
        if "then" not in row.get("Gherkin", "").lower()
    ]
    vague_terms = [
        "корректно",
        "удобно",
        "быстро",
        "при необходимости",
        "соответствующим образом",
        "стандартно",
    ]
    ac_vague: dict[str, list[str]] = {}
    for row in acceptance:
        text = f"{row.get('Scenario', '')}\n{row.get('Gherkin', '')}".lower()
        hits = [term for term in vague_terms if term in text]
        if hits:
            ac_vague[row["AC ID"].strip()] = hits

    fr_to_ac: dict[str, set[str]] = defaultdict(set)
    broken_ac_refs: dict[str, list[str]] = {}
    for row in fr_rows:
        fr_id = row["Requirement"].strip()
        refs = ID_PATTERNS["ac"].findall(row.get("AC", ""))
        fr_to_ac[fr_id].update(refs)
        unknown = sorted(set(refs) - ac_set)
        if unknown:
            broken_ac_refs[fr_id] = unknown

    fr_without_ac = sorted(fr for fr in fr_set if not fr_to_ac.get(fr))
    ac_orphan_references = sorted(
        ac_id
        for ac_id, refs in parent_refs.items()
        if any(ref.startswith("FR-") and ref not in fr_set for ref in refs)
        or any(ref.startswith("BR-") and ref not in br_set for ref in refs)
    )

    br_without_related_fr = sorted(
        row["BR ID"].strip()
        for row in business
        if not ID_PATTERNS["fr"].findall(row.get("Related FR", ""))
    )
    br_without_module = sorted(
        row["BR ID"].strip() for row in business if not row.get("Module", "").strip()
    )
    br_without_fr_or_module = sorted(
        row["BR ID"].strip()
        for row in business
        if not ID_PATTERNS["fr"].findall(row.get("Related FR", ""))
        and not row.get("Module", "").strip()
    )

    requirements_without_source = sorted(
        row["Requirement"].strip() for row in trace if not row.get("Source", "").strip()
    )
    orphaned_requirements = sorted(
        row["Requirement"].strip()
        for row in trace
        if not row.get("Module", "").strip()
        or (
            row["Requirement"].startswith(("FR-", "BR-"))
            and row.get("Module", "").strip() not in set(modules) | {"ALL"}
        )
    )

    stage3_ids = extract_stage3_ids()
    ux_unknown: dict[str, list[str]] = {}
    allowed_aliases = {"flow": {"FLOW-038"}}
    for kind, column in (("scr", "SCR"), ("flow", "FLOW"), ("state", "STATE")):
        referenced = {
            value
            for row in trace
            for value in ID_PATTERNS[kind].findall(row.get(column, ""))
        }
        ux_unknown[kind] = sorted(
            referenced - stage3_ids[kind] - allowed_aliases.get(kind, set())
        )
    combined_candidate = "\n".join(read_text(path) for path in all_text_files(CANDIDATE))
    cmp_refs = set(ID_PATTERNS["cmp"].findall(combined_candidate))
    ux_unknown["cmp"] = sorted(cmp_refs - stage3_ids["cmp"])

    permissions = read_csv(STAGE2 / "catalogs" / "permissions.csv")
    errors = read_csv(STAGE2 / "catalogs" / "errors.csv")
    permission_set = {row["code"].strip() for row in permissions}
    error_set = {row["code"].strip() for row in errors}
    permission_refs: set[str] = set()
    permission_candidate_pattern = re.compile(
        r"\b[A-Z][A-Za-z0-9]*(?:\.[A-Z][A-Za-z0-9]*)+\b"
    )
    for row in trace:
        permission_refs.update(permission_candidate_pattern.findall(row.get("Permission", "")))
    unknown_permissions = sorted(permission_refs - permission_set)
    error_refs = {
        code
        for row in trace
        for code in re.findall(r"\b[A-Z][A-Z0-9_]{2,}\b", row.get("Error", ""))
        if "_" in code
    }
    unknown_errors = sorted(error_refs - error_set)

    operations = parse_openapi_operations(OPENAPI_PATH)
    operation_ids = [row["operation_id"] for row in operations]
    operation_set = set(operation_ids)
    covered_by_fr = {
        operation_id
        for row in fr_rows
        for operation_id in operation_set
        if operation_id in row.get("API", "")
    }
    covered_any_trace = {
        operation_id
        for row in trace
        for operation_id in operation_set
        if operation_id in row.get("API", "")
    }
    candidate_operation_refs = set(
        re.findall(r"\b(?:GET|POST|PUT|PATCH|DELETE)_[A-Za-z0-9_]+\b", combined_candidate)
    )
    unknown_operation_refs = sorted(candidate_operation_refs - operation_set)

    module_sections = [
        "A. Паспорт модуля",
        "B. Scope",
        "C. Пользовательские задачи",
        "D. Функциональные требования",
        "E. Бизнес-правила",
        "F. Поля и валидация",
        "G. Permissions",
        "H. Состояния и ошибки",
        "I. Sync, read-only и conflicts",
        "J. Уведомления и аудит",
        "K. Acceptance criteria",
        "L. Нефункциональные требования",
        "M. Аналитика",
        "N. Зависимости и риски",
        "O. Definition of Done",
    ]
    module_blocks = re.split(r"(?=^# MOD-\d{3}\.)", module_text, flags=re.MULTILINE)[1:]
    module_completeness: dict[str, list[str]] = {}
    for block in module_blocks:
        module_id_match = ID_PATTERNS["module"].search(block)
        if module_id_match:
            missing = [section for section in module_sections if section not in block]
            module_completeness[module_id_match.group()] = missing

    result = {
        "manifest": {
            "rows": manifest_rows(),
            "all_pass": all(
                row["size_pass"] and row["sha256_pass"] for row in manifest_rows()
            ),
        },
        "counts": {
            "modules_unique": len(modules),
            "module_ids": modules,
            "trace_rows": len(trace),
            "fr_rows": len(fr_ids),
            "fr_unique": len(fr_set),
            "br_rows": len(br_ids),
            "br_unique": len(br_set),
            "ac_rows": len(ac_ids),
            "ac_unique": len(ac_set),
            "nfr_rows": len(nfr_ids),
            "nfr_unique": len(set(nfr_ids)),
            "openapi_operations": len(operations),
            "openapi_operations_unique": len(operation_set),
            "api_covered_by_fr": len(covered_by_fr),
            "api_covered_any_trace": len(covered_any_trace),
            "permissions_catalog": len(permission_set),
            "stable_errors_catalog": len(error_set),
        },
        "duplicates": {
            "trace_requirement_ids": duplicate_values(trace_ids),
            "fr_ids": duplicate_values(fr_ids),
            "br_ids": duplicate_values(br_ids),
            "ac_ids": duplicate_values(ac_ids),
            "nfr_ids": duplicate_values(nfr_ids),
            "openapi_operation_ids": duplicate_values(operation_ids),
        },
        "relationships": {
            "fr_without_ac": fr_without_ac,
            "fr_broken_ac_refs": broken_ac_refs,
            "ac_without_valid_parent": ac_without_valid_parent,
            "ac_without_direct_fr_count": len(ac_without_direct_fr),
            "ac_without_direct_fr_sample": ac_without_direct_fr[:30],
            "ac_orphan_references": ac_orphan_references,
            "br_without_related_fr_count": len(br_without_related_fr),
            "br_without_related_fr_sample": br_without_related_fr[:30],
            "br_without_module": br_without_module,
            "br_without_fr_or_module": br_without_fr_or_module,
            "requirements_without_source": requirements_without_source,
            "orphaned_requirements": orphaned_requirements,
        },
        "acceptance_quality": {
            "missing_given_count": len(ac_missing_given),
            "missing_given_sample": ac_missing_given[:30],
            "missing_when_count": len(ac_missing_when),
            "missing_when_sample": ac_missing_when[:30],
            "missing_then_count": len(ac_missing_then),
            "missing_then_sample": ac_missing_then[:30],
            "vague_terms_count": len(ac_vague),
            "vague_terms_sample": dict(list(ac_vague.items())[:30]),
        },
        "references": {
            "unknown_scr": ux_unknown["scr"],
            "unknown_flow_except_FLOW_038_alias": ux_unknown["flow"],
            "unknown_state": ux_unknown["state"],
            "unknown_cmp": ux_unknown["cmp"],
            "unknown_permissions": unknown_permissions,
            "unknown_stable_errors": unknown_errors,
            "unknown_operation_refs": unknown_operation_refs,
            "uncovered_openapi_operations_by_fr": sorted(operation_set - covered_by_fr),
            "uncovered_openapi_operations_any_trace": sorted(
                operation_set - covered_any_trace
            ),
        },
        "module_completeness": module_completeness,
        "stale_or_conflicting_occurrences": {
            "241_operations": line_occurrences(r"\b241(?:/241)?\b"),
            "stage_2_2": line_occurrences(r"Stage\s*2\.2"),
            "stage_3_4": line_occurrences(r"Stage\s*3\.4"),
            "oq_remain_high": line_occurrences(r"OQ-001.*remain|OQ-001.*оста[её]тся|OQ-001.*остаются|OQ-001.*remain High"),
            "unverified": line_occurrences(r"\bunverified\b"),
            "provisional": line_occurrences(r"\bprovisional\b"),
        },
    }
    rendered = json.dumps(result, ensure_ascii=False, indent=2)
    (WORK / "audit_metrics.json").write_text(rendered + "\n", encoding="utf-8")
    print(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
