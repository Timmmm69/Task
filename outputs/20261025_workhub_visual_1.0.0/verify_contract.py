from pathlib import Path
import subprocess, re, collections, xml.etree.ElementTree as E
p=Path('work/production/src/Task.Desktop/Views/WorkHubView.xaml'); current=p.read_text(); original=subprocess.check_output(['git','show','3fe76562b3c51fee3e36b933b169b101376afb47:'+str(p)],text=True)
r=E.fromstring(current); old=E.fromstring(original); ns='{http://schemas.microsoft.com/winfx/2006/xaml/presentation}'; x='{http://schemas.microsoft.com/winfx/2006/xaml}'
# Behavior attributes must survive with original multiplicity, independent of containers.
def contract(root):
    keys={'Command','CommandParameter','Click','Text','ItemsSource','SelectedItem','SelectedValue','SelectedValuePath','DisplayMemberPath','IsChecked','IsEnabled','Visibility','DataContext','AutomationProperties.AutomationId','Drop','DragOver','PreviewDrop','PreviewDragOver','DragLeave','PreviewMouseMove','PreviewMouseLeftButtonDown','SelectedItemChanged'}
    return collections.Counter((el.tag,k,v) for el in root.iter() for k,v in el.attrib.items() if k in keys and (k!='Text' or v.startswith('{Binding')))
missing=contract(old)-contract(r); assert not missing, missing
for tag in ['TextBox','ComboBox','Button','CheckBox','Expander','ListBox']:
    els=list(r.iter(ns+tag)); assert all('Style' in el.attrib for el in els),tag
    print(f'PASS explicit styles: {tag} ({len(els)})')
for el in r.iter():
    for child in el:
        prop=child.tag.split('}')[-1]
        if '.' in prop:
            assert prop.split('.')[-1] not in el.attrib, (el.tag,prop)
keys={el.attrib[x+'Key'] for file in Path('work/production/src/Task.Desktop/Resources').glob('*.xaml') for el in E.parse(file).iter() if x+'Key' in el.attrib}
keys|={el.attrib[x+'Key'] for el in r.iter() if x+'Key' in el.attrib}
keys|={'Task.BooleanToVisibilityConverter'}
refs=set(re.findall(r'\{(?:Static|Dynamic)Resource ([\w.]+)\}',current))
assert not refs-keys,refs-keys
print('PASS resource references:',len(refs))
print('PASS original commands, bindings, event handlers, visibility/access gates and automation IDs preserved')
print('PASS XML and duplicate property checks')
subprocess.run(['git','diff','--check'],check=True)
print('PASS git diff --check')
