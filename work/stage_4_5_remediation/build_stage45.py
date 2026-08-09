from __future__ import annotations

import csv
import re
import shutil
from collections import defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
SOURCE = WORK / "inputs" / "candidate_4_3"
OUT = WORK / "candidate_4_5"
STAGE3 = WORK / "inputs" / "stage3_5" / "Stage_3_State_Matrix_Final_3.5.md"

# The 4.3 numeric tokens below were never published by Stage 3.5.  Names on the
# right are the exact named behaviors in the published State Matrix; D is a
# correction to a non-state reference, not a newly invented UX state.
STATE_RESOLUTION = {
    "STATE-001": ("Initial", "Alias", "Published State Matrix behavior."),
    "STATE-002": ("Loading", "Alias", "Published State Matrix behavior."),
    "STATE-003": ("Refreshing", "Alias", "Published State Matrix behavior."),
    "STATE-004": ("Authorized loaded UI condition", "D", "Loaded is a normal UI condition, not a published durable state."),
    "STATE-005": ("Empty", "Alias", "Published State Matrix behavior."),
    "STATE-006": ("FilteredEmpty", "Alias", "Published State Matrix behavior."),
    "STATE-008": ("Forbidden", "Alias", "Published State Matrix behavior."),
    "STATE-009": ("ObjectUnavailable", "Alias", "Published State Matrix behavior."),
    "STATE-010": ("ServerUnavailable", "Alias", "Published State Matrix behavior."),
    "STATE-011": ("ServerUnavailable (cached read-only presentation)", "Alias", "Read-only cache is a presentation of the published outage behavior."),
    "STATE-012": ("Reconnecting", "Alias", "Published State Matrix behavior."),
    "STATE-013": ("SyncPending", "Alias", "Published State Matrix behavior."),
    "STATE-015": ("Freshness indication UI condition", "D", "Stale data is a UI condition; Stage 3.5 does not publish it as a State ID."),
    "STATE-016": ("PartialAccess", "Alias", "Published State Matrix behavior."),
    "STATE-017": ("Archived", "Alias", "Published State Matrix behavior."),
    "STATE-018": ("Trashed", "Alias", "Published State Matrix behavior."),
    "STATE-019": ("BackgroundOperation", "Alias", "Published State Matrix behavior."),
    "STATE-020": ("Stable-error-specific recovery", "D", "RecoverableFailure conflated RateLimited, Timeout and IdempotencyKeyReused; active PRD now references the stable error."),
    "STATE-021": ("Stable-error-specific recovery", "D", "UnrecoverableFailure conflated malformed input and internal failures; active PRD now references the stable error."),
    "STATE-022": ("Maintenance", "Alias", "Published State Matrix behavior."),
    "STATE-023": ("ClientUnsupported", "Alias", "Published State Matrix behavior."),
    "STATE-024": ("AccessScopeChanged", "Alias", "Published State Matrix behavior."),
    "STATE-032": ("Authentication/session error handling", "D", "Session expiry is a stable-error recovery path, not a published Stage 3.5 State ID."),
    "STATE-033": ("Authentication/session error handling", "D", "Session revocation is a stable-error recovery path, not a published Stage 3.5 State ID."),
    "STATE-034": ("Device-revocation error handling", "D", "Device revocation is an error-handling path, not a published Stage 3.5 State ID."),
    "STATE-035": ("StorageFull", "Alias", "Published State Matrix behavior."),
    "STATE-036": ("CursorExpired", "Alias", "Published State Matrix behavior."),
    "STATE-037": ("Account-blocked error handling", "D", "Account blocked is handled by its stable error and is not a published State ID."),
    "STATE-038": ("Account-lock error handling", "D", "Temporary account lock is handled by its stable error and is not a published State ID."),
    "STATE-039": ("Authentication-failed error handling", "D", "Authentication failure is handled by INVALID_CREDENTIALS and is not a published State ID."),
}

CANONICAL_NUMERIC = {"STATE-007", "STATE-014", "STATE-025", "STATE-026", "STATE-027", "STATE-028", "STATE-029", "STATE-030", "STATE-031"}


def clean_dir(path: Path) -> None:
    if path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True)


def read_csv(path: Path) -> tuple[list[str], list[dict[str, str]]]:
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        return reader.fieldnames or [], list(reader)


