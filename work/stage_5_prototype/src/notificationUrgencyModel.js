const TIERS = {
  far: { key: "far", label: "Более 24 часов", className: "urgency-far" },
  hours: { key: "hours", label: "6–24 часа", className: "urgency-hours" },
  soon: { key: "soon", label: "1–6 часов", className: "urgency-soon" },
  critical: { key: "critical", label: "Менее 1 часа", className: "urgency-critical" },
  overdue: { key: "overdue", label: "Просрочено", className: "urgency-overdue" },
};

const DEFAULT_URGENCY_THRESHOLDS = {
  criticalMinutes: 60,
  soonMinutes: 360,
  hoursMinutes: 1440,
};

function urgencyForMinutes(minutes, thresholds = DEFAULT_URGENCY_THRESHOLDS) {
  if (minutes < 0) return TIERS.overdue;
  if (minutes < thresholds.criticalMinutes) return TIERS.critical;
  if (minutes < thresholds.soonMinutes) return TIERS.soon;
  if (minutes < thresholds.hoursMinutes) return TIERS.hours;
  return TIERS.far;
}

function urgencyTierForNotification(notification, thresholds = DEFAULT_URGENCY_THRESHOLDS) {
  if (!notification || typeof notification.deadlineMinutesFromNow !== "number") return null;
  return urgencyForMinutes(notification.deadlineMinutesFromNow, thresholds);
}

export { TIERS, DEFAULT_URGENCY_THRESHOLDS, urgencyForMinutes, urgencyTierForNotification };
