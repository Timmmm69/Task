import json
from pathlib import Path
import openpyxl

root = Path(__file__).resolve().parent
source = root / 'source' / 'Flicker_TEO_financial_model (4).xlsx'
wb = openpyxl.load_workbook(source, data_only=False)
cache = openpyxl.load_workbook(source, data_only=True)
out = {}
for ws in wb:
    rows = []
    for row in ws:
        cells = []
        for c in row:
            if c.value is not None:
                cells.append({'cell':c.coordinate, 'value':c.value, 'cached':cache[ws.title][c.coordinate].value})
        if cells: rows.append(cells)
    out[ws.title] = rows
(root/'qa'/'source_cells.json').write_text(json.dumps(out, ensure_ascii=False, indent=2, default=str), encoding='utf-8')
for name, rows in out.items():
    print(name, len(rows), 'rows')
    for row in rows:
        print(' | '.join(c['cell']+': '+str(c['value']) + (' [='+str(c['cached'])+']' if str(c['value']).startswith('=') else '') for c in row))
