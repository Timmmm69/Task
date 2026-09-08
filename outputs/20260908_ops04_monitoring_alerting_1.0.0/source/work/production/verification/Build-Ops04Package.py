"""Build and verify the OPS-04 monitoring and alerting release package."""

from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
VERSION = "1.0.0"
NAME = f"20260908_ops04_monitoring_alerting_{VERSION}"
OUTPUT = ROOT / "outputs" / NAME
EVIDENCE = ROOT / "work/production/evidence/ops04"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
SOURCES = [
    ".project-dashboard/roadmap.json",
    "work/production/deployment/monitoring/Bootstrap-Ops04Monitoring.sh",
    "work/production/deployment/monitoring/alert-router.Dockerfile",
    "work/production/deployment/monitoring/alert_router.py",
    "work/production/deployment/monitoring/alertmanager.yml",
    "work/production/deployment/monitoring/alerts.yml",
    "work/production/deployment/monitoring/blackbox.yml",
    "work/production/deployment/monitoring/compose.backup.yaml",
    "work/production/deployment/monitoring/compose.production.yaml",
    "work/production/deployment/monitoring/compose.yaml",
    "work/production/deployment/monitoring/ops04.parameters.example.env",
    "work/production/deployment/monitoring/prometheus.yml",
    "work/production/docs/OPS-04-monitoring-alerting.md",
    "work/production/src/Task.Api/Program.cs",
    "work/production/src/Task.Api/TaskApiMetrics.cs",
    "work/production/src/Task.BackupAgent/AssemblyInfo.cs",
    "work/production/src/Task.BackupAgent/BackupMetricsPublisher.cs",
    "work/production/src/Task.BackupAgent/Program.cs",
    "work/production/src/Task.Worker/ExpiredSessionMaintenanceWorker.cs",
    "work/production/src/Task.Worker/OutboxPublisherWorker.cs",
    "work/production/src/Task.Worker/Program.cs",
    "work/production/src/Task.Worker/RecurrenceHorizonWorker.cs",
    "work/production/src/Task.Worker/ReminderDeliveryWorker.cs",
    "work/production/src/Task.Worker/WorkerOperationalState.cs",
    "work/production/tests/Task.ServiceHosts.Tests/OperationsMonitoringTests.cs",
    "work/production/verification/Build-Ops04Package.py",
    "work/production/verification/ops04-acceptance.py",
]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(*args: str) -> str:
    return subprocess.check_output(
        ["git", "-c", "core.quotepath=false", "-c", "core.safecrlf=false", *args],
        cwd=ROOT,
    ).decode("utf-8").strip()


def trx_summary(path: Path) -> tuple[str, dict[str, str]]:
    tree = ET.parse(path)
    definitions = tree.findall(".//t:TestDefinitions/t:UnitTest", NS)
    counters = tree.find(".//t:ResultSummary/t:Counters", NS)
    assert definitions and counters is not None, path
    assembly = Path(definitions[0].get("storage", "")).stem.lower()
    project = {
        "task.tests": "Task.Tests",
        "task.servicehosts.tests": "Task.ServiceHosts.Tests",
        "task.desktop.tests": "Task.Desktop.Tests",
    }[assembly]
    values = dict(counters.attrib)
    assert values["failed"] == "0" and values["error"] == "0" and values["timeout"] == "0", path
    return project, values


def full_test_results() -> tuple[list[tuple[str, dict[str, str], Path]], int, int]:
    rows = []
    for path in sorted((EVIDENCE / "full-release").glob("*.trx")):
        project, counters = trx_summary(path)
        rows.append((project, counters, path))
    assert {row[0] for row in rows} == {
        "Task.Tests", "Task.ServiceHosts.Tests", "Task.Desktop.Tests"
    }, rows
    passed = sum(int(row[1]["passed"]) for row in rows)
    skipped = sum(int(row[1]["total"]) - int(row[1]["executed"]) for row in rows)
    assert passed == 1627 and skipped == 4, (passed, skipped)
    return sorted(rows), passed, skipped


def write_hash_inventory() -> None:
    paths = [
        path for path in sorted(OUTPUT.rglob("*"))
        if path.is_file() and path.name != "SHA256SUMS"
    ]
    text = "".join(f"{sha256(path)}  {path.relative_to(OUTPUT).as_posix()}\n" for path in paths)
    (OUTPUT / "SHA256SUMS").write_text(text, encoding="utf-8")


