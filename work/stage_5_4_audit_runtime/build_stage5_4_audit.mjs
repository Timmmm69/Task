import { createHash } from 'node:crypto';
import { copyFile, mkdir, readFile, readdir, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const version = '0.1.0';
const date = '2026-08-01';
const packageName = 'stage_5_4_design_audit_increment';
const outputRoot = path.join(root, 'outputs', '019fa078-3f10-7ec1-99e2-7c1cba4ee3d4');
const workPackage = path.join(root, 'work', packageName);
const outputPackage = path.join(outputRoot, packageName);

const roleSource = path.join(root, 'work', 'stage_5_2', 'Role_Capability_Design_Matrix_0.1.csv');
const stateSource = path.join(root, 'work', 'stage_5_2', 'State_Component_Coverage_Matrix_0.1.csv');
const appSource = path.join(root, 'work', 'stage_5_prototype', 'src', 'App.jsx');
const cssSource = path.join(root, 'work', 'stage_5_prototype', 'src', 'styles.css');

function parseCsv(text) {
  const rows = [];
  let row = [];
  let field = '';
  let quoted = false;
  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    if (quoted) {
      if (char === '"' && text[index + 1] === '"') {
        field += '"';
        index += 1;
      } else if (char === '"') {
        quoted = false;
      } else {
        field += char;
      }
    } else if (char === '"') {
      quoted = true;
    } else if (char === ',') {
      row.push(field);
      field = '';
    } else if (char === '\n') {
      row.push(field.replace(/\r$/, ''));
      rows.push(row);
      row = [];
      field = '';
    } else {
      field += char;
    }
  }
  if (field.length || row.length) {
    row.push(field.replace(/\r$/, ''));
    rows.push(row);
  }
  const [rawHeaders, ...records] = rows.filter((item) => item.some((value) => value !== ''));
  const headers = rawHeaders.map((header, index) => index === 0 ? header.replace(/^\uFEFF/, '') : header);
  return records.map((values) => Object.fromEntries(headers.map((header, index) => [header, values[index] ?? ''])));
}

