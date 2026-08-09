from __future__ import annotations

import csv
import json
import re
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path


ROOT = Path(r"C:\Users\novik\Таск")
WORK = ROOT / "work" / "stage_4_3_remediation"
CANDIDATE = WORK / "candidate_4_3"
STAGE23 = ROOT / "work" / "stage_4_2_audit" / "stage_2_3_1" / "stage_2_3"

BR_PATH = CANDIDATE / "Stage_4_Business_Rules_Catalog_4.1.2.csv"
AC_PATH = CANDIDATE / "Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv"
TRACE_PATH = CANDIDATE / "Stage_4_Requirements_Traceability_4.1.2.csv"
MODULE_PRD_PATH = CANDIDATE / "Stage_4_Module_PRDs_4.3.md"
API_PATH = STAGE23 / "catalogs" / "api_catalog.csv"
DTO_PATH = STAGE23 / "dto_field_catalog.csv"
OUTPUT_JSON = WORK / "semantic_maps.json"
OUTPUT_REPORT = WORK / "semantic_mapping_report.md"


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def id_num(identifier: str) -> int:
    match = re.search(r"(\d+)$", identifier)
    return int(match.group(1)) if match else 0


def sorted_ids(values: set[str] | list[str]) -> list[str]:
    return sorted(set(values), key=lambda value: (value.split("-", 1)[0], id_num(value)))


br_rows = read_csv(BR_PATH)
ac_rows = read_csv(AC_PATH)
trace_rows = read_csv(TRACE_PATH)
api_rows = read_csv(API_PATH)
dto_rows = read_csv(DTO_PATH)
module_prd_lines = MODULE_PRD_PATH.read_text(encoding="utf-8").splitlines()

trace_by_requirement = {row["Requirement"]: row for row in trace_rows}
fr_trace = {
    row["Requirement"]: row
    for row in trace_rows
    if re.fullmatch(r"FR-\d{3}", row["Requirement"])
}
ac_by_id = {row["AC ID"]: row for row in ac_rows}
br_by_id = {row["BR ID"]: row for row in br_rows}

fr_description: dict[str, str] = {}
for line in module_prd_lines:
    cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
    if not cells or not re.fullmatch(r"FR-\d{3}", cells[0]):
        continue
    fr_id = cells[0]
    if fr_id in fr_description:
        continue
    if len(cells) >= 4 and re.fullmatch(r"MOD-\d{3}", cells[1]):
        fr_description[fr_id] = cells[2]
    elif len(cells) >= 2:
        fr_description[fr_id] = cells[1]

api_by_key: dict[tuple[str, str], dict[str, str]] = {}
api_by_operation: dict[str, dict[str, str]] = {}
fr_by_operation: dict[str, str] = {}
operation_by_fr: dict[str, str] = {}

for row in api_rows:
    api_by_key[(row["method"].upper(), row["path"])] = row

for fr_id, row in fr_trace.items():
    match = re.match(r"^([A-Z]+)\s+(\S+)\s+\(([^)]+)\)", row["API"])
    if not match:
        continue
    method, path, operation_id = match.groups()
    api_row = api_by_key.get((method, path))
    if not api_row:
        continue
    if operation_id in fr_by_operation:
        raise RuntimeError(f"operationId is not unique in trace: {operation_id}")
    fr_by_operation[operation_id] = fr_id
    operation_by_fr[fr_id] = operation_id
    api_by_operation[operation_id] = api_row

if len(fr_by_operation) != len(api_rows):
    raise RuntimeError(
        f"Stage 2.3.1 operation-to-FR coverage is not bijective: "
        f"{len(fr_by_operation)} trace matches for {len(api_rows)} APIs"
    )


