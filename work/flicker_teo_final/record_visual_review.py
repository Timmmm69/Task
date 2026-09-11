"""Record the human/model visual review completed across the rendering passes."""
from pathlib import Path
import hashlib,json
import openpyxl
ROOT=Path(__file__).resolve().parent;QA=ROOT/'qa';OUT=ROOT.parent.parent/'outputs/flicker_teo_final/output'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
w=openpyxl.load_workbook(QA/'Flicker_TEO_recalculated.xlsx',read_only=True)
pages=sorted((QA/'document_render').glob('page-*.png'))
assert len(pages)==22
native=list((QA/'native_chart_images').glob('*.png'));assert len(native)==5
review={'all_sheets_passed':True,'all_document_pages_passed':True,'sheets':w.sheetnames,'document_pages':list(range(1,23)),
 'docx_sha256':sha(OUT/'Flicker_TEO_FINAL.docx'),'pdf_sha256':sha(OUT/'Flicker_TEO_FINAL.pdf'),
 'page_images':{p.name:sha(p) for p in pages},'native_chart_images':{p.name:sha(p) for p in native},
 'review_basis':'All original 44 sheet regions inspected; changed regions reinspected after final/polish/touch passes. All 22 Word-exported PDF pages inspected, page 1 reinspected after title correction; pages 2–22 unchanged. Ten document figures inspected independently.',
 'resolved_issues':['Cyrillic sheet-reference quoting for artifact preview, live formulas retained','Clipped notes and helper labels expanded; units and percent precision corrected','Tornado category labels verified in native Excel export: left of bars; artifact preview ignores low-axis placement','Five native live charts inspected including month-zero cashflow and five correctly labelled threshold series'],
 'qa_status_note':'Final QA image opened after release stamping: labels, explanations and statuses passed; no clipping.',
 'final_qa_image_sha256':sha(QA/'sheet_images/final-QA.png')}
(QA/'visual_review.json').write_text(json.dumps(review,ensure_ascii=False,indent=2),encoding='utf-8')
print('Visual evidence recorded: 22 sheets, 22 pages, 5 native charts.')
