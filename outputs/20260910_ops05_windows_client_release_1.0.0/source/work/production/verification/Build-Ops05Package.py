"""Build and verify the OPS-05 signed Windows client release package."""

from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
VERSION = "1.0.0"
NAME = f"20260910_ops05_windows_client_release_{VERSION}"
OUTPUT = ROOT / "outputs" / NAME
EVIDENCE = ROOT / "work/production/evidence/ops05"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
SOURCES = [
    ".project-dashboard/roadmap.json",
    "work/production/deployment/desktop/Build-TaskDesktopRelease.ps1",
    "work/production/deployment/desktop/Install-TaskDesktop.ps1",
    "work/production/deployment/desktop/Invoke-TaskDesktopUpdate.ps1",
    "work/production/deployment/desktop/Rollback-TaskDesktop.ps1",
    "work/production/deployment/desktop/TaskDesktop.Release.psm1",
    "work/production/deployment/desktop/ops05.parameters.example.json",
    "work/production/docs/OPS-05-windows-client-release.md",
    "work/production/src/Task.Api/Capabilities/ClientVersionCompatibilityMiddleware.cs",
    "work/production/src/Task.Api/Program.cs",
    "work/production/src/Task.Api/appsettings.json",
    "work/production/src/Task.Application/System/ServerCapabilitiesService.cs",
    "work/production/src/Task.Application/packages.lock.json",
    "work/production/src/Task.Desktop/App.xaml.cs",
    "work/production/src/Task.Desktop/Security/DesktopAuthApiClient.cs",
    "work/production/src/Task.Desktop/Security/SessionService.cs",
    "work/production/src/Task.Desktop/Task.Desktop.csproj",
    "work/production/src/Task.Desktop/ViewModels/AuthWorkflowViewModel.cs",
    "work/production/src/Task.Domain/packages.lock.json",
    "work/production/tests/Task.Desktop.Tests/Security/DesktopAuthApiClientTests.cs",
    "work/production/tests/Task.ServiceHosts.Tests/ClientVersionCompatibilityMiddlewareTests.cs",
    "work/production/tests/Task.Tests/System/ServerCapabilitiesServiceTests.cs",
    "work/production/verification/Build-Ops05Package.py",
    "work/production/verification/Test-Ops05WindowsRelease.ps1",
]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(*args: str) -> str:
    return subprocess.check_output(
        ["git", "-c", "core.quotepath=false", "-c", "core.safecrlf=false", *args], cwd=ROOT
    ).decode("utf-8").strip()


def trx_results() -> tuple[list[dict], int, int]:
    rows, passed, skipped = [], 0, 0
    for path in sorted((EVIDENCE / "full-release").glob("*.trx")):
        root = ET.parse(path).getroot()
        counters = root.find(".//t:ResultSummary/t:Counters", NS)
        definition = root.find(".//t:TestDefinitions/t:UnitTest", NS)
        assert counters is not None and definition is not None, path
        values = dict(counters.attrib)
        assert values["failed"] == values["error"] == values["timeout"] == "0", path
        row_passed = int(values["passed"])
        row_skipped = int(values["total"]) - int(values["executed"])
        assembly = Path(definition.get("storage", "")).stem
        rows.append({"assembly": assembly, "passed": row_passed, "skipped": row_skipped, "file": path.name})
        passed += row_passed
        skipped += row_skipped
    assert len(rows) == 3 and passed == 1668 and skipped == 4, (rows, passed, skipped)
    return rows, passed, skipped


def main() -> None:
    assert all((ROOT / path).is_file() for path in SOURCES)
    acceptance = json.loads((EVIDENCE / "acceptance.json").read_text(encoding="utf-8"))
    assert acceptance["task"] == "OPS-05" and acceptance["result"] == "PASS"
    assert all(acceptance["scenarios"].values())
    rows, passed, skipped = trx_results()
    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    for relative in SOURCES:
        destination = OUTPUT / "source" / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, destination)
    evidence_output = OUTPUT / "evidence"
    evidence_output.mkdir(parents=True)
    shutil.copyfile(EVIDENCE / "acceptance.json", evidence_output / "acceptance.json")
    for row in rows:
        shutil.copyfile(EVIDENCE / "full-release" / row["file"], evidence_output / row["file"])
    (OUTPUT / "VERSION").write_text(VERSION + "\n", encoding="utf-8")
    report = f"""# OPS-05 validation report — {VERSION}

Result: SOURCE / SIGNED WINDOWS ACCEPTANCE PASS. Base commit: {git('rev-parse', 'HEAD')}.

The Windows x64 gate built self-contained 1.0.0 and 1.1.0 clients with an ephemeral in-memory
code-signing publisher. It verified Authenticode publisher pins, detached signatures, every SHA-256,
idempotent initial install, mandatory update overriding a 0% rollout, rejection of a local channel
without the explicit acceptance switch, tamper rejection, direct-downgrade rejection and atomic
rollback to the retained validated release. No test certificate was written to a certificate store.

The full Release solution passed {passed} tests with zero failures; {skipped} PostgreSQL integration
tests were skipped because TASK_TEST_POSTGRES is not configured. Assemblies: {json.dumps(rows)}.

Production boundary: the repository contains no private signing key and no customer URL. The final
operator must use the corporate code-signing certificate and RFC 3161 timestamp service, publish the
ZIP and signed channel on a controlled HTTPS origin, distribute OS trust through company PKI and
retain pilot-PC antivirus/launch/update/restart/rollback evidence as described in the runbook.
"""
    (OUTPUT / "validation-report.md").write_text(report, encoding="utf-8")
    payload = [
        {"path": path.relative_to(OUTPUT).as_posix(), "size": path.stat().st_size, "sha256": sha256(path)}
        for path in sorted(OUTPUT.rglob("*")) if path.is_file() and path.name not in {"manifest.json", "SHA256SUMS"}
    ]
    manifest = {
        "package": NAME, "version": VERSION, "base_commit": git("rev-parse", "HEAD"),
        "release_status": "source_and_windows_acceptance_validated_production_certificate_external",
        "tests_passed": passed, "tests_skipped": skipped, "acceptance": acceptance,
        "sources": SOURCES, "files": payload,
    }
    (OUTPUT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    inventory = [path for path in sorted(OUTPUT.rglob("*")) if path.is_file() and path.name != "SHA256SUMS"]
    (OUTPUT / "SHA256SUMS").write_text("".join(f"{sha256(path)}  {path.relative_to(OUTPUT).as_posix()}\n" for path in inventory), encoding="utf-8")
    archive = OUTPUT.parent / f"{OUTPUT.name}.zip"
    if archive.exists(): archive.unlink()
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(OUTPUT.rglob("*")):
            if path.is_file(): bundle.write(path, path.relative_to(OUTPUT).as_posix())
    with zipfile.ZipFile(archive) as bundle:
        assert bundle.testzip() is None
        assert set(bundle.namelist()) == {path.relative_to(OUTPUT).as_posix() for path in OUTPUT.rglob("*") if path.is_file()}
    archive.with_suffix(".zip.sha256").write_text(f"{sha256(archive)}  {archive.name}\n", encoding="utf-8")
    print(json.dumps({"package": str(archive), "sha256": sha256(archive), "tests": passed}))


if __name__ == "__main__":
    main()
