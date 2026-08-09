from __future__ import annotations

import shutil
from pathlib import Path


ROOT = Path(r"C:\Users\novik\Таск")
WORK = ROOT / "work" / "stage_4_3_remediation"
SOURCE = WORK / "candidate_4_3"
FINAL = WORK / "final_candidate"

CORE_MARKDOWN = [
    "Stage_4_Product_PRD_4.3.md",
    "Stage_4_Module_PRDs_4.3.md",
    "Stage_4_Analytics_Audit_Requirements_4.3.md",
    "Stage_4_Dependency_Risk_Register_4.3.md",
    "Stage_4_Decision_Log_4.3.md",
    "Stage_4_Open_Questions_4.3.md",
    "Stage_4_Candidate_Validation_4.3.md",
    "Stage_4_0_PRD_Readiness_4.3.md",
]

REPORTS = [
    "Stage_4_3_MOD_014_Conflict_Analysis.md",
    "Stage_4_3_Reference_Repair_Report.md",
    "Stage_4_3_Independent_Precheck.md",
]


def safe_clean(path: Path) -> None:
    allowed = WORK.resolve()
    resolved = path.resolve()
    if resolved != allowed and allowed not in resolved.parents:
        raise RuntimeError(f"Refusing to clean outside Stage 4.3 work directory: {resolved}")
    if path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True)


def main() -> None:
    safe_clean(FINAL)
    for name in CORE_MARKDOWN:
        source = SOURCE / name
        if not source.exists():
            raise RuntimeError(f"Missing core 4.3 artifact: {source}")
        shutil.copy2(source, FINAL / name)
    for name in REPORTS:
        source = WORK / name
        if not source.exists():
            raise RuntimeError(f"Missing Stage 4.3 report: {source}")
        shutil.copy2(source, FINAL / name)
    print(f"Copied {len(CORE_MARKDOWN) + len(REPORTS)} Markdown artifacts.")


if __name__ == "__main__":
    main()
