import { createServer } from "node:http";
import { readFileSync, statSync } from "node:fs";
import { extname, join, normalize, relative } from "node:path";
import { spawn } from "node:child_process";
import { buildDashboard, dashboardDir, readJson } from "./lib.mjs";

const host = process.env.DASHBOARD_HOST || "127.0.0.1";
const port = Number(process.env.DASHBOARD_PORT || 4178);
const publicDir = join(dashboardDir, "public");
const mime = { ".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".json": "application/json; charset=utf-8" };

function json(res, status, value) {
  res.writeHead(status, { "content-type": mime[".json"], "cache-control": "no-store" });
  res.end(JSON.stringify(value));
}

function staticFile(req, res) {
  const pathname = new URL(req.url, `http://${host}:${port}`).pathname;
  const requested = pathname === "/" ? "index.html" : decodeURIComponent(pathname.slice(1));
  const file = normalize(join(publicDir, requested));
  const inside = relative(publicDir, file);
  if (inside.startsWith("..") || inside.includes(":")) return json(res, 403, { error: "forbidden" });
  try {
    if (!statSync(file).isFile()) throw new Error("not a file");
    res.writeHead(200, { "content-type": mime[extname(file)] || "application/octet-stream", "cache-control": "no-cache" });
    res.end(readFileSync(file));
  } catch {
    json(res, 404, { error: "not_found" });
  }
}

const server = createServer((req, res) => {
  if (req.method !== "GET") return json(res, 405, { error: "method_not_allowed" });
  if (req.url.startsWith("/api/dashboard")) {
    try {
      return json(res, 200, buildDashboard(readJson("roadmap.json"), readJson("test-results.json")));
    } catch (error) {
      return json(res, 500, { error: "dashboard_data_invalid", message: error.message });
    }
  }
  staticFile(req, res);
});

server.listen(port, host, () => {
  const url = `http://${host}:${port}`;
  console.log(`Task readiness dashboard: ${url}`);
  if (process.argv.includes("--open")) {
    const command = process.platform === "win32" ? "cmd" : process.platform === "darwin" ? "open" : "xdg-open";
    const args = process.platform === "win32" ? ["/c", "start", "", url] : [url];
    spawn(command, args, { detached: true, stdio: "ignore", windowsHide: true }).unref();
  }
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => server.close(() => process.exit(0)));
}
