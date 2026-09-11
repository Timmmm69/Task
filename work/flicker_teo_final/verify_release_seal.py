"""Confirm release stamping changed no financial value or native chart binding."""
from pathlib import Path
import json,math,zipfile,xml.etree.ElementTree as ET
import openpyxl
Q=Path(__file__).resolve().parent/'qa'
a=openpyxl.load_workbook(Q/'Flicker_TEO_visual_baseline.xlsx',data_only=True)
b=openpyxl.load_workbook(Q/'Flicker_TEO_recalculated.xlsx',data_only=True)
allowed={f'{col}{r}' for col in ['C','D'] for r in [19,22,23,24]}
changed=[];count=0
for s in a:
 for row in s:
  for c in row:
   if s.title=='QA' and c.coordinate in allowed:continue
   x,y=c.value,b[s.title][c.coordinate].value;count+=1
   same=math.isclose(x,y,rel_tol=1e-12,abs_tol=.00001) if isinstance(x,(int,float)) and isinstance(y,(int,float)) else x==y
   if not same:changed.append([s.title,c.coordinate,str(x),str(y)])
assert not changed,changed[:20]
for r in [19,22,23,24]:assert b['QA'][f'C{r}'].value=='ПРОВЕРЕНО'
ns={'c':'http://schemas.openxmlformats.org/drawingml/2006/chart'}
def chart_semantics(p):
 with zipfile.ZipFile(p) as z:
  return [[n,[(e.tag.split('}')[-1],e.text,dict(e.attrib)) for e in ET.fromstring(z.read(n)).iter() if e.tag.split('}')[-1] in ['f','ptCount','tickLblPos','orientation','barDir']]] for n in sorted(z.namelist()) if n.startswith('xl/charts/chart') and n.endswith('.xml')]
assert chart_semantics(Q/'Flicker_TEO_visual_baseline.xlsx')==chart_semantics(Q/'Flicker_TEO_recalculated.xlsx')
report={'cells_compared':count,'unintended_value_changes':changed,'chart_bindings_and_axes_unchanged':True,'qa_stamps_passed':True}
(Q/'release_seal_check.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(report)
