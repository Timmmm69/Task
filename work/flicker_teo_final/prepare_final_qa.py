"""Read-only snapshots for native-cache rendering and document page comparison."""
import hashlib, json, re
from pathlib import Path
import openpyxl
ROOT=Path(__file__).resolve().parent; QA=ROOT/'qa'
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
pages={p.name:sha(p) for p in (QA/'document_render').glob('page-*.png')}
if not (QA/'document_pages_before.json').exists():
    (QA/'document_pages_before.json').write_text(json.dumps(pages,indent=2))
w=openpyxl.load_workbook(QA/'Flicker_TEO_recalculated.xlsx',data_only=False)
changes=[]
pattern=re.compile(r"(?<!['\w])([А-Яа-яЁё][А-Яа-яЁё0-9_]*)!")
for s in w:
    for row in s:
        for c in row:
            if c.data_type=='f':
                f=pattern.sub(r"'\1'!",c.value)
                if f!=c.value: changes.append([s.title,c.coordinate,f])
(QA/'quoted_formulas.json').write_text(json.dumps(changes,ensure_ascii=False),encoding='utf-8')
print('Quote-normalization cells:',len(changes))
