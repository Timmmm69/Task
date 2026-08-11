import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const text=await readFile(path.join(root,'evidence','GATE_EVIDENCE_INDEX.csv'),'utf8');
const lines=text.trim().split(/\r?\n/).slice(1);
const rows=lines.map(line=>{const fields=[];let field='',q=false;for(let i=0;i<line.length;i++){const c=line[i];if(q){if(c==='"'&&line[i+1]==='"'){field+='"';i++;}else if(c==='"')q=false;else field+=c;}else if(c==='"')q=true;else if(c===','){fields.push(field);field='';}else field+=c;}fields.push(field);return fields;});
const results=[];
for(const [id,artifact,owner,requiredPath,status,criterion,expectedHash] of rows){let actualHash='',present=false;try{const data=await readFile(path.join(root,requiredPath));actualHash=createHash('sha256').update(data).digest('hex').toUpperCase();present=data.length>0;}catch{}const accepted=status==='ACCEPTED'&&present&&/^[A-F0-9]{64}$/.test(expectedHash)&&expectedHash===actualHash;results.push({id,artifact,owner,requiredPath,status,present,hashMatches:present&&expectedHash===actualHash,accepted,criterion});}
const accepted=results.filter(r=>r.accepted).length;
const technicalPartial=results.filter(r=>r.status==='TECHNICAL_PARTIAL'&&r.present).map(r=>r.id);
console.log(JSON.stringify({result:accepted===results.length?'READY':'NOT_READY',accepted,total:results.length,technicalPartial,missing:results.filter(r=>!r.accepted).map(r=>r.id),results},null,2));
