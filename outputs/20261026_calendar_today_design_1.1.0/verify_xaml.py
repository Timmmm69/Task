from pathlib import Path
from lxml import etree
from collections import Counter
import re,subprocess
root=Path('work/production/src/Task.Desktop'); x='{http://schemas.microsoft.com/winfx/2006/xaml}'
parser=etree.XMLParser(remove_comments=True)
paths=['Views/CalendarView.xaml','Resources/Calendar.Forms.xaml']
for path in paths:
    tree=etree.parse(str(root/path),parser)
    for node in tree.iter():
        for child in node:
            prop=etree.QName(child).localname
            if '.' in prop: assert prop.split('.')[-1] not in node.attrib,(path,prop)
    print('PASS XML and unique property assignments:',path)
before=etree.fromstring(subprocess.check_output(['git','show','3ef60359be732898f520da65942730982a55d947:work/production/src/Task.Desktop/Views/CalendarView.xaml']),parser)
after=etree.parse(str(root/paths[0]),parser)
for attr in [x+'Name','AutomationProperties.AutomationId']:
    assert {e.get(attr) for e in before.iter() if e.get(attr)} <= {e.get(attr) for e in after.iter() if e.get(attr)}
for tag,attr in [('TextBox','Text'),('ComboBox','SelectedValue'),('ComboBox','SelectedItem'),('DatePicker','SelectedDate'),('CheckBox','IsChecked'),('Button','Command')]:
    old=Counter(e.get(attr) for e in before.iter() if etree.QName(e).localname==tag and e.get(attr))
    new=Counter(e.get(attr) for e in after.iter() if etree.QName(e).localname==tag and e.get(attr))
    assert old==new,(tag,old-new,new-old)
print('PASS original bindings, commands and automation identities preserved')
keys={e.get(x+'Key') for p in root.rglob('*.xaml') for e in etree.parse(str(p),parser).iter()}
pattern=r'\{(?:Static|Dynamic)Resource ([^{}]+)\}'
original=subprocess.check_output(['git','show','3ef60359be732898f520da65942730982a55d947:work/production/src/Task.Desktop/Views/CalendarView.xaml'],text=True)
legacy_missing=set(re.findall(pattern,original))-keys
for path in paths: assert not(set(re.findall(pattern,(root/path).read_text()))-keys-legacy_missing)
print('PASS no new missing resource keys')
ns={'p':'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}
styles=etree.parse(str(root/paths[1]),parser)
for style,parts in [('Calendar.Form.Date',{'PART_Root','PART_Button','PART_TextBox','PART_Popup'}),('Calendar.Form.Combo',{'PART_Popup'}),('Calendar.Form.Text',{'PART_ContentHost'})]:
    node=next(e for e in styles.iter() if e.get(x+'Key')==style)
    assert parts <= {e.get(x+'Name') for e in node.iter()}
print('PASS native control template parts retained')
byname={e.get(x+'Name'):e for e in after.iter() if e.get(x+'Name')}
assert 'CalendarEditorBody' in byname and 'CalendarEditorAttendees' in byname
assert any(e.get('ConverterParameter')=='900' or 'ConverterParameter=900' in str(e.attrib) for e in after.iter())
assert etree.QName(byname['CalendarEditorActions'].getparent()).localname=='Border'
assert byname['CalendarEditorActions'].getparent().get('Grid.Row')=='2'
print('PASS responsive editor and separate action footer declared (runtime layout requires Windows)')
print('REFERENCE TodayView.xaml: flat Surface.Base panels, Border.Default separators, 20px panel padding, compact icon headings, queue rows and Brand.Soft accents')