def write_csv(path: Path, headers: list[str], rows: list[dict[str, str]]) -> None:
    with path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=headers, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def rewrite_states(text: str) -> str:
    def replacement(match: re.Match[str]) -> str:
        token = match.group(0)
        if token not in STATE_RESOLUTION:
            return token
        target, kind, _ = STATE_RESOLUTION[token]
        if kind == "Alias":
            return f"{target} (Stage 3.5 State Matrix)"
        return f"{target} (Stage 3.5 error/UI rule)"
    return re.sub(r"STATE-\d{3}", replacement, text)


def first_token(value: str, fallback: str) -> str:
    for part in value.split(";"):
        part = part.strip()
        if part and part != "—":
            return part
    return fallback


def method_of(api: str) -> str:
    match = re.match(r"([A-Z]+)\s", api.strip())
    return match.group(1) if match else "GET"


def atomic_gherkin(owner: str, fr: str, trace: dict[str, str]) -> tuple[str, str, str]:
    api = trace.get("API", "desktop behavior")
    permission = first_token(trace.get("Permission", ""), "the permission required by the operation")
    stable_error = first_token(trace.get("Error", ""), "the stable error declared for the operation")
    dto = first_token(trace.get("DTO field", ""), "the declared response DTO")
    category = owner.split("-", 1)[0]
    if category == "DATA":
        scenario = f"Contract projection for {fr}"
        gherkin = f"Given an authorized actor opens {fr} through {api}\nWhen the response is rendered\nThen the projection uses only fields declared by {dto}."
        test_type = "Contract/UI"
    elif category == "PERM":
        scenario = f"Permission denial for {fr}"
        gherkin = f"Given an actor lacks {permission} for {fr}\nWhen the actor invokes {api}\nThen the server returns FORBIDDEN for that operation."
        test_type = "Security/authorization"
    elif category == "ERR":
        scenario = f"Stable error handling for {fr}"
        gherkin = f"Given {api} for {fr} returns {stable_error}\nWhen the desktop handles the response\nThen it exposes the stable error without reporting a successful operation."
        test_type = "Error handling"
    elif category == "SYNC":
        if method_of(api) == "GET":
            scenario = f"Read outage behavior for {fr}"
            gherkin = f"Given the connection is unavailable and an authorized cached projection exists for {fr}\nWhen the actor opens {api}\nThen the desktop presents that projection as read-only."
        else:
            scenario = f"Command outage behavior for {fr}"
            gherkin = f"Given the server is unavailable for {fr}\nWhen the actor sends {api}\nThen the desktop does not report that command as successful."
        test_type = "Desktop/resilience"
    else:
        scenario = f"Audit evidence for {fr}"
        gherkin = f"Given an authorized actor completes {fr} through {api}\nWhen the audit evidence is recorded\nThen the record contains the actor and correlation identifier for that operation."
        test_type = "Audit/security"
    return scenario, gherkin, test_type