# Curated semantic relations for inherited business rules. Each relation points
# only to existing FRs whose wording/API behavior implements the rule.
CURATED_BR_TO_FR: dict[str, list[str]] = {
    "BR-001": ["FR-008", "FR-233", "FR-234"],
    "BR-002": ["FR-003", "FR-221", "FR-222", "FR-223", "FR-224", "FR-275", "FR-277"],
    "BR-003": ["FR-231", "FR-232", "FR-233", "FR-234", "FR-260", "FR-266", "FR-267"],
    "BR-004": ["FR-210", "FR-228"],
    "BR-005": ["FR-029", "FR-032", "FR-038", "FR-166", "FR-168"],
    "BR-006": ["FR-166", "FR-167", "FR-168", "FR-169", "FR-170", "FR-262"],
    "BR-007": ["FR-099", "FR-169", "FR-263"],
    "BR-008": ["FR-103", "FR-108", "FR-112", "FR-256", "FR-257"],
    "BR-009": ["FR-031", "FR-087", "FR-172", "FR-174", "FR-176", "FR-271"],
    "BR-010": ["FR-019", "FR-027", "FR-084", "FR-232", "FR-271"],
    "BR-011": ["FR-235", "FR-236", "FR-237", "FR-238", "FR-239", "FR-240", "FR-241", "FR-269"],
    "BR-012": ["FR-231", "FR-232", "FR-233", "FR-234", "FR-268"],
    "BR-013": ["FR-159", "FR-275", "FR-277", "FR-278"],
    "BR-014": ["FR-210", "FR-228", "FR-244", "FR-276"],
    "BR-015": ["FR-031", "FR-087", "FR-101", "FR-117", "FR-141", "FR-172", "FR-174", "FR-176"],
    "BR-016": ["FR-003", "FR-221", "FR-222"],
    "BR-017": ["FR-007"],
    "BR-018": ["FR-006", "FR-226", "FR-227"],
    "BR-019": ["FR-003"],
    "BR-020": ["FR-008", "FR-244", "FR-245"],
    "BR-021": ["FR-245"],
    "BR-022": ["FR-015", "FR-016", "FR-244"],
    "BR-023": ["FR-244", "FR-245"],
    "BR-024": ["FR-017", "FR-249"],
    "BR-025": ["FR-246"],
    "BR-026": ["FR-246"],
    "BR-027": ["FR-017"],
    "BR-028": ["FR-018", "FR-019", "FR-021", "FR-022"],
    "BR-029": ["FR-023", "FR-024"],
    "BR-030": ["FR-023", "FR-024", "FR-247"],
    "BR-031": ["FR-028", "FR-038", "FR-250"],
    "BR-032": ["FR-032", "FR-038"],
    "BR-033": ["FR-031", "FR-033", "FR-036", "FR-038", "FR-040", "FR-248"],
    "BR-034": ["FR-028", "FR-038", "FR-250"],
    "BR-035": ["FR-038", "FR-250"],
    "BR-036": ["FR-050", "FR-051"],
    "BR-037": ["FR-051", "FR-251"],
    "BR-038": ["FR-041", "FR-042", "FR-043", "FR-044", "FR-045", "FR-046", "FR-047", "FR-048", "FR-049"],
    "BR-039": ["FR-046"],
    "BR-040": ["FR-053", "FR-056"],
    "BR-041": ["FR-057", "FR-058"],
    "BR-042": ["FR-057", "FR-252"],
    "BR-043": ["FR-054"],
    "BR-044": ["FR-063", "FR-067"],
    "BR-045": ["FR-070", "FR-253"],
    "BR-046": ["FR-063", "FR-068", "FR-070"],
    "BR-047": ["FR-071", "FR-082"],
    "BR-048": ["FR-071"],
    "BR-049": ["FR-071"],
    "BR-050": ["FR-071", "FR-073", "FR-076"],
    "BR-051": ["FR-084", "FR-095"],
    "BR-052": ["FR-087", "FR-088", "FR-096"],
    "BR-053": ["FR-085"],
    "BR-054": ["FR-093", "FR-228"],
    "BR-055": ["FR-097", "FR-098", "FR-100", "FR-101", "FR-103", "FR-104", "FR-105", "FR-106", "FR-108", "FR-112"],
    "BR-056": ["FR-103", "FR-108", "FR-112", "FR-256", "FR-257"],
    "BR-057": ["FR-107"],
    "BR-058": ["FR-099", "FR-102", "FR-109", "FR-110", "FR-169", "FR-263"],
    "BR-059": ["FR-103", "FR-104", "FR-105", "FR-106", "FR-108", "FR-112", "FR-256"],
    "BR-060": ["FR-119", "FR-123", "FR-124"],
    "BR-061": ["FR-115", "FR-118", "FR-121", "FR-122", "FR-125", "FR-129", "FR-133", "FR-134"],
    "BR-062": ["FR-130", "FR-131", "FR-132", "FR-258"],
    "BR-063": ["FR-136", "FR-141"],
    "BR-064": ["FR-135", "FR-139"],
    "BR-065": ["FR-143", "FR-146"],
    "BR-066": ["FR-149", "FR-150", "FR-151", "FR-259"],
    "BR-067": ["FR-159", "FR-275", "FR-277"],
    "BR-068": ["FR-159", "FR-278"],
    "BR-069": ["FR-159", "FR-278"],
    "BR-071": ["FR-161", "FR-162", "FR-163", "FR-164", "FR-165"],
    "BR-072": ["FR-164"],
    "BR-073": ["FR-164"],
    "BR-074": ["FR-163", "FR-164"],
    "BR-075": ["FR-166", "FR-167", "FR-262"],
    "BR-076": ["FR-166", "FR-167", "FR-262"],
    "BR-077": ["FR-167"],
    "BR-078": ["FR-168", "FR-169", "FR-170"],
    "BR-079": ["FR-170"],
    "BR-080": ["FR-169"],
    "BR-081": ["FR-168", "FR-169", "FR-227"],
    "BR-082": ["FR-171", "FR-172", "FR-173", "FR-174", "FR-175", "FR-176"],
    "BR-083": ["FR-172", "FR-174", "FR-176"],
    "BR-084": ["FR-264"],
    "BR-085": ["FR-228", "FR-265"],
    "BR-086": ["FR-210", "FR-213", "FR-214", "FR-215", "FR-216", "FR-217", "FR-218", "FR-219"],
    "BR-087": ["FR-178", "FR-179", "FR-180"],
    "BR-088": ["FR-179", "FR-181", "FR-184", "FR-186", "FR-187", "FR-212", "FR-219", "FR-220", "FR-226", "FR-227"],
    "BR-089": ["FR-231", "FR-232", "FR-233", "FR-234", "FR-267"],
    "BR-090": ["FR-266"],
    "BR-091": ["FR-268"],
    "BR-092": ["FR-231", "FR-232", "FR-233", "FR-234"],
    "BR-093": ["FR-266", "FR-267"],
    "BR-094": ["FR-235"],
    "BR-095": ["FR-237", "FR-238", "FR-239", "FR-240", "FR-241"],
    "BR-096": ["FR-235", "FR-236", "FR-269"],
    "BR-097": ["FR-236"],
}

