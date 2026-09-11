"""Last read-only release gate, after audit creation."""
from pathlib import Path
import hashlib,json
R=Path(__file__).resolve().parent;Q=R/'qa';O=R.parent.parent/'outputs/flicker_teo_final/output'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
m=json.loads((O/'manifest.json').read_text(encoding='utf-8'))
for f in m['files']:
 p=O/f['name'];assert p.stat().st_size==f['bytes'];assert sha(p)==f['sha256']
assert sha(O/'Flicker_TEO_FINAL.xlsx')==sha(Q/'Flicker_TEO_recalculated.xlsx')
assert m['validation']['status']=='passed'
print(json.dumps({'manifest_verified':True,'files':m['files'],'validation':m['validation']},ensure_ascii=False,indent=2))
