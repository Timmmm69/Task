"""Run PROD-04 gates and build the verified release package."""

from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
VERSION = "1.0.0"
NAME = f"20260909_prod04_projects_members_{VERSION}"
OUTPUT = ROOT / "outputs" / NAME
EVIDENCE = ROOT / "work/production/evidence/prod04"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
SOURCES = [
    ".project-dashboard/roadmap.json",
    "work/production/docs/PROD-04-projects-and-members.md",
    "work/production/docs/task-product-api.md",
    "work/production/src/Task.Application/ProductData/ProductApiContracts.cs",
    "work/production/src/Task.Infrastructure/Persistence/PostgresProductApiRoles.cs",
    "work/production/src/Task.Infrastructure/Persistence/PostgresProductApiStore.cs",
    "work/production/src/Task.Desktop/App.xaml.cs",
    "work/production/src/Task.Desktop/MainWindow.xaml",
    "work/production/src/Task.Desktop/MainWindow.xaml.cs",
    "work/production/src/Task.Desktop/Projects/DesktopProjectsApiClient.cs",
    "work/production/src/Task.Desktop/ViewModels/MainWindowViewModel.cs",
    "work/production/src/Task.Desktop/ViewModels/ProjectsViewModel.cs",
    "work/production/src/Task.Desktop/Views/ProjectsView.xaml",
    "work/production/src/Task.Desktop/Views/ProjectsView.xaml.cs",
    "work/production/tests/Task.Desktop.Tests/Projects/DesktopProjectsApiClientTests.cs",
    "work/production/tests/Task.Desktop.Tests/Projects/ProjectsViewModelTests.cs",
    "work/production/verification/Build-Prod04Package.py",
]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(*args: str) -> str:
    return subprocess.check_output(
        ["git", "-c", "core.quotepath=false", "-c", "core.safecrlf=false", *args], cwd=ROOT
    ).decode("utf-8").strip()


def run(command: list[str], log_name: str) -> None:
    result = subprocess.run(command, cwd=ROOT, text=True, encoding="utf-8", errors="replace",
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (EVIDENCE / log_name).write_text(result.stdout, encoding="utf-8")
    if result.returncode:
        raise RuntimeError(f"Gate failed ({result.returncode}): {' '.join(command)}")


def trx_summary(path: Path) -> dict[str, int]:
    counters = ET.parse(path).find(".//t:ResultSummary/t:Counters", NS)
    assert counters is not None, path
    values = {key: int(value) for key, value in counters.attrib.items()}
    assert values.get("failed", 0) == values.get("error", 0) == values.get("timeout", 0) == 0, values
    return values


def write_hash_inventory() -> None:
    files = [path for path in sorted(OUTPUT.rglob("*")) if path.is_file() and path.name != "SHA256SUMS"]
    (OUTPUT / "SHA256SUMS").write_text(
        "".join(f"{sha256(path)}  {path.relative_to(OUTPUT).as_posix()}\n" for path in files), encoding="utf-8")


def main() -> None:
    assert all((ROOT / path).is_file() for path in SOURCES)
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    for old in EVIDENCE.glob("*.trx"):
        old.unlink()

    run(["dotnet", "build", "work/production/Task.sln", "--no-restore"], "build.log")
    gates = [
        ("desktop-projects.trx", "work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj", "FullyQualifiedName~Projects", 8),
        ("service-project-routes.trx", "work/production/tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj", "FullyQualifiedName~ProductEndpointsTests", 208),
        ("application-product-api.trx", "work/production/tests/Task.Tests/Task.Tests.csproj", "FullyQualifiedName~PostgresProductApiTests", 15),
    ]
    summaries = {}
    for trx, project, filter_value, expected in gates:
        run(["dotnet", "test", project, "--no-build", "--filter", filter_value,
             "--results-directory", str(EVIDENCE), "--logger", f"trx;LogFileName={trx}"], trx + ".log")
        summary = trx_summary(EVIDENCE / trx)
        assert summary["passed"] == expected and summary["total"] == expected, (trx, summary)
        summaries[trx] = summary

    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    OUTPUT.mkdir(parents=True)
    for relative in SOURCES:
        target = OUTPUT / "source" / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, target)
    evidence_output = OUTPUT / "evidence"
    evidence_output.mkdir()
    for path in sorted(EVIDENCE.iterdir()):
        if path.is_file():
            shutil.copyfile(path, evidence_output / path.name)

    total = sum(summary["passed"] for summary in summaries.values())
    report = f"""# PROD-04 validation report — {VERSION}

Result: SOURCE/BUILD/TARGETED TESTS PASS. Base commit: {git('rev-parse', 'HEAD')}.

## Verified

- Solution build: PASS, zero errors.
- Desktop project client and view-model scenarios: 8/8.
- Product route policies, deny-before-store and HTTP envelope suite: 208/208.
- Product API contract/store unit suite: 15/15.
- Total focused assertions: {total} passed, zero failed.

The Windows client now loads projects, project roles, active members and linked tasks; supports
create/update/status/archive and add/change/remove member operations; applies server responses only;
and handles permissions, auth failures, validation and version conflicts. The canonical
`GET /api/v1/project-roles` endpoint is policy-protected by `Project.Read` and filters roles that a
non-administrator is not allowed to assign.

## Environment boundary

Docker Desktop and `TASK_POSTGRES_TEST_ADMIN_CONNECTION` were unavailable on this host, so a new
live PostgreSQL execution was not claimed. No schema migration was introduced: project-role reads
use the already deployed `iam.roles` and `iam.role_permissions` tables. Existing API-04 project and
member writes retain their previously packaged PostgreSQL evidence. Customer deployment, native
Windows UIA/Narrator walkthrough and an end-to-end server session remain deployment acceptance,
not claims of this source package.
"""
    (OUTPUT / "validation-report.md").write_text(report, encoding="utf-8")
    (OUTPUT / "VERSION").write_text(VERSION + "\n", encoding="utf-8")
    payload = [
        {"path": path.relative_to(OUTPUT).as_posix(), "size": path.stat().st_size, "sha256": sha256(path)}
        for path in sorted(OUTPUT.rglob("*")) if path.is_file() and path.name not in {"manifest.json", "SHA256SUMS"}
    ]
    manifest = {
        "package": NAME, "version": VERSION, "base_commit": git("rev-parse", "HEAD"),
        "task": "PROD-04", "release_status": "source_validated_deployment_acceptance_required",
        "focused_tests_passed": total, "focused_tests_failed": 0,
        "postgresql_runtime_executed": False, "sources": SOURCES, "files": payload,
    }
    (OUTPUT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    write_hash_inventory()
    for item in payload:
        assert sha256(OUTPUT / item["path"]) == item["sha256"]

    archive = OUTPUT.parent / f"{OUTPUT.name}.zip"
    if archive.exists():
        archive.unlink()
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(OUTPUT.rglob("*")):
            if path.is_file():
                bundle.write(path, path.relative_to(OUTPUT).as_posix())
    with zipfile.ZipFile(archive) as bundle:
        assert bundle.testzip() is None
        for item in payload:
            assert hashlib.sha256(bundle.read(item["path"])).hexdigest() == item["sha256"]
    digest = sha256(archive)
    archive.with_suffix(".zip.sha256").write_text(f"{digest}  {archive.name}\n", encoding="utf-8")
    print(json.dumps({"package": str(archive), "sha256": digest, "tests": total}))


if __name__ == "__main__":
    main()
