from pathlib import Path
import hashlib, json, shutil, zipfile, xml.etree.ElementTree as ET
repo=Path(__file__).resolve().parents[2]
work=repo/'work/employee_picker_5_3'
name='20261015_calendar_employee_picker_5_3_1.0.0'
target=repo/'outputs'/name
target.mkdir(parents=True,exist_ok=True)
scope=[
'work/production/src/Task.Desktop/Modes/CorporateApplicationContext.cs',
'work/production/src/Task.Desktop/ViewModels/CalendarViewModel.cs',
'work/production/src/Task.Desktop/ViewModels/CalendarEventEditorViewModel.Employees.cs',
'work/production/src/Task.Desktop/Views/CalendarView.xaml',
'work/production/tests/Task.Desktop.Tests/Calendar/CalendarAttendeeEditorTests.cs',
'work/production/tests/Task.Desktop.Tests/Calendar/DesktopCalendarApiClientTests.cs',
'work/production/tests/Task.Desktop.Tests/Calendar/CalendarEmployeePickerTests.cs',
'work/production/tests/Task.Desktop.Tests/Calendar/CalendarPickerWindowsTests.cs',
'work/production/docs/CALENDAR-EMPLOYEE-PICKER-5.3.md',
'.project-dashboard/roadmap.json']
for p in scope:
 dest=target/'source'/p;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(repo/p,dest)
(target/'evidence').mkdir(exist_ok=True)
exclude={'calendar-targeted.trx','desktop-regression-window-activation-failure.trx','desktop-window-activation-failure.log'}
for p in (work/'evidence').iterdir():
 if p.is_file() and p.name not in exclude:shutil.copyfile(p,target/'evidence'/p.name)
shutil.copyfile(work/'validation-report.md',target/'validation-report.md')
(target/'VERSION').write_text('1.0.0\n',encoding='utf-8')
(target/'.gitattributes').write_text('* -text whitespace=cr-at-eol\n*.diff -diff\n',encoding='utf-8')
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
expected={'employee-project-calendar-final':(204,204,0),'desktop-regression':(957,956,1),'server-contract':(259,259,0),'domain-calendar':(174,174,0),'baseline-window-activation':(1,0,1),'window-activation-recheck':(1,0,1)}
receipts={}
for test,counts in expected.items():
 root=ET.parse(target/f'evidence/{test}.trx').getroot();c=root.find('t:ResultSummary/t:Counters',ns).attrib
 actual=tuple(int(c[k]) for k in ['total','passed','failed'])
 assert counts==actual,(test,actual)
 assert int(c.get('notExecuted','0'))==0
 receipts[test]={k:int(c[k]) for k in ['total','passed','failed','notExecuted']}
 if counts[2]:
  failed=[n.attrib['testName'] for n in root.findall('t:Results/t:UnitTestResult',ns) if n.attrib['outcome']=='Failed']
  assert failed==['Task.Desktop.Tests.TaskScreen.TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow'],failed
  assert 'line 62' in (target/f'evidence/{test}.trx').read_text(encoding='utf-8')
  receipts[test]['failed_tests']=failed

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
entries=[{'path':p.relative_to(target).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(target.rglob('*')) if p.is_file() and p.name not in {'manifest.json','SHA256SUMS'}]
manifest={'artifact':'Task calendar Employee Picker 5.3 and final task 5 verification','version':'1.0.0','date':'2026-10-15','branch':'feature/employee-picker','source_commit':'5ab9abfc0c57c364dc7659f844c870f0e4eb77c4','source_base':'ed80529','main_base':'5465c4e','scope':scope,'test_receipts':receipts,'scoped_gate_passed':True,'full_desktop_gate_passed':False,'limitations':['Inherited native window activation failure reproduced at stage 5.2 commit ed80529','UUID compatibility remains without User.Read and for contacts','Calendar server has no transactional active-account attendee validation','No live organization/real PostgreSQL or manual production UI acceptance'],'files':entries}
(target/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
all_files=sorted(p for p in target.rglob('*') if p.is_file() and p.name!='SHA256SUMS')
(target/'SHA256SUMS').write_text(''.join(f'{sha(p)}  {p.relative_to(target).as_posix()}\n' for p in all_files),encoding='utf-8')
archive=Path(str(target)+'.zip')
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED) as z:
 for p in sorted(target.rglob('*')):
  if p.is_file():z.write(p,p.relative_to(target).as_posix())
with zipfile.ZipFile(archive) as z:
 names={p.relative_to(target).as_posix() for p in target.rglob('*') if p.is_file()}
 assert names==set(z.namelist())
 for rel in names:assert hashlib.sha256(z.read(rel)).hexdigest()==sha(target/rel),rel
 for line in z.read('SHA256SUMS').decode('utf-8').splitlines():
  digest,rel=line.split('  ',1);assert hashlib.sha256(z.read(rel)).hexdigest()==digest,rel
 for entry in entries:assert sha(target/entry['path'])==entry['sha256']
zipsha=sha(archive)
Path(str(archive)+'.sha256').write_text(f'{zipsha}  {archive.name}\n',encoding='utf-8')
Path(str(archive)+'.validation.json').write_text(json.dumps({'version':'1.0.0','zip_sha256':zipsha,'files_verified':len(names),'artifact_integrity_passed':True,'scoped_gate_passed':True,'full_desktop_gate_passed':False,'tests':receipts},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'artifact':str(target),'files_verified':len(names),'zip_sha256':zipsha,'integrity':'PASS','scoped_gate':'PASS','full_desktop_gate':'FAIL (baseline reproduced)'},ensure_ascii=False))
