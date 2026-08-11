const fallbackAccount = Object.freeze({
  fixtureId: "browser-prototype-default", id: "employee", login: "ivan.s", displayName: "Иван Сергеев", shortName: "Иван С.", initials: "ИС",
  role: "Employee", roleLabel: "Сотрудник", department: "Отдел продаж",
  capabilities: ["Task.Read", "Task.Write", "Admin.Read", "Admin.Write", "Operations.Read", "Operations.Write"],
});

export function getGateAccount(source = globalThis.taskGateFixture) {
  const account = source?.account;
  if (!account || !Array.isArray(account.capabilities)) return fallbackAccount;
  return Object.freeze({ ...fallbackAccount, ...account, capabilities: [...account.capabilities] });
}

export function hasCapability(account, capability) {
  return account.capabilities.includes(capability);
}
