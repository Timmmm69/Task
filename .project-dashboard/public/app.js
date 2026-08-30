const $ = (selector) => document.querySelector(selector);
const fmt = (value) => `${Math.round(value)}%`;
const dateTime = (value, options = {}) => value ? new Intl.DateTimeFormat("ru-RU", { dateStyle: "short", timeStyle: "short", ...options }).format(new Date(value)) : "Нет данных";
const statusLabel = { done: "Готово", in_progress: "В работе", blocked: "Заблокировано", not_started: "Не начато", unverified: "Не проверено" };

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = String(text);
  return node;
}

function replace(selector, children) {
  const target = $(selector);
  target.replaceChildren(...children);
}

function renderCounts(counts) {
  const definitions = [
    ["done", "Готово", "done"], ["in_progress", "В работе", "active"],
    ["blocked", "Заблокировано", "blocked"], ["unknown", "Не начато / не проверено", "unknown"]
  ];
  replace("#counts", definitions.map(([key, label, style]) => {
    const box = el("div", `count ${style}`);
    box.append(el("strong", "", counts[key]), el("span", "", label));
    return box;
  }));
}

function renderCategories(categories) {
  replace("#stage-rail", categories.map((category, index) => {
    const state = category.progress === 100 ? "done" : category.blocked ? "blocked" : category.progress > 0 ? "active" : "";
    const step = el("div", `stage-step ${state}`);
    step.append(el("div", "step-circle", index + 1), el("div", "step-label", category.name));
    step.lastChild.append(el("span", "step-value", fmt(category.progress)));
    return step;
  }));
  replace("#category-cards", categories.map((category) => {
    const card = el("article", "category-card");
    const top = el("div", "category-top");
    top.append(el("strong", "", category.name), el("span", "category-code", category.short));
    const bar = el("div", "mini-bar");
    const fill = el("span"); fill.style.width = fmt(category.progress); bar.append(fill);
    const bottom = el("div", "category-bottom");
    bottom.append(el("span", "", `${category.ready}/${category.total} готово`), el("span", category.blocked ? "bad" : "", fmt(category.progress)));
    card.append(top, bar, bottom);
    return card;
  }));
}

function renderGates(gates) {
  const failed = gates.filter((gate) => !gate.passed);
  $("#blocker-count").textContent = `${failed.length} открыто`;
  replace("#gate-list", gates.map((gate) => {
    const box = el("section", "gate");
    const head = el("div", "gate-head");
    head.append(el("h3", "", gate.title), el("strong", "", gate.passed ? "ЗАКРЫТ" : `${gate.blockers.length} BLOCKERS`));
    const reason = gate.passed ? "Все обязательные результаты подтверждены." : gate.blockers.slice(0, 2).map((item) => item.title).join("; ");
    box.append(head, el("p", "", reason));
    return box;
  }));
}

function taskMeta(entry) {
  const item = entry.item;
  return `${item.category_name} / ${statusLabel[item.status]} / P${item.priority} / ${item.progress}%`;
}

function unlockText(entry) {
  if (entry.unlocks.length) return entry.unlocks.slice(0, 3).map((item) => item.title).join("; ");
  return "Не блокирует другие roadmap items";
}

function renderExecutionPlan(plan) {
  $("#queue-total").textContent = `${plan.total_remaining} задач до релиза`;
  const now = plan.now;
  const nowCard = el("div", "now-task");
  if (now) {
    const top = el("div", "now-top");
    top.append(el("code", "", `№${String(now.order).padStart(2, "0")} / ${now.item.id}`), el("span", "state-tag pass", "ДОСТУПНА"));
    nowCard.append(top, el("h3", "", now.item.title), el("p", "task-meta", taskMeta(now)));
    const why = el("div", "task-explain");
    why.append(el("span", "", "Почему сейчас"), el("p", "", now.item.note), el("span", "", "Следующее действие"), el("p", "", now.item.next_action), el("span", "", "Разблокирует"), el("p", "", unlockText(now)));
    nowCard.append(why);
  } else {
    nowCard.append(el("h3", "", "Очередь завершена"), el("p", "", "Открытых roadmap items нет."));
  }
  replace("#plan-now", [nowCard]);

  replace("#plan-after", plan.after.map((entry) => {
    const row = el("li", "after-task");
    const order = el("span", "after-order", String(entry.order).padStart(2, "0"));
    const content = el("div");
    content.append(el("strong", "", entry.item.title), el("span", "task-meta", taskMeta(entry)), el("p", "", entry.item.note), el("small", "", `Разблокирует: ${unlockText(entry)}`));
    if (entry.unresolved.length) content.append(el("small", "dependency-note", `Сначала: ${entry.unresolved.join(", ")}`));
    row.append(order, content);
    return row;
  }));

  $("#later-count").textContent = `${plan.later.length} задач`;
  replace("#plan-later", plan.later.map((entry) => {
    const row = el("li");
    row.append(el("span", "later-order", String(entry.order).padStart(2, "0")), el("strong", "", entry.item.title), el("span", "", entry.item.category_name), el("span", "", statusLabel[entry.item.status]), el("small", "", entry.unresolved.length ? `После: ${entry.unresolved.join(", ")}` : "Доступна"));
    return row;
  }));
}