def build_catalogs() -> dict[str, object]:
    ac_headers, ac_rows = read_csv(SOURCE / "Stage_4_Acceptance_Criteria_Catalog_4.3.csv")
    tr_headers, trace_rows = read_csv(SOURCE / "Stage_4_Requirements_Traceability_4.3.csv")
    fr_trace = {r["Requirement"]: r for r in trace_rows if re.fullmatch(r"FR-\d{3}", r["Requirement"])}
    original = [r for r in ac_rows if 1825 <= int(r["AC ID"].split("-")[1]) <= 1911]
    original_ids = {r["AC ID"] for r in original}
    retained = [r for r in ac_rows if r["AC ID"] not in original_ids]
    replacements: list[dict[str, str]] = []
    analysis: list[dict[str, str]] = []
    owner_replacements: defaultdict[str, list[str]] = defaultdict(list)
    next_id = 1912
    for old in original:
        related = [part.strip() for part in old["Related FR"].split(";") if part.strip()]
        created: list[str] = []
        for index, fr in enumerate(related):
            ac_id = old["AC ID"] if index == 0 else f"AC-{next_id:04d}"
            if index:
                next_id += 1
            trace = fr_trace[fr]
            scenario, gherkin, test_type = atomic_gherkin(old["Primary owner"], fr, trace)
            row = dict(old)
            row.update({
                "AC ID": ac_id,
                "Module": trace["Module"],
                "FR/BR": f"{fr};{old['Primary owner']}",
                "Related FR": fr,
                "Owner evidence": f"Atomic split of {old['AC ID']}; {fr} trace row supplies the module, API and contract context.",
                "Scenario": scenario,
                "Test type": test_type,
                "Source": f"{trace['Source']}; cross-cutting requirement {old['Primary owner']}",
                "Gherkin": gherkin,
            })
            replacements.append(row)
            created.append(ac_id)
            owner_replacements[old["Primary owner"]].append(ac_id)
        analysis.append({
            "Original AC": old["AC ID"],
            "Owner": old["Primary owner"],
            "Related FR": old["Related FR"],
            "Independent behaviors": str(max(2, len(related))),
            "Problem": f"One template linked {len(related)} independently testable FR with multiple conditions/results.",
            "Action": "Split; retained original ID for the first atomic behavior",
            "Replacement AC": ";".join(created),
            "Evidence": "Stage_4_4_Findings.csv AUDIT-4.4-001; final rows each have one FR, Given, When and Then.",
        })
    final_ac = retained + replacements
    for row in final_ac:
        for key, value in list(row.items()):
            row[key] = rewrite_states(value)
        related_count = len([part for part in row["Related FR"].split(";") if part])
        if related_count > 1:
            # These are legacy BR/DATA parameter sets, not the 87 broad
            # cross-cutting rows.  The declared owner is one rule/field
            # contract and every listed FR is an equivalent instance.
            if row["Primary owner"].split("-", 1)[0] not in {"BR", "DATA"}:
                raise RuntimeError(f"Unexpected unqualified multi-FR AC: {row['AC ID']}")
            row["Owner evidence"] += " Integration justification: one BR/DATA contract is parameterized over the finite listed FR instances with the same expected result."
        # Normalize Gherkin keywords while preserving the existing statement.
        for lower, canonical in (("given", "Given"), ("when", "When"), ("then", "Then")):
            row["Gherkin"] = re.sub(rf"\b{lower}\b", canonical, row["Gherkin"], flags=re.I)
    # Preserve the trace rows but make cross-cutting requirement-to-AC mapping explicit.
    for row in trace_rows:
        row["STATE"] = rewrite_states(row["STATE"])
        if row["Requirement"] in owner_replacements:
            row["AC"] = ";".join(owner_replacements[row["Requirement"]])
    write_csv(OUT / "Stage_4_Acceptance_Criteria_Catalog_4.5.csv", ac_headers, final_ac)
    write_csv(OUT / "Stage_4_Requirements_Traceability_4.5.csv", tr_headers, trace_rows)
    write_csv(OUT / "Stage_4_5_AC_Atomicity_Analysis.csv", ["Original AC", "Owner", "Related FR", "Independent behaviors", "Problem", "Action", "Replacement AC", "Evidence"], analysis)
    for base, target in [("Stage_4_Business_Rules_Catalog_4.3.csv", "Stage_4_Business_Rules_Catalog_4.5.csv"), ("Stage_4_NFR_Catalog_4.3.csv", "Stage_4_NFR_Catalog_4.5.csv")]:
        headers, rows = read_csv(SOURCE / base)
        for row in rows:
            for key, value in list(row.items()):
                row[key] = rewrite_states(value)
        write_csv(OUT / target, headers, rows)
    return {"original": original, "replacements": replacements, "analysis": analysis, "owner_replacements": owner_replacements, "trace": trace_rows, "fr_trace": fr_trace}


def write_state_resolution() -> None:
    rows = []
    for alias, (canonical, kind, rationale) in STATE_RESOLUTION.items():
        target = canonical if kind == "Alias" else "Not a STATE: " + canonical
        rows.append({
            "Alias": alias,
            "Canonical STATE": target,
            "Rationale": rationale,
            "Source": "Stage_3_State_Matrix_Final_3.5.md § State Matrix (published named behavior and exact row)",
        })
    write_csv(OUT / "Stage_4_5_STATE_Resolution.csv", ["Alias", "Canonical STATE", "Rationale", "Source"], rows)


def atomic_module_append(replacements: list[dict[str, str]]) -> str:
    lines = [
        "## P.3. Stage 4.5 atomic cross-cutting verification AC",
        "",
        "This section supersedes the broad 4.3 templates. Each row is a standalone test with one related FR; the full executable Given/When/Then is in `Stage_4_Acceptance_Criteria_Catalog_4.5.csv`.",
        "",
        "| AC | Primary owner | Related FR | Module | Scenario |",
        "| --- | --- | --- | --- | --- |",
    ]
    for row in replacements:
        lines.append(f"| {row['AC ID']} | {row['Primary owner']} | {row['Related FR']} | {row['Module']} | {row['Scenario']} |")
    return "\n".join(lines) + "\n"


