"""Stage only verified view-state sources and the task's dashboard delta."""
from pathlib import Path
import subprocess,json,hashlib

def git(*args):
    return subprocess.check_output(['git',*args])
assert not git('diff','--cached','--name-only').strip(), 'Index must be empty'
package=Path('outputs/20261008_view_state_1.0.0')
manifest=json.loads((package/'manifest.json').read_text(encoding='utf-8'))
source=[]
for entry in manifest['files']:
    path=package/entry['path']
    assert hashlib.sha256(path.read_bytes()).hexdigest()==entry['sha256'],path
    if entry['path'].startswith('source-snapshot/'):
        local=entry['path'].removeprefix('source-snapshot/')
        assert hashlib.sha256(Path(local).read_bytes()).hexdigest()==entry['sha256'],local
        source.append(local)
assert len(source)==20
# Working dashboard also contains unrelated changes. Keep them in the worktree.
dashboard='.project-dashboard/roadmap.json'
base=json.loads(git('show','HEAD:'+dashboard).decode('utf-8-sig'))
before=json.loads(Path('work/view_state_1/roadmap-before.json').read_text(encoding='utf-8-sig'))
current=json.loads(Path(dashboard).read_text(encoding='utf-8-sig'))
for key in ['DESK-02','DESK-05']:
    target=next(x for x in base['items'] if x['id']==key)
    prior=next(x for x in before['items'] if x['id']==key)
    latest=next(x for x in current['items'] if x['id']==key)
    assert latest['note'].startswith(prior['note']),key
    target['note'] += latest['note'][len(prior['note']):]
    target['evidence'] += [x for x in latest['evidence'] if x not in prior['evidence']]
    target['updated_at']=latest['updated_at']
index=Path('work/view_state_1/roadmap-for-index.json')
index.write_text(json.dumps(base,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
blob=git('hash-object','-w','--path='+dashboard,str(index)).decode().strip()
git('update-index','--add','--cacheinfo','100644,'+blob+','+dashboard)
git('add','--',*source,'work/view_state_1/package.py','work/view_state_1/stage.py',str(package),str(package)+'.zip',str(package)+'.zip.sha256')
# Logs are ignored globally but are explicit, hash-verified package payload.
git('add','-f','--',*[str(package/entry['path']) for entry in manifest['files']])
staged=git('diff','--cached','--name-only').decode().splitlines()
allowed=set(source+[dashboard,'work/view_state_1/package.py','work/view_state_1/stage.py',package.as_posix()+'.zip',package.as_posix()+'.zip.sha256'])
unexpected=[path for path in staged if path not in allowed and not path.startswith(package.as_posix()+'/')]
assert not unexpected,unexpected
print('Verified scoped index:',len(staged),'files')