function renderTests(tests) {
  $("#test-date").textContent = dateTime(tests.checked_at);
  const definitions = [["passed", "Passed", "pass"], ["failed", "Failed", "fail"], ["skipped", "Skipped", "skip"], ["total", "Total", ""]];
  replace("#test-stats", definitions.map(([key, label, style]) => {
    const box = el("div", `test-stat ${style}`);
    box.append(el("strong", "", tests[key] ?? "Не проверено"), el("span", "", label));
    return box;
  }));
  $("#test-note").textContent = tests.note || "Нет данных";
  const states = [["Build", tests.build], ["Lint", tests.lint], ["Typecheck", tests.typecheck]];
  replace("#build-states", states.map(([label, state]) => el("span", `state-tag ${state === "passed" ? "pass" : "warn"}`, `${label}: ${state === "passed" ? "PASS" : state === "not_applicable" ? "не применимо" : "не проверено"}`)));
}

function renderGit(git) {
  const clean = $("#git-clean");
  clean.className = `state-tag ${git.dirty ? "warn" : "pass"}`;
  clean.textContent = git.dirty ? `${git.changed_files} изменений` : "Чисто";
  const facts = [["Ветка", git.branch], ["Commit", git.commit], ["Сообщение", git.last_message], ["Относительно origin", git.ahead === null ? "Нет данных" : `впереди ${git.ahead}, позади ${git.behind}`], ["Последний commit", dateTime(git.last_commit_at)]];
  const nodes = [];
  for (const [term, value] of facts) nodes.push(el("dt", "", term), el("dd", "", value));
  replace("#git-facts", nodes);
  replace("#changes", git.recent.slice(0, 6).map((change) => {
    const row = el("li");
    row.append(el("code", "", change.hash), el("span", "", change.message), el("time", "", dateTime(change.at, { dateStyle: "short", timeStyle: undefined })));
    return row;
  }));
}

function renderRoadmap(items) {
  replace("#roadmap-table", items.map((item) => {
    const row = el("div", "roadmap-row");
    row.append(el("code", "", item.id), el("strong", "", item.title), el("span", "status", statusLabel[item.status]), el("span", "value", `${item.progress}%`), el("p", "", item.evidence.join(" | ")));
    return row;
  }));
}

function render(data) {
  $("#project-subtitle").textContent = data.project.subtitle;
  $("#current-stage").textContent = data.project.current_stage;
  $("#stage-number").textContent = data.project.current_stage_number;
  $("#overall").textContent = fmt(data.overall);
  $("#progress-label").textContent = fmt(data.overall);
  $("#progress-bar").style.width = fmt(data.overall);
  $("#remaining").textContent = `Осталось ${fmt(data.remaining_weighted_work)} взвешенной работы`;
  const status = $("#handoff-status");
  status.textContent = data.handoff_ready ? "ГОТОВ К ПЕРЕДАЧЕ" : "НЕ ГОТОВ К ПЕРЕДАЧЕ";
  status.className = `handoff-status ${data.handoff_ready ? "ready" : ""}`;
  renderCounts(data.counts);
  renderCategories(data.categories);
  renderGates(data.gates);
  renderExecutionPlan(data.execution_plan);
  renderTests(data.tests);
  renderGit(data.git);
  renderRoadmap(data.items);
  $("#last-refresh").textContent = dateTime(data.generated_at);
  $("#generated-at").textContent = `Обновлено ${dateTime(data.generated_at)}`;
}

async function refresh() {
  try {
    const response = await fetch(`/api/dashboard?t=${Date.now()}`, { cache: "no-store" });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    render(await response.json());
    $("#loading").hidden = true;
    $("#error").hidden = true;
    $("#dashboard").hidden = false;
  } catch (error) {
    if ($("#dashboard").hidden) {
      $("#loading").hidden = true;
      $("#error").hidden = false;
      $("#error-message").textContent = `Не удалось получить локальные данные: ${error.message}`;
    }
  }
}

$("#retry").addEventListener("click", refresh);
refresh();
setInterval(refresh, 5000);
