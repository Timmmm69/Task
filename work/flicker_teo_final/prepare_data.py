"""Read-only source extraction and independent reproduction of the OLD model."""
import json, math
from pathlib import Path
import openpyxl

ROOT=Path(__file__).resolve().parent
wb=openpyxl.load_workbook(ROOT/'source'/'Flicker_TEO_financial_model (4).xlsx',data_only=True)
def gross(net):
    # Independent forward tax integration and bisection, not source inverse formula.
    def tax(g):
        edges=[0,2400000,5000000,20000000,50000000,float('inf')]
        return sum(max(0,min(g,edges[i+1])-edges[i])*r for i,r in enumerate([.13,.15,.18,.20,.22]))
    lo,hi=net,net*2
    for _ in range(100):
        mid=(lo+hi)/2
        if mid-tax(mid)<net: lo=mid
        else: hi=mid
    return (lo+hi)/2

roles=[]
for i in range(6,19):
    w=wb['Команда']; annual=gross(w[f'D{i}'].value*12)
    contributions=min(annual,2979000)*.3+max(annual-2979000,0)*.151
    original=(annual*1.15+contributions)/12
    assert abs(original-w[f'H{i}'].value)<.01
    j=i-6
    roles.append(dict(name=w[f'A{i}'].value,coefficient=w[f'B{i}'].value,
        gross_annual=annual,old_monthly=original,
        fte=[wb['Команда'].cell(42+j,c).value for c in range(2,6)]))
leavers=[]
for i in range(6,14):
    w=wb['Стоимость ухода']
    leavers.append(dict(name=w[f'A{i}'].value,share=w[f'B{i}'].value,pay=w[f'C{i}'].value,hours=w[f'F{i}'].value))
old_exit=sum(r['share']*(r['pay']*(.3+2*.3+.15+3*.3)+r['pay']/r['hours']*(25+30)) for r in leavers)
old_team=[sum(r['old_monthly']*r['fte'][s] for r in roles)*m for s,m in enumerate([3,4,5,12])]
old_direct=[295000,220000,275000,660000]
old_cost=[a+b for a,b in zip(old_team,old_direct)]
old_count=10000*.08*.4*.35*1*.7*.9*.7*.8*.3
old_benefit=old_count*old_exit
baseline={
 'Инвестиции до масштабирования':(sum(old_cost[:3]),'Панель','B6'),
 'Годовая эксплуатация':(old_cost[3],'Панель','D6'),
 'Годовой эффект удержания':(old_benefit,'Панель','F6'),
 'Годовой чистый эффект':(old_benefit-old_cost[3],'Панель','H6'),
 'Предотвращённые уходы':(old_count,'Панель','B9'),
 'Безубыточность по уходам':(old_cost[3]/old_exit,'Панель','F9'),
 'Требуемый совокупный эффект':(old_cost[3]/old_exit/112,None,None),
 'Текущий совокупный эффект':(1*.7*.9*.7*.8*.3,'Удержание','C21')}
audit=[]
for metric,(value,sheet,cell) in baseline.items():
    saved=wb[sheet][cell].value if sheet else wb['Панель']['F9'].value/112
    diff=value-saved
    assert abs(diff)<.01
    audit.append(dict(metric=metric,independent=value,cached=saved,difference=diff,source=f'{sheet}!{cell}' if sheet else 'Панель!F9 / Удержание!C12'))
data={'roles':roles,'leavers':leavers,'old_audit':audit,'old_stage_costs':old_cost,
      'data_requests':[[wb['Запрос данных'].cell(i,c).value for c in [1,2,3,6,7]] for i in range(6,27)]}
(ROOT/'qa'/'model_data.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
for a in audit: print(a['metric'],round(a['independent'],6),'difference',round(a['difference'],9))
print('All eight baseline metrics independently reproduced.')
