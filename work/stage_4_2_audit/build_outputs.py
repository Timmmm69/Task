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
OUT = ROOT / "outputs" / "stage_4_2_audit"
CANDIDATE = WORK / "candidate" / "Organizer_Stage4_PRD_Candidate_4.1.2"
STAGE2 = WORK / "stage_2_3_1" / "stage_2_3"
STAGE3 = WORK / "stage_3_5"
VERSION = "4.2-audit.1"
AUDIT_DATE = "2026-07-26"


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def write_text(path: Path, text: str) -> None:
    path.write_text(text.rstrip() + "\n", encoding="utf-8")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


FINDINGS = [
    {
        "Audit ID": "AUDIT-4.2-001",
        "Severity": "High",
        "Category": "Other",
        "Artifact": "Stage_4_Product_PRD_4.1.2.md; Stage_4_Dependency_Risk_Register_4.1.2.md",
        "Location": "Product PRD §9 line 186 and §14.4 line 229; Risk Register §3 line 63; Open Questions OQ-001/OQ-003",
        "Related IDs": "OQ-001; OQ-003; FR-159; FR-264; FR-270–279; BR-098–112; AC-1790–1824; CMP-001; CMP-002",
        "Source of truth": "Stage 2.3.1 OpenAPI urgency GET/PUT/reset and employee search schemas; Stage 3.5 SCR-153 and SCR-133/134/135; candidate DEC-053–059",
        "Expected": "Current PRD and risk register use one current OQ status. Superseded gap text is explicitly historical.",
        "Actual": "§9 and the current risk section say OQ-001/OQ-003 remain High and block Stage 4.2; §14.4 and Open Questions say Fixed.",
        "Defect": "The package simultaneously opens and closes both product-blocking OQ.",
        "Consequence": "Approvers and implementation teams receive mutually exclusive normative instructions; the audit gate and feature scope are indeterminate.",
        "Recommended fix": "Replace current blocking wording with explicit resolved history and one Fixed status linked to Stage 2.3.1/3.5 evidence.",
        "Verification": "No current occurrence says remain High/block Stage 4.2/contract absent; every current status resolves to Fixed and the resolved-history chain remains.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-002",
        "Severity": "High",
        "Category": "FR",
        "Artifact": "Stage_4_Module_PRDs_4.1.2.md; Stage_4_Requirements_Traceability_4.1.2.csv; Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv",
        "Location": "Appendix P.2 lines 6792–6801; FR-159, FR-160, FR-243, FR-244, FR-260, FR-261, FR-265, FR-266, FR-269 and their mapped AC",
        "Related IDs": "FR-159; FR-160; FR-243; FR-244; FR-260; FR-261; FR-265; FR-266; FR-269; AC-1002–1008; AC-1404; AC-1405; AC-1425; AC-1426; AC-1430; AC-1431; AC-1435",
        "Source of truth": "Stage 2.3.1 OpenAPI 1.2.0-stage2.3; Stage 3.5 delta/flows; candidate Appendix P.2",
        "Expected": "Each updated FR has AC that directly verifies its current formulation.",
        "Actual": "Nine of ten updated FR retain legacy AC mappings. Examples: FR-243 employee group → AC-1404 safe route; FR-261 urgency projection → AC-1426 toast text alternative; FR-269 scale audit event → AC-1435 generic history.",
        "Defect": "Formal FR→AC links exist but do not test the effective normative FR text.",
        "Consequence": "The candidate can pass its AC suite while omitting updated OQ-001/OQ-003 behavior.",
        "Recommended fix": "Consolidate each changed FR into its primary module row and remap/add atomic Gherkin AC for every new outcome.",
        "Verification": "A semantic FR→AC review confirms that every mandatory outcome in each changed FR appears in observable Then/And steps.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-003",
        "Severity": "High",
        "Category": "UX",
        "Artifact": "Stage_4_Module_PRDs_4.1.2.md",
        "Location": "MOD-014 field table line 4446 and embedded AC-070 line 4508; conflicting addendum lines 6814–6817 and 6862–6873",
        "Related IDs": "MOD-014; OQ-003; FR-159; FR-275–278; BR-070; BR-105–112; AC-070; AC-1804–1820; CMP-002",
        "Source of truth": "Stage 2.3.1 OpenAPI query.types includes employee with maxItems=10; SearchSuggestion/EmployeeSearchResult; Stage 3.5 SCR-133/134/135 and FLOW-019",
        "Expected": "The main module table contains employee/maxItems=10 and embedded AC-070 tests only BR-070 deprecation/replacement.",
        "Actual": "MOD-014 still lists nine types without employee and maxItems=9; embedded AC-070 says employee is unsupported and remains an OQ. The later addendum says the opposite.",
        "Defect": "One module contains two incompatible current search contracts.",
        "Consequence": "Desktop and QA may legally implement and accept search without employees; OQ-003 is reopened.",
        "Recommended fix": "Update the main MOD-014 field table and embedded AC-070 to match OpenAPI and the canonical AC catalog; mark old text historical.",
        "Verification": "employee is present, maxItems=10, embedded/catalog AC-070 agree, and no current statement says employee is unsupported.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-004",
        "Severity": "High",
        "Category": "AC",
        "Artifact": "Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv; module sections K",
        "Location": "211 rows with empty Gherkin: 96 BR-oriented and 115 FR-oriented; examples AC-001–069, AC-113, AC-128, AC-130, AC-1403–1435",
        "Related IDs": "96 BR; 115 FR; 211 AC",
        "Source of truth": "Stage 4.2 audit charter Part 7; linked FR/BR; Product PRD §10 test coverage gate",
        "Expected": "Every AC contains Given, When and an observable, unambiguous Then.",
        "Actual": "211/1824 AC contain only a short Scenario title and a blank Gherkin field.",
        "Defect": "The criteria repeat a rule or happy-path label without executable preconditions, action and result.",
        "Consequence": "QA cannot derive deterministic tests; incompatible implementations can pass the same criterion.",
        "Recommended fix": "Add atomic Gherkin for all 211, including role/capability, state/error, exact response/UI outcome and boundaries where applicable.",
        "Verification": "1824/1824 Gherkin cells are non-empty and contain Given/When/Then; manual spot-check confirms observable outcomes.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-005",
        "Severity": "Medium",
        "Category": "Traceability",
        "Artifact": "Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv",
        "Location": "466 AC without a direct FR reference: 112 BR-only and 354 DATA-only",
        "Related IDs": "AC-001–097 with exceptions; AC-1436–1789; BR-*; DATA-001–021",
        "Source of truth": "Stage 4.2 audit charter Part 7 requires each AC to link to an existing FR; catalog column is named FR/BR",
        "Expected": "Every AC resolves to an effective FR, directly or through an explicit normalized relation.",
        "Actual": "466 AC have no FR token. DATA-only rows also violate the declared FR/BR column domain.",
        "Defect": "The verification graph cannot mechanically demonstrate FR coverage for these AC.",
        "Consequence": "AC counts overstate effective FR verification and make impact analysis unreliable.",
        "Recommended fix": "Add direct FR links or a normalized AC↔requirement↔FR relation with validated transitive resolution.",
        "Verification": "AC without resolvable FR=0; all intermediate requirement links exist and resolve deterministically.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-006",
        "Severity": "Medium",
        "Category": "Traceability",
        "Artifact": "Stage_4_Requirements_Traceability_4.1.2.csv",
        "Location": "87 rows with empty AC: DATA-002/003/016; PERM-001–021; ERR-001–021; SYNC-001–021; AUDIT-001–021",
        "Related IDs": "87 cross-cutting requirement IDs",
        "Source of truth": "OpenAPI; permissions.csv; errors.csv; Stage 1 server-authoritative/sync/audit rules; Stage 3.5 state matrix",
        "Expected": "Every row presented as a requirement has explicit verification criteria.",
        "Actual": "87 requirements have no AC link; related prose elsewhere does not provide requirement-level traceability.",
        "Defect": "Cross-cutting requirements are orphaned from the verification ledger.",
        "Consequence": "Permission, error, sync and audit regressions can escape module DoD.",
        "Recommended fix": "Link each row to concrete AC or fold it into an FR/BR/NFR with explicit verification.",
        "Verification": "Requirement rows with blank AC=0 and every link resolves to an AC that tests the stated behavior.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-007",
        "Severity": "Medium",
        "Category": "BR",
        "Artifact": "Stage_4_Business_Rules_Catalog_4.1.2.csv; module sections E",
        "Location": "96/113 BR have an empty Related FR field",
        "Related IDs": "BR-001–097 except the explicitly related/deprecated subset",
        "Source of truth": "Concept and current contract/UX sources for each module; Stage 4.2 audit charter Part 6",
        "Expected": "Each BR identifies the FR scope to which it applies, including exceptions and priority.",
        "Actual": "Module and Verification are present, but Related FR is blank for 96 BR.",
        "Defect": "The BR↔FR applicability graph is missing.",
        "Consequence": "A changed FR can silently violate a rule and one BR-level AC cannot prove application to all affected functions.",
        "Recommended fix": "Populate a many-to-many BR↔FR mapping; formalize scope/exceptions for global rules.",
        "Verification": "No unexplained empty Related FR; all links exist and rule priority is unambiguous.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-008",
        "Severity": "Medium",
        "Category": "Traceability",
        "Artifact": "Stage_4_Requirements_Traceability_4.1.2.csv; Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv",
        "Location": "260 trace rows and 1305 AC source cells reference Stage_3_Field_Traceability.csv",
        "Related IDs": "1 missing target; 1565 reference occurrences",
        "Source of truth": "Actual Stage 3.5 file Stage_3_Field_Traceability_Final_3.5.csv and Stage 3.5 manifest",
        "Expected": "Every source reference resolves to the exact canonical local file or to a declared manifest alias.",
        "Actual": "The referenced filename does not exist and no alias is declared.",
        "Defect": "Mass broken reference contradicts lost references=0.",
        "Consequence": "Automated evidence resolution and manual review cannot open the claimed source.",
        "Recommended fix": "Replace the old filename with the exact 3.5 filename or define and validate a formal alias.",
        "Verification": "Old-name occurrences=0; link checker resolves every source path.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-009",
        "Severity": "Medium",
        "Category": "Traceability",
        "Artifact": "Stage 3.5 User Flows; candidate Decision Log, Traceability CSV and validation",
        "Location": "Stage 3.5 User Flows has FLOW-035 at lines 537 and 879 and no FLOW-038; candidate has 17 trace rows with FLOW-038",
        "Related IDs": "FLOW-035; FLOW-038; FR-264; FR-266; FR-269–274; FR-279; CMP-001; AC-1824",
        "Source of truth": "Stage 3.5 User Flows; audit rule forbidding PRD flow without an addressable UX flow",
        "Expected": "FLOW-035 resolves only to project completion; FLOW-038 has an addressable downstream definition for urgency management.",
        "Actual": "DEC-060 explains the alias, but the normative UX package still has two FLOW-035 definitions and no FLOW-038 definition.",
        "Defect": "The semantic correction is not a resolvable cross-artifact trace target.",
        "Consequence": "Automated trace treats FLOW-038 as unknown and designers must infer which FLOW-035 section is intended.",
        "Recommended fix": "Add a downstream errata/alias artifact with a full FLOW-038 definition, without changing the Stage 3.5 source ZIP.",
        "Verification": "Unique flow registry; all FLOW-035 references are project-only and FLOW-038 references urgency-only.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-010",
        "Severity": "Medium",
        "Category": "Traceability",
        "Artifact": "Requirements, BR, AC and NFR catalogs; Readiness artifact",
        "Location": "Active sources: 20 FR, 81 BR trace rows, 81 BR catalog rows, 108 AC rows use Stage 3.4; NFR-012 uses Stage 2.2",
        "Related IDs": "FR-242…269 subset; BR-016…097 subset; related AC; NFR-012",
        "Source of truth": "Candidate Product PRD line 204 and CANONICAL_BASELINE.md: Stage 2.3.1/3.5 current; 2.2/3.4 historical only",
        "Expected": "Active source fields cite current baselines; historical versions are explicitly qualified and secondary.",
        "Actual": "Hundreds of active rows cite 3.4 or 2.2 as their operative source without historical qualification.",
        "Defect": "The declared source hierarchy is not consistently applied.",
        "Consequence": "Regeneration or design review can use stale 1040-row UX/241-operation contract semantics.",
        "Recommended fix": "Revalidate and relink active rows to 3.5/2.3.1; retain old versions only as marked provenance.",
        "Verification": "No active source cell uses 2.2/3.4 without explicit historical/superseded qualification and current replacement.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-011",
        "Severity": "Medium",
        "Category": "NFR",
        "Artifact": "Stage_4_NFR_Catalog_4.1.2.csv; Stage_4_Open_Questions_4.1.2.md",
        "Location": "NFR-024 and OQ-008; measurement gaps in NFR-001/003/006/007/015",
        "Related IDs": "NFR-001; NFR-003; NFR-006; NFR-007; NFR-015; NFR-024; OQ-008",
        "Source of truth": "Architecture §0.5; candidate Open Questions OQ-008; Stage 4.2 NFR measurement gate",
        "Expected": "Every NFR has an objective pass threshold or is explicitly provisional/unverified.",
        "Actual": "NFR-024 says availability/RPO/RTO must be measured and confirmed, while OQ-008 remains open; several other targets use undefined terms such as approved/stable/critical.",
        "Defect": "The catalog contains at least one explicit provisional/unverified NFR despite candidate claims of zero.",
        "Consequence": "100% readiness and a final NFR pass cannot be reproduced.",
        "Recommended fix": "Mark NFR-024 provisional until OQ-008 closes and define objective policies/thresholds for the listed NFR.",
        "Verification": "Every NFR has reproducible pass/fail criteria; provisional/unverified ledger accurately reports outstanding decisions.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-012",
        "Severity": "Medium",
        "Category": "Other",
        "Artifact": "Stage_4_Dependency_Risk_Register_4.1.2.md; module risk sections",
        "Location": "RISK-001–025",
        "Related IDs": "DEP-001–024; RISK-001–025; OQ-001; OQ-003",
        "Source of truth": "Stage 4.2 audit charter Part 19",
        "Expected": "Each risk has probability, impact, owner, trigger, mitigation, verification and status.",
        "Actual": "All 25 lack probability, owner and trigger; RISK-022–025 also lack a separate impact field; older risks repeat generic mitigation.",
        "Defect": "The file is a topic list, not an operable risk register.",
        "Consequence": "Material privacy, privilege, data-loss and concurrency risks cannot be owned, monitored or closed.",
        "Recommended fix": "Extend the schema and populate probability, impact, owner role, trigger, preventive/contingency actions, verification and status.",
        "Verification": "Risk lint confirms all required fields are non-empty and closure evidence is traceable.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-013",
        "Severity": "Medium",
        "Category": "UX",
        "Artifact": "NFR catalog; AC catalog; Module PRDs",
        "Location": "NFR-002–005; AC-1805/1807/1815; target CMP accessibility sections",
        "Related IDs": "CMP-001; CMP-002; SCR-133/134/135/153; FLOW-019; NFR-002–005",
        "Source of truth": "Stage 3.5 UX Architecture active-descendant/Up/Down/Enter/Esc semantics and adaptive desktop layout rules",
        "Expected": "Atomic AC cover exact keyboard/focus behavior and adaptive window resizing for CMP-001/CMP-002.",
        "Actual": "General NFR exist, but no atomic AC covers active descendant, Up/Down, normal Esc focus return, CMP-001 tab order, or below-1100 logical-pixel adaptation.",
        "Defect": "Published UX interactions are not fully transferred into the PRD verification matrix.",
        "Consequence": "Desktop implementations can diverge and remain inaccessible or clipped while satisfying broad NFR wording.",
        "Recommended fix": "Add exact keyboard/UIA/focus and resize AC for both components.",
        "Verification": "Keyboard-only and resize matrix passes Tab/Shift+Tab/arrows/Enter/Esc, focus return, minimum window and 200% scaling.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-014",
        "Severity": "Medium",
        "Category": "API",
        "Artifact": "Stage_4_Product_PRD_4.1.2.md; Stage_4_0_PRD_Readiness_4.1.2.md",
        "Location": "Product PRD line 191; Readiness lines 107 and 127",
        "Related IDs": "OpenAPI operation inventory; product DoD",
        "Source of truth": "Stage 2.3.1 openapi.yaml: 244 unique operationId values",
        "Expected": "Current DoD/readiness uses 244/244.",
        "Actual": "Current-looking sections retain 241/241 or all 241 while later sections say 244.",
        "Defect": "The release gate has incompatible API totals.",
        "Consequence": "A three-operation regression can still satisfy the stale DoD.",
        "Recommended fix": "Update or clearly supersede all active 241-operation gates.",
        "Verification": "No current count uses 241; independently parsed operationId count and coverage both equal 244.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-015",
        "Severity": "Low",
        "Category": "AC",
        "Artifact": "Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv",
        "Location": "AC-1486, AC-1487, AC-1501, AC-1579, AC-1709, AC-1710, AC-1715, AC-1716, AC-1767",
        "Related IDs": "9 AC",
        "Source of truth": "Stage 4.2 audit charter Part 7",
        "Expected": "Expected results use measurable wording.",
        "Actual": "Nine AC use the undefined word корректно.",
        "Defect": "A local test oracle is ambiguous.",
        "Consequence": "Reviewers may accept different results.",
        "Recommended fix": "Replace each occurrence with an exact state/value/ordering/error outcome.",
        "Verification": "Undefined-term scan=0 and each affected Then is objectively assertable.",
        "Confidence": "High",
        "Status": "Open",
    },
    {
        "Audit ID": "AUDIT-4.2-016",
        "Severity": "Low",
        "Category": "Security",
        "Artifact": "Stage_4_Analytics_Audit_Requirements_4.1.2.md; Stage_4_Open_Questions_4.1.2.md",
        "Location": "Analytics §5 line 92; OQ-010",
        "Related IDs": "OQ-010; AN-001–052; BR-113; AC-1823",
        "Source of truth": "Candidate privacy/minimization requirements and Product+Security ownership of OQ-010",
        "Expected": "Production retention, access, rotation/deletion and storage boundary are approved.",
        "Actual": "Retention remains open and temporary structured-log storage is allowed, although payload minimization is well specified.",
        "Defect": "Acknowledged governance gap.",
        "Consequence": "Operational metadata may be retained longer or more broadly than necessary.",
        "Recommended fix": "Close OQ-010 with an explicit storage/access/retention/rotation/deletion policy before production.",
        "Verification": "Policy/config tests confirm retention and access while event allowlists and PII bans remain.",
        "Confidence": "High",
        "Status": "Open",
    },
]


