const path = require("node:path");
const { app, BrowserWindow, ipcMain, shell } = require("electron");
const { authenticate, resolveAccount } = require("./fixture.cjs");

// Must be present before `ready` so Chromium starts the renderer with the
// accessibility tree enabled. This complements the runtime API call below.
app.commandLine.appendSwitch("force-renderer-accessibility");

const selectedAccount = resolveAccount(process.argv);

function clientIndexPath() {
  return app.isPackaged ? path.join(process.resourcesPath, "client", "index.html") : path.resolve(__dirname, "..", "stage_5_prototype", "dist", "client", "index.html");
}

function createWindow() {
  const account = selectedAccount;
  const mainWindow = new BrowserWindow({
    width: 1440, height: 960, minWidth: 1024, minHeight: 720, show: false, frame: false,
    backgroundColor: "#f3f5f7", title: `Task — ${account.roleLabel} — Gate 5.6`,
    accessibleTitle: `Task, тестовая роль ${account.roleLabel}`,
    webPreferences: {
      preload: path.join(__dirname, "preload.cjs"), nodeIntegration: false, contextIsolation: true, sandbox: true, webSecurity: true,
      additionalArguments: [`--task-gate-fixture=${encodeURIComponent(JSON.stringify(account))}`],
    },
  });
  mainWindow.removeMenu();
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith("https://")) void shell.openExternal(url);
    return { action: "deny" };
  });
  mainWindow.webContents.on("will-navigate", (event) => event.preventDefault());
  mainWindow.once("ready-to-show", () => mainWindow.show());
  mainWindow.loadFile(clientIndexPath()).catch((error) => { console.error("Failed to load Stage 5 client:", error); app.exit(1); });
}

ipcMain.on("task:window-action", (event, action) => {
  const window = BrowserWindow.fromWebContents(event.sender);
  if (!window) return;
  if (action === "minimize") window.minimize();
  if (action === "toggleMaximize") window.isMaximized() ? window.unmaximize() : window.maximize();
  if (action === "close") window.close();
});

ipcMain.handle("task:authenticate", (_event, credentials) => authenticate(credentials?.login, credentials?.password, selectedAccount.id));

app.whenReady().then(() => {
  // Gate 5.6 is evaluated with native Windows UI Automation and Narrator.
  // Electron does not always detect those tools early enough in a portable
  // process, so expose Chromium's accessibility tree explicitly before the
  // renderer is created.
  app.setAccessibilitySupportEnabled(true);
  app.setAppUserModelId("by.company.task.gate56");
  createWindow();
  app.on("activate", () => { if (BrowserWindow.getAllWindows().length === 0) createWindow(); });
});
app.on("window-all-closed", () => app.quit());
