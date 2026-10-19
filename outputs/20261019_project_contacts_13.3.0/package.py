from pathlib import Path
import hashlib, json, shutil, subprocess, zipfile, xml.etree.ElementTree as ET

repo = Path.cwd()
work = repo / 'work/project_contacts_13_3'
output = Path('C:/Users/novik/Таск/outputs/20261019_project_contacts_13.3.0')
version = '13.3.0'
def git(*args):
    return subprocess.check_output(['git', *args], cwd=repo, encoding='utf-8').strip()
def sha(data):
    return hashlib.sha256(data).hexdigest()
def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

commit = git('rev-parse', 'HEAD')
assert git('branch', '--show-current') == 'feature/project-detail-sections'
assert git('rev-parse', 'HEAD^').startswith('4accd66')
assert not git('status', '--porcelain', '--untracked-files=no')
files = git('diff-tree', '--no-commit-id', '--name-only', '-r', 'HEAD').splitlines()
assert len(files) == 21 and not any(path.startswith('sources/') for path in files)
summary = json.loads((work / 'test-summary.json').read_text(encoding='utf-8'))
assert summary['runs']['desktop-release-final.trx']['passed'] == 1750
assert summary['runs']['desktop-release-final.trx']['failed'] == 0
assert summary['runs']['product-endpoints-release.trx']['passed'] == 254
assert summary['newStage13_3Tests'] == 70
output.mkdir(parents=True, exist_ok=False)
for name in files:
    target = output / 'source' / name
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(repo / name, target)
    # Git normalizes text line endings; the package preserves the actual verified working files.
    blob = subprocess.check_output(['git', 'show', 'HEAD:' + name], cwd=repo)
    assert blob.replace(b'\r\n', b'\n') == target.read_bytes().replace(b'\r\n', b'\n')
for name in ['implementation.md', 'validation-report.md', 'test-summary.json', 'changed-files.txt', 'package.py']:
    shutil.copyfile(work / name, output / name)
(output / 'VERSION').write_text(version + '\n', encoding='utf-8')
(output / 'source.patch').write_bytes(subprocess.check_output(['git', 'diff', 'HEAD^', 'HEAD', '--binary'], cwd=repo))
write(output / 'source-receipt.json', {'version': version, 'commit': commit, 'base': git('rev-parse', 'HEAD^'),
    'branch': git('branch', '--show-current'), 'originMain': git('rev-parse', 'origin/main'), 'publishedToMain': False,
    'sourceFilesMatchCommitWithNormalizedLineEndings': True, 'files': files})
evidence = output / 'evidence'
evidence.mkdir()
for source in list(work.glob('*.log')) + list((work / 'evidence').glob('*.trx')):
    shutil.copyfile(source, evidence / source.name)
screens = evidence / 'screenshots'
screens.mkdir()
for source in (work / 'evidence/screenshots').glob('project-contacts-*.png'):
    shutil.copyfile(source, screens / source.name)
assert len(list(screens.glob('*.png'))) == 2
manifest = {'version': version, 'sourceCommit': commit, 'files': []}
for source in sorted(path for path in output.rglob('*') if path.is_file()):
    data = source.read_bytes()
    manifest['files'].append({'path': source.relative_to(output).as_posix(), 'bytes': len(data), 'sha256': sha(data)})
mp = output / 'manifest.json'
write(mp, manifest)
(output / 'manifest.json.sha256').write_text(sha(mp.read_bytes()) + '  manifest.json\n', encoding='ascii')
for item in manifest['files']:
    data = (output / item['path']).read_bytes()
    assert len(data) == item['bytes'] and sha(data) == item['sha256']
archive = Path(str(output) + '.zip')
with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for source in sorted(path for path in output.rglob('*') if path.is_file()):
        z.write(source, source.relative_to(output).as_posix())
expected = {path.relative_to(output).as_posix(): path.read_bytes() for path in output.rglob('*') if path.is_file()}
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None and set(z.namelist()) == set(expected)
    for name, data in expected.items():
        assert z.read(name) == data
archive_sha = sha(archive.read_bytes())
Path(str(archive) + '.sha256').write_text(archive_sha + '  ' + archive.name + '\n', encoding='ascii')
validation = {'version': version, 'sourceCommit': commit, 'validated': True, 'manifestFileCount': len(manifest['files']),
    'archiveEntryCount': len(expected), 'sourceFiles': len(files), 'sha256': archive_sha, 'manifestSha256': sha(mp.read_bytes()),
    'zipCRC': True, 'zipEntriesMatchManifestAndChecksums': True, 'fileHashesVerified': True,
    'solutionReleaseBuild': 'PASS', 'desktopPassed': 1750, 'desktopFailed': 0, 'desktopSkipped': 0, 'endpointPassed': 254,
    'stage13_3NewTests': 70, 'initialDesktopFailed': 1, 'focusIsolatedRecheckPassed': 1, 'liveCorporateApiPostgresTested': False}
write(Path(str(archive) + '.validation.json'), validation)
print(json.dumps({'archive': str(archive), 'sha256': archive_sha, 'validated': True, 'sourceCommit': commit, 'files': len(expected)}))