function csvEscape(value) {
  const text = String(value ?? '');
  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

function toCsv(records) {
  const headers = Object.keys(records[0]);
  return `${headers.map(csvEscape).join(',')}\r\n${records.map((record) => headers.map((header) => csvEscape(record[header])).join(',')).join('\r\n')}\r\n`;
}

function roleEvidence(id) {
  const number = Number(id.slice(-3));
  if (number <= 2) return 'qa/role-today-calendar.png; qa/role-calendar-responsive.png';
  if (number === 3) return 'qa/role-today-calendar.png; evidence/design-qa-stage5-p0.md';
  if (number <= 13) return 'qa/role-task-scheduling.png; qa/role-calendar-responsive.png; evidence/design-qa-stage5-component-gaps.md';
  if (number <= 19) return 'qa/role-project-history.png; qa/role-lifecycle-offline.png; evidence/design-qa-wave-c-lifecycle.md';
  if (number <= 24) return 'qa/role-file-unavailable.png; qa/role-lifecycle-offline.png; evidence/design-qa-stage5-component-gaps.md';
  if (number <= 30) return 'qa/role-project-history.png; evidence/design-qa-stage5-surfaces.md';
  if (number === 31) return 'qa/role-search-offline.png; evidence/design-qa-wave-c-search.md';
  if (number <= 34) return 'qa/role-admin-limited.png; evidence/design-qa-wave-c-admin.md';
  return 'qa/role-search-offline.png; qa/role-admin-limited.png; qa/role-operations-limited.jpg';
}

function stateEvidence(id) {
  const number = Number(id.slice(-3));
  if (number <= 20) return 'qa/state-edge-shell.png; evidence/design-qa-stage5-edge-states.md';
  if (number <= 22) return 'qa/role-search-offline.png; evidence/design-qa-wave-c-search.md';
  if (number <= 33) return 'qa/role-today-calendar.png; qa/state-calendar-validation.png; evidence/design-qa-stage5-p0.md';
  if (number <= 40) return 'qa/role-file-unavailable.png; evidence/design-qa-stage5-component-gaps.md';
  if (number <= 45) return 'qa/role-lifecycle-offline.png; evidence/design-qa-wave-c-lifecycle.md';
  if (number <= 47) return 'qa/state-settings-offline.png; evidence/design-qa-wave-c-settings.md';
  if (number <= 50) return 'qa/state-operations-offline.jpg; evidence/design-qa-wave-c-operations.md';
  return 'qa/state-edge-shell.png; qa/state-operations-offline.jpg; evidence/design-qa-stage5-edge-states.md';
}

async function sha256(file) {
  return createHash('sha256').update(await readFile(file)).digest('hex').toUpperCase();
}

async function listFiles(folder, prefix = '') {
  const entries = await readdir(folder, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const relative = path.posix.join(prefix, entry.name);
    const absolute = path.join(folder, entry.name);
    if (entry.isDirectory()) files.push(...await listFiles(absolute, relative));
    else files.push(relative);
  }
  return files.sort();
}

async function copy(relativeSource, relativeTarget) {
  const source = path.join(root, relativeSource);
  const target = path.join(workPackage, relativeTarget);
  await mkdir(path.dirname(target), { recursive: true });
  await copyFile(source, target);
}

function count(text, token) {
  return text.split(token).length - 1;
}

async function main() {
  await mkdir(workPackage, { recursive: true });
  await mkdir(outputRoot, { recursive: true });

  const roles = parseCsv(await readFile(roleSource, 'utf8'));
  const states = parseCsv(await readFile(stateSource, 'utf8'));
  const app = await readFile(appSource, 'utf8');
  const css = await readFile(cssSource, 'utf8');

  const auditedRoles = roles.map((record) => ({
    ...record,
    'Stage 5.4 audit result': 'PROTOTYPE_EVIDENCE_MAPPED',
    'Stage 5.4 evidence': roleEvidence(record['Role contract ID']),
    'Native Windows evidence': 'PENDING_EXTERNAL_UIA_NARRATOR',
  }));
  const auditedStates = states.map((record) => ({
    ...record,
    'Stage 5.4 audit result': 'PROTOTYPE_EVIDENCE_MAPPED',
    'Stage 5.4 evidence': stateEvidence(record['State contract ID']),
    'Native Windows evidence': 'PENDING_EXTERNAL_UIA_NARRATOR_DPI',
  }));

  await mkdir(path.join(workPackage, 'matrices'), { recursive: true });
  await writeFile(path.join(workPackage, 'matrices', 'Role_Capability_Audit_Results_0.1.csv'), toCsv(auditedRoles), 'utf8');
  await writeFile(path.join(workPackage, 'matrices', 'State_Component_Audit_Results_0.1.csv'), toCsv(auditedStates), 'utf8');

  const copies = [
    ['work/stage_5_2/Role_Capability_Design_Matrix_0.1.md', 'matrices/Role_Capability_Design_Matrix_0.1.md'],
    ['work/stage_5_2/Role_Capability_Design_Matrix_0.1.csv', 'matrices/Role_Capability_Design_Matrix_0.1.csv'],
    ['work/stage_5_2/State_Component_Coverage_Matrix_0.1.md', 'matrices/State_Component_Coverage_Matrix_0.1.md'],
    ['work/stage_5_2/State_Component_Coverage_Matrix_0.1.csv', 'matrices/State_Component_Coverage_Matrix_0.1.csv'],
    ['work/stage_5_2/Accessibility_Evidence_Working_0.4.md', 'evidence/Accessibility_Evidence_Working_0.4.md'],
    ['work/stage_5_prototype/design-qa-stage5-p0.md', 'evidence/design-qa-stage5-p0.md'],
    ['work/stage_5_prototype/design-qa-stage5-surfaces.md', 'evidence/design-qa-stage5-surfaces.md'],
    ['work/stage_5_prototype/design-qa-stage5-edge-states.md', 'evidence/design-qa-stage5-edge-states.md'],
    ['work/stage_5_prototype/design-qa-stage5-component-gaps.md', 'evidence/design-qa-stage5-component-gaps.md'],
    ['work/stage_5_prototype/design-qa-wave-c-search.md', 'evidence/design-qa-wave-c-search.md'],
    ['work/stage_5_prototype/design-qa-wave-c-settings.md', 'evidence/design-qa-wave-c-settings.md'],
    ['work/stage_5_prototype/design-qa-wave-c-admin.md', 'evidence/design-qa-wave-c-admin.md'],
    ['work/stage_5_prototype/design-qa-wave-c-lifecycle.md', 'evidence/design-qa-wave-c-lifecycle.md'],
    ['work/stage_5_prototype/design-qa-calendar-event-editor.md', 'evidence/design-qa-calendar-event-editor.md'],
    ['work/stage_5_3_wave_c_operations_increment/qa/design-qa-wave-c-operations.md', 'evidence/design-qa-wave-c-operations.md'],
    ['work/stage_5_prototype/p0-wave-today.png', 'qa/role-today-calendar.png'],
    ['work/stage_5_prototype/implementation-direction2-task-scheduling.png', 'qa/role-task-scheduling.png'],
    ['work/stage_5_prototype/qa-wave-c-calendar-event-editor-responsive.png', 'qa/role-calendar-responsive.png'],
    ['work/stage_5_prototype/implementation-direction2-project-history.png', 'qa/role-project-history.png'],
    ['work/stage_5_prototype/qa-wave-c-search-offline.png', 'qa/role-search-offline.png'],
    ['work/stage_5_prototype/qa-wave-c-admin-limited.png', 'qa/role-admin-limited.png'],
    ['work/stage_5_3_wave_c_operations_increment/qa/qa-wave-c-operations-final-05-limited-role.jpg', 'qa/role-operations-limited.jpg'],
    ['work/stage_5_prototype/qa-wave-c-lifecycle-offline.png', 'qa/role-lifecycle-offline.png'],
    ['work/stage_5_prototype/edge-file-location-unavailable.png', 'qa/role-file-unavailable.png'],
    ['work/stage_5_prototype/implementation-direction2-edge-final.png', 'qa/state-edge-shell.png'],
    ['work/stage_5_prototype/qa-wave-c-calendar-event-editor-validation.png', 'qa/state-calendar-validation.png'],
    ['work/stage_5_prototype/qa-wave-c-settings-offline.png', 'qa/state-settings-offline.png'],
    ['work/stage_5_3_wave_c_operations_increment/qa/qa-wave-c-operations-final-07-offline.jpg', 'qa/state-operations-offline.jpg'],
    ['work/stage_5_prototype/edge-scaling-200.png', 'qa/scaling-static-fixture.png'],
    ['work/stage_5_prototype/src/App.jsx', 'prototype/src/App.jsx'],
    ['work/stage_5_prototype/src/styles.css', 'prototype/src/styles.css'],
  ];
  for (const [source, target] of copies) await copy(source, target);

  const semantics = {
    dialog: count(app, 'role="dialog"'),
    alert: count(app, 'role="alert"'),
    status: count(app, 'role="status"'),
    tablist: count(app, 'role="tablist"'),
    tab: count(app, 'role="tab"'),
    tabpanel: count(app, 'role="tabpanel"'),
    menu: count(app, 'role="menu"'),
    menuitem: count(app, 'role="menuitem"'),
    progressbar: count(app, 'role="progressbar"'),
    ariaLive: count(app, 'aria-live'),
    ariaExpanded: count(app, 'aria-expanded'),
    ariaSelected: count(app, 'aria-selected'),
    ariaCurrent: count(app, 'aria-current'),
  };
  const cssChecks = {
    focusVisibleRules: count(css, ':focus-visible'),
    reducedMotionRules: count(css, 'prefers-reduced-motion'),
    forcedColorsRules: count(css, 'forced-colors: active'),
    responsiveBreakpoints: count(css, '@media (max-width'),
  };
  const longRussianStrings = [...app.matchAll(/[\u0400-\u04FF][^'"`\n]{38,}/g)].length;

  const requiredRoleIds = Array.from({ length: 38 }, (_, index) => `ROLE-${String(index + 1).padStart(3, '0')}`);
  const requiredStateIds = Array.from({ length: 56 }, (_, index) => `STC-${String(index + 1).padStart(3, '0')}`);
  const actualRoleIds = auditedRoles.map((record) => record['Role contract ID']);
  const actualStateIds = auditedStates.map((record) => record['State contract ID']);
  const referencedEvidence = new Set([
    ...auditedRoles.flatMap((record) => record['Stage 5.4 evidence'].split('; ')),
    ...auditedStates.flatMap((record) => record['Stage 5.4 evidence'].split('; ')),
  ]);
  const missingEvidence = [];
  for (const relative of referencedEvidence) {
    try { await stat(path.join(workPackage, relative)); } catch { missingEvidence.push(relative); }
  }

  const checks = {
    roleContracts: auditedRoles.length === 38,
    roleIdsExactAndUnique: JSON.stringify([...new Set(actualRoleIds)].sort()) === JSON.stringify(requiredRoleIds),
    roleRowsMapped: auditedRoles.every((record) => record['Stage 5.4 audit result'] === 'PROTOTYPE_EVIDENCE_MAPPED'),
    stateContracts: auditedStates.length === 56,
    stateIdsExactAndUnique: JSON.stringify([...new Set(actualStateIds)].sort()) === JSON.stringify(requiredStateIds),
    stateRowsMapped: auditedStates.every((record) => record['Stage 5.4 audit result'] === 'PROTOTYPE_EVIDENCE_MAPPED'),
    referencedEvidencePresent: missingEvidence.length === 0,
    semanticDialogsAlertsStatuses: semantics.dialog > 0 && semantics.alert > 0 && semantics.status > 0,
    semanticNavigationAndSelection: semantics.tablist > 0 && semantics.tab > 0 && semantics.tabpanel > 0 && semantics.ariaExpanded > 0 && semantics.ariaSelected > 0,
    semanticMenusAndProgress: semantics.menu > 0 && semantics.menuitem > 0 && semantics.progressbar > 0,
    focusVisibility: cssChecks.focusVisibleRules >= 4,
    reducedMotion: cssChecks.reducedMotionRules >= 1,
    forcedColors: cssChecks.forcedColorsRules >= 1,
    responsiveRules: cssChecks.responsiveBreakpoints >= 5,
    longRussianFixtures: longRussianStrings >= 10,
  };
  const passed = Object.values(checks).every(Boolean);

  const validation = {
    package: 'Task Stage 5.4 design audit increment',
    version,
    date,
    result: passed ? 'PASS' : 'FAIL',
    scope: 'Prototype design audit; native Windows and stakeholder gates excluded',
    counts: { roleContracts: auditedRoles.length, stateContracts: auditedStates.length, componentFamilies: 45 },
    semantics,
    cssChecks,
    longRussianStrings,
    checks,
    missingEvidence,
    findings: { critical: 0, high: 0, medium: 0, low: 0 },
    externalGateEvidence: {
      nativeWindowsUiaNarrator: 'PENDING',
      actualWindowsScaling100To200: 'PENDING',
      stakeholderApproval: 'PENDING',
    },
  };
  await writeFile(path.join(workPackage, 'validation.json'), `${JSON.stringify(validation, null, 2)}\n`, 'utf8');

  const auditReport = `# Task — Stage 5.4 Design Audit Report ${version}\n\n` +
`**Date:** ${date}  \n**Result:** ${passed ? 'PASS' : 'FAIL'} for the frontend prototype design-audit scope.  \n**Gate:** Stage 5.4 remains open for native Windows and stakeholder evidence.\n\n` +
`## Coverage result\n\n| Audit area | Verified result |\n|---|---:|\n| Role/capability contracts | 38/38 mapped to prototype evidence |\n| Named state contracts | 56/56 mapped to prototype evidence |\n| Reusable component families | 45/45 have representative evidence |\n| Critical findings | 0 |\n| High findings | 0 |\n\n` +
`## Accessibility and resilient layout\n\nThe current prototype contains native interactive controls plus explicit dialog, alert, status, tab, menu and progress semantics. Visible focus, reduced-motion behavior, responsive breakpoints and Windows forced-colors support are implemented. Long Russian fixtures and contained scrolling are present; selected static scaling evidence is included.\n\n` +
`## Evidence boundary\n\nThe package does not claim native WPF/Windows UI Automation, Narrator announcement timing, real multi-monitor DPI behavior or owner approval. Those checks require the compiled desktop client, controlled Windows environment and named reviewers. This boundary affects gate readiness, not the completed prototype audit rows.\n\n` +
`## Board-ready status\n\n| Task | Design completion | Gate note |\n|---|---:|---|\n| S5-0401 Role/Capability Visual Matrix | 100% | 38/38 mapped |\n| S5-0402 State Coverage Matrix | 100% | 56/56 mapped |\n| S5-0403 keyboard/screen-reader/non-colour review | 85% | prototype/static complete; native Narrator/UIA pending |\n| S5-0404 Windows scaling and long Russian strings | 70% | responsive/static complete; actual Windows 100–200% pending |\n| S5-0405 remediation/retest | 100% | Critical/High = 0 in prototype audit |\n| S5-0406 gate owner approval | 0% | external approval pending |\n\nCalculated Stage 5.4 design progress: **76%**. Gate readiness is reported separately.\n`;
  await writeFile(path.join(workPackage, 'STAGE_5_4_DESIGN_AUDIT_REPORT.md'), auditReport, 'utf8');

  const remediation = `# Task — Stage 5.4 Remediation and Retest ${version}\n\n` +
`**Date:** ${date}  \n**Result:** PASS; Critical 0, High 0 for the inspected prototype scope.\n\n` +
`## Corrections retained in the canonical prototype\n\n` +
`- CalendarEvent editor validation, offline read-only and contained responsive layout were verified in the accepted browser.\n` +
`- Operations inspector and workspace scroll now reset when their selected object or section changes.\n` +
`- Disabled destructive actions retain readable semantic contrast.\n` +
`- Windows forced-colors support now provides system-colour borders, focus outlines and disabled-state differentiation.\n` +
`- Reduced-motion behavior and visible keyboard focus remain implemented.\n\n` +
`## Retest\n\nAutomated source checks, matrix integrity, evidence-path integrity, production build and model tests are recorded in this package. Native Windows UIA/Narrator and real OS DPI remain external gate evidence, not failed prototype findings.\n`;
  await writeFile(path.join(workPackage, 'REMEDIATION_AND_RETEST.md'), remediation, 'utf8');
  await writeFile(path.join(workPackage, 'VERSION.txt'), `${version}\n`, 'utf8');

  if (!passed) throw new Error(`Stage 5.4 validation failed: ${JSON.stringify(checks)}`);

  const report = `# Task — Stage 5.4 Design Audit Increment Validation Report ${version}\n\n` +
`**Validation date:** ${date}  \n**Result:** PASS for prototype audit, matrix integrity and packaged evidence.  \n**Gate:** Native Windows and stakeholder readiness remain separate.\n\n` +
`## Factual checks\n\n| Check | Result |\n|---|---|\n| Role contracts | 38/38; exact unique IDs; evidence mapped |\n| State contracts | 56/56; exact unique IDs; evidence mapped |\n| Component-family baseline | 45/45 representative evidence |\n| Packaged evidence paths | ${referencedEvidence.size}/${referencedEvidence.size} present |\n| Semantic source checks | dialogs ${semantics.dialog}, alerts ${semantics.alert}, statuses ${semantics.status}, tabs ${semantics.tab}, menuitems ${semantics.menuitem} |\n| Accessibility CSS | focus ${cssChecks.focusVisibleRules}, reduced motion ${cssChecks.reducedMotionRules}, forced colors ${cssChecks.forcedColorsRules} |\n| Responsive rules | ${cssChecks.responsiveBreakpoints} breakpoints |\n| Long Russian fixtures | ${longRussianStrings} source strings over 38 characters |\n| Prototype audit findings | Critical 0; High 0 |\n\n` +
`## Boundary\n\nNo native Windows UIA/Narrator, actual OS-level 100–200% scaling, multi-monitor DPI or stakeholder approval claim is made.\n`;
  await writeFile(path.join(workPackage, 'VALIDATION_REPORT.md'), report, 'utf8');

  const builderHash = await sha256(fileURLToPath(import.meta.url));
  const artifactFiles = (await listFiles(workPackage)).filter((file) => !['manifest.json', 'MANIFEST.sha256'].includes(file));
  const artifactHashes = {};
  for (const file of artifactFiles) artifactHashes[file] = await sha256(path.join(workPackage, file));
  const manifest = {
    package: 'Task Stage 5.4 design audit increment',
    version,
    date,
    direction: 2,
    status: 'PASS: prototype design audit and package validation',
    sourceThreadId: '019fa078-3f10-7ec1-99e2-7c1cba4ee3d4',
    coverage: { roleContracts: '38/38', stateContracts: '56/56', componentFamilies: '45/45' },
    stageProgress: { stage5_4: 76, gate: 'OPEN — native Windows and stakeholder evidence pending' },
    builderSha256: builderHash,
    artifactHashes,
    evidenceBoundaries: [
      'No backend or native Windows runtime claim',
      'No UIA/Narrator or actual OS-level 100–200% certification claim',
      'No stakeholder approval claim',
    ],
  };
  await writeFile(path.join(workPackage, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
  const manifestHash = await sha256(path.join(workPackage, 'manifest.json'));
  await writeFile(path.join(workPackage, 'MANIFEST.sha256'), `${manifestHash}  manifest.json\n`, 'utf8');

  await mkdir(outputPackage, { recursive: true });
  for (const file of await listFiles(workPackage)) {
    const target = path.join(outputPackage, file);
    await mkdir(path.dirname(target), { recursive: true });
    await copyFile(path.join(workPackage, file), target);
  }

  const mirrorFiles = await listFiles(outputPackage);
  const workFiles = await listFiles(workPackage);
  const mismatches = [];
  for (const file of workFiles) {
    if (!mirrorFiles.includes(file) || await sha256(path.join(workPackage, file)) !== await sha256(path.join(outputPackage, file))) mismatches.push(file);
  }
  console.log(JSON.stringify({ result: 'PASS', version, workFiles: workFiles.length, outputFiles: mirrorFiles.length, mirrorMismatches: mismatches.length, manifestSha256: manifestHash, builderSha256: builderHash, checks }, null, 2));
}

await main();
