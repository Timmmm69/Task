"""Verify the exact staged evidence bytes and protected scope before committing."""
from pathlib import Path
import hashlib,json,subprocess

root=Path(__file__).resolve().parents[2]
prefix='outputs/20261016_project_related_tasks_8.1.0/'
def staged(name):
    return subprocess.check_output(['git','show',':'+name],cwd=root)
manifest=json.loads(staged(prefix+'manifest.json'))
for item in manifest['files']:
    assert hashlib.sha256(staged(prefix+item['path'])).hexdigest()==item['sha256'],item['path']
checksum=staged(prefix+'SHA256SUMS').decode('utf-8')
for line in checksum.splitlines():
    digest,name=line.split('  ',1)
    assert hashlib.sha256(staged(prefix+name)).hexdigest()==digest,name
archive='outputs/20261016_project_related_tasks_8.1.0.zip'
assert hashlib.sha256(staged(archive)).hexdigest()==staged(archive+'.sha256').decode().split()[0]
names=subprocess.check_output(['git','diff','--cached','--name-only'],cwd=root,text=True).splitlines()
assert all(n.startswith(('work/production/src/Task.Desktop/ViewModels/ProjectsViewModel',
    'work/production/src/Task.Desktop/Views/ProjectsView.',
    'work/production/tests/Task.Desktop.Tests/Projects/',
    'work/production/docs/PROD-04-projects-and-members.md',
    'work/project_related_tasks_8_1/',prefix,archive)) or n=='.project-dashboard/roadmap.json' for n in names)
print(f'PASS: {len(manifest["files"])} manifest entries, SHA256SUMS, ZIP, and {len(names)} staged scope files')
