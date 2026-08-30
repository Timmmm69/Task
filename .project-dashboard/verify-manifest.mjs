import { createHash } from "node:crypto";
import { readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { dashboardDir } from "./lib.mjs";

const hash = (path) => createHash("sha256").update(readFileSync(path)).digest("hex");
const manifestPath = join(dashboardDir, "manifest.json");
const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
const errors = [];
for (const entry of manifest.files) {
  const path = join(dashboardDir, entry.path);
  try {
    if (statSync(path).size !== entry.bytes) errors.push(`${entry.path}: size mismatch`);
    if (hash(path) !== entry.sha256) errors.push(`${entry.path}: SHA-256 mismatch`);
  } catch {
    errors.push(`${entry.path}: missing`);
  }
}
const recorded = readFileSync(join(dashboardDir, "MANIFEST.sha256"), "utf8").trim().split(/\s+/)[0];
if (recorded !== hash(manifestPath)) errors.push("manifest.json: SHA-256 mismatch");
if (errors.length) {
  console.error(errors.join("\n"));
  process.exit(1);
}
console.log(`Manifest PASS: ${manifest.files.length} files, version ${manifest.version}`);
