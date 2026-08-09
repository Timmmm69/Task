const fs = require("node:fs");
const path = require("node:path");

const PUBLIC_FIELDS = ["id", "login", "displayName", "shortName", "initials", "role", "roleLabel", "department", "capabilities"];

function loadFixture(root = __dirname) {
  return JSON.parse(fs.readFileSync(path.join(root, "fixtures", "gate-test-accounts.json"), "utf8"));
}

function requestedAccountId(argv, fallback) {
  const argument = argv.find((value) => value.startsWith("--gate-account="));
  return argument ? argument.slice("--gate-account=".length).trim().toLowerCase() : fallback;
}

function resolveAccount(argv = process.argv, root = __dirname) {
  const fixture = loadFixture(root);
  const id = requestedAccountId(argv, fixture.defaultAccountId);
  const account = fixture.accounts.find((candidate) => candidate.id === id);
  if (!account) throw new Error(`Unknown Gate account '${id}'. Allowed values: ${fixture.accounts.map(({ id: value }) => value).join(", ")}`);
  return Object.fromEntries([["fixtureId", fixture.fixtureId], ...PUBLIC_FIELDS.map((field) => [field, account[field]])]);
}

function authenticate(login, password, selectedAccountId, root = __dirname) {
  const fixture = loadFixture(root);
  const account = fixture.accounts.find((candidate) => candidate.id === selectedAccountId);
  return Boolean(account && account.login === String(login).trim().toLowerCase() && password === fixture.sharedPassword);
}

module.exports = { authenticate, loadFixture, requestedAccountId, resolveAccount };
