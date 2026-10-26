from pathlib import Path
from lxml import etree
from collections import Counter
import subprocess,re
root=Path('work/production/src/Task.Desktop')
x='{http://schemas.microsoft.com/winfx/2006/xaml}'
parser=etree.XMLParser(remove_comments=True)
changed=[root/'Views/CalendarView.xaml',root/'Resources/Calendar.Forms.xaml']
for p in changed:
    tree=etree.parse(str(p),parser)
    for e in tree.iter():
        for child in e:
            prop=etree.QName(child).localname
            if '.' in prop:
                assert prop.split('.')[-1] not in e.attrib, (p,e.sourceline,prop)
    print('PASS XML and unique property assignments:',p)
resources={e.get(x+'Key') for p in root.rglob('*.xaml') for e in etree.parse(str(p),parser).iter()}
base=subprocess.check_output(['git','show','3fe76562b3c51fee3e36b933b169b101376afb47:work/production/src/Task.Desktop/Views/CalendarView.xaml'])
pattern=r'\{(?:Static|Dynamic)Resource ([^{}]+)\}'
baseline_missing=set(re.findall(pattern,base.decode()))-resources
for p in changed:
    missing=set(re.findall(pattern,p.read_text()))-resources
    assert not (missing-baseline_missing), (p,missing-baseline_missing)
print('PASS no new unresolved resource references; baseline Task.Radius.Window remains outside editor')
current=etree.parse(str(changed[0]),parser)
before=etree.fromstring(base,parser)
for attr in ['AutomationProperties.AutomationId',x+'Name']:
    for value in [e.get(attr) for e in before.iter() if e.get(attr)]:
        assert any(e.get(attr)==value for e in current.iter()), (attr,value)
print('PASS existing automation IDs and named controls retained')
for tag,attr in [('Button','Command'),('ComboBox','SelectedValue'),('ComboBox','SelectedItem'),('TextBox','Text'),('DatePicker','SelectedDate'),('CheckBox','IsChecked')]:
    old=Counter(e.get(attr) for e in before.iter() if etree.QName(e).localname==tag and e.get(attr))
    new=Counter(e.get(attr) for e in current.iter() if etree.QName(e).localname==tag and e.get(attr))
    assert old==new, (tag,old-new,new-old)
print('PASS data bindings and commands preserved')
parts={e.get(x+'Name') for e in etree.parse(str(changed[1]),parser).iter()}
assert {'PART_Root','PART_TextBox','PART_Button','PART_Popup'} <= parts
print('PASS native DatePicker template parts present')
