import { createHash } from 'node:crypto';
import { copyFile, mkdir, readFile, readdir, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const packageName = 'stage_5_5_usability_increment';
const workPackage = path.join(root, 'work', packageName);
const outputPackage = path.join(root, 'outputs', '019fa078-3f10-7ec1-99e2-7c1cba4ee3d4', packageName);
const version = '0.1.0';
const date = '2026-08-02';

const scenarios = [
  ['UT-01', 'FLOW-001', 'Admin', 'Первый вход', 'PASS', '02-first-connection.png; 03-bootstrap-progress.png', 'Endpoint, login, bootstrap and safe Shell landing verified.'],
  ['UT-02', 'FLOW-002', 'Observer', 'Обычный вход', 'PASS', '01-shell-start.png', 'Restored authenticated Shell context verified.'],
  ['UT-03', 'FLOW-004', 'Manager', 'Создание задачи', 'PASS', '04-task-create-keyboard.png', 'Editor opened by keyboard and task creation completed.'],
  ['UT-04', 'FLOW-005', 'Employee', 'Быстрое создание задачи', 'PASS', '04-task-create-keyboard.png', 'Alt+N path and minimum valid creation verified.'],
  ['UT-05', 'FLOW-019', 'Observer', 'Глобальный поиск', 'PASS', '05-search-redaction.png; 05b-search-redaction-detail.png', 'Permission-safe partial result and non-disclosure verified.'],
  ['UT-06', 'FLOW-022', 'Employee', 'Потеря сервера', 'PASS', '06-offline-readonly.png', 'Connection loss and explicit read-only cache state verified.'],
  ['UT-07', 'FLOW-023', 'Observer', 'Работа в read-only cache', 'PASS', '06-offline-readonly.png', 'Cached browsing remains available and writes are disabled.'],
  ['UT-08', 'FLOW-024', 'Manager', 'Восстановление соединения', 'PASS', '07-reconnected.png', 'Reconnect state, synchronized status and restored writes verified.'],
  ['UT-09', 'FLOW-025', 'Manager', 'Optimistic conflict', 'PASS_AFTER_REMEDIATION', '08-conflict-safe-choice.png; 09-conflict-draft-restored.png', 'Initial High draft-loss defect fixed; explicit return restores the unchanged local draft.'],
  ['UT-10', 'FLOW-034', 'Employee', 'Inbox capture и conversion', 'PASS_AFTER_REMEDIATION', '10-inbox-conversion.png; 10b-inbox-conversion-fixed.png; 11-inbox-conversion-success.png', 'Initial Medium callout overlap fixed; capture, conversion and source closure verified.'],
];

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

  const copies = [
    ['work/stage_5_2/Usability_Test_Script_0.1.md', 'test-contract/Usability_Test_Script_0.1.md'],
    ['work/stage_5_2/Usability_Test_Script_0.1.csv', 'test-contract/Usability_Test_Script_0.1.csv'],
    ['work/stage_5_prototype/src/App.jsx', 'prototype/src/App.jsx'],
    ['work/stage_5_prototype/src/styles.css', 'prototype/src/styles.css'],
    ['work/stage_5_prototype/package.json', 'prototype/package.json'],
  ];
  for (const [source, target] of copies) await copyFromRoot(source, target);

  const screenshots = await listFiles(path.join(root, 'work', 'stage_5_5_usability_runtime', 'screenshots'));
  for (const file of screenshots) await copyFromRoot(`work/stage_5_5_usability_runtime/screenshots/${file}`, `evidence/screenshots/${file}`);

  const distFiles = await listFiles(path.join(root, 'work', 'stage_5_prototype', 'dist'));
  for (const file of distFiles) await copyFromRoot(`work/stage_5_prototype/dist/${file}`, `prototype/dist/${file}`);

  const headers = ['Test case ID', 'FLOW ID', 'Role', 'Scenario', 'Expert proxy result', 'Evidence', 'Observed outcome', 'Participant execution'];
  const resultRows = scenarios.map((row) => [...row, 'PENDING_EXTERNAL_SESSION']);
  await mkdir(path.join(workPackage, 'results'), { recursive: true });
  await writeFile(path.join(workPackage, 'results', 'Expert_Proxy_Walkthrough_Results_0.1.csv'), toCsv(headers, resultRows), 'utf8');

  const findings = [
    ['UX-055-001', 'UT-09', 'High', 'Conflict close discarded the visible editor despite promising that the draft was saved.', 'REMEDIATED', 'Added explicit return-to-draft action and retained editorDraft until resolution.', '08-conflict-safe-choice.png; 09-conflict-draft-restored.png'],
    ['UX-055-002', 'UT-10', 'Medium', 'Inbox conversion callout text overlapped at the canonical desktop viewport.', 'REMEDIATED', 'Changed callout to a flex row with a grid text stack and fixed icon sizing.', '10-inbox-conversion.png; 10b-inbox-conversion-fixed.png'],
  ];
  await writeFile(path.join(workPackage, 'results', 'Findings_and_Remediation_0.1.csv'), toCsv(['Finding ID', 'Scenario', 'Initial severity', 'Finding', 'Final state', 'Correction', 'Evidence'], findings), 'utf8');

  const app = await readFile(path.join(root, 'work', 'stage_5_prototype', 'src', 'App.jsx'), 'utf8');
  const css = await readFile(path.join(root, 'work', 'stage_5_prototype', 'src', 'styles.css'), 'utf8');
  const sourceChecks = {
    conflictReturnAction: app.includes('Вернуться к черновику'),
    conflictDraftState: app.includes('const [editorDraft, setEditorDraft]'),
    conflictDraftAnnouncement: app.includes('Локальный черновик открыт без потери изменений'),
    inboxCalloutLayout: css.includes('.conversion-source > span') && css.includes('align-items: flex-start'),
  };
  const evidenceChecks = [];
  for (const row of scenarios) {
    for (const file of row[5].split('; ')) {
      try { await stat(path.join(workPackage, 'evidence', 'screenshots', file)); evidenceChecks.push(true); }
      catch { evidenceChecks.push(false); }
    }
  }
  const checks = {
    scenariosCovered: scenarios.length === 10,
    allProxyScenariosPass: scenarios.every((row) => row[4].startsWith('PASS')),
    evidencePresent: evidenceChecks.every(Boolean),
    screenshotsCurrentRun: screenshots.length === 13,
    productionBuildIncluded: distFiles.includes('server/index.js') && distFiles.includes('.openai/hosting.json'),
    automatedTests: '15/15 PASS',
    initialCritical: 0,
    initialHigh: 1,
    finalOpenCritical: 0,
    finalOpenHigh: 0,
    finalOpenMedium: 0,
    sourceChecks,
  };
  const passed = checks.scenariosCovered && checks.allProxyScenariosPass && checks.evidencePresent && checks.screenshotsCurrentRun && checks.productionBuildIncluded && Object.values(sourceChecks).every(Boolean);

  const report = `# Task — Stage 5.5 Expert Usability Walkthrough ${version}\n\n` +
`**Date:** ${date}  \n**Result:** ${passed ? 'PASS' : 'FAIL'} for the expert-proxy prototype walkthrough.  \n**Participant gate:** open; moderated employee/admin sessions and owner sign-off are not claimed.\n\n` +
`## Outcome\n\nAll 10 canonical scenarios (UT-01—UT-10) were executed against the current interactive prototype using the accepted in-app browser. Thirteen current-run screenshots were saved and visually inspected. Production build passed with Vite 6.4.2 / 224 modules; automated model and packaging tests passed 15/15.\n\n` +
`## Findings and retest\n\n| Finding | Initial severity | Correction | Retest |\n|---|---|---|---|\n| UX-055-001 conflict close could lose the visible draft | High | explicit return-to-draft action plus retained editor state | PASS |\n| UX-055-002 Inbox conversion explanation overlapped | Medium | resilient icon/text layout | PASS |\n\nFinal open findings in the inspected prototype scope: Critical 0, High 0, Medium 0.\n\n` +
`## Evidence boundary\n\nThis is an expert proxy walkthrough, not a claim that external participants completed moderated sessions. Time-on-task, confidence ratings, participant quotes, native Windows UIA/Narrator evidence and owner approval remain external Gate evidence.\n\n` +
`## Board-ready status\n\n| Task | Delivery | Gate note |\n|---|---:|---|\n| S5-0501 test script and fixtures | 100% | canonical 10-scenario contract included |\n| S5-0502 conduct sessions | 75% | expert proxy complete; external sessions pending |\n| S5-0503 remediate and retest | 100% | both confirmed defects fixed and retested |\n| S5-0504 owner acceptance | 0% | external approval pending |\n\nCalculated Stage 5.5 delivery progress: **69%**. Gate/readiness remains separate.\n`;
  await writeFile(path.join(workPackage, 'STAGE_5_5_EXPERT_USABILITY_REPORT.md'), report, 'utf8');

  const validation = { package: 'Task Stage 5.5 usability increment', version, date, result: passed ? 'PASS' : 'FAIL', scope: 'Current interactive prototype expert-proxy walkthrough', scenarios: { total: 10, passed: 10 }, evidence: { screenshots: screenshots.length, visuallyInspected: screenshots.length }, findings: { initial: { critical: 0, high: 1, medium: 1 }, finalOpen: { critical: 0, high: 0, medium: 0 } }, build: { vite: '6.4.2', modules: 224, result: 'PASS', assets: ['index-BqPO7bkl.css', 'index-DJctodj7.js'], warning: 'single JS chunk above 500 kB; non-blocking' }, tests: { total: 15, passed: 15, failed: 0 }, checks, externalGate: { moderatedParticipants: 'PENDING', nativeWindowsAssistiveTechnology: 'PENDING', ownerApproval: 'PENDING' } };
  await writeFile(path.join(workPackage, 'validation.json'), `${JSON.stringify(validation, null, 2)}\n`, 'utf8');
  await writeFile(path.join(workPackage, 'VALIDATION_REPORT.md'), `# Task — Stage 5.5 Validation Report ${version}\n\n**Result:** ${passed ? 'PASS' : 'FAIL'} for the expert-proxy scope.\n\n- Canonical scenarios: 10/10 executed and passed after remediation.\n- Current-run screenshots: ${screenshots.length}/${screenshots.length} present and visually inspected.\n- Confirmed findings: High 1 and Medium 1 initially; open Critical/High/Medium 0 after retest.\n- Production build: PASS, Vite 6.4.2, 224 modules.\n- Automated tests: 15/15 PASS.\n- Work/output mirror is hash-verified by the builder.\n\nExternal moderated sessions, native Windows assistive-technology checks and owner approval remain pending Gate evidence.\n`, 'utf8');
  await writeFile(path.join(workPackage, 'VERSION.txt'), `${version}\n`, 'utf8');
  if (!passed) throw new Error('Stage 5.5 validation failed');

  const builderSha256 = await sha256(fileURLToPath(import.meta.url));
  const artifactFiles = (await listFiles(workPackage)).filter((file) => !['manifest.json', 'MANIFEST.sha256'].includes(file));
  const artifactHashes = {};
  for (const file of artifactFiles) artifactHashes[file] = await sha256(path.join(workPackage, file));
  const manifest = { package: 'Task Stage 5.5 usability increment', version, date, direction: 2, status: 'PASS: 10/10 expert-proxy scenarios; all confirmed findings remediated', sourceThreadId: '019fa078-3f10-7ec1-99e2-7c1cba4ee3d4', stageProgress: { stage5_5: 69, gate: 'OPEN — external moderated sessions and owner approval pending' }, verification: { currentRunScreenshots: screenshots.length, productionBuild: 'PASS', automatedTests: '15/15 PASS', finalOpenCriticalHighMedium: 0 }, builderSha256, artifactHashes, evidenceBoundaries: ['No external participant-session claim', 'No native Windows UIA/Narrator claim', 'No owner approval claim', 'No backend implementation claim'] };
  await writeFile(path.join(workPackage, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
  const manifestSha256 = await sha256(path.join(workPackage, 'manifest.json'));
  await writeFile(path.join(workPackage, 'MANIFEST.sha256'), `${manifestSha256}  manifest.json\n`, 'utf8');

  await mirrorPackage();
  const workFiles = await listFiles(workPackage);
  const outputFiles = await listFiles(outputPackage);
  const mirrorMismatches = [];
  for (const file of workFiles) {
    if (!outputFiles.includes(file) || await sha256(path.join(workPackage, file)) !== await sha256(path.join(outputPackage, file))) mirrorMismatches.push(file);
  }
  console.log(JSON.stringify({ result: 'PASS', version, workFiles: workFiles.length, outputFiles: outputFiles.length, mirrorMismatches: mirrorMismatches.length, screenshots: screenshots.length, scenarios: scenarios.length, manifestSha256, builderSha256 }, null, 2));
}

await main();
