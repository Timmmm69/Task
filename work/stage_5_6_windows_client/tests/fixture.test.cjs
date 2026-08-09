const assert = require("node:assert/strict");
const test = require("node:test");
const { authenticate, loadFixture, resolveAccount } = require("../fixture.cjs");

test("fixture contains exactly the four Gate role lenses", () => {
  const fixture = loadFixture();
  assert.equal(fixture.classification, "SYNTHETIC_TEST_DATA_ONLY");
  assert.deepEqual(fixture.accounts.map(({ role }) => role), ["Admin", "Manager", "Employee", "Observer"]);
  assert.equal(new Set(fixture.accounts.map(({ login }) => login)).size, 4);
});

test("public fixture never exposes the shared password", () => {
  const account = resolveAccount(["task.exe", "--gate-account=admin"]);
  assert.equal(account.capabilities.includes("Operations.Write"), true);
  assert.equal("sharedPassword" in account, false);
});

test("unknown account is rejected instead of escalating permissions", () => {
  assert.throws(() => resolveAccount(["task.exe", "--gate-account=root"]), /Unknown Gate account/);
});

test("fixture authentication accepts only the selected synthetic account", () => {
  assert.equal(authenticate("gate.observer", "Task-Gate-Local-2026!", "observer"), true);
  assert.equal(authenticate("gate.admin", "Task-Gate-Local-2026!", "observer"), false);
  assert.equal(authenticate("gate.observer", "wrong", "observer"), false);
});
