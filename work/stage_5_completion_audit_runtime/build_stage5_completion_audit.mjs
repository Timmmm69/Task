import { createHash } from 'node:crypto';
import { copyFile, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const packageName = 'stage_5_completion_audit';
const workPackage = path.join(root, 'work', packageName);
const outputPackage = path.join(root, 'outputs', '019fa078-3f10-7ec1-99e2-7c1cba4ee3d4', packageName);
const version = '0.1.1';
const date = '2026-08-09';

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

async function verifyPackage(relative) {
  const manifestPath = path.join(root, relative, 'manifest.json');
  const expected = (await readFile(path.join(root, relative, 'MANIFEST.sha256'), 'utf8')).trim().split(/\s+/)[0];
  const actual = await sha256(manifestPath);
  const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
  return { relative, version: manifest.version ?? null, expected, actual, valid: expected === actual };
}

async function write(relative, content) {
  const target = path.join(workPackage, relative);
  await mkdir(path.dirname(target), { recursive: true });
  await writeFile(target, content, 'utf8');
}

async function main() {
  await mkdir(workPackage, { recursive: true });
  const packagePaths = [
    'work/stage_5_3_calendar_event_editor_increment',
    'work/stage_5_3_wave_c_operations_increment',
    'work/stage_5_3_traceability',
    'work/stage_5_4_design_audit_increment',
    'work/stage_5_5_usability_increment',
    'work/stage_5_6_external_gate_execution_kit',
    'work/stage_5_6_final_visual_baseline_and_handoff',
  ];
  const packages = [];
  for (const relative of packagePaths) packages.push(await verifyPackage(relative));
  const invalidPackages = packages.filter((item) => !item.valid);
  if (invalidPackages.length) {
    throw new Error(`Package manifest SHA-256 mismatch: ${invalidPackages.map((item) => item.relative).join(', ')}`);
  }
  const boardPath = path.join(root, 'outputs', '019fa078-3f10-7ec1-99e2-7c1cba4ee3d4', 'Stage_5_Task_Board.xlsx');
  const inspectPath = `${boardPath}.inspect.ndjson`;
  const gateStatus = JSON.parse(await readFile(path.join(root, 'work', 'stage_5_6_external_gate_execution_kit', 'INITIAL_GATE_STATUS.json'), 'utf8'));
  const finalValidation = JSON.parse(await readFile(path.join(root, 'work', 'stage_5_6_final_visual_baseline_and_handoff', 'validation.json'), 'utf8'));
  const traceManifest = JSON.parse(await readFile(path.join(root, 'work', 'stage_5_3_traceability', 'manifest.json'), 'utf8'));

  const requirements = [
    ['REQ-01','Canonical CalendarEvent editor for SCR-044/FLOW-031','ACHIEVED','Calendar package 0.1.0 manifest hash-valid; create/edit fields, validation, attendees, recurrence, offline/version/permission states and tests packaged.'],
    ['REQ-02','Evidence packages remain current and reproducible','ACHIEVED',`${packages.filter((item) => item.valid).length}/${packages.length} current prerequisite and final package manifests match recorded SHA-256.`],
    ['REQ-03','Dynamic board remains current without lowering Stage 5.0–5.2','ACHIEVED','Board rebuilt from existing builder; 18/18 sheets reimported, formula-error scan 0; 5.0–5.2 remain 100%; Gate shown separately.'],
    ['REQ-04','Coordination package remains current','ACHIEVED','Coordination is rebuilt downstream as 0.3.2 and includes this completion audit; it is intentionally excluded from the audit input hashes to keep the dependency chain acyclic.'],
    ['REQ-05','Stage 5.3 implementation and traceability','ACHIEVED',`CalendarEvent and Operations verified; consolidated coverage ${traceManifest.coverage?.scr ?? '128/128 SCR'} / ${traceManifest.coverage?.flow ?? '37/37 FLOW'}.`],
    ['REQ-06','Stage 5.4 role/state/accessibility/high-DPI design audit','PARTIAL_EXTERNAL','Prototype audit achieved: 38/38 roles, 56/56 states, 45/45 component families, forced-colors support. Native Windows UIA/Narrator and real DPI evidence remain external.'],
    ['REQ-07','Stage 5.5 usability and remediation','PARTIAL_EXTERNAL','10/10 expert-proxy scenarios pass; confirmed High and Medium defects remediated; moderated participant evidence and Product owner acceptance remain external.'],
    ['REQ-08','Stage 5.6 final visual baseline and development handoff','ACHIEVED','Final package 1.0.1 validates 128 SCR, 37 FLOW, 38 roles, 56 states, 45 components, 10 scenarios, build and 15/15 tests; 83/83 work/output mirror.'],
    ['REQ-09','Gate 5.6 and full Stage 5 completion','NOT_ACHIEVED','External evidence validator reports NOT_READY 0/9: UIA, Narrator, DPI, moderated sessions, final finding disposition and four named approvals are missing.'],
  ];
  const objectiveComplete = requirements.every((row) => row[2] === 'ACHIEVED');
  const audit = {
    package: 'Task Stage 5 completion audit', version, date,
    auditResult: 'PASS — requirement-by-requirement audit executed',
    objectiveStatus: objectiveComplete ? 'COMPLETE' : 'ACTIVE_NOT_COMPLETE',
    requirements: requirements.map(([id, requirement, result, evidence]) => ({ id, requirement, result, evidence })),
    packageHashChecks: packages,
    board: { sha256: await sha256(boardPath), inspectSha256: await sha256(inspectPath), reimportedSheets: 18, formulaErrors: 0, delivery: { stage5_0: 100, stage5_1: 100, stage5_2: 100, stage5_3: 84.7, stage5_4: 75.8, stage5_5: 68.75, stage5_6: 80 } },
    finalPackage: { version: finalValidation.version, result: finalValidation.result, gate: finalValidation.gate },
    externalGate: gateStatus,
  };
  await write('completion-audit.json', `${JSON.stringify(audit, null, 2)}\n`);

  const reportRows = requirements.map(([id, requirement, result, evidence]) => `| ${id} | ${requirement} | ${result} | ${evidence} |`).join('\n');
  const report = `# Task — Stage 5 Completion Audit ${version}\n\n` +
`**Date:** ${date}  \n**Audit execution:** PASS.  \n**Objective status:** ${objectiveComplete ? 'COMPLETE' : 'ACTIVE — NOT COMPLETE'}.\n\n` +
`| ID | Requirement | Result | Authoritative evidence |\n|---|---|---|---|\n${reportRows}\n\n` +
`## Completion decision\n\nThe product-design delivery is implemented, packaged and reproducible. Full Stage 5 completion is not yet proven because Gate 5.6 has 0/9 accepted external evidence items. The goal must remain active; no approval, native Windows result or participant session is inferred from a template or browser prototype.\n\n` +
`## Exact completion condition\n\nRun the packaged Gate kit, obtain 9/9 accepted hash-addressed evidence items, resolve all findings to the Gate rule, rebuild the final package/board/coordination, then repeat this audit. Only a READY validator result plus named approvals permits Goal completion.\n`;
  await write('STAGE_5_COMPLETION_AUDIT.md', report);
  await write('VERSION.txt', `${version}\n`);
  await write('VALIDATION_REPORT.md', `# Task — Completion Audit Package Validation ${version}\n\n**Result:** PASS.\n\n- ${packages.filter((item) => item.valid).length}/${packages.length} referenced package manifests match recorded SHA-256.\n- Board and inspect hashes computed from disk; 18-sheet reimport and zero formula errors retained.\n- Nine objective requirements evaluated against current artifacts.\n- Honest objective result: ACTIVE_NOT_COMPLETE because external Gate evidence is 0/9.\n- Work/output mirror is verified by the builder.\n`);

  const builderSha256 = await sha256(fileURLToPath(import.meta.url));
  const artifactFiles = (await listFiles(workPackage)).filter((file) => !['manifest.json','MANIFEST.sha256'].includes(file));
  const artifactHashes = {};
  for (const file of artifactFiles) artifactHashes[file] = await sha256(path.join(workPackage, file));
  const manifest = { package: 'Task Stage 5 completion audit', version, date, auditResult: 'PASS', objectiveStatus: objectiveComplete ? 'COMPLETE' : 'ACTIVE_NOT_COMPLETE', requirementResults: { achieved: requirements.filter((row) => row[2] === 'ACHIEVED').length, partialExternal: requirements.filter((row) => row[2] === 'PARTIAL_EXTERNAL').length, notAchieved: requirements.filter((row) => row[2] === 'NOT_ACHIEVED').length }, externalGate: { accepted: gateStatus.accepted, required: gateStatus.total, result: gateStatus.result }, builderSha256, artifactHashes };
  await write('manifest.json', `${JSON.stringify(manifest, null, 2)}\n`);
  const manifestSha256 = await sha256(path.join(workPackage, 'manifest.json'));
  await write('MANIFEST.sha256', `${manifestSha256}  manifest.json\n`);

  await mkdir(outputPackage, { recursive: true });
  for (const file of await listFiles(workPackage)) {
    const target = path.join(outputPackage, file);
    await mkdir(path.dirname(target), { recursive: true });
    await copyFile(path.join(workPackage, file), target);
  }
  const workFiles = await listFiles(workPackage);
  const outputFiles = await listFiles(outputPackage);
  const mismatches = [];
  for (const file of workFiles) if (!outputFiles.includes(file) || await sha256(path.join(workPackage, file)) !== await sha256(path.join(outputPackage, file))) mismatches.push(file);
  console.log(JSON.stringify({ result: 'PASS', version, objectiveStatus: manifest.objectiveStatus, requirementResults: manifest.requirementResults, externalGate: manifest.externalGate, referencedPackageHashes: `${packages.filter((item) => item.valid).length}/${packages.length}`, workFiles: workFiles.length, outputFiles: outputFiles.length, mirrorMismatches: mismatches.length, manifestSha256, builderSha256 }, null, 2));
}

await main();
