import { writeFileSync } from "node:fs";
import { join } from "node:path";
import { applyRecommendedOrder } from "./execution-plan.mjs";
import { dashboardDir, readJson } from "./lib.mjs";

const roadmap = readJson("roadmap.json");
roadmap.items = applyRecommendedOrder(roadmap.items);
writeFileSync(join(dashboardDir, "roadmap.json"), `${JSON.stringify(roadmap, null, 2)}\n`);
const next = roadmap.items.find((item) => item.recommended_order === 1);
console.log(next ? `Next: ${next.id} - ${next.title}` : "No remaining roadmap items.");
