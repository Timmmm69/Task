"""Run API-05 gates and build the verified source package."""

from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
VERSION = "1.0.0"
NAME = f"20260912_api05_audit_capabilities_{VERSION}"
OUTPUT = ROOT / "outputs" / NAME
EVIDENCE = ROOT / "work/production/evidence/api05"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
SOURCES = [
    ".project-dashboard/roadmap.json",
    "work/production/docs/API-05-audit-capabilities.md",
    "work/production/src/Task.Application/Audit/IAuditEntryStore.cs",
    "work/production/src/Task.Application/System/ServerCapabilitiesService.cs",
    "work/production/src/Task.Infrastructure/Postgres/PostgresAuditEntryStore.cs",
    "work/production/src/Task.Api/Audit/AuditEndpoints.cs",
    "work/production/src/Task.Api/Capabilities/CapabilitiesEndpoints.cs",
    "work/production/src/Task.Api/Capabilities/ClientVersionCompatibilityMiddleware.cs",
    "work/production/src/Task.Api/Server/SystemVersionEndpoints.cs",
    "work/production/src/Task.Api/Security/TaskPermissionAuthorization.cs",
    "work/production/src/Task.Api/Program.cs",
    "work/production/tests/Task.Tests/System/ServerCapabilitiesServiceTests.cs",
    "work/production/tests/Task.Tests/Postgres/PostgresAuditEntryStoreTests.cs",
    "work/production/tests/Task.ServiceHosts.Tests/AuditEndpointsTests.cs",
    "work/production/tests/Task.ServiceHosts.Tests/CapabilitiesEndpointsTests.cs",
    "work/production/tests/Task.ServiceHosts.Tests/SystemVersionEndpointsTests.cs",
    "work/production/tests/Task.ServiceHosts.Tests/ClientVersionCompatibilityMiddlewareTests.cs",
    "work/production/verification/Build-Api05Package.py",
]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(command: list[str], log_name: str) -> None:
    result = subprocess.run(command, cwd=ROOT, text=True, encoding="utf-8", errors="replace",
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (EVIDENCE / log_name).write_text(result.stdout, encoding="utf-8")
    if result.returncode:
        raise RuntimeError(f"Gate failed: {' '.join(command)}")


def summary(path: Path) -> dict[str, int]:
    counters = ET.parse(path).find(".//t:ResultSummary/t:Counters", NS)
    assert counters is not None
    values = {key: int(value) for key, value in counters.attrib.items()}
    assert values.get("failed", 0) == values.get("error", 0) == values.get("timeout", 0) == 0
    assert values.get("passed", 0) > 0
    return values


def main() -> None:
    assert all((ROOT / path).is_file() for path in SOURCES)
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    run(["dotnet", "build", "work/production/Task.sln", "--no-restore", "-c", "Release"], "build.log")
    gates = [
        ("service-audit-capabilities.trx", "work/production/tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj",
         "FullyQualifiedName~AuditEndpointsTests|FullyQualifiedName~CapabilitiesEndpointsTests|FullyQualifiedName~SystemVersionEndpointsTests|FullyQualifiedName~ClientVersionCompatibilityMiddlewareTests"),
        ("application-capabilities.trx", "work/production/tests/Task.Tests/Task.Tests.csproj",
         "FullyQualifiedName~ServerCapabilitiesServiceTests"),
        ("store-audit.trx", "work/production/tests/Task.Tests/Task.Tests.csproj",
         "FullyQualifiedName~PostgresAuditEntryStoreTests"),
    ]
    summaries = {}
    for trx, project, filter_value in gates:
        run(["dotnet", "test", project, "--no-build", "-c", "Release", "--filter", filter_value,
             "--results-directory", str(EVIDENCE), "--logger", f"trx;LogFileName={trx}"], trx + ".log")
        summaries[trx] = summary(EVIDENCE / trx)

    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    OUTPUT.mkdir(parents=True)
    for relative in SOURCES:
        target = OUTPUT / "source" / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, target)
    evidence = OUTPUT / "evidence"
    evidence.mkdir()
    for path in sorted(EVIDENCE.iterdir()):
        if path.is_file():
            shutil.copyfile(path, evidence / path.name)
    total = sum(item["passed"] for item in summaries.values())
    live_postgres = bool(os.environ.get("TASK_POSTGRES_TEST_ADMIN_CONNECTION"))
    postgres_line = (
        "PostgreSQL-backed audit store suite executed against a live disposable PostgreSQL 16 database"
        if live_postgres
        else "The PostgreSQL-backed audit store suite reports NOT RUN without TASK_POSTGRES_TEST_ADMIN_CONNECTION; "
             "its non-runtime contract assertions still pass"
    )
    report = f"""# API-05 validation report — {VERSION}

Result: SOURCE/BUILD/TARGETED TESTS PASS.

- Release solution build: PASS, zero errors.
- Focused suites: {total} passed, zero failed.
- Audit read surface: actor/object filters, objectId/objectType readback, exact safe field set
  (no metadata/oldState/newState leak), malformed actor/object 422.
- Capabilities: canonical ServerCapabilities shape, admin/server-capabilities authorization
  (403 without organization.manage), no operational detail in the payload.
- system/version: authenticated canonical SystemVersion, only public fields.
- Compatibility middleware and ServerCapabilitiesService regressions pass.
- {postgres_line}.

No migration was added. Customer live PostgreSQL/API smoke and real deployment
remain deployment acceptance.
"""
    (OUTPUT / "validation-report.md").write_text(report, encoding="utf-8")
    (OUTPUT / "VERSION").write_text(VERSION + "\n", encoding="utf-8")
    files = [{"path": p.relative_to(OUTPUT).as_posix(), "size": p.stat().st_size, "sha256": sha256(p)}
             for p in sorted(OUTPUT.rglob("*")) if p.is_file()]
    manifest = {"package": NAME, "version": VERSION, "task": "API-05",
                "release_status": "source_validated_deployment_acceptance_required",
                "focused_tests_passed": total, "focused_tests_failed": 0, "files": files}
    (OUTPUT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    inventory = [p for p in sorted(OUTPUT.rglob("*")) if p.is_file() and p.name != "SHA256SUMS"]
    (OUTPUT / "SHA256SUMS").write_text("".join(f"{sha256(p)}  {p.relative_to(OUTPUT).as_posix()}\n" for p in inventory), encoding="utf-8")
    archive = OUTPUT.parent / f"{OUTPUT.name}.zip"
    if archive.exists():
        archive.unlink()
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(OUTPUT.rglob("*")):
            if path.is_file():
                bundle.write(path, path.relative_to(OUTPUT).as_posix())
    with zipfile.ZipFile(archive) as bundle:
        assert bundle.testzip() is None
    digest = sha256(archive)
    archive.with_suffix(".zip.sha256").write_text(f"{digest}  {archive.name}\n", encoding="utf-8")
    print(json.dumps({"package": str(archive), "sha256": digest, "tests": total}))


if __name__ == "__main__":
    main()
