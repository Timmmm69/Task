"""Package the SEC-02 source delta only after current full PostgreSQL-backed tests pass."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
VERSION = '1.0.0'
NAME = '20260906_sec02_authorization_' + VERSION
OUTPUT = ROOT / 'outputs' / NAME
EVIDENCE = ROOT / 'work/production/evidence/sec02'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def git(*args):
    return subprocess.check_output(['git', '-c', 'core.quotepath=false', '-c', 'core.safecrlf=false', *args], cwd=ROOT).decode('utf-8').strip()

def main():
    changed = set(git('diff', '--name-only').splitlines()) | set(git('ls-files', '--others', '--exclude-standard').splitlines())
    sources = sorted(p for p in changed if (p.startswith('work/production/') and '/evidence/' not in p) or p == '.project-dashboard/roadmap.json')
    assert sources and all((ROOT / p).is_file() for p in sources)
    code_time = max((ROOT / p).stat().st_mtime for p in sources if Path(p).suffix in ('.cs', '.sql'))
    latest = {}
    for path in sorted(EVIDENCE.glob('*.trx'), key=lambda p: p.stat().st_mtime):
        tree = ET.parse(path)
        tests = tree.findall('.//t:TestDefinitions/t:UnitTest', NS)
        counters = tree.find('.//t:ResultSummary/t:Counters', NS)
        if not tests or counters is None or int(counters.get('total', '0')) < 100:
            continue
        assembly = Path(tests[0].get('storage')).stem.lower()
        project = {p.lower(): p for p in ('Task.Tests', 'Task.ServiceHosts.Tests', 'Task.Desktop.Tests')}.get(assembly, assembly)
        latest[project] = path, dict(counters.attrib)
    assert set(latest) == {'Task.Tests', 'Task.ServiceHosts.Tests', 'Task.Desktop.Tests'}
    for path, counters in latest.values():
        assert path.stat().st_mtime >= code_time, f'Stale test run: {path}'
        assert counters['total'] == counters['passed'] and int(counters.get('notExecuted', '0')) == 0, path
    checks = json.loads((EVIDENCE / 'checks.json').read_text(encoding='utf-8-sig'))
    assert all(value is True for value in checks.values()), checks
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for relative in sources:
        destination = OUTPUT / 'source' / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, destination)
    (OUTPUT / 'evidence').mkdir(exist_ok=True)
    for path, _ in latest.values():
        shutil.copyfile(path, OUTPUT / 'evidence' / path.name)
    for name in ['tests.log', 'security-gate-final.log', 'boundaries.log', 'dashboard.log', 'checks.json']:
        shutil.copyfile(EVIDENCE / name, OUTPUT / 'evidence' / name)
    total = sum(int(c['passed']) for _, c in latest.values())
    rows = '\n'.join(f'| {project} | {c["passed"]}/{c["total"]} | 0 |' for project, (_, c) in sorted(latest.items()))
    report = f'''# SEC-02 validation report — {VERSION}

Result: PASS. Schema version 13. Base commit: {git('rev-parse', 'HEAD')}.

## Verified

| Assembly | Passed / total | Skipped |
|---|---|---|
{rows}

Total: {total} passing tests. The full solution ran against a disposable local PostgreSQL 16 instance, including schema upgrades and the non-superuser runtime grants. No customer database was used.

Coverage includes private contacts/catalog/interactions, project inheritance and revocation, tree and search visibility, personal tasks/calendar/recurrence, generated task assignees, explicit-deny precedence, system role allowlists, department and expiration scope, stale request capability revocation, atomic role replacement/idempotency/versioning, last-administrator role removal and account blocking. Existing endpoint tests cover route authorization and pre-handler denial.

Additional checks: security gate, architecture boundaries, dashboard order/validation and git diff whitespace validation passed. Existing analyzer warnings are not new runtime failures. No manual desktop UX acceptance or customer deployment was performed; TLS/secrets and physical SMB ACL remain separate concerns.

## Deployment

Apply migration 013 through the normal migrator and reapply the runtime grant script. Assign roles explicitly: business roles are never automatically granted to existing users. Administrative roles must be permanent and organization-wide. Reserved system-role collisions stop migration rather than overwriting a custom role. See source/work/production/docs/SEC-02-authorization.md for scope rules.

This package contains the reviewed source delta and test evidence, not a full installer. Sources under sources/ were not modified. SHA-256 entries in manifest.json were recalculated from every packaged file; the ZIP has a separate SHA-256 file.
'''
    (OUTPUT / 'validation-report.md').write_text(report, encoding='utf-8')
    (OUTPUT / 'VERSION').write_text(VERSION + '\n', encoding='utf-8')
    files = [{'path': p.relative_to(OUTPUT).as_posix(), 'size': p.stat().st_size, 'sha256': sha(p)} for p in sorted(OUTPUT.rglob('*')) if p.is_file() and p.name != 'manifest.json']
    manifest = {'package': NAME, 'version': VERSION, 'schema_version': 13, 'base_commit': git('rev-parse', 'HEAD'), 'tests_passed': total, 'sources': sources, 'files': files}
    (OUTPUT / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    for row in files:
        assert sha(OUTPUT / row['path']) == row['sha256']
    archive = OUTPUT.parent / (OUTPUT.name + '.zip')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(OUTPUT.rglob('*')):
            if path.is_file(): bundle.write(path, path.relative_to(OUTPUT).as_posix())
    with zipfile.ZipFile(archive) as bundle:
        assert bundle.testzip() is None
        for row in files: assert hashlib.sha256(bundle.read(row['path'])).hexdigest() == row['sha256']
    archive.with_suffix('.zip.sha256').write_text(sha(archive) + '  ' + archive.name + '\n', encoding='utf-8')
    print(json.dumps({'package': str(archive), 'sha256': sha(archive), 'tests': total, 'source_files': len(sources)}, ensure_ascii=False))

if __name__ == '__main__':
    main()
