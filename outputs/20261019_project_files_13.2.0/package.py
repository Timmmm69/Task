from pathlib import Path
import hashlib,json,shutil,subprocess,zipfile,xml.etree.ElementTree as ET
repo=Path.cwd(); work=repo/'work/project_files_13_2'
output=Path('C:/Users/novik/Таск/outputs/20261019_project_files_13.2.0')
output.mkdir(parents=True,exist_ok=False)
version='13.2.0'
def git(*args): return subprocess.check_output(['git',*args],cwd=repo,encoding='utf-8').strip()
def digest(data): return hashlib.sha256(data).hexdigest()
commit=git('rev-parse','HEAD')
files=git('diff-tree','--no-commit-id','--name-only','-r','HEAD').splitlines()
assert len(files)==19 and not any(p.startswith('sources/') for p in files)
for name in files:
 target=output/'source'/name; target.parent.mkdir(parents=True,exist_ok=True); shutil.copyfile(repo/name,target)
for name in ['implementation.md','validation-report.md','test-summary.json']:
 shutil.copyfile(work/name,output/name)
(output/'changed-files.txt').write_text('\n'.join(files)+'\n',encoding='utf-8')
(output/'VERSION').write_text(version+'\n',encoding='utf-8')
(output/'source.patch').write_bytes(subprocess.check_output(['git','diff','HEAD^','HEAD','--binary'],cwd=repo))
receipt={'version':version,'commit':commit,'branch':git('branch','--show-current'),'base':git('rev-parse','HEAD^'),'originMain':git('rev-parse','origin/main'),'publishedToMain':False,'files':files}
(output/'source-receipt.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
for source in work.glob('*.log'):
 target=output/'evidence'/source.name; target.parent.mkdir(exist_ok=True); shutil.copyfile(source,target)
for source in (work/'evidence').glob('*.trx'): shutil.copyfile(source,output/'evidence'/source.name)
for source in (work/'evidence/screenshots').glob('project-files-*.png'):
 target=output/'evidence/screenshots'/source.name; target.parent.mkdir(parents=True,exist_ok=True); shutil.copyfile(source,target)
shutil.copyfile(work/'package.py',output/'package.py')
manifest={'version':version,'sourceCommit':commit,'files':[]}
for source in sorted(p for p in output.rglob('*') if p.is_file()):
 data=source.read_bytes();manifest['files'].append({'path':source.relative_to(output).as_posix(),'bytes':len(data),'sha256':digest(data)})
mp=output/'manifest.json';mp.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(output/'manifest.json.sha256').write_text(digest(mp.read_bytes())+'  manifest.json\n',encoding='ascii')
for item in manifest['files']:
 data=(output/item['path']).read_bytes();assert len(data)==item['bytes'] and digest(data)==item['sha256']
archive=output.with_suffix('.0.zip') if False else Path(str(output)+'.zip')
with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
 for source in sorted(p for p in output.rglob('*') if p.is_file()): z.write(source,source.relative_to(output).as_posix())
expected={p.relative_to(output).as_posix():p.read_bytes() for p in output.rglob('*') if p.is_file()}
with zipfile.ZipFile(archive) as z:
 assert z.testzip() is None
 assert set(z.namelist())==set(expected)
 for name,data in expected.items(): assert z.read(name)==data
archive_hash=digest(archive.read_bytes())
Path(str(archive)+'.sha256').write_text(archive_hash+'  '+archive.name+'\n',encoding='ascii')
validation={'version':version,'sourceCommit':commit,'validated':True,'manifestFileCount':len(manifest['files']),'archiveEntryCount':len(expected),'sourceFiles':len(files),'sha256':archive_hash,'manifestSha256':digest(mp.read_bytes()),'zipCRC':True,'zipEntriesMatchManifestAndChecksums':True,'fileHashesVerified':True,'build':'PASS','desktopPassed':1680,'desktopFailed':0,'desktopSkipped':0,'endpointPassed':1,'nativeFinalPassed':1,'liveCorporateApiNasTested':False}
Path(str(archive)+'.validation.json').write_text(json.dumps(validation,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'archive':str(archive),'sha256':archive_hash,'validated':True,'files':len(expected)},ensure_ascii=True))
