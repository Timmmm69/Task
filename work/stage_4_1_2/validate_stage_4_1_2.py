from __future__ import annotations

import csv
import hashlib
import json
import re
import zipfile
from pathlib import Path


ROOT = Path(r"C:\Users\novik\Таск")
BASE = ROOT / "work" / "stage_4_1_2"
OUT = BASE / "candidate_4_1_2"
S2 = BASE / "input_stage2" / "stage_2_3"
S3 = BASE / "input_stage3"

checks: dict[str, object] = {}
errors: list[str] = []


def check(name: str, condition: bool, detail: object = "") -> None:
    checks[name] = {"pass": bool(condition), "detail": detail}
    if not condition:
        errors.append(f"{name}: {detail}")


def csv_rows(name: str) -> list[dict[str, str]]:
    with (OUT / name).open("r", encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


expected_files = {
    "Stage_4_Product_PRD_4.1.2.md",
    "Stage_4_Module_PRDs_4.1.2.md",
    "Stage_4_Business_Rules_Catalog_4.1.2.csv",
    "Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv",
    "Stage_4_NFR_Catalog_4.1.2.csv",
    "Stage_4_Analytics_Audit_Requirements_4.1.2.md",
    "Stage_4_Requirements_Traceability_4.1.2.csv",
    "Stage_4_Dependency_Risk_Register_4.1.2.md",
    "Stage_4_Decision_Log_4.1.2.md",
    "Stage_4_Open_Questions_4.1.2.md",
    "Stage_4_Candidate_Validation_4.1.2.md",
    "Stage_4_0_PRD_Readiness_4.1.2.md",
    "Stage_4_1_2_Delta_Plan.md",
    "Stage_4_1_2_Update_Report.md",
    "00_MANIFEST.md",
}
actual_files = {p.name for p in OUT.iterdir() if p.is_file()}
check("files_15", actual_files == expected_files, sorted(actual_files ^ expected_files))

module_text = (OUT / "Stage_4_Module_PRDs_4.1.2.md").read_text(encoding="utf-8")
modules = re.findall(r"^# (MOD-\d{3})\.", module_text, flags=re.M)
check("modules_21", len(modules) == 21 and len(set(modules)) == 21, modules)

br = csv_rows("Stage_4_Business_Rules_Catalog_4.1.2.csv")
ac = csv_rows("Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv")
nfr = csv_rows("Stage_4_NFR_Catalog_4.1.2.csv")
trace = csv_rows("Stage_4_Requirements_Traceability_4.1.2.csv")

def id_check(rows: list[dict[str, str]], column: str, prefix: str, expected: int) -> None:
    ids = [r[column] for r in rows]
    expected_ids = {f"{prefix}-{i:03d}" for i in range(1, expected + 1)}
    check(f"{prefix}_count_unique", len(ids) == expected and len(set(ids)) == expected, {"count": len(ids), "unique": len(set(ids))})
    check(f"{prefix}_continuous", set(ids) == expected_ids, sorted(expected_ids ^ set(ids))[:20])

id_check(br, "BR ID", "BR", 113)
id_check(ac, "AC ID", "AC", 1824)
id_check(nfr, "NFR ID", "NFR", 25)

trace_fr = [r for r in trace if re.fullmatch(r"FR-\d{3}", r["Requirement"])]
trace_br = [r for r in trace if re.fullmatch(r"BR-\d{3}", r["Requirement"])]
fr_ids = [r["Requirement"] for r in trace_fr]
check("FR_total_unique", len(fr_ids) == 279 and len(set(fr_ids)) == 279, {"count": len(fr_ids), "unique": len(set(fr_ids))})
check("FR_continuous", set(fr_ids) == {f"FR-{i:03d}" for i in range(1, 280)}, "")
check("BR_trace_complete", {r["BR ID"] for r in br}.issubset({r["Requirement"] for r in trace_br}), "")

ac_ids = {r["AC ID"] for r in ac}
missing_ac_refs = []
for r in trace_fr:
    refs = [v for v in r["AC"].split(";") if v]
    if not refs or any(v not in ac_ids for v in refs):
        missing_ac_refs.append((r["Requirement"], refs))
check("FR_without_AC_0", not missing_ac_refs, missing_ac_refs[:20])

new_ac = [r for r in ac if 1790 <= int(r["AC ID"].split("-")[1]) <= 1824]
check("new_AC_35", len(new_ac) == 35, len(new_ac))
check("new_AC_all_Gherkin", all(r["Gherkin"].startswith("Given ") and "\nWhen " in r["Gherkin"] and "\nThen " in r["Gherkin"] for r in new_ac), [r["AC ID"] for r in new_ac if not r["Gherkin"]])

openapi = (S2 / "openapi" / "openapi.yaml").read_text(encoding="utf-8")
operation_ids = set(re.findall(r"^\s*operationId:\s*([A-Za-z0-9_]+)\s*$", openapi, flags=re.M))
trace_api_text = "\n".join(r["API"] for r in trace)
trace_ops = {operation_id for operation_id in operation_ids if operation_id in trace_api_text}
missing_ops = sorted(operation_ids - trace_ops)
check("openapi_operations_244", len(operation_ids) == 244, len(operation_ids))
check("operations_mapped_244", not missing_ops and len(trace_ops & operation_ids) == 244, missing_ops[:20])

with (S2 / "catalogs" / "permissions.csv").open("r", encoding="utf-8-sig", newline="") as f:
    perm_rows = list(csv.DictReader(f))
perm_fields = list(perm_rows[0].keys())
known_permissions = {r[perm_fields[0]] for r in perm_rows}
new_trace = [r for r in trace if r["Requirement"] in {f"FR-{i:03d}" for i in range(270, 280)}]
permission_tokens = set()
for r in new_trace:
    permission_tokens.update(re.findall(r"\b[A-Z][A-Za-z]+\.[A-Za-z][A-Za-z]+\b", r["Permission"]))
check("new_permissions_known", permission_tokens.issubset(known_permissions), sorted(permission_tokens - known_permissions))

with (S2 / "catalogs" / "errors.csv").open("r", encoding="utf-8-sig", newline="") as f:
    error_rows = list(csv.DictReader(f))
error_fields = list(error_rows[0].keys())
known_errors = {r[error_fields[0]] for r in error_rows}
error_tokens = set()
for r in new_trace:
    error_tokens.update(re.findall(r"\b[A-Z][A-Z_]+\b", r["Error"]))
check("new_stable_errors_known", error_tokens.issubset(known_errors), sorted(error_tokens - known_errors))

field_rows = []
with (S3 / "Stage_3_Field_Traceability_Final_3.5.csv").open("r", encoding="utf-8-sig", newline="") as f:
    field_rows = list(csv.DictReader(f))
check("stage3_field_rows_1078", len(field_rows) == 1078, len(field_rows))
check("stage3_unverified_0", not any("unverified" in " ".join(r.values()).lower() for r in field_rows), "")
check("stage3_provisional_0", not any("provisional" in " ".join(r.values()).lower() for r in field_rows), "")

required_fields = {
    "scope", "intervals", "intervals[].urgencyLevel", "intervals[].minScore",
    "intervals[].maxScore", "intervals[].displayToken", "version", "updatedAt",
    "updatedByUserId", "resultType", "employee", "employee.userId",
    "employee.displayName", "employee.departmentId", "employee.departmentName",
    "employee.jobTitle", "employee.accountStatus", "employee.deepLink",
    "employee.isRedacted",
}
available_fields = {r["Field"] for r in field_rows}
check("new_fields_in_stage3_trace", required_fields.issubset(available_fields), sorted(required_fields - available_fields))

all_output_text = "\n".join(
    p.read_text(encoding="utf-8-sig", errors="replace")
    for p in OUT.iterdir()
    if p.suffix.lower() in {".md", ".csv"}
)
check("OQ_001_fixed", "OQ-001" in all_output_text and "**Fixed**" in (OUT / "Stage_4_Open_Questions_4.1.2.md").read_text(encoding="utf-8"), "")
check("OQ_003_fixed", "OQ-003" in all_output_text and "**Fixed**" in (OUT / "Stage_4_Open_Questions_4.1.2.md").read_text(encoding="utf-8"), "")
check("no_avatar_in_employee_contract", "Avatar, arbitrary role" in module_text and "avatar отсутствует" in all_output_text, "")
check("no_client_postfilter", "client post-filter запрещён" in all_output_text or "client post-filter forbidden" in all_output_text, "")
check("non_color_accessibility", "не является единственным носителем" in module_text and "High Contrast" in module_text, "")

expected_hashes = {
    ROOT / "outputs" / "Organizer_Stage2_Technical_Specification_2.3_Final.zip": "75EFC3E83F09FBCC41AE7DA68A96F2EC0EBDFC74E61F62615F4DA3478AFE5019",
    ROOT / "outputs" / "Organizer_Stage3_Final_Baseline_3.5.zip": "6C2447E935DD413488E482F7DB3C481C8DC6E53AEB57A07D1DF23D3ADA85381E",
    ROOT / "outputs" / "Organizer_Stage4_1_2_PRD_Delta_Input.zip": "866F5DAC06ABA44B847F3C06D6AC8C326363B71DCB594F8E92C7A06A2E8AD21A",
}
for path, expected in expected_hashes.items():
    actual = hashlib.sha256(path.read_bytes()).hexdigest().upper()
    check(f"source_hash_{path.name}", actual == expected, actual)

# Final package and manifest verification.
candidate_zip = ROOT / "outputs" / "Organizer_Stage4_PRD_Candidate_4.1.2.zip"
audit_zip = ROOT / "outputs" / "Organizer_Stage4_2_Audit_Input.zip"
package_hashes = {
    candidate_zip: "84260071D3917AE00AA617FDBF2E5AB540A719F7D717367B0504E36159845AF9",
    audit_zip: "4CC6DF2A7CF54F3E692971BDB2A39322615442748E95AD7104A1564229CD845F",
}
for path, expected in package_hashes.items():
    actual = hashlib.sha256(path.read_bytes()).hexdigest().upper()
    check(f"package_hash_{path.name}", actual == expected, actual)
    sidecar = Path(str(path) + ".sha256").read_text(encoding="ascii").strip()
    check(f"sidecar_{path.name}", sidecar == f"{actual} *{path.name}", sidecar)
    with zipfile.ZipFile(path, "r") as zf:
        check(f"crc_{path.name}", zf.testzip() is None, "")
        for info in zf.infolist():
            zf.read(info.filename)
        expected_count = 15 if path == candidate_zip else 24
        check(f"entries_{path.name}", len(zf.infolist()) == expected_count, len(zf.infolist()))

candidate_manifest = (OUT / "00_MANIFEST.md").read_text(encoding="utf-8")
manifest_mismatches = []
for line in candidate_manifest.splitlines():
    match = re.match(r"\| ([^|]+) \| (\d+) \| `([0-9A-F]{64})` \|", line)
    if not match:
        continue
    name, size_text, digest = match.groups()
    path = OUT / name.strip()
    if not path.exists() or path.stat().st_size != int(size_text) or hashlib.sha256(path.read_bytes()).hexdigest().upper() != digest:
        manifest_mismatches.append(name.strip())
check("candidate_manifest_hashes", not manifest_mismatches, manifest_mismatches)

with zipfile.ZipFile(audit_zip, "r") as zf:
    audit_manifest_name = "Organizer_Stage4_2_Audit_Input/00_AUDIT_INPUT_MANIFEST.md"
    audit_manifest = zf.read(audit_manifest_name).decode("utf-8")
    audit_mismatches = []
    for line in audit_manifest.splitlines():
        match = re.match(r"\| `([^`]+)` \| (\d+) \| `([0-9A-F]{64})` \|", line)
        if not match:
            continue
        rel, size_text, digest = match.groups()
        name = f"Organizer_Stage4_2_Audit_Input/{rel}"
        data = zf.read(name)
        if len(data) != int(size_text) or hashlib.sha256(data).hexdigest().upper() != digest:
            audit_mismatches.append(rel)
    check("audit_manifest_hashes", not audit_mismatches, audit_mismatches)

normalized_s3 = ROOT / "sources" / "stage_3_5" / "Organizer_Stage3_Final_Baseline_3.5.zip"
check("normalized_stage3_hash", hashlib.sha256(normalized_s3.read_bytes()).hexdigest().upper() == expected_hashes[ROOT / "outputs" / "Organizer_Stage3_Final_Baseline_3.5.zip"], "")
root_docs = "\n".join(
    (ROOT / name).read_text(encoding="utf-8-sig")
    for name in ("PROJECT_BOOTSTRAP.md", "SOURCE_MANIFEST.md", "SOURCE_INTEGRITY_REPORT.md", "CANONICAL_BASELINE.md")
)
check("root_docs_current_baselines", "Stage 4.1.2" in root_docs and "current candidate" in root_docs and "Stage 2.3.1" in root_docs and "Stage 3.5" in root_docs, "")

report = {
    "status": "PASS" if not errors else "FAIL",
    "checks": checks,
    "errors": errors,
}
(BASE / "validation_run.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"status": report["status"], "failed": len(errors), "errors": errors}, ensure_ascii=False, indent=2))
raise SystemExit(0 if not errors else 1)