def finding_table(finding: dict[str, str]) -> str:
    rows = ["| Поле | Содержание |", "|---|---|"]
    for key in [
        "Audit ID",
        "Severity",
        "Category",
        "Artifact",
        "Location",
        "Related IDs",
        "Source of truth",
        "Expected",
        "Actual",
        "Defect",
        "Consequence",
        "Recommended fix",
        "Verification",
        "Confidence",
        "Status",
    ]:
        value = str(finding[key]).replace("|", "\\|").replace("\n", " ")
        rows.append(f"| {key} | {value} |")
    return "\n".join(rows)


def severity_counts() -> dict[str, int]:
    counter = Counter(f["Severity"] for f in FINDINGS)
    return {key: counter.get(key, 0) for key in ["Critical", "High", "Medium", "Low", "Observation"]}


def module_counts(trace: list[dict[str, str]], br: list[dict[str, str]], ac: list[dict[str, str]]) -> str:
    rows = [
        "| Module | FR | BR | AC | AC без Gherkin |",
        "|---|---:|---:|---:|---:|",
    ]
    for number in range(1, 22):
        module = f"MOD-{number:03d}"
        rows.append(
            f"| {module} | "
            f"{sum(r['Requirement'].startswith('FR-') and r['Module'] == module for r in trace)} | "
            f"{sum(r['Module'] == module for r in br)} | "
            f"{sum(r['Module'] == module for r in ac)} | "
            f"{sum(r['Module'] == module and not r.get('Gherkin', '').strip() for r in ac)} |"
        )
    rows.append(
        f"| ALL | — | {sum(r['Module'] == 'ALL' for r in br)} | "
        f"{sum(r['Module'] == 'ALL' for r in ac)} | "
        f"{sum(r['Module'] == 'ALL' and not r.get('Gherkin', '').strip() for r in ac)} |"
    )
    return "\n".join(rows)


