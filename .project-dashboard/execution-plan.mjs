const criticalityRank = { blocker: 0, critical: 1, high: 2, normal: 3 };

function itemDone(item) {
  return item?.status === "done" && item.progress === 100;
}

function prerequisites(item) {
  return [...new Set([...(item.dependencies || []), ...(item.blocked_by || [])])];
}

export function computeExecutionPlan(items) {
  const byId = new Map(items.map((item) => [item.id, item]));
  const completed = new Set(items.filter(itemDone).map((item) => item.id));
  const remaining = new Map(items.filter((item) => !itemDone(item)).map((item) => [item.id, item]));
  const unlockCount = new Map(items.map((item) => [item.id, 0]));

  for (const item of items) {
    for (const dependency of prerequisites(item)) {
      unlockCount.set(dependency, (unlockCount.get(dependency) || 0) + 1);
    }
  }

  const compare = (a, b) =>
    (a.priority - b.priority) ||
    ((criticalityRank[a.criticality] ?? 9) - (criticalityRank[b.criticality] ?? 9)) ||
    ((a.status === "unverified" ? 0 : 1) - (b.status === "unverified" ? 0 : 1)) ||
    ((unlockCount.get(b.id) || 0) - (unlockCount.get(a.id) || 0)) ||
    (b.progress - a.progress) ||
    a.id.localeCompare(b.id);

  const queue = [];
  while (remaining.size) {
    const available = [...remaining.values()]
      .filter((item) => prerequisites(item).every((id) => completed.has(id)))
      .sort(compare);
    if (!available.length) {
      for (const item of [...remaining.values()].sort(compare)) {
        queue.push({ item, available_now: false, unresolved: prerequisites(item).filter((id) => !completed.has(id)), graph_blocked: true });
      }
      break;
    }
    const item = available[0];
    const availableNow = prerequisites(item).every((id) => itemDone(byId.get(id)));
    queue.push({ item, available_now: availableNow, unresolved: prerequisites(item).filter((id) => !itemDone(byId.get(id))), graph_blocked: false });
    remaining.delete(item.id);
    completed.add(item.id);
  }

  const enriched = queue.map((entry, index) => {
    const unlocks = items.filter((item) => prerequisites(item).includes(entry.item.id)).map((item) => ({ id: item.id, title: item.title }));
    return { ...entry, order: index + 1, unlocks };
  });
  return {
    now: enriched.find((entry) => entry.available_now) || null,
    after: enriched.slice(1, 6),
    later: enriched.slice(6),
    queue: enriched
  };
}

export function applyRecommendedOrder(items) {
  const plan = computeExecutionPlan(items);
  const order = new Map(plan.queue.map((entry) => [entry.item.id, entry.order]));
  return items.map((item) => ({ ...item, recommended_order: itemDone(item) ? 0 : (order.get(item.id) || null) }));
}