MEDIUM_BR = {
    "BR-001", "BR-002", "BR-003", "BR-004", "BR-005", "BR-009",
    "BR-010", "BR-014", "BR-015", "BR-016", "BR-018", "BR-021",
    "BR-022", "BR-024", "BR-026", "BR-046", "BR-053", "BR-055",
    "BR-058", "BR-061", "BR-071", "BR-081", "BR-082", "BR-088",
}

br_to_fr: list[dict[str, object]] = []
br_map: dict[str, list[str]] = {}

for br in br_rows:
    br_id = br["BR ID"]
    preserved = re.findall(r"\bFR-\d{3}\b", br.get("Related FR", ""))
    fr_ids = preserved or CURATED_BR_TO_FR.get(br_id, [])
    if not fr_ids:
        raise RuntimeError(f"No semantic FR relation for {br_id}")
    unknown = [fr_id for fr_id in fr_ids if fr_id not in fr_trace]
    if unknown:
        raise RuntimeError(f"Unknown FRs for {br_id}: {unknown}")
    fr_ids = sorted_ids(fr_ids)
    br_map[br_id] = fr_ids
    confidence = "High" if preserved or br_id not in MEDIUM_BR else "Medium"
    method = (
        "preserved_normative_relation"
        if preserved
        else "curated_rule_to_existing_fr_semantic_match"
    )
    evidence = []
    for fr_id in fr_ids:
        trace = fr_trace[fr_id]
        evidence.append(
            {
                "fr_id": fr_id,
                "module": trace["Module"],
                "api": trace["API"],
                "module_prd_excerpt": fr_description.get(fr_id, ""),
                "sources": [
                    "Stage_4_Module_PRDs_4.3.md",
                    "Stage_4_Requirements_Traceability_4.1.2.csv",
                ],
            }
        )
    br_to_fr.append(
        {
            "br_id": br_id,
            "module": br["Module"],
            "rule": br["Rule"],
            "fr_ids": fr_ids,
            "verification_ac_ids": sorted_ids(
                re.findall(r"\bAC-\d{3,4}\b", br.get("Verification", ""))
            ),
            "method": method,
            "confidence": confidence,
            "evidence": evidence,
            "notes": (
                "Global or cross-module applicability represented by the smallest "
                "semantically sufficient existing FR set; independent owner review recommended."
                if confidence == "Medium"
                else "Direct rule/FR semantics and existing verification agree."
            ),
        }
    )


def schema_pattern(schema: str) -> re.Pattern[str]:
    return re.compile(
        rf"(?<![A-Za-z0-9_]){re.escape(schema)}(?![A-Za-z0-9_])"
    )


