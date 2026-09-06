"""Build the SEC-03 source/evidence package after its contract and solution tests pass."""

from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
VERSION = "1.0.0"
NAME = f"20260907_sec03_production_secrets_tls_{VERSION}"
OUTPUT = ROOT / "outputs" / NAME
EVIDENCE = ROOT / "work/production/evidence/sec03"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
SOURCES = [
    ".github/workflows/ci.yml",
    ".gitignore",
    ".project-dashboard/roadmap.json",
    "work/production/deployment/security/New-TaskTlsCertificateRequest.ps1",
    "work/production/deployment/security/compose.production.yaml",
    "work/production/deployment/security/nginx.conf",
    "work/production/deployment/security/postgresql.pg_hba.conf",
    "work/production/deployment/security/production.env.example",
    "work/production/docs/SEC-03-production-secrets-tls.md",
    "work/production/verification/Build-Sec03Package.py",
    "work/production/verification/Test-ProductionSecretsTls.Contract.ps1",
    "work/production/verification/Test-ProductionSecretsTls.ps1",
]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(*args: str) -> str:
    return subprocess.check_output(
        ["git", "-c", "core.quotepath=false", "-c", "core.safecrlf=false", *args],
        cwd=ROOT,
    ).decode("utf-8").strip()


def skipped_count(counters: dict[str, str]) -> int:
    """VSTest may leave notExecuted at zero for skipped xUnit tests."""
    return max(
        int(counters.get("notExecuted", "0")),
        int(counters["total"]) - int(counters["executed"]),
    )


def test_results() -> tuple[list[tuple[str, dict[str, str], Path]], int, int]:
    latest: dict[str, tuple[dict[str, str], Path]] = {}
    for path in sorted(EVIDENCE.glob("*.trx"), key=lambda item: item.stat().st_mtime):
        tree = ET.parse(path)
        tests = tree.findall(".//t:TestDefinitions/t:UnitTest", NS)
        counters = tree.find(".//t:ResultSummary/t:Counters", NS)
        if not tests or counters is None:
            continue
        assembly = Path(tests[0].get("storage", "")).stem.lower()
        project = {
            "task.tests": "Task.Tests",
            "task.servicehosts.tests": "Task.ServiceHosts.Tests",
            "task.desktop.tests": "Task.Desktop.Tests",
        }.get(assembly)
        if project:
            latest[project] = (dict(counters.attrib), path)
    assert set(latest) == {"Task.Tests", "Task.ServiceHosts.Tests", "Task.Desktop.Tests"}, latest
    rows = []
    passed = skipped = 0
    for project, (counters, path) in sorted(latest.items()):
        assert counters["failed"] == "0" and counters["error"] == "0" and counters["timeout"] == "0", path
        passed += int(counters["passed"])
        skipped += skipped_count(counters)
        rows.append((project, counters, path))
    return rows, passed, skipped


def main() -> None:
    assert all((ROOT / path).is_file() for path in SOURCES)
    checks = json.loads((EVIDENCE / "checks.json").read_text(encoding="utf-8-sig"))
    required_checks = {
        "compose_secret_and_network_contract",
        "edge_tls_policy",
        "database_tls_policy",
        "repository_secret_boundary",
        "immutable_deployment_environment",
        "external_secret_bundle",
        "certificate_chain_hostname_key_and_lifetime",
    }
    assert required_checks == {key for key, value in checks["checks"].items() if value is True}, checks
    rows, passed, skipped = test_results()

    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    OUTPUT.mkdir(parents=True)
    for relative in SOURCES:
        destination = OUTPUT / "source" / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, destination)
    (OUTPUT / "evidence").mkdir()
    shutil.copyfile(EVIDENCE / "checks.json", OUTPUT / "evidence/checks.json")
    for _, _, path in rows:
        shutil.copyfile(path, OUTPUT / "evidence" / path.name)

    table = "\n".join(
        f"| {project} | {counters['passed']}/{counters['total']} | {skipped_count(counters)} |"
        for project, counters, _ in rows
    )
    report = f"""# SEC-03 validation report — {VERSION}

Result: SOURCE/CONTRACT PASS. Base commit: {git('rev-parse', 'HEAD')}.

## Verified

| Assembly | Passed / total | Skipped |
|---|---:|---:|
{table}

Total: {passed} passed, {skipped} skipped. The SEC-03 executable contract also generated an
ephemeral CA, edge certificate, PostgreSQL certificate and JWT P-256 key ring outside the
repository; it accepted the complete valid bundle and rejected a DNS-name mismatch. The checked
topology exposes only the TLS proxy, keeps API/database networks internal, passes credentials via
owner-readable files, uses Npgsql VerifyFull and rejects non-TLS PostgreSQL traffic. Docker Compose
configuration parsing and dashboard validation passed. No private key or reusable credential is
included in this package.

## Validation limitations

The repository-wide `dotnet format whitespace --verify-no-changes` gate still reports formatting
differences in pre-existing C# files outside this SEC-03 change; no C# source was modified here.
The Docker daemon and a customer-like endpoint were unavailable, so Compose configuration was
parsed but the containers and live network path were not exercised locally.

## Production sign-off boundary

This package implements the source, CI gate, CSR tooling and operations runbook. It does not claim
that a customer environment, corporate CA, company secrets manager, host ACL or firewall has been
configured. SEC-03 remains a hard release blocker until `Test-ProductionSecretsTls.ps1` passes with
the real `SecretRoot` and trusted live `Endpoint`, and the protected evidence also contains the
firewall/port scan and rotation rehearsal required by the runbook. The certificate metadata in
this package is explicitly ephemeral test evidence, not a production certificate.
"""
    (OUTPUT / "validation-report.md").write_text(report, encoding="utf-8")
    (OUTPUT / "VERSION").write_text(VERSION + "\n", encoding="utf-8")
    files = [
        {
            "path": path.relative_to(OUTPUT).as_posix(),
            "size": path.stat().st_size,
            "sha256": sha256(path),
        }
        for path in sorted(OUTPUT.rglob("*"))
        if path.is_file() and path.name != "manifest.json"
    ]
    manifest = {
        "package": NAME,
        "version": VERSION,
        "base_commit": git("rev-parse", "HEAD"),
        "release_status": "blocked_pending_real_deployment_evidence",
        "tests_passed": passed,
        "tests_skipped": skipped,
        "sources": SOURCES,
        "files": files,
    }
    (OUTPUT / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    for item in files:
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
        for item in files:
            assert hashlib.sha256(bundle.read(item["path"])).hexdigest() == item["sha256"]
    archive.with_suffix(".zip.sha256").write_text(
        f"{sha256(archive)}  {archive.name}\n", encoding="utf-8"
    )
    print(json.dumps({"package": str(archive), "sha256": sha256(archive), "tests": passed}))


if __name__ == "__main__":
    main()
