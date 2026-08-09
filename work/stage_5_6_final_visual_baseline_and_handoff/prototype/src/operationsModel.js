const ALL_AUDIT_EVENTS = "\u0412\u0441\u0435 \u0441\u043e\u0431\u044b\u0442\u0438\u044f";

export function getVisibleOperationSections(sections, limitedMode) {
  if (!limitedMode) return sections;
  return sections.filter((item) => ["health", "audit"].includes(item.id));
}

export function isOperationsWritable({ offline, loading, writeBlocked, maintenance }) {
  return !offline && !loading && !writeBlocked && !maintenance;
}

export function transitionOperation(items, id, patch) {
  return items.map((item) => item.id === id ? { ...item, ...patch } : item);
}

export function filterAuthorizedAudit(rows, { query, type }) {
  const normalizedQuery = query.trim().toLowerCase();
  return rows.filter((item) => {
    if (type !== ALL_AUDIT_EVENTS && item.type !== type) return false;
    if (!normalizedQuery) return true;
    if (!item.authorized) return false;
    return `${item.actor} ${item.action} ${item.target}`.toLowerCase().includes(normalizedQuery);
  });
}

export function canEnterMaintenance({
  writable,
  approved,
  activeJobExists,
  confirmation,
}) {
  return writable && approved && !activeJobExists && confirmation === "RESTORE";
}
