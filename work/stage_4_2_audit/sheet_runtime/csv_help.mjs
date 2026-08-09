import { Workbook } from "@oai/artifact-tool";

const workbook = Workbook.create();
workbook.worksheets.add("Audit");
console.log(
  workbook.help("csv export", {
    search: "CSV|csv",
    include: "index,examples,notes",
    maxChars: 5000,
  }).ndjson,
);
