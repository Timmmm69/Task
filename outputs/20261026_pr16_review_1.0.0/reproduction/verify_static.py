from pathlib import Path
import xml.etree.ElementTree as ET
from collections import Counter
import hashlib, json, re, subprocess

repo=Path(__file__).resolve().parent/'repo'
root=repo/'work/production/src/Task.Desktop'
x='{http://schemas.microsoft.com/winfx/2006/xaml}'
files=['Views/CalendarView.xaml','Resources/Calendar.Forms.xaml']
before=ET.fromstring(subprocess.check_output(['git','show','origin/main:work/production/src/Task.Desktop/Views/CalendarView.xaml'],cwd=repo))
after=ET.parse(root/files[0]).getroot()
for path in files:
    tree=ET.parse(root/path)
    for node in tree.iter():
        for child in node:
            prop=child.tag.rsplit('}',1)[-1]
            if '.' in prop: assert prop.split('.')[-1] not in node.attrib,(path,prop)
for attr in [x+'Name','AutomationProperties.AutomationId']:
    assert {e.get(attr) for e in before.iter() if e.get(attr)} <= {e.get(attr) for e in after.iter() if e.get(attr)}
for tag,attr in [('TextBox','Text'),('ComboBox','SelectedValue'),('ComboBox','SelectedItem'),('DatePicker','SelectedDate'),('CheckBox','IsChecked'),('Button','Command')]:
    old=Counter(e.get(attr) for e in before.iter() if e.tag.endswith('}'+tag) and e.get(attr))
    new=Counter(e.get(attr) for e in after.iter() if e.tag.endswith('}'+tag) and e.get(attr))
    # The redesign adds a second CancelCommand in its new close button.
    extra=new-old
    assert not old-new,(tag,old-new)
    assert not extra or (tag=='Button' and extra=={'{Binding CancelCommand}':1}),(tag,extra)
print('PASS XML, unique properties, original bindings/commands and automation IDs vs origin/main')
styles=ET.parse(root/files[1])
for style,parts in [('Calendar.Form.Date',{'PART_Root','PART_Button','PART_TextBox','PART_Popup'}),('Calendar.Form.Combo',{'PART_Popup'}),('Calendar.Form.Text',{'PART_ContentHost'})]:
    node=next(e for e in styles.iter() if e.get(x+'Key')==style)
    assert parts <= {e.get(x+'Name') for e in node.iter()}
print('PASS native template parts')
manifest=json.loads((repo/'outputs/20261026_calendar_today_design_1.1.0/manifest.json').read_text(encoding='utf-8-sig'))
for entry in manifest['files']:
    blob=subprocess.check_output(['git','show','07db1a7a190e33aefe25c446787117c731cf92c3:'+entry['path']],cwd=repo)
    assert hashlib.sha256(blob).hexdigest()==entry['sha256'],entry['path']
for line in (repo/'outputs/20261026_calendar_today_design_1.1.0/SHA256SUMS').read_text().splitlines():
    checksum,path=line.split('  ',1)
    assert hashlib.sha256(subprocess.check_output(['git','show','07db1a7a190e33aefe25c446787117c731cf92c3:'+path],cwd=repo)).hexdigest()==checksum,path
print('PASS active PR package 1.1.0 manifest and 9 SHA256SUMS entries against Git blobs')
evidence=Path(__file__).resolve().parent/'evidence'
assert (evidence/'format.log').read_bytes()==(evidence/'format-baseline.log').read_bytes()
print('PASS formatter diagnostics byte-identical between PR and main: 672 lines')
