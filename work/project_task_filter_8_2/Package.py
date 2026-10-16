"""Package the documentation-only 8.2 review; never connects to a database."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def git(repo, *args):
    return subprocess.check_output(["git", "-C", str(repo), *args]).decode("utf-8").strip()


parser = argparse.ArgumentParser()
parser.add_argument("--repository", type=Path, default=Path(__file__).resolve().parents[2])
parser.add_argument("--inputs", type=Path, required=True, help="Original Task workspace; read only")
parser.add_argument("--evidence", type=Path, required=True, help="Directory containing baseline TRX files")
args = parser.parse_args()
repo, inputs = args.repository.resolve(), args.inputs.resolve()
version = "8.2.0"
name = "20261016_project_task_filter_contract_8.2.0"
package = repo / "outputs" / name
package.mkdir(parents=True, exist_ok=True)
(package / "evidence").mkdir(exist_ok=True)
(package / ".gitattributes").write_text("* -text\n", encoding="utf-8")
doc_rel = "work/production/docs/PROJECT-TASK-FILTER-CONTRACT-PROPOSAL.md"
doc = repo / doc_rel
text = doc.read_text(encoding="utf-8")
assert "PROPOSED — NOT APPROVED" in text
assert len(re.findall(r"^## \d+\.", text, re.M)) == 15
for target in re.findall(r"\]\(([^)]+)\)", text):
    if not target.startswith("https://"):
        assert (doc.parent / target).resolve().exists(), target
shutil.copy2(doc, package / doc.name)

results = []
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
for filename, expected in [("cursor-baseline.trx", 4), ("endpoints-baseline.trx", 166)]:
    source = args.evidence / filename
    root = ET.parse(source).getroot()
    counts = root.find("t:ResultSummary/t:Counters", ns).attrib
    assert int(counts["total"]) == expected
    assert int(counts["passed"]) == expected and int(counts["failed"]) == 0
    results.append({"file": "evidence/" + filename, "counts": counts, "scope": "existing baseline, no proposed filter implementation"})
    shutil.copy2(source, package / "evidence" / filename)

source_paths = [
    "AGENTS.md", "CANONICAL_BASELINE.md", "sources/concept/Task_Concept_Final.txt",
    "sources/stage_1/architecture_organizer.md",
    "sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2_Repacked.zip",
    "sources/stage_2_3/Organizer_Stage2_Technical_Specification_2.3_Final.zip",
    "sources/stage_3_4/Organizer_Stage3_Final_Baseline_3.4.zip",
    "sources/stage_3_5/Organizer_Stage3_Final_Baseline_3.5.zip",
    "sources/stage_4_1_1/Organizer_Stage4_PRD_Candidate_4.1.1.zip",
    "work/stage_4_6_lite/final_baseline/Organizer_Stage4_PRD_Candidate_4.5.zip",
    "work/stage_4_6_lite/final_baseline/Stage_4_Final_Baseline_Approval.md",
    "work/stage_4_6_lite/final_baseline/Stage_4_Final_Baseline_Manifest.md",
]
inventory = [{"path": p, "sha256": sha(inputs / p)} for p in source_paths]
for p in source_paths:
    if not p.endswith(".zip"):
        continue
    with zipfile.ZipFile(inputs / p) as z:
        for member in z.namelist():
            if any(token in member for token in ["openapi/openapi.yaml", "docs/02_api_and_concurrency.md",
                      "Search_Contract.md", "Stage_3_UX_Architecture", "Stage_3_Screen_Catalog",
                      "Stage_4_Module_PRDs", "Stage_4_NFR_Catalog"]) and member.endswith((".yaml", ".md", ".csv")):
                inventory.append({"path": p + "!" + member,
                                  "sha256": hashlib.sha256(z.read(member)).hexdigest()})
code_paths = [
    "src/Task.Api/Tasks/TaskEndpoints.cs", "src/Task.Api/Security/TaskPermissionAuthorization.cs",
    "src/Task.Api/Security/TaskJwtAuthenticationHandler.cs",
    "src/Task.Application/TaskReadContracts.cs", "src/Task.Application/TaskReadCursorCodec.cs",
    "src/Task.Application/ITaskReadStore.cs", "src/Task.Application/ProductData/ProductApiContracts.cs",
    "src/Task.Domain/TaskCardContent.cs", "src/Task.Infrastructure/Persistence/PostgresTaskReadStore.cs",
    "src/Task.Infrastructure/Persistence/PostgresProductApiSearch.cs",
    "src/Task.Infrastructure/Persistence/PostgresSessionRepository.cs",
    "src/Task.Infrastructure/Persistence/Migrations/001_task_persistence_foundation.sql",
    "src/Task.Infrastructure/Persistence/Migrations/011_task_card.sql",
    "src/Task.Infrastructure/Persistence/Migrations/013_object_authorization.sql",
    "src/Task.Desktop/Tasks/DesktopTasksApiClient.cs", "src/Task.Desktop/ViewModels/ProjectsViewModel.cs",
    "tests/Task.Tests/TaskReadCursorCodecTests.cs", "tests/Task.Tests/PostgresTaskReadStoreTests.cs",
    "tests/Task.ServiceHosts.Tests/TaskEndpointsTests.cs",
]
for p in code_paths:
    full = "work/production/" + p
    current = repo / full
    baseline = subprocess.check_output(["git", "-C", str(repo), "show", "deb16b3:" + full]).decode("utf-8-sig")
    assert current.read_text(encoding="utf-8-sig").splitlines() == baseline.splitlines(), full
    inventory.append({"path": full, "sha256": sha(current), "revision": "deb16b3"})
stage81 = inputs / "work/project_related_tasks_8_1/repo"
assert git(stage81, "rev-parse", "--short=7", "HEAD") == "eb1044d"
stage81_package = stage81 / "outputs/20261016_project_related_tasks_8.1.0"
for p in ["validation-report.md", "validation.json", "comparison.csv", "manifest.json"]:
    inventory.append({"path": "outputs/20261016_project_related_tasks_8.1.0/" + p,
                      "revision": "eb1044d", "sha256": sha(stage81_package / p)})
stage81_manifest = json.loads((stage81_package / "manifest.json").read_text(encoding="utf-8"))
for entry in stage81_manifest["files"]:
    assert sha(stage81_package / entry["path"]) == entry["sha256"], entry["path"]
stage81_test_counts = {}
for filename in ["baseline.trx", "related-tasks.trx", "projects-final.trx", "desktop-full.trx"]:
    counts = ET.parse(stage81_package / "evidence" / filename).getroot().find("t:ResultSummary/t:Counters", ns).attrib
    assert int(counts["failed"]) == 0
    stage81_test_counts[filename] = counts
write_json(package / "evidence/stage81-evidence-verification.json", {
    "revision": "eb1044d", "manifestEntriesVerified": len(stage81_manifest["files"]),
    "testCounts": stage81_test_counts, "testsRerun": False})
scope_diff = git(repo, "diff", "--name-only", "deb16b3", "eb1044d", "--",
                 "work/production/src/Task.Api", "work/production/src/Task.Application",
                 "work/production/src/Task.Infrastructure", "work/production/src/Task.Desktop/Tasks")
assert not scope_diff
write_json(package / "evidence/source-inventory.json", inventory)

# Excerpts retain exact source locations without copying entire normative archives.
excerpts = []
for rel, start, end in [
    ("src/Task.Api/Tasks/TaskEndpoints.cs", 51, 110),
    ("src/Task.Api/Tasks/TaskEndpoints.cs", 272, 288),
    ("src/Task.Application/TaskReadContracts.cs", 24, 29),
    ("src/Task.Infrastructure/Persistence/PostgresTaskReadStore.cs", 147, 164),
    ("src/Task.Infrastructure/Persistence/Migrations/011_task_card.sql", 1, 8),
    ("src/Task.Infrastructure/Persistence/Migrations/013_object_authorization.sql", 91, 115),
    ("src/Task.Desktop/ViewModels/ProjectsViewModel.cs", 349, 363),
]:
    lines = (repo / "work/production" / rel).read_text(encoding="utf-8-sig").splitlines()
    excerpts.append({"revision": "deb16b3", "path": "work/production/" + rel,
                     "lines": [{"line": i + 1, "text": lines[i]} for i in range(start - 1, min(end, len(lines)))]})
rel = "work/production/src/Task.Desktop/ViewModels/ProjectsViewModel.RelatedTasks.cs"
lines = (stage81 / rel).read_text(encoding="utf-8-sig").splitlines()
excerpts.append({"revision": "eb1044d", "path": rel,
                 "lines": [{"line": i + 1, "text": lines[i]} for i in range(73, 105)]})
write_json(package / "evidence/code-excerpts.json", excerpts)
write_json(package / "validation.json", {
    "version": version, "date": "2026-10-16", "status": "REVIEW_ARTIFACT_VALIDATED",
    "proposalStatus": "PROPOSED — NOT APPROVED", "baseline": "deb16b3", "stage81": "eb1044d",
    "tests": results, "documentSections": 15, "localLinksExist": True,
    "productionSourcesUnchangedAgainstBaseline": True, "stage81ServerAndClientApiUnchanged": True,
    "databaseValidation": "NOT RUN: no configured test connection; Docker daemon unavailable",
    "explainAnalyze": "NOT RUN", "proposedFilterImplemented": False,
    "dashboardChanged": False, "sourcesChanged": False,
})
(package / "validation-report.md").write_text(
    "# Contract Review 8.2.0 — validation\n\n"
    "Proposal: **PROPOSED — NOT APPROVED**. Date: 2026-10-16.\n\n"
    "Baseline origin/main: deb16b3; separate local Stage 8.1: eb1044d.\n\n"
    "- Release TaskReadCursorCodecTests: 4/4 passed.\n"
    "- Release TaskEndpointsTests: 166/166 passed; existing ASPDEPR004 build warnings.\n"
    "- These are baseline tests, not validation of a new project filter.\n"
    "- Fifteen document sections, status and local source links checked.\n"
    "- Source hashes and exact code excerpts recorded; inspected production files match deb16b3.\n"
    "- Stage 8.1 server/application/infrastructure/desktop API paths match the main baseline.\n"
    "- No API, DB, OpenAPI, permissions or dashboard implementation changes.\n"
    "- PostgreSQL/EXPLAIN/load/WPF: NOT RUN for this proposal; no configured safe test DB.\n"
    "- Package.py verifies manifest hashes, ZIP CRC and archived bytes after packaging.\n\n"
    "The proposal is ready for contract approval. Product implementation is not completed or approved.\n",
    encoding="utf-8")
(package / "VERSION").write_text(version + "\n", encoding="utf-8")
# Freeze text bytes independently of Windows/Git checkout newline settings.
for p in package.rglob("*"):
    if p.is_file():
        p.write_bytes(p.read_bytes().replace(b"\r\n", b"\n"))
files = [{"path": p.relative_to(package).as_posix(), "size": p.stat().st_size, "sha256": sha(p)}
         for p in sorted(package.rglob("*")) if p.is_file() and p.name not in {"manifest.json", "SHA256SUMS"}]
write_json(package / "manifest.json", {"version": version, "status": "PROPOSED — NOT APPROVED", "files": files})
checksums = [(sha(p), p.relative_to(package).as_posix()) for p in sorted(package.rglob("*"))
             if p.is_file() and p.name != "SHA256SUMS"]
(package / "SHA256SUMS").write_text("".join(f"{h}  {p}\n" for h, p in checksums), encoding="utf-8", newline="\n")
archive = package.with_suffix(package.suffix + ".zip")
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as z:
    for p in sorted(package.rglob("*")):
        if p.is_file():
            z.write(p, p.relative_to(package))
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for p in package.rglob("*"):
        if p.is_file():
            assert z.read(p.relative_to(package).as_posix()) == p.read_bytes()
for item in files:
    assert sha(package / item["path"]) == item["sha256"]
for digest, path in checksums:
    assert sha(package / path) == digest
archive.with_suffix(archive.suffix + ".sha256").write_text(f"{sha(archive)}  {archive.name}\n", encoding="utf-8", newline="\n")
print(json.dumps({"package": str(package), "archive": str(archive), "sha256": sha(archive),
                  "testsPassed": 170, "files": len(files), "manifestAndZipVerified": True}))
