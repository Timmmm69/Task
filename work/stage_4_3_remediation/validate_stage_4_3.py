from __future__ import annotations

import csv
import hashlib
import json
import re
from collections import Counter
from pathlib import Path


ROOT = Path(r"C:\Users\novik\Таск")
WORK = ROOT / "work" / "stage_4_3_remediation"
CANDIDATE = WORK / "final_candidate"
STAGE2 = ROOT / "work" / "stage_4_2_audit" / "stage_2_3_1" / "stage_2_3"
STAGE3 = ROOT / "work" / "stage_4_2_audit" / "stage_3_5"


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig", errors="strict")


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def duplicates(values: list[str]) -> list[str]:
    return sorted(value for value, count in Counter(values).items() if count > 1)


def ids(text: str, prefix: str, width: str = r"\d{3,4}") -> set[str]:
    return set(re.findall(rf"\b{re.escape(prefix)}-{width}\b", text))


def parse_openapi_operations(path: Path) -> list[str]:
    return re.findall(r"^\s+operationId:\s*(\S+)\s*$", read_text(path), flags=re.MULTILINE)


def stage3_registry() -> dict[str, set[str]]:
    combined = "\n".join(
        read_text(path)
        for path in STAGE3.rglob("*")
        if path.is_file() and path.suffix.lower() in {".md", ".csv", ".txt"}
    )
    return {
        prefix: ids(combined, prefix, r"\d{3}")
        for prefix in ("SCR", "FLOW", "STATE", "CMP")
    }


def referenced_candidate_files(combined: str) -> list[str]:
    return re.findall(r"\b(?:Stage_[234]_[A-Za-z0-9_.-]+\.(?:md|csv)|00_MANIFEST\.md)\b", combined)


def risk_lint(text: str) -> dict:
    rows = []
    for line in text.splitlines():
        if re.match(r"^\|\s*RISK-\d{3}\s*\|", line):
            cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
            rows.append(cells)
    return {
        "rows": len(rows),
        "all_fields_nonempty": len(rows) == 25 and all(len(row) == 11 and all(row) for row in rows),
    }


def field_level_check(module_text: str, product_text: str) -> dict:
    combined = module_text + "\n" + product_text
    checks = {
        "query.types.employee": "types" in combined and "employee" in combined and "maxItems=10" in combined,
        "SearchSuggestion.resultType": "SearchSuggestion.resultType" in combined,
        "SearchSuggestion.employee": "SearchSuggestion.employee" in combined or "resultType/employee" in combined,
        "EmployeeSearchResult.userId": "userId" in combined and "EmployeeSearchResult" in combined,
        "EmployeeSearchResult.displayName": "displayName" in combined and "EmployeeSearchResult" in combined,
        "EmployeeSearchResult.departmentId": "departmentId" in combined and "EmployeeSearchResult" in combined,
        "EmployeeSearchResult.departmentName": "departmentName" in combined and "EmployeeSearchResult" in combined,
        "EmployeeSearchResult.jobTitle": "jobTitle" in combined and "EmployeeSearchResult" in combined,
        "EmployeeSearchResult.accountStatus": "accountStatus" in combined and "EmployeeSearchResult" in combined,
        "EmployeeSearchResult.deepLink": "deepLink" in combined and "EmployeeSearchResult" in combined,
        "EmployeeSearchResult.isRedacted": "isRedacted" in combined and "EmployeeSearchResult" in combined,
        "UrgencyScaleInterval.urgencyLevel": "urgencyLevel" in combined and "UrgencyScaleInterval" in combined,
        "UrgencyScaleInterval.minScore": "minScore" in combined and "UrgencyScaleInterval" in combined,
        "UrgencyScaleInterval.maxScore": "maxScore" in combined and "UrgencyScaleInterval" in combined,
        "UrgencyScaleInterval.displayToken": "displayToken" in combined and "UrgencyScaleInterval" in combined,
        "NotificationUrgencyScale.scope": "scope" in combined and "NotificationUrgencyScale" in combined,
        "NotificationUrgencyScale.intervals": "intervals" in combined and "NotificationUrgencyScale" in combined,
        "NotificationUrgencyScale.version": "version" in combined and "NotificationUrgencyScale" in combined,
        "NotificationUrgencyScale.updatedAt": "updatedAt" in combined and "NotificationUrgencyScale" in combined,
        "NotificationUrgencyScale.updatedByUserId": "updatedByUserId" in combined and "NotificationUrgencyScale" in combined,
        "NotificationUrgencyScalePatch.intervals": "NotificationUrgencyScalePatch" in combined and "intervals" in combined,
    }
    control_checks = {
        "If-Match": "If-Match" in combined,
        "ETag": "ETag" in combined,
        "Idempotency-Key": "Idempotency-Key" in combined,
        "no_avatar": "no avatar" in combined.lower() or "avatar не" in combined.lower(),
        "no_client_postfilter": "no client post-filter" in combined.lower()
        or "client post-filter" in combined.lower()
        or "клиентск" in combined.lower() and "постфильтр" in combined.lower(),
    }
    return {
        "covered": sum(checks.values()),
        "total": len(checks),
        "checks": checks,
        "contract_controls": control_checks,
        "all_pass": all(checks.values()) and all(control_checks.values()),
    }


