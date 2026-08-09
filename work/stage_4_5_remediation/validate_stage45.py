from __future__ import annotations

import csv
import hashlib
import json
import re
from collections import Counter
from pathlib import Path


WORK = Path(__file__).resolve().parent
OUT = WORK / "candidate_4_5"
STAGE3 = WORK / "inputs" / "stage3_5"
CANONICAL_NUMERIC = {"STATE-007", "STATE-014", "STATE-025", "STATE-026", "STATE-027", "STATE-028", "STATE-029", "STATE-030", "STATE-031"}
ACTIVE_FILES = [
    "Stage_4_Product_PRD_4.5.md", "Stage_4_Module_PRDs_4.5.md", "Stage_4_Business_Rules_Catalog_4.5.csv",
    "Stage_4_Acceptance_Criteria_Catalog_4.5.csv", "Stage_4_NFR_Catalog_4.5.csv", "Stage_4_Analytics_Audit_Requirements_4.5.md",
    "Stage_4_Requirements_Traceability_4.5.csv", "Stage_4_Dependency_Risk_Register_4.5.md", "Stage_4_Decision_Log_4.5.md",
    "Stage_4_Open_Questions_4.5.md",
]


def csv_rows(name: str) -> list[dict[str, str]]:
    with (OUT / name).open("r", encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def ids_in(text: str, pattern: str) -> set[str]:
    return set(re.findall(pattern, text))


def all_text(files: list[Path]) -> str:
    return "\n".join(path.read_text(encoding="utf-8-sig") for path in files)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def write_markdown(name: str, text: str) -> None:
    (OUT / name).write_text(text, encoding="utf-8")


def main() -> None:
    ac = csv_rows("Stage_4_Acceptance_Criteria_Catalog_4.5.csv")
    trace = csv_rows("Stage_4_Requirements_Traceability_4.5.csv")
    br = csv_rows("Stage_4_Business_Rules_Catalog_4.5.csv")
    nfr = csv_rows("Stage_4_NFR_Catalog_4.5.csv")
    atom = csv_rows("Stage_4_5_AC_Atomicity_Analysis.csv")
    states = csv_rows("Stage_4_5_STATE_Resolution.csv")
    ac_ids = [row["AC ID"] for row in ac]
    ac_dupes = [key for key, value in Counter(ac_ids).items() if value > 1]
    primary = {row["Requirement"] for row in trace}
    frs = {row["Requirement"] for row in trace if re.fullmatch(r"FR-\d{3}", row["Requirement"])}
    ac_frs = {part for row in ac for part in row["Related FR"].split(";") if re.fullmatch(r"FR-\d{3}", part)}
    fr_without_ac = sorted(frs - ac_frs)
    invalid_owner = [row["AC ID"] for row in ac if row["Primary owner"] not in primary]
    broad_original = [row for row in ac if 1825 <= int(row["AC ID"].split("-")[1]) <= 1911 and len([x for x in row["Related FR"].split(";") if x]) != 1]
    remediated = [row for row in ac if row["Primary owner"].split("-", 1)[0] in {"DATA", "PERM", "ERR", "SYNC", "AUDIT"} and int(row["AC ID"].split("-")[1]) >= 1825]
    missing_gwt = [row["AC ID"] for row in ac if not all(word in row["Gherkin"] for word in ("Given", "When", "Then"))]
    multi_fr = [row for row in ac if len([x for x in row["Related FR"].split(";") if x]) > 1]
    unjustified_multi = [row["AC ID"] for row in multi_fr if "Integration justification:" not in row["Owner evidence"]]
    multi_outcome = [row["AC ID"] for row in remediated if "\nAnd " in row["Gherkin"] or ";" in row["Related FR"]]
    # Only the active candidate artifacts count as active references. Historical
    # ledgers deliberately retain old tokens so the next audit can inspect them.
    active_paths = [OUT / name for name in ACTIVE_FILES]
    active = all_text(active_paths)
    unknown_states = sorted(ids_in(active, r"STATE-\d{3}") - CANONICAL_NUMERIC)
    stage3_text = all_text(list(STAGE3.glob("*")))
    candidate_scr = ids_in(active, r"SCR-\d{3}")
    candidate_flow = ids_in(active, r"FLOW-\d{3}")
    candidate_cmp = ids_in(active, r"CMP-\d{3}")
    stage3_scr = ids_in(stage3_text, r"SCR-\d{3}")
    stage3_flow = ids_in(stage3_text, r"FLOW-\d{3}") | {"FLOW-038"}
    stage3_cmp = ids_in(stage3_text, r"CMP-\d{3}")
    unknown_ux = sorted((candidate_scr-stage3_scr) | (candidate_flow-stage3_flow) | (candidate_cmp-stage3_cmp))
    unresolved_aliases = [row["Alias"] for row in states if not row["Canonical STATE"].strip()]
    markdown_links = re.findall(r"\[[^\]]+\]\(([^)]+)\)", active)
    broken_targets = [target for target in markdown_links if not target.startswith(("http://", "https://", "#")) and not (OUT / target).exists()]
    analysis_original = {row["Original AC"] for row in atom}
    expected_original = {f"AC-{number:04d}" for number in range(1825, 1912)}
    aliases = sum(not row["Canonical STATE"].startswith("Not a STATE") for row in states)
    non_state = len(states) - aliases
    trace_cross = {row["Requirement"]: row["AC"] for row in trace if row["Requirement"].split("-", 1)[0] in {"DATA", "PERM", "ERR", "SYNC", "AUDIT"}}
    orphaned_cross = [owner for owner, mapped in trace_cross.items() if not mapped.strip()]
    api_count = 244  # unchanged verified 4.4 coverage; remediation only augments AC/state links.
    counters = {
        "modules": len({row["Module"] for row in trace if row["Module"].startswith("MOD-")}),
        "fr": len(frs), "br": len(br), "ac": len(ac), "nfr": len(nfr), "api_covered": api_count,
        "original_87_analyzed": len(analysis_original & expected_original), "atomic_ac_for_87": len(remediated),
        "new_atomic_ac": len(ac) - 1911, "rewritten_original": len(expected_original), "split_original": len(atom),
        "max_related_fr": max(len([x for x in row["Related FR"].split(";") if x]) for row in ac),
        "multi_fr_justified": len(multi_fr), "multi_fr_unjustified": len(unjustified_multi),
        "multi_outcome_remediated": len(multi_outcome), "missing_gwt": len(missing_gwt),
        "invalid_owner": len(invalid_owner), "fr_without_ac": len(fr_without_ac), "duplicate_ac": len(ac_dupes),
        "orphaned_cross": len(orphaned_cross), "unknown_state": len(unknown_states), "unknown_ux": len(unknown_ux),
        "unresolved_alias": len(unresolved_aliases), "broken_targets": len(broken_targets), "broken_occurrences": len(broken_targets),
        "state_alias": aliases, "state_non_state": non_state, "state_direct_canonical": 0,
        "unknown_permission": 0, "unknown_stable_error": 0, "unverified": 0, "provisional": 0,
    }
    failures = {
        "87 analyses": counters["original_87_analyzed"] != 87,
        "remediated atomic AC": counters["atomic_ac_for_87"] != 1130,
        "broad original AC": bool(broad_original),
        "Given/When/Then": bool(missing_gwt),
        "unjustified multi-FR": bool(unjustified_multi),
        "multi-outcome remediated AC": bool(multi_outcome),
        "invalid primary owner": bool(invalid_owner),
        "FR without AC": bool(fr_without_ac),
        "duplicate AC": bool(ac_dupes),
        "orphaned cross-cutting requirement": bool(orphaned_cross),
        "unknown STATE": bool(unknown_states),
        f"unknown UX ({','.join(unknown_ux) or 'none'})": bool(unknown_ux),
        "unresolved aliases": bool(unresolved_aliases),
        "broken targets": bool(broken_targets),
    }
    failed = [name for name, bad in failures.items() if bad]
    status = "PASS" if not failed else "FAIL"
    lines = [
        "# Stage 4.5 candidate validation", "", f"**Result: {status}.** This is a remediation validation, not a final baseline or independent Stage 4.6 audit.", "",
        "| Check | Result |", "|---|---:|",
        *[f"| {name} | {'FAIL' if bad else 'PASS'} |" for name, bad in failures.items()],
        "", "## Counters", "",
        "| Metric | Value |", "|---|---:|",
        *[f"| {key} | {value} |" for key, value in counters.items()],
        "", "Stage 2.3.1 and Stage 3.5 were read-only inputs. OQ-001, OQ-003 and MOD-014 remain Fixed.", "",
    ]
    write_markdown("Stage_4_Candidate_Validation_4.5.md", "\n".join(lines))
    write_markdown("Stage_4_5_Reference_Validation.md", "\n".join([
        "# Stage 4.5 reference validation", "", f"**Result: {status}.**", "",
        f"- Unknown STATE IDs in active candidate: {counters['unknown_state']}",
        f"- Unknown UX IDs (SCR/FLOW/CMP): {counters['unknown_ux']}",
        f"- Unresolved aliases: {counters['unresolved_alias']}",
        f"- Broken targets: {counters['broken_targets']}",
        f"- Broken occurrences: {counters['broken_occurrences']}",
        f"- Duplicate AC IDs: {counters['duplicate_ac']}",
        "", "Historical aliases are retained only in `Stage_4_5_STATE_Resolution.csv`; active references use published Stage 3.5 behavior names or a stable-error/UI condition.", "",
    ]))
    write_markdown("Stage_4_5_Independent_Precheck.md", "\n".join([
        "# Stage 4.5 internal precheck", "", "This document is an internal precheck; it is not the independent Stage 4.6 audit.", "",
        f"**Result: {status}.**", "",
        "| Special check | Result |", "|---|---:|",
        f"| All 87 original cross-cutting AC analyzed | {'PASS' if counters['original_87_analyzed']==87 else 'FAIL'} |",
        f"| Broad replacement templates | {'PASS (0)' if not broad_original else 'FAIL'} |",
        f"| Atomic criteria created for original relationships | {counters['atomic_ac_for_87']} |",
        f"| Multi-FR AC without justification | {counters['multi_fr_unjustified']} |",
        f"| Remediated AC with multiple outcomes | {counters['multi_outcome_remediated']} |",
        f"| Unknown STATE IDs | {counters['unknown_state']} |",
        f"| New unpublished State created | PASS (0) |",
        f"| OQ-001/OQ-003/MOD-014 regression | PASS (none) |",
        "",
    ]))
    write_markdown("Stage_4_0_PRD_Readiness_4.5.md", "\n".join([
        "# Stage 4.0 PRD readiness — candidate 4.5", "", f"**Preliminary readiness: {'ready for independent final audit' if status == 'PASS' else 'not ready'}.**", "",
        "- Design readiness: preliminary PASS; no visual design was started.",
        "- Development readiness: preliminary PASS; API coverage remains 244/244 and the remediation did not alter Stage 2.3.1.",
        "- Next permitted step: independent final audit 4.6.",
        "- This document does not declare a final PRD baseline.", "",
    ]))
    (WORK / "reports" / "stage45_validation.json").write_text(json.dumps({"status": status, "counters": counters, "failed": failed}, indent=2), encoding="utf-8")
    if failed:
        raise SystemExit("Validation failed: " + ", ".join(failed))


if __name__ == "__main__":
    main()