def direction_for_schema(api: dict[str, str], schema: str) -> str:
    pattern = schema_pattern(schema)
    in_request = bool(pattern.search(api.get("request", "")))
    in_response = bool(pattern.search(api.get("response", "")))
    if in_request and in_response:
        return "request+response"
    if in_request:
        return "request"
    if in_response:
        return "response"
    return "source-explicit"


def operation_from_query_source(source: str) -> str | None:
    match = re.search(r"openapi\.yaml#/paths//(.+?)/(get|post|put|patch|delete)/parameters/", source)
    if not match:
        return None
    path = "/" + match.group(1)
    method = match.group(2).upper()
    api = api_by_key.get((method, path))
    if not api:
        return None
    for operation_id, candidate in api_by_operation.items():
        if candidate is api:
            return operation_id
    return None


data_owner_evidence: list[dict[str, object]] = []
strategy_counts: Counter[str] = Counter()
multi_operation_data: list[dict[str, object]] = []

for ac in ac_rows:
    owner = ac["FR/BR"]
    if not re.fullmatch(r"DATA-\d{3}", owner):
        continue
    scenario_match = re.search(
        r"Field contract:\s+(.+?)\.(.+?)\s+respects", ac["Scenario"]
    )
    if not scenario_match:
        raise RuntimeError(f"Cannot parse DATA scenario for {ac['AC ID']}")
    scenario_schema, scenario_field = scenario_match.groups()
    source = ac["Source"]
    source_schema_match = re.search(
        r"(?:dto_field_catalog\.csv:|openapi\.yaml:)([A-Za-z0-9_]+)\.([^;]+)",
        source,
    )
    schema = (
        source_schema_match.group(1)
        if scenario_schema == "—" and source_schema_match
        else scenario_schema
    )
    field = (
        source_schema_match.group(2).strip()
        if scenario_schema == "—" and source_schema_match
        else scenario_field.strip()
    )

    operation_ids: list[str] = []
    explicit_match = re.search(r"openapi\.yaml operation ([A-Za-z0-9_]+)", source)
    if explicit_match:
        operation_ids = [explicit_match.group(1)]
        strategy = "source_explicit_operation_id"
    else:
        query_operation = operation_from_query_source(source)
        if query_operation:
            operation_ids = [query_operation]
            strategy = "source_exact_openapi_path"
        else:
            pattern = schema_pattern(schema)
            matching = [
                operation_id
                for operation_id, api in api_by_operation.items()
                if pattern.search(api.get("request", ""))
                or pattern.search(api.get("response", ""))
            ]
            same_module = [
                operation_id
                for operation_id in matching
                if fr_trace[fr_by_operation[operation_id]]["Module"] == ac["Module"]
            ]
            operation_ids = sorted(same_module or matching)
            strategy = (
                "schema_request_response_exact_with_module_scope"
                if same_module
                else "schema_request_response_exact_cross_module"
            )
    unknown_ops = [op for op in operation_ids if op not in fr_by_operation]
    if unknown_ops:
        raise RuntimeError(f"Unknown operationIds for {ac['AC ID']}: {unknown_ops}")
    if not operation_ids:
        raise RuntimeError(f"No operation/FR chain for DATA AC {ac['AC ID']}")

    fr_ids = sorted_ids([fr_by_operation[op] for op in operation_ids])
    operations = []
    for operation_id in operation_ids:
        api = api_by_operation[operation_id]
        fr_id = fr_by_operation[operation_id]
        operations.append(
            {
                "operation_id": operation_id,
                "method": api["method"],
                "path": api["path"],
                "direction": direction_for_schema(api, schema),
                "request": api["request"],
                "response": api["response"],
                "fr_id": fr_id,
                "fr_module": fr_trace[fr_id]["Module"],
            }
        )
    confidence = "High" if len(operation_ids) == 1 else "Medium"
    strategy_counts[strategy] += 1
    if len(operation_ids) > 1:
        multi_operation_data.append(
            {
                "ac_id": ac["AC ID"],
                "schema": schema,
                "field": field,
                "operation_count": len(operation_ids),
                "fr_ids": fr_ids,
            }
        )
    data_owner_evidence.append(
        {
            "ac_id": ac["AC ID"],
            "primary_owner": owner,
            "ac_module": ac["Module"],
            "schema": schema,
            "field": field,
            "fr_ids": fr_ids,
            "operations": operations,
            "method": strategy,
            "confidence": confidence,
            "source": source,
            "evidence_chain": [
                f"{schema}.{field}",
                "Stage 2.3.1 api_catalog request/response or exact OpenAPI Source",
                "method/path/operationId",
                "Stage 4 trace operationId -> existing FR",
            ],
            "notes": (
                "One schema is contractually used by several module operations; all exact "
                "operation-to-FR relations are retained."
                if confidence == "Medium"
                else "Single exact operation-to-FR chain."
            ),
        }
    )


