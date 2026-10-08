// Compare independently produced recovery reports without retaining payloads.
const fs=require('node:fs'),path=require('node:path'),rl=require('node:readline'),assert=require('node:assert/strict');
const [before,after]=process.argv.slice(2).map(p=>path.resolve(p));
async function* entries(file){for await(const line of rl.createInterface({input:fs.createReadStream(file),crlfDelay:Infinity}))if(line)yield JSON.parse(line)}
(async()=>{
 const reports=fs.readdirSync(after).filter(n=>n.endsWith('.json')&&n.includes('__'));assert.equal(reports.length,20,'The fixed corpus has 20 DBs');
 const total={dbs:0,rows:0,entries:0,decodedBytes:0,payloadHashesCompared:0,sourceFilesUnchanged:true,errors:0,materializedPreviewBytes:0,unclassifiedStreams:0};
 for(const name of reports){const old=JSON.parse(fs.readFileSync(path.join(before,name))),now=JSON.parse(fs.readFileSync(path.join(after,name)));assert(!now.failure);assert(now.originalsUnchanged);assert.equal(now.files[0].sha256,old.files[0].sha256);assert.equal(now.load.count,old.load.count,'Visible row count changed: '+name);assert.equal(now.load.errorCount,0);assert.equal(now.load.materializedResourceBytes,0,'Import eagerly materialized media');
  for(const audit of now.rawAudit){assert.equal(audit.byteCoverage,1);assert.equal(audit.failed,0);total.entries+=audit.entryCount;total.decodedBytes+=audit.recoveredPayloadBytes;total.unclassifiedStreams+=audit.rawUnknown;}
  total.dbs++;total.rows+=now.load.count;
 }
 const files=fs.readdirSync(path.join(after,'db')).filter(n=>n.endsWith('.entries.jsonl'));
 for(const file of files){const previous=new Map();for await(const e of entries(path.join(before,'db',file)))previous.set(e.fileId,[e.sha256,e.recoveredBytes]);
  for await(const e of entries(path.join(after,'db',file))){const old=previous.get(e.fileId);assert(old,'Unexpected entry '+file+'/'+e.fileId);assert.equal(e.sha256,old[0],'Payload changed '+file+'/'+e.fileId);assert.equal(e.recoveredBytes,old[1]);assert(e.complete);previous.delete(e.fileId);total.payloadHashesCompared++;}
  assert.equal(previous.size,0,'Previously recovered entries disappeared: '+file);
 }
 assert.equal(total.payloadHashesCompared,total.entries);console.log(JSON.stringify(total,null,2));
})().catch(e=>{console.error(e);process.exitCode=1});
