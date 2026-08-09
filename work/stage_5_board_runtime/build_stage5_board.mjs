import fs from "node:fs/promises";
import path from "node:path";
import sharp from "sharp";
import { FileBlob, SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const outputDir = "C:/Users/novik/Таск/outputs/019fa078-3f10-7ec1-99e2-7c1cba4ee3d4";
const previewDir = "C:/Users/novik/Таск/work/stage_5_board_runtime/previews";
const outputPath = path.join(outputDir, "Stage_5_Task_Board.xlsx");
await fs.mkdir(outputDir, { recursive: true });
await fs.mkdir(previewDir, { recursive: true });

function parseCsv(text) {
  const rows = [];
  let row = [];
  let field = "";
  let quoted = false;
  const source = text.replace(/^\uFEFF/, "");
  for (let i = 0; i < source.length; i++) {
    const ch = source[i];
    if (quoted) {
      if (ch === '"' && source[i + 1] === '"') {
        field += '"';
        i++;
      } else if (ch === '"') {
        quoted = false;
      } else {
        field += ch;
      }
    } else if (ch === '"') {
      quoted = true;
    } else if (ch === ",") {
      row.push(field);
      field = "";
    } else if (ch === "\n") {
      row.push(field.replace(/\r$/, ""));
      rows.push(row);
      row = [];
      field = "";
    } else {
      field += ch;
    }
  }
  if (field.length || row.length) {
    row.push(field.replace(/\r$/, ""));
    rows.push(row);
  }
  const headers = rows.shift() ?? [];
  return rows
    .filter((values) => values.some((value) => value !== ""))
    .map((values) => Object.fromEntries(headers.map((header, index) => [header, values[index] ?? ""])));
}

const scrRows = parseCsv(await fs.readFile(path.resolve("work/stage_5_2/Component_Inventory_0.1.csv"), "utf8"));
const flowRows = parseCsv(await fs.readFile(path.resolve("work/stage_5_2/Flow_Design_Inventory_0.1.csv"), "utf8"));
const stateRows = parseCsv(await fs.readFile(path.resolve("outputs/stage_4_6_lite/Stage_4_6_Lite_STATE_Audit.csv"), "utf8"));
const nfrRows = parseCsv(await fs.readFile(path.resolve("work/stage_4_6_lite/design_input/prd/Stage_4_NFR_Catalog_4.5.csv"), "utf8"));
const componentUsageRows = parseCsv(await fs.readFile(path.resolve("work/stage_5_2/Component_Usage_Map_1.0.csv"), "utf8"));
const implementationSpecRows = parseCsv(await fs.readFile(path.resolve("work/stage_5_2/Component_Implementation_Specs_0.9.csv"), "utf8"));
const verticalSliceRows = parseCsv(await fs.readFile(path.resolve("work/stage_5_2/Vertical_Slice_Scenario_Contracts_0.1.csv"), "utf8"));
const roleMatrixRows = parseCsv(await fs.readFile(path.resolve("work/stage_5_2/Role_Capability_Design_Matrix_0.1.csv"), "utf8"));
const stateComponentRows = parseCsv(await fs.readFile(path.resolve("work/stage_5_2/State_Component_Coverage_Matrix_0.1.csv"), "utf8"));
const usabilityRows = parseCsv(await fs.readFile(path.resolve("work/stage_5_2/Usability_Test_Script_0.1.csv"), "utf8"));
const visScorecardRows = parseCsv(await fs.readFile(path.resolve("work/stage_5_1/Visual_Direction_Decision_Scorecard_0.1.csv"), "utf8"));

const D = (iso) => new Date(`${iso}T12:00:00`);
const tasks = [
  ["S5-0001","5.0","Governance","5.0","P0","Зафиксировать входной Stage 5 Design Input и SHA-256","Готово","Codex","—",D("2026-07-28"),D("2026-07-28"),1,"Organizer_Stage5_Design_Input.validation.md","Stage 4 Design Handoff",D("2026-07-28"),"Сохранять baseline неизменным","SHA подтверждён"],
  ["S5-0002","5.0","Governance","5.0","P0","Утвердить план Stage 5.0–5.6","Готово","Product owner","S5-0001",D("2026-07-27"),D("2026-07-28"),1,"Stage_5_Visual_Design_Plan_1.0.zip","Stage 5 Plan 1.0",D("2026-07-28"),"Следовать gates и DoD","Запуск Stage 5 разрешён пользователем"],
  ["S5-0003","5.0","Governance","5.0","P0","Создать динамическую доску Stage 5","Готово","Codex","S5-0002",D("2026-07-28"),D("2026-07-28"),1,"Stage_5_Task_Board.xlsx","Stage 5 Plan §4",D("2026-07-28"),"Обновлять после каждого значимого шага","Канонический рабочий трекер"],
  ["S5-0004","5.0","Traceability","5.0","P0","Сформировать инвентаризацию 128 уникальных SCR и 37 FLOW","Готово","Codex","S5-0001",D("2026-07-28"),D("2026-07-30"),1,"Component + Flow Design Inventories 0.1","Stage 3.5 Screen Catalog / User Flows",D("2026-07-28"),"Использовать в traceability matrix","128/128 SCR и 37/37 FLOW; 132 rows включали delta-повторы"],
  ["S5-0005","5.0","Traceability","5.0","P0","Создать матрицу SCR/FLOW/STATE → design evidence","В работе","Codex","S5-0004",D("2026-07-29"),D("2026-07-31"),0.25,"Component + Flow inventories","Stage 3.5 + PRD 4.5",D("2026-07-28"),"Добавить STATE/NFR/evidence status и QA checks","SCR/FLOW основа готова"],
  ["S5-0006","5.0","Decisions","5.0","P1","Зафиксировать решения OQ-004/005/006/009, влияющие на дизайн","Ожидает решения","Product owner","—",D("2026-07-28"),D("2026-08-03"),0,"Лист Decisions","Stage 4 Open Questions 4.5",D("2026-07-28"),"Принять решения до массовой отрисовки","Не блокирует три направления 5.1"],

  ["S5-0101","5.1","Direction","5.1","P0","Зафиксировать minimum design brief для representative Today/Tasks surface","Готово","Codex","S5-0001",D("2026-07-28"),D("2026-07-28"),1,"Brief в текущем design run","PRD 4.5 + Stage 3.5",D("2026-07-28"),"Использовать RU desktop 1440×1024","Основной outcome: обзор дня и управление задачами"],
  ["S5-0102","5.1","Direction","5.1","P0","Проверить доступные визуальные референсы и saved context","Готово","Codex","S5-0101",D("2026-07-28"),D("2026-07-28"),1,"Product Design preflight","Workspace design context",D("2026-07-28"),"Работать от нормативного brief","Saved visual references отсутствуют"],
  ["S5-0103","5.1","Direction","5.1","P0","Сгенерировать три независимых визуальных направления","На проверке","Codex","S5-0102",D("2026-07-28"),D("2026-07-28"),1,"work/stage_5_1/directions — 3 images","Stage 5 Plan §5.1",D("2026-07-28"),"Получить выбор направления 1, 2 или 3","Все три направления показаны пользователю"],
  ["S5-0104","5.1","Decision","5.1","P0","Выбрать одно визуальное направление","Ожидает решения","Product owner","S5-0103",D("2026-07-28"),D("2026-07-29"),0,"Selected direction ID","Stage 5 Plan Gate 5.1",D("2026-07-28"),"Выбрать 1, 2 или 3","Блокирует визуальное развитие 5.2"],
  ["S5-0105","5.1","Foundation","5.1","P0","Определить типографику, цветовые роли, spacing и density","Блокировано","Codex","S5-0104",D("2026-07-29"),D("2026-08-03"),0,"Foundations & Tokens 0.1","Selected visual direction",D("2026-07-28"),"Начать после выбора направления",""],
  ["S5-0106","5.1","Foundation","5.1","P0","Определить interaction states и non-color semantics","Блокировано","Codex","S5-0104",D("2026-07-29"),D("2026-08-03"),0,"Interaction State Spec 0.1","NFR-002/003/005",D("2026-07-28"),"Связать с визуальным направлением",""],
  ["S5-0107","5.1","Accessibility","5.1","P0","Собрать accessibility baseline для focus/contrast/scaling","Готово","Codex","S5-0101",D("2026-07-28"),D("2026-08-03"),1,"work/stage_5_1/Accessibility_Baseline_0.1.md","NFR-002/003/004/005",D("2026-07-28"),"Применить baseline к выбранному направлению","Визуальные tokens проверяются после VIS-001"],
  ["S5-0108","5.1","Review","5.1","P0","Провести product + desktop tech review направления","Блокировано","Product owner + Tech lead","S5-0105,S5-0106",D("2026-08-03"),D("2026-08-04"),0,"Review evidence","Gate 5.1",D("2026-07-28"),"Проверить реализуемость WPF/Windows",""],
  ["S5-0109","5.1","Gate","5.1","P0","Закрыть Gate 5.1","Блокировано","Product owner","S5-0108",D("2026-08-04"),D("2026-08-04"),0,"Gate 5.1 approval","Stage 5 Plan",D("2026-07-28"),"Critical/High = 0",""],

  ["S5-0201","5.2","System","5.2","P0","Сформировать component inventory из SCR/FLOW","Готово","Codex","S5-0004",D("2026-07-28"),D("2026-07-31"),1,"Component Inventory 0.1 + 45 families","Stage 3.5 catalogs",D("2026-07-28"),"Использовать как backlog библиотеки","128/128 SCR mapped; FLOW grouping остаётся в S5-0004"],
  ["S5-0202","5.2","System","5.2","P0","Спроектировать архитектуру библиотеки и naming","Готово","Codex","S5-0201",D("2026-07-30"),D("2026-08-03"),1,"Component_Library_Architecture_0.1.md","Component Inventory",D("2026-07-28"),"Применить architecture после VIS-001","9 tiers, naming, tokens, variants и composition rules готовы"],
  ["S5-0203","5.2","System","5.2","P0","Реализовать foundations/tokens выбранного направления","Блокировано","Codex","S5-0104,S5-0202",D("2026-08-03"),D("2026-08-05"),0,"Design Tokens 1.0","Selected direction",D("2026-07-28"),"Начать после выбора направления",""],
  ["S5-0204","5.2","Components","5.2","P0","App shell, navigation, command bar и page header","Блокировано","Codex","S5-0203",D("2026-08-04"),D("2026-08-07"),0,"Core Shell Components","SCR Shell",D("2026-07-28"),"",""],
  ["S5-0205","5.2","Components","5.2","P0","Buttons, inputs, selectors, picker и search components","Блокировано","Codex","S5-0203",D("2026-08-04"),D("2026-08-08"),0,"Input Components","SCR/FLOW input surfaces",D("2026-07-28"),"",""],
  ["S5-0206","5.2","Components","5.2","P0","List/table/tree/cards/badges/pagination patterns","Блокировано","Codex","S5-0203",D("2026-08-05"),D("2026-08-10"),0,"Data Display Components","NFR-006",D("2026-07-28"),"Учесть virtualization и large lists",""],
  ["S5-0207","5.2","Components","5.2","P0","Dialog/panel/toast/inline message/Notification Center","Блокировано","Codex","S5-0203",D("2026-08-05"),D("2026-08-10"),0,"Overlay Components","SCR notifications/shared",D("2026-07-28"),"",""],
  ["S5-0208","5.2","States","5.2","P0","Loading/empty/error/read-only/offline/conflict/recovery components","Блокировано","Codex","S5-0203,S5-0106",D("2026-08-06"),D("2026-08-11"),0,"Shared State Components","Stage 3.5 State Matrix",D("2026-07-28"),"",""],
  ["S5-0209","5.2","Slice","5.2","P0","Макеты Auth/first connection и Shell/Today","Блокировано","Codex","S5-0204,S5-0205",D("2026-08-07"),D("2026-08-12"),0,"Vertical Slice frames","FLOW-001/002",D("2026-07-28"),"",""],
  ["S5-0210","5.2","Slice","5.2","P0","Макеты Inbox capture и Create/Edit Task","Блокировано","Codex","S5-0205,S5-0206",D("2026-08-08"),D("2026-08-13"),0,"Vertical Slice frames","FLOW-004/005/034",D("2026-07-28"),"",""],
  ["S5-0211","5.2","Slice","5.2","P0","Макеты Global Search с employee redaction","Блокировано","Codex","S5-0205,S5-0206",D("2026-08-09"),D("2026-08-13"),0,"Search frames","FLOW-019",D("2026-07-28"),"",""],
  ["S5-0212","5.2","Slice","5.2","P0","Макеты server loss → read-only → recovery и conflict","Блокировано","Codex","S5-0208",D("2026-08-10"),D("2026-08-14"),0,"Resilience frames","FLOW-022–025",D("2026-07-28"),"",""],
  ["S5-0213","5.2","Prototype","5.2","P0","Собрать интерактивный prototype vertical slice","Блокировано","Codex","S5-0209,S5-0210,S5-0211,S5-0212",D("2026-08-12"),D("2026-08-17"),0,"Interactive Prototype 0.1","Stage 5 Plan §5.2",D("2026-07-28"),"",""],
  ["S5-0214","5.2","Accessibility","5.2","P0","Проверить keyboard-only, focus и accessible names/states","Блокировано","QA + Codex","S5-0213,S5-0107",D("2026-08-17"),D("2026-08-18"),0,"Accessibility evidence","NFR-002/003/005",D("2026-07-28"),"",""],
  ["S5-0215","5.2","Handoff","5.2","P0","Подготовить component-to-SCR usage map и specs","Блокировано","Codex","S5-0213",D("2026-08-16"),D("2026-08-19"),0,"Usage Map + Specs","Traceability Matrix",D("2026-07-28"),"",""],
  ["S5-0216","5.2","Gate","5.2","P0","Закрыть Gate 5.2","Блокировано","Product owner + Tech lead + QA","S5-0214,S5-0215",D("2026-08-19"),D("2026-08-19"),0,"Gate 5.2 approval","Stage 5 Plan",D("2026-07-28"),"Critical/High = 0",""],

  ["S5-0301","5.3","Wave A","5.3","P1","Today, Inbox, Tasks, Subtasks/Checklists","Бэклог","Design","S5-0216",D("2026-08-20"),D("2026-08-28"),0,"Wave A frames","Stage 5 Plan §5.3",D("2026-07-28"),"",""],
  ["S5-0302","5.3","Wave A","5.3","P1","Recurrence, reminders, notifications, Calendar","Бэклог","Design","S5-0216",D("2026-08-24"),D("2026-09-04"),0,"Wave A frames","Stage 5 Plan §5.3",D("2026-07-28"),"",""],
  ["S5-0303","5.3","Wave B","5.3","P1","Projects, members и lifecycle","Бэклог","Design","S5-0216",D("2026-08-31"),D("2026-09-08"),0,"Wave B frames","Stage 5 Plan §5.3",D("2026-07-28"),"",""],
  ["S5-0304","5.3","Wave B","5.3","P1","Files/FileLocations и SMB diagnostics","Бэклог","Design","S5-0216",D("2026-09-02"),D("2026-09-10"),0,"Wave B frames","Stage 5 Plan §5.3",D("2026-07-28"),"",""],
  ["S5-0305","5.3","Wave B","5.3","P1","CRM, comments, watchers и history","Бэклог","Design","S5-0216",D("2026-09-04"),D("2026-09-14"),0,"Wave B frames","Stage 5 Plan §5.3",D("2026-07-28"),"",""],
  ["S5-0306","5.3","Wave C","5.3","P2","Search, Archive и Trash","Бэклог","Design","S5-0216",D("2026-09-08"),D("2026-09-16"),0,"Wave C frames","Stage 5 Plan §5.3",D("2026-07-28"),"",""],
  ["S5-0307","5.3","Wave C","5.3","P2","Settings и Admin surfaces","Бэклог","Design","S5-0216",D("2026-09-10"),D("2026-09-22"),0,"Wave C frames","Stage 5 Plan §5.3",D("2026-07-28"),"",""],
  ["S5-0308","5.3","Wave C","5.3","P2","Health, jobs, backups и audit surfaces","Бэклог","Design","S5-0216",D("2026-09-14"),D("2026-09-24"),0,"Wave C frames","Stage 5 Plan §5.3",D("2026-07-28"),"",""],
  ["S5-0309","5.3","Traceability","5.3","P0","Подтвердить 100% SCR и 37/37 FLOW coverage","Бэклог","Codex","S5-0301:S5-0308",D("2026-09-22"),D("2026-09-25"),0,"Coverage report","Gate 5.3",D("2026-07-28"),"",""],
  ["S5-0310","5.3","Gate","5.3","P0","Закрыть Gate 5.3","Бэклог","Product owner","S5-0309",D("2026-09-25"),D("2026-09-25"),0,"Gate 5.3 approval","Stage 5 Plan",D("2026-07-28"),"",""],

  ["S5-0401","5.4","Roles","5.4","P0","Role/Capability Visual Matrix","Бэклог","Codex + QA","S5-0216",D("2026-09-14"),D("2026-09-21"),0,"Role Matrix","Stage 3.5 Role Interface Matrix",D("2026-07-28"),"",""],
  ["S5-0402","5.4","States","5.4","P0","State Coverage Matrix","Бэклог","Codex + QA","S5-0208",D("2026-09-14"),D("2026-09-21"),0,"State Matrix","Stage 3.5 State Matrix",D("2026-07-28"),"",""],
  ["S5-0403","5.4","Accessibility","5.4","P0","Полный keyboard/screen-reader/non-color review","Бэклог","QA","S5-0310",D("2026-09-21"),D("2026-09-28"),0,"Accessibility Report","NFR-002/003/005",D("2026-07-28"),"",""],
  ["S5-0404","5.4","High DPI","5.4","P0","Проверка Windows scaling 100–200% и длинных RU строк","Бэклог","QA + Tech lead","S5-0310",D("2026-09-21"),D("2026-09-28"),0,"High-DPI Report","NFR-004",D("2026-07-28"),"",""],
  ["S5-0405","5.4","Remediation","5.4","P0","Исправить findings и повторить проверки","Бэклог","Design","S5-0401:S5-0404",D("2026-09-28"),D("2026-10-02"),0,"Retest evidence","Gate 5.4",D("2026-07-28"),"",""],
  ["S5-0406","5.4","Gate","5.4","P0","Закрыть Gate 5.4","Бэклог","Product owner + QA","S5-0405",D("2026-10-02"),D("2026-10-02"),0,"Gate 5.4 approval","Stage 5 Plan",D("2026-07-28"),"Critical/High = 0",""],

  ["S5-0501","5.5","Research","5.5","P0","Подготовить usability script и realistic fixtures","Бэклог","UX + QA","S5-0310",D("2026-09-28"),D("2026-10-02"),0,"Test Script","Stage 5 Plan §5.5",D("2026-07-28"),"",""],
  ["S5-0502","5.5","Research","5.5","P0","Провести sessions по ролям и admin review","Бэклог","UX","S5-0501,S5-0406",D("2026-10-05"),D("2026-10-09"),0,"Session evidence","Stage 5 Plan §5.5",D("2026-07-28"),"",""],
  ["S5-0503","5.5","Remediation","5.5","P0","Исправить Critical/High usability findings","Бэклог","Design","S5-0502",D("2026-10-09"),D("2026-10-14"),0,"Updated prototype","Usability findings",D("2026-07-28"),"",""],
  ["S5-0504","5.5","Gate","5.5","P0","Закрыть Gate 5.5","Бэклог","Product owner","S5-0503",D("2026-10-14"),D("2026-10-14"),0,"Gate 5.5 approval","Stage 5 Plan",D("2026-07-28"),"Critical/High = 0",""],

  ["S5-0601","5.6","Baseline","5.6","P0","Заморозить Figma baseline и Component Library 1.0","Бэклог","Design owner","S5-0406,S5-0504",D("2026-10-15"),D("2026-10-16"),0,"Final Visual Baseline","Stage 5 Plan §5.6",D("2026-07-28"),"",""],
  ["S5-0602","5.6","Validation","5.6","P0","Финальная SCR/FLOW/STATE/role traceability validation","Бэклог","Codex + QA","S5-0601",D("2026-10-16"),D("2026-10-19"),0,"Final validation","Stage 5 DoD",D("2026-07-28"),"",""],
  ["S5-0603","5.6","Handoff","5.6","P0","Подготовить specs, assets, decisions и Development Handoff","Бэклог","Design + Tech lead","S5-0601",D("2026-10-16"),D("2026-10-20"),0,"Development Handoff","Stage 5 Plan §5.6",D("2026-07-28"),"",""],
  ["S5-0604","5.6","Packaging","5.6","P0","Собрать manifest, version, SHA-256 и validation report","Бэклог","Codex","S5-0602,S5-0603",D("2026-10-20"),D("2026-10-21"),0,"Final package","AGENTS.md",D("2026-07-28"),"",""],
  ["S5-0605","5.6","Gate","5.6","P0","Подписать Gate 5.6 и допуск к разработке","Бэклог","Product owner + Design + Tech lead + QA","S5-0604",D("2026-10-21"),D("2026-10-21"),0,"Stage 5 Approval","Stage 5 DoD",D("2026-07-28"),"Stage 5 завершён только после фактической проверки",""],
];

const traceabilityTask = tasks.find((task) => task[0] === "S5-0005");
traceabilityTask[6] = "Готово";
traceabilityTask[11] = 1;
traceabilityTask[12] = "SCR/FLOW/STATE/NFR Coverage sheets";
traceabilityTask[14] = D("2026-07-28");
traceabilityTask[15] = "Поддерживать статусы design evidence по мере выполнения 5.2–5.4";
traceabilityTask[16] = "128 SCR, 37 FLOW, 30 STATE и 25 NFR связаны с требуемым design evidence";

for (const taskId of ["S5-0209","S5-0210","S5-0211","S5-0212"]) {
  const task = tasks.find((item) => item[0] === taskId);
  task[11] = 0.2;
  task[12] = "Vertical Slice Scenario Contracts 0.1";
  task[14] = D("2026-07-28");
  task[15] = "Построить visual frames после выбора VIS-001";
  task[16] = "Scenario, state, keyboard и accessibility contracts готовы; visual construction остаётся блокированной";
}
const usageMapTask = tasks.find((task) => task[0] === "S5-0215");
usageMapTask[6] = "Готово";
usageMapTask[11] = 1;
usageMapTask[12] = "Component Usage Map 1.0 + Implementation Specs 0.9 + validation";
usageMapTask[14] = D("2026-07-28");
usageMapTask[15] = "Поддерживать freeze при изменениях component contracts";
usageMapTask[16] = "45/45 families, 128/128 SCR и 37/37 FLOW; behavior/failure/accessibility contracts и explicit gaps зафиксированы";

const roleMatrixTask = tasks.find((task) => task[0] === "S5-0401");
roleMatrixTask[6] = "В работе";
roleMatrixTask[11] = 0.45;
roleMatrixTask[12] = "Role Capability Design Matrix 0.1";
roleMatrixTask[14] = D("2026-07-28");
roleMatrixTask[15] = "Добавить role comparison frames и prototype evidence после VIS-001/Gate 5.2";
roleMatrixTask[16] = "38 capability-first role/action contracts готовы; visual/UIA evidence pending";

const stateMatrixTask = tasks.find((task) => task[0] === "S5-0402");
stateMatrixTask[6] = "В работе";
stateMatrixTask[11] = 0.45;
stateMatrixTask[12] = "State Component Coverage Matrix 0.1";
stateMatrixTask[14] = D("2026-07-28");
stateMatrixTask[15] = "Добавить visual variants и state walkthrough evidence после VIS-001/Gate 5.2";
stateMatrixTask[16] = "56 published state rows связаны с shared components и evidence contract";

const usabilityTask = tasks.find((task) => task[0] === "S5-0501");
usabilityTask[6] = "В работе";
usabilityTask[11] = 0.75;
usabilityTask[12] = "Usability Test Script 0.1";
usabilityTask[14] = D("2026-07-28");
usabilityTask[15] = "Провести dry run и обновить fixtures после появления interactive prototype";
usabilityTask[16] = "10 сценариев и 4 роли покрыты; execution pending prototype";

const directionDecisionTask = tasks.find((task) => task[0] === "S5-0104");
directionDecisionTask[6] = "Готово";
directionDecisionTask[11] = 1;
directionDecisionTask[12] = "Direction 2 selected + Visual Direction Decision Scorecard 0.1";
directionDecisionTask[14] = D("2026-07-28");
directionDecisionTask[15] = "Реализовать foundations, components и vertical slice по Direction 2";
directionDecisionTask[16] = "Product owner выбрал Direction 2 — Timeline planner; trade-off по cross-surface complexity принят";

const directionGenerationTask = tasks.find((task) => task[0] === "S5-0103");
directionGenerationTask[6] = "Готово";
directionGenerationTask[15] = "Direction 2 выбрано; использовать как visual truth";
directionGenerationTask[16] = "Три направления показаны и рассмотрены; выбор завершён";

for (const [taskId, progress, evidence, nextAction, note] of [
  ["S5-0105",1,"Foundations_Tokens_Direction_2_0.1.md + browser-verified prototype","Использовать tokens в расширении component library","Typography, color roles, spacing, density и layout contract определены"],
  ["S5-0106",1,"Interaction_State_Spec_Direction_2_0.1.md + browser interaction evidence","Проверить full state set на Gate 5.2","Focus, keyboard и non-color semantics определены"],
  ["S5-0203",0.75,"Direction 2 foundations/tokens + prototype CSS across Today, Tasks, Projects, Notifications and component-completion wave","Довести Design Tokens до версии 1.0 после tech review","Tokens применены ко всем representative component families и прошли browser Design QA"],
]) {
  const task = tasks.find((item) => item[0] === taskId);
  task[6] = progress === 1 ? "Готово" : "В работе";
  task[11] = progress;
  task[12] = evidence;
  task[14] = D("2026-07-28");
  task[15] = nextAction;
  task[16] = note;
}

for (const [taskId, progress, evidence, nextAction, note] of [
  ["S5-0501",1.00,"Stage 5.5 usability increment 0.1.0: canonical 10-scenario script, fixtures and current-run evidence package","Maintain the script through the Stage 5.6 freeze","10/10 scenarios and all four roles are covered by the accepted test contract"],
  ["S5-0502",0.75,"10/10 expert-proxy scenarios executed in the in-app browser; 13/13 screenshots visually inspected","Conduct moderated employee/admin participant sessions","Expert proxy is complete; participant metrics and quotes are not claimed"],
  ["S5-0503",1.00,"UX-055-001 High conflict-draft defect and UX-055-002 Medium conversion-callout defect fixed and retested; build and 15/15 tests pass","Carry both regression checks into final Stage 5.6 validation","Final open Critical/High/Medium findings in inspected prototype scope = 0"],
  ["S5-0504",0.00,"Stage 5.5 package, validation report and Gate boundary ready","Obtain Product owner acceptance after external moderated sessions","External Gate decision remains open and is not counted as completed delivery evidence"],
]) {
  const task = tasks.find((item) => item[0] === taskId);
  task[6] = progress >= 1 ? "Готово" : progress > 0 ? "В работе" : "Бэклог";
  task[11] = progress;
  task[12] = evidence;
  task[14] = D("2026-08-02");
  task[15] = nextAction;
  task[16] = note;
}

for (const [taskId, progress, evidence, nextAction, note] of [
  ["S5-0601",1.00,"Stage 5 Final Visual Baseline 1.0 + Design System 1.0 + Interactive Prototype 1.0, code-based and editable","Preserve the frozen source and require a new version for design changes","No external Figma file is claimed; accepted Direction 2 is frozen in source and dist"],
  ["S5-0602",1.00,"Final audit: 128/128 SCR, 37/37 FLOW, 38/38 roles, 56/56 states, 45/45 components and 10/10 expert scenarios validated","Collect external native Windows and participant evidence without changing delivery counts","Five prerequisite package manifests hash-verified; open design Critical/High/Medium = 0"],
  ["S5-0603",1.00,"Development Handoff 1.0 with specs, assets/licenses, decisions, findings, implementation order and contract rules","Run joint Product/Design/Desktop/QA review","Handoff package is reproducible and does not ask developers to invent business logic"],
  ["S5-0604",1.00,"Final Stage 5.6 package 1.0.1: 83 work files / 83 output files, mirror mismatches 0; Gate execution kit included; build and 15/15 tests pass","Create a new package version only after accepted external Gate evidence is added","Manifest, VERSION, SHA-256 and factual validation report are complete"],
  ["S5-0605",0.00,"Gate kit 0.1.0 + completion audit 0.1.0: validator NOT_READY 0/9; audit ACTIVE_NOT_COMPLETE (6 achieved, 2 partial external, 1 not achieved)","Execute the kit; add 9/9 hash-addressed native Windows, moderated-session and named approval evidence","Templates are not evidence; Gate and Goal remain active until a repeated completion audit proves READY"],
]) {
  const task = tasks.find((item) => item[0] === taskId);
  task[6] = progress >= 1 ? "Готово" : progress > 0 ? "В работе" : "Бэклог";
  task[11] = progress;
  task[12] = evidence;
  task[14] = D("2026-08-02");
  task[15] = nextAction;
  task[16] = note;
}

for (const [taskId, progress, evidence, nextAction, note] of [
  ["S5-0108",0.35,"design-qa.md + design-qa-stage5-surfaces.md; production build succeeds","Провести product + WPF/Windows tech review","Browser/visual QA passed; formal product/tech sign-off remains"],
  ["S5-0204",0.86,"Prototype shell with Today, Inbox, Tasks and Projects navigation; Search, Notification and connection/profile commands","Добавить remaining settings/admin shell variants","Direction 2 shell and responsive header browser-verified at 1280×720"],
  ["S5-0205",0.94,"New Task, Auth, Inbox conversion, Edit/Search, People/Project pickers, DateTimePicker, ReminderEditor and RecurrenceEditor","Провести formal Windows control and timezone review","Representative input/picker families interactive and browser-verified"],
  ["S5-0206",0.94,"Timeline/cards, Inbox list/inspector, Search results, Tasks table/filter/pagination, SelectionBar, bulk summary and Projects tree/history","Добавить virtualization evidence for large datasets","Core list/table/tree/card/pagination and bulk patterns browser-verified"],
  ["S5-0207",0.94,"Dialogs, drawer, toast, inline messages, Search overlay, diagnostics, Notification Center and permission-aware ContextMenu","Добавить Windows-toast visual handoff variant","Overlay and action-surface families are interactive; formal OS handoff remains"],
  ["S5-0208",0.98,"Loading, empty, validation, TLS, locked, redaction, read-only, maintenance, storage-full, conflict, revoked session, recovery, lifecycle and file-location failure","Провести formal shared-state review","Representative shared-state family browser-verified across all 45 component families"],
  ["S5-0209",0.99,"Auth/first connection including TLS/incompatible/unavailable, locked/blocked, cursor/scope, download/signature failure, repeated failure and SESSION_REVOKED","Провести formal sign-off","FLOW-001/002 contracted primary and edge paths browser-verified"],
  ["S5-0210",0.95,"New Task + Inbox capture/conversion + full edit/conflict + assignee/project/date-time/reminder/recurrence editors","Провести formal sign-off и Windows control review","FLOW-004/005/034 primary paths and scheduling variants browser-verified"],
  ["S5-0211",0.93,"Global Search, filters, keyboard selection and employee redaction; Tasks pagination pattern","Добавить large-result performance evidence before sign-off","FLOW-019 permission-safe search browser-verified"],
  ["S5-0212",0.99,"Server loss → read-only → reconnect/interruption → scope validation → recovery + maintenance/storage/conflict/revoked/repeated failure","Провести formal resilience review","FLOW-022–025 primary and boundary walkthrough browser-verified"],
  ["S5-0213",0.99,"work/stage_5_prototype — all 45 component families have representative Direction 2 behavior and visual evidence","Freeze prototype candidate after formal accessibility and stakeholder review","Production build, scenario, semantic/browser and visual QA pass"],
  ["S5-0214",0.74,"Keyboard flows, focus trap wrap/return, accessible names/states, menu/table/tree/pagination/history semantics, 150% DPI, adaptive snapshot and reduced motion","Провести UIA/Narrator/actual 200% scaling and contrast-tool review","Browser semantic/responsive/focus evidence passed; OS assistive-tech evidence pending"],
]) {
  const task = tasks.find((item) => item[0] === taskId);
  task[6] = "В работе";
  task[11] = progress;
  task[12] = evidence;
  task[14] = D("2026-07-28");
  task[15] = nextAction;
  task[16] = note;
}

for (const [taskId, progress, evidence, nextAction, note] of [
  ["S5-0301",0.90,"Wave A prototype + VALIDATION_REPORT 0.1.1: Today/Inbox/Tasks и one-level checklist browser-verified","Завершить annotated-frame review Wave A","Работающая реализация принята; формальное утверждение экранов остаётся"],
  ["S5-0302",1.00,"CalendarEvent editor package 0.1.0: canonical create/edit fields, attendees, RSVP, validation/version/permission/session states, overlap, offline, 15/15 tests and browser QA verified","Провести formal annotated-frame, role and native Windows review","SCR-044/FLOW-031 prototype implementation доказана; formal Gate approval остаётся отдельно"],
  ["S5-0303",0.88,"Wave B implementation validation 0.1.0: Projects/members/lifecycle interactive states, build/tests и browser QA verified","Провести formal annotated-frame и role/runtime review","Prototype implementation доказана; formal approval и native Windows evidence остаются"],
  ["S5-0304",0.85,"Wave B implementation validation 0.1.0: Files/FileLocations, six safe diagnostics, UNSAFE_PATH, relink/add-alternative и metadata/SMB boundary verified","Провести Windows/SMB runtime и native picker verification","Prototype design states доказаны; реальная SMB/OS availability не заявляется"],
  ["S5-0305",0.84,"Wave B implementation validation 0.1.0: CRM, comments, history и task-card watchers с Task.Watch/read-only states verified","Провести formal frame/accessibility review","Manual CRM interaction не отправляет внешние сообщения; Gate approval остаётся"],
  ["S5-0306",1.00,"Wave C Search + Archive/Trash validation 0.1.0 packages: full Search, permission-safe partial, cross-object lifecycle, restore conflicts, legal hold, typed purge, offline, build/tests и browser QA verified","Перейти к Settings/Admin и сохранить lifecycle evidence в traceability","Search, Archive и Trash доказаны в prototype scope; formal approval остаётся частью Gate"],
  ["S5-0307",1.00,"Wave C Settings + Admin validation 0.1.0 packages: scoped preferences/security/cache/connection/sessions and capability-filtered users/departments/roles/sessions/resources, guards/conflicts/offline, build/tests и browser QA verified","Перейти к Operations и сохранить Settings/Admin evidence в traceability","Settings и Admin доказаны в prototype scope; formal approval остаётся частью Gate"],
  ["S5-0308",1.00,"Wave C Operations package 0.2.0: Health/jobs/backups/audit/organization, limited role, offline and recovery states browser-verified; 15/15 tests pass; 7/7 screenshots inspected","Перейти к Stage 5.4 role/state/accessibility/high-DPI audit","Operations design acceptance закрыта в prototype scope; native Windows/runtime evidence остаётся отдельной Gate-проверкой"],
  ["S5-0309",1.00,"Consolidated traceability package 0.1.2 maps 128/128 SCR and 37/37 FLOW; 82 SCR are VERIFIED_PACKAGE and all accepted evidence hashes pass","Провести formal evidence approval и Stage 5.4 audit","Mapping and package evidence complete; formal stakeholder/native-runtime approval remains separate"],
]) {
  const task = tasks.find((item) => item[0] === taskId);
  task[6] = progress >= 1 ? "Готово" : "В работе";
  task[11] = progress;
  task[12] = evidence;
  task[14] = D("2026-07-31");
  task[15] = nextAction;
  task[16] = note;
}

for (const [taskId, progress, evidence, nextAction, note] of [
  ["S5-0401",1.00,"Stage 5.4 design audit 0.1.0: 38/38 role/capability contracts mapped to packaged prototype evidence","Maintain mapping through final Stage 5.6 freeze","Prototype design matrix complete; native UIA/Narrator remains separate Gate evidence"],
  ["S5-0402",1.00,"Stage 5.4 design audit 0.1.0: 56/56 state contracts mapped to packaged prototype evidence","Maintain mapping through final Stage 5.6 freeze","State/component design matrix complete; native runtime evidence remains separate"],
  ["S5-0403",0.85,"Semantic source audit, visible focus, reduced motion, non-colour states and Windows forced-colors support verified","Run Narrator and Inspect/UIA against compiled Windows client","Prototype/static accessibility scope passes; native assistive-technology timing is pending"],
  ["S5-0404",0.70,"Seven responsive breakpoints, long Russian fixtures and static scaling evidence packaged","Run controlled Windows 100/125/150/175/200% multi-monitor checks","Responsive design evidence exists; actual OS DPI behavior is not claimed"],
  ["S5-0405",1.00,"Stage 5.4 remediation/retest: Operations scroll and disabled-danger contrast fixes retained; forced-colors support added; build and 15/15 tests pass","Proceed to Stage 5.5 usability walkthrough","Prototype audit Critical/High = 0"],
  ["S5-0406",0.00,"Gate checklist and evidence package ready","Obtain Product owner, QA and Windows/desktop technical approval","External Gate decision remains open and is not counted as delivery work"],
]) {
  const task = tasks.find((item) => item[0] === taskId);
  task[6] = progress >= 1 ? "Готово" : progress > 0 ? "В работе" : "Бэклог";
  task[11] = progress;
  task[12] = evidence;
  task[14] = D("2026-08-01");
  task[15] = nextAction;
  task[16] = note;
}

const decisions = [
  ["VIS-001","5.1","Выбор визуального направления","Direction 2 — Timeline planner","Product owner","Принято",D("2026-07-28"),"Visual foundation и vertical slice строятся по Direction 2; повышенная cross-surface complexity принята","S5-0104"],
  ["OQ-004","5.0","Аватар в MVP","Подтвердить исключение или инициировать contract change","Product owner","Открыто",D("2026-08-03"),"Влияет на Profile и employee search","S5-0006"],
  ["OQ-005","5.0","Fallback для Windows toast","Подтвердить Notification Center + diagnostics","Product owner + QA","Открыто",D("2026-08-03"),"Влияет на notification surfaces","S5-0006"],
  ["OQ-006","5.0","SMB diagnostics","Подтвердить разделение metadata permission и OS/SMB access","IT owner + QA","Открыто",D("2026-08-03"),"Влияет на Files states","S5-0006"],
  ["OQ-009","5.0","Locales первой поставки","Подтвердить RU-only или multi-locale scope","Product owner","Открыто",D("2026-08-03"),"Влияет на text expansion","S5-0006"],
];

const risks = [
  ["R-001","Высокий","До выбора visual direction начата детальная библиотека","Переделка компонентов","VIS-001 закрыт; styling ведётся только по Direction 2","Codex","Закрыт","S5-0104"],
  ["R-002","Высокий","128 нормативных поверхностей трактуются как 128 уникальных макетов","Перерасход и расхождение паттернов","45 component families и reuse map","Codex","Контролируется","S5-0004"],
  ["R-003","Высокий","Accessibility может быть отложена после foundation","Переделка foundation","Baseline завершён в 5.1; evidence gate в 5.2","QA","Контролируется","S5-0107"],
  ["R-004","Средний","Нет сохранённых визуальных референсов","Нужно выбрать новое направление","Три независимых grounded directions","Product owner","Контролируется","S5-0103"],
  ["R-005","Высокий","Figma не реализуема в WPF/Windows","Разрыв handoff","Tech review на Gates 5.1/5.2","Tech lead","Открыт","S5-0108"],
  ["R-006","Средний","Открытые OQ смешаны с design defects","Неясный допуск","Отдельный Decisions register","Product owner","Контролируется","S5-0006"],
];

const wb = Workbook.create();
const dashboard = wb.worksheets.add("Dashboard");
const stageStatusSheet = wb.worksheets.add("Stage Status");
const kanban = wb.worksheets.add("Kanban");
const register = wb.worksheets.add("Task Register");
const decisionSheet = wb.worksheets.add("Decisions");
const riskSheet = wb.worksheets.add("Risks");
const changeSheet = wb.worksheets.add("Change Log");
const scrSheet = wb.worksheets.add("SCR Coverage");
const flowSheet = wb.worksheets.add("FLOW Coverage");
const stateSheet = wb.worksheets.add("STATE Coverage");
const nfrSheet = wb.worksheets.add("NFR Coverage");
const componentUsageSheet = wb.worksheets.add("Component Usage");
const implementationSpecsSheet = wb.worksheets.add("Implementation Specs");
const verticalSliceSheet = wb.worksheets.add("Vertical Slice");
const roleMatrixSheet = wb.worksheets.add("Role Matrix");
const stateComponentSheet = wb.worksheets.add("State Components");
const usabilitySheet = wb.worksheets.add("Usability Script");
const visScorecardSheet = wb.worksheets.add("VIS Scorecard");
for (const s of [dashboard, stageStatusSheet, kanban, register, decisionSheet, riskSheet, changeSheet, scrSheet, flowSheet, stateSheet, nfrSheet, componentUsageSheet, implementationSpecsSheet, verticalSliceSheet, roleMatrixSheet, stateComponentSheet, usabilitySheet, visScorecardSheet]) {
  s.showGridLines = false;
}

const navy = "#14213D";
const blue = "#246BFD";
const teal = "#0E8A74";
const light = "#F4F7FB";
const border = "#D9E1EC";
const amber = "#F59E0B";
const red = "#D94A4A";
const green = "#2E9D69";
const grey = "#697386";
const titleFormat = { fill: navy, font: { bold: true, color: "#FFFFFF", size: 18 }, verticalAlignment: "center" };
const sectionFormat = { fill: "#E8EEF8", font: { bold: true, color: navy }, borders: { preset: "outside", style: "thin", color: border } };
const headerFormat = { fill: navy, font: { bold: true, color: "#FFFFFF" }, horizontalAlignment: "center", verticalAlignment: "center", wrapText: true };

// Task Register
register.getRange("A1:Q1").merge();
register.getRange("A1").values = [["Stage 5 — канонический реестр задач"]];
register.getRange("A1:Q1").format = titleFormat;
register.getRange("A2:Q2").merge();
register.getRange("A2").values = [["Редактируйте Status, Owner, Progress и Next action. Dashboard пересчитывается формулами; Kanban — визуальный снимок, который Codex обновляет при каждом проходе."]];
register.getRange("A2:Q2").format = { fill: "#EAF2FF", font: { color: navy, italic: true }, wrapText: true };
const taskHeaders = ["ID","Stage","Workstream","Gate","Priority","Task","Status","Owner","Dependency","Start","Target","Progress","Evidence","Canonical source","Last update","Next action","Notes"];
register.getRange("A4:Q4").values = [taskHeaders];
register.getRange("A4:Q4").format = headerFormat;
register.getRangeByIndexes(4,0,tasks.length,taskHeaders.length).values = tasks;
register.getRange(`J5:K${tasks.length+4}`).format.numberFormat = "yyyy-mm-dd";
register.getRange(`O5:O${tasks.length+4}`).format.numberFormat = "yyyy-mm-dd";
register.getRange(`B5:B${tasks.length+4}`).format.numberFormat = "0.0";
register.getRange(`D5:D${tasks.length+4}`).format.numberFormat = "0.0";
register.getRange(`L5:L${tasks.length+4}`).format.numberFormat = "0%";
register.getRange(`F5:Q${tasks.length+4}`).format.wrapText = true;
register.getRange(`A4:Q${tasks.length+4}`).format.borders = { preset: "inside", style: "thin", color: "#E7ECF3" };
register.tables.add(`A4:Q${tasks.length+4}`, true, "Stage5Tasks").style = "TableStyleMedium2";
register.freezePanes.freezeRows(4);
register.freezePanes.freezeColumns(2);
register.getRange(`G5:G${tasks.length+4}`).dataValidation = { rule: { type: "list", values: ["Бэклог","Готово к работе","В работе","На проверке","Ожидает решения","Блокировано","Готово"] } };
register.getRange(`E5:E${tasks.length+4}`).dataValidation = { rule: { type: "list", values: ["P0","P1","P2","P3"] } };
register.getRange(`L5:L${tasks.length+4}`).dataValidation = { rule: { type: "decimal", operator: "between", formula1: 0, formula2: 1 } };
const statusRange = register.getRange(`G5:G${tasks.length+4}`);
statusRange.conditionalFormats.add("containsText", { text: "Готово", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
statusRange.conditionalFormats.add("containsText", { text: "В работе", format: { fill: "#DCE9FF", font: { color: "#164E9B", bold: true } } });
statusRange.conditionalFormats.add("containsText", { text: "Блокировано", format: { fill: "#FDE3E3", font: { color: "#A12727", bold: true } } });
statusRange.conditionalFormats.add("containsText", { text: "Ожидает решения", format: { fill: "#FFF0CF", font: { color: "#8A5600", bold: true } } });
register.getRange(`L5:L${tasks.length+4}`).conditionalFormats.add("dataBar", { color: blue, gradient: true });
const widths = [12,8,16,8,9,38,18,20,18,12,12,11,24,25,12,34,28];
widths.forEach((w,i)=>register.getRangeByIndexes(0,i,tasks.length+4,1).format.columnWidth = w);
register.getRange("A1:Q1").format.rowHeight = 32;
register.getRange("A2:Q2").format.rowHeight = 34;
register.getRange("A4:Q4").format.rowHeight = 32;

// Stage Status: accepted delivery completion is independent from remaining Gate verification.
stageStatusSheet.getRange("A1:G2").merge();
stageStatusSheet.getRange("A1").values = [["Stage 5 — Delivery completion и Gate readiness"]];
stageStatusSheet.getRange("A1:G2").format = titleFormat;
stageStatusSheet.getRange("A3:G3").merge();
stageStatusSheet.getRange("A3").values = [["Accepted delivery baseline фиксирует уже завершённый объём и не снижается из-за последующих review/Gate задач. Task-derived progress остаётся рядом как диагностическая метрика."]];
stageStatusSheet.getRange("A3:G3").format = { fill: "#EAF2FF", font: { color: navy, italic: true }, wrapText: true };
stageStatusSheet.getRange("A4:G4").values = [["Stage","Accepted delivery baseline","Task-derived progress","Reported delivery completion","Gate status","Open verification / decisions","Evidence"]];
stageStatusSheet.getRange("A4:G4").format = headerFormat;
stageStatusSheet.getRange("A5:B11").values = [
  ["5.0",1],
  ["5.1",1],
  ["5.2",1],
  ["5.3",null],
  ["5.4",null],
  ["5.5",null],
  ["5.6",null],
];
stageStatusSheet.getRange("E5:G11").values = [
  ["Delivery завершён; OQ отдельно","OQ-004/005/006/009 не снижают завершённость Stage 5.0","План, inventory и динамическая доска приняты"],
  ["Delivery завершён; formal review открыт","Product + WPF/Windows tech review и формальная фиксация Gate","Direction 2, foundations и interaction states завершены"],
  ["Delivery завершён; OS/QA review открыт","UIA/Narrator, actual 200%, contrast, Windows runtime и formal approval","45/45 component families prototype-verified"],
  ["Открыт — formal approval","Formal evidence approval и native Windows/runtime review","128/128 SCR и 37/37 FLOW mapped; CalendarEvent и Operations browser-verified"],
  ["Открыт — native Windows + approval","Narrator/UIA, actual Windows 100–200% multi-monitor scaling и owner approval","38/38 roles, 56/56 states и prototype audit Critical/High = 0"],
  ["Не закрыт","Usability sessions и remediation","Usability script подготовлен"],
  ["Не закрыт","Final audit, handoff, manifest и approvals","План Stage 5.6"],
];
for (let i=0;i<7;i++) {
  const row=5+i;
  stageStatusSheet.getRange(`C${row}`).formulas = [[`=IFERROR(AVERAGEIF('Task Register'!$B$5:$B$${tasks.length+4},A${row},'Task Register'!$L$5:$L$${tasks.length+4}),0)`]];
  stageStatusSheet.getRange(`D${row}`).formulas = [[`=IF(B${row}="",C${row},B${row})`]];
}
stageStatusSheet.getRange("B5:D11").format.numberFormat = "0%";
stageStatusSheet.getRange("A5:A11").format.numberFormat = "0.0";
stageStatusSheet.getRange("A4:G11").format.borders = { preset: "inside", style: "thin", color: "#E7ECF3" };
stageStatusSheet.getRange("A5:G11").format.wrapText = true;
stageStatusSheet.getRange("D5:D11").conditionalFormats.add("dataBar", { color: blue, gradient: true });
stageStatusSheet.getRange("E5:E11").conditionalFormats.add("containsText", { text: "Delivery завершён", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
stageStatusSheet.getRange("E5:E11").conditionalFormats.add("containsText", { text: "Открыт", format: { fill: "#DCE9FF", font: { color: "#164E9B", bold: true } } });
stageStatusSheet.getRange("E5:E11").conditionalFormats.add("containsText", { text: "Не закрыт", format: { fill: "#FFF0CF", font: { color: "#8A5600", bold: true } } });
stageStatusSheet.tables.add("A4:G11",true,"StageDeliveryStatus").style="TableStyleMedium2";
[10,22,20,24,34,46,42].forEach((w,i)=>stageStatusSheet.getRangeByIndexes(0,i,11,1).format.columnWidth=w);
stageStatusSheet.getRange("A1:G2").format.rowHeight = 30;
stageStatusSheet.getRange("A3:G3").format.rowHeight = 34;
stageStatusSheet.getRange("A4:G4").format.rowHeight = 32;
stageStatusSheet.freezePanes.freezeRows(4);

// Dashboard
dashboard.getRange("A1:H2").merge();
dashboard.getRange("A1").values = [["Stage 5 — Task Board"]];
dashboard.getRange("A1:H2").format = { ...titleFormat, font: { bold: true, color: "#FFFFFF", size: 22 } };
dashboard.getRange("A3:H3").merge();
dashboard.getRange("A3").values = [["Обновлено: 2026-08-02 · Активная цель: выполнить Stage 5 · Сейчас: 5.0–5.2 delivery = 100%; Stage 5.3 = 85%; Stage 5.4 = 76%; Stage 5.5 = 69%; Stage 5.6 = 80%; Gate readiness отдельно"]];
dashboard.getRange("A3:H3").format = { fill: "#EAF2FF", font: { color: navy }, wrapText: true };
dashboard.getRange("A5:B5").values = [["Показатель","Значение"]];
dashboard.getRange("A5:B5").format = headerFormat;
dashboard.getRange("A6:A10").values = [["Всего задач"],["Готово"],["В работе"],["Блокировано"],["Общий прогресс"]];
dashboard.getRange("B6:B10").formulas = [
  [`=COUNTA('Task Register'!$A$5:$A$${tasks.length+4})`],
  [`=COUNTIF('Task Register'!$G$5:$G$${tasks.length+4},"Готово")`],
  [`=COUNTIF('Task Register'!$G$5:$G$${tasks.length+4},"В работе")`],
  [`=COUNTIF('Task Register'!$G$5:$G$${tasks.length+4},"Блокировано")`],
  [`=SUMPRODUCT(B13:B19,D13:D19)/SUM(B13:B19)`],
];
dashboard.getRange("B10").format.numberFormat = "0%";
dashboard.getRange("B10").conditionalFormats.add("dataBar", { color: teal, gradient: true });
dashboard.getRange("D5:H5").merge();
dashboard.getRange("D5").values = [["Текущий фокус"]];
dashboard.getRange("D5:H5").format = headerFormat;
dashboard.getRange("D6:H7").merge();
dashboard.getRange("D6").values = [["Stage 5.6 final package 1.0.1: code-based baseline + Design System + prototype + handoff + Gate kit; 128/128 SCR, 37/37 FLOW, 38/38 roles, 56/56 states, 45/45 components; 83/83 mirror; 15/15 tests."]];
dashboard.getRange("D6:H7").format = { fill: "#DCE9FF", font: { bold: true, color: navy, size: 13 }, wrapText: true, verticalAlignment: "center" };
dashboard.getRange("D6:H7").format.rowHeight = 40;
dashboard.getRange("D8:H9").merge();
dashboard.getRange("D8").values = [["Gate 5.6 execution kit 0.1.0 готов: Windows UIA/Narrator, DPI, moderated sessions и sign-off имеют точные протоколы и валидатор. Текущий результат NOT_READY 0/9 — реальные внешние evidence ещё не добавлены."]];
dashboard.getRange("D8:H9").format = { fill: "#FFF0CF", font: { color: "#6B4600" }, wrapText: true, verticalAlignment: "center" };
dashboard.getRange("A12:F12").values = [["Stage","Всего задач","Закрыто задач","Delivery completion","Gate","Gate status"]];
dashboard.getRange("A12:F12").format = headerFormat;
const stages = ["5.0","5.1","5.2","5.3","5.4","5.5","5.6"];
dashboard.getRange("A13:A19").values = stages.map(x=>[x]);
dashboard.getRange("A13:A19").format.numberFormat = "0.0";
dashboard.getRange("E13:E19").values = stages.map(x=>[`Gate ${x}`]);
for (let i=0;i<stages.length;i++) {
  const row=13+i;
  dashboard.getRange(`B${row}`).formulas = [[`=COUNTIF('Task Register'!$B$5:$B$${tasks.length+4},A${row})`]];
  dashboard.getRange(`C${row}`).formulas = [[`=COUNTIFS('Task Register'!$B$5:$B$${tasks.length+4},A${row},'Task Register'!$G$5:$G$${tasks.length+4},"Готово")`]];
  dashboard.getRange(`D${row}`).formulas = [[`='Stage Status'!D${5+i}`]];
  dashboard.getRange(`F${row}`).formulas = [[`='Stage Status'!E${5+i}`]];
}
dashboard.getRange("D13:D19").format.numberFormat = "0%";
dashboard.getRange("D13:D19").conditionalFormats.add("dataBar", { color: blue, gradient: true });
dashboard.getRange("F13:F19").conditionalFormats.add("containsText", { text: "Delivery завершён", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
dashboard.getRange("F13:F19").conditionalFormats.add("containsText", { text: "Открыт", format: { fill: "#DCE9FF", font: { color: "#164E9B", bold: true } } });
dashboard.getRange("F13:F19").conditionalFormats.add("containsText", { text: "Не закрыт", format: { fill: "#FFF0CF", font: { color: "#8A5600", bold: true } } });
dashboard.getRange("A21:H21").merge();
dashboard.getRange("A21").values = [["Текущий delivery focus"]];
dashboard.getRange("A21:H21").format = headerFormat;
dashboard.getRange("A22:H24").merge();
dashboard.getRange("A22").values = [["Следующий фактический результат: выполнить stage_5_6_external_gate_execution_kit, получить 9/9 принятых hash-addressed evidence items и только затем принимать решение по Gate 5.6."]];
dashboard.getRange("A22:H24").format = { fill: "#DDF3E8", font: { bold: true, color: "#146C43", size: 13 }, wrapText: true, verticalAlignment: "center" };
dashboard.getRange("A26:H26").merge();
dashboard.getRange("A26").values = [["Сквозное покрытие входов Stage 5"]];
dashboard.getRange("A26:H26").format = headerFormat;
dashboard.getRange("A27:E27").values = [["Контур","Всего","Mapped / canonical","Visual evidence pending","Состояние"]];
dashboard.getRange("A27:E27").format = sectionFormat;
dashboard.getRange("A28:A31").values = [["SCR"],["FLOW"],["STATE"],["NFR"]];
dashboard.getRange("B28:B31").formulas = [
  [`=COUNTA('SCR Coverage'!$A$5:$A$${scrRows.length + 4})`],
  [`=COUNTA('FLOW Coverage'!$A$5:$A$${flowRows.length + 4})`],
  [`=COUNTA('STATE Coverage'!$A$5:$A$${stateRows.length + 4})`],
  [`=COUNTA('NFR Coverage'!$A$5:$A$${nfrRows.length + 4})`],
];
dashboard.getRange("C28:C31").formulas = [
  [`=COUNTIF('SCR Coverage'!$J$5:$J$${scrRows.length + 4},"Mapped")`],
  [`=COUNTIF('FLOW Coverage'!$K$5:$K$${flowRows.length + 4},"Mapped")`],
  [`=COUNTIF('STATE Coverage'!$G$5:$G$${stateRows.length + 4},"PASS")`],
  [`=COUNTIF('NFR Coverage'!$I$5:$I$${nfrRows.length + 4},"Mapped")`],
];
dashboard.getRange("D28:D31").formulas = [
  [`=COUNTIF('SCR Coverage'!$L$5:$L$${scrRows.length + 4},"Visual evidence pending")`],
  [`=COUNTIF('FLOW Coverage'!$M$5:$M$${flowRows.length + 4},"Visual evidence pending")`],
  [`=COUNTIF('STATE Coverage'!$J$5:$J$${stateRows.length + 4},"Visual evidence pending")`],
  [`=COUNTIF('NFR Coverage'!$J$5:$J$${nfrRows.length + 4},"Visual/QA evidence pending")`],
];
dashboard.getRange("E28:E31").formulas = [
  [`=IF(C28=B28,"Трассировка готова","Есть пробелы")`],
  [`=IF(C29=B29,"Трассировка готова","Есть пробелы")`],
  [`=IF(C30=B30,"Трассировка готова","Есть пробелы")`],
  [`=IF(C31=B31,"Трассировка готова","Есть пробелы")`],
];
dashboard.getRange("A27:E31").format.borders = { preset: "all", style: "thin", color: border };
dashboard.getRange("E28:E31").conditionalFormats.add("containsText", { text: "Трассировка готова", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
dashboard.getRange("A33:H33").merge();
dashboard.getRange("A33").values = [["Готовность implementation contracts 5.2–5.5"]];
dashboard.getRange("A33:H33").format = headerFormat;
dashboard.getRange("A34:E34").values = [["Контракт","План","Готово","Готовность","Следующая зависимость"]];
dashboard.getRange("A34:E34").format = sectionFormat;
dashboard.getRange("A35:A39").values = [["Component families"],["Vertical-slice FLOW"],["Role/action contracts"],["Published state rows"],["Usability scenarios"]];
dashboard.getRange("B35:B39").values = [[45],[10],[38],[56],[10]];
dashboard.getRange("C35:C39").formulas = [
  [`=COUNTIF('Component Usage'!$R$5:$R$${componentUsageRows.length + 4},"Frozen for Gate 5.2 candidate")`],
  [`=COUNTIF('Vertical Slice'!$R$5:$R$${verticalSliceRows.length + 4},"Scenario contract ready")`],
  [`=COUNTIF('Role Matrix'!$N$5:$N$${roleMatrixRows.length + 4},"Role contract ready")`],
  [`=COUNTIF('State Components'!$O$5:$O$${stateComponentRows.length + 4},"State contract ready")`],
  [`=COUNTIF('Usability Script'!$R$5:$R$${usabilityRows.length + 4},"Test contract ready")`],
];
dashboard.getRange("D35:D39").formulas = [["=C35/B35"],["=C36/B36"],["=C37/B37"],["=C38/B38"],["=C39/B39"]];
dashboard.getRange("D35:D39").format.numberFormat = "0%";
dashboard.getRange("D35:D39").conditionalFormats.add("dataBar", { color: teal, gradient: true });
dashboard.getRange("E35:E39").values = [
  ["45/45 representative evidence → formal runtime verification"],
  ["Prototype → remaining P0 frames"],
  ["Gate 5.2 → role comparison frames"],
  ["Gate 5.2 → remaining state evidence"],
  ["Prototype → dry run/execution"],
];
dashboard.getRange("A34:E39").format.borders = { preset: "all", style: "thin", color: border };
["A","B","C","D","E","F","G","H"].forEach((c,i)=>dashboard.getRange(`${c}1:${c}39`).format.columnWidth=[18,14,18,20,24,18,18,18][i]);
dashboard.getRange("A1:H2").format.rowHeight = 30;

// Kanban snapshot
kanban.getRange("A1:F2").merge();
kanban.getRange("A1").values = [["Stage 5 — Kanban"]];
kanban.getRange("A1:F2").format = { ...titleFormat, font: { bold: true, color: "#FFFFFF", size: 20 } };
const statusCols = ["Бэклог","Готово к работе","В работе","На проверке","Ожидает решения","Блокировано"];
kanban.getRange("A4:F4").values = [statusCols];
kanban.getRange("A4:F4").format = headerFormat;
const grouped = Object.fromEntries(statusCols.map(s=>[s,tasks.filter(t=>t[6]===s)]));
const maxCards = Math.max(...statusCols.map(s=>grouped[s].length),1);
const grid = Array.from({length:maxCards},(_,r)=>statusCols.map(s=>{
  const t=grouped[s][r];
  return t ? `${t[0]} · ${t[1]}\n${t[5]}\n${t[7]} · ${Math.round(t[11]*100)}%` : "";
}));
kanban.getRangeByIndexes(4,0,maxCards,6).values = grid;
kanban.getRange(`A5:F${maxCards+4}`).format = { wrapText: true, verticalAlignment: "top", fill: "#FFFFFF", borders: { preset: "all", style: "thin", color: border } };
for(let r=5;r<=maxCards+4;r++) kanban.getRange(`A${r}:F${r}`).format.rowHeight = 58;
for(let c=0;c<6;c++) kanban.getRangeByIndexes(0,c,maxCards+4,1).format.columnWidth = 26;
kanban.freezePanes.freezeRows(4);
kanban.getRange("A3:F3").merge();
kanban.getRange("A3").values = [["Статусы меняются в Task Register; Codex обновляет этот визуальный снимок после каждого значимого прохода."]];
kanban.getRange("A3:F3").format = { fill: "#EAF2FF", font: { italic: true, color: navy } };

// Decisions
decisionSheet.getRange("A1:I2").merge();
decisionSheet.getRange("A1").values = [["Stage 5 — Decisions"]];
decisionSheet.getRange("A1:I2").format = titleFormat;
const decisionHeaders=["ID","Stage","Decision","Required action","Owner","Status","Due","Impact","Related task"];
decisionSheet.getRange("A4:I4").values=[decisionHeaders];
decisionSheet.getRange("A4:I4").format=headerFormat;
decisionSheet.getRangeByIndexes(4,0,decisions.length,decisionHeaders.length).values=decisions;
decisionSheet.getRange(`G5:G${decisions.length+4}`).format.numberFormat="yyyy-mm-dd";
decisionSheet.getRange(`B5:B${decisions.length+4}`).format.numberFormat="0.0";
decisionSheet.getRange(`A4:I${decisions.length+4}`).format.wrapText=true;
decisionSheet.tables.add(`A4:I${decisions.length+4}`,true,"Stage5Decisions").style="TableStyleMedium2";
decisionSheet.getRange(`F5:F${decisions.length+4}`).dataValidation={rule:{type:"list",values:["Открыто","Принято","Отложено","Не применимо"]}};
[12,8,24,34,22,14,12,28,14].forEach((w,i)=>decisionSheet.getRangeByIndexes(0,i,decisions.length+4,1).format.columnWidth=w);
decisionSheet.freezePanes.freezeRows(4);

// Risks
riskSheet.getRange("A1:H2").merge();
riskSheet.getRange("A1").values = [["Stage 5 — Risks"]];
riskSheet.getRange("A1:H2").format = titleFormat;
const riskHeaders=["ID","Severity","Risk","Consequence","Mitigation","Owner","Status","Related task"];
riskSheet.getRange("A4:H4").values=[riskHeaders];
riskSheet.getRange("A4:H4").format=headerFormat;
riskSheet.getRangeByIndexes(4,0,risks.length,riskHeaders.length).values=risks;
riskSheet.getRange(`A4:H${risks.length+4}`).format.wrapText=true;
riskSheet.tables.add(`A4:H${risks.length+4}`,true,"Stage5Risks").style="TableStyleMedium2";
riskSheet.getRange(`B5:B${risks.length+4}`).conditionalFormats.add("containsText",{text:"Высокий",format:{fill:"#FDE3E3",font:{color:"#A12727",bold:true}}});
riskSheet.getRange(`B5:B${risks.length+4}`).conditionalFormats.add("containsText",{text:"Средний",format:{fill:"#FFF0CF",font:{color:"#8A5600",bold:true}}});
[11,12,32,28,34,18,18,14].forEach((w,i)=>riskSheet.getRangeByIndexes(0,i,risks.length+4,1).format.columnWidth=w);
riskSheet.freezePanes.freezeRows(4);

function setupCoverageSheet(sheet, title, note, headers, rows, tableName, widths) {
  const lastColumn = String.fromCharCode(64 + headers.length);
  sheet.getRange(`A1:${lastColumn}2`).merge();
  sheet.getRange("A1").values = [[title]];
  sheet.getRange(`A1:${lastColumn}2`).format = titleFormat;
  sheet.getRange(`A3:${lastColumn}3`).merge();
  sheet.getRange("A3").values = [[note]];
  sheet.getRange(`A3:${lastColumn}3`).format = { fill: "#EAF2FF", font: { color: navy, italic: true }, wrapText: true };
  sheet.getRange(`A4:${lastColumn}4`).values = [headers];
  sheet.getRange(`A4:${lastColumn}4`).format = headerFormat;
  sheet.getRangeByIndexes(4, 0, rows.length, headers.length).values = rows;
  sheet.getRange(`A4:${lastColumn}${rows.length + 4}`).format.wrapText = true;
  sheet.getRange(`A4:${lastColumn}${rows.length + 4}`).format.borders = { preset: "inside", style: "thin", color: "#E7ECF3" };
  sheet.tables.add(`A4:${lastColumn}${rows.length + 4}`, true, tableName).style = "TableStyleMedium2";
  widths.forEach((width, index) => sheet.getRangeByIndexes(0, index, rows.length + 4, 1).format.columnWidth = width);
  sheet.freezePanes.freezeRows(4);
  sheet.freezePanes.freezeColumns(1);
}

const scrCoverage = scrRows.map((row) => [
  row["SCR ID"],
  row["Module"],
  row["Surface name"],
  row["Surface type"],
  row["Primary pattern"],
  row["Priority"],
  row["Shared components"],
  row["States"],
  row["Style dependency"],
  "Mapped",
  row["Priority"] === "P0" ? "Hi-fi frame + state set + keyboard walkthrough" : "Pattern/component specification + state set",
  "Visual evidence pending",
]);
setupCoverageSheet(
  scrSheet,
  "Stage 5 — SCR Coverage",
  "128 уникальных нормативных поверхностей. Строка считается трассированной, когда известны pattern/components и требуемое design evidence; визуальный статус обновляется по мере 5.2–5.4.",
  ["SCR ID","Module","Surface","Type","Pattern","Priority","Shared components","States","Style dependency","Mapping","Required design evidence","Evidence status"],
  scrCoverage,
  "Stage5SCRCoverage",
  [12,14,30,16,18,9,38,30,14,12,34,32],
);
scrSheet.getRange(`J5:J${scrRows.length + 4}`).conditionalFormats.add("containsText", { text: "Mapped", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
scrSheet.getRange(`L5:L${scrRows.length + 4}`).conditionalFormats.add("containsText", { text: "pending", format: { fill: "#FFF0CF", font: { color: "#8A5600" } } });

const flowCoverage = flowRows.map((row) => [
  row["FLOW ID"],
  row["Scenario group"],
  row["Flow"],
  row["Roles"],
  row["Outcome"],
  row["SCR references"],
  row["STATE references"],
  row["Modules"],
  row["Shared components"],
  row["Priority"],
  "Mapped",
  row["Required design evidence"],
  "Visual evidence pending",
]);
setupCoverageSheet(
  flowSheet,
  "Stage 5 — FLOW Coverage",
  "37/37 пользовательских потоков связаны с экранами, общими компонентами и формой требуемого design evidence.",
  ["FLOW ID","Scenario group","Flow","Roles","Outcome","SCR references","STATE references","Modules","Shared components","Priority","Mapping","Required design evidence","Evidence status"],
  flowCoverage,
  "Stage5FLOWCoverage",
  [12,20,30,24,32,34,24,22,40,9,12,34,28],
);
flowSheet.getRange(`K5:K${flowRows.length + 4}`).conditionalFormats.add("containsText", { text: "Mapped", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
flowSheet.getRange(`M5:M${flowRows.length + 4}`).conditionalFormats.add("containsText", { text: "pending", format: { fill: "#FFF0CF", font: { color: "#8A5600" } } });

const stateCoverage = stateRows.map((row) => [
  row["Original reference"],
  row["Resolution type"],
  row["Canonical target"],
  row["Source location"],
  row["Trigger/entry/UI/actions/recovery"],
  row["Hidden new STATE"],
  row["Result"],
  row["Evidence"],
  "State component/spec + trigger, UI, actions and recovery evidence",
  "Visual evidence pending",
]);
setupCoverageSheet(
  stateSheet,
  "Stage 5 — STATE Coverage",
  "30 исходных STATE нормализованы в Stage 4.6 Lite. Stage 5 не вводит скрытых состояний и должен дать визуальное/интерактивное доказательство для каждого канонического target.",
  ["STATE reference","Resolution","Canonical target","Canonical source","Behavior completeness","Hidden new STATE","Canonical result","Canonical evidence","Required design evidence","Design evidence status"],
  stateCoverage,
  "Stage5STATECoverage",
  [14,18,24,38,24,16,14,34,38,34],
);
stateSheet.getRange(`G5:G${stateRows.length + 4}`).conditionalFormats.add("containsText", { text: "PASS", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
stateSheet.getRange(`J5:J${stateRows.length + 4}`).conditionalFormats.add("containsText", { text: "pending", format: { fill: "#FFF0CF", font: { color: "#8A5600" } } });

const accessibilityNfr = new Set(["NFR-002","NFR-003","NFR-004","NFR-005"]);
const nfrCoverage = nfrRows.map((row) => [
  row["NFR ID"],
  row["Area"],
  row["Requirement"],
  row["Target"],
  row["Measurement"],
  row["Modules"],
  row["Source/Assumption"],
  accessibilityNfr.has(row["NFR ID"]) ? "Accessibility baseline + component/spec evidence + Gate 5.4 QA" : "NFR-specific design specification + prototype/QA evidence",
  "Mapped",
  "Visual/QA evidence pending",
]);
setupCoverageSheet(
  nfrSheet,
  "Stage 5 — NFR Coverage",
  "25/25 NFR привязаны к требуемому design/QA evidence. NFR-002–005 уже имеют рабочий accessibility baseline; финальные доказательства собираются на Gates 5.2–5.4.",
  ["NFR ID","Area","Requirement","Target","Measurement","Modules","Canonical source","Required Stage 5 evidence","Mapping","Evidence status"],
  nfrCoverage,
  "Stage5NFRCoverage",
  [12,16,42,42,42,20,30,40,12,34],
);
nfrSheet.getRange(`I5:I${nfrRows.length + 4}`).conditionalFormats.add("containsText", { text: "Mapped", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
nfrSheet.getRange(`J5:J${nfrRows.length + 4}`).conditionalFormats.add("containsText", { text: "pending", format: { fill: "#FFF0CF", font: { color: "#8A5600" } } });

const componentUsageHeaders = [
  "Component ID","Component","Library path","Library tier","Priority","Surface count","SCR IDs","FLOW count",
  "Required variants","Related canonical STATE","Related NFR","Behavior contract","Failure rule",
  "Implementation readiness","Evidence","Remaining verification","Spec version","Freeze status",
];
const componentUsageValues = componentUsageRows.map((row) => componentUsageHeaders.map((header) => {
  if (header === "Surface count" || header === "FLOW count") return Number(row[header]);
  return row[header];
}));
setupCoverageSheet(
  componentUsageSheet,
  "Stage 5.2 — Component Usage 1.0",
  "Frozen: 45/45 component families, 128/128 SCR и 37/37 FLOW. Каждая строка содержит behavior/failure contract, evidence и remaining verification.",
  componentUsageHeaders,
  componentUsageValues,
  "Stage5ComponentUsage",
  [12,22,28,22,9,12,42,11,34,30,24,42,42,20,42,42,12,24],
);
componentUsageSheet.getRange(`N5:N${componentUsageRows.length + 4}`).conditionalFormats.add("containsText", { text: "Prototype-verified", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
componentUsageSheet.getRange(`N5:N${componentUsageRows.length + 4}`).conditionalFormats.add("containsText", { text: "Partially verified", format: { fill: "#FFF0CF", font: { color: "#8A5600", bold: true } } });
componentUsageSheet.getRange(`N5:N${componentUsageRows.length + 4}`).conditionalFormats.add("containsText", { text: "Specified", format: { fill: "#FDE3E3", font: { color: "#A12727", bold: true } } });

const implementationSpecHeaders = [
  "Component ID","Component","Library path","Priority","Purpose","Anatomy","Required variants","State inputs",
  "Required NFR","Keyboard contract","UIA contract","Scaling contract","Failure rule","Implementation readiness",
  "Evidence","Remaining verification","Spec version",
];
setupCoverageSheet(
  implementationSpecsSheet,
  "Stage 5.2 — Implementation Specs 0.9",
  "Behavior frozen for 45 families. 45 prototype-verified with formal Windows runtime and library-frame verification still pending.",
  implementationSpecHeaders,
  implementationSpecRows.map((row) => implementationSpecHeaders.map((header) => row[header])),
  "Stage5ImplementationSpecs",
  [12,22,28,9,42,38,34,32,24,42,42,42,42,20,42,42,12],
);
implementationSpecsSheet.getRange(`N5:N${implementationSpecRows.length + 4}`).conditionalFormats.add("containsText", { text: "Prototype-verified", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
implementationSpecsSheet.getRange(`N5:N${implementationSpecRows.length + 4}`).conditionalFormats.add("containsText", { text: "Partially verified", format: { fill: "#FFF0CF", font: { color: "#8A5600", bold: true } } });
implementationSpecsSheet.getRange(`N5:N${implementationSpecRows.length + 4}`).conditionalFormats.add("containsText", { text: "Specified", format: { fill: "#FDE3E3", font: { color: "#A12727", bold: true } } });

const verticalSliceHeaders = [
  "Slice","FLOW ID","Flow","Roles","Permission","API","Outcome","SCR references","Entry points",
  "Required states","Required errors","Shared components","Test fixture","Critical-path acceptance",
  "Keyboard contract","Required design evidence","Accessibility evidence","Contract status","Visual status",
];
const verticalSliceValues = verticalSliceRows.map((row) => verticalSliceHeaders.map((header) => row[header]));
setupCoverageSheet(
  verticalSliceSheet,
  "Stage 5.2 — Vertical Slice Contracts",
  "10 P0 flow contracts across Auth, Task Creation, Search/Redaction and Resilience/Conflict. Contracts are ready; frames and prototype remain dependent on VIS-001.",
  verticalSliceHeaders,
  verticalSliceValues,
  "Stage5VerticalSlice",
  [24,12,26,24,24,34,30,34,30,38,32,44,40,46,46,32,42,22,18],
);
verticalSliceSheet.getRange(`R5:R${verticalSliceRows.length + 4}`).conditionalFormats.add("containsText", { text: "ready", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
verticalSliceSheet.getRange(`S5:S${verticalSliceRows.length + 4}`).conditionalFormats.add("containsText", { text: "VIS-001", format: { fill: "#FFF0CF", font: { color: "#8A5600" } } });

const roleMatrixHeaders = [
  "Role contract ID","Screen/action","Admin","Manager","Employee","Observer","Permission/capability",
  "UI/server policy","Required presentation states","Required components","Design evidence",
  "Accessibility evidence","Canonical source","Contract status","Visual status",
];
setupCoverageSheet(
  roleMatrixSheet,
  "Stage 5.4 — Role/Capability Matrix",
  "38 canonical screen/action contracts compare Admin, Manager, Employee and Observer while preserving capability-first, server-authoritative behavior.",
  roleMatrixHeaders,
  roleMatrixRows.map((row) => roleMatrixHeaders.map((header) => row[header])),
  "Stage5RoleMatrix",
  [16,34,26,26,26,26,28,38,32,34,38,40,32,20,18],
);
roleMatrixSheet.getRange(`N5:N${roleMatrixRows.length + 4}`).conditionalFormats.add("containsText", { text: "ready", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
roleMatrixSheet.getRange(`O5:O${roleMatrixRows.length + 4}`).conditionalFormats.add("containsText", { text: "VIS-001", format: { fill: "#FFF0CF", font: { color: "#8A5600" } } });

const stateComponentHeaders = [
  "State contract ID","STATE references","Surface","State","Trigger","UI behavior","Allowed actions",
  "Message","Recovery","API/error","Required components","Required design evidence",
  "Accessibility evidence","Canonical source","Contract status","Visual status",
];
setupCoverageSheet(
  stateComponentSheet,
  "Stage 5.4 — State/Component Coverage",
  "56 published State Matrix rows map trigger, UI behavior, actions, recovery and API/error semantics to reusable Stage 5 component families.",
  stateComponentHeaders,
  stateComponentRows.map((row) => stateComponentHeaders.map((header) => row[header])),
  "Stage5StateComponents",
  [16,18,22,22,34,40,32,40,32,28,34,40,40,30,20,18],
);
stateComponentSheet.getRange(`O5:O${stateComponentRows.length + 4}`).conditionalFormats.add("containsText", { text: "ready", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
stateComponentSheet.getRange(`P5:P${stateComponentRows.length + 4}`).conditionalFormats.add("containsText", { text: "VIS-001", format: { fill: "#FFF0CF", font: { color: "#8A5600" } } });

const usabilityHeaders = [
  "Test case ID","Slice","FLOW ID","Participant role","Scenario","Starting fixture","Task prompt",
  "Critical success","Keyboard checkpoint","Accessibility checkpoint","Primary metric","Secondary metrics",
  "Stop condition","Moderator prompt","Evidence capture","Pass threshold","Design dependency",
  "Script status","Execution status",
];
setupCoverageSheet(
  usabilitySheet,
  "Stage 5.5 — Usability Test Script",
  "10 P0 scenarios cover all four system roles. Script, fixtures, metrics and stop conditions are ready; execution awaits the interactive prototype.",
  usabilityHeaders,
  usabilityRows.map((row) => usabilityHeaders.map((header) => row[header])),
  "Stage5UsabilityScript",
  [14,24,12,18,26,42,42,46,44,42,28,38,40,42,38,40,26,20,20],
);
usabilitySheet.getRange(`R5:R${usabilityRows.length + 4}`).conditionalFormats.add("containsText", { text: "ready", format: { fill: "#DDF3E8", font: { color: "#146C43", bold: true } } });
usabilitySheet.getRange(`S5:S${usabilityRows.length + 4}`).conditionalFormats.add("containsText", { text: "Pending", format: { fill: "#FFF0CF", font: { color: "#8A5600" } } });

const visScorecardHeaders = [
  "Criterion ID","Criterion","Weight","Direction 1 score","Direction 1 evidence",
  "Direction 2 score","Direction 2 evidence","Direction 3 score","Direction 3 evidence","Canonical basis",
];
const visScorecardValues = visScorecardRows.map((row) => visScorecardHeaders.map((header) => {
  if (header === "Weight" || header.endsWith("score")) return Number(row[header]);
  return row[header];
}));
setupCoverageSheet(
  visScorecardSheet,
  "Stage 5.1 — VIS-001 Decision Scorecard",
  "Evidence-based comparison of exactly three generated directions. Scores are 1–5; weights total 100%. Recommendation assists but does not replace Product owner selection.",
  visScorecardHeaders,
  visScorecardValues,
  "Stage5VISScorecard",
  [14,34,10,14,44,14,44,14,44,38],
);
visScorecardSheet.getRange(`C5:C${visScorecardRows.length + 4}`).format.numberFormat = "0%";
visScorecardSheet.getRange(`D5:D${visScorecardRows.length + 4}`).conditionalFormats.add("dataBar", { color: "#246BFD", gradient: true });
visScorecardSheet.getRange(`F5:F${visScorecardRows.length + 4}`).conditionalFormats.add("dataBar", { color: "#7C8AA5", gradient: true });
visScorecardSheet.getRange(`H5:H${visScorecardRows.length + 4}`).conditionalFormats.add("dataBar", { color: "#0E8A74", gradient: true });
visScorecardSheet.getRange("A13:J13").merge();
visScorecardSheet.getRange("A13").values = [["Weighted result"]];
visScorecardSheet.getRange("A13:J13").format = headerFormat;
visScorecardSheet.getRange("A14:C14").values = [["Direction","Score","Primary trade-off"]];
visScorecardSheet.getRange("A14:C14").format = sectionFormat;
visScorecardSheet.getRange("A15:A17").values = [["Direction 1"],["Direction 2"],["Direction 3"]];
visScorecardSheet.getRange("B15:B17").formulas = [
  [`=SUMPRODUCT($C$5:$C$${visScorecardRows.length + 4},$D$5:$D$${visScorecardRows.length + 4})/5`],
  [`=SUMPRODUCT($C$5:$C$${visScorecardRows.length + 4},$F$5:$F$${visScorecardRows.length + 4})/5`],
  [`=SUMPRODUCT($C$5:$C$${visScorecardRows.length + 4},$H$5:$H$${visScorecardRows.length + 4})/5`],
];
visScorecardSheet.getRange("B15:B17").format.numberFormat = "0%";
visScorecardSheet.getRange("B15:B17").conditionalFormats.add("dataBar", { color: blue, gradient: true });
visScorecardSheet.getRange("C15:C17").values = [
  ["Best reusable list-detail baseline; calendar-specific depth is secondary"],
  ["Best schedule-centric planning; highest cross-surface template cost"],
  ["Best dense keyboard triage; highest accessibility/scaling risk"],
];
visScorecardSheet.getRange("A14:C17").format.borders = { preset: "all", style: "thin", color: border };
visScorecardSheet.getRange("A19:B19").merge();
visScorecardSheet.getRange("A19").values = [["Analytical recommendation"]];
visScorecardSheet.getRange("A19:B19").format = headerFormat;
visScorecardSheet.getRange("C19:J19").merge();
visScorecardSheet.getRange("C19").formulas = [[`=IF(B15=MAX(B15:B17),"Direction 1",IF(B16=MAX(B15:B17),"Direction 2","Direction 3"))`]];
visScorecardSheet.getRange("C19:J19").format = { fill: "#DDF3E8", font: { color: "#146C43", bold: true, size: 14 }, verticalAlignment: "center" };
visScorecardSheet.getRange("A21:J22").merge();
visScorecardSheet.getRange("A21").values = [["AUTHORITATIVE SELECTION: Direction 2 — Timeline planner. Product owner accepted the higher cross-surface template cost in favor of schedule-centric planning. VIS-001 is closed."]];
visScorecardSheet.getRange("A21:J22").format = { fill: "#DDF3E8", font: { color: "#146C43", bold: true }, wrapText: true, verticalAlignment: "center" };

// Change Log
changeSheet.getRange("A1:F2").merge();
changeSheet.getRange("A1").values = [["Stage 5 — Change Log"]];
changeSheet.getRange("A1:F2").format = titleFormat;
changeSheet.getRange("A4:F4").values=[["Date","Version","Changed by","Change","Affected tasks","Evidence"]];
changeSheet.getRange("A4:F4").format=headerFormat;
changeSheet.getRange("A5:F34").values=[
  [D("2026-07-27"),"1.0","Codex","Создан и проверен план Stage 5.0–5.6","All","Stage_5_Visual_Design_Plan_1.0.zip"],
  [D("2026-07-28"),"1.1","Product owner","Разрешён запуск Stage 5; запрошена динамическая доска","All","User instruction"],
  [D("2026-07-28"),"1.2","Codex","Создан канонический Task Board; 5.1 и 5.2 переведены в работу","S5-0003,S5-0103,S5-0201","Stage_5_Task_Board.xlsx"],
  [D("2026-07-28"),"1.2","Codex","Зафиксирован блокер VIS-001 до визуальной реализации 5.2","S5-0104,S5-0203","Decisions sheet"],
  [D("2026-07-28"),"1.3","Codex","Сгенерированы и сохранены три визуальных направления 5.1","S5-0103,S5-0104","work/stage_5_1/directions"],
  [D("2026-07-28"),"1.4","Codex","Mapped 128/128 unique SCR и 45 component families; устранён двойной счёт delta rows","S5-0004,S5-0201,S5-0202","work/stage_5_2"],
  [D("2026-07-28"),"1.4","Codex","Создан нормативный accessibility baseline для keyboard/UIA/contrast/scaling/states","S5-0107","work/stage_5_1/Accessibility_Baseline_0.1.md"],
  [D("2026-07-28"),"1.5","Codex","Mapped 37/37 FLOW и завершена архитектура библиотеки: 9 tiers, naming, variants, composition","S5-0004,S5-0005,S5-0202","work/stage_5_2"],
  [D("2026-07-28"),"1.6","Codex","В доску добавлена сквозная трассировка 128 SCR, 37 FLOW, 30 STATE и 25 NFR до требуемого design evidence","S5-0005","SCR/FLOW/STATE/NFR Coverage sheets"],
  [D("2026-07-28"),"1.7","Codex","Подготовлены 45 component usage contracts и 10 vertical-slice scenario contracts; зафиксирован частичный прогресс визуально заблокированных задач","S5-0209:S5-0212,S5-0215","Component Usage + Vertical Slice sheets"],
  [D("2026-07-28"),"1.8","Codex","Подготовлены 38 role/action contracts, 56 state/component contracts и usability script на 10 сценариев/4 роли","S5-0401,S5-0402,S5-0501","Role Matrix + State Components + Usability Script"],
  [D("2026-07-28"),"1.9","Product owner","Выбрано Direction 2 — Timeline planner; VIS-001 закрыт, visual foundations и Stage 5.2 разблокированы","S5-0103:S5-0106,S5-0203","User decision + VIS Scorecard"],
  [D("2026-07-28"),"2.0","Codex","Завершены foundations/state spec 0.1; собран интерактивный Today timeline slice, production build и browser Design QA passed","S5-0105:S5-0108,S5-0203:S5-0215","work/stage_5_prototype + design-qa.md"],
  [D("2026-07-28"),"2.1","Codex","Расширен P0 vertical slice: Auth, Inbox→Task, Edit/Conflict, Search/redaction, server loss/read-only/diagnostics/recovery; production build и browser QA passed","S5-0204:S5-0214","work/stage_5_prototype + design-qa-stage5-p0.md"],
  [D("2026-07-28"),"2.2","Codex","Добавлены Auth/resilience edge states, adaptive shell и accessibility fixes: TLS/incompatible/locked/blocked/cursor/scope/maintenance/storage/reconnect interruption; build/browser/visual QA passed","S5-0204:S5-0214","design-qa-stage5-edge-states.md + Accessibility_Evidence_Working_0.2.md"],
  [D("2026-07-28"),"2.3","Codex","Реализованы Tasks table/filter/pagination, Projects tree/inspector, Notification Center с target recheck, People/Project pickers, SESSION_REVOKED и download/signature/repeated-failure states","S5-0204:S5-0214","design-qa-stage5-surfaces.md + Accessibility_Evidence_Working_0.3.md"],
  [D("2026-07-28"),"2.4","Codex","Заморожены Component Usage Map 1.0 и Implementation Specs 0.9: 45/45 families, 128/128 SCR, 37/37 FLOW, behavior/failure/accessibility contracts и explicit gaps","S5-0215","Component_Usage_Map_1.0 + Component_Implementation_Specs_0.9"],
  [D("2026-07-28"),"2.5","Codex","Реализованы и browser-verified оставшиеся 11 component families: bulk/context actions, project lifecycle/history, scheduling/reminders/recurrence, focus/permission/file-location states","S5-0205:S5-0214","design-qa-stage5-component-gaps.md + Accessibility_Evidence_Working_0.4.md"],
  [D("2026-07-28"),"2.6","Codex","Приняты результаты трёх задач Stage 5.3: Wave A реализована и проверена; Wave B/C coverage packages проверены; исправлена SCR-привязка Calendar и зафиксирован следующий implementation front","S5-0301:S5-0309","work/stage_5_3_wave_a + work/stage_5_3_wave_b + work/stage_5_3_wave_c"],
  [D("2026-07-28"),"2.7","Codex","Разделены delivery completion и Gate readiness: завершённые 5.0–5.2 снова показывают 100%, открытые review/OQ/OS checks сохранены отдельным статусом","S5-0006,S5-0108:S5-0109,S5-0203:S5-0216","Stage Status + Dashboard"],
  [D("2026-07-30"),"2.8","Codex","Wave B implementation фактически проверена и упакована: Projects, Files/SMB design states, CRM/comments/watchers/history; следующий front переведён на Wave C, Gate 5.3 оставлен открытым","S5-0303:S5-0305,S5-0309","work/stage_5_3_wave_b_implementation + SHA-256 21/21"],
  [D("2026-07-30"),"2.9","Codex","Wave C Search реализован и фактически проверен: full Search, Ctrl+K/Ctrl+F, filters/query transfer, permission-safe partial, offline cache-only; исправлен P1 state-sync defect; следующий front Archive/Trash","S5-0306,S5-0309","work/stage_5_3_wave_c_search_increment + SHA-256 16/16"],
  [D("2026-07-30"),"3.0","Codex","Wave C Archive/Trash реализован и фактически проверен: read-only lifecycle, permission-safe redaction, restore conflicts, legal hold, typed purge, loading/empty/offline; следующий front Settings","S5-0306,S5-0309","work/stage_5_3_wave_c_lifecycle_increment + SHA-256 17/17"],
  [D("2026-07-30"),"3.1","Codex","Wave C Settings реализован и фактически проверен: scoped shell, profile/security/notifications/calendar/device/cache/connection/accessibility/sessions, conflict/forbidden/offline; следующий front Admin","S5-0307,S5-0309","work/stage_5_3_wave_c_settings_increment + SHA-256 18/18"],
  [D("2026-07-30"),"3.2","Codex","Wave C Admin реализован и фактически проверен: capability-filtered users/departments/roles/sessions/resources, lifecycle guards, redaction, effective deny, unsafe path, loading/offline; следующий front Operations","S5-0307,S5-0309","work/stage_5_3_wave_c_admin_increment + SHA-256 18/18"],
  [D("2026-07-31"),"3.3","Codex","Wave C Operations реализован и automation-verified: Health/jobs/backups/audit/organization, build/Sites и 9/9 tests; package 15/15 including pre-refactor Health screenshot. Browser acceptance честно оставлен PARTIAL/BLOCKED из-за local URL policy","S5-0308,S5-0309","work/stage_5_3_wave_c_operations_increment + SHA-256 15/15"],
  [D("2026-07-31"),"3.4","Codex","Собран consolidated traceability package: 128/128 SCR и 37/37 FLOW mapped, 21/21 accepted evidence hashes pass; SCR-044 и Operations browser gaps сохранены явно, Gate остаётся open","S5-0309","work/stage_5_3_traceability + manifest SHA-256 33C5D9AD..."],
  [D("2026-08-01"),"3.5","Codex","Реализован и browser-verified canonical CalendarEvent editor; SCR-044/FLOW-031 переведены в VERIFIED_PACKAGE; traceability обновлена до 0.1.1, Gate остаётся open только по Operations browser evidence и approval","S5-0302,S5-0309","stage_5_3_calendar_event_editor_increment 0.1.0 + traceability 0.1.1"],
  [D("2026-08-01"),"3.6","Codex","Operations browser acceptance завершена: Health/jobs/backups/restore/audit/organization/limited-role/offline проверены, scroll/disabled-action defects исправлены; Operations 0.2.0 и traceability 0.1.2 hash-verified","S5-0308,S5-0309","stage_5_3_wave_c_operations_increment 0.2.0 + traceability 0.1.2"],
  [D("2026-08-01"),"3.7","Codex","Stage 5.4 prototype audit собран и проверен: 38/38 role contracts, 56/56 states, 45/45 component families; добавлена Windows forced-colors поддержка; Critical/High = 0; native Windows и approvals оставлены отдельным Gate","S5-0401:S5-0406","stage_5_4_design_audit_increment 0.1.0 + SHA-256 validation"],
  [D("2026-08-02"),"3.8","Codex","Stage 5.5 expert-proxy walkthrough завершён: 10/10 сценариев, 13/13 screenshot evidence; High draft-loss и Medium callout-overlap defects исправлены и повторно проверены; external sessions остаются Gate","S5-0501:S5-0504","stage_5_5_usability_increment 0.1.0 + SHA-256 validation"],
  [D("2026-08-02"),"3.9","Codex","Stage 5.6 design delivery собран: code-based Final Visual Baseline 1.0, Design System 1.0, Interactive Prototype 1.0, final traceability/audit, assets/licenses, decisions/findings и Development Handoff; Gate остаётся external","S5-0601:S5-0605","stage_5_6_final_visual_baseline_and_handoff 1.0.0 + 64/64 SHA-256 mirror"],
  [D("2026-08-02"),"4.0","Codex","Собран исполнимый external Gate 5.6 kit: 12 Windows accessibility checkpoints, 8 DPI/multi-monitor cases, moderated-session templates для UT-01–UT-10/4 roles, 4-role sign-off и валидатор 9 evidence items; честный статус NOT_READY 0/9","S5-0403:S5-0406,S5-0502:S5-0504,S5-0605","stage_5_6_external_gate_execution_kit 0.1.0 + SHA-256 validation"],
  [D("2026-08-02"),"4.1","Codex","Final Stage 5.6 package обновлён до 1.0.1: Gate execution kit включён внутрь handoff, 6/6 prerequisite manifests validated, 83/83 work/output files mirror; прежняя 1.0.0 помещена в quarantine","S5-0603:S5-0605","stage_5_6_final_visual_baseline_and_handoff 1.0.1 + SHA-256 validation"],
  [D("2026-08-02"),"4.2","Codex","Выполнен requirement-by-requirement completion audit: 8/8 package manifests valid, 6 требований achieved, 2 partial external, 1 not achieved; Gate evidence 0/9 и objective честно сохранён ACTIVE_NOT_COMPLETE","S5-0605","stage_5_completion_audit 0.1.0 + SHA-256 validation"],
];
changeSheet.getRange("A5:A39").format.numberFormat="yyyy-mm-dd";
changeSheet.getRange("B5:B39").format.numberFormat="0.0";
changeSheet.getRange("A4:F39").format.wrapText=true;
changeSheet.tables.add("A4:F34",true,"Stage5Changes").style="TableStyleMedium2";
[12,10,18,40,24,28].forEach((w,i)=>changeSheet.getRangeByIndexes(0,i,34,1).format.columnWidth=w);
changeSheet.freezePanes.freezeRows(4);

// Compact visual verification assets.
const renders = [
  ["Dashboard","A1:H39","dashboard.png"],
  ["Stage Status","A1:G11","stage-status.png"],
  ["Kanban",`A1:F${Math.min(maxCards+4,32)}`,"kanban.png"],
  ["Task Register","A1:Q15","task-register.png"],
  ["Decisions",`A1:I${decisions.length+4}`,"decisions.png"],
  ["Risks",`A1:H${risks.length+4}`,"risks.png"],
  ["Change Log","A1:F34","change-log.png"],
  ["SCR Coverage","A1:L14","scr-coverage.png"],
  ["FLOW Coverage","A1:M14","flow-coverage.png"],
  ["STATE Coverage","A1:J14","state-coverage.png"],
  ["NFR Coverage","A1:J14","nfr-coverage.png"],
  ["Component Usage","A1:R14","component-usage.png"],
  ["Implementation Specs","A1:Q14","implementation-specs.png"],
  ["Vertical Slice","A1:S14","vertical-slice.png"],
  ["Role Matrix","A1:O14","role-matrix.png"],
  ["State Components","A1:P14","state-components.png"],
  ["Usability Script","A1:S14","usability-script.png"],
  ["VIS Scorecard","A1:J22","vis-scorecard.png"],
];
for (const [sheetName,range,file] of renders) {
  const blob = await wb.render({ sheetName, range, scale: 1.25, format: "png" });
  await fs.writeFile(path.join(previewDir,file),new Uint8Array(await blob.arrayBuffer()));
}

const inspect = await wb.inspect({kind:"table",range:"Dashboard!A1:H39",include:"values,formulas",tableMaxRows:39,tableMaxCols:8,maxChars:14000});
console.log(inspect.ndjson);
const errors = await wb.inspect({kind:"match",searchTerm:"#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",options:{useRegex:true,maxResults:300},summary:"final formula error scan",maxChars:4000});
console.log(errors.ndjson);
const out = await SpreadsheetFile.exportXlsx(wb);
await out.save(outputPath);
const imported = await SpreadsheetFile.importXlsx(await FileBlob.load(outputPath));
const importedSheets = await imported.inspect({kind:"sheet",include:"id,name",maxChars:6000});
const importedDashboard = await imported.inspect({kind:"table",range:"Dashboard!A1:H39",include:"values,formulas",tableMaxRows:39,tableMaxCols:8,maxChars:14000});
const importedErrors = await imported.inspect({kind:"match",searchTerm:"#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",options:{useRegex:true,maxResults:300},summary:"reimported workbook formula error scan",maxChars:4000});
await fs.writeFile(`${outputPath}.inspect.ndjson`, `${importedSheets.ndjson}\n${importedDashboard.ndjson}\n${importedErrors.ndjson}\n`, "utf8");
for (const [sheetName,range,file] of renders) {
  const blob = await imported.render({ sheetName, range, scale: 1.25, format: "png" });
  await fs.writeFile(path.join(previewDir,file),new Uint8Array(await blob.arrayBuffer()));
}
const contactColumns = 3;
const contactCellWidth = 620;
const contactCellHeight = 420;
const contactRows = Math.ceil(renders.length / contactColumns);
const contactComposites = [];
for (let index = 0; index < renders.length; index += 1) {
  const preview = await sharp(path.join(previewDir, renders[index][2]))
    .resize(contactCellWidth - 20, contactCellHeight - 20, { fit: "inside", withoutEnlargement: true })
    .png()
    .toBuffer();
  const metadata = await sharp(preview).metadata();
  contactComposites.push({
    input: preview,
    left: (index % contactColumns) * contactCellWidth + Math.floor((contactCellWidth - (metadata.width || 0)) / 2),
    top: Math.floor(index / contactColumns) * contactCellHeight + Math.floor((contactCellHeight - (metadata.height || 0)) / 2),
  });
}
await sharp({
  create: {
    width: contactColumns * contactCellWidth,
    height: contactRows * contactCellHeight,
    channels: 4,
    background: "#EEF2F7",
  },
})
  .composite(contactComposites)
  .png()
  .toFile(path.join(previewDir, "all-sheets-contact-sheet.png"));
console.log(importedSheets.ndjson);
console.log(importedErrors.ndjson);
console.log(JSON.stringify({outputPath,taskCount:tasks.length,decisionCount:decisions.length,riskCount:risks.length,reimportedSheetCount:renders.length,previews:renders.map(x=>path.join(previewDir,x[2]))},null,2));
