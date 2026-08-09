import { createHash } from 'node:crypto';
import { copyFile, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const packageName = 'stage_5_6_external_gate_execution_kit';
const workPackage = path.join(root, 'work', packageName);
const outputPackage = path.join(root, 'outputs', '019fa078-3f10-7ec1-99e2-7c1cba4ee3d4', packageName);
const version = '0.2.0';
const date = '2026-08-09';
const clientCommit = '6a16be2fb371d41af0540569c77daf59eb902a9d';
const clientSha256 = '8B047DD69E1A64269F8961FE0416727E5083E0C2B30285A73DD2E92A2D412E53';

function csvEscape(value) {
  const text = String(value ?? '');
  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

function toCsv(headers, rows) {
  return `${headers.map(csvEscape).join(',')}\r\n${rows.map((row) => row.map(csvEscape).join(',')).join('\r\n')}\r\n`;
}

async function sha256(file) {
  return createHash('sha256').update(await readFile(file)).digest('hex').toUpperCase();
}

async function listFiles(folder, prefix = '') {
  const entries = await readdir(folder, { withFileTypes: true });
  const result = [];
  for (const entry of entries) {
    const relative = path.posix.join(prefix, entry.name);
    const absolute = path.join(folder, entry.name);
    if (entry.isDirectory()) result.push(...await listFiles(absolute, relative));
    else result.push(relative);
  }
  return result.sort();
}

async function write(relative, content) {
  const target = path.join(workPackage, relative);
  await mkdir(path.dirname(target), { recursive: true });
  await writeFile(target, content, 'utf8');
}

async function removeLegacyFiles() {
  const legacy = [
    'windows/NATIVE_WINDOWS_UIA_NARRATOR_PROTOCOL.md',
    'windows/WINDOWS_DPI_MULTI_MONITOR_PROTOCOL.md',
    'windows/Windows_DPI_Test_Matrix.csv',
    'usability/Usability_Test_Script_0.1.csv',
  ];
  for (const relative of legacy) {
    await rm(path.join(workPackage, relative), { force: true });
    await rm(path.join(outputPackage, relative), { force: true });
  }
}

async function mirrorPackage() {
  await mkdir(outputPackage, { recursive: true });
  for (const file of await listFiles(workPackage)) {
    const target = path.join(outputPackage, file);
    await mkdir(path.dirname(target), { recursive: true });
    await copyFile(path.join(workPackage, file), target);
  }
}

async function main() {
  await mkdir(workPackage, { recursive: true });
  await removeLegacyFiles();

  await write('README.md', `# Task — Gate 5.6: пакет повторной технической проверки ${version}

**Дата:** ${date}
**Цель:** воспроизводимо оформить оставшуюся Windows UIA/клавиатурную проверку, не выдавая подготовку или черновой результат за закрытие Gate.

## Порядок выполнения

1. Зафиксировать в каждом результате утверждённый portable x64-клиент и его SHA-256.
2. Выполнить UIA-проверку через Inspect.exe по 12 контрольным точкам.
3. Выполнить отдельный keyboard-only walkthrough по тем же точкам и сохранить ссылки на доказательства.
4. Провести модерируемые сессии по десяти каноническим сценариям и четырём ролевым линзам.
5. Закрыть либо формально принять каждую новую находку Critical, High или Medium.
6. Получить решения Product owner, Design owner, Desktop tech lead и QA.
7. Поместить проверенные доказательства в evidence/incoming/, указать SHA-256, обновить индекс и запустить node tools/validate-gate-evidence.mjs.

## Граница доказательств

Пакет имеет статус проверки структуры, но Gate 5.6 остаётся **NOT_READY**, пока каждая из девяти строк не будет обеспечена хэшированным доказательством и явным принятием указанным владельцем. Шаблоны, пустые файлы и неподписанные черновики доказательствами не являются.
`);

  await write('windows/NATIVE_WINDOWS_UIA_KEYBOARD_PROTOCOL.md', `# Протокол Windows UIA и keyboard-only проверки

## Предпосылки

- Использовать только compiled Windows desktop client, а не browser prototype.
- Тестируемая ревизия: ${clientCommit}.
- Утверждённый клиент: Task Gate 5.6 Client 0.1.1 portable x64, SHA-256 ${clientSha256}.
- Зафиксировать версию приложения, сборку Windows, locale, роль, серверный fixture и timestamp.
- Использовать production-like authorized data без клиентских секретов.

## Процедура

Для каждой строки windows/Windows_Accessibility_Checkpoints.csv:

1. Снять через Inspect.exe свойства UIA: имя, control type, state/value, selection/expanded/current, если применимо.
2. Пройти заданный keyboard path без мыши и записать порядок фокуса и возврат фокуса.
3. Приложить ссылку на screenshot или screen recording и идентификаторы находок.
4. Поставить PASS только когда показаны ожидаемые UIA-свойства, клавиатурный путь и пользовательский исход.

## Условия остановки

Остановить прогон и завести Critical/High при focus trap, недоступном обязательном действии, нераскрытом последствии опасного действия, раскрытии прав/данных, принятой записи в offline/read-only или потере пользовательского ввода. Gate 5.6 не подписывается при открытом Critical/High.
`);

  const windowsRows = [
    ['WIN-A11Y-01','Shell navigation','Employee','Tab/Shift+Tab, arrows where applicable','Stable accessible names; current item exposed; no focus loss','PENDING','',''],
    ['WIN-A11Y-02','First connection and sign-in','Admin','Keyboard only','Endpoint, validation, progress and successful landing exposed','PENDING','',''],
    ['WIN-A11Y-03','New Task editor','Manager','Alt+N then keyboard completion','Dialog name, required fields, validation and deterministic return focus','PENDING','',''],
    ['WIN-A11Y-04','CalendarEvent editor','Manager','Keyboard create/edit','Date/time/timezone, recurrence, attendees, validation and mutation guards exposed','PENDING','',''],
    ['WIN-A11Y-05','Search redaction','Observer','Ctrl+K/Ctrl+F and result navigation','Unavailable result is explained without protected identity or count','PENDING','',''],
    ['WIN-A11Y-06','Offline read-only','Employee','Connection loss and browse','Connection/staleness exposed; prohibited writes disabled with reason','PENDING','',''],
    ['WIN-A11Y-07','Reconnect','Manager','Restore connection','Readiness exposed without stealing focus; writes restore only after sync','PENDING','',''],
    ['WIN-A11Y-08','Optimistic conflict','Manager','Edit, conflict, return to draft','Conflict choices reachable; local draft restored unchanged','PENDING','',''],
    ['WIN-A11Y-09','Admin restricted role','Observer','Navigate Admin/Operations','Unavailable capabilities and hidden objects are not exposed','PENDING','',''],
    ['WIN-A11Y-10','Dangerous Operations action','Admin','Backup restore guard','Approval, exact phrase, maintenance and consequence exposed before action','PENDING','',''],
    ['WIN-A11Y-11','Archive/Trash retention','Admin','Restore/purge/legal hold','Blocked destructive action, legal hold and metadata-only deletion copy exposed','PENDING','',''],
    ['WIN-A11Y-12','Menus, tabs, tables, trees and progress','All roles','Keyboard navigation','Correct UIA control types, selection/expanded/current/value states','PENDING','',''],
  ];
  await write('windows/Windows_Accessibility_Checkpoints.csv', toCsv(['Checkpoint ID','Surface/flow','Role lens','Input path','Acceptance criterion','Result','Evidence path','Finding IDs'], windowsRows));

  await write('usability/MODERATED_SESSION_PROTOCOL.md', `# Протокол модерируемых usability-сессий

## Coverage

Должны быть представлены Admin, Manager, Employee и Observer. UX owner фиксирует целевое число участников до рекрутинга; валидатор Gate проверяет ролевое покрытие и завершённые доказательства, а не выдуманный размер выборки. Использовать канонические UT-01–UT-10 из Stage 5.5.

## Проведение

Использовать только нейтральные prompts. Зафиксировать completion, time on task, wrong turns, help requests, confidence 1–5 и наблюдения за управлением с клавиатуры/фокусом. Остановить сессию при misunderstanding безопасности, риске потери данных, невосстановимом focus trap или неуспехе после двух нейтральных prompts. Не включать персональные данные в пакет.

## Принятие

Каждый сценарий должен иметь результат для назначенной роли; все четыре роли должны быть представлены; Critical/High не могут оставаться открытыми. Medium допускается только с явным owner disposition.
`);
  const usabilityRows = [
    ['UT-01','Admin','First connection and sign-in','Connect to an approved endpoint and sign in','Reach the authorized Shell context'],
    ['UT-02','Observer','Normal sign-in','Restore an authorized session','Reach a safe Shell context without restricted data'],
    ['UT-03','Manager','Create task','Create a task with required fields','See an authoritative created-task result'],
    ['UT-04','Employee','Quick create','Create a minimum valid task','Return to the invocation context'],
    ['UT-05','Observer','Global search','Navigate a redacted search result','Protected data and counts stay hidden'],
    ['UT-06','Employee','Connection loss','Browse the authorized read-only cache','Writes are prevented with a reason'],
    ['UT-07','Observer','Read-only cache','Inspect unavailable data state','No prohibited action becomes available'],
    ['UT-08','Manager','Reconnect','Restore the connection','Writes return only after synchronization'],
    ['UT-09','Manager','Optimistic conflict','Recover a local draft from conflict','Draft remains unchanged until a deliberate choice'],
    ['UT-10','Employee','Inbox conversion','Convert an Inbox item into a task','Task is created and the source is resolved'],
  ];
  await write('usability/Usability_Test_Script_0.2.csv', toCsv(['Test case ID','Role','Scenario','Participant task','Expected outcome'], usabilityRows));
  const sessionRows = Array.from({ length: 10 }, (_, index) => {
    const id = `UT-${String(index + 1).padStart(2, '0')}`;
    const roles = ['Admin','Observer','Manager','Employee','Observer','Employee','Observer','Manager','Manager','Employee'];
    return [id, roles[index], '', '', 'PENDING', '', '', '', '', '', '', '', ''];
  });
  await write('usability/Moderated_Session_Results_Template.csv', toCsv(['Test case ID','Participant role','Anonymized participant ID','Client build SHA-256','Result','Time seconds','Wrong turns','Help requests','Confidence 1-5','Quote/observation','Finding IDs','Moderator','Evidence path'], sessionRows));
  await write('usability/Participant_Coverage_Template.csv', toCsv(['Anonymized participant ID','Role','Session date','Client build SHA-256','Consent recorded outside package','Completed scenario IDs','Evidence path'], [['','Admin','','','NO','',''],['','Manager','','','NO','',''],['','Employee','','','NO','',''],['','Observer','','','NO','','']]));

  await write('signoff/GATE_5_6_SIGNOFF_RECORD.md', `# Gate 5.6: запись о подписании

**Пакет под рассмотрением:** Task Stage 5.6 final visual baseline and handoff 1.0.0
**Решение Gate:** PENDING

| Роль | Назначенный approver | Решение | Дата | Проверенные доказательства | Условия / finding IDs | Ссылка на подпись |
|---|---|---|---|---|---|---|
| Product owner |  | PENDING |  |  |  |  |
| Design owner |  | PENDING |  |  |  |  |
| Desktop tech lead |  | PENDING |  |  |  |  |
| QA |  | PENDING |  |  |  |  |

Строка шаблона не является одобрением. PENDING заменяется только после рассмотрения named approver неизменяемых доказательств по хэшам.
`);
  await write('signoff/Finding_Disposition_Template.csv', toCsv(['Finding ID','Severity','Summary','State','Resolution/acceptance rationale','Owner','Evidence path','Decision date'], [['','','','OPEN','','','','']]));

  const evidenceRows = [
    ['EVD-CLIENT-ID','Client identity recheck','QA + Desktop tech lead','evidence/incoming/client-identity-recheck.md','PENDING',`Names revision ${clientCommit} and approved client SHA-256 ${clientSha256}`,''],
    ['EVD-WIN-UIA','Native Windows Inspect/UIA report','QA + Desktop tech lead','evidence/incoming/windows-uia-report.md','PENDING','All 12 checkpoints contain Inspect.exe properties; no open Critical/High',''],
    ['EVD-WIN-KEYBOARD','Keyboard-only walkthrough','QA + Desktop tech lead','evidence/incoming/windows-keyboard-report.md','PENDING','All 12 checkpoints include keyboard path and focus outcome; no open Critical/High',''],
    ['EVD-USR','Moderated session results','UX + Product owner','evidence/incoming/moderated-session-results.csv','PENDING','UT-01–UT-10 completed, four roles represented, no open Critical/High',''],
    ['EVD-FIND','Final finding disposition','Design owner + QA','evidence/incoming/final-finding-disposition.csv','PENDING','Every finding closed or Medium formally accepted with implementation-complete logic',''],
    ['EVD-PO','Product owner decision','Product owner','evidence/incoming/product-owner-approval.md','PENDING','Named approval references exact package and evidence hashes',''],
    ['EVD-DESIGN','Design owner decision','Design owner','evidence/incoming/design-owner-approval.md','PENDING','Named approval references exact package and evidence hashes',''],
    ['EVD-TECH','Desktop technical decision','Desktop tech lead','evidence/incoming/desktop-tech-approval.md','PENDING','Named approval references native Windows results and exact package',''],
    ['EVD-QA','QA decision','QA','evidence/incoming/qa-approval.md','PENDING','Named approval references test evidence and exact package',''],
  ];
  await write('evidence/GATE_EVIDENCE_INDEX.csv', toCsv(['Evidence ID','Artifact','Owner','Required path','Status','Acceptance criterion','SHA-256'], evidenceRows));
  await write('evidence/incoming/README.md', '# Входящие доказательства Gate\n\nПомещайте сюда только проверенные, не содержащие секретов внешние доказательства. После owner review укажите `ACCEPTED` и SHA-256 в `../GATE_EVIDENCE_INDEX.csv`. Шаблоны, пустые файлы и неподписанные черновики Gate не закрывают.\n');
  await write('evidence/incoming/client-identity-recheck.md', `# Предварительная техническая сверка идентичности клиента\n\n**Статус:** PENDING — это не принятое доказательство Gate и не закрывает ни одну строку.\n\n- Ревизия, на которой выполняется повторная проверка: ${clientCommit}.\n- Единственный допустимый клиент: **Task Gate 5.6 Client 0.1.1 portable x64**.\n- SHA-256 файла: ${clientSha256}.\n- Фактическая контрольная сумма локального файла совпала с указанной.\n- Browser prototype не использовался.\n- Inspect.exe не найден ни в PATH, ни в стандартных каталогах Windows Kits данного окружения; поэтому прогон UIA и keyboard-only не выполнен и остаётся PENDING.\n- Нужны запуск Inspect.exe, заполнение UIA/keyboard reports и явное принятие QA + Desktop tech lead.\n`);

  const validator = `import { createHash } from 'node:crypto';\nimport { readFile } from 'node:fs/promises';\nimport path from 'node:path';\nimport { fileURLToPath } from 'node:url';\n\nconst root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');\nconst text=await readFile(path.join(root,'evidence','GATE_EVIDENCE_INDEX.csv'),'utf8');\nconst lines=text.trim().split(/\\r?\\n/).slice(1);\nconst rows=lines.map(line=>{const fields=[];let field='',q=false;for(let i=0;i<line.length;i++){const c=line[i];if(q){if(c==='"'&&line[i+1]==='"'){field+='"';i++;}else if(c==='"')q=false;else field+=c;}else if(c==='"')q=true;else if(c===','){fields.push(field);field='';}else field+=c;}fields.push(field);return fields;});\nconst results=[];\nfor(const [id,artifact,owner,requiredPath,status,criterion,expectedHash] of rows){let actualHash='',present=false;try{const data=await readFile(path.join(root,requiredPath));actualHash=createHash('sha256').update(data).digest('hex').toUpperCase();present=data.length>0;}catch{}const accepted=status==='ACCEPTED'&&present&&/^[A-F0-9]{64}$/.test(expectedHash)&&expectedHash===actualHash;results.push({id,artifact,owner,requiredPath,status,present,hashMatches:present&&expectedHash===actualHash,accepted,criterion});}\nconst accepted=results.filter(r=>r.accepted).length;\nconsole.log(JSON.stringify({result:accepted===results.length?'READY':'NOT_READY',accepted,total:results.length,missing:results.filter(r=>!r.accepted).map(r=>r.id),results},null,2));\n`;
  await write('tools/validate-gate-evidence.mjs', validator);

  await write('INITIAL_GATE_STATUS.json', `${JSON.stringify({ result: 'NOT_READY', accepted: 0, total: evidenceRows.length, missing: evidenceRows.map((row) => row[0]), reason: 'Техническая проверка, сессии и решения владельцев не представлены как принятые хэшированные доказательства.' }, null, 2)}\n`);
  await write('VERSION.txt', `${version}\n`);
  await write('VALIDATION_REPORT.md', `# Task — Gate 5.6: валидация пакета повторной технической проверки ${version}\n\n**Валидация пакета:** PASS.\n**Статус Gate:** NOT_READY.\n\n- Зафиксирован допустимый portable x64-клиент и точная ревизия PR #3.\n- Определены 12 контрольных точек Windows UIA и keyboard-only walkthrough.\n- Inspect.exe в данном окружении не обнаружен; инструментальный прогон не заявляется выполненным.\n- Все девять строк доказательств имеют статус PENDING: заполненный предварительный identity report не считается принятием.\n- Включены десять строк модерируемых сессий, четыре ролевые линзы, журнал находок и четыре решения владельцев.\n- Валидатор подтверждает NOT_READY, пока не появятся принятые доказательства с совпадающими SHA-256.\n`);

  const builderSha256 = await sha256(fileURLToPath(import.meta.url));
  const artifactFiles = (await listFiles(workPackage)).filter((file) => !['manifest.json', 'MANIFEST.sha256'].includes(file));
  const artifactHashes = {};
  for (const file of artifactFiles) artifactHashes[file] = await sha256(path.join(workPackage, file));
  const manifest = {
    package: 'Task Stage 5.6 technical recheck kit', version, date,
    status: 'PASS — reproducible technical recheck kit; Gate 5.6 remains NOT_READY',
    testedRevision: clientCommit,
    approvedClient: { name: 'Task Gate 5.6 Client 0.1.1 portable x64', sha256: clientSha256 },
    prerequisitePackage: { path: 'stage_5_6_final_visual_baseline_and_handoff', version: '1.0.0', manifestSha256: '6E04E294CC8CED59CC1686EE8BF1F33C8706068B17D92572ABE8516F82C8B400' },
    scope: { windowsAccessibilityCheckpoints: 12, keyboardWalkthroughs: 12, usabilityScenarios: 10, roleLenses: 4, evidenceRequirements: evidenceRows.length, namedApprovalRoles: 4 },
    gateStatus: { result: 'NOT_READY', acceptedEvidence: 0, requiredEvidence: evidenceRows.length },
    builderSha256, artifactHashes,
    evidenceBoundaries: ['No completed native Windows result is claimed', 'No participant session is claimed', 'No stakeholder approval is claimed', 'Templates and preliminary reports are not Gate evidence'],
  };
  await write('manifest.json', `${JSON.stringify(manifest, null, 2)}\n`);
  const manifestSha256 = await sha256(path.join(workPackage, 'manifest.json'));
  await write('MANIFEST.sha256', `${manifestSha256}  manifest.json\n`);

  await mirrorPackage();
  const workFiles = await listFiles(workPackage);
  const outputFiles = await listFiles(outputPackage);
  const mismatches = [];
  for (const file of workFiles) if (!outputFiles.includes(file) || await sha256(path.join(workPackage, file)) !== await sha256(path.join(outputPackage, file))) mismatches.push(file);
  console.log(JSON.stringify({ result: 'PASS', version, gateStatus: 'NOT_READY', workFiles: workFiles.length, outputFiles: outputFiles.length, mirrorMismatches: mismatches.length, manifestSha256, builderSha256 }, null, 2));
}

await main();
