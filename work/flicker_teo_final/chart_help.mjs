import {Workbook} from '@oai/artifact-tool';
const w=Workbook.create();
console.log(w.help('chart',{search:'tickLabelPosition|axes|crosses|reverse',include:'index,examples,notes',maxChars:8000}).ndjson);
