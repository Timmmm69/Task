from pathlib import Path
import hashlib,json,shutil,subprocess,zipfile,xml.etree.ElementTree as ET
repo=Path(__file__).resolve().parents[2];work=repo/'work/employee_picker_final_review'
name='20261015_employee_picker_final_review_1.0.1';target=repo/'outputs'/name;target.mkdir(parents=True,exist_ok=True)
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
expected={'desktop-final':959,'scoped-review':206,'domain-postgres':233,'server-final':259,'grant-fix':3}
receipts={}
for namepart,total in expected.items():
 root=ET.parse(work/f'evidence/{namepart}.trx').getroot();c=root.find('t:ResultSummary/t:Counters',ns).attrib
 assert int(c['total'])==total and int(c['passed'])==total and int(c['failed'])==0 and int(c['notExecuted'])==0,(namepart,c)
 receipts[namepart]={k:int(c[k]) for k in ['total','passed','failed','notExecuted']}
assert json.loads((work/'evidence/postgres-runtime.json').read_text(encoding='utf-8-sig'))['executed']
source_commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip()
scope=subprocess.check_output(['git','diff','--name-only','5465c4e',source_commit,'--','work/production'],cwd=repo,text=True).splitlines()
scope+=['.project-dashboard/roadmap.json','work/employee_picker_final_review/Test-Postgres.ps1']
for rel in scope:
 dest=target/'source'/rel;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(repo/rel,dest)
(target/'evidence').mkdir(exist_ok=True)
for p in (work/'evidence').iterdir():
 if p.is_file() and p.name not in {'postgres.log','postgres-error.log','postgres-init.log','postgres-stop.log'}:shutil.copyfile(p,target/'evidence'/p.name)
shutil.copyfile(work/'validation-report.md',target/'validation-report.md')
(target/'VERSION').write_text('1.0.1\n',encoding='utf-8')
(target/'.gitattributes').write_text('* -text whitespace=cr-at-eol\n*.trx -diff\n*.log -diff\n',encoding='utf-8')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
entries=[{'path':p.relative_to(target).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(target.rglob('*')) if p.is_file() and p.name not in {'manifest.json','SHA256SUMS'}]
manifest={'artifact':'Task final Employee Picker 5.1-5.3 verification','version':'1.0.1','date':'2026-10-15','branch':'feature/employee-picker','source_commit':source_commit,'base':'5465c4e','scope':scope,'tests':receipts,'final_gate_passed':True,'total_nonoverlapping_tests':1451,'real_postgres_16_executed':True,'historical_native_activation_flake_recorded':True,'limitations':['UUID compatibility without User.Read and for contacts','Calendar server active-account race remains existing contract limit','No manual production organization walkthrough'],'files':entries}
(target/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
files=sorted(p for p in target.rglob('*') if p.is_file() and p.name!='SHA256SUMS')
(target/'SHA256SUMS').write_text(''.join(f'{sha(p)}  {p.relative_to(target).as_posix()}\n' for p in files),encoding='utf-8')
archive=Path(str(target)+'.zip')
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED) as z:
 for p in sorted(target.rglob('*')):
  if p.is_file():z.write(p,p.relative_to(target).as_posix())
with zipfile.ZipFile(archive) as z:
 names={p.relative_to(target).as_posix() for p in target.rglob('*') if p.is_file()};assert names==set(z.namelist())
 for rel in names:assert hashlib.sha256(z.read(rel)).hexdigest()==sha(target/rel),rel
 for line in z.read('SHA256SUMS').decode('utf-8').splitlines():
  digest,rel=line.split('  ',1);assert hashlib.sha256(z.read(rel)).hexdigest()==digest,rel
ziphash=sha(archive)
Path(str(archive)+'.sha256').write_text(f'{ziphash}  {archive.name}\n',encoding='utf-8')
Path(str(archive)+'.validation.json').write_text(json.dumps({'version':'1.0.1','zip_sha256':ziphash,'files_verified':len(names),'artifact_integrity_passed':True,'final_gate_passed':True,'tests':receipts},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'files_verified':len(names),'sha256':ziphash,'tests':'1451 PASS; scoped 206 PASS','integrity':'PASS'}))