def build_payload() -> dict[str, object]:
    trace = read_csv(CANDIDATE / "Stage_4_Requirements_Traceability_4.1.2.csv")
    ac = read_csv(CANDIDATE / "Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv")
    br = read_csv(CANDIDATE / "Stage_4_Business_Rules_Catalog_4.1.2.csv")
    nfr = read_csv(CANDIDATE / "Stage_4_NFR_Catalog_4.1.2.csv")
    permissions = read_csv(STAGE2 / "catalogs" / "permissions.csv")
    errors = read_csv(STAGE2 / "catalogs" / "errors.csv")
    api_catalog = read_csv(STAGE2 / "catalogs" / "api_catalog.csv")
    metrics = json.loads((WORK / "audit_metrics.json").read_text(encoding="utf-8"))

    fr_set = {
        row["Requirement"]
        for row in trace
        if re.fullmatch(r"FR-\d{3}", row["Requirement"])
    }
    br_set = {row["BR ID"] for row in br}
    extended_set = {row["Requirement"] for row in trace}
    ac_set = {row["AC ID"] for row in ac}
    permission_set = {row["code"] for row in permissions}
    permission_or_access_set = permission_set | {"Anonymous.SessionRefresh"}
    error_set = {row["code"] for row in errors}

    ac_rows: list[dict[str, str]] = []
    for row in ac:
        parents = re.findall(r"\b(?:FR|BR|DATA|PERM|ERR|SYNC|AUDIT)-\d{3}\b", row["FR/BR"])
        direct_fr = re.findall(r"\bFR-\d{3}\b", row["FR/BR"])
        vague = [
            term
            for term in [
                "корректно",
                "удобно",
                "быстро",
                "при необходимости",
                "соответствующим образом",
                "стандартно",
            ]
            if term in (row["Scenario"] + "\n" + row["Gherkin"]).lower()
        ]
        gherkin_lower = row["Gherkin"].lower()
        ac_rows.append(
            {
                "Entity ID": row["AC ID"],
                "Type": "AC",
                "Module": row["Module"],
                "Parent IDs": ";".join(parents),
                "Direct FR": ";".join(direct_fr),
                "Parent exists": str(all(parent in extended_set or parent in br_set for parent in parents)),
                "Source present": str(bool(row["Source"].strip())),
                "Given": str("given" in gherkin_lower),
                "When": str("when" in gherkin_lower),
                "Then": str("then" in gherkin_lower),
                "Vague terms": ";".join(vague),
                "Status": "Finding"
                if not direct_fr or not all(token in gherkin_lower for token in ["given", "when", "then"]) or vague
                else "Pass",
                "Notes": "Direct FR link absent" if not direct_fr else "",
            }
        )

    fr_br_rows: list[dict[str, str]] = []
    for row in trace:
        requirement = row["Requirement"]
        if not requirement.startswith("FR-"):
            continue
        linked_ac = re.findall(r"\bAC-\d{3,4}\b", row["AC"])
        fr_br_rows.append(
            {
                "Entity ID": requirement,
                "Type": "FR",
                "Module": row["Module"],
                "Parent IDs": "",
                "Direct FR": requirement,
                "Parent exists": "True",
                "Source present": str(bool(row["Source"].strip())),
                "Given": "N/A",
                "When": "N/A",
                "Then": "N/A",
                "Vague terms": "",
                "Status": "Pass" if linked_ac and all(item in ac_set for item in linked_ac) else "Finding",
                "Notes": f"Linked AC: {len(linked_ac)}",
            }
        )
    for row in br:
        linked_fr = re.findall(r"\bFR-\d{3}\b", row["Related FR"])
        fr_br_rows.append(
            {
                "Entity ID": row["BR ID"],
                "Type": "BR",
                "Module": row["Module"],
                "Parent IDs": ";".join(linked_fr),
                "Direct FR": ";".join(linked_fr),
                "Parent exists": str(all(item in fr_set for item in linked_fr)),
                "Source present": str(bool(row["Source"].strip())),
                "Given": "N/A",
                "When": "N/A",
                "Then": "N/A",
                "Vague terms": "",
                "Status": "Pass" if linked_fr else "Finding",
                "Notes": "Related FR absent" if not linked_fr else "",
            }
        )
    fr_br_ac_rows = fr_br_rows + ac_rows

    operation_entries = []
    current_path = ""
    current_method = ""
    for line_no, line in enumerate(
        (STAGE2 / "openapi" / "openapi.yaml").read_text(encoding="utf-8").splitlines(),
        start=1,
    ):
        match_path = re.match(r"^  (/[^\s]+):\s*$", line)
        if match_path:
            current_path = match_path.group(1)
            continue
        match_method = re.match(r"^    (get|post|put|patch|delete):\s*$", line)
        if match_method:
            current_method = match_method.group(1).upper()
            continue
        match_operation = re.match(r"^\s+operationId:\s*(\S+)", line)
        if match_operation:
            operation_entries.append(
                {
                    "Operation ID": match_operation.group(1),
                    "Method": current_method,
                    "Path": current_path,
                    "OpenAPI line": str(line_no),
                }
            )
    catalog_by_key = {
        (row["method"].upper(), row["path"]): row for row in api_catalog
    }
    api_rows: list[dict[str, str]] = []
    for operation in operation_entries:
        mapping = [
            row
            for row in trace
            if row["Requirement"].startswith("FR-")
            and operation["Operation ID"] in row["API"]
        ]
        fr_ids = [row["Requirement"] for row in mapping]
        ac_ids = sorted(
            {
                ac_id
                for row in mapping
                for ac_id in re.findall(r"\bAC-\d{3,4}\b", row["AC"])
            }
        )
        ux = sorted(
            {
                ux_id
                for row in mapping
                for ux_id in re.findall(
                    r"\b(?:SCR|FLOW|STATE|CMP)-\d{3}\b",
                    " ".join([row["SCR"], row["FLOW"], row["STATE"]]),
                )
            }
        )
        catalog = catalog_by_key.get((operation["Method"], operation["Path"]), {})
        permission = catalog.get("permission", "")
        permission_codes = re.findall(r"\b[A-Z][A-Za-z0-9]*\.[A-Z][A-Za-z0-9]*\b", permission)
        referenced_errors = {
            code
            for row in mapping
            for code in re.findall(r"\b[A-Z][A-Z0-9_]{2,}\b", row["Error"])
            if "_" in code
        }
        status = (
            "Trace-mapped"
            if fr_ids
            and ac_ids
            and all(code in permission_or_access_set for code in permission_codes)
            and all(code in error_set for code in referenced_errors)
            else "Finding"
        )
        api_rows.append(
            {
                **operation,
                "Request": catalog.get("request", ""),
                "Response": catalog.get("response", ""),
                "Permission": permission,
                "HTTP codes": catalog.get("codes", ""),
                "Idempotency": catalog.get("idempotency", ""),
                "Locking": catalog.get("locking", ""),
                "FR IDs": ";".join(fr_ids),
                "AC IDs": ";".join(ac_ids),
                "UX IDs": ";".join(ux),
                "Permission refs valid": str(all(code in permission_or_access_set for code in permission_codes)),
                "Stable error refs valid": str(all(code in error_set for code in referenced_errors)),
                "Coverage status": status,
                "Evidence": "Operation-level: OpenAPI + api_catalog.csv + Stage_4_Requirements_Traceability_4.1.2.csv",
            }
        )

    trace_rows: list[dict[str, str]] = []
    for row in trace:
        requirement = row["Requirement"]
        ac_refs = re.findall(r"\bAC-\d{3,4}\b", row["AC"])
        permission_refs = re.findall(
            r"\b[A-Z][A-Za-z0-9]*\.[A-Z][A-Za-z0-9]*\b", row["Permission"]
        )
        error_refs = {
            code
            for code in re.findall(r"\b[A-Z][A-Z0-9_]{2,}\b", row["Error"])
            if "_" in code
        }
        notes = []
        if "Stage_3_Field_Traceability.csv" in " ".join(row.values()):
            notes.append("Broken source filename")
        if "Stage 3.4" in row["Source"]:
            notes.append("Historical baseline used as active source")
        if "FLOW-038" in row["FLOW"]:
            notes.append("FLOW-038 downstream alias lacks addressable Stage 3.5 definition")
        if not ac_refs:
            notes.append("No AC")
        api_ref_valid = not re.findall(
            r"\b(?:GET|POST|PUT|PATCH|DELETE)_[A-Za-z0-9_]+\b", row["API"]
        ) or all(
            operation_id
            in {operation["Operation ID"] for operation in operation_entries}
            for operation_id in re.findall(
                r"\b(?:GET|POST|PUT|PATCH|DELETE)_[A-Za-z0-9_]+\b", row["API"]
            )
        )
        trace_rows.append(
            {
                "Requirement": requirement,
                "Type": requirement.split("-")[0],
                "Module": row["Module"],
                "Source": row["Source"],
                "Source present": str(bool(row["Source"].strip())),
                "API refs valid": str(api_ref_valid),
                "Permission refs valid": str(
                    all(code in permission_or_access_set for code in permission_refs)
                ),
                "Stable error refs valid": str(all(code in error_set for code in error_refs)),
                "AC refs valid": str(bool(ac_refs) and all(ac_id in ac_set for ac_id in ac_refs)),
                "SCR": row["SCR"],
                "FLOW": row["FLOW"],
                "STATE": row["STATE"],
                "Status": "Finding" if notes else "Pass",
                "Notes": "; ".join(notes),
            }
        )

    return {
        "findings": FINDINGS,
        "traceability_rows": trace_rows,
        "api_rows": api_rows,
        "fr_br_ac_rows": fr_br_ac_rows,
        "counts": {
            "modules": 21,
            "fr": len(fr_set),
            "br": len(br),
            "ac": len(ac),
            "nfr": len(nfr),
            "api_operations": len(operation_entries),
            "api_covered": sum(row["Coverage status"] == "Trace-mapped" for row in api_rows),
            "fr_without_ac": sum(
                row["Requirement"].startswith("FR-") and not row["AC"].strip() for row in trace
            ),
            "ac_without_direct_fr": sum(not row["Direct FR"] for row in ac_rows),
            "orphaned_requirements": sum(
                not row["AC"].strip() and row["Requirement"].split("-")[0] in {"DATA", "PERM", "ERR", "SYNC", "AUDIT"}
                for row in trace
            ),
            "unknown_permissions": len(
                set(metrics["references"]["unknown_permissions"])
                - {"Anonymous.SessionRefresh"}
            ),
            "unknown_errors": len(metrics["references"]["unknown_stable_errors"]),
            "duplicate_ids": 0,
            "broken_reference_targets": 1,
            "broken_reference_occurrences": 1565,
            "unverified": 1,
            "provisional": 1,
        },
        "severity": severity_counts(),
        "module_table": module_counts(trace, br, ac),
    }


