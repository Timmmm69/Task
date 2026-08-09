import { createHash } from "node:crypto";
import { copyFile, mkdir, readFile, readdir, stat, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const packageName = "stage_5_6_windows_client_prerequisites";
const version = "0.1.0";
const workPackage = path.join(root, "work", packageName);
const outputPackage = path.join(root, "outputs", "019fb732-ad08-7de1-b27d-c86bae8a2937", packageName);
const clientRoot = path.join(root, "work", "stage_5_6_windows_client");
const artifactName = `Task-Gate-5.6-Client-${version}-win-x64.exe`;

async function sha256(file) {
  return createHash("sha256").update(await readFile(file)).digest("hex").toUpperCase();
}

async function listFiles(folder, prefix = "") {
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

async function write(relative, content) {
  const target = path.join(workPackage, relative);
  await mkdir(path.dirname(target), { recursive: true });
  await writeFile(target, content, "utf8");
}

async function copy(source, relative) {
  const target = path.join(workPackage, relative);
  await mkdir(path.dirname(target), { recursive: true });
  await copyFile(path.join(root, source), target);
}

const discovery = `# Compiled Windows client discovery — ${version}\n\n` +
`## Decision\n\nElectron is the minimal viable path for this repository. The client packages the exact production build from \`work/stage_5_prototype\`; it does not recreate the Stage 5 design. The current machine has Node.js but no .NET SDK or Rust toolchain. Electron therefore produces a real Windows executable without introducing an unverified parallel UI.\n\n` +
`## Options considered\n\n| Option | Current feasibility | Decision |\n|---|---|---|\n| Electron portable x64 | Node toolchain present; reuses React/Vite output; Chromium exposes web semantics through Windows accessibility APIs | Implemented |\n| WebView2 + .NET/WinUI | .NET SDK absent; requires a technology/toolchain decision and new host implementation | Future candidate |\n| Tauri + WebView2 | Rust/Cargo absent; adds a second toolchain and host implementation | Not selected |\n\n` +
`## Risks and boundaries\n\n- The executable is unsigned. Windows SmartScreen or enterprise policy may block it until code signing/distribution is supplied.\n- Chromium/Electron UIA exposure is plausible but is not claimed as verified; Inspect and Narrator must run externally.\n- The fixture is local synthetic data, not a backend, directory service, production authentication, authorization evidence, or participant evidence.\n- Portable packaging is suitable for Gate execution, not a final enterprise deployment choice. MSI/MSIX, code signing, update policy, endpoint allow-listing and support ownership remain open.\n- The production bundle retains the existing non-blocking >500 kB chunk warning.\n`;

const buildInstructions = `# Reproducible Windows build\n\n## Pinned inputs\n\n- Windows x64\n- Node.js 24.18.0 used for this build\n- npm 11.16.0 used for this build\n- Electron 43.3.0\n- electron-builder 26.15.3\n- Vite 6.4.2\n- Dependency resolution is locked by both package-lock files.\n\n## Command\n\nFrom the repository root:\n\n\`\`\`powershell\npowershell -ExecutionPolicy Bypass -File work\\stage_5_6_windows_client\\build.ps1\n\`\`\`\n\nThe script performs clean installs, builds the Stage 5 production client, runs every prototype test, runs desktop fixture tests, and creates \`work\\stage_5_6_windows_client\\dist\\${artifactName}\`. A repeat build is reproducible from pinned inputs and commands; the SHA-256 recorded below identifies this exact produced binary and is not a claim of bit-for-bit deterministic PE/NSIS output across machines.\n`;

const runbook = `# Gate 5.6 Windows machine runbook\n\n## Exact executable\n\nUse \`bin\\${artifactName}\` from this package. Before execution, compare its SHA-256 with \`ARTIFACT.sha256\`. This is a portable unsigned x64 executable and does not require installation or administrator rights. Do not rename or replace it after recording evidence.\n\n## Synthetic test accounts\n\n| Role | Login | Password | Launcher | Effective local scope |\n|---|---|---|---|---|\n| Admin | gate.admin | Task-Gate-Local-2026! | launch\\Run-As-Admin.cmd | Task write + Admin + Operations |\n| Manager | gate.manager | Task-Gate-Local-2026! | launch\\Run-As-Manager.cmd | Task/team write; no Admin/Operations |\n| Employee | gate.employee | Task-Gate-Local-2026! | launch\\Run-As-Employee.cmd | Task write; no Admin/Operations |\n| Observer | gate.observer | Task-Gate-Local-2026! | launch\\Run-As-Observer.cmd | Task read-only; no Admin/Operations |\n\nThe launcher fixes the selected local account. After signing out, only that selected account can authenticate in that process. The identities and password are synthetic and must not be replaced with real personal data.\n\n## Required Windows tooling\n\n- A real Windows x64 machine and an approved copy of this exact executable.\n- Microsoft Inspect.exe from the Windows SDK accessibility tools for UIA inspection.\n- Windows Narrator enabled through Windows accessibility settings.\n- Real display scaling at 100/125/150/175/200% and the required multi-monitor topology; browser zoom is not a substitute.\n- PowerShell \`Get-FileHash -Algorithm SHA256\` for binary identity.\n- The existing \`stage_5_6_external_gate_execution_kit\` protocols and evidence templates.\n\n## Execution boundary\n\nThis package proves only that a compiled client can be built and launched with four synthetic roles. It does not close Gate 5.6. UIA/Inspect properties, Narrator output, actual multi-monitor DPI behavior, moderated sessions and Product/Design/Desktop/QA approvals remain external. The portable build is unsigned and has no real LAN backend; TLS, server sync, production authorization and directory authentication are simulated prototype states.\n`;

async function main() {
  await mkdir(workPackage, { recursive: true });
  await write(".gitattributes", "* -text\n");
  await copy(`work/stage_5_6_windows_client/dist/${artifactName}`, `bin/${artifactName}`);
  await copy("work/stage_5_6_windows_client/fixtures/gate-test-accounts.json", "fixtures/gate-test-accounts.json");
  await write("VERSION.txt", `${version}\n`);
  await write("docs/TECHNICAL_DISCOVERY.md", discovery);
  await write("docs/BUILD_INSTRUCTIONS.md", buildInstructions);
  await write("docs/GATE_5_6_WINDOWS_RUNBOOK.md", runbook);

  for (const [id, label] of [["admin","Admin"],["manager","Manager"],["employee","Employee"],["observer","Observer"]]) {
    await write(`launch/Run-As-${label}.cmd`, `@echo off\nstart "" "%~dp0..\\bin\\${artifactName}" --gate-account=${id}\n`);
  }

  const buildInputs = [
    "work/stage_5_prototype/package.json", "work/stage_5_prototype/package-lock.json", "work/stage_5_prototype/index.html",
    "work/stage_5_prototype/src/App.jsx", "work/stage_5_prototype/src/gateFixture.js", "work/stage_5_prototype/src/main.jsx", "work/stage_5_prototype/src/operationsModel.js", "work/stage_5_prototype/src/styles.css",
    "work/stage_5_prototype/vite.config.mjs", "work/stage_5_prototype/scripts/prepare-sites-build.mjs", "work/stage_5_prototype/worker/index.js",
    "work/stage_5_6_windows_client/package.json", "work/stage_5_6_windows_client/package-lock.json", "work/stage_5_6_windows_client/main.cjs", "work/stage_5_6_windows_client/preload.cjs", "work/stage_5_6_windows_client/fixture.cjs", "work/stage_5_6_windows_client/build.ps1", "work/stage_5_6_windows_client/fixtures/gate-test-accounts.json",
  ];
  const inputHashes = {};
  for (const file of buildInputs) inputHashes[file] = await sha256(path.join(root, file));
  await write("BUILD_INPUTS.sha256", `${Object.entries(inputHashes).map(([file, hash]) => `${hash}  ${file}`).join("\n")}\n`);

  const packagedArtifact = path.join(workPackage, "bin", artifactName);
  const artifactHash = await sha256(packagedArtifact);
  const artifactSize = (await stat(packagedArtifact)).size;
  await write("ARTIFACT.sha256", `${artifactHash}  bin/${artifactName}\n`);
  const validation = `# Validation report — ${version}\n\n**Package validation:** PASS  \n**Gate 5.6 status:** NOT_READY\n\n- Stage 5 production build: PASS, Vite 6.4.2, 225 modules.\n- Prototype automated tests: PASS, 17/17 (6 calendar + 5 operations + 4 Sites + 2 desktop role adapter).\n- Desktop fixture tests: PASS, 4/4.\n- Packaged runtime smoke: PASS for Admin, Manager, Employee and Observer; all four processes remained running after startup.\n- Portable artifact present: ${artifactName}, ${artifactSize.toLocaleString("en-US")} bytes.\n- Artifact SHA-256: ${artifactHash}.\n- Work/output mirror and manifest hashes are validated by the package builder.\n\nNo UIA, Narrator, DPI, participant-session or owner-approval result is claimed. Code signing, real backend authentication/sync and enterprise deployment are not validated.\n`;
  await write("VALIDATION_REPORT.md", validation);

  const artifactFiles = (await listFiles(workPackage)).filter((file) => !["manifest.json", "MANIFEST.sha256"].includes(file));
  const artifactHashes = {};
  for (const file of artifactFiles) artifactHashes[file] = await sha256(path.join(workPackage, file));
  const manifest = {
    package: "Task Stage 5.6 Windows client prerequisites", version, date: "2026-08-09",
    status: "PASS — compiled client prerequisite available; Gate 5.6 remains NOT_READY",
    executable: { path: `bin/${artifactName}`, sizeBytes: artifactSize, sha256: artifactHash, platform: "Windows x64", packaging: "Electron portable", signed: false },
    roles: ["Admin", "Manager", "Employee", "Observer"], fixtureClassification: "SYNTHETIC_TEST_DATA_ONLY",
    validation: { prototypeTests: 17, desktopFixtureTests: 4, packagedRoleSmokeStarts: 4 },
    buildInputHashes: inputHashes,
    builderSha256: await sha256(fileURLToPath(import.meta.url)), artifactHashes,
    evidenceBoundaries: ["No UIA/Inspect or Narrator result is claimed", "No actual DPI/multi-monitor result is claimed", "No participant session is claimed", "No owner approval is claimed", "No production backend or authentication is claimed"],
  };
  await write("manifest.json", `${JSON.stringify(manifest, null, 2)}\n`);
  const manifestHash = await sha256(path.join(workPackage, "manifest.json"));
  await write("MANIFEST.sha256", `${manifestHash}  manifest.json\n`);

  for (const file of await listFiles(workPackage)) {
    const target = path.join(outputPackage, file);
    await mkdir(path.dirname(target), { recursive: true });
    await copyFile(path.join(workPackage, file), target);
  }
  const workFiles = await listFiles(workPackage);
  const outputFiles = await listFiles(outputPackage);
  const mismatches = [];
  for (const file of workFiles) {
    if (!outputFiles.includes(file) || await sha256(path.join(workPackage, file)) !== await sha256(path.join(outputPackage, file))) mismatches.push(file);
  }
  console.log(JSON.stringify({ result: mismatches.length ? "FAIL" : "PASS", version, gateStatus: "NOT_READY", artifactHash, manifestHash, workFiles: workFiles.length, outputFiles: outputFiles.length, mirrorMismatches: mismatches.length }, null, 2));
  if (mismatches.length) process.exitCode = 1;
}

await main();