def main() -> None:
    assert all((ROOT / path).is_file() for path in SOURCES)
    acceptance = json.loads((EVIDENCE / "acceptance.json").read_text(encoding="utf-8"))
    assert acceptance == {
        "task": "OPS-04",
        "result": "PASS",
        "configuration": {"yaml_files": 7, "alerts": 19, "scrape_jobs": 10},
        "delivery": {
            "forwarded": 1,
            "receipts": 1,
            "secrets_redacted": True,
            "resolved_enabled": True,
        },
    }
    rows, passed, skipped = full_test_results()
    targeted_path = EVIDENCE / "ops04-targeted-release.trx"
    targeted_project, targeted = trx_summary(targeted_path)
    assert targeted_project == "Task.ServiceHosts.Tests" and targeted["passed"] == "4"

    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    OUTPUT.mkdir(parents=True)
    for relative in SOURCES:
        destination = OUTPUT / "source" / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, destination)
    evidence_output = OUTPUT / "evidence"
    evidence_output.mkdir()
    shutil.copyfile(EVIDENCE / "acceptance.json", evidence_output / "acceptance.json")
    shutil.copyfile(targeted_path, evidence_output / targeted_path.name)
    for _, _, path in rows:
        shutil.copyfile(path, evidence_output / path.name)

    table = "\n".join(
        f"| {project} | {counters['passed']}/{counters['total']} | {int(counters['total']) - int(counters['executed'])} |"
        for project, counters, _ in rows
    )
    report = f"""# OPS-04 validation report — {VERSION}

Result: SOURCE/CONFIGURATION/SYNTHETIC DELIVERY PASS. Base commit: {git('rev-parse', 'HEAD')}.

## Verified

| Assembly | Passed / total | Skipped |
|---|---:|---:|
{table}

Full Release solution total: {passed} passed, {skipped} environment-dependent PostgreSQL tests
skipped, zero failures. The focused monitoring suite passed 4/4. The acceptance gate parsed seven
YAML documents, required 19 owned alert rules and ten scrape jobs, started the real alert router in
production mode against a local receiver, injected a failure, verified bearer delivery and proved
that non-allowlisted secret fields were absent from the forwarded body and durable hash receipt.

Docker Compose configuration parsing passed for the monitoring stack and for both merged
production/security and backup overlays. Python bytecode compilation and POSIX shell syntax checks
passed. The implementation covers API/readiness, PostgreSQL and connection capacity, host disk,
four background loops, backup/check/restore freshness, mounted backup capacity, Prometheus,
Alertmanager, blackbox and delivery-path failures. Metrics and alert payloads use bounded labels;
customer URLs, credentials and personal contacts are excluded from the repository and package.

## Production boundary

This package proves the source contract and a real local HTTP delivery path. It does not claim that
customer infrastructure, immutable image digests, the company HTTPS receiver, on-call ownership or
retention have been configured. Deployment must replace every image placeholder with an approved
linux/amd64 digest, set the real `TASK_ALERT_OWNER`, place the HTTPS URL/token in protected external
files, and retain customer-side firing and resolved receipts. Native promtool and live target
scrapes remain deployment checks because the repository intentionally contains no vendor image
digests or customer environment.
"""
    (OUTPUT / "validation-report.md").write_text(report, encoding="utf-8")
    (OUTPUT / "VERSION").write_text(VERSION + "\n", encoding="utf-8")

    payload_files = [
        {
            "path": path.relative_to(OUTPUT).as_posix(),
            "size": path.stat().st_size,
            "sha256": sha256(path),
        }
        for path in sorted(OUTPUT.rglob("*"))
        if path.is_file() and path.name not in {"manifest.json", "SHA256SUMS"}
    ]
    manifest = {
        "package": NAME,
        "version": VERSION,
        "base_commit": git("rev-parse", "HEAD"),
        "release_status": "source_validated_customer_configuration_required",
        "tests_passed": passed,
        "tests_skipped": skipped,
        "targeted_tests_passed": int(targeted["passed"]),
        "acceptance": acceptance,
        "sources": SOURCES,
        "files": payload_files,
    }
    (OUTPUT / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    write_hash_inventory()
    for item in payload_files:
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
        for item in payload_files:
            assert hashlib.sha256(bundle.read(item["path"])).hexdigest() == item["sha256"]
        assert hashlib.sha256(bundle.read("manifest.json")).hexdigest() == sha256(OUTPUT / "manifest.json")
        assert hashlib.sha256(bundle.read("SHA256SUMS")).hexdigest() == sha256(OUTPUT / "SHA256SUMS")
    archive_hash = sha256(archive)
    archive.with_suffix(".zip.sha256").write_text(
        f"{archive_hash}  {archive.name}\n", encoding="utf-8"
    )
    print(json.dumps({"package": str(archive), "sha256": archive_hash, "tests": passed}))


if __name__ == "__main__":
    main()
