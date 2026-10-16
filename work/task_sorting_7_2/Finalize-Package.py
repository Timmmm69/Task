import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

root = Path(__file__).resolve().parents[2]
work = root / 'work/task_sorting_7_2'
output = root / 'outputs/20261016_task_filtering_sorting_7.2.0'
output.mkdir(parents=True, exist_ok=True)
(output / '.gitattributes').write_text('* -text\n', encoding='utf-8')

def git(*args):
    return subprocess.check_output(['git', *args], cwd=root)

def sha(data):
    return hashlib.sha256(data).hexdigest()

commit = '687b3877c64af98d691dc2ef23978b604cfa8f98'
base = 'eb8e0da27c720f3b897caa3f8ecaf894e1d63465'
assert git('branch', '--show-current').decode().strip() == 'feature/task-list-filtering-sorting'
files = git('diff', '--name-only', base, commit, '--', 'work/production').decode().splitlines()
assert files and all(p.startswith(('work/production/src/Task.Desktop/', 'work/production/tests/Task.Desktop.Tests/')) for p in files)
with zipfile.ZipFile(output / 'source.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    for name in files:
        archive.writestr(name, git('show', f'{commit}:{name}'))
(output / 'feature.patch').write_bytes(git('diff', '--binary', base, commit, '--', 'work/production'))
with zipfile.ZipFile(output / 'source.zip') as archive:
    assert archive.testzip() is None
    assert sorted(archive.namelist()) == sorted(files)
    for name in files:
        assert sha(archive.read(name)) == sha(git('show', f'{commit}:{name}'))

shutil.copy2(work / 'validation-report.md', output / 'validation-report.md')
(output / 'VERSION').write_text('7.2.0\n', encoding='utf-8')
evidence = output / 'evidence'
evidence.mkdir(exist_ok=True)
names = ['build-release.txt', 'targeted-final.trx', 'sorting-final.trx', 'desktop-final.trx',
         'context-native-final.trx', 'api-integration.trx', 'domain-query-final.trx', 'sorting-performance.txt',
         'filters-800.png', 'filters-1200.png', 'filters-1536.png', 'filters-1280.png', 'filters-960.png']
for name in names:
    shutil.copy2(work / 'evidence' / name, evidence / name)
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
test_runs = {}
for name in names:
    if name.endswith('.trx'):
        tree = ET.parse(evidence / name)
        counters = tree.find('.//t:Counters', ns).attrib
        assert int(counters['failed']) == 0
        test_runs[name] = counters

before = json.loads(git('show', f'{commit}^:.project-dashboard/roadmap.json').decode('utf-8-sig'))
after = json.loads(git('show', f'{commit}:.project-dashboard/roadmap.json').decode('utf-8-sig'))
def without_desk(node):
    if isinstance(node, dict):
        return {} if node.get('id') == 'DESK-02' else {k: without_desk(v) for k, v in node.items()}
    return [without_desk(v) for v in node] if isinstance(node, list) else node
assert without_desk(before) == without_desk(after), 'Unexpected dashboard scope'
contract = 'work/production/src/Task.Desktop/Tasks/DesktopTasksApiClient.cs'
assert git('rev-parse', f'{base}:{contract}') == git('rev-parse', f'{commit}:{contract}')
validation = {
    'version': '7.2.0', 'sourceCommit': commit, 'baseMain': base,
    'branch': 'feature/task-list-filtering-sorting', 'status': 'passed_with_disclosed_environment_limits',
    'uniqueFinalResults': {'passed': 1378, 'failed': 0, 'skipped': 1}, 'testRuns': test_runs,
    'checks': {'sourceZipCrc': True, 'sourceZipMatchesGitBlobs': True, 'desktopApiClientUnchanged': True,
               'dashboardOnlyDESK02': True, 'physicalDpi': 150, 'layoutEquivalentPercentages': [125, 150, 200],
               'gitDiffCheck': True, 'releaseBuild': True, 'dashboardOrderAndValidate': True},
    'notRun': ['Real PostgreSQL PATCH test: TASK_POSTGRES_TEST_ADMIN_CONNECTION absent',
               'Manual live-server LAN disconnect/reconnect', 'Narrator', 'Physical 125/200% and mixed-monitor DPI'],
    'sourceFiles': files,
}
(output / 'validation.json').write_text(json.dumps(validation, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
entries = [{'path': p.relative_to(output).as_posix(), 'bytes': p.stat().st_size, 'sha256': sha(p.read_bytes())}
           for p in sorted(output.rglob('*')) if p.is_file() and p.name not in ('manifest.json', 'SHA256SUMS')]
manifest = {'version': '7.2.0', 'sourceCommit': commit, 'files': entries,
            'checksumPolicy': 'manifest excludes itself and SHA256SUMS; SHA256SUMS includes manifest, excludes itself'}
(output / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
checksums = [f"{entry['sha256']}  {entry['path']}" for entry in entries]
checksums.append(f"{sha((output / 'manifest.json').read_bytes())}  manifest.json")
(output / 'SHA256SUMS').write_text('\n'.join(checksums) + '\n', encoding='utf-8')
for entry in entries:
    p = output / entry['path']
    assert p.stat().st_size == entry['bytes'] and sha(p.read_bytes()) == entry['sha256']
for line in checksums:
    digest, name = line.split('  ', 1)
    assert sha((output / name).read_bytes()) == digest
archive_path = Path(str(output) + '.zip')
with zipfile.ZipFile(archive_path, 'w', zipfile.ZIP_DEFLATED) as archive:
    for p in sorted(output.rglob('*')):
        if p.is_file():
            archive.write(p, p.relative_to(output.parent))
with zipfile.ZipFile(archive_path) as archive:
    assert archive.testzip() is None
    for p in output.rglob('*'):
        if p.is_file():
            assert sha(archive.read(p.relative_to(output.parent).as_posix())) == sha(p.read_bytes())
archive_sha = sha(archive_path.read_bytes())
archive_path.with_suffix('.zip.sha256').write_text(f'{archive_sha}  {archive_path.name}\n', encoding='utf-8')
archive_path.with_suffix('.zip.validation.json').write_text(json.dumps({'version': '7.2.0', 'sha256': archive_sha,
    'zipCrc': 'pass', 'manifestHashes': 'pass', 'sourceMatchesGit': commit}, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'output': str(output), 'sourceFiles': len(files), 'packageSha256': archive_sha,
                  'manifestFiles': len(entries)}))
