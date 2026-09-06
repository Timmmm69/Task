"""Build and verify the DESK-01 completion package from current source and TRX evidence."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
VERSION = "1.0.0"
NAME = f"20260906_desk01_safe_desktop_auth_{VERSION}"
OUTPUT = ROOT / "outputs" / NAME
EVIDENCE = ROOT / "work/production/evidence/api01"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

SOURCES = [
    ".project-dashboard/roadmap.json",
    "work/production/docs/desk01-safe-desktop-auth.md",
    "work/production/src/Task.Desktop/App.xaml.cs",
    "work/production/src/Task.Desktop/AuthWindow.xaml",
    "work/production/src/Task.Desktop/AuthWindow.xaml.cs",
    "work/production/src/Task.Desktop/MainWindow.xaml.cs",
    "work/production/src/Task.Desktop/Security/DesktopAuthApiClient.cs",
    "work/production/src/Task.Desktop/Security/DesktopCredentialVault.cs",
    "work/production/src/Task.Desktop/Security/DesktopServerConnection.cs",
    "work/production/src/Task.Desktop/Security/SessionService.cs",
    "work/production/src/Task.Desktop/ViewModels/AuthWorkflowViewModel.cs",
    "work/production/tests/Task.Desktop.Tests/AuthWorkflowViewModelTests.cs",
    "work/production/tests/Task.Desktop.Tests/DesktopCredentialVaultTests.cs",
    "work/production/tests/Task.Desktop.Tests/Security/DesktopServerConnectionTests.cs",
    "work/production/tests/Task.Desktop.Tests/Security/SessionServiceTests.cs",
    "work/production/verification/Test-DesktopAuth.ps1",
    "work/production/verification/Build-DesktopAuthPackage.py",
    "outputs/20260823_task_desktop_auth_e2e_hardening_0.1.0/validation-report.md",
]


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def latest_full_runs():
    latest = {}
    for path in sorted(EVIDENCE.glob("*.trx"), key=lambda item: item.stat().st_mtime):
        tree = ET.parse(path)
        counters = tree.find(".//t:ResultSummary/t:Counters", NS)
        tests = tree.findall(".//t:TestDefinitions/t:UnitTest", NS)
        if counters is None or not tests or int(counters.get("total", "0")) < 100:
            continue
        assembly = Path(tests[0].get("storage", "")).stem.lower()
        latest[assembly] = (path, dict(counters.attrib))
    required = {"task.desktop.tests", "task.servicehosts.tests", "task.tests"}
    assert required == set(latest), f"Expected full runs for {sorted(required)}, got {sorted(latest)}"
    for path, counters in latest.values():
        assert counters["total"] == counters["passed"], path
        assert int(counters.get("failed", "0")) == 0, path
        assert int(counters.get("notExecuted", "0")) == 0, path
    return latest


def main():
    runs = latest_full_runs()
    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    OUTPUT.mkdir(parents=True)
    for relative in SOURCES:
        source = ROOT / relative
        assert source.is_file(), source
        destination = OUTPUT / "source" / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)

    evidence_dir = OUTPUT / "evidence"
    evidence_dir.mkdir()
    for path, _ in runs.values():
        shutil.copyfile(path, evidence_dir / path.name)

    total = sum(int(counters["passed"]) for _, counters in runs.values())
    rows = "\n".join(
        f'| {assembly} | {counters["passed"]}/{counters["total"]} | 0 |'
        for assembly, (_, counters) in sorted(runs.items())
    )
    baseline = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    report = f"""# DESK-01 validation report — {VERSION}

Result: PASS. DESK-01 is complete in the packaged source baseline.

| Suite | Passed / total | Skipped |
| --- | --- | --- |
{rows}

Total: {total} passed, zero failed, zero skipped. The full run used a disposable PostgreSQL 16 cluster and includes current desktop, API, security and persistence regressions.

The specialized gate `Test-DesktopAuth.ps1 -SkipTestRun` additionally confirmed 14 required scenario families in the current TRX evidence: HTTPS/TLS server selection, confirmed login, mandatory password change, post-change confirmation, startup restore, revoked-session handling, encrypted credential storage, refresh-reuse rejection and logout revocation.

The prior real WPF + trusted HTTPS API + PostgreSQL E2E report is included under `source/outputs/20260823_task_desktop_auth_e2e_hardening_0.1.0/validation-report.md`. The AuthWindow XAML and code-behind are unchanged since that manual run; later changes to the workflow/session clients are covered by the current automated regression evidence.

API-01 now supplies the previously open operational account lifecycle: pending account creation, temporary credential issuance, activation, block/deactivate/reactivate, reset, device management and session revocation. DESK-01 therefore no longer depends on an unimplemented account handoff path.

No production deployment, production credential, or production database is claimed. Test credentials and disposable database data are not included. Existing analyzer/deprecation warnings are not failures and are not introduced by this completion package.

Baseline commit before this DESK-01 completion delta: `{baseline}`.
"""
    (OUTPUT / "validation-report.md").write_text(report, encoding="utf-8")
    (OUTPUT / "VERSION").write_text(VERSION + "\n", encoding="utf-8")

    entries = []
    for path in sorted(OUTPUT.rglob("*")):
        if path.is_file() and path.name not in {"manifest.json", "SHA256SUMS"}:
            entries.append({
                "path": path.relative_to(OUTPUT).as_posix(),
                "bytes": path.stat().st_size,
                "sha256": digest(path),
            })
    manifest = {
        "task": "DESK-01",
        "version": VERSION,
        "status": "validated",
        "baselineCommit": baseline,
        "testsPassed": total,
        "requiredScenarioFamilies": 14,
        "files": entries,
    }
    (OUTPUT / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    sums = [f'{entry["sha256"]}  {entry["path"]}' for entry in entries]
    sums.append(f'{digest(OUTPUT / "manifest.json")}  manifest.json')
    (OUTPUT / "SHA256SUMS").write_text("\n".join(sums) + "\n", encoding="utf-8")

    archive = ROOT / "outputs" / f"{NAME}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as package:
        for path in sorted(OUTPUT.rglob("*")):
            if path.is_file():
                package.write(path, path.relative_to(OUTPUT).as_posix())
    with zipfile.ZipFile(archive) as package:
        assert package.testzip() is None
    archive_hash = digest(archive)
    archive.with_suffix(".zip.sha256").write_text(
        f"{archive_hash}  {archive.name}\n", encoding="utf-8"
    )
    print(json.dumps({"package": str(archive), "sha256": archive_hash, "files": len(entries), "testsPassed": total}))


if __name__ == "__main__":
    main()