def ac_fr_ids(ac_id: str) -> list[str]:
    owner = ac_by_id[ac_id]["FR/BR"]
    if re.fullmatch(r"FR-\d{3}", owner):
        return [owner]
    if re.fullmatch(r"BR-\d{3}", owner):
        return br_map[owner]
    return []


def module_ac_ids(module: str, pattern: str) -> list[str]:
    regex = re.compile(pattern, re.IGNORECASE)
    return sorted_ids(
        [
            row["AC ID"]
            for row in ac_rows
            if row["Module"] == module and regex.search(row["Scenario"])
        ]
    )


cross_cutting_to_ac: list[dict[str, object]] = []


def add_cross_mapping(
    requirement_id: str,
    ac_ids: list[str],
    method: str,
    confidence: str,
    notes: str,
) -> None:
    trace = trace_by_requirement[requirement_id]
    resolved = [ac_id for ac_id in sorted_ids(ac_ids) if ac_id in ac_by_id]
    if not resolved:
        raise RuntimeError(f"No AC evidence for {requirement_id}")
    linked_fr_ids = sorted_ids(
        {
            fr_id
            for ac_id in resolved
            for fr_id in ac_fr_ids(ac_id)
            if fr_id in fr_trace
        }
    )
    cross_cutting_to_ac.append(
        {
            "requirement_id": requirement_id,
            "category": requirement_id.split("-", 1)[0],
            "module": trace["Module"],
            "source": trace["Source"],
            "owner": "cross-cutting requirement ledger",
            "incoming_references": [],
            "outgoing_api": trace["API"],
            "outgoing_ux": f"{trace['FLOW']};{trace['STATE']}".strip(";"),
            "ac_ids": resolved,
            "linked_fr_ids": linked_fr_ids,
            "method": method,
            "confidence": confidence,
            "status": "active",
            "notes": notes,
        }
    )


add_cross_mapping(
    "DATA-002",
    ["AC-137", "AC-139", "AC-141", "AC-142", "AC-143", "AC-145"],
    "read_only_module_response_contract_happy_paths",
    "High",
    "MOD-002 has no editable DTO-field AC set; existing happy-path ACs verify each response-bearing operation.",
)
add_cross_mapping(
    "DATA-003",
    ["AC-147", "AC-150", "AC-1407"],
    "today_projection_contract_and_validation",
    "High",
    "GET /today contract, validation recovery, and section-isolation desktop behavior are all verified.",
)
add_cross_mapping(
    "DATA-016",
    ["AC-1032", "AC-1035", "AC-1038", "AC-1039", "AC-1427"],
    "archive_read_restore_contract_and_validation",
    "High",
    "Archive read/restore DTO behavior, validation, version conflict, and archived presentation are verified.",
)

for index in range(1, 22):
    module = f"MOD-{index:03d}"
    requirement_id = f"PERM-{index:03d}"
    permission_acs = module_ac_ids(module, r"Permission denied:")
    if not permission_acs and module == "MOD-002":
        permission_acs = ["AC-022", "AC-023", "AC-138", "AC-140", "AC-144", "AC-146"]
        confidence = "Medium"
        note = (
            "MOD-002 exposes Anonymous/Authenticated reads and therefore has no generated "
            "FORBIDDEN scenario; server-session interruption plus hidden-control/deep-link "
            "non-disclosure ACs provide the existing verification set."
        )
    else:
        confidence = "High"
        note = "All module-specific generated permission-denied scenarios plus global authorization rules."
    add_cross_mapping(
        requirement_id,
        ["AC-004", "AC-014", *permission_acs],
        "module_permission_denial_scenarios_and_global_server_enforcement",
        confidence,
        note,
    )

for index in range(1, 22):
    module = f"MOD-{index:03d}"
    requirement_id = f"ERR-{index:03d}"
    error_acs = module_ac_ids(
        module,
        r"Validation:|Conflict/precondition:|Server unavailable/read-only:|"
        r"Session/device interruption:|Permission denied:|Idempotency:",
    )
    add_cross_mapping(
        requirement_id,
        error_acs,
        "module_negative_scenarios_by_stable_error_state",
        "High",
        "Existing module ACs explicitly exercise validation, permission, session, conflict, outage, and idempotency recovery states.",
    )