def build_markdown(payload: dict[str, object]) -> None:
    counts = payload["counts"]
    severity = payload["severity"]
    assert isinstance(counts, dict)
    assert isinstance(severity, dict)
    high_findings = [f for f in FINDINGS if f["Severity"] == "High"]
    all_findings = "\n\n".join(
        f"### {f['Audit ID']} — {f['Severity']} / {f['Category']}\n\n{finding_table(f)}"
        for f in FINDINGS
    )
    summary_metrics = f"""
| Метрика | Независимый результат |
|---|---:|
| Модули | {counts['modules']} |
| Уникальные FR | {counts['fr']} |
| Уникальные BR | {counts['br']} |
| Уникальные AC | {counts['ac']} |
| Уникальные NFR | {counts['nfr']} |
| API operationId trace coverage | {counts['api_covered']}/{counts['api_operations']} |
| FR без AC | {counts['fr_without_ac']} |
| AC без прямой FR-связи | {counts['ac_without_direct_fr']} |
| Orphaned от verification requirements | {counts['orphaned_requirements']} |
| Unknown permissions / stable errors | {counts['unknown_permissions']} / {counts['unknown_errors']} |
| Duplicate IDs | {counts['duplicate_ids']} |
| Broken source target | {counts['broken_reference_targets']} target / {counts['broken_reference_occurrences']} occurrences |
| Unverified / provisional | {counts['unverified']} / {counts['provisional']} |
""".strip()

    write_text(
        OUT / "Stage_4_2_Executive_Summary.md",
        f"""
# Stage 4.2 — Executive Summary

**Версия:** {VERSION}  
**Дата:** {AUDIT_DATE}  
**Кандидат:** Organizer Stage 4 PRD Candidate 4.1.2  
**Итоговый вердикт:** **FAIL**

Причина вердикта: Critical={severity['Critical']}, High={severity['High']}. Кандидат нельзя утверждать до Этапа 4.3 и повторной проверки.

## Независимый пересчёт

{summary_metrics}

## Статус OQ

- **OQ-001: Conflicted / не может считаться Fixed.** Contract и UX закрытие существуют, но текущий Product PRD и risk register повторно объявляют OQ High/blocking.
- **OQ-003: Conflicted / не может считаться Fixed.** Помимо статусного противоречия, основная секция MOD-014 всё ещё исключает `employee`, задаёт `maxItems=9` и содержит старый AC-070.

## Findings

| Critical | High | Medium | Low | Observation |
|---:|---:|---:|---:|---:|
| {severity['Critical']} | {severity['High']} | {severity['Medium']} | {severity['Low']} | {severity['Observation']} |

Ключевые High:

""" + "\n".join(f"- **{f['Audit ID']}** — {f['Defect']}" for f in high_findings) + """

## Readiness

- Готовность к визуальному дизайну: **78%**.
- Готовность к разработке: **74%**.
- Этап 4.3: **обязателен**.

Количественные базовые заявления 21/279/113/1824/25 и operationId trace coverage 244/244 подтверждены, но качество и внутренняя согласованность требований не подтверждены. Полная field-by-field сертификация 1 340 DTO constraints и выполнение PostgreSQL migrations в этот показатель не входят.
""",
    )

    write_text(
        OUT / "Stage_4_2_Audit_Report.md",
        f"""
# Stage 4.2 — Independent Comprehensive Audit Report

**Версия:** {VERSION}  
**Дата:** {AUDIT_DATE}  
**Вердикт:** **FAIL**

## 1. Область и метод

Проверены исходные ZIP, candidate manifest, концепция, Stage 1, полный Stage 2.3.1 OpenAPI/catalogs/database evidence, Stage 3.5 UX baseline и все 15 файлов кандидата. Заявленные PASS и totals не принимались без пересчёта. Существенные findings повторно проверены в кандидатском артефакте, связанном разделе и source of truth.

Роли аудита: product, solution/backend/desktop/data architects, UX/accessibility, QA, security/permissions и requirements writing.

## 2. Целостность

- Audit Input SHA-256: `4CC6DF2A7CF54F3E692971BDB2A39322615442748E95AD7104A1564229CD845F` — PASS.
- Candidate SHA-256: `84260071D3917AE00AA617FDBF2E5AB540A719F7D717367B0504E36159845AF9` — PASS.
- CRC, повторное открытие, path traversal, пустые/временные файлы — PASS.
- Audit Input manifest: 23/23 files, size/hash PASS.
- Candidate manifest: 14/14 hashed files, size/hash PASS.
- Stage 2.3.1 и Stage 3.5 normative ZIP: SHA/CRC/reopen PASS.

## 3. Независимые метрики

{summary_metrics}

API coverage здесь означает operation-level подтверждение: каждый из 244 operationId существует в OpenAPI и имеет FR+AC mapping. Это не является сертификацией всех 1 340 DTO field constraints или выполнением PostgreSQL migrations и не отменяет High-дефекты семантической трассировки обновлённых FR.

## 4. Модульная структура

Все 21 module block имеют разделы A–O. Структурная форма полна; содержательная проверка выявила несогласованные updated FR/AC, stale current sources и incomplete cross-cutting verification.

{payload['module_table']}

## 5. Source hierarchy

Текущие baseline корректно названы в §14, но активные catalog rows продолжают использовать Stage 2.2/3.4, старое имя field traceability и stale count 241. Поэтому source hierarchy реализована не полностью.

## 6. FR / BR / AC

- 279 unique FR; формально FR blank AC=0.
- Семантический аудит выявил 9 updated FR с legacy AC.
- 113 BR; 96 без Related FR.
- 1824 AC; 211 без Given/When/Then; 466 без direct FR.
- 87 cross-cutting requirement rows не имеют AC.

## 7. API, permissions, errors, data and security

- OpenAPI: 244 unique operations; 244 operationId mapped to FR and AC.
- Normative DTO field catalog: 1 340 rows; entity catalog: 66 rows. Выполнены set-level и targeted semantic checks, но не заявляется полный PASS всех 1 340 field constraints или исполнения migration SQL.
- Permissions catalog: 91; unknown permission codes in PRD: 0.
- Stable errors catalog: 44; unknown codes: 0.
- Idempotency/ETag/If-Match/server-side filtering references сохранены в operation audit.
- OQ-003 основная MOD-014 field table противоречит фактическому OpenAPI employee enum.
- Отдельных Critical data-loss/privilege-escalation defects не подтверждено.

## 8. UX, accessibility and FLOW

Stage 3.5 field traceability: 1078 rows; 38-row delta воспроизводится как 28 urgency + 10 employee rows; 20 controls воспроизводятся только после semantic normalization. FLOW collision explained by DEC-060, but FLOW-038 lacks an addressable UX definition. Target accessibility behavior в целом сильное, однако atomic keyboard/focus/resize AC неполны.

## 9. Analytics, NFR, dependencies and risks

Analytics/diagnostics/security audit разделены, raw query/PII/path/secrets ограничены. Retention остаётся OQ-010. NFR-024 и OQ-008 доказывают минимум один provisional/unverified item. Risk register не имеет probability/owner/trigger.

## 10. Findings

{all_findings}

## 11. Verdict rule

Есть High findings → **FAIL**. Этап 4.3 обязателен; после исправления требуется повторный независимый audit, а не только внутренняя self-validation.
""",
    )

    write_text(
        OUT / "Stage_4_2_Permissions_Security_Audit.md",
        f"""
# Stage 4.2 — Permissions and Security Audit

**Вердикт области:** FAIL из-за cross-artifact High/Medium, при этом отдельного неизвестного permission/error не найдено.

## Recount

- Permission catalog: **91**.
- Unknown permission codes: **0**.
- Stable errors: **44**.
- Unknown stable errors: **0**.
- API operations with FR/AC mapping: **244/244**.
- DTO field catalog: **1 340** rows; entity catalog: **66** rows.

## Confirmed controls

- Server-side authorization is consistently stated as enforcement boundary; hidden/disabled is presentation only.
- Search filtering/redaction/blocked policy precede pagination in Stage 2.3.1 and the 4.1.2 addendum.
- Settings urgency reads use `Settings.ReadOwn`; writes/reset use `System.Configure`.
- `User.Block` is reused only for blocked-employee visibility; no new permission is invented.
- If-Match/ETag, idempotency, draft preservation, no offline write queue and sensitive audit requirements are present.

## Validation boundary

OperationId set coverage and targeted semantic checks are confirmed. Полное независимое воспроизведение всех 1 340 DTO field constraints и выполнение migration SQL на PostgreSQL не проводились и не объявляются PASS.

## Material gaps

- {FINDINGS[2]['Audit ID']}: stale MOD-014 enum and embedded AC can remove employee search despite server contract.
- {FINDINGS[5]['Audit ID']}: 87 permission/error/sync/audit/data requirements lack AC links.
- {FINDINGS[11]['Audit ID']}: security/data-loss risks lack owner/trigger/probability.
- {FINDINGS[15]['Audit ID']}: analytics retention remains open.

## Conclusion

No evidence of an invented permission or stable error was found. Security implementation is not approval-ready until the normative contradictions and verification gaps are remediated.
""",
    )

    write_text(
        OUT / "Stage_4_2_UX_Accessibility_Audit.md",
        f"""
# Stage 4.2 — UX and Accessibility Audit

**Вердикт области:** FAIL.

## Verified baseline figures

- Stage 3.5 field trace rows: **1078**.
- Added rows: **38** = 28 CMP-001 urgency + 10 CMP-002 employee.
- Contract-dependent controls: **20 after semantic normalization**; literal distinct Control strings are 29.
- SCR-133/134/135/153 and CMP-001/002 exist.

## FLOW-035 / FLOW-038

- Historical project FLOW-035 is preserved in candidate references.
- Urgency references use FLOW-038.
- Stage 3.5 still contains two FLOW-035 definitions and no FLOW-038 target; DEC-060 is not a full addressable UX definition. See AUDIT-4.2-009.

## Accessibility coverage

Covered: keyboard-only policy, visible/deterministic focus, high contrast, non-color urgency/status, screen-reader group/status, focus-first-invalid, conflict/draft preservation, neutral unavailable/redaction.

Not atomic enough: active descendant, Up/Down, normal Esc focus return, CMP-001 tab order, sub-1100 logical-pixel adaptation and minimum-window behavior. See AUDIT-4.2-013.

## Design conclusion

Design readiness is **78%**. Visual exploration may start, but production design handoff is blocked by High findings and unresolved trace/accessibility gaps.
""",
    )

    write_text(
        OUT / "Stage_4_2_NFR_Audit.md",
        f"""
# Stage 4.2 — NFR Audit

## Recount

- Rows: **25**.
- Unique IDs: **25**.
- Duplicate IDs: **0**.

## Assessment

Most NFR have a target and measurement path. Strong areas include online-only writes during outage, concurrency, idempotency, server authorization, file safety, local cache cleanup, stable error mapping and audit append-only behavior.

Material gap: NFR-024 explicitly requires future confirmation of 99.5%/RPO/RTO and OQ-008 remains open. Therefore independent counts are **unverified=1, provisional=1**, not zero. NFR-001/003/006/007/015 also need more objective policy or thresholds.

Current-source cleanup is required for NFR-012 (Stage 2.2) and for inconsistent inline NFR text in the Product PRD.

## Verdict

**Needs remediation.** NFR catalog cannot support a 100% readiness claim until AUDIT-4.2-011 is closed and revalidated.
""",
    )

    write_text(
        OUT / "Stage_4_2_OQ_001_Audit.md",
        """
# Stage 4.2 — OQ-001 Audit: Organizational Urgency Scale

## Independent result

**Status: Conflicted / cannot remain Fixed.**

Substantive contract and UX coverage is present:

- organization-owned GET/PUT/reset;
- four semantic intervals with full 0–100 coverage, ordering/no gaps/no overlap;
- server defaults and reset;
- `Settings.ReadOwn` / `System.Configure`;
- ETag/If-Match, idempotency, validation and conflict recovery;
- audit event, current/future notification presentation and legacy-client behavior;
- keyboard/screen reader/high-contrast/non-color requirements.

Closure fails at the candidate level because Product PRD §9 and Risk Register §3 still call OQ-001 High/blocking and say writable contract is absent. Several updated FR also retain legacy AC mapping. Exact remediation is in AUDIT-4.2-001 and AUDIT-4.2-002.

## Revalidation gate

One current status, consolidated FR rows, semantically matching AC, FLOW-038 addressable target, and no active statement that the contract is absent.
""",
    )

    write_text(
        OUT / "Stage_4_2_OQ_003_Audit.md",
        """
# Stage 4.2 — OQ-003 Audit: Employees in Global Search

## Independent result

**Status: Conflicted / reopened by candidate contradiction.**

Stage 2.3.1 and Stage 3.5 substantively provide:

- distinct `employee` result type and Employees group;
- `EmployeeSearchResult` fields only;
- department/jobTitle/status only when permitted/present; no avatar;
- server filtering/redaction/blocked policy before pagination;
- ranking, mixed search, deep link, cursor stability and no client post-filter;
- separation from contacts, admin users and `userIds`.

However, MOD-014 line 4446 still defines nine types without employee and maxItems=9; embedded AC-070 line 4508 requires employee to be unsupported. This contradicts OpenAPI, the current AC catalog and the addendum. OQ-003 cannot be Fixed until AUDIT-4.2-003 and related FR/AC trace defects are corrected.
""",
    )

    write_text(
        OUT / "Stage_4_2_Design_Readiness.md",
        """
# Stage 4.2 — Design and Development Readiness

## Visual design readiness: 78%

| Area | Weight | Completeness | Weighted |
|---|---:|---:|---:|
| Screens/components/flows | 15 | 73% | 11 |
| Fields/controls/DTO | 15 | 80% | 12 |
| States/error/read-only/conflict | 15 | 93% | 14 |
| Roles/permissions/partial access | 10 | 100% | 10 |
| Validation/recovery | 15 | 93% | 14 |
| Accessibility/adaptive desktop | 15 | 67% | 10 |
| UX writing inventory | 5 | 60% | 3 |
| Source freshness/traceability | 10 | 40% | 4 |
| **Total** | **100** |  | **78%** |

Designers can explore layouts, but cannot produce an approval-ready handoff without inventing choices around conflicting employee search, updated FR, exact keyboard behavior and trace targets.

## Development readiness: 74%

| Discipline | Weight | Completeness | Weighted |
|---|---:|---:|---:|
| Backend/API | 15 | 93% | 14 |
| Desktop | 20 | 70% | 14 |
| Database/data | 10 | 90% | 9 |
| QA/testability | 20 | 70% | 14 |
| Security/privacy | 15 | 87% | 13 |
| DevOps/diagnostics | 10 | 70% | 7 |
| Dependencies/risks | 10 | 30% | 3 |
| **Total** | **100** |  | **74%** |

Backend contract is mature, but the unified implementation baseline is blocked by High findings. Required UX-writing backlog includes read-only reason, interval validation, reset confirmation, compare/reapply/discard, redaction placeholder, partial failure, stale/unavailable result and cursor restart.
""",
    )

    write_text(
        OUT / "Stage_4_2_Remediation_Plan.md",
        """
# Stage 4.2 — Remediation Plan for Stage 4.3

## Priority 0 — approval blockers

1. Resolve AUDIT-4.2-001: one current OQ status and no active obsolete gap text.
2. Resolve AUDIT-4.2-002: consolidate all ten updated FR and repair semantic AC mappings.
3. Resolve AUDIT-4.2-003: correct MOD-014 employee enum/maxItems and embedded AC-070.
4. Resolve AUDIT-4.2-004: add complete Given/When/Then for all 211 AC.

## Priority 1 — traceability and quality

5. Normalize AC→FR, BR→FR and cross-cutting requirement→AC relations.
6. Repair 1565 source occurrences and define an addressable FLOW-038 downstream errata.
7. Replace active Stage 2.2/3.4 sources with 2.3.1/3.5 after content comparison.
8. Remove/supersede stale 241-operation gates.
9. Make risk register operable and NFR thresholds/provisional status explicit.
10. Add atomic accessibility/adaptive-window AC.

## Priority 2 — governance/polish

11. Replace nine vague AC terms.
12. Close analytics retention OQ-010 before production.

## Required Stage 4.3 verification

- All High=0 and Critical=0.
- 244/244 operations independently parsed and mapped.
- 1824/1824 AC have Given/When/Then and direct/transitive FR resolution.
- Updated FR semantic AC coverage passes manual review.
- Old field-trace filename occurrences=0; active 2.2/3.4 refs=0 unless marked historical.
- Unique flow registry resolves FLOW-035/FLOW-038.
- Unknown permission/error=0; duplicate IDs=0.
- Unverified/provisional accurately reflects outstanding decisions.
- Final package manifest, SHA-256, CRC and repeat-open pass.
""",
    )

    write_text(
        OUT / "Stage_4_2_Independent_Validation.md",
        f"""
# Stage 4.2 — Independent Validation

**Version:** {VERSION}  
**Assessment:** Needs revision / FAIL

## Input validation

| Check | Result |
|---|---|
| Audit Input SHA-256 | PASS — 4CC6DF2A7CF54F3E692971BDB2A39322615442748E95AD7104A1564229CD845F |
| Candidate SHA-256 | PASS — 84260071D3917AE00AA617FDBF2E5AB540A719F7D717367B0504E36159845AF9 |
| CRC/read-to-completion/reopen | PASS |
| Unsafe paths / empty files / temp files | 0 / 0 / 0 |
| Audit Input manifest | 23/23 size+hash PASS |
| Candidate manifest | 14/14 size+hash PASS |
| Stage 2.3.1 SHA/CRC/reopen | PASS |
| Stage 3.5 SHA/CRC/reopen | PASS |

## Calculation spot checks

| Claim | Result |
|---|---|
| 21 modules | Verified |
| 279 FR / 113 BR / 1824 AC / 25 NFR | Verified unique IDs |
| 244 OpenAPI operations | Verified by independent operationId parse |
| 244/244 mapped | Verified at operationId/FR/AC level; exhaustive DTO-field and migration execution not certified |
| FR blank AC=0 | Verified |
| Semantic updated-FR coverage | Discrepancy — 9/10 updated FR retain legacy AC |
| AC complete Gherkin | Discrepancy — 211 blank |
| Lost references=0 | Discrepancy — one missing target, 1565 occurrences |
| Unverified/provisional=0 | Discrepancy — minimum 1/1 |
| OQ-001/OQ-003 Fixed | Discrepancy — Conflicted |

## Validation stance

The numeric inventory is mostly accurate, but the candidate's conclusions are not ready to share as an approval baseline. Findings are evidence-backed and prioritized by implementation risk. Final ZIP CRC/reopen and external SHA-256 are validated after deterministic package assembly; hashes are recorded in adjacent `.sha256` files.
""",
    )


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    OUT.mkdir(parents=True, exist_ok=True)
    payload = build_payload()
    (WORK / "audit_payload.json").write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    build_markdown(payload)
    print(
        json.dumps(
            {
                "markdown_files": len(list(OUT.glob("*.md"))),
                "findings": len(FINDINGS),
                "severity": severity_counts(),
                "payload": str(WORK / "audit_payload.json"),
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
