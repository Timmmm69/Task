import { createHash } from 'node:crypto';
import { copyFile, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const packageName = 'stage_5_6_external_gate_execution_kit';
const workPackage = path.join(root, 'work', packageName);
const outputPackage = path.join(root, 'outputs', '019fa078-3f10-7ec1-99e2-7c1cba4ee3d4', packageName);
const version = '0.2.0';
const date = '2026-08-09';

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

async function copyFromRoot(source, target) {
  const destination = path.join(workPackage, target);
  await mkdir(path.dirname(destination), { recursive: true });
  await copyFile(path.join(root, source), destination);
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

  const readme = `# Task — Gate 5.6 External Evidence Execution Kit ${version}\n\n` +
`**Date:** ${date}\n\n**Purpose:** make the remaining external readiness work reproducible without claiming that it has already happened.\n\n` +
`## Execution order\n\n1. Freeze the exact compiled Windows client build and record its SHA-256 in every result file.\n2. Run the UIA/Inspect and Narrator protocol.\n3. Run the Windows DPI/multi-monitor matrix at 100/125/150/175/200%.\n4. Conduct moderated sessions with all four role lenses using the canonical 10 scenarios.\n5. Resolve or formally disposition every new Critical/High/Medium finding.\n6. Obtain Product owner, Design owner, Desktop tech lead and QA decisions.\n7. Place signed/approved evidence under \`evidence/incoming/\`, update the evidence index, and run \`node tools/validate-gate-evidence.mjs\`.\n\n` +
`## Honest status\n\nThe kit itself is validated, but Gate 5.6 is **NOT_READY** until every required evidence row is present, hash-addressed and accepted by its named owner. Templates are not evidence and blank signature fields are not approvals.\n`;
  await write('README.md', readme);

  const windowsProtocol = `# Native Windows UIA and Narrator Protocol\n\n` +
`## Preconditions\n\n- Use the compiled Windows desktop client, not the browser prototype.\n- Record application version, executable SHA-256, Windows version/build, screen-reader version, locale, user role, server fixture and timestamp.\n- Use production-like authorized data with no customer secrets.\n\n` +
`## Procedure\n\nFor each checkpoint in \`windows/Windows_Accessibility_Checkpoints.csv\`: capture Inspect/UIA properties, Narrator output notes, keyboard path, focus order/return and a screenshot or screen recording reference. Mark PASS only when the expected name, role, state/value and user outcome are all demonstrated.\n\n` +
`## Stop conditions\n\nStop and file a Critical/High finding for a focus trap, inaccessible required action, undisclosed destructive consequence, permission disclosure, accepted write in offline/read-only mode, or loss of user input. Do not sign Gate 5.6 while any Critical/High remains open.\n`;
  await write('windows/NATIVE_WINDOWS_UIA_NARRATOR_PROTOCOL.md', windowsProtocol);

  const windowsRows = [
    ['WIN-A11Y-01','Shell navigation','Employee','Tab/Shift+Tab, arrows where applicable','Stable accessible names; current item exposed; no focus loss','PENDING','',''],
    ['WIN-A11Y-02','First connection and sign-in','Admin','Keyboard only','Endpoint, validation, progress and successful landing announced','PENDING','',''],
    ['WIN-A11Y-03','New Task editor','Manager','Alt+N then keyboard completion','Dialog name, required fields, validation and deterministic return focus','PENDING','',''],
    ['WIN-A11Y-04','CalendarEvent editor','Manager','Keyboard create/edit','Date/time/timezone, recurrence, attendees, validation and mutation guards exposed','PENDING','',''],
    ['WIN-A11Y-05','Search redaction','Observer','Ctrl+K/Ctrl+F and result navigation','Unavailable result is explained without protected identity or count','PENDING','',''],
    ['WIN-A11Y-06','Offline read-only','Employee','Connection loss and browse','Connection/staleness announced; prohibited writes disabled with reason','PENDING','',''],
    ['WIN-A11Y-07','Reconnect','Manager','Restore connection','Readiness announced without stealing focus; writes restore only after sync','PENDING','',''],
    ['WIN-A11Y-08','Optimistic conflict','Manager','Edit, conflict, return to draft','Conflict choices reachable; local draft restored unchanged','PENDING','',''],
    ['WIN-A11Y-09','Admin restricted role','Observer','Navigate Admin/Operations','Unavailable capabilities and hidden objects are not exposed','PENDING','',''],
    ['WIN-A11Y-10','Dangerous Operations action','Admin','Backup restore guard','Approval, exact phrase, maintenance and consequence announced before action','PENDING','',''],
    ['WIN-A11Y-11','Archive/Trash retention','Admin','Restore/purge/legal hold','Blocked destructive action, legal hold and metadata-only deletion copy exposed','PENDING','',''],
    ['WIN-A11Y-12','Menus, tabs, tables, trees and progress','All roles','Keyboard navigation','Correct UIA control types, selection/expanded/current/value states','PENDING','',''],
  ];
  await write('windows/Windows_Accessibility_Checkpoints.csv', toCsv(['Checkpoint ID','Surface/flow','Role lens','Input path','Acceptance criterion','Result','Evidence path','Finding IDs'], windowsRows));

  const dpiProtocol = `# Windows DPI and Multi-monitor Protocol\n\n` +
`Run every row in \`Windows_DPI_Test_Matrix.csv\` against the same signed client build. Use Windows display scaling, not browser zoom. Verify no clipped meaning, inaccessible command, overlapping text, lost focus indicator, unusable horizontal scroll, misplaced overlay or pointer/focus mismatch. Long Russian fixtures and permission-safe states must remain legible. Record screenshots at native resolution and the monitor transition path.\n`;
  await write('windows/WINDOWS_DPI_MULTI_MONITOR_PROTOCOL.md', dpiProtocol);
  const dpiRows = [
    ['DPI-100','100%','1920x1080','Single monitor','Today, Tasks, Search, Admin, Operations','PENDING','',''],
    ['DPI-125','125%','1920x1080','Single monitor','Today, Calendar editor, Inbox conversion, Settings','PENDING','',''],
    ['DPI-150','150%','1920x1080','Single monitor','Today, Projects, Files recovery, CRM','PENDING','',''],
    ['DPI-175','175%','1920x1080','Single monitor','Search redaction, conflict, Archive/Trash','PENDING','',''],
    ['DPI-200','200%','1920x1080','Single monitor','Auth, Today, editors, dialogs, Operations','PENDING','',''],
    ['DPI-LAPTOP','150%','1366x768','Laptop panel','Shell navigation, Today, task editor, Inbox conversion','PENDING','',''],
    ['DPI-MULTI-1','100% → 200%','1920x1080 → 3840x2160','Move active window between monitors','Editor, conflict dialog, menus, inspector and focus target','PENDING','',''],
    ['DPI-MULTI-2','200% → 125%','3840x2160 → 1920x1080','Move active window between monitors','Today, Admin and Operations with open overlay','PENDING','',''],
  ];
  await write('windows/Windows_DPI_Test_Matrix.csv', toCsv(['Case ID','Scaling','Resolution','Topology/action','Required surfaces','Result','Evidence path','Finding IDs'], dpiRows));

  const moderated = `# Moderated Usability Session Protocol\n\n` +
`## Coverage\n\nAll four roles—Admin, Manager, Employee and Observer—must be represented. The UX owner records the target participant count before recruitment; the Gate validator checks role coverage and completed evidence, not a fabricated sample-size claim. Use the canonical UT-01–UT-10 tasks from the packaged Stage 5.5 script.\n\n` +
`## Moderation\n\nUse neutral prompts only. Capture task completion, time on task, wrong turns, help requests, confidence 1–5, accessibility/focus observations and participant quotes. Stop for security misunderstanding, data-loss risk, unrecoverable focus trap or failure after two neutral prompts. Do not include personal data in the package.\n\n` +
`## Acceptance\n\nEvery scenario must have completed evidence from its intended role lens, all four roles must be represented, and no Critical/High finding may remain open. Medium findings require explicit owner disposition and may not force developers to invent behavior.\n`;
  await write('usability/MODERATED_SESSION_PROTOCOL.md', moderated);
  await copyFromRoot('work/stage_5_2/Usability_Test_Script_0.1.csv', 'usability/Usability_Test_Script_0.1.csv');
  const sessionRows = Array.from({ length: 10 }, (_, index) => {
    const id = `UT-${String(index + 1).padStart(2, '0')}`;
    const roles = ['Admin','Observer','Manager','Employee','Observer','Employee','Observer','Manager','Manager','Employee'];
    return [id, roles[index], '', '', 'PENDING', '', '', '', '', '', '', '', ''];
  });
  await write('usability/Moderated_Session_Results_Template.csv', toCsv(['Test case ID','Participant role','Anonymized participant ID','Client build SHA-256','Result','Time seconds','Wrong turns','Help requests','Confidence 1-5','Quote/observation','Finding IDs','Moderator','Evidence path'], sessionRows));
  await write('usability/Participant_Coverage_Template.csv', toCsv(['Anonymized participant ID','Role','Session date','Client build SHA-256','Consent recorded outside package','Completed scenario IDs','Evidence path'], [
    ['','Admin','','','NO','',''],['','Manager','','','NO','',''],['','Employee','','','NO','',''],['','Observer','','','NO','',''],
  ]));

  const signoff = `# Gate 5.6 Sign-off Record\n\n**Package under review:** Task Stage 5.6 final visual baseline and handoff 1.0.0  \n**Gate decision:** PENDING\n\n| Role | Named approver | Decision | Date | Evidence reviewed | Conditions / finding IDs | Signature reference |\n|---|---|---|---|---|---|---|\n| Product owner |  | PENDING |  |  |  |  |\n| Design owner |  | PENDING |  |  |  |  |\n| Desktop tech lead |  | PENDING |  |  |  |  |\n| QA |  | PENDING |  |  |  |  |\n\nA typed template row is not an approval. Replace PENDING only after the named approver has reviewed the referenced immutable evidence.\n`;
  await write('signoff/GATE_5_6_SIGNOFF_RECORD.md', signoff);
  await write('signoff/Finding_Disposition_Template.csv', toCsv(['Finding ID','Severity','Summary','State','Resolution/acceptance rationale','Owner','Evidence path','Decision date'], [['','','','OPEN','','','','']]));

  const evidenceRows = [
    ['EVD-WIN-UIA','Native Windows Inspect/UIA report','QA + Desktop tech lead','evidence/incoming/windows-uia-report.md','PENDING','Report identifies client SHA and all 12 checkpoints; no open Critical/High',''],
    ['EVD-WIN-NARR','Narrator walkthrough report','QA + Design owner','evidence/incoming/narrator-report.md','PENDING','Announcements/focus documented for all required flows; no open Critical/High',''],
    ['EVD-WIN-DPI','Windows DPI results','QA + Desktop tech lead','evidence/incoming/windows-dpi-results.csv','PENDING','All 8 matrix rows PASS or have accepted non-blocking disposition',''],
    ['EVD-USR','Moderated session results','UX + Product owner','evidence/incoming/moderated-session-results.csv','PENDING','UT-01–UT-10 completed, four roles represented, no open Critical/High',''],
    ['EVD-FIND','Final finding disposition','Design owner + QA','evidence/incoming/final-finding-disposition.csv','PENDING','Every finding closed or Medium formally accepted with implementation-complete logic',''],
    ['EVD-PO','Product owner decision','Product owner','evidence/incoming/product-owner-approval.md','PENDING','Named approval references exact package and evidence hashes',''],
    ['EVD-DESIGN','Design owner decision','Design owner','evidence/incoming/design-owner-approval.md','PENDING','Named approval references exact package and evidence hashes',''],
    ['EVD-TECH','Desktop technical decision','Desktop tech lead','evidence/incoming/desktop-tech-approval.md','PENDING','Named approval references native Windows results and exact package',''],
    ['EVD-QA','QA decision','QA','evidence/incoming/qa-approval.md','PENDING','Named approval references test evidence and exact package',''],
  ];
  await write('evidence/GATE_EVIDENCE_INDEX.csv', toCsv(['Evidence ID','Artifact','Owner','Required path','Status','Acceptance criterion','SHA-256'], evidenceRows));
  await write('evidence/incoming/README.md', '# Incoming Gate evidence\n\nPlace only reviewed, non-secret external evidence here. Update `../GATE_EVIDENCE_INDEX.csv` with `ACCEPTED` and the file SHA-256 after owner review. Templates and empty files do not satisfy the Gate.\n');
  const recheckReport = [
    '# Native Windows UIA recheck — unaccepted attempt',
    '',
    `Date: ${date}`,
    'Client: Task Gate 5.6 Client 0.1.1 portable x64',
    'Executable SHA-256: 8B047DD69E1A64269F8961FE0416727E5083E0C2B30285A73DD2E92A2D412E53',
    'Source commit: 6a16be2fb371d41af0540569c77daf59eb902a9d (PR #3 head; not merged to main).',
    'Windows: 10.0.26200.0; one active 2560x1600 display; interactive session.',
    'Tool: .NET System.Windows.Automation plus native keyboard injection. Inspect.exe was absent after searching C:\\Program Files (x86)\\Windows Kits\\10\\bin.',
    '',
    'This is an Electron-client attempt, not browser-prototype evidence. It is not accepted EVD-WIN-UIA evidence: Inspect capture, full role/flow coverage, Narrator observation, and QA + Desktop tech lead review are absent.',
    '',
    'Observed: native Electron window Task — Сегодня (Chrome_WidgetWin_1) with Chromium RootWebArea; named focusable shell controls; Search redaction copy; and CalendarEvent editor with named title/date/timezone/attendees controls, validation/mutation guards, and a synthetic save result.',
    'Observed focus concern: after transition to sign-in, UIA could not set keyboard focus to Login or Password although they were reported enabled/focusable. This is a manual Inspect/Narrator retest candidate, not a confirmed production defect.',
    '',
    '| ID | Result | Basis / limitation |',
    '|---|---|---|',
    '| WIN-A11Y-01 | PARTIAL | Manager shell names and a foreground keyboard route observed; not Employee/full focus return. |',
    '| WIN-A11Y-02 | PARTIAL | Connection/sign-in controls named; authentication and announcement not demonstrated; focus retest required. |',
    '| WIN-A11Y-03 | NOT_RUN | New Task flow not reached after sign-in focus limitation. |',
    '| WIN-A11Y-04 | PARTIAL | Desktop CalendarEvent editor and synthetic save observed; full keyboard/focus/guard coverage unverified. |',
    '| WIN-A11Y-05 | PARTIAL | Manager Search route and permission-safe redaction observed; Observer flow not run. |',
    '| WIN-A11Y-06 | NOT_RUN | Offline read-only not executed. |',
    '| WIN-A11Y-07 | NOT_RUN | Reconnect not executed. |',
    '| WIN-A11Y-08 | NOT_RUN | Conflict/draft restoration not executed. |',
    '| WIN-A11Y-09 | NOT_RUN | Observer restriction not executed. |',
    '| WIN-A11Y-10 | NOT_RUN | Admin restore guard not executed. |',
    '| WIN-A11Y-11 | PARTIAL | Archive/Trash controls present; Admin destructive flow not run. |',
    '| WIN-A11Y-12 | PARTIAL | Tabs/comboboxes/state-bearing controls observed; menu/table/tree/progress coverage incomplete. |',
    '',
    'Narrator.exe is installed but this session has no auditable speech-output capture or listener, so no Narrator smoke result is claimed. DPI/multi-monitor, moderated sessions, finding disposition, and owner approvals were unavailable.',
    '',
    'Decision: all nine evidence rows remain PENDING. Do not sign Gate 5.6 or change any row to ACCEPTED based on this report.',
    '',
  ].join('\n');
  await write('evidence/incoming/windows-uia-report.md', recheckReport);

  const validator = `import { createHash } from 'node:crypto';\nimport { readFile } from 'node:fs/promises';\nimport path from 'node:path';\nimport { fileURLToPath } from 'node:url';\n\nconst root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');\nconst text=await readFile(path.join(root,'evidence','GATE_EVIDENCE_INDEX.csv'),'utf8');\nconst lines=text.trim().split(/\\r?\\n/).slice(1);\nconst rows=lines.map(line=>{const fields=[];let field='',q=false;for(let i=0;i<line.length;i++){const c=line[i];if(q){if(c==='"'&&line[i+1]==='"'){field+='"';i++;}else if(c==='"')q=false;else field+=c;}else if(c==='"')q=true;else if(c===','){fields.push(field);field='';}else field+=c;}fields.push(field);return fields;});\nconst results=[];\nfor(const [id,artifact,owner,requiredPath,status,criterion,expectedHash] of rows){let actualHash='',present=false;try{const data=await readFile(path.join(root,requiredPath));actualHash=createHash('sha256').update(data).digest('hex').toUpperCase();present=data.length>0;}catch{}const accepted=status==='ACCEPTED'&&present&&/^[A-F0-9]{64}$/.test(expectedHash)&&expectedHash===actualHash;results.push({id,artifact,owner,requiredPath,status,present,hashMatches:present&&expectedHash===actualHash,accepted,criterion});}\nconst accepted=results.filter(r=>r.accepted).length;\nconsole.log(JSON.stringify({result:accepted===results.length?'READY':'NOT_READY',accepted,total:results.length,missing:results.filter(r=>!r.accepted).map(r=>r.id),results},null,2));\n`;
  await write('tools/validate-gate-evidence.mjs', validator);

  const initialStatus = { result: 'NOT_READY', accepted: 0, total: evidenceRows.length, missing: evidenceRows.map((row) => row[0]), reason: 'External native Windows, participant and named approval evidence has not been supplied.' };
  await write('INITIAL_GATE_STATUS.json', `${JSON.stringify(initialStatus, null, 2)}\n`);
  await write('VERSION.txt', `${version}\n`, 'utf8');

  const validationReport = `# Task — Gate 5.6 Execution Kit Validation ${version}\n\n` +
`**Kit validation:** PASS.  \n**Gate status:** NOT_READY.\n\n` +
`- 12 native Windows accessibility checkpoints defined.\n` +
`- 8 actual DPI/multi-monitor cases defined.\n` +
`- UT-01–UT-10 moderated-session result rows and four-role coverage template included.\n` +
`- Four named approval roles and nine immutable evidence requirements defined.\n` +
`- An unaccepted Electron UIA recheck attempt is retained separately; it does not count as accepted evidence.\n` +
`- Gate validator included and expected to report NOT_READY until real accepted evidence is added.\n` +
`- No template is counted as test evidence or approval.\n`;
  await write('VALIDATION_REPORT.md', validationReport);

  const builderSha256 = await sha256(fileURLToPath(import.meta.url));
  const artifactFiles = (await listFiles(workPackage)).filter((file) => !['manifest.json', 'MANIFEST.sha256'].includes(file));
  const artifactHashes = {};
  for (const file of artifactFiles) artifactHashes[file] = await sha256(path.join(workPackage, file));
  const manifest = {
    package: 'Task Stage 5.6 external Gate execution kit', version, date,
    status: 'PASS — executable evidence kit; Gate 5.6 remains NOT_READY',
    prerequisitePackage: { path: 'stage_5_6_final_visual_baseline_and_handoff', version: '1.0.0', manifestSha256: '6E04E294CC8CED59CC1686EE8BF1F33C8706068B17D92572ABE8516F82C8B400' },
    scope: { windowsAccessibilityCheckpoints: 12, dpiCases: 8, usabilityScenarios: 10, roleLenses: 4, evidenceRequirements: evidenceRows.length, namedApprovalRoles: 4 },
    gateStatus: { result: 'NOT_READY', acceptedEvidence: 0, requiredEvidence: evidenceRows.length },
    builderSha256, artifactHashes,
    evidenceBoundaries: ['The retained Electron UIA attempt is incomplete and unaccepted', 'No Narrator or actual DPI/multi-monitor result is claimed', 'No participant session is claimed', 'No stakeholder approval is claimed', 'Templates are not Gate evidence'],
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
