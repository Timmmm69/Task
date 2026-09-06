"""Build and cryptographically verify the SEC-05 independent security review package."""

from __future__ import annotations

from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[3]
PRODUCTION = ROOT / "work" / "production"
EVIDENCE = PRODUCTION / "evidence" / "sec05"
VERSION = "1.0.0"
NAME = f"20260906_sec05_independent_security_review_{VERSION}"
OUTPUT = ROOT / "outputs" / NAME
TRX_NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
SOURCES = [
    ".project-dashboard/roadmap.json",
    "work/production/docs/SEC-05-threat-model.md",
    "work/production/docs/SEC-05-independent-security-review.md",
    "work/production/src/Task.Api/Auth/AuthEndpoints.cs",
    "work/production/src/Task.Api/Auth/LoginAbuseProtector.cs",
    "work/production/src/Task.Api/Program.cs",
    "work/production/src/Task.Api/Task.Api.csproj",
    "work/production/src/Task.Application/Security/LoginRateLimiter.cs",
    "work/production/src/Task.Application/Task.Application.csproj",
    "work/production/src/Task.BackupAgent/Task.BackupAgent.csproj",
    "work/production/src/Task.DatabaseMigrator/Task.DatabaseMigrator.csproj",
    "work/production/src/Task.Domain/Task.Domain.csproj",
    "work/production/src/Task.Infrastructure/Task.Infrastructure.csproj",
    "work/production/src/Task.Worker/Task.Worker.csproj",
    "work/production/tests/Task.ServiceHosts.Tests/AuthEndpointsTests.cs",
    "work/production/tests/Task.Tests/Security/LoginRateLimiterTests.cs",
    "work/production/verification/Test-IndependentSecurityReview.ps1",
    "work/production/verification/Build-IndependentSecurityReviewPackage.py",
    "work/production/verification/container-task-store/Task.ContainerValidation.csproj",
]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(*args: str) -> str:
    return subprocess.check_output(
        ["git", "-c", "core.quotepath=false", "-c", "core.safecrlf=false", *args],
        cwd=ROOT,
    ).decode("utf-8").strip()


def read_test_results() -> tuple[dict[str, tuple[Path, dict[str, str]]], int, int]:
    latest: dict[str, tuple[Path, dict[str, str]]] = {}
    for path in sorted(EVIDENCE.glob("*.trx"), key=lambda candidate: candidate.stat().st_mtime):
        tree = ET.parse(path)
        definitions = tree.findall(".//t:TestDefinitions/t:UnitTest", TRX_NS)
        counters = tree.find(".//t:ResultSummary/t:Counters", TRX_NS)
        if not definitions or counters is None:
            continue
        assembly = Path(definitions[0].get("storage", "")).stem.lower()
        project = {
            "task.tests": "Task.Tests",
            "task.servicehosts.tests": "Task.ServiceHosts.Tests",
            "task.desktop.tests": "Task.Desktop.Tests",
        }.get(assembly)
        if project:
            latest[project] = (path, dict(counters.attrib))

    expected = {"Task.Tests", "Task.ServiceHosts.Tests", "Task.Desktop.Tests"}
    assert set(latest) == expected, f"Missing current TRX evidence: {expected - set(latest)}"

    passed = 0
    skipped = 0
    for path, counters in latest.values():
        total = int(counters.get("total", "0"))
        executed = int(counters.get("executed", "0"))
        current_passed = int(counters.get("passed", "0"))
        current_failed = int(counters.get("failed", "0"))
        current_skipped = total - executed
        non_success = sum(
            int(counters.get(name, "0"))
            for name in (
                "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
                "notRunnable", "disconnected", "warning", "inProgress", "pending",
            )
        )
        assert total > 0 and current_failed == 0, f"Failed test evidence: {path}"
        assert non_success == 0 and executed == current_passed, f"Incomplete test evidence: {path}"
        assert current_skipped >= 0, f"Invalid test counters: {path}"
        counters["skipped"] = str(current_skipped)
        passed += current_passed
        skipped += current_skipped
    return latest, passed, skipped


def verify_dependency_evidence() -> int:
    inventory = json.loads((EVIDENCE / "dependency-vulnerabilities.json").read_text(encoding="utf-8-sig"))
    vulnerable = []
    for project in inventory.get("projects", []):
        for framework in project.get("frameworks", []):
            vulnerable.extend(framework.get("topLevelPackages", []))
            vulnerable.extend(framework.get("transitivePackages", []))
    assert not vulnerable, vulnerable
    return len(inventory.get("projects", []))