sync_core = [
    "AC-003", "AC-009", "AC-012",
    "AC-1364", "AC-1365", "AC-1367", "AC-1368",
    "AC-1369", "AC-1370", "AC-1372", "AC-1373", "AC-1374",
    "AC-1376", "AC-1377", "AC-1379", "AC-1380",
    "AC-1433", "AC-1434",
]
for index in range(1, 22):
    module = f"MOD-{index:03d}"
    requirement_id = f"SYNC-{index:03d}"
    local = module_ac_ids(module, r"Conflict/precondition:|Server unavailable/read-only:")
    add_cross_mapping(
        requirement_id,
        [*sync_core, *local],
        "sync_endpoint_core_plus_module_outage_conflict_semantics",
        "High",
        "Shared sync endpoint ACs verify cursor/scope/outage behavior; module-local ACs verify write blocking and conflict recovery.",
    )

happy_ac_by_fr: dict[str, list[str]] = defaultdict(list)
for row in ac_rows:
    if re.fullmatch(r"FR-\d{3}", row["FR/BR"]) and re.search(
        r"(?:^|\]\s*)Happy path:", row["Scenario"]
    ):
        happy_ac_by_fr[row["FR/BR"]].append(row["AC ID"])

audit_core = [
    "AC-011",
    "AC-1381", "AC-1382",
    "AC-1384", "AC-1385",
    "AC-1388", "AC-1389",
    "AC-1391", "AC-1392",
]
audit_fr_by_module: dict[str, set[str]] = defaultdict(set)
for operation_id, api in api_by_operation.items():
    if re.search(r"\baudit\b", api.get("effects", ""), re.IGNORECASE):
        fr_id = fr_by_operation[operation_id]
        audit_fr_by_module[fr_trace[fr_id]["Module"]].add(fr_id)

for index in range(1, 22):
    module = f"MOD-{index:03d}"
    requirement_id = f"AUDIT-{index:03d}"
    local = []
    for fr_id in audit_fr_by_module.get(module, set()):
        local.extend(happy_ac_by_fr.get(fr_id, []))
    if module == "MOD-018":
        local.append("AC-1801")
    if module == "MOD-019":
        local.append("AC-088")
    if module == "MOD-021":
        local.extend(["AC-094", "AC-095", "AC-096", "AC-097", "AC-1435", "AC-1801"])
    confidence = "High" if local or module == "MOD-021" else "Medium"
    add_cross_mapping(
        requirement_id,
        [*audit_core, *local],
        "global_audit_history_redaction_plus_local_audited_command_happy_paths",
        confidence,
        (
            "No local API operation in api_catalog declares an audit effect; the module is "
            "read/orchestration-only and is covered by global audit/history/redaction ACs."
            if not local and module != "MOD-021"
            else "Local audited command evidence is combined with global audit/history/redaction ACs."
        ),
    )


all_ac_ids = set(ac_by_id)
all_fr_ids = set(fr_trace)
all_br_ids = set(br_by_id)

if len(br_to_fr) != 113 or set(br_map) != all_br_ids:
    raise RuntimeError("BR map is incomplete")
if len(data_owner_evidence) != 354:
    raise RuntimeError("DATA-owned AC map is incomplete")
if len(cross_cutting_to_ac) != 87:
    raise RuntimeError("Cross-cutting requirement map is incomplete")

unknown_fr_links = sorted(
    {
        fr_id
        for item in br_to_fr
        for fr_id in item["fr_ids"]
        if fr_id not in all_fr_ids
    }
    | {
        fr_id
        for item in data_owner_evidence
        for fr_id in item["fr_ids"]
        if fr_id not in all_fr_ids
    }
)
unknown_ac_links = sorted(
    {
        ac_id
        for item in cross_cutting_to_ac
        for ac_id in item["ac_ids"]
        if ac_id not in all_ac_ids
    }
)
empty_cross = [
    item["requirement_id"]
    for item in cross_cutting_to_ac
    if not item["ac_ids"]
]
if unknown_fr_links or unknown_ac_links or empty_cross:
    raise RuntimeError(
        f"Integrity errors: FR={unknown_fr_links}, AC={unknown_ac_links}, empty={empty_cross}"
    )