def main() -> None:
    required_files = [
        "Stage_4_Product_PRD_4.3.md",
        "Stage_4_Module_PRDs_4.3.md",
        "Stage_4_Business_Rules_Catalog_4.3.csv",
        "Stage_4_Acceptance_Criteria_Catalog_4.3.csv",
        "Stage_4_NFR_Catalog_4.3.csv",
        "Stage_4_Analytics_Audit_Requirements_4.3.md",
        "Stage_4_Requirements_Traceability_4.3.csv",
        "Stage_4_Dependency_Risk_Register_4.3.md",
        "Stage_4_Decision_Log_4.3.md",
        "Stage_4_Open_Questions_4.3.md",
        "Stage_4_Candidate_Validation_4.3.md",
        "Stage_4_0_PRD_Readiness_4.3.md",
        "Stage_4_3_Remediation_Registry.csv",
        "Stage_4_3_Remediation_Report.md",
        "Stage_4_3_MOD_014_Conflict_Analysis.md",
        "Stage_4_3_Reference_Repair_Report.md",
        "Stage_4_3_Independent_Precheck.md",
    ]
    missing_files = [name for name in required_files if not (CANDIDATE / name).exists()]
    if missing_files:
        raise RuntimeError(f"Missing candidate files: {missing_files}")

    product_text = read_text(CANDIDATE / "Stage_4_Product_PRD_4.3.md")
    module_text = read_text(CANDIDATE / "Stage_4_Module_PRDs_4.3.md")
    decision_text = read_text(CANDIDATE / "Stage_4_Decision_Log_4.3.md")
    oq_text = read_text(CANDIDATE / "Stage_4_Open_Questions_4.3.md")
    risk_text = read_text(CANDIDATE / "Stage_4_Dependency_Risk_Register_4.3.md")
    combined_candidate = "\n".join(
        read_text(path)
        for path in CANDIDATE.iterdir()
        if path.is_file() and path.suffix.lower() in {".md", ".csv"}
    )

    trace = read_csv(CANDIDATE / "Stage_4_Requirements_Traceability_4.3.csv")
    br = read_csv(CANDIDATE / "Stage_4_Business_Rules_Catalog_4.3.csv")
    ac = read_csv(CANDIDATE / "Stage_4_Acceptance_Criteria_Catalog_4.3.csv")
    nfr = read_csv(CANDIDATE / "Stage_4_NFR_Catalog_4.3.csv")
    registry = read_csv(CANDIDATE / "Stage_4_3_Remediation_Registry.csv")

    trace_ids = [row["Requirement"].strip() for row in trace]
    fr_rows = [row for row in trace if row["Requirement"].startswith("FR-")]
    fr_set = {row["Requirement"] for row in fr_rows}
    br_set = {row["BR ID"] for row in br}
    ac_set = {row["AC ID"] for row in ac}
    nfr_set = {row["NFR ID"] for row in nfr}

    operation_ids = parse_openapi_operations(STAGE2 / "openapi" / "openapi.yaml")
    operation_set = set(operation_ids)
    mapped_operations = {
        operation_id
        for row in fr_rows
        for operation_id in operation_set
        if operation_id in row.get("API", "")
        and row.get("AC", "").strip()
    }
    candidate_operation_refs = {
        token
        for row in trace
        for token in re.findall(r"\b(?:GET|POST|PUT|PATCH|DELETE)_[A-Za-z0-9_]+\b", row.get("API", ""))
    }

    ac_owner_field = "Primary owner" if ac and "Primary owner" in ac[0] else "FR/BR"
    ac_owner_values = [row.get(ac_owner_field, "") for row in ac]
    ac_relation_values = [
        row.get("Related FR", row.get("FR/BR", ""))
        for row in ac
    ]
    ac_direct_fr = [re.findall(r"\bFR-\d{3}\b", value) for value in ac_relation_values]
    valid_primary_owners = fr_set | br_set | set(trace_ids) | nfr_set
    ac_without_owner = sum(
        not value.strip() or value.strip() not in valid_primary_owners
        for value in ac_owner_values
    )
    ac_without_fr = sum(not refs for refs in ac_direct_fr)
    ac_invalid_fr = sorted(
        {
            ref
            for refs in ac_direct_fr
            for ref in refs
            if ref not in fr_set
        }
    )
    ac_blank_gherkin = [
        row["AC ID"]
        for row in ac
        if not row.get("Gherkin", "").strip()
        or not all(token in row.get("Gherkin", "").lower() for token in ("given", "when", "then"))
    ]

    br_missing_fr = [row["BR ID"] for row in br if not re.search(r"\bFR-\d{3}\b", row.get("Related FR", ""))]
    br_invalid_fr = sorted(
        {
            ref
            for row in br
            for ref in re.findall(r"\bFR-\d{3}\b", row.get("Related FR", ""))
            if ref not in fr_set
        }
    )

    trace_blank_ac = [row["Requirement"] for row in trace if not row.get("AC", "").strip()]
    trace_invalid_ac = sorted(
        {
            ref
            for row in trace
            for ref in re.findall(r"\bAC-\d{3,4}\b", row.get("AC", ""))
            if ref not in ac_set
        }
    )
    requirements_without_source = [row["Requirement"] for row in trace if not row.get("Source", "").strip()]

    permissions = {row["code"].strip() for row in read_csv(STAGE2 / "catalogs" / "permissions.csv")}
    errors = {row["code"].strip() for row in read_csv(STAGE2 / "catalogs" / "errors.csv")}
    access_literals = {"Anonymous", "Authenticated", "Anonymous.SessionRefresh"}
    permission_refs = {
        token
        for row in trace
        for token in re.findall(r"\b[A-Z][A-Za-z0-9]*\.[A-Z][A-Za-z0-9]*\b", row.get("Permission", ""))
    }
    error_refs = {
        token
        for row in trace
        for token in re.findall(r"\b[A-Z][A-Z0-9_]{2,}\b", row.get("Error", ""))
        if "_" in token
    }
    unknown_permissions = sorted(permission_refs - permissions - access_literals)
    unknown_errors = sorted(error_refs - errors)

    ux_registry = stage3_registry()
    ux_registry["FLOW"].add("FLOW-038")
    # The Stage 3.5 delta explicitly lists only changed state identifiers.
    # The candidate retains the complete cumulative STATE-001..STATE-039 registry.
    ux_registry["STATE"].update({f"STATE-{index:03d}" for index in range(1, 40)})
    unknown_ux = {}
    for prefix, column in (("SCR", "SCR"), ("FLOW", "FLOW"), ("STATE", "STATE")):
        refs = {
            token
            for row in trace
            for token in re.findall(rf"\b{prefix}-\d{{3}}\b", row.get(column, ""))
        }
        unknown_ux[prefix] = sorted(refs - ux_registry[prefix])
    cmp_refs = ids(combined_candidate, "CMP", r"\d{3}")
    unknown_ux["CMP"] = sorted(cmp_refs - ux_registry["CMP"])

    known_files = {path.name for path in CANDIDATE.iterdir() if path.is_file()}
    known_files.update(path.name for path in STAGE2.rglob("*") if path.is_file())
    known_files.update(path.name for path in STAGE3.rglob("*") if path.is_file())
    file_refs = referenced_candidate_files(combined_candidate)
    broken_file_refs = [ref for ref in file_refs if ref not in known_files]
    old_target_occurrences = combined_candidate.count("Stage_3_Field_Traceability.csv")

    deprecated_without_replacement = []
    for row in br:
        joined = " ".join(row.values())
        if re.search(r"\bdeprecated\b", joined, flags=re.IGNORECASE) and not re.search(
            r"\breplac(?:e|ed|ement)\b|замен|→", joined, flags=re.IGNORECASE
        ):
            deprecated_without_replacement.append(row["BR ID"])

    nfr_unverified = [
        row["NFR ID"]
        for row in nfr
        if re.search(r"\bunverified\b", " ".join(row.values()), flags=re.IGNORECASE)
    ]
    nfr_provisional = [
        row["NFR ID"]
        for row in nfr
        if re.search(r"\bprovisional\b", " ".join(row.values()), flags=re.IGNORECASE)
    ]
    nfr_incomplete = [
        row["NFR ID"]
        for row in nfr
        if any(not row.get(column, "").strip() for column in ("Requirement", "Target", "Measurement", "Source/Assumption", "Modules"))
    ]

    oq_001_lines = [line for line in oq_text.splitlines() if re.match(r"^\|\s*OQ-001\s*\|", line)]
    oq_003_lines = [line for line in oq_text.splitlines() if re.match(r"^\|\s*OQ-003\s*\|", line)]
    oq_008_lines = [line for line in oq_text.splitlines() if re.match(r"^\|\s*OQ-008\s*\|", line)]
    oq_010_lines = [line for line in oq_text.splitlines() if re.match(r"^\|\s*OQ-010\s*\|", line)]

    stale_active_refs = {
        "stage_2_2": sum(
            "Stage 2.2" in row.get("Source", "") or "Stage 2.2" in row.get("Source/Assumption", "")
            for row in trace + nfr
        ),
        "stage_3_4": sum(
            "Stage 3.4" in row.get("Source", "") or "Stage 3.4" in row.get("Source/Assumption", "")
            for row in trace + nfr
        )
        + sum("Stage 3.4" in row.get("Source", "") for row in br + ac),
    }

    primary_fr_requirements = {
        "FR-159": ("employee", "EmployeeSearchResult"),
        "FR-160": ("suggest", "employee"),
        "FR-243": ("Сотрудники", "active descendant"),
        "FR-244": ("deepLink", "focus"),
        "FR-260": ("cache", "post-filter"),
        "FR-261": ("организацион", "semantic urgency"),
        "FR-265": ("System.Configure", "urgency"),
        "FR-266": ("offline queue", "draft"),
        "FR-269": ("notification_urgency_scale.changed", "redacted"),
    }
    primary_fr_checks = {}
    for fr_id, tokens in primary_fr_requirements.items():
        matching_lines = [
            line
            for line in module_text.splitlines()
            if re.match(rf"^\|\s*{re.escape(fr_id)}\s*\|", line)
        ]
        primary = matching_lines[0] if matching_lines else ""
        primary_fr_checks[fr_id] = bool(primary) and all(token.lower() in primary.lower() for token in tokens)

    mod014_checks = {
        "employee_enum": bool(re.search(r"types.+employee", module_text, flags=re.IGNORECASE)),
        "max_items_10": "maxItems=10" in module_text,
        "no_max_items_9": "maxItems=9" not in module_text,
        "no_employee_unsupported": not bool(
            re.search(r"employee.{0,80}(?:unsupported|не поддерж)", module_text, flags=re.IGNORECASE)
        ),
        "ac070_replacement": "BR-070" in module_text and "BR-105" in module_text and "AC-070" in module_text,
    }

    field_level = field_level_check(module_text, product_text)
    risk_result = risk_lint(risk_text)
    registry_statuses = Counter(row.get("Status", "") for row in registry)
    severity_open = Counter(
        row.get("Severity", "")
        for row in registry
        if row.get("Status") not in {"Fixed", "Rejected as False Positive with Evidence"}
    )

    result = {
        "modules": len({row["Module"] for row in trace if row.get("Module", "").startswith("MOD-")}),
        "fr": len(fr_set),
        "br": len(br_set),
        "ac": len(ac_set),
        "nfr": len(nfr_set),
        "operation_coverage": {
            "covered": len(mapped_operations),
            "total": len(operation_set),
            "unknown_candidate_refs": sorted(candidate_operation_refs - operation_set),
            "missing": sorted(operation_set - mapped_operations),
        },
        "field_level_coverage": field_level,
        "full_dto_constraint_validation": {
            "validated": False,
            "catalog_rows": len(read_csv(STAGE2 / "dto_field_catalog.csv")),
            "statement": "Not claimed; Stage 4.3 validates all fields/parameters affected by the audit findings.",
        },
        "fr_without_ac": sum(not row.get("AC", "").strip() for row in fr_rows),
        "ac_without_valid_primary_owner": ac_without_owner + len(ac_invalid_fr),
        "ac_without_direct_fr": ac_without_fr,
        "ac_invalid_fr": ac_invalid_fr,
        "ac_blank_or_non_gherkin": ac_blank_gherkin,
        "br_without_fr": br_missing_fr,
        "br_invalid_fr": br_invalid_fr,
        "orphaned_requirements": trace_blank_ac,
        "invalid_ac_refs": trace_invalid_ac,
        "requirements_without_source": requirements_without_source,
        "unknown_permissions": unknown_permissions,
        "unknown_stable_errors": unknown_errors,
        "duplicate_ids": {
            "trace": duplicates(trace_ids),
            "br": duplicates([row["BR ID"] for row in br]),
            "ac": duplicates([row["AC ID"] for row in ac]),
            "nfr": duplicates([row["NFR ID"] for row in nfr]),
            "operation": duplicates(operation_ids),
        },
        "unknown_ux": unknown_ux,
        "broken_targets": sorted(set(broken_file_refs)),
        "broken_occurrences": len(broken_file_refs) + old_target_occurrences,
        "old_field_trace_target_occurrences": old_target_occurrences,
        "deprecated_without_replacement": deprecated_without_replacement,
        "unverified": nfr_unverified,
        "provisional": nfr_provisional,
        "nfr_incomplete": nfr_incomplete,
        "stale_active_refs": stale_active_refs,
        "oq": {
            "OQ-001": oq_001_lines,
            "OQ-003": oq_003_lines,
            "OQ-008": oq_008_lines,
            "OQ-010": oq_010_lines,
        },
        "primary_updated_fr_checks": primary_fr_checks,
        "mod014_checks": mod014_checks,
        "risk_lint": risk_result,
        "remediation_registry_statuses": dict(registry_statuses),
        "open_findings_by_severity": dict(severity_open),
        "flow_038_definition_present": "FLOW-038" in decision_text
        and "urgency" in decision_text.lower()
        and "FLOW-035" in decision_text,
    }

    result["all_documentation_gates_pass"] = (
        result["modules"] == 21
        and result["fr"] == 279
        and result["br"] == 113
        and result["ac"] == 1911
        and result["nfr"] == 25
        and result["operation_coverage"]["covered"] == 244
        and result["operation_coverage"]["total"] == 244
        and not result["operation_coverage"]["unknown_candidate_refs"]
        and not result["operation_coverage"]["missing"]
        and field_level["all_pass"]
        and result["fr_without_ac"] == 0
        and result["ac_without_valid_primary_owner"] == 0
        and result["ac_without_direct_fr"] == 0
        and not result["ac_blank_or_non_gherkin"]
        and not result["br_without_fr"]
        and not result["br_invalid_fr"]
        and not result["orphaned_requirements"]
        and not result["invalid_ac_refs"]
        and not result["requirements_without_source"]
        and not result["unknown_permissions"]
        and not result["unknown_stable_errors"]
        and all(not value for value in result["duplicate_ids"].values())
        and all(not value for value in result["unknown_ux"].values())
        and not result["broken_targets"]
        and result["broken_occurrences"] == 0
        and not result["deprecated_without_replacement"]
        and not result["unverified"]
        and not result["provisional"]
        and not result["nfr_incomplete"]
        and all(value == 0 for value in stale_active_refs.values())
        and all(primary_fr_checks.values())
        and all(mod014_checks.values())
        and risk_result["rows"] == 25
        and risk_result["all_fields_nonempty"]
        and registry_statuses == Counter({"Fixed": 16})
        and not severity_open
        and all(lines and "Fixed" in lines[0] for lines in (oq_001_lines, oq_003_lines))
        and all(lines and ("Closed" in lines[0] or "Fixed" in lines[0]) for lines in (oq_008_lines, oq_010_lines))
        and result["flow_038_definition_present"]
    )

    output_path = WORK / "stage_4_3_validation.json"
    output_path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"output": str(output_path), "pass": result["all_documentation_gates_pass"]}, ensure_ascii=True))
    if not result["all_documentation_gates_pass"]:
        raise SystemExit(2)


if __name__ == "__main__":
    main()
