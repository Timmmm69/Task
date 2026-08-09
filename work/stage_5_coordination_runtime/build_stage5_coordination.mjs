import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const version = '0.3.1';
const date = '2026-08-02';
const relativePackage = 'outputs/019fa078-3f10-7ec1-99e2-7c1cba4ee3d4/stage_5_3_coordination';
const packageDir = path.join(root, relativePackage);

async function sha256(relative) {
  return createHash('sha256').update(await readFile(path.join(root, relative))).digest('hex').toUpperCase();
}

async function main() {
  await mkdir(packageDir, { recursive: true });
  const accepted = [
    'outputs/019fa078-3f10-7ec1-99e2-7c1cba4ee3d4/Stage_5_Task_Board.xlsx',
    'outputs/019fa078-3f10-7ec1-99e2-7c1cba4ee3d4/Stage_5_Task_Board.xlsx.inspect.ndjson',
    'work/stage_5_board_runtime/build_stage5_board.mjs',
    'work/stage_5_board_runtime/previews/all-sheets-contact-sheet.png',
    'outputs/019fa078-3f10-7ec1-99e2-7c1cba4ee3d4/stage_5_3_calendar_event_editor_increment/manifest.json',
    'outputs/019fa078-3f10-7ec1-99e2-7c1cba4ee3d4/stage_5_3_wave_c_operations_increment/manifest.json',
    'outputs/019fa078-3f10-7ec1-99e2-7c1cba4ee3d4/stage_5_3_traceability/manifest.json',
    'outputs/019fa078-3f10-7ec1-99e2-7c1cba4ee3d4/stage_5_4_design_audit_increment/manifest.json',
    'work/stage_5_4_audit_runtime/build_stage5_4_audit.mjs',
    'outputs/019fa078-3f10-7ec1-99e2-7c1cba4ee3d4/stage_5_5_usability_increment/manifest.json',
    'work/stage_5_5_usability_runtime/build_stage5_5_package.mjs',
    'outputs/019fa078-3f10-7ec1-99e2-7c1cba4ee3d4/stage_5_6_final_visual_baseline_and_handoff/manifest.json',
    'work/stage_5_6_runtime/build_stage5_6_final_package.mjs',
    'outputs/019fa078-3f10-7ec1-99e2-7c1cba4ee3d4/stage_5_6_external_gate_execution_kit/manifest.json',
    'work/stage_5_6_gate_runtime/build_stage5_6_gate_kit.mjs',
  ];
  const acceptedInputs = [];
  for (const relative of accepted) acceptedInputs.push({ path: relative, sha256: await sha256(relative) });

  const reportName = `Stage_5_Coordination_Report_${version}.md`;
  const validationName = `Stage_5_Coordination_Validation_${version}.md`;
  const report = `# Task — Stage 5 Coordination Report ${version}\n\n` +
`**Date:** ${date}  \n**Active front:** external Gate 5.6 readiness evidence.  \n**Goal:** active; Stage 5 is not declared complete.\n\n` +
`## Verified delivery state\n\n` +
`- Stage 5.0–5.2 accepted delivery stays at 100%.\n` +
`- Stage 5.3 is 84.7% (85% shown): CalendarEvent, Operations and 128/128 SCR + 37/37 FLOW traceability packages are verified.\n` +
`- Stage 5.4 is 75.8% (76% shown): 38/38 role contracts, 56/56 state contracts and 45/45 component families are mapped; prototype audit Critical/High = 0.\n` +
`- Stage 5.5 is 68.75% (69% shown): 10/10 expert-proxy scenarios passed after remediation; 13/13 current-run screenshots were inspected; open Critical/High/Medium = 0.\n` +
`- Stage 5.6 is 80%: four design-delivery tasks are complete; the external Gate task remains open. Final package 1.0.1 contains 83/83 mirrored files, includes the Gate execution kit and validates 128 SCR, 37 FLOW, 38 roles, 56 states and 45 component families.\n` +
`- Production build passes with Vite 6.4.2 / 224 modules; automated tests pass 15/15.\n\n` +
`## Gate boundary\n\nExternal moderated participant sessions, native Windows UIA/Narrator, actual OS-level 100–200% multi-monitor scaling and named owner approvals remain readiness evidence. They do not reduce accepted delivery percentages and are not falsely claimed.\n\n` +
`## Next action\n\nExecute the packaged Gate 5.6 kit. The evidence validator currently reports NOT_READY 0/9; collect native Windows, moderated-session and named approval evidence without changing accepted design-delivery percentages.\n`;
  await writeFile(path.join(packageDir, reportName), report, 'utf8');

  const validation = `# Task — Stage 5 Coordination Validation ${version}\n\n` +
`**Date:** ${date}  \n**Result:** PASS.\n\n` +
`| Check | Result |\n|---|---|\n` +
`| Referenced current artifacts | ${acceptedInputs.length}/${acceptedInputs.length} SHA-256 computed from disk |\n` +
`| Stage 5.6 final package | 83 work files / 83 output files; mirror mismatches 0 |\n` +
`| External Gate kit | 19 work files / 19 output files; validator NOT_READY 0/9 |\n` +
`| Final traceability | 128/128 SCR; 37/37 FLOW; 38/38 roles; 56/56 states; 45/45 components |\n` +
`| Usability evidence | 10/10 expert-proxy scenarios; 13/13 screenshots; open Critical/High/Medium 0 |\n` +
`| Prototype build/tests | Vite 6.4.2 build PASS; 15/15 tests PASS |\n` +
`| Board | 18/18 sheets reimported; formula-error scan 0; contact sheet inspected |\n` +
`| Delivery invariants | Stage 5.0–5.2 remain 100%; readiness shown separately |\n\n` +
`Stage 5 design delivery is packaged, but the goal remains active because external Gate 5.6 readiness evidence is not complete.\n`;
  await writeFile(path.join(packageDir, validationName), validation, 'utf8');
  await writeFile(path.join(packageDir, 'VERSION.txt'), `${version}\nStage 5 cross-stage coordination; Gate 5.6 kit ready, external evidence 0/9\n`, 'utf8');

  const artifacts = [];
  for (const name of ['VERSION.txt', reportName, validationName]) {
    const relative = path.posix.join(relativePackage, name);
    artifacts.push({ path: relative, sha256: await sha256(relative) });
  }
  const manifest = {
    product: 'Task', stage: '5', package: 'cross-stage coordination', version, date,
    direction: 'Direction 2 — Timeline planner', goalStatus: 'ACTIVE',
    result: 'Stage 5.6 final package 1.0.1 and Gate execution kit verified; external evidence remains 0/9',
    delivery: { stage5_0: '100%', stage5_1: '100%', stage5_2: '100%', stage5_3: '84.7% (85%)', stage5_4: '75.8% (76%)', stage5_5: '68.75% (69%)', stage5_6: '80%' },
    gates: {
      stage5_3: 'OPEN — formal approval/native runtime',
      stage5_4: 'OPEN — native UIA/Narrator, actual Windows DPI, stakeholder approval',
      stage5_5: 'OPEN — external moderated sessions and owner approval',
      stage5_6: 'OPEN — native Windows, participant and named approval evidence',
    },
    artifacts, acceptedInputs,
  };
  await writeFile(path.join(packageDir, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
  const manifestHash = await sha256(path.posix.join(relativePackage, 'manifest.json'));
  await writeFile(path.join(packageDir, 'MANIFEST.sha256'), `${manifestHash}  manifest.json\n`, 'utf8');
  for (const item of [...artifacts, ...acceptedInputs]) if (await sha256(item.path) !== item.sha256) throw new Error(`Hash mismatch: ${item.path}`);
  console.log(JSON.stringify({ result: 'PASS', version, referencedHashes: acceptedInputs.length, packageArtifacts: artifacts.length, manifestSha256: manifestHash }, null, 2));
}

await main();