confidence_counts = {
    "br_to_fr": dict(Counter(item["confidence"] for item in br_to_fr)),
    "data_owned_ac_to_fr": dict(
        Counter(item["confidence"] for item in data_owner_evidence)
    ),
    "cross_cutting_to_ac": dict(
        Counter(item["confidence"] for item in cross_cutting_to_ac)
    ),
}

questionable_cases = [
    {
        "scope": "BR global/cross-module semantics",
        "ids": sorted_ids(MEDIUM_BR),
        "reason": "A single Related FR cell cannot encode universal applicability; the map retains the smallest defensible implementing FR set.",
        "required_review": "Product owner confirms whether additional FRs should be added for exhaustive applicability.",
    },
    {
        "scope": "DATA schema used by multiple operations",
        "count": len(multi_operation_data),
        "reason": "The same exact request/response schema is reused by multiple operations; all valid operation-to-FR branches are retained.",
        "cases": multi_operation_data,
        "required_review": "None for referential validity; test owner may narrow an AC if its intended screen scope is smaller.",
    },
    {
        "scope": "PERM-002",
        "ids": ["PERM-002"],
        "reason": "MOD-002 operations use Anonymous/Authenticated policies and have no generated Permission denied AC.",
        "required_review": "Confirm that session interruption plus server-side route/deep-link recheck ACs are accepted as verification.",
    },
    {
        "scope": "AUDIT rows without local declared audit effect",
        "ids": [
            item["requirement_id"]
            for item in cross_cutting_to_ac
            if item["category"] == "AUDIT" and item["confidence"] == "Medium"
        ],
        "reason": "Stage 2.3.1 api_catalog declares no local audit effect for these read/orchestration modules.",
        "required_review": "Architecture owner confirms global audit/history/redaction ACs are sufficient or adds an explicit local audited command AC.",
    },
]

payload = {
    "metadata": {
        "artifact": "Stage 4.3 semantic remediation maps",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "scope": "BR-to-FR, DATA-owned AC-to-FR, and blank cross-cutting requirement-to-AC relations",
        "source_precedence": [
            str(STAGE23),
            str(TRACE_PATH),
            str(MODULE_PRD_PATH),
            str(AC_PATH),
            str(BR_PATH),
        ],
        "methodological_guardrail": "No FR is assigned without an explicit semantic rule match or an exact schema/field -> API -> operationId -> FR evidence chain.",
    },
    "counts": {
        "br_to_fr": len(br_to_fr),
        "data_owned_ac_to_fr": len(data_owner_evidence),
        "cross_cutting_to_ac": len(cross_cutting_to_ac),
        "stage_2_3_1_api_operations": len(api_rows),
        "operation_to_fr_bijection": len(fr_by_operation),
        "unknown_fr_links": len(unknown_fr_links),
        "unknown_ac_links": len(unknown_ac_links),
        "empty_cross_cutting_ac_links": len(empty_cross),
    },
    "confidence_counts": confidence_counts,
    "data_mapping_strategy_counts": dict(strategy_counts),
    "br_to_fr": br_to_fr,
    "data_owned_ac_to_fr": data_owner_evidence,
    "cross_cutting_to_ac": cross_cutting_to_ac,
    "questionable_cases": questionable_cases,
    "validation": {
        "br_ids_complete": len(br_to_fr) == 113 and set(br_map) == all_br_ids,
        "data_owned_ac_ids_complete": len(data_owner_evidence) == 354,
        "cross_cutting_ids_complete": len(cross_cutting_to_ac) == 87,
        "all_fr_links_resolve": not unknown_fr_links,
        "all_ac_links_resolve": not unknown_ac_links,
        "all_cross_cutting_rows_have_ac": not empty_cross,
        "api_operation_to_fr_is_bijective": len(fr_by_operation) == len(api_rows),
    },
}

OUTPUT_JSON.write_text(
    json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
    encoding="utf-8",
)

