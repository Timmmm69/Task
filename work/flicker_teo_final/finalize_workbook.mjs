import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {FileBlob,SpreadsheetFile} from '@oai/artifact-tool';
const root=path.dirname(fileURLToPath(import.meta.url));
const wb=await SpreadsheetFile.importXlsx(await FileBlob.load(path.join(root,'qa/Flicker_TEO_recalculated.xlsx')));
const sh=n=>wb.worksheets.getItem(n);
const put=(n,a,v)=>{const r=sh(n).getRange(a);if(typeof v==='string'&&v.startsWith('='))r.formulas=[[v]];else r.values=[[v]];};
const navy='#203864',blue='#235AC4',red='#A61C32';
const money='#,##0;[Red](#,##0);0',dec='#,##0.00;[Red](#,##0.00);0.00',pct='0.00%;[Red](0.00%);0.00%';
const seal=process.argv.includes('--seal');
const polish=process.argv.includes('--polish');
const touch=process.argv.includes('--touch');
const renderJobs=[];
function note(n,row,t){put(n,`B${row}`,t);sh(n).mergeCells(`B${row}:I${row+1}`);sh(n).getRange(`B${row}:I${row+1}`).format={wrapText:true,rowHeight:24,font:{name:'Arial',size:10,color:'#4B5563'}};}
// Excel legally removes quotes from simple Cyrillic worksheet names. The
// artifact renderer requires quoted Cyrillic references. Restore only syntax,
// retaining live formulas and native Excel calculation semantics.
for(const [n,a,f] of JSON.parse(await fs.readFile(path.join(root,'qa/quoted_formulas.json'),'utf8')))put(n,a,f);
if(touch){
 sh('Чувствительность').getRange('J:J').format.columnWidth=34;
 sh('Чувствительность').getRange('J45:L45').format={wrapText:true,rowHeight:40};
 const tor=sh('Чувствительность').charts.items[0];
 tor.xAxis={tickLabelPosition:'low',orientation:'maxMin',majorGridlines:null,minorGridlines:null,textStyle:{typeface:'Arial',fontSize:12}};
 tor.yAxis={numberFormatCode:money,numberFormatSourceLinked:false,majorUnit:25000000};
 sh('Денежный поток').charts.items[0].xAxis={tickLabelInterval:6,tickLabelPosition:'low',majorGridlines:null,minorGridlines:null};
 sh('Пороги').charts.items[0].xAxis={tickLabelPosition:'low',majorGridlines:null,minorGridlines:null};
 sh('Вероятностный анализ').getRange('AN11:AR11').values=[['Команда с резервом ₽/мес.','Опросы ₽/мес. при 100%','Руководители и обучение ₽/мес.','Прямые с резервом ₽/мес.','Прямые разовые с резервом ₽/этап']];
 sh('Вероятностный анализ').getRange('AN11:AR11').format.verticalAlignment='center';
 for(const [sheet,range] of [['Чувствительность','J1:T16'],['Денежный поток','Y2:AJ23'],['Пороги','K1:T21'],['Вероятностный анализ','AN11:AR20']])renderJobs.push({sheet,range,file:`touch-${sheet}.png`});
 renderJobs.push({sheet:'Чувствительность',range:'J45:L52',file:'touch-tornado-helper.png'});
}else if(polish){
 sh('Прямые расходы').getRange('B26:J29').format.rowHeight=90;
 sh('Прямые расходы').getRange('F7:F18').setNumberFormat(money);
 sh('Прямые расходы').getRange('C29').setNumberFormat(pct);
 sh('Время участников').getRange('E7:E30').setNumberFormat(pct);
 sh('Сценарии').getRange('C42:D45').setNumberFormat(money);
 sh('Альтернативы').getRange('C7:D11').setNumberFormat(money);
 sh('Альтернативы').getRange('F7:F11').setNumberFormat(dec);
 sh('Альтернативы').getRange('G7:G11').setNumberFormat(money);
 sh('Валидация').getRange('C7:C8').setNumberFormat(money);
 sh('Валидация').getRange('C21').setNumberFormat(money);
 for(const r of [10,11,12,13,16,20,22])sh('Валидация').getRange(`C${r}`).setNumberFormat(pct);
 sh('Валидация').getRange('C17:C19').setNumberFormat('0.000000;[Red](0.000000);0.000000');
 sh('Аудит').getRange('C13:D14').setNumberFormat(pct);
 sh('Аудит').getRange('E7:E14').setNumberFormat('0.000000000;[Red](0.000000000);0.000000000');
 sh('Аудит').getRange('C25').setNumberFormat(pct);
 sh('Этапы').getRange('C18:E23').setNumberFormat(money);
 put('Команда','V6','Новый cash ₽/мес. за 1 FTE');
 put('Команда','W6','Существующий / перераспределяемый ресурс ₽/мес. за 1 FTE');
 sh('Команда').getRange('U6:Y6').format.rowHeight=95;
 for(const n of ['Чувствительность','Пороги']){
  const a=n==='Пороги'?32:23,b=n==='Пороги'?56:37;
  sh(n).getRange(`C${a}:C${b}`).setNumberFormat(money);
  sh(n).getRange(`D${a}:F${b}`).setNumberFormat(pct);
  sh(n).getRange(`H${a}:H${b}`).setNumberFormat(pct);
  sh(n).getRange(`L${a}:AB${b}`).setNumberFormat(money);
 }
 for(const r of [9,10,11,13])sh('Чувствительность').getRange(`C${r}:E${r}`).setNumberFormat(pct);
 sh('Чувствительность').getRange('K46:L52').setNumberFormat(money);
 const tor=sh('Чувствительность').charts.items[0];
 tor.barOptions.direction='bar';tor.barOptions.grouping='stacked';tor.barOptions.overlap=100;
 // Proper range-backed threshold chart: text headers name exit-cost series;
 // equidistant RRR labels avoid a spurious header-as-data point.
 const n='Пороги';sh(n).charts.deleteAll();put(n,'AL5','Причинный RRR');
 for(let j=0;j<5;j++){
  const c=String.fromCharCode(67+j),dst=['AM','AN','AO','AP','AQ'][j];
  put(n,`${dst}5`,`=TEXT(${c}6,"#,##0")&" ₽/уход"`);
  for(let i=0;i<5;i++)put(n,`${dst}${6+i}`,`=${c}7+(${c}11-${c}7)*${i}/4`);
 }
 for(let i=0;i<5;i++)put(n,`AL${6+i}`,`${i*25}%`);
 sh(n).getRange('AL:AQ').format.columnWidth=25;
 sh(n).getRange('AM6:AQ10').setNumberFormat(money);
 const ch=sh(n).charts.add('line',sh(n).getRange('AL5:AQ10'));
 ch.setPosition('K3','T18');ch.title='NPV ₽ при причинном RRR и стоимости ухода';
 ch.titleTextStyle.typeface='Arial';ch.titleTextStyle.fontSize=13;
 ch.legend={position:'top',textStyle:{typeface:'Arial',fontSize:11}};
 ch.yAxis={numberFormatCode:money,numberFormatSourceLinked:false};
 ch.series.items.forEach((s,i)=>s.line={fill:[navy,'#4472C4','#7F8CA3','#C39224',red][i],style:'solid',width:2});
 sh(n).getRange('C6:G6').setNumberFormat(money);
 const mc='Вероятностный анализ';
 sh(mc).getRange('F20:H27').format.wrapText=true;
 sh(mc).getRange('F:F').format.columnWidth=30;sh(mc).getRange('G:G').format.columnWidth=32;sh(mc).getRange('H:H').format.columnWidth=24;
 sh(mc).getRange('B21:H27').format.rowHeight=55;
 sh(mc).getRange('C11:C13').setNumberFormat(pct);
 sh(mc).getRange('C22:E22').setNumberFormat(money);
 for(const r of [23,24,25,27])sh(mc).getRange(`C${r}:E${r}`).setNumberFormat(pct);
 sh(mc).getRange('AN:AR').format.columnWidth=24;
 sh(mc).getRange('AN11:AR11').values=[['S0–S5 команда ₽/этап','Опросы ₽/мес. при 100%','Руководители ₽/мес.','Обучение ₽/этап','Прямые ₽/этап']];
 sh(mc).getRange('AN11:AR11').format={fill:navy,font:{color:'#FFFFFF',bold:true},wrapText:true,rowHeight:60};
 sh(mc).getRange('AN12:AR17').setNumberFormat(money);
 sh(mc).getRange('AC43:AE43').values=[['','','']];
 put('Вводные','L46','Исключена во всех анализах версии 1.0; фиксировано 0. Стоимость невалидированного IP не признаётся.');
 sh('Вводные').getRange('D46:G46').format.font.color='#111111';
 sh('Вводные').getRange('D46:G46').dataValidation={rule:{type:'whole',operator:'between',formula1:0,formula2:0},errorAlert:{style:'stop',title:'Остаточная стоимость исключена',message:'В версии 1.0 фиксировано 0; иная оценка требует расширения модели и повторного QA.'}};
 for(const [sheet,ranges] of Object.entries({
  'Прямые расходы':['A1:J31'],'Время участников':['A1:L40'],'Сценарии':['A1:I47'],
  'Альтернативы':['A1:I25'],'Валидация':['A1:I43'],'Аудит':['A1:I34'],
  'Вводные':['B42:L53'],'Команда':['U5:Y20'],'Панель':['A1:I42'],'Этапы':['A1:K38'],
  'Чувствительность':['A1:I20','K1:T16','J45:L52','B22:K40','L22:AE38'],
  'Пороги':['A1:I27','K1:T21','B30:K56','L30:AE56','AL5:AQ10'],
  'Вероятностный анализ':['A1:I42','AN11:AR20','Z43:AJ52'],
  'Денежный поток':['Y2:AJ23']
 }))ranges.forEach((range,i)=>renderJobs.push({sheet,range,file:`polish-${sheet}-${i+1}.png`}));
}else if(!seal){
 put('Пороги','C22','=IF(NOT(ISNUMBER(\'Удержание\'!D17)),"не рассчитано",IF(\'Удержание\'!D17<=0,"недостижимо",\'Сценарии\'!D14/\'Удержание\'!D17))');
 put('Пороги','C23','=IF(OR(\'Чувствительность\'!X23<=0,\'Удержание\'!D20<=0),"недостижимо",\'Чувствительность\'!Y23/(\'Чувствительность\'!X23/\'Удержание\'!D20))');
 sh('Пороги').getRange('C19:C24').setNumberFormat(dec);sh('Пороги').getRange('C21').setNumberFormat(pct);
 for(let i=0;i<5;i++)for(let j=0;j<5;j++){
  const r=32+i*5+j;put('Пороги',`C${r}`,`=$${String.fromCharCode(67+j)}$6`);put('Пороги',`F${r}`,`=$B$${7+i}`);
 }
 sh('Пороги').getRange('B7:B11').format.font.color=blue;
 sh('Вводные').getRange('C8:G53').setNumberFormat(dec);
 for(let r=8;r<=53;r++){
  const unit=String(sh('Вводные').getRange(`H${r}`).values[0][0]);
  if(unit.startsWith('%')||r===42)sh('Вводные').getRange(`C${r}:G${r}`).setNumberFormat(pct);
 }
 for(const r of [8,20,28,29,30,34,37,40,41,46,50,51,52,53])sh('Вводные').getRange(`C${r}:G${r}`).setNumberFormat(money);
 sh('Вводные').getRange('C5').setNumberFormat('yyyy-mm-dd');
 put('Вводные','D5','Дата текстом');put('Вводные','E5','=TEXT(C5,"yyyy-mm-dd")');
 put('Вводные','L8','Одинаковая аудитория вне команды поставки Flicker для исключения двойного учёта времени. Фактическая численность неизвестна.');
 sh('Панель').getRange('C38').setNumberFormat(money);
 put('Обложка','B2','Flicker — технико-экономическое обоснование');
 // Classification is per role; existing budget and reallocated resource are the
 // same amount, not two cost lines to add together.
 const headers=['Роль','Новый cash ₽/мес.','Существующий бюджет и перераспределяемый ресурс ₽/мес.','Характер','Статус и владелец'];
 sh('Команда').getRange('U6:Y6').values=[headers];
 sh('Команда').getRange('U6:Y6').format={fill:navy,font:{name:'Arial',size:10,color:'#FFFFFF',bold:true},wrapText:true,rowHeight:80};
 for(let r=7;r<=19;r++){
  put('Команда',`U${r}`,`=B${r}`);put('Команда',`V${r}`,`=H${r}*'Вводные'!$C$31`);put('Команда',`W${r}`,`=H${r}-V${r}`);
  put('Команда',`X${r}`,'Регулярный ресурс; OPEX для планирования; учёт капитализации согласовать');
  put('Команда',`Y${r}`,'Допущение A01/P02; C&B и Finance');
 }
 sh('Команда').getRange('U:U').format.columnWidth=48;sh('Команда').getRange('V:W').format.columnWidth=26;sh('Команда').getRange('X:Y').format.columnWidth=34;
 sh('Команда').getRange('U7:Y19').format={wrapText:true,rowHeight:55};sh('Команда').getRange('V7:W19').setNumberFormat(money);
 note('Время участников',38,'N исключает команду поставки Flicker. Опросы, действия руководителей и работа HRBP — разные активности. Сверять фактические часы с FTE; существующий бюджет и перераспределяемый ресурс не суммировать повторно.');
 put('Этапы','E17','Накопленный ресурс по плану ₽');
 put('Этапы','J17','Жёсткий лимит ресурса ₽');
 put('Этапы','J18',1800000);for(let r=19;r<=23;r++)put('Этапы',`J${r}`,'неизвестно до gate');
 sh('Этапы').getRange('J:J').format.columnWidth=27;sh('Этапы').getRange('J17:J23').format.wrapText=true;sh('Этапы').getRange('J18').setNumberFormat(money);
 sh('Этапы').getRange('J17').format={fill:navy,font:{color:'#FFFFFF',bold:true},rowHeight:60};
 note('Этапы',36,'Накопленная оценка в ценах месяца 0 не является гарантированным максимумом ущерба. Предлагаемый жёсткий лимит S0 — 1,80 млн ₽; лимиты следующих этапов неизвестны. Новые обязательства запрещены без отдельного решения.');
 // A native tornado chart displays deviations from the baseline, not competing
 // totals from zero. Ranked helpers remain live when a sensitivity bound changes.
 sh('Чувствительность').getRange('J45:L45').values=[['Драйвер','Нижнее отклонение ₽','Верхнее отклонение ₽']];
 for(let r=46;r<=52;r++){
  const ix=`MATCH(LARGE($H$7:$H$13,${r-45}),$H$7:$H$13,0)`;
  put('Чувствительность',`J${r}`,`=INDEX($B$7:$B$13,${ix})`);
  put('Чувствительность',`K${r}`,`=MIN(INDEX($F$7:$F$13,${ix}),INDEX($G$7:$G$13,${ix}))-$Z$23`);
  put('Чувствительность',`L${r}`,`=MAX(INDEX($F$7:$F$13,${ix}),INDEX($G$7:$G$13,${ix}))-$Z$23`);
 }
 sh('Чувствительность').charts.deleteAll();
 const tornado=sh('Чувствительность').charts.add('bar',{barOptions:{direction:'bar',grouping:'stacked',gapWidth:65}});
 tornado.setData(sh('Чувствительность').getRange('J45:L52'));tornado.setPosition('K3','T15');tornado.title='Tornado — отклонение NPV от базы ₽';
 tornado.titleTextStyle.typeface='Arial';tornado.titleTextStyle.fontSize=13;
 tornado.legend={position:'top',textStyle:{typeface:'Arial',fontSize:11}};
 tornado.series.items[0].fill=red;tornado.series.items[1].fill=navy;
 tornado.yAxis={numberFormatCode:money,numberFormatSourceLinked:false,textStyle:{typeface:'Arial',fontSize:10}};
 // Cashflow chart explicitly includes month zero and has informative series names.
 sh('Денежный поток').getRange('AL5:AN5').values=[['Месяц','Денежный поток ₽','Экономический поток ₽']];
 for(let r=6;r<=66;r++){
  const src=68+r-6;put('Денежный поток',`AL${r}`,`="М"&C${src}`);put('Денежный поток',`AM${r}`,`=S${src}`);put('Денежный поток',`AN${r}`,`=T${src}`);
 }
 sh('Денежный поток').charts.deleteAll();
 const cf=sh('Денежный поток').charts.add('line',sh('Денежный поток').getRange('AL5:AN66'));
 cf.setPosition('Y3','AJ22');cf.title='Накопленный базовый поток ₽';cf.titleTextStyle.typeface='Arial';cf.titleTextStyle.fontSize=13;
 cf.legend={position:'top',textStyle:{typeface:'Arial',fontSize:11}};cf.yAxis={numberFormatCode:money,numberFormatSourceLinked:false};
 cf.series.items.forEach((s,i)=>s.line={fill:i===0?'#C39224':navy,style:'solid',width:2});
 sh('Вероятностный анализ').getRange('C30').format.font.color='#111111';
 sh('Вероятностный анализ').getRange('C30').dataValidation={rule:{type:'whole',operator:'between',formula1:20260908,formula2:20260908},errorAlert:{style:'stop',title:'Фиксированный seed',message:'Эта версия использует фиксированный seed 20260908. Изменяйте распределения в C21:E27.'}};
 for(const [sheet,ranges] of Object.entries({
  'Обложка':['A1:I28'],'Панель':['A1:I42'],'Вводные':['A1:L26','B27:L53','A55:I67'],
  'Команда':['U5:Y20'],'Время участников':['A34:I40'],'Этапы':['A1:K38'],
  'Чувствительность':['A1:I20','K1:T16','J45:L52'],'Пороги':['A1:I27','B30:K56'],
  'Денежный поток':['Y2:AJ23'],'Вероятностный анализ':['A1:I42']
 }))ranges.forEach((range,i)=>renderJobs.push({sheet,range,file:`final-${sheet}-${i+1}.png`}));
}else{
 const visual=JSON.parse(await fs.readFile(path.join(root,'qa/visual_review.json'),'utf8'));
 const tests=JSON.parse((await fs.readFile(path.join(root,'qa/input_tests.json'),'utf8')).replace(/^\uFEFF/,''));
 if(!visual.all_sheets_passed||!visual.all_document_pages_passed||tests.some(t=>!t.passed))throw new Error('Release evidence is incomplete');
 for(const r of [19,22,23,24])put('QA',`C${r}`,'ПРОВЕРЕНО');
 put('QA','D19','Полный скан формул после пересчёта Excel; независимый расчёт и граничные тесты.');
 put('QA','D22','DOCX и PDF созданы из сверенного набора результатов; числовая сверка зафиксирована в аудите.');
 put('QA','D23','Проверены все 22 листа и страницы документа; подробное покрытие в итоговом аудите.');
 put('QA','D24','SHA-256 двух оригиналов и рабочих копий совпадают; manifest содержит финальные хеши.');
 renderJobs.push({sheet:'QA',range:'A1:I29',file:'final-QA.png'});
}
wb.recalculate();
console.log((await wb.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#NUM!|#N/A',options:{useRegex:true,maxResults:15},maxChars:2000})).ndjson);
const x=await SpreadsheetFile.exportXlsx(wb);await x.save(path.join(root,'qa/Flicker_TEO_working.xlsx'));
console.log('Patched candidate exported.');
const dir=path.join(root,'qa/sheet_images');
for(const job of renderJobs){
 const blob=await wb.render({sheetName:job.sheet,range:job.range,scale:1.25,format:'png'});
 await fs.writeFile(path.join(dir,job.file),new Uint8Array(await blob.arrayBuffer()));console.log('Rendered '+job.file);
}
await fs.writeFile(path.join(dir,seal?'seal-index.json':touch?'touch-index.json':polish?'polish-index.json':'final-index.json'),JSON.stringify(renderJobs,null,2));
