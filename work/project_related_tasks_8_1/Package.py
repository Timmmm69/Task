"""Build and independently verify the Stage 8.1 evidence package; no source mutation."""
from pathlib import Path
import hashlib, json, shutil, subprocess, zipfile
from xml.etree import ElementTree as ET

root = Path(__file__).resolve().parents[2]
scratch = root / 'work/project_related_tasks_8_1'
output = root / 'outputs/20261016_project_related_tasks_8.1.0'
output.mkdir(parents=True, exist_ok=True)
scope = [
    '.project-dashboard/roadmap.json',
    'work/production/docs/PROD-04-projects-and-members.md',
    'work/production/src/Task.Desktop/ViewModels/ProjectsViewModel.cs',
    'work/production/src/Task.Desktop/ViewModels/ProjectsViewModel.RelatedTasks.cs',
    'work/production/src/Task.Desktop/Views/ProjectsView.xaml',
    'work/production/src/Task.Desktop/Views/ProjectsView.xaml.cs',
    'work/production/tests/Task.Desktop.Tests/Projects/ProjectsViewModelTests.cs',
    'work/production/tests/Task.Desktop.Tests/Projects/ProjectEmployeePickerTests.cs',
    'work/production/tests/Task.Desktop.Tests/Projects/ProjectRelatedTasksTests.cs',
    'work/production/tests/Task.Desktop.Tests/Projects/ProjectRelatedTasksWindowsTests.cs',
]
def copy(src, dest):
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(src, dest)
def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
for rel in scope:
    copy(root / rel, output / 'source' / rel)
for name in ['baseline.trx', 'related-tasks.trx', 'projects-final.trx', 'projects-final.log', 'desktop-full.trx', 'desktop-full.log', 'build.log']:
    copy(scratch / 'evidence' / name, output / 'evidence' / name)
for name in ['related-tasks-partial.png', 'related-tasks-complete.png']:
    copy(scratch / 'evidence/wpf' / name, output / 'evidence/wpf' / name)
for name in ['README.md', 'ProjectRelatedTasksBaselineTests.cs']:
    copy(scratch / 'baseline' / name, output / 'baseline' / name)

ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tests = {}
for name in ['baseline.trx', 'related-tasks.trx', 'projects-final.trx', 'desktop-full.trx']:
    tree = ET.parse(output / 'evidence' / name)
    counters = tree.find('t:ResultSummary/t:Counters', ns).attrib
    tests[name] = {key: int(counters[key]) for key in ['total','executed','passed','failed','notExecuted']}
    assert tests[name]['failed'] == 0 and tests[name]['notExecuted'] == 0

changed = subprocess.check_output(['git','diff','--name-only','deb16b3','--'], cwd=root, text=True).splitlines()
assert all(rel in scope or rel.startswith(('outputs/20261016_project_related_tasks_8.1.0', 'work/project_related_tasks_8_1/')) for rel in changed), changed
protected = [
    'work/production/src/Task.Desktop/Tasks/DesktopTasksApiClient.cs',
    'work/production/src/Task.Api/Tasks/TaskEndpoints.cs',
]
protected_hashes = {}
for rel in protected:
    original = subprocess.check_output(['git','show',f'deb16b3:{rel}'], cwd=root)
    # Normalize checkout EOL only; no contract changes are allowed.
    assert original.replace(b'\r\n',b'\n') == (root/rel).read_bytes().replace(b'\r\n',b'\n')
    protected_hashes[rel] = digest(root/rel)

validation = {
    'version':'8.1.0', 'baseCommit':'deb16b3', 'branch':'perf/project-related-tasks',
    'tests':tests, 'build':'Release solution PASS (see evidence/build.log)',
    'finalSourceValidation':'projects-final.trx: all final changes; desktop-full.trx precedes the final session/offline precedence guard and its new regression test',
    'nativeWpf':'PASS: UIA Invoke, focus, partial/complete, collapse/resume, unload cancellation; PNGs included',
    'protectedFilesSha256':protected_hashes, 'forbiddenScopeChanges':[],
    'limitations':['No live-server performance measurements', 'Full results still require every Task API page',
        'No new server filters, DTOs, DB schema, permissions, or permanent offline cache',
        'No full backend/database test suite; server code is unchanged'],
}
(output/'validation.json').write_text(json.dumps(validation,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
files = [p for p in sorted(output.rglob('*')) if p.is_file() and p.name not in ['manifest.json','SHA256SUMS']]
manifest = {'version':'8.1.0','baseCommit':'deb16b3','files':[
    {'path':p.relative_to(output).as_posix(),'bytes':p.stat().st_size,'sha256':digest(p)} for p in files]}
(output/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
files.append(output/'manifest.json')
(output/'SHA256SUMS').write_text(''.join(f'{digest(p)}  {p.relative_to(output).as_posix()}\n' for p in files),encoding='utf-8')
for entry in manifest['files']:
    assert digest(output/entry['path']) == entry['sha256']
archive = output.with_name(output.name+'.zip')
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
    for p in sorted(output.rglob('*')):
        if p.is_file(): z.write(p,p.relative_to(output).as_posix())
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for entry in manifest['files']:
        assert hashlib.sha256(z.read(entry['path'])).hexdigest() == entry['sha256']
archive.with_name(archive.name+'.sha256').write_text(f'{digest(archive)}  {archive.name}\n',encoding='utf-8')
print(json.dumps({'artifact':str(archive),'sha256':digest(archive),'tests':tests,'verifiedFiles':len(files)}))
