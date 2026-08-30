import { createHash } from "node:crypto";
import { readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { join, relative } from "node:path";
import { dashboardDir } from "./lib.mjs";

const excluded = new Set(["manifest.json", "MANIFEST.sha256"]);
function files(dir) {
  return readdirSync(dir).flatMap((name) => {
    const full = join(dir, name);
    return statSync(full).isDirectory() ? files(full) : [full];
  });
}
function hash(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}
const entries = files(dashboardDir)
  .map((path) => ({ path: relative(dashboardDir, path).replaceAll("\\", "/"), sha256: hash(path), bytes: statSync(path).size }))
  .filter((entry) => !excluded.has(entry.path))
  .sort((a, b) => a.path.localeCompare(b.path));
const manifest = {
  package: "Task Development / Handoff Readiness Dashboard",
  version: readFileSync(join(dashboardDir, "VERSION"), "utf8").trim(),
  generated_at: new Date().toISOString(),
  algorithm: "SHA-256",
  files: entries
};
writeFileSync(join(dashboardDir, "manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`);
const manifestHash = hash(join(dashboardDir, "manifest.json"));
writeFileSync(join(dashboardDir, "MANIFEST.sha256"), `${manifestHash}  manifest.json\n`);
console.log(`${entries.length} files; manifest ${manifestHash}`);
