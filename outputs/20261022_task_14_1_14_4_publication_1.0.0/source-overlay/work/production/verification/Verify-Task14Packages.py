"""Read-only verification of the Task 14 deliverables before publication."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import zipfile

root = Path(sys.argv[1]).resolve()
original = Path(sys.argv[2]).resolve()
results = []
failures = []
names = [
    '20261022_task_14_1_status_return_contract_audit_1.0.0',
    '20261022_task_14_2_status_return_contract_1.0.0',
    '20261022_task_14_2_server_return_1.0.0',
    '20261022_task_14_3_wpf_return_1.0.0',
    '20261022_task_14_4_independent_audit_1.0.0',
]

def sha(data):
    return hashlib.sha256(data).hexdigest()

for name in names:
    folder = root / 'outputs' / name
    if not folder.exists():
        folder = original / 'outputs' / name
    manifest = json.loads((folder / 'manifest.json').read_text(encoding='utf-8-sig'))
    checked = 0
    for line in (folder / 'SHA256SUMS').read_text(encoding='utf-8-sig').splitlines():
        digest, relative = line.split('  ', 1)
        path = (folder / relative).resolve()
        if not path.is_relative_to(folder.resolve()) or sha(path.read_bytes()) != digest:
            failures.append(f'{name}/{relative}: SHA256SUMS mismatch')
        checked += 1
    for entry in manifest['files']:
        data = (folder / entry['path']).read_bytes()
        if sha(data) != entry['sha256'] or len(data) != entry['bytes']:
            failures.append(f'{name}/{entry["path"]}: manifest mismatch')
    if name.endswith('14_4_independent_audit_1.0.0'):
        for entry in manifest['source_files']:
            data = subprocess.check_output(['git', 'show', f'{manifest["audit_source_commit"]}:{entry["path"]}'], cwd=root)
            if sha(data) != entry['git_blob_sha256']:
                failures.append(f'{entry["path"]}: audited source mismatch')
        archive = Path(str(folder) + '.zip')
        expected = Path(str(archive) + '.sha256').read_text().split()[0]
        if sha(archive.read_bytes()) != expected:
            failures.append('14.4 ZIP checksum mismatch')
        with zipfile.ZipFile(archive) as z:
            if z.testzip() is not None:
                failures.append('14.4 ZIP CRC mismatch')
            files = {str(p.relative_to(folder)).replace('\\', '/'): p for p in folder.rglob('*') if p.is_file()}
            members = {item.filename: item for item in z.infolist() if not item.is_dir()}
            # The publisher may include the package folder as the archive root.
            members = {key.removeprefix(name + '/'): item for key, item in members.items()}
            if files.keys() != members.keys():
                failures.append('14.4 ZIP inventory mismatch')
            for key in files.keys() & members.keys():
                if sha(files[key].read_bytes()) != sha(z.read(members[key])):
                    failures.append(f'14.4 ZIP payload mismatch: {key}')
    results.append({'package': name, 'sha256_entries_checked': checked, 'manifest_entries_checked': len(manifest['files'])})

print(json.dumps({'packages': results, 'failures': failures, 'passed': not failures}, indent=2))
sys.exit(bool(failures))
