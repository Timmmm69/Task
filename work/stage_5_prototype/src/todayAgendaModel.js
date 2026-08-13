const priorityRank = {
  high: 0,
  medium: 1,
  low: 2,
};

function startMinutes(time = "") {
  const match = time.match(/(\d{1,2}):(\d{2})/);
  if (!match) return Number.POSITIVE_INFINITY;
  return Number(match[1]) * 60 + Number(match[2]);
}

export function sortScheduledTasks(tasks) {
  return tasks
    .map((task, order) => ({ task, order }))
    .sort((left, right) => (
      startMinutes(left.task.time) - startMinutes(right.task.time)
      || (priorityRank[left.task.priorityTone] ?? 3) - (priorityRank[right.task.priorityTone] ?? 3)
      || left.order - right.order
    ))
    .map(({ task }) => task);
}

export function sortUntimedTasks(tasks) {
  return tasks
    .map((task, order) => ({ task, order }))
    .sort((left, right) => (
      (priorityRank[left.task.priorityTone] ?? 3) - (priorityRank[right.task.priorityTone] ?? 3)
      || left.order - right.order
    ))
    .map(({ task }) => task);
}

export function deriveAgendaSections(items) {
  const open = items.filter((task) => !task.completed);
  return {
    scheduled: sortScheduledTasks(open.filter((task) => task.time)),
    untimed: sortUntimedTasks(open.filter((task) => !task.time)),
    completed: items.filter((task) => task.completed),
  };
}

export function computeDayProgress(items) {
  const total = items.length;
  const completed = items.filter((task) => task.completed).length;
  return {
    total,
    completed,
    percent: total ? Math.round((completed / total) * 100) : 0,
    isEmpty: total === 0,
    isDone: total > 0 && completed === total,
  };
}
