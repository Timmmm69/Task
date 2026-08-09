const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");

const client = path.resolve(__dirname, "..");
const signIn = fs.readFileSync(path.join(client, "sign-in.html"), "utf8");
const preload = fs.readFileSync(path.join(client, "preload.cjs"), "utf8");

test("sign-in screen exposes labelled keyboard inputs", () => {
  assert.match(signIn, /<label for="login">Логин<input id="login"/);
  assert.match(signIn, /<label for="password">Пароль<input id="password"/);
  assert.match(signIn, /<button type="submit">Войти<\/button>/);
});

test("sign-in completion remains mediated by the preload bridge", () => {
  assert.match(signIn, /window\.taskDesktop\.completeSignIn\(\)/);
  assert.match(preload, /completeSignIn: \(\) => ipcRenderer\.invoke\("task:complete-sign-in"\)/);
});
