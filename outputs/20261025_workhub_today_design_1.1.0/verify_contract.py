from pathlib import Path
from collections import Counter
import subprocess,re,xml.etree.ElementTree as E
P=Path('work/production/src/Task.Desktop/Views/WorkHubView.xaml');NS='{http://schemas.microsoft.com/winfx/2006/xaml/presentation}';X='{http://schemas.microsoft.com/winfx/2006/xaml}'
r=E.parse(P).getroot();old=E.fromstring(subprocess.check_output(['git','show','28aeb4bec75dade0bbe08c7683769ec6f9b2991e:'+str(P)]))
# Compare the contract independently of the new visual wrappers.
properties={'Command','CommandParameter','Click','ItemsSource','SelectedItem','SelectedValue','SelectedValuePath','DisplayMemberPath','IsChecked','IsEnabled','Visibility','DataContext','AutomationProperties.AutomationId','Drop','DragOver','PreviewDrop','PreviewDragOver','DragLeave','PreviewMouseMove','PreviewMouseLeftButtonDown','SelectedItemChanged','MaxLength','AcceptsReturn','AllowDrop'}
def contract(root):
 return Counter((e.tag,k,v) for e in root.iter() for k,v in e.attrib.items() if k in properties or (k in {'Text','Value'} and v.startswith('{Binding')))
assert not contract(old)-contract(r),contract(old)-contract(r)
print('PASS: original commands, bound values, access gates, validation constraints, events and automation IDs preserved')
# DataContext-bound controls keep the same explicit DataContext ancestry.
def contexts(root):
 out=Counter()
 def walk(e,ctx=()):
  if 'DataContext' in e.attrib:ctx+=((e.tag,e.get('DataContext')),)
  for k,v in e.attrib.items():
   if k in properties and v.startswith('{Binding'):out[(e.tag,k,v,ctx)]+=1
  for child in e:walk(child,ctx)
 walk(root);return out
assert not contexts(old)-contexts(r),contexts(old)-contexts(r)
print('PASS: explicit DataContext ancestry preserved')
for e in r.iter():
 for c in e:
  tag=c.tag.split('}')[-1]
  if '.' in tag:assert tag.split('.')[-1] not in e.attrib,(e.tag,tag)
print('PASS: XML and property assignment checks')
# Discover application resources as well as theme dictionaries (including layout converters).
keys={e.get(X+'Key') for path in [Path('work/production/src/Task.Desktop/App.xaml'),*Path('work/production/src/Task.Desktop/Resources').glob('*.xaml')] for e in E.parse(path).iter() if X+'Key' in e.attrib}
keys|={e.get(X+'Key') for e in r.iter() if X+'Key' in e.attrib}
refs=set(re.findall(r'\{(?:Static|Dynamic)Resource ([\w.]+)\}',P.read_text()));assert not refs-keys,refs-keys
print('PASS: all',len(refs),'resource references resolve in view, theme or App.xaml')
ids=[e.get('AutomationProperties.AutomationId') for e in r.iter() if e.get('AutomationProperties.AutomationId')];assert len(ids)==len(set(ids))
print('PASS: unique automation IDs')
for tag in ['TextBox','ComboBox','Button','CheckBox','Expander','ListBox']:
 controls=list(r.iter(NS+tag));assert all('Style' in e.attrib or e.find(NS+tag+'.Style') is not None for e in controls)
 print('PASS: explicit control styling:',tag,len(controls))
subprocess.run(['git','diff','--check'],check=True)
print('PASS: git diff --check')
print('NOT RUN: WPF rendering, keyboard, high contrast, resize and scaling checks require Windows')
# Layout invariants: both columns must share star units, narrow panels must use separate rows.
styles={e.get(X+'Key'):e for e in r.iter(NS+'Style') if e.get(X+'Key')}
def setters(style):return {e.get('Property'):e.get('Value') for e in styles[style].findall(NS+'Setter')}
assert setters('WorkHub.LeftColumn')['Width']=='38*'
assert setters('WorkHub.RightColumn')['Width']=='62*'
assert setters('WorkHub.Master')['Grid.Column']=='0'
assert setters('WorkHub.Detail')['Grid.Column']=='1'
triggers=styles['WorkHub.Detail'].find(NS+'Style.Triggers')
assert any(e.get('Property')=='Grid.Row' and e.get('Value')=='1' for e in triggers.iter(NS+'Setter'))
assert any(e.get('Property')=='Grid.Column' and e.get('Value')=='0' for e in triggers.iter(NS+'Setter'))
# Every split with a responsive right rail must have a responsive left rail too.
for cols in r.iter(NS+'Grid.ColumnDefinitions'):
 right=any(e.get('Style')=='{StaticResource WorkHub.RightColumn}' or any(d.get('BasedOn')=='{StaticResource WorkHub.RightColumn}' for d in e.iter(NS+'Style')) for e in cols)
 if right:assert cols[0].get('Style')=='{StaticResource WorkHub.LeftColumn}'
print('PASS: split proportions and distinct narrow-screen rows')
