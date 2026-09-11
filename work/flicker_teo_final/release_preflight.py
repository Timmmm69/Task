"""Read-only final XML integrity and full DOCX/PDF numeric-token checks."""
from pathlib import Path
import json,re,zipfile,xml.etree.ElementTree as ET
from collections import Counter
from docx import Document
from pypdf import PdfReader
ROOT=Path(__file__).resolve().parent;QA=ROOT/'qa';OUT=ROOT.parent.parent/'outputs/flicker_teo_final/output'
w=QA/'Flicker_TEO_recalculated.xlsx'
ns={'c':'http://schemas.openxmlformats.org/drawingml/2006/chart','m':'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
with zipfile.ZipFile(w) as z:
    charts=[];errors=[];formula_count=0;long=[]
    assert not any(n.startswith('xl/externalLinks/') for n in z.namelist())
    for n in z.namelist():
        if re.fullmatch(r'xl/worksheets/sheet\d+\.xml',n):
            tree=ET.fromstring(z.read(n))
            for c in tree.findall('.//m:c',ns):
                if c.get('t')=='e':errors.append([n,c.get('r'),c.findtext('m:v',namespaces=ns)])
                f=c.find('m:f',ns)
                if f is not None:
                    formula_count+=1
                    if len(f.text or '')>8192:long.append([n,c.get('r')])
        if re.fullmatch(r'xl/charts/chart\d+\.xml',n):
            tree=ET.fromstring(z.read(n));series=[]
            for s in tree.findall('.//c:ser',ns):
                series.append({'refs':[f.text for f in s.findall('.//c:f',ns)],'points':[int(e.get('val')) for e in s.findall('.//c:ptCount',ns)],'name':s.findtext('c:tx/c:strRef/c:strCache/c:pt/c:v',namespaces=ns)})
            charts.append({'file':n,'series':series,'bar_direction':[e.get('val') for e in tree.findall('.//c:barDir',ns)]})
    assert not errors,errors[:5]
    assert not long,long[:5]
    assert len(charts)==5,len(charts)
doc=Document(OUT/'Flicker_TEO_FINAL.docx')
dt=' '.join([p.text for p in doc.paragraphs]+[c.text for t in doc.tables for row in t.rows for c in row.cells])
pdf=PdfReader(OUT/'Flicker_TEO_FINAL.pdf');pt=' '.join(p.extract_text() or '' for p in pdf.pages)
pat=re.compile(r'(?<![\w])(?:[−–-]?\d{1,3}(?: \d{3})+|[−–-]?\d+)(?:[,.]\d+)?')
def tokens(t):
    t=re.sub(r'\s+',' ',t)
    return Counter(x.replace(' ','').replace('−','-').replace('–','-').replace(',','.') for x in pat.findall(t))
missing=tokens(dt)-tokens(pt)
report={'formula_count':formula_count,'formula_errors':errors,'overlong_formulas':long,'charts':charts,'docx_numeric_tokens':sum(tokens(dt).values()),'missing_numeric_in_pdf':dict(missing),'document_pages':len(pdf.pages)}
(QA/'release_preflight.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
assert not missing,missing
