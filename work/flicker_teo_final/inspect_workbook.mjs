import fs from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {FileBlob,SpreadsheetFile} from '@oai/artifact-tool';
const root=new URL('.',import.meta.url);
const wb=await SpreadsheetFile.importXlsx(await FileBlob.load(fileURLToPath(new URL('source/Flicker_TEO_financial_model (4).xlsx',root))));
console.log((await wb.inspect({kind:'sheet',include:'id,name',maxChars:2500})).ndjson);
const preview=await wb.render({sheetName:'Панель',range:'A1:H12',scale:1.5,format:'png'});
await fs.writeFile(new URL('qa/source_dashboard.png',root),new Uint8Array(await preview.arrayBuffer()));
console.log('Source dashboard rendered');