def main() -> None:
    assert not OUTPUT.exists(), f"Refusing to overwrite existing package: {OUTPUT}"
    assert EVIDENCE.is_dir(), "Run Test-IndependentSecurityReview.ps1 first."
    for relative in SOURCES:
        assert (ROOT / relative).is_file(), relative

    checks = json.loads((EVIDENCE / "checks.json").read_text(encoding="utf-8-sig"))
    assert checks and all(value is True for value in checks.values()), checks
    results, passed, skipped = read_test_results()
    audited_projects = verify_dependency_evidence()

    newest_source = max((ROOT / relative).stat().st_mtime for relative in SOURCES)
    for path, _ in results.values():
        assert path.stat().st_mtime >= newest_source, f"Stale test evidence: {path}"

    OUTPUT.mkdir(parents=True)
    for relative in SOURCES:
        destination = OUTPUT / "source" / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, destination)

    packaged_evidence = OUTPUT / "evidence"
    packaged_evidence.mkdir()
    evidence_files = sorted(EVIDENCE.glob("*.json")) + sorted(EVIDENCE.glob("*.log"))
    evidence_files += [entry[0] for entry in results.values()]
    for path in dict.fromkeys(evidence_files):
        shutil.copyfile(path, packaged_evidence / path.name)

    rows = "\n".join(
        f"| {project} | {counters.get('passed', '0')}/{counters.get('total', '0')} | "
        f"{counters.get('skipped', '0')} |"
        for project, (_, counters) in sorted(results.items())
    )
    report = f"""# SEC-05 validation report — {VERSION}

Result: PASS. Independent source security review is closed for the current source baseline.
Base commit before this source delta: `{git('rev-parse', 'HEAD')}`.

## Проверено

| Assembly | Passed / total | Skipped |
|---|---:|---:|
{rows}

Итого: {passed} тестов успешно, {skipped} пропущено. Пропуски относятся только к локальным
PostgreSQL integration fixtures без заданной test connection string; актуальный SEC-02 пакет
содержит отдельный полный PostgreSQL-backed authorization gate. Изменения SEC-05 не затрагивают
схему или persistence-контракты.

- locked restore прошёл для solution и linux-x64 контейнерных publish-графов;
- NuGet проверил direct/transitive graph {audited_projects} проектов: известных уязвимых пакетов нет;
- проверены tracked-secret patterns, отсутствие TLS validation bypass и CORS, точный anonymous
  endpoint inventory, loopback/private DB network и container hardening;
- независимый review не воспроизвёл auth bypass, IDOR, SQL injection или mass assignment в
  текущих vertical slices;
- найденный High `SEC05-F-001` (unique-login Argon2/memory exhaustion) устранён и закрыт
  регрессиями: body/field bounds, account/address/global throttles, bounded key cardinality и
  максимум два одновременных memory-hard password checks.

## Ограничение результата

Это закрывает работу `SEC-05`, но не production security gate. `SEC-03` остаётся hard blocker:
пакет не доказывает реальный reverse proxy, TLS/CA issuance и rotation, firewall, DB transport,
secret-file ACL или backup key custody. Финальный network penetration pass обязателен после
стабилизации customer-like deployment. `SEC-04` по-прежнему должен встроить dependency/secret/image
scans в регулярный CI.

Пакет содержит reviewed source delta и доказательства, а не installer. Канонические `sources/` не
изменялись. `manifest.json`, `MANIFEST.sha256`, ZIP CRC и SHA-256 проверены программно.
"""
    (OUTPUT / "validation-report.md").write_text(report, encoding="utf-8")
    (OUTPUT / "VERSION").write_text(VERSION + "\n", encoding="utf-8")

    content_files = sorted(
        path for path in OUTPUT.rglob("*")
        if path.is_file() and path.name not in {"manifest.json", "MANIFEST.sha256"}
    )
    manifest = {
        "package": NAME,
        "version": VERSION,
        "base_commit": git("rev-parse", "HEAD"),
        "review_result": "PASS",
        "sec05_status": "done",
        "release_security_cleared": False,
        "open_release_blockers": ["SEC-03"],
        "tests_passed": passed,
        "tests_skipped": skipped,
        "dependency_projects_audited": audited_projects,
        "sources": SOURCES,
        "files": [
            {
                "path": path.relative_to(OUTPUT).as_posix(),
                "size": path.stat().st_size,
                "sha256": sha256(path),
            }
            for path in content_files
        ],
    }
    manifest_path = OUTPUT / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    hash_targets = sorted(path for path in OUTPUT.rglob("*") if path.is_file() and path.name != "MANIFEST.sha256")
    hash_lines = [f"{sha256(path)}  {path.relative_to(OUTPUT).as_posix()}" for path in hash_targets]
    (OUTPUT / "MANIFEST.sha256").write_text("\n".join(hash_lines) + "\n", encoding="utf-8")
    for line in hash_lines:
        expected_hash, relative = line.split("  ", 1)
        assert sha256(OUTPUT / relative) == expected_hash

    archive = OUTPUT.parent / f"{NAME}.zip"
    assert not archive.exists(), f"Refusing to overwrite existing archive: {archive}"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(OUTPUT.rglob("*")):
            if path.is_file():
                bundle.write(path, path.relative_to(OUTPUT).as_posix())

    with zipfile.ZipFile(archive) as bundle:
        assert bundle.testzip() is None
        names = set(bundle.namelist())
        assert "manifest.json" in names and "MANIFEST.sha256" in names and "VERSION" in names
        for line in hash_lines:
            expected_hash, relative = line.split("  ", 1)
            assert hashlib.sha256(bundle.read(relative)).hexdigest() == expected_hash

    archive_hash = sha256(archive)
    archive.with_suffix(".zip.sha256").write_text(
        f"{archive_hash}  {archive.name}\n",
        encoding="utf-8",
    )
    print(json.dumps({
        "package": str(archive),
        "sha256": archive_hash,
        "tests_passed": passed,
        "tests_skipped": skipped,
        "source_files": len(SOURCES),
    }, ensure_ascii=True))


if __name__ == "__main__":
    main()
