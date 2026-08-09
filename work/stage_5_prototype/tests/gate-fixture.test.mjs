import assert from "node:assert/strict";
import test from "node:test";
import { getGateAccount, hasCapability } from "../src/gateFixture.js";

test("browser prototype retains its existing writable baseline", () => {
  const account = getGateAccount(null);
  assert.equal(account.login, "ivan.s");
  assert.equal(hasCapability(account, "Task.Write"), true);
});

test("desktop observer fixture is read-only and cannot see Admin", () => {
  const account = getGateAccount({ account: { id: "observer", login: "gate.observer", role: "Observer", roleLabel: "Наблюдатель", capabilities: ["Task.Read"] } });
  assert.equal(hasCapability(account, "Task.Write"), false);
  assert.equal(hasCapability(account, "Admin.Read"), false);
});