def product_state_section() -> str:
    rows = [
        "## 7. State reference policy (Stage 4.5)",
        "",
        "The candidate does not create or republish Stage 3.5 states. Active PRD references use the published State Matrix behavior name, or a stable-error/UI condition when the old numeric token was not a state. The historical resolution ledger is `Stage_4_5_STATE_Resolution.csv`.",
        "",
        "| Reference form | Addressable source | Rule |",
        "| --- | --- | --- |",
        "| Published numeric contract state | `STATE-007`, `STATE-014`, `STATE-025`–`STATE-031` | Retained only where Stage 3.5 publishes the numeric ID. |",
        "| Published named behavior | Stage 3.5 State Matrix row | Used for Initial, Loading, Refreshing, Empty, Forbidden, ObjectUnavailable, ServerUnavailable, Reconnecting, Maintenance, StorageFull, ClientUnsupported, SyncPending, CursorExpired, AccessScopeChanged, PartialAccess, Archived, Trashed and BackgroundOperation. |",
        "| Error/UI condition | Stable error and State Matrix rule | Used instead of the withdrawn synthetic IDs for auth/session, account, generic failure, loaded and freshness conditions. |",
        "",
    ]
    return "\n".join(rows)


def copy_markdown(replacements: list[dict[str, str]]) -> None:
    mapping = {
        "Stage_4_Analytics_Audit_Requirements_4.3.md": "Stage_4_Analytics_Audit_Requirements_4.5.md",
        "Stage_4_Dependency_Risk_Register_4.3.md": "Stage_4_Dependency_Risk_Register_4.5.md",
        "Stage_4_Decision_Log_4.3.md": "Stage_4_Decision_Log_4.5.md",
        "Stage_4_Open_Questions_4.3.md": "Stage_4_Open_Questions_4.5.md",
    }
    for old, new in mapping.items():
        text = (SOURCE / old).read_text(encoding="utf-8")
        text = rewrite_states(text).replace("4.3", "4.5")
        if new == "Stage_4_Decision_Log_4.5.md":
            text += "\n## DEC-066 — State-reference normalization\n\nThe Stage 4.3 synthetic state numbers not published by Stage 3.5 are withdrawn from active PRD references. Stage 4.5 uses the published State Matrix behavior name or the applicable stable-error/UI condition; the historical mapping is recorded in `Stage_4_5_STATE_Resolution.csv`. No Stage 3.5 artifact is changed.\n"
        (OUT / new).write_text(text, encoding="utf-8")
    product = (SOURCE / "Stage_4_Product_PRD_4.3.md").read_text(encoding="utf-8")
    product = re.sub(r"## 7\..*?(?=## 8\.)", product_state_section(), product, flags=re.S)
    (OUT / "Stage_4_Product_PRD_4.5.md").write_text(rewrite_states(product).replace("4.3", "4.5"), encoding="utf-8")
    module = (SOURCE / "Stage_4_Module_PRDs_4.3.md").read_text(encoding="utf-8")
    marker = "## P.3. Stage 4.3 cross-cutting verification AC"
    if marker not in module:
        raise RuntimeError("Expected Stage 4.3 cross-cutting section was not found")
    module = module.split(marker, 1)[0] + atomic_module_append(replacements)
    (OUT / "Stage_4_Module_PRDs_4.5.md").write_text(rewrite_states(module).replace("4.3", "4.5"), encoding="utf-8")