medium_audit_ids = questionable_cases[-1]["ids"]
report = f"""# Stage 4.3 Semantic Mapping Report

## Overall assessment

**Ready for catalog remediation with explicit caveats.** All three required maps are complete and referentially valid. No random or module-only FR assignment was used.

## Dataset and grain

| Map | Grain | Rows | Result |
| --- | --- | ---: | --- |
| BR → FR | one business rule | {len(br_to_fr)} | PASS |
| DATA-owned AC → FR | one DATA-owned acceptance criterion | {len(data_owner_evidence)} | PASS |
| Cross-cutting requirement → AC | one blank DATA/PERM/ERR/SYNC/AUDIT trace row | {len(cross_cutting_to_ac)} | PASS |
| Stage 2.3.1 API → FR | one operationId | {len(fr_by_operation)}/{len(api_rows)} | bijective PASS |

## Methodology

1. BR relations were mapped to existing FRs by rule semantics, module PRD wording, API behavior, and the current trace row. Existing normative relations in BR-070 and BR-098…113 were preserved.
2. DATA-owned ACs were mapped by the chain `schema.field → api_catalog request/response → method/path/operationId → FR trace`. Explicit operationId or exact OpenAPI path in the AC Source takes precedence; exact schema use is the fallback.
3. The 87 blank cross-cutting rows were linked only to existing ACs with matching category and module semantics:
   - DATA: response/validation behavior of the affected read-only or archive module;
   - PERM: module permission-denied ACs plus global server-enforcement rules;
   - ERR: existing negative ACs for validation, permission, session, conflict, outage, and idempotency;
   - SYNC: core sync endpoint ACs plus module-specific outage/conflict ACs;
   - AUDIT: global audit/history/redaction ACs plus local happy paths whose Stage 2.3.1 API effects explicitly declare audit.

## Validation results

- BR IDs mapped: **{len(br_to_fr)}/113**.
- DATA-owned AC IDs mapped: **{len(data_owner_evidence)}/354**.
- Blank cross-cutting requirements mapped: **{len(cross_cutting_to_ac)}/87**.
- Unknown FR links: **{len(unknown_fr_links)}**.
- Unknown AC links: **{len(unknown_ac_links)}**.
- Cross-cutting mappings with empty AC: **{len(empty_cross)}**.
- Stage 2.3.1 operations with zero or multiple trace FRs: **0**.
- DATA mapping strategies: `{json.dumps(dict(strategy_counts), ensure_ascii=False)}`.
- Confidence BR: `{json.dumps(confidence_counts['br_to_fr'], ensure_ascii=False)}`.
- Confidence DATA: `{json.dumps(confidence_counts['data_owned_ac_to_fr'], ensure_ascii=False)}`.
- Confidence cross-cutting: `{json.dumps(confidence_counts['cross_cutting_to_ac'], ensure_ascii=False)}`.

## Caveats and cases requiring owner confirmation

1. **Global and cross-module BRs ({len(MEDIUM_BR)} Medium-confidence rows).** Their universal applicability cannot be exhaustively represented by one CSV cell without linking nearly every FR. The JSON records the smallest semantically sufficient implementing FR set and marks these rows Medium.
2. **Shared DATA schemas ({len(multi_operation_data)} rows).** When one exact schema is used by several operations, all valid operation-to-FR branches are retained. This is referentially correct; a test owner may narrow a criterion if the intended screen scope is smaller.
3. **PERM-002.** MOD-002 uses Anonymous/Authenticated access policies and has no generated `Permission denied` scenario. The fallback is evidence-based: session interruption plus hidden-control and deep-link non-disclosure ACs.
4. **AUDIT read/orchestration modules ({len(medium_audit_ids)} Medium-confidence rows).** Stage 2.3.1 does not declare a local audit effect for: `{';'.join(medium_audit_ids)}`. They are mapped to global audit/history/redaction verification. Architecture owner confirmation is recommended.
5. **BR-046 and BR-081.** The mapping is semantically supported but partly negative: trigger deduplication spans reminder creation/dismiss/snooze behavior; absence of generic UserAccount trash is evidenced by account deactivation plus generic trash/purge boundaries.

## Reproducibility

The full row-level evidence, method, confidence, exact API operations, and questionable-case inventory are in `semantic_maps.json`. Source artifacts were read from:

- `{STAGE23}`;
- `{TRACE_PATH}`;
- `{MODULE_PRD_PATH}`;
- `{AC_PATH}`;
- `{BR_PATH}`.
"""

OUTPUT_REPORT.write_text(report, encoding="utf-8")

print(
    json.dumps(
        {
            "br_to_fr": len(br_to_fr),
            "data_owned_ac_to_fr": len(data_owner_evidence),
            "cross_cutting_to_ac": len(cross_cutting_to_ac),
            "confidence": confidence_counts,
            "strategies": dict(strategy_counts),
            "multi_operation_data": len(multi_operation_data),
            "medium_audit_ids": medium_audit_ids,
            "json_bytes": OUTPUT_JSON.stat().st_size,
            "report_bytes": OUTPUT_REPORT.stat().st_size,
        },
        ensure_ascii=False,
        indent=2,
    )
)
