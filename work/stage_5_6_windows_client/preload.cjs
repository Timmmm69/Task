const { contextBridge, ipcRenderer } = require("electron");

const fixtureArgument = process.argv.find((value) => value.startsWith("--task-gate-fixture="));
const account = fixtureArgument ? JSON.parse(decodeURIComponent(fixtureArgument.slice("--task-gate-fixture=".length))) : null;

contextBridge.exposeInMainWorld("taskGateFixture", Object.freeze({ account: account ? Object.freeze(account) : null, synthetic: true }));
contextBridge.exposeInMainWorld("taskDesktop", Object.freeze({
  authenticate: (login, password) => ipcRenderer.invoke("task:authenticate", { login, password }),
  completeSignIn: () => ipcRenderer.invoke("task:complete-sign-in"),
  windowAction: (action) => {
    if (["minimize", "toggleMaximize", "close"].includes(action)) ipcRenderer.send("task:window-action", action);
  },
}));