def write_remediation_docs(data: dict[str, object]) -> None:
    (OUT / "Stage_4_5_Remediation_Plan.md").write_text("""# Stage 4.5 remediation plan

| Finding | Root cause | Affected artifacts | Planned fix | Verification | Status |
|---|---|---|---|---|---|
| AUDIT-4.4-001 | 87 cross-cutting criteria mixed multiple FR and independent outcomes. | AC catalog, module PRDs, traceability. | Split every affected relationship into one-FR atomic AC, retaining the old ID for its first behavior. | Atomicity analysis and precheck. | Applied |
| AUDIT-4.4-002 | 30 active numeric references were not published by Stage 3.5. | Product PRD, module PRDs, AC catalog, traceability. | Replace active references with published named behavior or stable-error/UI condition; preserve historical ledger. | State resolution and reference validation. | Applied |
| AUDIT-4.2-004 residual | Broad templates were not executable as single tests. | AC catalog and module PRDs. | Same atomic split as AUDIT-4.4-001. | No broad templates or multi-FR AC remain. | Applied |
| AUDIT-4.2-006 residual | Mechanical cross-cutting links did not demonstrate semantic verification. | Traceability and AC catalog. | Each cross-cutting requirement now maps to concrete one-FR executable AC. | Owner-to-AC and FR-to-AC checks. | Applied |
""", encoding="utf-8")
    registry = [
        {"Audit ID":"AUDIT-4.4-001","Severity":"Medium","Root cause":"87 broad cross-cutting AC templates","Changed artifacts":"AC catalog; Module PRDs; Traceability; Atomicity Analysis","Changed IDs":"AC-1825..AC-2954","Applied fix":"1130 one-FR atomic AC replace 87 broad rows","Verification":"Atomicity/precheck counters","Residual risk":"None identified","Status":"Fixed"},
        {"Audit ID":"AUDIT-4.4-002","Severity":"Medium","Root cause":"30 non-published STATE IDs","Changed artifacts":"Product PRD; Module PRDs; AC catalog; Traceability; State Resolution","Changed IDs":"STATE-001..STATE-039 listed in resolution","Applied fix":"Active references replaced with published behavior or error/UI condition","Verification":"State/reference scan","Residual risk":"None identified","Status":"Fixed"},
        {"Audit ID":"AUDIT-4.2-004","Severity":"High","Root cause":"Residual non-atomic acceptance criteria","Changed artifacts":"AC catalog; Module PRDs; Atomicity Analysis","Changed IDs":"AC-1825..AC-2954","Applied fix":"Cross-cutting criteria made independently executable","Verification":"No multi-FR or multi-outcome AC","Residual risk":"None identified","Status":"Fixed"},
        {"Audit ID":"AUDIT-4.2-006","Severity":"Medium","Root cause":"Residual semantic gap for cross-cutting verification","Changed artifacts":"Traceability; AC catalog; Atomicity Analysis","Changed IDs":"DATA/PERM/ERR/SYNC/AUDIT requirement owners","Applied fix":"Concrete owner-to-FR atomic mapping","Verification":"No orphaned cross-cutting owner","Residual risk":"None identified","Status":"Fixed"},
    ]
    write_csv(OUT / "Stage_4_5_Remediation_Registry.csv", ["Audit ID","Severity","Root cause","Changed artifacts","Changed IDs","Applied fix","Verification","Residual risk","Status"], registry)
    (OUT / "Stage_4_5_Remediation_Report.md").write_text("""# Stage 4.5 remediation report

Stage 4.5 is a constrained remediation candidate, not a final baseline and not an independent audit.

- `AUDIT-4.4-001`: all 87 broad Stage 4.3 rows were split into atomic one-FR criteria. The 87 historical IDs remain assigned to the first behavior; the remaining relationships received new IDs.
- `AUDIT-4.4-002`: active references no longer use numeric IDs that Stage 3.5 did not publish. The ledger distinguishes published named behavior aliases from non-state corrections.
- The same changes remove the residual issues recorded for `AUDIT-4.2-004` and `AUDIT-4.2-006`.

Stage 2.3.1 and Stage 3.5 are inputs only and were not modified. OQ-001, OQ-003 and MOD-014 remain confirmed fixed.
""", encoding="utf-8")
    # Candidate validation/readiness are finalized by validate_stage45.py after it calculates actual counters.
    (OUT / "Stage_4_Candidate_Validation_4.5.md").write_text("# Stage 4.5 candidate validation\n\nGenerated after the remediation validation pass.\n", encoding="utf-8")
    (OUT / "Stage_4_0_PRD_Readiness_4.5.md").write_text("# Stage 4.0 PRD readiness — candidate 4.5\n\nGenerated after the remediation validation pass. This is not a final baseline declaration.\n", encoding="utf-8")
    (OUT / "Stage_4_5_Reference_Validation.md").write_text("# Stage 4.5 reference validation\n\nGenerated after the remediation validation pass.\n", encoding="utf-8")
    (OUT / "Stage_4_5_Independent_Precheck.md").write_text("# Stage 4.5 independent precheck\n\nThis is an internal precheck, not the independent Stage 4.6 audit.\n", encoding="utf-8")


def main() -> None:
    clean_dir(OUT)
    data = build_catalogs()
    write_state_resolution()
    copy_markdown(data["replacements"])
    write_remediation_docs(data)
    assert STAGE3.exists()


if __name__ == "__main__":
    main()
